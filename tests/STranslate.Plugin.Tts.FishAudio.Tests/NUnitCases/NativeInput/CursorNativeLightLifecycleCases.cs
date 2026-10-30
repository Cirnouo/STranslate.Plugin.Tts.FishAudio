using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(183)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorNativeLightLifecycleCases
{
    public static IEnumerable<TestCaseData> LifecycleRetainsOriginalSameSourceCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/native/light/lifecycle",
            183, "native-input/183-input.cursor-native-light-lifecycle", false);
    }

    [TestCaseSource(nameof(LifecycleRetainsOriginalSameSourceCallbackRows))]
    [Order(183)]
    public Task LifecycleRetainsOriginalSameSourceCallback(bool dark)
        => ControlCursorSuite.ObserveNativeLifecycleAsync(dark);
}
