using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using FishAudio.Testing.Execution;

namespace FishAudio.Testing.Evidence;

// This is the caller's independent frozen build/discovery authority. Its digest
// is not authenticated by reading a collector's own manifest. SDK metadata may
// be unavailable on older tooling binaries; that absence remains explicit.
public sealed record CoverageBaselineBuildAuthority(string EvidenceId, string EvidenceSha256,
    string SdkVersion, string Configuration, string TargetFramework, string DotCoverToolSha256, string VstestConsoleSha256)
{
    public string? InstrumentationPolicyVersion { get; init; }
}
public sealed record CoverageBaselineCatalogAdmission(string Key, string DiscoveryPath, string DiscoverySha256,
    string SourceDirectory, string BuildDirectory, string BuildId, IReadOnlyList<CoverageBinaryBinding> Binaries);
public sealed record CoverageBaselineAdmission(int SchemaVersion, CoverageSourceIdentity Source,
    CoverageBaselineBuildAuthority BuildAuthority, IReadOnlyList<CoverageBaselineCatalogAdmission> Catalogs);
public sealed record CoverageBaselineIsolatedOwner(string CatalogKey, IsolatedCoverageSessionInput Input,
    IsolatedCoverageAdmission Expectation);
public sealed record CoverageBaselineOrderedGroup(string CatalogKey, OrderedProfileCoverageGroupInput Input,
    OrderedProfileCoverageGroupAdmission Expectation);
public sealed record CoverageBaselineCandidateInput(int SchemaVersion,
    IReadOnlyList<CoverageBaselineIsolatedOwner> IsolatedOwners, IReadOnlyList<CoverageBaselineOrderedGroup> OrderedGroups);
public sealed record CoverageBaselineCatalogEvidence(string Key, string DiscoverySha256, string StableMetadataSha256,
    string BuildId, IReadOnlyList<AdmittedCoverageBinary> Binaries, IReadOnlyList<DiscoveredNUnitCase> Cases,
    NUnitExecutionPlanResult Plan);
public sealed record CoverageBaselineCatalogBinding(string OwnerDiscoverySha256, string FreshDiscoverySha256,
    string StableMetadataSha256)
{
    public bool RuntimeIdsAreSessionLocal => true;
}
public sealed record CoverageBaselineIsolatedEvidence(string CatalogKey, CoverageBaselineCatalogBinding CatalogBinding,
    CaseCoverage Coverage);
public sealed record CoverageBaselineGroupEvidence(string CatalogKey, CoverageBaselineCatalogBinding CatalogBinding,
    OrderedProfileCoverageGroup Coverage);
public sealed record CoverageBaselineBinaryBuildMetadata(string CatalogKey, CoverageBinaryRole Role,
    string AssemblyName, string? Configuration, string? SdkVersion, string? RegressionConfiguration)
{
    public string MissingSdkAuthority => SdkVersion is null ? "unavailable-external-build-admission" : "observed-assembly-metadata";
}
public sealed record CoverageBaselineCandidateEvidence(int SchemaVersion, CoverageSourceIdentity Source,
    CoverageBaselineBuildAuthority BuildAuthority, IReadOnlyList<CoverageBaselineCatalogEvidence> Catalogs,
    IReadOnlyList<CoverageBaselineIsolatedEvidence> IsolatedOwners, IReadOnlyList<CoverageBaselineGroupEvidence> OrderedGroups,
    IReadOnlyList<CoverageBaselineBinaryBuildMetadata> BinaryBuildMetadata)
{
    // Complete only relative to the two independently supplied current catalogs,
    // provided build bindings, and reimported raw owners. This grants no reuse.
    public bool CoverageUnionComplete { get; init; } = true;
    public bool AllRequiredOwnersAdmitted => true;
    public bool DependencyRecordsComplete => IsolatedOwners.All(item => item.Coverage.Dependencies is not null) &&
        OrderedGroups.All(item => item.Coverage.Dependencies is not null);
    public string BuildAuthoritySource => "caller-independent-build-admission";
    public bool SdkProducedBinaryProven => false;
    public bool CompleteBaseline => false;
    public bool MainProvenanceVerified => false;
    public bool PublicationAuthorized => false;
    public bool SourceMappingsResolved => false;
    public bool DependencyClosureProven => false;
    public bool BuildInputClosureProven => false;
    public bool GeneratorInputClosureProven => false;
    public bool TiaSelectionAuthorized => false;
    public string DependencyUnknownAction => "future-full";
}
public sealed class CoverageBaselineCandidateException : Exception
{
    public string Code { get; }
    public IReadOnlyList<string> UnadmittedCanonicalIds { get; }
    internal CoverageBaselineCandidateException(string code, IReadOnlyList<string>? unadmitted = null) : base(code)
    { Code = code; UnadmittedCanonicalIds = unadmitted ?? []; }
}

// Read-only composition, not a runner, publication receipt, case registry or
// impact selector. Every owner is reimported from its own raw artifacts; an
// already serialized CaseCoverage or self-declared complete flag is not input.
public static class CoverageBaselineCandidate
{
    public static CoverageBaselineCandidateEvidence ReadValidated(CoverageBaselineCandidateInput input,
        CoverageBaselineAdmission expected) => ReadCore(input, expected, null, completeCatalogUnion: true);

    // The operational caller independently derives this selection from the
    // complete current FullCoveragePlan. Serialized evidence is never authority.
    internal static CoverageBaselineCandidateEvidence ReadValidatedSelection(CoverageBaselineCandidateInput input,
        CoverageBaselineAdmission expected, IReadOnlyCollection<string> selectedCanonicalIds) =>
        ReadCore(input, expected, selectedCanonicalIds, completeCatalogUnion: false);

    private static CoverageBaselineCandidateEvidence ReadCore(CoverageBaselineCandidateInput input,
        CoverageBaselineAdmission expected, IReadOnlyCollection<string>? selectedCanonicalIds, bool completeCatalogUnion)
    {
        var locks = new List<FileStream>();
        var pinned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var completed = false;
        try
        {
            Require(input is not null && expected is not null && input.SchemaVersion == 1 && expected.SchemaVersion == 1,
                "schema");
            Require(expected.Source is not null && GitHash(expected.Source.Commit) && GitHash(expected.Source.Tree) &&
                Hash(expected.Source.Fingerprint), "source-identity");
            var authority = expected.BuildAuthority;
            Require(authority is not null && Token(authority.EvidenceId) && Hash(authority.EvidenceSha256) &&
                authority.SdkVersion == "10.0.403" && authority.Configuration == "Release" &&
                authority.TargetFramework == "net10.0-windows" && Hash(authority.DotCoverToolSha256) &&
                Hash(authority.VstestConsoleSha256) && CoverageInstrumentationPolicy.IsSupported(authority.InstrumentationPolicyVersion), "build-authority");
            Require(expected.Catalogs is { Count: 2 } && input.IsolatedOwners is not null && input.OrderedGroups is not null,
                "catalog-or-owner-set");
            var catalogs = new Dictionary<string, CatalogState>(StringComparer.Ordinal);
            var required = new HashSet<string>(StringComparer.Ordinal);
            var stableByRawDigest = new Dictionary<string, string>(StringComparer.Ordinal);
            var testAssemblies = new HashSet<string>(StringComparer.Ordinal);
            var buildMetadata = new List<CoverageBaselineBinaryBuildMetadata>();
            foreach (var admission in expected.Catalogs)
            {
                Require(admission is not null && Token(admission.Key) && !catalogs.ContainsKey(admission.Key) &&
                    Hash(admission.BuildId) && Hash(admission.DiscoverySha256), "catalog-identity");
                _ = Root(admission.SourceDirectory);
                _ = Root(admission.BuildDirectory);
                Pin(admission.DiscoveryPath, admission.DiscoverySha256, locks, pinned);
                var binaries = ValidateBinaries(admission, authority, locks, pinned, buildMetadata);
                var tests = binaries.Single(item => item.Role == CoverageBinaryRole.Tests).Identity;
                Require(testAssemblies.Add(tests.AssemblyName), "duplicate-test-assembly");
                var cases = NUnitEvidence.ReadCatalog(admission.DiscoveryPath);
                Require(cases.Count is > 0 and <= 100_000, "catalog-size");
                foreach (var item in cases) Require(required.Add(item.CanonicalId), "ambiguous-canonical-id");
                var ids = cases.Select(item => item.CanonicalId).ToArray();
                var plan = NUnitExecutionPlan.ReadValidated(admission.DiscoveryPath, ids);
                var stable = StableMetadata(admission.DiscoveryPath);
                var unsupported = new List<string>();
                foreach (var phase in plan.Phases.Where(item => item.Kind == "ordered-profile"))
                {
                    // The existing group protocol owns its supported profiles.
                    // Do not duplicate its current native whitelist here.
                    try { _ = OrderedProfileCoveragePlan.ReadValidated(admission.DiscoveryPath, phase.Profile!); }
                    catch (OrderedProfileCoverageException)
                    { unsupported.AddRange(phase.Cases.Select(item => item.CanonicalId)); }
                }
                if (unsupported.Count != 0) throw Rejected("unsupported-ordered-profile", unsupported.AsReadOnly());
                var evidence = new CoverageBaselineCatalogEvidence(admission.Key, admission.DiscoverySha256, stable,
                    admission.BuildId, binaries, cases, plan);
                stableByRawDigest[admission.DiscoverySha256] = stable;
                catalogs.Add(admission.Key, new(admission, evidence, cases.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal),
                    plan.Phases.Where(item => item.Kind == "ordered-profile").SelectMany(item => item.Cases)
                        .Select(item => item.CanonicalId).ToHashSet(StringComparer.Ordinal)));
            }
            if (!completeCatalogUnion)
            {
                Require(selectedCanonicalIds is not null && selectedCanonicalIds.Count <= required.Count, "selection-count");
                var selected = new HashSet<string>(StringComparer.Ordinal);
                foreach (var id in selectedCanonicalIds)
                    Require(Token(id) && selected.Add(id) && required.Contains(id), "selection-identity");
                foreach (var phase in catalogs.Values.SelectMany(item => item.Evidence.Plan.Phases).Where(item => item.Kind == "ordered-profile"))
                {
                    var count = phase.Cases.Count(item => selected.Contains(item.CanonicalId));
                    Require(count == 0 || count == phase.Cases.Count, "selection-profile-split");
                }
                required = selected;
            }
            Require(input.IsolatedOwners.Count <= required.Count && input.OrderedGroups.Count <= required.Count &&
                ((long)input.IsolatedOwners.Count + input.OrderedGroups.Count > 0 || (!completeCatalogUnion && required.Count == 0)) &&
                (long)input.IsolatedOwners.Count + input.OrderedGroups.Count <= required.Count, "owner-count");
            var covered = new HashSet<string>(StringComparer.Ordinal);
            var sessions = new HashSet<string>(StringComparer.Ordinal);
            var isolated = new List<CoverageBaselineIsolatedEvidence>();
            var groups = new List<CoverageBaselineGroupEvidence>();
            var dependencyChecks = new List<(CoverageDependencyRecord Record, string SourceDirectory)>();
            foreach (var owner in input.IsolatedOwners)
            {
                Require(owner is not null && Token(owner.CatalogKey) && catalogs.TryGetValue(owner.CatalogKey, out _), "unknown-owner-catalog");
                var catalog = catalogs[owner.CatalogKey];
                var expectation = owner.Expectation;
                Require(owner.Input is not null && expectation is not null &&
                    expectation.Identity is not null && expectation.Identity.Source is not null &&
                    expectation.Supervisor is not null && expectation.Supervisor.FrameworkEnvelope is not null, "framework-owner-required");
                Require(expectation.DotCoverVersion == DotCoverAggregate.SupportedToolVersion &&
                    expectation.ToolSha256 == authority.DotCoverToolSha256, "owner-tool-binding");
                Require(expectation.InstrumentationPolicyVersion == authority.InstrumentationPolicyVersion, "owner-instrumentation-policy");
                ValidateOwnerBinding(expected.Source, catalog.Admission, expectation.Identity.Source, expectation.Identity.BuildId,
                    expectation.SourceDirectory, expectation.BuildDirectory, expectation.Binaries);
                Require(catalog.CasesById.ContainsKey(expectation.Identity.CanonicalId), "foreign-case");
                Require(required.Contains(expectation.Identity.CanonicalId), "unselected-owner");
                Require(!catalog.ProfileMembers.Contains(expectation.Identity.CanonicalId), "isolated-profile-member");
                CaseCoverage admitted;
                try { admitted = IsolatedCoverageSession.ReadValidated(owner.Input, expectation); }
                catch (IsolatedCoverageSessionException) { throw Rejected("isolated-owner-rejected"); }
                Require(sessions.Add(admitted.Identity.SessionId) && covered.Add(admitted.Identity.CanonicalId), "duplicate-owner");
                PinIsolated(owner.Input, expectation, admitted, authority, locks, pinned);
                if (admitted.Dependencies is { } isolatedDependencies) dependencyChecks.Add((isolatedDependencies, expectation.SourceDirectory));
                var binding = Reconcile(catalog, owner.Input.ArtifactDirectory, admitted.Artifacts, stableByRawDigest);
                isolated.Add(new(owner.CatalogKey, binding, admitted));
            }
            foreach (var owner in input.OrderedGroups)
            {
                Require(owner is not null && Token(owner.CatalogKey) && catalogs.TryGetValue(owner.CatalogKey, out _), "unknown-owner-catalog");
                var catalog = catalogs[owner.CatalogKey];
                var expectation = owner.Expectation;
                Require(owner.Input is not null && expectation is not null && expectation.Identity is not null && expectation.Identity.Source is not null, "group-owner-required");
                Require(expectation.DotCoverVersion == DotCoverAggregate.SupportedToolVersion &&
                    expectation.ToolSha256 == authority.DotCoverToolSha256, "owner-tool-binding");
                Require(expectation.InstrumentationPolicyVersion == authority.InstrumentationPolicyVersion, "owner-instrumentation-policy");
                ValidateOwnerBinding(expected.Source, catalog.Admission, expectation.Identity.Source, expectation.Identity.BuildId,
                    expectation.SourceDirectory, expectation.BuildDirectory, expectation.Binaries);
                OrderedProfileCoverageGroup admitted;
                try { admitted = OrderedProfileCoverageSession.ReadValidated(owner.Input, expectation); }
                catch (OrderedProfileCoverageException) { throw Rejected("group-owner-rejected"); }
                var phase = catalog.Evidence.Plan.Phases.SingleOrDefault(item => item.Kind == "ordered-profile" &&
                    item.Profile == admitted.Identity.Profile);
                Require(phase is not null && phase.RequireOrder && phase.MaximumConcurrency == 1 &&
                    phase.Cases.Select(StableCase).SequenceEqual(admitted.Members.Select(item => StableCase(item.Case)), StringComparer.Ordinal),
                    "group-member-order");
                Require(phase.Cases.All(item => required.Contains(item.CanonicalId)), "unselected-owner");
                Require(sessions.Add(admitted.Identity.SessionId), "duplicate-owner");
                foreach (var member in admitted.Members) Require(covered.Add(member.Case.CanonicalId), "duplicate-owner");
                PinGroup(owner.Input, expectation, admitted, authority, locks, pinned);
                if (admitted.Dependencies is { } groupDependencies) dependencyChecks.Add((groupDependencies, expectation.SourceDirectory));
                var binding = Reconcile(catalog, owner.Input.ArtifactDirectory, admitted.Artifacts, stableByRawDigest);
                groups.Add(new(owner.CatalogKey, binding, admitted));
            }
            var missing = required.Where(item => !covered.Contains(item)).Order(StringComparer.Ordinal).ToArray();
            if (missing.Length != 0) throw Rejected("missing-owner", missing);
            Require(covered.SetEquals(required), "owner-union");
            foreach (var check in dependencyChecks) CoverageDependencies.Recheck(check.Record, check.SourceDirectory, locks);
            completed = true;
            return new(1, expected.Source, authority, Array.AsReadOnly(catalogs.Values.Select(item => item.Evidence).ToArray()),
                isolated.AsReadOnly(), groups.AsReadOnly(), buildMetadata.AsReadOnly()) { CoverageUnionComplete = completeCatalogUnion };
        }
        catch (CoverageBaselineCandidateException) { throw; }
        catch (Exception error) when (error is CoverageDependencyException or NUnitEvidenceException or NUnitExecutionMetadataException or NUnitExecutionPlanException or
            BinaryIdentityException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or
            BadImageFormatException or NotSupportedException or OverflowException or KeyNotFoundException or JsonException)
        { throw Rejected("input-or-build-rejected"); }
        finally
        {
            var failed = false;
            foreach (var guard in locks) { try { guard.Dispose(); } catch { failed = true; } }
            if (completed && failed) throw Rejected("file-release-failed");
        }
    }

    private static IReadOnlyList<AdmittedCoverageBinary> ValidateBinaries(CoverageBaselineCatalogAdmission catalog,
        CoverageBaselineBuildAuthority authority, List<FileStream> locks, Dictionary<string, string> pinned,
        List<CoverageBaselineBinaryBuildMetadata> metadata)
    {
        Require(catalog.Binaries is { Count: 2 } && catalog.Binaries.All(item => item is not null && item.ExpectedIdentity is not null) &&
            catalog.Binaries.Select(item => item.Role).ToHashSet().SetEquals(new[] { CoverageBinaryRole.Product, CoverageBinaryRole.Tests }),
            "binary-set");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = new List<AdmittedCoverageBinary>();
        foreach (var binding in catalog.Binaries)
        {
            var dll = Child(catalog.BuildDirectory, binding.DllRelativePath);
            var pdb = Child(catalog.BuildDirectory, binding.PdbRelativePath);
            Require(paths.Add(dll) && paths.Add(pdb), "binary-alias");
            Pin(dll, binding.ExpectedIdentity.DllSha256, locks, pinned);
            Pin(pdb, binding.ExpectedIdentity.PdbSha256, locks, pinned);
            var actual = BinaryIdentity.ReadValidated(dll, pdb);
            Require(actual == binding.ExpectedIdentity, "binary-identity");
            output.Add(new(binding.Role, actual));
            var observed = ReadBuildMetadata(catalog.Key, binding.Role, dll, actual.AssemblyName);
            Require((observed.Configuration is null || observed.Configuration == authority.Configuration) &&
                (observed.RegressionConfiguration is null || observed.RegressionConfiguration == authority.Configuration) &&
                (observed.SdkVersion is null || observed.SdkVersion == authority.SdkVersion), "binary-build-metadata");
            metadata.Add(observed);
        }
        return output.AsReadOnly();
    }

    private static CoverageBaselineBinaryBuildMetadata ReadBuildMetadata(string key, CoverageBinaryRole role,
        string dll, string assemblyName)
    {
        using var stream = new FileStream(dll, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        string? configuration = null, sdk = null, regression = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var attributes = reader.GetAssemblyDefinition().GetCustomAttributes();
        Require(attributes.Count <= 4096, "binary-build-metadata-limit");
        foreach (var handle in attributes)
        {
            var attribute = reader.GetCustomAttribute(handle);
            EntityHandle type = attribute.Constructor.Kind switch
            {
                HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
                HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
                _ => default,
            };
            string? name = null;
            if (type.Kind == HandleKind.TypeReference)
            {
                var definition = reader.GetTypeReference((TypeReferenceHandle)type);
                name = reader.GetString(definition.Namespace) + "." + reader.GetString(definition.Name);
            }
            else if (type.Kind == HandleKind.TypeDefinition)
            {
                var definition = reader.GetTypeDefinition((TypeDefinitionHandle)type);
                name = reader.GetString(definition.Namespace) + "." + reader.GetString(definition.Name);
            }
            if (name is not ("System.Reflection.AssemblyConfigurationAttribute" or "System.Reflection.AssemblyMetadataAttribute")) continue;
            var blob = reader.GetBlobReader(attribute.Value);
            Require(blob.RemainingBytes is >= 4 and <= 32768, "binary-build-metadata-limit");
            Require(blob.ReadUInt16() == 1, "binary-build-metadata");
            var first = blob.ReadSerializedString();
            var second = name == "System.Reflection.AssemblyMetadataAttribute" ? blob.ReadSerializedString() : null;
            Require(blob.RemainingBytes == 2 && blob.ReadUInt16() == 0, "binary-build-metadata");
            var configurationAttribute = name == "System.Reflection.AssemblyConfigurationAttribute";
            var property = configurationAttribute ? "Configuration" : first;
            if (!configurationAttribute && property is not ("RegressionBuildSdk" or "RegressionBuildConfiguration")) continue;
            Require(property is not null && seen.Add(property), "binary-build-metadata");
            var value = configurationAttribute ? first : second;
            Require(Token(value), "binary-build-metadata");
            if (property == "Configuration") configuration = value;
            if (property == "RegressionBuildSdk") sdk = value;
            if (property == "RegressionBuildConfiguration") regression = value;
        }
        return new(key, role, assemblyName, configuration, sdk, regression);
    }

    private static void ValidateOwnerBinding(CoverageSourceIdentity source, CoverageBaselineCatalogAdmission catalog,
        CoverageSourceIdentity ownerSource, string buildId, string sourceDirectory, string buildDirectory,
        IReadOnlyList<CoverageBinaryBinding> binaries)
    {
        Require(ownerSource == source && buildId == catalog.BuildId && Same(sourceDirectory, catalog.SourceDirectory) &&
            Same(buildDirectory, catalog.BuildDirectory) && binaries is { Count: 2 } && catalog.Binaries.All(expected =>
                binaries.Count(actual => actual == expected) == 1), "owner-build-binding");
    }

    private static CoverageBaselineCatalogBinding Reconcile(CatalogState catalog, string artifactRoot,
        IReadOnlyList<CoverageArtifact> artifacts, Dictionary<string, string> stableByRawDigest)
    {
        var discovery = artifacts.Single(item => item.Kind == CoverageArtifactKind.Discovery);
        if (!stableByRawDigest.TryGetValue(discovery.Sha256, out var stable))
        {
            stable = StableMetadata(Child(artifactRoot, discovery.RelativePath));
            stableByRawDigest.Add(discovery.Sha256, stable);
        }
        Require(stable == catalog.Evidence.StableMetadataSha256, "fresh-catalog-reconciliation");
        return new(discovery.Sha256, catalog.Admission.DiscoverySha256, stable);
    }

    private static string StableMetadata(string discovery)
    {
        var policies = NUnitExecutionMetadata.ReadValidated(discovery);
        var document = NUnitEvidence.ReadDiscoveryDocument(discovery);
        var leaves = document.Descendants("test-case").ToDictionary(item => (string)item.Attribute("id")!, StringComparer.Ordinal);
        // Runtime numeric/platform IDs and observed run paths are not cross-run
        // keys. All canonical identities, full parameter names, inherited
        // execution policy and leaf declarations remain part of this comparison.
        var stable = policies.OrderBy(item => item.Case.CanonicalId, StringComparer.Ordinal).Select(policy =>
        {
            var leaf = leaves[policy.Case.PlatformId];
            var dependencyDeclarations = CoverageDependencies.DeclaredMetadata(leaf);
            foreach (var values in dependencyDeclarations.Values) Array.Sort(values, StringComparer.Ordinal);
            Require(leaf.Elements("properties").Count() <= 1, "duplicate-case-properties");
            var properties = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var group in leaf.Elements("properties").Elements("property").GroupBy(item => (string?)item.Attribute("name"), StringComparer.Ordinal))
            {
                Require(Token(group.Key), "case-property");
                var values = group.Select(item => (string?)item.Attribute("value")).ToArray();
                Require(values.All(Token), "case-property");
                properties.Add(group.Key!, values.Select(item => item!).Order(StringComparer.Ordinal).ToArray());
            }
            return new { policy.Case.CanonicalId, policy.Case.FullName, policy.Case.ClassName, policy.Case.MethodName,
                policy.FixtureFullName, policy.FreshHost, policy.Profile, policy.ProfileOrdinal,
                Resources = policy.ExclusiveResources.Order(StringComparer.Ordinal).ToArray(), Properties = properties,
                DependencyDeclarations = dependencyDeclarations };
        }).ToArray();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(stable))));
    }

    private static string StableCase(DiscoveredNUnitCase item) => JsonSerializer.Serialize(new
    { item.CanonicalId, item.FullName, item.ClassName, item.MethodName });

    private static void PinIsolated(IsolatedCoverageSessionInput input, IsolatedCoverageAdmission expected,
        CaseCoverage admitted, CoverageBaselineBuildAuthority authority, List<FileStream> locks, Dictionary<string, string> pinned)
    {
        PinArtifacts(input.ArtifactDirectory, admitted.Artifacts, locks, pinned);
        Pin(expected.ToolFile, expected.ToolSha256, locks, pinned);
        if (admitted.Dependencies is { } dependencies) CoverageDependencies.Recheck(dependencies, expected.SourceDirectory, locks);
        foreach (var process in admitted.Supervisor.Cover.OwnedProcesses)
            foreach (var item in process.Evidence) PinEvidence(input.ArtifactDirectory, item, locks, pinned);
        if (admitted.EmptyProduct is { } empty)
            foreach (var item in empty.Dependencies) Pin(Child(expected.SourceDirectory, item.RelativePath), item.Sha256, locks, pinned);
        var envelope = admitted.Supervisor.FrameworkEnvelope!;
        foreach (var item in new[] { envelope.DriverInvocation, envelope.DriverStdout, envelope.DriverStderr,
            envelope.ExecutionRequest, envelope.RunSettings, envelope.ControlContext, envelope.Supervisor, envelope.HostStart, envelope.HostEnd })
            PinEvidence(input.ArtifactDirectory, item, locks, pinned);
        Pin(Child(expected.BuildDirectory, envelope.DriverAssemblyRelativePath), envelope.DriverAssemblySha256, locks, pinned);
        PinCollection(Child(input.ArtifactDirectory, envelope.ExecutionRequest.RelativePath), authority, locks, pinned);
    }

    private static void PinGroup(OrderedProfileCoverageGroupInput input, OrderedProfileCoverageGroupAdmission expected,
        OrderedProfileCoverageGroup admitted, CoverageBaselineBuildAuthority authority, List<FileStream> locks, Dictionary<string, string> pinned)
    {
        PinArtifacts(input.ArtifactDirectory, admitted.Artifacts, locks, pinned);
        Pin(expected.ToolFile, expected.ToolSha256, locks, pinned);
        if (admitted.Dependencies is { } dependencies) CoverageDependencies.Recheck(dependencies, expected.SourceDirectory, locks);
        if (admitted.EmptyProduct is { } empty)
            foreach (var item in empty.Dependencies) Pin(Child(expected.SourceDirectory, item.RelativePath), item.Sha256, locks, pinned);
        var envelope = admitted.Supervisor.FrameworkEnvelope;
        foreach (var item in new[] { envelope.DriverInvocation, envelope.DriverStdout, envelope.DriverStderr,
            envelope.ExecutionRequest, envelope.RunSettings, envelope.ControlContext, envelope.Supervisor, envelope.ExportXml })
            PinEvidence(input.ArtifactDirectory, item, locks, pinned);
        Pin(Child(expected.BuildDirectory, envelope.DriverAssemblyRelativePath), envelope.DriverAssemblySha256, locks, pinned);
        PinCollection(Child(input.ArtifactDirectory, envelope.ExecutionRequest.RelativePath), authority, locks, pinned);
    }

    private static void PinCollection(string requestPath, CoverageBaselineBuildAuthority authority,
        List<FileStream> locks, Dictionary<string, string> pinned)
    {
        using var stream = new FileStream(requestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var request = JsonSerializer.Deserialize<FrameworkRunRequest>(stream, FrameworkExecutionDriver.JsonOptions);
        var launch = request?.ProfiledLaunch ?? request?.OrderedProfiledLaunch?.Collection;
        Require(launch is not null && launch.ToolSha256 == authority.DotCoverToolSha256 &&
            launch.VstestConsoleSha256 == authority.VstestConsoleSha256 &&
            launch.InstrumentationPolicyVersion == authority.InstrumentationPolicyVersion, "collection-toolchain-binding");
        Pin(launch.VstestConsoleAssembly, launch.VstestConsoleSha256, locks, pinned);
    }
    private static void PinArtifacts(string root, IReadOnlyList<CoverageArtifact> artifacts, List<FileStream> locks,
        Dictionary<string, string> pinned)
    {
        foreach (var item in artifacts)
        {
            var path = Child(root, item.RelativePath);
            Pin(path, item.Sha256, locks, pinned);
            Require(new FileInfo(path).Length == item.Length && new DateTimeOffset(File.GetLastWriteTimeUtc(path)) == item.LastWriteUtc,
                "owner-artifact-changed");
        }
    }

    private static void PinEvidence(string root, CoverageOwnedProcessEvidence item, List<FileStream> locks, Dictionary<string, string> pinned)
    {
        var path = Child(root, item.RelativePath);
        Pin(path, item.Sha256, locks, pinned);
        Require(new FileInfo(path).Length == item.Length && new DateTimeOffset(File.GetLastWriteTimeUtc(path)) == item.LastWriteUtc,
            "owner-evidence-changed");
    }

    private static void Pin(string path, string hash, List<FileStream> locks, Dictionary<string, string> pinned)
    {
        Require(Path.IsPathFullyQualified(path) && Hash(hash), "file-identity");
        path = Path.GetFullPath(path);
        if (pinned.TryGetValue(path, out var existing)) { Require(existing == hash, "conflicting-file-identity"); return; }
        Require(File.Exists(path) && (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0,
            "regular-file-required");
        _ = Root(Path.GetDirectoryName(path)!);
        var guard = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        locks.Add(guard);
        Require(Convert.ToHexStringLower(SHA256.HashData(guard)) == hash, "file-digest");
        guard.Position = 0;
        pinned.Add(path, hash);
    }

    private static string Root(string path)
    {
        Require(Path.IsPathFullyQualified(path) && Directory.Exists(path), "root");
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        for (var item = new DirectoryInfo(path); item is not null; item = item.Parent)
            Require((item.Attributes & FileAttributes.ReparsePoint) == 0, "reparse-root");
        return path;
    }

    private static string Child(string root, string relative)
    {
        root = Root(root);
        Require(Token(relative) && !Path.IsPathRooted(relative) && !relative.Contains('\\') && !relative.Contains(':') &&
            relative.Split('/').All(item => item is not ("" or "." or "..")), "relative-path");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        Require(path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "relative-path");
        return path;
    }

    private static bool Same(string first, string second) => Path.IsPathFullyQualified(first) && Path.IsPathFullyQualified(second) &&
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);
    private static bool Token(string? value) => value is { Length: > 0 and <= 4096 } && !value.Any(char.IsControl);
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(item => item is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool GitHash(string? value) => value is { Length: 40 or 64 } && value.All(item => item is >= '0' and <= '9' or >= 'a' and <= 'f');
    private sealed record CatalogState(CoverageBaselineCatalogAdmission Admission, CoverageBaselineCatalogEvidence Evidence,
        IReadOnlyDictionary<string, DiscoveredNUnitCase> CasesById, IReadOnlySet<string> ProfileMembers);
    private static CoverageBaselineCandidateException Rejected(string code, IReadOnlyList<string>? missing = null) =>
        new("coverage-baseline-candidate-" + code, missing);
    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    { if (!condition) throw Rejected(code); }
}
