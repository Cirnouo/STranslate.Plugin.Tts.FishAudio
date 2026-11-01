using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(1)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class LanguageCultureCases
{
    public static IEnumerable<TestCaseData> LabelsUseExplicitCultureAndTrustedFlagsRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/labels-explicit-culture-trusted-flags",
            1, "native-popups/001-popup.language-tags-labels-explicit-culture-trusted-flags");
    }

    [TestCaseSource(nameof(LabelsUseExplicitCultureAndTrustedFlagsRows))]
    [Order(1)]
    public Task LabelsUseExplicitCultureAndTrustedFlags()
        => StaTestHost.RunOnStaThreadAsync(VoiceLanguageTagsSuite.LabelsUseExplicitCultureAndTrustedFlags);
}
