using NUnit.Framework;

namespace NUnitCases.NativeSettings;

// Source candidate only. The complete profile retains its original cold-first-use order.
[TestFixture]
[NonParallelizable]
[Order(78)]
[Property("FishAudioExecutionProfile", "native-settings")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CompactNumericCases
{
    public static IEnumerable<TestCaseData> CompactGeometryRows()
    {
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/en/culture-en-us/light/dpi-1",
            78, "native-settings/078-compact-en-culture-en-us-light-dpi-1", "en", "en-US", false, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/en/culture-en-us/light/dpi-1-25",
            79, "native-settings/079-compact-en-culture-en-us-light-dpi-1-25", "en", "en-US", false, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/en/culture-en-us/light/dpi-1-5",
            80, "native-settings/080-compact-en-culture-en-us-light-dpi-1-5", "en", "en-US", false, 1.5);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/en/culture-en-us/dark/dpi-1",
            81, "native-settings/081-compact-en-culture-en-us-dark-dpi-1", "en", "en-US", true, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/en/culture-en-us/dark/dpi-1-25",
            82, "native-settings/082-compact-en-culture-en-us-dark-dpi-1-25", "en", "en-US", true, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/en/culture-en-us/dark/dpi-1-5",
            83, "native-settings/083-compact-en-culture-en-us-dark-dpi-1-5", "en", "en-US", true, 1.5);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/zh-cn/culture-zh-cn/light/dpi-1",
            84, "native-settings/084-compact-zh-cn-culture-zh-cn-light-dpi-1", "zh-cn", "zh-CN", false, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/zh-cn/culture-zh-cn/light/dpi-1-25",
            85, "native-settings/085-compact-zh-cn-culture-zh-cn-light-dpi-1-25", "zh-cn", "zh-CN", false, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/zh-cn/culture-zh-cn/light/dpi-1-5",
            86, "native-settings/086-compact-zh-cn-culture-zh-cn-light-dpi-1-5", "zh-cn", "zh-CN", false, 1.5);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/zh-cn/culture-zh-cn/dark/dpi-1",
            87, "native-settings/087-compact-zh-cn-culture-zh-cn-dark-dpi-1", "zh-cn", "zh-CN", true, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/zh-cn/culture-zh-cn/dark/dpi-1-25",
            88, "native-settings/088-compact-zh-cn-culture-zh-cn-dark-dpi-1-25", "zh-cn", "zh-CN", true, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/zh-cn/culture-zh-cn/dark/dpi-1-5",
            89, "native-settings/089-compact-zh-cn-culture-zh-cn-dark-dpi-1-5", "zh-cn", "zh-CN", true, 1.5);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/zh-tw/culture-zh-tw/light/dpi-1",
            90, "native-settings/090-compact-zh-tw-culture-zh-tw-light-dpi-1", "zh-tw", "zh-TW", false, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/zh-tw/culture-zh-tw/light/dpi-1-25",
            91, "native-settings/091-compact-zh-tw-culture-zh-tw-light-dpi-1-25", "zh-tw", "zh-TW", false, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/zh-tw/culture-zh-tw/light/dpi-1-5",
            92, "native-settings/092-compact-zh-tw-culture-zh-tw-light-dpi-1-5", "zh-tw", "zh-TW", false, 1.5);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/zh-tw/culture-zh-tw/dark/dpi-1",
            93, "native-settings/093-compact-zh-tw-culture-zh-tw-dark-dpi-1", "zh-tw", "zh-TW", true, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/zh-tw/culture-zh-tw/dark/dpi-1-25",
            94, "native-settings/094-compact-zh-tw-culture-zh-tw-dark-dpi-1-25", "zh-tw", "zh-TW", true, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/zh-tw/culture-zh-tw/dark/dpi-1-5",
            95, "native-settings/095-compact-zh-tw-culture-zh-tw-dark-dpi-1-5", "zh-tw", "zh-TW", true, 1.5);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/ja/culture-ja-jp/light/dpi-1",
            96, "native-settings/096-compact-ja-culture-ja-jp-light-dpi-1", "ja", "ja-JP", false, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/ja/culture-ja-jp/light/dpi-1-25",
            97, "native-settings/097-compact-ja-culture-ja-jp-light-dpi-1-25", "ja", "ja-JP", false, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/ja/culture-ja-jp/light/dpi-1-5",
            98, "native-settings/098-compact-ja-culture-ja-jp-light-dpi-1-5", "ja", "ja-JP", false, 1.5);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/ja/culture-ja-jp/dark/dpi-1",
            99, "native-settings/099-compact-ja-culture-ja-jp-dark-dpi-1", "ja", "ja-JP", true, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/ja/culture-ja-jp/dark/dpi-1-25",
            100, "native-settings/100-compact-ja-culture-ja-jp-dark-dpi-1-25", "ja", "ja-JP", true, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/ja/culture-ja-jp/dark/dpi-1-5",
            101, "native-settings/101-compact-ja-culture-ja-jp-dark-dpi-1-5", "ja", "ja-JP", true, 1.5);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/ko/culture-ko-kr/light/dpi-1",
            102, "native-settings/102-compact-ko-culture-ko-kr-light-dpi-1", "ko", "ko-KR", false, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/ko/culture-ko-kr/light/dpi-1-25",
            103, "native-settings/103-compact-ko-culture-ko-kr-light-dpi-1-25", "ko", "ko-KR", false, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/ko/culture-ko-kr/light/dpi-1-5",
            104, "native-settings/104-compact-ko-culture-ko-kr-light-dpi-1-5", "ko", "ko-KR", false, 1.5);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/ko/culture-ko-kr/dark/dpi-1",
            105, "native-settings/105-compact-ko-culture-ko-kr-dark-dpi-1", "ko", "ko-KR", true, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/ko/culture-ko-kr/dark/dpi-1-25",
            106, "native-settings/106-compact-ko-culture-ko-kr-dark-dpi-1-25", "ko", "ko-KR", true, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/ko/culture-ko-kr/dark/dpi-1-5",
            107, "native-settings/107-compact-ko-culture-ko-kr-dark-dpi-1-5", "ko", "ko-KR", true, 1.5);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/ru/culture-ru-ru/light/dpi-1",
            108, "native-settings/108-compact-ru-culture-ru-ru-light-dpi-1", "ru", "ru-RU", false, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/ru/culture-ru-ru/light/dpi-1-25",
            109, "native-settings/109-compact-ru-culture-ru-ru-light-dpi-1-25", "ru", "ru-RU", false, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/ru/culture-ru-ru/light/dpi-1-5",
            110, "native-settings/110-compact-ru-culture-ru-ru-light-dpi-1-5", "ru", "ru-RU", false, 1.5);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/ru/culture-ru-ru/dark/dpi-1",
            111, "native-settings/111-compact-ru-culture-ru-ru-dark-dpi-1", "ru", "ru-RU", true, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/ru/culture-ru-ru/dark/dpi-1-25",
            112, "native-settings/112-compact-ru-culture-ru-ru-dark-dpi-1-25", "ru", "ru-RU", true, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/ru/culture-ru-ru/dark/dpi-1-5",
            113, "native-settings/113-compact-ru-culture-ru-ru-dark-dpi-1-5", "ru", "ru-RU", true, 1.5);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/tr/culture-tr-tr/light/dpi-1",
            114, "native-settings/114-compact-tr-culture-tr-tr-light-dpi-1", "tr", "tr-TR", false, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/tr/culture-tr-tr/light/dpi-1-25",
            115, "native-settings/115-compact-tr-culture-tr-tr-light-dpi-1-25", "tr", "tr-TR", false, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/tr/culture-tr-tr/light/dpi-1-5",
            116, "native-settings/116-compact-tr-culture-tr-tr-light-dpi-1-5", "tr", "tr-TR", false, 1.5);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/tr/culture-tr-tr/dark/dpi-1",
            117, "native-settings/117-compact-tr-culture-tr-tr-dark-dpi-1", "tr", "tr-TR", true, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/tr/culture-tr-tr/dark/dpi-1-25",
            118, "native-settings/118-compact-tr-culture-tr-tr-dark-dpi-1-25", "tr", "tr-TR", true, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/tr/culture-tr-tr/dark/dpi-1-5",
            119, "native-settings/119-compact-tr-culture-tr-tr-dark-dpi-1-5", "tr", "tr-TR", true, 1.5);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/en/culture-de-de/light/dpi-1",
            120, "native-settings/120-compact-en-culture-de-de-light-dpi-1", "en", "de-DE", false, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/en/culture-de-de/light/dpi-1-25",
            121, "native-settings/121-compact-en-culture-de-de-light-dpi-1-25", "en", "de-DE", false, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/en/culture-de-de/light/dpi-1-5",
            122, "native-settings/122-compact-en-culture-de-de-light-dpi-1-5", "en", "de-DE", false, 1.5);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/en/culture-de-de/dark/dpi-1",
            123, "native-settings/123-compact-en-culture-de-de-dark-dpi-1", "en", "de-DE", true, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/en/culture-de-de/dark/dpi-1-25",
            124, "native-settings/124-compact-en-culture-de-de-dark-dpi-1-25", "en", "de-DE", true, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.geometry/compact/en/culture-de-de/dark/dpi-1-5",
            125, "native-settings/125-compact-en-culture-de-de-dark-dpi-1-5", "en", "de-DE", true, 1.5);
    }

    [TestCaseSource(nameof(CompactGeometryRows))]
    [Order(78)]
    public Task CompactGeometryForCulture(string language, string culture, bool dark, double dpi)
        => StaTestHost.RunOnStaThreadAsync(() => InlineNumericEditorSuite.CompactGeometryForCulture(language, culture, dark, dpi));
}
