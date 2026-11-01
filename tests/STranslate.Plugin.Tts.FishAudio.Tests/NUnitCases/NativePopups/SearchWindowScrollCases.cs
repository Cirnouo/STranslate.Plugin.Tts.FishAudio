using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(90)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SearchWindowScrollCases
{
    public static IEnumerable<TestCaseData> WindowAndScrollLifetimeRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/window-scroll-lifetime",
            90, "native-popups/090-popup.search-adjustment-native-window-scroll-lifetime");
    }

    [TestCaseSource(nameof(WindowAndScrollLifetimeRows))]
    [Order(90)]
    public Task WindowAndScrollLifetime()
        => StaTestHost.RunOnStaThreadAsync(SearchAdjustmentControlSuite.WindowAndScrollLifetime);
}
