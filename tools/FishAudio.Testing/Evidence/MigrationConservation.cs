using System.Diagnostics.CodeAnalysis;

namespace FishAudio.Testing.Evidence;

public sealed record MigrationReferenceSource(string Commit, string Tree, string ReferenceSha256);
public sealed record MigrationLegacyCase(string Id, string Partition);
public sealed record MigrationReference(int SchemaVersion, MigrationReferenceSource Source,
    IReadOnlyList<MigrationLegacyCase> Cases);
public sealed record MigrationRecord(string OriginalId, IReadOnlyList<string> SuccessorIds,
    string Responsibility);
public enum MigrationOwnerKind { Legacy, NUnit }
public sealed record CheckedMigrationOwner(string OriginalId, string Partition, MigrationOwnerKind Owner,
    IReadOnlyList<string> SuccessorIds, string? Responsibility);
public sealed record MigrationConservationEvidence(int SchemaVersion, MigrationReferenceSource SourceReference,
    IReadOnlyList<CheckedMigrationOwner> CheckedOwners, IReadOnlyList<string> OrdinaryNUnitIds)
{
    public bool AssertionConservationProven => false;
    public bool LifecycleConservationProven => false;
    public bool SourceCorrespondenceProven => false;
    public bool CompleteBaseline => false;
}

public sealed class MigrationConservationException : Exception
{
    internal MigrationConservationException(string code) : base(code) => Code = code;
    public string Code { get; }
}

/// <summary>
/// Reconciles declared identity ownership only; no delegate is registered or run.
/// The caller must authenticate the original reference bytes and their source.
/// Carrying a digest in <see cref="MigrationReferenceSource"/> does not authenticate
/// that reference. The caller also binds discovery artifacts to the current
/// source/build; parsing and read locks establish shape/stability, not freshness.
/// Responsibility descriptions require independent assertion,
/// lifecycle and source-correspondence review even after identity admission.
/// </summary>
public static class MigrationConservation
{
    private const int MaximumCases = 100_000;
    private const int MaximumDiscoveryFiles = 32;
    private const int MaximumTextLength = 16_384;
    private const long MaximumDiscoveryBytes = 64 * 1024 * 1024;
    private const long MaximumCombinedDiscoveryBytes = 128 * 1024 * 1024;

    public static MigrationConservationEvidence ReadValidated(MigrationReference reference,
        IReadOnlyList<MigrationLegacyCase> currentLegacy, IReadOnlyList<string> absoluteNUnitDiscoveryPaths,
        IReadOnlyList<MigrationRecord> migrations)
    {
        var locks = new List<FileStream>();
        var admitted = false;
        try
        {
            Require(reference is not null && reference.SchemaVersion == 1 && reference.Source is not null &&
                GitHash(reference.Source.Commit) && GitHash(reference.Source.Tree) &&
                reference.Source.Commit.Length == reference.Source.Tree.Length &&
                Hash(reference.Source.ReferenceSha256), "migration-invalid-reference");
            var originals = Snapshot(reference.Cases, 1, MaximumCases, "migration-invalid-reference");
            var originalById = IndexLegacy(originals, "migration-duplicate-original", "migration-invalid-reference");
            var legacy = Snapshot(currentLegacy, 0, MaximumCases, "migration-invalid-current-legacy");
            var legacyById = IndexLegacy(legacy, "migration-duplicate-legacy-case", "migration-invalid-current-legacy");
            foreach (var item in legacy)
            {
                Require(originalById.TryGetValue(item.Id, out var original), "migration-unknown-legacy-case");
                Require(item.Partition == original.Partition, "migration-legacy-partition-mismatch");
            }

            var records = Snapshot(migrations, 0, MaximumCases, "migration-invalid-records");
            var recordById = new Dictionary<string, MigrationRecord>(StringComparer.Ordinal);
            var successorIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                Require(record is not null && Text(record.OriginalId), "migration-invalid-records");
                Require(originalById.ContainsKey(record.OriginalId), "migration-unknown-original");
                Require(!recordById.ContainsKey(record.OriginalId), "migration-duplicate-record");
                Require(Text(record.Responsibility), "migration-invalid-responsibility");
                var successors = Snapshot(record.SuccessorIds, 1, MaximumCases, "migration-invalid-successors");
                Require(successors.Length <= MaximumCases - successorIds.Count, "migration-invalid-successors");
                foreach (var successor in successors)
                {
                    Require(Text(successor), "migration-invalid-successors");
                    Require(successorIds.Add(successor), "migration-duplicate-successor");
                }
                recordById.Add(record.OriginalId, new(record.OriginalId, Array.AsReadOnly(successors), record.Responsibility));
            }

            var paths = Snapshot(absoluteNUnitDiscoveryPaths, 1, MaximumDiscoveryFiles, "migration-discovery-rejected");
            var catalog = ReadDiscovery(paths, locks);
            var frameworkIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in catalog)
                Require(frameworkIds.Add(item.CanonicalId), "migration-duplicate-framework-case");
            foreach (var successor in successorIds)
                Require(frameworkIds.Contains(successor), "migration-successor-missing");

            var owners = new List<CheckedMigrationOwner>(originals.Length);
            foreach (var original in originals)
            {
                var retained = legacyById.ContainsKey(original.Id);
                var migrated = recordById.TryGetValue(original.Id, out var record);
                Require(!(retained && migrated), "migration-original-overlap");
                // An original canonical ID in NUnit must be explicitly assigned
                // to that original's own migration path. It cannot become an
                // ordinary new case or an alias for another historical owner.
                if (frameworkIds.Contains(original.Id))
                    Require(!retained && migrated && record!.SuccessorIds.Contains(original.Id, StringComparer.Ordinal),
                        "migration-original-overlap");
                Require(retained || migrated, "migration-original-unowned");
                owners.Add(retained
                    ? new(original.Id, original.Partition, MigrationOwnerKind.Legacy, Array.AsReadOnly(Array.Empty<string>()), null)
                    : new(original.Id, original.Partition, MigrationOwnerKind.NUnit, record!.SuccessorIds, record!.Responsibility));
            }
            var ordinary = catalog.Where(item => !successorIds.Contains(item.CanonicalId)).Select(item => item.CanonicalId).ToArray();
            var source = new MigrationReferenceSource(reference.Source.Commit, reference.Source.Tree, reference.Source.ReferenceSha256);
            var result = new MigrationConservationEvidence(1, source, owners.AsReadOnly(), Array.AsReadOnly(ordinary));
            admitted = true;
            return result;
        }
        catch (MigrationConservationException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or IndexOutOfRangeException or OverflowException)
        {
            // Caller collections and filesystem errors may contain private data.
            throw Rejected("migration-invalid-input-or-file");
        }
        finally
        {
            var releaseFailed = false;
            foreach (var stream in locks)
            {
                try { stream.Dispose(); }
                catch { releaseFailed = true; }
            }
            if (releaseFailed && admitted) throw Rejected("migration-file-release-failed");
        }
    }

    private static Dictionary<string, MigrationLegacyCase> IndexLegacy(MigrationLegacyCase[] cases,
        string duplicateCode, string invalidCode)
    {
        var result = new Dictionary<string, MigrationLegacyCase>(StringComparer.Ordinal);
        foreach (var item in cases)
        {
            Require(item is not null && Text(item.Id) && Text(item.Partition) && item.Partition.Length <= 256 &&
                !item.Partition.Any(char.IsWhiteSpace), invalidCode);
            Require(result.TryAdd(item.Id, item), duplicateCode);
        }
        return result;
    }

    private static IReadOnlyList<DiscoveredNUnitCase> ReadDiscovery(string[] paths, List<FileStream> locks)
    {
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var catalog = new List<DiscoveredNUnitCase>();
            long totalBytes = 0;
            // Hold every admitted dump against replacement until reconciliation
            // finishes. Neither discovery nor reference evidence is written.
            foreach (var path in paths)
            {
                Require(!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) &&
                    !path.StartsWith(@"\\?\", StringComparison.Ordinal) &&
                    !path.StartsWith(@"\\.\", StringComparison.Ordinal) &&
                    !path.StartsWith(@"\??\", StringComparison.Ordinal), "migration-discovery-rejected");
                var full = Path.GetFullPath(path);
                Require(seen.Add(full), "migration-discovery-rejected");
                var file = new FileInfo(full);
                Require(file.Exists && (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0,
                    "migration-discovery-rejected");
                for (var parent = file.Directory; parent is not null; parent = parent.Parent)
                    Require(parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) == 0, "migration-discovery-rejected");
                var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
                locks.Add(stream);
                Require(stream.Length is > 0 and <= MaximumDiscoveryBytes &&
                    stream.Length <= MaximumCombinedDiscoveryBytes - totalBytes, "migration-discovery-rejected");
                totalBytes += stream.Length;
                var cases = NUnitEvidence.ReadCatalog(full);
                Require(cases.Count <= MaximumCases - catalog.Count, "migration-discovery-rejected");
                catalog.AddRange(cases);
            }
            return catalog;
        }
        catch (MigrationConservationException) { throw; }
        catch (NUnitEvidenceException) { throw Rejected("migration-discovery-rejected"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or OverflowException)
        {
            throw Rejected("migration-discovery-rejected");
        }
    }

    private static T[] Snapshot<T>(IReadOnlyList<T>? values, int minimum, int maximum, string code)
    {
        Require(values is not null, code);
        var count = values.Count;
        Require(count >= minimum && count <= maximum, code);
        var result = new T[count];
        for (var index = 0; index < count; index++) result[index] = values[index];
        return result;
    }

    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumTextLength &&
        !value.Any(char.IsControl);
    private static bool GitHash(string? value) => value is { Length: 40 or 64 } && Hex(value);
    private static bool Hash(string? value) => value is { Length: 64 } && Hex(value);
    private static bool Hex(string value) => value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static MigrationConservationException Rejected(string code) => new(code);
    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw Rejected(code);
    }
}
