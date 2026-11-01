using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCases.NativeCards;

// Source proposal. Retain the complete ordered profile before transferring ownership.
[TestFixture]
[NonParallelizable]
[Order(795)]
[Property("FishAudioExecutionProfile", "native-cards")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class AvatarPreviewCases
{
    public static IEnumerable<TestCaseData> ContinuousLifecycleRows()
    {
        yield return NativeTestCaseSource.Row(
            "cards.avatar/light/dpi-1/width-320/search",
            795, "native-cards/795-cards.avatar-light-dpi-1-width-320-search", VoiceBrowseMode.Search, false, 1d, 320d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/light/dpi-1/width-320/by-id",
            796, "native-cards/796-cards.avatar-light-dpi-1-width-320-by-id", VoiceBrowseMode.ById, false, 1d, 320d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/light/dpi-1/width-320/favorites",
            797, "native-cards/797-cards.avatar-light-dpi-1-width-320-favorites", VoiceBrowseMode.Favorites, false, 1d, 320d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/light/dpi-1/width-640/search",
            798, "native-cards/798-cards.avatar-light-dpi-1-width-640-search", VoiceBrowseMode.Search, false, 1d, 640d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/light/dpi-1/width-640/by-id",
            799, "native-cards/799-cards.avatar-light-dpi-1-width-640-by-id", VoiceBrowseMode.ById, false, 1d, 640d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/light/dpi-1/width-640/favorites",
            800, "native-cards/800-cards.avatar-light-dpi-1-width-640-favorites", VoiceBrowseMode.Favorites, false, 1d, 640d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/light/dpi-1-25/width-320/search",
            801, "native-cards/801-cards.avatar-light-dpi-1-25-width-320-search", VoiceBrowseMode.Search, false, 1.25, 320d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/light/dpi-1-25/width-320/by-id",
            802, "native-cards/802-cards.avatar-light-dpi-1-25-width-320-by-id", VoiceBrowseMode.ById, false, 1.25, 320d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/light/dpi-1-25/width-320/favorites",
            803, "native-cards/803-cards.avatar-light-dpi-1-25-width-320-favorites", VoiceBrowseMode.Favorites, false, 1.25, 320d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/light/dpi-1-25/width-640/search",
            804, "native-cards/804-cards.avatar-light-dpi-1-25-width-640-search", VoiceBrowseMode.Search, false, 1.25, 640d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/light/dpi-1-25/width-640/by-id",
            805, "native-cards/805-cards.avatar-light-dpi-1-25-width-640-by-id", VoiceBrowseMode.ById, false, 1.25, 640d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/light/dpi-1-25/width-640/favorites",
            806, "native-cards/806-cards.avatar-light-dpi-1-25-width-640-favorites", VoiceBrowseMode.Favorites, false, 1.25, 640d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/light/dpi-1-5/width-320/search",
            807, "native-cards/807-cards.avatar-light-dpi-1-5-width-320-search", VoiceBrowseMode.Search, false, 1.5, 320d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/light/dpi-1-5/width-320/by-id",
            808, "native-cards/808-cards.avatar-light-dpi-1-5-width-320-by-id", VoiceBrowseMode.ById, false, 1.5, 320d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/light/dpi-1-5/width-320/favorites",
            809, "native-cards/809-cards.avatar-light-dpi-1-5-width-320-favorites", VoiceBrowseMode.Favorites, false, 1.5, 320d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/light/dpi-1-5/width-640/search",
            810, "native-cards/810-cards.avatar-light-dpi-1-5-width-640-search", VoiceBrowseMode.Search, false, 1.5, 640d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/light/dpi-1-5/width-640/by-id",
            811, "native-cards/811-cards.avatar-light-dpi-1-5-width-640-by-id", VoiceBrowseMode.ById, false, 1.5, 640d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/light/dpi-1-5/width-640/favorites",
            812, "native-cards/812-cards.avatar-light-dpi-1-5-width-640-favorites", VoiceBrowseMode.Favorites, false, 1.5, 640d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/dark/dpi-1/width-320/search",
            813, "native-cards/813-cards.avatar-dark-dpi-1-width-320-search", VoiceBrowseMode.Search, true, 1d, 320d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/dark/dpi-1/width-320/by-id",
            814, "native-cards/814-cards.avatar-dark-dpi-1-width-320-by-id", VoiceBrowseMode.ById, true, 1d, 320d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/dark/dpi-1/width-320/favorites",
            815, "native-cards/815-cards.avatar-dark-dpi-1-width-320-favorites", VoiceBrowseMode.Favorites, true, 1d, 320d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/dark/dpi-1/width-640/search",
            816, "native-cards/816-cards.avatar-dark-dpi-1-width-640-search", VoiceBrowseMode.Search, true, 1d, 640d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/dark/dpi-1/width-640/by-id",
            817, "native-cards/817-cards.avatar-dark-dpi-1-width-640-by-id", VoiceBrowseMode.ById, true, 1d, 640d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/dark/dpi-1/width-640/favorites",
            818, "native-cards/818-cards.avatar-dark-dpi-1-width-640-favorites", VoiceBrowseMode.Favorites, true, 1d, 640d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/dark/dpi-1-25/width-320/search",
            819, "native-cards/819-cards.avatar-dark-dpi-1-25-width-320-search", VoiceBrowseMode.Search, true, 1.25, 320d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/dark/dpi-1-25/width-320/by-id",
            820, "native-cards/820-cards.avatar-dark-dpi-1-25-width-320-by-id", VoiceBrowseMode.ById, true, 1.25, 320d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/dark/dpi-1-25/width-320/favorites",
            821, "native-cards/821-cards.avatar-dark-dpi-1-25-width-320-favorites", VoiceBrowseMode.Favorites, true, 1.25, 320d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/dark/dpi-1-25/width-640/search",
            822, "native-cards/822-cards.avatar-dark-dpi-1-25-width-640-search", VoiceBrowseMode.Search, true, 1.25, 640d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/dark/dpi-1-25/width-640/by-id",
            823, "native-cards/823-cards.avatar-dark-dpi-1-25-width-640-by-id", VoiceBrowseMode.ById, true, 1.25, 640d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/dark/dpi-1-25/width-640/favorites",
            824, "native-cards/824-cards.avatar-dark-dpi-1-25-width-640-favorites", VoiceBrowseMode.Favorites, true, 1.25, 640d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/dark/dpi-1-5/width-320/search",
            825, "native-cards/825-cards.avatar-dark-dpi-1-5-width-320-search", VoiceBrowseMode.Search, true, 1.5, 320d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/dark/dpi-1-5/width-320/by-id",
            826, "native-cards/826-cards.avatar-dark-dpi-1-5-width-320-by-id", VoiceBrowseMode.ById, true, 1.5, 320d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/dark/dpi-1-5/width-320/favorites",
            827, "native-cards/827-cards.avatar-dark-dpi-1-5-width-320-favorites", VoiceBrowseMode.Favorites, true, 1.5, 320d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/dark/dpi-1-5/width-640/search",
            828, "native-cards/828-cards.avatar-dark-dpi-1-5-width-640-search", VoiceBrowseMode.Search, true, 1.5, 640d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/dark/dpi-1-5/width-640/by-id",
            829, "native-cards/829-cards.avatar-dark-dpi-1-5-width-640-by-id", VoiceBrowseMode.ById, true, 1.5, 640d);
        yield return NativeTestCaseSource.Row(
            "cards.avatar/dark/dpi-1-5/width-640/favorites",
            830, "native-cards/830-cards.avatar-dark-dpi-1-5-width-640-favorites", VoiceBrowseMode.Favorites, true, 1.5, 640d);
    }

    [TestCaseSource(nameof(ContinuousLifecycleRows))]
    [Order(795)]
    public Task ContinuousLifecycle(VoiceBrowseMode mode, bool dark, double dpi, double width) =>
        AvatarPreviewSuite.ObserveContextAsync(mode, dark, dpi, width);

}
