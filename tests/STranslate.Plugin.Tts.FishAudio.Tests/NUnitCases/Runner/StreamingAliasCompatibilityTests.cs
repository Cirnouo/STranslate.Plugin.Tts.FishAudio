using NUnit.Framework;

namespace NUnitCases.Runner;

[TestFixture]
[NonParallelizable]
public sealed class StreamingAliasCompatibilityTests
{
    // Historical descriptor and synthetic-registry alias/union/replay rules.
    // This does not claim the real retired legacy selector remains executable.
    // RetiredStreamingSelectorCases independently checks the actual CLI boundary.
    [Test]
    [Property("FishAudioCaseId", "infra.runner/historical-streaming-descriptor-alias-rules")]
    public Task HistoricalStreamingAliasesRetainSelectionAndReplayAsync() =>
        RunnerContractSuite.HistoricalStreamingDescriptorAliasRulesAsync();
}
