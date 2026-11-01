using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(5)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class LanguageFitThresholdCases
{
    public static IEnumerable<TestCaseData> FitThresholdsAndLongLabelsRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/fit-thresholds-long-labels",
            5, "native-popups/005-popup.language-tags-fit-thresholds-long-labels");
    }

    [TestCaseSource(nameof(FitThresholdsAndLongLabelsRows))]
    [Order(5)]
    public Task FitThresholdsAndLongLabels()
        => StaTestHost.RunOnStaThreadAsync(VoiceLanguageTagsSuite.FitThresholdsAndLongLabels);
}
