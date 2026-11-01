using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(156)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class DirectionSearchNaturalSideCases
{
    public static IEnumerable<TestCaseData> NaturalSidesKeepEverySyntheticAndNativePolicyOnTheSameOwnerRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.direction/search/natural-sides",
            156, "native-popups/156-popup.direction-search-natural-sides");
    }

    [TestCaseSource(nameof(NaturalSidesKeepEverySyntheticAndNativePolicyOnTheSameOwnerRows))]
    [Order(156)]
    public Task NaturalSidesKeepEverySyntheticAndNativePolicyOnTheSameOwner()
        => PopupDirectionSuite.ObserveNaturalSidesAsync(false);
}
