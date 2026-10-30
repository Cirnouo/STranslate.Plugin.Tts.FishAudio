using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(181)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorNativeDarkPartsCases
{
    public static IEnumerable<TestCaseData> PartsRetainsOriginalSameSourceCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/native/dark/parts",
            181, "native-input/181-input.cursor-native-dark-parts", true);
    }

    [TestCaseSource(nameof(PartsRetainsOriginalSameSourceCallbackRows))]
    [Order(181)]
    public Task PartsRetainsOriginalSameSourceCallback(bool dark)
        => ControlCursorSuite.ObserveNativePartsAsync(dark);
}
