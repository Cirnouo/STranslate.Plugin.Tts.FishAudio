using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(40)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class NativeValueToolTipCases
{
    public static IEnumerable<TestCaseData> SliderWheelUsesNativeValueToolTipAsyncRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.settings/native/native-tooltip/en-us/speed",
            40, "native-input/040-input.settings-native-native-tooltip-en-us-speed", "en-US", "Speed");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/native-tooltip/en-us/volume",
            41, "native-input/041-input.settings-native-native-tooltip-en-us-volume", "en-US", "Volume");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/native-tooltip/en-us/temperature",
            42, "native-input/042-input.settings-native-native-tooltip-en-us-temperature", "en-US", "Temperature");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/native-tooltip/en-us/topp",
            43, "native-input/043-input.settings-native-native-tooltip-en-us-topp", "en-US", "TopP");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/native-tooltip/de-de/speed",
            44, "native-input/044-input.settings-native-native-tooltip-de-de-speed", "de-DE", "Speed");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/native-tooltip/de-de/volume",
            45, "native-input/045-input.settings-native-native-tooltip-de-de-volume", "de-DE", "Volume");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/native-tooltip/de-de/temperature",
            46, "native-input/046-input.settings-native-native-tooltip-de-de-temperature", "de-DE", "Temperature");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/native-tooltip/de-de/topp",
            47, "native-input/047-input.settings-native-native-tooltip-de-de-topp", "de-DE", "TopP");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/native-tooltip/ru-ru/speed",
            48, "native-input/048-input.settings-native-native-tooltip-ru-ru-speed", "ru-RU", "Speed");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/native-tooltip/ru-ru/volume",
            49, "native-input/049-input.settings-native-native-tooltip-ru-ru-volume", "ru-RU", "Volume");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/native-tooltip/ru-ru/temperature",
            50, "native-input/050-input.settings-native-native-tooltip-ru-ru-temperature", "ru-RU", "Temperature");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/native-tooltip/ru-ru/topp",
            51, "native-input/051-input.settings-native-native-tooltip-ru-ru-topp", "ru-RU", "TopP");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/native-tooltip/tr-tr/speed",
            52, "native-input/052-input.settings-native-native-tooltip-tr-tr-speed", "tr-TR", "Speed");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/native-tooltip/tr-tr/volume",
            53, "native-input/053-input.settings-native-native-tooltip-tr-tr-volume", "tr-TR", "Volume");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/native-tooltip/tr-tr/temperature",
            54, "native-input/054-input.settings-native-native-tooltip-tr-tr-temperature", "tr-TR", "Temperature");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/native-tooltip/tr-tr/topp",
            55, "native-input/055-input.settings-native-native-tooltip-tr-tr-topp", "tr-TR", "TopP");
    }

    [TestCaseSource(nameof(SliderWheelUsesNativeValueToolTipAsyncRows))]
    [Order(40)]
    public Task SliderWheelUsesNativeValueToolTipAsync(string culture, string field)
        => SettingsInputInteractionSuite.ObserveNativeToolTipActionAsync($"{culture}/{field}", stage => SettingsInputInteractionSuite.SliderWheelUsesNativeValueToolTipAsync(culture, field, stage));
}
