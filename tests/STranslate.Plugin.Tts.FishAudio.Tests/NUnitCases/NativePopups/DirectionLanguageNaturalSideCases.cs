using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(158)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class DirectionLanguageNaturalSideCases
{
    public static IEnumerable<TestCaseData> NaturalSidesKeepEverySyntheticAndNativePolicyOnTheSameOwnerRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.direction/language/natural-sides",
            158, "native-popups/158-popup.direction-language-natural-sides");
    }

    [TestCaseSource(nameof(NaturalSidesKeepEverySyntheticAndNativePolicyOnTheSameOwnerRows))]
    [Order(158)]
    public Task NaturalSidesKeepEverySyntheticAndNativePolicyOnTheSameOwner()
        => PopupDirectionSuite.ObserveNaturalSidesAsync(true);
}
