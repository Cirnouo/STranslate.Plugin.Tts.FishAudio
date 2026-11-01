using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(7)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class LanguageInitializationFailureCases
{
    public static IEnumerable<TestCaseData> InitializationFailureRetiresItsOriginalOwnershipRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/lifecycle/initialization-after-source",
            7, "native-popups/007-popup.language-tags-lifecycle-initialization-after-source", true, false);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/lifecycle/initialization-chrome",
            8, "native-popups/008-popup.language-tags-lifecycle-initialization-chrome", true, true);
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/lifecycle/initialization-before-source",
            9, "native-popups/009-popup.language-tags-lifecycle-initialization-before-source", false, false);
    }

    [TestCaseSource(nameof(InitializationFailureRetiresItsOriginalOwnershipRows))]
    [Order(7)]
    public Task InitializationFailureRetiresItsOriginalOwnership(bool failAfterSource, bool failInChrome)
        => StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => VoiceLanguageTagsSuite.PopupInitializationFailureRetiresOwnership(failAfterSource, failInChrome)));
}
