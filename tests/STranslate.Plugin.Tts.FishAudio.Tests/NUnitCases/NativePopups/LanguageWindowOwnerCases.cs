using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(11)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class LanguageWindowOwnerCases
{
    public static IEnumerable<TestCaseData> WindowOwnerRetiresAndRepositionsTheSamePopupRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.language-tags/lifecycle/window-owner",
            11, "native-popups/011-popup.language-tags-lifecycle-window-owner");
    }

    [TestCaseSource(nameof(WindowOwnerRetiresAndRepositionsTheSamePopupRows))]
    [Order(11)]
    public Task WindowOwnerRetiresAndRepositionsTheSamePopup()
        => StaTestHost.RunOnStaThreadAsync((Func<Task>)VoiceLanguageTagsSuite.WindowOwnerRetiresAndRepositionsPopup);
}
