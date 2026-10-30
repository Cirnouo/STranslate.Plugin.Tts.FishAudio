using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(12)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SelectedTabCases
{
    public static IEnumerable<TestCaseData> SelectedTabsKeepAccentThroughNativeStatesRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.settings/native/tabs/en/light",
            12, "native-input/012-input.settings-native-tabs-en-light", "en", false);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/tabs/en/dark",
            13, "native-input/013-input.settings-native-tabs-en-dark", "en", true);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/tabs/zh-cn/light",
            14, "native-input/014-input.settings-native-tabs-zh-cn-light", "zh-cn", false);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/tabs/zh-cn/dark",
            15, "native-input/015-input.settings-native-tabs-zh-cn-dark", "zh-cn", true);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/tabs/zh-tw/light",
            16, "native-input/016-input.settings-native-tabs-zh-tw-light", "zh-tw", false);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/tabs/zh-tw/dark",
            17, "native-input/017-input.settings-native-tabs-zh-tw-dark", "zh-tw", true);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/tabs/ja/light",
            18, "native-input/018-input.settings-native-tabs-ja-light", "ja", false);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/tabs/ja/dark",
            19, "native-input/019-input.settings-native-tabs-ja-dark", "ja", true);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/tabs/ko/light",
            20, "native-input/020-input.settings-native-tabs-ko-light", "ko", false);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/tabs/ko/dark",
            21, "native-input/021-input.settings-native-tabs-ko-dark", "ko", true);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/tabs/ru/light",
            22, "native-input/022-input.settings-native-tabs-ru-light", "ru", false);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/tabs/ru/dark",
            23, "native-input/023-input.settings-native-tabs-ru-dark", "ru", true);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/tabs/tr/light",
            24, "native-input/024-input.settings-native-tabs-tr-light", "tr", false);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/tabs/tr/dark",
            25, "native-input/025-input.settings-native-tabs-tr-dark", "tr", true);
    }

    [TestCaseSource(nameof(SelectedTabsKeepAccentThroughNativeStatesRows))]
    [Order(12)]
    public Task SelectedTabsKeepAccentThroughNativeStates(string language, bool dark)
        => StaTestHost.RunOnStaThreadAsync(() => SettingsInputInteractionSuite.SelectedTabsKeepAccentThroughNativeStates(language, dark));
}
