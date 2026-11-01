using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(88)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SearchHeldIntentCases
{
    public static IEnumerable<TestCaseData> HeldApplyReopensAppliedIntentAsyncRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/held-apply-reopens-intent",
            88, "native-popups/088-popup.search-adjustment-native-held-apply-reopens-intent");
    }

    [TestCaseSource(nameof(HeldApplyReopensAppliedIntentAsyncRows))]
    [Order(88)]
    public Task HeldApplyReopensAppliedIntentAsync()
        => StaTestHost.RunOnStaThreadAsync(SearchAdjustmentControlSuite.HeldApplyReopensAppliedIntentAsync);
}
