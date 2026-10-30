using NUnit.Framework;

namespace NUnitCases.Runner;

[TestFixture]
[NonParallelizable]
public sealed class RunnerProcessCases
{
    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/playback-timeout-assertions-cannot-accept-their-own-guard")]
    public Task PlaybackTimeoutAssertionsCannotAcceptTheirOwnGuardAsync() =>
        RegressionProcessSuite.PlaybackTimeoutAssertionsCannotAcceptTheirOwnGuardAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/playback-stall-clock-requires-the-outstanding-data-baseline")]
    public Task PlaybackStallClockRequiresTheOutstandingDataBaselineAsync() =>
        RegressionProcessSuite.PlaybackStallClockRequiresTheOutstandingDataBaselineAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/pure-owners-use-independent-deadlines-and-preserve-original-errors")]
    public Task PureOwnersUseIndependentDeadlinesAndPreserveOriginalErrorsAsync() =>
        RegressionProcessSuite.PureOwnersUseIndependentDeadlinesAndPreserveOriginalErrorsAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/warning-and-hard-deadline-use-the-original-queued-clock")]
    public void WarningAndHardDeadlineUseTheOriginalQueuedClock() =>
        RegressionProcessSuite.WarningAndHardDeadlineUseTheOriginalQueuedClock();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/separate-roots-and-startup-do-not-become-an-aggregate-deadline")]
    public void SeparateRootsAndStartupDoNotBecomeAnAggregateDeadline() =>
        RegressionProcessSuite.SeparateRootsAndStartupDoNotBecomeAnAggregateDeadline();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/action-context-survives-await-and-nested-off-sta-dispatch")]
    public Task ActionContextSurvivesAwaitAndNestedOffStaDispatchAsync() =>
        RegressionProcessSuite.ActionContextSurvivesAwaitAndNestedOffStaDispatchAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/strict-evidence-rejects-foreign-incomplete-and-reordered-manifests")]
    public void StrictEvidenceRejectsForeignIncompleteAndReorderedManifests() =>
        RegressionProcessSuite.StrictEvidenceRejectsForeignIncompleteAndReorderedManifests();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/atomic-evidence-replacement-preserves-an-open-reader-snapshot")]
    public void AtomicEvidenceReplacementPreservesAnOpenReaderSnapshot() =>
        RegressionProcessSuite.AtomicEvidenceReplacementPreservesAnOpenReaderSnapshot();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/concurrent-immutable-snapshots-never-compete-with-the-writer")]
    public Task ConcurrentImmutableSnapshotsNeverCompeteWithTheWriterAsync() =>
        RegressionProcessSuite.ConcurrentImmutableSnapshotsNeverCompeteWithTheWriterAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/child-completion-never-replaces-an-active-manifest")]
    public Task ChildCompletionNeverReplacesAnActiveManifestAsync() =>
        RegressionProcessSuite.ChildCompletionNeverReplacesAnActiveManifestAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/child-publication-failure-preserves-the-owned-failure")]
    public Task ChildPublicationFailurePreservesTheOwnedFailureAsync() =>
        RegressionProcessSuite.ChildPublicationFailurePreservesTheOwnedFailureAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/fake-transport-stops-only-the-failed-partition")]
    public Task FakeTransportStopsOnlyTheFailedPartitionAsync() =>
        RegressionProcessSuite.FakeTransportStopsOnlyTheFailedPartitionAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/fake-transport-cannot-rehabilitate-the-same-late-action")]
    public Task FakeTransportCannotRehabilitateTheSameLateActionAsync() =>
        RegressionProcessSuite.FakeTransportCannotRehabilitateTheSameLateActionAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/fake-transport-rejects-unconfirmed-passes")]
    public Task FakeTransportRejectsUnconfirmedPassesAsync() =>
        RegressionProcessSuite.FakeTransportRejectsUnconfirmedPassesAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/exit-cannot-skip-completion-frames-queued-by-the-final-drain")]
    public Task ExitCannotSkipCompletionFramesQueuedByTheFinalDrainAsync() =>
        RegressionProcessSuite.ExitCannotSkipCompletionFramesQueuedByTheFinalDrainAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/pipe-cleanup-cannot-replace-adurable-case-failure")]
    public Task PipeCleanupCannotReplaceADurableCaseFailureAsync() =>
        RegressionProcessSuite.PipeCleanupCannotReplaceADurableCaseFailureAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/progress-cleanup-joins-an-unstarted-consumer-after-failure")]
    public Task ProgressCleanupJoinsAnUnstartedConsumerAfterFailureAsync() =>
        RegressionProcessSuite.ProgressCleanupJoinsAnUnstartedConsumerAfterFailureAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/blocked-progress-cannot-block-supervision-and-retires-queued-writes")]
    public Task BlockedProgressCannotBlockSupervisionAndRetiresQueuedWritesAsync() =>
        RegressionProcessSuite.BlockedProgressCannotBlockSupervisionAndRetiresQueuedWritesAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/listing-never-starts-aprocess")]
    public Task ListingNeverStartsAProcessAsync() =>
        RegressionProcessSuite.ListingNeverStartsAProcessAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/real-children-drain-both-pipes-and-require-final-evidence")]
    public Task RealChildrenDrainBothPipesAndRequireFinalEvidenceAsync() =>
        RegressionProcessSuite.RealChildrenDrainBothPipesAndRequireFinalEvidenceAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/real-child-hard-expiry-kills-its-descendant")]
    public Task RealChildHardExpiryKillsItsDescendantAsync() =>
        RegressionProcessSuite.RealChildHardExpiryKillsItsDescendantAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/process/legacy-requests-retain-private-arguments-and-use-the-same-host")]
    public Task LegacyRequestsRetainPrivateArgumentsAndUseTheSameHostAsync() =>
        RegressionProcessSuite.LegacyRequestsRetainPrivateArgumentsAndUseTheSameHostAsync();

}
