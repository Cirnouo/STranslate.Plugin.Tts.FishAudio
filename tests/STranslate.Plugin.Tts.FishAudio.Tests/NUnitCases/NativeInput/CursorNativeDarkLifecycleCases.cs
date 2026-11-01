using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(184)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorNativeDarkLifecycleCases
{
    public static IEnumerable<TestCaseData> LifecycleRetainsOriginalSameSourceCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/native/dark/lifecycle",
            184, "native-input/184-input.cursor-native-dark-lifecycle", true);
    }

    [TestCaseSource(nameof(LifecycleRetainsOriginalSameSourceCallbackRows))]
    [Order(184)]
    public Task LifecycleRetainsOriginalSameSourceCallback(bool dark)
        => ControlCursorSuite.ObserveNativeLifecycleAsync(dark);
}
