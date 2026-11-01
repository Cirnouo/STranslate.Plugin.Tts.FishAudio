using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(92)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SearchViewportLifecycleCases
{
    public static IEnumerable<TestCaseData> CompleteSameSourceViewportChainRetainsItsAsyncObserverRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/viewport-lifecycle",
            92, "native-popups/092-popup.search-adjustment-native-viewport-lifecycle");
    }

    [TestCaseSource(nameof(CompleteSameSourceViewportChainRetainsItsAsyncObserverRows))]
    [Order(92)]
    public Task CompleteSameSourceViewportChainRetainsItsAsyncObserver()
        => SearchAdjustmentControlSuite.ObserveViewportLifecycleAsync();
}
