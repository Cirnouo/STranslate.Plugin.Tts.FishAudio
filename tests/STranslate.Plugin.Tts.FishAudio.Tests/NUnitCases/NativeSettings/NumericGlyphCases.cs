using NUnit.Framework;

namespace NUnitCases.NativeSettings;

// Source candidate only. The complete profile retains its original cold-first-use order.
[TestFixture]
[NonParallelizable]
[Order(77)]
[Property("FishAudioExecutionProfile", "native-settings")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class NumericGlyphCases
{
    public static IEnumerable<TestCaseData> EquivalentNativeRunGroupingRows()
    {
        yield return NativeTestCaseSource.Row(
            "numeric.glyph/equivalent-native-run-grouping",
            77, "native-settings/077-equivalent-native-run-grouping");
    }

    [TestCaseSource(nameof(EquivalentNativeRunGroupingRows))]
    [Order(77)]
    public Task EquivalentNativeRunGrouping()
        => NumericGlyphObservationSuite.EquivalentNativeRunGroupingAsync();
}
