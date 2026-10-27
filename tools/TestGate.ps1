#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [string]$CheckNonce,
    [int]$CheckExitCode,
    [switch]$FinalizeCommit,
    [string]$MessagePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$encoding = [Text.UTF8Encoding]::new($false)
$root = [IO.Path]::GetFullPath($RepositoryRoot)

function Invoke-GateGit {
    param([string[]]$Arguments)
    $output = & git -C $root @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'gate-git-failed' }
    return ($output -join "`n").Trim()
}

$state = Invoke-GateGit @('rev-parse', '--path-format=absolute', '--git-path', 'fish-audio-testing')
$installedRoot = Join-Path $state 'hooks'
if (-not [IO.Path]::GetFullPath($PSScriptRoot).Equals([IO.Path]::GetFullPath($installedRoot), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'gate-requires-installed-entry'
}
$installation = [IO.File]::ReadAllText((Join-Path $installedRoot 'installation.json')) | ConvertFrom-Json
if ($installation.schemaVersion -ne 1 -or $installation.gateProtocolVersion -ne 2 -or
    -not $root.Equals($installation.repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'gate-installation-owner-mismatch'
}

# Git does not pass arguments to pre-commit during a commit. Only the installer
# supplies this one-use challenge through `git hook run ... -- <arguments>`.
if ($CheckNonce) {
    $challengePath = Join-Path $installedRoot 'check.json'
    $challenge = [IO.File]::ReadAllText($challengePath) | ConvertFrom-Json
    if ($CheckNonce -cnotmatch '^[a-f0-9]{32}$' -or $challenge.nonce -cne $CheckNonce -or
        $challenge.exitCode -ne $CheckExitCode -or
        ([DateTimeOffset]::UtcNow - [DateTimeOffset]::Parse($challenge.createdUtc)).TotalSeconds -lt 0 -or
        ([DateTimeOffset]::UtcNow - [DateTimeOffset]::Parse($challenge.createdUtc)).TotalSeconds -gt 60) {
        throw 'gate-invalid-installation-challenge'
    }
    [IO.File]::Delete($challengePath)
    [IO.File]::WriteAllText((Join-Path $installedRoot 'check-ack'), $CheckNonce, $encoding)
    exit $CheckExitCode
}

$receiptPath = Join-Path $state 'commit-receipt.json'
if ($FinalizeCommit) {
    if ($installation.previousHooks -and (Test-Path -LiteralPath (Join-Path $installation.previousHooks 'commit-msg') -PathType Leaf)) {
        & git -C $root -c ('core.hooksPath=' + $installation.previousHooks) hook run commit-msg -- $MessagePath
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    $receipt = [IO.File]::ReadAllText($receiptPath) | ConvertFrom-Json
    if ($receipt.runId -cnotmatch '^[a-f0-9]{32}$') { throw 'gate-invalid-commit-receipt' }
    $completedGateBytes = [IO.File]::ReadAllBytes((Join-Path $state ('runs/' + $receipt.runId + '/gate.json')))
    if ($receipt.gateSha256 -cnotmatch '^[a-f0-9]{64}$' -or
        [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($completedGateBytes)).ToLowerInvariant() -cne $receipt.gateSha256) {
        throw 'gate-completed-manifest-changed-after-pre-commit'
    }
    $completedGate = $encoding.GetString($completedGateBytes) | ConvertFrom-Json
    $finalTree = Invoke-GateGit @('write-tree')
    $finalIndex = Invoke-GateGit @('rev-parse', '--path-format=absolute', '--git-path', 'index')
    if ($env:GIT_INDEX_FILE) { $finalIndex = [IO.Path]::GetFullPath($env:GIT_INDEX_FILE, $root) }
    if ($receipt.gateProtocolVersion -ne 2 -or $completedGate.gateProtocolVersion -ne 2 -or
        $completedGate.mode -cne 'bootstrap/full/hybrid' -or -not $completedGate.platformVerified -or
        -not $completedGate.complete -or $completedGate.exitCode -ne 0 -or $completedGate.snapshotTree -cne $receipt.tree -or
        $finalTree -cne $receipt.tree -or -not $finalIndex.Equals($receipt.indexPath, [StringComparison]::OrdinalIgnoreCase) -or
        (Get-FileHash -LiteralPath $finalIndex -Algorithm SHA256).Hash -cne $receipt.indexHash) {
        throw 'gate-index-changed-after-pre-commit'
    }
    $evidenceRoot = [IO.Path]::GetFullPath((Join-Path $state ('runs/' + $receipt.runId)))
    if (@($completedGate.platformArtifacts).Count -eq 0) { throw 'gate-missing-platform-receipt' }
    foreach ($artifact in $completedGate.platformArtifacts) {
        $path = [IO.Path]::GetFullPath((Join-Path $evidenceRoot $artifact.path))
        if ([IO.Path]::IsPathRooted($artifact.path) -or
            -not $path.StartsWith($evidenceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $artifact.sha256) {
            throw 'gate-platform-evidence-changed-after-pre-commit'
        }
    }
    [IO.File]::Move($receiptPath, (Join-Path $state ('runs/' + $receipt.runId + '/commit-final.json')))
    exit 0
}

$gateLock = [IO.File]::Open((Join-Path $state 'gate.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$exitCode = 1
$manifest = $null
$runRoot = $null
$snapshotRoot = $null
$snapshotOwned = $false
$runId = $null
$snapshotParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$stage = 'prior-hook'
function Remove-GateSnapshot {
    if (-not $snapshotOwned -or -not $snapshotRoot -or -not [IO.Directory]::Exists($snapshotRoot)) { return }
    $resolved = (Resolve-Path -LiteralPath $snapshotRoot).ProviderPath
    if ($runId -cnotmatch '^[a-f0-9]{32}$' -or
        ((Get-Item -LiteralPath $resolved -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        [IO.Path]::GetFileName($resolved) -cne ('fa-s-' + $runId) -or
        -not [IO.Path]::GetDirectoryName($resolved).Equals([IO.Path]::TrimEndingDirectorySeparator($snapshotParent), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'gate-unsafe-snapshot-cleanup'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
    $script:snapshotOwned = $false
}

function Invoke-GatePhase {
    param([string]$Name, [string]$Executable, [string[]]$Arguments, [int]$TimeoutMilliseconds = 0)
    $directory = Join-Path $runRoot $Name
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    [IO.File]::WriteAllText((Join-Path $directory 'argv.json'), (@{
        executable = $Executable; arguments = $Arguments; workingDirectory = $snapshotRoot
    } | ConvertTo-Json -Depth 8), $encoding)
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.WorkingDirectory = $snapshotRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    foreach ($key in @('GIT_INDEX_FILE','GIT_DIR','GIT_WORK_TREE','GIT_COMMON_DIR','GIT_PREFIX','FISHAUDIO_EXPECTED_TREE')) {
        [void]$start.Environment.Remove($key)
    }
    $start.Environment['FISHAUDIO_SNAPSHOT_CONTEXT'] = $contextPath
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $stdout = $null; $stderr = $null; $outDrain = $null; $errDrain = $null
    $started = $false; $complete = $false; $observedExit = $null; $primary = $null
    $clock = [Diagnostics.Stopwatch]::StartNew()
    try {
        $stdout = [IO.File]::Open((Join-Path $directory 'stdout.log'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
        $stderr = [IO.File]::Open((Join-Path $directory 'stderr.log'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
        $started = $process.Start()
        if (-not $started) { throw 'gate-platform-process-start' }
        $outDrain = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
        $errDrain = $process.StandardError.BaseStream.CopyToAsync($stderr)
        while (-not $process.WaitForExit(1000)) {
            if ($TimeoutMilliseconds -gt 0 -and $clock.ElapsedMilliseconds -ge $TimeoutMilliseconds) { throw 'gate-platform-metadata-timeout' }
            if ([int]$clock.Elapsed.TotalSeconds % 20 -eq 0) { Write-Host ('[gate] ' + $Name + '; elapsed=' + [int]$clock.Elapsed.TotalSeconds + 's') }
        }
        $observedExit = $process.ExitCode
        if ($TimeoutMilliseconds -gt 0 -and $clock.ElapsedMilliseconds -ge $TimeoutMilliseconds) { throw 'gate-platform-metadata-timeout' }
        if (-not [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($outDrain, $errDrain)).Wait(10000)) { throw 'gate-platform-output-incomplete' }
        if ($observedExit -ne 0) { throw 'gate-platform-command-failed' }
        $complete = $true
    } catch { $primary = $_; throw }
    finally {
        $cleanupClock = [Diagnostics.Stopwatch]::StartNew()
        try {
            if ($started -and -not $process.HasExited) { $process.Kill($true) }
            if ($started) {
                $remaining = [Math]::Max(0, 10000 - [int]$cleanupClock.ElapsedMilliseconds)
                if (-not $process.WaitForExit($remaining)) { throw 'gate-platform-cleanup-incomplete' }
                $observedExit = $process.ExitCode
            }
            if ($null -ne $outDrain -and $null -ne $errDrain) {
                $remaining = [Math]::Max(0, 10000 - [int]$cleanupClock.ElapsedMilliseconds)
                if (-not [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($outDrain, $errDrain)).Wait($remaining)) { throw 'gate-platform-cleanup-incomplete' }
            }
        } catch { if ($null -eq $primary) { throw } }
        finally {
            if ($null -ne $stdout) { $stdout.Dispose() }; if ($null -ne $stderr) { $stderr.Dispose() }
            $process.Dispose()
            [IO.File]::WriteAllText((Join-Path $directory 'process.json'), (@{
                complete = $complete; exitCode = $observedExit; elapsedMilliseconds = $clock.Elapsed.TotalMilliseconds
            } | ConvertTo-Json), $encoding)
        }
    }
}

function Resolve-GateArtifact {
    param([string]$Parent, [string]$Relative, [string]$Digest)
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Digest -cnotmatch '^[a-f0-9]{64}$') {
        throw 'gate-platform-invalid-artifact'
    }
    $path = [IO.Path]::GetFullPath((Join-Path $Parent $Relative))
    if (-not $path.StartsWith($Parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.File]::Exists($path) -or
        ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Digest) {
        throw 'gate-platform-artifact-mismatch'
    }
    return $path
}

function Assert-GateCaseSet {
    param([object[]]$Actual, [object[]]$Expected)
    $set = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($id in $Actual) {
        if ([string]::IsNullOrWhiteSpace($id) -or -not $set.Add([string]$id)) { throw 'gate-platform-invalid-case-set' }
    }
    if ($set.Count -eq 0 -or $set.Count -ne $Expected.Count -or -not $set.SetEquals([string[]]$Expected)) { throw 'gate-platform-case-set-mismatch' }
}
try {
    [IO.File]::Delete($receiptPath)
    if ($installation.previousHooks) {
        $priorHook = Join-Path $installation.previousHooks 'pre-commit'
        if (Test-Path -LiteralPath $priorHook -PathType Leaf) {
            & git -C $root -c ('core.hooksPath=' + $installation.previousHooks) hook run pre-commit
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }
    }
    $stage = 'freeze-index'
    if (Invoke-GateGit @('ls-files', '--unmerged')) { throw 'gate-unmerged-index' }
    $tree = Invoke-GateGit @('write-tree')
    if ($tree -cnotmatch '^[a-f0-9]{40}([a-f0-9]{24})?$') { throw 'gate-invalid-tree' }
    if ($env:FISHAUDIO_EXPECTED_TREE -and $env:FISHAUDIO_EXPECTED_TREE -cne $tree) { throw 'gate-unexpected-index-tree' }
    $indexPath = Invoke-GateGit @('rev-parse', '--path-format=absolute', '--git-path', 'index')
    if ($env:GIT_INDEX_FILE) { $indexPath = [IO.Path]::GetFullPath($env:GIT_INDEX_FILE, $root) }
    $indexHash = (Get-FileHash -LiteralPath $indexPath -Algorithm SHA256).Hash
    $head = & git -C $root rev-parse --verify HEAD 2>$null
    $sourceCommit = if ($LASTEXITCODE -eq 0) { ($head -join '').Trim() } else { $null }
    $runId = [Guid]::NewGuid().ToString('N')
    $runRoot = Join-Path $state ('runs/' + $runId)
    [IO.Directory]::CreateDirectory($runRoot) | Out-Null
    # Windows apphost startup still has path-length limits even when filesystem
    # APIs can read longer paths. Evidence stays in Git-private state; sources
    # use a short independently owned temporary directory.
    $snapshotRoot = Join-Path $snapshotParent ('fa-s-' + $runId)
    if ([IO.Directory]::Exists($snapshotRoot)) { throw 'gate-snapshot-already-exists' }
    if ((Join-Path $snapshotRoot 'tests/STranslate.Plugin.Tts.FishAudio.Tests/bin/Release/net10.0-windows/STranslate.Plugin.Tts.FishAudio.Tests.exe').Length -ge 260) {
        throw 'gate-snapshot-path-too-long'
    }
    [IO.Directory]::CreateDirectory($snapshotRoot) | Out-Null
    $snapshotOwned = $true
    $manifest = [ordered]@{
        schemaVersion = 1; gateProtocolVersion = 2; mode = 'bootstrap/full/hybrid'; runId = $runId
        snapshotTree = $tree; sourceCommit = $sourceCommit; complete = $false; exitCode = 1
        startedUtc = [DateTimeOffset]::UtcNow.ToString('O'); finishedUtc = $null
        validation = @{ report = 'regression.json'; stdout = 'build.stdout.log'; stderr = 'build.stderr.log' }
        platformVerified = $false; platformArtifacts = @(); failure = $null
    }
    [IO.File]::WriteAllText((Join-Path $runRoot 'gate.json'), ($manifest | ConvertTo-Json -Depth 12), $encoding)
    Write-Host ('[gate] bootstrap/full/hybrid; S=' + $tree + '; run=' + $runId)

    $stage = 'materialize-snapshot'
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
public static class FishAudioGitSnapshot
{
    static byte[] Git(string root, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) throw new IOException("snapshot-git-start");
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
        var error = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        try
        {
            if (!process.WaitForExit(120000) || !Task.WhenAll(copy, error).Wait(10000)) throw new IOException("snapshot-git-timeout");
            if (process.ExitCode != 0) throw new IOException("snapshot-git-failed");
            return output.ToArray();
        }
        finally { if (!process.HasExited) { process.Kill(true); process.WaitForExit(10000); } }
    }
    public static List<Dictionary<string,string>> Create(string root, string tree, string destination)
    {
        var files = new List<Dictionary<string,string>>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)) + Path.DirectorySeparatorChar;
        foreach (var record in new UTF8Encoding(false, true).GetString(Git(root,"ls-tree","-rz","--full-tree",tree)).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t');
            if (tab < 0) throw new IOException("snapshot-invalid-tree-record");
            var header = record.Substring(0,tab).Split(' ');
            var name = record.Substring(tab+1);
            if (header.Length != 3 || header[1] != "blob" || (header[0] != "100644" && header[0] != "100755"))
                throw new IOException("snapshot-unsupported-file-mode");
            if (name.Contains('\\') || Path.IsPathRooted(name) || !names.Add(name)) throw new IOException("snapshot-invalid-path");
            foreach (var part in name.Split('/'))
                if (part.Length == 0 || part == "." || part == ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    part.EndsWith(".",StringComparison.Ordinal) || part.EndsWith(" ",StringComparison.Ordinal)) throw new IOException("snapshot-invalid-path");
            var target = Path.GetFullPath(Path.Combine(destination,name));
            if (!target.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new IOException("snapshot-path-escape");
            var bytes = Git(root,"cat-file","blob",header[2]);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllBytes(target,bytes);
            files.Add(new Dictionary<string,string> { ["path"]=name, ["blob"]=header[2],
                ["sha256"]=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), ["mode"]=header[0] });
        }
        if (files.Count == 0) throw new IOException("snapshot-empty-tree");
        return files;
    }
}
'@
    $files = [FishAudioGitSnapshot]::Create($root, $tree, $snapshotRoot)
    if (-not [IO.File]::Exists((Join-Path $snapshotRoot 'build.ps1'))) { throw 'gate-snapshot-missing-build' }
    $contextPath = Join-Path $runRoot 'snapshot.json'
    $context = @{ schemaVersion = 1; root = $snapshotRoot; tree = $tree; sourceCommit = $sourceCommit; files = $files.ToArray() }
    [IO.File]::WriteAllText($contextPath, ($context | ConvertTo-Json -Depth 12), $encoding)
    $reportPath = Join-Path $runRoot 'regression.json'
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
    $start.WorkingDirectory = $snapshotRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('-NoLogo', '-NoProfile', '-File', (Join-Path $snapshotRoot 'build.ps1'), '-Release', '-Test', '-LegacyOnly', '-TestResults', $reportPath)) {
        $start.ArgumentList.Add($argument)
    }
    foreach ($key in @($start.Environment.Keys)) {
        if ($key -in @('GIT_INDEX_FILE','GIT_DIR','GIT_WORK_TREE','GIT_COMMON_DIR','GIT_PREFIX','FISHAUDIO_EXPECTED_TREE')) {
            $start.Environment.Remove($key) | Out-Null
        }
    }
    $start.Environment['FISHAUDIO_SNAPSHOT_CONTEXT'] = $contextPath
    $stage = 'build-package-and-tests'
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $stdout = [IO.File]::Open((Join-Path $runRoot 'build.stdout.log'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $stderr = [IO.File]::Open((Join-Path $runRoot 'build.stderr.log'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $outputDrain = $null
    $errorDrain = $null
    $processStarted = $false
    $primaryFailure = $null
    try {
        $processStarted = $process.Start()
        if (-not $processStarted) { throw 'gate-build-start' }
        $outputDrain = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
        $errorDrain = $process.StandardError.BaseStream.CopyToAsync($stderr)
        while (-not $process.WaitForExit(20000)) {
            Write-Host ('[gate] validating S=' + $tree + '; elapsed=' + [int]$clock.Elapsed.TotalSeconds + 's')
        }
        if (-not [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($outputDrain, $errorDrain)).Wait(10000)) { throw 'gate-build-output-incomplete' }
        if ($process.ExitCode -ne 0) { $exitCode = $process.ExitCode; throw 'gate-build-or-tests-failed' }
    } catch { $primaryFailure = $_; throw }
    finally {
        try {
            if ($processStarted -and -not $process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(10000) }
            $stdout.Dispose(); $stderr.Dispose(); $process.Dispose()
        } catch { if ($null -eq $primaryFailure) { throw } }
    }
    $stage = 'verify-results'
    $report = [IO.File]::ReadAllText($reportPath) | ConvertFrom-Json
    # This requested JSON can be a stale readable projection. Only the runner's
    # generation-aware metadata verifier below decides the authoritative result.
    $stage = 'verify-complete-catalog'
    $runner = Join-Path $snapshotRoot 'tests/STranslate.Plugin.Tts.FishAudio.Tests/bin/Release/net10.0-windows/STranslate.Plugin.Tts.FishAudio.Tests.dll'
    $verify = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $verify.WorkingDirectory = $snapshotRoot
    $verify.UseShellExecute = $false
    $verify.CreateNoWindow = $true
    $verify.RedirectStandardOutput = $true
    $verify.RedirectStandardError = $true
    foreach ($argument in @($runner, '--verify-snapshot-report', $reportPath, $tree)) { $verify.ArgumentList.Add($argument) }
    foreach ($key in @($verify.Environment.Keys)) {
        if ($key -in @('GIT_INDEX_FILE','GIT_DIR','GIT_WORK_TREE','GIT_COMMON_DIR','GIT_PREFIX','FISHAUDIO_EXPECTED_TREE')) {
            $verify.Environment.Remove($key) | Out-Null
        }
    }
    $verify.Environment['FISHAUDIO_SNAPSHOT_CONTEXT'] = $contextPath
    $verification = [Diagnostics.Process]::new()
    $verification.StartInfo = $verify
    $verificationStarted = $false
    try {
        $verificationStarted = $verification.Start()
        if (-not $verificationStarted) { throw 'gate-verifier-start-failed' }
        $verificationOutput = $verification.StandardOutput.ReadToEndAsync()
        $verificationError = $verification.StandardError.ReadToEndAsync()
        if (-not $verification.WaitForExit(120000)) { throw 'gate-verifier-timeout' }
        if (-not [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($verificationOutput, $verificationError)).Wait(10000)) { throw 'gate-verifier-output-incomplete' }
        [IO.File]::WriteAllText((Join-Path $runRoot 'verify.stdout.log'), $verificationOutput.Result, $encoding)
        [IO.File]::WriteAllText((Join-Path $runRoot 'verify.stderr.log'), $verificationError.Result, $encoding)
        if ($verification.ExitCode -ne 0) { throw 'gate-incomplete-catalog-evidence' }
    } finally {
        if ($verificationStarted -and -not $verification.HasExited) { $verification.Kill($true); [void]$verification.WaitForExit(10000) }
        $verification.Dispose()
    }

    # Transitional full hybrid: the old catalog and the framework-discovered
    # leaves are both obligations. A new selector cannot exempt its own changes.
    $stage = 'framework-full'
    $platformRoot = Join-Path $runRoot 'platform'
    Invoke-GatePhase 'framework-command' (Get-Process -Id $PID).Path @('-NoLogo', '-NoProfile', '-File',
        (Join-Path $snapshotRoot 'tools/Test.ps1'), '-Mode', 'Full', '-ResultsDirectory', $platformRoot)
    $stage = 'verify-framework-evidence'
    $platformManifestPath = Join-Path $platformRoot 'manifest.json'
    $platform = [IO.File]::ReadAllText($platformManifestPath) | ConvertFrom-Json
    $contextDigest = (Get-FileHash -LiteralPath $contextPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($platform.schemaVersion -ne 1 -or $platform.kind -cne 'unified-test-session' -or
        $platform.mode -cne 'Full' -or $platform.platform -cne 'VSTest' -or $platform.configuration -cne 'Release' -or
        $platform.selection -cne 'all-discovered' -or -not $platform.complete -or $platform.baselineClaim -ne $false -or
        -not $platform.source.snapshot -or -not $platform.source.snapshotRootValidated -or
        $platform.source.tree -cne $tree -or $platform.source.sourceCommit -cne $sourceCommit -or
        $platform.source.contextSha256 -cne $contextDigest -or @($platform.assemblies).Count -ne 2) {
        throw 'gate-platform-session-mismatch'
    }
    $tool = Join-Path $snapshotRoot 'tools/FishAudio.Testing/bin/Release/net10.0-windows/FishAudio.Testing.dll'
    if ((Get-FileHash -LiteralPath $tool -Algorithm SHA256).Hash.ToLowerInvariant() -cne $platform.toolSha256) { throw 'gate-platform-tool-mismatch' }
    $artifacts = [Collections.Generic.List[object]]::new()
    $artifacts.Add(@{ path = 'platform/manifest.json'; sha256 = (Get-FileHash -LiteralPath $platformManifestPath -Algorithm SHA256).Hash.ToLowerInvariant() })
    $assemblyKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $globalIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $expectedAssemblies = @{
        'plugin' = 'tests/STranslate.Plugin.Tts.FishAudio.Tests/bin/Release/net10.0-windows/STranslate.Plugin.Tts.FishAudio.Tests.dll'
        'testing-tool' = 'tests/FishAudio.Testing.Tests/bin/Release/net10.0-windows/FishAudio.Testing.Tests.dll'
    }
    foreach ($phase in $platform.phases) {
        if (-not $phase.complete -or $phase.exitCode -ne 0) { throw 'gate-platform-incomplete-phase' }
    }
    foreach ($assembly in $platform.assemblies) {
        $key = [string]$assembly.key
        if (-not $expectedAssemblies.ContainsKey($key) -or -not $assemblyKeys.Add($key) -or
            $assembly.assembly -cne $expectedAssemblies[$key] -or -not $assembly.executed -or
            $assembly.discoveredCount -le 0 -or $assembly.discoveredCount -ne $assembly.selectedCount -or
            $assembly.passedCount -ne $assembly.selectedCount) { throw 'gate-platform-assembly-mismatch' }
        [void](Resolve-GateArtifact $snapshotRoot $assembly.assembly $assembly.sha256)
        $paths = @{}
        foreach ($name in @('catalog','selection','terminal','resultXml','trx')) {
            $relative = [string]$assembly.$name
            $digest = [string]$assembly.($name + 'Sha256')
            $paths[$name] = Resolve-GateArtifact $platformRoot $relative $digest
            $artifacts.Add(@{ path = 'platform/' + $relative; sha256 = $digest })
        }
        $catalog = [IO.File]::ReadAllText($paths.catalog) | ConvertFrom-Json
        $selection = [IO.File]::ReadAllText($paths.selection) | ConvertFrom-Json
        $terminal = [IO.File]::ReadAllText($paths.terminal) | ConvertFrom-Json
        if ($catalog.schemaVersion -ne 1 -or $catalog.kind -cne 'nunit-catalog' -or -not $catalog.complete -or
            $selection.schemaVersion -ne 1 -or $terminal.schemaVersion -ne 1 -or $terminal.kind -cne 'nunit-terminal' -or
            -not $terminal.complete -or $terminal.passedCount -ne $assembly.passedCount -or
            @($catalog.cases).Count -ne $assembly.discoveredCount) { throw 'gate-platform-invalid-catalog-or-terminal' }
        $ids = @($catalog.cases | ForEach-Object canonicalId)
        Assert-GateCaseSet @($assembly.selectedIds) $ids
        Assert-GateCaseSet @($selection.ids) $ids
        Assert-GateCaseSet @($terminal.cases | ForEach-Object canonicalId) $ids
        foreach ($case in $catalog.cases) {
            if (-not $globalIds.Add([string]$case.canonicalId)) { throw 'gate-platform-ambiguous-canonical-id' }
        }
        foreach ($name in @('discover','catalog','execute','verify')) {
            $matching = @($platform.phases | Where-Object name -CEQ ($name + '-' + $key))
            if ($matching.Count -ne 1) { throw 'gate-platform-missing-required-phase' }
        }
        $discovery = @($platform.phases | Where-Object name -CEQ ('discover-' + $key))[0]
        $dump = Join-Path $platformRoot ($discovery.directory + '/discovery.dump')
        $dumpDigest = (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash.ToLowerInvariant()
        $dumpRelative = [IO.Path]::GetRelativePath($platformRoot, $dump).Replace('\', '/')
        [void](Resolve-GateArtifact $platformRoot $dumpRelative $dumpDigest)
        $artifacts.Add(@{ path = 'platform/' + $dumpRelative; sha256 = $dumpDigest })
        $verifiedCatalogPath = Join-Path $runRoot ('framework-catalog-' + $key + '.json')
        Invoke-GatePhase ('framework-catalog-' + $key) 'dotnet' @($tool, 'evidence-catalog', '--discovery', $dump, '--output', $verifiedCatalogPath) 120000
        $verifiedCatalog = [IO.File]::ReadAllText($verifiedCatalogPath) | ConvertFrom-Json
        Assert-GateCaseSet @($verifiedCatalog.cases | ForEach-Object canonicalId) $ids
        foreach ($case in $catalog.cases) {
            $other = @($verifiedCatalog.cases | Where-Object canonicalId -CEQ $case.canonicalId)
            if ($other.Count -ne 1 -or $other[0].fullName -cne $case.fullName -or
                $other[0].className -cne $case.className -or $other[0].methodName -cne $case.methodName) { throw 'gate-platform-discovery-mismatch' }
        }
        $verifiedPath = Join-Path $runRoot ('framework-terminal-' + $key + '.json')
        Invoke-GatePhase ('framework-verify-' + $key) 'dotnet' @($tool, 'evidence-verify', '--catalog', $verifiedCatalogPath,
            '--selection', $paths.selection, '--result', $paths.resultXml, '--trx', $paths.trx, '--output', $verifiedPath) 120000
        $verified = [IO.File]::ReadAllText($verifiedPath) | ConvertFrom-Json
        if ($verified.schemaVersion -ne 1 -or $verified.kind -cne 'nunit-terminal' -or -not $verified.complete -or
            $verified.passedCount -ne $ids.Count) { throw 'gate-platform-unverified-results' }
        Assert-GateCaseSet @($verified.cases | ForEach-Object canonicalId) $ids
        foreach ($name in @($verifiedCatalogPath, $verifiedPath)) {
            $artifacts.Add(@{ path = [IO.Path]::GetRelativePath($runRoot, $name).Replace('\', '/');
                sha256 = (Get-FileHash -LiteralPath $name -Algorithm SHA256).Hash.ToLowerInvariant() })
        }
    }
    foreach ($artifact in $artifacts) { [void](Resolve-GateArtifact $runRoot $artifact.path $artifact.sha256) }
    $manifest.platformArtifacts = $artifacts.ToArray()
    $manifest.platformVerified = $true
    $manifest['frameworkCaseCount'] = $globalIds.Count
    $stage = 'verify-final-index'
    if ((Invoke-GateGit @('write-tree')) -cne $tree -or (Get-FileHash -LiteralPath $indexPath -Algorithm SHA256).Hash -cne $indexHash) {
        throw 'gate-index-changed-during-validation'
    }
    $package = Join-Path $snapshotRoot 'STranslate.Plugin.Tts.FishAudio.spkg'
    if ([IO.File]::Exists($package)) {
        [IO.File]::Copy($package, (Join-Path $runRoot 'package.spkg'), $false)
        $manifest['packageSha256'] = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $stage = 'snapshot-cleanup'
    Remove-GateSnapshot
    $manifest.complete = $true
    $exitCode = 0
} catch {
    # Public diagnostics contain fixed stage/type tokens, never source text,
    # credentials, temporary machine paths or arbitrary subprocess messages.
    if ($manifest) { $manifest.failure = @{ stage = $stage; exceptionType = $_.Exception.GetType().FullName; scriptLine = $_.InvocationInfo.ScriptLineNumber;
        reason = $(if ($_.Exception.Message -cmatch '^gate-[a-z-]+$') { $_.Exception.Message } else { 'gate-operation-failed' }) } }
    Write-Host ('[gate] rejected at ' + $stage + ' (' + $_.Exception.GetType().Name + '); inspect private gate evidence.')
} finally {
    $cleanupFailure = $null
    try {
        if ($manifest -and $snapshotRoot -and [IO.File]::Exists((Join-Path $snapshotRoot 'STranslate.Plugin.Tts.FishAudio.spkg')) -and
            -not [IO.File]::Exists((Join-Path $runRoot 'package.spkg'))) {
            [IO.File]::Copy((Join-Path $snapshotRoot 'STranslate.Plugin.Tts.FishAudio.spkg'), (Join-Path $runRoot 'package.spkg'), $false)
            $manifest['packageSha256'] = (Get-FileHash -LiteralPath (Join-Path $runRoot 'package.spkg') -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    catch { $cleanupFailure = $_ }
    try { Remove-GateSnapshot }
    catch { if ($null -eq $cleanupFailure) { $cleanupFailure = $_ } }
    if ($null -ne $cleanupFailure) {
        if ($manifest) { $manifest['cleanupFailure'] = $cleanupFailure.Exception.GetType().FullName }
        if ($exitCode -eq 0) { $exitCode = 1; if ($manifest) { $manifest.complete = $false } }
    }
    if ($manifest) {
        $manifest.exitCode = $exitCode
        $manifest.finishedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        $completedGatePath = Join-Path $runRoot 'gate.json'
        [IO.File]::WriteAllText($completedGatePath, ($manifest | ConvertTo-Json -Depth 12), $encoding)
        if ($exitCode -eq 0 -and $manifest.complete) {
            # Freeze the complete manifest, including its admitted artifact list,
            # independently of that mutable list before message hooks run.
            $receipt = @{ gateProtocolVersion = 2; runId = $runId; tree = $tree; indexPath = $indexPath; indexHash = $indexHash;
                gateSha256 = (Get-FileHash -LiteralPath $completedGatePath -Algorithm SHA256).Hash.ToLowerInvariant() }
            [IO.File]::WriteAllText($receiptPath, ($receipt | ConvertTo-Json), $encoding)
            Write-Host ('[gate] passed legacy=' + @($report.cases).Count + '; NUnit=' + $globalIds.Count + '; S=' + $tree + '; run=' + $runId)
        }
    }
    $gateLock.Dispose()
}
exit $exitCode
