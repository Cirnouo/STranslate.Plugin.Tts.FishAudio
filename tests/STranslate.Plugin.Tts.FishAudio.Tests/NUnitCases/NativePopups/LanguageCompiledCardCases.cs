using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(0)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class LanguageCompiledCardCases
{
    public static IEnumerable<TestCaseData> CompiledSelectedCardRetainsReusableLanguagesRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/compiled-selected-card",
            0, "native-popups/000-popup.language-tags-compiled-selected-card");
    }

    [TestCaseSource(nameof(CompiledSelectedCardRetainsReusableLanguagesRows))]
    [Order(0)]
    public Task CompiledSelectedCardRetainsReusableLanguages()
        => VoiceLanguageTagsSuite.ObserveCompiledSelectedCardAsync();
}
