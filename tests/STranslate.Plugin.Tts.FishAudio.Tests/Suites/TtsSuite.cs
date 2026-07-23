using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using static AsyncTestWait;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static StaTestHost;
using static TestAssertions;

internal static class TtsSuite
{
    internal static async Task PlayAudioUsesOldInitializationWhileReinitLoadIsBlockedAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var oldSettings = CreateTtsSettings(FishAudioModelPolicy.S2ProModel);
        var oldAudio = new TestAudioPlayer();
        var releaseTts = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ttsRequestStarted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (oldHttpService, oldHttp) = TestHttpServiceProxy.Create();
        oldHttp.GetAsyncHandler = (url, _, _) => Task.FromResult(
            url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
                ? "{\"credit\":\"1.00\"}"
                : "{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
        oldHttp.PostAsBytesAsyncHandler = (_, _, _, _) =>
        {
            ttsRequestStarted.TrySetResult("old");
            return releaseTts.Task;
        };
        var oldContext = CreateContext(
            settings: oldSettings,
            httpService: oldHttpService,
            audioPlayer: oldAudio);
        var plugin = new Main();
        plugin.Init(oldContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        var oldViewModel = plugin.GetOrCreateSettingsViewModel();

        var newSettings = CreateTtsSettings(FishAudioModelPolicy.S1Model);
        newSettings.ApiKey = DraftKey;
        var newAudio = new TestAudioPlayer();
        var (newHttpService, newHttp) = TestHttpServiceProxy.Create();
        newHttp.GetAsyncHandler = (url, _, _) => Task.FromResult(
            url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
                ? "{\"credit\":\"2.00\"}"
                : "{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
        newHttp.PostAsBytesAsyncHandler = (_, _, _, _) =>
        {
            ttsRequestStarted.TrySetResult("new");
            return releaseTts.Task;
        };
        var secondLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var newContext = CreateContext(
            settings: newSettings,
            httpService: newHttpService,
            audioPlayer: newAudio);
        ((ContextProxy)(object)newContext).OnLoad = () =>
        {
            secondLoadStarted.TrySetResult();
            releaseSecondLoad.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        };

        var reinitializeTask = Task.Run(() =>
            plugin.Init(newContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1)));
        await secondLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var ttsTask = plugin.PlayAudioAsync("load-window snapshot");
        var requestInitialization = await ttsRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var oldViewModelLockedDuringTts = oldViewModel.IsApiKeyInputLocked;

        releaseSecondLoad.TrySetResult();
        await reinitializeTask.WaitAsync(TimeSpan.FromSeconds(2));
        var newStartupTask = plugin.PendingStartupTask;
        var newViewModel = plugin.GetOrCreateSettingsViewModel();
        releaseTts.TrySetResult(new byte[] { 7, 8, 9 });
        await ttsTask.WaitAsync(TimeSpan.FromSeconds(2));
        await newStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual("old", requestInitialization, "TTS started during settings load should use the complete old initialization");
        AssertEqual(1, oldHttp.PostAsBytesCallCount, "Load-window TTS should post through the old Context");
        AssertEqual(0, newHttp.PostAsBytesCallCount, "Load-window TTS should not combine the new Context with old Settings");
        var headers = AssertHeaders(oldHttp.LastPostOptions, "Load-window TTS should retain old request headers");
        AssertEqual($"Bearer {AppliedKey}", headers["Authorization"], "Load-window TTS should use the old API Key with the old Context");
        AssertEqual(FishAudioModelPolicy.S2ProModel, headers["model"], "Load-window TTS should use the old synthesis model with the old Context");
        AssertEqual(true, oldViewModelLockedDuringTts, "Load-window TTS should lock its old settings ViewModel");
        AssertEqual(false, oldViewModel.IsApiKeyInputLocked, "Load-window TTS completion should release its old ViewModel lock");
        AssertEqual(false, ReferenceEquals(oldViewModel, newViewModel), "The completed reinitialization should still replace the settings ViewModel");
        AssertEqual(1, oldAudio.PlayBytesCallCount, "Load-window TTS should play through the old AudioPlayer");
        AssertEqual(0, newAudio.PlayBytesCallCount, "Load-window TTS should not play through the new AudioPlayer");
    }

    internal static async Task PlayAudioUsesInitializationSnapshotAcrossReinitAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var oldSettings = CreateTtsSettings("s2-pro");
        var oldLogger = new TestLogger();
        var oldAudio = new TestAudioPlayer();
        var oldTtsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldTts = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (oldHttpService, oldHttp) = TestHttpServiceProxy.Create();
        oldHttp.GetAsyncHandler = (url, _, _) => Task.FromResult(
            url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
                ? "{\"credit\":\"1.00\"}"
                : "{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
        oldHttp.PostAsBytesAsyncHandler = (_, _, _, _) =>
        {
            oldTtsStarted.TrySetResult();
            return releaseOldTts.Task;
        };
        var oldContext = CreateContext(
            settings: oldSettings,
            httpService: oldHttpService,
            audioPlayer: oldAudio,
            logger: oldLogger);
        var plugin = new Main();
        plugin.Init(oldContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        plugin.GetOrCreateSettingsViewModel();

        var oldTtsTask = plugin.PlayAudioAsync("snapshot test");
        await oldTtsStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var newSettings = CreateTtsSettings("s2-pro");
        newSettings.ApiKey = DraftKey;
        var newLogger = new TestLogger();
        var newAudio = new TestAudioPlayer();
        var newCreditStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseNewStartupCredit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newCreditRequestCount = 0;
        var (newHttpService, newHttp) = TestHttpServiceProxy.Create();
        newHttp.GetAsyncHandler = (url, _, _) =>
        {
            if (!url.Contains("/wallet/self/api-credit", StringComparison.Ordinal))
                return Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");

            newCreditRequestCount++;
            if (newCreditRequestCount == 1)
            {
                newCreditStarted.TrySetResult();
                return releaseNewStartupCredit.Task;
            }

            return Task.FromResult("{\"credit\":\"999.00\"}");
        };
        var newContext = CreateContext(
            settings: newSettings,
            httpService: newHttpService,
            audioPlayer: newAudio,
            logger: newLogger);

        plugin.Init(newContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var newStartupTask = plugin.PendingStartupTask;
        var newViewModel = plugin.GetOrCreateSettingsViewModel();
        await newCreditStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(true, newViewModel.IsApiKeyInputLocked, "The second initialization startup credit should lock its new ViewModel");

        releaseOldTts.SetResult(new byte[] { 4, 5, 6 });
        await oldTtsTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(1, oldAudio.PlayBytesCallCount, "An old TTS operation should keep using its original audio player after reinitialization");
        AssertEqual(0, newAudio.PlayBytesCallCount, "An old TTS operation should not play through the new Context");
        AssertEqual(true, newViewModel.IsApiKeyInputLocked, "Old TTS completion should not decrement the new ViewModel API Key lock");
        AssertEqual(1, newCreditRequestCount, "Old TTS completion should not start a silent credit refresh on the new initialization");
        AssertEqual(1, oldHttp.GetUrls.Count(url => url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)), "A disposed old ViewModel should ignore post-TTS silent refresh");
        AssertEqual(false, oldLogger.Contains(AppliedKey), "Old TTS logs should not contain the old API Key");
        AssertEqual(false, newLogger.Contains(DraftKey), "New startup logs should not contain the new API Key");

        releaseNewStartupCredit.SetResult("{\"credit\":\"2.00\"}");
        await newStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !newViewModel.IsApiKeyInputLocked, "new startup credit lock to release");

        AssertEqual(false, newViewModel.IsApiKeyInputLocked, "Only the new startup credit completion should unlock the new ViewModel");
    }

    internal static async Task PlayAudioPreflightStopsBeforeRequestForMissingNetworkAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var settings = CreateTtsSettings("s2-pro");
        var snackbar = new TestSnackbar();
        var logger = new TestLogger();
        var (httpService, http) = TestHttpServiceProxy.Create();
        var audio = new TestAudioPlayer();
        var plugin = new Main();

        plugin.Init(CreateContext(snackbar, settings, httpService, audio, logger));
        await plugin.PlayAudioAsync("hello");

        AssertEqual(
            "STranslate_Plugin_Tts_FishAudio_Network_Unavailable",
            snackbar.LastError,
            "TTS should show a localized network error before sending a request");
        AssertEqual(0, http.PostAsBytesCallCount, "TTS should not call Fish Audio when the machine is offline");
        AssertEqual(0, audio.PlayBytesCallCount, "TTS should not play audio when preflight fails");
        AssertEqual(true, logger.Contains(LogLevel.Warning, "Network unavailable"), "Offline preflight should be logged as warning");
        AssertEqual(false, logger.Contains(AppliedKey), "Logs should not contain the plaintext API Key");
    }

    internal static async Task PlayAudioPreflightStopsBeforeRequestForEmptyAndMalformedKeysAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var snackbar = new TestSnackbar();
        var logger = new TestLogger();
        var (httpService, http) = TestHttpServiceProxy.Create();
        var audio = new TestAudioPlayer();
        var plugin = new Main();

        plugin.Init(CreateContext(snackbar, new Settings { ApiKey = "" }, httpService, audio, logger));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        network.Set(true);
        await plugin.PlayAudioAsync("hello");

        AssertEqual(
            "STranslate_Plugin_Tts_FishAudio_ApiKey_Empty",
            snackbar.LastError,
            "TTS should reject an empty API Key before sending a request");
        AssertEqual(0, http.PostAsBytesCallCount, "TTS should not call Fish Audio with an empty API Key");

        snackbar.Clear();
        network.Set(false);
        plugin = new Main();
        plugin.Init(CreateContext(snackbar, new Settings { ApiKey = "ABC" }, httpService, audio, logger));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        network.Set(true);
        await plugin.PlayAudioAsync("hello");

        AssertEqual(
            "STranslate_Plugin_Tts_FishAudio_ApiKey_InvalidFormat",
            snackbar.LastError,
            "TTS should reject a malformed API Key before sending a request");
        AssertEqual(0, http.PostAsBytesCallCount, "TTS should not call Fish Audio with a malformed API Key");
        AssertEqual(true, logger.Count(LogLevel.Warning) >= 2, "API Key preflight failures should be logged as warnings");
    }

    internal static async Task PlayAudioWithFormattedKeyPostsAndPlaysWithoutRuntimeValidationAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var settings = CreateTtsSettings("s2-pro");
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.PostBytes = new byte[] { 1, 2, 3 };
        var audio = new TestAudioPlayer();
        var plugin = new Main();

        plugin.Init(CreateContext(settings: settings, httpService: httpService, audioPlayer: audio));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        network.Set(true);
        await plugin.PlayAudioAsync("hello");

        AssertEqual(0, http.GetCallCount, "TTS should not validate credit before playback");
        AssertEqual(1, http.PostAsBytesCallCount, "TTS should send a request when API Key format passes");
        AssertEqual(1, audio.PlayBytesCallCount, "TTS should play bytes returned by Fish Audio");
        AssertSequenceEqual(new byte[] { 1, 2, 3 }, audio.LastPlayedBytes, "TTS should play the returned audio bytes");
    }

    internal static async Task PlayAudioTimeoutShowsLocalizedErrorAndLogsAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var settings = CreateTtsSettings("s2-pro");
        var snackbar = new TestSnackbar();
        var logger = new TestLogger();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.PostException = new TimeoutException("request timed out");
        var audio = new TestAudioPlayer();
        var plugin = new Main();

        plugin.Init(CreateContext(snackbar, settings, httpService, audio, logger));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        network.Set(true);
        await plugin.PlayAudioAsync("hello");

        AssertEqual(
            "STranslate_Plugin_Tts_FishAudio_Request_Timeout",
            snackbar.LastError,
            "TTS timeout should show a localized timeout error");
        AssertEqual(0, audio.PlayBytesCallCount, "Timed out TTS should not play audio");
        AssertEqual(true, logger.Contains(LogLevel.Error, "TTS request failed"), "TTS timeout should be logged as error");
    }
}
