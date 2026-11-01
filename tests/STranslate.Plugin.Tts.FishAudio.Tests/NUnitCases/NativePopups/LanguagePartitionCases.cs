using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(2)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class LanguagePartitionCases
{
    public static IEnumerable<TestCaseData> PartitionAndDictionaryReplacementRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/partition-dictionary-replacement",
            2, "native-popups/002-popup.language-tags-partition-dictionary-replacement");
    }

    [TestCaseSource(nameof(PartitionAndDictionaryReplacementRows))]
    [Order(2)]
    public Task PartitionAndDictionaryReplacement()
        => StaTestHost.RunOnStaThreadAsync(VoiceLanguageTagsSuite.PartitionAndDictionaryReplacement);
}
