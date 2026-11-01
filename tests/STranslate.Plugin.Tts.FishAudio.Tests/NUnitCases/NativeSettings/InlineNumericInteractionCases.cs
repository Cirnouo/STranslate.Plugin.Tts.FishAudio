using NUnit.Framework;

namespace NUnitCases.NativeSettings;

// Source candidate only. The complete profile retains its original cold-first-use order.
[TestFixture]
[NonParallelizable]
[Order(31)]
[Property("FishAudioExecutionProfile", "native-settings")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class InlineNumericInteractionCases
{
    public static IEnumerable<TestCaseData> InlineNumericRows()
    {
        yield return NativeTestCaseSource.Row(
            "numeric.interaction/compiled/en/culture-en-us",
            31, "native-settings/031-compiled-en-culture-en-us", "en", "en-US");
        yield return NativeTestCaseSource.Row(
            "numeric.interaction/compiled/zh-cn/culture-zh-cn",
            32, "native-settings/032-compiled-zh-cn-culture-zh-cn", "zh-cn", "zh-CN");
        yield return NativeTestCaseSource.Row(
            "numeric.interaction/compiled/zh-tw/culture-zh-tw",
            33, "native-settings/033-compiled-zh-tw-culture-zh-tw", "zh-tw", "zh-TW");
        yield return NativeTestCaseSource.Row(
            "numeric.interaction/compiled/ja/culture-ja-jp",
            34, "native-settings/034-compiled-ja-culture-ja-jp", "ja", "ja-JP");
        yield return NativeTestCaseSource.Row(
            "numeric.interaction/compiled/ko/culture-ko-kr",
            35, "native-settings/035-compiled-ko-culture-ko-kr", "ko", "ko-KR");
        yield return NativeTestCaseSource.Row(
            "numeric.interaction/compiled/ru/culture-ru-ru",
            36, "native-settings/036-compiled-ru-culture-ru-ru", "ru", "ru-RU");
        yield return NativeTestCaseSource.Row(
            "numeric.interaction/compiled/tr/culture-tr-tr",
            37, "native-settings/037-compiled-tr-culture-tr-tr", "tr", "tr-TR");
        yield return NativeTestCaseSource.Row(
            "numeric.interaction/compiled/en/culture-de-de",
            38, "native-settings/038-compiled-en-culture-de-de", "en", "de-DE");
    }

    [TestCaseSource(nameof(InlineNumericRows))]
    [Order(31)]
    public Task InlineNumericInteractions(string language, string culture)
        => StaTestHost.RunOnStaThreadAsync(() => InlineNumericEditorSuite.RunForCulture(
            SettingsLayoutSuite.CreateThemedView, SettingsLayoutSuite.Layout, language, culture));
}
