using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(10)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class LanguagePopupInputCases
{
    public static IEnumerable<TestCaseData> PopupOwnsItsCompleteInputAndLifetimeChainRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/lifecycle/popup-input",
            10, "native-popups/010-popup.language-tags-lifecycle-popup-input");
    }

    [TestCaseSource(nameof(PopupOwnsItsCompleteInputAndLifetimeChainRows))]
    [Order(10)]
    public Task PopupOwnsItsCompleteInputAndLifetimeChain()
        => StaTestHost.RunOnStaThreadAsync((Func<Task>)VoiceLanguageTagsSuite.PopupOwnsInputAndLifetime);
}
