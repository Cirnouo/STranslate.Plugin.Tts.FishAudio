using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.Presentation;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

internal sealed class BlockingSettingsViewModel : SettingsViewModel
{
    public BlockingSettingsViewModel(
        IPluginContext context,
        Settings settings,
        SettingsWriteLease settingsWriteLease,
        StartupCreditRefreshCycle startupCreditRefreshCycle,
        TaskCompletionSource constructionSnapshotCaptured,
        TaskCompletionSource releaseConstruction,
        Action<SettingsViewModel>? onConstructed = null)
        : base(
            context,
            settings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: null,
            startupCreditRefreshCycle: startupCreditRefreshCycle,
            settingsWriteLease: settingsWriteLease)
    {
        onConstructed?.Invoke(this);
        constructionSnapshotCaptured.TrySetResult();
        releaseConstruction.Task.WaitAsync(RegressionExecution.Timeout).GetAwaiter().GetResult();
    }
}
