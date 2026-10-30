using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(27)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class ContinuousHoverCases
{
    public static IEnumerable<TestCaseData> NumericWheelRequiresContinuousHoverRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.settings/native/continuous-hover/speed",
            27, "native-input/027-input.settings-native-continuous-hover-speed", "Speed");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/continuous-hover/volume",
            28, "native-input/028-input.settings-native-continuous-hover-volume", "Volume");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/continuous-hover/temperature",
            29, "native-input/029-input.settings-native-continuous-hover-temperature", "Temperature");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/continuous-hover/topp",
            30, "native-input/030-input.settings-native-continuous-hover-topp", "TopP");
    }

    [TestCaseSource(nameof(NumericWheelRequiresContinuousHoverRows))]
    [Order(27)]
    public Task NumericWheelRequiresContinuousHover(string property)
        => StaTestHost.RunOnStaThreadAsync(() => SettingsInputInteractionSuite.NumericWheelRequiresContinuousHover(property));
}
