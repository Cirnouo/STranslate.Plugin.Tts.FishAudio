using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Streaming;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static TestAssertions;

[TestFixture]
[NonParallelizable]
internal static class StreamingEntrySuite
{
    internal static Main CreatePlugin(Func<IStreamingTtsSession> factory) => new(
        (context, settings, lease, startup) => new SettingsViewModel(context, settings,
            clearCoverImageCacheAsync: null, clearCoverImageCacheTimeout: null,
            settingsWriteLease: lease, startupCreditRefreshCycle: startup),
        streamingSessionFactory: factory);

    [Test]
    [Property("FishAudioCaseId", "streaming.entry/formal-playback-uses-session-without-whole-buffer-host-audio")]
    [Order(1)]
    public static async Task FormalPlaybackUsesSessionWithoutWholeBufferHostAudioAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var settings = CreateTtsSettings(FishAudioModelPolicy.S2ProModel);
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.PostBytes = [1, 2, 3];
        var hostAudio = new TestAudioPlayer();
        var session = new StreamingSessionDouble();
        using var plugin = CreatePlugin(() => session);
        plugin.Init(CreateContext(settings: settings, httpService: httpService, audioPlayer: hostAudio));
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
        network.Set(true);
        await plugin.PlayAudioAsync("A prefix and its tail.");

        AssertEqual(1, session.Calls, "The public TTS entry should create exactly one native streaming session");
        AssertEqual(0, http.PostAsBytesCallCount, "Formal speech should not request the complete HTTP audio buffer");
        AssertEqual(0, hostAudio.PlayBytesCallCount, "Formal speech should not wait for host byte-array playback");
        AssertEqual(AppliedKey, session.Request!.ApiKey, "The session should receive the captured key");
        AssertEqual(settings.SelectedModel, session.Request.Model, "The session should receive the saved model");
        AssertEqual(1, session.DisposeCount, "The entry point should dispose its completed session");
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.entry/snapshot-stays-immutable-while-settings-are-edited")]
    [Order(2)]
    public static async Task SnapshotStaysImmutableWhileSettingsAreEditedAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var settings = CreateTtsSettings(FishAudioModelPolicy.S2ProModel);
        settings.AudioFormat = "mp3";
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new StreamingSessionDouble { Handler = (_, _, token) => release.Task.WaitAsync(token) };
        using var plugin = CreatePlugin(() => session);
        plugin.Init(CreateContext(settings: settings));
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
        var viewModel = plugin.GetOrCreateSettingsViewModel();
        network.Set(true);
        var playback = plugin.PlayAudioAsync("Immutable prefix and a longer tail.");
        await session.Entered.Task.WaitAsync(RegressionExecution.Timeout);
        AssertEqual(true, viewModel.IsApiKeyInputLocked, "Active streaming should lock its captured ViewModel key");
        viewModel.Speed = 1.75;
        viewModel.Mp3Bitrate = 64;
        viewModel.AudioFormat = "pcm";
        AssertEqual(1.25, session.Request!.Speed, "Later settings edits must not change an active request's prosody");
        AssertEqual(128, session.Request.Mp3Bitrate, "Later settings edits must not change active bitrate");
        AssertEqual("mp3", session.Request.AudioFormat, "Active format must remain immutable");
        release.TrySetResult();
        await playback.WaitAsync(RegressionExecution.Timeout);
        AssertEqual(false, viewModel.IsApiKeyInputLocked, "Completion should release only its own key operation");
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.entry/supersession-waits-for-output-cleanup-across-three-calls")]
    [Order(3)]
    public static Task SupersessionWaitsForOutputCleanupAcrossThreeCallsAsync() =>
        RunSupersessionWaitsForOutputCleanupAcrossThreeCallsAsync();

    internal static async Task RunSupersessionWaitsForOutputCleanupAcrossThreeCallsAsync(
        Action<NUnitCompatibility.SupersessionOwnershipObservation>? onHeldWork = null)
    {
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            using var network = OverrideNetworkAvailability(false);
            Main? ownedPlugin = null;
            TaskCompletionSource? ownedReleaseCleanup = null;
            Task? ownedStartup = null;
            STranslate.Plugin.Tts.FishAudio.Lifecycle.PluginInitializationCoordinator.InitializationWorkHandle? ownedInitializationWork = null;
            Task? ownedFirstCall = null;
            Task? ownedSecondCall = null;
            Task? ownedThirdCall = null;
            bool firstCancellationObserved = false;
            bool secondCancellationObserved = false;
            await RegressionFixtureOwner.RunAsync(async () =>
            {
                var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ownedReleaseCleanup = releaseCleanup;
                var first = new StreamingSessionDouble
                {
                    Handler = async (_, _, token) =>
                    {
                        try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                        finally { cleanupEntered.TrySetResult(); await releaseCleanup.Task; }
                    },
                };
                var final = new StreamingSessionDouble();
                var created = 0;
                var plugin = CreatePlugin(() => ++created == 1 ? first : final);
                ownedPlugin = plugin;
                plugin.Init(CreateContext(settings: CreateTtsSettings(FishAudioModelPolicy.S2ProModel)));
                var initializationWork = plugin.CaptureInitializationWork()?.Work
                    ?? throw new InvalidOperationException("supersession-initialization-work-not-published");
                ownedInitializationWork = initializationWork;
                var startup = plugin.PendingStartupTask;
                ownedStartup = startup;
                await startup.WaitAsync(RegressionExecution.Timeout);
                network.Set(true);
                var firstCall = plugin.PlayAudioAsync("first");
                ownedFirstCall = firstCall;
                onHeldWork?.Invoke(new NUnitCompatibility.SupersessionOwnershipObservation(
                    firstCall, null, null, startup, initializationWork, cleanupEntered.Task, releaseCleanup.Task,
                    () => { releaseCleanup.TrySetResult(); }, plugin, () => first.DisposeCount, () => created));
                await first.Entered.Task.WaitAsync(RegressionExecution.Timeout);
                var secondCall = plugin.PlayAudioAsync("second");
                ownedSecondCall = secondCall;
                onHeldWork?.Invoke(new NUnitCompatibility.SupersessionOwnershipObservation(
                    firstCall, secondCall, null, startup, initializationWork, cleanupEntered.Task, releaseCleanup.Task,
                    () => { releaseCleanup.TrySetResult(); }, plugin, () => first.DisposeCount, () => created));
                await cleanupEntered.Task.WaitAsync(RegressionExecution.Timeout);
                var thirdCall = plugin.PlayAudioAsync("third");
                ownedThirdCall = thirdCall;
                onHeldWork?.Invoke(new NUnitCompatibility.SupersessionOwnershipObservation(
                    firstCall, secondCall, thirdCall, startup, initializationWork, cleanupEntered.Task, releaseCleanup.Task,
                    () => { releaseCleanup.TrySetResult(); }, plugin, () => first.DisposeCount, () => created));
                AssertEqual(1, created, "Replacement calls must not create new output before prior cleanup completes");
                releaseCleanup.TrySetResult();
                await AssertCanceledAsync(firstCall);
                firstCancellationObserved = true;
                await AssertCanceledAsync(secondCall);
                secondCancellationObserved = true;
                await thirdCall.WaitAsync(RegressionExecution.Timeout);
                AssertEqual(2, created, "A superseded waiting call must not create a redundant synthesis session");
                AssertEqual("third", final.Text, "Only the latest call may acquire output after retirement");
                AssertEqual(1, first.DisposeCount, "Cancelled output must be disposed before replacement starts");
            },
            () => { ownedPlugin?.Dispose(); return Task.CompletedTask; },
            () => { ownedReleaseCleanup?.TrySetResult(); return Task.CompletedTask; },
            () => ownedStartup ?? Task.CompletedTask,
            async () =>
            {
                if (ownedFirstCall is null) return;
                try { await ownedFirstCall; }
                catch (TaskCanceledException) when (firstCancellationObserved) { }
            },
            async () =>
            {
                if (ownedSecondCall is null) return;
                try { await ownedSecondCall; }
                catch (TaskCanceledException) when (secondCancellationObserved) { }
            },
            () => ownedThirdCall ?? Task.CompletedTask,
            async () =>
            {
                if (ownedInitializationWork is null) return;
                var retirementTasks = ownedInitializationWork.RetirementTasks;
                if (retirementTasks.Count == 0)
                    throw new InvalidOperationException("supersession-owner-has-no-retirement-task");
                await Task.WhenAll(retirementTasks);
            });
        });
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.entry/caller-and-disposal-cancel-owned-sessions")]
    [Order(4)]
    public static async Task CallerAndDisposalCancelOwnedSessionsAsync()
    {
        foreach (var dispose in new[] { false, true })
        {
            using var network = OverrideNetworkAvailability(false);
            using var caller = new CancellationTokenSource();
            var session = new StreamingSessionDouble { Handler = (_, _, token) => Task.Delay(Timeout.InfiniteTimeSpan, token) };
            var snackbar = new TestSnackbar();
            using var plugin = CreatePlugin(() => session);
            plugin.Init(CreateContext(snackbar: snackbar, settings: CreateTtsSettings(FishAudioModelPolicy.S2ProModel)));
            await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
            network.Set(true);
            var playback = plugin.PlayAudioAsync("cancel", caller.Token);
            await session.Entered.Task.WaitAsync(RegressionExecution.Timeout);
            if (dispose) plugin.Dispose(); else caller.Cancel();
            await AssertCanceledAsync(playback);
            AssertEqual(true, session.CancellationObserved, "Caller/disposal must reach the owned streaming token");
            AssertEqual(1, session.DisposeCount, "Cancellation must dispose the session");
            AssertEqual<string?>(null, snackbar.LastError, "Expected stop must not become a playback error snackbar");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.entry/cancellation-probe-observes-synchronous-retirement")]
    [Order(5)]
    public static async Task CancellationProbeObservesSynchronousRetirementAsync()
    {
        foreach (var dispose in new[] { false, true })
        {
            using var network = OverrideNetworkAvailability(false);
            using var caller = new CancellationTokenSource();
            CancellationTokenRegistration handlerRegistration = default;
            var session = new StreamingSessionDouble
            {
                Handler = (_, _, token) =>
                {
                    // A legal synchronous continuation retires the session before
                    // cancellation reaches its earlier observation registration.
                    var canceled = new TaskCompletionSource();
                    handlerRegistration = token.Register(() => canceled.TrySetCanceled(token));
                    return canceled.Task;
                },
            };
            using var plugin = CreatePlugin(() => session);
            plugin.Init(CreateContext(settings: CreateTtsSettings(FishAudioModelPolicy.S2ProModel)));
            await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
            network.Set(true);
            var playback = plugin.PlayAudioAsync("synchronous cancel", caller.Token);
            await session.Entered.Task.WaitAsync(RegressionExecution.Timeout);
            try
            {
                if (dispose) plugin.Dispose(); else caller.Cancel();
                await AssertCanceledAsync(playback);
                AssertEqual(true, session.CancellationObserved, $"The session must observe its canceled token before synchronous retirement (dispose={dispose})");
                AssertEqual(1, session.DisposeCount, "Synchronous cancellation retains exactly one owned session disposal");
            }
            finally { handlerRegistration.Dispose(); }
        }
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.entry/cancellation-probe-retains-only-active-observation")]
    [Order(6)]
    public static async Task CancellationProbeRetainsOnlyActiveObservationAsync()
    {
        var request = new SynthesisRequest(CreateTtsSettings(FishAudioModelPolicy.S2ProModel));
        using var completed = new CancellationTokenSource();
        var session = new StreamingSessionDouble();
        await session.PlayAsync(request, "completed", completed.Token);
        completed.Cancel();
        AssertEqual(false, session.CancellationObserved, "Cancellation after a completed call must not count as an active observation");

        using var active = new CancellationTokenSource();
        session.Handler = (_, _, token) => { active.Cancel(); return Task.FromCanceled(token); };
        try { await session.PlayAsync(request, "canceled", active.Token); }
        catch (OperationCanceledException) { }
        AssertEqual(true, session.CancellationObserved, "An active call must observe its owned canceled token");
        session.Handler = (_, _, _) => Task.CompletedTask;
        await session.PlayAsync(request, "later completion", CancellationToken.None);
        AssertEqual(true, session.CancellationObserved, "A later uncanceled call must preserve the probe's prior observation");
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.entry/transport-failures-do-not-expose-credentials")]
    [Order(7)]
    public static async Task TransportFailuresDoNotExposeCredentialsAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var settings = CreateTtsSettings(FishAudioModelPolicy.S2ProModel);
        var logger = new TestLogger();
        var snackbar = new TestSnackbar();
        var session = new StreamingSessionDouble();
        using var plugin = CreatePlugin(() => session);
        plugin.Init(CreateContext(snackbar: snackbar, settings: settings, logger: logger));
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
        network.Set(true);
        session.Handler = (_, _, _) => throw new System.IO.InvalidDataException(AppliedKey + " http://user:private-password@localhost:8080");
        await plugin.PlayAudioAsync("transport failure");
        AssertEqual("STranslate_Plugin_Tts_FishAudio_Streaming_Failed", snackbar.LastError, "Transport details must not appear in user errors");
        AssertEqual(false, logger.Contains(AppliedKey), "Transport exceptions must not expose keys through logging");
        AssertEqual(false, logger.Contains("private-password"), "Transport exceptions must not expose proxy authentication through logging");
    }

    internal static async Task AssertCanceledAsync(Task operation)
    {
        try { await operation.WaitAsync(RegressionExecution.Timeout); }
        catch (TaskCanceledException) { return; }
        throw new InvalidOperationException("Expected a quiet host-compatible TaskCanceledException.");
    }
}
