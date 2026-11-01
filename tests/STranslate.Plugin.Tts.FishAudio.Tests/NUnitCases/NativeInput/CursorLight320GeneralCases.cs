using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(159)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorLight320GeneralCases
{
    public static IEnumerable<TestCaseData> GeneralRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/light/width-320/general",
            159, "native-input/159-input.cursor-plugin-light-width-320-general", 320d, false);
    }

    [TestCaseSource(nameof(GeneralRetainsOriginalOwnedCallbackRows))]
    [Order(159)]
    public Task GeneralRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveGeneralAsync(width, dark);
}
