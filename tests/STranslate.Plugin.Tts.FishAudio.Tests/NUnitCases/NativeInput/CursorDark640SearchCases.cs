using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(174)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorDark640SearchCases
{
    public static IEnumerable<TestCaseData> SearchRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/dark/width-640/search",
            174, "native-input/174-input.cursor-plugin-dark-width-640-search", 640d, true);
    }

    [TestCaseSource(nameof(SearchRetainsOriginalOwnedCallbackRows))]
    [Order(174)]
    public Task SearchRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveSearchAsync(width, dark);
}
