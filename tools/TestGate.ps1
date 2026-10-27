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
if ($installation.schemaVersion -ne 1 -or -not $root.Equals($installation.repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
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
    $completedGate = [IO.File]::ReadAllText((Join-Path $state ('runs/' + $receipt.runId + '/gate.json'))) | ConvertFrom-Json
    $finalTree = Invoke-GateGit @('write-tree')
    $finalIndex = Invoke-GateGit @('rev-parse', '--path-format=absolute', '--git-path', 'index')
    if ($env:GIT_INDEX_FILE) { $finalIndex = [IO.Path]::GetFullPath($env:GIT_INDEX_FILE, $root) }
    if (-not $completedGate.complete -or $completedGate.exitCode -ne 0 -or $completedGate.snapshotTree -cne $receipt.tree -or
        $finalTree -cne $receipt.tree -or -not $finalIndex.Equals($receipt.indexPath, [StringComparison]::OrdinalIgnoreCase) -or
        (Get-FileHash -LiteralPath $finalIndex -Algorithm SHA256).Hash -cne $receipt.indexHash) {
        throw 'gate-index-changed-after-pre-commit'
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
        schemaVersion = 1; mode = 'bootstrap/full/legacy-runner'; runId = $runId
        snapshotTree = $tree; sourceCommit = $sourceCommit; complete = $false; exitCode = 1
        startedUtc = [DateTimeOffset]::UtcNow.ToString('O'); finishedUtc = $null
        validation = @{ report = 'regression.json'; stdout = 'build.stdout.log'; stderr = 'build.stderr.log' }
        failure = $null
    }
    [IO.File]::WriteAllText((Join-Path $runRoot 'gate.json'), ($manifest | ConvertTo-Json -Depth 12), $encoding)
    Write-Host ('[gate] bootstrap/full/legacy-runner; S=' + $tree + '; run=' + $runId)

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
    foreach ($argument in @('-NoLogo', '-NoProfile', '-File', (Join-Path $snapshotRoot 'build.ps1'), '-Release', '-Test', '-TestResults', $reportPath)) {
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
    $receipt = @{ runId = $runId; tree = $tree; indexPath = $indexPath; indexHash = $indexHash }
    [IO.File]::WriteAllText($receiptPath, ($receipt | ConvertTo-Json), $encoding)
    Write-Host ('[gate] passed ' + @($report.cases).Count + ' cases; S=' + $tree + '; run=' + $runId)
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
        [IO.File]::WriteAllText((Join-Path $runRoot 'gate.json'), ($manifest | ConvertTo-Json -Depth 12), $encoding)
    }
    $gateLock.Dispose()
}
exit $exitCode
