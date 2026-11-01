using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

// Pure protocol fixtures: no network, filesystem, environment, native window,
// profiler, Git repository or test delegate is created by these controls.
public sealed class FullCoverageCiProvenanceTests
{
    [TestCase(2, TestName = "coverage-ci-provenance/select-exact-main-preparation-and-all-shards")]
    [TestCase(6, TestName = "coverage-ci-provenance/shard-count-independent-of-four-machine-limit")]
    public void CompleteCurrentOwnersAreSelectedWithoutPromotingTheirClaims(int count)
    {
        var scenario = Create(count);
        var result = FullCoverageCiProvenance.Select(scenario.Input, scenario.Context);
        Assert.Multiple(() =>
        {
            Assert.That(result.Preparation.ActualJobId, Is.EqualTo(101));
            Assert.That(result.Shards.Select(owner => owner.ShardIndex), Is.EqualTo(Enumerable.Range(0, count).Select(index => (int?)index)));
            Assert.That(result.Shards.All(owner => owner.PreparationJobId == 101 && owner.PreparationAttempt == 1), Is.True);
            Assert.That(result.MaximumConcurrentMachines, Is.EqualTo(4));
            Assert.That(result.OwnerSelectionComplete, Is.True);
            Assert.That(result.ApiEventAuthenticated || result.SourceBuildBytesVerified || result.MachineConcurrencyVerified || result.MainProvenanceVerified ||
                result.RawCoverageUnionAdmitted || result.CompleteBaseline || result.PublicationAuthorized || result.TiaSelectionAuthorized, Is.False);
            Assert.That(result.RequiresIndependentRawReimport, Is.True);
        });
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-ci-provenance/cached-siblings-retain-actual-first-attempt-owners")]
    public void CachedSuccessfulViewsRequireTheEarlierExactExecutionAndItsArtifacts()
    {
        var scenario = Create(currentAttempt: 2);
        scenario = AddJobs(scenario, scenario.Input.Jobs.Jobs.Select(job => job with { Id = job.Id + 1000, RunAttempt = 2 }).ToArray());
        var result = FullCoverageCiProvenance.Select(scenario.Input, scenario.Context);
        Assert.That(result.Preparation.ActualAttempt, Is.EqualTo(1));
        Assert.That(result.Shards.All(owner => owner.ActualAttempt == 1 && owner.ActualJobId < 1000), Is.True);
    }

    [TestCase("prepare", "failure", TestName = "coverage-ci-provenance/newer-failed-preparation-blocks-green")]
    [TestCase("prepare", "cancelled", TestName = "coverage-ci-provenance/newer-cancelled-preparation-blocks-green")]
    [TestCase("shard", "failure", TestName = "coverage-ci-provenance/newer-failed-shard-blocks-green")]
    [TestCase("shard", "cancelled", TestName = "coverage-ci-provenance/newer-cancelled-shard-blocks-green")]
    [TestCase("shard", "unfinished", TestName = "coverage-ci-provenance/newer-unfinished-shard-blocks-green")]
    [TestCase("shard", "skipped", TestName = "coverage-ci-provenance/newer-skipped-shard-blocks-green")]
    [TestCase("shard", "neutral", TestName = "coverage-ci-provenance/newer-neutral-shard-blocks-green")]
    public void NewUnsuccessfulOwnerCannotDisappearDuringPayloadFiltering(string role, string outcome)
    {
        var scenario = Create(currentAttempt: 2);
        var previous = scenario.Input.Jobs.Jobs[role == "prepare" ? 0 : 1];
        var failed = previous with { Id = previous.Id + 1000, RunAttempt = 2,
            Status = outcome == "unfinished" ? "in_progress" : "completed", Conclusion = outcome == "unfinished" ? null : outcome,
            StartedAt = null, CompletedAt = null, RunnerId = 0, RunnerName = null, Steps = null };
        scenario = AddJobs(scenario, failed);
        Reject(scenario, "unsuccessful-latest-job");
    }

    [TestCase("runner", TestName = "coverage-ci-provenance/reject-cached-runner-substitution")]
    [TestCase("step", TestName = "coverage-ci-provenance/reject-cached-ordered-step-substitution")]
    [TestCase("overlap", TestName = "coverage-ci-provenance/reject-cached-execution-crossing-attempt-fence")]
    public void CachedViewNeedsTheWholeUniqueTimeWitness(string mutation)
    {
        var scenario = Create(currentAttempt: 2);
        var previous = scenario.Input.Jobs.Jobs[1];
        var cached = previous with { Id = 1201, RunAttempt = 2 };
        if (mutation == "runner") cached = cached with { RunnerId = 999 };
        if (mutation == "step") cached = cached with { Steps = cached.Steps!.Select((step, index) => index == 0 ? step with { Name = "Changed ordered step" } : step).ToArray() };
        if (mutation == "overlap") cached = cached with { CompletedAt = Time(2, 0) };
        scenario = AddJobs(scenario, cached);
        Reject(scenario, mutation == "overlap" ? "overlapping-cached-job" : "unmatched-or-ambiguous-cached-job");
    }

    [TestCase(false, TestName = "coverage-ci-provenance/new-preparation-invalidates-old-shards-even-with-same-seals")]
    [TestCase(true, TestName = "coverage-ci-provenance/relabelled-old-shards-cannot-precede-new-preparation")]
    public void PreparationActualExecutionIdentityCannotBeReplacedByAnEqualDigest(bool relabel)
    {
        var scenario = Create(currentAttempt: 2);
        scenario = RebindPreparation(scenario, 2, preserveSeals: true);
        scenario = AddActual(scenario, FullCoverageCiRole.Prepare, null, 2, 1101, 1101, 2);
        scenario = scenario with { Input = scenario.Input with { Artifacts = scenario.Input.Artifacts.Select(artifact =>
            artifact.Owner.Role == FullCoverageCiRole.Shard ? artifact with { Owner = artifact.Owner with {
                Preparation = scenario.Context.Preparation, PreparationJobId = relabel ? 1101 : artifact.Owner.PreparationJobId,
                PreparationAttempt = relabel ? 2 : artifact.Owner.PreparationAttempt } } : artifact).ToArray() } };
        Reject(scenario, relabel ? "shard-before-preparation" : "mixed-preparation");
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-ci-provenance/latest-complete-rerun-owns-entire-artifacts")]
    public void NewActualOwnersReplaceWholeEarlierArtifacts()
    {
        var scenario = Create(currentAttempt: 2);
        scenario = RebindPreparation(scenario, 2, preserveSeals: false);
        scenario = AddActual(scenario, FullCoverageCiRole.Prepare, null, 2, 1101, 1101, 2);
        scenario = AddActual(scenario, FullCoverageCiRole.Shard, 0, 2, 1200, 1101, 2);
        scenario = AddActual(scenario, FullCoverageCiRole.Shard, 1, 2, 1201, 1101, 2);
        var result = FullCoverageCiProvenance.Select(scenario.Input, scenario.Context);
        Assert.That(result.Preparation.ActualJobId, Is.EqualTo(1101));
        Assert.That(result.Preparation.EvidencePath, Is.EqualTo(scenario.Context.Preparation.Prepared.PreparedPath));
        Assert.That(result.Shards.Select(owner => owner.ActualJobId), Is.EqualTo(new long[] { 1200, 1201 }));
        Assert.That(result.Shards.All(owner => owner.PreparationJobId == 1101 && owner.PreparationAttempt == 2), Is.True);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-ci-provenance/cached-witness-retains-null-time-skipped-step")]
    public void OptionalSkippedStepRetainsItsExistingNullTimeShape()
    {
        var scenario = Create(currentAttempt: 2);
        scenario = ReplaceJob(scenario, 1, scenario.Input.Jobs.Jobs[1] with { Steps = scenario.Input.Jobs.Jobs[1].Steps!
            .Concat([new FullCoverageCiStep(4, "Optional diagnostic", "completed", "skipped", null, null)]).ToArray() });
        scenario = AddJobs(scenario, scenario.Input.Jobs.Jobs[1] with { Id = 1200, RunAttempt = 2 });
        Assert.That(FullCoverageCiProvenance.Select(scenario.Input, scenario.Context).Shards[0].ActualAttempt, Is.EqualTo(1));
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-ci-provenance/reject-prepare-evidence-path-detached-from-independent-expectation")]
    public void SelectedPreparationEvidenceCannotPointAtAnotherAttemptRoot()
    {
        var scenario = RebindPreparation(Create(), 2, preserveSeals: true);
        // Make every payload binding agree with the caller. Only the selected
        // actual attempt's derived evidence path remains inconsistent.
        scenario = scenario with { Input = scenario.Input with { Artifacts = scenario.Input.Artifacts.Select(artifact =>
            artifact with { Owner = artifact.Owner with { Preparation = scenario.Context.Preparation } }).ToArray() } };
        Reject(scenario, "prepared-path");
    }

    [TestCase("missing-artifact", TestName = "coverage-ci-provenance/reject-latest-success-without-artifact")]
    [TestCase("orphan-artifact", TestName = "coverage-ci-provenance/newer-orphan-artifact-blocks-old-green")]
    [TestCase("duplicate-job", TestName = "coverage-ci-provenance/reject-duplicate-role-attempt-job")]
    [TestCase("duplicate-artifact", TestName = "coverage-ci-provenance/reject-duplicate-role-attempt-artifact")]
    [TestCase("missing-index", TestName = "coverage-ci-provenance/reject-missing-shard-index")]
    [TestCase("unknown-index", TestName = "coverage-ci-provenance/reject-out-of-range-shard-index")]
    public void ExactRoleAndArtifactMembershipIsRequired(string mutation)
    {
        var scenario = Create(currentAttempt: 2);
        var artifacts = scenario.Input.Artifacts;
        switch (mutation)
        {
            case "missing-artifact":
                scenario = AddJobs(scenario, Job(scenario.Context.Roles[1], 2, 1200));
                break;
            case "orphan-artifact":
                scenario = AddArtifacts(scenario, Artifact(scenario.Context, scenario.Context.Roles[1], 2, 1200, 101, 1));
                break;
            case "duplicate-job": scenario = AddJobs(scenario, scenario.Input.Jobs.Jobs[1] with { Id = 999 }); break;
            case "duplicate-artifact": scenario = AddArtifacts(scenario, artifacts[1]); break;
            case "missing-index":
                scenario = scenario with { Input = scenario.Input with { Artifacts = artifacts.Take(2).ToArray() } };
                break;
            default:
                scenario = AddArtifacts(scenario, artifacts[1] with { Owner = artifacts[1].Owner with { ShardIndex = 2 } });
                break;
        }
        Reject(scenario, mutation switch
        {
            "duplicate-job" => "duplicate-role-attempt-job", "duplicate-artifact" => "duplicate-role-attempt-artifact",
            "unknown-index" => "artifact-role", "orphan-artifact" => "missing-latest-job", _ => "missing-latest-artifact",
        });
    }

    [TestCase("source", TestName = "coverage-ci-provenance/reject-newer-source-seal-mismatch")]
    [TestCase("tool", TestName = "coverage-ci-provenance/reject-newer-tool-seal-mismatch")]
    [TestCase("build", TestName = "coverage-ci-provenance/reject-newer-build-seal-mismatch")]
    [TestCase("prepared", TestName = "coverage-ci-provenance/reject-newer-prepared-seal-mismatch")]
    public void NewMismatchedPayloadCannotBeFilteredOutBeforeSelection(string mutation)
    {
        var scenario = Create(currentAttempt: 2);
        scenario = AddActual(scenario, FullCoverageCiRole.Shard, 0, 2, 1200, 101, 1);
        var latest = scenario.Input.Artifacts[^1];
        var preparation = latest.Owner.Preparation;
        preparation = mutation switch
        {
            "source" => preparation with { Source = preparation.Source with { Fingerprint = Sha("foreign-source") } },
            "tool" => preparation with { MetadataToolSha256 = Sha("foreign-tool") },
            "build" => preparation with { BuildAuthoritySha256 = Sha("foreign-build") },
            _ => preparation with { Prepared = preparation.Prepared with { PreparedSha256 = Sha("foreign-prepared") } },
        };
        scenario = ReplaceArtifact(scenario, latest, latest with { Owner = latest.Owner with { Preparation = preparation } });
        Reject(scenario, "preparation-binding");
    }

    [TestCase("archive", TestName = "coverage-ci-provenance/reject-observed-archive-digest-drift")]
    [TestCase("inventory", TestName = "coverage-ci-provenance/reject-observed-inventory-byte-seal-drift")]
    [TestCase("root", TestName = "coverage-ci-provenance/reject-artifact-selected-original-root")]
    [TestCase("job", TestName = "coverage-ci-provenance/reject-artifact-bound-to-advertised-other-job-id")]
    [TestCase("time", TestName = "coverage-ci-provenance/reject-artifact-finished-outside-execution")]
    public void SelectedArtifactMustBindTheActualExecutionAndObservedInventory(string mutation)
    {
        var scenario = Create();
        var previous = scenario.Input.Artifacts[1];
        var changed = mutation switch
        {
            "archive" => previous with { ObservedArchiveSha256 = Sha("different-archive") },
            "inventory" => previous with { ObservedInventory = previous.ObservedInventory with {
                Files = previous.ObservedInventory.Files.Select((file, index) => index == 0 ? file with { Length = file.Length + 1 } : file).ToArray() } },
            "root" => previous with { Owner = previous.Owner with { OriginalRoot = "C:\\untrusted-destination" } },
            "job" => previous with { Owner = previous.Owner with { JobId = 999 } },
            _ => previous with { Owner = previous.Owner with { FinishedUtc = Time(1, 0) } },
        };
        scenario = ReplaceArtifact(scenario, previous, changed);
        Reject(scenario, mutation switch
        {
            "archive" => "archive-binding", "inventory" => "inventory-mismatch", "root" or "job" => "artifact-owner", _ => "owner-time",
        });
    }

    [TestCase("jobs-digest", TestName = "coverage-ci-provenance/reject-independent-jobs-digest-mismatch")]
    [TestCase("total", TestName = "coverage-ci-provenance/reject-incomplete-job-pagination")]
    [TestCase("fences", TestName = "coverage-ci-provenance/reject-missing-attempt-start-fence")]
    [TestCase("timezone", TestName = "coverage-ci-provenance/reject-non-utc-attempt-start")]
    [TestCase("head", TestName = "coverage-ci-provenance/reject-foreign-api-head")]
    [TestCase("required-step", TestName = "coverage-ci-provenance/reject-missing-required-successful-step")]
    public void RawAttemptWitnessRetainsItsExistingIndependentBoundaries(string mutation)
    {
        var scenario = Create();
        var jobs = scenario.Input.Jobs;
        switch (mutation)
        {
            case "jobs-digest": scenario = scenario with { Input = scenario.Input with { JobsSha256 = Sha("other-jobs") } }; break;
            case "total": scenario = scenario with { Input = scenario.Input with { Jobs = jobs with { TotalCount = jobs.TotalCount + 1 } } }; break;
            case "fences": scenario = scenario with { Input = scenario.Input with { Jobs = jobs with { Attempts = [] } } }; break;
            case "timezone": scenario = scenario with { Input = scenario.Input with { Jobs = jobs with { Attempts = [new(1, "2026-10-31T08:00:00+08:00")] } } }; break;
            case "head": scenario = ReplaceJob(scenario, 1, jobs.Jobs[1] with { HeadSha = new('c', 40) }); break;
            default: scenario = ReplaceJob(scenario, 1, jobs.Jobs[1] with { Steps = jobs.Jobs[1].Steps!.Skip(1).ToArray() }); break;
        }
        Reject(scenario, mutation switch
        {
            "jobs-digest" => "jobs-digest", "total" => "jobs-snapshot", "fences" => "attempt-fences", "timezone" => "invalid-time",
            "head" => "execution-sha", _ => "required-step",
        });
    }

    [TestCase("event", TestName = "coverage-ci-provenance/reject-pr-context-for-main-owner-selection")]
    [TestCase("ref", TestName = "coverage-ci-provenance/reject-tag-context-for-main-owner-selection")]
    [TestCase("concurrency", TestName = "coverage-ci-provenance/reject-concurrency-above-four")]
    public void CallerContextIsExplicitlyMainOnlyAndBounded(string mutation)
    {
        var scenario = Create();
        scenario = scenario with { Context = mutation switch
        {
            "event" => scenario.Context with { EventName = "pull_request" },
            "ref" => scenario.Context with { Ref = "refs/tags/v1.0.0" },
            _ => scenario.Context with { MaximumConcurrentMachines = 5 },
        } };
        Reject(scenario, "context");
    }

    private sealed record Scenario(FullCoverageCiContext Context, FullCoverageCiInput Input);
    private static Scenario Create(int shardCount = 2, int currentAttempt = 1)
    {
        var commit = new string('a', 40);
        var tree = new string('b', 40);
        const string root = "C:\\fish-ci-fixture\\run-901";
        var preparedRoot = Path.Combine(root, "prepare", "attempt-1");
        var source = Path.Combine(preparedRoot, "source");
        var prepared = new FullCoveragePreparedExpectation(1, Path.Combine(preparedRoot, "preparation", "prepared.json"), Sha("prepared"),
            source, Path.Combine(preparedRoot, "snapshot-context.json"), Path.Combine(source, "tools", "FishAudio.Testing.dll"),
            "C:\\runtime\\dotnet.exe", "C:\\runtime\\pwsh.exe", "C:\\runtime\\dotCover.dll");
        var preparation = new FullCoverageCiPreparationIdentity(new(commit, tree, Sha("source")), prepared, new('1', 32),
            Sha("context"), Sha("metadata-tool"), Sha("build-authority"), Sha("dotcover"), Sha("vstest"),
            "10.0.403", "Release", "net10.0-windows", CoverageInstrumentationPolicy.CurrentVersion);
        var roles = new List<FullCoverageCiRoleExpectation> {
            new(FullCoverageCiRole.Prepare, null, "Fixture coverage prepare", ["Prepare immutable inputs", "Record owner"], "preparation/prepared.json") };
        roles.AddRange(Enumerable.Range(0, shardCount).Select(index => new FullCoverageCiRoleExpectation(FullCoverageCiRole.Shard, index,
            "Fixture coverage shard " + index.ToString("D6", CultureInfo.InvariantCulture), ["Collect selected owners", "Admit raw owners", "Record owner"], "shard.json")));
        var context = new FullCoverageCiContext(1, "owner/repository", "push", "refs/heads/main", commit, tree, "Validation", 901,
            currentAttempt, root, Sha("complete-jobs-snapshot"), preparation, shardCount, 4, roles);
        var jobs = roles.Select((role, index) => Job(role, 1, index == 0 ? 101 : 199 + index)).ToArray();
        var artifacts = roles.Select((role, index) => Artifact(context, role, 1, jobs[index].Id, 101, 1)).ToArray();
        var fences = Enumerable.Range(1, currentAttempt).Select(number => new FullCoverageCiAttemptFence(number, Time(number, 0))).ToArray();
        return new(context, new(1, new(context.Repository, context.RunId, "all", jobs.Length, jobs, fences), context.JobsSha256, artifacts));
    }

    private static FullCoverageCiJob Job(FullCoverageCiRoleExpectation role, int attempt, long id)
    {
        var start = Time(attempt, role.Role == FullCoverageCiRole.Prepare ? 1 : 3 + role.ShardIndex!.Value);
        var end = Time(attempt, role.Role == FullCoverageCiRole.Prepare ? 2 : 4 + role.ShardIndex!.Value);
        return new(id, 901, attempt, role.JobName, new('a', 40), "completed", "success", start, end,
            10 + (role.ShardIndex ?? -1), "fixture-runner-" + role.Role + role.ShardIndex,
            role.RequiredSteps.Select((name, index) => new FullCoverageCiStep(index + 1, name, "completed", "success", start, end)).ToArray());
    }

    private static FullCoverageCiArtifact Artifact(FullCoverageCiContext context, FullCoverageCiRoleExpectation role,
        int attempt, long jobId, long preparationJobId, int preparationAttempt)
    {
        var files = new[] { new FullCoverageCiFile(role.EvidenceRelativePath, 10,
            role.Role == FullCoverageCiRole.Prepare ? context.Preparation.Prepared.PreparedSha256 : Sha("shard-manifest-" + role.ShardIndex)) };
        var inventory = new FullCoverageCiInventory(role.Role == FullCoverageCiRole.Prepare ? ["preparation"] : [], files);
        var key = role.Role == FullCoverageCiRole.Prepare ? "prepare" : "shard-" + role.ShardIndex!.Value.ToString("D6", CultureInfo.InvariantCulture);
        var archiveSha256 = Sha("archive-" + key + "-" + attempt);
        var owner = new FullCoverageCiOwner(1, "coverage-ci-operation-owner", role.Role, role.ShardIndex, context.ShardCount,
            context.Repository, context.Commit, context.Tree, context.WorkflowName, context.RunId, attempt, jobId,
            Time(attempt, role.Role == FullCoverageCiRole.Prepare ? 2 : 4 + role.ShardIndex!.Value),
            preparationJobId, preparationAttempt, context.Preparation,
            Path.Combine(context.OperationRoot, key, "attempt-" + attempt.ToString(CultureInfo.InvariantCulture)),
            "coverage-" + key + "-" + context.Commit + "-" + context.RunId + "-" + attempt, archiveSha256, files[0].Sha256, inventory);
        return new(owner, Sha("owner-record-" + key + "-" + attempt), archiveSha256,
            new(inventory.Directories.ToArray(), inventory.Files.Select(file => file with { }).ToArray()));
    }

    private static Scenario AddActual(Scenario scenario, FullCoverageCiRole role, int? index, int attempt,
        long jobId, long preparationJobId, int preparationAttempt)
    {
        var expected = scenario.Context.Roles.Single(item => item.Role == role && item.ShardIndex == index);
        return AddArtifacts(AddJobs(scenario, Job(expected, attempt, jobId)),
            Artifact(scenario.Context, expected, attempt, jobId, preparationJobId, preparationAttempt));
    }
    private static Scenario RebindPreparation(Scenario scenario, int attempt, bool preserveSeals)
    {
        var root = Path.Combine(scenario.Context.OperationRoot, "prepare", "attempt-" + attempt.ToString(CultureInfo.InvariantCulture));
        var source = Path.Combine(root, "source");
        var binding = scenario.Context.Preparation;
        var prepared = binding.Prepared with { PreparedPath = Path.Combine(root, "preparation", "prepared.json"),
            SourceDirectory = source, SnapshotContextPath = Path.Combine(root, "snapshot-context.json"),
            MetadataToolFile = Path.Combine(source, "tools", "FishAudio.Testing.dll"),
            PreparedSha256 = preserveSeals ? binding.Prepared.PreparedSha256 : Sha("prepared-attempt-" + attempt) };
        return scenario with { Context = scenario.Context with { Preparation = binding with { Prepared = prepared,
            RunId = preserveSeals ? binding.RunId : new string('2', 32),
            SnapshotContextSha256 = preserveSeals ? binding.SnapshotContextSha256 : Sha("context-attempt-" + attempt),
            BuildAuthoritySha256 = preserveSeals ? binding.BuildAuthoritySha256 : Sha("build-attempt-" + attempt) } } };
    }
    private static Scenario AddJobs(Scenario scenario, params FullCoverageCiJob[] additions)
    {
        var jobs = scenario.Input.Jobs.Jobs.Concat(additions).ToArray();
        return scenario with { Input = scenario.Input with { Jobs = scenario.Input.Jobs with { Jobs = jobs, TotalCount = jobs.Length } } };
    }
    private static Scenario AddArtifacts(Scenario scenario, params FullCoverageCiArtifact[] additions) =>
        scenario with { Input = scenario.Input with { Artifacts = scenario.Input.Artifacts.Concat(additions).ToArray() } };
    private static Scenario ReplaceArtifact(Scenario scenario, FullCoverageCiArtifact old, FullCoverageCiArtifact changed) =>
        scenario with { Input = scenario.Input with { Artifacts = scenario.Input.Artifacts.Select(item => ReferenceEquals(item, old) ? changed : item).ToArray() } };
    private static Scenario ReplaceJob(Scenario scenario, int index, FullCoverageCiJob changed) =>
        scenario with { Input = scenario.Input with { Jobs = scenario.Input.Jobs with {
            Jobs = scenario.Input.Jobs.Jobs.Select((job, ordinal) => ordinal == index ? changed : job).ToArray() } } };
    private static string Sha(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Time(int attempt, int minute) => new DateTimeOffset(2026, 10, 31, attempt - 1, 0, 0, TimeSpan.Zero)
        .AddMinutes(minute).ToString("O", CultureInfo.InvariantCulture);
    private static void Reject(Scenario scenario, string code) => Assert.That(
        Assert.Throws<FullCoverageCiProvenanceException>(() => FullCoverageCiProvenance.Select(scenario.Input, scenario.Context))!.Code,
        Is.EqualTo("full-coverage-ci-" + code));
}
