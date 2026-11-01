using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FishAudio.Testing.Evidence;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

// These are offline contract observations at the real PowerShell helper seam.
// They create no Git repository, network request, profiler, desktop or product
// case. Synthetic API data proves rejection/selection behavior, never origin or
// CI/event authentication. Existing transport/raw-admission cases own those seams.
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class FullCoverageCiOrchestrationTests
{
    private const string Commit = "0123456789012345678901234567890123456789";
    private const string Tree = "abcdefabcdefabcdefabcdefabcdefabcdefabcd";
    private const string Repository = "example/FishAudio";
    private const long RunId = 7109;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private string _root = "";
    private string _helper = "";
    private string _shell = "";
    private string _dotnet = "";
    private string _actualTool = "";
    private int _invocations;

    [SetUp]
    public void CreatePureHelperBoundary()
    {
        var repositoryRoot = Environment.GetEnvironmentVariable("FISHAUDIO_TEST_REPOSITORY_ROOT");
        if (Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT") is { Length: > 0 } contextPath)
        {
            using var context = JsonDocument.Parse(File.ReadAllText(contextPath));
            var snapshot = context.RootElement.GetProperty("root").GetString()!;
            if (repositoryRoot is not null && !Path.GetFullPath(repositoryRoot).Equals(Path.GetFullPath(snapshot), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("coverage-ci-contract-explicit-root-mismatch");
            repositoryRoot ??= snapshot;
        }
        if (repositoryRoot is null || !Path.IsPathFullyQualified(repositoryRoot))
            throw new InvalidOperationException("coverage-ci-contract-explicit-root-required");
        _helper = Path.Combine(repositoryRoot, "tools", "ci", "FullCoverageCi.Common.ps1");
        if (!File.Exists(_helper)) throw new InvalidOperationException("coverage-ci-contract-helper-missing");
        _shell = Environment.GetEnvironmentVariable("FISHAUDIO_PWSH_EXECUTABLE") ?? FindShell();
        _dotnet = Environment.GetEnvironmentVariable("FISHAUDIO_DOTNET_EXECUTABLE") ?? FindExecutable("dotnet.exe");
        var executing = Path.GetFullPath(typeof(FullCoverageCiOrchestrationTests).Assembly.Location);
        var normalTests = Path.GetFullPath(Path.Combine(repositoryRoot, "tests/FishAudio.Testing.Tests/bin/Release/net10.0-windows/FishAudio.Testing.Tests.dll"));
        var coverageTests = Path.GetFullPath(Path.Combine(repositoryRoot, "tests/FishAudio.Testing.Tests/bin/coverage/Release/net10.0-windows/FishAudio.Testing.Tests.dll"));
        var coverage = executing.Equals(coverageTests, StringComparison.OrdinalIgnoreCase);
        if (!coverage && !executing.Equals(normalTests, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("coverage-ci-contract-executing-layout");
        _actualTool = Path.GetFullPath(Path.Combine(repositoryRoot, "tools/FishAudio.Testing", coverage ?
            "bin/coverage/Release/net10.0-windows/FishAudio.Testing.dll" : "bin/Release/net10.0-windows/FishAudio.Testing.dll"));
        Assert.That(File.Exists(Path.ChangeExtension(_actualTool, ".runtimeconfig.json")), Is.True);
        Assert.That(ShaFile(_actualTool), Is.EqualTo(ShaFile(typeof(FullCoverageCiProvenance).Assembly.Location)));
        _root = Path.Combine(Path.GetTempPath(), "fa-coverage-ci-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "contract.ps1"), ContractScript, new UTF8Encoding(false));
    }

    [TearDown]
    public void ReleaseOwnedDirectory()
    {
        if (_root.Length == 0) return;
        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed) return;
        var root = RequireOwnedFixtureRoot();
        AssertRegularFixtureTree(root);
        Directory.Delete(root, recursive: true);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-ci-orchestration/actual-numeric-job-is-distinct-from-logical-github-job")]
    public async Task ActualNumericJobAndMainSourceAreBoundWithoutAuthenticatingFixtures()
    {
        var result = await AdmitAsync("Caller", Create());
        Assert.Multiple(() =>
        {
            Assert.That(result["numericJobId"]!.GetValue<long>(), Is.EqualTo(202));
            Assert.That(result["logicalJob"]!.GetValue<string>(), Is.EqualTo("coverage_shard"));
            Assert.That(result["apiEventAuthenticated"]!.GetValue<bool>(), Is.False);
        });
        var nonUtc = Create();
        // The same instant as the valid 01:05 UTC start. Normalizing away the
        // source offset would wrongly accept this otherwise valid caller.
        nonUtc["observations"]!["apiJob"]!["started_at"] = "2026-10-31T09:05:00.0000000+08:00";
        nonUtc["jobs"]!["jobs"]![2]!["started_at"] = "2026-10-31T09:05:00.0000000+08:00";
        await RejectAsync("Caller", nonUtc, "utc-time");
    }

    private static IEnumerable<TestCaseData> CallerFaults()
    {
        foreach (var pair in new[]
        {
            ("event", "runner-context"), ("ref", "runner-context"), ("runner-sha", "runner-context"),
            ("tree", "source-context"), ("api-run", "api-run-context"), ("api-repository", "api-run-context"),
            ("api-attempt", "api-run-context"), ("logical-as-numeric", "numeric-current-job"),
            ("numeric-unobserved", "current-job-observation"), ("jobs-digest", "context"),
            ("fence-order", "attempt-fences"), ("pagination", "jobs-snapshot"),
        }) yield return Row(pair.Item1, pair.Item2, "caller-reject-");
    }

    [TestCaseSource(nameof(CallerFaults))]
    public async Task CallerObservationDriftCannotAcquireAuthority(string fault, string reason)
    {
        var data = Create();
        await AdmitAsync("Caller", data); // Every fault starts from an admitted fixture.
        var observations = data["observations"]!;
        switch (fault)
        {
            case "event": observations["environment"]!["GITHUB_EVENT_NAME"] = "pull_request"; break;
            case "ref": observations["environment"]!["GITHUB_REF"] = "refs/pull/7/merge"; break;
            case "runner-sha": observations["environment"]!["GITHUB_SHA"] = new string('9', 40); break;
            case "tree": observations["git"]!["tree"] = new string('9', 40); break;
            case "api-run": observations["apiRun"]!["id"] = RunId + 1; break;
            case "api-repository": observations["apiRun"]!["head_repository"]!["full_name"] = "foreign/FishAudio"; break;
            case "api-attempt": observations["apiRun"]!["run_attempt"] = 1; break;
            case "logical-as-numeric": observations["apiJob"]!["id"] = "coverage_shard"; break;
            case "numeric-unobserved": observations["apiJob"]!["id"] = 99999; break;
            case "jobs-digest": data["jobsSha256"] = Sha("different jobs bytes"); break;
            case "fence-order": data["jobs"]!["attempts"]![1]!["runStartedAt"] = Time(0); break;
            case "pagination": data["jobs"]!["totalCount"] = 99; break;
            default: throw new InvalidOperationException("unknown-caller-fault");
        }
        await RejectAsync("Caller", data, reason);
        if (fault == "fence-order")
        {
            var nonUtc = Create();
            // Same instant and ordering as the valid second fence, with an
            // explicitly forbidden source offset that must remain observable.
            nonUtc["jobs"]!["attempts"]![1]!["runStartedAt"] = "2026-10-31T09:00:00.0000000+08:00";
            await RejectAsync("Caller", nonUtc, "utc-time");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-ci-orchestration/json-reader-preserves-strings-arrays-and-number-kinds")]
    public async Task CoverageJsonReaderPreservesUtcLexemesAndActualJsonValueKinds()
    {
        var result = await ContractRawAsync("JsonBoundary", """
            {"date":"2026-10-31T09:00:00.0000000+08:00","empty":[],"singleton":[7],"many":[true,null,"x"],
             "integer":9007199254740993,"fraction":1.25,"integralDouble":1.0,"emptyString":""}
            """);
        Assert.That(result["accepted"]!.GetValue<bool>(), Is.True, result["code"]?.ToString());
        var value = result["value"]!;
        Assert.Multiple(() =>
        {
            Assert.That(value["dateType"]!.GetValue<string>(), Is.EqualTo("System.String"));
            Assert.That(value["dateValue"]!.GetValue<string>(), Is.EqualTo("2026-10-31T09:00:00.0000000+08:00"));
            Assert.That(value["emptyIsArray"]!.GetValue<bool>(), Is.True);
            Assert.That(value["emptyCount"]!.GetValue<int>(), Is.Zero);
            Assert.That(value["singletonIsArray"]!.GetValue<bool>(), Is.True);
            Assert.That(value["singletonCount"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(value["singletonValue"]!.GetValue<long>(), Is.EqualTo(7));
            Assert.That(value["manyCount"]!.GetValue<int>(), Is.EqualTo(3));
            Assert.That(value["nullElementPreserved"]!.GetValue<bool>(), Is.True);
            Assert.That(value["integerType"]!.GetValue<string>(), Is.EqualTo("System.Int64"));
            Assert.That(value["integerValue"]!.GetValue<long>(), Is.EqualTo(9007199254740993L));
            Assert.That(value["fractionType"]!.GetValue<string>(), Is.EqualTo("System.Double"));
            Assert.That(value["fractionValue"]!.GetValue<double>(), Is.EqualTo(1.25));
            Assert.That(value["integralDoubleType"]!.GetValue<string>(), Is.EqualTo("System.Double"));
            Assert.That(value["integralDoubleAcceptedAsId"]!.GetValue<bool>(), Is.False);
            Assert.That(value["emptyStringLength"]!.GetValue<int>(), Is.Zero);
        });
        var empty = await ContractRawAsync("ReadValue", "[]");
        Assert.That(empty["accepted"]!.GetValue<bool>(), Is.True);
        Assert.That(empty["value"]!.AsArray(), Is.Empty);
        var singleton = await ContractRawAsync("ReadValue", "[{\"value\":7}]");
        Assert.That(singleton["accepted"]!.GetValue<bool>(), Is.True);
        Assert.That(singleton["value"]!.AsArray(), Has.Count.EqualTo(1));
        Assert.That(singleton["value"]![0]!["value"]!.GetValue<long>(), Is.EqualTo(7));
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-ci-orchestration/json-reader-rejects-case-collisions-and-duplicates")]
    public async Task CoverageJsonReaderRejectsAmbiguousMembersBeforePowerShellProjection()
    {
        var positive = await ContractRawAsync("ReadValue", "{\"parent\":{\"Foo\":1}}");
        Assert.That(positive["accepted"]!.GetValue<bool>(), Is.True);
        Assert.That(positive["value"]!["parent"]!["Foo"]!.GetValue<long>(), Is.EqualTo(1));
        var collision = await ContractRawAsync("ReadValue", "{\"parent\":{\"Foo\":1,\"foo\":2}}");
        Assert.That(collision["accepted"]!.GetValue<bool>(), Is.False);
        Assert.That(collision["code"]!.GetValue<string>(), Is.EqualTo("full-coverage-ci-orchestration-json-case-collision"));
        var duplicate = await ContractRawAsync("ReadValue", "{\"parent\":{\"Foo\":1,\"Foo\":2}}");
        Assert.That(duplicate["accepted"]!.GetValue<bool>(), Is.False);
        Assert.That(duplicate["code"]!.GetValue<string>(), Is.EqualTo("hybrid-json-duplicate-property"));
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-ci-orchestration/cached-view-retains-original-api-job-id-and-attempt")]
    public async Task CachedSuccessfulApiViewNeedsTheExactOriginalExecutionWitness()
    {
        var data = Create();
        data["roleIndex"] = 0;
        var cached = data["jobs"]!["jobs"]![0]!.DeepClone();
        cached["id"] = 1101; cached["run_attempt"] = 2;
        AddJob(data, cached);
        var selected = await AdmitAsync("Latest", data);
        Assert.That(selected["job"]!["id"]!.GetValue<long>(), Is.EqualTo(101));
        Assert.That(selected["actualAttempt"]!.GetValue<int>(), Is.EqualTo(1));
        cached["runner_id"] = 999;
        await RejectAsync("Latest", data, "unmatched-or-ambiguous-cached-job");
    }

    private static IEnumerable<TestCaseData> LatestFailures()
    {
        foreach (var outcome in new[] { "failure", "cancelled", "skipped", "neutral", "in_progress" })
            yield return Row(outcome, "unsuccessful-latest-job", "latest-reject-");
    }

    [TestCaseSource(nameof(LatestFailures))]
    public async Task NewActualPrepareFailureCannotBorrowPreviousSuccessfulIdentity(string outcome, string reason)
    {
        var data = Create(); data["roleIndex"] = 0;
        await AdmitAsync("Latest", data);
        var failed = Job(1101, "Coverage / prepare", 2, 61, 62);
        failed["status"] = outcome == "in_progress" ? "in_progress" : "completed";
        failed["conclusion"] = outcome == "in_progress" ? null : outcome;
        failed["started_at"] = null; failed["completed_at"] = null; failed["steps"] = null;
        AddJob(data, failed);
        await RejectAsync("Latest", data, reason);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-ci-orchestration/new-prepare-id-invalidates-old-shard-with-identical-seals")]
    public async Task PreparationChainUsesActualJobIdentityAndNotAnEqualPreparedDigest()
    {
        var data = Create();
        await AdmitAsync("Chain", data);
        data["prepare"]!["job"]!["id"] = 1101;
        data["prepare"]!["actualAttempt"] = 2;
        await RejectAsync("Chain", data, "mixed-preparation");
    }

    private static IEnumerable<TestCaseData> PrepareOutcomeFaults()
    {
        foreach (var fault in new[] { "incomplete", "cleanup-failure", "wrong-operation" })
            yield return Row(fault, "failed-preparation-outcome", "prepare-outcome-reject-");
    }

    [TestCaseSource(nameof(PrepareOutcomeFaults))]
    public async Task FailedPreparationCannotSealLeftoverPreparedBytes(string fault, string reason)
    {
        var data = Create();
        data["prepareOutcome"] = new JsonObject { ["schemaVersion"] = 1, ["kind"] = "full-coverage-local-outcome",
            ["operation"] = "Prepare", ["runId"] = data["context"]!["preparation"]!["runId"]!.DeepClone(),
            ["operationComplete"] = true, ["failureType"] = null, ["reason"] = null };
        await AdmitAsync("PrepareOutcome", data);
        switch (fault)
        {
            case "incomplete": data["prepareOutcome"]!["operationComplete"] = false; break;
            case "cleanup-failure": data["prepareOutcome"]!["failureType"] = "IOException"; break;
            case "wrong-operation": data["prepareOutcome"]!["operation"] = "Full"; break;
        }
        await RejectAsync("PrepareOutcome", data, reason);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-ci-orchestration/rest-job-cannot-start-before-successful-smoke-completes")]
    public async Task WaitingInsideAnAlreadyStartedRestJobCannotStandInForSmokeScheduling()
    {
        var data = Create();
        await AdmitAsync("Chain", data);
        data["shard"]!["execution"]!["started"] = Ticks(3.5);
        await RejectAsync("Chain", data, "shard-before-smoke");
    }

    private static IEnumerable<TestCaseData> SmokeFaults()
    {
        foreach (var fault in new[] { "empty-plan", "empty-execution", "wrong-case", "wrong-owner", "duplicate-case", "wrong-index", "wrong-count" })
            yield return Row(fault, "smoke-selection", "smoke-reject-");
    }

    [TestCaseSource(nameof(SmokeFaults))]
    public async Task SmokeRequiresNonemptyExactFreshRawReimportMembership(string fault, string reason)
    {
        var data = Create();
        await AdmitAsync("Smoke", data);
        var value = data["smokeValue"]!;
        switch (fault)
        {
            case "empty-plan": data["preparedValue"]!["wholePlan"]!["smokeCanonicalIds"] = new JsonArray(); break;
            case "empty-execution": value["selectedCanonicalIds"] = new JsonArray(); break;
            case "wrong-case": value["selectedCanonicalIds"]![0] = "foreign/case"; break;
            case "wrong-owner": value["selectedOwnerIds"]![0] = "foreign-owner"; break;
            case "duplicate-case": value["selectedCanonicalIds"] = new JsonArray("smoke/a", "smoke/a"); break;
            case "wrong-index": value["shardIndex"] = 1; break;
            case "wrong-count": value["shardCount"] = 4; break;
            default: throw new InvalidOperationException("unknown-smoke-fault");
        }
        // Complete/success flags cannot excuse a mismatched actual case set.
        value["selectedOwnersAdmitted"] = true; value["completeBaseline"] = true;
        await RejectAsync("Smoke", data, reason);
    }

    private static IEnumerable<TestCaseData> OwnerFaults()
    {
        foreach (var fault in new[] { "numeric-job", "root", "artifact-name", "attempt", "archive", "inventory", "evidence", "finished-time", "extra-success-flag", "enum-string" })
            yield return Row(fault, fault is "archive" or "inventory" ? "artifact-byte-binding" :
                fault == "evidence" ? "operation-evidence" : fault == "finished-time" ? "owner-time" :
                fault is "extra-success-flag" or "enum-string" ? "owner-shape" : "artifact-owner", "owner-reject-");
    }

    [TestCaseSource(nameof(OwnerFaults))]
    public async Task WholeOwnerAndObservedPayloadCannotBeRelabelledByReceiptFlags(string fault, string reason)
    {
        var data = Create();
        await AdmitAsync("Owner", data);
        var owner = data["owner"]!;
        switch (fault)
        {
            case "numeric-job": owner["jobId"] = 999; break;
            case "root": owner["originalRoot"] = @"C:\ci\other\shard-000000\attempt-1"; break;
            case "artifact-name": owner["artifactName"] = "coverage-diagnostics-shard-000000"; break;
            case "attempt": owner["attempt"] = 2; break;
            case "archive": data["archiveSha256"] = Sha("different actual archive"); break;
            case "inventory": data["inventory"]!["files"]![0]!["sha256"] = Sha("different actual bytes"); break;
            case "evidence": owner["evidenceSha256"] = Sha("different evidence"); break;
            case "finished-time": owner["finishedUtc"] = Time(65); break;
            case "extra-success-flag": owner["completeBaseline"] = true; break;
            case "enum-string": owner["role"] = "1"; break;
            default: throw new InvalidOperationException("unknown-owner-fault");
        }
        await RejectAsync("Owner", data, reason);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-ci-orchestration/complete-artifact-list-keeps-diagnostics-out-of-owner-set")]
    public async Task ArtifactInventoryRequiresEveryWholeOwnerWhileKeepingDiagnosticsSeparate()
    {
        var data = Create();
        await AdmitAsync("Downloads", data);
        data["downloads"]!.AsArray().Clear();
        await RejectAsync("Downloads", data, "download-set");
        data = Create();
        var diagnostic = data["downloads"]![0]!.DeepClone();
        diagnostic["artifactId"] = 802; diagnostic["artifactName"] = "coverage-diagnostics-smoke";
        data["downloads"]!.AsArray().Add(diagnostic);
        await RejectAsync("Downloads", data, "download-set");
    }

    private static IEnumerable<TestCaseData> FailureCategories()
    {
        foreach (var category in new[] { "TestFailed", "CollectionFailed", "Cancelled", "OperationFailed" })
            yield return Row(category, "unused", "diagnostic-");
    }

    [TestCaseSource(nameof(FailureCategories))]
    public async Task DiagnosticUploadRetainsFailureCategoryAndNeverBecomesAnOwner(string category, string unused)
    {
        var data = Create(); data["category"] = category;
        var diagnostic = await AdmitAsync("Diagnostic", data);
        Assert.Multiple(() =>
        {
            Assert.That(diagnostic["kind"]!.GetValue<string>(), Is.EqualTo("coverage-ci-diagnostic"));
            Assert.That(diagnostic["category"]!.GetValue<string>(), Is.EqualTo(category));
            Assert.That(diagnostic["stage"]!.GetValue<string>(), Is.EqualTo("raw-collection"));
            Assert.That(diagnostic["artifactName"]!.GetValue<string>(), Does.StartWith("coverage-diagnostics-"));
            Assert.That(diagnostic["testResult"]!.GetValue<string>(), Is.EqualTo(category == "TestFailed" ? "failed" : "not-established"));
            Assert.That(diagnostic["collectionResult"]!.GetValue<string>(), Is.EqualTo(category == "CollectionFailed" ? "failed" : "not-established"));
            foreach (var flag in new[] { "operationComplete", "ownerSelectionComplete", "coverageUnionComplete", "completeBaseline", "publicationAuthorized", "tiaSelectionAuthorized" })
                Assert.That(diagnostic[flag]!.GetValue<bool>(), Is.False, flag);
        });
    }

    private static IEnumerable<TestCaseData> ComposeAdapterCases()
    {
        foreach (var code in new[] { 0, 37 })
            yield return new TestCaseData(code).SetName("coverage-ci-orchestration/compose-array-adapter-exit-" + code)
                .SetProperty("FishAudioCaseId", "coverage-ci-orchestration/compose-array-adapter-exit-" + code);
    }

    [TestCaseSource(nameof(ComposeAdapterCases))]
    public async Task ComposeAdapterPreservesAllIndexedArraysAndOriginalExitCode(int expectedExit)
    {
        var data = Create();
        var invocation = await AdmitAsync("Compose", data);
        var capture = Path.Combine(_root, "capture.json");
        var target = Path.Combine(_root, "capture.ps1");
        File.WriteAllText(target, """
            param([string]$Operation, [string]$RepositoryRoot, [string]$ResultsDirectory, [string]$PreparedPath,
                [string]$ExpectedPreparedSha256, [int]$ShardCount, [string[]]$ShardManifestPath, [string[]]$ExpectedShardSha256)
            [IO.File]::WriteAllText($ResultsDirectory, (@{ operation=$Operation; count=$ShardCount; paths=@($ShardManifestPath);
                hashes=@($ExpectedShardSha256) } | ConvertTo-Json -Depth 10))
            exit EXIT_CODE
            """.Replace("EXIT_CODE", expectedExit.ToString(CultureInfo.InvariantCulture)), new UTF8Encoding(false));
        invocation["entry"] = target;
        invocation["parameters"]!["ResultsDirectory"] = capture;
        var input = Path.Combine(_root, "compose.json");
        File.WriteAllText(input, invocation.ToJsonString(), new UTF8Encoding(false));
        var adapter = Path.Combine(_root, "adapter.ps1");
        data["launcherPath"] = adapter;
        await AdmitAsync("Launcher", data);
        var execution = await RunProcessAsync(adapter, ["-InputPath", input]);
        Assert.That(execution.ExitCode, Is.EqualTo(expectedExit), execution.Error);
        var captured = JsonNode.Parse(File.ReadAllText(capture))!;
        Assert.Multiple(() =>
        {
            Assert.That(captured["operation"]!.GetValue<string>(), Is.EqualTo("Compose"));
            Assert.That(captured["count"]!.GetValue<int>(), Is.EqualTo(3));
            Assert.That(captured["paths"]!.AsArray().Select(node => node!.GetValue<string>()),
                Is.EqualTo(Enumerable.Range(0, 3).Select(index => @"C:\ci\coverage\shard-" + index.ToString("D6") + @"\attempt-1\shard.json")));
            Assert.That(captured["hashes"]!.AsArray().Select(node => node!.GetValue<string>()),
                Is.EqualTo(Enumerable.Range(0, 3).Select(index => Sha("shard-" + index))));
        });
    }

    private static IEnumerable<TestCaseData> EntryFaults()
    {
        foreach (var fault in new[] { "unknown-field", "driver-unknown-field", "index-string", "occupied-archive", "occupied-owner", "aliased-archive-owner" })
            yield return new TestCaseData("Producer", fault).SetName("coverage-ci-orchestration/entry-reject-producer-" + fault)
                .SetProperty("FishAudioCaseId", "coverage-ci-orchestration/entry-reject-producer-" + fault);
        foreach (var fault in new[] { "unknown-field", "driver-missing-field", "index-string", "occupied-result", "occupied-work", "occupied-compose" })
            yield return new TestCaseData("Consumer", fault).SetName("coverage-ci-orchestration/entry-reject-consumer-" + fault)
                .SetProperty("FishAudioCaseId", "coverage-ci-orchestration/entry-reject-consumer-" + fault);
    }

    [TestCaseSource(nameof(EntryFaults))]
    public async Task ActualEntryRejectsInvalidRequestOrOccupiedOutputBeforeCreatingWork(string entry, string fault)
    {
        var data = Create();
        var action = entry == "Producer" ? "RunShard" : fault == "occupied-compose" ? "SelectAndCompose" : "RestorePrerequisites";
        var request = CreateRequest(entry, action, data);
        data["request"] = request; data["action"] = action;
        await AdmitAsync(entry + "Request", data); // Causal precondition for the exact shape to be faulted.
        var positive = await InvokeEntryAsync(entry, action, data, request);
        var expectedStage = entry == "Producer" ? "preparation-prerequisites" :
            action == "RestorePrerequisites" ? "prerequisite-selection" : "complete-owner-selection";
        Assert.Multiple(() =>
        {
            Assert.That(positive.ExitCode, Is.EqualTo(1), "The inert archive must be rejected, without granting any evidence authority.");
            Assert.That(positive.Error, Does.Contain("failed stage=" + expectedStage + " reason=tool-rejected"));
            var work = request["workDirectory"]!.GetValue<string>();
            Assert.That(Directory.Exists(work), Is.True, "The real entry must first get through preflight.");
            var processes = Directory.GetFiles(work, "process.json", SearchOption.AllDirectories);
            Assert.That(processes, Has.Length.EqualTo(1), "Only the existing byte inspector/selector child may have run.");
            var process = JsonNode.Parse(File.ReadAllText(processes[0]))!;
            // The shared launcher intentionally records a rejected target as
            // failed even when Job ownership and every output drain completed.
            Assert.That(process["complete"]!.GetValue<bool>(), Is.False);
            Assert.That(process["reason"]!.GetValue<string>(), Is.EqualTo("hybrid-process-target-failed"));
            Assert.That(process["exitCode"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(process["targetExitCode"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(process["targetExitObserved"]!.GetValue<bool>(), Is.True);
            Assert.That(process["started"]!.GetValue<bool>() && process["assigned"]!.GetValue<bool>(), Is.True);
            Assert.That(process["activeProcesses"]!.GetValue<int>(), Is.Zero);
            Assert.That(process["controlEvents"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(process["cleanupExceptionTypes"]!.AsArray(), Is.Empty);
            Assert.That(process["executable"]!.GetValue<string>(), Is.EqualTo(_dotnet));
            Assert.That(process["arguments"]![0]!.GetValue<string>(), Is.EqualTo(_actualTool));
            Assert.That(process["stdoutDrained"]!.GetValue<bool>() && process["stderrDrained"]!.GetValue<bool>() &&
                process["allChildrenExited"]!.GetValue<bool>(), Is.True);
        });
        var positiveWork = RequireOwnedFixtureChild(request["workDirectory"]!.GetValue<string>(), entry.ToLowerInvariant() + "-work");
        AssertRegularFixtureTree(positiveWork);
        Directory.Delete(positiveWork, recursive: true);
        var positiveDiagnostic = RequireOwnedFixtureChild(request["diagnosticPath"]!.GetValue<string>(), entry.ToLowerInvariant() + "-diagnostic.json");
        if (File.Exists(positiveDiagnostic) && (File.GetAttributes(positiveDiagnostic) &
            (FileAttributes.ReparsePoint | FileAttributes.Device | FileAttributes.Directory)) != 0)
            throw new InvalidOperationException("coverage-ci-contract-diagnostic-kind");
        File.Delete(positiveDiagnostic);
        string? occupied = null;
        switch (fault)
        {
            case "unknown-field": request["ignoreFailure"] = true; break;
            case "driver-unknown-field": request["driver"]!["executeAnyway"] = true; break;
            case "driver-missing-field": request["driver"]!.AsObject().Remove("toolSha256"); break;
            case "index-string": request["shardIndex"] = "1"; break;
            case "occupied-archive": occupied = request["archivePath"]!.GetValue<string>(); break;
            case "occupied-owner": occupied = request["ownerRecordPath"]!.GetValue<string>(); break;
            case "occupied-result": occupied = request["resultPath"]!.GetValue<string>(); break;
            case "occupied-work": Directory.CreateDirectory(request["workDirectory"]!.GetValue<string>()); break;
            case "occupied-compose": Directory.CreateDirectory(request["composeDirectory"]!.GetValue<string>()); break;
            case "aliased-archive-owner": request["ownerRecordPath"] = request["archivePath"]!.DeepClone(); break;
            default: throw new InvalidOperationException("unknown-entry-fault");
        }
        var preserved = new byte[] { 0, 255, 13, 10, 10, 128, 65 };
        if (occupied is not null) File.WriteAllBytes(occupied, preserved);
        var execution = await InvokeEntryAsync(entry, action, data, request);
        var reason = fault switch
        {
            "unknown-field" => "request-shape",
            "driver-unknown-field" or "driver-missing-field" => "driver-shape",
            "index-string" => "request-role",
            "aliased-archive-owner" => "output-path-alias",
            _ => "fresh-output-required",
        };
        Assert.Multiple(() =>
        {
            Assert.That(execution.ExitCode, Is.EqualTo(1));
            Assert.That(execution.Error, Does.Contain("failed stage=preflight reason=" + reason + ";"));
            var work = request["workDirectory"]!.GetValue<string>();
            if (fault == "occupied-work") Assert.That(Directory.EnumerateFileSystemEntries(work), Is.Empty);
            else Assert.That(Directory.Exists(work), Is.False, "No work/process directory may precede preflight rejection.");
            if (occupied is not null) Assert.That(File.ReadAllBytes(occupied), Is.EqualTo(preserved));
            if (entry == "Producer" && fault != "occupied-owner")
                Assert.That(File.Exists(request["ownerRecordPath"]!.GetValue<string>()), Is.False);
        });
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-ci-orchestration/lowercase-diagnostic-retains-original-category-and-stage-without-work")]
    public async Task AcceptedLowercaseDiagnosticDispatchKeepsTheOriginalFailureClassification()
    {
        var data = Create();
        var diagnosticPath = Path.Combine(_root, "explicit-diagnostic.json");
        var request = new JsonObject { ["schemaVersion"] = 1, ["role"] = 1, ["shardIndex"] = 1,
            ["diagnosticPath"] = diagnosticPath, ["category"] = "TestFailed", ["stage"] = "framework-terminal" };
        data["request"] = request; data["action"] = "Diagnostic";
        await AdmitAsync("ProducerRequest", data);
        var execution = await InvokeEntryAsync("Producer", "diagnostic", data, request);
        Assert.That(execution.ExitCode, Is.EqualTo(1));
        var diagnostic = JsonNode.Parse(File.ReadAllText(diagnosticPath))!;
        Assert.Multiple(() =>
        {
            Assert.That(diagnostic["category"]!.GetValue<string>(), Is.EqualTo("TestFailed"));
            Assert.That(diagnostic["stage"]!.GetValue<string>(), Is.EqualTo("framework-terminal"));
            Assert.That(diagnostic["artifactName"]!.GetValue<string>(), Does.StartWith("coverage-diagnostics-"));
            Assert.That(diagnostic["operationComplete"]!.GetValue<bool>(), Is.False);
            Assert.That(execution.Error, Does.Contain("failed stage=framework-terminal"));
            Assert.That(Directory.Exists(Path.Combine(_root, "producer-work")), Is.False);
        });
    }

    private JsonObject CreateRequest(string entry, string action, JsonObject data)
    {
        var originalRoot = Path.Combine(_root, "original-roots");
        var preparationRoot = Path.Combine(originalRoot, "prepare", "attempt-1");
        var prepared = data["context"]!["preparation"]!["prepared"]!;
        data["context"]!["operationRoot"] = originalRoot;
        prepared["preparedPath"] = Path.Combine(preparationRoot, "coverage", "prepared.json");
        prepared["sourceDirectory"] = Path.Combine(preparationRoot, "source");
        prepared["snapshotContextPath"] = Path.Combine(preparationRoot, "snapshot-context.json");
        prepared["metadataToolFile"] = Path.Combine(preparationRoot, "source", "tools", "FishAudio.Testing.dll");
        if (entry == "Consumer" && action == "SelectAndCompose")
        {
            var current = Job(901, "Coverage / compose", 2, 65, null);
            data["observations"]!["apiJob"] = Clone(current);
            data["observations"]!["environment"]!["GITHUB_JOB"] = "coverage_compose";
            data["jobs"]!["jobs"] = new JsonArray(Job(101, "Coverage / prepare", 1, 1, 2), Job(201, "Coverage / shard 0", 1, 3, 4),
                Job(202, "Coverage / shard 1", 1, 5, 6), Job(203, "Coverage / shard 2", 1, 7, 8), current);
            data["jobs"]!["totalCount"] = 5;
        }
        JsonObject Artifact(int role, int? index, long id, long jobId)
        {
            var key = role == 0 ? "prepare" : "shard-" + index!.Value.ToString("D6", CultureInfo.InvariantCulture);
            var name = "coverage-" + key + "-" + Commit + "-" + RunId + "-1";
            var archivePath = Path.Combine(_root, key + ".zip");
            // These exact, inert bytes are deliberately not an archive. Their
            // real digest is pinned; rejection must occur in the existing byte
            // inspector after entry preflight, never through fake CI flags.
            File.WriteAllBytes(archivePath, [0, 255, 13, 10, 0, 128, 65]);
            var owner = (JsonObject)data["owner"]!.DeepClone();
            owner["role"] = role; owner["shardIndex"] = index; owner["jobId"] = jobId;
            owner["preparation"] = data["context"]!["preparation"]!.DeepClone();
            owner["originalRoot"] = Path.Combine(originalRoot, key, "attempt-1"); owner["artifactName"] = name;
            owner["archiveSha256"] = ShaFile(archivePath); owner["finishedUtc"] = Time(role == 0 ? 1.5 : 3.5 + index!.Value * 2);
            if (role == 0)
            {
                owner["preparationJobId"] = jobId; owner["evidenceSha256"] = prepared["preparedSha256"]!.DeepClone();
                owner["inventory"] = new JsonObject { ["directories"] = new JsonArray("coverage"), ["files"] = new JsonArray(new JsonObject
                    { ["path"] = "coverage/prepared.json", ["length"] = 31, ["sha256"] = prepared["preparedSha256"]!.DeepClone() }) };
            }
            var ownerPath = Path.Combine(_root, key + "-owner.json");
            File.WriteAllText(ownerPath, owner.ToJsonString(), new UTF8Encoding(false));
            return new JsonObject
            {
                ["ownerRecordPath"] = ownerPath, ["ownerRecordSha256"] = ShaFile(ownerPath),
                ["archivePath"] = archivePath, ["archiveSha256"] = ShaFile(archivePath), ["artifactId"] = id,
                ["artifactName"] = name, ["artifactRunId"] = RunId, ["artifactCommit"] = Commit,
            };
        }
        var request = new JsonObject
        {
            ["schemaVersion"] = 1, ["workDirectory"] = Path.Combine(_root, entry.ToLowerInvariant() + "-work"),
            ["diagnosticPath"] = Path.Combine(_root, entry.ToLowerInvariant() + "-diagnostic.json"),
            ["driver"] = new JsonObject { ["toolFile"] = _actualTool, ["toolSha256"] = ShaFile(_actualTool), ["dotnetFile"] = _dotnet },
        };
        if (entry == "Producer")
        {
            request["role"] = 1; request["shardIndex"] = 1;
            request["archivePath"] = Path.Combine(_root, "output-operation.zip");
            request["ownerRecordPath"] = Path.Combine(_root, "output-owner.json");
            request["preparationArtifact"] = Artifact(0, null, 800, 101); request["smokeArtifact"] = Artifact(1, 0, 801, 201);
        }
        else
        {
            request["resultPath"] = Path.Combine(_root, "consumer-result.json");
            if (action == "RestorePrerequisites")
            {
                request["shardIndex"] = 1; request["preparationArtifact"] = Artifact(0, null, 800, 101); request["smokeArtifact"] = Artifact(1, 0, 801, 201);
            }
            else
            {
                request["composeDirectory"] = Path.Combine(_root, "compose-output");
                var artifacts = new[] { Artifact(0, null, 800, 101), Artifact(1, 0, 801, 201), Artifact(1, 1, 802, 202), Artifact(1, 2, 803, 203) };
                request["artifacts"] = new JsonArray(artifacts.Cast<JsonNode>().ToArray());
                request["artifactSnapshot"] = new JsonObject
                {
                    ["repository"] = Repository, ["runId"] = RunId, ["totalCount"] = 4,
                    ["artifacts"] = new JsonArray(artifacts.Select(artifact => (JsonNode)new JsonObject
                    {
                        ["id"] = artifact["artifactId"]!.DeepClone(), ["name"] = artifact["artifactName"]!.DeepClone(), ["expired"] = false,
                        ["workflow_run"] = new JsonObject { ["id"] = RunId, ["head_sha"] = Commit },
                    }).ToArray()),
                };
            }
        }
        return request;
    }

    private async Task<(int ExitCode, string Error)> InvokeEntryAsync(string entry, string action, JsonObject data, JsonObject request)
    {
        var arguments = new List<string> { "-Action", action };
        foreach (var pair in new[] { ("Context", data["context"]!), ("Observations", data["observations"]!),
            ("Jobs", data["jobs"]!), ("Request", (JsonNode)request) })
        {
            var path = Path.Combine(_root, "entry-" + pair.Item1 + ".json");
            var bytes = Encoding.UTF8.GetBytes(pair.Item2.ToJsonString());
            File.WriteAllBytes(path, bytes);
            arguments.Add("-" + pair.Item1 + "Path"); arguments.Add(path);
            arguments.Add("-Expected" + pair.Item1 + "Sha256"); arguments.Add(Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        // Bind the exact raw jobs file, rather than the synthetic helper-only
        // fixture's digest label. Other independent identities remain unchanged.
        var jobsPath = Path.Combine(_root, "entry-Jobs.json");
        data["context"]!["jobsSha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(jobsPath)));
        var contextPath = Path.Combine(_root, "entry-Context.json");
        var contextBytes = Encoding.UTF8.GetBytes(data["context"]!.ToJsonString());
        File.WriteAllBytes(contextPath, contextBytes);
        arguments[arguments.IndexOf("-ExpectedContextSha256") + 1] = Convert.ToHexStringLower(SHA256.HashData(contextBytes));
        return await RunProcessAsync(Path.Combine(Path.GetDirectoryName(_helper)!, "FullCoverageCi." + entry + ".ps1"), arguments.ToArray());
    }

    private async Task<JsonNode> AdmitAsync(string mode, JsonObject data)
    {
        var result = await ContractAsync(mode, data);
        Assert.That(result["accepted"]!.GetValue<bool>(), Is.True, result["code"]?.ToString());
        return result["value"] ?? JsonValue.Create(true)!;
    }
    private async Task RejectAsync(string mode, JsonObject data, string reason)
    {
        var result = await ContractAsync(mode, data);
        Assert.That(result["accepted"]!.GetValue<bool>(), Is.False);
        Assert.That(result["code"]!.GetValue<string>(), Is.EqualTo("full-coverage-ci-orchestration-" + reason));
    }
    private async Task<JsonNode> ContractAsync(string mode, JsonObject data)
        => await ContractRawAsync(mode, data.ToJsonString());
    private async Task<JsonNode> ContractRawAsync(string mode, string rawJson)
    {
        var ordinal = ++_invocations;
        var input = Path.Combine(_root, "input-" + ordinal + ".json");
        var output = Path.Combine(_root, "output-" + ordinal + ".json");
        File.WriteAllText(input, rawJson, new UTF8Encoding(false));
        var execution = await RunProcessAsync(Path.Combine(_root, "contract.ps1"),
            ["-Helper", _helper, "-InputPath", input, "-OutputPath", output, "-Mode", mode]);
        Assert.That(execution.ExitCode, Is.Zero, execution.Error);
        return JsonNode.Parse(File.ReadAllText(output))!;
    }
    private async Task<(int ExitCode, string Error)> RunProcessAsync(string script, string[] arguments)
    {
        var remaining = TimeSpan.FromSeconds(120) - _clock.Elapsed;
        if (remaining <= TimeSpan.Zero) throw new TimeoutException("coverage-ci-contract-root-deadline");
        var start = new ProcessStartInfo(_shell)
        {
            WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", script }.Concat(arguments))
            start.ArgumentList.Add(argument);
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GITHUB_", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        using var process = new Process { StartInfo = start };
        Task<string>? stdout = null, stderr = null;
        Exception? primary = null;
        var started = false;
        try
        {
            started = process.Start();
            if (!started) throw new InvalidOperationException("coverage-ci-contract-process-start");
            stdout = process.StandardOutput.ReadToEndAsync(); stderr = process.StandardError.ReadToEndAsync();
            var completion = Task.WhenAll(process.WaitForExitAsync(), stdout, stderr);
            var warning = TimeSpan.FromSeconds(20) - _clock.Elapsed;
            if (warning <= TimeSpan.Zero || await Task.WhenAny(completion, Task.Delay(warning)) != completion)
                TestContext.Progress.WriteLine("coverage-ci-contract: same pure helper child still running after twenty seconds.");
            await completion.WaitAsync(TimeSpan.FromSeconds(120) - _clock.Elapsed);
            if (_clock.Elapsed >= TimeSpan.FromSeconds(120)) throw new TimeoutException("coverage-ci-contract-late-completion");
            return (process.ExitCode, await stderr);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            if (started && primary is not null)
            {
                // Pure helper fixture starts no descendants. Still terminate the
                // entire process tree and share one ten-second exit/drain budget.
                var drain = Stopwatch.StartNew();
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                try
                {
                    await Task.WhenAll(process.WaitForExitAsync(), stdout ?? Task.FromResult(""), stderr ?? Task.FromResult(""))
                        .WaitAsync(TimeSpan.FromSeconds(10) - drain.Elapsed);
                }
                catch { } // Preserve the original operation or deadline failure.
            }
        }
    }

    private static TestCaseData Row(string fault, string reason, string prefix) => new TestCaseData(fault, reason)
        .SetName("coverage-ci-orchestration/" + prefix + fault)
        .SetProperty("FishAudioCaseId", "coverage-ci-orchestration/" + prefix + fault);
    private static string FindShell() => FindExecutable("pwsh.exe");
    private static string FindExecutable(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        throw new InvalidOperationException("coverage-ci-contract-executable-missing");
    }
    private string RequireOwnedFixtureRoot()
    {
        if (!Path.IsPathFullyQualified(_root)) throw new InvalidOperationException("coverage-ci-contract-directory-absolute");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
        const string prefix = "fa-coverage-ci-contract-";
        var name = Path.GetFileName(full);
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (!full.Equals(Path.TrimEndingDirectorySeparator(_root), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(full), temp, StringComparison.OrdinalIgnoreCase) ||
            !name.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[prefix.Length..], "N", out var id) || id == Guid.Empty)
            throw new InvalidOperationException("coverage-ci-contract-directory-owner");
        for (DirectoryInfo? directory = new(full); directory is not null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidOperationException("coverage-ci-contract-parent-kind");
        return full;
    }
    private string RequireOwnedFixtureChild(string path, string expectedName)
    {
        var root = RequireOwnedFixtureRoot();
        if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException("coverage-ci-contract-child-absolute");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!full.Equals(Path.Combine(root, expectedName), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(full), root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("coverage-ci-contract-child-owner");
        return full;
    }
    private static void AssertRegularFixtureTree(string root)
    {
        for (DirectoryInfo? ancestor = new(root); ancestor is not null; ancestor = ancestor.Parent)
            if ((ancestor.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidOperationException("coverage-ci-contract-parent-kind");
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var currentDirectory))
            foreach (var path in Directory.EnumerateFileSystemEntries(currentDirectory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                    throw new InvalidOperationException("coverage-ci-contract-entry-kind");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
            }
    }
    private static string ShaFile(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string Time(double minute) => new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero)
        .AddMinutes(minute).ToString("O", CultureInfo.InvariantCulture);
    private static long Ticks(double minute) => DateTimeOffset.Parse(Time(minute), CultureInfo.InvariantCulture).UtcTicks;
    private static JsonObject Clone(JsonObject value) => (JsonObject)value.DeepClone();
    private static JsonObject Job(long id, string name, int attempt, int start, int? end) => new()
    {
        ["id"] = id, ["run_id"] = RunId, ["run_attempt"] = attempt, ["name"] = name, ["head_sha"] = Commit,
        ["status"] = end is null ? "in_progress" : "completed", ["conclusion"] = end is null ? null : "success",
        ["started_at"] = Time(start), ["completed_at"] = end is null ? null : Time(end.Value),
        ["runner_id"] = 44, ["runner_name"] = "fixture-runner",
        ["steps"] = new JsonArray(new JsonObject { ["number"] = 1, ["name"] = "Raw operation", ["status"] = "completed",
            ["conclusion"] = "success", ["started_at"] = Time(start), ["completed_at"] = Time(end ?? start) }),
    };
    private static void AddJob(JsonObject data, JsonNode job)
    {
        data["jobs"]!["jobs"]!.AsArray().Add(job);
        data["jobs"]!["totalCount"] = data["jobs"]!["jobs"]!.AsArray().Count;
    }
    private static JsonObject Create()
    {
        const string prepareRoot = @"C:\ci\coverage\prepare\attempt-1";
        var prepared = new JsonObject
        {
            ["schemaVersion"] = 1, ["preparedPath"] = prepareRoot + @"\coverage\prepared.json", ["preparedSha256"] = Sha("prepared"),
            ["sourceDirectory"] = prepareRoot + @"\source", ["snapshotContextPath"] = prepareRoot + @"\snapshot-context.json",
            ["metadataToolFile"] = prepareRoot + @"\source\tools\FishAudio.Testing.dll", ["dotnetFile"] = @"C:\dotnet\dotnet.exe",
            ["shellFile"] = @"C:\PowerShell\pwsh.exe", ["dotCoverToolFile"] = @"C:\dotcover\dotCover.dll",
        };
        var preparation = new JsonObject
        {
            ["source"] = new JsonObject { ["commit"] = Commit, ["tree"] = Tree, ["fingerprint"] = Sha("source") },
            ["prepared"] = prepared, ["runId"] = new string('a', 32), ["snapshotContextSha256"] = Sha("context"),
            ["metadataToolSha256"] = Sha("metadata-tool"), ["buildAuthoritySha256"] = Sha("authority"),
            ["dotCoverToolSha256"] = Sha("dotcover"), ["vstestConsoleSha256"] = Sha("vstest"), ["sdkVersion"] = "10.0.403",
            ["configuration"] = "Release", ["targetFramework"] = "net10.0-windows", ["instrumentationPolicyVersion"] = "framework-infrastructure-exclusions-v2",
        };
        var roles = new JsonArray();
        roles.Add(new JsonObject { ["role"] = 0, ["shardIndex"] = null, ["jobName"] = "Coverage / prepare",
            ["requiredSteps"] = new JsonArray("Raw operation"), ["evidenceRelativePath"] = "coverage/prepared.json" });
        for (var index = 0; index < 3; index++) roles.Add(new JsonObject { ["role"] = 1, ["shardIndex"] = index,
            ["jobName"] = "Coverage / shard " + index, ["requiredSteps"] = new JsonArray("Raw operation"), ["evidenceRelativePath"] = "shard.json" });
        var context = new JsonObject
        {
            ["schemaVersion"] = 1, ["repository"] = Repository, ["eventName"] = "push", ["ref"] = "refs/heads/main",
            ["commit"] = Commit, ["tree"] = Tree, ["workflowName"] = "Coverage", ["runId"] = RunId, ["currentAttempt"] = 2,
            ["operationRoot"] = @"C:\ci\coverage", ["jobsSha256"] = Sha("raw jobs"), ["preparation"] = preparation,
            ["shardCount"] = 3, ["maximumConcurrentMachines"] = 4, ["roles"] = roles,
        };
        var prepare = Job(101, "Coverage / prepare", 1, 1, 2);
        var smoke = Job(201, "Coverage / shard 0", 1, 3, 4);
        var current = Job(202, "Coverage / shard 1", 2, 65, null);
        var jobs = new JsonObject { ["repository"] = Repository, ["runId"] = RunId, ["filter"] = "all", ["totalCount"] = 3,
            ["jobs"] = new JsonArray(Clone(prepare), Clone(smoke), Clone(current)),
            ["attempts"] = new JsonArray(new JsonObject { ["runAttempt"] = 1, ["runStartedAt"] = Time(0) },
                new JsonObject { ["runAttempt"] = 2, ["runStartedAt"] = Time(60) }) };
        var observations = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["environment"] = new JsonObject { ["GITHUB_REPOSITORY"] = Repository, ["GITHUB_EVENT_NAME"] = "push",
                ["GITHUB_REF"] = "refs/heads/main", ["GITHUB_SHA"] = Commit, ["GITHUB_WORKFLOW"] = "Coverage",
                ["GITHUB_RUN_ID"] = RunId.ToString(CultureInfo.InvariantCulture), ["GITHUB_RUN_ATTEMPT"] = "2", ["GITHUB_JOB"] = "coverage_shard" },
            ["apiRun"] = new JsonObject { ["id"] = RunId, ["run_attempt"] = 2, ["event"] = "push", ["head_branch"] = "main",
                ["head_sha"] = Commit, ["name"] = "Coverage", ["repository"] = new JsonObject { ["full_name"] = Repository },
                ["head_repository"] = new JsonObject { ["full_name"] = Repository } },
            ["git"] = new JsonObject { ["commit"] = Commit, ["tree"] = Tree }, ["apiJob"] = Clone(current),
        };
        var inventory = new JsonObject { ["directories"] = new JsonArray(), ["files"] = new JsonArray(new JsonObject
            { ["path"] = "shard.json", ["length"] = 31, ["sha256"] = Sha("shard-0") }) };
        var artifactName = "coverage-shard-000000-" + Commit + "-" + RunId + "-1";
        var owner = new JsonObject
        {
            ["schemaVersion"] = 1, ["kind"] = "coverage-ci-operation-owner", ["role"] = 1, ["shardIndex"] = 0,
            ["shardCount"] = 3, ["repository"] = Repository, ["commit"] = Commit, ["tree"] = Tree, ["workflowName"] = "Coverage",
            ["workflowRunId"] = RunId, ["attempt"] = 1, ["jobId"] = 201, ["finishedUtc"] = Time(3.5),
            ["preparationJobId"] = 101, ["preparationAttempt"] = 1, ["preparation"] = Clone(preparation),
            ["originalRoot"] = @"C:\ci\coverage\shard-000000\attempt-1", ["artifactName"] = artifactName,
            ["archiveSha256"] = Sha("archive"), ["evidenceSha256"] = Sha("shard-0"), ["inventory"] = Clone(inventory),
        };
        var selectedPrepare = new JsonObject { ["job"] = Clone(prepare), ["actualAttempt"] = 1,
            ["execution"] = new JsonObject { ["started"] = Ticks(1), ["completed"] = Ticks(2) } };
        var selectedSmoke = new JsonObject { ["job"] = Clone(smoke), ["actualAttempt"] = 1,
            ["execution"] = new JsonObject { ["started"] = Ticks(3), ["completed"] = Ticks(4) } };
        var selectedShard = new JsonObject { ["job"] = Clone(current), ["actualAttempt"] = 2,
            ["execution"] = new JsonObject { ["started"] = Ticks(65), ["completed"] = Ticks(66) } };
        var artifacts = new JsonArray(new JsonObject { ["id"] = 801, ["name"] = artifactName, ["expired"] = false,
            ["workflow_run"] = new JsonObject { ["id"] = RunId, ["head_sha"] = Commit } },
            new JsonObject { ["id"] = 802, ["name"] = "coverage-diagnostics-smoke", ["expired"] = false });
        var shards = new JsonArray();
        for (var index = 0; index < 3; index++) shards.Add(new JsonObject { ["selected"] = new JsonObject
        {
            ["shardIndex"] = index, ["evidencePath"] = @"C:\ci\coverage\shard-" + index.ToString("D6") + @"\attempt-1\shard.json",
            ["evidenceSha256"] = Sha("shard-" + index),
        } });
        return new JsonObject
        {
            ["context"] = context, ["observations"] = observations, ["jobs"] = jobs, ["jobsSha256"] = Sha("raw jobs"), ["roleIndex"] = 1,
            ["owner"] = owner, ["inventory"] = inventory, ["archiveSha256"] = Sha("archive"), ["ownerSha256"] = Sha("owner"),
            ["prepare"] = selectedPrepare, ["smoke"] = selectedSmoke, ["shard"] = selectedShard,
            ["preparedValue"] = new JsonObject { ["wholePlan"] = new JsonObject { ["smokeCanonicalIds"] = new JsonArray("smoke/a", "smoke/b"),
                ["smokeOwnerIds"] = new JsonArray("owner-a", "owner-b") } },
            ["smokeValue"] = new JsonObject { ["shardIndex"] = 0, ["shardCount"] = 3, ["selectedCanonicalIds"] = new JsonArray("smoke/b", "smoke/a"),
                ["selectedOwnerIds"] = new JsonArray("owner-b", "owner-a") },
            ["artifactSnapshot"] = new JsonObject { ["repository"] = Repository, ["runId"] = RunId, ["totalCount"] = 2, ["artifacts"] = artifacts },
            ["downloads"] = new JsonArray(new JsonObject { ["artifactId"] = 801, ["artifactName"] = artifactName,
                ["artifactRunId"] = RunId, ["artifactCommit"] = Commit, ["ownerRecordSha256"] = Sha("owner"), ["archiveSha256"] = Sha("archive") }),
            ["shards"] = shards, ["category"] = "OperationFailed", ["stage"] = "raw-collection",
        };
    }

    private const string ContractScript = """
        param([string]$Helper, [string]$InputPath, [string]$OutputPath, [string]$Mode)
        . $Helper
        try {
            $data = CoverageCi-Read $InputPath (Get-HybridDigest $InputPath)
            $value = $null
            switch ($Mode) {
                'ReadValue' { $value = $data }
                'JsonBoundary' {
                    $value = @{
                        dateType = $data.date.GetType().FullName; dateValue = $data.date
                        emptyIsArray = ($data.empty -is [Array]); emptyCount = $data.empty.Count
                        singletonIsArray = ($data.singleton -is [Array]); singletonCount = $data.singleton.Count
                        singletonValue = $data.singleton[0]; manyCount = $data.many.Count
                        nullElementPreserved = ($null -eq $data.many[1]); integerType = $data.integer.GetType().FullName
                        integerValue = $data.integer; fractionType = $data.fraction.GetType().FullName; fractionValue = $data.fraction
                        integralDoubleType = $data.integralDouble.GetType().FullName
                        integralDoubleAcceptedAsId = CoverageCi-PositiveInteger $data.integralDouble
                        emptyStringLength = $data.emptyString.Length
                    }
                }
                'Caller' { $value = CoverageCi-AssertCaller $data.context $data.observations $data.jobs $data.jobsSha256 }
                'Latest' { $value = CoverageCi-Latest $data.context $data.jobs $data.context.roles[$data.roleIndex] -RequireSuccess }
                'Owner' {
                    $selected = CoverageCi-Latest $data.context $data.jobs $data.context.roles[1] -RequireSuccess
                    CoverageCi-AssertOwner $data.context $data.context.roles[1] $selected $data.owner $data.inventory $data.archiveSha256 $data.ownerSha256
                }
                'Chain' { CoverageCi-AssertChain $data.prepare $data.shard $data.owner $data.smoke }
                'PrepareOutcome' { CoverageCi-AssertPrepareOutcome $data.context $data.prepareOutcome }
                'Smoke' { CoverageCi-AssertSmoke $data.preparedValue $data.smokeValue $data.context.shardCount }
                'Downloads' { CoverageCi-AssertDownloads $data.context $data.artifactSnapshot $data.downloads }
                'Diagnostic' { $value = CoverageCi-Diagnostic $data.context 1 0 $data.category $data.stage }
                'Compose' {
                    $shards = [Collections.Generic.List[object]]::new()
                    foreach ($shard in $data.shards) { $shards.Add($shard) }
                    $value = CoverageCi-ComposeInvocation $data.context $shards 'C:\ci\compose'
                }
                'Launcher' { CoverageCi-ComposeLauncher $data.launcherPath }
                'ProducerRequest' { CoverageCi-AssertProducerRequest $data.context $data.request $data.action }
                'ConsumerRequest' { CoverageCi-AssertConsumerRequest $data.context $data.request $data.action }
                default { throw 'invalid-contract-mode' }
            }
            $result = @{ accepted = $true; value = $value; code = $null }
        } catch { $result = @{ accepted = $false; value = $null; code = $_.Exception.Message } }
        $primary = CoverageCi-Close $null
        if ($null -ne $primary) { throw $primary }
        [IO.File]::WriteAllText($OutputPath, ($result | ConvertTo-Json -Depth 64), [Text.UTF8Encoding]::new($false))
        """;
}
