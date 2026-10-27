using System.Text.Json;
using System.Text.Json.Serialization;
using FishAudio.Testing.Evidence;

namespace FishAudio.Testing;

internal static class CoverageCommands
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    internal static bool IsInvocation(string[] args) => args.Length > 0 && args[0]
        is "coverage-aggregate" or "coverage-review-export" or "coverage-review-cover" or "coverage-review-console" or "coverage-host-envelope" or "coverage-case-metadata" or "coverage-import";

    internal static int Run(string[] args)
    {
        try
        {
            var options = Options(args);
            object value;
            switch (args[0])
            {
                case "coverage-aggregate":
                    RequireKeys(options, "--report", "--assembly", "--output");
                    value = DotCoverAggregate.ReadValidated(options["--report"], options["--assembly"], DotCoverAggregate.SupportedToolVersion);
                    break;
                case "coverage-review-export":
                    RequireKeys(options, "--input", "--output");
                    var request = Read<ExportReviewRequest>(options["--input"]);
                    if (request.SchemaVersion != 1) throw new InvalidDataException();
                    value = DotCoverDiagnostics.ReviewExport(request.Log, request.ErrorLog, request.Aggregate,
                        request.SdkEngine, request.HostEngine, request.SdkVersion, request.TestHostVersion, request.DotCoverVersion);
                    break;
                case "coverage-review-cover":
                    RequireKeys(options, "--input", "--output");
                    var cover = Read<CoverReviewRequest>(options["--input"]);
                    if (cover.SchemaVersion != 1) throw new InvalidDataException();
                    value = DotCoverProcessDiagnostics.ReadValidated(cover.Log, cover.ErrorLog,
                        cover.RequiredProfiledProcessIds, cover.DotCoverVersion);
                    break;
                case "coverage-review-console":
                    RequireKeys(options, "--input", "--output");
                    var console = Read<ConsoleReviewRequest>(options["--input"]);
                    if (console.SchemaVersion != 1) throw new InvalidDataException();
                    value = DotCoverConsoleDiagnostics.ReadValidated(console.CoverStdout, console.CoverStderr,
                        console.ExportStdout, console.ExportStderr, console.DotCoverVersion);
                    break;
                case "coverage-host-envelope":
                    RequireKeys(options, "--input", "--output");
                    var host = Read<HostReviewRequest>(options["--input"]);
                    if (host.SchemaVersion != 1) throw new InvalidDataException();
                    value = NUnitHostEvidence.ReadValidated(host.Start, host.End, host.Expectation);
                    break;
                case "coverage-case-metadata":
                    RequireKeys(options, "--input", "--output");
                    var metadata = Read<CaseMetadataRequest>(options["--input"]);
                    if (metadata.SchemaVersion != 1) throw new InvalidDataException();
                    value = NUnitEvidence.ReadCaseMetadata(metadata.Discovery, metadata.CanonicalId);
                    break;
                default:
                    RequireKeys(options, "--input", "--expectation", "--output");
                    value = IsolatedCoverageSession.ReadValidated(Read<IsolatedCoverageSessionInput>(options["--input"]),
                        Read<IsolatedCoverageAdmission>(options["--expectation"]));
                    break;
            }
            var path = options["--output"];
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(output, new { schemaVersion = 1, kind = args[0], complete = true,
                baselineClaim = false, value }, Json);
            Console.WriteLine(args[0] + ": passed; no complete-baseline claim.");
            return 0;
        }
        catch (DotCoverAggregateException error) { return Rejected(error.Code); }
        catch (DotCoverDiagnosticException error) { return Rejected(error.Code); }
        catch (DotCoverProcessDiagnosticException error) { return Rejected(error.Code); }
        catch (DotCoverConsoleDiagnosticException error) { return Rejected(error.Code); }
        catch (NUnitHostEvidenceException error) { return Rejected(error.Code); }
        catch (NUnitEvidenceException error) { return Rejected(error.Code); }
        catch (IsolatedCoverageSessionException error) { return Rejected(error.Code); }
        catch (Exception) { return Rejected("invalid-input-or-output", 2); }
    }

    private static int Rejected(string code, int exitCode = 1)
    {
        Console.Error.WriteLine("coverage-evidence: " + code);
        return exitCode;
    }

    private static Dictionary<string, string> Options(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].Any(char.IsControl)
                || (args[index] != "--assembly" && !Path.IsPathFullyQualified(args[index + 1]))
                || !values.TryAdd(args[index], args[index + 1])) throw new InvalidDataException();
        }
        return values;
    }

    private static void RequireKeys(Dictionary<string, string> values, params string[] names)
    {
        if (values.Count != names.Length || names.Any(name => !values.ContainsKey(name))) throw new InvalidDataException();
    }

    private static T Read<T>(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is <= 0 or > 64 * 1024 * 1024) throw new InvalidDataException();
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 64 });
        var stack = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) stack.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) stack.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName && !stack.Peek().Add(reader.GetString()!))
                throw new InvalidDataException();
        }
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException();
    }

    private sealed record ExportReviewRequest(int SchemaVersion, string Log, string ErrorLog, string Aggregate,
        string SdkEngine, string HostEngine, string SdkVersion, string TestHostVersion, string DotCoverVersion);
    private sealed record CoverReviewRequest(int SchemaVersion, string Log, string ErrorLog,
        IReadOnlyList<int> RequiredProfiledProcessIds, string DotCoverVersion);
    private sealed record ConsoleReviewRequest(int SchemaVersion, string CoverStdout, string CoverStderr,
        string ExportStdout, string ExportStderr, string DotCoverVersion);
    private sealed record HostReviewRequest(int SchemaVersion, string Start, string End, NUnitHostExpectation Expectation);
    private sealed record CaseMetadataRequest(int SchemaVersion, string Discovery, string CanonicalId);
}
