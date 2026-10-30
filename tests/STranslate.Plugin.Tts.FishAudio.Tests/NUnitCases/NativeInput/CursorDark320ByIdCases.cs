using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(169)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorDark320ByIdCases
{
    public static IEnumerable<TestCaseData> ByIdRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/dark/width-320/by-id",
            169, "native-input/169-input.cursor-plugin-dark-width-320-by-id", 320d, true);
    }

    [TestCaseSource(nameof(ByIdRetainsOriginalOwnedCallbackRows))]
    [Order(169)]
    public Task ByIdRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveByIdAsync(width, dark);
}
