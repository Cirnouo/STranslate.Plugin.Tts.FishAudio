using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(175)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorDark640ByIdCases
{
    public static IEnumerable<TestCaseData> ByIdRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/dark/width-640/by-id",
            175, "native-input/175-input.cursor-plugin-dark-width-640-by-id", 640d, true);
    }

    [TestCaseSource(nameof(ByIdRetainsOriginalOwnedCallbackRows))]
    [Order(175)]
    public Task ByIdRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveByIdAsync(width, dark);
}
