using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FishAudio.Testing.Execution;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class FrameworkSupervisorPolicyTests
{
    private const string Prefix = "fa-supervisor-policy-";
    private const string RootRunId = "11111111111111111111111111111111";
    private const string Tree = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Fingerprint = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private const string AssemblyHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string XmlHash = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
    private const string TrxHash = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
    private const string First = "supervisor-policy/first";
    private const string Second = "supervisor-policy/second";
    private const string Rejected = "framework-supervisor-evidence-rejected";
    private const string ControlNonce = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
    private const string InvocationHash = "9999999999999999999999999999999999999999999999999999999999999999";
    private const int TargetPid = 1235, LauncherPid = 1236, HostPid = 1237;
    private const string SupervisorFile = "execution/supervisor.json";
    // These are synthetic consumer inputs, not observations of a real launch or Job.
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private readonly Stopwatch _clock = new();
    private string _parent = "", _root = "", _helper = "", _request = "", _requestHash = "", _pwsh = "";
    private JsonObject _supervisor = null!;
    private int _invocations;

    [SetUp]
    public void CreateOwnedAdmissionInputs()
    {
        _clock.Start();
        var repository = RepositoryRoot();
        _pwsh = FindPwsh();
        _parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        _root = Path.Combine(_parent, Prefix + Guid.NewGuid().ToString("N"));
        if (File.Exists(_root) || Directory.Exists(_root)) throw new InvalidOperationException("supervisor-policy-owner-exists");
        Directory.CreateDirectory(_root);
        RequireOwnedRoot();
        Directory.CreateDirectory(Path.Combine(_root, "execution"));
        _helper = Path.Combine(_root, "Assert-FrameworkSupervisor.ps1");
        File.Copy(Path.Combine(repository, "tools/Assert-FrameworkSupervisor.ps1"), _helper, overwrite: false);
        Write("invoke.ps1", InvocationBoundary);
        _request = Path.Combine(_root, "request.json");
        WriteJson("request.json", new FrameworkRunRequest(1, RootRunId, Tree, _root, _pwsh,
            Path.Combine(_root, "dotnet.exe"), Path.Combine(_root, "launch.ps1"), Path.Combine(_root, "example.dll"),
            AssemblyHash, Path.Combine(_root, "settings.runsettings"), Path.Combine(_root, "discovery.dump"),
            Path.Combine(_root, "execution/result.xml"), Path.Combine(_root, "execution/result.trx"), Path.Combine(_root, "execution"),
            [new(First, "Example.First.Case"), new(Second, "Example.Second.Case")],
            MaximumConcurrency: 1, RequireOrder: true, SourceFingerprint: Fingerprint));
        _requestHash = Hash(_request); // Exact serialized bytes, never a reconstructed projection.
        _supervisor = NormalSupervisor();
        WriteJson(SupervisorFile, _supervisor);
    }

    [TearDown]
    public void RetainFailureOrDeleteOnlyOwnedInputs()
    {
        if (_root.Length == 0) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            RequireOwnedRoot();
            if (failed) { Note("supervisor-policy: failed admission inputs retained in owned temporary directory."); return; }
            foreach (var path in Directory.GetFileSystemEntries(_root))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                    throw new InvalidOperationException("supervisor-policy-unexpected-cleanup-entry");
                if ((attributes & FileAttributes.Directory) == 0) continue;
                if (!Path.GetFullPath(path).Equals(Path.Combine(_root, "execution"), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("supervisor-policy-unexpected-cleanup-directory");
                foreach (var child in Directory.GetFileSystemEntries(path))
                    if ((File.GetAttributes(child) & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                        throw new InvalidOperationException("supervisor-policy-unexpected-execution-entry");
            }
            Directory.Delete(_root, recursive: true);
            _ = RemainingRoot();
        }
        catch
        {
            if (!failed) throw new InvalidOperationException("supervisor-policy-fixture-cleanup-failed");
            Note("supervisor-policy: cleanup failed; primary failure and owned inputs retained.");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "infra.supervisor-policy/legacy-v1-compatible")]
    public async Task HistoricalV1WithoutRequestBindingRetainsOnlyItsExistingAdmissionBoundary()
    {
        _supervisor.Remove("requestSha256");
        _supervisor.Remove("policy");
        WriteJson(SupervisorFile, _supervisor);
        await RequireAcceptedAsync(bound: false);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.supervisor-policy/matching-binding")]
    public async Task ACompleteMatchingRequestPolicyAndEventOrderCanBeAdmitted()
    {
        await RequireAcceptedAsync(bound: false);
        await RequireAcceptedAsync(bound: true);
    }

    [TestCaseSource(nameof(RejectedBindings))]
    public async Task AChangedBindingCannotBorrowTheSameSuccessfulCaseSets(string fault)
    {
        await RequireAcceptedAsync(bound: false);
        await RequireAcceptedAsync(bound: true);
        var bindings = Bindings(bound: true);
        switch (fault)
        {
            case "request-hash-mismatch": _supervisor["requestSha256"] = new string('0', 64); break;
            case "request-hash-array": _supervisor["requestSha256"] = new JsonArray(_requestHash, "other"); break;
            case "request-bytes-changed": File.AppendAllText(_request, "\n", new UTF8Encoding(false)); break;
            case "maximum-concurrency-mismatch": _supervisor["policy"]!["maximumConcurrency"] = 2; break;
            case "require-order-mismatch": _supervisor["policy"]!["requireOrder"] = false; break;
            case "case-start-order-mismatch": _supervisor["events"] = JsonSerializer.SerializeToNode(Events(reverse: true), Json); break;
            case "partial-binding": bindings.Remove("ExpectedRequireOrder"); break;
            default: throw new InvalidOperationException("supervisor-policy-fixture-fault");
        }
        WriteJson(SupervisorFile, _supervisor);
        var result = await InvokeAsync(bindings);
        Assert.That(result.ExitCode, Is.Not.EqualTo(0), "supervisor-policy-invalid-binding-must-reject-not-exit-zero");
        Assert.That(result.Error, Does.Contain(Rejected), "Only the consumer's fixed admission rejection establishes this boundary.");
    }

    private static IEnumerable<TestCaseData> RejectedBindings()
    {
        foreach (var fault in new[] { "request-hash-mismatch", "request-hash-array", "request-bytes-changed", "maximum-concurrency-mismatch",
            "require-order-mismatch", "case-start-order-mismatch", "partial-binding" })
            yield return new TestCaseData(fault).SetName("supervisor-policy/reject/" + fault)
                .SetProperty("FishAudioCaseId", "infra.supervisor-policy/" + fault);
    }

    private JsonObject NormalSupervisor() => JsonSerializer.SerializeToNode(new
    {
        schemaVersion = 1, kind = "framework-execution", runId = RootRunId, sourceTree = Tree,
        sourceFingerprint = Fingerprint, testAssemblySha256 = AssemblyHash, requestSha256 = _requestHash,
        policy = new { maximumConcurrency = 1, requireOrder = true },
        launch = new { kind = "vstest", sessionId = (string?)null, invocationSha256 = InvocationHash,
            toolSha256 = (string?)null, vstestConsoleSha256 = (string?)null },
        selectedIds = new[] { First, Second },
        state = new { stage = "child-exit", failureStage = (string?)null, failureCaseId = (string?)null,
            warningCount = 0, complete = true, terminationRequired = false, passedIds = new[] { First, Second },
            failedIds = Array.Empty<string>(), timedOutIds = Array.Empty<string>(), activeIds = Array.Empty<string>(),
            interruptedIds = Array.Empty<string>(), notRunIds = Array.Empty<string>() },
        target = new { started = true, pid = TargetPid, exitCode = 0 },
        process = new { launcherPid = LauncherPid, launcherExited = true, stdoutDrained = true, stderrDrained = true, ownedActiveProcesses = 0 },
        rawEvidence = new { verified = true, resultXmlPath = Path.Combine(_root, "execution/result.xml"), trxPath = Path.Combine(_root, "execution/result.trx"),
            resultXmlSha256 = XmlHash, trxSha256 = TrxHash },
        failureCode = (string?)null, events = Events(reverse: false), warnings = Array.Empty<string>(),
        diagnostics = new { deliveredWarnings = 0, writerCompleted = true }, elapsedMilliseconds = 32,
        finalization = new { cleanupAnchorTimestamp = 1032L, frequency = 1000L }, baselineClaim = false,
    }, Json)!.AsObject();

    private static object[] Events(bool reverse)
    {
        var result = new List<object> { Launcher("target-started", 1000), Host("ready", 1001) };
        var cases = reverse
            ? new[] { (Second, "Example.Second.Case"), (First, "Example.First.Case") }
            : new[] { (First, "Example.First.Case"), (Second, "Example.Second.Case") };
        for (var index = 0; index < cases.Length; index++)
        {
            var at = 1010L + index * 10;
            var (id, fullName) = cases[index];
            result.Add(Host("case-start", at, id, fullName));
            result.Add(Host("queued", at + 1, id, fullName, 1, at + 1));
            result.Add(Host("entered", at + 2, id, fullName, 1, at + 1));
            result.Add(Host("completed", at + 3, id, fullName, 1, at + 1));
            result.Add(Host("case-finished", at + 4, id, fullName, outcome: "passed"));
        }
        result.Add(Host("cleanup-complete", 1030));
        result.Add(Launcher("target-exited", 1031, exitCode: 0));
        return result.ToArray();
    }

    // Match the serialized ExecutionControlRecord report shape, including null
    // optional fields. final-published is driver-local and is never a raw event.
    private static object Host(string kind, long timestamp, string? caseId = null, string? fullName = null,
        int ordinal = 0, long queuedTimestamp = 0, string? outcome = null) => new
    {
        schemaVersion = 1, role = "nunit-host", nonce = ControlNonce, runId = RootRunId, sourceTree = Tree,
        pid = HostPid, kind, timestamp, frequency = 1000L, assemblySha256 = AssemblyHash, caseId, fullName,
        ordinal, queuedTimestamp, outcome, targetPid = (int?)null, exitCode = (int?)null,
    };
    private static object Launcher(string kind, long timestamp, int? exitCode = null) => new
    {
        schemaVersion = 1, role = "execution-launcher", nonce = ControlNonce, runId = RootRunId, sourceTree = Tree,
        pid = LauncherPid, kind, timestamp, frequency = 1000L, assemblySha256 = (string?)null,
        caseId = (string?)null, fullName = (string?)null, ordinal = 0, queuedTimestamp = 0L,
        outcome = (string?)null, targetPid = (int?)TargetPid, exitCode,
    };

    private JsonObject Bindings(bool bound)
    {
        var result = new JsonObject { ["Path"] = Path.Combine(_root, SupervisorFile), ["RunId"] = RootRunId,
            ["SourceTree"] = Tree, ["SourceFingerprint"] = Fingerprint, ["AssemblySha256"] = AssemblyHash,
            ["CaseIds"] = new JsonArray(First, Second), ["ResultXmlSha256"] = XmlHash, ["TrxSha256"] = TrxHash };
        if (bound)
        {
            result["ExecutionRequestPath"] = _request;
            result["ExecutionRequestSha256"] = _requestHash;
            result["ExpectedMaximumConcurrency"] = 1;
            result["ExpectedRequireOrder"] = true;
        }
        return result;
    }

    private async Task RequireAcceptedAsync(bool bound)
    {
        var result = await InvokeAsync(Bindings(bound));
        Assert.That(result.ExitCode, Is.EqualTo(0), "supervisor-policy-normal-input-preflight-must-pass");
    }

    private async Task<ProcessResult> InvokeAsync(JsonObject bindings)
    {
        var label = "call-" + (++_invocations).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var invocation = Path.Combine(_root, label + ".json");
        WriteJson(label + ".json", new JsonObject { ["helper"] = _helper, ["bindings"] = bindings });
        var start = new ProcessStartInfo(_pwsh) { WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", Path.Combine(_root, "invoke.ps1"),
            "-InvocationPath", invocation }) start.ArgumentList.Add(argument);
        _ = RemainingRoot();
        using var process = new Process { StartInfo = start };
        Task<string>? stdout = null, stderr = null;
        Exception? primary = null;
        var started = false;
        try
        {
            started = process.Start();
            if (!started) throw new InvalidOperationException("supervisor-policy-process-start-failed");
            stdout = process.StandardOutput.ReadToEndAsync();
            stderr = process.StandardError.ReadToEndAsync();
            var completion = Task.WhenAll(process.WaitForExitAsync(), stdout, stderr);
            var warning = TimeSpan.FromSeconds(20) - _clock.Elapsed;
            if (warning <= TimeSpan.Zero || await Task.WhenAny(completion, Task.Delay(warning)) != completion)
                Note("supervisor-policy: same child still running after twenty seconds.");
            await completion.WaitAsync(RemainingRoot());
            _ = RemainingRoot();
            Capture(label, process, stdout, stderr, null);
            return new(process.ExitCode, await stderr);
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
                    if (stdout is not null && stderr is not null) await Task.WhenAll(stdout, stderr).WaitAsync(RemainingDrain(drain));
                    _ = RemainingDrain(drain);
                    Capture(label, process, stdout, stderr, primary);
                    _ = RemainingDrain(drain);
                }
                catch { Note("supervisor-policy: bounded process cleanup failed; primary failure retained."); }
            }
        }
    }

    private void Capture(string label, Process process, Task<string>? stdout, Task<string>? stderr, Exception? primary)
    {
        if (stdout?.IsCompletedSuccessfully == true) Write(label + ".stdout.log", stdout.Result);
        if (stderr?.IsCompletedSuccessfully == true) Write(label + ".stderr.log", stderr.Result);
        WriteJson(label + ".process.json", new { exited = process.HasExited, exitCode = process.HasExited ? process.ExitCode : (int?)null,
            stdoutDrained = stdout?.IsCompletedSuccessfully == true, stderrDrained = stderr?.IsCompletedSuccessfully == true,
            failureType = primary?.GetType().Name, windowsJobClaim = false });
    }

    private TimeSpan RemainingRoot()
    {
        var remaining = TimeSpan.FromSeconds(120) - _clock.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException("supervisor-policy-root-deadline");
    }
    private static TimeSpan RemainingDrain(Stopwatch clock)
    {
        var remaining = TimeSpan.FromSeconds(10) - clock.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException("supervisor-policy-drain-deadline");
    }
    private void Write(string name, string value) => File.WriteAllText(Path.Combine(_root, name), value, new UTF8Encoding(false));
    private void WriteJson(string name, object value) => Write(name, JsonSerializer.Serialize(value, Json));
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(input)); }
    private static void Note(string value) { try { TestContext.Progress.WriteLine(value); } catch { } }
    private sealed record ProcessResult(int ExitCode, string Error);

    private void RequireOwnedRoot()
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
        var name = Path.GetFileName(full);
        if (!Path.IsPathFullyQualified(_root) || full != _root || Path.GetDirectoryName(full) != _parent ||
            !name.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[Prefix.Length..], "N", out _))
            throw new InvalidOperationException("supervisor-policy-owner-invalid");
        RequireDirectories(full);
    }
    private static void RequireDirectories(string path)
    {
        for (DirectoryInfo? item = new(path); item is not null; item = item.Parent)
            if (!item.Exists || (item.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidOperationException("supervisor-policy-directory-invalid");
    }
    private static string RepositoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("FISHAUDIO_TEST_REPOSITORY_ROOT");
        string? snapshot = null;
        if (Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT") is { Length: > 0 } path)
        {
            using var context = JsonDocument.Parse(File.ReadAllText(path));
            snapshot = context.RootElement.GetProperty("root").GetString();
        }
        foreach (var value in new[] { configured, snapshot }.OfType<string>())
            if (!Path.IsPathFullyQualified(value)) throw new InvalidOperationException("supervisor-policy-repository-not-absolute");
        if (configured is not null && snapshot is not null && !Path.GetFullPath(configured).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(snapshot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("supervisor-policy-repository-mismatch");
        var root = configured ?? snapshot ?? throw new InvalidOperationException("supervisor-policy-explicit-repository-required");
        RequireDirectories(root);
        return Path.GetFullPath(root);
    }
    private static string FindPwsh()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.Combine(directory.Trim('"'), "pwsh.exe");
            if (File.Exists(path)) return Path.GetFullPath(path);
        }
        throw new InvalidOperationException("supervisor-policy-pwsh-missing");
    }

    private const string InvocationBoundary = """
        #requires -Version 7.0
        param([Parameter(Mandatory)][string]$InvocationPath)
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        try {
            $call = Get-Content -Raw -LiteralPath $InvocationPath | ConvertFrom-Json -AsHashtable
            $bindings = $call.bindings
            $bindings['CaseIds'] = [string[]]$bindings['CaseIds']
            if ($bindings.ContainsKey('ExpectedMaximumConcurrency')) { $bindings['ExpectedMaximumConcurrency'] = [int]$bindings['ExpectedMaximumConcurrency'] }
            if ($bindings.ContainsKey('ExpectedRequireOrder')) { $bindings['ExpectedRequireOrder'] = [bool]$bindings['ExpectedRequireOrder'] }
            & $call.helper @bindings
            exit 0
        } catch {
            if ($_.Exception.Message -ceq 'framework-supervisor-evidence-rejected') {
                [Console]::Error.WriteLine('framework-supervisor-evidence-rejected')
            } else {
                [Console]::Error.WriteLine('supervisor-policy-fixture-unexpected-error')
            }
            exit 1
        }
        """;
}
