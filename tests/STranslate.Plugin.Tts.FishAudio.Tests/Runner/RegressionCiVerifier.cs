using System.Globalization;
using System.Text.Json;

internal sealed record RegressionCiExpectedContext(string Repository, long WorkflowRunId, int CurrentAttempt,
    RegressionSourceIdentity Source, string Configuration, string Sdk);

internal sealed record RegressionCiVerifiedPartition(string Partition, int Attempt, int CaseCount);

internal static class RegressionCiVerifier
{
    internal static bool IsInvocation(string[] args) => args.Any(arg => arg is "--verify-reports" or "--job-attempts");

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            Require(args.Length == 4 && args[0] == "--verify-reports" && args[2] == "--job-attempts" &&
                !string.IsNullOrWhiteSpace(args[1]) && !string.IsNullOrWhiteSpace(args[3]) &&
                !args[1].StartsWith("--", StringComparison.Ordinal) && !args[3].StartsWith("--", StringComparison.Ordinal), "invalid-ci-verifier-arguments");
            var validRun = long.TryParse(Environment.GetEnvironmentVariable("GITHUB_RUN_ID"), NumberStyles.None, CultureInfo.InvariantCulture, out var runId);
            var validAttempt = int.TryParse(Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT"), NumberStyles.None, CultureInfo.InvariantCulture, out var attempt);
            Require(validRun && validAttempt, "missing-ci-verifier-context");
            var source = await RegressionSource.CaptureAsync();
            Require(string.Equals(source.Identity.Commit, Environment.GetEnvironmentVariable("GITHUB_SHA"), StringComparison.OrdinalIgnoreCase), "ci-checkout-mismatch");
            var expected = new RegressionCiExpectedContext(Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") ?? "", runId, attempt,
                source.Identity, RegressionReport.BuildMetadata("RegressionBuildConfiguration"), RegressionReport.BuildMetadata("RegressionBuildSdk"));
            var result = Verify(expected, RegressionCatalog.Create(), args[1], args[3]);
            foreach (var partition in result)
                Console.WriteLine($"Verified {partition.Partition}: attempt {partition.Attempt}, {partition.CaseCount} passed case(s).");
            Console.WriteLine($"Verified all five partitions: {result.Sum(item => item.CaseCount)} current case(s).");
            return 0;
        }
        catch (Exception error)
        {
            // JSON/file errors can contain private artifact paths or raw contents.
            string? partition = null;
            string? code = null;
            try
            {
                partition = error.Data["RegressionCiPartition"] as string;
                code = error.Data["RegressionCiCode"] as string;
            }
            catch { /* Diagnostics cannot replace the rejected verification. */ }
            var owner = RegressionProcessProtocol.Partitions.Contains(partition, StringComparer.Ordinal) ? " for " + partition : "";
            var detail = code is not null && RegressionSelection.IsSafeId(code) ? "; " + code : "";
            Console.Error.WriteLine($"CI report verification failed{owner} ({error.GetType().Name}{detail}).");
            return 2;
        }
    }

    internal static IReadOnlyList<RegressionCiVerifiedPartition> Verify(RegressionCiExpectedContext expected,
        IReadOnlyList<RegressionCase> registry, string directory, string jobsPath)
    {
        ValidateExpected(expected);
        Require(registry.Count > 0 && registry.All(item => item is not null && RegressionSelection.IsSafeId(item.Id) &&
            RegressionProcessProtocol.Partitions.Contains(item.Partition, StringComparer.Ordinal)) &&
            registry.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == registry.Count &&
            RegressionProcessProtocol.Partitions.All(partition => registry.Any(item => item.Partition == partition)), "invalid-ci-registry");
        var jobs = ReadJobs(expected, jobsPath);
        var bundles = IndexBundles(expected, directory);
        var result = new List<RegressionCiVerifiedPartition>();
        foreach (var partition in RegressionProcessProtocol.Partitions)
        {
            try
            {
                // Choose the owner before opening JSON. An old corrupt failure is
                // retained evidence, while missing latest evidence cannot fall back.
                var attempts = jobs.Keys.Concat(bundles.Keys).Where(key => key.Partition == partition).Select(key => key.Attempt).ToArray();
                Require(attempts.Length > 0, "missing-ci-partition");
                var attempt = attempts.Max();
                Require(jobs.TryGetValue((partition, attempt), out var job) && job.Status == "completed" && job.Conclusion == "success", "unsuccessful-latest-ci-job");
                Require(bundles.TryGetValue((partition, attempt), out var bundle), "missing-latest-ci-artifact");
                var cases = registry.Where(item => item.Partition == partition).ToArray();
                VerifyBundle(expected, partition, attempt, cases, bundle!);
                result.Add(new(partition, attempt, cases.Length));
            }
            catch (Exception error)
            {
                try { error.Data["RegressionCiPartition"] = partition; }
                catch { /* Retain the original failure even if metadata is unavailable. */ }
                throw;
            }
        }
        return result;
    }

    private static void ValidateExpected(RegressionCiExpectedContext expected)
    {
        new RegressionCiIdentity { Repository = expected.Repository, WorkflowRunId = expected.WorkflowRunId, Attempt = expected.CurrentAttempt,
            Job = RegressionCiIdentity.ProducerJob, Partition = "core" }.Validate("core");
        Require(RegressionSource.HasConsistentCleanIdentity(expected.Source) && expected.Configuration == "Release" &&
            !string.IsNullOrWhiteSpace(expected.Sdk) && expected.Sdk != "unavailable", "invalid-ci-expected-build");
    }

    private sealed record Job(string Status, string? Conclusion);

    private static Dictionary<(string Partition, int Attempt), Job> ReadJobs(RegressionCiExpectedContext expected, string path)
    {
        using var document = JsonDocument.Parse(ReadBytes(path));
        var root = document.RootElement;
        RejectDuplicateProperties(root);
        Require(Text(root, "repository") == expected.Repository && Number(root, "runId") == expected.WorkflowRunId &&
            Text(root, "filter") == "all", "foreign-ci-job-snapshot");
        var items = Property(root, "jobs");
        Require(items.ValueKind == JsonValueKind.Array && Number(root, "totalCount") == items.GetArrayLength(), "incomplete-ci-job-snapshot");
        var ids = new HashSet<long>();
        var result = new Dictionary<(string, int), Job>();
        foreach (var item in items.EnumerateArray())
        {
            var id = Number(item, "id");
            var attemptNumber = Number(item, "run_attempt");
            Require(id > 0 && ids.Add(id) && Number(item, "run_id") == expected.WorkflowRunId &&
                attemptNumber > 0 && attemptNumber <= expected.CurrentAttempt, "invalid-ci-job-owner");
            var name = Text(item, "name");
            var status = Text(item, "status");
            var conclusionValue = Property(item, "conclusion");
            Require(conclusionValue.ValueKind is JsonValueKind.String or JsonValueKind.Null, "invalid-ci-job-conclusion");
            var conclusion = conclusionValue.ValueKind == JsonValueKind.Null ? null : conclusionValue.GetString();
            const string marker = "Regression (";
            var start = name.LastIndexOf(marker, StringComparison.Ordinal);
            if (start < 0) continue;
            Require((start == 0 || name[..start].EndsWith(" / ", StringComparison.Ordinal)) && name.EndsWith(')') &&
                name.IndexOf(marker, StringComparison.Ordinal) == start, "ambiguous-ci-job-name");
            var partition = name[(start + marker.Length)..^1];
            Require(RegressionProcessProtocol.Partitions.Contains(partition, StringComparer.Ordinal), "unknown-ci-job-partition");
            Require(result.TryAdd((partition, (int)attemptNumber), new(status, conclusion)), "duplicate-ci-partition-job");
        }
        return result;
    }

    private static Dictionary<(string Partition, int Attempt), string> IndexBundles(RegressionCiExpectedContext expected, string directory)
    {
        var result = new Dictionary<(string, int), string>();
        var prefix = $"regression-report-{expected.Source.Commit}-{expected.WorkflowRunId}-";
        foreach (var path in Directory.GetDirectories(directory))
        {
            var name = Path.GetFileName(path);
            Require(name.StartsWith(prefix, StringComparison.Ordinal), "foreign-ci-artifact");
            var suffix = name[prefix.Length..];
            var separator = suffix.IndexOf('-');
            Require(separator > 0, "invalid-ci-artifact-name");
            var validAttempt = int.TryParse(suffix[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var attempt);
            Require(validAttempt &&
                attempt > 0 && attempt <= expected.CurrentAttempt && suffix[..separator] == attempt.ToString(CultureInfo.InvariantCulture), "invalid-ci-artifact-attempt");
            var partition = suffix[(separator + 1)..];
            Require(RegressionProcessProtocol.Partitions.Contains(partition, StringComparer.Ordinal) &&
                result.TryAdd((partition, attempt), path), "duplicate-or-unknown-ci-artifact");
        }
        return result;
    }

    private static void VerifyBundle(RegressionCiExpectedContext expected, string partition, int attempt,
        RegressionCase[] registry, string directory)
    {
        var path = Path.Combine(directory, "report.json");
        var canonical = ReadParent(path, final: false);
        Require(Guid.TryParseExact(canonical.Report.RunId, "N", out _), "invalid-ci-report-run-id");
        var root = RegressionReport.GenerationsRoot(path);
        var owners = Directory.GetDirectories(root);
        Require(owners.Length == 1 && Path.GetFileName(owners[0]) == canonical.Report.RunId, "invalid-ci-generation-owner");
        var generations = Directory.GetFiles(owners[0], "*.json").Order(StringComparer.Ordinal).ToArray();
        Require(generations.Length > 0, "missing-ci-generations");
        foreach (var generation in generations) ValidateNumberedFile(generation, 20);
        var zeroPath = RegressionReport.GenerationPath(path, canonical.Report.RunId, 0);
        var zero = ReadParent(zeroPath, final: false);
        var projectedPath = RegressionReport.GenerationPath(path, canonical.Report.RunId, canonical.Report.Generation);
        Require(canonical.Bytes.AsSpan().SequenceEqual(ReadBytes(projectedPath)), "ci-projection-mismatch");
        var latest = ReadParent(generations[^1], final: true);
        Require(zero.Report.Generation == 0 && latest.Report.Generation == ValidateNumberedFile(generations[^1], 20), "ci-generation-number-mismatch");
        Require(RegressionReport.Identity(canonical.Report) == RegressionReport.Identity(zero.Report) &&
            RegressionReport.Identity(zero.Report) == RegressionReport.Identity(latest.Report), "ci-projected-owner-mismatch");
        // Reuse the established immutable identity/projection checks, after
        // mandatory-field checks have excluded default-filled CI identities.
        var report = RegressionReport.Read(path);
        Require(report.Generation == latest.Report.Generation, "ci-latest-generation-mismatch");
        ValidateIdentity(canonical.Report.Source, canonical.Report.Configuration, canonical.Report.Sdk, canonical.Report.Ci, expected, partition, attempt);
        ValidateIdentity(zero.Report.Source, zero.Report.Configuration, zero.Report.Sdk, zero.Report.Ci, expected, partition, attempt);
        ValidateIdentity(report.Source, report.Configuration, report.Sdk, report.Ci, expected, partition, attempt);
        Require(report.Complete && report.FinishedUtc is not null && report.ElapsedMilliseconds >= 0 &&
            report.Cases.Select(item => (item.Id, item.Partition)).SequenceEqual(registry.Select(item => (item.Id, item.Partition))) &&
            report.Cases.All(IsCleanPass), "incomplete-ci-coverage");
        var requests = report.Cases.Select(item => item.ChildRequestId).Distinct(StringComparer.Ordinal).ToArray();
        Require(requests.Length == 1 && Guid.TryParseExact(requests[0], "N", out _), "invalid-ci-child-owner");
        VerifyChild(expected, partition, attempt, report, registry, requests[0]!, directory);
    }

    private static (RegressionReport Report, byte[] Bytes) ReadParent(string path, bool final)
    {
        var bytes = ReadBytes(path);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        RejectDuplicateProperties(root);
        Properties(root, "schemaVersion", "generation", "runId", "startedUtc", "source", "configuration", "sdk", "runtime", "ci", "complete", "elapsedMilliseconds", "cases");
        ValidatePhysicalIdentity(root);
        ValidatePhysicalCases(Property(root, "cases"), requireRequestId: final);
        if (final) Properties(root, "finishedUtc");
        var report = JsonSerializer.Deserialize<RegressionReport>(bytes, RegressionReport.JsonOptions) ?? throw new InvalidDataException("missing-ci-report");
        Require(report.SchemaVersion == 1 && report.Generation >= 0 && report.Cases.Count > 0 &&
            !string.IsNullOrWhiteSpace(report.Runtime), "invalid-ci-report");
        return (report, bytes);
    }

    private static void VerifyChild(RegressionCiExpectedContext expected, string partition, int attempt, RegressionReport report,
        RegressionCase[] registry, string requestId, string directory)
    {
        var canonicals = Directory.GetFiles(directory, "*.child.json");
        Require(canonicals.Length == 1, "missing-or-duplicate-ci-child");
        var path = canonicals[0];
        var name = Path.GetFileName(path);
        var prefix = report.RunId + ".";
        const string suffix = ".child.json";
        Require(name.Length == prefix.Length + 32 + suffix.Length && name.StartsWith(prefix, StringComparison.Ordinal) &&
            Guid.TryParseExact(name[prefix.Length..^suffix.Length], "N", out _), "foreign-ci-child-path");
        var snapshots = Directory.GetFiles(directory, "*.child.json.snapshot-*.json").Order(StringComparer.Ordinal).ToArray();
        Require(snapshots.Length > 0, "missing-ci-child-snapshot");
        var highest = 0L;
        foreach (var snapshot in snapshots)
        {
            Require(snapshot.StartsWith(path + ".snapshot-", StringComparison.Ordinal), "orphan-ci-child-snapshot");
            var number = ValidateNumberedFile(snapshot, 6, Path.GetFileName(path) + ".snapshot-");
            Require(number > 0 && number <= registry.Length + 1, "invalid-ci-child-snapshot-index");
            highest = Math.Max(highest, number);
        }
        Require(highest == registry.Length + 1, "missing-final-ci-child-snapshot");
        var canonical = ReadChild(path);
        var final = ReadChild(path + $".snapshot-{highest:D6}.json");
        Require(canonical.Bytes.AsSpan().SequenceEqual(final.Bytes), "ci-child-final-mismatch");
        var request = new RegressionChildRequest
        {
            RequestId = requestId, RunId = report.RunId, Partition = partition, CaseIds = registry.Select(item => item.Id).ToArray(),
            Source = expected.Source, Configuration = expected.Configuration, Sdk = expected.Sdk, Ci = report.Ci, ManifestPath = path
        };
        RegressionProcessProtocol.ValidateManifest(canonical.Manifest, request);
        RegressionProcessProtocol.ValidateManifest(final.Manifest, request);
        ValidateIdentity(canonical.Manifest.Source, canonical.Manifest.Configuration, canonical.Manifest.Sdk, canonical.Manifest.Ci, expected, partition, attempt);
        Require(canonical.Manifest.Final && canonical.Manifest.Complete && canonical.Manifest.Cases.All(IsCleanPass) &&
            canonical.Manifest.Cases.Select(CommonResult).SequenceEqual(report.Cases.Select(CommonResult)), "inconsistent-ci-child-results");
    }

    private static (RegressionChildManifest Manifest, byte[] Bytes) ReadChild(string path)
    {
        var bytes = ReadBytes(path);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        RejectDuplicateProperties(root);
        Properties(root, "version", "requestId", "runId", "partition", "source", "configuration", "sdk", "ci", "final", "complete", "cases");
        ValidatePhysicalIdentity(root);
        ValidatePhysicalCases(Property(root, "cases"), requireRequestId: false);
        return (JsonSerializer.Deserialize<RegressionChildManifest>(bytes, RegressionProcessProtocol.Options) ?? throw new InvalidDataException("missing-ci-child"), bytes);
    }

    private static void ValidatePhysicalIdentity(JsonElement root)
    {
        Properties(Property(root, "source"), "commit", "tree", "fingerprint", "dirty", "available");
        Properties(Property(root, "ci"), "repository", "workflowRunId", "attempt", "job", "partition");
    }

    private static void ValidatePhysicalCases(JsonElement cases, bool requireRequestId)
    {
        Require(cases.ValueKind == JsonValueKind.Array && cases.GetArrayLength() > 0, "invalid-ci-case-list");
        foreach (var item in cases.EnumerateArray())
        {
            Properties(item, "id", "partition", "outcome", "elapsedMilliseconds");
            if (requireRequestId) Properties(item, "childRequestId");
        }
    }

    private static void ValidateIdentity(RegressionSourceIdentity source, string configuration, string sdk, RegressionCiIdentity? ci,
        RegressionCiExpectedContext expected, string partition, int attempt)
    {
        Require(RegressionSource.HasConsistentCleanIdentity(source) && RegressionProcessProtocol.SameSource(source, expected.Source) &&
            configuration == expected.Configuration && sdk == expected.Sdk && ci is not null, "ci-source-or-build-mismatch");
        ci!.Validate(partition);
        Require(ci.Repository == expected.Repository && ci.WorkflowRunId == expected.WorkflowRunId && ci.Attempt == attempt, "ci-context-mismatch");
    }

    private static bool IsCleanPass(RegressionCaseResult item) => item.Outcome == "passed" && item.ElapsedMilliseconds >= 0 &&
        item.ExceptionType is null && item.FailureStage is null && item.InfrastructureCode is null &&
        item.ChildInfrastructureExceptionType is null && item.SecondaryExceptionType is null && item.SecondaryFailureStage is null &&
        item.ChildExitCode is null or 0 && item.SlowActionWarnings >= 0;

    private static (string Id, string Partition, string Outcome, long Elapsed, string? Error, string? Stage) CommonResult(RegressionCaseResult item) =>
        (item.Id, item.Partition, item.Outcome, item.ElapsedMilliseconds, item.ExceptionType, item.FailureStage);

    private static long ValidateNumberedFile(string path, int width, string prefix = "")
    {
        var name = Path.GetFileNameWithoutExtension(path);
        Require(name.StartsWith(prefix, StringComparison.Ordinal), "invalid-ci-evidence-name");
        var number = name[prefix.Length..];
        var validNumber = long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value);
        Require(number.Length == width && validNumber &&
            value >= 0 && number == value.ToString("D" + width, CultureInfo.InvariantCulture), "invalid-ci-evidence-number");
        return value;
    }

    private static byte[] ReadBytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Require(stream.Length is > 0 and <= 16 * 1024 * 1024, "invalid-ci-record-size");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static JsonElement Property(JsonElement root, string name)
    {
        Require(root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out _), "missing-ci-json-field");
        return root.GetProperty(name);
    }

    private static void Properties(JsonElement root, params string[] names)
    {
        foreach (var name in names) _ = Property(root, name);
    }

    private static string Text(JsonElement root, string name)
    {
        var value = Property(root, name);
        Require(value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString()), "invalid-ci-json-text");
        return value.GetString()!;
    }

    private static long Number(JsonElement root, string name)
    {
        var value = Property(root, name);
        Require(value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _), "invalid-ci-json-number");
        return value.GetInt64();
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Require(names.Add(property.Name), "duplicate-ci-json-property");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    private static void Require(bool condition, string code)
    {
        if (condition) return;
        var error = new InvalidDataException(code);
        error.Data["RegressionCiCode"] = code;
        throw error;
    }
}
