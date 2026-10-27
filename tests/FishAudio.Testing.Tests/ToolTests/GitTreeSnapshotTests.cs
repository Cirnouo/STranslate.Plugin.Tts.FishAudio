using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FishAudio.Testing.Snapshots;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests.ToolTests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class GitTreeSnapshotTests
{
    private const string HelperPath = "tools/FishAudio.Testing/Snapshots/GitTreeSnapshot.cs";
    private readonly Stopwatch _clock = new();
    private string _parent = "";
    private string _owner = "";
    private string _repository = "";
    private string _git = "";
    private string _evidenceRoot = "";
    private string _ownedJunction = "";
    private string _ownedJunctionTarget = "";
    private Stopwatch? _diagnosticClock;
    private bool _failureArchiveAttempted;
    private int _ordinal;

    [SetUp]
    public void PrepareOwnedRepository()
    {
        _clock.Start();
        var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
        if (!string.IsNullOrEmpty(configured)) RequireDirectory(configured);
        _evidenceRoot = string.IsNullOrEmpty(configured) ? "" : Path.GetFullPath(configured);
        // Git init creates template hooks and objects before materialization.
        // A configured results directory can already exceed Git's Windows path
        // limit; keep the actual fixture short and retain a private owner pointer.
        _parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        RequireDirectory(_parent);
        _owner = Path.Combine(_parent, "fa-tree-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(_owner) || File.Exists(_owner)) throw new InvalidOperationException("git-tree-fixture-owner-exists");
        Directory.CreateDirectory(_owner);
        if (!string.IsNullOrEmpty(configured))
        {
            var pointer = Path.Combine(Path.GetFullPath(configured), Path.GetFileName(_owner) + ".json");
            using var record = new FileStream(pointer, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(record, new { schemaVersion = 1, fixtureRoot = _owner, retention = "on-failure" });
            record.Flush(flushToDisk: true);
        }
        _repository = Path.Combine(_owner, "repository");
        Directory.CreateDirectory(_repository);
        _git = Executable("git.exe");
    }

    [TearDown]
    public async Task ReleaseOwnedRepository()
    {
        if (string.IsNullOrEmpty(_owner) || !Directory.Exists(_owner)) return;
        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
        {
            await RetainFailureEvidenceAsync();
            return;
        }
        try { DeleteSuccessfulFixture(); }
        catch
        {
            await RetainFailureEvidenceAsync();
            throw;
        }
    }

    private void DeleteSuccessfulFixture()
    {
        _ = Remaining();
        var name = Path.GetFileName(_owner);
        Assert.That(Path.GetDirectoryName(Path.GetFullPath(_owner)), Is.EqualTo(_parent).IgnoreCase);
        Assert.That(name.StartsWith("fa-tree-", StringComparison.Ordinal) && Guid.TryParseExact(name[8..], "N", out _), Is.True);
        Assert.That((File.GetAttributes(_owner) & FileAttributes.ReparsePoint) == 0, Is.True);
        RemoveOwnedJunction();
        var pending = new Stack<DirectoryInfo>();
        var files = new List<FileInfo>();
        pending.Push(new DirectoryInfo(_owner));
        while (pending.Count != 0)
        {
            var directory = pending.Pop();
            directory.Refresh();
            Assert.That(directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) == 0, Is.True,
                "git-tree-fixture-cleanup-directory-missing-or-reparse");
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                entry.Refresh();
                Assert.That(entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) == 0, Is.True,
                    "git-tree-fixture-cleanup-entry-missing-or-reparse");
                if (entry is DirectoryInfo child) pending.Push(child);
                else files.Add((FileInfo)entry);
            }
        }
        foreach (var file in files) File.SetAttributes(file.FullName, FileAttributes.Normal);
        Directory.Delete(_owner, recursive: true);
    }

    private async Task RetainFailureEvidenceAsync()
    {
        try { TestContext.Error.WriteLine("git-tree: failed raw fixture retained as " + Path.GetFileName(_owner) + "."); }
        catch { /* Diagnostic output must preserve the original test or cleanup failure. */ }
        if (_failureArchiveAttempted || string.IsNullOrEmpty(_evidenceRoot)) return;
        _failureArchiveAttempted = true;
        // Archival never renews a process cleanup allowance or extends the
        // original fixture deadline. The retained root/pointer survive a skip.
        if (_clock.Elapsed >= TimeSpan.FromSeconds(120)) return;
        var diagnostics = _diagnosticClock ??= Stopwatch.StartNew();
        if (diagnostics.Elapsed >= TimeSpan.FromSeconds(10)) return;
        try
        {
            RequireDirectory(_parent);
            RequireDirectory(_owner);
            RequireDirectory(_evidenceRoot);
            var name = Path.GetFileName(_owner);
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(_owner)), _parent, StringComparison.OrdinalIgnoreCase) ||
                !name.StartsWith("fa-tree-", StringComparison.Ordinal) || !Guid.TryParseExact(name[8..], "N", out _))
                throw new InvalidOperationException("git-tree-fixture-archive-owner-invalid");
            if (string.Equals(Path.TrimEndingDirectorySeparator(_evidenceRoot), _owner, StringComparison.OrdinalIgnoreCase) ||
                _evidenceRoot.StartsWith(_owner + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("git-tree-fixture-archive-destination-inside-owner");
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(Pwsh())
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = _parent
                }
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(FailureArchiveScript)) })
                process.StartInfo.ArgumentList.Add(argument);
            process.StartInfo.Environment["FISHAUDIO_FIXTURE_ARCHIVE_ROOT"] = _owner;
            process.StartInfo.Environment["FISHAUDIO_FIXTURE_ARCHIVE_PARENT"] = _parent;
            process.StartInfo.Environment["FISHAUDIO_FIXTURE_ARCHIVE_EVIDENCE"] = _evidenceRoot;
            var started = false;
            Task? stdout = null;
            Task? stderr = null;
            try
            {
                if (ArchiveRemaining(diagnostics) <= TimeSpan.FromSeconds(2))
                    throw new TimeoutException("git-tree-fixture-archive-deadline");
                started = process.Start();
                if (!started) throw new InvalidOperationException("git-tree-fixture-archive-start");
                stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
                stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
                // The final two seconds belong to termination and both drains,
                // within this same ten-second diagnostic allowance.
                var available = ArchiveRemaining(diagnostics);
                var work = available - TimeSpan.FromSeconds(2);
                if (work <= TimeSpan.Zero) throw new TimeoutException("git-tree-fixture-archive-deadline");
                await Task.WhenAll(process.WaitForExitAsync(), stdout, stderr).WaitAsync(work);
                if (process.ExitCode != 0) throw new InvalidOperationException("git-tree-fixture-archive-failed");
            }
            finally
            {
                if (started)
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await Task.WhenAll(new[] { process.WaitForExitAsync(), stdout, stderr }.OfType<Task>())
                        .WaitAsync(ArchiveRemaining(diagnostics));
                }
            }
        }
        catch
        {
            try { TestContext.Error.WriteLine("git-tree-fixture-failure-archive-failed"); }
            catch { /* Keep the primary failure and the original raw fixture. */ }
        }
    }

    // A separate owned worker makes a blocked ZIP write terminable. It never
    // edits the original fixture and walks immediate children only.
    private const string FailureArchiveScript = """
        $ErrorActionPreference = 'Stop'
        $archive = $null
        $destination = $null
        try {
            function Require-RegularDirectory([string] $path) {
                if (![IO.Path]::IsPathFullyQualified($path)) { throw 'archive-relative-directory' }
                $item = [IO.DirectoryInfo]::new($path)
                while ($null -ne $item) {
                    $item.Refresh()
                    if (!$item.Exists -or (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { throw 'archive-directory-missing-or-reparse' }
                    $item = $item.Parent
                }
            }
            $root = [IO.Path]::GetFullPath($env:FISHAUDIO_FIXTURE_ARCHIVE_ROOT)
            $parent = [IO.Path]::GetFullPath($env:FISHAUDIO_FIXTURE_ARCHIVE_PARENT)
            $evidence = [IO.Path]::GetFullPath($env:FISHAUDIO_FIXTURE_ARCHIVE_EVIDENCE)
            Require-RegularDirectory $root
            Require-RegularDirectory $parent
            Require-RegularDirectory $evidence
            $name = [IO.Path]::GetFileName($root)
            if ($name -cnotmatch '^fa-tree-[0-9a-f]{32}$' -or
                ![string]::Equals([IO.Path]::GetDirectoryName($root), $parent, [StringComparison]::OrdinalIgnoreCase)) { throw 'archive-owner-invalid' }
            if ([string]::Equals([IO.Path]::TrimEndingDirectorySeparator($evidence), $root, [StringComparison]::OrdinalIgnoreCase) -or
                $evidence.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'archive-destination-inside-owner' }
            $zip = [IO.Path]::Combine($evidence, $name + '.zip')
            $destination = [IO.FileStream]::new($zip, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
            $archive = [IO.Compression.ZipArchive]::new($destination, [IO.Compression.ZipArchiveMode]::Create, $true)
            $pending = [Collections.Generic.Stack[IO.DirectoryInfo]]::new()
            $pending.Push([IO.DirectoryInfo]::new($root))
            $skipped = [Collections.Generic.List[string]]::new()
            $fileCount = 0
            while ($pending.Count -ne 0) {
                $directory = $pending.Pop()
                Require-RegularDirectory $directory.FullName
                foreach ($entry in $directory.EnumerateFileSystemInfos()) {
                    $entry.Refresh()
                    $relative = [IO.Path]::GetRelativePath($root, $entry.FullName).Replace('\', '/')
                    if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or $relative.StartsWith('../', [StringComparison]::Ordinal)) { throw 'archive-entry-outside-root' }
                    if (!$entry.Exists) {
                        if ($null -ne $entry.LinkTarget) { $skipped.Add($relative); continue }
                        throw 'archive-entry-missing'
                    }
                    if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                        $skipped.Add($relative)
                        continue
                    }
                    if ($entry -is [IO.DirectoryInfo]) { $pending.Push($entry); continue }
                    if (($entry -isnot [IO.FileInfo]) -or (($entry.Attributes -band [IO.FileAttributes]::Device) -ne 0)) { throw 'archive-entry-not-regular-file' }
                    Require-RegularDirectory $entry.DirectoryName
                    $input = $null
                    $output = $null
                    try {
                        $input = [IO.FileStream]::new($entry.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
                        $length = $input.Length
                        $output = $archive.CreateEntry('files/' + $relative, [IO.Compression.CompressionLevel]::Fastest).Open()
                        $input.CopyTo($output)
                        if ($input.Position -ne $length -or $input.Length -ne $length) { throw 'archive-entry-changed' }
                    }
                    finally {
                        if ($null -ne $output) { $output.Dispose() }
                        if ($null -ne $input) { $input.Dispose() }
                    }
                    $fileCount++
                }
            }
            $summary = [ordered]@{ schemaVersion = 1; kind = 'failed-git-tree-fixture'; complete = $true; fileCount = $fileCount; skippedReparsePaths = @($skipped.ToArray()) }
            $bytes = [Text.Encoding]::UTF8.GetBytes(($summary | ConvertTo-Json -Depth 4 -Compress))
            $output = $archive.CreateEntry('archive.json').Open()
            try { $output.Write($bytes, 0, $bytes.Length) } finally { $output.Dispose() }
            $archive.Dispose()
            $archive = $null
            $destination.Flush($true)
            $destination.Dispose()
            $destination = $null
            exit 0
        }
        catch { exit 1 }
        finally {
            if ($null -ne $archive) { $archive.Dispose() }
            if ($null -ne $destination) { $destination.Dispose() }
        }
        """;

    private void RemoveOwnedJunction()
    {
        if (string.IsNullOrEmpty(_ownedJunction)) return;
        Assert.That(_ownedJunction, Is.EqualTo(Path.Combine(_owner, "junction")).IgnoreCase);
        Assert.That(_ownedJunctionTarget, Is.EqualTo(Path.Combine(_owner, "outside")).IgnoreCase);
        var junction = new DirectoryInfo(_ownedJunction);
        Assert.That(junction.Exists && (junction.Attributes & FileAttributes.ReparsePoint) != 0, Is.True,
            "git-tree-fixture-cleanup-owned-junction-invalid");
        RequireDirectory(_ownedJunctionTarget);
        var sentinel = Path.Combine(_ownedJunctionTarget, "sentinel.bin");
        var before = File.ReadAllBytes(sentinel);
        Assert.That(before, Is.EqualTo(new byte[] { 9 }));
        // Remove this link itself, without following or recursively deleting its target.
        Directory.Delete(_ownedJunction, recursive: false);
        Assert.That(Directory.Exists(_ownedJunction), Is.False);
        Assert.That(Directory.Exists(_ownedJunctionTarget), Is.True);
        Assert.That(File.ReadAllBytes(sentinel), Is.EqualTo(before));
    }

    [TestCase("sha1", TestName = "git-tree/raw-index-bytes-sha1")]
    [TestCase("sha256", TestName = "git-tree/raw-index-bytes-sha256")]
    public async Task RawStagedBytesIgnoreWorkingFilesFiltersAndArchiveSubstitution(string format)
    {
        await Initialize(format);
        byte[] binary = [0x00, 0xff, 0xc3, 0x28, 0x0d, 0x0a, 0x80, 0x41];
        var text = Encoding.UTF8.GetBytes("$Format:%H$\r\nraw\n\0中文");
        var attributes = Encoding.UTF8.GetBytes("*.txt text eol=crlf\nsubst.txt export-subst\n");
        await Stage("bytes.bin", binary);
        await Stage("subst.txt", text);
        await Stage("nested/源 file.txt", text, mode: "100755");
        await Stage(".gitattributes", attributes);
        var tree = await GitText(["write-tree"]);
        var index = Path.Combine(_repository, ".git", "index");
        var before = File.ReadAllBytes(index);
        File.WriteAllText(Path.Combine(_repository, "subst.txt"), "unstaged replacement");
        Directory.CreateDirectory(Path.Combine(_repository, "bin"));
        File.WriteAllText(Path.Combine(_repository, "bin", "old-output.dll"), "untracked old output");

        var snapshot = Materialize(tree);
        Assert.That(snapshot.Tree, Is.EqualTo(tree));
        Assert.That(snapshot.Files, Has.Count.EqualTo(4));
        Assert.That(File.ReadAllBytes(Path.Combine(snapshot.Root, "bytes.bin")), Is.EqualTo(binary));
        Assert.That(File.ReadAllBytes(Path.Combine(snapshot.Root, "subst.txt")), Is.EqualTo(text));
        Assert.That(File.ReadAllBytes(Path.Combine(snapshot.Root, "nested", "源 file.txt")), Is.EqualTo(text));
        Assert.That(snapshot.Files.Single(item => item.Path == "nested/源 file.txt").Mode, Is.EqualTo("100755"));
        Assert.That(File.Exists(Path.Combine(snapshot.Root, "bin", "old-output.dll")), Is.False);
        Assert.That(File.ReadAllText(Path.Combine(_repository, "subst.txt")), Is.EqualTo("unstaged replacement"));
        Assert.That(File.ReadAllBytes(index), Is.EqualTo(before));
        foreach (var file in snapshot.Files)
        {
            var bytes = File.ReadAllBytes(Path.Combine(snapshot.Root, file.Path));
            Assert.That(file.Sha256, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(bytes))));
            Assert.That(file.Blob.Length, Is.EqualTo(format == "sha1" ? 40 : 64));
            Assert.That(file.Length, Is.EqualTo(bytes.Length));
        }
    }

    [TestCase("120000", TestName = "git-tree/reject-symbolic-link-mode")]
    [TestCase("160000", TestName = "git-tree/reject-submodule-mode")]
    public async Task UnsupportedModesCannotBecomeSnapshotFiles(string mode)
    {
        await Initialize();
        var oid = await Blob(Encoding.UTF8.GetBytes("target"));
        if (mode == "160000") oid = await Commit(await Tree(("100644", "file.txt", oid)));
        var tree = await Tree((mode, "unsupported", oid));
        Reject(tree, "snapshot-unsupported-file-mode");
    }

    [TestCase(false, TestName = "git-tree/reject-case-colliding-files")]
    [TestCase(true, TestName = "git-tree/reject-case-colliding-directories")]
    public async Task CaseAliasesCannotMergeDistinctGitEntries(bool directories)
    {
        await Initialize();
        var oid = await Blob([1, 2, 3]);
        var mode = "100644";
        if (directories) { oid = await Tree((mode, "leaf.bin", oid)); mode = "40000"; }
        Reject(await Tree((mode, "Alias", oid), (mode, "alias", oid)), "snapshot-duplicate-or-case-colliding-path");
    }

    [TestCase("..", "invalid-path", TestName = "git-tree/reject-parent-segment")]
    [TestCase(".git", "invalid-path", TestName = "git-tree/reject-git-metadata-path")]
    [TestCase("back\\slash", "invalid-path", TestName = "git-tree/reject-backslash-path")]
    [TestCase("trailing.", "invalid-path", TestName = "git-tree/reject-windows-trailing-dot")]
    [TestCase("NUL.txt", "reserved-path", TestName = "git-tree/reject-windows-device-path")]
    public async Task UnsafeRawTreeNamesCannotWriteOutsideTheFreshRoot(string name, string reason)
    {
        await Initialize();
        var oid = await Blob([4, 5, 6]);
        await Stage("staged-safe.bin", [9, 8, 7]);
        var index = Path.Combine(_repository, ".git", "index");
        var indexBytes = File.ReadAllBytes(index);
        var sentinel = Path.Combine(_owner, "sentinel.bin");
        File.WriteAllBytes(sentinel, [9, 8, 7]);
        Reject(await Tree(("100644", name, oid)), "snapshot-" + reason);
        Assert.That(File.ReadAllBytes(sentinel), Is.EqualTo(new byte[] { 9, 8, 7 }));
        Assert.That(File.ReadAllBytes(index), Is.EqualTo(indexBytes));
    }

    [Test]
    public async Task ExplicitSubdirectoryCannotReadItsAncestorRepository()
    {
        await Initialize();
        var tree = await Tree(("100644", "file.bin", await Blob([1])));
        var nested = Path.Combine(_repository, "nested");
        Directory.CreateDirectory(nested);
        var error = Assert.Throws<GitTreeSnapshotException>(() => GitTreeSnapshot.Materialize(_git, nested, tree,
            Path.Combine(_owner, "snapshot"), Remaining()))!;
        Assert.That(error.Code, Is.EqualTo("snapshot-repository-marker-missing"));
        Assert.That(Directory.Exists(Path.Combine(_owner, "snapshot")), Is.False);
    }

    [Test]
    public async Task AWin32PathAliasCannotHideADestinationInsideTheRepository()
    {
        await Initialize();
        var tree = await Tree(("100644", "file.bin", await Blob([1])));
        var error = Assert.Throws<GitTreeSnapshotException>(() => GitTreeSnapshot.Materialize(_git, _repository, tree,
            Path.Combine(_repository + ".", "escaped-snapshot"), Remaining()))!;
        Assert.That(error.Code, Is.EqualTo("snapshot-destination-inside-repository"));
        Assert.That(Directory.Exists(Path.Combine(_repository, "escaped-snapshot")), Is.False);
    }

    [Test]
    public async Task AnExtendedNamespaceCannotHideAFreshDestinationInsideTheRepository()
    {
        await Initialize();
        var tree = await Tree(("100644", "file.bin", await Blob([1])));
        var destination = Path.Combine(_repository, "extended-snapshot");
        var error = Assert.Throws<GitTreeSnapshotException>(() => GitTreeSnapshot.Materialize(_git, _repository, tree,
            @"\\?\" + destination, Remaining()))!;
        Assert.That(error.Code, Is.EqualTo("snapshot-unsupported-namespace-path"));
        Assert.That(Directory.Exists(destination), Is.False);
    }

    [Test]
    public async Task RawShortAliasDirectoriesAreUnsupportedBeforeTheyCanMergeWithLongNames()
    {
        await Initialize();
        var blob = await Blob([1, 2, 3]);
        var longTree = await Tree(("100644", "first.bin", blob));
        var aliasTree = await Tree(("100644", "second.bin", blob));
        var tree = await Tree(("40000", "LongDirectoryName", longTree), ("40000", "LONGDI~1", aliasTree));
        Reject(tree, "snapshot-unsupported-alias-path");
        Assert.That(Directory.EnumerateFileSystemEntries(Path.Combine(_owner, "snapshot")), Is.Empty);
    }

    [TestCase(false, false, TestName = "git-tree/prep-namespace-context-rejects-before-write")]
    [TestCase(true, false, TestName = "git-tree/prep-namespace-root-rejects-before-write")]
    [TestCase(false, true, TestName = "git-tree/prep-alias-context-rejects-before-write")]
    [TestCase(true, true, TestName = "git-tree/prep-alias-root-rejects-before-write")]
    public async Task PublicPrepRejectsNamespaceAndAliasOwnersBeforeCreatingAnyOutput(bool rootAlias, bool shortAlias)
    {
        await Initialize();
        var implementation = ImplementationRoot();
        var snapshot = Path.Combine(_owner, "snapshot");
        var context = Path.Combine(_owner, shortAlias && !rootAlias ? "context~1.json" : "context.json");
        var repository = _repository;
        if (rootAlias)
        {
            if (shortAlias) { repository = Path.Combine(_owner, "repository~1"); Directory.CreateDirectory(repository); }
            else repository = @"\\?\" + repository;
        }
        else if (!shortAlias) context = @"\\?\" + context;
        var args = new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", Path.Combine(implementation, "tools/New-TestSnapshot.ps1"),
            "-RepositoryRoot", repository, "-SnapshotDirectory", snapshot, "-ContextPath", context };
        var result = await Run(Pwsh(), args);
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Stdout, Is.Empty);
        Assert.That(new UTF8Encoding(false, true).GetString(result.Stderr), Does.Contain(shortAlias
            ? "test-snapshot-unsupported-alias-path" : "test-snapshot-unsupported-namespace-path"));
        Assert.That(Directory.Exists(snapshot), Is.False);
        var ordinaryContext = shortAlias && !rootAlias ? Path.Combine(_owner, "context~1.json") : Path.Combine(_owner, "context.json");
        Assert.That(File.Exists(ordinaryContext), Is.False);
        Assert.That(Directory.Exists(ordinaryContext + ".preparation"), Is.False);
        Assert.That(File.Exists(Path.Combine(_repository, ".git", "index")), Is.False);
    }

    [Test]
    public async Task ACorruptLooseBlobCannotPassItsDeclaredObjectIdentity()
    {
        await Initialize();
        var oid = await Blob(Encoding.UTF8.GetBytes("original bytes"));
        var tree = await Tree(("100644", "payload.bin", oid));
        var objectPath = Path.Combine(_repository, ".git", "objects", oid[..2], oid[2..]);
        File.SetAttributes(objectPath, FileAttributes.Normal);
        using (var output = File.Create(objectPath))
        using (var compressed = new ZLibStream(output, CompressionLevel.Optimal))
            compressed.Write(Encoding.ASCII.GetBytes("blob 13\0changed bytes"));
        var error = Assert.Throws<GitTreeSnapshotException>(() => Materialize(tree))!;
        Assert.That(error.Code, Is.AnyOf("snapshot-object-oid-mismatch", "snapshot-missing-or-invalid-object", "snapshot-truncated-object"));
        Assert.That(error.InnerException, Is.Null);
    }

    [Test]
    public async Task AnExistingDestinationCannotBeReusedOrOverwriteItsSentinel()
    {
        await Initialize();
        var tree = await Tree(("100644", "sentinel.bin", await Blob([1])));
        var destination = Path.Combine(_owner, "snapshot");
        Directory.CreateDirectory(destination);
        File.WriteAllBytes(Path.Combine(destination, "sentinel.bin"), [9]);
        Reject(tree, "snapshot-destination-not-fresh");
        Assert.That(File.ReadAllBytes(Path.Combine(destination, "sentinel.bin")), Is.EqualTo(new byte[] { 9 }));
    }

    [Test]
    public async Task AReparseAncestorCannotRedirectTheFreshSnapshot()
    {
        await Initialize();
        var tree = await Tree(("100644", "sentinel.bin", await Blob([1])));
        var target = Path.Combine(_owner, "outside");
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "sentinel.bin"), [9]);
        var link = Path.Combine(_owner, "junction");
        var script = Path.Combine(_owner, "junction.ps1");
        File.WriteAllText(script, "param([string]$Link,[string]$Target)\nNew-Item -ItemType Junction -Path $Link -Target $Target | Out-Null\n", new UTF8Encoding(false));
        var result = await Run(Pwsh(), ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", script, "-Link", link, "-Target", target]);
        Assert.That(result.ExitCode, Is.Zero);
        _ownedJunction = link;
        _ownedJunctionTarget = target;
        Assert.That((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, Is.True);
        var error = Assert.Throws<GitTreeSnapshotException>(() => GitTreeSnapshot.Materialize(_git, _repository, tree,
            Path.Combine(link, "snapshot"), Remaining()))!;
        Assert.That(error.Code, Is.EqualTo("snapshot-missing-or-reparse-path"));
        Assert.That(File.Exists(Path.Combine(target, "snapshot", "sentinel.bin")), Is.False);
        Assert.That(File.ReadAllBytes(Path.Combine(target, "sentinel.bin")), Is.EqualTo(new byte[] { 9 }));
    }

    [TestCase(false, false, TestName = "git-tree/prep-alternate-index-uses-staged-helper")]
    [TestCase(true, false, TestName = "git-tree/prep-alternate-split-index-uses-staged-helper")]
    [TestCase(false, true, TestName = "git-tree/prep-relative-alternate-index-uses-staged-helper")]
    public async Task PublicPrepUsesTheActualAlternateIndexAndStagedHelperWithoutChangingEitherIndex(bool split, bool relative)
    {
        await Initialize();
        await Stage("default-only.txt", Encoding.UTF8.GetBytes("default index"));
        var head = await Commit(await GitText(["write-tree"]));
        await Git(["update-ref", "HEAD", head]);
        var defaultIndex = Path.Combine(_repository, ".git", "index");
        var defaultBytes = File.ReadAllBytes(defaultIndex);
        var alternate = Path.Combine(_owner, "alternate.index");
        await Git(["read-tree", "--empty"], index: alternate);
        var implementation = ImplementationRoot();
        var helperBytes = File.ReadAllBytes(Path.Combine(implementation, HelperPath));
        await Stage(HelperPath, helperBytes, index: alternate);
        byte[] payload = [0, 255, 13, 10, 42];
        await Stage("payload.bin", payload, index: alternate);
        if (split) await Git(["update-index", "--split-index"], index: alternate);
        var expectedTree = await GitText(["write-tree"], index: alternate);
        var alternateBytes = File.ReadAllBytes(alternate);
        var shared = await GitText(["rev-parse", "--shared-index-path"], index: alternate);
        byte[]? sharedBytes = string.IsNullOrEmpty(shared) ? null : File.ReadAllBytes(Path.GetFullPath(shared, _repository));
        Assert.That(sharedBytes is not null, Is.EqualTo(split));
        var liveHelper = Path.Combine(_repository, HelperPath);
        Directory.CreateDirectory(Path.GetDirectoryName(liveHelper)!);
        File.WriteAllText(liveHelper, "this working helper is deliberately invalid C#", new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(_repository, "payload.bin"), [7]);
        File.WriteAllBytes(Path.Combine(_repository, "untracked.bin"), [8]);

        var result = await Prep(implementation, relative ? Path.GetRelativePath(_repository, alternate) : alternate, expectedTree);
        Assert.That(result.ExitCode, Is.Zero);
        using var envelope = JsonDocument.Parse(result.Stdout);
        var metadata = envelope.RootElement;
        Assert.That(metadata.GetProperty("complete").GetBoolean(), Is.True);
        Assert.That(metadata.GetProperty("baselineClaim").GetBoolean(), Is.False);
        Assert.That(metadata.GetProperty("tree").GetString(), Is.EqualTo(expectedTree));
        Assert.That(metadata.GetProperty("indexPath").GetString(), Is.EqualTo(alternate).IgnoreCase);
        Assert.That(File.ReadAllBytes(defaultIndex), Is.EqualTo(defaultBytes));
        Assert.That(File.ReadAllBytes(alternate), Is.EqualTo(alternateBytes));
        if (sharedBytes is not null) Assert.That(File.ReadAllBytes(Path.GetFullPath(shared, _repository)), Is.EqualTo(sharedBytes));
        using var context = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_owner, "context.json")));
        Assert.That(context.RootElement.GetProperty("sourceCommit").GetString(), Is.EqualTo(head));
        Assert.That(context.RootElement.GetProperty("files").GetArrayLength(), Is.EqualTo(2));
        var snapshot = Path.Combine(_owner, "snapshot");
        Assert.That(File.ReadAllBytes(Path.Combine(snapshot, HelperPath)), Is.EqualTo(helperBytes));
        Assert.That(File.ReadAllBytes(Path.Combine(snapshot, "payload.bin")), Is.EqualTo(payload));
        Assert.That(File.Exists(Path.Combine(snapshot, "default-only.txt")), Is.False);
        Assert.That(File.Exists(Path.Combine(snapshot, "untracked.bin")), Is.False);
    }

    [TestCase(false, "test-snapshot-materializer-not-staged", TestName = "git-tree/prep-rejects-working-only-helper")]
    [TestCase(true, "test-snapshot-unmerged-index", TestName = "git-tree/prep-rejects-unmerged-index")]
    public async Task PublicPrepCannotFallBackToAWorkingHelperOrFreezeConflicts(bool conflict, string reason)
    {
        await Initialize();
        await Stage("payload.bin", [1]);
        await Git(["update-ref", "HEAD", await Commit(await GitText(["write-tree"]))]);
        var implementation = ImplementationRoot();
        var helperBytes = File.ReadAllBytes(Path.Combine(implementation, HelperPath));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_repository, HelperPath))!);
        File.WriteAllBytes(Path.Combine(_repository, HelperPath), helperBytes);
        if (conflict)
        {
            await Stage(HelperPath, helperBytes);
            var oid = await Blob([2]);
            await Git(["update-index", "--index-info"], Encoding.ASCII.GetBytes($"100644 {oid} 1\tconflict.txt\n100644 {oid} 2\tconflict.txt\n"));
        }
        var index = Path.Combine(_repository, ".git", "index");
        var before = File.ReadAllBytes(index);
        var result = await Prep(implementation, index);
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(new UTF8Encoding(false, true).GetString(result.Stderr), Does.Contain(reason));
        Assert.That(result.Stdout, Is.Empty);
        Assert.That(File.Exists(Path.Combine(_owner, "context.json")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(_owner, "snapshot")), Is.False);
        Assert.That(File.ReadAllBytes(index), Is.EqualTo(before));
    }

    private GitTreeSnapshotResult Materialize(string tree)
        => GitTreeSnapshot.Materialize(_git, _repository, tree, Path.Combine(_owner, "snapshot"), Remaining());
    private void Reject(string tree, string code)
    {
        var error = Assert.Throws<GitTreeSnapshotException>(() => Materialize(tree))!;
        Assert.That(error.Code, Is.EqualTo(code));
        Assert.That(error.Message, Is.EqualTo(code));
        Assert.That(error.InnerException, Is.Null);
    }
    private Task Initialize(string format = "sha1") => Git(["init", "--quiet", "--object-format=" + format]);
    private async Task<string> Blob(byte[] bytes) => await GitText(["hash-object", "-w", "--stdin"], bytes);
    private async Task Stage(string path, byte[] bytes, string mode = "100644", string? index = null)
    {
        await Git(["update-index", "--add", "--cacheinfo", mode + "," + await Blob(bytes) + "," + path], index: index);
    }
    private async Task<string> Commit(string tree)
        => await GitText(["hash-object", "-t", "commit", "-w", "--stdin"], Encoding.ASCII.GetBytes(
            $"tree {tree}\nauthor Fixture <fixture@example.invalid> 1700000000 +0000\ncommitter Fixture <fixture@example.invalid> 1700000000 +0000\n\nfixture\n"));
    private async Task<string> Tree(params (string Mode, string Name, string Oid)[] entries)
    {
        using var bytes = new MemoryStream();
        foreach (var entry in entries)
        {
            bytes.Write(Encoding.UTF8.GetBytes(entry.Mode + " " + entry.Name + "\0"));
            bytes.Write(Convert.FromHexString(entry.Oid));
        }
        return await GitText(["hash-object", "-t", "tree", "--literally", "-w", "--stdin"], bytes.ToArray());
    }
    private async Task<string> GitText(string[] args, byte[]? input = null, string? index = null)
        => new UTF8Encoding(false, true).GetString(await Git(args, input, index)).TrimEnd('\r', '\n');
    private async Task<byte[]> Git(string[] args, byte[]? input = null, string? index = null)
    {
        var arguments = new[] { "-c", "core.hooksPath=" + Path.Combine(_owner, "no-hooks") }.Concat(args).ToArray();
        var result = await Run(_git, arguments, input, index);
        Assert.That(result.ExitCode, Is.Zero, "Disposable Git fixture command failed; retained raw files own its diagnostics.");
        return result.Stdout;
    }
    private Task<RawProcess> Prep(string implementation, string index, string? expectedTree = null)
    {
        var args = new List<string> { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", Path.Combine(implementation, "tools/New-TestSnapshot.ps1"),
            "-RepositoryRoot", _repository, "-SnapshotDirectory", Path.Combine(_owner, "snapshot"), "-ContextPath", Path.Combine(_owner, "context.json") };
        if (expectedTree is not null) { args.Add("-ExpectedTree"); args.Add(expectedTree); }
        return Run(Pwsh(), args.ToArray(), index: index);
    }

    private async Task<RawProcess> Run(string executable, string[] arguments, byte[]? input = null, string? index = null)
    {
        _ = Remaining();
        var prefix = Path.Combine(_owner, "process-" + (++_ordinal).ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
        File.WriteAllText(prefix + ".argv.json", JsonSerializer.Serialize(new { executable, arguments, index }), new UTF8Encoding(false));
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = _repository, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        if (index is not null) start.Environment["GIT_INDEX_FILE"] = index;
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        using var process = new Process { StartInfo = start };
        using var output = new MemoryStream();
        using var errorOutput = new MemoryStream();
        Task? stdout = null; Task? stderr = null;
        var started = false;
        Exception? primary = null;
        try
        {
            started = process.Start();
            if (!started) throw new InvalidOperationException("git-tree-fixture-process-start");
            stdout = process.StandardOutput.BaseStream.CopyToAsync(output);
            stderr = process.StandardError.BaseStream.CopyToAsync(errorOutput);
            if (input is not null)
            {
                await process.StandardInput.BaseStream.WriteAsync(input).AsTask().WaitAsync(Remaining());
                process.StandardInput.Close();
            }
            await Task.WhenAll(process.WaitForExitAsync(), stdout, stderr).WaitAsync(Remaining());
            _ = Remaining();
            return new(process.ExitCode, output.ToArray(), errorOutput.ToArray());
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            if (primary is not null && started)
            {
                var cleanup = Stopwatch.StartNew();
                _diagnosticClock ??= cleanup;
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await Task.WhenAll(new[] { process.WaitForExitAsync(), stdout, stderr }.OfType<Task>()).WaitAsync(DrainRemaining(cleanup));
                }
                catch (Exception cleanupError) { TestContext.Error.WriteLine("git-tree: bounded cleanup failed (" + cleanupError.GetType().Name + ")."); }
            }
            try
            {
                File.WriteAllBytes(prefix + ".stdout.bin", output.ToArray());
                File.WriteAllBytes(prefix + ".stderr.bin", errorOutput.ToArray());
            }
            catch when (primary is not null) { }
        }
    }

    private TimeSpan Remaining() => _clock.Elapsed < TimeSpan.FromSeconds(120)
        ? TimeSpan.FromSeconds(120) - _clock.Elapsed : throw new TimeoutException("git-tree-fixture-root-deadline");
    private static TimeSpan DrainRemaining(Stopwatch clock) => clock.Elapsed < TimeSpan.FromSeconds(10)
        ? TimeSpan.FromSeconds(10) - clock.Elapsed : throw new TimeoutException("git-tree-fixture-cleanup-deadline");
    private TimeSpan ArchiveRemaining(Stopwatch diagnostics)
    {
        var root = TimeSpan.FromSeconds(120) - _clock.Elapsed;
        var diagnostic = TimeSpan.FromSeconds(10) - diagnostics.Elapsed;
        var remaining = root < diagnostic ? root : diagnostic;
        return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException("git-tree-fixture-archive-deadline");
    }
    private static string Pwsh()
    {
        var value = Environment.GetEnvironmentVariable("FISHAUDIO_PWSH_EXECUTABLE");
        if (value is null || !Path.IsPathFullyQualified(value) || !File.Exists(value)) throw new InvalidOperationException("git-tree-fixture-pwsh-required");
        return value;
    }
    private static string Executable(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.Combine(directory.Trim('"'), name);
            if (Path.IsPathFullyQualified(path) && File.Exists(path)) return Path.GetFullPath(path);
        }
        throw new InvalidOperationException("git-tree-fixture-executable-missing");
    }
    private static void RequireDirectory(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException("git-tree-fixture-relative-directory");
        for (var item = new DirectoryInfo(path); item is not null; item = item.Parent)
            if (!item.Exists || (item.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("git-tree-fixture-directory-missing-or-reparse");
    }
    private static string ImplementationRoot()
    {
        var root = Environment.GetEnvironmentVariable("FISHAUDIO_TEST_REPOSITORY_ROOT");
        if (Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT") is { Length: > 0 } contextPath)
        {
            if (!Path.IsPathFullyQualified(contextPath)) throw new InvalidOperationException("git-tree-fixture-relative-context");
            using var context = JsonDocument.Parse(File.ReadAllBytes(contextPath));
            var snapshotRoot = context.RootElement.GetProperty("root").GetString();
            if (root is not null && !string.Equals(Path.GetFullPath(root), snapshotRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("git-tree-fixture-context-root-mismatch");
            root = snapshotRoot;
        }
        if (string.IsNullOrEmpty(root)) throw new InvalidOperationException("git-tree-fixture-explicit-root-required");
        RequireDirectory(root);
        return Path.GetFullPath(root);
    }
    private sealed record RawProcess(int ExitCode, byte[] Stdout, byte[] Stderr);
}
