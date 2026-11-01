using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCases.NativeCards;

// Source proposal. Retain the complete ordered profile before transferring ownership.
[TestFixture]
[NonParallelizable]
[Order(683)]
[Property("FishAudioExecutionProfile", "native-cards")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class VoiceDescriptionCases
{
    public static IEnumerable<TestCaseData> ConstructionRows()
    {
        yield return NativeTestCaseSource.Row(
            "cards.description/construction",
            683, "native-cards/683-cards.description-construction");
    }

    [TestCaseSource(nameof(ConstructionRows))]
    [Order(683)]
    public Task Construction() =>
        VoiceDescriptionSuite.ObserveConstructionAsync();

    public static IEnumerable<TestCaseData> RuntimeDescriptionSequenceRows()
    {
        yield return NativeTestCaseSource.Row(
            "cards.description/light/dpi-1-25/width-400/search",
            684, "native-cards/684-cards.description-light-dpi-1-25-width-400-search", 400d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.description/light/dpi-1-25/width-400/by-id",
            685, "native-cards/685-cards.description-light-dpi-1-25-width-400-by-id", 400d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.description/light/dpi-1-25/width-400/favorites",
            686, "native-cards/686-cards.description-light-dpi-1-25-width-400-favorites", 400d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.description/light/dpi-1-25/width-424/search",
            687, "native-cards/687-cards.description-light-dpi-1-25-width-424-search", 424d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.description/light/dpi-1-25/width-424/by-id",
            688, "native-cards/688-cards.description-light-dpi-1-25-width-424-by-id", 424d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.description/light/dpi-1-25/width-424/favorites",
            689, "native-cards/689-cards.description-light-dpi-1-25-width-424-favorites", 424d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.description/light/dpi-1-25/width-480/search",
            690, "native-cards/690-cards.description-light-dpi-1-25-width-480-search", 480d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.description/light/dpi-1-25/width-480/by-id",
            691, "native-cards/691-cards.description-light-dpi-1-25-width-480-by-id", 480d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.description/light/dpi-1-25/width-480/favorites",
            692, "native-cards/692-cards.description-light-dpi-1-25-width-480-favorites", 480d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.description/light/dpi-1-25/width-640/search",
            693, "native-cards/693-cards.description-light-dpi-1-25-width-640-search", 640d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.description/light/dpi-1-25/width-640/by-id",
            694, "native-cards/694-cards.description-light-dpi-1-25-width-640-by-id", 640d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.description/light/dpi-1-25/width-640/favorites",
            695, "native-cards/695-cards.description-light-dpi-1-25-width-640-favorites", 640d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.description/dark/dpi-1-25/width-400/search",
            696, "native-cards/696-cards.description-dark-dpi-1-25-width-400-search", 400d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.description/dark/dpi-1-25/width-400/by-id",
            697, "native-cards/697-cards.description-dark-dpi-1-25-width-400-by-id", 400d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.description/dark/dpi-1-25/width-400/favorites",
            698, "native-cards/698-cards.description-dark-dpi-1-25-width-400-favorites", 400d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.description/dark/dpi-1-25/width-424/search",
            699, "native-cards/699-cards.description-dark-dpi-1-25-width-424-search", 424d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.description/dark/dpi-1-25/width-424/by-id",
            700, "native-cards/700-cards.description-dark-dpi-1-25-width-424-by-id", 424d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.description/dark/dpi-1-25/width-424/favorites",
            701, "native-cards/701-cards.description-dark-dpi-1-25-width-424-favorites", 424d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.description/dark/dpi-1-25/width-480/search",
            702, "native-cards/702-cards.description-dark-dpi-1-25-width-480-search", 480d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.description/dark/dpi-1-25/width-480/by-id",
            703, "native-cards/703-cards.description-dark-dpi-1-25-width-480-by-id", 480d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.description/dark/dpi-1-25/width-480/favorites",
            704, "native-cards/704-cards.description-dark-dpi-1-25-width-480-favorites", 480d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.description/dark/dpi-1-25/width-640/search",
            705, "native-cards/705-cards.description-dark-dpi-1-25-width-640-search", 640d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.description/dark/dpi-1-25/width-640/by-id",
            706, "native-cards/706-cards.description-dark-dpi-1-25-width-640-by-id", 640d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.description/dark/dpi-1-25/width-640/favorites",
            707, "native-cards/707-cards.description-dark-dpi-1-25-width-640-favorites", 640d, true, VoiceBrowseMode.Favorites);
    }

    [TestCaseSource(nameof(RuntimeDescriptionSequenceRows))]
    [Order(684)]
    public Task RuntimeDescriptionSequence(double width, bool dark, VoiceBrowseMode mode) =>
        VoiceDescriptionSuite.ObserveContextAsync(width, dark, mode);

}
