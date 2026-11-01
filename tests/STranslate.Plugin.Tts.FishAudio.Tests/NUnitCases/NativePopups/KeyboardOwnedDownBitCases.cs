using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(146)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class KeyboardOwnedDownBitCases
{
    public static IEnumerable<TestCaseData> PostActionChecksOnlyOwnedBitsAndRestoresEverythingRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.keyboard-state/post-action-checks-only-owned-bits-and-restores-everything",
            146, "native-popups/146-popup.keyboard-state-post-action-checks-only-owned-bits-and-restores-everything");
    }

    [TestCaseSource(nameof(PostActionChecksOnlyOwnedBitsAndRestoresEverythingRows))]
    [Order(146)]
    public void PostActionChecksOnlyOwnedBitsAndRestoresEverything()
        => NativeKeyboardStateSuite.PostActionChecksOnlyOwnedBitsAndRestoresEverything();
}
