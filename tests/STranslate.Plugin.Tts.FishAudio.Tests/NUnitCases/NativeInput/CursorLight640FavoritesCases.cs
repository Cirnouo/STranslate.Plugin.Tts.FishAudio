using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(164)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorLight640FavoritesCases
{
    public static IEnumerable<TestCaseData> FavoritesRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/light/width-640/favorites",
            164, "native-input/164-input.cursor-plugin-light-width-640-favorites", 640d, false);
    }

    [TestCaseSource(nameof(FavoritesRetainsOriginalOwnedCallbackRows))]
    [Order(164)]
    public Task FavoritesRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveFavoritesAsync(width, dark);
}
