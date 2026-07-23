using Microsoft.Extensions.Logging;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Service;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using System.Text.Json;
using System.Windows.Controls;

namespace STranslate.Plugin.Tts.FishAudio;

public class Main : ITtsPlugin
{
    private Control? _settingUi;
    private SettingsViewModel? _viewModel;
    private CancellationTokenSource? _startupRefreshCancellationTokenSource;
    private Settings Settings { get; set; } = null!;
    private IPluginContext Context { get; set; } = null!;
    internal Task PendingStartupTask { get; private set; } = Task.CompletedTask;

    public Control GetSettingUI()
    {
        _viewModel ??= new SettingsViewModel(Context, Settings);
        _settingUi ??= new SettingsView { DataContext = _viewModel };
        return _settingUi;
    }

    public void Init(IPluginContext context)
    {
        Init(context, FishAudioRuntime.LocalUtcNow());
    }

    internal void Init(IPluginContext context, DateTimeOffset nowUtc)
    {
        CancelStartupRefresh();
        Context = context;
        Settings = SettingsStore.Load(context, nowUtc);

        _startupRefreshCancellationTokenSource = new CancellationTokenSource();
        PendingStartupTask = RunStartupRefreshAsync(context, Settings, _viewModel, _startupRefreshCancellationTokenSource.Token);
    }

    public void Dispose()
    {
        CancelStartupRefresh();
        _viewModel?.Dispose();
    }

    public async Task PlayAudioAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Context.Snackbar.ShowWarning(Context.GetTranslation("STranslate_Plugin_Tts_FishAudio_Text_Empty"));
            return;
        }

        if (!FishAudioRuntime.TryPreflightApiKey(Context, Settings.ApiKey, "TTS", showError: true, out _))
            return;

        _viewModel?.BeginApiKeyOperation();
        try
        {
            var audioBytes = await FishAudioApi.PostTtsAsync(Context, Settings, text, cancellationToken);
            await Context.AudioPlayer.PlayAsync(audioBytes, cancellationToken);

            if (_viewModel is not null)
                _ = _viewModel.RefreshCreditSilentlyAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            FishAudioRuntime.LogRequestFailure(Context, "TTS request", ex);
            var message = FishAudioRuntime.IsTimeout(ex)
                ? Context.GetTranslation(FishAudioRuntime.RequestTimeoutKey)
                : TryExtractApiError(ex) ?? ex.Message;
            Context.Snackbar.ShowError(message);
        }
        finally
        {
            _viewModel?.EndApiKeyOperation();
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

    private async Task RunStartupRefreshAsync(
        IPluginContext context,
        Settings settings,
        SettingsViewModel? viewModel,
        CancellationToken cancellationToken)
    {
        try
        {
            var onlineUtc = await FishAudioRuntime.TryGetOnlineUtcNowAsync(context, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (onlineUtc is not null && SettingsNormalizer.NormalizeSelectedModel(settings, onlineUtc.Value))
            {
                SettingsStore.Save(context, settings);
                viewModel?.ApplyAvailableModels(onlineUtc.Value);
            }

            await RefreshSelectedVoiceMetadataOnStartupAsync(context, settings, viewModel, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Logger?.LogWarning(ex, "Fish Audio startup refresh failed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshSelectedVoiceMetadataOnStartupAsync(
        IPluginContext context,
        Settings settings,
        SettingsViewModel? viewModel,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.VoiceId) || !SettingsValidation.IsValidVoiceIdFormat(settings.VoiceId))
            return;

        if (!FishAudioRuntime.IsNetworkAvailable())
        {
            context.Logger?.LogWarning("Startup voice refresh skipped: Network unavailable");
            return;
        }

        try
        {
            var model = await FishAudioApi.GetModelAsync(context, "dummy", settings.VoiceId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (model is null)
                return;

            var cached = SettingsViewModel.CreateCachedVoiceInfo(model);
            settings.CachedVoice = cached;
            SettingsStore.Save(context, settings);
            viewModel?.ApplyRefreshedCachedVoice(cached);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Logger?.LogWarning(ex, "Startup voice refresh failed");
        }
    }

    private void CancelStartupRefresh()
    {
        var cts = _startupRefreshCancellationTokenSource;
        if (cts is null)
            return;

        _startupRefreshCancellationTokenSource = null;
        cts.Cancel();
        _ = PendingStartupTask.ContinueWith(_ => cts.Dispose(), TaskScheduler.Default);
    }
}
