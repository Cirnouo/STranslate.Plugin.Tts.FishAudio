using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class CompatibilityCommandTests
{
    private const string OwnerPrefix = "fa-compat-command-";
    private readonly Stopwatch _clock = new();
    private string _repositoryRoot = "";
    private string _pwsh = "";
    private string _parent = "";
    private string _root = "";

    [SetUp]
    public void PrepareOwnedCommand()
    {
        _clock.Start();
        _repositoryRoot = ResolveRepositoryRoot();
        _pwsh = ResolvePowerShell();
        var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
        _parent = RequireDirectory(string.IsNullOrEmpty(configured) ? Path.GetTempPath() : configured);
        _root = Path.Combine(_parent, OwnerPrefix + Guid.NewGuid().ToString("N"));
        Require(!Directory.Exists(_root) && !File.Exists(_root), "compatibility-command-owner-exists");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void ReleaseOwnedCommand()
    {
        if (string.IsNullOrEmpty(_root) || !Directory.Exists(_root)) return;
        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
        {
            TestContext.Error.WriteLine("compatibility-command: failed raw fixture retained as " + Path.GetFileName(_root) + ".");
            return;
        }
        _ = RemainingRoot();
        var full = RequireDirectory(_root);
        var name = Path.GetFileName(full);
        Require(string.Equals(Path.GetDirectoryName(full), _parent, StringComparison.OrdinalIgnoreCase) &&
            name.StartsWith(OwnerPrefix, StringComparison.Ordinal) && Guid.TryParseExact(name[OwnerPrefix.Length..], "N", out _),
            "compatibility-command-cleanup-owner-invalid");
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new(full));
        while (pending.TryPop(out var directory))
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                Require((entry.Attributes & FileAttributes.ReparsePoint) == 0, "compatibility-command-cleanup-reparse");
                if (entry is DirectoryInfo child) pending.Push(child);
            }
        Directory.Delete(full, recursive: true);
        _ = RemainingRoot();
    }

    [Test]
    [Property("FishAudioCaseId", "compatibility-command/unknown-scenario-is-nonzero")]
    public async Task UnknownScenarioCannotReturnAStaleSuccessOrEnterTheCoordinatorBody()
    {
        var results = Path.Combine(_root, "invalid-scenario-results");
        var exit = await RunPublicCommandAsync("-Scenario", "NotACompatibilityScenario", "-ResultsDirectory", results);
        Assert.That(exit, Is.Not.Zero);
        Assert.That(Directory.Exists(results) || File.Exists(results), Is.False);
        Assert.That(ErrorText().TrimEnd('\r', '\n'), Is.EqualTo("test-driver: invalid compatibility invocation."));
    }

    [Test]
    [Property("FishAudioCaseId", "compatibility-command/existing-results-remain-untouched")]
    public async Task RejectedExistingResultsDoNotAcquireOrOverwriteFailureEvidence()
    {
        var results = Path.Combine(_root, "existing-results");
        Directory.CreateDirectory(results);
        var sentinels = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["sentinel.bin"] = [0x00, 0xff, 0xc3, 0x28, 0x0d, 0x0a],
            ["private-failure.txt"] = [0xfe, 0x00, 0x80, 0x41, 0x0a],
            ["compatibility.json"] = Encoding.UTF8.GetBytes("{\"existingOwner\":true}\r\n"),
        };
        foreach (var sentinel in sentinels) WriteNew(Path.Combine(results, sentinel.Key), sentinel.Value);

        var exit = await RunPublicCommandAsync("-Scenario", "Performance", "-ResultsDirectory", results,
            "-EvidenceDirectory", Path.Combine(_root, "missing-evidence"));
        Assert.That(exit, Is.Not.Zero);
        Assert.That(Directory.EnumerateFileSystemEntries(results).Select(Path.GetFileName), Is.EquivalentTo(sentinels.Keys));
        foreach (var sentinel in sentinels)
            Assert.That(File.ReadAllBytes(Path.Combine(results, sentinel.Key)), Is.EqualTo(sentinel.Value), sentinel.Key);
    }

    [Test]
    [Property("FishAudioCaseId", "compatibility-command/missing-performance-evidence-is-incomplete")]
    public async Task MissingPerformanceEvidenceProducesOnlyAnOwnedIncompleteManifest()
    {
        var results = Path.Combine(_root, "performance-results");
        var exit = await RunPublicCommandAsync("-Scenario", "Performance", "-ResultsDirectory", results,
            "-EvidenceDirectory", Path.Combine(_root, "missing-evidence"));
        Assert.That(exit, Is.Not.Zero);
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(results, "compatibility.json")));
        var record = manifest.RootElement;
        Assert.That(record.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(record.GetProperty("complete").GetBoolean(), Is.False);
        Assert.That(record.GetProperty("baselineClaim").GetBoolean(), Is.False);
        Assert.That(record.GetProperty("commitReceiptClaim").GetBoolean(), Is.False);
        Assert.That(record.GetProperty("cases").GetArrayLength(), Is.Zero);
        Assert.That(record.GetProperty("failure").GetProperty("stage").GetString(), Is.EqualTo("measure-existing-evidence"));
        Assert.That(File.Exists(Path.Combine(results, "private-failure.txt")), Is.True);
        Assert.That(Directory.EnumerateFileSystemEntries(results).Select(Path.GetFileName),
            Is.EquivalentTo(new[] { "compatibility.json", "private-failure.txt" }));
    }

    [TestCase(true, TestName = "compatibility-command/scenario-requires-compatibility-mode")]
    [TestCase(false, TestName = "compatibility-command/nobuild-does-not-apply-to-compatibility")]
    public async Task MisplacedModeParametersRejectBeforeRepositoryOrResultsWork(bool scenarioOutsideCompatibility)
    {
        var results = Path.Combine(_root, "rejected-mode-results");
        var invocationRoot = Path.Combine(_root, "nonexistent-repository");
        // If the mode guard regresses, ordinary repository preflight rejects
        // this nonexistent root before discovery or any full-suite execution.
        var mode = scenarioOutsideCompatibility ? "Full" : "Compatibility";
        var arguments = scenarioOutsideCompatibility
            ? new[] { "-Scenario", "IsolatedCase", "-ResultsDirectory", results }
            : new[] { "-NoBuild", "-ResultsDirectory", results };
        var expectedError = scenarioOutsideCompatibility
            ? "test-driver: compatibility parameters require compatibility mode."
            : "test-driver: parameters do not apply to compatibility mode.";

        var exit = await RunPublicCommandAsync(mode, invocationRoot, arguments);
        Assert.That(exit, Is.EqualTo(2));
        Assert.That(ErrorText().TrimEnd('\r', '\n'), Is.EqualTo(expectedError));
        Assert.That(Directory.Exists(results) || File.Exists(results), Is.False);
        Assert.That(Directory.Exists(invocationRoot) || File.Exists(invocationRoot), Is.False);
        Assert.That(Directory.EnumerateFileSystemEntries(_root).Select(Path.GetFileName),
            Is.EquivalentTo(new[] { "argv.json", "stdout.bin", "stderr.bin", "process.json" }));
    }

    // Public command failures must precede source preflight: the deliberately
    // missing invocation root is a second guard against entering a full run.
    public static IEnumerable<TestCaseData> RejectedFileSelections()
    {
        yield return FileSelectionRow("empty", "{\"schemaVersion\":1,\"ids\":[]}", "Full", false,
            "test-driver-empty-or-invalid-selection");
        yield return FileSelectionRow("duplicate", "{\"schemaVersion\":1,\"ids\":[\"compat.sync/api-key-format\",\"compat.sync/api-key-format\"]}", "Full", false,
            "test-driver-invalid-or-duplicate-case-id");
        yield return FileSelectionRow("truncated", "{\"schemaVersion\":1,\"ids\":[", "Full", false,
            "test-driver-invalid-selection-input");
        yield return FileSelectionRow("conflicting-arguments", "{\"schemaVersion\":1,\"ids\":[\"compat.sync/api-key-format\"]}", "Full", true,
            "test-driver-conflicting-selection-input");
        yield return FileSelectionRow("discovery-mode", "{\"schemaVersion\":1,\"ids\":[\"compat.sync/api-key-format\"]}", "Discover", false,
            "test-driver-conflicting-selection-input");
        yield return FileSelectionRow("compatibility-mode", "{\"schemaVersion\":1,\"ids\":[\"compat.sync/api-key-format\"]}", "Compatibility", false,
            "test-driver: file selection requires Full mode.");
    }

    private static TestCaseData FileSelectionRow(string key, string json, string mode, bool includeCaseId, string reason)
        => new TestCaseData(json, mode, includeCaseId, reason).SetName("file-selection/" + key)
            .SetProperty("FishAudioCaseId", "test-driver-file-selection/reject-" + key);

    [TestCaseSource(nameof(RejectedFileSelections))]
    public async Task RejectedFileSelectionCannotEnterSourcePreflightOrOverwriteInput(
        string json, string mode, bool includeCaseId, string reason)
    {
        var input = Path.Combine(_root, "selection.json");
        var bytes = new UTF8Encoding(false).GetBytes(json);
        WriteNew(input, bytes);
        var results = Path.Combine(_root, "must-not-create-results");
        var invocationRoot = Path.Combine(_root, "must-not-enter-repository");
        var arguments = new List<string> { "-SelectionPath", input, "-ResultsDirectory", results };
        if (includeCaseId) arguments.AddRange(["-CaseId", "compat.sync/api-key-format"]);
        var exit = await RunPublicCommandAsync(mode, invocationRoot, arguments.ToArray());
        Assert.That(exit, Is.Not.Zero);
        // The public guard owns this fixed ASCII diagnostic. Keep localized
        // PowerShell binding bytes unchanged instead of guessing their encoding.
        var expectedDiagnostic = mode == "Compatibility" ? reason : "reason=" + reason + " ";
        Assert.That(File.ReadAllBytes(Path.Combine(_root, "stderr.bin")).AsSpan().IndexOf(Encoding.ASCII.GetBytes(expectedDiagnostic)),
            Is.GreaterThanOrEqualTo(0), "selection-file-guard-must-publish-controlled-ASCII-reason");
        Assert.That(Directory.Exists(results) || File.Exists(results), Is.False);
        Assert.That(Directory.Exists(invocationRoot) || File.Exists(invocationRoot), Is.False);
        Assert.That(File.ReadAllBytes(input), Is.EqualTo(bytes), "Rejected file selection retains its exact source bytes.");
    }
    private string ErrorText() => new UTF8Encoding(false, true).GetString(File.ReadAllBytes(Path.Combine(_root, "stderr.bin")));

    private Task<int> RunPublicCommandAsync(params string[] arguments)
        => RunPublicCommandAsync("Compatibility", _repositoryRoot, arguments);

    private async Task<int> RunPublicCommandAsync(string mode, string invocationRepositoryRoot, string[] arguments)
    {
        _ = RemainingRoot();
        var start = new ProcessStartInfo(_pwsh)
        {
            WorkingDirectory = _repositoryRoot, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File",
            Path.Combine(_repositoryRoot, "tools", "Test.ps1"), "-Mode", mode, "-RepositoryRoot", invocationRepositoryRoot })
            start.ArgumentList.Add(argument);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.Environment["VSLANG"] = "1033";
        WriteNew(Path.Combine(_root, "argv.json"), JsonSerializer.SerializeToUtf8Bytes(new
        {
            executable = _pwsh, arguments = start.ArgumentList.ToArray(), workingDirectory = _repositoryRoot,
        }));
        using var process = new Process { StartInfo = start };
        using var stdoutFile = new FileStream(Path.Combine(_root, "stdout.bin"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var stderrFile = new FileStream(Path.Combine(_root, "stderr.bin"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var warningCancellation = new CancellationTokenSource();
        Task? stdout = null;
        Task? stderr = null;
        Task? exit = null;
        Exception? primary = null;
        var started = false;
        try
        {
            started = process.Start();
            Require(started, "compatibility-command-not-started");
            process.StandardInput.Close();
            stdout = process.StandardOutput.BaseStream.CopyToAsync(stdoutFile);
            stderr = process.StandardError.BaseStream.CopyToAsync(stderrFile);
            exit = process.WaitForExitAsync();
            var pending = new List<Task> { exit, stdout, stderr };
            var untilWarning = TimeSpan.FromSeconds(20) - _clock.Elapsed;
            var warning = Task.Delay(untilWarning > TimeSpan.Zero ? untilWarning : TimeSpan.Zero, warningCancellation.Token);
            var warned = false;
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(warned ? pending : pending.Append(warning)).WaitAsync(RemainingRoot());
                if (!warned && completed == warning)
                {
                    warned = true;
                    TestContext.Progress.WriteLine("compatibility-command: same command still running after twenty seconds.");
                    continue;
                }
                await completed;
                pending.Remove(completed);
            }
            _ = RemainingRoot();
            return process.ExitCode;
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            warningCancellation.Cancel();
            if (primary is not null && started)
            {
                var cleanup = Stopwatch.StartNew();
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    exit ??= process.WaitForExitAsync();
                    await Task.WhenAll(new[] { exit, stdout, stderr }.OfType<Task>()).WaitAsync(RemainingCleanup(cleanup));
                    _ = RemainingCleanup(cleanup);
                }
                catch (Exception cleanupError)
                {
                    TestContext.Error.WriteLine("compatibility-command: bounded cleanup failed (" + cleanupError.GetType().Name + ").");
                }
            }
            try
            {
                stdoutFile.Flush(flushToDisk: true);
                stderrFile.Flush(flushToDisk: true);
                WriteNew(Path.Combine(_root, "process.json"), JsonSerializer.SerializeToUtf8Bytes(new
                {
                    processId = started ? process.Id : (int?)null, exited = started && process.HasExited,
                    exitCode = started && process.HasExited ? process.ExitCode : (int?)null,
                    stdoutDrained = stdout?.IsCompletedSuccessfully == true, stderrDrained = stderr?.IsCompletedSuccessfully == true,
                    elapsedMilliseconds = _clock.Elapsed.TotalMilliseconds, failureType = primary?.GetType().Name,
                    windowsJobClaim = false,
                }));
            }
            catch (Exception evidenceError) when (primary is not null)
            {
                TestContext.Error.WriteLine("compatibility-command: failure evidence could not finish (" + evidenceError.GetType().Name + ").");
            }
        }
    }

    private TimeSpan RemainingRoot()
    {
        var remaining = TimeSpan.FromSeconds(120) - _clock.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException("compatibility-command-root-deadline");
    }

    private static TimeSpan RemainingCleanup(Stopwatch clock)
    {
        var remaining = TimeSpan.FromSeconds(10) - clock.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException("compatibility-command-cleanup-deadline");
    }

    private static string ResolveRepositoryRoot()
    {
        var explicitRoot = Environment.GetEnvironmentVariable("FISHAUDIO_TEST_REPOSITORY_ROOT");
        var contextPath = Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT");
        string? snapshotRoot = null;
        JsonDocument? context = null;
        try
        {
            if (!string.IsNullOrEmpty(contextPath))
            {
                RequireFile(contextPath);
                Require(new FileInfo(contextPath).Length is > 0 and <= 16 * 1024 * 1024, "compatibility-command-context-size");
                context = JsonDocument.Parse(File.ReadAllBytes(contextPath));
                var record = context.RootElement;
                Require(record.GetProperty("schemaVersion").GetInt32() == 1 &&
                    record.GetProperty("tree").GetString() is { Length: 40 or 64 } tree && tree.All(Uri.IsHexDigit) &&
                    record.GetProperty("files").GetArrayLength() > 0, "compatibility-command-context-invalid");
                snapshotRoot = RequireDirectory(record.GetProperty("root").GetString() ?? "");
            }
            if (!string.IsNullOrEmpty(explicitRoot))
            {
                explicitRoot = RequireDirectory(explicitRoot);
                Require(snapshotRoot is null || string.Equals(snapshotRoot, explicitRoot, StringComparison.OrdinalIgnoreCase),
                    "compatibility-command-context-root-mismatch");
            }
            var root = snapshotRoot ?? explicitRoot;
            Require(!string.IsNullOrEmpty(root), "compatibility-command-explicit-root-required");
            foreach (var relative in new[] { "global.json", "tools/Test.ps1", "tools/TestCompatibility.ps1" })
            {
                var path = Path.Combine(root!, relative);
                RequireFile(path);
                if (context is null) continue;
                var matches = context.RootElement.GetProperty("files").EnumerateArray()
                    .Where(item => item.GetProperty("path").GetString() == relative).ToArray();
                Require(matches.Length == 1 && matches[0].GetProperty("sha256").GetString() ==
                    Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), "compatibility-command-snapshot-input-changed");
            }
            return root!;
        }
        finally { context?.Dispose(); }
    }

    private static string ResolvePowerShell()
    {
        if (Environment.GetEnvironmentVariable("FISHAUDIO_PWSH_EXECUTABLE") is { Length: > 0 } configured)
        {
            RequireFile(configured);
            return Path.GetFullPath(configured);
        }
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.Combine(directory.Trim('"'), "pwsh.exe");
            if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) continue;
            RequireFile(path);
            return Path.GetFullPath(path);
        }
        throw new InvalidOperationException("compatibility-command-pwsh-missing");
    }

    private static string RequireDirectory(string path)
    {
        Require(Path.IsPathFullyQualified(path), "compatibility-command-directory-not-absolute");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        for (DirectoryInfo? directory = new(full); directory is not null; directory = directory.Parent)
            Require(directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) == 0,
                "compatibility-command-directory-missing-or-reparse");
        return full;
    }

    private static void RequireFile(string path)
    {
        Require(Path.IsPathFullyQualified(path), "compatibility-command-file-not-absolute");
        var file = new FileInfo(path);
        Require(file.Exists && (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0,
            "compatibility-command-file-missing-or-reparse");
        _ = RequireDirectory(file.DirectoryName!);
    }

    private static void WriteNew(string path, byte[] bytes)
    {
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        output.Write(bytes);
        output.Flush(flushToDisk: true);
    }

    private static void Require(bool condition, string code)
    {
        if (!condition) throw new InvalidOperationException(code);
    }
}
