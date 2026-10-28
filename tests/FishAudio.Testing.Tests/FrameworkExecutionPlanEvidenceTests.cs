using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FishAudio.Testing.Evidence;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class FrameworkExecutionPlanEvidenceTests
{
    private const string Prefix = "fa-plan-evidence-";
    private const string RawHash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private const string FreshRawHash = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
    private const string OrdinaryA = "plan.ordinary/a", OrdinaryB = "plan.ordinary/b";
    private const string FreshA = "plan.fresh/a", FreshB = "plan.fresh/b";
    private const string ProfileFirst = "plan.profile/first", ProfileSecond = "plan.profile/second";
    private const string Profile = "native-profile";
    private const string Rejected = "framework-execution-plan-rejected";
    private static readonly string[] Requested = [OrdinaryA, FreshA, FreshB, ProfileSecond, OrdinaryB];
    private static readonly string[] Expanded = [OrdinaryA, FreshA, FreshB, ProfileSecond, ProfileFirst, OrdinaryB];
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private readonly Stopwatch _clock = new();
    private string _parent = "", _root = "", _helper = "", _pwsh = "";
    private JsonObject _plan = null!;
    private JsonArray _records = null!, _selection = null!;
    private int _invocations;

    [SetUp]
    public void CreateOwnedSyntheticAdmissionInputs()
    {
        _clock.Start();
        var repository = RepositoryRoot();
        _pwsh = FindPwsh();
        _parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        _root = Path.Combine(_parent, Prefix + Guid.NewGuid().ToString("N"));
        if (File.Exists(_root) || Directory.Exists(_root)) throw new InvalidOperationException("plan-evidence-owner-exists");
        Directory.CreateDirectory(_root);
        RequireOwnedRoot();
        _helper = Path.Combine(_root, "Assert-FrameworkExecutionPlan.ps1");
        File.Copy(Path.Combine(repository, "tools/Assert-FrameworkExecutionPlan.ps1"), _helper, overwrite: false);
        Write("invoke.ps1", InvocationBoundary);
        _plan = NormalPlan(freshDiscovery: false);
        _records = NormalPhaseRecords();
        _selection = Ids(Expanded);
        WriteJson("producer-plan.json", _plan);
        WriteJson("expected-plan.json", NormalPlan(freshDiscovery: true));
    }

    [TearDown]
    public void RetainFailureOrDeleteOnlyOwnedInputs()
    {
        if (_root.Length == 0) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            RequireOwnedRoot();
            if (failed) { Note("plan-evidence: failed synthetic inputs retained in owned temporary directory."); return; }
            foreach (var path in Directory.GetFileSystemEntries(_root))
                if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                    throw new InvalidOperationException("plan-evidence-unexpected-cleanup-entry");
            Directory.Delete(_root, recursive: true);
            _ = RemainingRoot();
        }
        catch
        {
            if (!failed) throw new InvalidOperationException("plan-evidence-cleanup-failed");
            Note("plan-evidence: cleanup failed; primary failure retained.");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "infra.execution-plan-evidence/mixed-fresh-platform-identities")]
    public async Task TheMixedPlanMatchesFreshDiscoveryDespiteDifferentPlatformIdsAndRawHash()
        => await RequireAcceptedAsync();

    [TestCaseSource(nameof(RejectedPlans))]
    public async Task AChangedPhaseShapeCannotBorrowTheSameSuccessfulCaseUnion(string fault)
    {
        await RequireAcceptedAsync();
        var phases = _plan["plan"]!["phases"]!.AsArray();
        switch (fault)
        {
            case "collapsed-fresh":
                phases[1]!["cases"]!.AsArray().Add(phases[2]!["cases"]![0]!.DeepClone());
                phases.RemoveAt(2);
                phases[2]!["id"] = "phase-0003";
                _records[1]!["selectedIds"] = Ids(FreshA, FreshB);
                _records[1]!["passedCount"] = 2;
                _records.RemoveAt(2);
                _records[2]!["id"] = "phase-0003";
                break;
            case "profile-reordered":
                phases[3]!["cases"] = Reverse(phases[3]!["cases"]!.AsArray());
                _records[3]!["selectedIds"] = Ids(ProfileSecond, ProfileFirst);
                break;
            case "wrong-concurrency": phases[0]!["maximumConcurrency"] = 2; _records[0]!["maximumConcurrency"] = 2; break;
            case "wrong-order": phases[3]!["requireOrder"] = false; _records[3]!["requireOrder"] = false; break;
            case "wrong-resource": phases[0]!["exclusiveResources"] = Ids("foreign-resource"); _records[0]!["exclusiveResources"] = Ids("foreign-resource"); break;
            case "missing-phase": _records.RemoveAt(2); break;
            case "duplicate-phase": _records.Add(_records[1]!.DeepClone()); break;
            case "extra-phase": _records.Add(PhaseRecord("phase-9999", "fresh-case", null, false, 1, [], "plan.foreign/extra")); break;
            case "phase-record-order":
                var first = _records[1]!.DeepClone(); var second = _records[2]!.DeepClone();
                _records[1] = second; _records[2] = first;
                break;
            case "raw-hash": _plan["plan"]!["rawDiscoverySha256"] = new string('0', 64); break;
            case "unknown-identity": phases[0]!["cases"]![0]!["fullName"] = "Example.Foreign.Unrelated"; break;
            case "expanded-selection-missing": _selection = Ids(OrdinaryA, FreshA, FreshB, ProfileSecond, OrdinaryB); break;
            case "expansion-reason": _plan["plan"]!["expansionReasons"]![0]!["addedIds"] = new JsonArray(); break;
            case "truncated-json": break;
            default: throw new InvalidOperationException("plan-evidence-fixture-fault");
        }
        if (fault == "truncated-json") Write("producer-plan.json", "{");
        else WriteJson("producer-plan.json", _plan);
        var result = await InvokeAsync();
        Assert.That(result.ExitCode, Is.Not.EqualTo(0), "framework-execution-plan-invalid-evidence-must-reject-not-exit-zero");
        Assert.That(result.Error, Does.Contain(Rejected), "Only the consumer's fixed rejection establishes this admission boundary.");
    }

    private static IEnumerable<TestCaseData> RejectedPlans()
    {
        foreach (var fault in new[] { "collapsed-fresh", "profile-reordered", "wrong-concurrency", "wrong-order", "wrong-resource",
            "missing-phase", "duplicate-phase", "extra-phase", "phase-record-order", "raw-hash", "unknown-identity",
            "expanded-selection-missing", "expansion-reason", "truncated-json" })
            yield return new TestCaseData(fault).SetName("execution-plan-evidence/reject/" + fault)
                .SetProperty("FishAudioCaseId", "infra.execution-plan-evidence/" + fault);
    }

    // Hand-authored synthetic CLI output; no discovery, metadata CLI or test is run.
    private static JsonObject NormalPlan(bool freshDiscovery)
    {
        DiscoveredNUnitCase Case(string id, string name, string platform) => new(id, "Example.Cases." + name,
            (freshDiscovery ? "fresh-" : "captured-") + platform, "Example.Cases", "Rows");
        NUnitExecutionPhase Phase(string id, string kind, string? profile, bool order, int concurrency,
            string[] resources, params DiscoveredNUnitCase[] cases) => new(id, kind, profile, cases, order, concurrency, resources);
        var plan = new NUnitExecutionPlanResult(freshDiscovery ? FreshRawHash : RawHash, Requested, Expanded,
            [new("complete-profile", Profile, [ProfileSecond], [ProfileFirst])],
            [Phase("phase-0001", "ordinary", null, false, 1, ["desktop"],
                Case(OrdinaryA, "OrdinaryA", "oa"), Case(OrdinaryB, "OrdinaryB", "ob")),
             Phase("phase-0002", "fresh-case", null, false, 1, ["credentials"], Case(FreshA, "FreshA", "fa")),
             Phase("phase-0003", "fresh-case", null, false, 1, ["credentials"], Case(FreshB, "FreshB", "fb")),
             Phase("phase-0004", "ordered-profile", Profile, true, 1, ["desktop", "keyboard"],
                Case(ProfileFirst, "ProfileFirst", "p0"), Case(ProfileSecond, "ProfileSecond", "p1"))]);
        return JsonSerializer.SerializeToNode(new { schemaVersion = 1, kind = "nunit-execution-plan", complete = true, plan }, Json)!.AsObject();
    }

    private static JsonArray NormalPhaseRecords() => new(
        PhaseRecord("phase-0001", "ordinary", null, false, 1, ["desktop"], OrdinaryA, OrdinaryB),
        PhaseRecord("phase-0002", "fresh-case", null, false, 1, ["credentials"], FreshA),
        PhaseRecord("phase-0003", "fresh-case", null, false, 1, ["credentials"], FreshB),
        PhaseRecord("phase-0004", "ordered-profile", Profile, true, 1, ["desktop", "keyboard"], ProfileFirst, ProfileSecond));

    private static JsonObject PhaseRecord(string id, string kind, string? profile, bool requireOrder,
        int maximumConcurrency, string[] exclusiveResources, params string[] selectedIds)
        => JsonSerializer.SerializeToNode(new { id, kind, profile, requireOrder, maximumConcurrency,
            exclusiveResources, selectedIds, executed = true, passedCount = selectedIds.Length }, Json)!.AsObject();
    private static JsonArray Ids(params string[] ids) => new(ids.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
    private static JsonArray Reverse(JsonArray items) => new(items.Reverse().Select(item => item!.DeepClone()).ToArray());

    private async Task RequireAcceptedAsync()
    {
        var result = await InvokeAsync();
        Assert.That(result.ExitCode, Is.EqualTo(0), "framework-execution-plan-normal-input-preflight-must-pass");
    }
    private async Task<ProcessResult> InvokeAsync()
    {
        var label = "call-" + (++_invocations).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var invocation = Path.Combine(_root, label + ".json");
        WriteJson(label + ".json", new JsonObject { ["helper"] = _helper,
            ["planPath"] = Path.Combine(_root, "producer-plan.json"), ["expectedPlanPath"] = Path.Combine(_root, "expected-plan.json"),
            ["rawDiscoverySha256"] = RawHash, ["requestedIds"] = Ids(Requested),
            ["selectionIds"] = _selection.DeepClone(), ["phaseRecords"] = _records.DeepClone() });
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
            if (!started) throw new InvalidOperationException("plan-evidence-process-start-failed");
            stdout = process.StandardOutput.ReadToEndAsync(); stderr = process.StandardError.ReadToEndAsync();
            var completion = Task.WhenAll(process.WaitForExitAsync(), stdout, stderr);
            var warning = TimeSpan.FromSeconds(20) - _clock.Elapsed;
            if (warning <= TimeSpan.Zero || await Task.WhenAny(completion, Task.Delay(warning)) != completion)
                Note("plan-evidence: same child still running after twenty seconds.");
            await completion.WaitAsync(RemainingRoot()); _ = RemainingRoot();
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
                    _ = RemainingDrain(drain); Capture(label, process, stdout, stderr, primary); _ = RemainingDrain(drain);
                }
                catch { Note("plan-evidence: bounded process cleanup failed; primary failure retained."); }
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
        return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException("plan-evidence-root-deadline");
    }
    private static TimeSpan RemainingDrain(Stopwatch clock)
    {
        var remaining = TimeSpan.FromSeconds(10) - clock.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException("plan-evidence-drain-deadline");
    }
    private void Write(string name, string value) => File.WriteAllText(Path.Combine(_root, name), value, new UTF8Encoding(false));
    private void WriteJson(string name, object value) => Write(name, JsonSerializer.Serialize(value, Json));
    private static void Note(string value) { try { TestContext.Progress.WriteLine(value); } catch { } }
    private sealed record ProcessResult(int ExitCode, string Error);

    private void RequireOwnedRoot()
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root)); var name = Path.GetFileName(full);
        if (!Path.IsPathFullyQualified(_root) || full != _root || Path.GetDirectoryName(full) != _parent ||
            !name.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[Prefix.Length..], "N", out _))
            throw new InvalidOperationException("plan-evidence-owner-invalid");
        RequireDirectories(full);
    }
    private static void RequireDirectories(string path)
    {
        for (DirectoryInfo? item = new(path); item is not null; item = item.Parent)
            if (!item.Exists || (item.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidOperationException("plan-evidence-directory-invalid");
    }
    private static string RepositoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("FISHAUDIO_TEST_REPOSITORY_ROOT"); string? snapshot = null;
        if (Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT") is { Length: > 0 } path)
        { using var context = JsonDocument.Parse(File.ReadAllText(path)); snapshot = context.RootElement.GetProperty("root").GetString(); }
        foreach (var value in new[] { configured, snapshot }.OfType<string>())
            if (!Path.IsPathFullyQualified(value)) throw new InvalidOperationException("plan-evidence-repository-not-absolute");
        if (configured is not null && snapshot is not null && !Path.GetFullPath(configured).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(snapshot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("plan-evidence-repository-mismatch");
        var root = configured ?? snapshot ?? throw new InvalidOperationException("plan-evidence-explicit-repository-required");
        RequireDirectories(root); return Path.GetFullPath(root);
    }
    private static string FindPwsh()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        { var path = Path.Combine(directory.Trim('"'), "pwsh.exe"); if (File.Exists(path)) return Path.GetFullPath(path); }
        throw new InvalidOperationException("plan-evidence-pwsh-missing");
    }

    private const string InvocationBoundary = """
        #requires -Version 7.0
        param([Parameter(Mandatory)][string]$InvocationPath)
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        try {
            $call = Get-Content -Raw -LiteralPath $InvocationPath | ConvertFrom-Json -Depth 40
            $bindings = @{
                PlanPath = $call.planPath; ExpectedPlanPath = $call.expectedPlanPath
                RawDiscoverySha256 = $call.rawDiscoverySha256
                RequestedIds = [string[]]$call.requestedIds; SelectionIds = [string[]]$call.selectionIds
                PhaseRecords = [object[]]$call.phaseRecords
            }
            & $call.helper @bindings
            exit 0
        } catch {
            if ($_.Exception.Message -ceq 'framework-execution-plan-rejected') {
                [Console]::Error.WriteLine('framework-execution-plan-rejected')
            } else {
                [Console]::Error.WriteLine('execution-plan-evidence-fixture-unexpected-error')
            }
            exit 1
        }
        """;
}
