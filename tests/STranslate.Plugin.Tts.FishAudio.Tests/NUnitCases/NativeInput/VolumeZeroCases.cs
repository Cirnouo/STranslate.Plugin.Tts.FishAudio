using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(4)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class VolumeZeroCases
{
    public static IEnumerable<TestCaseData> VolumeZeroIsCanonicalAcrossInputRoutesRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.settings/native/volume-zero/en-us",
            4, "native-input/004-input.settings-native-volume-zero-en-us", "en-US");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/volume-zero/de-de",
            5, "native-input/005-input.settings-native-volume-zero-de-de", "de-DE");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/volume-zero/ru-ru",
            6, "native-input/006-input.settings-native-volume-zero-ru-ru", "ru-RU");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/volume-zero/tr-tr",
            7, "native-input/007-input.settings-native-volume-zero-tr-tr", "tr-TR");
    }

    [TestCaseSource(nameof(VolumeZeroIsCanonicalAcrossInputRoutesRows))]
    [Order(4)]
    public Task VolumeZeroIsCanonicalAcrossInputRoutes(string culture)
        => StaTestHost.RunOnStaThreadAsync(() => SettingsInputInteractionSuite.VolumeZeroIsCanonicalAcrossInputRoutes(culture));
}
