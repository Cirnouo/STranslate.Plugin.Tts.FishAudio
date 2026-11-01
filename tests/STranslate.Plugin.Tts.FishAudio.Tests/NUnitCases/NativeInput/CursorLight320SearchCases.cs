using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(156)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorLight320SearchCases
{
    public static IEnumerable<TestCaseData> SearchRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/light/width-320/search",
            156, "native-input/156-input.cursor-plugin-light-width-320-search", 320d, false);
    }

    [TestCaseSource(nameof(SearchRetainsOriginalOwnedCallbackRows))]
    [Order(156)]
    public Task SearchRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveSearchAsync(width, dark);
}
