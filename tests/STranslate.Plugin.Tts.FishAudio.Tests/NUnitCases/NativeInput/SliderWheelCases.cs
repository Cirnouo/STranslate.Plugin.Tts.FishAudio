using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(36)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SliderWheelCases
{
    public static IEnumerable<TestCaseData> SliderWheelEditsWithoutScrollingRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.settings/native/slider-wheel/speed",
            36, "native-input/036-input.settings-native-slider-wheel-speed", "Speed", 1d, 1.01d, 0.99d, 0.5d, 2d, 1.99d, 0.51d, 0.01d);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/slider-wheel/volume",
            37, "native-input/037-input.settings-native-slider-wheel-volume", "Volume", 0d, 0.01d, -0.01d, -10d, 10d, 9.99d, -9.99d, 0.01d);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/slider-wheel/temperature",
            38, "native-input/038-input.settings-native-slider-wheel-temperature", "Temperature", 0.7d, 0.71d, 0.69d, 0d, 1d, 0.99d, 0.01d, 0.01d);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/slider-wheel/topp",
            39, "native-input/039-input.settings-native-slider-wheel-topp", "TopP", 0.7d, 0.71d, 0.69d, 0.05d, 1d, 0.99d, 0.06d, 0.01d);
    }

    [TestCaseSource(nameof(SliderWheelEditsWithoutScrollingRows))]
    [Order(36)]
    public Task SliderWheelEditsWithoutScrolling(string property, double initial, double increased, double decreased, double minimum, double maximum, double belowMaximum, double aboveMinimum, double tick)
        => StaTestHost.RunOnStaThreadAsync(() => SettingsInputInteractionSuite.SliderWheelEditsWithoutScrolling((property, initial, increased, decreased, minimum, maximum, belowMaximum, aboveMinimum, tick)));
}
