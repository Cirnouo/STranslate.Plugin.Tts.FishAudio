using NUnit.Framework;

namespace NUnitCases.NativeSettings;

// Source candidate only. The complete profile retains its original cold-first-use order.
[TestFixture]
[NonParallelizable]
[Order(39)]
[Property("FishAudioExecutionProfile", "native-settings")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SettingsControlAlignmentCases
{
    public static IEnumerable<TestCaseData> BrowseTabPositionsRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.alignment/browse-tab-positions/en",
            39, "native-settings/039-browse-tab-positions-en", "en");
        yield return NativeTestCaseSource.Row(
            "settings.alignment/browse-tab-positions/zh-cn",
            40, "native-settings/040-browse-tab-positions-zh-cn", "zh-cn");
        yield return NativeTestCaseSource.Row(
            "settings.alignment/browse-tab-positions/zh-tw",
            41, "native-settings/041-browse-tab-positions-zh-tw", "zh-tw");
        yield return NativeTestCaseSource.Row(
            "settings.alignment/browse-tab-positions/ja",
            42, "native-settings/042-browse-tab-positions-ja", "ja");
        yield return NativeTestCaseSource.Row(
            "settings.alignment/browse-tab-positions/ko",
            43, "native-settings/043-browse-tab-positions-ko", "ko");
        yield return NativeTestCaseSource.Row(
            "settings.alignment/browse-tab-positions/ru",
            44, "native-settings/044-browse-tab-positions-ru", "ru");
        yield return NativeTestCaseSource.Row(
            "settings.alignment/browse-tab-positions/tr",
            45, "native-settings/045-browse-tab-positions-tr", "tr");
    }

    [TestCaseSource(nameof(BrowseTabPositionsRows))]
    [Order(39)]
    public Task BrowseTabPositionsStayFixed(string language)
        => StaTestHost.RunOnStaThreadAsync(() => SettingsControlAlignmentSuite.BrowseTabPositionsStayFixed(language));

    public static IEnumerable<TestCaseData> SliderStartsAtNativeContentAnchorRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.alignment/slider-native-content-anchor",
            46, "native-settings/046-slider-native-content-anchor");
    }

    [TestCaseSource(nameof(SliderStartsAtNativeContentAnchorRows))]
    [Order(46)]
    public Task SliderStartsAtNativeContentAnchor()
        => StaTestHost.RunOnStaThreadAsync(SettingsControlAlignmentSuite.SliderStartsAtNativeContentAnchor);

    public static IEnumerable<TestCaseData> EmptyBrowseRowsKeepModelPositionRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.alignment/empty-browse-model-position",
            47, "native-settings/047-empty-browse-model-position");
    }

    [TestCaseSource(nameof(EmptyBrowseRowsKeepModelPositionRows))]
    [Order(47)]
    public Task EmptyBrowseRowsKeepModelPosition()
        => StaTestHost.RunOnStaThreadAsync(SettingsControlAlignmentSuite.EmptyBrowseRowsKeepModelPosition);

    public static IEnumerable<TestCaseData> VisibleCardIconsRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.alignment/visible-card-icon-center/width-400",
            48, "native-settings/048-visible-card-icon-center-width-400", 400d);
        yield return NativeTestCaseSource.Row(
            "settings.alignment/visible-card-icon-center/width-600",
            49, "native-settings/049-visible-card-icon-center-width-600", 600d);
    }

    [TestCaseSource(nameof(VisibleCardIconsRows))]
    [Order(48)]
    public Task VisibleCardIconsShareCenter(double width)
        => StaTestHost.RunOnStaThreadAsync(() => SettingsControlAlignmentSuite.VisibleCardIconsShareCenter(width));

    public static IEnumerable<TestCaseData> InputViewportsKeepTheirWidthRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.alignment/input-viewport-width",
            50, "native-settings/050-input-viewport-width");
    }

    [TestCaseSource(nameof(InputViewportsKeepTheirWidthRows))]
    [Order(50)]
    public Task InputViewportsKeepTheirWidth()
        => StaTestHost.RunOnStaThreadAsync(SettingsControlAlignmentSuite.InputViewportsKeepTheirWidth);

    public static IEnumerable<TestCaseData> VoiceIdCompositionAndPasteRejectInvalidTextRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.alignment/voice-id-composition-and-paste-validation",
            51, "native-settings/051-voice-id-composition-and-paste-validation");
    }

    [TestCaseSource(nameof(VoiceIdCompositionAndPasteRejectInvalidTextRows))]
    [Order(51)]
    public Task VoiceIdCompositionAndPasteRejectInvalidText()
        => StaTestHost.RunOnStaThreadAsync(SettingsControlAlignmentSuite.VoiceIdCompositionAndPasteRejectInvalidText);
}
