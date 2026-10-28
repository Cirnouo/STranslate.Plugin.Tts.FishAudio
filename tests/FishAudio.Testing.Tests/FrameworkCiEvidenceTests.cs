using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FishAudio.Testing.Evidence;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    private static readonly Lazy<byte[]> LegacyMetadataImage = new(CompileLegacyMetadataImage);
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
        File.Copy(Path.Combine(_repositoryRoot, "tools/Assert-FrameworkSupervisor.ps1"),
            Path.Combine(_root, "repository/tools/Assert-FrameworkSupervisor.ps1"), overwrite: false);
        File.Copy(Path.Combine(_repositoryRoot, "tools/Assert-FrameworkExecutionPlan.ps1"),
            Path.Combine(_root, "repository/tools/Assert-FrameworkExecutionPlan.ps1"), overwrite: false);
        PrepareLegacyMetadataFixture();
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
        Assert.That(Invocations(), Is.EqualTo(new[] { "discover", "evidence-catalog", "evidence-execution-plan", "evidence-execution-plan", "evidence-verify",
            "evidence-catalog", "evidence-execution-plan", "evidence-execution-plan", "evidence-verify", "evidence-migration" }));
        Assert.That(File.Exists(Path.Combine(_root, "verification/plugin-phase-0001-terminal.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(_root, "verification/testing-tool-phase-0001-terminal.json")), Is.True);
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
    [Property("FishAudioCaseId", "framework-ci/flat-single-artifact-keeps-owner-and-results")]
    public async Task SingleDownloadedArtifactCanBeAdmittedFromItsFlatRoot()
    {
        FlattenSingleBundle();
        var resultHash = Hash(Path.Combine(_bundle, FirstExecutionPhase["resultXml"]!.GetValue<string>()));

        var result = await AdmitAsync();

        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain("verified complete NUnit foundation attempt 1 (2 cases)."));
        Assert.That(Invocations(), Is.EqualTo(new[] { "discover", "evidence-catalog", "evidence-execution-plan", "evidence-execution-plan", "evidence-verify",
            "evidence-catalog", "evidence-execution-plan", "evidence-execution-plan", "evidence-verify", "evidence-migration" }));
        Assert.That(Directory.Exists(Bundle(1)), Is.False, "Admission must read the flat artifact without manufacturing a wrapper.");
        Assert.That(Hash(Path.Combine(_bundle, FirstExecutionPhase["resultXml"]!.GetValue<string>())), Is.EqualTo(resultHash));
        Assert.That(File.Exists(Path.Combine(_root, "verification/plugin-phase-0001-terminal.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(_root, "verification/testing-tool-phase-0001-terminal.json")), Is.True);
    }

    [Test]
    [Property("FishAudioCaseId", "framework-ci/flat-cached-artifact-keeps-original-attempt")]
    public async Task SingleFlatArtifactRetainsItsNumericOriginalCachedAttempt()
    {
        AdvertiseCachedJob();
        FlattenSingleBundle();
        var ownerPath = Path.Combine(_bundle, "ci-owner.json");
        var owner = JsonNode.Parse(File.ReadAllText(ownerPath))!;
        owner["attempt"] = 1;
        WriteJson(ownerPath, owner);

        var result = await AdmitAsync();

        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain("verified complete NUnit foundation attempt 1 (2 cases)."));
        Assert.That(Directory.Exists(Bundle(1)), Is.False);
        Assert.That(Directory.Exists(Bundle(2)), Is.False, "A cached job retains the flat original owner rather than creating attempt 2 evidence.");
        Assert.That(Invocations().Count(item => item == "evidence-verify"), Is.EqualTo(2));
        Assert.That(JsonNode.Parse(File.ReadAllText(ownerPath))!["attempt"]!.ToJsonString(), Is.EqualTo("1"));
    }

    [TestCase("mixed-flat-wrapped", "framework-ci-foreign-artifact", TestName = "framework-ci/flat-mixed-wrapped-layout")]
    [TestCase("foreign-repository", "framework-ci-artifact-owner", TestName = "framework-ci/flat-foreign-repository")]
    [TestCase("foreign-run", "framework-ci-artifact-owner", TestName = "framework-ci/flat-foreign-run")]
    [TestCase("foreign-commit", "framework-ci-artifact-owner", TestName = "framework-ci/flat-foreign-commit")]
    [TestCase("zero-attempt", "framework-ci-artifact-attempt", TestName = "framework-ci/flat-zero-attempt")]
    [TestCase("future-attempt", "framework-ci-artifact-attempt", TestName = "framework-ci/flat-future-attempt")]
    [TestCase("leading-zero-attempt", "framework-ci-artifact-attempt", TestName = "framework-ci/flat-noncanonical-attempt")]
    [TestCase("fractional-attempt", "framework-ci-artifact-attempt", TestName = "framework-ci/flat-fractional-attempt")]
    [TestCase("boolean-attempt", "framework-ci-artifact-attempt", TestName = "framework-ci/flat-boolean-attempt")]
    [TestCase("null-attempt", "framework-ci-artifact-attempt", TestName = "framework-ci/flat-null-attempt")]
    [TestCase("unexpected-flat-directory", "framework-ci-foreign-artifact", TestName = "framework-ci/flat-unexpected-root-directory")]
    [TestCase("unexpected-flat-file", "framework-ci-foreign-artifact", TestName = "framework-ci/flat-unexpected-root-file")]
    [TestCase("unexpected-wrapped-file", "framework-ci-foreign-artifact", TestName = "framework-ci/wrapped-unexpected-root-file")]
    public async Task FlatArtifactsCannotRelaxLayoutAttemptOrOwnerAdmission(string fault, string expectedReason)
    {
        if (fault == "unexpected-wrapped-file")
        {
            Write(Path.Combine(_root, "artifacts/unexpected.txt"), "unexpected-root-entry");
        }
        else
        {
            FlattenSingleBundle();
            var ownerPath = Path.Combine(_bundle, "ci-owner.json");
            var owner = JsonNode.Parse(File.ReadAllText(ownerPath))!;
            switch (fault)
            {
                case "mixed-flat-wrapped":
                    Directory.CreateDirectory(Bundle(1));
                    break;
                case "foreign-repository":
                    owner["repository"] = "other/fish-audio";
                    break;
                case "foreign-run":
                    owner["workflowRunId"] = (RunId + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "foreign-commit":
                    owner["commit"] = ForeignCommit;
                    owner["head"] = ForeignCommit;
                    break;
                case "zero-attempt":
                    owner["attempt"] = 0;
                    break;
                case "future-attempt":
                    owner["attempt"] = _attempt + 1;
                    break;
                case "leading-zero-attempt":
                    owner["attempt"] = "01";
                    break;
                case "fractional-attempt":
                    owner["attempt"] = 1.5;
                    break;
                case "boolean-attempt":
                    owner["attempt"] = true;
                    break;
                case "null-attempt":
                    owner["attempt"] = null;
                    break;
                case "unexpected-flat-directory":
                    Directory.CreateDirectory(Path.Combine(_bundle, "unexpected"));
                    break;
                case "unexpected-flat-file":
                    Write(Path.Combine(_bundle, "unexpected.txt"), "unexpected-root-entry");
                    break;
                default:
                    throw new InvalidOperationException("framework-ci-fixture-unknown-flat-fault");
            }
            WriteJson(ownerPath, owner);
        }

        var result = await AdmitAsync();

        Assert.That(result.ExitCode, Is.EqualTo(1), result.Output + result.Error);
        Assert.That(result.Error, Does.Contain("framework-ci: rejected (" + expectedReason + ";"));
        Assert.That(result.Output, Does.Not.Contain("verified complete NUnit foundation"));
        Assert.That(Directory.Exists(Path.Combine(_root, "verification")), Is.False,
            "Invalid artifact layout, attempt or owner must fail before fresh discovery or parser admission.");
    }

    private void FlattenSingleBundle()
    {
        var source = Path.GetFullPath(_bundle);
        var destination = Path.GetFullPath(Path.Combine(_root, "artifacts"));
        Assert.That(source, Is.EqualTo(Path.GetFullPath(Bundle(1))));
        Assert.That(Path.GetDirectoryName(source), Is.EqualTo(destination));
        Assert.That(Directory.GetFileSystemEntries(destination), Is.EqualTo(new[] { source }));
        RequireNoReparseAncestors(source);
        RequireNoReparseAncestors(destination);
        WriteJson(Path.Combine(source, "manifest.json"), _manifest);
        foreach (var name in new[] { "owner.json", "source.json" })
        {
            var auxiliary = Path.Combine(source, name);
            Assert.That(File.Exists(auxiliary), Is.False);
            // These optional root entries are opaque layout controls. The
            // fixture does not claim that their contents are admitted evidence.
            WriteJson(auxiliary, new JsonObject { ["fixtureAuxiliary"] = name });
        }
        var entries = Directory.GetFileSystemEntries(source);
        foreach (var entry in entries)
        {
            Assert.That(File.GetAttributes(entry) & FileAttributes.ReparsePoint, Is.EqualTo((FileAttributes)0));
            var target = Path.Combine(destination, Path.GetFileName(entry));
            Assert.That(File.Exists(target) || Directory.Exists(target), Is.False, "Flattening cannot overwrite an artifact entry.");
        }
        foreach (var entry in entries)
        {
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0) Directory.Move(entry, target);
            else File.Move(entry, target, overwrite: false);
        }
        Directory.Delete(source, recursive: false);
        _bundle = destination;
    }

    [Test]
    [Property("FishAudioCaseId", "framework-ci/pr-synthetic-merge-keeps-merge-artifact-owner")]
    public async Task PullRequestJobHeadCanDifferFromItsAuthenticatedMergeCheckout()
    {
        ConfigurePullRequest();

        var result = await AdmitAsync();

        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain("verified complete NUnit foundation attempt 1 (2 cases)."));
        Assert.That(Invocations(), Is.EqualTo(new[] { "discover", "evidence-catalog", "evidence-execution-plan", "evidence-execution-plan", "evidence-verify",
            "evidence-catalog", "evidence-execution-plan", "evidence-execution-plan", "evidence-verify", "evidence-migration" }));
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
    [TestCase("missing-phase-settings-digest", "framework-ci-missing-artifact-digest", TestName = "framework-ci/phase-settings-require-a-digest")]
    [TestCase("wrong-supervised-full-name", "framework-ci-phase-request-binding", TestName = "framework-ci/supervised-full-name-requires-current-binding")]
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
                var resultPath = Path.Combine(_bundle, FirstExecutionPhase["resultXml"]!.GetValue<string>());
                var xml = File.ReadAllText(resultPath).Replace("result=\"Passed\" duration=\"0.125\"", "result=\"Skipped\" duration=\"0.125\"", StringComparison.Ordinal);
                ReplaceArtifact("resultXml", xml);
                break;
            case "missing-phase-settings-digest":
                FirstExecutionPhase["runSettingsSha256"] = "";
                break;
            case "wrong-supervised-full-name":
                var phase = FirstExecutionPhase;
                var requestPath = Artifact(phase["request"]!.GetValue<string>());
                var request = JsonNode.Parse(File.ReadAllText(requestPath))!;
                request["cases"]![0]!["fullName"] = "Fixture.Foreign.Unrelated";
                WriteJson(requestPath, request);
                phase["requestSha256"] = Hash(requestPath);
                var supervisorPath = Artifact(phase["supervisor"]!.GetValue<string>());
                var supervisor = JsonNode.Parse(File.ReadAllText(supervisorPath))!;
                supervisor["requestSha256"] = phase["requestSha256"]!.DeepClone();
                foreach (var observation in supervisor["events"]!.AsArray())
                    if (observation!["caseId"] is not null)
                        observation["fullName"] = "Fixture.Foreign.Unrelated";
                WriteJson(supervisorPath, supervisor);
                phase["supervisorSha256"] = Hash(supervisorPath);
                break;
            case "skipped-execution-step":
                _jobs["jobs"]![0]!["steps"]![1]!["conclusion"] = "skipped";
                break;
            default:
                throw new InvalidOperationException("framework-ci-fixture-unknown-fault");
        }

        var result = await AdmitAsync();
        var marker = fault == "wrong-supervised-full-name" ? "framework-ci-request-full-name-must-bind-current-catalog\n" : "";
        Assert.That(result.ExitCode, Is.EqualTo(1), marker + result.Output + result.Error);
        Assert.That(result.Error, Does.Contain("framework-ci: rejected (" + expectedReason + ";"));
        Assert.That(result.Output, Does.Not.Contain("verified complete NUnit foundation"));
        if (fault is "malformed-raw-result" or "not-run")
        {
            Assert.That(Invocations(), Is.EqualTo(new[] { "discover", "evidence-catalog", "evidence-execution-plan", "evidence-execution-plan", "evidence-verify" }));
            Assert.That(File.Exists(Path.Combine(_root, "verification/plugin-phase-0001-terminal.json")), Is.False);
        }
    }

    [Test]
    [Property("FishAudioCaseId", "framework-ci/pending-supervisor-cannot-publish-success")]
    public async Task MatchingDigestCannotPromoteAnUnpublishedSupervisorFile()
    {
        var phase = FirstExecutionPhase;
        var relative = phase["supervisor"]!.GetValue<string>();
        var pending = relative + ".pending";
        File.Move(Path.Combine(_bundle, relative), Path.Combine(_bundle, pending));
        phase["supervisor"] = pending;
        // The report bytes, digest, execution and raw case results remain valid.
        // Only the producer-owned final publication path is absent.
        var result = await AdmitAsync();

        Assert.That(result.ExitCode, Is.EqualTo(1),
            "pending-supervisor-must-not-admit\n" + result.Output + result.Error);
        Assert.That(result.Error, Does.Contain("framework-ci: rejected (framework-ci-supervisor-rejected;"));
        Assert.That(result.Output, Does.Not.Contain("verified complete NUnit foundation"));
    }

    private void AddAssembly(CaseDefinition definition, JsonObject fresh)
    {
        var key = definition.Key;
        var relativeDirectory = "phases/discover-" + key;
        var discoveryPath = Artifact(relativeDirectory + "/discovery.dump");
        Write(discoveryPath, Discovery(definition));
        WriteJson(Path.Combine(_bundle, key, "catalog.json"), Catalog(definition, "captured-1"));
        WriteJson(Path.Combine(_bundle, key, "selection.json"), new JsonObject { ["schemaVersion"] = 1, ["ids"] = new JsonArray(definition.Id) });
        WriteJson(Path.Combine(_bundle, key, "requested-selection.json"), new JsonObject
        {
            ["schemaVersion"] = 1, ["ids"] = new JsonArray(definition.Id), ["maximumConcurrency"] = 4,
        });
        WriteJson(Path.Combine(_bundle, key, "execution-plan.json"), ExecutionPlan(definition, discoveryPath));
        var relativeAssembly = "tests/" + key + "/bin/Release/net10.0-windows/" + key + ".dll";
        var currentAssembly = Path.Combine(_root, "repository", relativeAssembly);
        if (key == "testing-tool")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(currentAssembly)!);
            File.Copy(typeof(FrameworkCiEvidenceTests).Assembly.Location, currentAssembly, overwrite: false);
        }
        var assemblyHash = Hash(currentAssembly);
        var phaseDirectory = key + "/execution/phase-0001";
        var phase = new JsonObject
        {
            ["id"] = "phase-0001", ["kind"] = "ordinary", ["profile"] = null,
            ["requireOrder"] = false, ["maximumConcurrency"] = 4, ["exclusiveResources"] = new JsonArray(),
            ["selectedIds"] = new JsonArray(definition.Id), ["executed"] = true, ["passedCount"] = 1,
            ["runtimeEvidence"] = phaseDirectory + "/runtime-evidence",
            ["markerRequired"] = key == "plugin", ["markerObserved"] = key == "plugin", ["parallelProof"] = false,
        };
        WriteJson(Artifact(phaseDirectory + "/selection.json"), new JsonObject { ["schemaVersion"] = 1, ["ids"] = new JsonArray(definition.Id) });
        WriteJson(Artifact(phaseDirectory + "/terminal.json"), new JsonObject
        {
            ["schemaVersion"] = 1, ["kind"] = "nunit-terminal", ["complete"] = true, ["passedCount"] = 1,
            ["cases"] = new JsonArray(new JsonObject
            {
                ["canonicalId"] = definition.Id, ["fullName"] = definition.ClassName + ".Probe", ["outcome"] = "passed",
                ["duration"] = "00:00:00.1250000", ["platformDuration"] = "00:00:00.1250000",
            }),
        });
        Write(Artifact(phaseDirectory + "/supervised/nunit/" + key + ".xml"), Terminal(definition));
        Write(Artifact(phaseDirectory + "/supervised/trx/results.trx"), Trx(definition));
        Write(Artifact(phaseDirectory + "/execution.runsettings"), RunSettings(definition, Artifact(phaseDirectory + "/supervised/nunit")));
        var requestPath = Artifact(phaseDirectory + "/execution-request.json");
        WriteJson(requestPath, ExecutionRequest(definition, currentAssembly, assemblyHash, discoveryPath, phaseDirectory));
        Directory.CreateDirectory(Artifact(phaseDirectory + "/runtime-evidence"));
        if (key == "plugin") Write(Artifact(phaseDirectory + "/runtime-evidence/fixture-created-1234.txt"), "constructed");
        var supervisorPath = Artifact(phaseDirectory + "/supervised/supervisor.json");
        // This is explicitly synthetic admission input. The real XML/TRX parser
        // still decides whether its claimed case results can be admitted; these
        // process fields do not claim execution of real product scenarios.
        WriteJson(supervisorPath, Supervisor(definition, assemblyHash, phaseDirectory, requestPath));
        foreach (var item in new[] { ("selection", "selection.json"), ("request", "execution-request.json"),
                     ("runSettings", "execution.runsettings"), ("terminal", "terminal.json"),
                     ("resultXml", "supervised/nunit/" + key + ".xml"), ("trx", "supervised/trx/results.trx"),
                     ("supervisor", "supervised/supervisor.json") })
        {
            phase[item.Item1] = phaseDirectory + "/" + item.Item2;
            phase[item.Item1 + "Sha256"] = Hash(Artifact(phaseDirectory + "/" + item.Item2));
        }
        var assembly = new JsonObject
        {
            ["key"] = key, ["project"] = "tests/" + key + "/" + key + ".csproj",
            ["assembly"] = relativeAssembly, ["sha256"] = assemblyHash,
            ["discoveredCount"] = 1, ["selectedCount"] = 1, ["passedCount"] = 1, ["executed"] = true,
            ["selectedIds"] = new JsonArray(definition.Id), ["executionPhases"] = new JsonArray(phase),
        };
        foreach (var item in new[] { ("catalog", "catalog.json"), ("selection", "selection.json"),
                     ("requestedSelection", "requested-selection.json"), ("executionPlan", "execution-plan.json") })
        {
            assembly[item.Item1] = key + "/" + item.Item2;
            assembly[item.Item1 + "Sha256"] = Hash(Path.Combine(_bundle, key, item.Item2));
        }
        _manifest["assemblies"]!.AsArray().Add(assembly);
        foreach (var name in new[] { "build-" + key, "discover-" + key, "catalog-" + key, "plan-" + key,
                     "execute-" + key + "-phase-0001", "verify-" + key + "-phase-0001" })
        {
            _manifest["phases"]!.AsArray().Add(new JsonObject
            {
                ["name"] = name, ["complete"] = true, ["exitCode"] = 0, ["directory"] = "phases/" + name,
            });
        }
        // Cross-process adapter IDs deliberately differ from both raw discovery
        // and the captured catalog; only canonical and exact named identity join.
        var freshRoot = Path.Combine(_root, "repository/discovery-template");
        var freshCatalogPath = Path.Combine(freshRoot, key, "catalog.json");
        var freshSelectionPath = Path.Combine(freshRoot, key, "selection.json");
        WriteJson(freshCatalogPath, Catalog(definition, "fresh-99"));
        WriteJson(freshSelectionPath, new JsonObject { ["schemaVersion"] = 1, ["ids"] = new JsonArray(definition.Id) });
        Write(Path.Combine(freshRoot, relativeDirectory, "discovery.dump"), Discovery(definition, "fresh-99"));
        fresh["assemblies"]!.AsArray().Add(new JsonObject
        {
            ["key"] = key, ["project"] = "tests/" + key + "/" + key + ".csproj",
            ["assembly"] = relativeAssembly, ["sha256"] = assemblyHash,
            ["catalog"] = key + "/catalog.json", ["catalogSha256"] = Hash(freshCatalogPath),
            ["selection"] = key + "/selection.json", ["selectionSha256"] = Hash(freshSelectionPath),
            ["discoveredCount"] = 1, ["selectedCount"] = 1, ["selectedIds"] = new JsonArray(definition.Id),
            ["executed"] = false, ["passedCount"] = 0, ["terminal"] = null, ["terminalSha256"] = null,
            ["resultXml"] = null, ["resultXmlSha256"] = null, ["trx"] = null, ["trxSha256"] = null,
            ["supervisor"] = null, ["supervisorSha256"] = null,
            ["markerRequired"] = false, ["markerObserved"] = false, ["parallelProof"] = false,
        });
        fresh["phases"]!.AsArray().Add(new JsonObject
        {
            ["name"] = "discover-" + key, ["complete"] = true, ["exitCode"] = 0, ["directory"] = relativeDirectory,
        });
    }

    private static JsonObject Session(string mode) => new()
    {
        ["schemaVersion"] = mode == "Full" ? 2 : 1, ["kind"] = "unified-test-session", ["complete"] = true, ["mode"] = mode,
        ["runId"] = mode == "Full" ? "11111111111111111111111111111111" : "22222222222222222222222222222222",
        ["platform"] = "VSTest", ["configuration"] = "Release", ["workers"] = 4, ["selection"] = "all-discovered", ["baselineClaim"] = false,
        ["source"] = new JsonObject
        {
            ["snapshot"] = false, ["dirty"] = false, ["tree"] = null, ["commit"] = MergeCommit, ["sourceCommit"] = MergeCommit,
            ["fingerprint"] = new string('c', 64), ["fingerprintKind"] = "sha256-source-path-and-bytes-ordinal-v1",
        },
        ["toolSha256"] = Hash(typeof(BinaryIdentity).Assembly.Location),
        ["finishedUtc"] = "2026-10-20T01:05:00Z", ["assemblies"] = new JsonArray(), ["phases"] = new JsonArray(),
    };

    private string Artifact(string relative) => Path.GetFullPath(Path.Combine(_bundle, relative));

    private static JsonObject ExecutionPlan(CaseDefinition definition, string discoveryPath) => new()
    {
        ["schemaVersion"] = 1, ["kind"] = "nunit-execution-plan", ["complete"] = true,
        ["plan"] = new JsonObject
        {
            ["rawDiscoverySha256"] = Hash(discoveryPath), ["requestedIds"] = new JsonArray(definition.Id),
            ["expandedIds"] = new JsonArray(definition.Id), ["expansionReasons"] = new JsonArray(),
            ["phases"] = new JsonArray(new JsonObject
            {
                ["id"] = "phase-0001", ["kind"] = "ordinary", ["profile"] = null,
                ["cases"] = Catalog(definition, "0-1")["cases"]!.DeepClone(), ["requireOrder"] = false,
                ["maximumConcurrency"] = 4, ["exclusiveResources"] = new JsonArray(),
            }),
        },
    };

    private JsonObject ExecutionRequest(CaseDefinition definition, string assemblyPath, string assemblyHash,
        string discoveryPath, string phaseDirectory) => new()
    {
        ["schemaVersion"] = 1, ["runId"] = _manifest["runId"]!.DeepClone(),
        ["sourceTree"] = null, ["sourceFingerprint"] = new string('c', 64),
        ["workingDirectory"] = Path.Combine(_root, "repository"), ["shellExecutable"] = FindExecutable("pwsh.exe"),
        ["dotnetExecutable"] = FindExecutable("dotnet.exe"), ["launcherScript"] = Path.Combine(_repositoryRoot, "tools/Invoke-TestProcess.ps1"),
        ["testAssembly"] = Path.GetFullPath(assemblyPath), ["testAssemblySha256"] = assemblyHash,
        ["runSettings"] = Artifact(phaseDirectory + "/execution.runsettings"), ["discoveryPath"] = discoveryPath,
        ["resultXmlPath"] = Artifact(phaseDirectory + "/supervised/nunit/" + definition.Key + ".xml"),
        ["trxPath"] = Artifact(phaseDirectory + "/supervised/trx/results.trx"), ["outputDirectory"] = Artifact(phaseDirectory + "/supervised"),
        ["cases"] = new JsonArray(new JsonObject { ["canonicalId"] = definition.Id, ["fullName"] = definition.ClassName + ".Probe" }),
        ["maximumConcurrency"] = 4, ["requireOrder"] = false,
        ["environment"] = new JsonObject { ["FISHAUDIO_COMPAT_EVIDENCE_ROOT"] = Artifact(phaseDirectory + "/runtime-evidence") },
    };

    private static string RunSettings(CaseDefinition definition, string outputDirectory)
    {
        var regex = @"\A" + Regex.Escape(definition.ClassName + ".Probe") + @"\z";
        var literal = regex.Replace(@"\", @"\\").Replace("\"", "\\\"");
        return new XElement("RunSettings",
            new XElement("RunConfiguration", new XElement("MaxCpuCount", "1"), new XElement("TreatNoTestsAsError", "true")),
            new XElement("NUnit", new XElement("NumberOfTestWorkers", "4"), new XElement("PreFilter", "false"),
                new XElement("UseParentFQNForParametrizedTests", "false"), new XElement("IncludeStackTrace", "false"),
                new XElement("DumpXmlTestDiscovery", "true"), new XElement("DumpXmlTestResults", "false"),
                new XElement("TestOutputXml", outputDirectory), new XElement("OutputXmlFolderMode", "AsSpecified"),
                new XElement("StopOnError", "true"), new XElement("Where", "(test =~ \"" + literal + "\")"))).ToString();
    }

    private JsonObject Supervisor(CaseDefinition definition, string assemblyHash, string phaseDirectory, string requestPath) => new()
    {
        ["schemaVersion"] = 1, ["kind"] = "framework-execution", ["runId"] = _manifest["runId"]!.DeepClone(),
        ["sourceTree"] = null, ["sourceFingerprint"] = new string('c', 64), ["testAssemblySha256"] = assemblyHash,
        ["requestSha256"] = Hash(requestPath),
        ["policy"] = new JsonObject { ["maximumConcurrency"] = 4, ["requireOrder"] = false },
        ["launch"] = new JsonObject { ["kind"] = "vstest", ["sessionId"] = null, ["invocationSha256"] = new string('9', 64),
            ["toolSha256"] = null, ["vstestConsoleSha256"] = null },
        ["selectedIds"] = new JsonArray(definition.Id), ["failureCode"] = null, ["baselineClaim"] = false,
        ["state"] = new JsonObject
        {
            ["stage"] = "child-exit", ["failureStage"] = null, ["failureCaseId"] = null, ["warningCount"] = 0,
            ["complete"] = true, ["terminationRequired"] = false, ["passedIds"] = new JsonArray(definition.Id),
            ["failedIds"] = new JsonArray(), ["timedOutIds"] = new JsonArray(), ["activeIds"] = new JsonArray(),
            ["interruptedIds"] = new JsonArray(), ["notRunIds"] = new JsonArray(),
        },
        ["target"] = new JsonObject { ["started"] = true, ["pid"] = 1235, ["exitCode"] = 0 },
        ["process"] = new JsonObject
        {
            ["launcherPid"] = 1236, ["launcherExited"] = true, ["stdoutDrained"] = true, ["stderrDrained"] = true,
            ["ownedActiveProcesses"] = 0,
        },
        ["rawEvidence"] = new JsonObject
        {
            ["verified"] = true, ["resultXmlPath"] = Artifact(phaseDirectory + "/supervised/nunit/" + definition.Key + ".xml"),
            ["trxPath"] = Artifact(phaseDirectory + "/supervised/trx/results.trx"),
            ["resultXmlSha256"] = Hash(Artifact(phaseDirectory + "/supervised/nunit/" + definition.Key + ".xml")),
            ["trxSha256"] = Hash(Artifact(phaseDirectory + "/supervised/trx/results.trx")),
        },
        ["events"] = ControlEvents(definition, assemblyHash), ["warnings"] = new JsonArray(),
        ["diagnostics"] = new JsonObject { ["deliveredWarnings"] = 0, ["writerCompleted"] = true }, ["elapsedMilliseconds"] = 32,
        ["finalization"] = new JsonObject { ["cleanupAnchorTimestamp"] = 1032L, ["frequency"] = 1000L },
    };

    // Minimal serialized lifecycle shape accepted by the real consumer. These
    // six records are synthetic input, not authenticated runtime observations.
    private JsonArray ControlEvents(CaseDefinition definition, string assemblyHash)
    {
        var runId = _manifest["runId"]!.GetValue<string>();
        const string nonce = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
        object Host(string kind, long timestamp, string? caseId = null, string? fullName = null, string? outcome = null) => new
        {
            schemaVersion = 1, role = "nunit-host", nonce, runId, sourceTree = (string?)null,
            pid = 1234, kind, timestamp, frequency = 1000L, assemblySha256 = assemblyHash, caseId, fullName,
            ordinal = 0, queuedTimestamp = 0L, outcome, targetPid = (int?)null, exitCode = (int?)null,
        };
        object Launcher(string kind, long timestamp, int? exitCode = null) => new
        {
            schemaVersion = 1, role = "execution-launcher", nonce, runId, sourceTree = (string?)null,
            pid = 1236, kind, timestamp, frequency = 1000L, assemblySha256 = (string?)null,
            caseId = (string?)null, fullName = (string?)null, ordinal = 0, queuedTimestamp = 0L,
            outcome = (string?)null, targetPid = (int?)1235, exitCode,
        };
        return JsonSerializer.SerializeToNode(new[]
        {
            Launcher("target-started", 1000), Host("ready", 1001),
            Host("case-start", 1010, definition.Id, definition.ClassName + ".Probe"),
            Host("case-finished", 1020, definition.Id, definition.ClassName + ".Probe", "passed"),
            Host("cleanup-complete", 1030), Launcher("target-exited", 1031, 0),
        })!.AsArray();
    }

    private void PrepareLegacyMetadataFixture()
    {
        var migrationDirectory = Path.Combine(_root, "repository/tests/Migration");
        Directory.CreateDirectory(migrationDirectory);
        var reference = Path.Combine(migrationDirectory, "legacy-catalog.json");
        File.Copy(Path.Combine(_repositoryRoot, "tests/Migration/legacy-catalog.json"), reference, overwrite: false);
        WriteJson(Path.Combine(migrationDirectory, "migrated-cases.json"), new JsonObject
        {
            ["schemaVersion"] = 1, ["migrations"] = new JsonArray(),
        });
        // The fixture deliberately declares the whole original reference as
        // synthetic legacy-owned metadata. Its two NUnit IDs are ordinary new
        // cases. Neither this producer nor that declaration executes a test.
        using var original = JsonDocument.Parse(File.ReadAllBytes(reference));
        var rows = original.RootElement.GetProperty("cases").EnumerateArray().Select(item =>
            item.GetProperty("id").GetString()! + "\t" + item.GetProperty("partition").GetString()!).ToArray();
        var directory = Path.Combine(_root, "repository/tests/plugin/bin/Release/net10.0-windows");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "plugin.dll"), LegacyMetadataImage.Value);
        Write(Path.Combine(directory, "fixture-legacy-catalog.txt"), string.Join("\n", rows) + "\n");
        WriteJson(Path.Combine(directory, "plugin.runtimeconfig.json"), new JsonObject
        {
            ["runtimeOptions"] = new JsonObject
            {
                ["tfm"] = "net10.0", ["framework"] = new JsonObject
                {
                    ["name"] = "Microsoft.NETCore.App", ["version"] = "10.0.0",
                },
            },
        });
    }

    private static byte[] CompileLegacyMetadataImage()
    {
        // Lazy evaluation occurs in SetUp, never during NUnit discovery. Roslyn
        // is already a project dependency; no build command or package install
        // is involved. The emitted program references only the BCL and can list
        // fixture data for this one metadata-only command, never run scenarios.
        var platform = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("framework-ci-fixture-runtime-references-missing");
        var references = platform.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("FrameworkCiLegacyMetadataFixture",
            [CSharpSyntaxTree.ParseText(LegacyMetadataSource, path: "/_fixture/LegacyMetadata.cs")], references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel: OptimizationLevel.Release,
                deterministic: true));
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        Assert.That(emitted.Success, Is.True, "Metadata-only fixture emit must succeed: " +
            string.Join(",", emitted.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error).Select(item => item.Id)));
        return output.ToArray();
    }

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
        var record = name == "catalog" ? assembly : FirstExecutionPhase;
        var path = Path.Combine(_bundle, record[name]!.GetValue<string>());
        Write(path, contents);
        record[name + "Sha256"] = Hash(path);
        if (name is "resultXml" or "trx")
        {
            // Keep this outer claim coherent with intentionally corrupted raw
            // bytes so the existing negative oracle still reaches the parser.
            var supervisorPath = Path.Combine(_bundle, record["supervisor"]!.GetValue<string>());
            var supervisor = JsonNode.Parse(File.ReadAllText(supervisorPath))!;
            supervisor["rawEvidence"]![name + "Sha256"] = record[name + "Sha256"]!.DeepClone();
            WriteJson(supervisorPath, supervisor);
            record["supervisorSha256"] = Hash(supervisorPath);
        }
    }

    private JsonObject FirstExecutionPhase => _manifest["assemblies"]![0]!["executionPhases"]![0]!.AsObject();

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
        var configured = Environment.GetEnvironmentVariable("FISHAUDIO_TEST_REPOSITORY_ROOT");
        string? snapshot = null;
        if (Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT") is { Length: > 0 } contextPath)
        {
            using var context = JsonDocument.Parse(File.ReadAllText(contextPath));
            snapshot = context.RootElement.GetProperty("root").GetString();
        }
        foreach (var candidate in new[] { configured, snapshot }.Where(value => value is not null))
        {
            if (!Path.IsPathFullyQualified(candidate!))
                throw new InvalidOperationException("framework-ci-fixture-repository-not-absolute");
            RequireNoReparseAncestors(candidate!);
        }
        if (configured is not null && snapshot is not null &&
            !Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured)).Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(snapshot)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("framework-ci-fixture-repository-context-mismatch");
        var root = configured ?? snapshot;
        if (root is null || !File.Exists(Path.Combine(root, "tools/VerifyFrameworkCi.ps1")) ||
            !File.Exists(Path.Combine(root, "tools/Assert-FrameworkSupervisor.ps1")) ||
            !File.Exists(Path.Combine(root, "tools/Assert-FrameworkExecutionPlan.ps1")) ||
            !File.Exists(Path.Combine(root, "tests/Migration/legacy-catalog.json")))
            throw new InvalidOperationException("framework-ci-fixture-explicit-repository-required");
        return Path.GetFullPath(root);
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

    private static string Discovery(CaseDefinition definition, string platformId = "0-1") => $$"""
        <NUnitXml><test-run id="0" name="example.dll" runstate="Runnable" testcasecount="1">
          <test-suite type="Assembly" id="0-10" name="example.dll" runstate="Runnable" testcasecount="1">
            <test-suite type="TestFixture" id="0-20" name="Fixture" fullname="{{definition.ClassName}}" runstate="Runnable" testcasecount="1">
              <test-case id="{{platformId}}" name="Probe" fullname="{{definition.ClassName}}.Probe" methodname="Probe" classname="{{definition.ClassName}}" runstate="Runnable">
                <properties><property name="FishAudioCaseId" value="{{definition.Id}}" /></properties>
              </test-case>
            </test-suite>
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

    private const string LegacyMetadataSource = """
        using System;
        using System.Globalization;
        using System.IO;
        using System.Text;

        internal static class Program
        {
            private static int Main(string[] args)
            {
                if (args.Length != 2 || args[0] != "--legacy" || args[1] != "--list")
                {
                    Console.Error.WriteLine("fixture-metadata-only-invocation");
                    return 2;
                }
                var rows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory,
                    "fixture-legacy-catalog.txt"), new UTF8Encoding(false, true));
                foreach (var row in rows) Console.WriteLine(row);
                Console.WriteLine("Selected " + rows.Length.ToString(CultureInfo.InvariantCulture) + " case(s).");
                return 0;
            }
        }
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
            if ($args.Count -lt 2 -or $args[0] -cne $expected -or $args[1] -cnotin @('evidence-catalog', 'evidence-execution-plan', 'evidence-verify', 'evidence-migration')) { throw 'fixture-unexpected-dotnet' }
            [IO.File]::AppendAllText((Join-Path $global:fixtureRoot 'invocations.txt'), ([string]$args[1] + "`n"))
            $forward = @($args[1..($args.Count - 1)])
            & $global:fixtureDotnet $global:fixtureTool @forward
            $global:LASTEXITCODE = $LASTEXITCODE
        }
        & $Helper -ArtifactDirectory $Artifacts -JobAttemptsPath $Jobs -ResultsDirectory $Results -RepositoryRoot $Root
        exit $LASTEXITCODE
        """;
}
