using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(167)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorDark320FormatCases
{
    public static IEnumerable<TestCaseData> FormatRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/dark/width-320/format",
            167, "native-input/167-input.cursor-plugin-dark-width-320-format", 320d, true);
    }

    [TestCaseSource(nameof(FormatRetainsOriginalOwnedCallbackRows))]
    [Order(167)]
    public Task FormatRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveFormatAsync(width, dark);
}
