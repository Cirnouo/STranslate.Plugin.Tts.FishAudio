using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(91)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SearchShortViewportCases
{
    public static IEnumerable<TestCaseData> ShortViewportKeepsBothTriggerEdgesInsideItsOwnerRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/short-viewport",
            91, "native-popups/091-popup.search-adjustment-native-short-viewport");
    }

    [TestCaseSource(nameof(ShortViewportKeepsBothTriggerEdgesInsideItsOwnerRows))]
    [Order(91)]
    public Task ShortViewportKeepsBothTriggerEdgesInsideItsOwner()
        => StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => new OrdinaryPopupProofDirectory().RunAsync(directory =>
            SearchAdjustmentControlSuite.ShortViewportKeepsPopupInsideVisibleArea(directory))));
}
