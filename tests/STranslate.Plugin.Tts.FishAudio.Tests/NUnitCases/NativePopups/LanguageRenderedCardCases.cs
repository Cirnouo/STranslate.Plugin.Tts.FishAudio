using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(12)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class LanguageRenderedCardCases
{
    public static IEnumerable<TestCaseData> SelectedAndActiveCardsRetainTheirCompleteRenderedOwnerRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1/width-320/search",
            12, "native-popups/012-popup.language-tags-cards-light-dpi-1-width-320-search", 320d, 1d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1/width-320/by-id",
            13, "native-popups/013-popup.language-tags-cards-light-dpi-1-width-320-by-id", 320d, 1d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1/width-320/favorites",
            14, "native-popups/014-popup.language-tags-cards-light-dpi-1-width-320-favorites", 320d, 1d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1/width-400/search",
            15, "native-popups/015-popup.language-tags-cards-light-dpi-1-width-400-search", 400d, 1d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1/width-400/by-id",
            16, "native-popups/016-popup.language-tags-cards-light-dpi-1-width-400-by-id", 400d, 1d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1/width-400/favorites",
            17, "native-popups/017-popup.language-tags-cards-light-dpi-1-width-400-favorites", 400d, 1d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1/width-480/search",
            18, "native-popups/018-popup.language-tags-cards-light-dpi-1-width-480-search", 480d, 1d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1/width-480/by-id",
            19, "native-popups/019-popup.language-tags-cards-light-dpi-1-width-480-by-id", 480d, 1d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1/width-480/favorites",
            20, "native-popups/020-popup.language-tags-cards-light-dpi-1-width-480-favorites", 480d, 1d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1/width-640/search",
            21, "native-popups/021-popup.language-tags-cards-light-dpi-1-width-640-search", 640d, 1d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1/width-640/by-id",
            22, "native-popups/022-popup.language-tags-cards-light-dpi-1-width-640-by-id", 640d, 1d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1/width-640/favorites",
            23, "native-popups/023-popup.language-tags-cards-light-dpi-1-width-640-favorites", 640d, 1d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-25/width-320/search",
            24, "native-popups/024-popup.language-tags-cards-light-dpi-1-25-width-320-search", 320d, 1.25d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-25/width-320/by-id",
            25, "native-popups/025-popup.language-tags-cards-light-dpi-1-25-width-320-by-id", 320d, 1.25d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-25/width-320/favorites",
            26, "native-popups/026-popup.language-tags-cards-light-dpi-1-25-width-320-favorites", 320d, 1.25d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-25/width-400/search",
            27, "native-popups/027-popup.language-tags-cards-light-dpi-1-25-width-400-search", 400d, 1.25d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-25/width-400/by-id",
            28, "native-popups/028-popup.language-tags-cards-light-dpi-1-25-width-400-by-id", 400d, 1.25d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-25/width-400/favorites",
            29, "native-popups/029-popup.language-tags-cards-light-dpi-1-25-width-400-favorites", 400d, 1.25d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-25/width-480/search",
            30, "native-popups/030-popup.language-tags-cards-light-dpi-1-25-width-480-search", 480d, 1.25d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-25/width-480/by-id",
            31, "native-popups/031-popup.language-tags-cards-light-dpi-1-25-width-480-by-id", 480d, 1.25d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-25/width-480/favorites",
            32, "native-popups/032-popup.language-tags-cards-light-dpi-1-25-width-480-favorites", 480d, 1.25d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-25/width-640/search",
            33, "native-popups/033-popup.language-tags-cards-light-dpi-1-25-width-640-search", 640d, 1.25d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-25/width-640/by-id",
            34, "native-popups/034-popup.language-tags-cards-light-dpi-1-25-width-640-by-id", 640d, 1.25d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-25/width-640/favorites",
            35, "native-popups/035-popup.language-tags-cards-light-dpi-1-25-width-640-favorites", 640d, 1.25d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-5/width-320/search",
            36, "native-popups/036-popup.language-tags-cards-light-dpi-1-5-width-320-search", 320d, 1.5d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-5/width-320/by-id",
            37, "native-popups/037-popup.language-tags-cards-light-dpi-1-5-width-320-by-id", 320d, 1.5d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-5/width-320/favorites",
            38, "native-popups/038-popup.language-tags-cards-light-dpi-1-5-width-320-favorites", 320d, 1.5d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-5/width-400/search",
            39, "native-popups/039-popup.language-tags-cards-light-dpi-1-5-width-400-search", 400d, 1.5d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-5/width-400/by-id",
            40, "native-popups/040-popup.language-tags-cards-light-dpi-1-5-width-400-by-id", 400d, 1.5d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-5/width-400/favorites",
            41, "native-popups/041-popup.language-tags-cards-light-dpi-1-5-width-400-favorites", 400d, 1.5d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-5/width-480/search",
            42, "native-popups/042-popup.language-tags-cards-light-dpi-1-5-width-480-search", 480d, 1.5d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-5/width-480/by-id",
            43, "native-popups/043-popup.language-tags-cards-light-dpi-1-5-width-480-by-id", 480d, 1.5d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-5/width-480/favorites",
            44, "native-popups/044-popup.language-tags-cards-light-dpi-1-5-width-480-favorites", 480d, 1.5d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-5/width-640/search",
            45, "native-popups/045-popup.language-tags-cards-light-dpi-1-5-width-640-search", 640d, 1.5d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-5/width-640/by-id",
            46, "native-popups/046-popup.language-tags-cards-light-dpi-1-5-width-640-by-id", 640d, 1.5d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/light/dpi-1-5/width-640/favorites",
            47, "native-popups/047-popup.language-tags-cards-light-dpi-1-5-width-640-favorites", 640d, 1.5d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1/width-320/search",
            48, "native-popups/048-popup.language-tags-cards-dark-dpi-1-width-320-search", 320d, 1d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1/width-320/by-id",
            49, "native-popups/049-popup.language-tags-cards-dark-dpi-1-width-320-by-id", 320d, 1d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1/width-320/favorites",
            50, "native-popups/050-popup.language-tags-cards-dark-dpi-1-width-320-favorites", 320d, 1d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1/width-400/search",
            51, "native-popups/051-popup.language-tags-cards-dark-dpi-1-width-400-search", 400d, 1d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1/width-400/by-id",
            52, "native-popups/052-popup.language-tags-cards-dark-dpi-1-width-400-by-id", 400d, 1d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1/width-400/favorites",
            53, "native-popups/053-popup.language-tags-cards-dark-dpi-1-width-400-favorites", 400d, 1d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1/width-480/search",
            54, "native-popups/054-popup.language-tags-cards-dark-dpi-1-width-480-search", 480d, 1d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1/width-480/by-id",
            55, "native-popups/055-popup.language-tags-cards-dark-dpi-1-width-480-by-id", 480d, 1d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1/width-480/favorites",
            56, "native-popups/056-popup.language-tags-cards-dark-dpi-1-width-480-favorites", 480d, 1d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1/width-640/search",
            57, "native-popups/057-popup.language-tags-cards-dark-dpi-1-width-640-search", 640d, 1d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1/width-640/by-id",
            58, "native-popups/058-popup.language-tags-cards-dark-dpi-1-width-640-by-id", 640d, 1d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1/width-640/favorites",
            59, "native-popups/059-popup.language-tags-cards-dark-dpi-1-width-640-favorites", 640d, 1d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-25/width-320/search",
            60, "native-popups/060-popup.language-tags-cards-dark-dpi-1-25-width-320-search", 320d, 1.25d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-25/width-320/by-id",
            61, "native-popups/061-popup.language-tags-cards-dark-dpi-1-25-width-320-by-id", 320d, 1.25d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-25/width-320/favorites",
            62, "native-popups/062-popup.language-tags-cards-dark-dpi-1-25-width-320-favorites", 320d, 1.25d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-25/width-400/search",
            63, "native-popups/063-popup.language-tags-cards-dark-dpi-1-25-width-400-search", 400d, 1.25d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-25/width-400/by-id",
            64, "native-popups/064-popup.language-tags-cards-dark-dpi-1-25-width-400-by-id", 400d, 1.25d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-25/width-400/favorites",
            65, "native-popups/065-popup.language-tags-cards-dark-dpi-1-25-width-400-favorites", 400d, 1.25d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-25/width-480/search",
            66, "native-popups/066-popup.language-tags-cards-dark-dpi-1-25-width-480-search", 480d, 1.25d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-25/width-480/by-id",
            67, "native-popups/067-popup.language-tags-cards-dark-dpi-1-25-width-480-by-id", 480d, 1.25d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-25/width-480/favorites",
            68, "native-popups/068-popup.language-tags-cards-dark-dpi-1-25-width-480-favorites", 480d, 1.25d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-25/width-640/search",
            69, "native-popups/069-popup.language-tags-cards-dark-dpi-1-25-width-640-search", 640d, 1.25d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-25/width-640/by-id",
            70, "native-popups/070-popup.language-tags-cards-dark-dpi-1-25-width-640-by-id", 640d, 1.25d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-25/width-640/favorites",
            71, "native-popups/071-popup.language-tags-cards-dark-dpi-1-25-width-640-favorites", 640d, 1.25d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-5/width-320/search",
            72, "native-popups/072-popup.language-tags-cards-dark-dpi-1-5-width-320-search", 320d, 1.5d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-5/width-320/by-id",
            73, "native-popups/073-popup.language-tags-cards-dark-dpi-1-5-width-320-by-id", 320d, 1.5d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-5/width-320/favorites",
            74, "native-popups/074-popup.language-tags-cards-dark-dpi-1-5-width-320-favorites", 320d, 1.5d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-5/width-400/search",
            75, "native-popups/075-popup.language-tags-cards-dark-dpi-1-5-width-400-search", 400d, 1.5d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-5/width-400/by-id",
            76, "native-popups/076-popup.language-tags-cards-dark-dpi-1-5-width-400-by-id", 400d, 1.5d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-5/width-400/favorites",
            77, "native-popups/077-popup.language-tags-cards-dark-dpi-1-5-width-400-favorites", 400d, 1.5d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-5/width-480/search",
            78, "native-popups/078-popup.language-tags-cards-dark-dpi-1-5-width-480-search", 480d, 1.5d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-5/width-480/by-id",
            79, "native-popups/079-popup.language-tags-cards-dark-dpi-1-5-width-480-by-id", 480d, 1.5d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-5/width-480/favorites",
            80, "native-popups/080-popup.language-tags-cards-dark-dpi-1-5-width-480-favorites", 480d, 1.5d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-5/width-640/search",
            81, "native-popups/081-popup.language-tags-cards-dark-dpi-1-5-width-640-search", 640d, 1.5d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-5/width-640/by-id",
            82, "native-popups/082-popup.language-tags-cards-dark-dpi-1-5-width-640-by-id", 640d, 1.5d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/cards/dark/dpi-1-5/width-640/favorites",
            83, "native-popups/083-popup.language-tags-cards-dark-dpi-1-5-width-640-favorites", 640d, 1.5d, true, VoiceBrowseMode.Favorites);
    }

    [TestCaseSource(nameof(SelectedAndActiveCardsRetainTheirCompleteRenderedOwnerRows))]
    [Order(12)]
    public Task SelectedAndActiveCardsRetainTheirCompleteRenderedOwner(double width, double dpi, bool dark, VoiceBrowseMode mode)
        => StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => VoiceLanguageTagsSuite.RenderCompiledCardsAsync(width, dpi, dark, mode, null)));
}
