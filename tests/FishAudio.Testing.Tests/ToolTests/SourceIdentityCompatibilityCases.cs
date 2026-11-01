using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests.ToolTests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Property("FishAudioCoverageResponsibility", "RepositoryResourcesOnly")]
[Property("FishAudioCodeDependency", "tests/FishAudio.Testing.Tests/ToolTests/SourceIdentityCompatibilityCases.cs")]
[Property("FishAudioRepositoryDependency", "tools/Test.ps1")]
[Property("FishAudioRepositoryDependency", "tools/HybridSession.Common.ps1")]
[Property("FishAudioRepositoryDependency", "tools/TestProcessOwner.ps1")]
public sealed class SourceIdentityCompatibilityCases
{
    private const string Prefix = "fa-source-identity-";
    private readonly Stopwatch _clock = new();
    private string _parent = "";
    private string _root = "";
    private string _repository = "";
    private string _shell = "";

    [SetUp]
    public void PrepareOwnedSourceFixture()
    {
        _clock.Start();
        _repository = Environment.GetEnvironmentVariable("FISHAUDIO_TEST_REPOSITORY_ROOT") ?? "";
        if (Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT") is { Length: > 0 } contextPath)
        {
            using var context = JsonDocument.Parse(File.ReadAllBytes(contextPath));
            var snapshot = context.RootElement.GetProperty("root").GetString()!;
            if (_repository.Length != 0 && !SamePath(snapshot, _repository))
                throw new InvalidOperationException("source-identity-fixture-root-mismatch");
            _repository = snapshot;
        }
        Require(Path.IsPathFullyQualified(_repository) && Directory.Exists(_repository), "source-identity-fixture-explicit-root");
        _shell = Environment.GetEnvironmentVariable("FISHAUDIO_PWSH_EXECUTABLE") ?? "";
        Require(Path.IsPathFullyQualified(_shell) && File.Exists(_shell), "source-identity-fixture-explicit-shell");
        var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
        _parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(string.IsNullOrEmpty(configured) ? Path.GetTempPath() : configured));
        RequireRegularDirectory(_parent);
        _root = Path.Combine(_parent, Prefix + Guid.NewGuid().ToString("N"));
        Require(!Directory.Exists(_root) && !File.Exists(_root), "source-identity-fixture-not-fresh");
        Directory.CreateDirectory(_root);
        WriteNew(Path.Combine(_root, "contract.ps1"), Contract);
    }

    [TearDown]
    public void ReleaseOwnedSourceFixture()
    {
        if (_root.Length == 0 || !Directory.Exists(_root)) return;
        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
        {
            TestContext.Error.WriteLine("source-identity: failed fixture retained as " + Path.GetFileName(_root) + ".");
            return;
        }
        Require(SamePath(Path.GetDirectoryName(_root)!, _parent) && Path.GetFileName(_root).StartsWith(Prefix, StringComparison.Ordinal) &&
            Guid.TryParseExact(Path.GetFileName(_root)[Prefix.Length..], "N", out _), "source-identity-fixture-owner-changed");
        var pending = new Stack<string>(); pending.Push(_root);
        while (pending.TryPop(out var directory))
        {
            Require((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0, "source-identity-fixture-directory-reparse");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                Require((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) == 0, "source-identity-fixture-entry-reparse");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }
        Directory.Delete(_root, recursive: true);
    }

    [Test, Property("FishAudioCaseId", "infra.source-identity/binary-fingerprint-and-context-lock-lifetimes")]
    public Task BinaryFingerprintAndContextLocksRetainTheirActualBoundaries() => RunAsync("positive");

    [Test, Property("FishAudioCaseId", "infra.source-identity/unicode-equivalent-rows-remain-ambiguous")]
    public Task OrdinallyDistinctUnicodeRowsRemainAmbiguousToTheActualReaders() => RunAsync("unicode");

    [Test, Property("FishAudioCaseId", "infra.source-identity/duplicate-inventory-rejects")]
    public Task ExactDuplicateInventoryRowsReject() => RunAsync("duplicate");

    [Test, Property("FishAudioCaseId", "infra.source-identity/missing-source-rejects")]
    public Task MissingSourceCannotAcquireAnIdentity() => RunAsync("missing");

    [Test, Property("FishAudioCaseId", "infra.source-identity/changed-source-bytes-reject")]
    public Task SourceDigestFailureRetainsTheOriginalBytes() => RunAsync("changed");

    [Test, Property("FishAudioCaseId", "infra.source-identity/context-and-confinement-boundaries")]
    public Task ContextAndConfinementKeepTheirOriginalReaderBoundaries() => RunAsync("context");

    private async Task RunAsync(string mode)
    {
        _ = RemainingRoot();
        var start = new ProcessStartInfo(_shell)
        {
            WorkingDirectory = _repository, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", Path.Combine(_root, "contract.ps1"),
                     "-Repository", _repository, "-Fixture", _root, "-Mode", mode }) start.ArgumentList.Add(argument);
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("FISHAUDIO_", StringComparison.OrdinalIgnoreCase) ||
                     key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        using var process = new Process { StartInfo = start };
        using var output = new FileStream(Path.Combine(_root, "stdout.bin"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var error = new FileStream(Path.Combine(_root, "stderr.bin"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        Task? exited = null; Task? stdout = null; Task? stderr = null;
        Exception? primary = null; var started = false;
        try
        {
            started = process.Start(); Require(started, "source-identity-fixture-process-start");
            stdout = process.StandardOutput.BaseStream.CopyToAsync(output);
            stderr = process.StandardError.BaseStream.CopyToAsync(error);
            exited = process.WaitForExitAsync();
            await Task.WhenAll(exited, stdout, stderr).WaitAsync(RemainingRoot());
            output.Flush(flushToDisk: true); error.Flush(flushToDisk: true);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            if (primary is not null && started)
            {
                var cleanup = Stopwatch.StartNew();
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    exited ??= process.WaitForExitAsync();
                    await Task.WhenAll(new[] { exited, stdout, stderr }.OfType<Task>()).WaitAsync(RemainingCleanup(cleanup));
                    output.Flush(flushToDisk: true); error.Flush(flushToDisk: true);
                }
                catch (Exception secondary)
                { TestContext.Error.WriteLine("source-identity: bounded process cleanup failed (" + secondary.GetType().Name + ")."); }
            }
        }
        AssertCompletedOutput(process, output, error, mode);
    }

    // Read the completed byte drains only after their writers have released the files.
    private void AssertCompletedOutput(Process process, FileStream output, FileStream error, string mode)
    {
        output.Dispose(); error.Dispose();
        Assert.That(process.ExitCode, Is.Zero, File.ReadAllText(Path.Combine(_root, "stderr.bin"), Encoding.UTF8));
        Assert.That(File.ReadAllText(Path.Combine(_root, "stdout.bin"), Encoding.UTF8), Does.Contain("source-identity-passed:" + mode));
    }

    private static void RequireRegularDirectory(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
            Require(directory.Exists && (directory.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) == 0,
                "source-identity-fixture-parent-reparse");
    }

    // Existing ProcessTransportTests fixture allowances, not a performance assertion.
    private TimeSpan RemainingRoot() => Remaining(TimeSpan.FromSeconds(120), _clock, "source-identity-fixture-root-deadline");
    private static TimeSpan RemainingCleanup(Stopwatch clock) => Remaining(TimeSpan.FromSeconds(10), clock, "source-identity-fixture-cleanup-deadline");
    private static TimeSpan Remaining(TimeSpan allowance, Stopwatch clock, string code)
    {
        var remaining = allowance - clock.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException(code);
    }
    private static bool SamePath(string first, string second) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(first))
        .Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);
    private static void Require(bool value, string code) { if (!value) throw new InvalidOperationException(code); }
    private static void WriteNew(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(new UTF8Encoding(false).GetBytes(text));
    }

    private const string Contract = """
        param([string]$Repository,[string]$Fixture,[ValidateSet('positive','unicode','duplicate','missing','changed','context')][string]$Mode)
        Set-StrictMode -Version Latest
        $ErrorActionPreference='Stop'
        function Check([bool]$Value,[string]$Code){if(-not $Value){throw $Code}}
        $sourcePath=Join-Path $Repository 'tools/Test.ps1'
        $tokens=$null;$errors=$null
        $ast=[Management.Automation.Language.Parser]::ParseFile($sourcePath,[ref]$tokens,[ref]$errors)
        Check ($errors.Count -eq 0) 'fixture-source-parse'
        foreach($name in @('Require-Condition','Get-Digest','Test-SamePath','Test-ContainedPath','Get-SourceIdentity')){
            $found=@($ast.FindAll({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name},$true))
            Check ($found.Count -eq 1) 'fixture-source-function'
            . ([scriptblock]::Create($found[0].Extent.Text))
        }
        # Only the snapshot branch is under test. A missing context cannot silently invoke real Git.
        function Invoke-MetadataGit{throw 'fixture-unexpected-live-fallback'}
        . (Join-Path $Repository 'tools/HybridSession.Common.ps1')
        $utf8=[Text.UTF8Encoding]::new($false)
        function Bytes([string]$Path,[byte[]]$Value){
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path))|Out-Null
            $s=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
            try{$s.Write($Value,0,$Value.Length)}finally{$s.Dispose()}
        }
        function Digest([byte[]]$Value){[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Value)).ToLowerInvariant()}
        function Context([string]$Path,[string]$Source,$Rows,[string]$Tree=('a'*40)){
            $value=@{schemaVersion=1;root=$Source;tree=$Tree;sourceCommit=$null;files=@($Rows)}
            Bytes $Path $utf8.GetBytes(($value|ConvertTo-Json -Depth 12))
        }
        function ReadIdentity([string]$Reader,[string]$Source,[string]$Context){
            $root=$Source;$runRoot=Join-Path $Fixture 'excluded-output';$env:FISHAUDIO_SNAPSHOT_CONTEXT=$Context
            if($Reader -ceq 'framework'){return Get-SourceIdentity}
            return (Get-HybridSource $Source (Join-Path $Fixture 'hybrid-read')).identity
        }
        function Reject([string]$Reader,[string]$Source,[string]$Context,[string]$Expected){
            $rejected=$false
            try{[void](ReadIdentity $Reader $Source $Context)}catch{
                $rejected=$true
                if($_.Exception.Message -cne $Expected){throw}
            }
            Check $rejected ('fixture-invalid-source-admitted:'+ $Reader)
        }
        function Exclusive([string]$Path,[bool]$Blocked){
            $opened=$null;$denied=$false
            try{$opened=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}
            catch [IO.IOException]{$denied=$true}
            finally{if($null -ne $opened){$opened.Dispose()}}
            Check ($denied -eq $Blocked) 'fixture-original-lock-boundary'
        }
        $primary=$null
        try{
            foreach($reader in @('framework','hybrid')){
                Close-HybridLocks
                $source=Join-Path $Fixture $reader;[IO.Directory]::CreateDirectory($source)|Out-Null
                $context=Join-Path $Fixture ($reader+'.json')
                if($Mode -ceq 'positive'){
                    $first=[byte[]]@(0,255,128,13,10,0);$second=[byte[]]@(195,169,13,10,255)
                    Bytes (Join-Path $source 'A.bin') $first;Bytes (Join-Path $source 'nested/z.bin') $second
                    $rows=@(@{path='nested/z.bin';sha256=(Digest $second)},@{path='A.bin';sha256=(Digest $first)})
                    Context $context $source $rows
                    Exclusive $context $false;Exclusive (Join-Path $source 'A.bin') $false
                    $identity=ReadIdentity $reader $source $context
                    Check ($identity.fingerprint -ceq 'b6f6b4ed9e7707a474a945b42efb395a61ea62a884b204e0681129a33310702f' -and $identity.fingerprintKind -ceq 'sha256-source-path-and-bytes-ordinal-v1') 'fixture-binary-golden'
                    Check ($identity.snapshot -and $identity.snapshotRootValidated -and $identity.tree -ceq ('a'*40) -and $null -eq $identity.sourceCommit -and $null -eq $identity.commit) 'fixture-snapshot-identity'
                    Check ($identity.contextSha256 -ceq (Digest ([IO.File]::ReadAllBytes($context)))) 'fixture-raw-context-hash'
                    Exclusive $context ($reader -ceq 'hybrid');Exclusive (Join-Path $source 'A.bin') ($reader -ceq 'hybrid')
                    $other=Join-Path $Fixture ($reader+'-reordered.json');Context $other $source @($rows[1],$rows[0])
                    $again=ReadIdentity $reader $source $other
                    Check ($again.fingerprint -ceq $identity.fingerprint -and $again.contextSha256 -cne $identity.contextSha256) 'fixture-context-reorder'
                    Close-HybridLocks;Exclusive $context $false;Exclusive (Join-Path $source 'A.bin') $false
                    Check ([Convert]::ToHexString([IO.File]::ReadAllBytes((Join-Path $source 'A.bin'))) -ceq '00FF800D0A00') 'fixture-original-binary-bytes'
                    Check ([Convert]::ToHexString([IO.File]::ReadAllBytes((Join-Path $source 'nested/z.bin'))) -ceq 'C3A90D0AFF') 'fixture-second-binary-bytes'
                }elseif($Mode -ceq 'unicode'){
                    $nfc='caf'+[char]0x00e9+'.bin';$nfd='cafe'+[char]0x0301+'.bin'
                    Check (-not [StringComparer]::Ordinal.Equals($nfc,$nfd) -and ($nfc -ceq $nfd)) 'fixture-actual-unicode-comparison'
                    Bytes (Join-Path $source $nfc) ([byte[]]@(1));Bytes (Join-Path $source $nfd) ([byte[]]@(2))
                    Context $context $source @(@{path=$nfc;sha256=(Digest ([byte[]]@(1)))},@{path=$nfd;sha256=(Digest ([byte[]]@(2)))})
                    $expected=if($reader -ceq 'framework'){'test-driver-snapshot-file-changed'}else{'hybrid-snapshot-file-changed'}
                    Reject $reader $source $context $expected
                }elseif($Mode -ceq 'context'){
                    Bytes (Join-Path $source '7') ([byte[]]@(0,1,2));$row=@{path='7';sha256=(Digest ([byte[]]@(0,1,2)))}
                    $wrong=Join-Path $Fixture 'wrong-root';Context $context $wrong @($row)
                    Reject $reader $source $context $(if($reader -ceq 'framework'){'test-driver-snapshot-root-or-tree-mismatch'}else{'hybrid-snapshot-context'})
                    Close-HybridLocks
                    $badTree=Join-Path $Fixture ($reader+'-bad-tree.json');Context $badTree $source @($row) 'bad-tree'
                    Reject $reader $source $badTree $(if($reader -ceq 'framework'){'test-driver-snapshot-root-or-tree-mismatch'}else{'hybrid-snapshot-context'})
                    Close-HybridLocks
                    $outside=Join-Path $Fixture ($reader+'-outside.bin');Bytes $outside ([byte[]]@(3))
                    $escape=Join-Path $Fixture ($reader+'-escape.json');Context $escape $source @(@{path=('../'+$reader+'-outside.bin');sha256=(Digest ([byte[]]@(3)))})
                    Reject $reader $source $escape $(if($reader -ceq 'framework'){'test-driver-source-file-outside-root'}else{'hybrid-source-file-escape'})
                    Close-HybridLocks
                    # Hybrid historically accepts a numeric path through its original comparison. Do not harden it incidentally.
                    $numeric=Join-Path $Fixture ($reader+'-numeric.json');Context $numeric $source @(@{path=7;sha256=$row.sha256})
                    if($reader -ceq 'framework'){Reject $reader $source $numeric 'test-driver-invalid-snapshot-file'}
                    else{Check ((ReadIdentity $reader $source $numeric).fingerprint -ceq 'dda914e9ac632173756df12664f1d5daf973c2ec927b5226e8b1ec71afde0319') 'fixture-hybrid-original-numeric-path'}
                }else{
                    $value=[byte[]]@(10,0,255);Bytes (Join-Path $source 'owned.bin') $value
                    $rows=@(@{path='owned.bin';sha256=(Digest $value)})
                    if($Mode -ceq 'duplicate'){$rows+=@{path='owned.bin';sha256=(Digest $value)}}
                    Context $context $source $rows
                    if($Mode -ceq 'missing'){[IO.File]::Delete((Join-Path $source 'owned.bin'))}
                    if($Mode -ceq 'changed'){[IO.File]::WriteAllBytes((Join-Path $source 'owned.bin'),[byte[]]@(10,0,254))}
                    $expected=if($reader -ceq 'framework'){
                        if($Mode -ceq 'duplicate'){'test-driver-snapshot-file-changed'}elseif($Mode -ceq 'missing'){'test-driver-source-file-missing'}else{'test-driver-snapshot-file-changed'}
                    }else{
                        if($Mode -ceq 'duplicate'){'hybrid-snapshot-file-changed'}elseif($Mode -ceq 'missing'){'hybrid-file-not-regular'}else{'hybrid-snapshot-file-changed'}
                    }
                    Reject $reader $source $context $expected
                    if($Mode -ceq 'changed'){Check ([Convert]::ToHexString([IO.File]::ReadAllBytes((Join-Path $source 'owned.bin'))) -ceq '0A00FE') 'fixture-changed-bytes-retained'}
                }
            }
            [Console]::Out.WriteLine('source-identity-passed:'+$Mode)
        }catch{$primary=$_;[Console]::Error.WriteLine($_.Exception.Message);exit 1}
        finally{
            try{Close-HybridLocks}catch{if($null -eq $primary){throw};[Console]::Error.WriteLine('fixture-secondary-lock-release-failure')}
        }
        """;
}
