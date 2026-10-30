using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(172)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorDark320OverlaysCases
{
    public static IEnumerable<TestCaseData> OverlaysRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/dark/width-320/overlays",
            172, "native-input/172-input.cursor-plugin-dark-width-320-overlays", 320d, true);
    }

    [TestCaseSource(nameof(OverlaysRetainsOriginalOwnedCallbackRows))]
    [Order(172)]
    public Task OverlaysRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveOverlaysAsync(width, dark);
}
