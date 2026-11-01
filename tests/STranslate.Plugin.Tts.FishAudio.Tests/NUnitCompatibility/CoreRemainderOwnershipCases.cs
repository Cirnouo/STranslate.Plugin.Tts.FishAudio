using System.Runtime.ExceptionServices;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
public sealed class CoreRemainderOwnershipCases
{
    private const string VoiceId = "11111111111111111111111111111111";

    private static IEnumerable<TestCaseData> RequestStages()
    {
        yield return new TestCaseData("first-search-held", 1, 1)
            .SetName("ReplacementRequests_FirstPendingSearchFailureJoinsOriginalWork")
            .SetProperty("FishAudioCaseId", "infra.voice-discovery/replacement-first-search-failure-joins-original-work");
        yield return new TestCaseData("by-id-held", 3, 2)
            .SetName("ReplacementRequests_PendingByIdFailureJoinsOriginalWork")
            .SetProperty("FishAudioCaseId", "infra.voice-discovery/replacement-by-id-failure-joins-original-work");
    }

    [TestCaseSource(nameof(RequestStages))]
    public Task ReplacementRequestBodyFailureRetiresActualWork(string stage, int operations, int requests) =>
        CheckDiscoveryOwnershipAsync(
            probe => VoiceDiscoverySuite.RunVoiceLookupRequestsCancelPreviousAndDisposeWorkAsync(probe),
            stage, operations, requests, expectedViews: 1, cancellationThrows: false);

    private static IEnumerable<TestCaseData> CompletionStages()
    {
        yield return new TestCaseData("search-held", 1, 1)
            .SetName("LateCompletion_PendingSearchFailureJoinsOriginalWork")
            .SetProperty("FishAudioCaseId", "infra.voice-discovery/late-completion-search-failure-joins-original-work");
        yield return new TestCaseData("by-id-held", 2, 2)
            .SetName("LateCompletion_PendingByIdFailureJoinsOriginalWork")
            .SetProperty("FishAudioCaseId", "infra.voice-discovery/late-completion-by-id-failure-joins-original-work");
    }

    [TestCaseSource(nameof(CompletionStages))]
    public Task LateCompletionBodyFailureRetiresActualWork(string stage, int operations, int views) =>
        CheckDiscoveryOwnershipAsync(
            probe => VoiceDiscoverySuite.RunVoiceLookupCompletionsAfterDisposeDoNotMutateStateAsync(probe),
            stage, operations, expectedRequests: 0, expectedViews: views, cancellationThrows: false);

    [Test]
    [Property("FishAudioCaseId", "infra.voice-discovery/throwing-cancellation-body-failure-joins-both-original-lookups")]
    public Task ThrowingCancellationBodyFailureRetiresBothOriginalLookups() =>
        CheckDiscoveryOwnershipAsync(
            probe => VoiceDiscoverySuite.RunDisposeRestoresDiscoveryBusyStateBeforeCancelCallbacksAsync(probe),
            "both-lookups-held", expectedOperations: 2, expectedRequests: 0, expectedViews: 1,
            cancellationThrows: true);

    private static async Task CheckDiscoveryOwnershipAsync(
        Func<Action<DiscoveryRemainderFixtureOwner, string>, Task> run,
        string targetStage, int expectedOperations, int expectedRequests, int expectedViews,
        bool cancellationThrows)
    {
        var marker = new InvalidOperationException("core-remainder-discovery-owned-body-failure");
        DiscoveryRemainderFixtureOwner? ownership = null;
        Task? scenario = null;
        var ready = false;
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            scenario = run((observed, stage) =>
            {
                // Keep every acquisition for independent rescue, even if a
                // readiness assertion fails before the deliberate marker.
                ownership = observed;
                if (!string.Equals(stage, targetStage, StringComparison.Ordinal)) return;
                var view = observed.Views[^1];
                var command = stage == "first-search-held" || stage == "search-held"
                    ? view.SearchVoicesCommand.ExecutionTask
                    : view.SubmitVoiceIdCommand.ExecutionTask;
                Assert.Multiple(() =>
                {
                    Assert.That(observed.Views, Has.Count.EqualTo(expectedViews));
                    Assert.That(observed.Operations, Has.Count.EqualTo(expectedOperations));
                    Assert.That(observed.Requests, Has.Count.EqualTo(expectedRequests));
                    Assert.That(observed.Operations[^1], Is.SameAs(command));
                    Assert.That(observed.Operations[^1].IsCompleted, Is.False,
                        "discovery-fault-must-observe-the-original-pending-command");
                    Assert.That(observed.Views[^1].CopyVoiceIdCommand.CanExecute(VoiceId), Is.True);
                    if (stage == "both-lookups-held")
                        Assert.That(observed.Operations.All(task => !task.IsCompleted), Is.True);
                    if (stage == "first-search-held")
                    {
                        Assert.That(observed.Requests[0].Task.IsCompleted, Is.False);
                        Assert.That(observed.Requests[0].Token.IsCancellationRequested, Is.False);
                    }
                    if (stage == "by-id-held" && expectedRequests > 0)
                    {
                        Assert.That(observed.Requests[^1].Task.IsCompleted, Is.False);
                        Assert.That(observed.Requests[^1].Token.IsCancellationRequested, Is.False);
                    }
                    if (expectedRequests == 0)
                        Assert.That(observed.Responses[^1].IsCompleted, Is.False);
                });
                ready = true;
                throw marker;
            });
            Exception? failure = null;
            try { await scenario; }
            catch (Exception error) { failure = error; }

            // All assertions run before independent rescue releases anything.
            Assert.That(ready, Is.True, "discovery-fault-did-not-reach-real-pending-work");
            Assert.That(ownership, Is.Not.Null);
            var completed = ownership!;
            Assert.Multiple(() =>
            {
                Assert.That(scenario.IsFaulted, Is.True);
                Assert.That(failure, Is.SameAs(marker), "discovery-cleanup-must-preserve-primary-identity");
                Assert.That(completed.Operations.All(task => task.IsCompletedSuccessfully), Is.True,
                    "discovery-owner-returned-before-original-commands-completed");
                Assert.That(completed.Requests.All(request => request.Task.IsCompleted), Is.True,
                    "discovery-owner-returned-before-original-requests-completed");
                Assert.That(completed.Requests.All(request => request.Token.IsCancellationRequested), Is.True);
                Assert.That(completed.Responses.All(task => task.IsCompletedSuccessfully), Is.True,
                    "discovery-owner-returned-before-original-responses-released");
                Assert.That(completed.RetiredViews, Has.Count.EqualTo(completed.Views.Count),
                    "discovery-owner-returned-before-complete-original-disposal");
                Assert.That(completed.Views.All(view => !view.CopyVoiceIdCommand.CanExecute(VoiceId)), Is.True);
                if (cancellationThrows)
                    Assert.That(marker.Data[RegressionFixtureOwner.CleanupTypesKey],
                        Is.EqualTo(new[] { typeof(AggregateException).FullName }),
                        "throwing-cancellation-cleanup-failure-must-remain-visible");
                else
                    Assert.That(marker.Data[RegressionFixtureOwner.CleanupTypesKey], Is.Null,
                        "discovery-unknown-cleanup-failure-must-remain-visible");
            });
        }, () => RescueDiscoveryAsync(ownership, cancellationThrows));
    }

    [Test]
    [Property("FishAudioCaseId", "infra.cover-cache/busy-clear-body-failure-joins-original-clear")]
    public Task BusyClearBodyFailureJoinsOriginalClear() => CheckCoverOwnershipAsync(
        probe => CoverImageCacheSuite.RunClearCoverImageCacheCommandTracksBusyStateAsync(probe),
        expectedOperations: 1, expectedClearTasks: 1, expectsTimeoutContinuation: false);

    [Test]
    [Property("FishAudioCaseId", "infra.cover-cache/timed-out-clear-body-failure-joins-original-continuation")]
    public Task TimedOutClearBodyFailureJoinsOriginalContinuation() => CheckCoverOwnershipAsync(
        probe => CoverImageCacheSuite.RunClearCoverImageCacheCommandTimesOutAndRestoresButtonAsync(probe),
        expectedOperations: 1, expectedClearTasks: 1, expectsTimeoutContinuation: true);

    [Test]
    [Property("FishAudioCaseId", "infra.cover-cache/late-clear-body-failure-joins-both-original-clears")]
    public Task LateClearBodyFailureJoinsBothOriginalClears() => CheckCoverOwnershipAsync(
        probe => CoverImageCacheSuite.RunLateClearCoverImageCacheTaskDoesNotUnlockNewOperationAsync(probe),
        expectedOperations: 2, expectedClearTasks: 2, expectsTimeoutContinuation: true);

    [Test]
    [Property("FishAudioCaseId", "infra.cover-cache/second-timeout-retains-both-original-continuations")]
    public async Task SecondClearTimeoutRetainsAndJoinsBothOriginalContinuations()
    {
        var marker = new InvalidOperationException("core7-01-owned-body-failure");
        CoverRemainderFixtureOwner? ownership = null;
        Task? scenario = null;
        Task? firstContinuation = null;
        Task? secondContinuation = null;
        var independentlyObserved = new List<Task>();
        var ready = false;
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            scenario = CoverImageCacheSuite.RunLateClearCoverImageCacheTaskDoesNotUnlockNewOperationAsync(
                onHeldWorkAsync: async observed =>
                {
                    ownership = observed;
                    var view = observed.Views.Single();
                    // Read the actual first value before the same facade's next
                    // existing timeout can replace its last-task observation.
                    var first = view.TimedOutCoverCacheContinuationTask
                        ?? throw new InvalidOperationException("core7-01-first-continuation-unavailable");
                    firstContinuation = first;
                    independentlyObserved.Add(first);
                    Assert.Multiple(() =>
                    {
                        Assert.That(observed.Operations, Has.Count.EqualTo(2));
                        Assert.That(observed.ClearTasks, Has.Count.EqualTo(2));
                        Assert.That(observed.Operations[0].IsCompletedSuccessfully, Is.True);
                        Assert.That(observed.Operations[1], Is.SameAs(view.ClearCoverImageCacheCommand.ExecutionTask));
                        Assert.That(observed.Operations[1].IsCompleted, Is.False,
                            "core7-01-second-original-command-must-still-be-pending");
                        Assert.That(observed.ClearTasks.All(task => !task.IsCompleted), Is.True);
                        Assert.That(first.IsCompleted, Is.False);
                        Assert.That(observed.TimeoutContinuations, Has.Count.EqualTo(1));
                        Assert.That(observed.TimeoutContinuations[0], Is.SameAs(first),
                            "core7-01-first-continuation-must-be-captured-before-second-timeout");
                    });

                    // Await only the original second command. Its existing
                    // 150 ms timeout is the stimulus; both original clears stay
                    // held. This creates no timer, delay, gate or new deadline.
                    await observed.Operations[1];
                    var second = view.TimedOutCoverCacheContinuationTask
                        ?? throw new InvalidOperationException("core7-01-second-continuation-unavailable");
                    secondContinuation = second;
                    independentlyObserved.Add(second);
                    Assert.Multiple(() =>
                    {
                        Assert.That(observed.Operations.All(task => task.IsCompletedSuccessfully), Is.True);
                        Assert.That(observed.ClearTasks.All(task => !task.IsCompleted), Is.True,
                            "core7-01-original-clear-responses-must-remain-held-through-both-timeouts");
                        Assert.That(first, Is.Not.SameAs(second));
                        Assert.That(first.IsCompleted, Is.False);
                        Assert.That(second.IsCompleted, Is.False);
                    });
                    ready = true;
                    throw marker;
                });
            Exception? failure = null;
            try { await scenario; }
            catch (Exception error) { failure = error; }

            // These ownership observations precede any independent rescue.
            Assert.That(ready, Is.True, "core7-01-did-not-observe-both-original-timeouts");
            Assert.That(ownership, Is.Not.Null);
            Assert.That(firstContinuation, Is.Not.Null);
            Assert.That(secondContinuation, Is.Not.Null);
            var completed = ownership!;
            var captured = completed.TimeoutContinuations;
            Assert.Multiple(() =>
            {
                Assert.That(scenario.IsFaulted, Is.True);
                Assert.That(failure, Is.SameAs(marker), "core7-01-cleanup-must-preserve-primary-identity");
                Assert.That(captured.Any(task => ReferenceEquals(task, firstContinuation)), Is.True,
                    "core7-01-owner-lost-the-exact-first-continuation");
                Assert.That(captured.Any(task => ReferenceEquals(task, secondContinuation)), Is.True,
                    "core7-01-owner-lost-the-exact-second-continuation");
                Assert.That(captured, Has.Count.EqualTo(2));
                Assert.That(firstContinuation!.IsCompletedSuccessfully, Is.True,
                    "core7-01-owner-returned-before-first-continuation-retired");
                Assert.That(secondContinuation!.IsCompletedSuccessfully, Is.True,
                    "core7-01-owner-returned-before-second-continuation-retired");
                Assert.That(completed.Operations.All(task => task.IsCompletedSuccessfully), Is.True);
                Assert.That(completed.ClearTasks.All(task => task.IsCompletedSuccessfully), Is.True);
                Assert.That(completed.Views.All(view => !view.CopyVoiceIdCommand.CanExecute(VoiceId)), Is.True);
                Assert.That(marker.Data[RegressionFixtureOwner.CleanupTypesKey], Is.Null,
                    "core7-01-unknown-cleanup-failure-must-remain-visible");
            });
        }, () => RescueCoverAsync(ownership, independentlyObserved));
    }

    private static async Task CheckCoverOwnershipAsync(
        Func<Action<CoverRemainderFixtureOwner>, Task> run,
        int expectedOperations, int expectedClearTasks, bool expectsTimeoutContinuation)
    {
        var marker = new InvalidOperationException("core-remainder-clear-owned-body-failure");
        CoverRemainderFixtureOwner? ownership = null;
        Task? scenario = null;
        var ready = false;
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            scenario = run(observed =>
            {
                ownership = observed;
                Assert.Multiple(() =>
                {
                    Assert.That(observed.Views, Has.Count.EqualTo(1));
                    Assert.That(observed.Operations, Has.Count.EqualTo(expectedOperations));
                    Assert.That(observed.ClearTasks, Has.Count.EqualTo(expectedClearTasks));
                    Assert.That(observed.ClearTasks.All(task => !task.IsCompleted), Is.True,
                        "clear-fault-must-observe-the-original-held-clear-tasks");
                    Assert.That(observed.Operations[^1],
                        Is.SameAs(observed.Views[0].ClearCoverImageCacheCommand.ExecutionTask));
                    if (expectsTimeoutContinuation)
                    {
                        Assert.That(observed.Operations[0].IsCompletedSuccessfully, Is.True);
                        Assert.That(observed.TimeoutContinuations, Has.Count.EqualTo(1));
                        Assert.That(observed.TimeoutContinuations[0].IsCompleted, Is.False,
                            "clear-fault-must-observe-the-original-held-timeout-continuation");
                    }
                    else
                        Assert.That(observed.Operations[0].IsCompleted, Is.False);
                    if (expectedOperations == 2)
                        Assert.That(observed.Operations[1].IsCompleted, Is.False);
                });
                ready = true;
                throw marker;
            });
            Exception? failure = null;
            try { await scenario; }
            catch (Exception error) { failure = error; }
            Assert.That(ready, Is.True, "clear-fault-did-not-reach-real-held-work");
            Assert.That(ownership, Is.Not.Null);
            var completed = ownership!;
            Assert.Multiple(() =>
            {
                Assert.That(scenario.IsFaulted, Is.True);
                Assert.That(failure, Is.SameAs(marker), "clear-cleanup-must-preserve-primary-identity");
                Assert.That(completed.ClearTasks.All(task => task.IsCompletedSuccessfully), Is.True,
                    "clear-owner-returned-before-original-clear-tasks-completed");
                Assert.That(completed.Operations.All(task => task.IsCompletedSuccessfully), Is.True,
                    "clear-owner-returned-before-original-commands-completed");
                Assert.That(completed.TimeoutContinuations.All(task => task.IsCompletedSuccessfully), Is.True,
                    "clear-owner-returned-before-actual-timeout-continuation-completed");
                Assert.That(completed.Views.All(view => !view.CopyVoiceIdCommand.CanExecute(VoiceId)), Is.True,
                    "clear-owner-returned-before-original-view-retired");
                Assert.That(marker.Data[RegressionFixtureOwner.CleanupTypesKey], Is.Null,
                    "clear-unknown-cleanup-failure-must-remain-visible");
            });
        }, () => RescueCoverAsync(ownership));
    }

    private static async Task RescueDiscoveryAsync(DiscoveryRemainderFixtureOwner? owner, bool cancellationThrows)
    {
        if (owner is null) return;
        ExceptionDispatchInfo? failure = null;
        foreach (var view in owner.Views)
            try { view.Dispose(); }
            catch (AggregateException) when (cancellationThrows) { }
            catch (Exception error) { Keep(ref failure, error); }
        foreach (var release in owner.ResponseReleases)
            try { release(); }
            catch (Exception error) { Keep(ref failure, error); }
        foreach (var operation in owner.Operations)
            try { await operation; }
            catch (Exception error) { Keep(ref failure, error); }
        foreach (var request in owner.Requests)
            try { await request.Task; }
            catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
            catch (Exception error) { Keep(ref failure, error); }
        foreach (var response in owner.Responses)
            try { await response; }
            catch (Exception error) { Keep(ref failure, error); }
        foreach (var view in owner.Views)
            try { view.Dispose(); }
            catch (Exception error) { Keep(ref failure, error); }
        foreach (var registration in owner.Registrations)
            try { registration.Dispose(); }
            catch (Exception error) { Keep(ref failure, error); }
        failure?.Throw();
    }

    private static async Task RescueCoverAsync(
        CoverRemainderFixtureOwner? owner, IReadOnlyList<Task>? independentlyObserved = null)
    {
        if (owner is null) return;
        ExceptionDispatchInfo? failure = null;
        var continuations = new List<Task>();
        // Retain observations before any release or command join can expose a
        // different last value. This rescue does not call candidate capture.
        if (independentlyObserved is not null)
            foreach (var task in independentlyObserved) RetainContinuation(task);
        foreach (var task in owner.TimeoutContinuations) RetainContinuation(task);
        CaptureCurrentValues();
        foreach (var release in owner.ClearReleases)
            try { release(); }
            catch (Exception error) { Keep(ref failure, error); }
        foreach (var operation in owner.Operations)
            try { await operation; }
            catch (Exception error) { Keep(ref failure, error); }
        // A command may have timed out while it was being joined. Preserve any
        // distinct actual late task alongside every already retained reference.
        CaptureCurrentValues();
        foreach (var clear in owner.ClearTasks)
            try { await clear; }
            catch (Exception error) { Keep(ref failure, error); }
        foreach (var continuation in continuations)
            try { await continuation; }
            catch (Exception error) { Keep(ref failure, error); }
        foreach (var view in owner.Views)
            try { view.Dispose(); }
            catch (Exception error) { Keep(ref failure, error); }
        failure?.Throw();

        void CaptureCurrentValues()
        {
            foreach (var view in owner.Views)
                try
                {
                    if (view.TimedOutCoverCacheContinuationTask is { } task)
                        RetainContinuation(task);
                }
                catch (Exception error) { Keep(ref failure, error); }
        }

        void RetainContinuation(Task task)
        {
            if (!continuations.Any(existing => ReferenceEquals(existing, task)))
                continuations.Add(task);
        }
    }

    private static void Keep(ref ExceptionDispatchInfo? failure, Exception error)
    {
        if (failure is null) { failure = ExceptionDispatchInfo.Capture(error); return; }
        if (ReferenceEquals(failure.SourceException, error)) return;
        try
        {
            if (failure.SourceException.Data[RegressionFixtureOwner.CleanupTypesKey] is not List<string> types)
                failure.SourceException.Data[RegressionFixtureOwner.CleanupTypesKey] = types = [];
            types.Add(error.GetType().FullName ?? error.GetType().Name);
        }
        catch { /* Secondary diagnostics cannot replace the original rescue failure. */ }
    }
}
