using FishAudio.Testing.Evidence;

namespace FishAudio.Testing;

internal static class CoverageCiTransportCommands
{
    internal static bool IsInvocation(string[] args) => args.Length > 0 &&
        args[0] is "coverage-ci-transport-pack" or "coverage-ci-transport-restore" or "coverage-ci-transport-inspect" or
            "coverage-ci-transport-pack-v2" or "coverage-ci-transport-restore-v2" or "coverage-ci-transport-inspect-v2";

    internal static int Run(string[] args)
    {
        try
        {
            if (!IsInvocation(args)) return Reject("invalid-invocation", 2);
            var schemaVersion = args[0].EndsWith("-v2", StringComparison.Ordinal) ? 2 : 1;
            string? outputPath = null;
            CoverageCiTransportExpectation? outputExpected = null;
            var result = CoverageCiTransport.Held<object>(guards =>
            {
                var options = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var index = 1; index < args.Length; index += 2)
                    if (index + 1 >= args.Length || args[index] is not ("--input" or "--expectation" or "--output") ||
                        string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].Any(char.IsControl) ||
                        !Path.IsPathFullyQualified(args[index + 1]) || !options.TryAdd(args[index], args[index + 1]))
                        throw new InvalidDataException();
                if (options.Count != 3) throw new InvalidDataException();
                var expected = CoverageCiTransport.ReadJsonFile<CoverageCiTransportExpectation>(options["--expectation"], guards);
                if (expected.SchemaVersion != schemaVersion) throw new InvalidDataException();
                outputExpected = expected;
                // Input guards are released before a final receipt is published.
                // A receipt path is never allowed to add bytes to its owner.
                outputPath = CoverageCiTransport.ExternalOutput(options["--output"], expected);
                if (args[0] is "coverage-ci-transport-pack" or "coverage-ci-transport-pack-v2")
                {
                    var input = CoverageCiTransport.ReadJsonFile<CoverageCiTransportPackInput>(options["--input"], guards);
                    if (Path.GetFullPath(input.ArchivePath).Equals(outputPath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException();
                    return schemaVersion == 2 ? CoverageCiTransport.PackV2(input, expected) : CoverageCiTransport.Pack(input, expected);
                }
                var archive = CoverageCiTransport.ReadJsonFile<CoverageCiTransportRestoreInput>(options["--input"], guards);
                if (args[0] is "coverage-ci-transport-inspect" or "coverage-ci-transport-inspect-v2")
                {
                    if (schemaVersion == 2) return CoverageCiTransport.InspectV2(archive, expected);
                    return CoverageCiTransport.Inspect(archive, expected);
                }
                return schemaVersion == 2 ? CoverageCiTransport.RestoreV2(archive, expected) : CoverageCiTransport.Restore(archive, expected);
            });
            CoverageCiTransport.PublishJson(outputPath!, result, outputExpected!);
            Console.WriteLine(schemaVersion == 2
                ? "coverage-ci-transport: exact bytes and file-time metadata admitted; source/build/main/test/baseline claims remain false."
                : "coverage-ci-transport: exact directory bytes admitted; source/build/main/test/baseline claims remain false.");
            return 0;
        }
        catch (CoverageCiTransportException error) { return Reject(error.Code, 1); }
        catch (Exception) { return Reject("invalid-input-or-output", 2); }
    }

    private static int Reject(string code, int exitCode)
    {
        Console.Error.WriteLine("coverage-ci-transport: " + code);
        return exitCode;
    }
}
