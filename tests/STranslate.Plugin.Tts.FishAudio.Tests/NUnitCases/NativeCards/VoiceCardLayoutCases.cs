using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCases.NativeCards;

// Source proposal. Retain the complete ordered profile before transferring ownership.
[TestFixture]
[NonParallelizable]
[Order(1)]
[Property("FishAudioExecutionProfile", "native-cards")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class VoiceCardLayoutCases
{
    public static IEnumerable<TestCaseData> ColdConstructionRows()
    {
        yield return NativeTestCaseSource.Row(
            "cards.layout/cold-construction",
            1, "native-cards/001-cards.layout-cold-construction");
    }

    [TestCaseSource(nameof(ColdConstructionRows))]
    [Order(1)]
    public Task ColdConstruction() =>
        VoiceCardLayoutSuite.ObserveColdConstructionAsync();

    public static IEnumerable<TestCaseData> StaticCardsRows()
    {
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1/width-320/search",
            2, "native-cards/002-cards.layout-en-light-dpi-1-width-320-search", 320d, 1d, false, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1/width-320/by-id",
            3, "native-cards/003-cards.layout-en-light-dpi-1-width-320-by-id", 320d, 1d, false, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1/width-320/favorites",
            4, "native-cards/004-cards.layout-en-light-dpi-1-width-320-favorites", 320d, 1d, false, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1/width-400/search",
            5, "native-cards/005-cards.layout-en-light-dpi-1-width-400-search", 400d, 1d, false, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1/width-400/by-id",
            6, "native-cards/006-cards.layout-en-light-dpi-1-width-400-by-id", 400d, 1d, false, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1/width-400/favorites",
            7, "native-cards/007-cards.layout-en-light-dpi-1-width-400-favorites", 400d, 1d, false, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1/width-480/search",
            8, "native-cards/008-cards.layout-en-light-dpi-1-width-480-search", 480d, 1d, false, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1/width-480/by-id",
            9, "native-cards/009-cards.layout-en-light-dpi-1-width-480-by-id", 480d, 1d, false, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1/width-480/favorites",
            10, "native-cards/010-cards.layout-en-light-dpi-1-width-480-favorites", 480d, 1d, false, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1/width-640/search",
            11, "native-cards/011-cards.layout-en-light-dpi-1-width-640-search", 640d, 1d, false, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1/width-640/by-id",
            12, "native-cards/012-cards.layout-en-light-dpi-1-width-640-by-id", 640d, 1d, false, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1/width-640/favorites",
            13, "native-cards/013-cards.layout-en-light-dpi-1-width-640-favorites", 640d, 1d, false, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-25/width-320/search",
            14, "native-cards/014-cards.layout-en-light-dpi-1-25-width-320-search", 320d, 1.25, false, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-25/width-320/by-id",
            15, "native-cards/015-cards.layout-en-light-dpi-1-25-width-320-by-id", 320d, 1.25, false, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-25/width-320/favorites",
            16, "native-cards/016-cards.layout-en-light-dpi-1-25-width-320-favorites", 320d, 1.25, false, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-25/width-400/search",
            17, "native-cards/017-cards.layout-en-light-dpi-1-25-width-400-search", 400d, 1.25, false, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-25/width-400/by-id",
            18, "native-cards/018-cards.layout-en-light-dpi-1-25-width-400-by-id", 400d, 1.25, false, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-25/width-400/favorites",
            19, "native-cards/019-cards.layout-en-light-dpi-1-25-width-400-favorites", 400d, 1.25, false, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-25/width-480/search",
            20, "native-cards/020-cards.layout-en-light-dpi-1-25-width-480-search", 480d, 1.25, false, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-25/width-480/by-id",
            21, "native-cards/021-cards.layout-en-light-dpi-1-25-width-480-by-id", 480d, 1.25, false, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-25/width-480/favorites",
            22, "native-cards/022-cards.layout-en-light-dpi-1-25-width-480-favorites", 480d, 1.25, false, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-25/width-640/search",
            23, "native-cards/023-cards.layout-en-light-dpi-1-25-width-640-search", 640d, 1.25, false, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-25/width-640/by-id",
            24, "native-cards/024-cards.layout-en-light-dpi-1-25-width-640-by-id", 640d, 1.25, false, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-25/width-640/favorites",
            25, "native-cards/025-cards.layout-en-light-dpi-1-25-width-640-favorites", 640d, 1.25, false, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-5/width-320/search",
            26, "native-cards/026-cards.layout-en-light-dpi-1-5-width-320-search", 320d, 1.5, false, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-5/width-320/by-id",
            27, "native-cards/027-cards.layout-en-light-dpi-1-5-width-320-by-id", 320d, 1.5, false, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-5/width-320/favorites",
            28, "native-cards/028-cards.layout-en-light-dpi-1-5-width-320-favorites", 320d, 1.5, false, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-5/width-400/search",
            29, "native-cards/029-cards.layout-en-light-dpi-1-5-width-400-search", 400d, 1.5, false, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-5/width-400/by-id",
            30, "native-cards/030-cards.layout-en-light-dpi-1-5-width-400-by-id", 400d, 1.5, false, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-5/width-400/favorites",
            31, "native-cards/031-cards.layout-en-light-dpi-1-5-width-400-favorites", 400d, 1.5, false, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-5/width-480/search",
            32, "native-cards/032-cards.layout-en-light-dpi-1-5-width-480-search", 480d, 1.5, false, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-5/width-480/by-id",
            33, "native-cards/033-cards.layout-en-light-dpi-1-5-width-480-by-id", 480d, 1.5, false, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-5/width-480/favorites",
            34, "native-cards/034-cards.layout-en-light-dpi-1-5-width-480-favorites", 480d, 1.5, false, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-5/width-640/search",
            35, "native-cards/035-cards.layout-en-light-dpi-1-5-width-640-search", 640d, 1.5, false, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-5/width-640/by-id",
            36, "native-cards/036-cards.layout-en-light-dpi-1-5-width-640-by-id", 640d, 1.5, false, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/light/dpi-1-5/width-640/favorites",
            37, "native-cards/037-cards.layout-en-light-dpi-1-5-width-640-favorites", 640d, 1.5, false, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1/width-320/search",
            38, "native-cards/038-cards.layout-en-dark-dpi-1-width-320-search", 320d, 1d, true, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1/width-320/by-id",
            39, "native-cards/039-cards.layout-en-dark-dpi-1-width-320-by-id", 320d, 1d, true, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1/width-320/favorites",
            40, "native-cards/040-cards.layout-en-dark-dpi-1-width-320-favorites", 320d, 1d, true, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1/width-400/search",
            41, "native-cards/041-cards.layout-en-dark-dpi-1-width-400-search", 400d, 1d, true, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1/width-400/by-id",
            42, "native-cards/042-cards.layout-en-dark-dpi-1-width-400-by-id", 400d, 1d, true, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1/width-400/favorites",
            43, "native-cards/043-cards.layout-en-dark-dpi-1-width-400-favorites", 400d, 1d, true, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1/width-480/search",
            44, "native-cards/044-cards.layout-en-dark-dpi-1-width-480-search", 480d, 1d, true, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1/width-480/by-id",
            45, "native-cards/045-cards.layout-en-dark-dpi-1-width-480-by-id", 480d, 1d, true, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1/width-480/favorites",
            46, "native-cards/046-cards.layout-en-dark-dpi-1-width-480-favorites", 480d, 1d, true, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1/width-640/search",
            47, "native-cards/047-cards.layout-en-dark-dpi-1-width-640-search", 640d, 1d, true, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1/width-640/by-id",
            48, "native-cards/048-cards.layout-en-dark-dpi-1-width-640-by-id", 640d, 1d, true, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1/width-640/favorites",
            49, "native-cards/049-cards.layout-en-dark-dpi-1-width-640-favorites", 640d, 1d, true, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-25/width-320/search",
            50, "native-cards/050-cards.layout-en-dark-dpi-1-25-width-320-search", 320d, 1.25, true, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-25/width-320/by-id",
            51, "native-cards/051-cards.layout-en-dark-dpi-1-25-width-320-by-id", 320d, 1.25, true, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-25/width-320/favorites",
            52, "native-cards/052-cards.layout-en-dark-dpi-1-25-width-320-favorites", 320d, 1.25, true, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-25/width-400/search",
            53, "native-cards/053-cards.layout-en-dark-dpi-1-25-width-400-search", 400d, 1.25, true, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-25/width-400/by-id",
            54, "native-cards/054-cards.layout-en-dark-dpi-1-25-width-400-by-id", 400d, 1.25, true, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-25/width-400/favorites",
            55, "native-cards/055-cards.layout-en-dark-dpi-1-25-width-400-favorites", 400d, 1.25, true, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-25/width-480/search",
            56, "native-cards/056-cards.layout-en-dark-dpi-1-25-width-480-search", 480d, 1.25, true, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-25/width-480/by-id",
            57, "native-cards/057-cards.layout-en-dark-dpi-1-25-width-480-by-id", 480d, 1.25, true, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-25/width-480/favorites",
            58, "native-cards/058-cards.layout-en-dark-dpi-1-25-width-480-favorites", 480d, 1.25, true, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-25/width-640/search",
            59, "native-cards/059-cards.layout-en-dark-dpi-1-25-width-640-search", 640d, 1.25, true, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-25/width-640/by-id",
            60, "native-cards/060-cards.layout-en-dark-dpi-1-25-width-640-by-id", 640d, 1.25, true, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-25/width-640/favorites",
            61, "native-cards/061-cards.layout-en-dark-dpi-1-25-width-640-favorites", 640d, 1.25, true, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-5/width-320/search",
            62, "native-cards/062-cards.layout-en-dark-dpi-1-5-width-320-search", 320d, 1.5, true, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-5/width-320/by-id",
            63, "native-cards/063-cards.layout-en-dark-dpi-1-5-width-320-by-id", 320d, 1.5, true, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-5/width-320/favorites",
            64, "native-cards/064-cards.layout-en-dark-dpi-1-5-width-320-favorites", 320d, 1.5, true, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-5/width-400/search",
            65, "native-cards/065-cards.layout-en-dark-dpi-1-5-width-400-search", 400d, 1.5, true, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-5/width-400/by-id",
            66, "native-cards/066-cards.layout-en-dark-dpi-1-5-width-400-by-id", 400d, 1.5, true, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-5/width-400/favorites",
            67, "native-cards/067-cards.layout-en-dark-dpi-1-5-width-400-favorites", 400d, 1.5, true, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-5/width-480/search",
            68, "native-cards/068-cards.layout-en-dark-dpi-1-5-width-480-search", 480d, 1.5, true, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-5/width-480/by-id",
            69, "native-cards/069-cards.layout-en-dark-dpi-1-5-width-480-by-id", 480d, 1.5, true, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-5/width-480/favorites",
            70, "native-cards/070-cards.layout-en-dark-dpi-1-5-width-480-favorites", 480d, 1.5, true, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-5/width-640/search",
            71, "native-cards/071-cards.layout-en-dark-dpi-1-5-width-640-search", 640d, 1.5, true, "en", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-5/width-640/by-id",
            72, "native-cards/072-cards.layout-en-dark-dpi-1-5-width-640-by-id", 640d, 1.5, true, "en", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/en/dark/dpi-1-5/width-640/favorites",
            73, "native-cards/073-cards.layout-en-dark-dpi-1-5-width-640-favorites", 640d, 1.5, true, "en", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1/width-320/search",
            74, "native-cards/074-cards.layout-zh-cn-light-dpi-1-width-320-search", 320d, 1d, false, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1/width-320/by-id",
            75, "native-cards/075-cards.layout-zh-cn-light-dpi-1-width-320-by-id", 320d, 1d, false, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1/width-320/favorites",
            76, "native-cards/076-cards.layout-zh-cn-light-dpi-1-width-320-favorites", 320d, 1d, false, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1/width-400/search",
            77, "native-cards/077-cards.layout-zh-cn-light-dpi-1-width-400-search", 400d, 1d, false, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1/width-400/by-id",
            78, "native-cards/078-cards.layout-zh-cn-light-dpi-1-width-400-by-id", 400d, 1d, false, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1/width-400/favorites",
            79, "native-cards/079-cards.layout-zh-cn-light-dpi-1-width-400-favorites", 400d, 1d, false, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1/width-480/search",
            80, "native-cards/080-cards.layout-zh-cn-light-dpi-1-width-480-search", 480d, 1d, false, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1/width-480/by-id",
            81, "native-cards/081-cards.layout-zh-cn-light-dpi-1-width-480-by-id", 480d, 1d, false, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1/width-480/favorites",
            82, "native-cards/082-cards.layout-zh-cn-light-dpi-1-width-480-favorites", 480d, 1d, false, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1/width-640/search",
            83, "native-cards/083-cards.layout-zh-cn-light-dpi-1-width-640-search", 640d, 1d, false, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1/width-640/by-id",
            84, "native-cards/084-cards.layout-zh-cn-light-dpi-1-width-640-by-id", 640d, 1d, false, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1/width-640/favorites",
            85, "native-cards/085-cards.layout-zh-cn-light-dpi-1-width-640-favorites", 640d, 1d, false, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-25/width-320/search",
            86, "native-cards/086-cards.layout-zh-cn-light-dpi-1-25-width-320-search", 320d, 1.25, false, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-25/width-320/by-id",
            87, "native-cards/087-cards.layout-zh-cn-light-dpi-1-25-width-320-by-id", 320d, 1.25, false, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-25/width-320/favorites",
            88, "native-cards/088-cards.layout-zh-cn-light-dpi-1-25-width-320-favorites", 320d, 1.25, false, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-25/width-400/search",
            89, "native-cards/089-cards.layout-zh-cn-light-dpi-1-25-width-400-search", 400d, 1.25, false, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-25/width-400/by-id",
            90, "native-cards/090-cards.layout-zh-cn-light-dpi-1-25-width-400-by-id", 400d, 1.25, false, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-25/width-400/favorites",
            91, "native-cards/091-cards.layout-zh-cn-light-dpi-1-25-width-400-favorites", 400d, 1.25, false, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-25/width-480/search",
            92, "native-cards/092-cards.layout-zh-cn-light-dpi-1-25-width-480-search", 480d, 1.25, false, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-25/width-480/by-id",
            93, "native-cards/093-cards.layout-zh-cn-light-dpi-1-25-width-480-by-id", 480d, 1.25, false, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-25/width-480/favorites",
            94, "native-cards/094-cards.layout-zh-cn-light-dpi-1-25-width-480-favorites", 480d, 1.25, false, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-25/width-640/search",
            95, "native-cards/095-cards.layout-zh-cn-light-dpi-1-25-width-640-search", 640d, 1.25, false, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-25/width-640/by-id",
            96, "native-cards/096-cards.layout-zh-cn-light-dpi-1-25-width-640-by-id", 640d, 1.25, false, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-25/width-640/favorites",
            97, "native-cards/097-cards.layout-zh-cn-light-dpi-1-25-width-640-favorites", 640d, 1.25, false, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-5/width-320/search",
            98, "native-cards/098-cards.layout-zh-cn-light-dpi-1-5-width-320-search", 320d, 1.5, false, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-5/width-320/by-id",
            99, "native-cards/099-cards.layout-zh-cn-light-dpi-1-5-width-320-by-id", 320d, 1.5, false, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-5/width-320/favorites",
            100, "native-cards/100-cards.layout-zh-cn-light-dpi-1-5-width-320-favorites", 320d, 1.5, false, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-5/width-400/search",
            101, "native-cards/101-cards.layout-zh-cn-light-dpi-1-5-width-400-search", 400d, 1.5, false, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-5/width-400/by-id",
            102, "native-cards/102-cards.layout-zh-cn-light-dpi-1-5-width-400-by-id", 400d, 1.5, false, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-5/width-400/favorites",
            103, "native-cards/103-cards.layout-zh-cn-light-dpi-1-5-width-400-favorites", 400d, 1.5, false, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-5/width-480/search",
            104, "native-cards/104-cards.layout-zh-cn-light-dpi-1-5-width-480-search", 480d, 1.5, false, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-5/width-480/by-id",
            105, "native-cards/105-cards.layout-zh-cn-light-dpi-1-5-width-480-by-id", 480d, 1.5, false, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-5/width-480/favorites",
            106, "native-cards/106-cards.layout-zh-cn-light-dpi-1-5-width-480-favorites", 480d, 1.5, false, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-5/width-640/search",
            107, "native-cards/107-cards.layout-zh-cn-light-dpi-1-5-width-640-search", 640d, 1.5, false, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-5/width-640/by-id",
            108, "native-cards/108-cards.layout-zh-cn-light-dpi-1-5-width-640-by-id", 640d, 1.5, false, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/light/dpi-1-5/width-640/favorites",
            109, "native-cards/109-cards.layout-zh-cn-light-dpi-1-5-width-640-favorites", 640d, 1.5, false, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1/width-320/search",
            110, "native-cards/110-cards.layout-zh-cn-dark-dpi-1-width-320-search", 320d, 1d, true, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1/width-320/by-id",
            111, "native-cards/111-cards.layout-zh-cn-dark-dpi-1-width-320-by-id", 320d, 1d, true, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1/width-320/favorites",
            112, "native-cards/112-cards.layout-zh-cn-dark-dpi-1-width-320-favorites", 320d, 1d, true, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1/width-400/search",
            113, "native-cards/113-cards.layout-zh-cn-dark-dpi-1-width-400-search", 400d, 1d, true, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1/width-400/by-id",
            114, "native-cards/114-cards.layout-zh-cn-dark-dpi-1-width-400-by-id", 400d, 1d, true, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1/width-400/favorites",
            115, "native-cards/115-cards.layout-zh-cn-dark-dpi-1-width-400-favorites", 400d, 1d, true, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1/width-480/search",
            116, "native-cards/116-cards.layout-zh-cn-dark-dpi-1-width-480-search", 480d, 1d, true, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1/width-480/by-id",
            117, "native-cards/117-cards.layout-zh-cn-dark-dpi-1-width-480-by-id", 480d, 1d, true, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1/width-480/favorites",
            118, "native-cards/118-cards.layout-zh-cn-dark-dpi-1-width-480-favorites", 480d, 1d, true, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1/width-640/search",
            119, "native-cards/119-cards.layout-zh-cn-dark-dpi-1-width-640-search", 640d, 1d, true, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1/width-640/by-id",
            120, "native-cards/120-cards.layout-zh-cn-dark-dpi-1-width-640-by-id", 640d, 1d, true, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1/width-640/favorites",
            121, "native-cards/121-cards.layout-zh-cn-dark-dpi-1-width-640-favorites", 640d, 1d, true, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-25/width-320/search",
            122, "native-cards/122-cards.layout-zh-cn-dark-dpi-1-25-width-320-search", 320d, 1.25, true, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-25/width-320/by-id",
            123, "native-cards/123-cards.layout-zh-cn-dark-dpi-1-25-width-320-by-id", 320d, 1.25, true, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-25/width-320/favorites",
            124, "native-cards/124-cards.layout-zh-cn-dark-dpi-1-25-width-320-favorites", 320d, 1.25, true, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-25/width-400/search",
            125, "native-cards/125-cards.layout-zh-cn-dark-dpi-1-25-width-400-search", 400d, 1.25, true, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-25/width-400/by-id",
            126, "native-cards/126-cards.layout-zh-cn-dark-dpi-1-25-width-400-by-id", 400d, 1.25, true, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-25/width-400/favorites",
            127, "native-cards/127-cards.layout-zh-cn-dark-dpi-1-25-width-400-favorites", 400d, 1.25, true, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-25/width-480/search",
            128, "native-cards/128-cards.layout-zh-cn-dark-dpi-1-25-width-480-search", 480d, 1.25, true, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-25/width-480/by-id",
            129, "native-cards/129-cards.layout-zh-cn-dark-dpi-1-25-width-480-by-id", 480d, 1.25, true, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-25/width-480/favorites",
            130, "native-cards/130-cards.layout-zh-cn-dark-dpi-1-25-width-480-favorites", 480d, 1.25, true, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-25/width-640/search",
            131, "native-cards/131-cards.layout-zh-cn-dark-dpi-1-25-width-640-search", 640d, 1.25, true, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-25/width-640/by-id",
            132, "native-cards/132-cards.layout-zh-cn-dark-dpi-1-25-width-640-by-id", 640d, 1.25, true, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-25/width-640/favorites",
            133, "native-cards/133-cards.layout-zh-cn-dark-dpi-1-25-width-640-favorites", 640d, 1.25, true, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-5/width-320/search",
            134, "native-cards/134-cards.layout-zh-cn-dark-dpi-1-5-width-320-search", 320d, 1.5, true, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-5/width-320/by-id",
            135, "native-cards/135-cards.layout-zh-cn-dark-dpi-1-5-width-320-by-id", 320d, 1.5, true, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-5/width-320/favorites",
            136, "native-cards/136-cards.layout-zh-cn-dark-dpi-1-5-width-320-favorites", 320d, 1.5, true, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-5/width-400/search",
            137, "native-cards/137-cards.layout-zh-cn-dark-dpi-1-5-width-400-search", 400d, 1.5, true, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-5/width-400/by-id",
            138, "native-cards/138-cards.layout-zh-cn-dark-dpi-1-5-width-400-by-id", 400d, 1.5, true, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-5/width-400/favorites",
            139, "native-cards/139-cards.layout-zh-cn-dark-dpi-1-5-width-400-favorites", 400d, 1.5, true, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-5/width-480/search",
            140, "native-cards/140-cards.layout-zh-cn-dark-dpi-1-5-width-480-search", 480d, 1.5, true, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-5/width-480/by-id",
            141, "native-cards/141-cards.layout-zh-cn-dark-dpi-1-5-width-480-by-id", 480d, 1.5, true, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-5/width-480/favorites",
            142, "native-cards/142-cards.layout-zh-cn-dark-dpi-1-5-width-480-favorites", 480d, 1.5, true, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-5/width-640/search",
            143, "native-cards/143-cards.layout-zh-cn-dark-dpi-1-5-width-640-search", 640d, 1.5, true, "zh-cn", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-5/width-640/by-id",
            144, "native-cards/144-cards.layout-zh-cn-dark-dpi-1-5-width-640-by-id", 640d, 1.5, true, "zh-cn", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-cn/dark/dpi-1-5/width-640/favorites",
            145, "native-cards/145-cards.layout-zh-cn-dark-dpi-1-5-width-640-favorites", 640d, 1.5, true, "zh-cn", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1/width-320/search",
            146, "native-cards/146-cards.layout-zh-tw-light-dpi-1-width-320-search", 320d, 1d, false, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1/width-320/by-id",
            147, "native-cards/147-cards.layout-zh-tw-light-dpi-1-width-320-by-id", 320d, 1d, false, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1/width-320/favorites",
            148, "native-cards/148-cards.layout-zh-tw-light-dpi-1-width-320-favorites", 320d, 1d, false, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1/width-400/search",
            149, "native-cards/149-cards.layout-zh-tw-light-dpi-1-width-400-search", 400d, 1d, false, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1/width-400/by-id",
            150, "native-cards/150-cards.layout-zh-tw-light-dpi-1-width-400-by-id", 400d, 1d, false, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1/width-400/favorites",
            151, "native-cards/151-cards.layout-zh-tw-light-dpi-1-width-400-favorites", 400d, 1d, false, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1/width-480/search",
            152, "native-cards/152-cards.layout-zh-tw-light-dpi-1-width-480-search", 480d, 1d, false, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1/width-480/by-id",
            153, "native-cards/153-cards.layout-zh-tw-light-dpi-1-width-480-by-id", 480d, 1d, false, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1/width-480/favorites",
            154, "native-cards/154-cards.layout-zh-tw-light-dpi-1-width-480-favorites", 480d, 1d, false, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1/width-640/search",
            155, "native-cards/155-cards.layout-zh-tw-light-dpi-1-width-640-search", 640d, 1d, false, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1/width-640/by-id",
            156, "native-cards/156-cards.layout-zh-tw-light-dpi-1-width-640-by-id", 640d, 1d, false, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1/width-640/favorites",
            157, "native-cards/157-cards.layout-zh-tw-light-dpi-1-width-640-favorites", 640d, 1d, false, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-25/width-320/search",
            158, "native-cards/158-cards.layout-zh-tw-light-dpi-1-25-width-320-search", 320d, 1.25, false, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-25/width-320/by-id",
            159, "native-cards/159-cards.layout-zh-tw-light-dpi-1-25-width-320-by-id", 320d, 1.25, false, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-25/width-320/favorites",
            160, "native-cards/160-cards.layout-zh-tw-light-dpi-1-25-width-320-favorites", 320d, 1.25, false, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-25/width-400/search",
            161, "native-cards/161-cards.layout-zh-tw-light-dpi-1-25-width-400-search", 400d, 1.25, false, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-25/width-400/by-id",
            162, "native-cards/162-cards.layout-zh-tw-light-dpi-1-25-width-400-by-id", 400d, 1.25, false, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-25/width-400/favorites",
            163, "native-cards/163-cards.layout-zh-tw-light-dpi-1-25-width-400-favorites", 400d, 1.25, false, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-25/width-480/search",
            164, "native-cards/164-cards.layout-zh-tw-light-dpi-1-25-width-480-search", 480d, 1.25, false, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-25/width-480/by-id",
            165, "native-cards/165-cards.layout-zh-tw-light-dpi-1-25-width-480-by-id", 480d, 1.25, false, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-25/width-480/favorites",
            166, "native-cards/166-cards.layout-zh-tw-light-dpi-1-25-width-480-favorites", 480d, 1.25, false, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-25/width-640/search",
            167, "native-cards/167-cards.layout-zh-tw-light-dpi-1-25-width-640-search", 640d, 1.25, false, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-25/width-640/by-id",
            168, "native-cards/168-cards.layout-zh-tw-light-dpi-1-25-width-640-by-id", 640d, 1.25, false, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-25/width-640/favorites",
            169, "native-cards/169-cards.layout-zh-tw-light-dpi-1-25-width-640-favorites", 640d, 1.25, false, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-5/width-320/search",
            170, "native-cards/170-cards.layout-zh-tw-light-dpi-1-5-width-320-search", 320d, 1.5, false, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-5/width-320/by-id",
            171, "native-cards/171-cards.layout-zh-tw-light-dpi-1-5-width-320-by-id", 320d, 1.5, false, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-5/width-320/favorites",
            172, "native-cards/172-cards.layout-zh-tw-light-dpi-1-5-width-320-favorites", 320d, 1.5, false, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-5/width-400/search",
            173, "native-cards/173-cards.layout-zh-tw-light-dpi-1-5-width-400-search", 400d, 1.5, false, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-5/width-400/by-id",
            174, "native-cards/174-cards.layout-zh-tw-light-dpi-1-5-width-400-by-id", 400d, 1.5, false, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-5/width-400/favorites",
            175, "native-cards/175-cards.layout-zh-tw-light-dpi-1-5-width-400-favorites", 400d, 1.5, false, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-5/width-480/search",
            176, "native-cards/176-cards.layout-zh-tw-light-dpi-1-5-width-480-search", 480d, 1.5, false, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-5/width-480/by-id",
            177, "native-cards/177-cards.layout-zh-tw-light-dpi-1-5-width-480-by-id", 480d, 1.5, false, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-5/width-480/favorites",
            178, "native-cards/178-cards.layout-zh-tw-light-dpi-1-5-width-480-favorites", 480d, 1.5, false, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-5/width-640/search",
            179, "native-cards/179-cards.layout-zh-tw-light-dpi-1-5-width-640-search", 640d, 1.5, false, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-5/width-640/by-id",
            180, "native-cards/180-cards.layout-zh-tw-light-dpi-1-5-width-640-by-id", 640d, 1.5, false, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/light/dpi-1-5/width-640/favorites",
            181, "native-cards/181-cards.layout-zh-tw-light-dpi-1-5-width-640-favorites", 640d, 1.5, false, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1/width-320/search",
            182, "native-cards/182-cards.layout-zh-tw-dark-dpi-1-width-320-search", 320d, 1d, true, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1/width-320/by-id",
            183, "native-cards/183-cards.layout-zh-tw-dark-dpi-1-width-320-by-id", 320d, 1d, true, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1/width-320/favorites",
            184, "native-cards/184-cards.layout-zh-tw-dark-dpi-1-width-320-favorites", 320d, 1d, true, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1/width-400/search",
            185, "native-cards/185-cards.layout-zh-tw-dark-dpi-1-width-400-search", 400d, 1d, true, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1/width-400/by-id",
            186, "native-cards/186-cards.layout-zh-tw-dark-dpi-1-width-400-by-id", 400d, 1d, true, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1/width-400/favorites",
            187, "native-cards/187-cards.layout-zh-tw-dark-dpi-1-width-400-favorites", 400d, 1d, true, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1/width-480/search",
            188, "native-cards/188-cards.layout-zh-tw-dark-dpi-1-width-480-search", 480d, 1d, true, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1/width-480/by-id",
            189, "native-cards/189-cards.layout-zh-tw-dark-dpi-1-width-480-by-id", 480d, 1d, true, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1/width-480/favorites",
            190, "native-cards/190-cards.layout-zh-tw-dark-dpi-1-width-480-favorites", 480d, 1d, true, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1/width-640/search",
            191, "native-cards/191-cards.layout-zh-tw-dark-dpi-1-width-640-search", 640d, 1d, true, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1/width-640/by-id",
            192, "native-cards/192-cards.layout-zh-tw-dark-dpi-1-width-640-by-id", 640d, 1d, true, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1/width-640/favorites",
            193, "native-cards/193-cards.layout-zh-tw-dark-dpi-1-width-640-favorites", 640d, 1d, true, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-25/width-320/search",
            194, "native-cards/194-cards.layout-zh-tw-dark-dpi-1-25-width-320-search", 320d, 1.25, true, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-25/width-320/by-id",
            195, "native-cards/195-cards.layout-zh-tw-dark-dpi-1-25-width-320-by-id", 320d, 1.25, true, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-25/width-320/favorites",
            196, "native-cards/196-cards.layout-zh-tw-dark-dpi-1-25-width-320-favorites", 320d, 1.25, true, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-25/width-400/search",
            197, "native-cards/197-cards.layout-zh-tw-dark-dpi-1-25-width-400-search", 400d, 1.25, true, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-25/width-400/by-id",
            198, "native-cards/198-cards.layout-zh-tw-dark-dpi-1-25-width-400-by-id", 400d, 1.25, true, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-25/width-400/favorites",
            199, "native-cards/199-cards.layout-zh-tw-dark-dpi-1-25-width-400-favorites", 400d, 1.25, true, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-25/width-480/search",
            200, "native-cards/200-cards.layout-zh-tw-dark-dpi-1-25-width-480-search", 480d, 1.25, true, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-25/width-480/by-id",
            201, "native-cards/201-cards.layout-zh-tw-dark-dpi-1-25-width-480-by-id", 480d, 1.25, true, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-25/width-480/favorites",
            202, "native-cards/202-cards.layout-zh-tw-dark-dpi-1-25-width-480-favorites", 480d, 1.25, true, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-25/width-640/search",
            203, "native-cards/203-cards.layout-zh-tw-dark-dpi-1-25-width-640-search", 640d, 1.25, true, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-25/width-640/by-id",
            204, "native-cards/204-cards.layout-zh-tw-dark-dpi-1-25-width-640-by-id", 640d, 1.25, true, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-25/width-640/favorites",
            205, "native-cards/205-cards.layout-zh-tw-dark-dpi-1-25-width-640-favorites", 640d, 1.25, true, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-5/width-320/search",
            206, "native-cards/206-cards.layout-zh-tw-dark-dpi-1-5-width-320-search", 320d, 1.5, true, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-5/width-320/by-id",
            207, "native-cards/207-cards.layout-zh-tw-dark-dpi-1-5-width-320-by-id", 320d, 1.5, true, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-5/width-320/favorites",
            208, "native-cards/208-cards.layout-zh-tw-dark-dpi-1-5-width-320-favorites", 320d, 1.5, true, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-5/width-400/search",
            209, "native-cards/209-cards.layout-zh-tw-dark-dpi-1-5-width-400-search", 400d, 1.5, true, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-5/width-400/by-id",
            210, "native-cards/210-cards.layout-zh-tw-dark-dpi-1-5-width-400-by-id", 400d, 1.5, true, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-5/width-400/favorites",
            211, "native-cards/211-cards.layout-zh-tw-dark-dpi-1-5-width-400-favorites", 400d, 1.5, true, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-5/width-480/search",
            212, "native-cards/212-cards.layout-zh-tw-dark-dpi-1-5-width-480-search", 480d, 1.5, true, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-5/width-480/by-id",
            213, "native-cards/213-cards.layout-zh-tw-dark-dpi-1-5-width-480-by-id", 480d, 1.5, true, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-5/width-480/favorites",
            214, "native-cards/214-cards.layout-zh-tw-dark-dpi-1-5-width-480-favorites", 480d, 1.5, true, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-5/width-640/search",
            215, "native-cards/215-cards.layout-zh-tw-dark-dpi-1-5-width-640-search", 640d, 1.5, true, "zh-tw", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-5/width-640/by-id",
            216, "native-cards/216-cards.layout-zh-tw-dark-dpi-1-5-width-640-by-id", 640d, 1.5, true, "zh-tw", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/zh-tw/dark/dpi-1-5/width-640/favorites",
            217, "native-cards/217-cards.layout-zh-tw-dark-dpi-1-5-width-640-favorites", 640d, 1.5, true, "zh-tw", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1/width-320/search",
            218, "native-cards/218-cards.layout-ja-light-dpi-1-width-320-search", 320d, 1d, false, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1/width-320/by-id",
            219, "native-cards/219-cards.layout-ja-light-dpi-1-width-320-by-id", 320d, 1d, false, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1/width-320/favorites",
            220, "native-cards/220-cards.layout-ja-light-dpi-1-width-320-favorites", 320d, 1d, false, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1/width-400/search",
            221, "native-cards/221-cards.layout-ja-light-dpi-1-width-400-search", 400d, 1d, false, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1/width-400/by-id",
            222, "native-cards/222-cards.layout-ja-light-dpi-1-width-400-by-id", 400d, 1d, false, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1/width-400/favorites",
            223, "native-cards/223-cards.layout-ja-light-dpi-1-width-400-favorites", 400d, 1d, false, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1/width-480/search",
            224, "native-cards/224-cards.layout-ja-light-dpi-1-width-480-search", 480d, 1d, false, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1/width-480/by-id",
            225, "native-cards/225-cards.layout-ja-light-dpi-1-width-480-by-id", 480d, 1d, false, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1/width-480/favorites",
            226, "native-cards/226-cards.layout-ja-light-dpi-1-width-480-favorites", 480d, 1d, false, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1/width-640/search",
            227, "native-cards/227-cards.layout-ja-light-dpi-1-width-640-search", 640d, 1d, false, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1/width-640/by-id",
            228, "native-cards/228-cards.layout-ja-light-dpi-1-width-640-by-id", 640d, 1d, false, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1/width-640/favorites",
            229, "native-cards/229-cards.layout-ja-light-dpi-1-width-640-favorites", 640d, 1d, false, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-25/width-320/search",
            230, "native-cards/230-cards.layout-ja-light-dpi-1-25-width-320-search", 320d, 1.25, false, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-25/width-320/by-id",
            231, "native-cards/231-cards.layout-ja-light-dpi-1-25-width-320-by-id", 320d, 1.25, false, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-25/width-320/favorites",
            232, "native-cards/232-cards.layout-ja-light-dpi-1-25-width-320-favorites", 320d, 1.25, false, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-25/width-400/search",
            233, "native-cards/233-cards.layout-ja-light-dpi-1-25-width-400-search", 400d, 1.25, false, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-25/width-400/by-id",
            234, "native-cards/234-cards.layout-ja-light-dpi-1-25-width-400-by-id", 400d, 1.25, false, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-25/width-400/favorites",
            235, "native-cards/235-cards.layout-ja-light-dpi-1-25-width-400-favorites", 400d, 1.25, false, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-25/width-480/search",
            236, "native-cards/236-cards.layout-ja-light-dpi-1-25-width-480-search", 480d, 1.25, false, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-25/width-480/by-id",
            237, "native-cards/237-cards.layout-ja-light-dpi-1-25-width-480-by-id", 480d, 1.25, false, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-25/width-480/favorites",
            238, "native-cards/238-cards.layout-ja-light-dpi-1-25-width-480-favorites", 480d, 1.25, false, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-25/width-640/search",
            239, "native-cards/239-cards.layout-ja-light-dpi-1-25-width-640-search", 640d, 1.25, false, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-25/width-640/by-id",
            240, "native-cards/240-cards.layout-ja-light-dpi-1-25-width-640-by-id", 640d, 1.25, false, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-25/width-640/favorites",
            241, "native-cards/241-cards.layout-ja-light-dpi-1-25-width-640-favorites", 640d, 1.25, false, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-5/width-320/search",
            242, "native-cards/242-cards.layout-ja-light-dpi-1-5-width-320-search", 320d, 1.5, false, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-5/width-320/by-id",
            243, "native-cards/243-cards.layout-ja-light-dpi-1-5-width-320-by-id", 320d, 1.5, false, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-5/width-320/favorites",
            244, "native-cards/244-cards.layout-ja-light-dpi-1-5-width-320-favorites", 320d, 1.5, false, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-5/width-400/search",
            245, "native-cards/245-cards.layout-ja-light-dpi-1-5-width-400-search", 400d, 1.5, false, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-5/width-400/by-id",
            246, "native-cards/246-cards.layout-ja-light-dpi-1-5-width-400-by-id", 400d, 1.5, false, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-5/width-400/favorites",
            247, "native-cards/247-cards.layout-ja-light-dpi-1-5-width-400-favorites", 400d, 1.5, false, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-5/width-480/search",
            248, "native-cards/248-cards.layout-ja-light-dpi-1-5-width-480-search", 480d, 1.5, false, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-5/width-480/by-id",
            249, "native-cards/249-cards.layout-ja-light-dpi-1-5-width-480-by-id", 480d, 1.5, false, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-5/width-480/favorites",
            250, "native-cards/250-cards.layout-ja-light-dpi-1-5-width-480-favorites", 480d, 1.5, false, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-5/width-640/search",
            251, "native-cards/251-cards.layout-ja-light-dpi-1-5-width-640-search", 640d, 1.5, false, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-5/width-640/by-id",
            252, "native-cards/252-cards.layout-ja-light-dpi-1-5-width-640-by-id", 640d, 1.5, false, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/light/dpi-1-5/width-640/favorites",
            253, "native-cards/253-cards.layout-ja-light-dpi-1-5-width-640-favorites", 640d, 1.5, false, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1/width-320/search",
            254, "native-cards/254-cards.layout-ja-dark-dpi-1-width-320-search", 320d, 1d, true, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1/width-320/by-id",
            255, "native-cards/255-cards.layout-ja-dark-dpi-1-width-320-by-id", 320d, 1d, true, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1/width-320/favorites",
            256, "native-cards/256-cards.layout-ja-dark-dpi-1-width-320-favorites", 320d, 1d, true, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1/width-400/search",
            257, "native-cards/257-cards.layout-ja-dark-dpi-1-width-400-search", 400d, 1d, true, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1/width-400/by-id",
            258, "native-cards/258-cards.layout-ja-dark-dpi-1-width-400-by-id", 400d, 1d, true, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1/width-400/favorites",
            259, "native-cards/259-cards.layout-ja-dark-dpi-1-width-400-favorites", 400d, 1d, true, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1/width-480/search",
            260, "native-cards/260-cards.layout-ja-dark-dpi-1-width-480-search", 480d, 1d, true, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1/width-480/by-id",
            261, "native-cards/261-cards.layout-ja-dark-dpi-1-width-480-by-id", 480d, 1d, true, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1/width-480/favorites",
            262, "native-cards/262-cards.layout-ja-dark-dpi-1-width-480-favorites", 480d, 1d, true, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1/width-640/search",
            263, "native-cards/263-cards.layout-ja-dark-dpi-1-width-640-search", 640d, 1d, true, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1/width-640/by-id",
            264, "native-cards/264-cards.layout-ja-dark-dpi-1-width-640-by-id", 640d, 1d, true, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1/width-640/favorites",
            265, "native-cards/265-cards.layout-ja-dark-dpi-1-width-640-favorites", 640d, 1d, true, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-25/width-320/search",
            266, "native-cards/266-cards.layout-ja-dark-dpi-1-25-width-320-search", 320d, 1.25, true, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-25/width-320/by-id",
            267, "native-cards/267-cards.layout-ja-dark-dpi-1-25-width-320-by-id", 320d, 1.25, true, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-25/width-320/favorites",
            268, "native-cards/268-cards.layout-ja-dark-dpi-1-25-width-320-favorites", 320d, 1.25, true, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-25/width-400/search",
            269, "native-cards/269-cards.layout-ja-dark-dpi-1-25-width-400-search", 400d, 1.25, true, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-25/width-400/by-id",
            270, "native-cards/270-cards.layout-ja-dark-dpi-1-25-width-400-by-id", 400d, 1.25, true, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-25/width-400/favorites",
            271, "native-cards/271-cards.layout-ja-dark-dpi-1-25-width-400-favorites", 400d, 1.25, true, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-25/width-480/search",
            272, "native-cards/272-cards.layout-ja-dark-dpi-1-25-width-480-search", 480d, 1.25, true, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-25/width-480/by-id",
            273, "native-cards/273-cards.layout-ja-dark-dpi-1-25-width-480-by-id", 480d, 1.25, true, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-25/width-480/favorites",
            274, "native-cards/274-cards.layout-ja-dark-dpi-1-25-width-480-favorites", 480d, 1.25, true, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-25/width-640/search",
            275, "native-cards/275-cards.layout-ja-dark-dpi-1-25-width-640-search", 640d, 1.25, true, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-25/width-640/by-id",
            276, "native-cards/276-cards.layout-ja-dark-dpi-1-25-width-640-by-id", 640d, 1.25, true, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-25/width-640/favorites",
            277, "native-cards/277-cards.layout-ja-dark-dpi-1-25-width-640-favorites", 640d, 1.25, true, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-5/width-320/search",
            278, "native-cards/278-cards.layout-ja-dark-dpi-1-5-width-320-search", 320d, 1.5, true, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-5/width-320/by-id",
            279, "native-cards/279-cards.layout-ja-dark-dpi-1-5-width-320-by-id", 320d, 1.5, true, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-5/width-320/favorites",
            280, "native-cards/280-cards.layout-ja-dark-dpi-1-5-width-320-favorites", 320d, 1.5, true, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-5/width-400/search",
            281, "native-cards/281-cards.layout-ja-dark-dpi-1-5-width-400-search", 400d, 1.5, true, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-5/width-400/by-id",
            282, "native-cards/282-cards.layout-ja-dark-dpi-1-5-width-400-by-id", 400d, 1.5, true, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-5/width-400/favorites",
            283, "native-cards/283-cards.layout-ja-dark-dpi-1-5-width-400-favorites", 400d, 1.5, true, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-5/width-480/search",
            284, "native-cards/284-cards.layout-ja-dark-dpi-1-5-width-480-search", 480d, 1.5, true, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-5/width-480/by-id",
            285, "native-cards/285-cards.layout-ja-dark-dpi-1-5-width-480-by-id", 480d, 1.5, true, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-5/width-480/favorites",
            286, "native-cards/286-cards.layout-ja-dark-dpi-1-5-width-480-favorites", 480d, 1.5, true, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-5/width-640/search",
            287, "native-cards/287-cards.layout-ja-dark-dpi-1-5-width-640-search", 640d, 1.5, true, "ja", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-5/width-640/by-id",
            288, "native-cards/288-cards.layout-ja-dark-dpi-1-5-width-640-by-id", 640d, 1.5, true, "ja", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ja/dark/dpi-1-5/width-640/favorites",
            289, "native-cards/289-cards.layout-ja-dark-dpi-1-5-width-640-favorites", 640d, 1.5, true, "ja", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1/width-320/search",
            290, "native-cards/290-cards.layout-ko-light-dpi-1-width-320-search", 320d, 1d, false, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1/width-320/by-id",
            291, "native-cards/291-cards.layout-ko-light-dpi-1-width-320-by-id", 320d, 1d, false, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1/width-320/favorites",
            292, "native-cards/292-cards.layout-ko-light-dpi-1-width-320-favorites", 320d, 1d, false, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1/width-400/search",
            293, "native-cards/293-cards.layout-ko-light-dpi-1-width-400-search", 400d, 1d, false, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1/width-400/by-id",
            294, "native-cards/294-cards.layout-ko-light-dpi-1-width-400-by-id", 400d, 1d, false, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1/width-400/favorites",
            295, "native-cards/295-cards.layout-ko-light-dpi-1-width-400-favorites", 400d, 1d, false, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1/width-480/search",
            296, "native-cards/296-cards.layout-ko-light-dpi-1-width-480-search", 480d, 1d, false, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1/width-480/by-id",
            297, "native-cards/297-cards.layout-ko-light-dpi-1-width-480-by-id", 480d, 1d, false, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1/width-480/favorites",
            298, "native-cards/298-cards.layout-ko-light-dpi-1-width-480-favorites", 480d, 1d, false, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1/width-640/search",
            299, "native-cards/299-cards.layout-ko-light-dpi-1-width-640-search", 640d, 1d, false, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1/width-640/by-id",
            300, "native-cards/300-cards.layout-ko-light-dpi-1-width-640-by-id", 640d, 1d, false, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1/width-640/favorites",
            301, "native-cards/301-cards.layout-ko-light-dpi-1-width-640-favorites", 640d, 1d, false, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-25/width-320/search",
            302, "native-cards/302-cards.layout-ko-light-dpi-1-25-width-320-search", 320d, 1.25, false, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-25/width-320/by-id",
            303, "native-cards/303-cards.layout-ko-light-dpi-1-25-width-320-by-id", 320d, 1.25, false, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-25/width-320/favorites",
            304, "native-cards/304-cards.layout-ko-light-dpi-1-25-width-320-favorites", 320d, 1.25, false, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-25/width-400/search",
            305, "native-cards/305-cards.layout-ko-light-dpi-1-25-width-400-search", 400d, 1.25, false, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-25/width-400/by-id",
            306, "native-cards/306-cards.layout-ko-light-dpi-1-25-width-400-by-id", 400d, 1.25, false, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-25/width-400/favorites",
            307, "native-cards/307-cards.layout-ko-light-dpi-1-25-width-400-favorites", 400d, 1.25, false, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-25/width-480/search",
            308, "native-cards/308-cards.layout-ko-light-dpi-1-25-width-480-search", 480d, 1.25, false, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-25/width-480/by-id",
            309, "native-cards/309-cards.layout-ko-light-dpi-1-25-width-480-by-id", 480d, 1.25, false, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-25/width-480/favorites",
            310, "native-cards/310-cards.layout-ko-light-dpi-1-25-width-480-favorites", 480d, 1.25, false, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-25/width-640/search",
            311, "native-cards/311-cards.layout-ko-light-dpi-1-25-width-640-search", 640d, 1.25, false, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-25/width-640/by-id",
            312, "native-cards/312-cards.layout-ko-light-dpi-1-25-width-640-by-id", 640d, 1.25, false, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-25/width-640/favorites",
            313, "native-cards/313-cards.layout-ko-light-dpi-1-25-width-640-favorites", 640d, 1.25, false, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-5/width-320/search",
            314, "native-cards/314-cards.layout-ko-light-dpi-1-5-width-320-search", 320d, 1.5, false, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-5/width-320/by-id",
            315, "native-cards/315-cards.layout-ko-light-dpi-1-5-width-320-by-id", 320d, 1.5, false, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-5/width-320/favorites",
            316, "native-cards/316-cards.layout-ko-light-dpi-1-5-width-320-favorites", 320d, 1.5, false, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-5/width-400/search",
            317, "native-cards/317-cards.layout-ko-light-dpi-1-5-width-400-search", 400d, 1.5, false, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-5/width-400/by-id",
            318, "native-cards/318-cards.layout-ko-light-dpi-1-5-width-400-by-id", 400d, 1.5, false, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-5/width-400/favorites",
            319, "native-cards/319-cards.layout-ko-light-dpi-1-5-width-400-favorites", 400d, 1.5, false, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-5/width-480/search",
            320, "native-cards/320-cards.layout-ko-light-dpi-1-5-width-480-search", 480d, 1.5, false, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-5/width-480/by-id",
            321, "native-cards/321-cards.layout-ko-light-dpi-1-5-width-480-by-id", 480d, 1.5, false, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-5/width-480/favorites",
            322, "native-cards/322-cards.layout-ko-light-dpi-1-5-width-480-favorites", 480d, 1.5, false, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-5/width-640/search",
            323, "native-cards/323-cards.layout-ko-light-dpi-1-5-width-640-search", 640d, 1.5, false, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-5/width-640/by-id",
            324, "native-cards/324-cards.layout-ko-light-dpi-1-5-width-640-by-id", 640d, 1.5, false, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/light/dpi-1-5/width-640/favorites",
            325, "native-cards/325-cards.layout-ko-light-dpi-1-5-width-640-favorites", 640d, 1.5, false, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1/width-320/search",
            326, "native-cards/326-cards.layout-ko-dark-dpi-1-width-320-search", 320d, 1d, true, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1/width-320/by-id",
            327, "native-cards/327-cards.layout-ko-dark-dpi-1-width-320-by-id", 320d, 1d, true, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1/width-320/favorites",
            328, "native-cards/328-cards.layout-ko-dark-dpi-1-width-320-favorites", 320d, 1d, true, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1/width-400/search",
            329, "native-cards/329-cards.layout-ko-dark-dpi-1-width-400-search", 400d, 1d, true, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1/width-400/by-id",
            330, "native-cards/330-cards.layout-ko-dark-dpi-1-width-400-by-id", 400d, 1d, true, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1/width-400/favorites",
            331, "native-cards/331-cards.layout-ko-dark-dpi-1-width-400-favorites", 400d, 1d, true, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1/width-480/search",
            332, "native-cards/332-cards.layout-ko-dark-dpi-1-width-480-search", 480d, 1d, true, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1/width-480/by-id",
            333, "native-cards/333-cards.layout-ko-dark-dpi-1-width-480-by-id", 480d, 1d, true, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1/width-480/favorites",
            334, "native-cards/334-cards.layout-ko-dark-dpi-1-width-480-favorites", 480d, 1d, true, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1/width-640/search",
            335, "native-cards/335-cards.layout-ko-dark-dpi-1-width-640-search", 640d, 1d, true, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1/width-640/by-id",
            336, "native-cards/336-cards.layout-ko-dark-dpi-1-width-640-by-id", 640d, 1d, true, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1/width-640/favorites",
            337, "native-cards/337-cards.layout-ko-dark-dpi-1-width-640-favorites", 640d, 1d, true, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-25/width-320/search",
            338, "native-cards/338-cards.layout-ko-dark-dpi-1-25-width-320-search", 320d, 1.25, true, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-25/width-320/by-id",
            339, "native-cards/339-cards.layout-ko-dark-dpi-1-25-width-320-by-id", 320d, 1.25, true, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-25/width-320/favorites",
            340, "native-cards/340-cards.layout-ko-dark-dpi-1-25-width-320-favorites", 320d, 1.25, true, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-25/width-400/search",
            341, "native-cards/341-cards.layout-ko-dark-dpi-1-25-width-400-search", 400d, 1.25, true, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-25/width-400/by-id",
            342, "native-cards/342-cards.layout-ko-dark-dpi-1-25-width-400-by-id", 400d, 1.25, true, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-25/width-400/favorites",
            343, "native-cards/343-cards.layout-ko-dark-dpi-1-25-width-400-favorites", 400d, 1.25, true, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-25/width-480/search",
            344, "native-cards/344-cards.layout-ko-dark-dpi-1-25-width-480-search", 480d, 1.25, true, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-25/width-480/by-id",
            345, "native-cards/345-cards.layout-ko-dark-dpi-1-25-width-480-by-id", 480d, 1.25, true, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-25/width-480/favorites",
            346, "native-cards/346-cards.layout-ko-dark-dpi-1-25-width-480-favorites", 480d, 1.25, true, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-25/width-640/search",
            347, "native-cards/347-cards.layout-ko-dark-dpi-1-25-width-640-search", 640d, 1.25, true, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-25/width-640/by-id",
            348, "native-cards/348-cards.layout-ko-dark-dpi-1-25-width-640-by-id", 640d, 1.25, true, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-25/width-640/favorites",
            349, "native-cards/349-cards.layout-ko-dark-dpi-1-25-width-640-favorites", 640d, 1.25, true, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-5/width-320/search",
            350, "native-cards/350-cards.layout-ko-dark-dpi-1-5-width-320-search", 320d, 1.5, true, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-5/width-320/by-id",
            351, "native-cards/351-cards.layout-ko-dark-dpi-1-5-width-320-by-id", 320d, 1.5, true, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-5/width-320/favorites",
            352, "native-cards/352-cards.layout-ko-dark-dpi-1-5-width-320-favorites", 320d, 1.5, true, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-5/width-400/search",
            353, "native-cards/353-cards.layout-ko-dark-dpi-1-5-width-400-search", 400d, 1.5, true, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-5/width-400/by-id",
            354, "native-cards/354-cards.layout-ko-dark-dpi-1-5-width-400-by-id", 400d, 1.5, true, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-5/width-400/favorites",
            355, "native-cards/355-cards.layout-ko-dark-dpi-1-5-width-400-favorites", 400d, 1.5, true, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-5/width-480/search",
            356, "native-cards/356-cards.layout-ko-dark-dpi-1-5-width-480-search", 480d, 1.5, true, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-5/width-480/by-id",
            357, "native-cards/357-cards.layout-ko-dark-dpi-1-5-width-480-by-id", 480d, 1.5, true, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-5/width-480/favorites",
            358, "native-cards/358-cards.layout-ko-dark-dpi-1-5-width-480-favorites", 480d, 1.5, true, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-5/width-640/search",
            359, "native-cards/359-cards.layout-ko-dark-dpi-1-5-width-640-search", 640d, 1.5, true, "ko", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-5/width-640/by-id",
            360, "native-cards/360-cards.layout-ko-dark-dpi-1-5-width-640-by-id", 640d, 1.5, true, "ko", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ko/dark/dpi-1-5/width-640/favorites",
            361, "native-cards/361-cards.layout-ko-dark-dpi-1-5-width-640-favorites", 640d, 1.5, true, "ko", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1/width-320/search",
            362, "native-cards/362-cards.layout-ru-light-dpi-1-width-320-search", 320d, 1d, false, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1/width-320/by-id",
            363, "native-cards/363-cards.layout-ru-light-dpi-1-width-320-by-id", 320d, 1d, false, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1/width-320/favorites",
            364, "native-cards/364-cards.layout-ru-light-dpi-1-width-320-favorites", 320d, 1d, false, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1/width-400/search",
            365, "native-cards/365-cards.layout-ru-light-dpi-1-width-400-search", 400d, 1d, false, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1/width-400/by-id",
            366, "native-cards/366-cards.layout-ru-light-dpi-1-width-400-by-id", 400d, 1d, false, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1/width-400/favorites",
            367, "native-cards/367-cards.layout-ru-light-dpi-1-width-400-favorites", 400d, 1d, false, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1/width-480/search",
            368, "native-cards/368-cards.layout-ru-light-dpi-1-width-480-search", 480d, 1d, false, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1/width-480/by-id",
            369, "native-cards/369-cards.layout-ru-light-dpi-1-width-480-by-id", 480d, 1d, false, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1/width-480/favorites",
            370, "native-cards/370-cards.layout-ru-light-dpi-1-width-480-favorites", 480d, 1d, false, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1/width-640/search",
            371, "native-cards/371-cards.layout-ru-light-dpi-1-width-640-search", 640d, 1d, false, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1/width-640/by-id",
            372, "native-cards/372-cards.layout-ru-light-dpi-1-width-640-by-id", 640d, 1d, false, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1/width-640/favorites",
            373, "native-cards/373-cards.layout-ru-light-dpi-1-width-640-favorites", 640d, 1d, false, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-25/width-320/search",
            374, "native-cards/374-cards.layout-ru-light-dpi-1-25-width-320-search", 320d, 1.25, false, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-25/width-320/by-id",
            375, "native-cards/375-cards.layout-ru-light-dpi-1-25-width-320-by-id", 320d, 1.25, false, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-25/width-320/favorites",
            376, "native-cards/376-cards.layout-ru-light-dpi-1-25-width-320-favorites", 320d, 1.25, false, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-25/width-400/search",
            377, "native-cards/377-cards.layout-ru-light-dpi-1-25-width-400-search", 400d, 1.25, false, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-25/width-400/by-id",
            378, "native-cards/378-cards.layout-ru-light-dpi-1-25-width-400-by-id", 400d, 1.25, false, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-25/width-400/favorites",
            379, "native-cards/379-cards.layout-ru-light-dpi-1-25-width-400-favorites", 400d, 1.25, false, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-25/width-480/search",
            380, "native-cards/380-cards.layout-ru-light-dpi-1-25-width-480-search", 480d, 1.25, false, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-25/width-480/by-id",
            381, "native-cards/381-cards.layout-ru-light-dpi-1-25-width-480-by-id", 480d, 1.25, false, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-25/width-480/favorites",
            382, "native-cards/382-cards.layout-ru-light-dpi-1-25-width-480-favorites", 480d, 1.25, false, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-25/width-640/search",
            383, "native-cards/383-cards.layout-ru-light-dpi-1-25-width-640-search", 640d, 1.25, false, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-25/width-640/by-id",
            384, "native-cards/384-cards.layout-ru-light-dpi-1-25-width-640-by-id", 640d, 1.25, false, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-25/width-640/favorites",
            385, "native-cards/385-cards.layout-ru-light-dpi-1-25-width-640-favorites", 640d, 1.25, false, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-5/width-320/search",
            386, "native-cards/386-cards.layout-ru-light-dpi-1-5-width-320-search", 320d, 1.5, false, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-5/width-320/by-id",
            387, "native-cards/387-cards.layout-ru-light-dpi-1-5-width-320-by-id", 320d, 1.5, false, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-5/width-320/favorites",
            388, "native-cards/388-cards.layout-ru-light-dpi-1-5-width-320-favorites", 320d, 1.5, false, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-5/width-400/search",
            389, "native-cards/389-cards.layout-ru-light-dpi-1-5-width-400-search", 400d, 1.5, false, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-5/width-400/by-id",
            390, "native-cards/390-cards.layout-ru-light-dpi-1-5-width-400-by-id", 400d, 1.5, false, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-5/width-400/favorites",
            391, "native-cards/391-cards.layout-ru-light-dpi-1-5-width-400-favorites", 400d, 1.5, false, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-5/width-480/search",
            392, "native-cards/392-cards.layout-ru-light-dpi-1-5-width-480-search", 480d, 1.5, false, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-5/width-480/by-id",
            393, "native-cards/393-cards.layout-ru-light-dpi-1-5-width-480-by-id", 480d, 1.5, false, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-5/width-480/favorites",
            394, "native-cards/394-cards.layout-ru-light-dpi-1-5-width-480-favorites", 480d, 1.5, false, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-5/width-640/search",
            395, "native-cards/395-cards.layout-ru-light-dpi-1-5-width-640-search", 640d, 1.5, false, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-5/width-640/by-id",
            396, "native-cards/396-cards.layout-ru-light-dpi-1-5-width-640-by-id", 640d, 1.5, false, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/light/dpi-1-5/width-640/favorites",
            397, "native-cards/397-cards.layout-ru-light-dpi-1-5-width-640-favorites", 640d, 1.5, false, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1/width-320/search",
            398, "native-cards/398-cards.layout-ru-dark-dpi-1-width-320-search", 320d, 1d, true, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1/width-320/by-id",
            399, "native-cards/399-cards.layout-ru-dark-dpi-1-width-320-by-id", 320d, 1d, true, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1/width-320/favorites",
            400, "native-cards/400-cards.layout-ru-dark-dpi-1-width-320-favorites", 320d, 1d, true, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1/width-400/search",
            401, "native-cards/401-cards.layout-ru-dark-dpi-1-width-400-search", 400d, 1d, true, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1/width-400/by-id",
            402, "native-cards/402-cards.layout-ru-dark-dpi-1-width-400-by-id", 400d, 1d, true, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1/width-400/favorites",
            403, "native-cards/403-cards.layout-ru-dark-dpi-1-width-400-favorites", 400d, 1d, true, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1/width-480/search",
            404, "native-cards/404-cards.layout-ru-dark-dpi-1-width-480-search", 480d, 1d, true, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1/width-480/by-id",
            405, "native-cards/405-cards.layout-ru-dark-dpi-1-width-480-by-id", 480d, 1d, true, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1/width-480/favorites",
            406, "native-cards/406-cards.layout-ru-dark-dpi-1-width-480-favorites", 480d, 1d, true, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1/width-640/search",
            407, "native-cards/407-cards.layout-ru-dark-dpi-1-width-640-search", 640d, 1d, true, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1/width-640/by-id",
            408, "native-cards/408-cards.layout-ru-dark-dpi-1-width-640-by-id", 640d, 1d, true, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1/width-640/favorites",
            409, "native-cards/409-cards.layout-ru-dark-dpi-1-width-640-favorites", 640d, 1d, true, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-25/width-320/search",
            410, "native-cards/410-cards.layout-ru-dark-dpi-1-25-width-320-search", 320d, 1.25, true, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-25/width-320/by-id",
            411, "native-cards/411-cards.layout-ru-dark-dpi-1-25-width-320-by-id", 320d, 1.25, true, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-25/width-320/favorites",
            412, "native-cards/412-cards.layout-ru-dark-dpi-1-25-width-320-favorites", 320d, 1.25, true, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-25/width-400/search",
            413, "native-cards/413-cards.layout-ru-dark-dpi-1-25-width-400-search", 400d, 1.25, true, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-25/width-400/by-id",
            414, "native-cards/414-cards.layout-ru-dark-dpi-1-25-width-400-by-id", 400d, 1.25, true, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-25/width-400/favorites",
            415, "native-cards/415-cards.layout-ru-dark-dpi-1-25-width-400-favorites", 400d, 1.25, true, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-25/width-480/search",
            416, "native-cards/416-cards.layout-ru-dark-dpi-1-25-width-480-search", 480d, 1.25, true, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-25/width-480/by-id",
            417, "native-cards/417-cards.layout-ru-dark-dpi-1-25-width-480-by-id", 480d, 1.25, true, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-25/width-480/favorites",
            418, "native-cards/418-cards.layout-ru-dark-dpi-1-25-width-480-favorites", 480d, 1.25, true, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-25/width-640/search",
            419, "native-cards/419-cards.layout-ru-dark-dpi-1-25-width-640-search", 640d, 1.25, true, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-25/width-640/by-id",
            420, "native-cards/420-cards.layout-ru-dark-dpi-1-25-width-640-by-id", 640d, 1.25, true, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-25/width-640/favorites",
            421, "native-cards/421-cards.layout-ru-dark-dpi-1-25-width-640-favorites", 640d, 1.25, true, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-5/width-320/search",
            422, "native-cards/422-cards.layout-ru-dark-dpi-1-5-width-320-search", 320d, 1.5, true, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-5/width-320/by-id",
            423, "native-cards/423-cards.layout-ru-dark-dpi-1-5-width-320-by-id", 320d, 1.5, true, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-5/width-320/favorites",
            424, "native-cards/424-cards.layout-ru-dark-dpi-1-5-width-320-favorites", 320d, 1.5, true, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-5/width-400/search",
            425, "native-cards/425-cards.layout-ru-dark-dpi-1-5-width-400-search", 400d, 1.5, true, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-5/width-400/by-id",
            426, "native-cards/426-cards.layout-ru-dark-dpi-1-5-width-400-by-id", 400d, 1.5, true, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-5/width-400/favorites",
            427, "native-cards/427-cards.layout-ru-dark-dpi-1-5-width-400-favorites", 400d, 1.5, true, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-5/width-480/search",
            428, "native-cards/428-cards.layout-ru-dark-dpi-1-5-width-480-search", 480d, 1.5, true, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-5/width-480/by-id",
            429, "native-cards/429-cards.layout-ru-dark-dpi-1-5-width-480-by-id", 480d, 1.5, true, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-5/width-480/favorites",
            430, "native-cards/430-cards.layout-ru-dark-dpi-1-5-width-480-favorites", 480d, 1.5, true, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-5/width-640/search",
            431, "native-cards/431-cards.layout-ru-dark-dpi-1-5-width-640-search", 640d, 1.5, true, "ru", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-5/width-640/by-id",
            432, "native-cards/432-cards.layout-ru-dark-dpi-1-5-width-640-by-id", 640d, 1.5, true, "ru", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/ru/dark/dpi-1-5/width-640/favorites",
            433, "native-cards/433-cards.layout-ru-dark-dpi-1-5-width-640-favorites", 640d, 1.5, true, "ru", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1/width-320/search",
            434, "native-cards/434-cards.layout-tr-light-dpi-1-width-320-search", 320d, 1d, false, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1/width-320/by-id",
            435, "native-cards/435-cards.layout-tr-light-dpi-1-width-320-by-id", 320d, 1d, false, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1/width-320/favorites",
            436, "native-cards/436-cards.layout-tr-light-dpi-1-width-320-favorites", 320d, 1d, false, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1/width-400/search",
            437, "native-cards/437-cards.layout-tr-light-dpi-1-width-400-search", 400d, 1d, false, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1/width-400/by-id",
            438, "native-cards/438-cards.layout-tr-light-dpi-1-width-400-by-id", 400d, 1d, false, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1/width-400/favorites",
            439, "native-cards/439-cards.layout-tr-light-dpi-1-width-400-favorites", 400d, 1d, false, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1/width-480/search",
            440, "native-cards/440-cards.layout-tr-light-dpi-1-width-480-search", 480d, 1d, false, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1/width-480/by-id",
            441, "native-cards/441-cards.layout-tr-light-dpi-1-width-480-by-id", 480d, 1d, false, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1/width-480/favorites",
            442, "native-cards/442-cards.layout-tr-light-dpi-1-width-480-favorites", 480d, 1d, false, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1/width-640/search",
            443, "native-cards/443-cards.layout-tr-light-dpi-1-width-640-search", 640d, 1d, false, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1/width-640/by-id",
            444, "native-cards/444-cards.layout-tr-light-dpi-1-width-640-by-id", 640d, 1d, false, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1/width-640/favorites",
            445, "native-cards/445-cards.layout-tr-light-dpi-1-width-640-favorites", 640d, 1d, false, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-25/width-320/search",
            446, "native-cards/446-cards.layout-tr-light-dpi-1-25-width-320-search", 320d, 1.25, false, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-25/width-320/by-id",
            447, "native-cards/447-cards.layout-tr-light-dpi-1-25-width-320-by-id", 320d, 1.25, false, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-25/width-320/favorites",
            448, "native-cards/448-cards.layout-tr-light-dpi-1-25-width-320-favorites", 320d, 1.25, false, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-25/width-400/search",
            449, "native-cards/449-cards.layout-tr-light-dpi-1-25-width-400-search", 400d, 1.25, false, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-25/width-400/by-id",
            450, "native-cards/450-cards.layout-tr-light-dpi-1-25-width-400-by-id", 400d, 1.25, false, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-25/width-400/favorites",
            451, "native-cards/451-cards.layout-tr-light-dpi-1-25-width-400-favorites", 400d, 1.25, false, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-25/width-480/search",
            452, "native-cards/452-cards.layout-tr-light-dpi-1-25-width-480-search", 480d, 1.25, false, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-25/width-480/by-id",
            453, "native-cards/453-cards.layout-tr-light-dpi-1-25-width-480-by-id", 480d, 1.25, false, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-25/width-480/favorites",
            454, "native-cards/454-cards.layout-tr-light-dpi-1-25-width-480-favorites", 480d, 1.25, false, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-25/width-640/search",
            455, "native-cards/455-cards.layout-tr-light-dpi-1-25-width-640-search", 640d, 1.25, false, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-25/width-640/by-id",
            456, "native-cards/456-cards.layout-tr-light-dpi-1-25-width-640-by-id", 640d, 1.25, false, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-25/width-640/favorites",
            457, "native-cards/457-cards.layout-tr-light-dpi-1-25-width-640-favorites", 640d, 1.25, false, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-5/width-320/search",
            458, "native-cards/458-cards.layout-tr-light-dpi-1-5-width-320-search", 320d, 1.5, false, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-5/width-320/by-id",
            459, "native-cards/459-cards.layout-tr-light-dpi-1-5-width-320-by-id", 320d, 1.5, false, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-5/width-320/favorites",
            460, "native-cards/460-cards.layout-tr-light-dpi-1-5-width-320-favorites", 320d, 1.5, false, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-5/width-400/search",
            461, "native-cards/461-cards.layout-tr-light-dpi-1-5-width-400-search", 400d, 1.5, false, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-5/width-400/by-id",
            462, "native-cards/462-cards.layout-tr-light-dpi-1-5-width-400-by-id", 400d, 1.5, false, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-5/width-400/favorites",
            463, "native-cards/463-cards.layout-tr-light-dpi-1-5-width-400-favorites", 400d, 1.5, false, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-5/width-480/search",
            464, "native-cards/464-cards.layout-tr-light-dpi-1-5-width-480-search", 480d, 1.5, false, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-5/width-480/by-id",
            465, "native-cards/465-cards.layout-tr-light-dpi-1-5-width-480-by-id", 480d, 1.5, false, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-5/width-480/favorites",
            466, "native-cards/466-cards.layout-tr-light-dpi-1-5-width-480-favorites", 480d, 1.5, false, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-5/width-640/search",
            467, "native-cards/467-cards.layout-tr-light-dpi-1-5-width-640-search", 640d, 1.5, false, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-5/width-640/by-id",
            468, "native-cards/468-cards.layout-tr-light-dpi-1-5-width-640-by-id", 640d, 1.5, false, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/light/dpi-1-5/width-640/favorites",
            469, "native-cards/469-cards.layout-tr-light-dpi-1-5-width-640-favorites", 640d, 1.5, false, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1/width-320/search",
            470, "native-cards/470-cards.layout-tr-dark-dpi-1-width-320-search", 320d, 1d, true, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1/width-320/by-id",
            471, "native-cards/471-cards.layout-tr-dark-dpi-1-width-320-by-id", 320d, 1d, true, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1/width-320/favorites",
            472, "native-cards/472-cards.layout-tr-dark-dpi-1-width-320-favorites", 320d, 1d, true, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1/width-400/search",
            473, "native-cards/473-cards.layout-tr-dark-dpi-1-width-400-search", 400d, 1d, true, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1/width-400/by-id",
            474, "native-cards/474-cards.layout-tr-dark-dpi-1-width-400-by-id", 400d, 1d, true, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1/width-400/favorites",
            475, "native-cards/475-cards.layout-tr-dark-dpi-1-width-400-favorites", 400d, 1d, true, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1/width-480/search",
            476, "native-cards/476-cards.layout-tr-dark-dpi-1-width-480-search", 480d, 1d, true, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1/width-480/by-id",
            477, "native-cards/477-cards.layout-tr-dark-dpi-1-width-480-by-id", 480d, 1d, true, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1/width-480/favorites",
            478, "native-cards/478-cards.layout-tr-dark-dpi-1-width-480-favorites", 480d, 1d, true, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1/width-640/search",
            479, "native-cards/479-cards.layout-tr-dark-dpi-1-width-640-search", 640d, 1d, true, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1/width-640/by-id",
            480, "native-cards/480-cards.layout-tr-dark-dpi-1-width-640-by-id", 640d, 1d, true, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1/width-640/favorites",
            481, "native-cards/481-cards.layout-tr-dark-dpi-1-width-640-favorites", 640d, 1d, true, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-25/width-320/search",
            482, "native-cards/482-cards.layout-tr-dark-dpi-1-25-width-320-search", 320d, 1.25, true, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-25/width-320/by-id",
            483, "native-cards/483-cards.layout-tr-dark-dpi-1-25-width-320-by-id", 320d, 1.25, true, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-25/width-320/favorites",
            484, "native-cards/484-cards.layout-tr-dark-dpi-1-25-width-320-favorites", 320d, 1.25, true, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-25/width-400/search",
            485, "native-cards/485-cards.layout-tr-dark-dpi-1-25-width-400-search", 400d, 1.25, true, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-25/width-400/by-id",
            486, "native-cards/486-cards.layout-tr-dark-dpi-1-25-width-400-by-id", 400d, 1.25, true, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-25/width-400/favorites",
            487, "native-cards/487-cards.layout-tr-dark-dpi-1-25-width-400-favorites", 400d, 1.25, true, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-25/width-480/search",
            488, "native-cards/488-cards.layout-tr-dark-dpi-1-25-width-480-search", 480d, 1.25, true, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-25/width-480/by-id",
            489, "native-cards/489-cards.layout-tr-dark-dpi-1-25-width-480-by-id", 480d, 1.25, true, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-25/width-480/favorites",
            490, "native-cards/490-cards.layout-tr-dark-dpi-1-25-width-480-favorites", 480d, 1.25, true, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-25/width-640/search",
            491, "native-cards/491-cards.layout-tr-dark-dpi-1-25-width-640-search", 640d, 1.25, true, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-25/width-640/by-id",
            492, "native-cards/492-cards.layout-tr-dark-dpi-1-25-width-640-by-id", 640d, 1.25, true, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-25/width-640/favorites",
            493, "native-cards/493-cards.layout-tr-dark-dpi-1-25-width-640-favorites", 640d, 1.25, true, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-5/width-320/search",
            494, "native-cards/494-cards.layout-tr-dark-dpi-1-5-width-320-search", 320d, 1.5, true, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-5/width-320/by-id",
            495, "native-cards/495-cards.layout-tr-dark-dpi-1-5-width-320-by-id", 320d, 1.5, true, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-5/width-320/favorites",
            496, "native-cards/496-cards.layout-tr-dark-dpi-1-5-width-320-favorites", 320d, 1.5, true, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-5/width-400/search",
            497, "native-cards/497-cards.layout-tr-dark-dpi-1-5-width-400-search", 400d, 1.5, true, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-5/width-400/by-id",
            498, "native-cards/498-cards.layout-tr-dark-dpi-1-5-width-400-by-id", 400d, 1.5, true, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-5/width-400/favorites",
            499, "native-cards/499-cards.layout-tr-dark-dpi-1-5-width-400-favorites", 400d, 1.5, true, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-5/width-480/search",
            500, "native-cards/500-cards.layout-tr-dark-dpi-1-5-width-480-search", 480d, 1.5, true, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-5/width-480/by-id",
            501, "native-cards/501-cards.layout-tr-dark-dpi-1-5-width-480-by-id", 480d, 1.5, true, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-5/width-480/favorites",
            502, "native-cards/502-cards.layout-tr-dark-dpi-1-5-width-480-favorites", 480d, 1.5, true, "tr", VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-5/width-640/search",
            503, "native-cards/503-cards.layout-tr-dark-dpi-1-5-width-640-search", 640d, 1.5, true, "tr", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-5/width-640/by-id",
            504, "native-cards/504-cards.layout-tr-dark-dpi-1-5-width-640-by-id", 640d, 1.5, true, "tr", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout/tr/dark/dpi-1-5/width-640/favorites",
            505, "native-cards/505-cards.layout-tr-dark-dpi-1-5-width-640-favorites", 640d, 1.5, true, "tr", VoiceBrowseMode.Favorites);
    }

    [TestCaseSource(nameof(StaticCardsRows))]
    [Order(2)]
    public Task StaticCards(double width, double dpi, bool dark, string locale, VoiceBrowseMode mode) =>
        VoiceCardLayoutSuite.ObserveCardsAsync(width, dpi, dark, locale, mode);

    public static IEnumerable<TestCaseData> DescriptionsRows()
    {
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1/width-320/search",
            506, "native-cards/506-cards.layout.description-en-light-dpi-1-width-320-search", 320d, 1d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1/width-320/by-id",
            507, "native-cards/507-cards.layout.description-en-light-dpi-1-width-320-by-id", 320d, 1d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1/width-320/favorites",
            508, "native-cards/508-cards.layout.description-en-light-dpi-1-width-320-favorites", 320d, 1d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1/width-400/search",
            509, "native-cards/509-cards.layout.description-en-light-dpi-1-width-400-search", 400d, 1d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1/width-400/by-id",
            510, "native-cards/510-cards.layout.description-en-light-dpi-1-width-400-by-id", 400d, 1d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1/width-400/favorites",
            511, "native-cards/511-cards.layout.description-en-light-dpi-1-width-400-favorites", 400d, 1d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1/width-480/search",
            512, "native-cards/512-cards.layout.description-en-light-dpi-1-width-480-search", 480d, 1d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1/width-480/by-id",
            513, "native-cards/513-cards.layout.description-en-light-dpi-1-width-480-by-id", 480d, 1d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1/width-480/favorites",
            514, "native-cards/514-cards.layout.description-en-light-dpi-1-width-480-favorites", 480d, 1d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1/width-640/search",
            515, "native-cards/515-cards.layout.description-en-light-dpi-1-width-640-search", 640d, 1d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1/width-640/by-id",
            516, "native-cards/516-cards.layout.description-en-light-dpi-1-width-640-by-id", 640d, 1d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1/width-640/favorites",
            517, "native-cards/517-cards.layout.description-en-light-dpi-1-width-640-favorites", 640d, 1d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-25/width-320/search",
            518, "native-cards/518-cards.layout.description-en-light-dpi-1-25-width-320-search", 320d, 1.25, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-25/width-320/by-id",
            519, "native-cards/519-cards.layout.description-en-light-dpi-1-25-width-320-by-id", 320d, 1.25, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-25/width-320/favorites",
            520, "native-cards/520-cards.layout.description-en-light-dpi-1-25-width-320-favorites", 320d, 1.25, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-25/width-400/search",
            521, "native-cards/521-cards.layout.description-en-light-dpi-1-25-width-400-search", 400d, 1.25, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-25/width-400/by-id",
            522, "native-cards/522-cards.layout.description-en-light-dpi-1-25-width-400-by-id", 400d, 1.25, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-25/width-400/favorites",
            523, "native-cards/523-cards.layout.description-en-light-dpi-1-25-width-400-favorites", 400d, 1.25, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-25/width-480/search",
            524, "native-cards/524-cards.layout.description-en-light-dpi-1-25-width-480-search", 480d, 1.25, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-25/width-480/by-id",
            525, "native-cards/525-cards.layout.description-en-light-dpi-1-25-width-480-by-id", 480d, 1.25, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-25/width-480/favorites",
            526, "native-cards/526-cards.layout.description-en-light-dpi-1-25-width-480-favorites", 480d, 1.25, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-25/width-640/search",
            527, "native-cards/527-cards.layout.description-en-light-dpi-1-25-width-640-search", 640d, 1.25, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-25/width-640/by-id",
            528, "native-cards/528-cards.layout.description-en-light-dpi-1-25-width-640-by-id", 640d, 1.25, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-25/width-640/favorites",
            529, "native-cards/529-cards.layout.description-en-light-dpi-1-25-width-640-favorites", 640d, 1.25, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-5/width-320/search",
            530, "native-cards/530-cards.layout.description-en-light-dpi-1-5-width-320-search", 320d, 1.5, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-5/width-320/by-id",
            531, "native-cards/531-cards.layout.description-en-light-dpi-1-5-width-320-by-id", 320d, 1.5, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-5/width-320/favorites",
            532, "native-cards/532-cards.layout.description-en-light-dpi-1-5-width-320-favorites", 320d, 1.5, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-5/width-400/search",
            533, "native-cards/533-cards.layout.description-en-light-dpi-1-5-width-400-search", 400d, 1.5, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-5/width-400/by-id",
            534, "native-cards/534-cards.layout.description-en-light-dpi-1-5-width-400-by-id", 400d, 1.5, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-5/width-400/favorites",
            535, "native-cards/535-cards.layout.description-en-light-dpi-1-5-width-400-favorites", 400d, 1.5, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-5/width-480/search",
            536, "native-cards/536-cards.layout.description-en-light-dpi-1-5-width-480-search", 480d, 1.5, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-5/width-480/by-id",
            537, "native-cards/537-cards.layout.description-en-light-dpi-1-5-width-480-by-id", 480d, 1.5, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-5/width-480/favorites",
            538, "native-cards/538-cards.layout.description-en-light-dpi-1-5-width-480-favorites", 480d, 1.5, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-5/width-640/search",
            539, "native-cards/539-cards.layout.description-en-light-dpi-1-5-width-640-search", 640d, 1.5, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-5/width-640/by-id",
            540, "native-cards/540-cards.layout.description-en-light-dpi-1-5-width-640-by-id", 640d, 1.5, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/light/dpi-1-5/width-640/favorites",
            541, "native-cards/541-cards.layout.description-en-light-dpi-1-5-width-640-favorites", 640d, 1.5, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1/width-320/search",
            542, "native-cards/542-cards.layout.description-en-dark-dpi-1-width-320-search", 320d, 1d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1/width-320/by-id",
            543, "native-cards/543-cards.layout.description-en-dark-dpi-1-width-320-by-id", 320d, 1d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1/width-320/favorites",
            544, "native-cards/544-cards.layout.description-en-dark-dpi-1-width-320-favorites", 320d, 1d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1/width-400/search",
            545, "native-cards/545-cards.layout.description-en-dark-dpi-1-width-400-search", 400d, 1d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1/width-400/by-id",
            546, "native-cards/546-cards.layout.description-en-dark-dpi-1-width-400-by-id", 400d, 1d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1/width-400/favorites",
            547, "native-cards/547-cards.layout.description-en-dark-dpi-1-width-400-favorites", 400d, 1d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1/width-480/search",
            548, "native-cards/548-cards.layout.description-en-dark-dpi-1-width-480-search", 480d, 1d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1/width-480/by-id",
            549, "native-cards/549-cards.layout.description-en-dark-dpi-1-width-480-by-id", 480d, 1d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1/width-480/favorites",
            550, "native-cards/550-cards.layout.description-en-dark-dpi-1-width-480-favorites", 480d, 1d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1/width-640/search",
            551, "native-cards/551-cards.layout.description-en-dark-dpi-1-width-640-search", 640d, 1d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1/width-640/by-id",
            552, "native-cards/552-cards.layout.description-en-dark-dpi-1-width-640-by-id", 640d, 1d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1/width-640/favorites",
            553, "native-cards/553-cards.layout.description-en-dark-dpi-1-width-640-favorites", 640d, 1d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-25/width-320/search",
            554, "native-cards/554-cards.layout.description-en-dark-dpi-1-25-width-320-search", 320d, 1.25, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-25/width-320/by-id",
            555, "native-cards/555-cards.layout.description-en-dark-dpi-1-25-width-320-by-id", 320d, 1.25, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-25/width-320/favorites",
            556, "native-cards/556-cards.layout.description-en-dark-dpi-1-25-width-320-favorites", 320d, 1.25, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-25/width-400/search",
            557, "native-cards/557-cards.layout.description-en-dark-dpi-1-25-width-400-search", 400d, 1.25, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-25/width-400/by-id",
            558, "native-cards/558-cards.layout.description-en-dark-dpi-1-25-width-400-by-id", 400d, 1.25, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-25/width-400/favorites",
            559, "native-cards/559-cards.layout.description-en-dark-dpi-1-25-width-400-favorites", 400d, 1.25, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-25/width-480/search",
            560, "native-cards/560-cards.layout.description-en-dark-dpi-1-25-width-480-search", 480d, 1.25, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-25/width-480/by-id",
            561, "native-cards/561-cards.layout.description-en-dark-dpi-1-25-width-480-by-id", 480d, 1.25, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-25/width-480/favorites",
            562, "native-cards/562-cards.layout.description-en-dark-dpi-1-25-width-480-favorites", 480d, 1.25, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-25/width-640/search",
            563, "native-cards/563-cards.layout.description-en-dark-dpi-1-25-width-640-search", 640d, 1.25, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-25/width-640/by-id",
            564, "native-cards/564-cards.layout.description-en-dark-dpi-1-25-width-640-by-id", 640d, 1.25, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-25/width-640/favorites",
            565, "native-cards/565-cards.layout.description-en-dark-dpi-1-25-width-640-favorites", 640d, 1.25, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-5/width-320/search",
            566, "native-cards/566-cards.layout.description-en-dark-dpi-1-5-width-320-search", 320d, 1.5, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-5/width-320/by-id",
            567, "native-cards/567-cards.layout.description-en-dark-dpi-1-5-width-320-by-id", 320d, 1.5, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-5/width-320/favorites",
            568, "native-cards/568-cards.layout.description-en-dark-dpi-1-5-width-320-favorites", 320d, 1.5, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-5/width-400/search",
            569, "native-cards/569-cards.layout.description-en-dark-dpi-1-5-width-400-search", 400d, 1.5, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-5/width-400/by-id",
            570, "native-cards/570-cards.layout.description-en-dark-dpi-1-5-width-400-by-id", 400d, 1.5, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-5/width-400/favorites",
            571, "native-cards/571-cards.layout.description-en-dark-dpi-1-5-width-400-favorites", 400d, 1.5, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-5/width-480/search",
            572, "native-cards/572-cards.layout.description-en-dark-dpi-1-5-width-480-search", 480d, 1.5, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-5/width-480/by-id",
            573, "native-cards/573-cards.layout.description-en-dark-dpi-1-5-width-480-by-id", 480d, 1.5, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-5/width-480/favorites",
            574, "native-cards/574-cards.layout.description-en-dark-dpi-1-5-width-480-favorites", 480d, 1.5, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-5/width-640/search",
            575, "native-cards/575-cards.layout.description-en-dark-dpi-1-5-width-640-search", 640d, 1.5, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-5/width-640/by-id",
            576, "native-cards/576-cards.layout.description-en-dark-dpi-1-5-width-640-by-id", 640d, 1.5, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.description/en/dark/dpi-1-5/width-640/favorites",
            577, "native-cards/577-cards.layout.description-en-dark-dpi-1-5-width-640-favorites", 640d, 1.5, true, VoiceBrowseMode.Favorites);
    }

    [TestCaseSource(nameof(DescriptionsRows))]
    [Order(506)]
    public Task Descriptions(double width, double dpi, bool dark, VoiceBrowseMode mode) =>
        VoiceCardLayoutSuite.ObserveDescriptionsAsync(width, dpi, dark, mode);

    public static IEnumerable<TestCaseData> HeadersRows()
    {
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1/width-320/search",
            578, "native-cards/578-cards.layout.headers-en-light-dpi-1-width-320-search", 320d, 1d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1/width-320/by-id",
            579, "native-cards/579-cards.layout.headers-en-light-dpi-1-width-320-by-id", 320d, 1d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1/width-320/favorites",
            580, "native-cards/580-cards.layout.headers-en-light-dpi-1-width-320-favorites", 320d, 1d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1/width-400/search",
            581, "native-cards/581-cards.layout.headers-en-light-dpi-1-width-400-search", 400d, 1d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1/width-400/by-id",
            582, "native-cards/582-cards.layout.headers-en-light-dpi-1-width-400-by-id", 400d, 1d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1/width-400/favorites",
            583, "native-cards/583-cards.layout.headers-en-light-dpi-1-width-400-favorites", 400d, 1d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1/width-480/search",
            584, "native-cards/584-cards.layout.headers-en-light-dpi-1-width-480-search", 480d, 1d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1/width-480/by-id",
            585, "native-cards/585-cards.layout.headers-en-light-dpi-1-width-480-by-id", 480d, 1d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1/width-480/favorites",
            586, "native-cards/586-cards.layout.headers-en-light-dpi-1-width-480-favorites", 480d, 1d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1/width-640/search",
            587, "native-cards/587-cards.layout.headers-en-light-dpi-1-width-640-search", 640d, 1d, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1/width-640/by-id",
            588, "native-cards/588-cards.layout.headers-en-light-dpi-1-width-640-by-id", 640d, 1d, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1/width-640/favorites",
            589, "native-cards/589-cards.layout.headers-en-light-dpi-1-width-640-favorites", 640d, 1d, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-25/width-320/search",
            590, "native-cards/590-cards.layout.headers-en-light-dpi-1-25-width-320-search", 320d, 1.25, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-25/width-320/by-id",
            591, "native-cards/591-cards.layout.headers-en-light-dpi-1-25-width-320-by-id", 320d, 1.25, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-25/width-320/favorites",
            592, "native-cards/592-cards.layout.headers-en-light-dpi-1-25-width-320-favorites", 320d, 1.25, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-25/width-400/search",
            593, "native-cards/593-cards.layout.headers-en-light-dpi-1-25-width-400-search", 400d, 1.25, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-25/width-400/by-id",
            594, "native-cards/594-cards.layout.headers-en-light-dpi-1-25-width-400-by-id", 400d, 1.25, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-25/width-400/favorites",
            595, "native-cards/595-cards.layout.headers-en-light-dpi-1-25-width-400-favorites", 400d, 1.25, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-25/width-480/search",
            596, "native-cards/596-cards.layout.headers-en-light-dpi-1-25-width-480-search", 480d, 1.25, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-25/width-480/by-id",
            597, "native-cards/597-cards.layout.headers-en-light-dpi-1-25-width-480-by-id", 480d, 1.25, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-25/width-480/favorites",
            598, "native-cards/598-cards.layout.headers-en-light-dpi-1-25-width-480-favorites", 480d, 1.25, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-25/width-640/search",
            599, "native-cards/599-cards.layout.headers-en-light-dpi-1-25-width-640-search", 640d, 1.25, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-25/width-640/by-id",
            600, "native-cards/600-cards.layout.headers-en-light-dpi-1-25-width-640-by-id", 640d, 1.25, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-25/width-640/favorites",
            601, "native-cards/601-cards.layout.headers-en-light-dpi-1-25-width-640-favorites", 640d, 1.25, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-5/width-320/search",
            602, "native-cards/602-cards.layout.headers-en-light-dpi-1-5-width-320-search", 320d, 1.5, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-5/width-320/by-id",
            603, "native-cards/603-cards.layout.headers-en-light-dpi-1-5-width-320-by-id", 320d, 1.5, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-5/width-320/favorites",
            604, "native-cards/604-cards.layout.headers-en-light-dpi-1-5-width-320-favorites", 320d, 1.5, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-5/width-400/search",
            605, "native-cards/605-cards.layout.headers-en-light-dpi-1-5-width-400-search", 400d, 1.5, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-5/width-400/by-id",
            606, "native-cards/606-cards.layout.headers-en-light-dpi-1-5-width-400-by-id", 400d, 1.5, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-5/width-400/favorites",
            607, "native-cards/607-cards.layout.headers-en-light-dpi-1-5-width-400-favorites", 400d, 1.5, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-5/width-480/search",
            608, "native-cards/608-cards.layout.headers-en-light-dpi-1-5-width-480-search", 480d, 1.5, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-5/width-480/by-id",
            609, "native-cards/609-cards.layout.headers-en-light-dpi-1-5-width-480-by-id", 480d, 1.5, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-5/width-480/favorites",
            610, "native-cards/610-cards.layout.headers-en-light-dpi-1-5-width-480-favorites", 480d, 1.5, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-5/width-640/search",
            611, "native-cards/611-cards.layout.headers-en-light-dpi-1-5-width-640-search", 640d, 1.5, false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-5/width-640/by-id",
            612, "native-cards/612-cards.layout.headers-en-light-dpi-1-5-width-640-by-id", 640d, 1.5, false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/light/dpi-1-5/width-640/favorites",
            613, "native-cards/613-cards.layout.headers-en-light-dpi-1-5-width-640-favorites", 640d, 1.5, false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1/width-320/search",
            614, "native-cards/614-cards.layout.headers-en-dark-dpi-1-width-320-search", 320d, 1d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1/width-320/by-id",
            615, "native-cards/615-cards.layout.headers-en-dark-dpi-1-width-320-by-id", 320d, 1d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1/width-320/favorites",
            616, "native-cards/616-cards.layout.headers-en-dark-dpi-1-width-320-favorites", 320d, 1d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1/width-400/search",
            617, "native-cards/617-cards.layout.headers-en-dark-dpi-1-width-400-search", 400d, 1d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1/width-400/by-id",
            618, "native-cards/618-cards.layout.headers-en-dark-dpi-1-width-400-by-id", 400d, 1d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1/width-400/favorites",
            619, "native-cards/619-cards.layout.headers-en-dark-dpi-1-width-400-favorites", 400d, 1d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1/width-480/search",
            620, "native-cards/620-cards.layout.headers-en-dark-dpi-1-width-480-search", 480d, 1d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1/width-480/by-id",
            621, "native-cards/621-cards.layout.headers-en-dark-dpi-1-width-480-by-id", 480d, 1d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1/width-480/favorites",
            622, "native-cards/622-cards.layout.headers-en-dark-dpi-1-width-480-favorites", 480d, 1d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1/width-640/search",
            623, "native-cards/623-cards.layout.headers-en-dark-dpi-1-width-640-search", 640d, 1d, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1/width-640/by-id",
            624, "native-cards/624-cards.layout.headers-en-dark-dpi-1-width-640-by-id", 640d, 1d, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1/width-640/favorites",
            625, "native-cards/625-cards.layout.headers-en-dark-dpi-1-width-640-favorites", 640d, 1d, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-25/width-320/search",
            626, "native-cards/626-cards.layout.headers-en-dark-dpi-1-25-width-320-search", 320d, 1.25, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-25/width-320/by-id",
            627, "native-cards/627-cards.layout.headers-en-dark-dpi-1-25-width-320-by-id", 320d, 1.25, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-25/width-320/favorites",
            628, "native-cards/628-cards.layout.headers-en-dark-dpi-1-25-width-320-favorites", 320d, 1.25, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-25/width-400/search",
            629, "native-cards/629-cards.layout.headers-en-dark-dpi-1-25-width-400-search", 400d, 1.25, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-25/width-400/by-id",
            630, "native-cards/630-cards.layout.headers-en-dark-dpi-1-25-width-400-by-id", 400d, 1.25, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-25/width-400/favorites",
            631, "native-cards/631-cards.layout.headers-en-dark-dpi-1-25-width-400-favorites", 400d, 1.25, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-25/width-480/search",
            632, "native-cards/632-cards.layout.headers-en-dark-dpi-1-25-width-480-search", 480d, 1.25, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-25/width-480/by-id",
            633, "native-cards/633-cards.layout.headers-en-dark-dpi-1-25-width-480-by-id", 480d, 1.25, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-25/width-480/favorites",
            634, "native-cards/634-cards.layout.headers-en-dark-dpi-1-25-width-480-favorites", 480d, 1.25, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-25/width-640/search",
            635, "native-cards/635-cards.layout.headers-en-dark-dpi-1-25-width-640-search", 640d, 1.25, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-25/width-640/by-id",
            636, "native-cards/636-cards.layout.headers-en-dark-dpi-1-25-width-640-by-id", 640d, 1.25, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-25/width-640/favorites",
            637, "native-cards/637-cards.layout.headers-en-dark-dpi-1-25-width-640-favorites", 640d, 1.25, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-5/width-320/search",
            638, "native-cards/638-cards.layout.headers-en-dark-dpi-1-5-width-320-search", 320d, 1.5, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-5/width-320/by-id",
            639, "native-cards/639-cards.layout.headers-en-dark-dpi-1-5-width-320-by-id", 320d, 1.5, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-5/width-320/favorites",
            640, "native-cards/640-cards.layout.headers-en-dark-dpi-1-5-width-320-favorites", 320d, 1.5, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-5/width-400/search",
            641, "native-cards/641-cards.layout.headers-en-dark-dpi-1-5-width-400-search", 400d, 1.5, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-5/width-400/by-id",
            642, "native-cards/642-cards.layout.headers-en-dark-dpi-1-5-width-400-by-id", 400d, 1.5, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-5/width-400/favorites",
            643, "native-cards/643-cards.layout.headers-en-dark-dpi-1-5-width-400-favorites", 400d, 1.5, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-5/width-480/search",
            644, "native-cards/644-cards.layout.headers-en-dark-dpi-1-5-width-480-search", 480d, 1.5, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-5/width-480/by-id",
            645, "native-cards/645-cards.layout.headers-en-dark-dpi-1-5-width-480-by-id", 480d, 1.5, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-5/width-480/favorites",
            646, "native-cards/646-cards.layout.headers-en-dark-dpi-1-5-width-480-favorites", 480d, 1.5, true, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-5/width-640/search",
            647, "native-cards/647-cards.layout.headers-en-dark-dpi-1-5-width-640-search", 640d, 1.5, true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-5/width-640/by-id",
            648, "native-cards/648-cards.layout.headers-en-dark-dpi-1-5-width-640-by-id", 640d, 1.5, true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.headers/en/dark/dpi-1-5/width-640/favorites",
            649, "native-cards/649-cards.layout.headers-en-dark-dpi-1-5-width-640-favorites", 640d, 1.5, true, VoiceBrowseMode.Favorites);
    }

    [TestCaseSource(nameof(HeadersRows))]
    [Order(578)]
    public Task Headers(double width, double dpi, bool dark, VoiceBrowseMode mode) =>
        VoiceCardLayoutSuite.ObserveHeadersAsync(width, dpi, dark, mode);

    public static IEnumerable<TestCaseData> InheritanceRows()
    {
        yield return NativeTestCaseSource.Row(
            "cards.layout.inheritance/en/light/dpi-1/width-400/search",
            650, "native-cards/650-cards.layout.inheritance-en-light-dpi-1-width-400-search", false, 1d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.inheritance/en/light/dpi-1/width-400/by-id",
            651, "native-cards/651-cards.layout.inheritance-en-light-dpi-1-width-400-by-id", false, 1d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.inheritance/en/light/dpi-1/width-400/favorites",
            652, "native-cards/652-cards.layout.inheritance-en-light-dpi-1-width-400-favorites", false, 1d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.inheritance/en/light/dpi-1-25/width-400/search",
            653, "native-cards/653-cards.layout.inheritance-en-light-dpi-1-25-width-400-search", false, 1.25, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.inheritance/en/light/dpi-1-25/width-400/by-id",
            654, "native-cards/654-cards.layout.inheritance-en-light-dpi-1-25-width-400-by-id", false, 1.25, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.inheritance/en/light/dpi-1-25/width-400/favorites",
            655, "native-cards/655-cards.layout.inheritance-en-light-dpi-1-25-width-400-favorites", false, 1.25, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.inheritance/en/light/dpi-1-5/width-400/search",
            656, "native-cards/656-cards.layout.inheritance-en-light-dpi-1-5-width-400-search", false, 1.5, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.inheritance/en/light/dpi-1-5/width-400/by-id",
            657, "native-cards/657-cards.layout.inheritance-en-light-dpi-1-5-width-400-by-id", false, 1.5, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.inheritance/en/light/dpi-1-5/width-400/favorites",
            658, "native-cards/658-cards.layout.inheritance-en-light-dpi-1-5-width-400-favorites", false, 1.5, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.inheritance/en/dark/dpi-1/width-400/search",
            659, "native-cards/659-cards.layout.inheritance-en-dark-dpi-1-width-400-search", true, 1d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.inheritance/en/dark/dpi-1/width-400/by-id",
            660, "native-cards/660-cards.layout.inheritance-en-dark-dpi-1-width-400-by-id", true, 1d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.inheritance/en/dark/dpi-1/width-400/favorites",
            661, "native-cards/661-cards.layout.inheritance-en-dark-dpi-1-width-400-favorites", true, 1d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.inheritance/en/dark/dpi-1-25/width-400/search",
            662, "native-cards/662-cards.layout.inheritance-en-dark-dpi-1-25-width-400-search", true, 1.25, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.inheritance/en/dark/dpi-1-25/width-400/by-id",
            663, "native-cards/663-cards.layout.inheritance-en-dark-dpi-1-25-width-400-by-id", true, 1.25, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.inheritance/en/dark/dpi-1-25/width-400/favorites",
            664, "native-cards/664-cards.layout.inheritance-en-dark-dpi-1-25-width-400-favorites", true, 1.25, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.inheritance/en/dark/dpi-1-5/width-400/search",
            665, "native-cards/665-cards.layout.inheritance-en-dark-dpi-1-5-width-400-search", true, 1.5, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.inheritance/en/dark/dpi-1-5/width-400/by-id",
            666, "native-cards/666-cards.layout.inheritance-en-dark-dpi-1-5-width-400-by-id", true, 1.5, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.inheritance/en/dark/dpi-1-5/width-400/favorites",
            667, "native-cards/667-cards.layout.inheritance-en-dark-dpi-1-5-width-400-favorites", true, 1.5, VoiceBrowseMode.Favorites);
    }

    [TestCaseSource(nameof(InheritanceRows))]
    [Order(650)]
    public Task Inheritance(bool dark, double dpi, VoiceBrowseMode mode) =>
        VoiceCardLayoutSuite.ObserveInheritanceAsync(dark, dpi, mode);

    public static IEnumerable<TestCaseData> DatesRows()
    {
        yield return NativeTestCaseSource.Row(
            "cards.layout.date/en/light/dpi-1-25/width-400/search",
            668, "native-cards/668-cards.layout.date-en-light-dpi-1-25-width-400-search", false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.date/en/light/dpi-1-25/width-400/by-id",
            669, "native-cards/669-cards.layout.date-en-light-dpi-1-25-width-400-by-id", false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.date/en/light/dpi-1-25/width-400/favorites",
            670, "native-cards/670-cards.layout.date-en-light-dpi-1-25-width-400-favorites", false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.date/en/dark/dpi-1-25/width-400/search",
            671, "native-cards/671-cards.layout.date-en-dark-dpi-1-25-width-400-search", true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.date/en/dark/dpi-1-25/width-400/by-id",
            672, "native-cards/672-cards.layout.date-en-dark-dpi-1-25-width-400-by-id", true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.date/en/dark/dpi-1-25/width-400/favorites",
            673, "native-cards/673-cards.layout.date-en-dark-dpi-1-25-width-400-favorites", true, VoiceBrowseMode.Favorites);
    }

    [TestCaseSource(nameof(DatesRows))]
    [Order(668)]
    public Task Dates(bool dark, VoiceBrowseMode mode) =>
        VoiceCardLayoutSuite.ObserveDatesAsync(dark, mode);

    public static IEnumerable<TestCaseData> TooltipsRows()
    {
        yield return NativeTestCaseSource.Row(
            "cards.layout.tooltip/en/light/dpi-1/width-400/search",
            674, "native-cards/674-cards.layout.tooltip-en-light-dpi-1-width-400-search", VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.tooltip/en/light/dpi-1/width-400/by-id",
            675, "native-cards/675-cards.layout.tooltip-en-light-dpi-1-width-400-by-id", VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.tooltip/en/light/dpi-1/width-400/favorites",
            676, "native-cards/676-cards.layout.tooltip-en-light-dpi-1-width-400-favorites", VoiceBrowseMode.Favorites);
    }

    [TestCaseSource(nameof(TooltipsRows))]
    [Order(674)]
    public Task Tooltips(VoiceBrowseMode mode) =>
        VoiceCardLayoutSuite.ObserveTooltipsAsync(mode);

    public static IEnumerable<TestCaseData> LoadingRows()
    {
        yield return NativeTestCaseSource.Row(
            "cards.layout.loading/en/light/dpi-1-25/width-320/search",
            677, "native-cards/677-cards.layout.loading-en-light-dpi-1-25-width-320-search", false, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.loading/en/light/dpi-1-25/width-320/by-id",
            678, "native-cards/678-cards.layout.loading-en-light-dpi-1-25-width-320-by-id", false, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.loading/en/light/dpi-1-25/width-320/favorites",
            679, "native-cards/679-cards.layout.loading-en-light-dpi-1-25-width-320-favorites", false, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.layout.loading/en/dark/dpi-1-25/width-320/search",
            680, "native-cards/680-cards.layout.loading-en-dark-dpi-1-25-width-320-search", true, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.layout.loading/en/dark/dpi-1-25/width-320/by-id",
            681, "native-cards/681-cards.layout.loading-en-dark-dpi-1-25-width-320-by-id", true, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.layout.loading/en/dark/dpi-1-25/width-320/favorites",
            682, "native-cards/682-cards.layout.loading-en-dark-dpi-1-25-width-320-favorites", true, VoiceBrowseMode.Favorites);
    }

    [TestCaseSource(nameof(LoadingRows))]
    [Order(677)]
    public Task Loading(bool dark, VoiceBrowseMode mode) =>
        VoiceCardLayoutSuite.ObserveLoadingAsync(dark, mode);

}
