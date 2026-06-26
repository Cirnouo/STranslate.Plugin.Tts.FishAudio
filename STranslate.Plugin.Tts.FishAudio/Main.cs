using Microsoft.Extensions.Logging;
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
    private Settings Settings { get; set; } = null!;
    private IPluginContext Context { get; set; } = null!;
    internal Task PendingStartupTask { get; private set; } = Task.CompletedTask;

    public Control GetSettingUI()
    {
        _viewModel ??= new SettingsViewModel(Context, Settings, null);
        _settingUi ??= new SettingsView { DataContext = _viewModel };
        return _settingUi;
    }

    public void Init(IPluginContext context)
    {
        Init(context, FishAudioRuntime.LocalUtcNow());
    }

    internal void Init(IPluginContext context, DateTimeOffset nowUtc)
    {
        Context = context;
        Settings = context.LoadSettingStorage<Settings>();
        var shouldSave = false;

        if (Settings.NeedsMigration)
        {
            Settings.Migrate();
            shouldSave = true;
        }

        if (FishAudioRuntime.NormalizeSettings(Settings, nowUtc))
            shouldSave = true;

        if (shouldSave)
            context.SaveSettingStorage<Settings>();

        PendingStartupTask = RunStartupRefreshAsync(CancellationToken.None);
    }

    public void Dispose() => _viewModel?.Dispose();

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

    private async Task RunStartupRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var onlineUtc = await FishAudioRuntime.TryGetOnlineUtcNowAsync(Context, cancellationToken);
            if (onlineUtc is not null && FishAudioRuntime.NormalizeSelectedModel(Settings, onlineUtc.Value))
            {
                Context.SaveSettingStorage<Settings>();
                _viewModel?.ApplyAvailableModels(onlineUtc.Value);
            }

            await RefreshSelectedVoiceMetadataOnStartupAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Context.Logger?.LogWarning(ex, "Fish Audio startup refresh failed");
        }
    }

    private async Task RefreshSelectedVoiceMetadataOnStartupAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Settings.VoiceId) || !Settings.IsValidVoiceIdFormat(Settings.VoiceId))
            return;

        if (!FishAudioRuntime.IsNetworkAvailable())
        {
            Context.Logger?.LogWarning("Startup voice refresh skipped: Network unavailable");
            return;
        }

        try
        {
            var model = await FishAudioApi.GetModelAsync(Context, "dummy", Settings.VoiceId, cancellationToken);
            if (model is null)
                return;

            var cached = SettingsViewModel.CreateCachedVoiceInfo(model);
            Settings.CachedVoice = cached;
            Context.SaveSettingStorage<Settings>();
            _viewModel?.ApplyRefreshedCachedVoice(cached);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Context.Logger?.LogWarning(ex, "Startup voice refresh failed");
        }
    }
}
