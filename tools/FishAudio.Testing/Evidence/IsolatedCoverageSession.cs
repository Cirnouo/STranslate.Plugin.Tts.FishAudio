using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;

namespace FishAudio.Testing.Evidence;

public enum CoverageArtifactKind
{
    Discovery, NUnitXml, Trx, Snapshot, AggregateJson,
    CoverInvocation, CoverProcess, CoverStdout, CoverStderr, CoverLog,
    ExportInvocation, ExportProcess, ExportStdout, ExportStderr, ExportLog,
}

public enum CoverageBinaryRole { Product, Tests }
public enum CoverageProcessRole { TestHost, OwnedAppHost }
public enum CoverageDiagnosticDisposition { Unreviewed, Clean, ReviewedWarnings, Error }
public enum EmptyProductCoverageReason { Unspecified, RepositoryResourcesOnly }

public sealed record CoverageSourceIdentity(string Commit, string Tree, string Fingerprint);
public sealed record CoverageSessionIdentity(string RunId, string SessionId, string CanonicalId,
    CoverageSourceIdentity Source, string BuildId);
public sealed record CoverageArtifact(CoverageArtifactKind Kind, string RelativePath, string Sha256,
    long Length, DateTimeOffset LastWriteUtc);
public sealed record CoverageBinaryBinding(CoverageBinaryRole Role, string DllRelativePath,
    string PdbRelativePath, BinaryIdentityRecord ExpectedIdentity);
public sealed record CoverageOwnedProcessEvidence(string RelativePath, string Sha256, long Length, DateTimeOffset LastWriteUtc);
public sealed record CoverageOwnedProcess(int ProcessId, CoverageProcessRole Role, bool Completed,
    int ExitCode, bool StdoutDrained, bool StderrDrained)
{
    // The trusted supervisor must first validate the existing child protocol's
    // run/source/case ownership. These are its sealed current-run raw records,
    // retained for audit; the importer does not invent another child schema.
    public IReadOnlyList<CoverageOwnedProcessEvidence> Evidence { get; init; } = [];
}
public sealed record CoverageProcessCompletion(DateTimeOffset StartedUtc, DateTimeOffset FinishedUtc,
    bool Completed, int ExitCode, bool StdoutDrained, bool StderrDrained, bool OwnedTreeCompleted,
    IReadOnlyList<CoverageOwnedProcess> OwnedProcesses);
public sealed record CoverageDiagnosticBinding(CoverageArtifactKind Kind, string Sha256);
public sealed record CoverageDiagnosticReview(CoverageDiagnosticDisposition Disposition, string RuleId,
    string RuleSourceSha256, IReadOnlyList<CoverageDiagnosticBinding> Evidence);

// This is a versioned contract of the trusted process supervisor, not a schema
// attributed to dotCover. In particular, owned-tree and per-pipe completion must
// come from supervision; native process.json's outputDrained alone is insufficient.
public sealed record CoverageSupervisorReceipt(int SchemaVersion, CoverageSessionIdentity Identity,
    DateTimeOffset StartedUtc, DateTimeOffset SealedUtc, CoverageProcessCompletion Cover,
    CoverageProcessCompletion Export, CoverageDiagnosticReview Diagnostics, IReadOnlyList<CoverageArtifact> Artifacts)
{
    // Historical bounded receipts without this envelope establish their basic
    // process/artifact boundary, not framework native-action supervision.
    public CoverageFrameworkEnvelope? FrameworkEnvelope { get; init; }
}
public sealed record CoverageRepositoryDependency(string RelativePath, string Sha256);
public sealed record EmptyProductCoverage(EmptyProductCoverageReason Reason,
    IReadOnlyList<CoverageRepositoryDependency> Dependencies);
public sealed record IsolatedCoverageSessionInput(int SchemaVersion, CoverageSessionIdentity Identity,
    string ArtifactDirectory, IReadOnlyList<CoverageArtifact> Artifacts, EmptyProductCoverage? EmptyProduct);

// The caller independently establishes the immutable source/build and receives
// the supervisor receipt directly. A receipt read from an untrusted session's
// own manifest is not an independent admission expectation.
public sealed record IsolatedCoverageAdmission(int SchemaVersion, CoverageSessionIdentity Identity,
    string SourceDirectory, string BuildDirectory, IReadOnlyList<CoverageBinaryBinding> Binaries,
    string DiscoverySha256, string DotCoverVersion, string ToolFile, string ToolSha256,
    bool RequireOwnedAppHost, CoverageSupervisorReceipt Supervisor)
{
    public bool RequireFrameworkSupervision { get; init; }
}
public sealed record AdmittedCoverageBinary(CoverageBinaryRole Role, BinaryIdentityRecord Identity);
public sealed record UnresolvedCaseCoverageSymbol(DotCoverMemberRecord Observation);
public sealed record CaseCoverage(int SchemaVersion, CoverageSessionIdentity Identity,
    NUnitCaseResult TestResult, IReadOnlyList<AdmittedCoverageBinary> Binaries,
    IReadOnlyList<CoverageArtifact> Artifacts, DotCoverAggregateReport Aggregate,
    IReadOnlyList<UnresolvedCaseCoverageSymbol> UnresolvedSymbols,
    CoverageSupervisorReceipt Supervisor, EmptyProductCoverage? EmptyProduct)
{
    public bool CompleteBaseline => false;
    public string AttributionMode => "isolated-case";
    public bool SourceMappingsResolved => false;
}

public sealed class IsolatedCoverageSessionException : Exception
{
    internal IsolatedCoverageSessionException(string code) : base(code) => Code = code;
    public string Code { get; }
}

/// <summary>
/// Admits one isolated, passed framework leaf. Raw files remain caller-owned and
/// are never created, rewritten or deleted. Aggregate symbol observations retain
/// their ambiguity; this reader performs no source-symbol or baseline admission.
/// </summary>
public static partial class IsolatedCoverageSession
{
    private const long MaximumArtifactBytes = 1024L * 1024 * 1024;

    public static CaseCoverage ReadValidated(IsolatedCoverageSessionInput input, IsolatedCoverageAdmission expected)
    {
        var locks = new List<FileStream>();
        var admitted = false;
        try
        {
            Require(input is not null && expected is not null && input.SchemaVersion == 1 && expected.SchemaVersion == 1,
                "unsupported-schema");
            ValidateIdentity(expected.Identity);
            Require(input.Identity == expected.Identity, "session-identity-mismatch");
            var supervisor = expected.Supervisor;
            Require(supervisor is not null && supervisor.SchemaVersion == 1 && supervisor.Identity == expected.Identity,
                "supervisor-identity-mismatch");
            Require(!expected.RequireFrameworkSupervision || supervisor.FrameworkEnvelope is not null,
                "framework-supervision-required");
            ValidateCompletion(supervisor, expected.RequireOwnedAppHost);
            Require(expected.DotCoverVersion == DotCoverAggregate.SupportedToolVersion, "unsupported-tool-version");
            Require(IsHash(expected.ToolSha256), "invalid-tool-hash");
            var tool = OpenAbsolute(expected.ToolFile, locks);
            Require(Digest(tool) == expected.ToolSha256, "tool-hash-mismatch");
            var artifactRoot = Root(input.ArtifactDirectory);
            var buildRoot = Root(expected.BuildDirectory);
            var sourceRoot = Root(expected.SourceDirectory);
            Require(input.Artifacts is not null && input.Artifacts.Count == Enum.GetValues<CoverageArtifactKind>().Length,
                "artifact-set-mismatch");
            Require(supervisor.Artifacts is not null && supervisor.Artifacts.Count == input.Artifacts.Count &&
                input.Artifacts.All(item => item is not null && supervisor.Artifacts.Count(sealedArtifact => sealedArtifact == item) == 1),
                "sealed-artifact-set-mismatch");
            var paths = new Dictionary<CoverageArtifactKind, string>();
            var relativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var artifact in input.Artifacts)
            {
                Require(artifact is not null && Enum.IsDefined(artifact.Kind) && !paths.ContainsKey(artifact.Kind) &&
                    relativePaths.Add(artifact.RelativePath), "duplicate-or-unknown-artifact");
                Require(IsHash(artifact.Sha256) && artifact.Length >= 0 && artifact.Length <= MaximumArtifactBytes &&
                    Utc(artifact.LastWriteUtc), "invalid-artifact-identity");
                Require(artifact.Kind == CoverageArtifactKind.Snapshot || artifact.Length <= 64 * 1024 * 1024,
                    "artifact-size-limit");
                var path = Child(artifactRoot, artifact.RelativePath);
                var stream = OpenAbsolute(path, locks);
                Require(stream.Length == artifact.Length && Digest(stream) == artifact.Sha256,
                    "artifact-hash-or-length-mismatch");
                Require(new DateTimeOffset(File.GetLastWriteTimeUtc(path)) == artifact.LastWriteUtc,
                    "artifact-timestamp-mismatch");
                if (artifact.Kind == CoverageArtifactKind.Discovery)
                    Require(artifact.Sha256 == expected.DiscoverySha256, "discovery-build-mismatch");
                else
                {
                    var earliest = artifact.Kind is CoverageArtifactKind.ExportProcess or CoverageArtifactKind.ExportStdout or
                        CoverageArtifactKind.ExportStderr or CoverageArtifactKind.ExportLog or CoverageArtifactKind.AggregateJson
                        ? supervisor.Export.StartedUtc : supervisor.StartedUtc;
                    Require(artifact.LastWriteUtc >= earliest && artifact.LastWriteUtc <= supervisor.SealedUtc,
                        "stale-artifact");
                    if (artifact.Kind == CoverageArtifactKind.Snapshot)
                        Require(artifact.LastWriteUtc <= supervisor.Export.StartedUtc, "snapshot-modified-after-export-start");
                }
                if (artifact.Kind is CoverageArtifactKind.Discovery or CoverageArtifactKind.NUnitXml or CoverageArtifactKind.Trx or
                    CoverageArtifactKind.Snapshot or CoverageArtifactKind.AggregateJson or CoverageArtifactKind.CoverInvocation or
                    CoverageArtifactKind.CoverProcess or CoverageArtifactKind.ExportInvocation or CoverageArtifactKind.ExportProcess)
                    Require(stream.Length > 0, "empty-required-artifact");
                paths.Add(artifact.Kind, path);
            }
            Require(IsHash(expected.DiscoverySha256), "invalid-discovery-hash");
            ValidateDiagnosticBindings(supervisor.Diagnostics, input.Artifacts);
            foreach (var process in supervisor.Cover.OwnedProcesses.Where(item => item.Role == CoverageProcessRole.OwnedAppHost))
            {
                Require(process.Evidence is not null && process.Evidence.Count is > 0 and <= 1024, "owned-process-proof-required");
                foreach (var evidence in process.Evidence)
                {
                    Require(evidence is not null && IsHash(evidence.Sha256) && evidence.Length > 0 && evidence.Length <= 64 * 1024 * 1024 &&
                        Utc(evidence.LastWriteUtc) && evidence.LastWriteUtc >= supervisor.Cover.StartedUtc && evidence.LastWriteUtc <= supervisor.Cover.FinishedUtc &&
                        relativePaths.Add(evidence.RelativePath), "invalid-owned-process-proof");
                    var path = Child(artifactRoot, evidence.RelativePath);
                    var stream = OpenAbsolute(path, locks);
                    Require(stream.Length == evidence.Length && Digest(stream) == evidence.Sha256 &&
                        new DateTimeOffset(File.GetLastWriteTimeUtc(path)) == evidence.LastWriteUtc, "owned-process-proof-mismatch");
                }
            }
            Require(expected.Binaries is not null && expected.Binaries.Count == 2, "binary-set-mismatch");
            var binaries = new List<AdmittedCoverageBinary>();
            var binaryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var roles = new HashSet<CoverageBinaryRole>();
            string? testDll = null;
            foreach (var binding in expected.Binaries)
            {
                Require(binding is not null && Enum.IsDefined(binding.Role) && roles.Add(binding.Role) && binding.ExpectedIdentity is not null,
                    "binary-set-mismatch");
                var dll = Child(buildRoot, binding.DllRelativePath);
                var pdb = Child(buildRoot, binding.PdbRelativePath);
                Require(binaryPaths.Add(dll) && binaryPaths.Add(pdb), "duplicate-binary-file");
                _ = OpenAbsolute(dll, locks);
                _ = OpenAbsolute(pdb, locks);
                var identity = BinaryIdentity.ReadValidated(dll, pdb);
                Require(identity == binding.ExpectedIdentity, "binary-build-mismatch");
                binaries.Add(new(binding.Role, identity));
                if (binding.Role == CoverageBinaryRole.Tests) testDll = dll;
            }
            var product = binaries.Single(item => item.Role == CoverageBinaryRole.Product).Identity;
            ValidateProcessRecord(paths[CoverageArtifactKind.CoverProcess], supervisor.Cover);
            ValidateProcessRecord(paths[CoverageArtifactKind.ExportProcess], supervisor.Export);
            ValidateInvocation(paths[CoverageArtifactKind.CoverInvocation], expected.Identity, "cover",
                "--snapshot-output", paths[CoverageArtifactKind.Snapshot], testDll!, expected.ToolFile);
            ValidateInvocation(paths[CoverageArtifactKind.ExportInvocation], expected.Identity, "report",
                "--snapshot-source", paths[CoverageArtifactKind.Snapshot], testDll: null, expectedToolFile: expected.ToolFile);
            using (var invocation = ReadJson(paths[CoverageArtifactKind.ExportInvocation]))
                Require(Option(invocation.RootElement, "--json-report-output") == paths[CoverageArtifactKind.AggregateJson], "export-output-mismatch");
            var catalog = NUnitEvidence.ReadCatalog(paths[CoverageArtifactKind.Discovery]);
            Require(catalog.Count(item => item.CanonicalId == expected.Identity.CanonicalId) == 1, "canonical-case-not-discovered");
            var result = NUnitEvidence.VerifyResults(catalog, [expected.Identity.CanonicalId],
                paths[CoverageArtifactKind.NUnitXml], paths[CoverageArtifactKind.Trx]);
            Require(result.PassedCount == 1 && result.Cases.Count == 1, "terminal-case-set-mismatch");
            if (supervisor.FrameworkEnvelope is { } frameworkEnvelope)
                ValidateFrameworkEnvelope(frameworkEnvelope, expected, artifactRoot, buildRoot, sourceRoot, testDll!,
                    paths, relativePaths, binaryPaths, catalog, locks);
            var aggregate = DotCoverAggregate.ReadValidated(paths[CoverageArtifactKind.AggregateJson], product.AssemblyName,
                expected.DotCoverVersion);
            Require(aggregate.ReportSha256 == input.Artifacts.Single(item => item.Kind == CoverageArtifactKind.AggregateJson).Sha256,
                "aggregate-digest-mismatch");
            var empty = aggregate.TargetAssembly is null || aggregate.TargetAssembly.CoveredStatements == 0;
            if (empty)
            {
                // Keep the established explanation/hash checks first. The same
                // already-hashed discovery remains held in locks while its
                // selected canonical case supplies independent declarations.
                ValidateEmptyProduct(input.EmptyProduct, sourceRoot, locks);
                var metadata = NUnitEvidence.ReadCaseMetadata(paths[CoverageArtifactKind.Discovery], expected.Identity.CanonicalId);
                ValidateEmptyProductDeclaration(metadata, input.EmptyProduct!, sourceRoot);
            }
            else Require(input.EmptyProduct is null, "unexpected-empty-product-explanation");
            var observations = aggregate.Members.Select(member => new UnresolvedCaseCoverageSymbol(member)).ToArray();
            admitted = true;
            return new(1, expected.Identity, result.Cases[0], binaries.AsReadOnly(), Array.AsReadOnly(input.Artifacts.ToArray()),
                aggregate, Array.AsReadOnly(observations), supervisor, input.EmptyProduct);
        }
        catch (IsolatedCoverageSessionException) { throw; }
        catch (NUnitEvidenceException) { throw Rejected("test-evidence-rejected"); }
        catch (BinaryIdentityException) { throw Rejected("binary-evidence-rejected"); }
        catch (DotCoverAggregateException) { throw Rejected("aggregate-evidence-rejected"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or JsonException or OverflowException or KeyNotFoundException or FormatException)
        {
            throw Rejected("invalid-input-or-file");
        }
        finally
        {
            var releaseFailed = false;
            foreach (var stream in locks)
            {
                try { stream.Dispose(); }
                catch { releaseFailed = true; }
            }
            if (releaseFailed && admitted) throw Rejected("file-release-failed");
        }
    }

    private static void ValidateIdentity(CoverageSessionIdentity identity)
    {
        Require(identity is not null && Guid.TryParseExact(identity.RunId, "N", out var run) && run != Guid.Empty &&
            Guid.TryParseExact(identity.SessionId, "N", out var session) && session != Guid.Empty &&
            Token(identity.CanonicalId) && IsHash(identity.BuildId) && identity.Source is not null &&
            GitHash(identity.Source.Commit) && GitHash(identity.Source.Tree) && IsHash(identity.Source.Fingerprint), "invalid-session-identity");
    }

    private static void ValidateCompletion(CoverageSupervisorReceipt supervisor, bool requireOwnedAppHost)
    {
        Require(Utc(supervisor.StartedUtc) && Utc(supervisor.SealedUtc) && supervisor.SealedUtc >= supervisor.StartedUtc,
            "invalid-session-time");
        ValidatePhase(supervisor.Cover);
        ValidatePhase(supervisor.Export);
        Require(supervisor.Cover.StartedUtc >= supervisor.StartedUtc && supervisor.Export.StartedUtc >= supervisor.Cover.FinishedUtc &&
            supervisor.Export.FinishedUtc <= supervisor.SealedUtc, "invalid-phase-order");
        Require(supervisor.Diagnostics is not null &&
            supervisor.Diagnostics.Disposition is CoverageDiagnosticDisposition.Clean or CoverageDiagnosticDisposition.ReviewedWarnings,
            "diagnostics-not-admitted");
        Require(Token(supervisor.Diagnostics.RuleId) && IsHash(supervisor.Diagnostics.RuleSourceSha256), "diagnostic-rule-required");
        Require(!requireOwnedAppHost || supervisor.Cover.OwnedProcesses.Any(item => item.Role == CoverageProcessRole.OwnedAppHost),
            "required-owned-process-missing");
        Require(supervisor.Export.OwnedProcesses.Count == 0, "unexpected-export-owned-process");
    }

    private static void ValidatePhase(CoverageProcessCompletion phase)
    {
        Require(phase is not null && Utc(phase.StartedUtc) && Utc(phase.FinishedUtc) && phase.FinishedUtc >= phase.StartedUtc &&
            phase.Completed && phase.ExitCode == 0 && phase.StdoutDrained && phase.StderrDrained && phase.OwnedTreeCompleted,
            "process-not-complete");
        Require(phase.OwnedProcesses is not null && phase.OwnedProcesses.Count <= 1024, "invalid-owned-processes");
        var pids = new HashSet<int>();
        foreach (var process in phase.OwnedProcesses)
            Require(process is not null && process.ProcessId > 0 && pids.Add(process.ProcessId) && Enum.IsDefined(process.Role) &&
                process.Completed && process.ExitCode == 0 && process.StdoutDrained && process.StderrDrained,
                "owned-process-not-complete");
    }

    private static void ValidateDiagnosticBindings(CoverageDiagnosticReview review, IReadOnlyList<CoverageArtifact> artifacts)
    {
        CoverageArtifactKind[] kinds = [CoverageArtifactKind.CoverStdout, CoverageArtifactKind.CoverStderr, CoverageArtifactKind.CoverLog,
            CoverageArtifactKind.ExportStdout, CoverageArtifactKind.ExportStderr, CoverageArtifactKind.ExportLog];
        Require(review.Evidence is not null && review.Evidence.Count == kinds.Length, "diagnostic-evidence-mismatch");
        var seen = new HashSet<CoverageArtifactKind>();
        foreach (var binding in review.Evidence)
            Require(binding is not null && kinds.Contains(binding.Kind) && seen.Add(binding.Kind) && IsHash(binding.Sha256) &&
                artifacts.Single(item => item.Kind == binding.Kind).Sha256 == binding.Sha256, "diagnostic-evidence-mismatch");
    }

    private static void ValidateProcessRecord(string path, CoverageProcessCompletion expected)
    {
        using var document = ReadJson(path);
        var root = document.RootElement;
        var started = root.GetProperty("startedUtc").GetDateTimeOffset();
        var finished = root.GetProperty("finishedUtc").GetDateTimeOffset();
        Require(root.GetProperty("completed").GetBoolean() && root.GetProperty("exitCode").GetInt32() == 0 &&
            root.GetProperty("outputDrained").GetBoolean() && Utc(started) && Utc(finished) &&
            started == expected.StartedUtc && finished == expected.FinishedUtc &&
            root.GetProperty("elapsedMilliseconds").TryGetDouble(out var elapsed) && double.IsFinite(elapsed) && elapsed >= 0,
            "supervisor-raw-record-mismatch");
    }

    private static void ValidateInvocation(string path, CoverageSessionIdentity identity, string command,
        string option, string expectedPath, string? testDll, string expectedToolFile)
    {
        using var document = ReadJson(path);
        var root = document.RootElement;
        Require(root.GetProperty("sessionRunId").GetString() == identity.SessionId &&
            root.GetProperty("canonicalCaseId").GetString() == identity.CanonicalId, "invocation-owner-mismatch");
        var arguments = Arguments(root);
        Require(arguments.Length >= 2 && Path.IsPathFullyQualified(arguments[0]) &&
            Path.GetFullPath(arguments[0]).Equals(Path.GetFullPath(expectedToolFile), StringComparison.OrdinalIgnoreCase) &&
            arguments[1] == command && arguments.Count(item => item == command) == 1 && Option(root, option) == expectedPath &&
            (testDll is null || arguments.Count(item => item == testDll) == 1), "invocation-target-mismatch");
    }

    private static string[] Arguments(JsonElement root)
    {
        var array = root.GetProperty("arguments");
        Require(array.ValueKind == JsonValueKind.Array && array.GetArrayLength() is > 0 and <= 1024, "invalid-invocation-arguments");
        var arguments = array.EnumerateArray().Select(item => item.GetString()).ToArray();
        Require(arguments.All(Token), "invalid-invocation-arguments");
        return arguments.Select(item => item!).ToArray();
    }

    private static string Option(JsonElement root, string name)
    {
        var arguments = Arguments(root);
        var positions = Enumerable.Range(0, arguments.Length).Where(index => arguments[index] == name).ToArray();
        Require(positions.Length == 1 && positions[0] + 1 < arguments.Length, "missing-or-duplicate-invocation-option");
        return arguments[positions[0] + 1];
    }

    private static void ValidateEmptyProduct(EmptyProductCoverage? explanation, string sourceRoot, List<FileStream> locks)
    {
        Require(explanation is not null && explanation.Reason == EmptyProductCoverageReason.RepositoryResourcesOnly &&
            explanation.Dependencies is not null && explanation.Dependencies.Count is > 0 and <= 4096,
            "empty-product-reason-required");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in explanation.Dependencies)
        {
            Require(dependency is not null && IsHash(dependency.Sha256) && paths.Add(dependency.RelativePath),
                "invalid-repository-dependency");
            var file = OpenAbsolute(Child(sourceRoot, dependency.RelativePath), locks);
            Require(Digest(file) == dependency.Sha256, "repository-dependency-mismatch");
        }
    }

    private static void ValidateEmptyProductDeclaration(NUnitCaseMetadata metadata,
        EmptyProductCoverage explanation, string sourceRoot)
    {
        Require(metadata.Properties.TryGetValue("FishAudioEmptyProductCoverage", out var reasons),
            "empty-product-declaration-required");
        Require(reasons.Count == 1 && reasons[0] == "RepositoryResourcesOnly", "invalid-empty-product-declaration");
        Require(metadata.Properties.TryGetValue("FishAudioRepositoryDependency", out var dependencies) &&
            dependencies.Count is > 0 and <= 4096, "invalid-repository-dependency-declaration");
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in dependencies)
        {
            // Use exactly the existing relative-path/root contract. A declaration
            // cannot authorize an absolute, parent, backslash or duplicate path.
            _ = Child(sourceRoot, dependency);
            Require(declared.Add(dependency), "invalid-repository-dependency-declaration");
        }
        Require(declared.Count == explanation.Dependencies.Count &&
            declared.SetEquals(explanation.Dependencies.Select(item => item.RelativePath)),
            "empty-product-dependency-declaration-mismatch");
    }

    private static JsonDocument ReadJson(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(stream.Length is > 0 and <= 4 * 1024 * 1024, "metadata-size-limit");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 32 });
        var properties = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) properties.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) properties.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName)
                Require(properties.Peek().Add(reader.GetString()!), "duplicate-metadata-property");
        }
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
    }

    private static string Root(string path)
    {
        Require(!string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path) && Directory.Exists(path), "invalid-root");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        NoReparse(full);
        return full;
    }

    private static string Child(string parent, string relative)
    {
        Require(Token(relative) && !Path.IsPathRooted(relative) && !relative.Contains('\\') &&
            relative.Split('/').All(part => part is not ("" or "." or "..")), "invalid-relative-path");
        var full = Path.GetFullPath(Path.Combine(parent, relative));
        Require(full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "path-escapes-root");
        return full;
    }

    private static FileStream OpenAbsolute(string path, List<FileStream> locks)
    {
        Require(!string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path), "path-must-be-absolute");
        NoReparse(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        locks.Add(stream);
        return stream;
    }

    private static void NoReparse(string path)
    {
        for (FileSystemInfo? item = File.Exists(path) ? new FileInfo(path) : new DirectoryInfo(path); item is not null;
             item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent)
        {
            item.Refresh();
            Require(item.Exists, "file-or-directory-missing");
            Require((item.Attributes & FileAttributes.ReparsePoint) == 0, "reparse-point");
        }
    }

    private static string Digest(FileStream stream)
    {
        Require(stream.Length <= MaximumArtifactBytes, "file-size-limit");
        stream.Position = 0;
        var digest = Convert.ToHexStringLower(SHA256.HashData(stream));
        stream.Position = 0;
        return digest;
    }

    private static bool Token(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 16384 && !value.Any(char.IsControl);
    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool GitHash(string? value) => value is { Length: 40 or 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool Utc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;
    private static IsolatedCoverageSessionException Rejected(string code) => new("coverage-session-" + code);
    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw Rejected(code);
    }
}
