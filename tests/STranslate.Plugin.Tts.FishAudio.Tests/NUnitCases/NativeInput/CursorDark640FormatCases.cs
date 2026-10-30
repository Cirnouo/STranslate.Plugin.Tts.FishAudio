using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(173)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorDark640FormatCases
{
    public static IEnumerable<TestCaseData> FormatRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/dark/width-640/format",
            173, "native-input/173-input.cursor-plugin-dark-width-640-format", 640d, true);
    }

    [TestCaseSource(nameof(FormatRetainsOriginalOwnedCallbackRows))]
    [Order(173)]
    public Task FormatRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveFormatAsync(width, dark);
}
