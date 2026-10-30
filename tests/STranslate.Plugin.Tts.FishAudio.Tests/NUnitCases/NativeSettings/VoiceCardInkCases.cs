using NUnit.Framework;
using System.Windows.Media;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCases.NativeSettings;

// Source candidate only. The complete profile retains its original cold-first-use order.
[TestFixture]
[NonParallelizable]
[Order(52)]
[Property("FishAudioExecutionProfile", "native-settings")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class VoiceCardInkCases
{
    public static IEnumerable<TestCaseData> RenderedCardsRows()
    {
        yield return NativeTestCaseSource.Row(
            "cards.ink/captured-display125-search",
            52, "native-settings/052-captured-display125-search", 1.25, TextFormattingMode.Display, VoiceBrowseMode.Search, true);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-1/ideal/search",
            53, "native-settings/053-dpi-1-ideal-search", 1d, TextFormattingMode.Ideal, VoiceBrowseMode.Search, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-1/ideal/by-id",
            54, "native-settings/054-dpi-1-ideal-by-id", 1d, TextFormattingMode.Ideal, VoiceBrowseMode.ById, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-1/ideal/favorites",
            55, "native-settings/055-dpi-1-ideal-favorites", 1d, TextFormattingMode.Ideal, VoiceBrowseMode.Favorites, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-1/display/search",
            56, "native-settings/056-dpi-1-display-search", 1d, TextFormattingMode.Display, VoiceBrowseMode.Search, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-1/display/by-id",
            57, "native-settings/057-dpi-1-display-by-id", 1d, TextFormattingMode.Display, VoiceBrowseMode.ById, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-1/display/favorites",
            58, "native-settings/058-dpi-1-display-favorites", 1d, TextFormattingMode.Display, VoiceBrowseMode.Favorites, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-1-25/ideal/search",
            59, "native-settings/059-dpi-1-25-ideal-search", 1.25, TextFormattingMode.Ideal, VoiceBrowseMode.Search, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-1-25/ideal/by-id",
            60, "native-settings/060-dpi-1-25-ideal-by-id", 1.25, TextFormattingMode.Ideal, VoiceBrowseMode.ById, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-1-25/ideal/favorites",
            61, "native-settings/061-dpi-1-25-ideal-favorites", 1.25, TextFormattingMode.Ideal, VoiceBrowseMode.Favorites, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-1-25/display/search",
            62, "native-settings/062-dpi-1-25-display-search", 1.25, TextFormattingMode.Display, VoiceBrowseMode.Search, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-1-25/display/by-id",
            63, "native-settings/063-dpi-1-25-display-by-id", 1.25, TextFormattingMode.Display, VoiceBrowseMode.ById, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-1-25/display/favorites",
            64, "native-settings/064-dpi-1-25-display-favorites", 1.25, TextFormattingMode.Display, VoiceBrowseMode.Favorites, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-1-5/ideal/search",
            65, "native-settings/065-dpi-1-5-ideal-search", 1.5, TextFormattingMode.Ideal, VoiceBrowseMode.Search, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-1-5/ideal/by-id",
            66, "native-settings/066-dpi-1-5-ideal-by-id", 1.5, TextFormattingMode.Ideal, VoiceBrowseMode.ById, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-1-5/ideal/favorites",
            67, "native-settings/067-dpi-1-5-ideal-favorites", 1.5, TextFormattingMode.Ideal, VoiceBrowseMode.Favorites, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-1-5/display/search",
            68, "native-settings/068-dpi-1-5-display-search", 1.5, TextFormattingMode.Display, VoiceBrowseMode.Search, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-1-5/display/by-id",
            69, "native-settings/069-dpi-1-5-display-by-id", 1.5, TextFormattingMode.Display, VoiceBrowseMode.ById, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-1-5/display/favorites",
            70, "native-settings/070-dpi-1-5-display-favorites", 1.5, TextFormattingMode.Display, VoiceBrowseMode.Favorites, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-2/ideal/search",
            71, "native-settings/071-dpi-2-ideal-search", 2d, TextFormattingMode.Ideal, VoiceBrowseMode.Search, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-2/ideal/by-id",
            72, "native-settings/072-dpi-2-ideal-by-id", 2d, TextFormattingMode.Ideal, VoiceBrowseMode.ById, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-2/ideal/favorites",
            73, "native-settings/073-dpi-2-ideal-favorites", 2d, TextFormattingMode.Ideal, VoiceBrowseMode.Favorites, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-2/display/search",
            74, "native-settings/074-dpi-2-display-search", 2d, TextFormattingMode.Display, VoiceBrowseMode.Search, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-2/display/by-id",
            75, "native-settings/075-dpi-2-display-by-id", 2d, TextFormattingMode.Display, VoiceBrowseMode.ById, false);
        yield return NativeTestCaseSource.Row(
            "cards.ink/dpi-2/display/favorites",
            76, "native-settings/076-dpi-2-display-favorites", 2d, TextFormattingMode.Display, VoiceBrowseMode.Favorites, false);
    }

    [TestCaseSource(nameof(RenderedCardsRows))]
    [Order(52)]
    public Task CheckRenderedCards(double dpi, TextFormattingMode format, VoiceBrowseMode mode, bool capturedRegression)
        => StaTestHost.RunOnStaThreadAsync(() =>
            VoiceCardInkSuite.CheckRenderedCards(dpi, format, mode, capturedRegression, proofDirectory: null));
}
