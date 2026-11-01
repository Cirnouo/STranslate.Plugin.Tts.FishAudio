using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(3)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class LanguageOverflowRowCases
{
    public static IEnumerable<TestCaseData> OverflowDoesNotRaiseLanguageRowRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/overflow-keeps-language-row",
            3, "native-popups/003-popup.language-tags-overflow-keeps-language-row");
    }

    [TestCaseSource(nameof(OverflowDoesNotRaiseLanguageRowRows))]
    [Order(3)]
    public Task OverflowDoesNotRaiseLanguageRow()
        => StaTestHost.RunOnStaThreadAsync((Func<Task>)VoiceLanguageTagsSuite.OverflowDoesNotRaiseLanguageRow);
}
