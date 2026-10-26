using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class RegressionCiSuite
{
    internal static async Task RunAsync()
    {
        AmbiguousNewAttemptCannotBorrowAnEarlierPass();
        LatestMissingAttemptCannotBorrowAnOlderPass();
        CompactReportsRetainExactCoverageWithoutExecutingDelegates();
        SelectiveRecoveryUsesEachLatestAttemptAndPreservesOldEvidence();
        GateOnlyRerunRetainsOlderCompletedSiblings();
        CoverageMustMatchRegistryRatherThanCounts();
        SelectedReportsRequirePhysicalIdentityAndFinalOutcomes();
        DuplicateIdentityPropertiesCannotUseLastValueWins();
        ParentAndChildEvidenceCannotDisagree();
        JobAndArtifactOwnersMustBeUnambiguous();
        await CliRejectsUnknownArgumentsWithoutPrintingPrivateValuesAsync();
    }

    private static void AmbiguousNewAttemptCannotBorrowAnEarlierPass()
    {
        foreach (var name in new[] { "Other Regression (native-popups) extra", "Other Regression (native-popups)", "Regression (core) / Regression (native-popups)" })
        {
            using var fixture = new CiFixture();
            fixture.AddAll();
            fixture.AddJob("native-popups", 2);
            fixture.Jobs[^1]!["name"] = name;
            fixture.WriteJobs();
            var before = fixture.Bytes();
            Reject(() => fixture.Verify(), "An ambiguously named newer matrix job cannot disappear and authorize an older successful partition.");
            fixture.Unchanged(before);
        }
    }

    private static void LatestMissingAttemptCannotBorrowAnOlderPass()
    {
        using var fixture = new CiFixture();
        fixture.AddAll();
        fixture.AddJob("native-popups", 2);
        fixture.WriteJobs();
        var error = Reject(() => fixture.Verify(), "A newer job with no artifact cannot borrow the earlier successful popup report.");
        Require(Equals("native-popups", error.Data["RegressionCiPartition"]) && Equals("missing-latest-ci-artifact", error.Data["RegressionCiCode"]),
            "Missing latest evidence identifies only the partition needing recovery, using a fixed safe code.");
        fixture.AddBundle("native-popups", 2);
        Require(fixture.Verify().Single(item => item.Partition == "native-popups").Attempt == 2,
            "Only the missing partition needs a new complete attempt.");
    }

    private static void CompactReportsRetainExactCoverageWithoutExecutingDelegates()
    {
        using var fixture = new CiFixture();
        fixture.AddAll();
        var before = fixture.Bytes();
        var result = fixture.Verify();
        Require(result.Select(item => item.Partition).SequenceEqual(new[] { "core", "native-settings", "native-input", "native-popups", "native-cards" }) &&
            result.All(item => item.Attempt == 1 && item.CaseCount == 2), "Five compact reports cover the exact ordered current registry.");
        Require(fixture.Calls == 0, "Metadata verification never invokes case delegates.");
        fixture.Unchanged(before);
    }

    private static void SelectiveRecoveryUsesEachLatestAttemptAndPreservesOldEvidence()
    {
        using var fixture = new CiFixture();
        fixture.AddAll();
        File.WriteAllText(Path.Combine(fixture.Bundle("native-popups", 1), "report.json"), "{");
        fixture.AddJob("native-popups", 2);
        fixture.AddBundle("native-popups", 2);
        fixture.WriteJobs();
        var before = fixture.Bytes();
        Require(fixture.Verify().Select(item => item.Attempt).SequenceEqual(new[] { 1, 1, 1, 2, 1 }),
            "An old malformed D1 stays evidence while the latest D2 completes selective recovery.");
        fixture.Unchanged(before);
        fixture.ChangeLatest("native-popups", 2, node => node["complete"] = false);
        Reject(() => fixture.Verify(), "A bad D2 cannot fall back to D1.");
        fixture.AddJob("native-popups", 3);
        fixture.AddBundle("native-popups", 3);
        fixture.WriteJobs();
        Require(fixture.Verify().Single(item => item.Partition == "native-popups").Attempt == 3, "D3 alone repairs the outstanding partition.");
    }

    private static void GateOnlyRerunRetainsOlderCompletedSiblings()
    {
        using var fixture = new CiFixture();
        fixture.AddAll();
        fixture.AddJob("native-popups", 2);
        fixture.AddBundle("native-popups", 2, runtime: "10.0.14");
        fixture.WriteJobs();
        Require(fixture.Verify().Select(item => item.Attempt).SequenceEqual(new[] { 1, 1, 1, 2, 1 }),
            "Gate attempt three reuses the latest five completed producers; a runtime patch change does not invalidate them.");
    }

    private static void CoverageMustMatchRegistryRatherThanCounts()
    {
        foreach (var mutation in new[] { "removed", "added", "partition", "duplicate", "order" })
        {
            using var fixture = new CiFixture();
            fixture.AddAll();
            var registry = fixture.Registry.ToList();
            if (mutation == "removed") registry.RemoveAt(0);
            if (mutation == "added") registry.Add(new("ci.new/owner", "core", () => throw new InvalidOperationException("not executable")));
            if (mutation == "partition") registry[0] = registry[0] with { Partition = "native-input" };
            if (mutation == "duplicate") registry[0] = registry[1];
            if (mutation == "order") (registry[0], registry[1]) = (registry[1], registry[0]);
            Reject(() => fixture.Verify(registry), "A registry change cannot pass by retaining the same aggregate report count.");
        }
    }

    private static void SelectedReportsRequirePhysicalIdentityAndFinalOutcomes()
    {
        foreach (var field in new[] { "schemaVersion", "generation", "runId", "startedUtc", "source", "configuration", "sdk", "ci", "complete", "cases" })
        {
            using var fixture = new CiFixture();
            fixture.AddAll();
            fixture.ChangeParents("core", 1, node => node.AsObject().Remove(field));
            Reject(() => fixture.Verify(), "Missing parent fields cannot acquire valid values from DTO defaults.");
        }
        foreach (var field in new[] { "commit", "tree", "fingerprint", "dirty", "available" })
        {
            using var fixture = new CiFixture();
            fixture.AddAll();
            fixture.ChangeParents("core", 1, node => node["source"]!.AsObject().Remove(field));
            Reject(() => fixture.Verify(), "Source identity must be physically present.");
        }
        foreach (var outcome in new[] { "failed", "timed-out", "running", "not-run" })
        {
            using var fixture = new CiFixture();
            fixture.AddAll();
            fixture.ChangeLatest("core", 1, node => node["cases"]![0]!["outcome"] = outcome);
            Reject(() => fixture.Verify(), "Complete=true does not authorize an unfinished or failed case.");
        }
        foreach (var mutation in new[] { "no-end", "dirty", "unavailable", "commit", "tree", "fingerprint", "sdk", "configuration", "ci-job", "ci-run", "ci-repository", "ci-attempt", "ci-partition", "case-elapsed", "passed-error", "bare", "missing-canonical", "malformed-highest", "bad-generation-name", "projection-metadata" })
        {
            using var fixture = new CiFixture();
            fixture.AddAll();
            var bundle = fixture.Bundle("core", 1);
            if (mutation == "no-end") fixture.ChangeLatest("core", 1, node => node.AsObject().Remove("finishedUtc"));
            if (mutation == "dirty") fixture.ChangeParents("core", 1, node => node["source"]!["dirty"] = true);
            if (mutation == "unavailable") fixture.ChangeParents("core", 1, node => node["source"]!["available"] = false);
            if (mutation is "commit" or "tree" or "fingerprint") fixture.ChangeParents("core", 1, node => node["source"]![mutation] = new string('c', mutation == "fingerprint" ? 64 : 40));
            if (mutation is "sdk" or "configuration") fixture.ChangeParents("core", 1, node => node[mutation] = "different");
            if (mutation.StartsWith("ci-", StringComparison.Ordinal)) fixture.ChangeParents("core", 1, node =>
            {
                var ci = node["ci"]!;
                if (mutation == "ci-job") ci["job"] = "build-test-package";
                if (mutation == "ci-run") ci["workflowRunId"] = 432;
                if (mutation == "ci-repository") ci["repository"] = "another/repository";
                if (mutation == "ci-attempt") ci["attempt"] = 2;
                if (mutation == "ci-partition") ci["partition"] = "native-input";
            });
            if (mutation == "case-elapsed") fixture.ChangeLatest("core", 1, node => node["cases"]![0]!["elapsedMilliseconds"] = -1);
            if (mutation == "passed-error") fixture.ChangeLatest("core", 1, node => node["cases"]![0]!["exceptionType"] = "System.IO.IOException");
            if (mutation == "bare") Directory.Delete(Path.Combine(bundle, "report.json.generations"), recursive: true);
            if (mutation == "missing-canonical") File.Delete(Path.Combine(bundle, "report.json"));
            if (mutation == "malformed-highest") File.WriteAllText(fixture.Latest("core", 1), "{");
            if (mutation == "bad-generation-name") File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.Latest("core", 1))!, "invalid.json"), "{}");
            if (mutation == "projection-metadata")
            {
                var canonical = Path.Combine(bundle, "report.json");
                var node = JsonNode.Parse(File.ReadAllBytes(canonical))!;
                var projected = RegressionReport.GenerationPath(canonical, node["runId"]!.GetValue<string>(), node["generation"]!.GetValue<long>());
                foreach (var path in new[] { canonical, projected }) Change(path, value => value["cases"]![0]!["id"] = "ci.changed/owner");
            }
            Reject(() => fixture.Verify(), "Selected source/build/owner or durable evidence corruption cannot authorize green coverage.");
        }
    }

    private static void ParentAndChildEvidenceCannotDisagree()
    {
        foreach (var mutation in new[] { "missing", "missing-final", "extra-child", "source", "sdk", "configuration", "ci", "request", "case", "order", "elapsed", "outcome", "final", "canonical-tamper", "later-malformed", "missing-source-field" })
        {
            using var fixture = new CiFixture();
            fixture.AddAll();
            var canonical = Directory.GetFiles(fixture.Bundle("core", 1), "*.child.json").Single();
            var snapshots = Directory.GetFiles(fixture.Bundle("core", 1), "*.child.json.snapshot-*.json").Order(StringComparer.Ordinal).ToArray();
            if (mutation == "missing") File.Delete(canonical);
            else if (mutation == "missing-final") File.Delete(snapshots[^1]);
            else if (mutation == "extra-child") File.Copy(canonical, canonical.Replace(".child.json", ".extra.child.json", StringComparison.Ordinal));
            else if (mutation == "later-malformed") File.WriteAllText(canonical + ".snapshot-000004.json", "{");
            else
            {
                foreach (var path in mutation == "canonical-tamper" ? new[] { canonical } : new[] { canonical, snapshots[^1] })
                {
                    Change(path, node =>
                    {
                        if (mutation == "source") node["source"]!["commit"] = new string('d', 40);
                        if (mutation is "sdk" or "configuration") node[mutation] = "different";
                        if (mutation == "ci") node["ci"]!["attempt"] = 2;
                        if (mutation == "request") node["requestId"] = Guid.NewGuid().ToString("N");
                        if (mutation is "case" or "canonical-tamper") node["cases"]![0]!["id"] = "ci.foreign/owner";
                        if (mutation == "order") { var items = node["cases"]!.AsArray(); var first = items[0]!.DeepClone(); items[0] = items[1]!.DeepClone(); items[1] = first; }
                        if (mutation == "elapsed") node["cases"]![0]!["elapsedMilliseconds"] = 99;
                        if (mutation == "outcome") node["cases"]![0]!["outcome"] = "not-run";
                        if (mutation == "final") node["final"] = false;
                        if (mutation == "missing-source-field") node["source"]!.AsObject().Remove("dirty");
                    });
                }
            }
            Reject(() => fixture.Verify(), "A green parent requires one coherent complete child and exact final immutable evidence.");
        }
    }

    private static void DuplicateIdentityPropertiesCannotUseLastValueWins()
    {
        using var fixture = new CiFixture();
        fixture.AddAll();
        var bundle = fixture.Bundle("core", 1);
        foreach (var path in Directory.GetFiles(Path.Combine(bundle, "report.json.generations"), "*.json", SearchOption.AllDirectories).Append(Path.Combine(bundle, "report.json")))
        {
            var text = File.ReadAllText(path);
            const string field = "\"sdk\": \"10.0.402\"";
            Require(text.Contains(field, StringComparison.Ordinal), "The duplicate-SDK fixture targets an actual serialized identity field.");
            File.WriteAllText(path, text.Replace(field, "\"sdk\": \"foreign\", " + field, StringComparison.Ordinal));
        }
        Reject(() => fixture.Verify(), "Duplicate critical identity fields cannot silently authorize the final JSON value.");
    }

    private static void JobAndArtifactOwnersMustBeUnambiguous()
    {
        foreach (var mutation in new[] { "total", "duplicate-id", "duplicate-owner", "future", "wrong-run", "repository", "filter", "failed", "cancelled", "skipped", "in-progress", "ambiguous-name", "artifact-future", "artifact-without-job", "artifact-wrong-run", "extra-directory" })
        {
            using var fixture = new CiFixture();
            fixture.AddAll();
            if (mutation == "duplicate-id") fixture.Jobs.Add(fixture.Jobs[0]!.DeepClone());
            if (mutation == "duplicate-owner") { var extra = fixture.Jobs[0]!.DeepClone(); extra["id"] = 9999; fixture.Jobs.Add(extra); }
            if (mutation == "future") fixture.Jobs[0]!["run_attempt"] = 4;
            if (mutation == "wrong-run") fixture.Jobs[0]!["run_id"] = 432;
            if (mutation is "failed" or "cancelled" or "skipped") fixture.Jobs[0]!["conclusion"] = mutation == "failed" ? "failure" : mutation;
            if (mutation == "in-progress") fixture.Jobs[0]!["status"] = "in_progress";
            if (mutation == "ambiguous-name") fixture.Jobs[0]!["name"] = "Other Regression (core) extra";
            if (mutation == "artifact-future") fixture.AddBundle("core", 4);
            if (mutation == "artifact-without-job") fixture.AddBundle("core", 2);
            if (mutation == "artifact-wrong-run") Directory.Move(fixture.Bundle("core", 1), fixture.Bundle("core", 1).Replace("-431-", "-432-", StringComparison.Ordinal));
            if (mutation == "extra-directory") Directory.CreateDirectory(Path.Combine(fixture.DirectoryPath, "unrecognized"));
            fixture.WriteJobs();
            if (mutation is "total" or "repository" or "filter") Change(fixture.JobsPath, node =>
            {
                if (mutation == "total") node["totalCount"] = 100;
                if (mutation == "repository") node["repository"] = "other/repository";
                if (mutation == "filter") node["filter"] = "latest";
            });
            Reject(() => fixture.Verify(), "Missing, ambiguous, foreign, future or unsuccessful latest producers fail closed.");
        }
    }

    private static async Task CliRejectsUnknownArgumentsWithoutPrintingPrivateValuesAsync()
    {
        var original = Console.Error;
        using var output = new StringWriter();
        try
        {
            Console.SetError(output);
            Require(await RegressionCiVerifier.RunAsync(["--verify-reports", "private-artifact-sentinel", "--unknown", "private-job-sentinel"]) != 0,
                "The verifier cannot ignore an unsupported option.");
            Require(output.ToString().Contains("invalid-ci-verifier-arguments", StringComparison.Ordinal) &&
                !output.ToString().Contains("sentinel", StringComparison.Ordinal), "CLI diagnostics use a fixed code without private paths or values.");
        }
        finally { Console.SetError(original); }
    }

    private static Exception Reject(Action action, string reason)
    {
        try { action(); }
        catch (Exception error) when (error is RegressionSelectionException or InvalidDataException or JsonException or IOException or UnauthorizedAccessException) { return error; }
        throw new InvalidOperationException(reason);
    }

    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    private static void Change(string path, Action<JsonNode> change)
    {
        var node = JsonNode.Parse(File.ReadAllBytes(path))!;
        change(node);
        File.WriteAllText(path, node.ToJsonString(RegressionReport.JsonOptions));
    }

    private sealed class CiFixture : IDisposable
    {
        private static readonly string[] Partitions = ["core", "native-settings", "native-input", "native-popups", "native-cards"];
        internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "fishaudio-ci-verifier-" + Guid.NewGuid().ToString("N"));
        internal string JobsPath => Path.Combine(DirectoryPath, "jobs.json");
        internal RegressionCiExpectedContext Expected { get; }
        internal RegressionCase[] Registry { get; }
        internal JsonArray Jobs { get; } = [];
        internal int Calls { get; private set; }
        internal CiFixture()
        {
            Directory.CreateDirectory(DirectoryPath);
            var commit = new string('a', 40);
            var tree = new string('b', 40);
            Expected = new("example/repository", 431, 3, new RegressionSourceIdentity
            {
                Available = true, Commit = commit, Tree = tree,
                Fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(commit + "\n" + tree + "\n")))
            }, "Release", "10.0.402");
            Registry = Partitions.SelectMany((partition, index) => new[]
            {
                new RegressionCase($"ci.partition-{index}/first", partition, () => { Calls++; throw new InvalidOperationException("Metadata must not execute."); }),
                new RegressionCase($"ci.partition-{index}/second", partition, () => { Calls++; throw new InvalidOperationException("Metadata must not execute."); })
            }).ToArray();
        }
        internal void AddAll()
        {
            foreach (var partition in Partitions) { AddJob(partition, 1); AddBundle(partition, 1); }
            WriteJobs();
        }
        internal void AddJob(string partition, int attempt) => Jobs.Add(new JsonObject
        {
            ["id"] = 1000 + Jobs.Count, ["run_id"] = 431, ["run_attempt"] = attempt,
            ["name"] = "Verify release / Regression (" + partition + ")", ["status"] = "completed", ["conclusion"] = "success",
            ["head_sha"] = new string('c', 40), ["steps"] = new JsonArray()
        });
        internal void WriteJobs() => File.WriteAllText(JobsPath, new JsonObject
        {
            ["repository"] = Expected.Repository, ["runId"] = Expected.WorkflowRunId, ["filter"] = "all", ["totalCount"] = Jobs.Count, ["jobs"] = Jobs.DeepClone()
        }.ToJsonString());
        internal string Bundle(string partition, int attempt) => Path.Combine(DirectoryPath, $"regression-report-{Expected.Source.Commit}-431-{attempt}-{partition}");
        internal void AddBundle(string partition, int attempt, string runtime = "10.0.13")
        {
            var directory = Bundle(partition, attempt);
            Directory.CreateDirectory(directory);
            var requestId = Guid.NewGuid().ToString("N");
            var report = new RegressionReport
            {
                StartedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), Source = Expected.Source,
                Configuration = Expected.Configuration, Sdk = Expected.Sdk, Runtime = runtime,
                Ci = new() { Repository = Expected.Repository, WorkflowRunId = 431, Attempt = attempt, Job = "regression", Partition = partition },
                Invocation = ["--all", "--partition", partition, "--results", "<results>"],
                Cases = Registry.Where(item => item.Partition == partition).Select(item => new RegressionCaseResult { Id = item.Id, Partition = partition }).ToList()
            };
            var path = Path.Combine(directory, "report.json");
            report.Create(path);
            foreach (var item in report.Cases) item.ChildRequestId = requestId;
            report.Write(path);
            var stale = File.ReadAllBytes(path);
            report.Write(path);
            foreach (var item in report.Cases)
            {
                item.Outcome = "passed"; item.ElapsedMilliseconds = 7; item.ActionOrdinal = 3;
                item.QueueToEntryMilliseconds = 1; item.SlowActionWarnings = 1;
            }
            report.Complete = true; report.FinishedUtc = report.StartedUtc.AddMinutes(1); report.ElapsedMilliseconds = 20;
            report.Write(path);
            File.WriteAllBytes(path, stale);
            File.Delete(RegressionReport.GenerationPath(path, report.RunId, 2));
            var request = new RegressionChildRequest
            {
                RequestId = requestId, RunId = report.RunId, Partition = partition, CaseIds = report.Cases.Select(item => item.Id).ToArray(),
                Source = report.Source, Configuration = report.Configuration, Sdk = report.Sdk, Ci = report.Ci,
                ManifestPath = Path.Combine(directory, report.RunId + "." + Guid.NewGuid().ToString("N") + ".child.json")
            };
            var manifest = RegressionChildManifest.Create(request);
            foreach (var item in manifest.Cases) { item.Outcome = "passed"; item.ElapsedMilliseconds = 7; }
            RegressionProcessProtocol.WriteNew(RegressionProcessProtocol.SnapshotPath(request, 2), manifest);
            manifest.Final = true; manifest.Complete = true;
            RegressionProcessProtocol.WriteNew(RegressionProcessProtocol.SnapshotPath(request, 3), manifest);
            RegressionProcessProtocol.WriteNew(request.ManifestPath, manifest);
        }
        internal string Latest(string partition, int attempt) => Directory.GetFiles(Path.Combine(Bundle(partition, attempt), "report.json.generations"), "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Last();
        internal void ChangeLatest(string partition, int attempt, Action<JsonNode> change) => Change(Latest(partition, attempt), change);
        internal void ChangeParents(string partition, int attempt, Action<JsonNode> change)
        {
            var directory = Bundle(partition, attempt);
            foreach (var path in Directory.GetFiles(Path.Combine(directory, "report.json.generations"), "*.json", SearchOption.AllDirectories).Append(Path.Combine(directory, "report.json"))) Change(path, change);
        }
        internal IReadOnlyList<RegressionCiVerifiedPartition> Verify(IReadOnlyList<RegressionCase>? registry = null) => RegressionCiVerifier.Verify(Expected, registry ?? Registry, DirectoryPath, JobsPath);
        internal Dictionary<string, byte[]> Bytes() => Directory.GetFiles(DirectoryPath, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
        internal void Unchanged(Dictionary<string, byte[]> original) => Require(original.All(item => File.ReadAllBytes(item.Key).SequenceEqual(item.Value)), "Verification cannot rewrite any failed or successful evidence bytes.");
        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
