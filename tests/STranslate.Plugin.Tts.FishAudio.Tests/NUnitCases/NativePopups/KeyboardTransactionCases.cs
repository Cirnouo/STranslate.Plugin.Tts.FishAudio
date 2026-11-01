using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(144)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class KeyboardTransactionCases
{
    public static IEnumerable<TestCaseData> TransactionsPreserveStateAndFailuresRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.keyboard-state/transactions-preserve-state-and-failures",
            144, "native-popups/144-popup.keyboard-state-transactions-preserve-state-and-failures");
    }

    [TestCaseSource(nameof(TransactionsPreserveStateAndFailuresRows))]
    [Order(144)]
    public void TransactionsPreserveStateAndFailures()
    {
        var previousError = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            NativeKeyboardStateSuite.TransactionsPreserveStateAndFailures();
        }
        finally { Console.SetError(previousError); }

        Assert.That(error.ToString(), Is.EqualTo(string.Join(Environment.NewLine, new[]
        {
            "[native-key-state] phase=before-action; controlledChangedCount=0; controlledChanges=[]; spaceChanged=False; otherChangedCount=1",
            "[native-key-state] phase=before-action; nativeKey=0x01; plannedDown=False; tableDown=False; nativeDown=True",
            "[native-key-state] phase=restore; controlledChangedCount=0; controlledChanges=[]; spaceChanged=False; otherChangedCount=1",
            "[native-key-state] secondary cleanup failure (InvalidOperationException)",
            "[native-key-state] secondary cleanup failure (InvalidOperationException)",
        }) + Environment.NewLine));
    }
}
