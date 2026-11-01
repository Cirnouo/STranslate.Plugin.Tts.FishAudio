using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(6)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class LanguageFontMetricsCases
{
    public static IEnumerable<TestCaseData> InheritedFontMetricsRefreshTheSameInstanceRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/inherited-font-metrics",
            6, "native-popups/006-popup.language-tags-inherited-font-metrics");
    }

    [TestCaseSource(nameof(InheritedFontMetricsRefreshTheSameInstanceRows))]
    [Order(6)]
    public Task InheritedFontMetricsRefreshTheSameInstance()
        => StaTestHost.RunOnStaThreadAsync(VoiceLanguageTagsSuite.InheritedFontMetricsRefreshTheSameInstance);
}
