using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(150)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class HeadingCompiledSearchCases
{
    public static IEnumerable<TestCaseData> CompiledSearchRetainsAllLocalesAndItsFreshReopenRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.heading/compiled-search-locales-and-reopen",
            150, "native-popups/150-popup.heading-compiled-search-locales-and-reopen");
    }

    [TestCaseSource(nameof(CompiledSearchRetainsAllLocalesAndItsFreshReopenRows))]
    [Order(150)]
    public Task CompiledSearchRetainsAllLocalesAndItsFreshReopen()
        => StaTestHost.RunOnStaThreadAsync(PopupHeadingSuite.CompiledSearchHeading);
}
