using System.Text.Json;
using System.Text.Json.Serialization;
using FishAudio.Testing.Evidence;

namespace FishAudio.Testing;

// Separate experimental commands; existing coverage-import remains one-case only.
internal static class OrderedProfileCoverageCommands
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    internal static bool IsInvocation(string[] args) => args.Length > 0 &&
        args[0] is "coverage-profile-plan" or "coverage-profile-import" or "coverage-group-source-candidates" or "coverage-group-workspace-candidates";

    internal static int Run(string[] args)
    {
        try
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 1; index < args.Length; index += 2)
                if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal) ||
                    !Path.IsPathFullyQualified(args[index + 1]) || args[index + 1].Any(char.IsControl) ||
                    !options.TryAdd(args[index], args[index + 1])) throw new InvalidDataException();
            object value;
            if (args[0] == "coverage-profile-plan")
            {
                RequireKeys(options, "--input", "--output");
                var request = Read<PlanRequest>(options["--input"]);
                if (request.SchemaVersion != 1) throw new InvalidDataException();
                value = OrderedProfileCoveragePlan.ReadValidated(request.Discovery, request.Profile);
            }
            else if (args[0] == "coverage-group-workspace-candidates")
            {
                RequireKeys(options, "--input", "--expectation", "--source-expectation", "--workspace-expectation", "--output");
                try
                {
                    value = OrderedProfileCoverageWorkspaceJoin.ReadValidatedAsync(
                        Read<OrderedProfileCoverageGroupInput>(options["--input"]),
                        Read<OrderedProfileCoverageGroupAdmission>(options["--expectation"]),
                        Read<CoverageSourceCandidatesInput>(options["--source-expectation"]),
                        Read<OrderedProfileCoverageWorkspaceRequest>(options["--workspace-expectation"])).GetAwaiter().GetResult();
                }
                catch (OrderedProfileCoverageWorkspaceJoinException error) when (error.Evidence is not null)
                {
                    using var rejected = new FileStream(options["--output"], FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    JsonSerializer.Serialize(rejected, new { schemaVersion = 1, kind = args[0], complete = false,
                        baselineClaim = false, value = error.Evidence }, Json);
                    throw;
                }
            }
            else if (args[0] == "coverage-group-source-candidates")
            {
                RequireKeys(options, "--input", "--expectation", "--source-expectation", "--output");
                value = OrderedProfileCoverageSourceCandidates.ReadValidated(
                    Read<OrderedProfileCoverageGroupInput>(options["--input"]),
                    Read<OrderedProfileCoverageGroupAdmission>(options["--expectation"]),
                    Read<CoverageSourceCandidatesInput>(options["--source-expectation"]));
            }
            else
            {
                RequireKeys(options, "--input", "--expectation", "--output");
                value = OrderedProfileCoverageSession.ReadValidated(Read<OrderedProfileCoverageGroupInput>(options["--input"]),
                    Read<OrderedProfileCoverageGroupAdmission>(options["--expectation"]));
            }
            using var output = new FileStream(options["--output"], FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(output, new { schemaVersion = 1, kind = args[0], complete = true,
                baselineClaim = false, value }, Json);
            return 0;
        }
        catch (OrderedProfileCoverageWorkspaceJoinException error)
        {
            Console.Error.WriteLine("ordered-profile-coverage: " + error.Code);
            return 1;
        }
        catch (OrderedProfileCoverageSourceCandidatesException error)
        {
            Console.Error.WriteLine("ordered-profile-coverage: " + error.Code);
            return 1;
        }
        catch (OrderedProfileCoverageException error)
        {
            Console.Error.WriteLine("ordered-profile-coverage: " + error.Code);
            return 1;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("ordered-profile-coverage: invalid-input-or-output");
            return 2;
        }
    }

    private static void RequireKeys(Dictionary<string, string> options, params string[] keys)
    {
        if (options.Count != keys.Length || keys.Any(key => !options.ContainsKey(key))) throw new InvalidDataException();
    }

    private static T Read<T>(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is <= 0 or > 64 * 1024 * 1024) throw new InvalidDataException();
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 64 });
        var names = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) names.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) names.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName && !names.Peek().Add(reader.GetString()!))
                throw new InvalidDataException();
        }
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException();
    }

    private sealed record PlanRequest(int SchemaVersion, string Discovery, string Profile);
}
