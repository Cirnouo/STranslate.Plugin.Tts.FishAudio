using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(145)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class KeyboardPrivateFailureContextCases
{
    public static IEnumerable<TestCaseData> FailureContextIsBoundedAndPrivateRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.keyboard-state/failure-context-is-bounded-and-private",
            145, "native-popups/145-popup.keyboard-state-failure-context-is-bounded-and-private");
    }

    [TestCaseSource(nameof(FailureContextIsBoundedAndPrivateRows))]
    [Order(145)]
    public void FailureContextIsBoundedAndPrivate()
        => NativeKeyboardStateSuite.FailureContextIsBoundedAndPrivate();
}
