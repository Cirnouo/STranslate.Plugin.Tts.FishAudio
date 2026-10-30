using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(166)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorLight640OverlaysCases
{
    public static IEnumerable<TestCaseData> OverlaysRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/light/width-640/overlays",
            166, "native-input/166-input.cursor-plugin-light-width-640-overlays", 640d, false);
    }

    [TestCaseSource(nameof(OverlaysRetainsOriginalOwnedCallbackRows))]
    [Order(166)]
    public Task OverlaysRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveOverlaysAsync(width, dark);
}
