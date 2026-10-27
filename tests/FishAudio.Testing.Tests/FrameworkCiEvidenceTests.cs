using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FishAudio.Testing.Evidence;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class FrameworkCiEvidenceTests
{
    // These fixtures admit CI evidence. They execute the real parser and helper,
    // but their two declared cases do not claim execution of product scenarios.
    private const string MergeCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string PullRequestHead = "dddddddddddddddddddddddddddddddddddddddd";
    private const string PullRequestBase = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
    private const string ForeignCommit = "ffffffffffffffffffffffffffffffffffffffff";
    private const string Tree = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Repository = "example/fish-audio";
    private const long RunId = 901;
    private const string ExecutionStep = "Run all discovered NUnit foundation cases in both assemblies";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly Stopwatch _clock = new();
    private string _root = "";
    private string _repositoryRoot = "";
    private string _actualTool = "";
    private string _bundle = "";
    private JsonObject _manifest = null!;
    private JsonObject _jobs = null!;
    private JsonObject _event = new();
    private string _eventName = "push";
    private string[] _parents = [];
    private int _attempt = 1;

    [SetUp]
    public void CreateOwnedEvidence()
    {
        _clock.Start();
        _repositoryRoot = FindRepositoryRoot();
        _actualTool = Path.Combine(_repositoryRoot, "tools/FishAudio.Testing/bin/Release/net10.0-windows/FishAudio.Testing.dll");
        Assert.That(File.Exists(_actualTool), Is.True, "The normal Release metadata tool must already be built.");
        Assert.That(File.Exists(Path.ChangeExtension(_actualTool, ".runtimeconfig.json")), Is.True);
        _root = Path.Combine(Path.GetTempPath(), "fa-framework-ci-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "repository/tools"));
        Directory.CreateDirectory(Path.Combine(_root, "artifacts"));
        _bundle = Bundle(1);
        Directory.CreateDirectory(_bundle);
        _manifest = Session("Full");
        var fresh = Session("Discover");
        foreach (var definition in new[]
        {
            new CaseDefinition("plugin", "ci.plugin/pass", "NUnitCompatibility.CompatibilityCases"),
            new CaseDefinition("testing-tool", "ci.tool/pass", "Example.ToolCases"),
        })
        {
            AddAssembly(definition, fresh);
        }
        WriteJson(Path.Combine(_root, "repository/discovery-template/manifest.json"), fresh);
        Write(Path.Combine(_root, "repository/tools/Test.ps1"), DiscoveryBoundary);
        Write(Path.Combine(_root, "invoke.ps1"), ProcessBoundary);
        Write(Path.Combine(_bundle, "runtime-evidence/fixture-created-1234.txt"), "constructed");
        WriteOwner(_bundle, 1);
        _jobs = new JsonObject
        {
            ["repository"] = Repository, ["runId"] = RunId, ["filter"] = "all", ["totalCount"] = 1,
            ["attempts"] = new JsonArray(Fence(1)), ["jobs"] = new JsonArray(Job(1)),
        };
    }

    [TearDown]
    public void DeleteOwnedEvidence()
    {
        var hasPrimaryFailure = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            var full = Path.GetFullPath(_root);
            if (!string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(full).StartsWith("fa-framework-ci-", StringComparison.Ordinal) ||
                !Guid.TryParseExact(Path.GetFileName(full)[16..], "N", out _))
                throw new InvalidOperationException("framework-ci-fixture-owner-changed");
            if (hasPrimaryFailure && Directory.Exists(full) && !RetainFailedEvidence(full)) return;
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
            Assert.That(_clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(120)), "framework-ci-fixture-root-deadline");
        }
        catch (Exception error) when (hasPrimaryFailure)
        {
            TestContext.Error.WriteLine("framework-ci-fixture: cleanup failed (" + error.GetType().Name + ").");
        }
    }

    private static bool RetainFailedEvidence(string source)
    {
        var configuredRoot = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
        if (string.IsNullOrEmpty(configuredRoot))
        {
            TestContext.Error.WriteLine("framework-ci-fixture: failed evidence retained in original owned temporary directory " + Path.GetFileName(source) + " (artifact root not configured).");
            return false;
        }
        try
        {
            if (!Path.IsPathFullyQualified(configuredRoot))
                throw new InvalidOperationException("framework-ci-fixture-retention-root-not-absolute");
            var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredRoot));
            if (!Directory.Exists(parent)) throw new InvalidOperationException("framework-ci-fixture-retention-root-missing");
            if (parent.Equals(source, StringComparison.OrdinalIgnoreCase) ||
                parent.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("framework-ci-fixture-retention-overlaps-source");
            RequireNoReparseAncestors(source);
            RequireNoReparseAncestors(parent);
            var destination = Path.Combine(parent, "framework-ci-failure-" + Guid.NewGuid().ToString("N"));
            if (!destination.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                File.Exists(destination) || Directory.Exists(destination))
                throw new InvalidOperationException("framework-ci-fixture-retention-destination-not-fresh");
            CopyEvidenceDirectory(source, destination);
            TestContext.Error.WriteLine("framework-ci-fixture: failed evidence retained as " + Path.GetFileName(destination) + ".");
            return true;
        }
        catch (Exception error)
        {
            // A failed or partial retention never authorizes deleting the source.
            TestContext.Error.WriteLine("framework-ci-fixture: retention failed (" + error.GetType().Name + "); original owned evidence retained as " + Path.GetFileName(source) + ".");
            return false;
        }
    }

    private static void RequireNoReparseAncestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("framework-ci-fixture-retention-reparse");
    }

    private static void CopyEvidenceDirectory(string source, string destination)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0 || File.Exists(destination) || Directory.Exists(destination))
            throw new InvalidOperationException("framework-ci-fixture-retention-not-fresh-or-reparse");
        Directory.CreateDirectory(destination);
        RequireNoReparseAncestors(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("framework-ci-fixture-retention-reparse");
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0) CopyEvidenceDirectory(entry, target);
            else File.Copy(entry, target, overwrite: false);
        }
    }

    [Test]
    public async Task CompleteFullAttemptReparsesBothRawCatalogsAndResults()
    {
        var result = await AdmitAsync();
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain("verified complete NUnit foundation attempt 1 (2 cases)."));
        Assert.That(Invocations(), Is.EqualTo(new[] { "discover", "evidence-catalog", "evidence-verify", "evidence-catalog", "evidence-verify" }));
        Assert.That(File.Exists(Path.Combine(_root, "verification/plugin-terminal.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(_root, "verification/testing-tool-terminal.json")), Is.True);
    }

    [Test]
    public async Task CachedAdvertisedLaterJobUsesItsUniqueEarlierExecutionAndOriginalArtifact()
    {
        AdvertiseCachedJob();
        var result = await AdmitAsync();
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain("verified complete NUnit foundation attempt 1 (2 cases)."));
        Assert.That(Directory.Exists(Bundle(2)), Is.False, "A cached sibling does not manufacture a new artifact owner.");
        Assert.That(Invocations().Count(item => item == "evidence-verify"), Is.EqualTo(2));
    }

    [Test]
    [Property("FishAudioCaseId", "framework-ci/pr-synthetic-merge-keeps-merge-artifact-owner")]
    public async Task PullRequestJobHeadCanDifferFromItsAuthenticatedMergeCheckout()
    {
        ConfigurePullRequest();

        var result = await AdmitAsync();

        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain("verified complete NUnit foundation attempt 1 (2 cases)."));
        Assert.That(Invocations(), Is.EqualTo(new[] { "discover", "evidence-catalog", "evidence-verify", "evidence-catalog", "evidence-verify" }));
        var owner = JsonNode.Parse(File.ReadAllText(Path.Combine(_bundle, "ci-owner.json")))!;
        Assert.That(owner["commit"]!.GetValue<string>(), Is.EqualTo(MergeCommit));
        Assert.That(owner["head"]!.GetValue<string>(), Is.EqualTo(MergeCommit));
        Assert.That(_manifest["source"]!["commit"]!.GetValue<string>(), Is.EqualTo(MergeCommit));
        Assert.That(_manifest["source"]!["sourceCommit"]!.GetValue<string>(), Is.EqualTo(MergeCommit));
    }

    [Test]
    [Property("FishAudioCaseId", "framework-ci/pr-cached-merge-keeps-original-artifact-owner")]
    public async Task PullRequestCachedJobRetainsItsOriginalMergeArtifactOwner()
    {
        ConfigurePullRequest();
        AdvertiseCachedJob();

        var result = await AdmitAsync();

        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain("verified complete NUnit foundation attempt 1 (2 cases)."));
        Assert.That(Directory.Exists(Bundle(2)), Is.False, "A cached PR job cannot manufacture a later merge artifact owner.");
        Assert.That(Invocations().Count(item => item == "evidence-verify"), Is.EqualTo(2));
    }

    [TestCase("job-head-is-merge", "framework-ci-execution-sha", TestName = "framework-ci/pr-job-head-cannot-be-merge-commit")]
    [TestCase("foreign-job-head", "framework-ci-execution-sha", TestName = "framework-ci/pr-foreign-job-head")]
    [TestCase("reversed-parents", "framework-ci-pull-request-parents", TestName = "framework-ci/pr-reversed-parents")]
    [TestCase("wrong-base-parent", "framework-ci-pull-request-parents", TestName = "framework-ci/pr-wrong-base-parent")]
    [TestCase("wrong-head-parent", "framework-ci-pull-request-parents", TestName = "framework-ci/pr-wrong-head-parent")]
    [TestCase("missing-parent", "framework-ci-pull-request-parents", TestName = "framework-ci/pr-missing-parent")]
    [TestCase("extra-parent", "framework-ci-pull-request-parents", TestName = "framework-ci/pr-extra-parent")]
    [TestCase("owner-is-head", "framework-ci-artifact-owner", TestName = "framework-ci/pr-artifact-owner-cannot-be-head")]
    [TestCase("manifest-is-head", "framework-ci-session-identity", TestName = "framework-ci/pr-manifest-source-cannot-be-head")]
    [TestCase("invalid-event-head", "framework-ci-pull-request-context", TestName = "framework-ci/pr-invalid-event-head")]
    [TestCase("invalid-event-base", "framework-ci-pull-request-context", TestName = "framework-ci/pr-invalid-event-base")]
    [TestCase("missing-pull-request", "framework-ci-pull-request-context", TestName = "framework-ci/pr-missing-event-pull-request")]
    [TestCase("foreign-event-repository", "framework-ci-pull-request-context", TestName = "framework-ci/pr-foreign-event-repository")]
    [TestCase("foreign-base-repository", "framework-ci-pull-request-context", TestName = "framework-ci/pr-foreign-base-repository")]
    public async Task PullRequestEvidenceCannotRelaxCheckoutOrArtifactIdentity(string fault, string expectedReason)
    {
        ConfigurePullRequest();
        switch (fault)
        {
            case "job-head-is-merge":
                _jobs["jobs"]![0]!["head_sha"] = MergeCommit;
                break;
            case "foreign-job-head":
                _jobs["jobs"]![0]!["head_sha"] = ForeignCommit;
                break;
            case "reversed-parents":
                _parents = [PullRequestHead, PullRequestBase];
                break;
            case "wrong-base-parent":
                _parents = [ForeignCommit, PullRequestHead];
                break;
            case "wrong-head-parent":
                _parents = [PullRequestBase, ForeignCommit];
                break;
            case "missing-parent":
                _parents = [PullRequestBase];
                break;
            case "extra-parent":
                _parents = [PullRequestBase, PullRequestHead, ForeignCommit];
                break;
            case "owner-is-head":
                var ownerPath = Path.Combine(_bundle, "ci-owner.json");
                var owner = JsonNode.Parse(File.ReadAllText(ownerPath))!;
                owner["commit"] = PullRequestHead;
                owner["head"] = PullRequestHead;
                WriteJson(ownerPath, owner);
                break;
            case "manifest-is-head":
                _manifest["source"]!["commit"] = PullRequestHead;
                _manifest["source"]!["sourceCommit"] = PullRequestHead;
                break;
            case "invalid-event-head":
                _event["pull_request"]!["head"]!["sha"] = "not-a-commit";
                break;
            case "invalid-event-base":
                _event["pull_request"]!["base"]!["sha"] = "not-a-commit";
                break;
            case "missing-pull-request":
                _event.Remove("pull_request");
                break;
            case "foreign-event-repository":
                _event["repository"]!["full_name"] = "other/fish-audio";
                break;
            case "foreign-base-repository":
                _event["pull_request"]!["base"]!["repo"]!["full_name"] = "other/fish-audio";
                break;
            default:
                throw new InvalidOperationException("framework-ci-fixture-unknown-pr-fault");
        }

        var result = await AdmitAsync();

        Assert.That(result.ExitCode, Is.EqualTo(1), result.Output + result.Error);
        Assert.That(result.Error, Does.Contain("framework-ci: rejected (" + expectedReason + ";"));
        Assert.That(result.Output, Does.Not.Contain("verified complete NUnit foundation"));
        Assert.That(Directory.Exists(Path.Combine(_root, "verification")), Is.False,
            "Invalid PR identity must fail before fresh discovery or parser admission.");
    }

    private void ConfigurePullRequest()
    {
        _eventName = "pull_request";
        _event = new JsonObject
        {
            ["repository"] = new JsonObject { ["full_name"] = Repository },
            ["pull_request"] = new JsonObject
            {
                ["head"] = new JsonObject { ["sha"] = PullRequestHead },
                ["base"] = new JsonObject
                {
                    ["sha"] = PullRequestBase,
                    ["repo"] = new JsonObject { ["full_name"] = Repository },
                },
            },
        };
        _parents = [PullRequestBase, PullRequestHead];
        _jobs["jobs"]![0]!["head_sha"] = PullRequestHead;
    }

    [TestCase("changed-cached-witness", "framework-ci-unmatched-or-ambiguous-cached-job", TestName = "framework-ci/changed-cached-witness")]
    [TestCase("changed-cached-step", "framework-ci-unmatched-or-ambiguous-cached-job", TestName = "framework-ci/changed-cached-step-timing")]
    [TestCase("foreign-job-head", "framework-ci-execution-sha", TestName = "framework-ci/foreign-job-head")]
    [TestCase("failed-latest", "framework-ci-unsuccessful-latest-job", TestName = "framework-ci/failed-latest-cannot-borrow-old-green")]
    [TestCase("failed-latest-without-artifact", "framework-ci-missing-latest-evidence", TestName = "framework-ci/failed-latest-without-artifact")]
    [TestCase("unfinished-latest", "framework-ci-unsuccessful-latest-job", TestName = "framework-ci/unfinished-latest-cannot-borrow-old-green")]
    [TestCase("missing-latest-artifact", "framework-ci-missing-latest-evidence", TestName = "framework-ci/missing-latest-artifact")]
    [TestCase("artifact-without-execution", "framework-ci-missing-latest-evidence", TestName = "framework-ci/artifact-without-execution")]
    [TestCase("malformed-raw-result", "framework-ci-parser-rejected", TestName = "framework-ci/malformed-raw-result-with-matching-digest")]
    [TestCase("wrong-catalog", "framework-ci-current-catalog-mismatch", TestName = "framework-ci/catalog-metadata-drift")]
    [TestCase("not-run", "framework-ci-parser-rejected", TestName = "framework-ci/raw-not-run-cannot-borrow-terminal-success")]
    [TestCase("skipped-execution-step", "framework-ci-missing-execution-step", TestName = "framework-ci/successful-job-with-skipped-execution-step")]
    public async Task IncompleteOrInconsistentEvidenceCannotPass(string fault, string expectedReason)
    {
        switch (fault)
        {
            case "changed-cached-witness":
                AdvertiseCachedJob();
                _jobs["jobs"]![1]!["runner_name"] = "different-hosted-runner";
                break;
            case "changed-cached-step":
                AdvertiseCachedJob();
                _jobs["jobs"]![1]!["steps"]![1]!["completed_at"] = "2026-10-20T01:04:01Z";
                break;
            case "foreign-job-head":
                _jobs["jobs"]![0]!["head_sha"] = "dddddddddddddddddddddddddddddddddddddddd";
                break;
            case "failed-latest":
                AddActualAttempt("completed", "failure", artifact: true);
                break;
            case "failed-latest-without-artifact":
                AddActualAttempt("completed", "failure", artifact: false);
                break;
            case "unfinished-latest":
                AddActualAttempt("in_progress", null, artifact: true);
                _jobs["jobs"]![1]!["completed_at"] = null;
                break;
            case "missing-latest-artifact":
                AddActualAttempt("completed", "success", artifact: false);
                break;
            case "artifact-without-execution":
                AdvertiseCachedJob();
                Directory.CreateDirectory(Bundle(2));
                break;
            case "malformed-raw-result":
                ReplaceArtifact("resultXml", "<test-run result=\"Passed\"");
                break;
            case "wrong-catalog":
                var catalogPath = Path.Combine(_bundle, "plugin/catalog.json");
                var catalog = JsonNode.Parse(File.ReadAllText(catalogPath))!;
                catalog["cases"]![0]!["methodName"] = "DifferentSourceMethod";
                ReplaceArtifact("catalog", catalog.ToJsonString(Json));
                break;
            case "not-run":
                var resultPath = Path.Combine(_bundle, "plugin/result.xml");
                var xml = File.ReadAllText(resultPath).Replace("result=\"Passed\" duration=\"0.125\"", "result=\"Skipped\" duration=\"0.125\"", StringComparison.Ordinal);
                ReplaceArtifact("resultXml", xml);
                break;
            case "skipped-execution-step":
                _jobs["jobs"]![0]!["steps"]![1]!["conclusion"] = "skipped";
                break;
            default:
                throw new InvalidOperationException("framework-ci-fixture-unknown-fault");
        }

        var result = await AdmitAsync();
        Assert.That(result.ExitCode, Is.EqualTo(1), result.Output + result.Error);
        Assert.That(result.Error, Does.Contain("framework-ci: rejected (" + expectedReason + ";"));
        Assert.That(result.Output, Does.Not.Contain("verified complete NUnit foundation"));
        if (fault is "malformed-raw-result" or "not-run")
        {
            Assert.That(Invocations(), Is.EqualTo(new[] { "discover", "evidence-catalog", "evidence-verify" }));
            Assert.That(File.Exists(Path.Combine(_root, "verification/plugin-terminal.json")), Is.False);
        }
    }

    private void AddAssembly(CaseDefinition definition, JsonObject fresh)
    {
        var key = definition.Key;
        var relativeDirectory = "phases/discover-" + key;
        Write(Path.Combine(_bundle, relativeDirectory, "discovery.dump"), Discovery(definition));
        WriteJson(Path.Combine(_bundle, key, "catalog.json"), Catalog(definition, "captured-1"));
        WriteJson(Path.Combine(_bundle, key, "selection.json"), new JsonObject { ["schemaVersion"] = 1, ["ids"] = new JsonArray(definition.Id) });
        WriteJson(Path.Combine(_bundle, key, "terminal.json"), new JsonObject
        {
            ["schemaVersion"] = 1, ["kind"] = "nunit-terminal", ["complete"] = true, ["passedCount"] = 1,
            ["cases"] = new JsonArray(new JsonObject { ["canonicalId"] = definition.Id, ["outcome"] = "passed" }),
        });
        Write(Path.Combine(_bundle, key, "result.xml"), Terminal(definition));
        Write(Path.Combine(_bundle, key, "result.trx"), Trx(definition));
        var assembly = new JsonObject
        {
            ["key"] = key, ["project"] = "tests/" + key + "/" + key + ".csproj",
            ["assembly"] = "tests/" + key + "/bin/Release/net10.0-windows/" + key + ".dll",
            ["sha256"] = Hash(typeof(FrameworkCiEvidenceTests).Assembly.Location),
            ["discoveredCount"] = 1, ["selectedCount"] = 1, ["passedCount"] = 1, ["executed"] = true,
            ["selectedIds"] = new JsonArray(definition.Id), ["markerRequired"] = key == "plugin", ["markerObserved"] = key == "plugin",
        };
        foreach (var item in new[] { ("catalog", "catalog.json"), ("selection", "selection.json"), ("terminal", "terminal.json"), ("resultXml", "result.xml"), ("trx", "result.trx") })
        {
            assembly[item.Item1] = key + "/" + item.Item2;
            assembly[item.Item1 + "Sha256"] = Hash(Path.Combine(_bundle, key, item.Item2));
        }
        _manifest["assemblies"]!.AsArray().Add(assembly);
        foreach (var phase in new[] { "build", "discover", "catalog", "execute", "verify" })
        {
            _manifest["phases"]!.AsArray().Add(new JsonObject
            {
                ["name"] = phase + "-" + key, ["complete"] = true, ["exitCode"] = 0,
                ["directory"] = "phases/" + phase + "-" + key,
            });
        }
        var current = (JsonObject)assembly.DeepClone();
        current["executed"] = false;
        current["passedCount"] = 0;
        fresh["assemblies"]!.AsArray().Add(current);
        // Cross-process adapter IDs deliberately differ from both raw discovery
        // and the captured catalog; only canonical and exact named identity join.
        WriteJson(Path.Combine(_root, "repository/discovery-template", key, "catalog.json"), Catalog(definition, "fresh-99"));
    }

    private static JsonObject Session(string mode) => new()
    {
        ["schemaVersion"] = 1, ["kind"] = "unified-test-session", ["complete"] = true, ["mode"] = mode,
        ["platform"] = "VSTest", ["configuration"] = "Release", ["selection"] = "all-discovered", ["baselineClaim"] = false,
        ["source"] = new JsonObject
        {
            ["snapshot"] = false, ["dirty"] = false, ["tree"] = null, ["commit"] = MergeCommit, ["sourceCommit"] = MergeCommit,
            ["fingerprint"] = new string('c', 64), ["fingerprintKind"] = "sha256-source-path-and-bytes-v1",
        },
        ["toolSha256"] = Hash(typeof(BinaryIdentity).Assembly.Location),
        ["finishedUtc"] = "2026-10-20T01:05:00Z", ["assemblies"] = new JsonArray(), ["phases"] = new JsonArray(),
    };

    private static JsonObject Catalog(CaseDefinition definition, string platformId) => new()
    {
        ["schemaVersion"] = 1, ["kind"] = "nunit-catalog", ["complete"] = true,
        ["cases"] = new JsonArray(new JsonObject
        {
            ["canonicalId"] = definition.Id, ["fullName"] = definition.ClassName + ".Probe", ["platformId"] = platformId,
            ["className"] = definition.ClassName, ["methodName"] = "Probe",
        }),
    };

    private static JsonObject Fence(int attempt) => new()
    {
        ["runAttempt"] = attempt, ["runStartedAt"] = $"2026-10-20T0{attempt}:00:00Z",
    };

    private static JsonObject Job(int attempt) => new()
    {
        ["id"] = 100 + attempt, ["run_id"] = RunId, ["run_attempt"] = attempt, ["name"] = "NUnit foundation",
        ["head_sha"] = MergeCommit, ["status"] = "completed", ["conclusion"] = "success", ["runner_id"] = 17,
        ["runner_name"] = "hosted-fixture-01", ["started_at"] = $"2026-10-20T0{attempt}:01:00Z", ["completed_at"] = $"2026-10-20T0{attempt}:10:00Z",
        ["steps"] = new JsonArray(
            Step("Set up job", 1, attempt, "01:00", "01:10"),
            Step(ExecutionStep, 2, attempt, "02:00", "04:00"),
            Step("Preserve framework evidence", 3, attempt, "06:00", "07:00")),
    };

    private static JsonObject Step(string name, int number, int attempt, string start, string end) => new()
    {
        ["name"] = name, ["number"] = number, ["status"] = "completed", ["conclusion"] = "success",
        ["started_at"] = $"2026-10-20T0{attempt}:{start}Z", ["completed_at"] = $"2026-10-20T0{attempt}:{end}Z",
    };

    private void AdvertiseCachedJob()
    {
        _attempt = 2;
        _jobs["attempts"]!.AsArray().Add(Fence(2));
        var cached = (JsonObject)_jobs["jobs"]![0]!.DeepClone();
        cached["id"] = 102;
        cached["run_attempt"] = 2;
        _jobs["jobs"]!.AsArray().Add(cached);
        _jobs["totalCount"] = 2;
    }

    private void AddActualAttempt(string status, string? conclusion, bool artifact)
    {
        _attempt = 2;
        _jobs["attempts"]!.AsArray().Add(Fence(2));
        var job = Job(2);
        job["status"] = status;
        job["conclusion"] = conclusion;
        _jobs["jobs"]!.AsArray().Add(job);
        _jobs["totalCount"] = 2;
        if (artifact)
        {
            // An empty newest bundle suffices: job admission must reject before
            // parsing content, even while a complete older success remains.
            Directory.CreateDirectory(Bundle(2));
        }
    }

    private void ReplaceArtifact(string name, string contents)
    {
        var assembly = _manifest["assemblies"]![0]!;
        var path = Path.Combine(_bundle, assembly[name]!.GetValue<string>());
        Write(path, contents);
        assembly[name + "Sha256"] = Hash(path);
    }

    private async Task<ProcessResult> AdmitAsync()
    {
        WriteJson(Path.Combine(_bundle, "manifest.json"), _manifest);
        WriteJson(Path.Combine(_root, "jobs.json"), _jobs);
        var eventPath = Path.Combine(_root, "event.json");
        var gitSnapshotPath = Path.Combine(_root, "git-snapshot.json");
        WriteJson(eventPath, _event);
        WriteJson(gitSnapshotPath, new JsonObject
        {
            ["head"] = MergeCommit, ["tree"] = Tree,
            ["parents"] = new JsonArray(_parents.Select(parent => (JsonNode?)JsonValue.Create(parent)).ToArray()),
        });
        var start = new ProcessStartInfo(FindExecutable("pwsh.exe"))
        {
            WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-File", Path.Combine(_root, "invoke.ps1"),
            "-Root", Path.Combine(_root, "repository"), "-Helper", Path.Combine(_repositoryRoot, "tools/VerifyFrameworkCi.ps1"),
            "-Artifacts", Path.Combine(_root, "artifacts"), "-Jobs", Path.Combine(_root, "jobs.json"),
            "-Results", Path.Combine(_root, "verification"), "-ActualDotnet", FindExecutable("dotnet.exe"), "-ActualTool", _actualTool,
            "-GitSnapshot", gitSnapshotPath,
        }) start.ArgumentList.Add(argument);
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase) ||
                     key.StartsWith("GITHUB_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("FISHAUDIO_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        start.Environment["GITHUB_SHA"] = MergeCommit;
        start.Environment["GITHUB_EVENT_NAME"] = _eventName;
        start.Environment["GITHUB_EVENT_PATH"] = eventPath;
        start.Environment["GITHUB_REPOSITORY"] = Repository;
        start.Environment["GITHUB_RUN_ID"] = RunId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["GITHUB_RUN_ATTEMPT"] = _attempt.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return await RunOwnedProcessAsync(start);
    }

    private async Task<ProcessResult> RunOwnedProcessAsync(ProcessStartInfo start)
    {
        _ = RemainingRoot();
        using var process = new Process { StartInfo = start };
        Task<string>? stdout = null;
        Task<string>? stderr = null;
        Exception? primary = null;
        var started = false;
        try
        {
            started = process.Start();
            if (!started) throw new InvalidOperationException("framework-ci-fixture-process-start");
            stdout = process.StandardOutput.ReadToEndAsync();
            stderr = process.StandardError.ReadToEndAsync();
            var completion = Task.WhenAll(process.WaitForExitAsync(), stdout, stderr);
            var warning = TimeSpan.FromSeconds(20) - _clock.Elapsed;
            if (warning <= TimeSpan.Zero || await Task.WhenAny(completion, Task.Delay(warning)) != completion)
                TestContext.Progress.WriteLine("framework-ci-fixture: same child still running after twenty seconds.");
            await completion.WaitAsync(RemainingRoot());
            _ = RemainingRoot();
            CaptureProcessEvidence(process, stdout, stderr, primary: null);
            return new(process.ExitCode, await stdout, await stderr);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            if (primary is not null && started)
            {
                var drain = Stopwatch.StartNew();
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(RemainingDrain(drain));
                    if (stdout is not null && stderr is not null)
                        await Task.WhenAll(stdout, stderr).WaitAsync(RemainingDrain(drain));
                    _ = RemainingDrain(drain);
                    CaptureProcessEvidence(process, stdout, stderr, primary);
                    _ = RemainingDrain(drain);
                }
                catch (Exception cleanup)
                {
                    TestContext.Error.WriteLine("framework-ci-fixture: bounded process cleanup failed (" + cleanup.GetType().Name + ").");
                }
            }
        }
    }

    private void CaptureProcessEvidence(Process process, Task<string>? stdout, Task<string>? stderr, Exception? primary)
    {
        if (stdout?.IsCompletedSuccessfully == true) WriteNewEvidence("process.stdout.log", stdout.Result);
        if (stderr?.IsCompletedSuccessfully == true) WriteNewEvidence("process.stderr.log", stderr.Result);
        WriteNewEvidence("process.json", new JsonObject
        {
            ["exited"] = process.HasExited, ["exitCode"] = process.HasExited ? JsonValue.Create(process.ExitCode) : null,
            ["stdoutComplete"] = stdout?.IsCompletedSuccessfully == true, ["stderrComplete"] = stderr?.IsCompletedSuccessfully == true,
            ["failureType"] = primary?.GetType().Name,
        }.ToJsonString(Json));
    }

    private void WriteNewEvidence(string name, string value)
    {
        var path = Path.Combine(_root, name);
        if (File.Exists(path)) return; // Retain an already captured diagnostic verbatim.
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var bytes = Encoding.UTF8.GetBytes(value);
        output.Write(bytes);
    }

    private TimeSpan RemainingRoot()
    {
        var remaining = TimeSpan.FromSeconds(120) - _clock.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException("framework-ci-fixture-root-deadline");
    }

    private static TimeSpan RemainingDrain(Stopwatch clock)
    {
        var remaining = TimeSpan.FromSeconds(10) - clock.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException("framework-ci-fixture-drain-deadline");
    }

    private string[] Invocations() => File.ReadAllLines(Path.Combine(_root, "repository/invocations.txt"));
    private string Bundle(int attempt) => Path.Combine(_root, "artifacts", $"framework-report-{MergeCommit}-{RunId}-{attempt}");

    private static void WriteOwner(string bundle, int attempt) => WriteJson(Path.Combine(bundle, "ci-owner.json"), new JsonObject
    {
        ["schemaVersion"] = 1, ["repository"] = Repository, ["commit"] = MergeCommit, ["head"] = MergeCommit, ["tree"] = Tree,
        ["workflowRunId"] = RunId.ToString(System.Globalization.CultureInfo.InvariantCulture), ["attempt"] = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["job"] = "nunit-foundation", ["sdk"] = "10.0.403",
    });

    private static void WriteJson(string path, JsonNode value) => Write(path, value.ToJsonString(Json));

    private static void Write(string path, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }

    private static string FindRepositoryRoot()
    {
        if (Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT") is { Length: > 0 } contextPath)
        {
            using var context = JsonDocument.Parse(File.ReadAllText(contextPath));
            var snapshot = context.RootElement.GetProperty("root").GetString()!;
            if (!Path.IsPathFullyQualified(snapshot) || !File.Exists(Path.Combine(snapshot, "tools/VerifyFrameworkCi.ps1")))
                throw new InvalidOperationException("framework-ci-fixture-snapshot-helper-missing");
            return snapshot;
        }
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "tools/VerifyFrameworkCi.ps1"))) return directory.FullName;
        throw new InvalidOperationException("framework-ci-fixture-helper-missing");
    }

    private static string FindExecutable(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim('"'), name);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        throw new InvalidOperationException("framework-ci-fixture-executable-missing");
    }

    private sealed record CaseDefinition(string Key, string Id, string ClassName);
    private sealed record ProcessResult(int ExitCode, string Output, string Error);

    private static string Discovery(CaseDefinition definition) => $$"""
        <NUnitXml><test-run id="0" name="example.dll" runstate="Runnable" testcasecount="1">
          <test-suite type="Assembly" id="0-10" name="example.dll" runstate="Runnable" testcasecount="1">
            <test-case id="0-1" name="Probe" fullname="{{definition.ClassName}}.Probe" methodname="Probe" classname="{{definition.ClassName}}" runstate="Runnable">
              <properties><property name="FishAudioCaseId" value="{{definition.Id}}" /></properties>
            </test-case>
          </test-suite>
        </test-run></NUnitXml>
        """;

    private static string Terminal(CaseDefinition definition) => $$"""
        <test-run id="7" name="example.dll" result="Passed" total="1" passed="1" failed="0" warnings="0" inconclusive="0" skipped="0">
          <test-suite type="Assembly" id="7-10" name="example.dll" result="Passed" total="1" passed="1" failed="0" warnings="0" inconclusive="0" skipped="0">
            <test-case id="7-1" name="Probe" fullname="{{definition.ClassName}}.Probe" methodname="Probe" classname="{{definition.ClassName}}" result="Passed" duration="0.125">
              <properties><property name="FishAudioCaseId" value="{{definition.Id}}" /></properties>
            </test-case>
          </test-suite>
        </test-run>
        """;

    private static string Trx(CaseDefinition definition) => $$"""
        <TestRun id="11111111-1111-1111-1111-111111111111" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results><UnitTestResult executionId="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" testId="11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa" testName="Probe" duration="00:00:00.1250000" outcome="Passed" /></Results>
          <TestDefinitions><UnitTest name="Probe" id="11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa">
            <Execution id="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" />
            <TestMethod adapterTypeName="executor://nunit3testexecutor/" className="{{definition.ClassName}}" name="Probe" />
          </UnitTest></TestDefinitions>
          <ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" /></ResultSummary>
        </TestRun>
        """;

    private const string DiscoveryBoundary = """
        param([string]$Mode, [string]$ResultsDirectory, [string]$RepositoryRoot)
        $ErrorActionPreference = 'Stop'
        if ($Mode -cne 'Discover' -or [IO.Directory]::Exists($ResultsDirectory)) { throw 'fixture-discovery-contract' }
        [IO.File]::AppendAllText((Join-Path $RepositoryRoot 'invocations.txt'), "discover`n")
        [IO.Directory]::CreateDirectory($ResultsDirectory) | Out-Null
        Copy-Item -Path (Join-Path $RepositoryRoot 'discovery-template/*') -Destination $ResultsDirectory -Recurse
        $global:LASTEXITCODE = 0
        """;

    private const string ProcessBoundary = """
        param([string]$Root, [string]$Helper, [string]$Artifacts, [string]$Jobs, [string]$Results, [string]$ActualDotnet, [string]$ActualTool, [string]$GitSnapshot)
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        $PSNativeCommandUseErrorActionPreference = $false
        $global:fixtureRoot = $Root
        $global:fixtureDotnet = $ActualDotnet
        $global:fixtureTool = $ActualTool
        $global:fixtureGit = [IO.File]::ReadAllText($GitSnapshot) | ConvertFrom-Json
        function global:git {
            if ($args.Count -lt 4 -or $args[0] -cne '-C' -or $args[1] -cne $global:fixtureRoot) { throw 'fixture-unexpected-git' }
            $global:LASTEXITCODE = 0
            if ($args[2] -ceq 'rev-parse') {
                if ($args.Count -eq 5 -and $args[3] -ceq '--verify' -and $args[4] -ceq 'HEAD') { return $global:fixtureGit.head }
                if ($args.Count -eq 4 -and $args[3] -ceq 'HEAD^{tree}') { return $global:fixtureGit.tree }
            }
            if ($args.Count -eq 7 -and $args[2] -ceq 'rev-list' -and $args[3] -ceq '--parents' -and
                $args[4] -ceq '-n' -and $args[5] -ceq '1' -and $args[6] -ceq 'HEAD') {
                return (@($global:fixtureGit.head) + @($global:fixtureGit.parents)) -join ' '
            }
            throw 'fixture-unexpected-git'
        }
        function global:dotnet {
            if ($args.Count -eq 1 -and $args[0] -ceq '--version') { $global:LASTEXITCODE = 0; return '10.0.403' }
            $expected = Join-Path $global:fixtureRoot 'tools/FishAudio.Testing/bin/Release/net10.0-windows/FishAudio.Testing.dll'
            if ($args.Count -lt 2 -or $args[0] -cne $expected -or $args[1] -cnotin @('evidence-catalog', 'evidence-verify')) { throw 'fixture-unexpected-dotnet' }
            [IO.File]::AppendAllText((Join-Path $global:fixtureRoot 'invocations.txt'), ([string]$args[1] + "`n"))
            $forward = @($args[1..($args.Count - 1)])
            & $global:fixtureDotnet $global:fixtureTool @forward
            $global:LASTEXITCODE = $LASTEXITCODE
        }
        & $Helper -ArtifactDirectory $Artifacts -JobAttemptsPath $Jobs -ResultsDirectory $Results -RepositoryRoot $Root
        exit $LASTEXITCODE
        """;
}
