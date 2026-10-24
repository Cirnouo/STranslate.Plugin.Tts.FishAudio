using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Streaming;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static TestAssertions;

internal static class StreamingEntrySuite
{
    internal static Main CreatePlugin(Func<IStreamingTtsSession> factory) => new(
        (context, settings, lease, startup) => new SettingsViewModel(context, settings,
            clearCoverImageCacheAsync: null, clearCoverImageCacheTimeout: null,
            settingsWriteLease: lease, startupCreditRefreshCycle: startup),
        streamingSessionFactory: factory);

    internal static async Task FormalPlaybackUsesSessionWithoutWholeBufferHostAudioAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var settings = CreateTtsSettings(FishAudioModelPolicy.S2ProModel);
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.PostBytes = [1, 2, 3];
        var hostAudio = new TestAudioPlayer();
        var session = new StreamingSessionDouble();
        using var plugin = CreatePlugin(() => session);
        plugin.Init(CreateContext(settings: settings, httpService: httpService, audioPlayer: hostAudio));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        network.Set(true);
        await plugin.PlayAudioAsync("A prefix and its tail.");

        AssertEqual(1, session.Calls, "The public TTS entry should create exactly one native streaming session");
        AssertEqual(0, http.PostAsBytesCallCount, "Formal speech should not request the complete HTTP audio buffer");
        AssertEqual(0, hostAudio.PlayBytesCallCount, "Formal speech should not wait for host byte-array playback");
        AssertEqual(AppliedKey, session.Request!.ApiKey, "The session should receive the captured key");
        AssertEqual(settings.SelectedModel, session.Request.Model, "The session should receive the saved model");
        AssertEqual(1, session.DisposeCount, "The entry point should dispose its completed session");
    }

    internal static async Task SnapshotStaysImmutableWhileSettingsAreEditedAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var settings = CreateTtsSettings(FishAudioModelPolicy.S2ProModel);
        settings.AudioFormat = "mp3";
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new StreamingSessionDouble { Handler = (_, _, token) => release.Task.WaitAsync(token) };
        using var plugin = CreatePlugin(() => session);
        plugin.Init(CreateContext(settings: settings));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        var viewModel = plugin.GetOrCreateSettingsViewModel();
        network.Set(true);
        var playback = plugin.PlayAudioAsync("Immutable prefix and a longer tail.");
        await session.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(true, viewModel.IsApiKeyInputLocked, "Active streaming should lock its captured ViewModel key");
        viewModel.Speed = 1.75;
        viewModel.Mp3Bitrate = 64;
        viewModel.AudioFormat = "pcm";
        AssertEqual(1.25, session.Request!.Speed, "Later settings edits must not change an active request's prosody");
        AssertEqual(128, session.Request.Mp3Bitrate, "Later settings edits must not change active bitrate");
        AssertEqual("mp3", session.Request.AudioFormat, "Active format must remain immutable");
        release.TrySetResult();
        await playback.WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(false, viewModel.IsApiKeyInputLocked, "Completion should release only its own key operation");
    }

    internal static async Task SupersessionWaitsForOutputCleanupAcrossThreeCallsAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
        using var plugin = CreatePlugin(() => ++created == 1 ? first : final);
        plugin.Init(CreateContext(settings: CreateTtsSettings(FishAudioModelPolicy.S2ProModel)));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        network.Set(true);
        var firstCall = plugin.PlayAudioAsync("first");
        await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondCall = plugin.PlayAudioAsync("second");
        await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var thirdCall = plugin.PlayAudioAsync("third");
        AssertEqual(1, created, "Replacement calls must not create new output before prior cleanup completes");
        releaseCleanup.TrySetResult();
        await AssertCanceledAsync(firstCall);
        await AssertCanceledAsync(secondCall);
        await thirdCall.WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(2, created, "A superseded waiting call must not create a redundant synthesis session");
        AssertEqual("third", final.Text, "Only the latest call may acquire output after retirement");
        AssertEqual(1, first.DisposeCount, "Cancelled output must be disposed before replacement starts");
    }

    internal static async Task CallerAndDisposalCancelOwnedSessionsAsync()
    {
        foreach (var dispose in new[] { false, true })
        {
            using var network = OverrideNetworkAvailability(false);
            using var caller = new CancellationTokenSource();
            var session = new StreamingSessionDouble { Handler = (_, _, token) => Task.Delay(Timeout.InfiniteTimeSpan, token) };
            var snackbar = new TestSnackbar();
            using var plugin = CreatePlugin(() => session);
            plugin.Init(CreateContext(snackbar: snackbar, settings: CreateTtsSettings(FishAudioModelPolicy.S2ProModel)));
            await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
            network.Set(true);
            var playback = plugin.PlayAudioAsync("cancel", caller.Token);
            await session.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (dispose) plugin.Dispose(); else caller.Cancel();
            await AssertCanceledAsync(playback);
            AssertEqual(true, session.CancellationObserved, "Caller/disposal must reach the owned streaming token");
            AssertEqual(1, session.DisposeCount, "Cancellation must dispose the session");
            AssertEqual<string?>(null, snackbar.LastError, "Expected stop must not become a playback error snackbar");
        }
    }

    internal static async Task CancellationProbeObservesSynchronousRetirementAsync()
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
            await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
            network.Set(true);
            var playback = plugin.PlayAudioAsync("synchronous cancel", caller.Token);
            await session.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
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

    internal static async Task CancellationProbeRetainsOnlyActiveObservationAsync()
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

    internal static async Task TransportFailuresDoNotExposeCredentialsAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var settings = CreateTtsSettings(FishAudioModelPolicy.S2ProModel);
        var logger = new TestLogger();
        var snackbar = new TestSnackbar();
        var session = new StreamingSessionDouble();
        using var plugin = CreatePlugin(() => session);
        plugin.Init(CreateContext(snackbar: snackbar, settings: settings, logger: logger));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        network.Set(true);
        session.Handler = (_, _, _) => throw new System.IO.InvalidDataException(AppliedKey + " http://user:private-password@localhost:8080");
        await plugin.PlayAudioAsync("transport failure");
        AssertEqual("STranslate_Plugin_Tts_FishAudio_Streaming_Failed", snackbar.LastError, "Transport details must not appear in user errors");
        AssertEqual(false, logger.Contains(AppliedKey), "Transport exceptions must not expose keys through logging");
        AssertEqual(false, logger.Contains("private-password"), "Transport exceptions must not expose proxy authentication through logging");
    }

    internal static async Task AssertCanceledAsync(Task operation)
    {
        try { await operation.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TaskCanceledException) { return; }
        throw new InvalidOperationException("Expected a quiet host-compatible TaskCanceledException.");
    }
}
