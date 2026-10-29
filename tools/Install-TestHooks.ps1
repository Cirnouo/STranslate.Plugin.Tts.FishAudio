#requires -Version 7.0
[CmdletBinding()]
param([switch]$Install,[switch]$Check,[string]$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')))
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Install -eq $Check) { throw 'Choose exactly one of -Install or -Check.' }
$root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RepositoryRoot))
$encoding = [Text.UTF8Encoding]::new($false)
function Invoke-HookGit([string[]]$Arguments) {
    $output = & git -C $root @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'hook-install-git-failed' }
    return ($output -join "`n").Trim()
}
function Get-Digest([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Quote-Shell([string]$Value) { "'" + $Value.Replace("'",("'"+'"'+"'"+'"'+"'")) + "'" }
function Assert-HookRegular([string]$Path) {
    $item = [IO.FileInfo]::new($Path)
    if (-not $item.Exists -or ($item.Attributes -band ([IO.FileAttributes]::Directory -bor [IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Device)) -ne 0) { throw 'hook-install-input-not-regular' }
    for($parent=$item.Directory;$null -ne $parent;$parent=$parent.Parent) {
        if (-not $parent.Exists -or ($parent.Attributes -band ([IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Device)) -ne 0) { throw 'hook-install-parent-not-regular' }
    }
}
$actualRoot = [IO.Path]::GetFullPath((Invoke-HookGit @('rev-parse','--show-toplevel')))
if (-not $root.Equals([IO.Path]::TrimEndingDirectorySeparator($actualRoot),[StringComparison]::OrdinalIgnoreCase)) { throw 'hook-install-root-mismatch' }
$state = [IO.Path]::GetFullPath((Invoke-HookGit @('rev-parse','--path-format=absolute','--git-path','fish-audio-testing')))
$common = [IO.Path]::GetFullPath((Invoke-HookGit @('rev-parse','--path-format=absolute','--git-common-dir')))
$bundles = Join-Path $state 'hooks/bundles'
$sources = [ordered]@{
    'pre-commit' = Join-Path $root '.githooks/pre-commit'
    'commit-msg' = Join-Path $root '.githooks/commit-msg'
    'TestGate.ps1' = Join-Path $root 'tools/TestGate.ps1'
    'HybridReceipt.ps1' = Join-Path $root 'tools/HybridReceipt.ps1'
}
[IO.Directory]::CreateDirectory($state) | Out-Null
$gateLock = [IO.File]::Open((Join-Path $state 'gate.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
$guards = [Collections.Generic.List[IO.FileStream]]::new()
try {
    if ($Install) {
        $extension = & git -C $root config --local --get extensions.worktreeConfig
        if ($LASTEXITCODE -ne 0 -or $extension -cne 'true') {
            $gitDir = [IO.Path]::GetFullPath((Invoke-HookGit @('rev-parse','--absolute-git-dir')))
            $worktrees = @((Invoke-HookGit @('worktree','list','--porcelain')) -split "`n" | Where-Object { $_ -match '^worktree ' })
            if (-not $gitDir.Equals($common,[StringComparison]::OrdinalIgnoreCase) -or $worktrees.Count -gt 1) { throw 'hook-install-needs-safe-worktree-config-migration' }
            [void](Invoke-HookGit @('config','--local','extensions.worktreeConfig','true'))
        }
        $previous = [IO.Path]::GetFullPath((Invoke-HookGit @('rev-parse','--path-format=absolute','--git-path','hooks')))
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        # Both schema-1/protocol-2 and schema-2/protocol-3 managed installations
        # resolve to their original unrelated chain, including inherited paths.
        while ($previous -and [IO.File]::Exists((Join-Path $previous 'installation.json')) -and [IO.File]::Exists((Join-Path $previous 'TestGate.ps1'))) {
            if (-not $seen.Add([IO.Path]::GetFullPath($previous))) { throw 'hook-install-cyclic-managed-chain' }
            Assert-HookRegular (Join-Path $previous 'installation.json')
            $prior = [IO.File]::ReadAllText((Join-Path $previous 'installation.json')) | ConvertFrom-Json
            if ($prior.schemaVersion -notin @(1,2) -or -not $prior.PSObject.Properties['commonGitDirectory'] -or
                -not $common.Equals([IO.Path]::GetFullPath($prior.commonGitDirectory),[StringComparison]::OrdinalIgnoreCase)) { throw 'hook-install-foreign-managed-owner' }
            $previous = $prior.previousHooks
        }
        [IO.Directory]::CreateDirectory($bundles) | Out-Null
        $bundleId = [Guid]::NewGuid().ToString('N')
        $bundle = Join-Path $bundles $bundleId
        $stage = Join-Path $bundles ('.preparing-'+$bundleId)
        if ([IO.Directory]::Exists($stage) -or [IO.Directory]::Exists($bundle)) { throw 'hook-install-bundle-exists' }
        [IO.Directory]::CreateDirectory($stage) | Out-Null
        $digests = [ordered]@{}
        foreach ($name in $sources.Keys) {
            Assert-HookRegular $sources[$name]
            $guards.Add([IO.File]::Open($sources[$name],[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read))
            $digest = Get-Digest $sources[$name]
            [IO.File]::Copy($sources[$name],(Join-Path $stage $name),$false)
            if ((Get-Digest (Join-Path $stage $name)) -cne $digest) { throw 'hook-install-copy-digest' }
            $digests[$name] = $digest
        }
        # Forward unrelated existing hook types verbatim through a wrapper. No
        # test entry is installed at pre-push; an existing user hook stays owned.
        if ($previous -and [IO.Directory]::Exists($previous)) {
            foreach ($file in Get-ChildItem -LiteralPath $previous -File) {
                if ($file.Name -in @('pre-commit','commit-msg') -or $file.Name.EndsWith('.sample') -or $file.Name -notmatch '^[a-z][a-z-]+$') { continue }
                $entry = "#!/bin/sh`nexec " + (Quote-Shell $file.FullName) + ' "$@"' + "`n"
                [IO.File]::WriteAllText((Join-Path $stage $file.Name),$entry,$encoding)
            }
        }
        $manifest = [ordered]@{schemaVersion=2;gateProtocolVersion=3;repositoryRoot=$root;commonGitDirectory=$common
            installedRoot=$bundle;bundleId=$bundleId;previousHooks=$previous;digests=$digests}
        [IO.File]::WriteAllText((Join-Path $stage 'installation.json'),($manifest|ConvertTo-Json -Depth 8),$encoding)
        # Complete immutable bundle first, then one effective hooksPath switch.
        # The previous installation is never modified or recursively deleted.
        [IO.Directory]::Move($stage,$bundle)
        [void](Invoke-HookGit @('config','--worktree','core.hooksPath',$bundle))
        [IO.File]::Delete((Join-Path $state 'commit-receipt.json'))
    }
    $hooks = [IO.Path]::GetFullPath((Invoke-HookGit @('rev-parse','--path-format=absolute','--git-path','hooks')))
    $manifestPath = Join-Path $hooks 'installation.json'
    if (-not [IO.File]::Exists($manifestPath)) { throw 'hook-not-installed' }
    Assert-HookRegular $manifestPath
    $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 2 -or $manifest.gateProtocolVersion -ne 3 -or $manifest.bundleId -cnotmatch '^[a-f0-9]{32}$' -or
        -not $root.Equals($manifest.repositoryRoot,[StringComparison]::OrdinalIgnoreCase) -or
        -not $common.Equals($manifest.commonGitDirectory,[StringComparison]::OrdinalIgnoreCase) -or
        -not $hooks.Equals([IO.Path]::GetFullPath((Join-Path $bundles $manifest.bundleId)),[StringComparison]::OrdinalIgnoreCase) -or
        -not $hooks.Equals($manifest.installedRoot,[StringComparison]::OrdinalIgnoreCase) -or
        @($manifest.digests.PSObject.Properties).Count -ne $sources.Count) { throw 'hook-installation-mismatch' }
    foreach ($name in $sources.Keys) {
        Assert-HookRegular (Join-Path $hooks $name)
        if ((Get-Digest (Join-Path $hooks $name)) -cne (Get-Digest $sources[$name]) -or
            $manifest.digests.$name -cne (Get-Digest $sources[$name])) { throw 'hook-installed-source-mismatch' }
    }
    foreach ($code in @(0,23)) {
        $nonce = [Guid]::NewGuid().ToString('N')
        [IO.File]::WriteAllText((Join-Path $hooks 'check.json'),(@{nonce=$nonce;exitCode=$code;createdUtc=[DateTimeOffset]::UtcNow.ToString('O')}|ConvertTo-Json),$encoding)
        try {
            & git -C $root hook run pre-commit -- -CheckNonce $nonce -CheckExitCode $code
            if ($LASTEXITCODE -ne $code -or -not [IO.File]::Exists((Join-Path $hooks 'check-ack')) -or
                [IO.File]::ReadAllText((Join-Path $hooks 'check-ack')) -cne $nonce) { throw 'hook-exit-code-check-failed' }
        } finally { [IO.File]::Delete((Join-Path $hooks 'check.json')); [IO.File]::Delete((Join-Path $hooks 'check-ack')) }
    }
} finally {
    foreach ($guard in $guards) { $guard.Dispose() }
    $gateLock.Dispose()
}
Write-Output 'Worktree protocol-3 hooks are installed, digest-bound and exit-code checked; product tests run on git commit.'
