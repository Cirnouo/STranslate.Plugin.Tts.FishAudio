using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(32)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class RestingWheelCases
{
    public static IEnumerable<TestCaseData> RestingNumericWheelEditsWithoutScrollingRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.settings/native/resting-wheel/speed",
            32, "native-input/032-input.settings-native-resting-wheel-speed", "Speed");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/resting-wheel/volume",
            33, "native-input/033-input.settings-native-resting-wheel-volume", "Volume");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/resting-wheel/temperature",
            34, "native-input/034-input.settings-native-resting-wheel-temperature", "Temperature");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/resting-wheel/topp",
            35, "native-input/035-input.settings-native-resting-wheel-topp", "TopP");
    }

    [TestCaseSource(nameof(RestingNumericWheelEditsWithoutScrollingRows))]
    [Order(32)]
    public Task RestingNumericWheelEditsWithoutScrolling(string property)
        => StaTestHost.RunOnStaThreadAsync(() => SettingsInputInteractionSuite.RestingNumericWheelEditsWithoutScrolling(property));
}
