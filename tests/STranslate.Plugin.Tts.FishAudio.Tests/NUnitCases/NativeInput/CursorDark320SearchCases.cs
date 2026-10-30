using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(168)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorDark320SearchCases
{
    public static IEnumerable<TestCaseData> SearchRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/dark/width-320/search",
            168, "native-input/168-input.cursor-plugin-dark-width-320-search", 320d, true);
    }

    [TestCaseSource(nameof(SearchRetainsOriginalOwnedCallbackRows))]
    [Order(168)]
    public Task SearchRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveSearchAsync(width, dark);
}
