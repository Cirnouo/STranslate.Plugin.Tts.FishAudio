using NUnit.Framework;

namespace NUnitCases.NativeSettings;

// Source candidate only. The complete profile retains its original cold-first-use order.
[TestFixture]
[NonParallelizable]
[Order(0)]
[Property("FishAudioExecutionProfile", "native-settings")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class OperationActivityDispatcherCases
{
    public static IEnumerable<TestCaseData> PlaybackRows()
    {
        yield return NativeTestCaseSource.Row(
            "lifecycle.activity-dispatcher/playback-timeout-retires-bound-commands-on-ui-thread",
            0, "native-settings/000-playback-timeout-retires-bound-commands-on-ui-thread", false);
        yield return NativeTestCaseSource.Row(
            "lifecycle.activity-dispatcher/playback-cancellation-retires-bound-commands-on-ui-thread",
            1, "native-settings/001-playback-cancellation-retires-bound-commands-on-ui-thread", true);
    }

    [TestCaseSource(nameof(PlaybackRows))]
    [Order(0)]
    public Task PlaybackRetiresBoundCommands(bool cancel)
        => OperationActivityDispatcherSuite.ObserveDispatcherActionAsync(
            cancel ? "cancellation" : "timeout",
            stage => OperationActivityDispatcherSuite.PlaybackRetiresBoundCommandsAsync(cancel, stage));

    public static IEnumerable<TestCaseData> QueuedRetirementRows()
    {
        yield return NativeTestCaseSource.Row(
            "lifecycle.activity-dispatcher/queued-retirement-cannot-unlock-new-ui-operation",
            2, "native-settings/002-queued-retirement-cannot-unlock-new-ui-operation");
    }

    [TestCaseSource(nameof(QueuedRetirementRows))]
    [Order(2)]
    public Task QueuedRetirementCannotUnlockNewUiOperation()
        => OperationActivityDispatcherSuite.QueuedRetirementCannotUnlockNewUiOperationAsync();
}
