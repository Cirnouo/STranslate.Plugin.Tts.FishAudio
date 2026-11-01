using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(155)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class HeadingSourceReplacementCases
{
    public static IEnumerable<TestCaseData> SourceReplacementRetainsReadBoundsStylesAndDestroyedRetirementRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.heading/source-replacement-and-destroyed-retirement",
            155, "native-popups/155-popup.heading-source-replacement-and-destroyed-retirement");
    }

    [TestCaseSource(nameof(SourceReplacementRetainsReadBoundsStylesAndDestroyedRetirementRows))]
    [Order(155)]
    public Task SourceReplacementRetainsReadBoundsStylesAndDestroyedRetirement()
        => StaTestHost.RunOnStaThreadAsync(PopupHeadingSuite.SourceReplacementAndDestroyedRetirement);
}
