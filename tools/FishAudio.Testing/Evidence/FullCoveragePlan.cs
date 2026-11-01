using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;

namespace FishAudio.Testing.Evidence;

public sealed record FullCoverageCatalogInput(string Key, string DiscoveryPath, string DiscoverySha256);
public sealed record FullCoveragePlanInput(int SchemaVersion, IReadOnlyList<FullCoverageCatalogInput> Catalogs,
    int ShardIndex = 0, int ShardCount = 1);
public sealed record FullCoverageCatalogPlan(string Key, string DiscoveryPath, string DiscoverySha256,
    IReadOnlyList<string> CanonicalIds);
public sealed record FullCoverageOwner(string Id, string CatalogKey, string Kind, string? Profile,
    IReadOnlyList<DiscoveredNUnitCase> Cases, IReadOnlyList<string> ExecutionDependencies,
    IReadOnlyList<string> ExclusiveResources);
public sealed record FullCoveragePlanResult(int SchemaVersion, IReadOnlyList<FullCoverageCatalogPlan> Catalogs,
    IReadOnlyList<FullCoverageOwner> Owners, int ShardIndex, int ShardCount,
    IReadOnlyList<string> SelectedOwnerIds, IReadOnlyList<string> UnselectedOwnerIds,
    IReadOnlyList<string> CatalogCanonicalIds, IReadOnlyList<string> SelectedCanonicalIds)
{
    public IReadOnlyList<string> SmokeOwnerIds { get; init; } = [];
    public IReadOnlyList<string> SmokeCanonicalIds { get; init; } = [];
    public string SelectionPolicy => SmokeOwnerIds.Count == 0 ? "ordinal-modulo-v1" : "smoke-first-v1";
    public int CoverageConcurrency => 1;
    public bool IsWholeCatalogSelection => ShardCount == 1;
    public bool CoverageUnionComplete => false;
    public bool CompleteBaseline => false;
    public bool PublicationAuthorized => false;
}
public sealed class FullCoveragePlanException : Exception
{
    internal FullCoveragePlanException(string code) : base(code) => Code = code;
    public string Code { get; }
}

// Coverage collection ownership is derived from current framework execution
// metadata, not a second case catalog. Ordinary execution batching does not
// justify attributing its aggregate to each independent leaf.
public static class FullCoveragePlan
{
    public static FullCoveragePlanResult ReadValidated(FullCoveragePlanInput input)
    {
        var guards = new List<FileStream>();
        var complete = false;
        try
        {
            Require(input is not null && input.SchemaVersion == 1 && input.Catalogs is { Count: 2 }, "schema-or-catalog-set");
            Require(input.ShardCount is >= 1 and <= 100_000 && input.ShardIndex >= 0 && input.ShardIndex < input.ShardCount,
                "shard-range");
            Require(input.Catalogs.All(item => item is not null) && input.Catalogs.Select(item => item.Key)
                .ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "plugin", "testing-tool" }), "catalog-keys");
            var catalogs = new List<FullCoverageCatalogPlan>();
            var owners = new List<FullCoverageOwner>();
            var required = new HashSet<string>(StringComparer.Ordinal);
            var owned = new HashSet<string>(StringComparer.Ordinal);
            var smokeCases = new HashSet<string>(StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var catalog in input.Catalogs.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                Require(Path.IsPathFullyQualified(catalog.DiscoveryPath) && Hash(catalog.DiscoverySha256) &&
                    paths.Add(Path.GetFullPath(catalog.DiscoveryPath)), "catalog-input");
                var guard = new FileStream(catalog.DiscoveryPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                guards.Add(guard);
                Require(guard.Length is > 0 and <= 64 * 1024 * 1024 &&
                    Convert.ToHexStringLower(SHA256.HashData(guard)) == catalog.DiscoverySha256, "discovery-changed");
                var cases = NUnitEvidence.ReadCatalog(catalog.DiscoveryPath);
                Require(cases.Count is > 0 and <= 100_000, "empty-or-oversized-catalog");
                foreach (var item in cases) Require(required.Add(item.CanonicalId), "duplicate-canonical-id");
                var policies = NUnitExecutionMetadata.ReadValidated(catalog.DiscoveryPath)
                    .ToDictionary(item => item.Case.CanonicalId, StringComparer.Ordinal);
                foreach (var policy in policies.Values.Where(item => item.Smoke))
                {
                    Require(policy.Profile is null && !policy.ExclusiveResources.Any(resource => resource is "desktop" or "keyboard"),
                        "unsafe-smoke-owner");
                    smokeCases.Add(policy.Case.CanonicalId);
                }
                var ids = cases.Select(item => item.CanonicalId).ToArray();
                var plan = NUnitExecutionPlan.ReadValidated(catalog.DiscoveryPath, ids, 1);
                Require(plan.RawDiscoverySha256 == catalog.DiscoverySha256 &&
                    plan.ExpandedIds.ToHashSet(StringComparer.Ordinal).SetEquals(ids), "execution-plan-union");
                catalogs.Add(new(catalog.Key, catalog.DiscoveryPath, catalog.DiscoverySha256, Array.AsReadOnly(ids)));
                foreach (var phase in plan.Phases)
                {
                    if (phase.Kind == "ordered-profile")
                    {
                        var group = OrderedProfileCoveragePlan.ReadValidated(catalog.DiscoveryPath, phase.Profile!);
                        Require(group.RawDiscoverySha256 == catalog.DiscoverySha256 && group.RequireOrder &&
                            group.MaximumConcurrency == 1 && group.Members.Select(item => item.Case).SequenceEqual(phase.Cases),
                            "profile-boundary");
                        Add("ordered-profile", phase.Profile, phase.Cases, [group.ExecutionDependency], phase.ExclusiveResources);
                    }
                    else
                    {
                        Require(phase.Kind is "ordinary" or "fresh-case" && !phase.RequireOrder && phase.Profile is null, "phase-kind");
                        foreach (var item in phase.Cases) Add("isolated-case", null, [item], [], policies[item.CanonicalId].ExclusiveResources);
                    }
                }
                void Add(string kind, string? profile, IReadOnlyList<DiscoveredNUnitCase> members,
                    IReadOnlyList<string> dependencies, IReadOnlyList<string> resources)
                {
                    Require(members.Count > 0, "empty-owner");
                    foreach (var item in members) Require(owned.Add(item.CanonicalId), "duplicate-owner");
                    owners.Add(new("owner-" + (owners.Count + 1).ToString("D6", CultureInfo.InvariantCulture),
                        catalog.Key, kind, profile, Array.AsReadOnly(members.ToArray()),
                        Array.AsReadOnly(dependencies.ToArray()), Array.AsReadOnly(resources.ToArray())));
                }
            }
            Require(owners.Count > 0 && owned.SetEquals(required), "owner-union");
            // Assign identities before reordering: adding smoke metadata cannot
            // rename the existing whole owner or split an ordered profile.
            var smokeOwners = owners.Where(owner => owner.Cases.Any(item => smokeCases.Contains(item.CanonicalId))).ToArray();
            var remaining = owners.Where(owner => !owner.Cases.Any(item => smokeCases.Contains(item.CanonicalId))).ToArray();
            var orderedOwners = smokeOwners.Concat(remaining).ToArray();
            var selected = smokeOwners.Length == 0
                ? owners.Where((_, index) => index % input.ShardCount == input.ShardIndex).ToArray()
                : input.ShardCount == 1 ? orderedOwners
                : input.ShardIndex == 0 ? smokeOwners
                : remaining.Where((_, index) => index % (input.ShardCount - 1) == input.ShardIndex - 1).ToArray();
            var selectedIds = selected.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            complete = true;
            return new(1, catalogs.AsReadOnly(), Array.AsReadOnly(orderedOwners), input.ShardIndex, input.ShardCount,
                Array.AsReadOnly(selected.Select(item => item.Id).ToArray()),
                Array.AsReadOnly(orderedOwners.Where(item => !selectedIds.Contains(item.Id)).Select(item => item.Id).ToArray()),
                Array.AsReadOnly(catalogs.SelectMany(item => item.CanonicalIds).ToArray()),
                Array.AsReadOnly(selected.SelectMany(item => item.Cases).Select(item => item.CanonicalId).ToArray()))
            {
                SmokeOwnerIds = Array.AsReadOnly(smokeOwners.Select(owner => owner.Id).ToArray()),
                SmokeCanonicalIds = Array.AsReadOnly(smokeOwners.SelectMany(owner => owner.Cases).Select(item => item.CanonicalId).ToArray()),
            };
        }
        catch (FullCoveragePlanException) { throw; }
        catch (Exception error) when (error is NUnitEvidenceException or NUnitExecutionMetadataException or NUnitExecutionPlanException or
            OrderedProfileCoverageException or IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or OverflowException)
        { throw new FullCoveragePlanException("full-coverage-plan-input-rejected"); }
        finally
        {
            var failed = false;
            foreach (var guard in guards) { try { guard.Dispose(); } catch { failed = true; } }
            if (complete && failed) throw new FullCoveragePlanException("full-coverage-plan-input-release-failed");
        }
    }
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(item => item is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw new FullCoveragePlanException("full-coverage-plan-" + code);
    }
}
