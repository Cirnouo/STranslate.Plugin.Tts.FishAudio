using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests.ToolTests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class ProcessTransportTests
{
    private const string OwnerPrefix = "fa-transport-";
    private readonly Stopwatch _clock = new();
    private string _repositoryRoot = "";
    private string _pwsh = "";
    private string _shim = "";
    private string _parent = "";
    private string _root = "";

    [SetUp]
    public void PrepareOwnedTransport()
    {
        _clock.Start();
        _repositoryRoot = ResolveRepositoryRoot();
        _shim = Path.Combine(_repositoryRoot, "tools", "Invoke-TestProcess.ps1");
        RequireRegularFile(_shim);
        _pwsh = Environment.GetEnvironmentVariable("FISHAUDIO_PWSH_EXECUTABLE") ?? "";
        RequireRegularFile(_pwsh);

        var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
        var parent = string.IsNullOrEmpty(configured) ? Path.GetTempPath() : configured;
        _parent = RequireExistingDirectory(parent);
        _root = Path.Combine(_parent, OwnerPrefix + Guid.NewGuid().ToString("N"));
        Require(!Directory.Exists(_root) && !File.Exists(_root), "process-transport-owner-exists");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void ReleaseOwnedTransport()
    {
        if (string.IsNullOrEmpty(_root) || !Directory.Exists(_root)) return;
        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
        {
            TestContext.Error.WriteLine("process-transport: failed raw fixture retained as " + Path.GetFileName(_root) + ".");
            return;
        }
        _ = RemainingRoot();
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
        var name = Path.GetFileName(full);
        Require(string.Equals(Path.GetDirectoryName(full), _parent, StringComparison.OrdinalIgnoreCase) &&
            name.StartsWith(OwnerPrefix, StringComparison.Ordinal) &&
            Guid.TryParseExact(name[OwnerPrefix.Length..], "N", out _) &&
            (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0, "process-transport-cleanup-owner-invalid");
        Directory.Delete(full, recursive: true);
    }

    [Test]
    public async Task PinnedSdkVersionCrossesTheActualWaitingShim()
    {
        var dotnet = ResolveDotnet();
        var result = await RunShimAsync(dotnet, ["--version"], sendGo: true);
        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(new UTF8Encoding(false, true).GetString(File.ReadAllBytes(OutputPath)).TrimEnd('\r', '\n'),
            Is.EqualTo("10.0.403"));
        Assert.That(File.ReadAllBytes(ErrorPath), Is.Empty);
    }

    [TestCase(0, TestName = "process-transport/raw-pipes-and-exit-0")]
    [TestCase(23, TestName = "process-transport/raw-pipes-and-exit-23")]
    public async Task IndependentPipesPreserveRawBytesAndTheTargetExit(int exitCode)
    {
        var target = PrepareRawTarget(exitCode);
        var result = await RunShimAsync(_pwsh, target.Arguments, sendGo: true);
        Assert.That(result.ExitCode, Is.EqualTo(exitCode));
        Assert.That(File.ReadAllBytes(target.Marker), Is.EqualTo(Encoding.UTF8.GetBytes("target-started")));
        Assert.That(File.ReadAllBytes(OutputPath), Is.EqualTo(target.Stdout));
        Assert.That(File.ReadAllBytes(ErrorPath), Is.EqualTo(target.Stderr));
    }

    [Test]
    public async Task ClosedInputWithoutGoCannotLaunchTheDeclaredTarget()
    {
        var target = PrepareRawTarget(exitCode: 23);
        var result = await RunShimAsync(_pwsh, target.Arguments, sendGo: false);
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(File.Exists(target.Marker), Is.False);
        Assert.That(File.ReadAllBytes(OutputPath), Is.Empty);
        Assert.That(new UTF8Encoding(false, true).GetString(File.ReadAllBytes(ErrorPath)).TrimEnd('\r', '\n'),
            Is.EqualTo("test-process-failed"));
    }

    private string OutputPath => Path.Combine(_root, "stdout.bin");
    private string ErrorPath => Path.Combine(_root, "stderr.bin");

    private RawTarget PrepareRawTarget(int exitCode)
    {
        // Each pipe exceeds the OS pipe buffer. Alternating raw writes require
        // concurrent forwarding; neither decoding nor sequential drains suffice.
        byte[] stdoutPattern = [0x00, 0xff, 0xc3, 0x28, 0x80, 0x0d, 0x0a, 0x41, 0x0d, 0x42, 0x0a];
        byte[] stderrPattern = [0xfe, 0x00, 0xed, 0xa0, 0x80, 0x0d, 0x0a, 0x7f, 0x45, 0x52, 0x52];
        var stdout = Enumerable.Range(0, 196613).Select(index => stdoutPattern[index % stdoutPattern.Length]).ToArray();
        var stderr = Enumerable.Range(0, 131079).Select(index => stderrPattern[index % stderrPattern.Length]).ToArray();
        var stdoutSource = Path.Combine(_root, "expected.stdout.bin");
        var stderrSource = Path.Combine(_root, "expected.stderr.bin");
        File.WriteAllBytes(stdoutSource, stdout);
        File.WriteAllBytes(stderrSource, stderr);
        var marker = Path.Combine(_root, "target-started.bin");
        var script = Path.Combine(_root, "raw-target.ps1");
        WriteNewText(script, """
            param([string]$StdoutPath, [string]$StderrPath, [string]$MarkerPath, [int]$TargetExit)
            $ErrorActionPreference = 'Stop'
            [IO.File]::WriteAllBytes($MarkerPath, [Text.Encoding]::UTF8.GetBytes('target-started'))
            $stdoutBytes = [IO.File]::ReadAllBytes($StdoutPath)
            $stderrBytes = [IO.File]::ReadAllBytes($StderrPath)
            $stdout = [Console]::OpenStandardOutput()
            $stderr = [Console]::OpenStandardError()
            for ($offset = 0; $offset -lt [Math]::Max($stdoutBytes.Length, $stderrBytes.Length); $offset += 8192) {
                if ($offset -lt $stdoutBytes.Length) { $stdout.Write($stdoutBytes, $offset, [Math]::Min(8192, $stdoutBytes.Length - $offset)) }
                if ($offset -lt $stderrBytes.Length) { $stderr.Write($stderrBytes, $offset, [Math]::Min(8192, $stderrBytes.Length - $offset)) }
            }
            $stdout.Flush()
            $stderr.Flush()
            exit $TargetExit
            """);
        return new RawTarget(marker, stdout, stderr,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", script,
                "-StdoutPath", stdoutSource, "-StderrPath", stderrSource,
                "-MarkerPath", marker, "-TargetExit", exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
    }

    private async Task<ProcessResult> RunShimAsync(string executable, string[] arguments, bool sendGo)
    {
        _ = RemainingRoot();
        var invocation = Path.Combine(_root, "argv.json");
        WriteNewText(invocation, JsonSerializer.Serialize(new
        {
            schemaVersion = 1, executable, arguments, workingDirectory = _repositoryRoot
        }));
        var start = new ProcessStartInfo(_pwsh)
        {
            WorkingDirectory = _repositoryRoot, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-File", _shim, "-ArgumentsPath", invocation
        }) start.ArgumentList.Add(argument);
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        using var process = new Process { StartInfo = start };
        using var stdoutFile = new FileStream(OutputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var stderrFile = new FileStream(ErrorPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        Task? stdout = null;
        Task? stderr = null;
        Task? exit = null;
        Exception? primary = null;
        var started = false;
        try
        {
            started = process.Start();
            Require(started, "process-transport-shim-not-started");
            stdout = process.StandardOutput.BaseStream.CopyToAsync(stdoutFile);
            stderr = process.StandardError.BaseStream.CopyToAsync(stderrFile);
            exit = process.WaitForExitAsync();
            if (sendGo) await process.StandardInput.WriteLineAsync("GO").WaitAsync(RemainingRoot());
            process.StandardInput.Close();
            var completion = Task.WhenAll(exit, stdout, stderr);
            var warning = TimeSpan.FromSeconds(20) - _clock.Elapsed;
            if (warning <= TimeSpan.Zero || await Task.WhenAny(completion, Task.Delay(warning)) != completion)
                TestContext.Progress.WriteLine("process-transport: same shim still running after twenty seconds.");
            await completion.WaitAsync(RemainingRoot());
            _ = RemainingRoot();
            stdoutFile.Flush(flushToDisk: true);
            stderrFile.Flush(flushToDisk: true);
            WriteProcessRecord(process, stdout, stderr, primary: null);
            return new(process.ExitCode);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            if (primary is not null && started)
            {
                // Exit and both original drains share one cleanup allowance;
                // this test does not establish Windows Job ownership.
                var cleanup = Stopwatch.StartNew();
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    exit ??= process.WaitForExitAsync();
                    await Task.WhenAll(new[] { exit, stdout, stderr }.OfType<Task>()).WaitAsync(RemainingCleanup(cleanup));
                    stdoutFile.Flush(flushToDisk: true);
                    stderrFile.Flush(flushToDisk: true);
                    WriteProcessRecord(process, stdout, stderr, primary);
                    _ = RemainingCleanup(cleanup);
                }
                catch (Exception cleanupError)
                {
                    TestContext.Error.WriteLine("process-transport: bounded cleanup failed (" + cleanupError.GetType().Name + ").");
                }
            }
        }
    }

    private void WriteProcessRecord(Process process, Task? stdout, Task? stderr, Exception? primary)
        => WriteNewText(Path.Combine(_root, "process.json"), JsonSerializer.Serialize(new
        {
            processId = process.Id, exited = process.HasExited, exitCode = process.HasExited ? process.ExitCode : (int?)null,
            stdoutDrained = stdout?.IsCompletedSuccessfully == true, stderrDrained = stderr?.IsCompletedSuccessfully == true,
            elapsedMilliseconds = _clock.Elapsed.TotalMilliseconds, failureType = primary?.GetType().Name,
            windowsJobClaim = false
        }));

    private TimeSpan RemainingRoot()
    {
        var remaining = TimeSpan.FromSeconds(120) - _clock.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException("process-transport-root-deadline");
    }

    private static TimeSpan RemainingCleanup(Stopwatch clock)
    {
        var remaining = TimeSpan.FromSeconds(10) - clock.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException("process-transport-cleanup-deadline");
    }

    private static string ResolveRepositoryRoot()
    {
        var explicitRoot = Environment.GetEnvironmentVariable("FISHAUDIO_TEST_REPOSITORY_ROOT");
        var contextPath = Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT");
        string? snapshotRoot = null;
        if (!string.IsNullOrEmpty(contextPath))
        {
            RequireRegularFile(contextPath);
            Require(new FileInfo(contextPath).Length <= 16 * 1024 * 1024, "process-transport-context-too-large");
            using var context = JsonDocument.Parse(File.ReadAllBytes(contextPath));
            Require(context.RootElement.GetProperty("schemaVersion").GetInt32() == 1, "process-transport-context-schema");
            snapshotRoot = RequireExistingDirectory(context.RootElement.GetProperty("root").GetString() ?? "");
        }
        if (!string.IsNullOrEmpty(explicitRoot))
        {
            explicitRoot = RequireExistingDirectory(explicitRoot);
            Require(snapshotRoot is null || string.Equals(snapshotRoot, explicitRoot, StringComparison.OrdinalIgnoreCase),
                "process-transport-context-root-mismatch");
        }
        var root = snapshotRoot ?? explicitRoot;
        Require(!string.IsNullOrEmpty(root), "process-transport-explicit-repository-required");
        RequireRegularFile(Path.Combine(root!, "global.json"));
        return root!;
    }

    private static string ResolveDotnet()
    {
        foreach (var key in new[] { "FISHAUDIO_DOTNET_EXECUTABLE", "DOTNET_HOST_PATH" })
        {
            if (Environment.GetEnvironmentVariable(key) is not { Length: > 0 } candidate) continue;
            RequireRegularFile(candidate);
            return candidate;
        }
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim('"'), "dotnet.exe");
            if (!Path.IsPathFullyQualified(candidate) || !File.Exists(candidate)) continue;
            RequireRegularFile(candidate);
            return Path.GetFullPath(candidate);
        }
        throw new InvalidOperationException("process-transport-dotnet-missing");
    }

    private static string RequireExistingDirectory(string path)
    {
        Require(Path.IsPathFullyQualified(path), "process-transport-directory-not-absolute");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        for (DirectoryInfo? directory = new(full); directory is not null; directory = directory.Parent)
            Require(directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) == 0,
                "process-transport-directory-missing-or-reparse");
        return full;
    }

    private static void RequireRegularFile(string path)
    {
        Require(Path.IsPathFullyQualified(path), "process-transport-file-not-absolute");
        var file = new FileInfo(path);
        Require(file.Exists && (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0,
            "process-transport-file-missing-or-reparse");
        _ = RequireExistingDirectory(file.DirectoryName!);
    }

    private static void WriteNewText(string path, string text)
    {
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        output.Write(new UTF8Encoding(false).GetBytes(text));
        output.Flush(flushToDisk: true);
    }

    private static void Require(bool condition, string code)
    {
        if (!condition) throw new InvalidOperationException(code);
    }

    private sealed record ProcessResult(int ExitCode);
    private sealed record RawTarget(string Marker, byte[] Stdout, byte[] Stderr, string[] Arguments);
}
