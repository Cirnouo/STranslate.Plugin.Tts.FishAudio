namespace FishAudio.Testing.Execution;

// This wire contract carries lifecycle observations, never framework results or
// a baseline claim. The independently owned driver publishes its final barrier.
public sealed record FrameworkCaseBinding(string CanonicalId, string FullName);

public sealed record FrameworkClientContext(
    int SchemaVersion,
    string RunId,
    string? SourceTree,
    string Nonce,
    string PipeName,
    string TestAssembly,
    string TestAssemblySha256,
    IReadOnlyList<FrameworkCaseBinding> Cases);

public sealed record FrameworkHostMessage(
    int SchemaVersion,
    string Role,
    string Nonce,
    string RunId,
    string? SourceTree,
    int Pid,
    string AssemblySha256,
    string Kind,
    string? CaseId,
    string? FullName,
    int Ordinal,
    long Timestamp,
    long Frequency,
    long QueuedTimestamp,
    string? Outcome);
