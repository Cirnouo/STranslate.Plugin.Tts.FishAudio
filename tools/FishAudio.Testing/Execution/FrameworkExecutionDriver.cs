using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FishAudio.Testing.Evidence;

namespace FishAudio.Testing.Execution;

public sealed record FrameworkRunRequest(int SchemaVersion, string RunId, string? SourceTree,
    string WorkingDirectory, string ShellExecutable, string DotnetExecutable, string LauncherScript,
    string TestAssembly, string TestAssemblySha256, string RunSettings, string DiscoveryPath,
    string ResultXmlPath, string TrxPath, string OutputDirectory,
    IReadOnlyList<FrameworkCaseBinding> Cases, int MaximumConcurrency = 1, bool RequireOrder = false,
    IReadOnlyDictionary<string, string>? Environment = null, string? SourceFingerprint = null,
    DotCoverSdkVstestLaunch? ProfiledLaunch = null);

// The only profiled route is the pinned CLI around the SDK's VSTest console.
// It does not accept an arbitrary executable or replacement test arguments.
public sealed record DotCoverSdkVstestLaunch(string SessionId, string ToolAssembly, string ToolSha256,
    string VstestConsoleAssembly, string VstestConsoleSha256, string SnapshotPath, string LogPath);

/// <summary>
/// Owns the process and evidence envelope of one standard VSTest invocation.
/// NUnit remains the owner of discovery, fixtures, execution and assertions.
/// </summary>
public static class FrameworkExecutionDriver
{
    private const string FinalizationFaultVariable = "FISHAUDIO_FRAMEWORK_FINALIZATION_TEST_FAULT";
    private const string FinalizationWriteHang = "block-final-write";
    internal static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };
    public static async Task<int> RunAsync(string absoluteRequestPath)
    {
        var guards = new List<FileStream>();
        var executionOwnsInputs = false;
        try
        {
            var finalizationFault = Environment.GetEnvironmentVariable(FinalizationFaultVariable);
            Require(string.IsNullOrEmpty(finalizationFault) || finalizationFault == FinalizationWriteHang,
                "framework-finalization-test-fault");
            var requestInput = OpenInput(absoluteRequestPath);
            guards.Add(requestInput);
            Require(requestInput.Length is > 0 and <= 16 * 1024 * 1024, "framework-request-size");
            var requestSha256 = Convert.ToHexStringLower(SHA256.HashData(requestInput));
            requestInput.Position = 0;
            var request = await JsonSerializer.DeserializeAsync<FrameworkRunRequest>(requestInput, JsonOptions)
                ?? throw new InvalidDataException("framework-request-empty");
            var catalog = ValidateRequest(request, guards);
            return await ExecuteAsync(request, requestSha256, catalog, guards, () => executionOwnsInputs = true, finalizationFault);
        }
        finally
        {
            // Before launch these are preflight reads. After the handoff, even
            // their read-only releases belong to the existing cleanup anchor.
            if (!executionOwnsInputs)
                foreach (var guard in guards) guard.Dispose();
        }
    }

    private static IReadOnlyList<DiscoveredNUnitCase> ValidateRequest(FrameworkRunRequest request,
        List<FileStream> guards)
    {
        var inheritedContext = Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT");
        Require(OperatingSystem.IsWindows() && request.SchemaVersion == 1 &&
            Guid.TryParseExact(request.RunId, "N", out var id) && id != Guid.Empty && id.ToString("N") == request.RunId &&
            (request.SourceTree is null
                ? Hash(request.SourceFingerprint, 64) && inheritedContext is null
                : Hash(request.SourceTree, 40) || Hash(request.SourceTree, 64)) &&
            (request.SourceFingerprint is null || Hash(request.SourceFingerprint, 64)) &&
            Hash(request.TestAssemblySha256, 64) && request.MaximumConcurrency is >= 1 and <= 4 &&
            (!request.RequireOrder || request.MaximumConcurrency == 1), "framework-request-identity");
        Require(request.Cases is { Count: > 0 and <= 100_000 } && request.Cases.All(item => item is not null &&
            Identity(item.CanonicalId) && Identity(item.FullName)) &&
            request.Cases.Select(item => item.CanonicalId).Distinct(StringComparer.Ordinal).Count() == request.Cases.Count &&
            request.Cases.Select(item => item.FullName).Distinct(StringComparer.Ordinal).Count() == request.Cases.Count,
            "framework-request-selection");
        RequireDirectory(request.WorkingDirectory);
        foreach (var path in new[] { request.ShellExecutable, request.DotnetExecutable, request.LauncherScript,
            request.TestAssembly, request.RunSettings, request.DiscoveryPath }) guards.Add(OpenInput(path));
        Require(Digest(request.TestAssembly) == request.TestAssemblySha256, "framework-request-assembly-mismatch");
        var catalog = NUnitEvidence.ReadCatalog(request.DiscoveryPath);
        foreach (var item in request.Cases)
            Require(catalog.Count(candidate => candidate.CanonicalId == item.CanonicalId && candidate.FullName == item.FullName) == 1,
                "framework-request-unknown-or-mismatched-case");
        var output = Absolute(request.OutputDirectory);
        Require(!Directory.Exists(output) && !File.Exists(output), "framework-output-already-exists");
        RequireDirectory(Path.GetDirectoryName(output)!);
        foreach (var path in new[] { request.ResultXmlPath, request.TrxPath })
            Require(Within(Absolute(path), output) && !File.Exists(path) && !Directory.Exists(path), "framework-result-not-fresh");
        ValidateProfiledLaunch(request, output, guards);
        if (request.Environment is not null)
            foreach (var item in request.Environment)
                Require(Identity(item.Key) && !item.Key.Contains('=') && item.Value is not null &&
                    !item.Value.Contains('\0') && item.Value.Length <= 32_767 &&
                    !item.Key.Equals("FISHAUDIO_FRAMEWORK_CONTROL_CONTEXT", StringComparison.OrdinalIgnoreCase) &&
                    !item.Key.Equals(FinalizationFaultVariable, StringComparison.OrdinalIgnoreCase) &&
                    !item.Key.Equals("FISHAUDIO_SNAPSHOT_CONTEXT", StringComparison.OrdinalIgnoreCase),
                    "framework-request-environment");
        // The caller owns raw-tree materialization and inventory validation.
        // Bind to its explicit context when present; never search a checkout.
        if (inheritedContext is not null)
        {
            Require(request.SourceTree is not null && !string.IsNullOrWhiteSpace(inheritedContext),
                "framework-snapshot-context-mismatch");
            var contextGuard = OpenInput(inheritedContext);
            guards.Add(contextGuard);
            Require(contextGuard.Length is > 0 and <= 16 * 1024 * 1024, "framework-snapshot-context-size");
            using var context = JsonDocument.Parse(contextGuard);
            Require(context.RootElement.GetProperty("schemaVersion").GetInt32() == 1 &&
                context.RootElement.GetProperty("tree").GetString() == request.SourceTree &&
                Same(context.RootElement.GetProperty("root").GetString()!, request.WorkingDirectory),
                "framework-snapshot-context-mismatch");
        }
        return catalog;
    }

    private static void ValidateProfiledLaunch(FrameworkRunRequest request, string output, List<FileStream> guards)
    {
        if (request.ProfiledLaunch is not { } launch) return;
        Require(Guid.TryParseExact(launch.SessionId, "N", out var session) && session != Guid.Empty &&
            session.ToString("N") == launch.SessionId && Hash(launch.ToolSha256, 64) &&
            Hash(launch.VstestConsoleSha256, 64) && request.Cases.Count == 1 && request.MaximumConcurrency == 1,
            "framework-profiled-request-identity");
        guards.Add(OpenInput(launch.ToolAssembly));
        Require(Digest(launch.ToolAssembly) == launch.ToolSha256, "framework-profiled-tool-mismatch");
        guards.Add(OpenInput(launch.VstestConsoleAssembly));
        Require(Digest(launch.VstestConsoleAssembly) == launch.VstestConsoleSha256, "framework-profiled-vstest-mismatch");
        Require(Path.GetFileName(launch.ToolAssembly).Equals("dotCover.dll", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(launch.VstestConsoleAssembly).Equals("vstest.console.dll", StringComparison.OrdinalIgnoreCase),
            "framework-profiled-input-kind");
        var ownedFiles = new List<string>();
        foreach (var name in new[] { "control-context.json", "invocation.json", "stdout.bin", "stderr.bin",
            "supervisor.json.pending", "supervisor.json", "finalization-write-entered.json" })
            ownedFiles.Add(Path.Combine(output, name));
        foreach (var value in new[] { request.ResultXmlPath, request.TrxPath, launch.SnapshotPath, launch.LogPath })
        {
            var path = Absolute(value);
            Require(Within(path, output) && !File.Exists(path) && !Directory.Exists(path) &&
                ownedFiles.All(other => !Same(path, other) && !Within(path, other) && !Within(other, path)),
                "framework-profiled-output-not-fresh");
            ownedFiles.Add(path);
        }
    }

    private static string[] TargetArguments(FrameworkRunRequest request)
    {
        var results = new[] { "--Settings:" + request.RunSettings,
            "--ResultsDirectory:" + Path.GetDirectoryName(request.TrxPath),
            "--logger:trx;LogFileName=" + Path.GetFileName(request.TrxPath) };
        if (request.ProfiledLaunch is not { } launch)
            return ["vstest", request.TestAssembly, .. results];
        return [launch.ToolAssembly, "cover", "--target-executable", request.DotnetExecutable,
            "--snapshot-output", launch.SnapshotPath, "--log-file", launch.LogPath, "--", "exec",
            launch.VstestConsoleAssembly, request.TestAssembly, results[0], results[2], results[1]];
    }

    private static async Task<int> ExecuteAsync(FrameworkRunRequest request, string requestSha256,
        IReadOnlyList<DiscoveredNUnitCase> catalog,
        List<FileStream> inputGuards, Action takeInputOwnership, string? finalizationFault)
    {
        Directory.CreateDirectory(request.OutputDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(request.ResultXmlPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(request.TrxPath)!);
        if (request.ProfiledLaunch is { } profiled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(profiled.SnapshotPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(profiled.LogPath)!);
        }
        var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var pipeId = Guid.NewGuid().ToString("N");
        var hostPipe = "fa-fw-" + pipeId + "-host";
        var launcherPipe = "fa-fw-" + pipeId + "-launcher";
        var contextPath = Path.Combine(request.OutputDirectory, "control-context.json");
        await WriteNewAsync(contextPath, new FrameworkClientContext(1, request.RunId, request.SourceTree,
            nonce, hostPipe, request.TestAssembly, request.TestAssemblySha256, request.Cases));
        var targetEnvironment = request.Environment?.ToDictionary(item => item.Key, item => item.Value,
            StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        targetEnvironment.Add("FISHAUDIO_FRAMEWORK_CONTROL_CONTEXT", contextPath);
        var arguments = TargetArguments(request);
        var invocationPath = Path.Combine(request.OutputDirectory, "invocation.json");
        var invocation = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1, ["executable"] = request.DotnetExecutable, ["workingDirectory"] = request.WorkingDirectory,
            ["arguments"] = arguments, ["environment"] = targetEnvironment,
            ["driverControl"] = new { pipeName = launcherPipe, nonce, runId = request.RunId, sourceTree = request.SourceTree },
        };
        if (request.ProfiledLaunch is { } coverage)
        {
            invocation.Add("sessionRunId", coverage.SessionId);
            invocation.Add("canonicalCaseId", request.Cases[0].CanonicalId);
        }
        await WriteNewAsync(invocationPath, invocation);
        inputGuards.Add(OpenInput(invocationPath));
        var invocationSha256 = Digest(invocationPath);
        using var owner = new WindowsJobOwner();
        using var cancellation = new CancellationTokenSource();
        using var process = new Process { StartInfo = new(request.ShellExecutable)
        {
            WorkingDirectory = request.WorkingDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        } };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", request.LauncherScript,
            "-ArgumentsPath", invocationPath }) process.StartInfo.ArgumentList.Add(argument);
        // This opt-in affects the owning driver only, never NUnit fixtures or
        // the launcher/target environment serialized into the invocation.
        process.StartInfo.Environment.Remove(FinalizationFaultVariable);
        var channel = Channel.CreateBounded<ExecutionControlRecord>(new BoundedChannelOptions(20_000)
        {
            SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait,
        });
        using var host = new ExecutionControlServer(hostPipe, "nunit-host", nonce, owner, request, channel.Writer);
        using var launcher = new ExecutionControlServer(launcherPipe, "execution-launcher", nonce, owner,
            request, channel.Writer, () => process.Id);
        var hostRead = host.ReadAsync(cancellation.Token);
        var launcherRead = launcher.ReadAsync(cancellation.Token);
        var started = Stopwatch.GetTimestamp();
        var watchdog = new FrameworkWatchdog(request.Cases.Select(item => item.CanonicalId).ToArray(),
            started, Stopwatch.Frequency, request.MaximumConcurrency, request.RequireOrder);
        var events = new List<ExecutionControlRecord>();
        var warnings = new List<object>();
        var warningCount = 0;
        var deliveredWarnings = 0;
        var diagnostics = Channel.CreateBounded<string>(new BoundedChannelOptions(64)
        {
            SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait,
        });
        // Console is observational. A blocked diagnostic writer cannot stop the
        // owner from enforcing a deadline and terminating a polluted Job.
        var diagnosticWriter = Task.Run(async () =>
        {
            await foreach (var message in diagnostics.Reader.ReadAllAsync())
            {
                Console.Error.WriteLine(message);
                Interlocked.Increment(ref deliveredWarnings);
            }
        });
        var targetStarted = false;
        var targetPid = 0;
        int? targetExitCode = null;
        long? targetExitTimestamp = null;
        var published = false;
        Task<(NUnitEvidenceResult Evidence, string ResultSha256, string TrxSha256)>? verification = null;
        long? verificationStarted = null;
        var accepted = false;
        var verified = false;
        var resultGuards = new List<FileStream>();
        string? resultXmlSha256 = null, trxSha256 = null;
        var launched = false;
        var assigned = false;
        var stdoutDrained = false;
        var stderrDrained = false;
        var launcherExited = false;
        uint? activeProcesses = null;
        string? failureCode = null;
        long? cleanupAnchor = null;
        Task? exited = null, stdout = null, stderr = null;
        FileStream? outFile = null, errFile = null;
        takeInputOwnership();
        try
        {
            outFile = new FileStream(Path.Combine(request.OutputDirectory, "stdout.bin"), FileMode.CreateNew,
                FileAccess.Write, FileShare.Read, 4096, useAsync: true);
            errFile = new FileStream(Path.Combine(request.OutputDirectory, "stderr.bin"), FileMode.CreateNew,
                FileAccess.Write, FileShare.Read, 4096, useAsync: true);
            Require(process.Start(), "framework-launcher-start");
            launched = true;
            owner.Assign(process.Handle);
            assigned = true;
            Require(owner.IsMember(process.Handle), "framework-launcher-not-owned");
            stdout = process.StandardOutput.BaseStream.CopyToAsync(outFile);
            stderr = process.StandardError.BaseStream.CopyToAsync(errFile);
            exited = process.WaitForExitAsync();
            await process.StandardInput.WriteLineAsync("GO");
            process.StandardInput.Close();
            while (true)
            {
                var received = 0;
                while (received++ < 256 && channel.Reader.TryRead(out var record))
                {
                    Require(events.Count < 1_000_000, "framework-control-event-limit");
                    events.Add(record);
                    if (record.Role == "execution-launcher")
                    {
                        if (record.Kind == "target-started")
                        {
                            Require(!targetStarted && targetExitTimestamp is null, "framework-target-start-order");
                            targetStarted = true;
                            targetPid = record.TargetPid!.Value;
                        }
                        else
                        {
                            Require(targetStarted && targetExitTimestamp is null && record.TargetPid == targetPid,
                                "framework-target-exit-order");
                            targetExitTimestamp = record.Timestamp;
                            targetExitCode = record.ExitCode;
                        }
                    }
                    else
                    {
                        if (record.CaseId is null)
                            Require(record.FullName is null, "framework-host-unexpected-name");
                        else
                            Require(request.Cases.Any(item => item.CanonicalId == record.CaseId && item.FullName == record.FullName),
                                "framework-host-case-binding-mismatch");
                        watchdog.Observe(new(record.Kind, record.Timestamp, record.Frequency, record.CaseId,
                            record.Ordinal, record.QueuedTimestamp, record.Outcome));
                    }
                }
                if (hostRead.IsFaulted) await hostRead;
                if (launcherRead.IsFaulted) await launcherRead;
                if (targetExitTimestamp is { } publication && hostRead.IsCompletedSuccessfully &&
                    !channel.Reader.TryPeek(out _) && !published)
                {
                    // The launcher observed the real standard-platform process
                    // exit, independently of output drain or fixture callbacks.
                    watchdog.Observe(new("final-published", publication, Stopwatch.Frequency));
                    published = true;
                    Require(targetExitCode == 0, "framework-target-failed");
                    verificationStarted = Stopwatch.GetTimestamp();
                    // VSTest has closed publication. Hold both exact regular
                    // files against replacement/writes through final admission
                    // and report publication, including remaining descendants.
                    var resultGuard = OpenInput(request.ResultXmlPath);
                    resultGuards.Add(resultGuard);
                    var trxGuard = OpenInput(request.TrxPath);
                    resultGuards.Add(trxGuard);
                    // Match the existing metadata-admission boundary without
                    // blocking the independent event/deadline observer on file
                    // I/O or XML parsing. This is not another case allowance.
                    verification = Task.Run(() =>
                    {
                        Require(resultGuard.Length is > 0 and <= 64 * 1024 * 1024 &&
                            trxGuard.Length is > 0 and <= 64 * 1024 * 1024, "framework-result-size-limit");
                        var resultHash = Convert.ToHexStringLower(SHA256.HashData(resultGuard));
                        var trxHash = Convert.ToHexStringLower(SHA256.HashData(trxGuard));
                        var evidence = NUnitEvidence.VerifyResults(catalog, request.Cases.Select(item => item.CanonicalId).ToArray(),
                            request.ResultXmlPath, request.TrxPath);
                        return (evidence, resultHash, trxHash);
                    });
                }
                if (verification is not null && !accepted)
                {
                    Require((Stopwatch.GetTimestamp() - verificationStarted!.Value) / (double)Stopwatch.Frequency < 120,
                        "framework-evidence-verification-deadline");
                    if (verification.IsCompleted)
                    {
                        var admitted = await verification;
                        resultXmlSha256 = admitted.ResultSha256;
                        trxSha256 = admitted.TrxSha256;
                        verified = true;
                        var acceptedTimestamp = Stopwatch.GetTimestamp();
                        watchdog.AcceptFinal(acceptedTimestamp);
                        accepted = true;
                        cleanupAnchor = acceptedTimestamp;
                    }
                }
                watchdog.Tick(Stopwatch.GetTimestamp());
                if (watchdog.State.WarningCount > warningCount)
                {
                    warningCount = watchdog.State.WarningCount;
                    warnings.Add(new { timestamp = Stopwatch.GetTimestamp(), count = warningCount, watchdog.State.Stage });
                    _ = diagnostics.Writer.TryWrite("framework-run: twenty-second slow execution warning; stage=" + watchdog.State.Stage);
                }
                Require(!watchdog.State.TerminationRequired, "framework-watchdog-hard-failure");
                launcherExited = exited.IsCompletedSuccessfully;
                stdoutDrained = stdout.IsCompletedSuccessfully;
                stderrDrained = stderr.IsCompletedSuccessfully;
                activeProcesses = owner.ActiveProcesses;
                if (stdout.IsFaulted) await stdout;
                if (stderr.IsFaulted) await stderr;
                if (accepted && launcherExited && stdoutDrained && stderrDrained && activeProcesses == 0 &&
                    launcherRead.IsCompletedSuccessfully && hostRead.IsCompletedSuccessfully && !channel.Reader.TryPeek(out _))
                {
                    watchdog.HostExited(Stopwatch.GetTimestamp(), accepted && process.ExitCode == 0, true, true);
                    Require(watchdog.State.Complete && verified, "framework-envelope-incomplete");
                    break;
                }
                // A failed launcher can exit without ever creating a test host.
                if (launcherExited && process.ExitCode != 0)
                    throw new InvalidDataException("framework-launcher-failed");
                await Task.Delay(25);
            }
        }
        catch (Exception error)
        {
            failureCode = error switch
            {
                NUnitEvidenceException evidence => evidence.Code,
                InvalidDataException data when data.Message.StartsWith("framework-", StringComparison.Ordinal) => data.Message,
                _ => "framework-process-or-control-failure",
            };
            // A timeout after acceptance does not receive a fresh allowance.
            // Protocol/hard failures before acceptance get the one shared ten
            // seconds for termination, original exit and both original drains.
            cleanupAnchor ??= Stopwatch.GetTimestamp();
            try { if (assigned) owner.Terminate(); else if (launched) process.Kill(entireProcessTree: true); }
            catch { }
            while (launched && (Stopwatch.GetTimestamp() - cleanupAnchor.Value) / (double)Stopwatch.Frequency < 10)
            {
                try
                {
                    launcherExited = launched && process.HasExited;
                    stdoutDrained = stdout?.IsCompletedSuccessfully == true;
                    stderrDrained = stderr?.IsCompletedSuccessfully == true;
                    activeProcesses = assigned ? owner.ActiveProcesses : null;
                    if (launcherExited && stdoutDrained && stderrDrained && (!assigned || activeProcesses == 0)) break;
                }
                catch { break; }
                await Task.Delay(25);
            }
            try { watchdog.HostExited(Stopwatch.GetTimestamp(), false, stdoutDrained, stderrDrained); }
            catch { /* Retain the original failure and the already latched state. */ }
        }
        finally
        {
            diagnostics.Writer.TryComplete();
        }
        var anchor = cleanupAnchor ?? throw new InvalidOperationException("framework-cleanup-anchor-missing");
        var report = new
        {
            schemaVersion = 1, kind = "framework-execution", request.RunId, request.SourceTree, request.SourceFingerprint,
            request.TestAssemblySha256, requestSha256,
            policy = new { request.MaximumConcurrency, request.RequireOrder },
            launch = new { kind = request.ProfiledLaunch is null ? "vstest" : "dotcover-sdk-vstest",
                sessionId = request.ProfiledLaunch?.SessionId, invocationSha256,
                toolSha256 = request.ProfiledLaunch?.ToolSha256,
                vstestConsoleSha256 = request.ProfiledLaunch?.VstestConsoleSha256 },
            selectedIds = request.Cases.Select(item => item.CanonicalId).ToArray(), state = watchdog.State,
            target = new { started = targetStarted, pid = targetPid, exitCode = targetExitCode },
            process = new { launcherPid = launched ? process.Id : (int?)null, launcherExited,
                stdoutDrained, stderrDrained, ownedActiveProcesses = activeProcesses },
            rawEvidence = new { verified, request.ResultXmlPath, request.TrxPath, resultXmlSha256, trxSha256 }, failureCode,
            events, warnings, diagnostics = new { deliveredWarnings = Volatile.Read(ref deliveredWarnings),
                writerCompleted = diagnosticWriter.IsCompletedSuccessfully },
            elapsedMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency,
            finalization = new { cleanupAnchorTimestamp = anchor, frequency = Stopwatch.Frequency },
            baselineClaim = false,
        };
        var outputsReleased = 0;
        var inputsReleased = 0;
        var finalizationStarted = 0;
        async Task ReleaseOutputsAsync(CancellationToken token)
        {
            if (Interlocked.Exchange(ref outputsReleased, 1) != 0) return;
            var failed = false;
            foreach (var stream in new[] { outFile, errFile })
            {
                if (stream is null) continue;
                try { await stream.FlushAsync(token).ConfigureAwait(false); }
                catch { failed = true; }
                try { await stream.DisposeAsync().ConfigureAwait(false); }
                catch { failed = true; }
            }
            if (failed) throw new InvalidDataException("framework-finalization-output-release");
        }
        void ReleaseInputs()
        {
            if (Interlocked.Exchange(ref inputsReleased, 1) != 0) return;
            var failed = false;
            foreach (var guard in resultGuards.Concat(inputGuards))
            {
                try { guard.Dispose(); }
                catch { failed = true; }
            }
            if (failed) throw new InvalidDataException("framework-finalization-input-release");
        }
        var pendingPath = Path.Combine(request.OutputDirectory, "supervisor.json.pending");
        var finalPath = Path.Combine(request.OutputDirectory, "supervisor.json");
        var finalization = await FrameworkFinalization.RunAsync(anchor, async token =>
        {
            if (Interlocked.CompareExchange(ref finalizationStarted, 1, 0) != 0) return;
            var publishedFinal = false;
            try
            {
                cancellation.Cancel();
                await ReleaseOutputsAsync(token).ConfigureAwait(false);
                RequireFinalizationTime(anchor, token);
                if (finalizationFault == FinalizationWriteHang)
                {
                    await WriteFinalizationJsonAsync(Path.Combine(request.OutputDirectory, "finalization-write-entered.json"), new
                    {
                        schemaVersion = 1, kind = "framework-finalization-test-entered", testFault = FinalizationWriteHang,
                        request.RunId, request.SourceTree, cleanupAnchorTimestamp = anchor, frequency = Stopwatch.Frequency,
                        enteredTimestamp = Stopwatch.GetTimestamp(), execution = report, baselineClaim = false,
                    }, token).ConfigureAwait(false);
                    // Deliberately ignores cancellation. The existing owner
                    // allowance must expire without waiting for cooperation.
                    await new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task.ConfigureAwait(false);
                }
                await WriteFinalizationJsonAsync(pendingPath, report, token).ConfigureAwait(false);
                ReleaseInputs();
                RequireFinalizationTime(anchor, token);
                File.Move(pendingPath, finalPath);
                publishedFinal = true;
                RequireFinalizationTime(anchor, token);
            }
            catch
            {
                // A late filesystem completion cannot turn the nonzero owner
                // outcome green. Withdrawal is background work in this same
                // envelope; consumers must independently require CLI success.
                if (publishedFinal)
                    try { File.Delete(finalPath); } catch { }
                throw;
            }
            finally
            {
                try { await ReleaseOutputsAsync(token).ConfigureAwait(false); }
                finally { ReleaseInputs(); }
            }
        }).ConfigureAwait(false);
        if (finalization.Outcome != FrameworkFinalizationOutcome.Completed)
        {
            // An already-spent anchor never invokes the operation. Release
            // those handles off-owner without publishing or starting a new
            // allowance. A running operation retains its own releases.
            if (Interlocked.CompareExchange(ref finalizationStarted, -1, 0) == 0)
                _ = Task.Run(async () =>
                {
                    try { cancellation.Cancel(); } catch { }
                    try { await ReleaseOutputsAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { ReleaseInputs(); } catch { }
                });
            return 1;
        }
        return failureCode is null && report.state.Complete && verified ? 0 : 1;
    }

    private static void RequireFinalizationTime(long anchor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var now = Stopwatch.GetTimestamp();
        Require(now >= anchor && Stopwatch.GetElapsedTime(anchor, now) < TimeSpan.FromSeconds(10),
            "framework-finalization-deadline");
    }

    private static async Task WriteFinalizationJsonAsync<T>(string path, T value, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
            4096, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    private static bool Hash(string? value, int length) => value is not null && value.Length == length &&
        value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');
    private static bool Identity(string? value) => value is { Length: > 0 and <= 4096 } && !value.Any(char.IsControl);
    private static string Absolute(string path)
    {
        Require(!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) &&
            !path.StartsWith(@"\\?\", StringComparison.Ordinal) && !path.StartsWith(@"\\.\", StringComparison.Ordinal),
            "framework-absolute-regular-path-required");
        return Path.GetFullPath(path);
    }
    private static bool Same(string first, string second) =>
        Path.TrimEndingDirectorySeparator(Absolute(first)).Equals(Path.TrimEndingDirectorySeparator(Absolute(second)),
            StringComparison.OrdinalIgnoreCase);
    private static bool Within(string path, string root) => path.StartsWith(
        Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void RequireDirectory(string path)
    {
        for (var current = new DirectoryInfo(Absolute(path)); current is not null; current = current.Parent)
            Require(current.Exists && (current.Attributes & FileAttributes.ReparsePoint) == 0, "framework-regular-directory-required");
    }
    private static FileStream OpenInput(string path)
    {
        path = Absolute(path);
        RequireDirectory(Path.GetDirectoryName(path)!);
        Require(File.Exists(path) && (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0,
            "framework-regular-input-required");
        return new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
    private static string Digest(string path)
    {
        using var stream = OpenInput(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    private static async Task WriteNewAsync<T>(string path, T value)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions);
    }
    private static void Require(bool condition, string code)
    {
        if (!condition) throw new InvalidDataException(code);
    }
}
