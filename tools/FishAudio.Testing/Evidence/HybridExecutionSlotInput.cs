using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FishAudio.Testing.Evidence;

public sealed record HybridExecutionInputDigests(string ReferenceSha256, string LegacyCatalogSha256,
    string DiscoveriesSha256, string MigrationsSha256);
public sealed record HybridExecutionSlotDocument(int SchemaVersion, string Kind, bool Complete,
    HybridExecutionInputDigests Inputs, HybridExecutionSlotPlanResult Plan, bool BaselineClaim);

/// <summary>
/// Reads exactly the existing source-current legacy-list and migration document
/// shapes. Hashes bind bytes actually read; callers still admit current assembly,
/// raw list-process receipt, source/build freshness and input ownership.
/// </summary>
public static class HybridExecutionSlotInput
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static HybridExecutionSlotDocument ReadValidated(string absoluteReference, string absoluteLegacy,
        string absoluteDiscoveries, string absoluteMigrations)
    {
        var guards = new List<FileStream>();
        var completed = false;
        try
        {
            var referenceInput = HybridSlotInputFiles.Open(absoluteReference, guards);
            var reference = PinnedLegacyReference.ReadValidated(referenceInput.Path);
            var legacyInput = HybridSlotInputFiles.Open(absoluteLegacy, guards);
            var discoveriesInput = HybridSlotInputFiles.Open(absoluteDiscoveries, guards);
            var migrationsInput = HybridSlotInputFiles.Open(absoluteMigrations, guards);
            var legacy = Read<LegacyDocument>(legacyInput.Stream);
            var discoveries = Read<DiscoveryDocument>(discoveriesInput.Stream);
            var migrations = Read<MigrationsDocument>(migrationsInput.Stream);
            Require(legacy.SchemaVersion == 1 && discoveries.SchemaVersion == 1 && migrations.SchemaVersion == 1 &&
                legacy.Cases is not null && discoveries.Paths is not null && migrations.Migrations is not null,
                "invalid-document");
            var plan = HybridExecutionSlotPlan.ReadValidated(reference, legacy.Cases, discoveries.Paths,
                migrations.Migrations);
            var digests = new HybridExecutionInputDigests(reference.Source.ReferenceSha256,
                Digest(legacyInput.Stream), Digest(discoveriesInput.Stream), Digest(migrationsInput.Stream));
            completed = true;
            return new(1, "hybrid-execution-slot-plan", true, digests, plan, false);
        }
        catch (HybridExecutionSlotPlanException) { throw; }
        catch (PinnedLegacyReferenceException) { throw Rejected("reference-rejected"); }
        catch (JsonException) { throw Rejected("invalid-document"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or OverflowException or System.Security.SecurityException)
        { throw Rejected("invalid-input-or-file"); }
        finally
        {
            var releaseFailed = false;
            foreach (var guard in guards)
                try { guard.Dispose(); } catch { releaseFailed = true; }
            if (releaseFailed && completed) throw Rejected("file-release-failed");
        }
    }

    private static T Read<T>(FileStream stream)
    {
        using var document = JsonDocument.Parse(stream);
        RejectDuplicateProperties(document.RootElement);
        return document.RootElement.Deserialize<T>(Json) ?? throw Rejected("invalid-document");
    }

    private static void RejectDuplicateProperties(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in item.EnumerateObject())
            {
                Require(seen.Add(property.Name), "invalid-document");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (item.ValueKind == JsonValueKind.Array)
            foreach (var child in item.EnumerateArray()) RejectDuplicateProperties(child);
    }

    private static string Digest(FileStream stream)
    {
        stream.Position = 0;
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private sealed record LegacyDocument(int SchemaVersion, IReadOnlyList<MigrationLegacyCase>? Cases);
    private sealed record DiscoveryDocument(int SchemaVersion, IReadOnlyList<string>? Paths);
    private sealed record MigrationsDocument(int SchemaVersion, IReadOnlyList<MigrationRecord>? Migrations);
    private static HybridExecutionSlotPlanException Rejected(string code) => new("hybrid-slot-" + code);
    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw Rejected(code);
    }
}

internal static class HybridSlotInputFiles
{
    internal static (string Path, FileStream Stream) Open(string path, List<FileStream> guards)
    {
        Require(!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && !Device(path));
        var full = Path.GetFullPath(path);
        Require(!Device(full));
        Regular(full);
        var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        guards.Add(stream);
        Regular(full);
        Require(stream.Length is > 0 and <= 64 * 1024 * 1024);
        return (full, stream);
    }

    private static void Regular(string path)
    {
        var file = new FileInfo(path);
        Require(file.Exists && (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) == 0);
        for (var parent = file.Directory; parent is not null; parent = parent.Parent)
            Require(parent.Exists && (parent.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) == 0);
    }

    private static bool Device(string path) => path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
        path.StartsWith(@"\\.\", StringComparison.Ordinal) || path.StartsWith(@"\??\", StringComparison.Ordinal);
    private static void Require([DoesNotReturnIf(false)] bool condition)
    {
        if (!condition) throw new HybridExecutionSlotPlanException("hybrid-slot-invalid-input-or-file");
    }
}
