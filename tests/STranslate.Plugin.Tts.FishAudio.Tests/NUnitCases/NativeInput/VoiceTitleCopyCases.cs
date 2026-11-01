using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(58)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class VoiceTitleCopyCases
{
    public static IEnumerable<TestCaseData> VoiceTitlesExposeCopyOnlyActionsRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/en/width-320/search",
            58, "native-input/058-input.settings-native-copy-en-width-320-search", "en", 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/en/width-320/by-id",
            59, "native-input/059-input.settings-native-copy-en-width-320-by-id", "en", 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/en/width-320/favorites",
            60, "native-input/060-input.settings-native-copy-en-width-320-favorites", "en", 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/en/width-400/search",
            61, "native-input/061-input.settings-native-copy-en-width-400-search", "en", 400d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/en/width-400/by-id",
            62, "native-input/062-input.settings-native-copy-en-width-400-by-id", "en", 400d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/en/width-400/favorites",
            63, "native-input/063-input.settings-native-copy-en-width-400-favorites", "en", 400d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/en/width-480/search",
            64, "native-input/064-input.settings-native-copy-en-width-480-search", "en", 480d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/en/width-480/by-id",
            65, "native-input/065-input.settings-native-copy-en-width-480-by-id", "en", 480d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/en/width-480/favorites",
            66, "native-input/066-input.settings-native-copy-en-width-480-favorites", "en", 480d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/en/width-640/search",
            67, "native-input/067-input.settings-native-copy-en-width-640-search", "en", 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/en/width-640/by-id",
            68, "native-input/068-input.settings-native-copy-en-width-640-by-id", "en", 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/en/width-640/favorites",
            69, "native-input/069-input.settings-native-copy-en-width-640-favorites", "en", 640d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-cn/width-320/search",
            70, "native-input/070-input.settings-native-copy-zh-cn-width-320-search", "zh-cn", 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-cn/width-320/by-id",
            71, "native-input/071-input.settings-native-copy-zh-cn-width-320-by-id", "zh-cn", 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-cn/width-320/favorites",
            72, "native-input/072-input.settings-native-copy-zh-cn-width-320-favorites", "zh-cn", 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-cn/width-400/search",
            73, "native-input/073-input.settings-native-copy-zh-cn-width-400-search", "zh-cn", 400d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-cn/width-400/by-id",
            74, "native-input/074-input.settings-native-copy-zh-cn-width-400-by-id", "zh-cn", 400d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-cn/width-400/favorites",
            75, "native-input/075-input.settings-native-copy-zh-cn-width-400-favorites", "zh-cn", 400d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-cn/width-480/search",
            76, "native-input/076-input.settings-native-copy-zh-cn-width-480-search", "zh-cn", 480d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-cn/width-480/by-id",
            77, "native-input/077-input.settings-native-copy-zh-cn-width-480-by-id", "zh-cn", 480d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-cn/width-480/favorites",
            78, "native-input/078-input.settings-native-copy-zh-cn-width-480-favorites", "zh-cn", 480d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-cn/width-640/search",
            79, "native-input/079-input.settings-native-copy-zh-cn-width-640-search", "zh-cn", 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-cn/width-640/by-id",
            80, "native-input/080-input.settings-native-copy-zh-cn-width-640-by-id", "zh-cn", 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-cn/width-640/favorites",
            81, "native-input/081-input.settings-native-copy-zh-cn-width-640-favorites", "zh-cn", 640d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-tw/width-320/search",
            82, "native-input/082-input.settings-native-copy-zh-tw-width-320-search", "zh-tw", 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-tw/width-320/by-id",
            83, "native-input/083-input.settings-native-copy-zh-tw-width-320-by-id", "zh-tw", 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-tw/width-320/favorites",
            84, "native-input/084-input.settings-native-copy-zh-tw-width-320-favorites", "zh-tw", 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-tw/width-400/search",
            85, "native-input/085-input.settings-native-copy-zh-tw-width-400-search", "zh-tw", 400d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-tw/width-400/by-id",
            86, "native-input/086-input.settings-native-copy-zh-tw-width-400-by-id", "zh-tw", 400d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-tw/width-400/favorites",
            87, "native-input/087-input.settings-native-copy-zh-tw-width-400-favorites", "zh-tw", 400d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-tw/width-480/search",
            88, "native-input/088-input.settings-native-copy-zh-tw-width-480-search", "zh-tw", 480d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-tw/width-480/by-id",
            89, "native-input/089-input.settings-native-copy-zh-tw-width-480-by-id", "zh-tw", 480d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-tw/width-480/favorites",
            90, "native-input/090-input.settings-native-copy-zh-tw-width-480-favorites", "zh-tw", 480d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-tw/width-640/search",
            91, "native-input/091-input.settings-native-copy-zh-tw-width-640-search", "zh-tw", 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-tw/width-640/by-id",
            92, "native-input/092-input.settings-native-copy-zh-tw-width-640-by-id", "zh-tw", 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/zh-tw/width-640/favorites",
            93, "native-input/093-input.settings-native-copy-zh-tw-width-640-favorites", "zh-tw", 640d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ja/width-320/search",
            94, "native-input/094-input.settings-native-copy-ja-width-320-search", "ja", 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ja/width-320/by-id",
            95, "native-input/095-input.settings-native-copy-ja-width-320-by-id", "ja", 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ja/width-320/favorites",
            96, "native-input/096-input.settings-native-copy-ja-width-320-favorites", "ja", 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ja/width-400/search",
            97, "native-input/097-input.settings-native-copy-ja-width-400-search", "ja", 400d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ja/width-400/by-id",
            98, "native-input/098-input.settings-native-copy-ja-width-400-by-id", "ja", 400d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ja/width-400/favorites",
            99, "native-input/099-input.settings-native-copy-ja-width-400-favorites", "ja", 400d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ja/width-480/search",
            100, "native-input/100-input.settings-native-copy-ja-width-480-search", "ja", 480d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ja/width-480/by-id",
            101, "native-input/101-input.settings-native-copy-ja-width-480-by-id", "ja", 480d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ja/width-480/favorites",
            102, "native-input/102-input.settings-native-copy-ja-width-480-favorites", "ja", 480d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ja/width-640/search",
            103, "native-input/103-input.settings-native-copy-ja-width-640-search", "ja", 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ja/width-640/by-id",
            104, "native-input/104-input.settings-native-copy-ja-width-640-by-id", "ja", 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ja/width-640/favorites",
            105, "native-input/105-input.settings-native-copy-ja-width-640-favorites", "ja", 640d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ko/width-320/search",
            106, "native-input/106-input.settings-native-copy-ko-width-320-search", "ko", 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ko/width-320/by-id",
            107, "native-input/107-input.settings-native-copy-ko-width-320-by-id", "ko", 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ko/width-320/favorites",
            108, "native-input/108-input.settings-native-copy-ko-width-320-favorites", "ko", 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ko/width-400/search",
            109, "native-input/109-input.settings-native-copy-ko-width-400-search", "ko", 400d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ko/width-400/by-id",
            110, "native-input/110-input.settings-native-copy-ko-width-400-by-id", "ko", 400d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ko/width-400/favorites",
            111, "native-input/111-input.settings-native-copy-ko-width-400-favorites", "ko", 400d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ko/width-480/search",
            112, "native-input/112-input.settings-native-copy-ko-width-480-search", "ko", 480d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ko/width-480/by-id",
            113, "native-input/113-input.settings-native-copy-ko-width-480-by-id", "ko", 480d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ko/width-480/favorites",
            114, "native-input/114-input.settings-native-copy-ko-width-480-favorites", "ko", 480d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ko/width-640/search",
            115, "native-input/115-input.settings-native-copy-ko-width-640-search", "ko", 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ko/width-640/by-id",
            116, "native-input/116-input.settings-native-copy-ko-width-640-by-id", "ko", 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ko/width-640/favorites",
            117, "native-input/117-input.settings-native-copy-ko-width-640-favorites", "ko", 640d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ru/width-320/search",
            118, "native-input/118-input.settings-native-copy-ru-width-320-search", "ru", 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ru/width-320/by-id",
            119, "native-input/119-input.settings-native-copy-ru-width-320-by-id", "ru", 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ru/width-320/favorites",
            120, "native-input/120-input.settings-native-copy-ru-width-320-favorites", "ru", 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ru/width-400/search",
            121, "native-input/121-input.settings-native-copy-ru-width-400-search", "ru", 400d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ru/width-400/by-id",
            122, "native-input/122-input.settings-native-copy-ru-width-400-by-id", "ru", 400d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ru/width-400/favorites",
            123, "native-input/123-input.settings-native-copy-ru-width-400-favorites", "ru", 400d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ru/width-480/search",
            124, "native-input/124-input.settings-native-copy-ru-width-480-search", "ru", 480d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ru/width-480/by-id",
            125, "native-input/125-input.settings-native-copy-ru-width-480-by-id", "ru", 480d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ru/width-480/favorites",
            126, "native-input/126-input.settings-native-copy-ru-width-480-favorites", "ru", 480d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ru/width-640/search",
            127, "native-input/127-input.settings-native-copy-ru-width-640-search", "ru", 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ru/width-640/by-id",
            128, "native-input/128-input.settings-native-copy-ru-width-640-by-id", "ru", 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/ru/width-640/favorites",
            129, "native-input/129-input.settings-native-copy-ru-width-640-favorites", "ru", 640d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/tr/width-320/search",
            130, "native-input/130-input.settings-native-copy-tr-width-320-search", "tr", 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/tr/width-320/by-id",
            131, "native-input/131-input.settings-native-copy-tr-width-320-by-id", "tr", 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/tr/width-320/favorites",
            132, "native-input/132-input.settings-native-copy-tr-width-320-favorites", "tr", 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/tr/width-400/search",
            133, "native-input/133-input.settings-native-copy-tr-width-400-search", "tr", 400d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/tr/width-400/by-id",
            134, "native-input/134-input.settings-native-copy-tr-width-400-by-id", "tr", 400d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/tr/width-400/favorites",
            135, "native-input/135-input.settings-native-copy-tr-width-400-favorites", "tr", 400d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/tr/width-480/search",
            136, "native-input/136-input.settings-native-copy-tr-width-480-search", "tr", 480d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/tr/width-480/by-id",
            137, "native-input/137-input.settings-native-copy-tr-width-480-by-id", "tr", 480d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/tr/width-480/favorites",
            138, "native-input/138-input.settings-native-copy-tr-width-480-favorites", "tr", 480d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/tr/width-640/search",
            139, "native-input/139-input.settings-native-copy-tr-width-640-search", "tr", 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/tr/width-640/by-id",
            140, "native-input/140-input.settings-native-copy-tr-width-640-by-id", "tr", 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "input.settings/native/copy/tr/width-640/favorites",
            141, "native-input/141-input.settings-native-copy-tr-width-640-favorites", "tr", 640d, VoiceBrowseMode.Favorites);
    }

    [TestCaseSource(nameof(VoiceTitlesExposeCopyOnlyActionsRows))]
    [Order(58)]
    public Task VoiceTitlesExposeCopyOnlyActions(string language, double width, VoiceBrowseMode mode)
        => RunCopyAsync(language, width, mode);

    private static Task RunCopyAsync(string language, double width, VoiceBrowseMode mode) => StaTestHost.RunOnStaThreadAsync(() =>
    {
        var owner = new HeldFavoritePageFixtureOwner();
        return owner.RunAsync(() =>
        {
            var directory = TestAssertions.CreateTempDirectory();
            owner.DirectoryPath = directory;
            SettingsInputInteractionSuite.VoiceTitlesExposeCopyOnlyActions(language, width, mode, directory, owner);
            return Task.CompletedTask;
        });
    });
}
