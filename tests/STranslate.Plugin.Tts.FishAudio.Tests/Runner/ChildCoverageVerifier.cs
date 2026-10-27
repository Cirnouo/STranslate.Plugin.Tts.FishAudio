using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NUnitCompatibility;

// Metadata only. The existing supervised scenario writes its observations; this
// reader never starts a process, invokes a scenario or supplies a Job claim.
internal static class ChildCoverageVerifier
{
    private const int MaximumFiles = 1024;
    private const int MaximumDirectories = 64;
    private const int MaximumFileBytes = 16 * 1024 * 1024;
    private const int MaximumTotalBytes = 64 * 1024 * 1024;

    internal static bool IsInvocation(string[] args) => args.Length > 0 && args[0] == "--verify-child-coverage";

    internal static int Run(string[] args)
    {
        try
        {
            var options = ReadOptions(args);
            var receipt = ReadValidated(options["--fixture-directory"], options["--canonical-id"], options["--tree"]);
            var output = options["--output"];
            Require(Path.IsPathFullyQualified(output), "output-must-be-absolute");
            var fullOutput = Path.GetFullPath(output);
            Require(!Within(fullOutput, Path.GetFullPath(options["--fixture-directory"])), "output-overlaps-raw-fixture");
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
            using var stream = new FileStream(fullOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, receipt, RegressionReport.JsonOptions);
            stream.Flush(flushToDisk: true);
            Console.WriteLine("child-coverage: verified supervised child observations; no Job or baseline claim.");
            return 0;
        }
        catch (ChildCoverageException error)
        {
            Console.Error.WriteLine("child-coverage: " + error.Code);
            return 1;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("child-coverage: invalid-input-or-output");
            return 2;
        }
    }

    internal static ChildCoverageReceipt ReadValidated(string fixtureDirectory, string expectedCanonical, string expectedTree)
    {
        var locks = new List<FileStream>();
        var succeeded = false;
        try
        {
            Require(Path.IsPathFullyQualified(fixtureDirectory) && Directory.Exists(fixtureDirectory), "fixture-must-be-absolute-directory");
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fixtureDirectory));
            for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
                Require(directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) == 0, "raw-root-ancestor-missing-or-reparse");
            Require(expectedCanonical == ChildCompatibilityCases.NUnitCaseId && IsGitHash(expectedTree), "unexpected-case-or-tree");
            var files = OpenRawTree(root, locks);
            var identityPath = Child(root, "identity.json", files);
            Require(files[identityPath].Length <= 64 * 1024, "identity-size-limit");
            var identity = RegressionProcessProtocol.Read<ChildIdentity>(identityPath);
            Require(identity.SchemaVersion == 1 && identity.ProcessId > 0 && identity.ChildProcessId > 0 &&
                identity.ProcessId != identity.ChildProcessId && identity.NUnitCaseId == expectedCanonical &&
                identity.NUnitFullName == typeof(ChildCompatibilityCases).FullName + "." + nameof(ChildCompatibilityCases.StreamingTextRunsInTheSupervisedChild) &&
                identity.LegacyCaseId == ChildCompatibilityCases.LegacyCaseId && IsRunId(identity.RunId) && IsRunId(identity.ChildRequestId) &&
                identity.ChildExitCode == 0 && identity.ChildStdoutDrained && identity.ChildStderrDrained,
                "incomplete-child-identity");
            ValidateSource(identity.Source, expectedTree);

            var requests = files.Keys.Where(path => Path.GetFileName(path).EndsWith(".child.json.request.json", StringComparison.Ordinal)).ToArray();
            Require(requests.Length == 1 && Path.GetDirectoryName(requests[0])!.Equals(root, StringComparison.OrdinalIgnoreCase),
                "owned-request-count");
            var request = RegressionProcessProtocol.Read<RegressionChildRequest>(requests[0]);
            RegressionProcessProtocol.ValidateRequest(request);
            Require(request.RequestId == identity.ChildRequestId && request.RunId == identity.RunId && request.Partition == "core" &&
                request.CaseIds.SequenceEqual([identity.LegacyCaseId], StringComparer.Ordinal) && request.LegacyArguments is null && request.Probe is null &&
                RegressionProcessProtocol.SameSource(request.Source, identity.Source), "request-identity-mismatch");
            var manifestPath = DeclaredChild(root, request.ManifestPath, files);
            var manifestName = Path.GetFileName(manifestPath).Split('.');
            Require((manifestPath + ".request.json").Equals(requests[0], StringComparison.OrdinalIgnoreCase) &&
                manifestName.Length == 4 && manifestName[0] == identity.RunId && IsRunId(manifestName[1]) &&
                manifestName[2] == "child" && manifestName[3] == "json", "request-manifest-owner-mismatch");
            var manifests = files.Keys.Where(path => Path.GetFileName(path).EndsWith(".child.json", StringComparison.Ordinal)).ToArray();
            Require(manifests.Length == 1 && manifests[0].Equals(manifestPath, StringComparison.OrdinalIgnoreCase), "owned-manifest-count");
            var manifest = RegressionProcessProtocol.Read<RegressionChildManifest>(manifestPath);
            RegressionProcessProtocol.ValidateManifest(manifest, request);
            var snapshots = files.Keys.Where(path => Path.GetFileName(path).Contains(".child.json.snapshot-", StringComparison.Ordinal)).ToArray();
            Require(snapshots.Length == request.CaseIds.Length + 1, "incomplete-or-extra-child-snapshot-set");
            var indices = new HashSet<int>();
            foreach (var snapshot in snapshots)
            {
                var prefix = manifestPath + ".snapshot-";
                Require(snapshot.StartsWith(prefix, StringComparison.Ordinal) && snapshot.EndsWith(".json", StringComparison.Ordinal)
                    && snapshot.Length == prefix.Length + 6 + 5, "orphan-or-malformed-child-snapshot");
                var number = snapshot.AsSpan(prefix.Length, 6);
                var index = 0;
                Require(number.ToString().All(char.IsAsciiDigit) && int.TryParse(number, out index)
                    && index > 0 && index <= request.CaseIds.Length + 1 && indices.Add(index), "invalid-child-snapshot-index");
                var observed = RegressionProcessProtocol.Read<RegressionChildManifest>(snapshot);
                RegressionProcessProtocol.ValidateManifest(observed, request);
                Require(index == request.CaseIds.Length + 1 ? observed.Final && observed.Complete : !observed.Final && !observed.Complete,
                    "invalid-child-snapshot-lifecycle");
                if (index <= request.CaseIds.Length)
                    Require(observed.Cases.Take(index).All(item => item.Outcome == "passed") &&
                        observed.Cases.Skip(index).All(item => item.Outcome == "not-run"), "invalid-child-snapshot-progress");
            }
            var finalPath = DeclaredChild(root, RegressionProcessProtocol.SnapshotPath(request, request.CaseIds.Length + 1), files);
            var final = RegressionProcessProtocol.Read<RegressionChildManifest>(finalPath);
            RegressionProcessProtocol.ValidateManifest(final, request);
            Require(manifest.Final && manifest.Complete && final.Final && final.Complete && manifest.Cases.Count == 1 &&
                manifest.Cases[0].Outcome == "passed" && manifest.Cases[0].Id == identity.LegacyCaseId &&
                JsonSerializer.Serialize(manifest, RegressionProcessProtocol.Options) == JsonSerializer.Serialize(final, RegressionProcessProtocol.Options),
                "incomplete-or-inconsistent-final-manifest");

            var reportPath = Child(root, "regression.json", files);
            var report = RegressionReport.Read(reportPath);
            var initialPath = DeclaredChild(root, RegressionReport.GenerationPath(reportPath, report.RunId, 0), files);
            var generationPath = DeclaredChild(root, RegressionReport.GenerationPath(reportPath, report.RunId, report.Generation), files);
            Require(report.SchemaVersion == 1 && report.Generation > 0 && report.RunId == identity.RunId && report.Complete &&
                report.StartedUtc.Offset == TimeSpan.Zero && report.FinishedUtc is { } finished && finished.Offset == TimeSpan.Zero && finished >= report.StartedUtc &&
                report.Configuration == "Release" && report.Configuration == request.Configuration && report.Sdk == request.Sdk &&
                report.Sdk == "10.0.403" && RegressionProcessProtocol.SameSource(report.Source, identity.Source) &&
                report.Cases.Count == 1 && report.Invocation.SequenceEqual(["nunit-compatibility-child", identity.LegacyCaseId], StringComparer.Ordinal) &&
                report.SelectionReasons.SequenceEqual(["nunit-child-compatibility"], StringComparer.Ordinal), "incomplete-authoritative-report");
            var result = report.Cases[0];
            Require(result.Id == identity.LegacyCaseId && result.Partition == request.Partition && result.Outcome == "passed" &&
                result.ChildRequestId == identity.ChildRequestId && (result.ChildExitCode is null or 0) &&
                result.ExceptionType is null && result.FailureStage is null && result.InfrastructureCode is null &&
                result.ChildInfrastructureExceptionType is null && result.SecondaryExceptionType is null && result.SecondaryFailureStage is null,
                "report-child-result-mismatch");
            _ = initialPath; // RegressionReport.Read owns initial/latest generation identity validation.
            var artifacts = files.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item =>
            {
                item.Value.Position = 0;
                return new ChildCoverageArtifact(Path.GetRelativePath(root, item.Key).Replace('\\', '/'),
                    Convert.ToHexStringLower(SHA256.HashData(item.Value)), item.Value.Length);
            }).ToArray();
            succeeded = true;
            return new(1, true, expectedCanonical, identity.NUnitFullName, identity.LegacyCaseId, identity.RunId,
                identity.ChildRequestId, identity.ProcessId, identity.ChildProcessId, 0, true, true, identity.Source,
                Path.GetRelativePath(root, reportPath).Replace('\\', '/'), Path.GetRelativePath(root, generationPath).Replace('\\', '/'),
                Path.GetRelativePath(root, requests[0]).Replace('\\', '/'), Path.GetRelativePath(root, manifestPath).Replace('\\', '/'),
                Path.GetRelativePath(root, finalPath).Replace('\\', '/'), Array.AsReadOnly(artifacts));
        }
        catch (ChildCoverageException) { throw; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or JsonException or OverflowException or KeyNotFoundException or RegressionSelectionException)
        {
            throw new ChildCoverageException("raw-child-evidence-rejected");
        }
        finally
        {
            var releaseFailed = false;
            foreach (var stream in locks)
            {
                try { stream.Dispose(); }
                catch { releaseFailed = true; }
            }
            if (succeeded && releaseFailed) throw new ChildCoverageException("raw-file-release-failed");
        }
    }

    private static Dictionary<string, FileStream> OpenRawTree(string root, List<FileStream> locks)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new(root));
        var files = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);
        var directoryCount = 0;
        long totalBytes = 0;
        while (pending.TryPop(out var directory))
        {
            Require(++directoryCount <= MaximumDirectories && directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) == 0, "raw-directory-limit-missing-or-reparse");
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                Require(entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) == 0 && Within(entry.FullName, root), "raw-entry-missing-escape-or-reparse");
                if (entry is DirectoryInfo child) { pending.Push(child); continue; }
                Require(files.Count < MaximumFiles && entry is FileInfo, "raw-file-count-or-type");
                var stream = new FileStream(entry.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                locks.Add(stream);
                Require(stream.Length is >= 0 and <= MaximumFileBytes && (totalBytes += stream.Length) <= MaximumTotalBytes,
                    "raw-file-size-limit");
                Require(files.TryAdd(entry.FullName, stream), "duplicate-raw-file");
                if (entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ValidateJson(stream);
            }
        }
        return files;
    }

    private static void ValidateJson(FileStream stream)
    {
        Require(stream.Length > 0, "empty-json-record");
        var bytes = new byte[(int)stream.Length];
        stream.Position = 0;
        stream.ReadExactly(bytes);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 64 });
        var names = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) names.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) names.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName)
                Require(names.Peek().Add(reader.GetString()!), "duplicate-json-property");
        }
        stream.Position = 0;
    }

    private static string Child(string root, string relative, Dictionary<string, FileStream> files)
        => DeclaredChild(root, Path.Combine(root, relative), files);

    private static string DeclaredChild(string root, string path, Dictionary<string, FileStream> files)
    {
        Require(Path.IsPathFullyQualified(path), "declared-path-must-be-absolute");
        var full = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(root, full);
        Require(Within(full, root) && !relative.Contains(':') && files.ContainsKey(full), "declared-path-escapes-or-missing");
        return full;
    }

    private static bool Within(string path, string root) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void ValidateSource(RegressionSourceIdentity source, string tree)
        => Require(source is not null && source.Available && source.Snapshot && !source.Dirty && source.Tree == tree &&
            IsGitHash(source.Commit) && IsSha256(source.Fingerprint) && source.Fingerprint == Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes("snapshot-v1\n" + source.Commit + "\n" + tree + "\n"))), "snapshot-source-mismatch");

    private static bool IsGitHash(string? value) => value is { Length: 40 or 64 } && value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');
    private static bool IsSha256(string? value) => value is { Length: 64 } && IsGitHash(value);
    private static bool IsRunId(string? value) => Guid.TryParseExact(value, "N", out var parsed) && parsed != Guid.Empty;

    private static Dictionary<string, string> ReadOptions(string[] args)
    {
        Require(IsInvocation(args) && args.Length == 9, "invalid-arguments");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
            Require(args[index] is "--fixture-directory" or "--canonical-id" or "--tree" or "--output" &&
                !string.IsNullOrWhiteSpace(args[index + 1]) && result.TryAdd(args[index], args[index + 1]), "invalid-arguments");
        return result;
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw new ChildCoverageException(code);
    }

    private sealed class ChildIdentity
    {
        [JsonRequired] public int SchemaVersion { get; set; }
        [JsonRequired] public int ProcessId { get; set; }
        [JsonRequired] public int ChildProcessId { get; set; }
        [JsonRequired, JsonPropertyName("nUnitCaseId")] public string NUnitCaseId { get; set; } = "";
        [JsonRequired, JsonPropertyName("nUnitFullName")] public string NUnitFullName { get; set; } = "";
        [JsonRequired] public string LegacyCaseId { get; set; } = "";
        [JsonRequired] public string RunId { get; set; } = "";
        [JsonRequired] public string ChildRequestId { get; set; } = "";
        [JsonRequired] public int ChildExitCode { get; set; }
        [JsonRequired] public bool ChildStdoutDrained { get; set; }
        [JsonRequired] public bool ChildStderrDrained { get; set; }
        [JsonRequired] public RegressionSourceIdentity Source { get; set; } = null!;
    }
}

internal sealed class ChildCoverageException(string code) : Exception(code)
{
    internal string Code { get; } = code;
}

internal sealed record ChildCoverageArtifact(string RelativePath, string Sha256, long Length);
internal sealed record ChildCoverageReceipt(int SchemaVersion, bool Complete, string CanonicalId, string NUnitFullName,
    string LegacyCaseId, string RunId, string RequestId, int ParentProcessId, int ChildProcessId, int ChildExitCode,
    bool ChildStdoutDrained, bool ChildStderrDrained, RegressionSourceIdentity Source, string ReportRelativePath,
    string AuthoritativeGenerationRelativePath, string RequestRelativePath, string ManifestRelativePath,
    string FinalSnapshotRelativePath, IReadOnlyList<ChildCoverageArtifact> Artifacts)
{
    public bool JobOwnershipClaim => false;
    public bool BaselineClaim => false;
    public string SourceFingerprintKind => "sha256-snapshot-v1-commit-tree";
}
