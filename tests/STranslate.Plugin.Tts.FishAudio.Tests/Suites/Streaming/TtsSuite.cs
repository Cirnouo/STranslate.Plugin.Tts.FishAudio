using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using Microsoft.Extensions.Logging;
using static AsyncTestWait;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static TestAssertions;

[TestFixture]
[NonParallelizable]
internal static class TtsSuite
{
    [Test]
    [Property("FishAudioCaseId", "streaming.tts/play-audio-uses-old-initialization-while-reinit-load-is-blocked")]
    [Property("OriginalOrdinal", 154)]
    [Order(154)]
    public static Task PlayAudioUsesOldInitializationWhileReinitLoadIsBlockedAsync() => RunPlayAudioUsesOldInitializationWhileReinitLoadIsBlockedAsync();

    internal static async Task RunPlayAudioUsesOldInitializationWhileReinitLoadIsBlockedAsync(
        Action<NUnitCompatibility.HeldTtsOwnershipObservation>? onHeldWork = null)
    {
        using var network = OverrideNetworkAvailability(true);
        var owner = new HeldTtsFixtureOwner();
        await owner.RunAsync(async () =>
        {
            var oldSettings = CreateTtsSettings(FishAudioModelPolicy.S2ProModel);
            var (oldService, oldHttp) = TestHttpServiceProxy.Create();
            oldHttp.GetAsyncHandler = (url, _, _) => Task.FromResult(url.Contains("/wallet/") ? "{\"credit\":\"1.00\"}" : "{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
            var session = new StreamingSessionDouble { Handler = (_, _, token) => Task.Delay(Timeout.InfiniteTimeSpan, token) };
            var plugin = StreamingEntrySuite.CreatePlugin(() => session);
            owner.Plugin = plugin;
            plugin.Init(CreateContext(settings: oldSettings, httpService: oldService), FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
            owner.CaptureCurrent();
            await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
            var oldVm = plugin.GetOrCreateSettingsViewModel();
            owner.CaptureCurrent();
            var newSettings = CreateTtsSettings(FishAudioModelPolicy.S1Model);
            newSettings.ApiKey = DraftKey;
            var (newService, newHttp) = TestHttpServiceProxy.Create();
            newHttp.GetAsyncHandler = (url, _, _) => Task.FromResult(url.Contains("/wallet/") ? "{\"credit\":\"2.00\"}" : "{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
            var loadEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.ReleaseHeldBarrier = () => releaseLoad.TrySetResult();
            owner.Barrier = releaseLoad.Task;
            owner.Entered = loadEntered.Task;
            var newContext = CreateContext(settings: newSettings, httpService: newService);
            ((ContextProxy)(object)newContext).OnLoad = () => { loadEntered.TrySetResult(); releaseLoad.Task.WaitAsync(RegressionExecution.Timeout).GetAwaiter().GetResult(); };
            var replacement = Task.Run(() => plugin.Init(newContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1)));
            owner.Replacement = replacement;
            await loadEntered.Task.WaitAsync(RegressionExecution.Timeout);
            var playback = plugin.PlayAudioAsync("load-window snapshot");
            owner.Playback = playback;
            await session.Entered.Task.WaitAsync(RegressionExecution.Timeout);
            onHeldWork?.Invoke(new NUnitCompatibility.HeldTtsOwnershipObservation(owner, session));
            AssertEqual(AppliedKey, session.Request!.ApiKey, "Playback during settings load must retain the old initialization key");
            AssertEqual(FishAudioModelPolicy.S2ProModel, session.Request.Model, "Playback during settings load must retain the old model");
            AssertEqual(true, oldVm.IsApiKeyInputLocked, "Playback must lock its captured old ViewModel");
            releaseLoad.TrySetResult();
            await replacement.WaitAsync(RegressionExecution.Timeout);
            owner.CaptureCurrent();
            await StreamingEntrySuite.AssertCanceledAsync(playback);
            owner.PlaybackCancellationObserved = true;
            await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
            AssertEqual(true, session.CancellationObserved, "Publishing replacement initialization must cancel old output");
            AssertEqual(false, oldVm.IsApiKeyInputLocked, "Retirement must release the old key operation");
            AssertEqual(false, ReferenceEquals(oldVm, plugin.GetOrCreateSettingsViewModel()), "Replacement must publish a new ViewModel");
            AssertEqual(0, oldHttp.PostAsBytesCallCount + newHttp.PostAsBytesCallCount, "Neither context should request whole-buffer HTTP audio");
        });
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.tts/play-audio-uses-initialization-snapshot-across-reinit")]
    [Property("OriginalOrdinal", 155)]
    [Order(155)]
    public static Task PlayAudioUsesInitializationSnapshotAcrossReinitAsync() => RunPlayAudioUsesInitializationSnapshotAcrossReinitAsync();

    internal static async Task RunPlayAudioUsesInitializationSnapshotAcrossReinitAsync(
        Action<NUnitCompatibility.HeldTtsOwnershipObservation>? onHeldWork = null)
    {
        using var network = OverrideNetworkAvailability(true);
        var owner = new HeldTtsFixtureOwner();
        await owner.RunAsync(async () =>
        {
            var oldLogger = new TestLogger();
            var (oldService, oldHttp) = TestHttpServiceProxy.Create();
            oldHttp.GetAsyncHandler = (url, _, _) => Task.FromResult(url.Contains("/wallet/") ? "{\"credit\":\"1.00\"}" : "{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
            var session = new StreamingSessionDouble { Handler = (_, _, token) => Task.Delay(Timeout.InfiniteTimeSpan, token) };
            var plugin = StreamingEntrySuite.CreatePlugin(() => session);
            owner.Plugin = plugin;
            plugin.Init(CreateContext(settings: CreateTtsSettings("s2-pro"), httpService: oldService, logger: oldLogger), FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
            owner.CaptureCurrent();
            await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
            var oldVm = plugin.GetOrCreateSettingsViewModel();
            owner.CaptureCurrent();
            var oldPlayback = plugin.PlayAudioAsync("snapshot test");
            owner.Playback = oldPlayback;
            await session.Entered.Task.WaitAsync(RegressionExecution.Timeout);
            var newSettings = CreateTtsSettings("s2-pro"); newSettings.ApiKey = DraftKey;
            var newLogger = new TestLogger();
            var creditEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCredit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.ReleaseHeldBarrier = () => releaseCredit.TrySetResult("{\"credit\":\"2.00\"}");
            owner.Barrier = releaseCredit.Task;
            owner.Entered = creditEntered.Task;
            var (newService, newHttp) = TestHttpServiceProxy.Create();
            var creditRequests = 0;
            newHttp.GetAsyncHandler = (url, _, _) =>
            {
                if (!url.Contains("/wallet/")) return Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
                ++creditRequests; creditEntered.TrySetResult(); return releaseCredit.Task;
            };
            plugin.Init(CreateContext(settings: newSettings, httpService: newService, logger: newLogger), FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
            owner.CaptureCurrent();
            var newStartup = plugin.PendingStartupTask;
            owner.NewStartup = newStartup;
            var newVm = plugin.GetOrCreateSettingsViewModel();
            owner.CaptureCurrent();
            await creditEntered.Task.WaitAsync(RegressionExecution.Timeout);
            await StreamingEntrySuite.AssertCanceledAsync(oldPlayback);
            owner.PlaybackCancellationObserved = true;
            onHeldWork?.Invoke(new NUnitCompatibility.HeldTtsOwnershipObservation(owner, session));
            AssertEqual(AppliedKey, session.Request!.ApiKey, "Retired playback must retain its captured key");
            AssertEqual(false, oldVm.IsApiKeyInputLocked, "Old cancellation must release only its own key lock");
            AssertEqual(true, newVm.IsApiKeyInputLocked, "Old completion must not decrement the new startup lock");
            AssertEqual(1, creditRequests, "Retired playback must not refresh credit on the new initialization");
            AssertEqual(1, oldHttp.GetUrls.Count(url => url.Contains("/wallet/")), "Cancelled playback must not add post-TTS credit work");
            AssertEqual(false, oldLogger.Contains(AppliedKey), "Old logs must not disclose the old key");
            AssertEqual(false, newLogger.Contains(DraftKey), "New logs must not disclose the new key");
            releaseCredit.TrySetResult("{\"credit\":\"2.00\"}");
            await newStartup.WaitAsync(RegressionExecution.Timeout);
            await WaitUntilAsync(() => !newVm.IsApiKeyInputLocked, "new startup credit lock release");
        });
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.tts/play-audio-preflight-stops-before-request-for-missing-network")]
    [Order(156)]
    public static async Task PlayAudioPreflightStopsBeforeRequestForMissingNetworkAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var snackbar = new TestSnackbar(); var logger = new TestLogger(); var session = new StreamingSessionDouble();
        using var plugin = StreamingEntrySuite.CreatePlugin(() => session);
        plugin.Init(CreateContext(snackbar: snackbar, settings: CreateTtsSettings("s2-pro"), logger: logger));
        await plugin.PlayAudioAsync("hello");
        AssertEqual("STranslate_Plugin_Tts_FishAudio_Network_Unavailable", snackbar.LastError, "Offline TTS must show its localized preflight error");
        AssertEqual(0, session.Calls, "Offline preflight must prevent session creation");
        AssertEqual(true, logger.Contains(LogLevel.Warning, "Network unavailable"), "Offline preflight remains a warning");
        AssertEqual(false, logger.Contains(AppliedKey), "Preflight logs must not include keys");
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.tts/play-audio-preflight-stops-before-request-for-empty-and-malformed-keys")]
    [Order(157)]
    public static async Task PlayAudioPreflightStopsBeforeRequestForEmptyAndMalformedKeysAsync()
    {
        foreach (var key in new[] { "", "ABC" })
        {
            using var network = OverrideNetworkAvailability(false);
            var snackbar = new TestSnackbar(); var session = new StreamingSessionDouble();
            using var plugin = StreamingEntrySuite.CreatePlugin(() => session);
            plugin.Init(CreateContext(snackbar: snackbar, settings: new Settings { ApiKey = key }));
            await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout); network.Set(true);
            await plugin.PlayAudioAsync("hello");
            AssertEqual(key.Length == 0 ? "STranslate_Plugin_Tts_FishAudio_ApiKey_Empty" : "STranslate_Plugin_Tts_FishAudio_ApiKey_InvalidFormat", snackbar.LastError, "Key preflight must retain its empty/malformed distinction");
            AssertEqual(0, session.Calls, "Invalid keys must never create a session");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.tts/play-audio-with-formatted-key-streams-without-credit-validation")]
    [Order(158)]
    public static async Task PlayAudioWithFormattedKeyStreamsWithoutCreditValidationAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var (service, http) = TestHttpServiceProxy.Create(); var session = new StreamingSessionDouble();
        using var plugin = StreamingEntrySuite.CreatePlugin(() => session);
        plugin.Init(CreateContext(settings: CreateTtsSettings("s2-pro"), httpService: service));
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout); network.Set(true);
        await plugin.PlayAudioAsync("hello");
        AssertEqual(0, http.GetCallCount, "TTS must not add synchronous credit validation");
        AssertEqual(1, session.Calls, "A formatted key must start one session");
        AssertEqual(AppliedKey, session.Request!.ApiKey, "The session must receive the full key unchanged");
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.tts/play-audio-timeout-shows-localized-error-and-logs")]
    [Order(159)]
    public static async Task PlayAudioTimeoutShowsLocalizedErrorAndLogsAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var snackbar = new TestSnackbar(); var logger = new TestLogger();
        var session = new StreamingSessionDouble { Handler = (_, _, _) => throw new TimeoutException("request timed out") };
        using var plugin = StreamingEntrySuite.CreatePlugin(() => session);
        plugin.Init(CreateContext(snackbar: snackbar, settings: CreateTtsSettings("s2-pro"), logger: logger));
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout); network.Set(true);
        await plugin.PlayAudioAsync("hello");
        AssertEqual("STranslate_Plugin_Tts_FishAudio_Request_Timeout", snackbar.LastError, "Streaming timeout must remain localized");
        AssertEqual(true, logger.Contains(LogLevel.Error, "TTS request failed"), "Timeout must be logged without transport details");
        AssertEqual(1, session.DisposeCount, "Failed sessions must be disposed");
    }
}
