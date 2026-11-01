using NUnit.Framework;

namespace NUnitCases.Runner;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
public sealed class RunnerExecutionCases
{
    [Test]
    [Property("FishAudioCaseId", "infra.runner/execution/invalid-guards-do-not-dispatch-or-warm-sta")]
    public Task InvalidGuardsDoNotDispatchOrWarmStaAsync() =>
        RegressionExecutionSuite.InvalidGuardsDoNotDispatchOrWarmStaAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/execution/original-deadline-and-late-completion-keep-the-same-task/nongeneric")]
    public Task OriginalDeadlineAndLateCompletionKeepTheSameTaskAsyncNongeneric() =>
        RegressionExecutionSuite.OriginalDeadlineAndLateCompletionKeepTheSameTaskAsync(false);

    [Test]
    [Property("FishAudioCaseId", "infra.runner/execution/original-deadline-and-late-completion-keep-the-same-task/generic")]
    public Task OriginalDeadlineAndLateCompletionKeepTheSameTaskAsyncGeneric() =>
        RegressionExecutionSuite.OriginalDeadlineAndLateCompletionKeepTheSameTaskAsync(true);

    [Test]
    [Property("FishAudioCaseId", "infra.runner/execution/warning-retires-without-renewing-the-deadline/nongeneric")]
    public Task WarningRetiresWithoutRenewingTheDeadlineAsyncNongeneric() =>
        RegressionExecutionSuite.WarningRetiresWithoutRenewingTheDeadlineAsync(false);

    [Test]
    [Property("FishAudioCaseId", "infra.runner/execution/warning-retires-without-renewing-the-deadline/generic")]
    public Task WarningRetiresWithoutRenewingTheDeadlineAsyncGeneric() =>
        RegressionExecutionSuite.WarningRetiresWithoutRenewingTheDeadlineAsync(true);

    [Test]
    [Property("FishAudioCaseId", "infra.runner/execution/warning-failure-cannot-replace-the-deadline")]
    public Task WarningFailureCannotReplaceTheDeadlineAsync() =>
        RegressionExecutionSuite.WarningFailureCannotReplaceTheDeadlineAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/execution/completed-fault-retains-its-original-exception/nongeneric")]
    public Task CompletedFaultRetainsItsOriginalExceptionAsyncNongeneric() =>
        RegressionExecutionSuite.CompletedFaultRetainsItsOriginalExceptionAsync(false);

    [Test]
    [Property("FishAudioCaseId", "infra.runner/execution/completed-fault-retains-its-original-exception/generic")]
    public Task CompletedFaultRetainsItsOriginalExceptionAsyncGeneric() =>
        RegressionExecutionSuite.CompletedFaultRetainsItsOriginalExceptionAsync(true);

    [Test]
    [Property("FishAudioCaseId", "infra.runner/execution/guard-follows-the-original-task-across-await")]
    public Task GuardFollowsTheOriginalTaskAcrossAwaitAsync() =>
        RegressionExecutionSuite.GuardFollowsTheOriginalTaskAcrossAwaitAsync();

}
