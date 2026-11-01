using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;

namespace FishAudio.Testing.Evidence;

public sealed record OrderedProfileCoverageWorkspaceRequest(int SchemaVersion, string SnapshotContext,
    string SnapshotContextSha256, string SdkVersion, string Configuration, string Platform, string TargetFramework,
    bool Coverage, string EntryProject, string ProductProject);

public sealed record CoverageWorkspaceToolchain(string RuntimeVersion, string LocatorVersion, string SelectedSdkVersion,
    string MsBuildFileVersion, string MsBuildSha256, string WorkspaceVersion, string CSharpVersion);
public sealed record CoverageWorkspaceDiagnostic(string Stage, string Kind, string Code, string? RelativePath = null,
    int? Line = null, int? Column = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CoverageWorkspaceDiagnosticFacts? Facts { get; init; }
}
public sealed record CoverageWorkspaceDiagnosticFacts(string MessageSha256, IReadOnlyList<string> Hints,
    IReadOnlyList<string> KnownRelativePaths);
public sealed record CoverageWorkspaceFailure(string Stage, string Reason, string? ExceptionType = null);
public sealed record CoverageWorkspaceTree(string RelativePath, string PathSha256, string ChecksumAlgorithm,
    string Checksum, int Characters, bool SourceGeneratorOutput);
public sealed record CoverageWorkspaceProject(string RelativePath, string ProjectSha256, string OutputRelativePath,
    string LanguageVersion, IReadOnlyList<string> Defines, string Optimization, string Platform,
    IReadOnlyList<string> ProjectReferences, int MetadataReferences, string ReferenceIdentitySha256, int AnalyzerReferences, int AdditionalDocuments,
    int AnalyzerConfigDocuments, int GeneratedDocuments, string? TargetFramework, string? TargetPlatform,
    IReadOnlyList<CoverageWorkspaceTree> Trees);

public sealed record OrderedProfileCoverageWorkspaceEvidence(int SchemaVersion, CoverageProfileGroupIdentity Identity,
    BinaryIdentityRecord Product, string AggregateSha256, int GroupMembers, IReadOnlyList<CoverageCandidateScope> CandidateScopes,
    string SnapshotContextSha256,
    string SourceFingerprint, CoverageWorkspaceToolchain? Toolchain, IReadOnlyDictionary<string, string> RequestedProperties,
    IReadOnlyList<CoverageWorkspaceProject> Projects, string WorkspaceContextSha256,
    IReadOnlyList<CoverageWorkspaceDiagnostic> Diagnostics, IReadOnlyList<CoverageWorkspaceFailure> Failures,
    CoverageSourceSemanticJoinEvidence? SemanticJoin)
{
    public string Scope => "ordered-group-actual-project-candidate-declarations";
    public string Status => Failures.Count == 0 && SemanticJoin is not null ? "inspected" : "rejected";
    public string SourceIdentityAuthority => "caller-supplied-expectations";
    public bool IndividualCaseCoverage => false;
    public bool ProducedProductBinaryProven => false;
    public bool ProjectCompletenessProven => false;
    public bool ReferenceBytesAuthenticated => false;
    public bool GeneratorDiagnosticsComplete => false;
    public bool SourceMappingsResolved => false;
    public bool BuildInputClosureProven => false;
    public bool GeneratorInputClosureProven => false;
    public bool DependencyClosureProven => false;
    public bool CompleteBaseline => false;
    public bool BaselineClaim => false;
}

public sealed class OrderedProfileCoverageWorkspaceJoinException : Exception
{
    internal OrderedProfileCoverageWorkspaceJoinException(string reason, OrderedProfileCoverageWorkspaceEvidence? evidence = null)
        : base("ordered-profile-workspace-" + reason) { Code = Message; Evidence = evidence; }
    public string Code { get; }
    public OrderedProfileCoverageWorkspaceEvidence? Evidence { get; }
}

/// <summary>
/// Experimental actual-project consumer, not a baseline or runner. Reimports raw
/// group/source evidence and reads the caller-pinned snapshot itself. Registration
/// is process-global: use one invocation in a fresh externally owned process.
/// MSBuild design-time evaluation and generators retain their external SDK/package
/// input boundary; this reader neither restores packages nor authenticates closure.
/// </summary>
public static class OrderedProfileCoverageWorkspaceJoin
{
    private const int MaximumDiagnostics = 4096;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static async Task<OrderedProfileCoverageWorkspaceEvidence> ReadValidatedAsync(
        OrderedProfileCoverageGroupInput groupInput, OrderedProfileCoverageGroupAdmission expectedGroup,
        CoverageSourceCandidatesInput sourceInput, OrderedProfileCoverageWorkspaceRequest request,
        CancellationToken cancellationToken = default)
    {
        SnapshotLease? snapshot = null;
        var guards = new List<FileStream>();
        var completed = false;
        try
        {
            Require(expectedGroup is not null && expectedGroup.Identity is not null && expectedGroup.Identity.Source is not null &&
                expectedGroup.Binaries is { Count: 2 } && groupInput is not null && groupInput.Artifacts is not null &&
                sourceInput is not null && sourceInput.ProductBuild is not null && sourceInput.TrackedFiles is not null &&
                sourceInput.GeneratedSourceRoots is not null, "invalid-input");
            snapshot = OpenSnapshot(expectedGroup.Identity.Source, sourceInput.TrackedFiles, request, cancellationToken);
            Require(Same(snapshot.Root, expectedGroup.SourceDirectory) &&
                sourceInput.ProductBuild.SourceTree == expectedGroup.Identity.Source.Tree &&
                sourceInput.ProductBuild.SourceFingerprint == snapshot.SourceFingerprint &&
                sourceInput.ProductBuild.BuildId == expectedGroup.Identity.BuildId, "snapshot-group-mismatch");
            var productBindings = expectedGroup.Binaries.Where(item => item.Role == CoverageBinaryRole.Product).ToArray();
            var testBindings = expectedGroup.Binaries.Where(item => item.Role == CoverageBinaryRole.Tests).ToArray();
            var aggregates = groupInput.Artifacts.Where(item => item.Kind == CoverageArtifactKind.AggregateJson).ToArray();
            Require(productBindings.Length == 1 && testBindings.Length == 1 && aggregates.Length == 1, "invalid-input");
            var buildRoot = Root(expectedGroup.BuildDirectory);
            var dll = Child(buildRoot, productBindings[0].DllRelativePath);
            var pdb = Child(buildRoot, productBindings[0].PdbRelativePath);
            var testDll = Child(buildRoot, testBindings[0].DllRelativePath);
            guards.Add(PortablePdbSource.OpenRead(dll, 256 * 1024 * 1024));
            guards.Add(PortablePdbSource.OpenRead(pdb, 64 * 1024 * 1024));
            guards.Add(PortablePdbSource.OpenRead(Child(Root(groupInput.ArtifactDirectory), aggregates[0].RelativePath), 64 * 1024 * 1024));
            // No graph JSON can enter this path. These original raw inputs and
            // independent expectations must pass the existing admission again.
            var admitted = OrderedProfileCoverageSourceCandidates.ReadValidated(groupInput, expectedGroup, sourceInput);
            foreach (var document in admitted.CandidateGraph.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = PortablePdbSource.OpenRead(Child(snapshot.Root, document.Provenance.SnapshotRelativePath), 16 * 1024 * 1024, allowEmpty: true);
                guards.Add(file);
                Require(Convert.ToHexStringLower(SHA256.HashData(file)) == document.Provenance.Sha256, "graph-document-changed");
            }
            var toolchain = RegisterMSBuild(Path.GetDirectoryName(Child(snapshot.Root, request.EntryProject))!, request.SdkVersion);
            var result = await LoadAndJoinAsync(admitted, sourceInput, request, snapshot, dll, testDll, toolchain, cancellationToken).ConfigureAwait(false);
            completed = true;
            return result;
        }
        catch (OrderedProfileCoverageWorkspaceJoinException) { throw; }
        catch (OrderedProfileCoverageSourceCandidatesException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is PortablePdbSourceException or IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or CryptographicException or System.Security.SecurityException)
        { throw Rejected("input-or-file-rejected"); }
        catch (Exception) { throw Rejected("workspace-initialization-failed"); }
        finally
        {
            var failed = false;
            foreach (var guard in guards) { try { guard.Dispose(); } catch { failed = true; } }
            try { snapshot?.Dispose(); } catch { failed = true; }
            if (failed && completed) throw Rejected("input-release-failed");
        }
    }

    // No workspace or Microsoft.Build API type enters this registration method.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CoverageWorkspaceToolchain RegisterMSBuild(string projectDirectory, string sdkVersion)
    {
        Require(!MSBuildLocator.IsRegistered && !AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
            assembly.GetName().Name is "Microsoft.Build" or "Microsoft.Build.Framework" or "Microsoft.Build.Utilities.Core" or "Microsoft.Build.Tasks.Core"),
            "msbuild-already-registered-or-loaded");
        var instance = MSBuildLocator.QueryVisualStudioInstances(new VisualStudioInstanceQueryOptions
        { DiscoveryTypes = DiscoveryType.DotNetSdk, WorkingDirectory = projectDirectory }).FirstOrDefault();
        Require(instance is not null && instance.Version.ToString() == sdkVersion, "selected-sdk-mismatch");
        var msbuild = Path.Combine(instance.MSBuildPath, "Microsoft.Build.dll");
        using var file = PortablePdbSource.OpenRead(msbuild, 64 * 1024 * 1024);
        var digest = Convert.ToHexStringLower(SHA256.HashData(file));
        var version = ProbePaths.Token(FileVersionInfo.GetVersionInfo(msbuild).FileVersion);
        Require(version != "[unavailable]", "msbuild-version-unavailable");
        MSBuildLocator.RegisterInstance(instance);
        return new(Environment.Version.ToString(), Program.VersionOf(typeof(MSBuildLocator).Assembly), sdkVersion, version, digest, "", "");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<OrderedProfileCoverageWorkspaceEvidence> LoadAndJoinAsync(
        OrderedProfileCoverageSourceCandidatesEvidence admitted, CoverageSourceCandidatesInput sourceInput,
        OrderedProfileCoverageWorkspaceRequest request, SnapshotLease snapshot, string productDll, string testsDll,
        CoverageWorkspaceToolchain toolchain, CancellationToken token)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        { ["Configuration"] = request.Configuration, ["Platform"] = request.Platform, ["TargetFramework"] = request.TargetFramework,
            ["FishAudioCoverage"] = "true" };
        toolchain = toolchain with { WorkspaceVersion = Program.VersionOf(typeof(MSBuildWorkspace).Assembly),
            CSharpVersion = Program.VersionOf(typeof(CSharpCompilation).Assembly) };
        var projects = new List<CoverageWorkspaceProject>();
        var diagnostics = new List<CoverageWorkspaceDiagnostic>();
        var failures = new List<CoverageWorkspaceFailure>();
        var observed = new ConcurrentQueue<WorkspaceDiagnostic>();
        var workspaceDiagnostics = new List<WorkspaceDiagnostic>();
        var diagnosticCount = 0;
        var overflow = 0;
        var diagnosticFacts = CoverageWorkspaceDiagnosticFactsIndex.Create(snapshot.Root, snapshot.Files,
            admitted.CandidateGraph.Documents);
        CoverageSourceSemanticJoinEvidence? join = null;
        var stage = "workspace-open";
        MSBuildWorkspace? workspace = null;
        IDisposable? registration = null;
        try
        {
            workspace = MSBuildWorkspace.Create(properties);
            workspace.LoadMetadataForReferencedProjects = false;
            workspace.SkipUnrecognizedProjects = false;
            registration = workspace.RegisterWorkspaceFailedHandler(args =>
            {
                if (Interlocked.Increment(ref diagnosticCount) <= MaximumDiagnostics) observed.Enqueue(args.Diagnostic);
                else Interlocked.Exchange(ref overflow, 1);
            });
            await workspace.OpenProjectAsync(Child(snapshot.Root, request.EntryProject), cancellationToken: token).ConfigureAwait(false);
            var solution = workspace.CurrentSolution;
            Require(solution.Projects.Count() is > 0 and <= 32, "project-count-limit");
            var entry = solution.Projects.SingleOrDefault(project => Same(project.FilePath, Child(snapshot.Root, request.EntryProject)));
            var product = solution.Projects.SingleOrDefault(project => Same(project.FilePath, Child(snapshot.Root, request.ProductProject)));
            Require(entry is not null && product is not null && entry.ProjectReferences.Any(reference => reference.ProjectId == product.Id),
                "required-source-project-reference-missing");
            Require(Same(product.OutputFilePath, productDll) && Same(entry.OutputFilePath, testsDll), "coverage-output-project-mismatch");
            var compilations = new Dictionary<ProjectId, CSharpCompilation>();
            foreach (var project in solution.Projects.OrderBy(item => item.FilePath, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                stage = "project-compilation";
                var relative = ListedProject(snapshot, project.FilePath);
                // Roslyn 5.9 keeps its full-load predicate internal. Public
                // compilation/diagnostic checks do not prove project completeness.
                Require(project.SupportsCompilation, "csharp-compilation-unavailable");
                var compilation = await project.GetCompilationAsync(token).ConfigureAwait(false) as CSharpCompilation;
                Require(compilation is not null && project.ParseOptions is CSharpParseOptions, "csharp-compilation-unavailable");
                Require(compilation.Options.OptimizationLevel == OptimizationLevel.Release && compilation.Options.Platform == Platform.AnyCpu,
                    "observed-compilation-options-mismatch");
                var parse = (CSharpParseOptions)project.ParseOptions;
                Require(parse.PreprocessorSymbolNames.Contains("TRACE", StringComparer.Ordinal) &&
                    !parse.PreprocessorSymbolNames.Contains("DEBUG", StringComparer.Ordinal) &&
                    parse.PreprocessorSymbolNames.Contains("NET10_0", StringComparer.Ordinal) &&
                    parse.PreprocessorSymbolNames.Contains("WINDOWS", StringComparer.Ordinal) &&
                    parse.PreprocessorSymbolNames.All(symbol => ProbePaths.Token(symbol) == symbol), "observed-defines-mismatch");
                Require(compilation.SyntaxTrees.Count() <= 200_000 && compilation.References.Count() <= 4096, "compilation-input-limit");
                var generated = (await project.GetSourceGeneratedDocumentsAsync(token).ConfigureAwait(false)).ToArray();
                var generatedTrees = new HashSet<SyntaxTree>();
                foreach (var document in generated)
                {
                    var tree = await document.GetSyntaxTreeAsync(token).ConfigureAwait(false);
                    if (tree is not null) generatedTrees.Add(tree);
                }
                var trees = new List<CoverageWorkspaceTree>();
                long characters = 0;
                foreach (var tree in compilation.SyntaxTrees)
                {
                    var text = tree.GetText(token);
                    characters += text.Length;
                    Require(text.Length <= 16 * 1024 * 1024 && characters <= 128 * 1024 * 1024, "compilation-text-limit");
                    if (project.Id == product.Id && !generatedTrees.Contains(tree))
                    {
                        Require(Path.IsPathFullyQualified(tree.FilePath), "product-tree-outside-snapshot");
                        var relativeTree = Path.GetRelativePath(snapshot.Root, tree.FilePath).Replace('\\', '/');
                        Require(PortablePdbSource.RelativePath(relativeTree) && (snapshot.Files.ContainsKey(relativeTree) ||
                            sourceInput.GeneratedSourceRoots.Any(root => relativeTree.StartsWith(root + "/", StringComparison.Ordinal))),
                            "product-tree-outside-snapshot");
                    }
                    trees.Add(new(ProbePaths.Relative(snapshot.Root, tree.FilePath), HashText(tree.FilePath), text.ChecksumAlgorithm.ToString(),
                        Convert.ToHexStringLower(text.GetChecksum().AsSpan()), text.Length, generatedTrees.Contains(tree)));
                }
                foreach (var diagnostic in compilation.GetDiagnostics(token).Where(item => item.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error))
                {
                    Require(diagnostics.Count < MaximumDiagnostics, "diagnostic-count-limit");
                    var location = diagnostic.Location;
                    var position = location.IsInSource ? location.GetLineSpan().StartLinePosition : default;
                    diagnostics.Add(new("compilation", diagnostic.Severity.ToString(), ProbePaths.Token(diagnostic.Id),
                        location.IsInSource ? ProbePaths.Relative(snapshot.Root, location.SourceTree?.FilePath) : null,
                        location.IsInSource ? position.Line + 1 : null, location.IsInSource ? position.Character + 1 : null));
                }
                var framework = AttributeValue(compilation, "System.Runtime.Versioning.TargetFrameworkAttribute");
                var platform = AttributeValue(compilation, "System.Runtime.Versioning.TargetPlatformAttribute");
                Require(framework == ".NETCoreApp,Version=v10.0" && platform is not null && ProbePaths.Token(platform) == platform &&
                    platform.StartsWith("Windows", StringComparison.Ordinal),
                    "observed-target-framework-mismatch");
                projects.Add(new(relative, snapshot.Files[relative], ProbePaths.Relative(snapshot.Root, project.OutputFilePath),
                    parse.LanguageVersion.ToString(), Array.AsReadOnly(parse.PreprocessorSymbolNames.Order(StringComparer.Ordinal).ToArray()),
                    compilation.Options.OptimizationLevel.ToString(), compilation.Options.Platform.ToString(),
                    Array.AsReadOnly(project.ProjectReferences.Select(reference => ListedProject(snapshot, solution.GetProject(reference.ProjectId)?.FilePath))
                        .Order(StringComparer.Ordinal).ToArray()), project.MetadataReferences.Count, ReferenceFingerprint(compilation), project.AnalyzerReferences.Count,
                    project.AdditionalDocuments.Count(), project.AnalyzerConfigDocuments.Count(), generated.Length, framework, platform,
                    Array.AsReadOnly(trees.OrderBy(item => item.PathSha256, StringComparer.Ordinal).ToArray())));
                compilations.Add(project.Id, compilation);
            }
            var entryCompilation = compilations[entry.Id];
            Require(MetadataValue(entryCompilation, "RegressionBuildSdk") == request.SdkVersion &&
                MetadataValue(entryCompilation, "RegressionBuildConfiguration") == request.Configuration, "evaluated-sdk-or-configuration-mismatch");
            stage = "semantic-inspection";
            if (!diagnostics.Any(item => item.Kind == "Error") && !workspace.Diagnostics.Any(item => item.Kind == WorkspaceDiagnosticKind.Failure) &&
                !observed.Any(item => item.Kind == WorkspaceDiagnosticKind.Failure) && Volatile.Read(ref overflow) == 0)
                join = CoverageSourceSemanticJoin.Inspect(admitted.CandidateGraph, compilations[product.Id], snapshot.Root,
                    sourceInput.GeneratedSourceRoots, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (OrderedProfileCoverageWorkspaceJoinException error) { failures.Add(new(stage, error.Code)); }
        catch (CoverageSourceSemanticJoinException error) { failures.Add(new(stage, error.Code)); }
        catch (Exception error) { failures.Add(new(stage, "exception", ProbePaths.Token(error.GetType().FullName))); }
        finally
        {
            try { if (workspace is not null) workspaceDiagnostics.AddRange(workspace.Diagnostics.Take(MaximumDiagnostics + 1)); }
            catch (Exception error) { failures.Add(new("workspace-diagnostics", "exception", ProbePaths.Token(error.GetType().FullName))); }
            try { workspace?.Dispose(); }
            catch (Exception error) { failures.Add(new("workspace-disposal", "exception", ProbePaths.Token(error.GetType().FullName))); }
            try { registration?.Dispose(); }
            catch (Exception error) { failures.Add(new("workspace-diagnostic-release", "exception", ProbePaths.Token(error.GetType().FullName))); }
        }
        foreach (var diagnostic in observed)
            if (!workspaceDiagnostics.Contains(diagnostic)) workspaceDiagnostics.Add(diagnostic);
        if (workspaceDiagnostics.Count > MaximumDiagnostics || Volatile.Read(ref overflow) != 0)
            failures.Add(new("workspace-diagnostics", "diagnostic-count-limit"));
        diagnostics.AddRange(workspaceDiagnostics.Take(MaximumDiagnostics).Select(item =>
            new CoverageWorkspaceDiagnostic("workspace", item.Kind.ToString(), ProbePaths.DiagnosticCode(item.Message))
            { Facts = diagnosticFacts.Describe(item.Message) }));
        if (diagnostics.Any(item => item.Kind is "Failure" or "Error")) failures.Add(new("diagnostics", "workspace-or-compilation-errors"));
        if (join is null && failures.Count == 0) failures.Add(new("semantic-inspection", "not-attempted"));
        if (failures.Count != 0) join = null;
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { toolchain, properties, projects }, Json)));
        var evidence = new OrderedProfileCoverageWorkspaceEvidence(1, admitted.Identity, admitted.CandidateGraph.Product,
            admitted.CandidateGraph.AggregateSha256, admitted.Members.Count, admitted.CandidateGraph.Scopes,
            snapshot.ContextSha256, snapshot.SourceFingerprint,
            toolchain, new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(properties), projects.AsReadOnly(), fingerprint,
            diagnostics.AsReadOnly(), failures.AsReadOnly(), join);
        if (failures.Count != 0) throw new OrderedProfileCoverageWorkspaceJoinException("workspace-rejected", evidence);
        return evidence;
    }

    internal static SnapshotLease OpenSnapshot(CoverageSourceIdentity source, IReadOnlyList<PortablePdbSourceFile> tracked,
        OrderedProfileCoverageWorkspaceRequest request, CancellationToken token)
    {
        var guards = new List<FileStream>();
        try
        {
            Require(request is not null && request.SchemaVersion == 1 && Hash(request.SnapshotContextSha256) &&
                request.SdkVersion == "10.0.403" && request.Configuration == "Release" && request.Platform == "AnyCPU" &&
                request.TargetFramework == "net10.0-windows" && request.Coverage && request.EntryProject == ProbePaths.TestProject &&
                request.ProductProject == ProbePaths.ProductProject && source is not null && Hash(source.Fingerprint) &&
                ObjectId(source.Tree) && ObjectId(source.Commit) && tracked is { Count: > 0 and <= 20_000 }, "workspace-request-invalid");
            var context = PortablePdbSource.OpenRead(Absolute(request.SnapshotContext), 16 * 1024 * 1024);
            guards.Add(context);
            var bytes = new byte[checked((int)context.Length)];
            context.ReadExactly(bytes);
            Require(Convert.ToHexStringLower(SHA256.HashData(bytes)) == request.SnapshotContextSha256, "snapshot-context-mismatch");
            CheckJson(bytes);
            var snapshot = JsonSerializer.Deserialize<SnapshotContext>(bytes, Json);
            Require(snapshot is not null && snapshot.SchemaVersion == 1 && snapshot.Tree == source.Tree && snapshot.SourceCommit == source.Commit &&
                snapshot.Files is { Count: > 0 and <= 20_000 }, "snapshot-context-mismatch");
            var root = Root(snapshot.Root);
            Require(snapshot.Files.Count == tracked.Count, "snapshot-inventory-mismatch");
            var inventory = new Dictionary<string, string>(StringComparer.Ordinal);
            var physical = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in tracked)
                Require(file is not null && PortablePdbSource.RelativePath(file.RelativePath) && Hash(file.Sha256) &&
                    physical.Add(file.RelativePath) && inventory.TryAdd(file.RelativePath, file.Sha256), "snapshot-inventory-mismatch");
            var rows = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in snapshot.Files)
                Require(file is not null && PortablePdbSource.RelativePath(file.Path) && rows.Add(file.Path) &&
                    inventory.TryGetValue(file.Path, out var hash) && hash == file.Sha256 && ObjectId(file.Blob) && file.Mode is "100644" or "100755",
                    "snapshot-inventory-mismatch");
            Require(inventory.ContainsKey("global.json") && inventory.ContainsKey(request.EntryProject) && inventory.ContainsKey(request.ProductProject),
                "required-snapshot-project-missing");
            using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var length = new byte[8];
            var buffer = new byte[64 * 1024];
            FileStream? global = null;
            long total = 0;
            foreach (var file in inventory.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                var input = PortablePdbSource.OpenRead(Child(root, file.Key), 256 * 1024 * 1024, allowEmpty: true);
                guards.Add(input);
                if (file.Key == "global.json") global = input;
                total += input.Length;
                Require(total <= 1024L * 1024 * 1024, "snapshot-size-limit");
                var name = Encoding.UTF8.GetBytes(file.Key);
                BinaryPrimitives.WriteInt64LittleEndian(length, name.Length); fingerprint.AppendData(length); fingerprint.AppendData(name);
                BinaryPrimitives.WriteInt64LittleEndian(length, input.Length); fingerprint.AppendData(length);
                using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                int read;
                while ((read = input.Read(buffer)) != 0)
                {
                    token.ThrowIfCancellationRequested();
                    digest.AppendData(buffer.AsSpan(0, read)); fingerprint.AppendData(buffer.AsSpan(0, read));
                }
                Require(Convert.ToHexStringLower(digest.GetHashAndReset()) == file.Value, "snapshot-file-mismatch");
            }
            var actual = Convert.ToHexStringLower(fingerprint.GetHashAndReset());
            Require(actual == source.Fingerprint, "snapshot-fingerprint-mismatch");
            Require(global is not null && global.Length <= 64 * 1024, "snapshot-sdk-mismatch");
            global.Position = 0;
            var globalBytes = new byte[checked((int)global.Length)];
            global.ReadExactly(globalBytes);
            CheckJson(globalBytes);
            using var globalJson = JsonDocument.Parse(globalBytes);
            var sdk = globalJson.RootElement.GetProperty("sdk");
            Require(sdk.GetProperty("version").GetString() == request.SdkVersion && sdk.GetProperty("rollForward").GetString() == "disable" &&
                sdk.GetProperty("allowPrerelease").ValueKind == JsonValueKind.False && sdk.EnumerateObject().Count() == 3, "snapshot-sdk-mismatch");
            return new(root, actual, request.SnapshotContextSha256, inventory, guards);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { DisposeAfterFailure(guards); throw; }
        catch (OrderedProfileCoverageWorkspaceJoinException) { DisposeAfterFailure(guards); throw; }
        catch (Exception error) when (error is PortablePdbSourceException or IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or JsonException or KeyNotFoundException or OverflowException or System.Security.SecurityException)
        { DisposeAfterFailure(guards); throw Rejected("snapshot-read-failed"); }
        catch { DisposeAfterFailure(guards); throw; }
    }

    internal sealed class SnapshotLease(string root, string fingerprint, string contextSha256,
        Dictionary<string, string> files, List<FileStream> guards) : IDisposable
    {
        private bool _disposed;
        internal string Root { get; } = root;
        internal string SourceFingerprint { get; } = fingerprint;
        internal string ContextSha256 { get; } = contextSha256;
        internal IReadOnlyDictionary<string, string> Files { get; } = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(files);
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var failed = false;
            foreach (var guard in guards) { try { guard.Dispose(); } catch { failed = true; } }
            if (failed) throw Rejected("snapshot-release-failed");
        }
    }

    private sealed record SnapshotContext(int SchemaVersion, string Root, string Tree, string SourceCommit, IReadOnlyList<SnapshotFile> Files);
    private sealed record SnapshotFile(string Path, string Sha256, string Blob, string Mode);

    private static string ListedProject(SnapshotLease snapshot, string? path)
    {
        Require(path is not null && Path.IsPathFullyQualified(path), "project-path-outside-snapshot");
        var relative = Path.GetRelativePath(snapshot.Root, path).Replace('\\', '/');
        Require(PortablePdbSource.RelativePath(relative) && snapshot.Files.ContainsKey(relative), "project-path-outside-snapshot");
        return relative;
    }

    private static string? AttributeValue(Compilation compilation, string name) => compilation.Assembly.GetAttributes()
        .Where(attribute => attribute.AttributeClass?.ToDisplayString() == name && attribute.ConstructorArguments.Length >= 1)
        .Select(attribute => attribute.ConstructorArguments[0].Value as string).SingleOrDefault();

    private static string? MetadataValue(Compilation compilation, string key) => compilation.Assembly.GetAttributes()
        .Where(attribute => attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute" &&
            attribute.ConstructorArguments.Length == 2 && attribute.ConstructorArguments[0].Value as string == key)
        .Select(attribute => attribute.ConstructorArguments[1].Value as string).SingleOrDefault();

    private static string ReferenceFingerprint(CSharpCompilation compilation)
    {
        long characters = 0;
        var identities = new List<string>();
        foreach (var reference in compilation.References)
        {
            Require(reference.Properties.Aliases.Length <= 128 && reference.Properties.Aliases.All(alias => alias.Length <= 1024),
                "reference-alias-limit");
            var symbol = compilation.GetAssemblyOrModuleSymbol(reference);
            Require(symbol is IAssemblySymbol, "reference-kind-unsupported");
            var identity = ((IAssemblySymbol)symbol).Identity.ToString();
            characters += identity.Length;
            Require(identity.Length <= 32_768 && characters <= 16 * 1024 * 1024, "reference-identity-limit");
            identities.Add(identity + "|" + reference.Properties.EmbedInteropTypes + "|" + string.Join(",", reference.Properties.Aliases.Order(StringComparer.Ordinal)));
        }
        return HashText(string.Join("\n", identities.Order(StringComparer.Ordinal)));
    }

    private static void CheckJson(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 64 });
        var names = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) names.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) names.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName && !names.Peek().Add(reader.GetString()!)) throw Rejected("duplicate-json-field");
        }
    }

    private static string Absolute(string path)
    {
        Require(path is not null && path.Length <= 32_768 && Path.IsPathFullyQualified(path), "invalid-path");
        var root = Path.GetPathRoot(path);
        Require(root is { Length: 3 } && char.IsAsciiLetter(root[0]) && root[1] == ':' && root[2] is '/' or '\\', "invalid-path");
        var relative = path[root.Length..].Replace('\\', '/').TrimEnd('/');
        Require(relative.Length == 0 || PortablePdbSource.RelativePath(relative), "invalid-path");
        return PortablePdbSource.AbsolutePath(path);
    }

    private static string Root(string path)
    {
        var root = Absolute(path);
        PortablePdbSource.RequireNoReparseAncestors(root, directory: true);
        return root;
    }

    private static string Child(string root, string relative)
    {
        Require(relative is not null && PortablePdbSource.RelativePath(relative), "invalid-path");
        return PortablePdbSource.ResolveSource(root, relative);
    }

    private static bool Same(string? left, string right) => left is not null && Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    private static string HashText(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static bool Hash(string value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool ObjectId(string value) => value is { Length: 40 or 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void DisposeAfterFailure(List<FileStream> guards) { foreach (var guard in guards) { try { guard.Dispose(); } catch { } } }
    private static OrderedProfileCoverageWorkspaceJoinException Rejected(string reason) => new(reason);
    private static void Require([DoesNotReturnIf(false)] bool condition, string reason) { if (!condition) throw Rejected(reason); }
}

// Optional lexical facts never establish a cause or change admission. All paths
// originate in the held snapshot/graph, never in diagnostic text or filesystem lookup.
internal sealed class CoverageWorkspaceDiagnosticFactsIndex
{
    internal const int MaximumMessageCharacters = 32 * 1024;
    internal const int MaximumScanCharacters = 1024 * 1024;
    internal const int MaximumKnownPaths = 40_000;
    internal const int MaximumPathCharacters = 32_768;
    internal const int MaximumKnownPathCharacters = 8 * 1024 * 1024;
    internal const int MaximumReportedPaths = 16;
    private static readonly IReadOnlyList<string> Unknown = Array.AsReadOnly(new[] { "unknown" });
    private readonly Dictionary<string, string>? _paths;
    private int _remainingCharacters = MaximumScanCharacters;

    private CoverageWorkspaceDiagnosticFactsIndex(Dictionary<string, string>? paths) => _paths = paths;

    internal static CoverageWorkspaceDiagnosticFactsIndex Create(string root,
        IReadOnlyDictionary<string, string> tracked, IReadOnlyList<CoverageCandidateDocument> documents)
    {
        if (tracked.Count > MaximumKnownPaths || documents.Count > MaximumKnownPaths - tracked.Count)
            return new(null);
        var paths = tracked.Keys.Concat(documents.Where(item =>
                item.Provenance.Provenance == BuildSourceFileProvenance.BuildGenerated)
            .Select(item => item.Provenance.SnapshotRelativePath)).ToArray();
        return Create(root, paths);
    }

    internal static CoverageWorkspaceDiagnosticFactsIndex Create(string root, IReadOnlyList<string> relativePaths)
    {
        if (root.Length > MaximumPathCharacters || !DriveAbsolute(root) || relativePaths.Count > MaximumKnownPaths)
            return new(null);
        var prefix = root.Replace('\\', '/').TrimEnd('/') + "/";
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long characters = 0;
        foreach (var relative in relativePaths)
        {
            if (relative is null || relative.Length > MaximumPathCharacters || !PortablePdbSource.RelativePath(relative))
                return new(null);
            var pathCharacters = (long)prefix.Length + relative.Length;
            characters += pathCharacters + relative.Length;
            if (pathCharacters > MaximumPathCharacters || characters > MaximumKnownPathCharacters) return new(null);
            paths.TryAdd(prefix + relative, relative);
        }
        return new(paths);
    }

    internal CoverageWorkspaceDiagnosticFacts Describe(string message)
    {
        var digest = MessageDigest(message);
        if (_paths is null || message.Length > MaximumMessageCharacters || message.Length > _remainingCharacters)
        {
            if (message.Length > _remainingCharacters) _remainingCharacters = 0;
            return UnknownFacts(digest);
        }
        _remainingCharacters -= message.Length;
        var hints = new List<string>();
        if (message.Contains("The process cannot access the file", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("because it is being used by another process", StringComparison.OrdinalIgnoreCase))
            hints.Add("sharing-violation-text");
        if (Token(message, "System.IO.IOException")) hints.Add("ioexception-type");
        if (Token(message, "0x80070020")) hints.Add("hresult-sharing-violation");
        if (Token(message, "0x80070021")) hints.Add("hresult-lock-violation");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index + 4 < message.Length; index++)
        {
            var quote = message[index];
            if (quote is not ('\'' or '"') || !DriveAbsolute(message.AsSpan(index + 1))) continue;
            var end = message.IndexOf(quote, index + 1);
            if (end < 0) continue;
            var length = end - index - 1;
            if (length <= MaximumPathCharacters &&
                _paths.TryGetValue(message.Substring(index + 1, length).Replace('\\', '/'), out var relative))
            {
                paths.Add(relative);
                if (paths.Count > MaximumReportedPaths) return UnknownFacts(digest);
            }
            index = end;
        }
        if (hints.Count == 0 && paths.Count == 0) return UnknownFacts(digest);
        return new(digest, hints.AsReadOnly(), Array.AsReadOnly(paths.Order(StringComparer.Ordinal).ToArray()));
    }

    private static CoverageWorkspaceDiagnosticFacts UnknownFacts(string digest) => new(digest, Unknown, Array.Empty<string>());

    private static bool DriveAbsolute(ReadOnlySpan<char> path) => path.Length >= 3 && char.IsAsciiLetter(path[0]) &&
        path[1] == ':' && path[2] is '/' or '\\';

    private static bool Token(string message, string value)
    {
        var start = 0;
        while ((start = message.IndexOf(value, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = start + value.Length;
            if ((start == 0 || !TokenCharacter(message[start - 1])) &&
                (end == message.Length || !TokenCharacter(message[end]))) return true;
            start = end;
        }
        return false;
    }

    private static bool TokenCharacter(char value) => char.IsAsciiLetterOrDigit(value) || value is '_' or '.' or '+' or '`';

    // Keep one encoder across chunks, including a surrogate pair that straddles
    // a boundary. Oversized messages retain their full UTF-8 digest, not a prefix.
    private static string MessageDigest(string message)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var encoder = Encoding.UTF8.GetEncoder();
        Span<byte> bytes = stackalloc byte[4096];
        var offset = 0;
        while (offset < message.Length)
        {
            var length = Math.Min(1024, message.Length - offset);
            encoder.Convert(message.AsSpan(offset, length), bytes, offset + length == message.Length,
                out var consumed, out var written, out _);
            hash.AppendData(bytes[..written]);
            offset += consumed;
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
