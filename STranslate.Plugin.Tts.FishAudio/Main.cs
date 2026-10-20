using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Caching;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Lifecycle;
using STranslate.Plugin.Tts.FishAudio.Presentation;
using STranslate.Plugin.Tts.FishAudio.Runtime;
using STranslate.Plugin.Tts.FishAudio.Streaming;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Windows.Controls;

namespace STranslate.Plugin.Tts.FishAudio;

public class Main : ITtsPlugin
{
    private readonly PluginInitializationCoordinator _initializationCoordinator;
    private readonly Func<IStreamingTtsSession>? _streamingSessionFactory;
    private readonly VoiceMetadataCache _voiceMetadataCache = new();
    private readonly object _playbackGate = new();
    private ActivePlayback? _activePlayback;
    private bool _disposed;

    public Main()
        : this(null)
    {
    }

    internal Main(
        Func<IPluginContext, Settings, SettingsWriteLease, StartupCreditRefreshCycle, SettingsViewModel>? viewModelFactory,
        Action? startupSettingsRevisionPublished = null,
        Action<long>? initializationGenerationDeclared = null,
        Action<long>? initializationPublished = null,
        Func<IStreamingTtsSession>? streamingSessionFactory = null)
    {
        _streamingSessionFactory = streamingSessionFactory;
        _initializationCoordinator = new PluginInitializationCoordinator(
            viewModelFactory ?? ((context, settings, lease, creditCycle) => new SettingsViewModel(
                context, settings, null, null, startupCreditRefreshCycle: creditCycle,
                settingsWriteLease: lease, voiceMetadataCache: _voiceMetadataCache)),
            startupSettingsRevisionPublished,
            initializationGenerationDeclared,
            initializationPublished);
    }

    internal long InitializationGeneration => _initializationCoordinator.InitializationGeneration;

    internal Task PendingStartupTask => _initializationCoordinator.PendingStartupTask;

    public Control GetSettingUI() => _initializationCoordinator.GetSettingUI();

    internal SettingsViewModel GetOrCreateSettingsViewModel() =>
        _initializationCoordinator.GetOrCreateSettingsViewModel();

    public void Init(IPluginContext context) => _initializationCoordinator.Init(context);

    internal void Init(IPluginContext context, DateTimeOffset nowUtc) =>
        _initializationCoordinator.Init(context, nowUtc);

    public void Dispose()
    {
        ActivePlayback? active;
        lock (_playbackGate)
        {
            if (_disposed) return;
            _disposed = true;
            active = _activePlayback;
        }
        if (active is not null) _ = CancelPlaybackAsync(active);
        _initializationCoordinator.Dispose();
    }

    public async Task PlayAudioAsync(string text, CancellationToken cancellationToken = default)
    {
        var operation = _initializationCoordinator.CaptureOperationSnapshot();

        if (string.IsNullOrWhiteSpace(text))
        {
            operation.Context.Snackbar.ShowWarning(
                operation.Context.GetTranslation("STranslate_Plugin_Tts_FishAudio_Text_Empty"));
            return;
        }

        if (!FishAudioRequestPolicy.TryPreflightApiKey(
                operation.Context,
                operation.Settings.ApiKey,
                "TTS",
                showError: true,
                out _))
        {
            return;
        }

        var request = new SynthesisRequest(operation.Settings);
        if (!SettingsValidation.IsValidStreamingProxyConfiguration(request.ProxyMode, request.ProxyAddress))
        {
            operation.Context.Snackbar.ShowError(operation.Context.GetTranslation(
                "STranslate_Plugin_Tts_FishAudio_StreamingProxy_Invalid"));
            return;
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operation.LifetimeToken);
        var active = new ActivePlayback(cancellation);
        ActivePlayback? previous;
        lock (_playbackGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = _activePlayback;
            _activePlayback = active;
        }
        var keyOperationStarted = false;
        try
        {
            if (previous is not null)
            {
                await CancelPlaybackAsync(previous).ConfigureAwait(false);
                // Each completion includes its predecessor's cleanup, so a third
                // call cannot overtake a cancelled call still retiring old output.
                await previous.Completion.Task.ConfigureAwait(false);
            }
            cancellation.Token.ThrowIfCancellationRequested();
            keyOperationStarted = true;
            operation.ViewModel?.BeginApiKeyOperation();
            using var session = _streamingSessionFactory is { } factory
                ? factory()
                : new NativeStreamingTtsSession(observer: observation => operation.Context.Logger?.LogInformation(
                    "Fish Audio streaming {Stage} at {ElapsedMilliseconds} ms",
                    observation.Stage, observation.Elapsed.TotalMilliseconds));
            await session.PlayAsync(request, text, cancellation.Token).ConfigureAwait(false);

            if (operation.ViewModel is not null)
                _ = operation.ViewModel.RefreshCreditSilentlyAsync();
        }
        catch (OperationCanceledException ex) when (cancellation.IsCancellationRequested)
        {
            // STranslate handles TaskCanceledException as a quiet stop. Keep
            // caller, supersession and retired-initialization cancellations alike.
            throw new TaskCanceledException("Fish Audio playback was canceled.", ex,
                cancellationToken.IsCancellationRequested ? cancellationToken : cancellation.Token);
        }
        catch (Exception ex)
        {
            // Transport exceptions can contain proxy addresses or credentials.
            // Publish a fixed localized message and only a safe exception type.
            operation.Context.Logger?.LogError("TTS request failed ({ErrorType})", ex.GetType().Name);
            var message = FishAudioRequestPolicy.IsTimeout(ex)
                ? operation.Context.GetTranslation(FishAudioRequestPolicy.RequestTimeoutKey)
                : operation.Context.GetTranslation("STranslate_Plugin_Tts_FishAudio_Streaming_Failed");
            operation.Context.Snackbar.ShowError(message);
        }
        finally
        {
            try
            {
                if (keyOperationStarted) operation.ViewModel?.EndApiKeyOperation();
            }
            finally
            {
                lock (_playbackGate)
                {
                    if (ReferenceEquals(_activePlayback, active)) _activePlayback = null;
                }
                active.Completion.TrySetResult();
            }
        }
    }

    private static async Task CancelPlaybackAsync(ActivePlayback active)
    {
        try { await active.Cancellation.CancelAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { /* The previous operation already ended. */ }
        catch { /* A throwing cancellation callback cannot prevent retirement. */ }
    }

    private sealed class ActivePlayback(CancellationTokenSource cancellation)
    {
        internal CancellationTokenSource Cancellation { get; } = cancellation;
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
