using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(93)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SearchMaterialSourceCases
{
    public static IEnumerable<TestCaseData> MaterialOwnsOnlyItsExactPopupSourceRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/material-own-source",
            93, "native-popups/093-popup.search-adjustment-native-material-own-source");
    }

    [TestCaseSource(nameof(MaterialOwnsOnlyItsExactPopupSourceRows))]
    [Order(93)]
    public Task MaterialOwnsOnlyItsExactPopupSource()
        => StaTestHost.RunOnStaThreadAsync(SearchAdjustmentControlSuite.PopupMaterialUsesItsOwnNonlayeredSource);
}
