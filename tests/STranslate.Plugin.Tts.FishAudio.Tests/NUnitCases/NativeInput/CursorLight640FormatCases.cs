using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(161)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorLight640FormatCases
{
    public static IEnumerable<TestCaseData> FormatRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/light/width-640/format",
            161, "native-input/161-input.cursor-plugin-light-width-640-format", 640d, false);
    }

    [TestCaseSource(nameof(FormatRetainsOriginalOwnedCallbackRows))]
    [Order(161)]
    public Task FormatRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveFormatAsync(width, dark);
}
