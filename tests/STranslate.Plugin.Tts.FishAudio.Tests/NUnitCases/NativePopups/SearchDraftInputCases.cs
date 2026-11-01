using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(87)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SearchDraftInputCases
{
    public static IEnumerable<TestCaseData> DraftsApplyAndTileInputAsyncRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/drafts-apply-tile-input",
            87, "native-popups/087-popup.search-adjustment-native-drafts-apply-tile-input");
    }

    [TestCaseSource(nameof(DraftsApplyAndTileInputAsyncRows))]
    [Order(87)]
    public Task DraftsApplyAndTileInputAsync()
        => StaTestHost.RunOnStaThreadAsync(SearchAdjustmentControlSuite.DraftsApplyAndTileInputAsync);
}
