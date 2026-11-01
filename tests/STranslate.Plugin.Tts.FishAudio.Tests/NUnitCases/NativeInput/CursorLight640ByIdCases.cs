using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(163)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorLight640ByIdCases
{
    public static IEnumerable<TestCaseData> ByIdRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/light/width-640/by-id",
            163, "native-input/163-input.cursor-plugin-light-width-640-by-id", 640d, false);
    }

    [TestCaseSource(nameof(ByIdRetainsOriginalOwnedCallbackRows))]
    [Order(163)]
    public Task ByIdRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveByIdAsync(width, dark);
}
