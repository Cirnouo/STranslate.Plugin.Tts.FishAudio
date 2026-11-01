using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using FishAudio.Testing.Evidence;

namespace FishAudio.Testing;

internal static class FullCoverageCommands
{
    internal sealed record OwnerFiles(string CatalogKey, string InputPath, string ExpectationPath);
    internal sealed record CompositionFiles(int SchemaVersion, IReadOnlyList<OwnerFiles> IsolatedOwners, IReadOnlyList<OwnerFiles> OrderedGroups);
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        MaxDepth = 64,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    internal static bool IsInvocation(string[] args) => args.Length > 0 &&
        args[0] is "coverage-full-plan" or "coverage-full-compose" or "coverage-full-environment" or
            "coverage-full-prepared-check" or "coverage-full-shard-check" or "coverage-full-shards-compose";

    internal static int Run(string[] args)
    {
        var guards = new List<FileStream>();
        var result = RunHeld(args, guards);
        var releaseFailed = false;
        foreach (var guard in guards) { try { guard.Dispose(); } catch { releaseFailed = true; } }
        return result == 0 && releaseFailed ? Rejected("input-release-failed", 2) : result;
    }

    private static int RunHeld(string[] args, List<FileStream> guards)
    {
        try
        {
            if (!IsInvocation(args)) throw new InvalidDataException();
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 1; index < args.Length; index += 2)
                if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].Any(char.IsControl) ||
                    !Path.IsPathFullyQualified(args[index + 1]) || !options.TryAdd(args[index], args[index + 1]))
                    throw new InvalidDataException();
            object value;
            if (args[0] == "coverage-full-environment")
            {
                RequireKeys(options, "--output");
                value = new { osVersion = Environment.OSVersion.VersionString, osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                    processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(), runtime = RuntimeInformation.FrameworkDescription,
                    runtimeVersion = Environment.Version.ToString(), isWindows = OperatingSystem.IsWindows() };
            }
            else if (args[0] == "coverage-full-plan")
            {
                RequireKeys(options, "--input", "--output");
                value = FullCoveragePlan.ReadValidated(Read<FullCoveragePlanInput>(options["--input"], guards));
            }
            else if (args[0] == "coverage-full-prepared-check")
            {
                RequireKeys(options, "--expectation", "--output");
                value = FullCoverageShards.ReadPrepared(Read<FullCoveragePreparedExpectation>(options["--expectation"], guards), guards);
            }
            else if (args[0] == "coverage-full-shard-check")
            {
                RequireKeys(options, "--input", "--expectation", "--output");
                value = FullCoverageShards.ReadShard(Read<FullCoverageShardInput>(options["--input"], guards),
                    Read<FullCoveragePreparedExpectation>(options["--expectation"], guards), guards);
            }
            else if (args[0] == "coverage-full-shards-compose")
            {
                RequireKeys(options, "--input", "--expectation", "--output");
                value = FullCoverageShards.Compose(Read<FullCoverageShardCompositionInput>(options["--input"], guards),
                    Read<FullCoveragePreparedExpectation>(options["--expectation"], guards), guards);
            }
            else
            {
                RequireKeys(options, "--input", "--expectation", "--output");
                // Both records contain actual raw-session inputs and independent
                // build/catalog authority. No serialized success report is input.
                var files = Read<CompositionFiles>(options["--input"], guards);
                if (files.SchemaVersion != 1 || files.IsolatedOwners is null || files.OrderedGroups is null ||
                    (long)files.IsolatedOwners.Count + files.OrderedGroups.Count is <= 0 or > 200_000)
                    throw new InvalidDataException();
                var raw = new CoverageBaselineCandidateInput(1, files.IsolatedOwners.Select(owner =>
                    new CoverageBaselineIsolatedOwner(owner.CatalogKey, Read<IsolatedCoverageSessionInput>(owner.InputPath, guards),
                        Read<IsolatedCoverageAdmission>(owner.ExpectationPath, guards))).ToArray(),
                    files.OrderedGroups.Select(owner => new CoverageBaselineOrderedGroup(owner.CatalogKey,
                        Read<OrderedProfileCoverageGroupInput>(owner.InputPath, guards),
                        Read<OrderedProfileCoverageGroupAdmission>(owner.ExpectationPath, guards))).ToArray());
                var candidate = CoverageBaselineCandidate.ReadValidated(raw,
                    Read<CoverageBaselineAdmission>(options["--expectation"], guards));
                if (!candidate.DependencyRecordsComplete)
                    return Rejected("incomplete-dependency-records", 1);
                value = candidate;
            }
            using var output = new FileStream(options["--output"], FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(output, new { schemaVersion = 1, kind = args[0], complete = true,
                baselineClaim = false, value }, JsonOptions);
            output.Flush(true);
            return 0;
        }
        catch (FullCoveragePlanException error) { return Rejected(error.Code, 1); }
        catch (FullCoverageShardException error) { return Rejected(error.Code, 1); }
        catch (CoverageBaselineCandidateException error) { return Rejected(error.Code, 1); }
        catch (Exception) { return Rejected("invalid-input-or-output", 2); }
    }

    private static int Rejected(string code, int exitCode)
    {
        Console.Error.WriteLine("coverage-full: " + code);
        return exitCode;
    }
    private static void RequireKeys(Dictionary<string, string> values, params string[] keys)
    {
        if (values.Count != keys.Length || keys.Any(key => !values.ContainsKey(key))) throw new InvalidDataException();
    }
    private static T Read<T>(string path, List<FileStream> guards)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Any(char.IsControl))
            throw new InvalidDataException();
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        guards.Add(stream);
        if (stream.Length is <= 0 or > 64 * 1024 * 1024) throw new InvalidDataException();
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 64 });
        var names = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) names.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) names.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName && !names.Peek().Add(reader.GetString()!))
                throw new InvalidDataException();
        }
        return JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new InvalidDataException();
    }
}
