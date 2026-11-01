using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(151)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class HeadingCompiledLanguageCases
{
    public static IEnumerable<TestCaseData> CompiledLanguageRetainsAllLocalesAndItsFreshReopenRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.heading/compiled-language-locales-and-reopen",
            151, "native-popups/151-popup.heading-compiled-language-locales-and-reopen");
    }

    [TestCaseSource(nameof(CompiledLanguageRetainsAllLocalesAndItsFreshReopenRows))]
    [Order(151)]
    public Task CompiledLanguageRetainsAllLocalesAndItsFreshReopen()
        => StaTestHost.RunOnStaThreadAsync(PopupHeadingSuite.CompiledLanguageHeading);
}
