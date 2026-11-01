using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(148)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class KeyboardNativePeerCases
{
    public static IEnumerable<TestCaseData> NativeButtonAndToggleKeepTheOriginalTransactionChainRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.keyboard-state/native-button-and-toggle-peer-transactions",
            148, "native-popups/148-popup.keyboard-state-native-button-and-toggle-peer-transactions");
    }

    [TestCaseSource(nameof(NativeButtonAndToggleKeepTheOriginalTransactionChainRows))]
    [Order(148)]
    public Task NativeButtonAndToggleKeepTheOriginalTransactionChain()
        => NativeKeyboardStateSuite.ObserveNativePeersAsync();
}
