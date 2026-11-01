using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(89)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SearchOverlayOwnershipCases
{
    public static IEnumerable<TestCaseData> OverlayOwnershipAndLifetimeRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/overlay-ownership",
            89, "native-popups/089-popup.search-adjustment-native-overlay-ownership");
    }

    [TestCaseSource(nameof(OverlayOwnershipAndLifetimeRows))]
    [Order(89)]
    public Task OverlayOwnershipAndLifetime()
        => StaTestHost.RunOnStaThreadAsync(SearchAdjustmentControlSuite.OverlayOwnershipAndLifetime);
}
