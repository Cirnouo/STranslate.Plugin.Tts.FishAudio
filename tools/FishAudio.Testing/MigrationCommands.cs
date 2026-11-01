using System.Text.Json;
using FishAudio.Testing.Evidence;

namespace FishAudio.Testing;

// Data-only retirement evidence. No test is registered, selected or dispatched
// by a migration row; ordinary NUnit cases require no entry in this document.
internal static class MigrationCommands
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
    };

    internal static bool IsInvocation(string[] args) => args.Length > 0 && args[0] == "evidence-migration";

    internal static int Run(string[] args)
    {
        var guards = new List<FileStream>();
        try
        {
            string[] keys = ["--reference", "--legacy", "--discoveries", "--migrations", "--output"];
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 1; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length || !keys.Contains(args[index], StringComparer.Ordinal) ||
                    !Path.IsPathFullyQualified(args[index + 1]) || !options.TryAdd(args[index], args[index + 1]))
                    throw new InvalidDataException();
            }
            if (options.Count != keys.Length) throw new InvalidDataException();
            var reference = PinnedLegacyReference.ReadValidated(options["--reference"]);
            var legacy = Read<LegacyDocument>(options["--legacy"], guards);
            var discoveries = Read<DiscoveryDocument>(options["--discoveries"], guards);
            var migrations = Read<MigrationsDocument>(options["--migrations"], guards);
            if (legacy.SchemaVersion != 1 || discoveries.SchemaVersion != 1 || migrations.SchemaVersion != 1 ||
                legacy.Cases is null || discoveries.Paths is null || migrations.Migrations is null)
                throw new InvalidDataException();
            var evidence = MigrationConservation.ReadValidated(reference, legacy.Cases, discoveries.Paths, migrations.Migrations);
            using var output = new FileStream(options["--output"], FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(output, new { schemaVersion = 1, kind = "migration-conservation", complete = true,
                originalCount = evidence.CheckedOwners.Count, legacyCount = evidence.CheckedOwners.Count(item => item.Owner == MigrationOwnerKind.Legacy),
                migratedCount = evidence.CheckedOwners.Count(item => item.Owner == MigrationOwnerKind.NUnit),
                evidence, baselineClaim = false }, Json);
            Console.WriteLine("evidence-migration: identities conserved.");
            return 0;
        }
        catch (PinnedLegacyReferenceException error) { Console.Error.WriteLine("migration-evidence: " + error.Code); return 1; }
        catch (MigrationConservationException error) { Console.Error.WriteLine("migration-evidence: " + error.Code); return 1; }
        catch (Exception) { Console.Error.WriteLine("migration-evidence: invalid-input-or-output."); return 2; }
        finally { foreach (var guard in guards) guard.Dispose(); }
    }

    private static T Read<T>(string path, List<FileStream> guards)
    {
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException();
        var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        guards.Add(input);
        if (input.Length is <= 0 or > 64 * 1024 * 1024) throw new InvalidDataException();
        using var document = JsonDocument.Parse(input);
        RejectDuplicateProperties(document.RootElement);
        return document.RootElement.Deserialize<T>(Json) ?? throw new InvalidDataException();
    }

    private static void RejectDuplicateProperties(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in item.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw new InvalidDataException();
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (item.ValueKind == JsonValueKind.Array)
            foreach (var child in item.EnumerateArray()) RejectDuplicateProperties(child);
    }

    private sealed record LegacyDocument(int SchemaVersion, IReadOnlyList<MigrationLegacyCase> Cases);
    private sealed record DiscoveryDocument(int SchemaVersion, IReadOnlyList<string> Paths);
    private sealed record MigrationsDocument(int SchemaVersion, IReadOnlyList<MigrationRecord> Migrations);
}
