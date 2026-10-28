using FishAudio.Testing.Execution;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

[TestFixture]
public sealed class FrameworkWatchdogTests
{
    private const long Frequency = 1_000;

    [Test]
    [Property("FishAudioCaseId", "infra.framework-watchdog/complete-envelope")]
    public void ACaseNeedsCleanupExitAndBothDrainsBeforeCompletion()
    {
        var watchdog = Create("a");
        watchdog.Observe(Event("ready", 10));
        watchdog.Observe(Event("case-start", 20, "a"));
        watchdog.Observe(Event("case-finished", 30, "a", outcome: "passed"));
        Assert.That(watchdog.State.Complete, Is.False);
        watchdog.Observe(Event("cleanup-complete", 40));
        Assert.That(watchdog.State.Complete, Is.False);
        watchdog.Observe(Event("final-published", 44));
        watchdog.AcceptFinal(45);
        watchdog.HostExited(50, successfulExit: true, stdoutDrained: true, stderrDrained: true);
        Assert.That(watchdog.State.Complete, Is.True);
        Assert.That(watchdog.State.PassedIds, Is.EqualTo(new[] { "a" }));
        Assert.That(watchdog.State.NotRunIds, Is.Empty);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-watchdog/late-completion")]
    public void AGapTimeoutCannotBeRehabilitatedByALatePassingResult()
    {
        var watchdog = Create("a", "b");
        watchdog.Observe(Event("ready", 10));
        watchdog.Observe(Event("case-start", 20, "a"));
        watchdog.Tick(20_020);
        Assert.That(watchdog.State.WarningCount, Is.EqualTo(1));
        watchdog.Tick(120_020);
        watchdog.Observe(Event("case-finished", 120_021, "a", outcome: "passed"));
        Assert.That(watchdog.State.FailureStage, Is.EqualTo("gap"));
        Assert.That(watchdog.State.FailureCaseId, Is.EqualTo("a"));
        Assert.That(watchdog.State.Complete, Is.False);
        Assert.That(watchdog.State.PassedIds, Is.Empty);
        Assert.That(watchdog.State.TimedOutIds, Is.EqualTo(new[] { "a" }));
        Assert.That(watchdog.State.NotRunIds, Does.Contain("b"));
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-watchdog/queue-deadline")]
    public void EnteringAnAwaitedDispatcherActionDoesNotRenewItsQueuedDeadline()
    {
        var watchdog = Create("a");
        watchdog.Observe(Event("ready", 10));
        watchdog.Observe(Event("case-start", 20, "a"));
        watchdog.Observe(Event("queued", 30, "a", ordinal: 1, queued: 30));
        watchdog.Observe(Event("entered", 110_000, "a", ordinal: 1, queued: 30));
        watchdog.Tick(120_030);
        Assert.That(watchdog.State.FailureStage, Is.EqualTo("action"));
        Assert.That(watchdog.State.FailureCaseId, Is.EqualTo("a"));
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-watchdog/dispatcher-startup")]
    public void DispatcherStartupHasItsOwnBoundAndLateReadinessCannotPass()
    {
        var watchdog = Create("a");
        watchdog.Observe(Event("ready", 10));
        watchdog.Observe(Event("case-start", 20, "a"));
        watchdog.Observe(Event("dispatcher-starting", 100_000, "a"));
        watchdog.Tick(219_999);
        Assert.That(watchdog.State.FailureStage, Is.Null);
        watchdog.Observe(Event("dispatcher-ready", 220_000, "a"));
        Assert.That(watchdog.State.FailureStage, Is.EqualTo("dispatcher-startup"));
        Assert.That(watchdog.State.Complete, Is.False);
    }

    [TestCase("missing-cleanup", TestName = "missing cleanup")]
    [TestCase("missing-output-drain", TestName = "missing output drain")]
    [TestCase("late-exit", TestName = "late exit")]
    public void FinalResultDoesNotReplaceTheSharedTenSecondExitEnvelope(string fault)
    {
        var watchdog = Create("a");
        watchdog.Observe(Event("ready", 10));
        watchdog.Observe(Event("case-start", 20, "a"));
        watchdog.Observe(Event("case-finished", 30, "a", outcome: "passed"));
        if (fault != "missing-cleanup")
        {
            watchdog.Observe(Event("cleanup-complete", 40));
            watchdog.Observe(Event("final-published", 44));
            watchdog.AcceptFinal(45);
        }
        watchdog.HostExited(fault == "late-exit" ? 10_045 : 50, successfulExit: true,
            stdoutDrained: fault != "missing-output-drain", stderrDrained: true);
        Assert.That(watchdog.State.Complete, Is.False);
        Assert.That(watchdog.State.FailureStage, Is.Not.Null);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-watchdog/failed-prefix")]
    public void AFailedCaseKeepsItsUnexecutedSuffixDistinctFromPassedCases()
    {
        var watchdog = Create("a", "b", "c");
        watchdog.Observe(Event("ready", 10));
        watchdog.Observe(Event("case-start", 20, "a"));
        watchdog.Observe(Event("case-finished", 30, "a", outcome: "passed"));
        watchdog.Observe(Event("case-start", 40, "b"));
        watchdog.Observe(Event("case-finished", 50, "b", outcome: "failed"));
        Assert.That(watchdog.State.PassedIds, Is.EqualTo(new[] { "a" }));
        Assert.That(watchdog.State.FailedIds, Is.EqualTo(new[] { "b" }));
        Assert.That(watchdog.State.NotRunIds, Is.EqualTo(new[] { "c" }));
        Assert.That(watchdog.State.Complete, Is.False);
        Assert.Throws<InvalidDataException>(() => watchdog.Observe(Event("case-start", 60, "c")));
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-watchdog/parallel-deadlines")]
    public void ACompletedParallelSiblingCannotRenewAnotherCasesDeadline()
    {
        var watchdog = new FrameworkWatchdog(new[] { "a", "b" }, 0, Frequency, maximumConcurrency: 2);
        watchdog.Observe(Event("ready", 10));
        watchdog.Observe(Event("case-start", 20, "a"));
        watchdog.Observe(Event("case-start", 30, "b"));
        watchdog.Observe(Event("case-finished", 110_000, "b", outcome: "passed"));
        watchdog.Tick(120_020);
        Assert.That(watchdog.State.PassedIds, Is.EqualTo(new[] { "b" }));
        Assert.That(watchdog.State.FailureCaseId, Is.EqualTo("a"));
        Assert.That(watchdog.State.FailureStage, Is.EqualTo("gap"));
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-watchdog/native-order")]
    public void AColdNativeProfileRejectsAnOutOfOrderFirstCase()
    {
        var watchdog = new FrameworkWatchdog(new[] { "cold", "warm" }, 0, Frequency, requireOrder: true);
        watchdog.Observe(Event("ready", 10));
        Assert.Throws<InvalidDataException>(() => watchdog.Observe(Event("case-start", 20, "warm")));
        Assert.That(watchdog.State.Complete, Is.False);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-watchdog/process-startup")]
    public void AnUnresponsiveHostCannotAvoidItsStartupDeadline()
    {
        var watchdog = Create("a");
        watchdog.Tick(120_000);
        Assert.That(watchdog.State.FailureStage, Is.EqualTo("child-startup"));
        Assert.That(watchdog.State.Complete, Is.False);
        Assert.That(watchdog.State.NotRunIds, Is.EqualTo(new[] { "a" }));
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-watchdog/incomplete-root")]
    public void APassingFrameworkResultCannotCompleteAnUnfinishedOwnedAction()
    {
        var watchdog = Create("a");
        watchdog.Observe(Event("ready", 10));
        watchdog.Observe(Event("case-start", 20, "a"));
        watchdog.Observe(Event("queued", 30, "a", ordinal: 1, queued: 30));
        watchdog.Observe(Event("entered", 40, "a", ordinal: 1, queued: 30));
        watchdog.Observe(Event("case-finished", 50, "a", outcome: "passed"));
        Assert.That(watchdog.State.FailureStage, Is.EqualTo("incomplete-root-action"));
        Assert.That(watchdog.State.PassedIds, Is.Empty);
        Assert.That(watchdog.State.InterruptedIds, Is.EqualTo(new[] { "a" }));
        Assert.That(watchdog.State.Complete, Is.False);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-watchdog/exit-chronology")]
    public void HostExitCannotPrecedeAnAlreadyObservedCleanupCompletion()
    {
        var watchdog = Create("a");
        watchdog.Observe(Event("ready", 10));
        watchdog.Observe(Event("case-start", 20, "a"));
        watchdog.Observe(Event("case-finished", 30, "a", outcome: "passed"));
        watchdog.Observe(Event("cleanup-complete", 9_000));
        Assert.Throws<InvalidDataException>(() => watchdog.HostExited(50,
            successfulExit: true, stdoutDrained: true, stderrDrained: true));
        Assert.That(watchdog.State.Complete, Is.False);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-watchdog/pre-final-cleanup-budget")]
    public void OneTimeCleanupUsesTheExistingPreFinalGuardBeforeTheExitAllowance()
    {
        var watchdog = Create("a");
        watchdog.Observe(Event("ready", 10));
        watchdog.Observe(Event("case-start", 20, "a"));
        watchdog.Observe(Event("case-finished", 30, "a", outcome: "passed"));
        watchdog.Tick(110_000);
        Assert.That(watchdog.State.FailureStage, Is.Null);
        watchdog.Observe(Event("cleanup-complete", 110_001));
        watchdog.Observe(Event("final-published", 110_001));
        watchdog.AcceptFinal(110_002);
        watchdog.HostExited(110_003, successfulExit: true, stdoutDrained: true, stderrDrained: true);
        Assert.That(watchdog.State.Complete, Is.True);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-watchdog/failed-pre-final-cleanup-budget")]
    public void AnOrdinaryFailureKeepsItsBoundedPreFinalCleanupAndUnexecutedSuffix()
    {
        var watchdog = Create("a", "b");
        watchdog.Observe(Event("ready", 10));
        watchdog.Observe(Event("case-start", 20, "a"));
        watchdog.Observe(Event("case-finished", 30, "a", outcome: "failed"));
        watchdog.Tick(110_000);
        Assert.That(watchdog.State.FailureStage, Is.EqualTo("case-failed"));
        Assert.That(watchdog.State.TerminationRequired, Is.False);
        watchdog.Observe(Event("cleanup-complete", 110_001));
        watchdog.Observe(Event("final-published", 110_001));
        watchdog.AcceptFinal(110_002);
        watchdog.HostExited(110_003, successfulExit: false, stdoutDrained: true, stderrDrained: true);
        Assert.That(watchdog.State.Complete, Is.False);
        Assert.That(watchdog.State.FailedIds, Is.EqualTo(new[] { "a" }));
        Assert.That(watchdog.State.NotRunIds, Is.EqualTo(new[] { "b" }));
        Assert.That(watchdog.State.TerminationRequired, Is.False);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-watchdog/parent-final-anchor")]
    public void TheParentOwnsOneSharedExitAllowanceAfterVerifiedFinalEvidence()
    {
        var watchdog = Create("a");
        watchdog.Observe(Event("ready", 10));
        watchdog.Observe(Event("case-start", 20, "a"));
        watchdog.Observe(Event("case-finished", 30, "a", outcome: "passed"));
        watchdog.Observe(Event("cleanup-complete", 109_999));
        watchdog.Observe(Event("final-published", 110_000));
        // Parent evidence handling may finish after the child interval would
        // otherwise expire. It does not consume the following exit allowance.
        watchdog.Tick(123_000);
        watchdog.AcceptFinal(123_001);
        watchdog.HostExited(133_000, successfulExit: true, stdoutDrained: true, stderrDrained: true);
        Assert.That(watchdog.State.Complete, Is.True);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-watchdog/pre-final-hard-boundary")]
    public void MissingFinalEvidenceRetainsThePreFinalHardBoundaryAndCannotRecoverLate()
    {
        var watchdog = Create("a");
        watchdog.Observe(Event("ready", 10));
        watchdog.Observe(Event("case-start", 20, "a"));
        watchdog.Observe(Event("case-finished", 30, "a", outcome: "passed"));
        watchdog.Tick(120_029);
        Assert.That(watchdog.State.FailureStage, Is.Null);
        watchdog.Tick(120_030);
        Assert.That(watchdog.State.FailureStage, Is.Not.Null);
        Assert.That(watchdog.State.TerminationRequired, Is.True);
        watchdog.Observe(Event("cleanup-complete", 120_031));
        watchdog.Observe(Event("final-published", 120_031));
        watchdog.AcceptFinal(120_032);
        watchdog.HostExited(120_033, successfulExit: true, stdoutDrained: true, stderrDrained: true);
        Assert.That(watchdog.State.TerminationRequired, Is.True);
        Assert.That(watchdog.State.Complete, Is.False);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-watchdog/publication-chronology")]
    public void FinalPublicationCannotPrecedeTheAcceptedCleanupBarrier()
    {
        var watchdog = Create("a");
        watchdog.Observe(Event("ready", 10));
        watchdog.Observe(Event("case-start", 20, "a"));
        watchdog.Observe(Event("case-finished", 30, "a", outcome: "passed"));
        watchdog.Observe(Event("cleanup-complete", 110_000));
        Assert.Throws<InvalidDataException>(() => watchdog.Observe(Event("final-published", 100_000)));
        Assert.That(watchdog.State.Complete, Is.False);
    }

    private static FrameworkWatchdog Create(params string[] ids) => new(ids, 0, Frequency);

    private static FrameworkExecutionEvent Event(string kind, long at, string? id = null,
        int ordinal = 0, long queued = 0, string? outcome = null)
        => new(kind, at, Frequency, id, ordinal, queued, outcome);
}
