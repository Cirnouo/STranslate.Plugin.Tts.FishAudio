using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(149)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class HeadingLeaseCases
{
    public static IEnumerable<TestCaseData> HeadingLeaseRetainsEveryOwnershipAndFailureBoundaryRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.heading/lease-ownership-and-failures",
            149, "native-popups/149-popup.heading-lease-ownership-and-failures");
    }

    [TestCaseSource(nameof(HeadingLeaseRetainsEveryOwnershipAndFailureBoundaryRows))]
    [Order(149)]
    public void HeadingLeaseRetainsEveryOwnershipAndFailureBoundary()
        => PopupHeadingSuite.LeaseOwnershipAndFailures();
}
