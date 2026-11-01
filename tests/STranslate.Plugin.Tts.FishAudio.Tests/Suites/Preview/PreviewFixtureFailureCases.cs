using System.Diagnostics;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static TestAssertions;

internal static partial class PreviewSuite
{
    internal static async Task FixtureBodyFailureReleasesSameHeldTaskAsync(bool streamResponse)
    {
        var elapsed = Stopwatch.StartNew();
        var previousNetworkOverride = FishAudioRequestPolicy.NetworkAvailableOverride;
        var primary = new InvalidOperationException("preview-owned-body-failure");
        var secondary = new IOException("preview-owned-cleanup-failure");
        var cleanup = new ThrowingPreviewFixtureResource(secondary);
        var stream = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOriginalCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = streamResponse ? new CountingPreviewStream(3) : null;
        NetworkAvailabilityOverride? network = null;
        SettingsViewModel? viewModel = null;
        Task? originalCommand = null;
        Task? ownershipTask = null;
        CancellationToken token = default;
        Exception? observed = null;
        var verified = false;
        var factoryCalls = 0;
        var player = new TestPreviewAudioPlayer();
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            ownershipTask = PreviewCaseFixtureOwner.RunAsync(async owner =>
                {
                    network = owner.Scope(OverrideNetworkAvailability(true));
                    owner.Own(cleanup);
                    var (service, http) = TestHttpServiceProxy.Create();
                    var settings = new Settings
                    {
                        VoiceId = "11111111111111111111111111111111",
                        CachedVoice = new() { Title = "Owned preview", SampleAudioUrl = streamResponse ? PreviewScenario.Url : CreateExpiredPreviewUrl("owned-metadata.mp3") },
                    };
                    viewModel = owner.Own(new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings,
                        clearCoverImageCacheAsync: null, clearCoverImageCacheTimeout: null,
                        previewAudioPlayerFactory: () => { factoryCalls++; return player; }));
                    if (streamResponse)
                    {
                        owner.ReleaseOnCleanup(stream, () => late!);
                        http.GetAsStreamAsyncHandler = async (_, _, captured) =>
                        {
                            token = captured; started.SetResult();
                            var content = await stream.Task;
                            cleanupEntered.TrySetResult();
                            await releaseOriginalCompletion.Task.WaitAsync(PreviewFixtureRemaining(elapsed));
                            return content;
                        };
                    }
                    else
                    {
                        owner.ReleaseOnCleanup(metadata);
                        http.GetAsyncHandler = async (_, _, captured) =>
                        {
                            token = captured; started.SetResult();
                            var content = await metadata.Task;
                            cleanupEntered.TrySetResult();
                            await releaseOriginalCompletion.Task.WaitAsync(PreviewFixtureRemaining(elapsed));
                            return content;
                        };
                    }
                    originalCommand = owner.Track(viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null));
                    // A preflight that completes without invoking the fake HTTP
                    // boundary is a fixture setup failure, never pending work.
                    await Task.WhenAny(started.Task, originalCommand).WaitAsync(PreviewFixtureRemaining(elapsed));
                    AssertEqual(true, started.Task.IsCompleted, "The configured preview must enter its original HTTP handler before the injected body failure");
                    AssertEqual(false, originalCommand.IsCompleted, "The actual original command must still own its held response before the injected assertion failure");
                    throw primary;
                });
                try
                {
                    await Task.WhenAny(cleanupEntered.Task, ownershipTask).WaitAsync(PreviewFixtureRemaining(elapsed));
                    AssertEqual(true, cleanupEntered.Task.IsCompleted, "Cleanup must reach the same held original HTTP task before reporting the body failure");
                    AssertEqual(false, ownershipTask.IsCompleted, "The fixture cannot publish failure while that same original task is deliberately still held");
                    AssertEqual(false, originalCommand!.IsCompleted, "The completion gate belongs to the original pending command, not replacement work");
                    AssertEqual(false, ReferenceEquals(previousNetworkOverride, FishAudioRequestPolicy.NetworkAvailableOverride), "The global network scope remains owned until the original task has joined");
                }
                finally { releaseOriginalCompletion.TrySetResult(); }
            try { await ownershipTask; }
            catch (Exception error) { observed = error; }
            AssertEqual(true, ReferenceEquals(primary, observed), "The owned preview fixture must preserve the actual body failure when disposal also fails");
            AssertEqual(true, token.IsCancellationRequested, "Failure cleanup cancels the original pending preview owner");
            AssertEqual(true, originalCommand!.IsCompletedSuccessfully, "Failure reporting follows the original task's complete late-response cleanup");
            AssertEqual(true, ReferenceEquals(originalCommand, viewModel!.ToggleDisplayPreviewCommand.ExecutionTask), "Cleanup joins the original command rather than a replacement task");
            AssertEqual(false, viewModel.CopyVoiceIdCommand.CanExecute("11111111111111111111111111111111"), "Cleanup retires the same real ViewModel");
            AssertEqual(1, cleanup.DisposeCount, "The failing cleanup resource is attempted once without retry");
            AssertEqual(true, primary.Data[RegressionFixtureOwner.CleanupTypesKey] is List<string> types && types.Contains(typeof(IOException).FullName!), "The secondary cleanup failure remains attached without replacing the primary error");
            AssertEqual(true, ReferenceEquals(previousNetworkOverride, FishAudioRequestPolicy.NetworkAvailableOverride), "The exact prior global network override is restored after the same original task joins");
            AssertEqual(0, factoryCalls, "Failure cleanup cannot acquire local playback from a held response");
            AssertEqual(0, player.DisposeCallCount, "An unused factory result never becomes an owned player");
            if (streamResponse) AssertEqual(true, late!.Disposed, "The real cancelled downloader closes its late original response before the fixture returns");
            verified = true;
        },
        () => { if (!verified) viewModel?.Dispose(); return Task.CompletedTask; },
        () =>
        {
            if (!verified)
            {
                releaseOriginalCompletion.TrySetResult();
                metadata.TrySetResult("{}");
                if (late is not null && !stream.TrySetResult(late)) late.Dispose();
            }
            return Task.CompletedTask;
        },
        async () =>
        {
            if (verified || originalCommand is null) return;
            if (originalCommand.IsCompleted) await originalCommand;
            else
            {
                var remaining = RegressionExecution.Timeout - elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero) throw new RegressionExecutionTimeoutException();
                await originalCommand.WaitAsync(remaining);
            }
        },
        async () =>
        {
            if (verified || ownershipTask is null) return;
            if (ownershipTask.IsCompleted) await ownershipTask;
            else await ownershipTask.WaitAsync(PreviewFixtureRemaining(elapsed));
        },
        () => { if (!verified) network?.Dispose(); return Task.CompletedTask; });
    }

    internal static async Task FixtureSoleCleanupFailureRemainsFailureAsync()
    {
        var elapsed = Stopwatch.StartNew();
        var previousNetworkOverride = FishAudioRequestPolicy.NetworkAvailableOverride;
        NetworkAvailabilityOverride? network = null;
        var cleanupFailure = new IOException("preview-sole-cleanup-failure");
        var cleanup = new ThrowingPreviewFixtureResource(cleanupFailure);
        SettingsViewModel? viewModel = null;
        Task? originalCommand = null;
        Exception? observed = null;
        var verified = false;
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            try
            {
                await PreviewCaseFixtureOwner.RunAsync(async owner =>
                {
                    network = owner.Scope(OverrideNetworkAvailability(false));
                    owner.Own(cleanup);
                    var (service, _) = TestHttpServiceProxy.Create();
                    viewModel = owner.Own(new SettingsViewModel(CreateContext(httpService: service), new Settings(),
                        clearCoverImageCacheAsync: null, clearCoverImageCacheTimeout: null,
                        previewAudioPlayerFactory: () => new TestPreviewAudioPlayer()));
                    var item = new VoiceSearchItem { Id = "11111111111111111111111111111111", SampleAudioUrl = PreviewScenario.Url };
                    viewModel.SearchResults = [item];
                    originalCommand = owner.Track(viewModel.ToggleSearchItemPreviewCommand.ExecuteAsync(item));
                    AssertEqual(true, originalCommand.IsCompletedSuccessfully, "The original offline preflight succeeds as a completed command before sole cleanup failure");
                    await originalCommand;
                });
            }
            catch (Exception error) { observed = error; }
            AssertEqual(true, ReferenceEquals(cleanupFailure, observed), "A cleanup-only fault remains the actual case failure");
            AssertEqual(1, cleanup.DisposeCount, "A sole cleanup failure is never retried");
            AssertEqual(true, ReferenceEquals(previousNetworkOverride, FishAudioRequestPolicy.NetworkAvailableOverride), "A sole cleanup failure cannot skip restoration of the exact prior global override");
            AssertEqual(true, originalCommand!.IsCompletedSuccessfully, "A sole cleanup fault never replaces or reexecutes the original completed command");
            AssertEqual(false, viewModel!.CopyVoiceIdCommand.CanExecute("11111111111111111111111111111111"), "Other owned resource cleanup still retires the same ViewModel");
            verified = true;
        },
        () => { if (!verified) viewModel?.Dispose(); return Task.CompletedTask; },
        async () =>
        {
            if (verified || originalCommand is null) return;
            if (originalCommand.IsCompleted) await originalCommand;
            else
            {
                var remaining = RegressionExecution.Timeout - elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero) throw new RegressionExecutionTimeoutException();
                await originalCommand.WaitAsync(remaining);
            }
        },
        () => { if (!verified) network?.Dispose(); return Task.CompletedTask; });
    }

    private static TimeSpan PreviewFixtureRemaining(Stopwatch elapsed)
    {
        var remaining = RegressionExecution.Timeout - elapsed.Elapsed;
        if (remaining <= TimeSpan.Zero) throw new RegressionExecutionTimeoutException();
        return remaining;
    }

    private sealed class ThrowingPreviewFixtureResource(Exception failure) : IDisposable
    {
        internal int DisposeCount { get; private set; }
        public void Dispose() { DisposeCount++; throw failure; }
    }
}
