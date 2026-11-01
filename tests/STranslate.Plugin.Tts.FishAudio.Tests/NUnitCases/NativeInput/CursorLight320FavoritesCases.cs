using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(158)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorLight320FavoritesCases
{
    public static IEnumerable<TestCaseData> FavoritesRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/light/width-320/favorites",
            158, "native-input/158-input.cursor-plugin-light-width-320-favorites", 320d, false);
    }

    [TestCaseSource(nameof(FavoritesRetainsOriginalOwnedCallbackRows))]
    [Order(158)]
    public Task FavoritesRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveFavoritesAsync(width, dark);
}
