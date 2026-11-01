using System.Text;
using System.Text.Json;
using FishAudio.Testing.Evidence;
using FishAudio.Testing.Execution;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

[TestFixture]
public sealed class OrderedProfileFrameworkEvidenceTests
{
    [TestCase("missing-root", TestName = "ordered-group/reject-member-without-original-root")]
    [TestCase("reordered", TestName = "ordered-group/reject-runtime-case-order")]
    [TestCase("expired-root", TestName = "ordered-group/reject-original-root-hard-expiry")]
    [TestCase("late-exit", TestName = "ordered-group/reject-late-shared-finalization")]
    [TestCase("foreign-nonce", TestName = "ordered-group/reject-foreign-control-events")]
    public void IncompleteOrForeignNativeExecutionCannotBecomeGroupEvidence(string fault)
    {
        var (request, context) = Request();
        var records = Events(request, context).ToList();
        if (fault == "missing-root") records.RemoveAll(item => item.CaseId == "group/cold" && item.Ordinal == 1);
        if (fault == "reordered")
            records = Events(request with { Cases = request.Cases.Reverse().ToArray() }, context).ToList();
        if (fault == "expired-root")
        {
            var index = records.FindIndex(item => item.Kind == "completed");
            records[index] = records[index] with { Timestamp = 121_000 };
        }
        if (fault == "foreign-nonce") records[1] = records[1] with { Nonce = new string('e', 64) };
        Assert.Throws<OrderedProfileCoverageException>(() =>
            OrderedProfileFrameworkEvidence.ReplayValidated(request, context, records,
                0, 1000, 700, fault == "late-exit" ? 10_700 : fault == "expired-root" ? 122_000 : 800, 100, 101));
    }

    [Test]
    public void CompleteTwoMemberRootLifecycleIsAdmittedOnlyAsAGroupEnvelope()
    {
        var (request, context) = Request();
        var evidence = OrderedProfileFrameworkEvidence.ReplayValidated(request, context, Events(request, context),
            0, 1000, 700, 800, 100, 101);
        Assert.That(evidence.State.Complete, Is.True);
        Assert.That(evidence.State.PassedIds, Is.EqualTo(new[] { "group/cold", "group/later" }));
        Assert.That(evidence.HostProcessId, Is.EqualTo(102));
    }

    [TestCase(false, TestName = "ordered-group/general-profile-with-no-dispatched-roots-is-supervised")]
    [TestCase(true, TestName = "ordered-group/general-profile-observed-roots-remain-supervised")]
    public void GeneralProfileRetainsOneHostOrderedCallbacksAndActualRootEvents(bool includeRoots)
    {
        WithGeneralPlan((request, context, plan) =>
        {
            var records = Events(request, context).Where(item => includeRoots || item.Kind is not ("queued" or "entered" or "completed")).ToArray();
            var evidence = OrderedProfileFrameworkEvidence.ReplayValidated(request, context, records,
                0, 1000, 900, 1000, 100, 101, plan);
            Assert.That(evidence.State.Complete, Is.True);
            Assert.That(evidence.State.PassedIds, Is.EqualTo(plan.Members.Select(item => item.Case.CanonicalId)));
            Assert.That(evidence.HostProcessId, Is.EqualTo(102));
            Assert.That(evidence.RawControlEvents.Count(item => item.Kind == "queued"), Is.EqualTo(includeRoots ? 4 : 0));
            Assert.That(evidence.RawControlEvents.Count(item => item.Kind == "case-start"), Is.EqualTo(4));
        });
    }

    [TestCase("order", TestName = "ordered-group/general-profile-still-rejects-callback-reorder")]
    [TestCase("host", TestName = "ordered-group/general-profile-still-rejects-multiple-hosts")]
    [TestCase("unfinished-root", TestName = "ordered-group/general-profile-still-rejects-unfinished-observed-root")]
    [TestCase("expired-case", TestName = "ordered-group/general-profile-with-no-roots-still-rejects-case-hard-expiry")]
    [TestCase("missing-finish", TestName = "ordered-group/general-profile-still-rejects-missing-callback-finish")]
    [TestCase("plan-mismatch", TestName = "ordered-group/general-profile-rejects-unrelated-plan-context")]
    public void GeneralProfileSupportDoesNotWaiveWatchdogOrValidatedPlanRelationships(string fault)
    {
        WithGeneralPlan((request, context, plan) =>
        {
            var records = Events(request, context).ToList();
            long accepted = 900, exited = 1000;
            if (fault == "order") records = Events(request with { Cases = request.Cases.Reverse().ToArray() }, context).ToList();
            if (fault == "host")
            {
                var at = records.FindIndex(item => item.CaseId == request.Cases[1].CanonicalId);
                records[at] = records[at] with { Pid = 103 };
            }
            if (fault == "unfinished-root")
                records.RemoveAt(records.FindIndex(item => item.Kind == "completed"));
            if (fault == "expired-case")
            {
                records.RemoveAll(item => item.Kind is "queued" or "entered" or "completed");
                var start = records.FindIndex(item => item.Kind == "case-finished");
                for (var index = start; index < records.Count; index++) records[index] = records[index] with { Timestamp = records[index].Timestamp + 121_000 };
                accepted += 121_000;
                exited += 121_000;
            }
            if (fault == "missing-finish") records.RemoveAt(records.FindIndex(item => item.Kind == "case-finished"));
            if (fault == "plan-mismatch") plan = plan with { Profile = "unrelated.profile" };
            Assert.Throws<OrderedProfileCoverageException>(() => OrderedProfileFrameworkEvidence.ReplayValidated(request, context,
                records, 0, 1000, accepted, exited, 100, 101, plan));
        });
    }

    [Test]
    [Property("FishAudioCaseId", "ordered-group/planless-replay-retains-native-root-requirement")]
    public void ARequestLabelAloneCannotDisableTheLegacyRootEvidenceRequirement()
    {
        WithGeneralPlan((request, context, _) =>
        {
            var records = Events(request, context).Where(item => item.Kind is not ("queued" or "entered" or "completed")).ToArray();
            Assert.Throws<OrderedProfileCoverageException>(() => OrderedProfileFrameworkEvidence.ReplayValidated(request, context,
                records, 0, 1000, 900, 1000, 100, 101));
        });
    }

    [Test]
    [Property("FishAudioCaseId", "ordered-group/general-supervisor-binds-zero-root-callback-envelope")]
    public void GeneralSupervisorUsesTheValidatedPlanWithoutInventingQueuedActions()
    {
        WithGeneralPlan((request, context, plan) =>
        {
            var ids = request.Cases.Select(item => item.CanonicalId).ToArray();
            var launch = request.OrderedProfiledLaunch!.Collection;
            var records = Events(request, context).Where(item => item.Kind is not ("queued" or "entered" or "completed")).ToArray();
            var requestSha = new string('1', 64);
            var invocationSha = new string('2', 64);
            var resultSha = new string('3', 64);
            var trxSha = new string('4', 64);
            var json = JsonSerializer.Serialize(new
            {
                schemaVersion = 1, kind = "framework-execution", runId = request.RunId, sourceTree = request.SourceTree,
                sourceFingerprint = request.SourceFingerprint, testAssemblySha256 = request.TestAssemblySha256,
                requestSha256 = requestSha, baselineClaim = false, failureCode = (string?)null, selectedIds = ids,
                policy = new { maximumConcurrency = 1, requireOrder = true },
                state = new { complete = true, stage = "child-exit", failureStage = (string?)null, failureCaseId = (string?)null,
                    terminationRequired = false, passedIds = ids, failedIds = Array.Empty<string>(), timedOutIds = Array.Empty<string>(),
                    activeIds = Array.Empty<string>(), interruptedIds = Array.Empty<string>(), notRunIds = Array.Empty<string>() },
                target = new { started = true, pid = 101, exitCode = 0 },
                process = new { launcherPid = 100, launcherExited = true, stdoutDrained = true, stderrDrained = true, ownedActiveProcesses = 0 },
                rawEvidence = new { verified = true, resultXmlPath = request.ResultXmlPath, trxPath = request.TrxPath,
                    resultXmlSha256 = resultSha, trxSha256 = trxSha },
                launch = new { kind = "dotcover-sdk-vstest-ordered-profile", sessionId = launch.SessionId, profile = plan.Profile,
                    rawDiscoverySha256 = plan.RawDiscoverySha256, invocationSha256 = invocationSha,
                    toolSha256 = launch.ToolSha256, vstestConsoleSha256 = launch.VstestConsoleSha256 },
                orderedProfileClock = new { startedTimestamp = 0, completedTimestamp = 1000 },
                finalization = new { frequency = 1000, cleanupAnchorTimestamp = 900 },
                events = records,
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            using var document = JsonDocument.Parse(json);
            var evidence = OrderedProfileFrameworkEvidence.ReadValidated(document.RootElement, request, context, plan,
                requestSha, invocationSha, resultSha, trxSha);
            Assert.That(evidence.State.Complete, Is.True);
            Assert.That(evidence.State.PassedIds, Is.EqualTo(ids));
            Assert.That(evidence.RawControlEvents.Any(item => item.Kind == "queued"), Is.False);
        });
    }
    private static void WithGeneralPlan(Action<FrameworkRunRequest, FrameworkClientContext, OrderedProfileCoveragePlanRecord> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "fa-declared-profile-events-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var discovery = Path.Combine(root, "discovery.dump");
        try
        {
            File.WriteAllText(discovery, OrderedProfileCoveragePlanTests.GeneralDiscovery(), new UTF8Encoding(false));
            var plan = OrderedProfileCoveragePlan.ReadValidated(discovery, "compat.ordered-profile");
            var (request, context) = Request();
            var cases = plan.Members.Select(item => new FrameworkCaseBinding(item.Case.CanonicalId, item.Case.FullName)).ToArray();
            var inert = new DotCoverSdkVstestLaunch(new string('a', 32), request.TestAssembly, new string('e', 64),
                request.TestAssembly, new string('e', 64), request.TestAssembly, request.TestAssembly);
            request = request with { Cases = cases, DiscoveryPath = discovery,
                OrderedProfiledLaunch = new(plan.Profile, plan.RawDiscoverySha256, inert) };
            context = context with { Cases = cases };
            body(request, context, plan);
        }
        finally { if (File.Exists(discovery)) File.Delete(discovery); Directory.Delete(root); }
    }
    private static (FrameworkRunRequest Request, FrameworkClientContext Context) Request()
    {
        var path = Path.Combine(Path.GetTempPath(), "ordered-profile-evidence.fixture");
        var cases = new[] { new FrameworkCaseBinding("group/cold", "Cases.Cold"), new FrameworkCaseBinding("group/later", "Cases.Later") };
        var request = new FrameworkRunRequest(1, new string('a', 32), new string('b', 40),
            Path.GetTempPath(), path, path, path, path, new string('c', 64),
            path, path, path, path, path, cases, 1, true, SourceFingerprint: new string('d', 64));
        var context = new FrameworkClientContext(1, request.RunId, request.SourceTree, new string('f', 64),
            "fa-fw-" + new string('a', 32) + "-host", path, request.TestAssemblySha256, cases);
        return (request, context);
    }

    private static OrderedProfileCoverageControlEvent[] Events(FrameworkRunRequest request, FrameworkClientContext context)
    {
        OrderedProfileCoverageControlEvent Host(string kind, long at, FrameworkCaseBinding? item = null,
            int ordinal = 0, long queued = 0, string? outcome = null) =>
            new(1, "nunit-host", context.Nonce, request.RunId, request.SourceTree, 102, kind, at, 1000,
                request.TestAssemblySha256, item?.CanonicalId, item?.FullName, ordinal, queued, outcome);
        OrderedProfileCoverageControlEvent Launcher(string kind, long at, int? exitCode) =>
            new(1, "execution-launcher", context.Nonce, request.RunId, request.SourceTree, 100, kind, at, 1000,
                TargetPid: 101, ExitCode: exitCode);
        var result = new List<OrderedProfileCoverageControlEvent> { Launcher("target-started", 100, null), Host("ready", 200) };
        for (var index = 0; index < request.Cases.Count; index++)
        {
            var item = request.Cases[index];
            var at = 300 + 100 * index;
            result.Add(Host("case-start", at, item));
            result.Add(Host("queued", at + 10, item, 1, at + 10));
            result.Add(Host("entered", at + 20, item, 1, at + 10));
            result.Add(Host("completed", at + 30, item, 1, at + 10));
            result.Add(Host("case-finished", at + 40, item, outcome: "passed"));
        }
        var cleanup = 300 + 100 * request.Cases.Count;
        result.Add(Host("cleanup-complete", cleanup));
        result.Add(Launcher("target-exited", cleanup + 100, 0));
        return result.ToArray();
    }
}
