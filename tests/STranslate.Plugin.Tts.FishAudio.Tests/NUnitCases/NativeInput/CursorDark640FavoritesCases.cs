using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(176)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorDark640FavoritesCases
{
    public static IEnumerable<TestCaseData> FavoritesRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/dark/width-640/favorites",
            176, "native-input/176-input.cursor-plugin-dark-width-640-favorites", 640d, true);
    }

    [TestCaseSource(nameof(FavoritesRetainsOriginalOwnedCallbackRows))]
    [Order(176)]
    public Task FavoritesRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveFavoritesAsync(width, dark);
}
