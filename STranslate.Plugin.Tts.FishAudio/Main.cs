using STranslate.Plugin.Tts.FishAudio.Service;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using STranslate.Plugin.Tts.FishAudio.Model;
using System.Text.Json;
using System.Windows.Controls;

namespace STranslate.Plugin.Tts.FishAudio;

public class Main : ITtsPlugin
{
    private Control? _settingUi;
    private SettingsViewModel? _viewModel;
    private Settings Settings { get; set; } = null!;
    private IPluginContext Context { get; set; } = null!;
    private Task<(WalletCreditResponse?, long)>? _pendingCreditTask;

    public Control GetSettingUI()
    {
        _viewModel ??= new SettingsViewModel(Context, Settings, _pendingCreditTask);
        _settingUi ??= new SettingsView { DataContext = _viewModel };
        return _settingUi;
    }

    public void Init(IPluginContext context)
    {
        Context = context;
        Settings = context.LoadSettingStorage<Settings>();

        if (!string.IsNullOrWhiteSpace(Settings.ApiKey))
            _pendingCreditTask = FishAudioApi.GetCreditAsync(context, Settings.ApiKey, CancellationToken.None);
    }

    public void Dispose() => _viewModel?.Dispose();

    public async Task PlayAudioAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Settings.ApiKey))
        {
            Context.Snackbar.ShowError(Context.GetTranslation("STranslate_Plugin_Tts_FishAudio_ApiKey_Empty"));
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            Context.Snackbar.ShowWarning(Context.GetTranslation("STranslate_Plugin_Tts_FishAudio_Text_Empty"));
            return;
        }

        try
        {
            var audioBytes = await FishAudioApi.PostTtsAsync(Context, Settings, text, cancellationToken);
            await Context.AudioPlayer.PlayAsync(audioBytes, cancellationToken);

            if (_viewModel is not null)
                _ = _viewModel.RefreshCreditSilentlyAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = TryExtractApiError(ex) ?? ex.Message;
            Context.Snackbar.ShowError(message);
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
