using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(157)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorLight320ByIdCases
{
    public static IEnumerable<TestCaseData> ByIdRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/light/width-320/by-id",
            157, "native-input/157-input.cursor-plugin-light-width-320-by-id", 320d, false);
    }

    [TestCaseSource(nameof(ByIdRetainsOriginalOwnedCallbackRows))]
    [Order(157)]
    public Task ByIdRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveByIdAsync(width, dark);
}
