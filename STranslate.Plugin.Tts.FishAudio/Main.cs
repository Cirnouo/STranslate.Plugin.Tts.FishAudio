using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Lifecycle;
using STranslate.Plugin.Tts.FishAudio.Presentation;
using STranslate.Plugin.Tts.FishAudio.Runtime;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using System.Text.Json;
using System.Windows.Controls;

namespace STranslate.Plugin.Tts.FishAudio;

public class Main : ITtsPlugin
{
    private readonly PluginInitializationCoordinator _initializationCoordinator;

    public Main()
        : this(static (context, settings, settingsWriteLease, startupCreditRefreshCycle) =>
            new SettingsViewModel(
                context,
                settings,
                clearCoverImageCacheAsync: null,
                clearCoverImageCacheTimeout: null,
                previewAudioPlayerFactory: null,
                startupCreditRefreshCycle: startupCreditRefreshCycle,
                settingsWriteLease: settingsWriteLease))
    {
    }

    internal Main(
        Func<IPluginContext, Settings, SettingsWriteLease, StartupCreditRefreshCycle, SettingsViewModel> viewModelFactory,
        Action? startupSettingsRevisionPublished = null,
        Action<long>? initializationGenerationDeclared = null,
        Action<long>? initializationPublished = null)
    {
        _initializationCoordinator = new PluginInitializationCoordinator(
            viewModelFactory,
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

    public void Dispose() => _initializationCoordinator.Dispose();

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

        operation.ViewModel?.BeginApiKeyOperation();
        try
        {
            var audioBytes = await FishAudioApi.PostTtsAsync(
                operation.Context,
                operation.Settings,
                text,
                cancellationToken);
            await operation.AudioPlayer.PlayAsync(audioBytes, cancellationToken);

            if (operation.ViewModel is not null)
                _ = operation.ViewModel.RefreshCreditSilentlyAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            FishAudioRequestPolicy.LogRequestFailure(operation.Context, "TTS request", ex);
            var message = FishAudioRequestPolicy.IsTimeout(ex)
                ? operation.Context.GetTranslation(FishAudioRequestPolicy.RequestTimeoutKey)
                : TryExtractApiError(ex) ?? ex.Message;
            operation.Context.Snackbar.ShowError(message);
        }
        finally
        {
            operation.ViewModel?.EndApiKeyOperation();
        }
    }

    private static string? TryExtractApiError(Exception ex)
    {
        try
        {
            var msg = ex.Message;
            if (msg.Contains('{') && msg.Contains("message"))
            {
                using var doc = JsonDocument.Parse(msg[msg.IndexOf('{')..]);
                if (doc.RootElement.TryGetProperty("message", out var m))
                    return m.GetString();
            }
        }
        catch { }
        return null;
    }
}
