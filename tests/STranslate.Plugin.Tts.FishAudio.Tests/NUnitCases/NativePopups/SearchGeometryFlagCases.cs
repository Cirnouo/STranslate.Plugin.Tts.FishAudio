using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(102)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SearchGeometryFlagCases
{
    public static IEnumerable<TestCaseData> GeometryAndFlagsRetainBothWidthsAndEveryLiveLocaleRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/en/light/dpi-1",
            102, "native-popups/102-popup.search-adjustment-native-geometry-en-light-dpi-1", "en", false, 1d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/en/light/dpi-1-25",
            103, "native-popups/103-popup.search-adjustment-native-geometry-en-light-dpi-1-25", "en", false, 1.25d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/en/light/dpi-1-5",
            104, "native-popups/104-popup.search-adjustment-native-geometry-en-light-dpi-1-5", "en", false, 1.5d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/en/dark/dpi-1",
            105, "native-popups/105-popup.search-adjustment-native-geometry-en-dark-dpi-1", "en", true, 1d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/en/dark/dpi-1-25",
            106, "native-popups/106-popup.search-adjustment-native-geometry-en-dark-dpi-1-25", "en", true, 1.25d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/en/dark/dpi-1-5",
            107, "native-popups/107-popup.search-adjustment-native-geometry-en-dark-dpi-1-5", "en", true, 1.5d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/zh-cn/light/dpi-1",
            108, "native-popups/108-popup.search-adjustment-native-geometry-zh-cn-light-dpi-1", "zh-cn", false, 1d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/zh-cn/light/dpi-1-25",
            109, "native-popups/109-popup.search-adjustment-native-geometry-zh-cn-light-dpi-1-25", "zh-cn", false, 1.25d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/zh-cn/light/dpi-1-5",
            110, "native-popups/110-popup.search-adjustment-native-geometry-zh-cn-light-dpi-1-5", "zh-cn", false, 1.5d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/zh-cn/dark/dpi-1",
            111, "native-popups/111-popup.search-adjustment-native-geometry-zh-cn-dark-dpi-1", "zh-cn", true, 1d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/zh-cn/dark/dpi-1-25",
            112, "native-popups/112-popup.search-adjustment-native-geometry-zh-cn-dark-dpi-1-25", "zh-cn", true, 1.25d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/zh-cn/dark/dpi-1-5",
            113, "native-popups/113-popup.search-adjustment-native-geometry-zh-cn-dark-dpi-1-5", "zh-cn", true, 1.5d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/zh-tw/light/dpi-1",
            114, "native-popups/114-popup.search-adjustment-native-geometry-zh-tw-light-dpi-1", "zh-tw", false, 1d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/zh-tw/light/dpi-1-25",
            115, "native-popups/115-popup.search-adjustment-native-geometry-zh-tw-light-dpi-1-25", "zh-tw", false, 1.25d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/zh-tw/light/dpi-1-5",
            116, "native-popups/116-popup.search-adjustment-native-geometry-zh-tw-light-dpi-1-5", "zh-tw", false, 1.5d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/zh-tw/dark/dpi-1",
            117, "native-popups/117-popup.search-adjustment-native-geometry-zh-tw-dark-dpi-1", "zh-tw", true, 1d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/zh-tw/dark/dpi-1-25",
            118, "native-popups/118-popup.search-adjustment-native-geometry-zh-tw-dark-dpi-1-25", "zh-tw", true, 1.25d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/zh-tw/dark/dpi-1-5",
            119, "native-popups/119-popup.search-adjustment-native-geometry-zh-tw-dark-dpi-1-5", "zh-tw", true, 1.5d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/ja/light/dpi-1",
            120, "native-popups/120-popup.search-adjustment-native-geometry-ja-light-dpi-1", "ja", false, 1d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/ja/light/dpi-1-25",
            121, "native-popups/121-popup.search-adjustment-native-geometry-ja-light-dpi-1-25", "ja", false, 1.25d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/ja/light/dpi-1-5",
            122, "native-popups/122-popup.search-adjustment-native-geometry-ja-light-dpi-1-5", "ja", false, 1.5d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/ja/dark/dpi-1",
            123, "native-popups/123-popup.search-adjustment-native-geometry-ja-dark-dpi-1", "ja", true, 1d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/ja/dark/dpi-1-25",
            124, "native-popups/124-popup.search-adjustment-native-geometry-ja-dark-dpi-1-25", "ja", true, 1.25d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/ja/dark/dpi-1-5",
            125, "native-popups/125-popup.search-adjustment-native-geometry-ja-dark-dpi-1-5", "ja", true, 1.5d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/ko/light/dpi-1",
            126, "native-popups/126-popup.search-adjustment-native-geometry-ko-light-dpi-1", "ko", false, 1d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/ko/light/dpi-1-25",
            127, "native-popups/127-popup.search-adjustment-native-geometry-ko-light-dpi-1-25", "ko", false, 1.25d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/ko/light/dpi-1-5",
            128, "native-popups/128-popup.search-adjustment-native-geometry-ko-light-dpi-1-5", "ko", false, 1.5d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/ko/dark/dpi-1",
            129, "native-popups/129-popup.search-adjustment-native-geometry-ko-dark-dpi-1", "ko", true, 1d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/ko/dark/dpi-1-25",
            130, "native-popups/130-popup.search-adjustment-native-geometry-ko-dark-dpi-1-25", "ko", true, 1.25d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/ko/dark/dpi-1-5",
            131, "native-popups/131-popup.search-adjustment-native-geometry-ko-dark-dpi-1-5", "ko", true, 1.5d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/ru/light/dpi-1",
            132, "native-popups/132-popup.search-adjustment-native-geometry-ru-light-dpi-1", "ru", false, 1d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/ru/light/dpi-1-25",
            133, "native-popups/133-popup.search-adjustment-native-geometry-ru-light-dpi-1-25", "ru", false, 1.25d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/ru/light/dpi-1-5",
            134, "native-popups/134-popup.search-adjustment-native-geometry-ru-light-dpi-1-5", "ru", false, 1.5d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/ru/dark/dpi-1",
            135, "native-popups/135-popup.search-adjustment-native-geometry-ru-dark-dpi-1", "ru", true, 1d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/ru/dark/dpi-1-25",
            136, "native-popups/136-popup.search-adjustment-native-geometry-ru-dark-dpi-1-25", "ru", true, 1.25d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/ru/dark/dpi-1-5",
            137, "native-popups/137-popup.search-adjustment-native-geometry-ru-dark-dpi-1-5", "ru", true, 1.5d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/tr/light/dpi-1",
            138, "native-popups/138-popup.search-adjustment-native-geometry-tr-light-dpi-1", "tr", false, 1d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/tr/light/dpi-1-25",
            139, "native-popups/139-popup.search-adjustment-native-geometry-tr-light-dpi-1-25", "tr", false, 1.25d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/tr/light/dpi-1-5",
            140, "native-popups/140-popup.search-adjustment-native-geometry-tr-light-dpi-1-5", "tr", false, 1.5d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/tr/dark/dpi-1",
            141, "native-popups/141-popup.search-adjustment-native-geometry-tr-dark-dpi-1", "tr", true, 1d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/tr/dark/dpi-1-25",
            142, "native-popups/142-popup.search-adjustment-native-geometry-tr-dark-dpi-1-25", "tr", true, 1.25d);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/geometry/tr/dark/dpi-1-5",
            143, "native-popups/143-popup.search-adjustment-native-geometry-tr-dark-dpi-1-5", "tr", true, 1.5d);
    }

    [TestCaseSource(nameof(GeometryAndFlagsRetainBothWidthsAndEveryLiveLocaleRows))]
    [Order(102)]
    public Task GeometryAndFlagsRetainBothWidthsAndEveryLiveLocale(string locale, bool dark, double dpi)
        => SearchAdjustmentControlSuite.ObserveGeometryAsync(locale, dark, dpi);
}
