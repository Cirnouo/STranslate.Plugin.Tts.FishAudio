using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(170)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorDark320FavoritesCases
{
    public static IEnumerable<TestCaseData> FavoritesRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/dark/width-320/favorites",
            170, "native-input/170-input.cursor-plugin-dark-width-320-favorites", 320d, true);
    }

    [TestCaseSource(nameof(FavoritesRetainsOriginalOwnedCallbackRows))]
    [Order(170)]
    public Task FavoritesRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveFavoritesAsync(width, dark);
}
