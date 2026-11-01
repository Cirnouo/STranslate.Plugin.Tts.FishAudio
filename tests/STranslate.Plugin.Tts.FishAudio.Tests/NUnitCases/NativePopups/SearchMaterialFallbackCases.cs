using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.View;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(94)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SearchMaterialFallbackCases
{
    public static IEnumerable<TestCaseData> FallbackRetainsTheCompleteFirstSourceThemeAndReopenChainRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/material-fallback/unsupported/mica",
            94, "native-popups/094-popup.search-adjustment-native-material-fallback-unsupported-mica", false, 0, 0, 2);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/material-fallback/unsupported/acrylic",
            95, "native-popups/095-popup.search-adjustment-native-material-fallback-unsupported-acrylic", false, 0, 0, 3);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/material-fallback/unsupported/readback-failure",
            96, "native-popups/096-popup.search-adjustment-native-material-fallback-unsupported-readback-failure", false, 0, -2147467259, 2);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/material-fallback/unsupported/frame-failure",
            97, "native-popups/097-popup.search-adjustment-native-material-fallback-unsupported-frame-failure", false, -2147467259, 0, 2);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/material-fallback/supported/mica",
            98, "native-popups/098-popup.search-adjustment-native-material-fallback-supported-mica", true, 0, 0, 2);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/material-fallback/supported/acrylic",
            99, "native-popups/099-popup.search-adjustment-native-material-fallback-supported-acrylic", true, 0, 0, 3);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/material-fallback/supported/readback-failure",
            100, "native-popups/100-popup.search-adjustment-native-material-fallback-supported-readback-failure", true, 0, -2147467259, 2);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/material-fallback/supported/frame-failure",
            101, "native-popups/101-popup.search-adjustment-native-material-fallback-supported-frame-failure", true, -2147467259, 0, 2);
    }

    [TestCaseSource(nameof(FallbackRetainsTheCompleteFirstSourceThemeAndReopenChainRows))]
    [Order(94)]
    public Task FallbackRetainsTheCompleteFirstSourceThemeAndReopenChain(bool supported, int frameHResult, int readbackHResult, int backdropType)
        => StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => SearchAdjustmentControlSuite.PopupMaterialFallbackAndSourceLifetimes(supported, new PopupBackdropResult(frameHResult, readbackHResult, backdropType))));
}
