using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(162)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorLight640SearchCases
{
    public static IEnumerable<TestCaseData> SearchRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/light/width-640/search",
            162, "native-input/162-input.cursor-plugin-light-width-640-search", 640d, false);
    }

    [TestCaseSource(nameof(SearchRetainsOriginalOwnedCallbackRows))]
    [Order(162)]
    public Task SearchRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveSearchAsync(width, dark);
}
