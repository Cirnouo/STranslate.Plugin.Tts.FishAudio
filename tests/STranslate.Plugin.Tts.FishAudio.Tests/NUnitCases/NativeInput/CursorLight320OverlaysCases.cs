using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(160)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorLight320OverlaysCases
{
    public static IEnumerable<TestCaseData> OverlaysRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/light/width-320/overlays",
            160, "native-input/160-input.cursor-plugin-light-width-320-overlays", 320d, false);
    }

    [TestCaseSource(nameof(OverlaysRetainsOriginalOwnedCallbackRows))]
    [Order(160)]
    public Task OverlaysRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveOverlaysAsync(width, dark);
}
