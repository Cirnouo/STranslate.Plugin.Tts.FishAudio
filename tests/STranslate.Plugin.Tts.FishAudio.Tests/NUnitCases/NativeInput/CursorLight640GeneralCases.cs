using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(165)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorLight640GeneralCases
{
    public static IEnumerable<TestCaseData> GeneralRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/plugin/light/width-640/general",
            165, "native-input/165-input.cursor-plugin-light-width-640-general", 640d, false);
    }

    [TestCaseSource(nameof(GeneralRetainsOriginalOwnedCallbackRows))]
    [Order(165)]
    public Task GeneralRetainsOriginalOwnedCallback(double width, bool dark)
        => ControlCursorSuite.ObserveGeneralAsync(width, dark);
}
