using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(177)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorDark640GeneralCases
{
    public static IEnumerable<TestCaseData> GeneralRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/dark/width-640/general",
            177, "native-input/177-input.cursor-plugin-dark-width-640-general", 640d, true);
    }

    [TestCaseSource(nameof(GeneralRetainsOriginalOwnedCallbackRows))]
    [Order(177)]
    public Task GeneralRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveGeneralAsync(width, dark);
}
