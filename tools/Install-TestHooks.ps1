#requires -Version 7.0
[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$Check,
    [string]$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Install -eq $Check) { throw 'Choose exactly one of -Install or -Check.' }
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$encoding = [Text.UTF8Encoding]::new($false)
function Invoke-HookGit {
    param([string[]]$Arguments)
    $output = & git -C $root @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'hook-install-git-failed' }
    return ($output -join "`n").Trim()
}
function Get-Digest([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Quote-Shell([string]$Value) { return "'" + $Value.Replace("'", ("'" + '"' + "'" + '"' + "'")) + "'" }
$actualRoot = Invoke-HookGit @('rev-parse', '--show-toplevel')
if (-not $root.Equals([IO.Path]::GetFullPath($actualRoot), [StringComparison]::OrdinalIgnoreCase)) { throw 'hook-install-root-mismatch' }
$state = [IO.Path]::GetFullPath((Invoke-HookGit @('rev-parse', '--path-format=absolute', '--git-path', 'fish-audio-testing')))
$hooks = Join-Path $state 'hooks'
$manifestPath = Join-Path $hooks 'installation.json'
$sources = @{ 'pre-commit' = Join-Path $root '.githooks/pre-commit'; 'commit-msg' = Join-Path $root '.githooks/commit-msg'; 'TestGate.ps1' = Join-Path $root 'tools/TestGate.ps1' }

if ($Install) {
    $extension = & git -C $root config --local --get extensions.worktreeConfig
    if ($LASTEXITCODE -ne 0 -or $extension -cne 'true') {
        $gitDir = [IO.Path]::GetFullPath((Invoke-HookGit @('rev-parse', '--absolute-git-dir')))
        $common = [IO.Path]::GetFullPath((Invoke-HookGit @('rev-parse', '--path-format=absolute', '--git-common-dir')))
        $worktrees = @((Invoke-HookGit @('worktree', 'list', '--porcelain')) -split "`n" | Where-Object { $_ -match '^worktree ' })
        if (-not $gitDir.Equals($common, [StringComparison]::OrdinalIgnoreCase) -or $worktrees.Count -gt 1) {
            throw 'hook-install-needs-safe-worktree-config-migration'
        }
        # A sole fresh checkout can opt into per-worktree config without changing
        # any other worktree. Existing multi-worktree config is never rewritten.
        [void](Invoke-HookGit @('config', '--local', 'extensions.worktreeConfig', 'true'))
    }
    $previous = [IO.Path]::GetFullPath((Invoke-HookGit @('rev-parse', '--path-format=absolute', '--git-path', 'hooks')))
    if ([IO.File]::Exists($manifestPath)) {
        $old = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
        if (-not $root.Equals($old.repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'hook-install-existing-owner-mismatch' }
        if ($previous.Equals($hooks, [StringComparison]::OrdinalIgnoreCase)) { $previous = $old.previousHooks }
    }
    # `git worktree add` can inherit the managed hooksPath. Its generic entry
    # resolves the invoking worktree, so chaining it would recurse into this
    # gate while its lock is held. Recover the original unrelated hook chain.
    $seenHooks = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    while ($previous -and [IO.File]::Exists((Join-Path $previous 'installation.json')) -and
        [IO.File]::Exists((Join-Path $previous 'TestGate.ps1'))) {
        if (-not $seenHooks.Add([IO.Path]::GetFullPath($previous))) { throw 'hook-install-cyclic-managed-chain' }
        $prior = [IO.File]::ReadAllText((Join-Path $previous 'installation.json')) | ConvertFrom-Json
        if ($prior.schemaVersion -ne 1 -or -not $prior.PSObject.Properties['commonGitDirectory']) { throw 'hook-install-unknown-managed-owner' }
        $commonOwner = [IO.Path]::GetFullPath((Invoke-HookGit @('rev-parse', '--path-format=absolute', '--git-common-dir')))
        if (-not $commonOwner.Equals([IO.Path]::GetFullPath($prior.commonGitDirectory), [StringComparison]::OrdinalIgnoreCase)) {
            throw 'hook-install-foreign-managed-owner'
        }
        $previous = $prior.previousHooks
    }
    [IO.Directory]::CreateDirectory($hooks) | Out-Null
    $digests = @{}
    foreach ($name in $sources.Keys) {
        [IO.File]::Copy($sources[$name], (Join-Path $hooks $name), $true)
        $digests[$name] = Get-Digest $sources[$name]
    }
    # Forward existing unrelated hook types, including any non-test pre-push
    # behavior. The migration itself never installs a pre-push test entry.
    if ($previous -and [IO.Directory]::Exists($previous)) {
        foreach ($file in Get-ChildItem -LiteralPath $previous -File) {
            if ($file.Name -in @('pre-commit', 'commit-msg') -or $file.Name.EndsWith('.sample')) { continue }
            if ($file.Name -notmatch '^[a-z][a-z-]+$') { continue }
            $entry = "#!/bin/sh`nexec " + (Quote-Shell $file.FullName) + ' "$@"' + "`n"
            [IO.File]::WriteAllText((Join-Path $hooks $file.Name), $entry, $encoding)
        }
    }
    $manifest = @{ schemaVersion = 1; gateProtocolVersion = 2; repositoryRoot = $root; previousHooks = $previous; digests = $digests;
        commonGitDirectory = [IO.Path]::GetFullPath((Invoke-HookGit @('rev-parse', '--path-format=absolute', '--git-common-dir'))) }
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 8), $encoding)
    [void](Invoke-HookGit @('config', '--worktree', 'core.hooksPath', $hooks))
}

if (-not [IO.File]::Exists($manifestPath)) { throw 'hook-not-installed' }
$manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.gateProtocolVersion -ne 2 -or
    -not $root.Equals($manifest.repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'hook-installation-mismatch' }
$effective = [IO.Path]::GetFullPath((Invoke-HookGit @('rev-parse', '--path-format=absolute', '--git-path', 'hooks')))
if (-not $effective.Equals($hooks, [StringComparison]::OrdinalIgnoreCase)) { throw 'hook-path-not-effective' }
foreach ($name in $sources.Keys) {
    if ((Get-Digest (Join-Path $hooks $name)) -cne (Get-Digest $sources[$name]) -or
        $manifest.digests.$name -cne (Get-Digest $sources[$name])) { throw 'hook-installed-source-mismatch' }
}
foreach ($code in @(0, 23)) {
    $nonce = [Guid]::NewGuid().ToString('N')
    $challenge = @{ nonce = $nonce; exitCode = $code; createdUtc = [DateTimeOffset]::UtcNow.ToString('O') }
    [IO.File]::WriteAllText((Join-Path $hooks 'check.json'), ($challenge | ConvertTo-Json), $encoding)
    try {
        & git -C $root hook run pre-commit -- -CheckNonce $nonce -CheckExitCode $code
        if ($LASTEXITCODE -ne $code -or -not [IO.File]::Exists((Join-Path $hooks 'check-ack')) -or
            [IO.File]::ReadAllText((Join-Path $hooks 'check-ack')) -cne $nonce) { throw 'hook-exit-code-check-failed' }
    } finally {
        [IO.File]::Delete((Join-Path $hooks 'check.json'))
        [IO.File]::Delete((Join-Path $hooks 'check-ack'))
    }
}
Write-Output 'Worktree pre-commit is installed, effective and exit-code checked; product tests run on git commit.'
