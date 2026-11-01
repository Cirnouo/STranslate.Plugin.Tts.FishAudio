using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(178)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorDark640OverlaysCases
{
    public static IEnumerable<TestCaseData> OverlaysRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/dark/width-640/overlays",
            178, "native-input/178-input.cursor-plugin-dark-width-640-overlays", 640d, true);
    }

    [TestCaseSource(nameof(OverlaysRetainsOriginalOwnedCallbackRows))]
    [Order(178)]
    public Task OverlaysRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveOverlaysAsync(width, dark);
}
