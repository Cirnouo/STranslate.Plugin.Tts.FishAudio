using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(4)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class LanguageResponsivePrefixCases
{
    public static IEnumerable<TestCaseData> ResponsivePrefixFitsOneRowWithoutACapRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/responsive-prefix",
            4, "native-popups/004-popup.language-tags-responsive-prefix");
    }

    [TestCaseSource(nameof(ResponsivePrefixFitsOneRowWithoutACapRows))]
    [Order(4)]
    public Task ResponsivePrefixFitsOneRowWithoutACap()
        => StaTestHost.RunOnStaThreadAsync(VoiceLanguageTagsSuite.ResponsivePrefixFitsOneRowWithoutACap);
}
