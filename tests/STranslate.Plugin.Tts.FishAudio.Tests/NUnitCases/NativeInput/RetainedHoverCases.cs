using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(31)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class RetainedHoverCases
{
    public static IEnumerable<TestCaseData> NumericHoverRearmsWhenPointerRemainsOverTargetRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.settings/native/hover-rearms-over-target",
            31, "native-input/031-input.settings-native-hover-rearms-over-target");
    }

    [TestCaseSource(nameof(NumericHoverRearmsWhenPointerRemainsOverTargetRows))]
    [Order(31)]
    public Task NumericHoverRearmsWhenPointerRemainsOverTarget()
        => StaTestHost.RunOnStaThreadAsync(SettingsInputInteractionSuite.NumericHoverRearmsWhenPointerRemainsOverTarget);
}
