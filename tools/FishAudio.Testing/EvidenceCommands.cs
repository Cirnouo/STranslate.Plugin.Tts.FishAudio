using System.Text.Json;
using FishAudio.Testing.Evidence;

namespace FishAudio.Testing;

// Command-line glue around framework-authored XML. No scenario is invoked here.
internal static class EvidenceCommands
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static bool IsInvocation(string[] args)
        => args.Length != 0 && args[0] is "evidence-catalog" or "evidence-verify" or "evidence-binary";

    internal static int Run(string[] args)
    {
        try
        {
            var options = ReadOptions(args);
            object result;
            if (args[0] == "evidence-catalog")
            {
                RequireKeys(options, "--discovery", "--output");
                result = new CatalogDocument(1, "nunit-catalog", true,
                    NUnitEvidence.ReadCatalog(options["--discovery"]));
            }
            else if (args[0] == "evidence-binary")
            {
                RequireKeys(options, "--assembly", "--pdb", "--output");
                result = new { schemaVersion = 1, kind = "binary-identity", complete = true,
                    binary = BinaryIdentity.ReadValidated(options["--assembly"], options["--pdb"]) };
            }
            else
            {
                RequireKeys(options, "--catalog", "--selection", "--result", "--trx", "--output");
                var catalog = Read<CatalogDocument>(options["--catalog"]);
                var selection = Read<SelectionDocument>(options["--selection"]);
                if (catalog.SchemaVersion != 1 || catalog.Kind != "nunit-catalog" || !catalog.Complete
                    || selection.SchemaVersion != 1 || catalog.Cases is null || selection.Ids is null)
                    throw new InvalidDataException();
                var verified = NUnitEvidence.VerifyResults(catalog.Cases, selection.Ids,
                    options["--result"], options["--trx"]);
                result = new { schemaVersion = 1, kind = "nunit-terminal", complete = true,
                    passedCount = verified.PassedCount, cases = verified.Cases };
            }
            var outputPath = options["--output"];
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(output, result, Json);
            Console.WriteLine(args[0] + ": passed.");
            return 0;
        }
        catch (NUnitEvidenceException error)
        {
            Console.Error.WriteLine("nunit-evidence: " + error.Code);
            return 1;
        }
        catch (BinaryIdentityException error)
        {
            Console.Error.WriteLine("binary-evidence: " + error.Code);
            return 1;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("nunit-evidence: invalid-input-or-output.");
            return 2;
        }
    }

    private static Dictionary<string, string> ReadOptions(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal)
                || !Path.IsPathFullyQualified(args[index + 1])
                || !values.TryAdd(args[index], Path.GetFullPath(args[index + 1])))
                throw new InvalidDataException();
        }
        return values;
    }

    private static void RequireKeys(Dictionary<string, string> options, params string[] expected)
    {
        if (options.Count != expected.Length || expected.Any(key => !options.ContainsKey(key)))
            throw new InvalidDataException();
    }

    private static T Read<T>(string path)
    {
        using var input = File.OpenRead(path);
        if (input.Length is <= 0 or > 64 * 1024 * 1024) throw new InvalidDataException();
        return JsonSerializer.Deserialize<T>(input, Json) ?? throw new InvalidDataException();
    }

    private sealed record CatalogDocument(int SchemaVersion, string Kind, bool Complete,
        IReadOnlyList<DiscoveredNUnitCase> Cases);
    private sealed record SelectionDocument(int SchemaVersion, IReadOnlyList<string> Ids);
}
