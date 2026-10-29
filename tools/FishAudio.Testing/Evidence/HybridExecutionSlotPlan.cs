using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;

namespace FishAudio.Testing.Evidence;

public sealed record HybridDiscoveryCatalog(string Path, string Sha256, IReadOnlyList<string> CaseIds);
public sealed record HybridExecutionSlot(string Id, string Owner, string? Partition, IReadOnlyList<string> SelectedIds);
public sealed record HybridExecutionSlotPlanResult(IReadOnlyList<HybridDiscoveryCatalog> Discoveries,
    IReadOnlyList<HybridExecutionSlot> Slots);

public sealed class HybridExecutionSlotPlanException : Exception
{
    internal HybridExecutionSlotPlanException(string code) : base(code) => Code = code;
    public string Code { get; }
}

public static class HybridExecutionSlotPlan
{
    // Protocol slots, not a test registry. Never take this order from producer JSON.
    private static readonly string[] Partitions =
        ["core", "native-settings", "native-input", "native-popups", "native-cards"];

    /// <summary>
    /// Derives selectors only. The caller authenticates reference/current source,
    /// build and discovery provenance; successful identity admission does not prove
    /// assertion/lifecycle conservation, execution or a reusable baseline.
    /// </summary>
    public static HybridExecutionSlotPlanResult ReadValidated(MigrationReference reference,
        IReadOnlyList<MigrationLegacyCase> currentLegacy, IReadOnlyList<string> absoluteDiscoveries,
        IReadOnlyList<MigrationRecord> migrations)
    {
        var guards = new List<FileStream>();
        var completed = false;
        try
        {
            Require(reference is not null && reference.Cases is not null, "invalid-reference");
            var originals = Snapshot(reference.Cases, 1, 100_000, "invalid-reference");
            Require(originals.All(item => item is not null &&
                Partitions.Contains(item.Partition, StringComparer.Ordinal)) &&
                Partitions.All(partition => originals.Any(item => item.Partition == partition)), "unknown-partition");
            var legacy = Snapshot(currentLegacy, 0, 100_000, "invalid-legacy");
            var records = Snapshot(migrations, 0, 100_000, "invalid-migrations");
            var paths = Snapshot(absoluteDiscoveries, 1, 32, "invalid-discovery");
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var policies = new List<Member>();
            var discoveries = new List<HybridDiscoveryCatalog>();
            var allFrameworkIds = new HashSet<string>(StringComparer.Ordinal);
            long totalBytes = 0;
            foreach (var path in paths)
            {
                var input = HybridSlotInputFiles.Open(path, guards);
                Require(seenPaths.Add(input.Path), "duplicate-discovery");
                Require(input.Stream.Length <= 128 * 1024 * 1024 - totalBytes, "discovery-limit");
                totalBytes += input.Stream.Length;
                var current = NUnitExecutionMetadata.ReadValidated(input.Path);
                var document = NUnitEvidence.ReadDiscoveryDocument(input.Path);
                var assemblies = document.Descendants("test-suite")
                    .Where(item => (string?)item.Attribute("type") == "Assembly").ToArray();
                Require(assemblies.Length == 1 && document.Descendants("test-case").All(leaf =>
                    ReferenceEquals(leaf.Ancestors("test-suite").SingleOrDefault(owner =>
                        (string?)owner.Attribute("type") == "Assembly"), assemblies[0])), "discovery-assembly");
                Require(current.Count > 0 && current.Count <= 100_000 - policies.Count, "invalid-discovery");
                foreach (var item in current)
                {
                    Require(allFrameworkIds.Add(item.Case.CanonicalId), "duplicate-framework-case");
                    Require(item.Profile is null || !item.Profile.StartsWith("native-", StringComparison.Ordinal) ||
                        Partitions.Contains(item.Profile, StringComparer.Ordinal), "unknown-native-profile");
                    policies.Add(new(input.Path, item));
                }
                discoveries.Add(new(input.Path, Convert.ToHexStringLower(SHA256.HashData(input.Stream)),
                    Array.AsReadOnly(current.Select(item => item.Case.CanonicalId).ToArray())));
            }

            // This independent reader reconciles every original owner against all
            // actual raw catalogs, not only the selected native profile.
            var ownership = MigrationConservation.ReadValidated(reference, legacy, paths, records);
            var retained = legacy.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            Require(legacy.Select(item => item.Id).SequenceEqual(originals.Where(item => retained.Contains(item.Id))
                .Select(item => item.Id), StringComparer.Ordinal), "legacy-order");

            var slots = new List<HybridExecutionSlot>();
            var consumed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var partition in Partitions)
            {
                var selectedLegacy = legacy.Where(item => item.Partition == partition).Select(item => item.Id).ToArray();
                if (partition == "core")
                {
                    // Core remains a required legacy protocol slot until a future
                    // explicit protocol revision; migrated core leaves use the tail.
                    Require(selectedLegacy.Length > 0, "owner-missing");
                    slots.Add(new(NextId(slots.Count), "legacy", partition, Array.AsReadOnly(selectedLegacy)));
                    continue;
                }

                var owners = ownership.CheckedOwners.Where(item => item.Partition == partition).ToArray();
                var moved = owners.Where(item => item.Owner == MigrationOwnerKind.NUnit).ToArray();
                var members = policies.Where(item => item.Policy.Profile == partition).ToArray();
                if (moved.Length == 0)
                {
                    Require(members.Length == 0, "owner-overlap");
                    Require(selectedLegacy.Length > 0, "owner-missing");
                    slots.Add(new(NextId(slots.Count), "legacy", partition, Array.AsReadOnly(selectedLegacy)));
                    continue;
                }

                Require(moved.Length == owners.Length, "partial-native-ownership");
                Require(selectedLegacy.Length == 0, "owner-overlap");
                Require(members.Length > 0, "owner-missing");
                Require(members.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1,
                    "profile-cross-assembly");
                var successors = moved.SelectMany(item => item.SuccessorIds).ToHashSet(StringComparer.Ordinal);
                Require(successors.SetEquals(members.Select(item => item.Policy.Case.CanonicalId)),
                    "native-successor-union");
                Require(members.All(item => item.Policy.ExclusiveResources.Contains("desktop", StringComparer.Ordinal)),
                    "native-resource");
                var selected = members.OrderBy(item => item.Policy.ProfileOrdinal!.Value)
                    .Select(item => item.Policy.Case.CanonicalId).ToArray();
                var plan = NUnitExecutionPlan.ReadValidated(members[0].Path, selected, maximumConcurrency: 1);
                Require(plan.Phases.Count == 1 && plan.Phases[0].Kind == "ordered-profile" &&
                    plan.Phases[0].Profile == partition && plan.Phases[0].RequireOrder &&
                    plan.Phases[0].MaximumConcurrency == 1 &&
                    plan.Phases[0].Cases.Select(item => item.CanonicalId).SequenceEqual(selected, StringComparer.Ordinal),
                    "invalid-profile-plan");
                foreach (var id in selected) Require(consumed.Add(id), "framework-union");
                slots.Add(new(NextId(slots.Count), "framework", partition, Array.AsReadOnly(selected)));
            }

            var tail = policies.Where(item => !consumed.Contains(item.Policy.Case.CanonicalId))
                .Select(item => item.Policy.Case.CanonicalId).ToArray();
            if (tail.Length != 0)
            {
                foreach (var id in tail) Require(consumed.Add(id), "framework-union");
                slots.Add(new(NextId(slots.Count), "framework", null, Array.AsReadOnly(tail)));
            }
            Require(consumed.SetEquals(allFrameworkIds), "framework-union");
            completed = true;
            return new(discoveries.AsReadOnly(), slots.AsReadOnly());
        }
        catch (HybridExecutionSlotPlanException) { throw; }
        catch (MigrationConservationException) { throw Rejected("migration-rejected"); }
        catch (NUnitEvidenceException) { throw Rejected("invalid-discovery"); }
        catch (NUnitExecutionMetadataException) { throw Rejected("invalid-discovery"); }
        catch (NUnitExecutionPlanException) { throw Rejected("invalid-profile-plan"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or OverflowException or IndexOutOfRangeException or
            System.Security.SecurityException)
        { throw Rejected("invalid-input-or-file"); }
        finally
        {
            var releaseFailed = false;
            foreach (var guard in guards)
                try { guard.Dispose(); } catch { releaseFailed = true; }
            if (releaseFailed && completed) throw Rejected("file-release-failed");
        }
    }

    private sealed record Member(string Path, NUnitCaseExecutionPolicy Policy);
    private static string NextId(int count) => "slot-" + (count + 1).ToString("D4", CultureInfo.InvariantCulture);

    private static T[] Snapshot<T>(IReadOnlyList<T>? input, int minimum, int maximum, string code)
    {
        Require(input is not null && input.Count >= minimum && input.Count <= maximum, code);
        var result = new T[input.Count];
        for (var index = 0; index < result.Length; index++) result[index] = input[index];
        return result;
    }

    private static HybridExecutionSlotPlanException Rejected(string code) => new("hybrid-slot-" + code);
    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw Rejected(code);
    }
}
