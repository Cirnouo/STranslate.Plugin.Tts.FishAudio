using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(155)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorLight320FormatCases
{
    public static IEnumerable<TestCaseData> FormatRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/light/width-320/format",
            155, "native-input/155-input.cursor-plugin-light-width-320-format", 320d, false);
    }

    [TestCaseSource(nameof(FormatRetainsOriginalOwnedCallbackRows))]
    [Order(155)]
    public Task FormatRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveFormatAsync(width, dark);
}
