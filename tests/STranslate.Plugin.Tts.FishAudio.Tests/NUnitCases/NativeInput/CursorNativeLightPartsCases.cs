using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(179)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorNativeLightPartsCases
{
    public static IEnumerable<TestCaseData> PartsRetainsOriginalSameSourceCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/native/light/parts",
            179, "native-input/179-input.cursor-native-light-parts", false);
    }

    [TestCaseSource(nameof(PartsRetainsOriginalSameSourceCallbackRows))]
    [Order(179)]
    public Task PartsRetainsOriginalSameSourceCallback(bool dark)
        => ControlCursorSuite.ObserveNativePartsAsync(dark);
}
