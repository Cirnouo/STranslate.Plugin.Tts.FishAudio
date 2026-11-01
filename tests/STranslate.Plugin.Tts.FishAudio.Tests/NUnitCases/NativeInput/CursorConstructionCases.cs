using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(154)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorConstructionCases
{
    public static IEnumerable<TestCaseData> ConstructionRetainsOriginalOwnedCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/construction",
            154, "native-input/154-input.cursor-construction");
    }

    [TestCaseSource(nameof(ConstructionRetainsOriginalOwnedCallbackRows))]
    [Order(154)]
    public Task ConstructionRetainsOriginalOwnedCallback()
        => ControlCursorSuite.ObserveConstructionAsync();
}
