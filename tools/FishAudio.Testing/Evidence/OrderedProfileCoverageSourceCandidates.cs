using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;

namespace FishAudio.Testing.Evidence;

public sealed record OrderedProfileCoverageSourceCandidatesEvidence(int SchemaVersion,
    CoverageProfileGroupIdentity Identity, IReadOnlyList<OrderedProfileCoverageGroupMember> Members,
    CoverageSourceCandidateGraph CandidateGraph)
{
    public string AttributionMode => "ordered-profile-group";
    public string SourceIdentityAuthority => "caller-supplied";
    public bool IndividualCaseCoverage => false;
    public bool SourceMappingsResolved => false;
    public bool DependencyClosureProven => false;
    public bool BuildInputClosureProven => false;
    public bool GeneratorInputClosureProven => false;
    public bool CompleteBaseline => false;
    public bool BaselineClaim => false;
}

/// <summary>
/// Failed-source-stage diagnostic only. It carries no candidate graph, methods,
/// documents or member attribution and cannot authorize a successful source read.
/// The Product/aggregate/group passed raw group and PE admission before this stage.
/// </summary>
public sealed record OrderedProfileCoverageSourceStageFailure(CoverageProfileGroupIdentity AdmittedGroup,
    BinaryIdentityRecord AdmittedProduct, string AggregateSha256,
    CoverageCandidateScope ConservativeScope, string Reason)
{
    public string SourceStage => "rejected";
    public bool ValidSourceCandidateGraph => false;
    public bool SourceMappingsResolved => false;
    public bool DependencyClosureProven => false;
    public bool CompleteBaseline => false;
}

public sealed class OrderedProfileCoverageSourceCandidatesException : Exception
{
    internal OrderedProfileCoverageSourceCandidatesException(string code,
        OrderedProfileCoverageSourceStageFailure? sourceStageFailure = null) : base(code)
    {
        Code = code;
        SourceStageFailure = sourceStageFailure;
    }
    public string Code { get; }
    public OrderedProfileCoverageSourceStageFailure? SourceStageFailure { get; }
}

/// <summary>
/// Reads actual raw group inputs and independent admission itself. No overload
/// accepts a deserialized group, isolated CaseCoverage or collector self-assertion.
/// One aggregate graph belongs to the group; member rows remain declaration and
/// execution references only. No Roslyn, static/resource closure, CLI or TIA runs.
/// </summary>
public static class OrderedProfileCoverageSourceCandidates
{
    public static OrderedProfileCoverageSourceCandidatesEvidence ReadValidated(
        OrderedProfileCoverageGroupInput groupInput, OrderedProfileCoverageGroupAdmission expectedGroup,
        CoverageSourceCandidatesInput sourceInput)
    {
        var guards = new List<FileStream>();
        var completed = false;
        try
        {
            Require(groupInput is not null && expectedGroup is not null && sourceInput is not null &&
                groupInput.SchemaVersion == 1 && expectedGroup.SchemaVersion == 1 && sourceInput.SchemaVersion == 1 &&
                expectedGroup.Identity is not null && expectedGroup.Identity.Source is not null &&
                expectedGroup.Binaries is { Count: 2 } &&
                groupInput.Artifacts is not null && groupInput.Artifacts.Count == Enum.GetValues<CoverageArtifactKind>().Length &&
                expectedGroup.Supervisor is not null && expectedGroup.Supervisor.Artifacts is not null &&
                expectedGroup.Supervisor.Artifacts.Count == groupInput.Artifacts.Count &&
                expectedGroup.Supervisor.Cover is not null && expectedGroup.Supervisor.Cover.OwnedProcesses is { Count: 0 } &&
                expectedGroup.Supervisor.Export is not null && expectedGroup.Supervisor.Export.OwnedProcesses is { Count: 0 } &&
                expectedGroup.Supervisor.Diagnostics is not null && expectedGroup.Supervisor.Diagnostics.Evidence is { Count: <= 15 } &&
                sourceInput.ProductBuild is not null && sourceInput.ProductBuild.SchemaVersion == 1 &&
                sourceInput.ProductBuild.ExpectedAssembly is not null &&
                sourceInput.DocumentMaps is { Count: <= 128 } && sourceInput.TrackedFiles is { Count: <= 200_000 } &&
                sourceInput.GeneratedSourceRoots is { Count: <= 128 }, "invalid-input");
            var source = sourceInput with
            {
                ProductBuild = sourceInput.ProductBuild with { ExpectedAssembly = sourceInput.ProductBuild.ExpectedAssembly with { } },
                DocumentMaps = Array.AsReadOnly(sourceInput.DocumentMaps.ToArray()),
                TrackedFiles = Array.AsReadOnly(sourceInput.TrackedFiles.ToArray()),
                GeneratedSourceRoots = Array.AsReadOnly(sourceInput.GeneratedSourceRoots.ToArray()),
            };
            var expected = expectedGroup with
            {
                Binaries = Array.AsReadOnly(expectedGroup.Binaries.ToArray()),
                Supervisor = expectedGroup.Supervisor with
                {
                    Artifacts = Array.AsReadOnly(expectedGroup.Supervisor.Artifacts.ToArray()),
                    Cover = expectedGroup.Supervisor.Cover with { OwnedProcesses = Array.Empty<CoverageOwnedProcess>() },
                    Export = expectedGroup.Supervisor.Export with { OwnedProcesses = Array.Empty<CoverageOwnedProcess>() },
                    Diagnostics = expectedGroup.Supervisor.Diagnostics with
                    { Evidence = Array.AsReadOnly(expectedGroup.Supervisor.Diagnostics.Evidence.ToArray()) },
                },
            };
            var input = groupInput with { Artifacts = Array.AsReadOnly(groupInput.Artifacts.ToArray()) };
            var bindings = expected.Binaries.Where(item => item is not null && item.Role == CoverageBinaryRole.Product).Take(2).ToArray();
            var aggregates = input.Artifacts.Where(item => item is not null && item.Kind == CoverageArtifactKind.AggregateJson).Take(2).ToArray();
            Require(bindings.Length == 1 && aggregates.Length == 1, "invalid-input");
            var binding = bindings[0];
            var buildRoot = Root(expected.BuildDirectory);
            var sourceRoot = Root(expected.SourceDirectory);
            var artifactRoot = Root(input.ArtifactDirectory);
            var dllPath = Child(buildRoot, binding.DllRelativePath);
            var pdbPath = Child(buildRoot, binding.PdbRelativePath);
            var aggregatePath = Child(artifactRoot, aggregates[0].RelativePath);
            // These guards span raw group admission, PE, strict build/source
            // validation and candidate construction. No later path can replace
            // the current Product pair or the group's one actual aggregate.
            guards.Add(PortablePdbSource.OpenRead(dllPath, 256 * 1024 * 1024));
            guards.Add(PortablePdbSource.OpenRead(pdbPath, 64 * 1024 * 1024));
            var aggregateGuard = PortablePdbSource.OpenRead(aggregatePath, 64 * 1024 * 1024);
            guards.Add(aggregateGuard);
            OrderedProfileCoverageGroup admitted;
            try { admitted = OrderedProfileCoverageSession.ReadValidated(input, expected); }
            catch (OrderedProfileCoverageException) { throw Rejected("group-evidence-rejected"); }
            Require(source.ProductBuild.BuildId == admitted.Identity.BuildId &&
                source.ProductBuild.SourceTree == admitted.Identity.Source.Tree &&
                source.ProductBuild.SourceFingerprint == admitted.Identity.Source.Fingerprint, "group-build-mismatch");
            var product = admitted.Binaries.Single(item => item.Role == CoverageBinaryRole.Product).Identity;
            Require(source.ProductBuild.ExpectedAssembly == product && binding.ExpectedIdentity == product, "product-identity-mismatch");
            aggregateGuard.Position = 0;
            var bytes = new byte[checked((int)aggregateGuard.Length)];
            aggregateGuard.ReadExactly(bytes);
            var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Require(digest == aggregates[0].Sha256 && digest == admitted.Aggregate.ReportSha256 &&
                admitted.Aggregate.TargetAssemblyName == product.AssemblyName, "aggregate-digest-mismatch");
            PeMethodInventoryEvidence pe;
            try { pe = PeMethodInventory.ReadValidated(dllPath, pdbPath, product); }
            catch (Exception error) when (error is PeMethodInventoryException or BinaryIdentityException)
            { throw Rejected("pe-evidence-rejected"); }
            Require(pe.Assembly == product, "pe-product-identity-mismatch");
            BuildSourceInventoryEvidence sources;
            try
            {
                sources = BuildSourceInventory.ReadValidated(source.ProductBuild, dllPath, pdbPath, sourceRoot,
                    source.DocumentMaps, source.TrackedFiles, source.GeneratedSourceRoots);
            }
            catch (Exception error) when (error is BuildSourceInventoryException or PortablePdbSourceException or BinaryIdentityException)
            {
                // Retain the already proved first-layer assembly boundary, but
                // reject this read. This is deliberately not a partial success
                // or a graph that downstream code could admit as source evidence.
                var reason = error switch
                {
                    BuildSourceInventoryException build => build.Code,
                    PortablePdbSourceException pdb => pdb.Code,
                    _ => "binary-evidence-rejected",
                };
                throw new OrderedProfileCoverageSourceCandidatesException("ordered-profile-source-candidates-source-evidence-rejected",
                    new(admitted.Identity, product, digest, new(0, CoverageCandidateScopeKind.Assembly, null, null), reason));
            }
            Require(sources.SourceEvidence.Assembly == product && sources.ExpectedBuild == source.ProductBuild,
                "pe-source-identity-mismatch");
            var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            using var document = JsonDocument.Parse(bytes.AsMemory(offset), new JsonDocumentOptions { MaxDepth = 128 });
            var root = document.RootElement;
            var summary = new CoverageContainerRootSummary(root.GetProperty("CoveredStatements").GetInt64(),
                root.GetProperty("TotalStatements").GetInt64(), root.GetProperty("CoveragePercent").GetInt32());
            CoverageSourceCandidateGraph graph;
            try
            {
                graph = CoverageSourceCandidates.ConstructGraph(pe, sources, admitted.Aggregate, admitted.EmptyProduct, digest, summary,
                    new(CoverageSourceCandidates.MaximumCandidateReferences, CoverageSourceCandidates.MaximumSequencePointAssociations));
            }
            catch (CoverageSourceCandidatesException) { throw Rejected("candidate-graph-rejected"); }
            completed = true;
            return new(1, admitted.Identity, Array.AsReadOnly(admitted.Members.ToArray()), graph);
        }
        catch (OrderedProfileCoverageSourceCandidatesException) { throw; }
        catch (PortablePdbSourceException) { throw Rejected("invalid-input-or-file"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or JsonException or OverflowException or KeyNotFoundException or
            FormatException or CryptographicException or System.Security.SecurityException)
        { throw Rejected("invalid-input-or-file"); }
        finally
        {
            var failed = false;
            foreach (var guard in guards) { try { guard.Dispose(); } catch { failed = true; } }
            if (failed && completed) throw Rejected("file-release-failed");
        }
    }

    private static string Root(string path)
    {
        Require(path is not null && path.Length <= 32768 && Path.IsPathFullyQualified(path), "invalid-path");
        var rawRoot = Path.GetPathRoot(path);
        Require(rawRoot is { Length: 3 } && char.IsAsciiLetter(rawRoot[0]) && rawRoot[1] == ':' &&
            rawRoot[2] is '/' or '\\', "invalid-path");
        var relative = path[rawRoot.Length..].Replace('\\', '/').TrimEnd('/');
        Require(relative.Length == 0 || PortablePdbSource.RelativePath(relative), "invalid-path");
        var full = PortablePdbSource.AbsolutePath(path);
        PortablePdbSource.RequireNoReparseAncestors(full, directory: true);
        return full;
    }

    private static string Child(string root, string relative)
    {
        Require(relative is not null && PortablePdbSource.RelativePath(relative), "invalid-path");
        return PortablePdbSource.ResolveSource(root, relative);
    }

    private static OrderedProfileCoverageSourceCandidatesException Rejected(string reason) =>
        new("ordered-profile-source-candidates-" + reason);

    private static void Require([DoesNotReturnIf(false)] bool condition, string reason)
    {
        if (!condition) throw Rejected(reason);
    }
}
