using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(147)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class KeyboardPrimaryFailureCases
{
    public static IEnumerable<TestCaseData> CleanupMetadataCannotReplacePrimaryFailureRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.keyboard-state/cleanup-metadata-cannot-replace-primary-failure",
            147, "native-popups/147-popup.keyboard-state-cleanup-metadata-cannot-replace-primary-failure");
    }

    [TestCaseSource(nameof(CleanupMetadataCannotReplacePrimaryFailureRows))]
    [Order(147)]
    public void CleanupMetadataCannotReplacePrimaryFailure()
        => NativeKeyboardStateSuite.CleanupMetadataCannotReplacePrimaryFailure();
}
