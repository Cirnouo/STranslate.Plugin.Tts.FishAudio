using NUnit.Framework;

namespace NUnitCases.NativeSettings;

// Source candidate only. The complete profile retains its original cold-first-use order.
[TestFixture]
[NonParallelizable]
[Order(20)]
[Property("FishAudioExecutionProfile", "native-settings")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CompiledSettingsCases
{
    public static IEnumerable<TestCaseData> PcmHidesWholeBitrateCardAndMp3RestoresItRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.layout/pcm-bitrate-card-restoration",
            20, "native-settings/020-pcm-bitrate-card-restoration");
    }

    [TestCaseSource(nameof(PcmHidesWholeBitrateCardAndMp3RestoresItRows))]
    [Order(20)]
    public Task PcmHidesWholeBitrateCardAndMp3RestoresIt()
        => SettingsLayoutSuite.ObserveCompiledScenarioAsync(SettingsLayoutSuite.PcmHidesWholeBitrateCardAndMp3RestoresIt);

    public static IEnumerable<TestCaseData> ReadingNetworkSettingsAreAbsentRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.layout/reading-network-settings-absent",
            21, "native-settings/021-reading-network-settings-absent");
    }

    [TestCaseSource(nameof(ReadingNetworkSettingsAreAbsentRows))]
    [Order(21)]
    public Task ReadingNetworkSettingsAreAbsent()
        => SettingsLayoutSuite.ObserveCompiledScenarioAsync(SettingsLayoutSuite.ReadingNetworkSettingsAreAbsent);

    public static IEnumerable<TestCaseData> FavoriteVoicesTabMatchesPeerTabsRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.layout/favorite-tabs-match-peers",
            22, "native-settings/022-favorite-tabs-match-peers");
    }

    [TestCaseSource(nameof(FavoriteVoicesTabMatchesPeerTabsRows))]
    [Order(22)]
    public Task FavoriteVoicesTabMatchesPeerTabs()
        => SettingsLayoutSuite.ObserveCompiledScenarioAsync(SettingsLayoutSuite.FavoriteVoicesTabMatchesPeerTabs);

    public static IEnumerable<TestCaseData> SelectedVoiceHeartSharesFavoriteStateRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.layout/selected-heart-shares-state",
            23, "native-settings/023-selected-heart-shares-state");
    }

    [TestCaseSource(nameof(SelectedVoiceHeartSharesFavoriteStateRows))]
    [Order(23)]
    public Task SelectedVoiceHeartSharesFavoriteState()
        => SettingsLayoutSuite.ObserveCompiledScenarioAsync(SettingsLayoutSuite.SelectedVoiceHeartSharesFavoriteState);

    public static IEnumerable<TestCaseData> PasteButtonsReserveInputSpaceRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.layout/paste-buttons-reserve-input-space",
            24, "native-settings/024-paste-buttons-reserve-input-space");
    }

    [TestCaseSource(nameof(PasteButtonsReserveInputSpaceRows))]
    [Order(24)]
    public Task PasteButtonsReserveInputSpace()
        => SettingsLayoutSuite.ObserveCompiledScenarioAsync(SettingsLayoutSuite.PasteButtonsReserveInputSpace);

    public static IEnumerable<TestCaseData> SelectionAndPagingActionsUseBareIconsRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.layout/selection-and-paging-bare-icons",
            25, "native-settings/025-selection-and-paging-bare-icons");
    }

    [TestCaseSource(nameof(SelectionAndPagingActionsUseBareIconsRows))]
    [Order(25)]
    public Task SelectionAndPagingActionsUseBareIcons()
        => SettingsLayoutSuite.ObserveCompiledScenarioAsync(SettingsLayoutSuite.SelectionAndPagingActionsUseBareIcons);

    public static IEnumerable<TestCaseData> LoudnessRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.layout/loudness/model-disabled",
            26, "native-settings/026-loudness-model-disabled", false);
        yield return NativeTestCaseSource.Row(
            "settings.layout/loudness/model-enabled",
            27, "native-settings/027-loudness-model-enabled", true);
    }

    [TestCaseSource(nameof(LoudnessRows))]
    [Order(26)]
    public Task LoudnessCardFollowsModelCapability(bool stored)
        => SettingsLayoutSuite.ObserveCompiledScenarioAsync(() => SettingsLayoutSuite.LoudnessCardFollowsModelCapability(stored));

    public static IEnumerable<TestCaseData> TopPMinimumKeepsSliderTracksAlignedRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.layout/top-p-minimum-slider-track-alignment",
            28, "native-settings/028-top-p-minimum-slider-track-alignment");
    }

    [TestCaseSource(nameof(TopPMinimumKeepsSliderTracksAlignedRows))]
    [Order(28)]
    public Task TopPMinimumKeepsSliderTracksAligned()
        => SettingsLayoutSuite.ObserveCompiledScenarioAsync(SettingsLayoutSuite.TopPMinimumKeepsSliderTracksAligned);

    public static IEnumerable<TestCaseData> ResultTogglesKeepIndependentViewStateRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.layout/result-toggle-independent-state",
            29, "native-settings/029-result-toggle-independent-state");
    }

    [TestCaseSource(nameof(ResultTogglesKeepIndependentViewStateRows))]
    [Order(29)]
    public Task ResultTogglesKeepIndependentViewState()
        => SettingsLayoutSuite.ObserveCompiledScenarioAsync(SettingsLayoutSuite.ResultTogglesKeepIndependentViewState);

    public static IEnumerable<TestCaseData> SharedVoiceCardReflectsMetadataStateRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.layout/shared-voice-card-metadata-state",
            30, "native-settings/030-shared-voice-card-metadata-state");
    }

    [TestCaseSource(nameof(SharedVoiceCardReflectsMetadataStateRows))]
    [Order(30)]
    public Task SharedVoiceCardReflectsMetadataState()
        => SettingsLayoutSuite.ObserveCompiledScenarioAsync(SettingsLayoutSuite.SharedVoiceCardReflectsMetadataState);
}
