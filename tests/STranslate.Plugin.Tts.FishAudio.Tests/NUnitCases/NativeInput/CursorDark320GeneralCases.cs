using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(171)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorDark320GeneralCases
{
    public static IEnumerable<TestCaseData> GeneralRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/dark/width-320/general",
            171, "native-input/171-input.cursor-plugin-dark-width-320-general", 320d, true);
    }

    [TestCaseSource(nameof(GeneralRetainsOriginalOwnedCallbackRows))]
    [Order(171)]
    public Task GeneralRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveGeneralAsync(width, dark);
}
