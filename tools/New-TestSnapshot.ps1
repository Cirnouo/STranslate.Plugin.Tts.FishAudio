#requires -Version 7.0
<#
.SYNOPSIS
    Prepare a raw staged-tree snapshot without building or running tests.
.DESCRIPTION
    Loads the standalone materializer source from S, never the working copy.
    Index copies and partial snapshots remain private caller-owned evidence.
    Extended/device namespaces and tilde-plus-digit path components are
    conservatively unsupported; aliases are never treated as external owners.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SnapshotDirectory,
    [Parameter(Mandatory)][string]$ContextPath,
    [string]$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')),
    [string]$ExpectedTree
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$clock = [Diagnostics.Stopwatch]::StartNew()
$stage = 'preflight'
$reason = 'test-snapshot-operation-failed'
$root = $null
$preparation = $null
$ordinal = 0

function Require-Snapshot([bool]$Condition, [string]$Code) {
    if (-not $Condition) { $script:reason = $Code; throw $Code }
}
function Remaining-Snapshot {
    $remaining = [TimeSpan]::FromSeconds(120) - $clock.Elapsed
    Require-Snapshot ($remaining -gt [TimeSpan]::Zero) 'test-snapshot-metadata-deadline'
    return $remaining
}
function Same-SnapshotPath([string]$Left, [string]$Right) {
    return [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Left)).Equals(
        [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Right)), [StringComparison]::OrdinalIgnoreCase)
}
function Within-SnapshotPath([string]$Child, [string]$Parent) {
    return [IO.Path]::GetFullPath($Child).StartsWith(
        [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Parent)) + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)
}
function Require-SnapshotRegular([string]$Path, [bool]$Directory = $false) {
    Require-Snapshot ([IO.Path]::IsPathFullyQualified($Path)) 'test-snapshot-relative-path'
    Require-Snapshot ($(if ($Directory) { [IO.Directory]::Exists($Path) } else { [IO.File]::Exists($Path) })) 'test-snapshot-path-missing'
    $item = Get-Item -LiteralPath $Path -Force
    Require-Snapshot ($item.PSIsContainer -eq $Directory) 'test-snapshot-path-kind'
    while ($null -ne $item) {
        Require-Snapshot (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'test-snapshot-reparse-path'
        $item = $(if ($item -is [IO.DirectoryInfo]) { $item.Parent } else { $item.Directory })
    }
}
function Require-SnapshotInputPath([string]$Path) {
    $normalized = $Path.Replace('/', '\')
    Require-Snapshot (-not $normalized.StartsWith('\\?\', [StringComparison]::Ordinal) -and
        -not $normalized.StartsWith('\\.\', [StringComparison]::Ordinal) -and
        -not $normalized.StartsWith('\??\', [StringComparison]::Ordinal)) 'test-snapshot-unsupported-namespace-path'
    Require-Snapshot ($normalized -cnotmatch '~[0-9]') 'test-snapshot-unsupported-alias-path'
}
function Require-SnapshotPathComponents([string]$Path) {
    Require-SnapshotInputPath $Path
    $suffix = $Path.Substring([IO.Path]::GetPathRoot($Path).Length)
    foreach ($part in $suffix.Split([char[]]@('\','/'), [StringSplitOptions]::RemoveEmptyEntries)) {
        Require-Snapshot (-not $part.EndsWith('.') -and -not $part.EndsWith(' ') -and
            $part.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -lt 0) 'test-snapshot-invalid-path-component'
        $stem = $part.Split('.')[0].TrimEnd(' ', '.').ToUpperInvariant()
        Require-Snapshot ($stem -cnotmatch '^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])$') 'test-snapshot-reserved-path-component'
    }
}
function Get-SnapshotDigest([byte[]]$Bytes) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}
function Write-SnapshotBytes([string]$Path, [byte[]]$Bytes) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $stream.Write($Bytes, 0, $Bytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
}
function Invoke-SnapshotGit([string[]]$Arguments, [string]$PrivateIndex) {
    $script:ordinal++
    $start = [Diagnostics.ProcessStartInfo]::new($git)
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $start.Environment['GIT_NO_REPLACE_OBJECTS'] = '1'
    $start.Environment['GIT_OPTIONAL_LOCKS'] = '0'
    if ($PrivateIndex) { $start.Environment['GIT_INDEX_FILE'] = $PrivateIndex }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $output = [IO.MemoryStream]::new()
    $errorOutput = [IO.MemoryStream]::new()
    $stdout = $null; $stderr = $null; $started = $false; $primary = $null
    try {
        $started = $process.Start()
        Require-Snapshot $started 'test-snapshot-git-start'
        $stdout = $process.StandardOutput.BaseStream.CopyToAsync($output)
        $stderr = $process.StandardError.BaseStream.CopyToAsync($errorOutput)
        $completion = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($process.WaitForExitAsync(), $stdout, $stderr))
        [void]$completion.WaitAsync((Remaining-Snapshot)).GetAwaiter().GetResult()
        Require-Snapshot ($process.ExitCode -eq 0) 'test-snapshot-git-failed'
        return ,$output.ToArray()
    } catch { $primary = $_; throw }
    finally {
        if ($null -ne $primary -and $started) {
            $cleanup = [Diagnostics.Stopwatch]::StartNew()
            try {
                if (-not $process.HasExited) { $process.Kill($true) }
                $remaining = [TimeSpan]::FromSeconds(10) - $cleanup.Elapsed
                Require-Snapshot ($remaining -gt [TimeSpan]::Zero) 'test-snapshot-cleanup-deadline'
                [void][Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($process.WaitForExitAsync(), $stdout, $stderr)).WaitAsync($remaining).GetAwaiter().GetResult()
            } catch { } # Keep the original operation failure.
        }
        try {
            Write-SnapshotBytes (Join-Path $preparation ('git-{0:D3}.stdout.bin' -f $ordinal)) $output.ToArray()
            Write-SnapshotBytes (Join-Path $preparation ('git-{0:D3}.stderr.bin' -f $ordinal)) $errorOutput.ToArray()
        } catch { if ($null -eq $primary) { throw } }
        finally { $output.Dispose(); $errorOutput.Dispose(); $process.Dispose() }
    }
}
function Read-SnapshotGit([string[]]$Arguments, [string]$PrivateIndex) {
    return $utf8.GetString((Invoke-SnapshotGit $Arguments $PrivateIndex)).TrimEnd("`r", "`n")
}
function Read-SnapshotIndex {
    $reported = Read-SnapshotGit @('rev-parse', '--path-format=absolute', '--git-path', 'index')
    Require-Snapshot (-not [string]::IsNullOrWhiteSpace($reported)) 'test-snapshot-invalid-index-path'
    $path = [IO.Path]::GetFullPath($reported, $root)
    if ($env:GIT_INDEX_FILE) {
        $actual = [IO.Path]::GetFullPath($env:GIT_INDEX_FILE, $root)
        Require-Snapshot (Same-SnapshotPath $path $actual) 'test-snapshot-index-route-mismatch'
        $path = $actual
    }
    Require-SnapshotRegular $path
    $bytes = [IO.File]::ReadAllBytes($path)
    $shared = Read-SnapshotGit @('rev-parse', '--shared-index-path')
    $sharedPath = $null; $sharedHash = $null
    if ($shared) {
        $sharedPath = [IO.Path]::GetFullPath($shared, $root)
        Require-SnapshotRegular $sharedPath
        $sharedHash = Get-SnapshotDigest ([IO.File]::ReadAllBytes($sharedPath))
    }
    return @{ path = $path; bytes = $bytes; sha256 = Get-SnapshotDigest $bytes; sharedPath = $sharedPath; sharedSha256 = $sharedHash }
}
function Require-SnapshotIndexSame($Actual, $Expected) {
    Require-Snapshot ((Same-SnapshotPath $Actual.path $Expected.path) -and $Actual.sha256 -ceq $Expected.sha256 -and
        $Actual.sharedPath -ceq $Expected.sharedPath -and $Actual.sharedSha256 -ceq $Expected.sharedSha256) 'test-snapshot-index-changed'
}

try {
    Require-Snapshot $IsWindows 'test-snapshot-windows-required'
    foreach ($path in @($RepositoryRoot, $SnapshotDirectory, $ContextPath)) { Require-SnapshotInputPath $path }
    Require-Snapshot ([IO.Path]::IsPathFullyQualified($RepositoryRoot) -and [IO.Path]::IsPathFullyQualified($SnapshotDirectory) -and
        [IO.Path]::IsPathFullyQualified($ContextPath)) 'test-snapshot-absolute-path-required'
    $root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RepositoryRoot))
    $SnapshotDirectory = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($SnapshotDirectory))
    $ContextPath = [IO.Path]::GetFullPath($ContextPath)
    $preparation = $ContextPath + '.preparation'
    foreach ($path in @($root, $SnapshotDirectory, $ContextPath, $preparation)) { Require-SnapshotPathComponents $path }
    Require-SnapshotRegular $root $true
    Require-SnapshotRegular ([IO.Path]::GetDirectoryName($SnapshotDirectory)) $true
    Require-SnapshotRegular ([IO.Path]::GetDirectoryName($ContextPath)) $true
    foreach ($path in @($SnapshotDirectory, $ContextPath, $preparation)) {
        Require-Snapshot (-not [IO.File]::Exists($path) -and -not [IO.Directory]::Exists($path) -and
            -not (Same-SnapshotPath $path $root) -and -not (Within-SnapshotPath $path $root)) 'test-snapshot-fresh-external-path-required'
    }
    Require-Snapshot (-not (Within-SnapshotPath $ContextPath $SnapshotDirectory) -and -not (Within-SnapshotPath $preparation $SnapshotDirectory) -and
        -not (Within-SnapshotPath $SnapshotDirectory $preparation)) 'test-snapshot-overlapping-outputs'
    [IO.Directory]::CreateDirectory($preparation) | Out-Null
    Require-SnapshotRegular $preparation $true
    $git = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    Require-SnapshotRegular $git
    $actualRoot = Read-SnapshotGit @('rev-parse', '--show-toplevel')
    Require-Snapshot (Same-SnapshotPath $actualRoot $root) 'test-snapshot-repository-root-mismatch'

    $stage = 'freeze-index'
    $initial = Read-SnapshotIndex
    $initialCopy = Join-Path $preparation 'initial.index'
    Write-SnapshotBytes $initialCopy $initial.bytes
    Require-Snapshot ((Invoke-SnapshotGit @('ls-files', '--unmerged', '-z') $initialCopy).Length -eq 0) 'test-snapshot-unmerged-index'
    $tree = Read-SnapshotGit @('write-tree') $initialCopy
    Require-Snapshot ($tree -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$' -and
        (-not $ExpectedTree -or $ExpectedTree -ceq $tree)) 'test-snapshot-unexpected-tree'
    $sourceCommit = Read-SnapshotGit @('rev-parse', '--verify', 'HEAD^{commit}')
    Require-Snapshot ($sourceCommit -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$') 'test-snapshot-head-required'

    $stage = 'load-staged-materializer'
    $sourcePath = 'tools/FishAudio.Testing/Snapshots/GitTreeSnapshot.cs'
    $entry = $utf8.GetString((Invoke-SnapshotGit @('ls-tree', '-z', '--full-tree', $tree, '--', $sourcePath)))
    $match = [regex]::Match($entry, '\A(100644|100755) blob ([a-f0-9]{40}(?:[a-f0-9]{24})?)\t' + [regex]::Escape($sourcePath) + '\x00\z')
    Require-Snapshot $match.Success 'test-snapshot-materializer-not-staged'
    $oid = $match.Groups[2].Value
    $sourceLengthText = Read-SnapshotGit @('cat-file', '-s', $oid)
    $sourceLength = 0L
    Require-Snapshot ([long]::TryParse($sourceLengthText, [Globalization.NumberStyles]::None,
        [Globalization.CultureInfo]::InvariantCulture, [ref]$sourceLength) -and
        $sourceLength -gt 0 -and $sourceLength -le 4 * 1024 * 1024) 'test-snapshot-materializer-size'
    [byte[]]$sourceBytes = Invoke-SnapshotGit @('cat-file', 'blob', $oid)
    Require-Snapshot ($sourceBytes.LongLength -eq $sourceLength) 'test-snapshot-materializer-size'
    $objectHash = [Security.Cryptography.IncrementalHash]::CreateHash($(if ($oid.Length -eq 40) { [Security.Cryptography.HashAlgorithmName]::SHA1 } else { [Security.Cryptography.HashAlgorithmName]::SHA256 }))
    try {
        $objectHash.AppendData($utf8.GetBytes('blob ' + $sourceBytes.Length.ToString([Globalization.CultureInfo]::InvariantCulture) + [char]0))
        $objectHash.AppendData($sourceBytes)
        Require-Snapshot ([Convert]::ToHexString($objectHash.GetHashAndReset()).ToLowerInvariant() -ceq $oid) 'test-snapshot-materializer-oid-mismatch'
    } finally { $objectHash.Dispose() }
    Write-SnapshotBytes (Join-Path $preparation 'materializer.cs') $sourceBytes
    Require-Snapshot (-not ('FishAudio.Testing.Snapshots.GitTreeSnapshot' -as [type])) 'test-snapshot-materializer-already-loaded'
    Add-Type -TypeDefinition ($utf8.GetString($sourceBytes)) -ErrorAction Stop 2>$null

    $stage = 'materialize'
    $snapshot = [FishAudio.Testing.Snapshots.GitTreeSnapshot]::Materialize($git, $root, $tree, $SnapshotDirectory, (Remaining-Snapshot))
    $files = @($snapshot.Files | ForEach-Object { [ordered]@{ path = $_.Path; sha256 = $_.Sha256; blob = $_.Blob; mode = $_.Mode } })

    $stage = 'recheck-index'
    $final = Read-SnapshotIndex
    Require-SnapshotIndexSame $final $initial
    $finalCopy = Join-Path $preparation 'final.index'
    Write-SnapshotBytes $finalCopy $final.bytes
    Require-Snapshot ((Read-SnapshotGit @('write-tree') $finalCopy) -ceq $tree) 'test-snapshot-index-tree-changed'
    $context = [ordered]@{ schemaVersion = 1; root = $SnapshotDirectory; tree = $tree; sourceCommit = $sourceCommit; files = $files }
    [byte[]]$contextBytes = $utf8.GetBytes(($context | ConvertTo-Json -Depth 12))
    Write-SnapshotBytes $ContextPath $contextBytes
    Require-SnapshotIndexSame (Read-SnapshotIndex) $initial
    [void](Remaining-Snapshot)
    [Console]::Out.WriteLine(([ordered]@{
        schemaVersion = 1; kind = 'git-tree-snapshot'; complete = $true; root = $SnapshotDirectory
        tree = $tree; sourceCommit = $sourceCommit; contextPath = $ContextPath; contextSha256 = Get-SnapshotDigest $contextBytes
        indexPath = $initial.path; indexSha256 = $initial.sha256; sharedIndexPath = $initial.sharedPath; sharedIndexSha256 = $initial.sharedSha256
        preparationDirectory = $preparation; filesCount = $files.Count; baselineClaim = $false
    } | ConvertTo-Json -Depth 4))
    exit 0
} catch {
    $errorValue = $_.Exception
    while ($null -ne $errorValue) {
        if ($errorValue.PSObject.Properties['Code'] -and $errorValue.Code -cmatch '^snapshot-[a-z-]+$') { $reason = $errorValue.Code; break }
        $errorValue = $errorValue.InnerException
    }
    [Console]::Error.WriteLine('test-snapshot: failed stage=' + $stage + ' reason=' + $reason + '; retained output is not an admitted snapshot.')
    exit 1
}
