using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(8)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class FoldFocusCases
{
    public static IEnumerable<TestCaseData> FoldControlsSeparateFocusFromPointerStateRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.settings/native/fold-focus/light/search-results",
            8, "native-input/008-input.settings-native-fold-focus-light-search-results", false, "VoiceSearchResultsToggle");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/fold-focus/light/favorites",
            9, "native-input/009-input.settings-native-fold-focus-light-favorites", false, "FavoriteVoicesToggle");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/fold-focus/dark/search-results",
            10, "native-input/010-input.settings-native-fold-focus-dark-search-results", true, "VoiceSearchResultsToggle");
        yield return NativeTestCaseSource.Row(
            "input.settings/native/fold-focus/dark/favorites",
            11, "native-input/011-input.settings-native-fold-focus-dark-favorites", true, "FavoriteVoicesToggle");
    }

    [TestCaseSource(nameof(FoldControlsSeparateFocusFromPointerStateRows))]
    [Order(8)]
    public Task FoldControlsSeparateFocusFromPointerState(bool dark, string name)
        => StaTestHost.RunOnStaThreadAsync(() => SettingsInputInteractionSuite.FoldControlsSeparateFocusFromPointerState(dark, name));
}
