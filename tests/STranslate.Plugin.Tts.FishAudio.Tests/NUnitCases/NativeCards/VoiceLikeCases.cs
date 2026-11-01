using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCases.NativeCards;

// Source proposal. Retain the complete ordered profile before transferring ownership.
[TestFixture]
[NonParallelizable]
[Order(708)]
[Property("FishAudioExecutionProfile", "native-cards")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class VoiceLikeCases
{
    public static IEnumerable<TestCaseData> SharedCardTemplateIsCompiledRows()
    {
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/shared-card-template-is-compiled",
            708, "native-cards/708-cards.like-presentation-shared-card-template-is-compiled");
    }

    [TestCaseSource(nameof(SharedCardTemplateIsCompiledRows))]
    [Order(708)]
    public Task SharedCardTemplateIsCompiled() =>
        VoiceLikeSuite.SharedCardTemplateIsCompiledAsync();

    public static IEnumerable<TestCaseData> TabPresentationAndLiveLocalesRows()
    {
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/tab-presentation-and-live-locales",
            709, "native-cards/709-cards.like-presentation-tab-presentation-and-live-locales");
    }

    [TestCaseSource(nameof(TabPresentationAndLiveLocalesRows))]
    [Order(709)]
    public Task TabPresentationAndLiveLocales() =>
        StaTestHost.RunOnStaThreadAsync(SettingsLayoutSuite.FavoriteVoicesTabMatchesPeerTabs);

    public static IEnumerable<TestCaseData> SelectedHeartSharedStateRows()
    {
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/selected-heart-shared-state",
            710, "native-cards/710-cards.like-presentation-selected-heart-shared-state");
    }

    [TestCaseSource(nameof(SelectedHeartSharedStateRows))]
    [Order(710)]
    public Task SelectedHeartSharedState() =>
        VoiceLikeSuite.ObserveSelectedHeartSharedStateAsync();

    public static IEnumerable<TestCaseData> LocalizedLikeStateRows()
    {
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/en/light/width-320/search",
            711, "native-cards/711-cards.like-presentation-en-light-width-320-search", "en", false, 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/en/light/width-320/by-id",
            712, "native-cards/712-cards.like-presentation-en-light-width-320-by-id", "en", false, 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/en/light/width-320/favorites",
            713, "native-cards/713-cards.like-presentation-en-light-width-320-favorites", "en", false, 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/en/light/width-640/search",
            714, "native-cards/714-cards.like-presentation-en-light-width-640-search", "en", false, 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/en/light/width-640/by-id",
            715, "native-cards/715-cards.like-presentation-en-light-width-640-by-id", "en", false, 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/en/light/width-640/favorites",
            716, "native-cards/716-cards.like-presentation-en-light-width-640-favorites", "en", false, 640d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/en/dark/width-320/search",
            717, "native-cards/717-cards.like-presentation-en-dark-width-320-search", "en", true, 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/en/dark/width-320/by-id",
            718, "native-cards/718-cards.like-presentation-en-dark-width-320-by-id", "en", true, 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/en/dark/width-320/favorites",
            719, "native-cards/719-cards.like-presentation-en-dark-width-320-favorites", "en", true, 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/en/dark/width-640/search",
            720, "native-cards/720-cards.like-presentation-en-dark-width-640-search", "en", true, 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/en/dark/width-640/by-id",
            721, "native-cards/721-cards.like-presentation-en-dark-width-640-by-id", "en", true, 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/en/dark/width-640/favorites",
            722, "native-cards/722-cards.like-presentation-en-dark-width-640-favorites", "en", true, 640d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-cn/light/width-320/search",
            723, "native-cards/723-cards.like-presentation-zh-cn-light-width-320-search", "zh-cn", false, 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-cn/light/width-320/by-id",
            724, "native-cards/724-cards.like-presentation-zh-cn-light-width-320-by-id", "zh-cn", false, 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-cn/light/width-320/favorites",
            725, "native-cards/725-cards.like-presentation-zh-cn-light-width-320-favorites", "zh-cn", false, 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-cn/light/width-640/search",
            726, "native-cards/726-cards.like-presentation-zh-cn-light-width-640-search", "zh-cn", false, 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-cn/light/width-640/by-id",
            727, "native-cards/727-cards.like-presentation-zh-cn-light-width-640-by-id", "zh-cn", false, 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-cn/light/width-640/favorites",
            728, "native-cards/728-cards.like-presentation-zh-cn-light-width-640-favorites", "zh-cn", false, 640d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-cn/dark/width-320/search",
            729, "native-cards/729-cards.like-presentation-zh-cn-dark-width-320-search", "zh-cn", true, 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-cn/dark/width-320/by-id",
            730, "native-cards/730-cards.like-presentation-zh-cn-dark-width-320-by-id", "zh-cn", true, 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-cn/dark/width-320/favorites",
            731, "native-cards/731-cards.like-presentation-zh-cn-dark-width-320-favorites", "zh-cn", true, 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-cn/dark/width-640/search",
            732, "native-cards/732-cards.like-presentation-zh-cn-dark-width-640-search", "zh-cn", true, 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-cn/dark/width-640/by-id",
            733, "native-cards/733-cards.like-presentation-zh-cn-dark-width-640-by-id", "zh-cn", true, 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-cn/dark/width-640/favorites",
            734, "native-cards/734-cards.like-presentation-zh-cn-dark-width-640-favorites", "zh-cn", true, 640d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-tw/light/width-320/search",
            735, "native-cards/735-cards.like-presentation-zh-tw-light-width-320-search", "zh-tw", false, 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-tw/light/width-320/by-id",
            736, "native-cards/736-cards.like-presentation-zh-tw-light-width-320-by-id", "zh-tw", false, 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-tw/light/width-320/favorites",
            737, "native-cards/737-cards.like-presentation-zh-tw-light-width-320-favorites", "zh-tw", false, 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-tw/light/width-640/search",
            738, "native-cards/738-cards.like-presentation-zh-tw-light-width-640-search", "zh-tw", false, 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-tw/light/width-640/by-id",
            739, "native-cards/739-cards.like-presentation-zh-tw-light-width-640-by-id", "zh-tw", false, 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-tw/light/width-640/favorites",
            740, "native-cards/740-cards.like-presentation-zh-tw-light-width-640-favorites", "zh-tw", false, 640d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-tw/dark/width-320/search",
            741, "native-cards/741-cards.like-presentation-zh-tw-dark-width-320-search", "zh-tw", true, 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-tw/dark/width-320/by-id",
            742, "native-cards/742-cards.like-presentation-zh-tw-dark-width-320-by-id", "zh-tw", true, 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-tw/dark/width-320/favorites",
            743, "native-cards/743-cards.like-presentation-zh-tw-dark-width-320-favorites", "zh-tw", true, 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-tw/dark/width-640/search",
            744, "native-cards/744-cards.like-presentation-zh-tw-dark-width-640-search", "zh-tw", true, 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-tw/dark/width-640/by-id",
            745, "native-cards/745-cards.like-presentation-zh-tw-dark-width-640-by-id", "zh-tw", true, 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/zh-tw/dark/width-640/favorites",
            746, "native-cards/746-cards.like-presentation-zh-tw-dark-width-640-favorites", "zh-tw", true, 640d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ja/light/width-320/search",
            747, "native-cards/747-cards.like-presentation-ja-light-width-320-search", "ja", false, 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ja/light/width-320/by-id",
            748, "native-cards/748-cards.like-presentation-ja-light-width-320-by-id", "ja", false, 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ja/light/width-320/favorites",
            749, "native-cards/749-cards.like-presentation-ja-light-width-320-favorites", "ja", false, 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ja/light/width-640/search",
            750, "native-cards/750-cards.like-presentation-ja-light-width-640-search", "ja", false, 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ja/light/width-640/by-id",
            751, "native-cards/751-cards.like-presentation-ja-light-width-640-by-id", "ja", false, 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ja/light/width-640/favorites",
            752, "native-cards/752-cards.like-presentation-ja-light-width-640-favorites", "ja", false, 640d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ja/dark/width-320/search",
            753, "native-cards/753-cards.like-presentation-ja-dark-width-320-search", "ja", true, 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ja/dark/width-320/by-id",
            754, "native-cards/754-cards.like-presentation-ja-dark-width-320-by-id", "ja", true, 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ja/dark/width-320/favorites",
            755, "native-cards/755-cards.like-presentation-ja-dark-width-320-favorites", "ja", true, 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ja/dark/width-640/search",
            756, "native-cards/756-cards.like-presentation-ja-dark-width-640-search", "ja", true, 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ja/dark/width-640/by-id",
            757, "native-cards/757-cards.like-presentation-ja-dark-width-640-by-id", "ja", true, 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ja/dark/width-640/favorites",
            758, "native-cards/758-cards.like-presentation-ja-dark-width-640-favorites", "ja", true, 640d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ko/light/width-320/search",
            759, "native-cards/759-cards.like-presentation-ko-light-width-320-search", "ko", false, 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ko/light/width-320/by-id",
            760, "native-cards/760-cards.like-presentation-ko-light-width-320-by-id", "ko", false, 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ko/light/width-320/favorites",
            761, "native-cards/761-cards.like-presentation-ko-light-width-320-favorites", "ko", false, 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ko/light/width-640/search",
            762, "native-cards/762-cards.like-presentation-ko-light-width-640-search", "ko", false, 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ko/light/width-640/by-id",
            763, "native-cards/763-cards.like-presentation-ko-light-width-640-by-id", "ko", false, 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ko/light/width-640/favorites",
            764, "native-cards/764-cards.like-presentation-ko-light-width-640-favorites", "ko", false, 640d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ko/dark/width-320/search",
            765, "native-cards/765-cards.like-presentation-ko-dark-width-320-search", "ko", true, 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ko/dark/width-320/by-id",
            766, "native-cards/766-cards.like-presentation-ko-dark-width-320-by-id", "ko", true, 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ko/dark/width-320/favorites",
            767, "native-cards/767-cards.like-presentation-ko-dark-width-320-favorites", "ko", true, 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ko/dark/width-640/search",
            768, "native-cards/768-cards.like-presentation-ko-dark-width-640-search", "ko", true, 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ko/dark/width-640/by-id",
            769, "native-cards/769-cards.like-presentation-ko-dark-width-640-by-id", "ko", true, 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ko/dark/width-640/favorites",
            770, "native-cards/770-cards.like-presentation-ko-dark-width-640-favorites", "ko", true, 640d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ru/light/width-320/search",
            771, "native-cards/771-cards.like-presentation-ru-light-width-320-search", "ru", false, 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ru/light/width-320/by-id",
            772, "native-cards/772-cards.like-presentation-ru-light-width-320-by-id", "ru", false, 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ru/light/width-320/favorites",
            773, "native-cards/773-cards.like-presentation-ru-light-width-320-favorites", "ru", false, 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ru/light/width-640/search",
            774, "native-cards/774-cards.like-presentation-ru-light-width-640-search", "ru", false, 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ru/light/width-640/by-id",
            775, "native-cards/775-cards.like-presentation-ru-light-width-640-by-id", "ru", false, 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ru/light/width-640/favorites",
            776, "native-cards/776-cards.like-presentation-ru-light-width-640-favorites", "ru", false, 640d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ru/dark/width-320/search",
            777, "native-cards/777-cards.like-presentation-ru-dark-width-320-search", "ru", true, 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ru/dark/width-320/by-id",
            778, "native-cards/778-cards.like-presentation-ru-dark-width-320-by-id", "ru", true, 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ru/dark/width-320/favorites",
            779, "native-cards/779-cards.like-presentation-ru-dark-width-320-favorites", "ru", true, 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ru/dark/width-640/search",
            780, "native-cards/780-cards.like-presentation-ru-dark-width-640-search", "ru", true, 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ru/dark/width-640/by-id",
            781, "native-cards/781-cards.like-presentation-ru-dark-width-640-by-id", "ru", true, 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/ru/dark/width-640/favorites",
            782, "native-cards/782-cards.like-presentation-ru-dark-width-640-favorites", "ru", true, 640d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/tr/light/width-320/search",
            783, "native-cards/783-cards.like-presentation-tr-light-width-320-search", "tr", false, 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/tr/light/width-320/by-id",
            784, "native-cards/784-cards.like-presentation-tr-light-width-320-by-id", "tr", false, 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/tr/light/width-320/favorites",
            785, "native-cards/785-cards.like-presentation-tr-light-width-320-favorites", "tr", false, 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/tr/light/width-640/search",
            786, "native-cards/786-cards.like-presentation-tr-light-width-640-search", "tr", false, 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/tr/light/width-640/by-id",
            787, "native-cards/787-cards.like-presentation-tr-light-width-640-by-id", "tr", false, 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/tr/light/width-640/favorites",
            788, "native-cards/788-cards.like-presentation-tr-light-width-640-favorites", "tr", false, 640d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/tr/dark/width-320/search",
            789, "native-cards/789-cards.like-presentation-tr-dark-width-320-search", "tr", true, 320d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/tr/dark/width-320/by-id",
            790, "native-cards/790-cards.like-presentation-tr-dark-width-320-by-id", "tr", true, 320d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/tr/dark/width-320/favorites",
            791, "native-cards/791-cards.like-presentation-tr-dark-width-320-favorites", "tr", true, 320d, VoiceBrowseMode.Favorites);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/tr/dark/width-640/search",
            792, "native-cards/792-cards.like-presentation-tr-dark-width-640-search", "tr", true, 640d, VoiceBrowseMode.Search);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/tr/dark/width-640/by-id",
            793, "native-cards/793-cards.like-presentation-tr-dark-width-640-by-id", "tr", true, 640d, VoiceBrowseMode.ById);
        yield return NativeTestCaseSource.Row(
            "cards.like/presentation/tr/dark/width-640/favorites",
            794, "native-cards/794-cards.like-presentation-tr-dark-width-640-favorites", "tr", true, 640d, VoiceBrowseMode.Favorites);
    }

    [TestCaseSource(nameof(LocalizedLikeStateRows))]
    [Order(711)]
    public Task LocalizedLikeState(string locale, bool dark, double width, VoiceBrowseMode mode) =>
        VoiceLikeSuite.ObserveContextAsync(locale, dark, width, mode);

}
