using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FishAudio.Testing.Evidence;

namespace FishAudio.Testing;

internal sealed record FullCoverageCiArtifactRequest(string OwnerRecordPath, string ArchivePath);
internal sealed record FullCoverageCiSelectionRequest(int SchemaVersion, string JobsPath,
    IReadOnlyList<FullCoverageCiArtifactRequest> Artifacts);

// This boundary observes actual files. It does not authenticate how the caller
// acquired its expectation, the jobs response or the downloaded owner/archive.
// Selected owners still require independent raw coverage admission, API/event
// authentication, smoke ordering and a separate eventual publication receipt.
internal static class FullCoverageCiCommands
{
    private static readonly JsonSerializerOptions JobsJson = CreateJobsJson();

    internal static bool IsInvocation(string[] args) => args.Length > 0 && args[0] == "coverage-ci-select";

    internal static int Run(string[] args)
    {
        try
        {
            if (!IsInvocation(args)) return Reject("invalid-invocation", 2);
            string? outputPath = null;
            CoverageCiTransportExpectation? outputExpected = null;
            var selection = CoverageCiTransport.Held(guards =>
            {
                var options = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var index = 1; index < args.Length; index += 2)
                    if (index + 1 >= args.Length || args[index] is not ("--input" or "--expectation" or "--output") ||
                        string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].Any(char.IsControl) ||
                        !Path.IsPathFullyQualified(args[index + 1]) || !options.TryAdd(args[index], args[index + 1]))
                        throw new InvalidDataException();
                if (options.Count != 3) throw new InvalidDataException();
                var expected = CoverageCiTransport.ReadJsonFile<FullCoverageCiContext>(options["--expectation"], guards);
                FullCoverageCiProvenance.ValidateContext(expected);
                // Output must remain outside every possible attempt owner and
                // outside the independently pinned source directory.
                outputExpected = new(1, expected.OperationRoot, expected.Preparation.Prepared.SourceDirectory, expected.Tree);
                outputPath = CoverageCiTransport.ExternalOutput(options["--output"], outputExpected);
                var input = CoverageCiTransport.ReadJsonFile<FullCoverageCiSelectionRequest>(options["--input"], guards);
                if (input.SchemaVersion != 1 || input.Artifacts is not { Count: > 0 and <= 200_000 })
                    throw new InvalidDataException();
                var jobsBytes = CoverageCiTransport.ReadJsonBytes(input.JobsPath, guards);
                var jobsSha256 = Sha(jobsBytes);
                if (jobsSha256 != expected.JobsSha256) throw new FullCoverageCiProvenanceException("jobs-digest");
                // Duplicate keys are rejected before DTO deserialization, even
                // within otherwise ignored API metadata. Only raw job/step
                // records tolerate extra fields retained by the existing fetcher.
                var jobs = CoverageCiTransport.ReadJson<FullCoverageCiJobsSnapshot>(jobsBytes, JobsJson);
                var artifacts = new List<FullCoverageCiArtifact>();
                foreach (var item in input.Artifacts)
                {
                    if (item is null) throw new InvalidDataException();
                    var ownerBytes = CoverageCiTransport.ReadJsonBytes(item.OwnerRecordPath, guards);
                    var owner = CoverageCiTransport.ReadJson<FullCoverageCiOwner>(ownerBytes);
                    if (owner.SchemaVersion != 1 || owner.Kind != "coverage-ci-operation-owner")
                        throw new FullCoverageCiProvenanceException("artifact-role");
                    var originalRoot = FullCoverageCiProvenance.OriginalRootFor(expected, owner.Role, owner.ShardIndex, owner.Attempt);
                    var archiveExpected = new CoverageCiTransportExpectation(1, originalRoot,
                        expected.Preparation.Prepared.SourceDirectory, expected.Tree);
                    // This outer guard keeps the observed container immutable
                    // through whole-owner selection. Inspect owns its own shared
                    // parser/lease guards and returns only after their release.
                    _ = CoverageCiTransport.OpenFile(item.ArchivePath, guards);
                    var inspected = CoverageCiTransport.Inspect(new(1, item.ArchivePath, owner.ArchiveSha256), archiveExpected);
                    var observed = new FullCoverageCiInventory(inspected.Manifest.Directories,
                        inspected.Manifest.Files.Select(file => new FullCoverageCiFile(file.Path, file.Length, file.Sha256)).ToArray());
                    artifacts.Add(new(owner, Sha(ownerBytes), inspected.Receipt.ArchiveSha256, observed));
                }
                return FullCoverageCiProvenance.Select(new(1, jobs, jobsSha256, artifacts.AsReadOnly()), expected);
            });
            // The immutable final name appears only after every held input has
            // released and a fresh pending file has flushed and closed.
            CoverageCiTransport.PublishJson(outputPath!, selection, outputExpected!);
            Console.WriteLine("coverage-ci-select: byte-observed whole owners selected; event/raw/main/baseline/publication claims remain false.");
            return 0;
        }
        catch (FullCoverageCiProvenanceException error) { return Reject(error.Code, 1); }
        catch (CoverageCiTransportException error)
        {
            return Reject(error.InnerException is FullCoverageCiProvenanceException provenance ? provenance.Code : error.Code, 1);
        }
        catch (Exception) { return Reject("invalid-input-or-output", 2); }
    }

    private static JsonSerializerOptions CreateJobsJson()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(FullCoverageCiJob) || info.Type == typeof(FullCoverageCiStep))
                info.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
        });
        return new(CoverageCiTransport.JsonOptions) { TypeInfoResolver = resolver };
    }
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static int Reject(string code, int exitCode)
    {
        Console.Error.WriteLine("coverage-ci-select: " + code);
        return exitCode;
    }
}
