// One explicit owned-child compatibility operation. This is not a default
// legacy registration or a discovered NUnit case/source. Never invoke Run here.
internal static class StreamingTextChildCompatibilityTarget
{
    internal const string HistoricalScenarioId = "streaming.text/first-window-is-lazy-and-cuts-ordinary-long-sentences";

    internal static RegressionCase CreateCase() => new(HistoricalScenarioId, "core",
        () => StreamingTextSuite.FirstWindowIsLazyAndCutsOrdinaryLongSentencesAsync());

    // The caller first validates the complete request and exact current source.
    // Only this exact single operation can outlive default legacy retirement.
    internal static RegressionCase? BindExact(RegressionChildRequest request) =>
        request.LegacyArguments is null && request.Probe is null &&
        request.Partition == "core" && request.CaseIds is { Length: 1 } &&
        request.CaseIds[0] == HistoricalScenarioId
            ? CreateCase() : null;
}
