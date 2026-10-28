using System.Text.Json;
using FishAudio.Testing.Execution;

namespace FishAudio.Testing.Evidence;

// These supplemental seals belong to the independently supplied supervisor
// receipt. They do not extend the historical fifteen-artifact profiler schema.
public sealed record CoverageFrameworkEnvelope(int SchemaVersion,
    CoverageOwnedProcessEvidence DriverInvocation, CoverageOwnedProcessEvidence DriverStdout,
    CoverageOwnedProcessEvidence DriverStderr, CoverageOwnedProcessEvidence ExecutionRequest,
    CoverageOwnedProcessEvidence RunSettings, CoverageOwnedProcessEvidence ControlContext,
    CoverageOwnedProcessEvidence Supervisor, CoverageOwnedProcessEvidence HostStart, CoverageOwnedProcessEvidence HostEnd,
    string DriverAssemblyRelativePath, string DriverAssemblySha256);

public static partial class IsolatedCoverageSession
{
    private static void ValidateFrameworkEnvelope(CoverageFrameworkEnvelope envelope,
        IsolatedCoverageAdmission expected, string artifactRoot, string buildRoot, string sourceRoot,
        string testDll, IReadOnlyDictionary<CoverageArtifactKind, string> paths,
        HashSet<string> relativePaths, HashSet<string> binaryPaths,
        IReadOnlyList<DiscoveredNUnitCase> catalog, List<FileStream> locks)
    {
        Require(envelope.SchemaVersion == 1, "framework-envelope-schema");
        var pins = new[] { envelope.DriverInvocation, envelope.DriverStdout, envelope.DriverStderr,
            envelope.ExecutionRequest, envelope.RunSettings, envelope.ControlContext, envelope.Supervisor, envelope.HostStart, envelope.HostEnd };
        var occupied = new HashSet<string>(relativePaths.Select(path => Child(artifactRoot, path)),
            StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();
        foreach (var pin in pins)
        {
            Require(pin is not null && IsHash(pin.Sha256) && pin.Length >= 0 && pin.Length <= 64 * 1024 * 1024 &&
                Utc(pin.LastWriteUtc) && pin.LastWriteUtc >= expected.Supervisor.StartedUtc &&
                pin.LastWriteUtc <= expected.Supervisor.Cover.FinishedUtc && relativePaths.Add(pin.RelativePath),
                "framework-envelope-file-identity");
            var path = Child(artifactRoot, pin.RelativePath);
            Require(occupied.Add(path), "framework-envelope-file-alias");
            var stream = OpenAbsolute(path, locks);
            Require(stream.Length == pin.Length && Digest(stream) == pin.Sha256 &&
                new DateTimeOffset(File.GetLastWriteTimeUtc(path)) == pin.LastWriteUtc,
                "framework-envelope-file-mismatch");
            files.Add(path);
        }
        var outerInvocation = files[0];
        var executionRequest = files[3];
        var runSettings = files[4];
        var controlContext = files[5];
        var supervisorPath = files[6];
        var phaseRoot = Path.GetDirectoryName(paths[CoverageArtifactKind.CoverProcess])!;
        Require(SameFrameworkPath(outerInvocation, Path.Combine(phaseRoot, "argv.json")) &&
            SameFrameworkPath(files[1], Path.Combine(phaseRoot, "stdout.log")) &&
            SameFrameworkPath(files[2], Path.Combine(phaseRoot, "stderr.log")) &&
            SameFrameworkPath(paths[CoverageArtifactKind.CoverProcess], Path.Combine(phaseRoot, "process.json")),
            "framework-outer-phase-mismatch");
        using (var phaseDocument = ReadJson(paths[CoverageArtifactKind.CoverProcess]))
        {
            var phase = phaseDocument.RootElement;
            Require(phase.GetProperty("stdoutDrained").GetBoolean() && phase.GetProperty("stderrDrained").GetBoolean() &&
                phase.GetProperty("ownedTreeCompleted").GetBoolean() && !phase.GetProperty("cleanupFailed").GetBoolean() &&
                phase.GetProperty("ownership").GetString() == "windows-job-before-target-go" &&
                phase.GetProperty("rootProcessId").GetInt32() > 0 && phase.GetProperty("metadataTimeoutMilliseconds").GetInt32() == 0 &&
                phase.GetProperty("individualChildExitObservation").GetString() == "not-collected",
                "framework-outer-process-mismatch");
        }
        Require(Path.GetFileName(supervisorPath) == "supervisor.json", "framework-final-supervisor-required");
        Require(IsHash(envelope.DriverAssemblySha256), "framework-driver-identity");
        var driver = Child(buildRoot, envelope.DriverAssemblyRelativePath);
        Require(Path.GetFileName(driver).Equals("FishAudio.Testing.dll", StringComparison.OrdinalIgnoreCase) &&
            !occupied.Contains(driver) && !binaryPaths.Contains(driver), "framework-driver-identity");
        Require(Digest(OpenAbsolute(driver, locks)) == envelope.DriverAssemblySha256, "framework-driver-mismatch");

        using var requestDocument = ReadJson(executionRequest);
        var request = requestDocument.RootElement.Deserialize<FrameworkRunRequest>(FrameworkExecutionDriver.JsonOptions);
        Require(request is not null && request.SchemaVersion == 1 && request.RunId == expected.Identity.RunId &&
            request.SourceTree == expected.Identity.Source.Tree && request.SourceFingerprint == expected.Identity.Source.Fingerprint &&
            SameFrameworkPath(request.WorkingDirectory, sourceRoot) && SameFrameworkPath(request.TestAssembly, testDll) &&
            request.TestAssemblySha256 == expected.Binaries.Single(item => item.Role == CoverageBinaryRole.Tests).ExpectedIdentity.DllSha256 &&
            request.Cases is { Count: 1 } && request.Cases[0] is not null && request.MaximumConcurrency == 1,
            "framework-request-identity-mismatch");
        var selected = catalog.Single(item => item.CanonicalId == expected.Identity.CanonicalId);
        var binding = request.Cases[0];
        Require(binding.CanonicalId == selected.CanonicalId && binding.FullName == selected.FullName,
            "framework-request-case-mismatch");
        var launch = request.ProfiledLaunch;
        Require(launch is not null && launch.SessionId == expected.Identity.SessionId &&
            SameFrameworkPath(launch.ToolAssembly, expected.ToolFile) && launch.ToolSha256 == expected.ToolSha256 &&
            IsHash(launch.VstestConsoleSha256) &&
            string.Equals(Path.GetFileName(launch.ToolAssembly), "dotCover.dll", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetFileName(launch.VstestConsoleAssembly), "vstest.console.dll", StringComparison.OrdinalIgnoreCase),
            "framework-profiled-launch-mismatch");
        // This is live producer admission: the installed SDK input must still
        // exist with its declared bytes. No portable consumer admission is claimed.
        Require(Digest(OpenAbsolute(launch.VstestConsoleAssembly, locks)) == launch.VstestConsoleSha256,
            "framework-vstest-input-mismatch");
        var outputRoot = Path.GetDirectoryName(supervisorPath)!;
        Require(SameFrameworkPath(request.OutputDirectory, outputRoot) &&
            SameFrameworkPath(request.RunSettings, runSettings) &&
            SameFrameworkPath(request.DiscoveryPath, paths[CoverageArtifactKind.Discovery]) &&
            SameFrameworkPath(request.ResultXmlPath, paths[CoverageArtifactKind.NUnitXml]) &&
            SameFrameworkPath(request.TrxPath, paths[CoverageArtifactKind.Trx]) &&
            SameFrameworkPath(launch.SnapshotPath, paths[CoverageArtifactKind.Snapshot]) &&
            SameFrameworkPath(launch.LogPath, paths[CoverageArtifactKind.CoverLog]) &&
            SameFrameworkPath(controlContext, Path.Combine(outputRoot, "control-context.json")) &&
            SameFrameworkPath(paths[CoverageArtifactKind.CoverInvocation], Path.Combine(outputRoot, "invocation.json")) &&
            SameFrameworkPath(paths[CoverageArtifactKind.CoverStdout], Path.Combine(outputRoot, "stdout.bin")) &&
            SameFrameworkPath(paths[CoverageArtifactKind.CoverStderr], Path.Combine(outputRoot, "stderr.bin")),
            "framework-request-artifact-mismatch");
        foreach (var path in new[] { request.ResultXmlPath, request.TrxPath, launch.SnapshotPath, launch.LogPath })
            Require(FrameworkWithin(path, outputRoot), "framework-output-outside-envelope");

        using var contextDocument = ReadJson(controlContext);
        var context = contextDocument.RootElement.Deserialize<FrameworkClientContext>(FrameworkExecutionDriver.JsonOptions);
        Require(context is not null && context.SchemaVersion == 1 && context.RunId == request.RunId &&
            context.SourceTree == request.SourceTree && SameFrameworkPath(context.TestAssembly, testDll) &&
            context.TestAssemblySha256 == request.TestAssemblySha256 && context.Cases is { Count: 1 } &&
            context.Cases[0] == binding && IsHash(context.Nonce) && context.Nonce.Any(character => character != '0') &&
            context.PipeName is { Length: >= 17 and <= 128 } && context.PipeName.StartsWith("fa-fw-", StringComparison.Ordinal) &&
            context.PipeName.EndsWith("-host", StringComparison.Ordinal) &&
            context.PipeName.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'),
            "framework-control-context-mismatch");
        ValidateFrameworkInvocations(expected, request, context, driver, executionRequest, outerInvocation, paths, controlContext);
        NUnitHostLifecycleEvidence host;
        try
        {
            host = NUnitHostEvidence.ReadValidated(files[7], files[8], new(1, request.RunId,
                expected.Identity.SessionId, expected.Identity.CanonicalId, request.SourceTree,
                request.TestAssembly, request.TestAssemblySha256, expected.Supervisor.Cover.StartedUtc,
                expected.Supervisor.Cover.FinishedUtc));
        }
        catch (NUnitHostEvidenceException)
        {
            throw Rejected("framework-host-lifecycle-invalid");
        }
        ValidateFrameworkSupervisor(expected, request, context, supervisorPath, paths, host.ProcessId);
    }

    private static void ValidateFrameworkInvocations(IsolatedCoverageAdmission expected, FrameworkRunRequest request,
        FrameworkClientContext context, string driver, string executionRequest, string outerInvocation,
        IReadOnlyDictionary<CoverageArtifactKind, string> paths, string controlContext)
    {
        using var outerDocument = ReadJson(outerInvocation);
        var outer = outerDocument.RootElement;
        Require(outer.GetProperty("schemaVersion").GetInt32() == 1 &&
            outer.GetProperty("sessionRunId").GetString() == expected.Identity.SessionId &&
            outer.GetProperty("canonicalCaseId").GetString() == expected.Identity.CanonicalId &&
            SameFrameworkPath(outer.GetProperty("executable").GetString(), request.DotnetExecutable) &&
            SameFrameworkPath(outer.GetProperty("workingDirectory").GetString(), request.WorkingDirectory) &&
            Arguments(outer).SequenceEqual(new[] { driver, "framework-run", "--request", executionRequest }, StringComparer.Ordinal),
            "framework-driver-invocation-mismatch");
        using var innerDocument = ReadJson(paths[CoverageArtifactKind.CoverInvocation]);
        var inner = innerDocument.RootElement;
        var launch = request.ProfiledLaunch!;
        // Keep this bounded argv contract identical to the existing driver's
        // direct SDK VSTest route; no arbitrary command descriptor is admitted.
        string[] target = [launch.ToolAssembly, "cover", "--target-executable", request.DotnetExecutable,
            "--snapshot-output", launch.SnapshotPath, "--log-file", launch.LogPath, "--", "exec",
            launch.VstestConsoleAssembly, request.TestAssembly, "--Settings:" + request.RunSettings,
            "--logger:trx;LogFileName=" + Path.GetFileName(request.TrxPath),
            "--ResultsDirectory:" + Path.GetDirectoryName(request.TrxPath)];
        Require(inner.GetProperty("schemaVersion").GetInt32() == 1 &&
            SameFrameworkPath(inner.GetProperty("executable").GetString(), request.DotnetExecutable) &&
            SameFrameworkPath(inner.GetProperty("workingDirectory").GetString(), request.WorkingDirectory) &&
            Arguments(inner).SequenceEqual(target, StringComparer.Ordinal), "framework-profiler-invocation-mismatch");
        var control = inner.GetProperty("driverControl");
        var launcherPipe = context.PipeName[..^"-host".Length] + "-launcher";
        Require(control.GetProperty("pipeName").GetString() == launcherPipe &&
            control.GetProperty("nonce").GetString() == context.Nonce &&
            control.GetProperty("runId").GetString() == request.RunId &&
            control.GetProperty("sourceTree").GetString() == request.SourceTree, "framework-launcher-context-mismatch");
        var environment = inner.GetProperty("environment");
        Require(environment.ValueKind == JsonValueKind.Object, "framework-environment-mismatch");
        var actual = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in environment.EnumerateObject())
        {
            Require(property.Value.ValueKind == JsonValueKind.String && actual.TryAdd(property.Name, property.Value.GetString()!),
                "framework-environment-mismatch");
        }
        var declared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.Environment is not null)
            foreach (var item in request.Environment)
            {
                Require(Token(item.Key) && item.Key.Length <= 4096 && !item.Key.Contains('=') && item.Value is not null &&
                    item.Value.Length <= 32_767 && !item.Value.Contains('\0') &&
                    !item.Key.Equals("FISHAUDIO_FRAMEWORK_CONTROL_CONTEXT", StringComparison.OrdinalIgnoreCase) &&
                    !item.Key.Equals("FISHAUDIO_FRAMEWORK_FINALIZATION_TEST_FAULT", StringComparison.OrdinalIgnoreCase) &&
                    !item.Key.Equals("FISHAUDIO_SNAPSHOT_CONTEXT", StringComparison.OrdinalIgnoreCase) &&
                    declared.TryAdd(item.Key, item.Value), "framework-environment-mismatch");
            }
        declared.Add("FISHAUDIO_FRAMEWORK_CONTROL_CONTEXT", controlContext);
        Require(actual.Count == declared.Count && declared.All(item => actual.TryGetValue(item.Key, out var value) && value == item.Value),
            "framework-environment-mismatch");
    }

    private static void ValidateFrameworkSupervisor(IsolatedCoverageAdmission expected, FrameworkRunRequest request,
        FrameworkClientContext context, string supervisorPath, IReadOnlyDictionary<CoverageArtifactKind, string> paths, int hostPid)
    {
        using var document = ReadJson(supervisorPath);
        var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("kind").GetString() == "framework-execution" &&
            root.GetProperty("runId").GetString() == request.RunId && root.GetProperty("sourceTree").GetString() == request.SourceTree &&
            root.GetProperty("sourceFingerprint").GetString() == request.SourceFingerprint &&
            root.GetProperty("testAssemblySha256").GetString() == request.TestAssemblySha256 &&
            !root.GetProperty("baselineClaim").GetBoolean() && root.GetProperty("failureCode").ValueKind == JsonValueKind.Null,
            "framework-supervisor-identity-mismatch");
        RequireFrameworkIds(root.GetProperty("selectedIds"), expected.Identity.CanonicalId);
        var state = root.GetProperty("state");
        Require(state.GetProperty("complete").GetBoolean() && state.GetProperty("stage").GetString() == "child-exit" &&
            state.GetProperty("failureStage").ValueKind == JsonValueKind.Null &&
            state.GetProperty("failureCaseId").ValueKind == JsonValueKind.Null &&
            !state.GetProperty("terminationRequired").GetBoolean(), "framework-supervisor-incomplete");
        RequireFrameworkIds(state.GetProperty("passedIds"), expected.Identity.CanonicalId);
        foreach (var name in new[] { "failedIds", "timedOutIds", "activeIds", "interruptedIds", "notRunIds" })
            RequireFrameworkIds(state.GetProperty(name));
        var target = root.GetProperty("target");
        var process = root.GetProperty("process");
        var targetPid = target.GetProperty("pid").GetInt32();
        var launcherPid = process.GetProperty("launcherPid").GetInt32();
        Require(target.GetProperty("started").GetBoolean() && targetPid > 0 && target.GetProperty("exitCode").GetInt32() == 0 &&
            launcherPid > 0 && launcherPid != targetPid && process.GetProperty("launcherExited").GetBoolean() &&
            process.GetProperty("stdoutDrained").GetBoolean() && process.GetProperty("stderrDrained").GetBoolean() &&
            process.GetProperty("ownedActiveProcesses").GetInt32() == 0, "framework-supervisor-process-incomplete");
        var evidence = root.GetProperty("rawEvidence");
        Require(evidence.GetProperty("verified").GetBoolean() &&
            SameFrameworkPath(evidence.GetProperty("resultXmlPath").GetString(), paths[CoverageArtifactKind.NUnitXml]) &&
            SameFrameworkPath(evidence.GetProperty("trxPath").GetString(), paths[CoverageArtifactKind.Trx]) &&
            evidence.GetProperty("resultXmlSha256").GetString() == ArtifactHash(expected, CoverageArtifactKind.NUnitXml) &&
            evidence.GetProperty("trxSha256").GetString() == ArtifactHash(expected, CoverageArtifactKind.Trx),
            "framework-supervisor-raw-evidence-mismatch");
        var launch = root.GetProperty("launch");
        Require(launch.GetProperty("kind").GetString() == "dotcover-sdk-vstest" &&
            launch.GetProperty("sessionId").GetString() == expected.Identity.SessionId &&
            launch.GetProperty("invocationSha256").GetString() == ArtifactHash(expected, CoverageArtifactKind.CoverInvocation) &&
            launch.GetProperty("toolSha256").GetString() == expected.ToolSha256 &&
            launch.GetProperty("vstestConsoleSha256").GetString() == request.ProfiledLaunch!.VstestConsoleSha256,
            "framework-supervisor-launch-mismatch");
        var finalization = root.GetProperty("finalization");
        var frequency = finalization.GetProperty("frequency").GetInt64();
        Require(frequency is > 0 and <= 1_000_000_000_000 && finalization.GetProperty("cleanupAnchorTimestamp").GetInt64() >= 0 &&
            root.GetProperty("elapsedMilliseconds").TryGetDouble(out var elapsed) && double.IsFinite(elapsed) && elapsed >= 0,
            "framework-supervisor-clock-invalid");
        // The callback pair identifies the host without claiming its individual
        // OS exit or pipe drains. Those completion facts remain the driver's
        // observed target exit, aggregate drains and empty owned Job below.
        Require(hostPid > 0 && hostPid != targetPid && hostPid != launcherPid, "framework-supervisor-host-identity");
        ValidateFrameworkEvents(root.GetProperty("events"), request, context, hostPid, targetPid, launcherPid, frequency);
    }

    private static void ValidateFrameworkEvents(JsonElement events, FrameworkRunRequest request,
        FrameworkClientContext context, int hostPid, int targetPid, int launcherPid, long frequency)
    {
        Require(events.ValueKind == JsonValueKind.Array && events.GetArrayLength() is >= 6 and <= 1_000_000,
            "framework-control-events-required");
        var lifecycle = new List<string>();
        var launcherKinds = new List<string>();
        long lastLauncherTimestamp = -1;
        foreach (var item in events.EnumerateArray())
        {
            var record = item.Deserialize<ExecutionControlRecord>(FrameworkExecutionDriver.JsonOptions);
            Require(record is not null && record.SchemaVersion == 1 && record.Nonce == context.Nonce &&
                record.RunId == request.RunId && record.SourceTree == request.SourceTree && record.Frequency == frequency &&
                record.Timestamp >= 0 && record.QueuedTimestamp >= 0 && record.QueuedTimestamp <= record.Timestamp,
                "framework-control-event-identity");
            if (record.Role == "execution-launcher")
            {
                Require(record.Pid == launcherPid && record.TargetPid == targetPid && record.Timestamp >= lastLauncherTimestamp &&
                    record.AssemblySha256 is null && record.CaseId is null && record.FullName is null && record.Ordinal == 0 &&
                    record.QueuedTimestamp == 0 && record.Outcome is null &&
                    (record.Kind == "target-started" && record.ExitCode is null || record.Kind == "target-exited" && record.ExitCode == 0),
                    "framework-launcher-event-mismatch");
                launcherKinds.Add(record.Kind);
                lastLauncherTimestamp = record.Timestamp;
                continue;
            }
            // The driver checked the pipe's kernel PID and membership in its
            // Job before accepting these events. The receipt independently
            // binds that NUnit host PID; it is not the profiler target PID.
            Require(record.Role == "nunit-host" && record.Pid == hostPid && record.AssemblySha256 == request.TestAssemblySha256 &&
                record.TargetPid is null && record.ExitCode is null && record.Ordinal >= 0,
                "framework-host-event-mismatch");
            if (record.Kind is "ready" or "cleanup-complete")
                Require(record.CaseId is null && record.FullName is null && record.Ordinal == 0 && record.QueuedTimestamp == 0 &&
                    record.Outcome is null, "framework-host-lifecycle-mismatch");
            else
            {
                Require(record.CaseId == request.Cases[0].CanonicalId && record.FullName == request.Cases[0].FullName,
                    "framework-host-case-mismatch");
                if (record.Kind is "case-start" or "case-finished")
                    Require(record.Ordinal == 0 && record.QueuedTimestamp == 0 &&
                        (record.Kind == "case-start" ? record.Outcome is null : record.Outcome == "passed"),
                        "framework-host-lifecycle-mismatch");
                else
                    Require(record.Kind is "dispatcher-starting" or "dispatcher-ready" or "queued" or "entered" or "completed" &&
                        record.Outcome is null && lifecycle.SequenceEqual(new[] { "ready", "case-start" }, StringComparer.Ordinal),
                        "framework-host-action-mismatch");
            }
            if (record.Kind is "ready" or "case-start" or "case-finished" or "cleanup-complete") lifecycle.Add(record.Kind);
        }
        Require(lifecycle.SequenceEqual(new[] { "ready", "case-start", "case-finished", "cleanup-complete" }, StringComparer.Ordinal) &&
            launcherKinds.SequenceEqual(new[] { "target-started", "target-exited" }, StringComparer.Ordinal),
            "framework-control-lifecycle-incomplete");
    }

    private static void RequireFrameworkIds(JsonElement value, params string[] expected)
    {
        Require(value.ValueKind == JsonValueKind.Array, "framework-supervisor-case-set-mismatch");
        var ids = value.EnumerateArray().Select(item => item.GetString()).ToArray();
        Require(ids.Length == expected.Length && ids.All(Token), "framework-supervisor-case-set-mismatch");
        var unique = new HashSet<string>(ids.Select(item => item!), StringComparer.Ordinal);
        Require(unique.Count == ids.Length && unique.SetEquals(expected),
            "framework-supervisor-case-set-mismatch");
    }

    private static string ArtifactHash(IsolatedCoverageAdmission expected, CoverageArtifactKind kind) =>
        expected.Supervisor.Artifacts.Single(item => item.Kind == kind).Sha256;

    private static bool SameFrameworkPath(string? first, string? second) => Token(first) && Token(second) &&
        Path.IsPathFullyQualified(first!) && Path.IsPathFullyQualified(second!) &&
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(first!)).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second!)), StringComparison.OrdinalIgnoreCase);

    private static bool FrameworkWithin(string path, string root) => Path.IsPathFullyQualified(path) &&
        Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
