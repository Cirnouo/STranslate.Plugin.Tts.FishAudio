using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(0)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class TrackPointCases
{
    public static IEnumerable<TestCaseData> SliderTrackClickMovesToPointRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.settings/native/track-point/speed",
            0, "native-input/000-input.settings-native-track-point-speed", "Speed", 0.5d, 2d, 1.67d);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/track-point/volume",
            1, "native-input/001-input.settings-native-track-point-volume", "Volume", -10d, 10d, -3.21d);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/track-point/temperature",
            2, "native-input/002-input.settings-native-track-point-temperature", "Temperature", 0d, 1d, 0.83d);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/track-point/topp",
            3, "native-input/003-input.settings-native-track-point-topp", "TopP", 0.05d, 1d, 0.82d);
    }

    [TestCaseSource(nameof(SliderTrackClickMovesToPointRows))]
    [Order(0)]
    public Task SliderTrackClickMovesToPoint(string property, double minimum, double maximum, double requested)
        => StaTestHost.RunOnStaThreadAsync(() => SettingsInputInteractionSuite.SliderTrackClickMovesToPoint((property, minimum, maximum, requested)));
}
