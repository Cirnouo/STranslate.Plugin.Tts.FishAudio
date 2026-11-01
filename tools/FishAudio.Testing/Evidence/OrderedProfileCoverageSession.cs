using System.Text.Json;
using System.Xml.Linq;
using FishAudio.Testing.Execution;

namespace FishAudio.Testing.Evidence;

public sealed record CoverageProfileGroupIdentity(string RunId, string SessionId, string Profile,
    CoverageSourceIdentity Source, string BuildId);
public sealed record OrderedProfileCoverageGroupInput(int SchemaVersion, CoverageProfileGroupIdentity Identity,
    string ArtifactDirectory, IReadOnlyList<CoverageArtifact> Artifacts);
public sealed record OrderedProfileCoverageEnvelope(int SchemaVersion,
    CoverageOwnedProcessEvidence DriverInvocation, CoverageOwnedProcessEvidence DriverStdout,
    CoverageOwnedProcessEvidence DriverStderr, CoverageOwnedProcessEvidence ExecutionRequest,
    CoverageOwnedProcessEvidence RunSettings, CoverageOwnedProcessEvidence ControlContext,
    CoverageOwnedProcessEvidence Supervisor, CoverageOwnedProcessEvidence ExportXml,
    string DriverAssemblyRelativePath, string DriverAssemblySha256);
public sealed record OrderedProfileCoverageSupervisorReceipt(int SchemaVersion, CoverageProfileGroupIdentity Identity,
    DateTimeOffset StartedUtc, DateTimeOffset SealedUtc, CoverageProcessCompletion Cover,
    CoverageProcessCompletion Export, CoverageDiagnosticReview Diagnostics, IReadOnlyList<CoverageArtifact> Artifacts,
    OrderedProfileCoverageEnvelope FrameworkEnvelope);
// Expectations come directly from an independently observed live owner and its
// validated frozen source/build, never from the group's collector.json itself.
public sealed record OrderedProfileCoverageGroupAdmission(int SchemaVersion, CoverageProfileGroupIdentity Identity,
    string SourceDirectory, string BuildDirectory, IReadOnlyList<CoverageBinaryBinding> Binaries,
    string DiscoverySha256, string DotCoverVersion, string ToolFile, string ToolSha256,
    OrderedProfileCoverageSupervisorReceipt Supervisor)
{
    // Independent source responsibility, never an inference from a collector's
    // empty aggregate, execution resource label or compatibility trace.
    public EmptyProductCoverage? EmptyProduct { get; init; }
    public CoverageDependencyObservation? Dependencies { get; init; }
    public string? InstrumentationPolicyVersion { get; init; }
}
public sealed record OrderedProfileCoverageGroupMember(int Ordinal, DiscoveredNUnitCase Case,
    NUnitCaseResult Result, string ExecutionDependency, string DeclaredScene);
public sealed record OrderedProfileCoverageGroup(int SchemaVersion, CoverageProfileGroupIdentity Identity,
    IReadOnlyList<OrderedProfileCoverageGroupMember> Members, IReadOnlyList<AdmittedCoverageBinary> Binaries,
    IReadOnlyList<CoverageArtifact> Artifacts, DotCoverAggregateReport Aggregate,
    OrderedProfileFrameworkEvidenceRecord Execution, OrderedProfileCoverageSupervisorReceipt Supervisor)
{
    public EmptyProductCoverage? EmptyProduct { get; init; }
    public string AttributionMode => "ordered-profile-group";
    public bool IndividualCaseCoverage => false;
    public bool SourceMappingsResolved => false;
    public bool DependencyClosureProven => false;
    public bool CompleteBaseline => false;
    public CoverageDependencyRecord? Dependencies { get; init; }
    public string? InstrumentationPolicyVersion { get; init; }
}

// A separate input/result contract. No group is converted to CoverageSessionIdentity,
// CaseCoverage, native-per-test hits or individual isolated-case observations.
public static class OrderedProfileCoverageSession
{
    public static OrderedProfileCoverageGroup ReadValidated(OrderedProfileCoverageGroupInput input,
        OrderedProfileCoverageGroupAdmission expected) =>
        IsolatedCoverageSession.ReadOrderedProfileGroupValidated(input, expected);
}

// Share the established bounded file/binary/artifact helpers only. The original
// ReadValidated(IsolatedCoverageSessionInput, IsolatedCoverageAdmission) is unchanged.
public static partial class IsolatedCoverageSession
{
    internal static OrderedProfileCoverageGroup ReadOrderedProfileGroupValidated(OrderedProfileCoverageGroupInput input,
        OrderedProfileCoverageGroupAdmission expected)
    {
        var locks = new List<FileStream>();
        var admitted = false;
        try
        {
            Require(input is not null && expected is not null && input.SchemaVersion == 1 && expected.SchemaVersion == 1,
                "group-schema");
            var identity = expected.Identity;
            Require(identity is not null && IsOwnerId(identity.RunId) && IsOwnerId(identity.SessionId) &&
                IsHash(identity.BuildId) && identity.Source is not null && GitHash(identity.Source.Commit) &&
                GitHash(identity.Source.Tree) && IsHash(identity.Source.Fingerprint) && input.Identity == identity, "group-identity");
            var supervisor = expected.Supervisor;
            Require(supervisor is not null && supervisor.SchemaVersion == 1 && supervisor.Identity == identity &&
                supervisor.FrameworkEnvelope is not null, "group-supervisor");
            Require(Utc(supervisor.StartedUtc) && Utc(supervisor.SealedUtc) &&
                supervisor.SealedUtc >= supervisor.StartedUtc, "group-time");
            ValidatePhase(supervisor.Cover);
            ValidatePhase(supervisor.Export);
            Require(supervisor.Cover.StartedUtc >= supervisor.StartedUtc &&
                supervisor.Export.StartedUtc >= supervisor.Cover.FinishedUtc &&
                supervisor.Export.FinishedUtc <= supervisor.SealedUtc, "group-phase-order");
            // This group protocol does not declare supervised application children.
            // A future child-aware group protocol must independently extend this boundary.
            Require(supervisor.Cover.OwnedProcesses.Count == 0 && supervisor.Export.OwnedProcesses.Count == 0,
                "group-owned-app-host-unsupported");
            Require(supervisor.Diagnostics is not null &&
                supervisor.Diagnostics.Disposition is CoverageDiagnosticDisposition.Clean or CoverageDiagnosticDisposition.ReviewedWarnings &&
                Token(supervisor.Diagnostics.RuleId) && IsHash(supervisor.Diagnostics.RuleSourceSha256), "group-diagnostics");
            Require(expected.DotCoverVersion == DotCoverAggregate.SupportedToolVersion &&
                IsHash(expected.ToolSha256) && Digest(OpenAbsolute(expected.ToolFile, locks)) == expected.ToolSha256,
                "group-tool");
            var artifactRoot = Root(input.ArtifactDirectory);
            var buildRoot = Root(expected.BuildDirectory);
            var sourceRoot = Root(expected.SourceDirectory);
            Require(input.Artifacts is not null && input.Artifacts.Count == Enum.GetValues<CoverageArtifactKind>().Length &&
                supervisor.Artifacts is not null && supervisor.Artifacts.Count == input.Artifacts.Count &&
                input.Artifacts.All(item => item is not null && supervisor.Artifacts.Count(pin => pin == item) == 1),
                "group-artifact-set");
            var paths = new Dictionary<CoverageArtifactKind, string>();
            var relative = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var artifact in input.Artifacts)
            {
                Require(artifact is not null && Enum.IsDefined(artifact.Kind) && !paths.ContainsKey(artifact.Kind) &&
                    relative.Add(artifact.RelativePath) && IsHash(artifact.Sha256) && artifact.Length >= 0 &&
                    artifact.Length <= MaximumArtifactBytes && Utc(artifact.LastWriteUtc), "group-artifact-identity");
                Require(artifact.Kind == CoverageArtifactKind.Snapshot || artifact.Length <= 64 * 1024 * 1024,
                    "group-artifact-size");
                var path = Child(artifactRoot, artifact.RelativePath);
                var stream = OpenAbsolute(path, locks);
                Require(stream.Length == artifact.Length && Digest(stream) == artifact.Sha256 &&
                    new DateTimeOffset(File.GetLastWriteTimeUtc(path)) == artifact.LastWriteUtc, "group-artifact-mismatch");
                if (artifact.Kind == CoverageArtifactKind.Discovery)
                    Require(IsHash(expected.DiscoverySha256) && artifact.Sha256 == expected.DiscoverySha256, "group-discovery-build");
                else
                {
                    var export = artifact.Kind is CoverageArtifactKind.ExportProcess or CoverageArtifactKind.ExportStdout or
                        CoverageArtifactKind.ExportStderr or CoverageArtifactKind.ExportLog or CoverageArtifactKind.AggregateJson;
                    Require(artifact.LastWriteUtc >= (export ? supervisor.Export.StartedUtc : supervisor.StartedUtc) &&
                        artifact.LastWriteUtc <= supervisor.SealedUtc, "group-stale-artifact");
                    if (artifact.Kind == CoverageArtifactKind.Snapshot)
                        Require(artifact.LastWriteUtc <= supervisor.Export.StartedUtc, "group-snapshot-modified");
                }
                if (artifact.Kind is CoverageArtifactKind.Discovery or CoverageArtifactKind.NUnitXml or CoverageArtifactKind.Trx or
                    CoverageArtifactKind.Snapshot or CoverageArtifactKind.AggregateJson or CoverageArtifactKind.CoverInvocation or
                    CoverageArtifactKind.CoverProcess or CoverageArtifactKind.ExportInvocation or CoverageArtifactKind.ExportProcess)
                    Require(stream.Length > 0, "group-empty-artifact");
                paths.Add(artifact.Kind, path);
            }
            ValidateDiagnosticBindings(supervisor.Diagnostics, input.Artifacts);
            Require(expected.Binaries is { Count: 2 }, "group-binaries");
            var binaries = new List<AdmittedCoverageBinary>();
            var binaryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var roles = new HashSet<CoverageBinaryRole>();
            string? testDll = null;
            foreach (var binding in expected.Binaries)
            {
                Require(binding is not null && Enum.IsDefined(binding.Role) && roles.Add(binding.Role) &&
                    binding.ExpectedIdentity is not null, "group-binaries");
                var dll = Child(buildRoot, binding.DllRelativePath);
                var pdb = Child(buildRoot, binding.PdbRelativePath);
                Require(binaryPaths.Add(dll) && binaryPaths.Add(pdb), "group-binary-alias");
                _ = OpenAbsolute(dll, locks); _ = OpenAbsolute(pdb, locks);
                var actual = BinaryIdentity.ReadValidated(dll, pdb);
                Require(actual == binding.ExpectedIdentity, "group-binary-build");
                binaries.Add(new(binding.Role, actual));
                if (binding.Role == CoverageBinaryRole.Tests) testDll = dll;
            }
            var product = binaries.Single(item => item.Role == CoverageBinaryRole.Product).Identity;
            Require(testDll is not null, "group-test-binary");
            var plan = OrderedProfileCoveragePlan.ReadValidated(paths[CoverageArtifactKind.Discovery], identity.Profile);
            Require(plan.RawDiscoverySha256 == expected.DiscoverySha256, "group-plan-discovery");
            var catalog = NUnitEvidence.ReadCatalog(paths[CoverageArtifactKind.Discovery]);
            var results = NUnitEvidence.VerifyResults(catalog, plan.Members.Select(item => item.Case.CanonicalId).ToArray(),
                paths[CoverageArtifactKind.NUnitXml], paths[CoverageArtifactKind.Trx]);
            Require(results.PassedCount == plan.Members.Count && results.Cases.Count == plan.Members.Count, "group-terminal-set");
            ValidateOrderedGroupProcessRecord(paths[CoverageArtifactKind.CoverProcess], supervisor.Cover, cover: true);
            ValidateOrderedGroupProcessRecord(paths[CoverageArtifactKind.ExportProcess], supervisor.Export, cover: false);
            var (execution, framework) = IsolatedCoverageSession.ValidatedDependencyFramework.ReadOrderedValidated(expected, plan, artifactRoot, buildRoot, sourceRoot, testDll,
                paths, relative, binaryPaths, locks);
            CoverageDependencyRecord? dependencies = null;
            if (expected.Dependencies is { } dependencyExpectation)
            {
                var testsBinding = expected.Binaries.Single(item => item.Role == CoverageBinaryRole.Tests);
                dependencies = CoverageDependencies.Validate(dependencyExpectation, identity.Source, identity.BuildId, sourceRoot,
                    testDll, Child(buildRoot, testsBinding.PdbRelativePath), paths[CoverageArtifactKind.Discovery],
                    plan.Members.Select(item => item.Case).ToArray(), "ordered-profile-group", identity.RunId, identity.SessionId,
                    framework, locks);
            }
            var aggregate = DotCoverAggregate.ReadValidated(paths[CoverageArtifactKind.AggregateJson], product.AssemblyName,
                expected.DotCoverVersion);
            Require(aggregate.ReportSha256 == input.Artifacts.Single(item => item.Kind == CoverageArtifactKind.AggregateJson).Sha256,
                "group-aggregate");
            EmptyProductCoverage? emptyProduct = null;
            var empty = aggregate.TargetAssembly is null || aggregate.TargetAssembly.CoveredStatements == 0;
            if (empty && dependencies is not null)
                Require(dependencies.Observation.Cases.All(item => item.Responsibility != CoverageResponsibility.ProductExpected),
                    "empty-product-expected-hit-required");
            var frameworkOnly = dependencies is not null && CoverageDependencies.AllowsEmpty(dependencies) &&
                dependencies.Observation.Cases.All(item => item.Responsibility == CoverageResponsibility.FrameworkLifecycleOnly);
            if (empty && !(frameworkOnly && expected.EmptyProduct is null))
            {
                var explanation = expected.EmptyProduct;
                Require(explanation is not null && explanation.Dependencies is { Count: > 0 and <= 4096 },
                    "empty-product-reason-required");
                emptyProduct = new(explanation.Reason, Array.AsReadOnly(explanation.Dependencies.ToArray()));
                ValidateEmptyProduct(emptyProduct, sourceRoot, locks);
                ValidateEmptyOrderedGroupDeclaration(paths[CoverageArtifactKind.Discovery], plan, emptyProduct, sourceRoot);
            }
            else if (!empty)
            {
                Require(expected.EmptyProduct is null, "group-unexpected-empty-product-explanation");
                CoverageDependencies.ValidateNonempty(dependencies);
            }
            if (dependencies is not null) CoverageDependencies.Recheck(dependencies, sourceRoot, locks);
            var byId = results.Cases.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
            // No report methods appear on a member. DeclaredScene comes only
            // from raw framework class/method metadata, not semantic/source closure.
            var members = plan.Members.Select(item => new OrderedProfileCoverageGroupMember(item.Ordinal, item.Case,
                byId[item.Case.CanonicalId], plan.ExecutionDependency,
                item.Case.ClassName + "::" + item.Case.MethodName)).ToArray();
            admitted = true;
            return new(1, identity, Array.AsReadOnly(members), binaries.AsReadOnly(),
                Array.AsReadOnly(input.Artifacts.ToArray()), aggregate, execution, supervisor)
                { EmptyProduct = emptyProduct, Dependencies = dependencies, InstrumentationPolicyVersion = expected.InstrumentationPolicyVersion };
        }
        catch (OrderedProfileCoverageException) { throw; }
        catch (CoverageDependencyException error) { throw new OrderedProfileCoverageException("ordered-profile-coverage-" + error.Code); }
        catch (IsolatedCoverageSessionException error)
        { throw new OrderedProfileCoverageException("ordered-profile-coverage-" + error.Code); }
        catch (NUnitEvidenceException)
        { throw new OrderedProfileCoverageException("ordered-profile-coverage-test-evidence"); }
        catch (BinaryIdentityException)
        { throw new OrderedProfileCoverageException("ordered-profile-coverage-binary-evidence"); }
        catch (DotCoverAggregateException)
        { throw new OrderedProfileCoverageException("ordered-profile-coverage-aggregate-evidence"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or JsonException or OverflowException or KeyNotFoundException or FormatException)
        { throw new OrderedProfileCoverageException("ordered-profile-coverage-invalid-input-or-file"); }
        finally
        {
            var failed = false;
            foreach (var guard in locks) { try { guard.Dispose(); } catch { failed = true; } }
            if (admitted && failed) throw new OrderedProfileCoverageException("ordered-profile-coverage-file-release");
        }
    }

    private static void ValidateEmptyOrderedGroupDeclaration(string discovery, OrderedProfileCoveragePlanRecord plan,
        EmptyProductCoverage explanation, string sourceRoot)
    {
        // The catalog and its raw digest were independently admitted and remain
        // held. Read this bounded document once, then require each selected leaf's
        // own declaration; fixture resource labels cannot supply responsibility.
        var document = NUnitEvidence.ReadDiscoveryDocument(discovery);
        var leaves = document.Descendants("test-case").ToDictionary(item => (string?)item.Attribute("id") ?? "", StringComparer.Ordinal);
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in plan.Members)
        {
            var leaf = leaves[member.Case.PlatformId];
            Require(leaf.Elements("properties").Count() <= 1, "group-invalid-empty-product-declaration");
            var properties = leaf.Elements("properties").Elements("property").ToArray();
            var reasons = properties.Where(item => (string?)item.Attribute("name") == "FishAudioEmptyProductCoverage").ToArray();
            Require(reasons.Length > 0, "group-empty-product-declaration-required");
            Require(reasons.Length == 1 && (string?)reasons[0].Attribute("value") == "RepositoryResourcesOnly",
                "group-invalid-empty-product-declaration");
            var dependencies = properties.Where(item => (string?)item.Attribute("name") == "FishAudioRepositoryDependency")
                .Select(item => (string?)item.Attribute("value") ?? "").ToArray();
            Require(dependencies.Length is > 0 and <= 4096, "group-invalid-repository-dependency-declaration");
            var memberPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dependency in dependencies)
            {
                _ = Child(sourceRoot, dependency);
                Require(memberPaths.Add(dependency), "group-invalid-repository-dependency-declaration");
                declared.Add(dependency);
                Require(declared.Count <= 4096, "group-invalid-repository-dependency-declaration");
            }
        }
        Require(declared.Count == explanation.Dependencies.Count &&
            declared.SetEquals(explanation.Dependencies.Select(item => item.RelativePath)),
            "group-empty-product-dependency-declaration-mismatch");
    }

    internal static OrderedProfileFrameworkEvidenceRecord ValidateOrderedProfileEnvelope(
        OrderedProfileCoverageGroupAdmission expected, OrderedProfileCoveragePlanRecord plan,
        string artifactRoot, string buildRoot, string sourceRoot, string testDll,
        IReadOnlyDictionary<CoverageArtifactKind, string> paths, HashSet<string> relative,
        HashSet<string> binaryPaths, List<FileStream> locks)
    {
        var envelope = expected.Supervisor.FrameworkEnvelope;
        Require(envelope.SchemaVersion == 1, "group-framework-envelope-schema");
        var pins = new[] { envelope.DriverInvocation, envelope.DriverStdout, envelope.DriverStderr, envelope.ExecutionRequest,
            envelope.RunSettings, envelope.ControlContext, envelope.Supervisor, envelope.ExportXml };
        var occupied = new HashSet<string>(relative.Select(item => Child(artifactRoot, item)), StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();
        for (var index = 0; index < pins.Length; index++)
        {
            var pin = pins[index];
            Require(pin is not null && IsHash(pin.Sha256) && pin.Length >= 0 && pin.Length <= 64 * 1024 * 1024 &&
                Utc(pin.LastWriteUtc) && relative.Add(pin.RelativePath) &&
                pin.LastWriteUtc >= (index == 7 ? expected.Supervisor.Export.StartedUtc : expected.Supervisor.StartedUtc) &&
                pin.LastWriteUtc <= (index == 7 ? expected.Supervisor.Export.FinishedUtc : expected.Supervisor.Cover.FinishedUtc),
                "group-framework-pin");
            var path = Child(artifactRoot, pin.RelativePath);
            Require(occupied.Add(path), "group-framework-alias");
            var guard = OpenAbsolute(path, locks);
            Require(guard.Length == pin.Length && Digest(guard) == pin.Sha256 &&
                new DateTimeOffset(File.GetLastWriteTimeUtc(path)) == pin.LastWriteUtc, "group-framework-pin-mismatch");
            files.Add(path);
        }
        var phaseRoot = Path.GetDirectoryName(paths[CoverageArtifactKind.CoverProcess])!;
        Require(SameFrameworkPath(files[0], Path.Combine(phaseRoot, "argv.json")) &&
            SameFrameworkPath(files[1], Path.Combine(phaseRoot, "stdout.log")) &&
            SameFrameworkPath(files[2], Path.Combine(phaseRoot, "stderr.log")) &&
            Path.GetFileName(files[6]) == "supervisor.json", "group-outer-phase");
        using (var outer = ReadJson(paths[CoverageArtifactKind.CoverProcess]))
        {
            var phase = outer.RootElement;
            Require(phase.GetProperty("stdoutDrained").GetBoolean() && phase.GetProperty("stderrDrained").GetBoolean() &&
                phase.GetProperty("ownedTreeCompleted").GetBoolean() && !phase.GetProperty("cleanupFailed").GetBoolean() &&
                phase.GetProperty("ownership").GetString() == "windows-job-before-target-go" &&
                phase.GetProperty("rootProcessId").GetInt32() > 0 && phase.GetProperty("metadataTimeoutMilliseconds").GetInt32() == 0,
                "group-outer-process");
        }
        var driver = Child(buildRoot, envelope.DriverAssemblyRelativePath);
        Require(IsHash(envelope.DriverAssemblySha256) &&
            Path.GetFileName(driver).Equals("FishAudio.Testing.dll", StringComparison.OrdinalIgnoreCase) &&
            !occupied.Contains(driver) && (!binaryPaths.Contains(driver) ||
                IsBoundFrameworkToolProductDriver(driver, envelope.DriverAssemblySha256, buildRoot, expected.Binaries, locks)) &&
            Digest(OpenAbsolute(driver, locks)) == envelope.DriverAssemblySha256, "group-driver-binary");
        using var requestDocument = ReadJson(files[3]);
        var request = requestDocument.RootElement.Deserialize<FrameworkRunRequest>(FrameworkExecutionDriver.JsonOptions);
        Require(request is not null && request.SchemaVersion == 1 && request.RunId == expected.Identity.RunId &&
            request.SourceTree == expected.Identity.Source.Tree && request.SourceFingerprint == expected.Identity.Source.Fingerprint &&
            SameFrameworkPath(request.WorkingDirectory, sourceRoot) && SameFrameworkPath(request.TestAssembly, testDll) &&
            request.TestAssemblySha256 == expected.Binaries.Single(item => item.Role == CoverageBinaryRole.Tests).ExpectedIdentity.DllSha256 &&
            request.ProfiledLaunch is null && request.OrderedProfiledLaunch is not null, "group-framework-request");
        var collection = request.OrderedProfiledLaunch.Collection;
        Require(collection is not null && collection.SessionId == expected.Identity.SessionId &&
            SameFrameworkPath(collection.ToolAssembly, expected.ToolFile) && collection.ToolSha256 == expected.ToolSha256 &&
            Path.GetFileName(collection.ToolAssembly).Equals("dotCover.dll", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(collection.VstestConsoleAssembly).Equals("vstest.console.dll", StringComparison.OrdinalIgnoreCase) &&
            IsHash(collection.VstestConsoleSha256) &&
            Digest(OpenAbsolute(collection.VstestConsoleAssembly, locks)) == collection.VstestConsoleSha256, "group-framework-collection");
        ValidateInstrumentationBinding(collection, expected.InstrumentationPolicyVersion, buildRoot, expected.Binaries);
        _ = OrderedProfileCoveragePlan.ValidateSelection(paths[CoverageArtifactKind.Discovery], expected.Identity.Profile,
            request.OrderedProfiledLaunch.RawDiscoverySha256, request.Cases, request.MaximumConcurrency, request.RequireOrder);
        Require(request.OrderedProfiledLaunch.Profile == expected.Identity.Profile, "group-request-profile");
        var output = Path.GetDirectoryName(files[6])!;
        Require(SameFrameworkPath(request.OutputDirectory, output) && SameFrameworkPath(request.RunSettings, files[4]) &&
            SameFrameworkPath(request.DiscoveryPath, paths[CoverageArtifactKind.Discovery]) &&
            SameFrameworkPath(request.ResultXmlPath, paths[CoverageArtifactKind.NUnitXml]) &&
            SameFrameworkPath(request.TrxPath, paths[CoverageArtifactKind.Trx]) &&
            SameFrameworkPath(collection.SnapshotPath, paths[CoverageArtifactKind.Snapshot]) &&
            SameFrameworkPath(collection.LogPath, paths[CoverageArtifactKind.CoverLog]) &&
            SameFrameworkPath(files[5], Path.Combine(output, "control-context.json")) &&
            SameFrameworkPath(paths[CoverageArtifactKind.CoverInvocation], Path.Combine(output, "invocation.json")) &&
            SameFrameworkPath(paths[CoverageArtifactKind.CoverStdout], Path.Combine(output, "stdout.bin")) &&
            SameFrameworkPath(paths[CoverageArtifactKind.CoverStderr], Path.Combine(output, "stderr.bin")) &&
            new[] { request.ResultXmlPath, request.TrxPath, collection.SnapshotPath, collection.LogPath }
                .All(path => FrameworkWithin(path, output)), "group-framework-paths");
        using var contextDocument = ReadJson(files[5]);
        var context = contextDocument.RootElement.Deserialize<FrameworkClientContext>(FrameworkExecutionDriver.JsonOptions);
        Require(context is not null, "group-control-context");
        ValidateOrderedGroupInvocations(expected.Identity, request, context, driver, files[3], files[0], files[5], files[7], paths);
        using var supervisor = ReadOrderedGroupJson(files[6]);
        return OrderedProfileFrameworkEvidence.ReadValidated(supervisor.RootElement, request, context, plan,
            envelope.ExecutionRequest.Sha256, expected.Supervisor.Artifacts.Single(item =>
                item.Kind == CoverageArtifactKind.CoverInvocation).Sha256,
            expected.Supervisor.Artifacts.Single(item => item.Kind == CoverageArtifactKind.NUnitXml).Sha256,
            expected.Supervisor.Artifacts.Single(item => item.Kind == CoverageArtifactKind.Trx).Sha256);
    }

    private static void ValidateOrderedGroupInvocations(CoverageProfileGroupIdentity identity, FrameworkRunRequest request,
        FrameworkClientContext context, string driver, string requestPath, string outerPath, string contextPath, string exportXml,
        IReadOnlyDictionary<CoverageArtifactKind, string> paths)
    {
        var launch = request.OrderedProfiledLaunch!.Collection;
        using var outer = ReadJson(outerPath);
        var value = outer.RootElement;
        Require(value.GetProperty("schemaVersion").GetInt32() == 1 &&
            value.GetProperty("sessionRunId").GetString() == identity.SessionId &&
            value.GetProperty("coverageProfile").GetString() == identity.Profile &&
            !value.TryGetProperty("canonicalCaseId", out _) &&
            SameFrameworkPath(value.GetProperty("executable").GetString(), request.DotnetExecutable) &&
            SameFrameworkPath(value.GetProperty("workingDirectory").GetString(), request.WorkingDirectory) &&
            Arguments(value).SequenceEqual(new[] { driver, "framework-run", "--request", requestPath }, StringComparer.Ordinal),
            "group-driver-invocation");
        using var inner = ReadJson(paths[CoverageArtifactKind.CoverInvocation]);
        value = inner.RootElement;
        string[] target = [launch.ToolAssembly, "cover", "--target-executable", request.DotnetExecutable,
            "--snapshot-output", launch.SnapshotPath, "--log-file", launch.LogPath,
            .. CoverageInstrumentationPolicy.Arguments(launch.InstrumentationPolicyVersion), "--", "exec",
            launch.VstestConsoleAssembly, request.TestAssembly, "--Settings:" + request.RunSettings,
            "--logger:trx;LogFileName=" + Path.GetFileName(request.TrxPath),
            "--ResultsDirectory:" + Path.GetDirectoryName(request.TrxPath)];
        Require(value.GetProperty("schemaVersion").GetInt32() == 1 &&
            value.GetProperty("sessionRunId").GetString() == identity.SessionId &&
            value.GetProperty("coverageProfile").GetString() == identity.Profile &&
            !value.TryGetProperty("canonicalCaseId", out _) &&
            SameFrameworkPath(value.GetProperty("executable").GetString(), request.DotnetExecutable) &&
            SameFrameworkPath(value.GetProperty("workingDirectory").GetString(), request.WorkingDirectory) &&
            Arguments(value).SequenceEqual(target, StringComparer.Ordinal), "group-cover-invocation");
        var control = value.GetProperty("driverControl");
        Require(context.PipeName is { Length: 43 } &&
            control.GetProperty("pipeName").GetString() == context.PipeName[..^5] + "-launcher" &&
            control.GetProperty("nonce").GetString() == context.Nonce &&
            control.GetProperty("runId").GetString() == request.RunId &&
            control.GetProperty("sourceTree").GetString() == request.SourceTree, "group-driver-control");
        var actual = value.GetProperty("environment").Deserialize<Dictionary<string, string>>(FrameworkExecutionDriver.JsonOptions);
        var declared = request.Environment?.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "FISHAUDIO_COVERAGE_SESSION_ID", "FISHAUDIO_COVERAGE_RUN_ID", "FISHAUDIO_COVERAGE_CASE_ID",
            "FISHAUDIO_COVERAGE_SOURCE_TREE", "FISHAUDIO_COVERAGE_EVIDENCE_ROOT" })
            Require(declared.TryGetValue(name, out var text) && text == "", "group-single-case-context-forbidden");
        Require(!declared.ContainsKey("FISHAUDIO_FRAMEWORK_CONTROL_CONTEXT") &&
            !declared.ContainsKey("FISHAUDIO_FRAMEWORK_FINALIZATION_TEST_FAULT") &&
            !declared.ContainsKey("FISHAUDIO_SNAPSHOT_CONTEXT"), "group-environment");
        declared.Add("FISHAUDIO_FRAMEWORK_CONTROL_CONTEXT", contextPath);
        Require(actual is not null && actual.Count == declared.Count &&
            declared.All(item => actual.TryGetValue(item.Key, out var text) && text == item.Value), "group-environment");
        using var export = ReadJson(paths[CoverageArtifactKind.ExportInvocation]);
        value = export.RootElement;
        string[] report = [launch.ToolAssembly, "report", "--snapshot-source", launch.SnapshotPath,
            "--json-report-output", paths[CoverageArtifactKind.AggregateJson], "--xml-report-output", exportXml,
            "--log-file", paths[CoverageArtifactKind.ExportLog]];
        Require(value.GetProperty("schemaVersion").GetInt32() == 1 &&
            value.GetProperty("sessionRunId").GetString() == identity.SessionId &&
            value.GetProperty("coverageProfile").GetString() == identity.Profile &&
            !value.TryGetProperty("canonicalCaseId", out _) &&
            SameFrameworkPath(value.GetProperty("executable").GetString(), request.DotnetExecutable) &&
            SameFrameworkPath(value.GetProperty("workingDirectory").GetString(), request.WorkingDirectory) &&
            Arguments(value).SequenceEqual(report, StringComparer.Ordinal), "group-export-invocation");
    }

    private static void ValidateOrderedGroupProcessRecord(string path, CoverageProcessCompletion completion, bool cover)
    {
        ValidateProcessRecord(path, completion);
        using var document = ReadJson(path);
        var phase = document.RootElement;
        Require(phase.GetProperty("stdoutDrained").GetBoolean() && phase.GetProperty("stderrDrained").GetBoolean() &&
            phase.GetProperty("ownedTreeCompleted").GetBoolean() && !phase.GetProperty("cleanupFailed").GetBoolean() &&
            phase.GetProperty("ownership").GetString() == "windows-job-before-target-go" &&
            phase.GetProperty("rootProcessId").GetInt32() > 0 &&
            phase.GetProperty("metadataTimeoutMilliseconds").GetInt32() == (cover ? 0 : 120000),
            "group-process-record");
    }

    private static JsonDocument ReadOrderedGroupJson(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(stream.Length is > 0 and <= 64 * 1024 * 1024, "group-metadata-size");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 64 });
        var names = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) names.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) names.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName)
                Require(names.Peek().Add(reader.GetString()!), "group-duplicate-property");
        }
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
    }

    private static bool IsOwnerId(string value) => Guid.TryParseExact(value, "N", out var id) &&
        id != Guid.Empty && id.ToString("N") == value;
}
