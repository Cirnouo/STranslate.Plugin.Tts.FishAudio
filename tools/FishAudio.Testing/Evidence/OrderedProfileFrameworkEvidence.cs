using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using FishAudio.Testing.Execution;

namespace FishAudio.Testing.Evidence;

public sealed record OrderedProfileCoverageControlEvent(int SchemaVersion, string Role, string Nonce,
    string RunId, string? SourceTree, int Pid, string Kind, long Timestamp, long Frequency,
    string? AssemblySha256 = null, string? CaseId = null, string? FullName = null,
    int Ordinal = 0, long QueuedTimestamp = 0, string? Outcome = null,
    int? TargetPid = null, int? ExitCode = null);

public sealed record OrderedProfileFrameworkEvidenceRecord(int HostProcessId,
    FrameworkWatchdogState State, IReadOnlyList<OrderedProfileCoverageControlEvent> RawControlEvents);

// Replays the original standard framework/root watchdog, without inventing
// per-case profiler attribution or treating predecessor cases as setup.
public static class OrderedProfileFrameworkEvidence
{
    public static OrderedProfileFrameworkEvidenceRecord ReadValidated(JsonElement root, FrameworkRunRequest request,
        FrameworkClientContext context, OrderedProfileCoveragePlanRecord plan, string requestSha256,
        string invocationSha256, string resultSha256, string trxSha256)
    {
        try
        {
            var collection = request.OrderedProfiledLaunch;
            Require(collection is not null && request.ProfiledLaunch is null &&
                collection.Profile == plan.Profile && collection.RawDiscoverySha256 == plan.RawDiscoverySha256 &&
                request.MaximumConcurrency == 1 && request.RequireOrder &&
                request.Cases.SequenceEqual(plan.Members.Select(item => new FrameworkCaseBinding(item.Case.CanonicalId, item.Case.FullName))),
                "request-selection");
            Require(root.GetProperty("schemaVersion").GetInt32() == 1 &&
                root.GetProperty("kind").GetString() == "framework-execution" &&
                root.GetProperty("runId").GetString() == request.RunId &&
                root.GetProperty("sourceTree").GetString() == request.SourceTree &&
                root.GetProperty("sourceFingerprint").GetString() == request.SourceFingerprint &&
                root.GetProperty("testAssemblySha256").GetString() == request.TestAssemblySha256 &&
                root.GetProperty("requestSha256").GetString() == requestSha256 &&
                !root.GetProperty("baselineClaim").GetBoolean() &&
                root.GetProperty("failureCode").ValueKind == JsonValueKind.Null, "supervisor-identity");
            RequireIds(root.GetProperty("selectedIds"), request.Cases.Select(item => item.CanonicalId));
            var policy = root.GetProperty("policy");
            Require(policy.GetProperty("maximumConcurrency").GetInt32() == 1 &&
                policy.GetProperty("requireOrder").GetBoolean(), "policy");
            var target = root.GetProperty("target");
            var process = root.GetProperty("process");
            var targetPid = target.GetProperty("pid").GetInt32();
            var launcherPid = process.GetProperty("launcherPid").GetInt32();
            Require(target.GetProperty("started").GetBoolean() && targetPid > 0 &&
                target.GetProperty("exitCode").GetInt32() == 0 && launcherPid > 0 && launcherPid != targetPid &&
                process.GetProperty("launcherExited").GetBoolean() && process.GetProperty("stdoutDrained").GetBoolean() &&
                process.GetProperty("stderrDrained").GetBoolean() && process.GetProperty("ownedActiveProcesses").GetInt32() == 0,
                "target-or-drains-incomplete");
            var raw = root.GetProperty("rawEvidence");
            Require(raw.GetProperty("verified").GetBoolean() &&
                Same(raw.GetProperty("resultXmlPath").GetString(), request.ResultXmlPath) &&
                Same(raw.GetProperty("trxPath").GetString(), request.TrxPath) &&
                raw.GetProperty("resultXmlSha256").GetString() == resultSha256 &&
                raw.GetProperty("trxSha256").GetString() == trxSha256, "raw-results");
            var launch = root.GetProperty("launch");
            Require(CoverageInstrumentationPolicy.RecordedVersionMatches(launch, collection.Collection.InstrumentationPolicyVersion) &&
                launch.GetProperty("kind").GetString() == "dotcover-sdk-vstest-ordered-profile" &&
                launch.GetProperty("sessionId").GetString() == collection.Collection.SessionId &&
                launch.GetProperty("profile").GetString() == plan.Profile &&
                launch.GetProperty("rawDiscoverySha256").GetString() == plan.RawDiscoverySha256 &&
                launch.GetProperty("invocationSha256").GetString() == invocationSha256 &&
                launch.GetProperty("toolSha256").GetString() == collection.Collection.ToolSha256 &&
                launch.GetProperty("vstestConsoleSha256").GetString() == collection.Collection.VstestConsoleSha256,
                "launch");
            var clock = root.GetProperty("orderedProfileClock");
            var started = clock.GetProperty("startedTimestamp").GetInt64();
            var exited = clock.GetProperty("completedTimestamp").GetInt64();
            var frequency = root.GetProperty("finalization").GetProperty("frequency").GetInt64();
            var accepted = root.GetProperty("finalization").GetProperty("cleanupAnchorTimestamp").GetInt64();
            var records = root.GetProperty("events").EnumerateArray().Select(item =>
                item.Deserialize<OrderedProfileCoverageControlEvent>(FrameworkExecutionDriver.JsonOptions)
                    ?? throw new InvalidDataException("ordered-profile-group-null-event")).ToArray();
            var evidence = ReplayValidated(request, context, records, started, frequency, accepted, exited, launcherPid, targetPid, plan);
            var state = root.GetProperty("state");
            Require(state.GetProperty("complete").GetBoolean() && state.GetProperty("stage").GetString() == "child-exit" &&
                state.GetProperty("failureStage").ValueKind == JsonValueKind.Null &&
                state.GetProperty("failureCaseId").ValueKind == JsonValueKind.Null &&
                !state.GetProperty("terminationRequired").GetBoolean(), "state");
            RequireIds(state.GetProperty("passedIds"), request.Cases.Select(item => item.CanonicalId));
            foreach (var name in new[] { "failedIds", "timedOutIds", "activeIds", "interruptedIds", "notRunIds" })
                RequireIds(state.GetProperty(name), []);
            return evidence;
        }
        catch (OrderedProfileCoverageException) { throw; }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or
            ArgumentException or InvalidDataException or OverflowException)
        { throw new OrderedProfileCoverageException("ordered-profile-coverage-framework-evidence-rejected"); }
    }

    public static OrderedProfileFrameworkEvidenceRecord ReplayValidated(FrameworkRunRequest request,
        FrameworkClientContext context, IReadOnlyList<OrderedProfileCoverageControlEvent> records,
        long started, long frequency, long accepted, long exited, int launcherPid, int targetPid) =>
        ReplayValidated(request, context, records, started, frequency, accepted, exited, launcherPid, targetPid, null);

    public static OrderedProfileFrameworkEvidenceRecord ReplayValidated(FrameworkRunRequest request,
        FrameworkClientContext context, IReadOnlyList<OrderedProfileCoverageControlEvent> records,
        long started, long frequency, long accepted, long exited, int launcherPid, int targetPid,
        OrderedProfileCoveragePlanRecord? plan)
    {
        try
        {
            Require(request is not null && context is not null && request.MaximumConcurrency == 1 && request.RequireOrder, "ordered-policy");
            Require(records is { Count: >= 6 and <= 1_000_000 } && started >= 0 && frequency is > 0 and <= 1_000_000_000_000 &&
                exited >= accepted && accepted >= started && launcherPid > 0 && targetPid > 0 && launcherPid != targetPid,
                "clock");
            Require(context.SchemaVersion == 1 && context.RunId == request.RunId && context.SourceTree == request.SourceTree &&
                Same(context.TestAssembly, request.TestAssembly) && context.TestAssemblySha256 == request.TestAssemblySha256 &&
                context.Cases.SequenceEqual(request.Cases) && IsHash(context.Nonce) &&
                context.PipeName is { Length: 43 } && context.PipeName.StartsWith("fa-fw-", StringComparison.Ordinal) &&
                context.PipeName.EndsWith("-host", StringComparison.Ordinal) &&
                context.PipeName[6..38].All(char.IsAsciiHexDigit), "context");
            Require(plan is null || plan.Members is { Count: > 0 } &&
                plan.Members.All(item => item is not null && item.Case is not null) &&
                request.ProfiledLaunch is null && request.OrderedProfiledLaunch is { } ordered &&
                ordered.Profile == plan.Profile && ordered.RawDiscoverySha256 == plan.RawDiscoverySha256 &&
                plan.Members.Select(item => item.Ordinal).SequenceEqual(Enumerable.Range(0, plan.Members.Count)) &&
                request.Cases.SequenceEqual(plan.Members.Select(item => new FrameworkCaseBinding(item.Case.CanonicalId, item.Case.FullName))),
                "ordered-plan-binding");
            // A verified general profile may publish only callback lifecycle
            // events. Every observed action still goes through the original
            // watchdog; native profiles and the old plan-less API need roots.
            var requireQueuedRoots = plan?.RequiresQueuedRootActions ?? true;
            var bindings = request.Cases.ToDictionary(item => item.CanonicalId, item => item.FullName, StringComparer.Ordinal);
            var watchdog = new FrameworkWatchdog(request.Cases.Select(item => item.CanonicalId).ToArray(),
                started, frequency, maximumConcurrency: 1, requireOrder: true);
            var launcherKinds = new List<string>();
            var queued = new HashSet<string>(StringComparer.Ordinal);
            var hostPid = 0;
            long? publication = null;
            long lastLauncher = started;
            foreach (var record in records)
            {
                Require(record is not null && record.SchemaVersion == 1 && record.Nonce == context.Nonce &&
                    record.RunId == request.RunId && record.SourceTree == request.SourceTree && record.Frequency == frequency &&
                    record.Timestamp >= started && record.Timestamp <= exited && record.QueuedTimestamp >= 0 &&
                    record.QueuedTimestamp <= record.Timestamp, "event-identity");
                if (record.Role == "execution-launcher")
                {
                    Require(record.Pid == launcherPid && record.TargetPid == targetPid && record.Timestamp >= lastLauncher &&
                        record.AssemblySha256 is null && record.CaseId is null && record.FullName is null &&
                        record.Ordinal == 0 && record.QueuedTimestamp == 0 && record.Outcome is null &&
                        (record.Kind == "target-started" && record.ExitCode is null ||
                         record.Kind == "target-exited" && record.ExitCode == 0), "launcher-event");
                    launcherKinds.Add(record.Kind);
                    lastLauncher = record.Timestamp;
                    if (record.Kind == "target-exited") publication = record.Timestamp;
                    continue;
                }
                Require(record.Role == "nunit-host" && record.Pid > 0 && record.Pid != targetPid && record.Pid != launcherPid &&
                    record.AssemblySha256 == request.TestAssemblySha256 && record.TargetPid is null && record.ExitCode is null,
                    "host-event");
                if (hostPid == 0) hostPid = record.Pid;
                Require(record.Pid == hostPid, "host-pid");
                if (record.CaseId is null) Require(record.FullName is null, "host-case");
                else Require(bindings.TryGetValue(record.CaseId, out var fullName) && fullName == record.FullName, "host-case");
                if (record.Kind == "queued") queued.Add(record.CaseId!);
                watchdog.Observe(new(record.Kind, record.Timestamp, record.Frequency, record.CaseId,
                    record.Ordinal, record.QueuedTimestamp, record.Outcome));
            }
            Require(launcherKinds.SequenceEqual(new[] { "target-started", "target-exited" }, StringComparer.Ordinal) &&
                publication is not null && hostPid > 0 && (!requireQueuedRoots || queued.SetEquals(bindings.Keys)), "complete-native-roots");
            watchdog.Observe(new("final-published", publication!.Value, frequency));
            watchdog.AcceptFinal(accepted);
            watchdog.HostExited(exited, true, true, true);
            Require(watchdog.State.Complete && !watchdog.State.TerminationRequired &&
                watchdog.State.PassedIds.SequenceEqual(request.Cases.Select(item => item.CanonicalId), StringComparer.Ordinal),
                "root-or-finalization-incomplete");
            return new(hostPid, watchdog.State, Array.AsReadOnly(records.ToArray()));
        }
        catch (OrderedProfileCoverageException) { throw; }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException or OverflowException)
        { throw new OrderedProfileCoverageException("ordered-profile-coverage-control-events-rejected"); }
    }

    private static void RequireIds(JsonElement element, IEnumerable<string> expected) =>
        Require(element.ValueKind == JsonValueKind.Array &&
            element.EnumerateArray().Select(item => item.GetString()).SequenceEqual(expected), "terminal-id-order");

    private static bool IsHash(string? value) => value is not null && value.Length == 64 && value.Any(item => item != '0') &&
        value.All(item => item is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool Same(string? left, string? right) => left is not null && right is not null &&
        Path.IsPathFullyQualified(left) && Path.IsPathFullyQualified(right) &&
        Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static void Require([DoesNotReturnIf(false)] bool value, string code)
    {
        if (!value) throw new OrderedProfileCoverageException("ordered-profile-coverage-" + code);
    }
}
