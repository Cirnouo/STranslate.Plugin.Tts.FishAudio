using NUnit.Framework;

namespace NUnitCases.Core;

// Complete original scenario responsibilities use framework discovery and execution.
[TestFixture]
[NonParallelizable]
public sealed class OperationActivityOwnershipCases
{
    [Test]
    [Property("FishAudioCaseId", "lifecycle.activity/overlapping-startup-manual-and-tts-operations-unlock-after-final-completion")]
    public Task OverlappingStartupManualAndTtsOperationsUnlockAfterFinalCompletion()
        => OperationActivitySuite.OverlappingStartupManualAndTtsOperationsUnlockAfterFinalCompletionAsync();
}
