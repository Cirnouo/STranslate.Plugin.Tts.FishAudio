using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FishAudio.Testing.Fixtures;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[NonParallelizable]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class FixtureSigningTests
{
    private readonly Stopwatch _clock = new();
    private string _temporaryRoot = "";
    private string _fixtureRoot = "";
    private string _signingRoot = "";
    private string _repository = "";
    private string _evidence = "";
    private string _identity = "";
    private string _git = "";
    private string _sshKeygen = "";
    private Stopwatch? _failureCleanup;
    private int _ordinal;

    [SetUp]
    public void PrepareOwnedFixture()
    {
        _clock.Start();
        _git = GitFixtureSigning.ResolveGitExecutable();
        _sshKeygen = GitFixtureSigning.ResolveSshKeygenExecutable(_git);
        _temporaryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var runId = Guid.NewGuid().ToString("N");
        _fixtureRoot = Path.Combine(_temporaryRoot, "fa-sign-test-" + runId);
        // A separate sibling is deliberately outside every repository/evidence
        // capture. Both Git signing and verification must handle this real path.
        _signingRoot = Path.Combine(_temporaryRoot, "fa-sign-" + runId + " 空间");
        _identity = "fixture-" + runId + "@example.invalid";
        Require(!Directory.Exists(_fixtureRoot) && !File.Exists(_fixtureRoot) &&
            !Directory.Exists(_signingRoot) && !File.Exists(_signingRoot), "owner-already-exists");
        _repository = Path.Combine(_fixtureRoot, "repository");
        Directory.CreateDirectory(_repository);
        var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
        _evidence = string.IsNullOrEmpty(configured) ? Path.Combine(_fixtureRoot, "evidence") :
            Path.Combine(Path.GetFullPath(configured), "fixture-signing-" + runId);
        Directory.CreateDirectory(_evidence);
        File.WriteAllText(Path.Combine(_fixtureRoot, "empty.gitconfig"), "", new UTF8Encoding(false));
    }

    [TearDown]
    public void ReleaseOwnedFixture()
    {
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        var rootExpired = _clock.Elapsed >= TimeSpan.FromSeconds(120);
        // Failed execution and private-key cleanup share the one existing
        // diagnostic allowance. An expired root never renews test execution.
        if (failed || rootExpired) _failureCleanup ??= Stopwatch.StartNew();
        Exception? cleanupFailure = !failed && rootExpired
            ? new TimeoutException("fixture-signing-root-deadline") : null;
        try { DeleteOwnedRoot(_signingRoot, "fa-sign-", " 空间"); }
        catch (Exception error) { cleanupFailure ??= error; }
        if (!failed && cleanupFailure is null)
        {
            try { DeleteOwnedRoot(_fixtureRoot, "fa-sign-test-", ""); }
            catch (Exception error) { cleanupFailure = error; }
        }
        if (cleanupFailure is not null && !failed) throw cleanupFailure;
        if (failed || cleanupFailure is not null)
            TestContext.Progress.WriteLine("fixture-signing: failed raw fixture retained; cleanup=" +
                (cleanupFailure?.GetType().Name ?? "complete") + ".");
    }

    [Test]
    [Property("FishAudioCaseId", "fixture-signing/ordinary-commit-spaced-unicode-paths")]
    public async Task OrdinaryCommitUsesItsOwnedKeyAndKeepsTheRealHook()
    {
        var signing = GenerateSigning();
        await InitializeAndStageAsync();
        File.WriteAllText(Path.Combine(_repository, ".git", "hooks", "pre-commit"),
            "#!/bin/sh\nprintf 'ran' > .git/fixture-hook-ran\n", new UTF8Encoding(false));
        await GitAsync([.. signing.ConfigurationArguments(), .. AuthorArguments(),
            "commit", "-S", "-m", "test: signed fixture"]);
        var objectId = await GitTextAsync(["rev-parse", "HEAD"]);
        await VerifyActualSignatureAsync(signing, objectId);
        Assert.That(File.ReadAllText(Path.Combine(_repository, ".git", "fixture-hook-ran")), Is.EqualTo("ran"));
        Assert.That(await GitTextAsync(["ls-files"]), Is.EqualTo("payload.txt"),
            "Signing material remains outside the repository and its captured source tree.");
    }

    [Test]
    [Property("FishAudioCaseId", "fixture-signing/commit-tree-signed-object")]
    public async Task CommitTreeCreatesAndVerifiesAnActuallySignedObject()
    {
        var signing = GenerateSigning();
        await InitializeAndStageAsync();
        var tree = await GitTextAsync(["write-tree"]);
        var objectId = await GitTextAsync([.. signing.ConfigurationArguments(), .. AuthorArguments(),
            "commit-tree", "-S", tree, "-m", "test: signed tree fixture"]);
        await VerifyActualSignatureAsync(signing, objectId);
        await GitAsync(["update-ref", "HEAD", objectId]);
        Assert.That(await GitTextAsync(["rev-parse", "HEAD^{tree}"]), Is.EqualTo(tree));
    }

    private GitFixtureSigning GenerateSigning()
    {
        var signing = GitFixtureSigning.Generate(_signingRoot, _sshKeygen, _identity, (executable, arguments) =>
            RunAsync(executable, arguments).GetAwaiter().GetResult());
        Assert.That(signing.RootDirectory, Is.EqualTo(_signingRoot));
        Assert.That(signing.SshKeygenPath, Is.EqualTo(_sshKeygen));
        Assert.That(File.Exists(signing.PrivateKeyPath), Is.True);
        Assert.That(File.ReadAllText(signing.PublicKeyPath).Trim(), Is.EqualTo(signing.SshPublicKey));
        Assert.That(signing.SshPublicKey, Does.StartWith("ssh-ed25519 "));
        Assert.That(File.ReadAllText(signing.AllowedSignersPath), Does.Contain(_identity + " "));
        var keyRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(signing.RootDirectory));
        foreach (var capturedRoot in new[] { _repository, _evidence })
        {
            var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(capturedRoot));
            Assert.That(!keyRoot.Equals(parent, StringComparison.OrdinalIgnoreCase) &&
                !keyRoot.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True,
                "Private signing material must remain outside repository and captured evidence, including across volumes.");
        }
        File.WriteAllText(Path.Combine(_evidence, "public-signing-context.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, principal = _identity, publicKey = signing.SshPublicKey,
            privateKeyCaptured = false, userKeyClaim = false
        }), new UTF8Encoding(false));
        return signing;
    }

    private async Task InitializeAndStageAsync()
    {
        await GitAsync(["init", "--quiet"]);
        File.WriteAllText(Path.Combine(_repository, "payload.txt"), "owned signing fixture\n", new UTF8Encoding(false));
        await GitAsync(["add", "--", "payload.txt"]);
    }

    private string[] AuthorArguments() => ["-c", "user.name=Fixture Signing", "-c", "user.email=" + _identity];

    private async Task VerifyActualSignatureAsync(GitFixtureSigning signing, string objectId)
    {
        Assert.That(objectId.Length, Is.AnyOf(40, 64));
        Assert.That(objectId.All(Uri.IsHexDigit), Is.True);
        await GitAsync(signing.VerifyCommitArguments(objectId));
        var commit = await GitTextAsync(["cat-file", "commit", objectId]);
        Assert.That(commit, Does.Contain("gpgsig -----BEGIN SSH SIGNATURE-----\n"));
        File.WriteAllText(Path.Combine(_evidence, "verified-commit.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, objectId, signed = true, verified = true, principal = _identity,
            completeBaseline = false
        }), new UTF8Encoding(false));
    }

    private Task<byte[]> GitAsync(string[] arguments) => RunAsync(_git, ["-C", _repository, .. arguments]);
    private async Task<string> GitTextAsync(string[] arguments)
        => new UTF8Encoding(false, true).GetString(await GitAsync(arguments)).TrimEnd('\r', '\n');

    private async Task<byte[]> RunAsync(string executable, string[] arguments)
    {
        _ = Remaining();
        var prefix = Path.Combine(_evidence, (++_ordinal).ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
        File.WriteAllText(prefix + ".argv.json", JsonSerializer.Serialize(new { executable, arguments }), new UTF8Encoding(false));
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = _repository, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(_fixtureRoot, "empty.gitconfig");
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment.Remove("SSH_AUTH_SOCK");
        using var process = new Process { StartInfo = start };
        using var stdout = new MemoryStream();
        using var stderr = new MemoryStream();
        Task? output = null, errorOutput = null, exit = null;
        Exception? primary = null;
        var started = false;
        try
        {
            started = process.Start();
            Require(started, "process-not-started");
            output = process.StandardOutput.BaseStream.CopyToAsync(stdout);
            errorOutput = process.StandardError.BaseStream.CopyToAsync(stderr);
            exit = process.WaitForExitAsync();
            process.StandardInput.Close();
            await Task.WhenAll(exit, output, errorOutput).WaitAsync(Remaining()).ConfigureAwait(false);
            Require(process.ExitCode == 0, "process-nonzero");
            return stdout.ToArray();
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            if (primary is not null && started)
            {
                _failureCleanup ??= Stopwatch.StartNew();
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    exit ??= process.WaitForExitAsync();
                    await Task.WhenAll(new[] { exit, output, errorOutput }.OfType<Task>())
                        .WaitAsync(CleanupRemaining()).ConfigureAwait(false);
                }
                catch { /* The same primary remains failed; the parent owns hard termination. */ }
            }
            try
            {
                File.WriteAllBytes(prefix + ".stdout.bin", stdout.ToArray());
                File.WriteAllBytes(prefix + ".stderr.bin", stderr.ToArray());
                File.WriteAllText(prefix + ".process.json", JsonSerializer.Serialize(new
                {
                    started, processId = started ? process.Id : (int?)null,
                    exited = started && process.HasExited,
                    exitCode = started && process.HasExited ? process.ExitCode : (int?)null,
                    stdoutDrained = output?.IsCompletedSuccessfully == true,
                    stderrDrained = errorOutput?.IsCompletedSuccessfully == true,
                    failureType = primary?.GetType().Name, windowsJobClaim = false
                }), new UTF8Encoding(false));
            }
            catch when (primary is not null) { /* Raw-output publication cannot replace the primary. */ }
        }
    }

    private TimeSpan Remaining() => _clock.Elapsed < TimeSpan.FromSeconds(120)
        ? TimeSpan.FromSeconds(120) - _clock.Elapsed : throw new TimeoutException("fixture-signing-root-deadline");
    private TimeSpan CleanupRemaining() => _failureCleanup is not null && _failureCleanup.Elapsed < TimeSpan.FromSeconds(10)
        ? TimeSpan.FromSeconds(10) - _failureCleanup.Elapsed : throw new TimeoutException("fixture-signing-cleanup-deadline");

    private void DeleteOwnedRoot(string root, string prefix, string suffix)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
        _ = _failureCleanup is null ? Remaining() : CleanupRemaining();
        var name = Path.GetFileName(root);
        Require(Path.GetDirectoryName(Path.GetFullPath(root)) == _temporaryRoot &&
            name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(suffix, StringComparison.Ordinal) &&
            name.Length == prefix.Length + 32 + suffix.Length &&
            Guid.TryParseExact(name.Substring(prefix.Length, 32), "N", out _), "cleanup-owner-invalid");
        var signingMaterials = string.Equals(root, _signingRoot, StringComparison.Ordinal);
        var allowedSigningFiles = new HashSet<string>(StringComparer.Ordinal)
            { "fixture-ed25519", "fixture-ed25519.pub", "allowed_signers" };
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        var files = new List<FileInfo>();
        while (pending.Count != 0)
        {
            _ = _failureCleanup is null ? Remaining() : CleanupRemaining();
            var directory = pending.Pop();
            Require((directory.Attributes & FileAttributes.ReparsePoint) == 0, "cleanup-directory-reparse");
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                Require((entry.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) == 0,
                    "cleanup-entry-reparse");
                if (signingMaterials)
                    Require(entry is FileInfo && allowedSigningFiles.Remove(entry.Name), "cleanup-signing-entry-unknown");
                if (entry is DirectoryInfo child) pending.Push(child);
                else files.Add((FileInfo)entry);
            }
        }
        foreach (var file in files) File.SetAttributes(file.FullName, FileAttributes.Normal);
        if (signingMaterials)
        {
            foreach (var file in files)
            {
                _ = _failureCleanup is null ? Remaining() : CleanupRemaining();
                File.Delete(file.FullName);
            }
            Directory.Delete(root, recursive: false);
        }
        else Directory.Delete(root, recursive: true);
    }

    private static void Require(bool condition, string token)
    {
        if (!condition) throw new InvalidOperationException("fixture-signing-" + token);
    }
}
