namespace FishAudio.Testing.Evidence;

/// <summary>
/// Identity-neutral candidate construction output. Product scopes every token;
/// neither a case nor a group is an input authority for this materialized graph.
/// Candidate names, counts and sequence points establish no exact semantic join.
/// </summary>
public sealed record CoverageSourceCandidateGraph(int SchemaVersion, BinaryIdentityRecord Product,
    string AggregateSha256, CoverageContainerRootSummary RootSummary,
    IReadOnlyList<CoverageCandidateScope> Scopes,
    IReadOnlyList<CoverageCandidateDocument> Documents,
    IReadOnlyList<CoverageCandidateMethod> Methods,
    IReadOnlyList<CoverageObservationCandidates> Observations, EmptyProductCoverage? EmptyProduct)
{
    public bool SourceMappingsResolved => false;
    public bool DependencyClosureProven => false;
    public bool BuildInputClosureProven => false;
    public bool GeneratorInputClosureProven => false;
    public bool CompleteBaseline => false;
    public bool BaselineClaim => false;
}
