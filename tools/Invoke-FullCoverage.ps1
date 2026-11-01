#requires -Version 7.0
<#
.SYNOPSIS
    Collect the complete current two-catalog local coverage union from frozen S.
.DESCRIPTION
    Materializes the actual index through New-TestSnapshot, or uses the exact
    FISHAUDIO_SNAPSHOT_CONTEXT. DiscoverCoverage owns one Release coverage build
    and both fresh catalogs. Existing collectors own all profiled processes.
    Full remains serial. Prepare builds once; Shard reuses the exact original
    source/build root and collects whole owners; Compose reimports every shard.
    Cross-root restoration, CI scheduling and publication are separate work.
#>
[CmdletBinding()]
param(
    [string]$ResultsDirectory,
    [string]$DotCoverToolFile,
    [ValidateSet('Full','Prepare','Shard','Compose')][string]$Operation = 'Full',
    [string]$PreparedPath,
    [string]$ExpectedPreparedSha256,
    [ValidateRange(0,99999)][int]$ShardIndex = 0,
    [ValidateRange(1,100000)][int]$ShardCount = 1,
    [string[]]$ShardManifestPath = @(),
    [string[]]$ExpectedShardSha256 = @(),
    [string]$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false)
$runId = [Guid]::NewGuid().ToString('N')
$root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RepositoryRoot))
$runRoot = $null
$stage = 'preflight'
$reason = 'full-coverage-operation-failed'
$source = $null
$plan = $null
$unionAdmitted = $false
$delegated = $false
$allCollectorsReturned = $false
$operationComplete = $false
$sourceLease = $null
$pendingShard = $null
$preparedSha256 = $null
$preparedExpectationPath = $null
$failure = $null
$locks = [Collections.Generic.List[IO.FileStream]]::new()
$pins = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
$owners = [Collections.Generic.List[object]]::new()
$knownIds = [Collections.Generic.List[string]]::new()
$isolated = [Collections.Generic.List[object]]::new()
$groups = [Collections.Generic.List[object]]::new()
$phases = [Collections.Generic.List[object]]::new()
$phaseOrdinal = 0
$dotnet = $null
$shell = (Get-Process -Id $PID).Path

function Require-FullCoverage([bool]$Condition, [string]$Code) {
    if (-not $Condition) { $script:reason = $Code; throw $Code }
}
function Same-FullCoveragePath([string]$Left, [string]$Right) {
    return [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Left)).Equals(
        [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Right)), [StringComparison]::OrdinalIgnoreCase)
}
function Within-FullCoveragePath([string]$Path, [string]$Parent) {
    return [IO.Path]::GetFullPath($Path).StartsWith(
        [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Parent)) + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)
}
function Require-FullCoverageRegular([string]$Path, [bool]$Directory = $false) {
    Require-FullCoverage ([IO.Path]::IsPathFullyQualified($Path)) 'full-coverage-absolute-path-required'
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    Require-FullCoverage ($item.PSIsContainer -eq $Directory) 'full-coverage-path-kind'
    while ($null -ne $item) {
        Require-FullCoverage (($item.Attributes -band ([IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Device)) -eq 0) 'full-coverage-reparse-input'
        $item = $(if ($item -is [IO.DirectoryInfo]) { $item.Parent } else { $item.Directory })
    }
}
function FullCoverage-Digest([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Write-FullCoverageText([string]$Path, [string]$Text) {
    $bytes = $utf8.GetBytes($Text)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
}
function Write-FullCoverageJson([string]$Path, $Value) {
    $bytes = $utf8.GetBytes(($Value | ConvertTo-Json -Depth 64))
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
}
function Pin-FullCoverageFile([string]$Path, [string]$Expected = '') {
    Require-FullCoverageRegular $Path
    $pathValue = [IO.Path]::GetFullPath($Path)
    if (-not $pins.ContainsKey($pathValue)) {
        $locks.Add([IO.File]::Open($pathValue, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read))
        $pins.Add($pathValue, (FullCoverage-Digest $pathValue))
    }
    if ($Expected) { Require-FullCoverage ($pins[$pathValue] -ceq $Expected) 'full-coverage-input-digest-mismatch' }
    return $pins[$pathValue]
}
function Assert-FullCoveragePins {
    foreach ($entry in $pins.GetEnumerator()) {
        Require-FullCoverage ((FullCoverage-Digest $entry.Key) -ceq $entry.Value) 'full-coverage-input-changed'
    }
}
# Reuse the existing Test.ps1 owned-phase protocol and shared Windows Job type.
# This wrapper owns subordinate commands; NUnit and the collectors still own
# framework execution, action deadlines and profiled-session admission.
. (Join-Path $PSScriptRoot 'TestProcessOwner.ps1')
function Invoke-FullCoverageOwnedPhase {
    param([string]$Label, [string]$Executable, [string[]]$Arguments, [hashtable]$Environment = @{},
        [int]$PhaseTimeoutMilliseconds = 0)
    $script:phaseOrdinal++
    $relative = 'phases/{0:D3}-{1}' -f $phaseOrdinal, $Label
    $directory = Join-Path $runRoot $relative
    [void][IO.Directory]::CreateDirectory($directory)
    $invocationPath = Join-Path $directory 'argv.json'
    Write-FullCoverageJson $invocationPath ([ordered]@{
        schemaVersion = 1; executable = $Executable; arguments = @($Arguments)
        workingDirectory = $root; environment = $Environment
    })
    $start = [Diagnostics.ProcessStartInfo]::new($shell)
    foreach ($argument in @('-NoLogo', '-NoProfile', '-NonInteractive', '-File',
        (Join-Path $PSScriptRoot 'Invoke-TestProcess.ps1'), '-ArgumentsPath', $invocationPath)) {
        $start.ArgumentList.Add($argument)
    }
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = $utf8
    $start.StandardErrorEncoding = $utf8
    $start.Environment['DOTNET_CLI_UI_LANGUAGE'] = 'en'
    $start.Environment['VSLANG'] = '1033'
    $start.Environment['FISHAUDIO_COMPAT_PARALLEL_PROOF'] = '0'
    if ($dotnet) { $start.Environment['FISHAUDIO_DOTNET_EXECUTABLE'] = $dotnet }
    $start.Environment['FISHAUDIO_TEST_REPOSITORY_ROOT'] = $root
    $start.Environment['FISHAUDIO_PWSH_EXECUTABLE'] = $shell
    foreach ($key in @('FISHAUDIO_COVERAGE_SESSION_ID', 'FISHAUDIO_COVERAGE_RUN_ID',
        'FISHAUDIO_COVERAGE_CASE_ID', 'FISHAUDIO_COVERAGE_SOURCE_TREE', 'FISHAUDIO_COVERAGE_EVIDENCE_ROOT')) {
        [void]$start.Environment.Remove($key)
    }
    foreach ($key in $Environment.Keys) { $start.Environment[$key] = [string]$Environment[$key] }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $owner = $null
    $stdout = $null; $stderr = $null; $exitTask = $null
    $outStream = $null; $errStream = $null
    $started = $false; $assigned = $false
    $drained = $false; $childrenExited = $false; $boundaryVerified = $false
    $exit = $null; $rootProcessId = $null
    $primary = $null; $primaryCode = $null
    $cleanupFailures = [Collections.Generic.List[object]]::new()
    $cleanupClock = $null
    $startedUtc = [DateTimeOffset]::UtcNow
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $script:reason = 'full-coverage-phase-operation-failed'
    try {
        $owner = [FishAudio.Testing.Driver.JobOwner]::new()
        $outStream = [IO.FileStream]::new((Join-Path $directory 'stdout.log'), [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write, [IO.FileShare]::Read, 65536, $true)
        $errStream = [IO.FileStream]::new((Join-Path $directory 'stderr.log'), [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write, [IO.FileShare]::Read, 65536, $true)
        $started = $process.Start()
        Require-FullCoverage $started 'full-coverage-phase-process-not-started'
        $rootProcessId = $process.Id
        $exitTask = $process.WaitForExitAsync()
        $stdout = $process.StandardOutput.BaseStream.CopyToAsync($outStream)
        $stderr = $process.StandardError.BaseStream.CopyToAsync($errStream)
        $owner.Assign($process.Handle)
        $assigned = $true
        $process.StandardInput.WriteLine('GO')
        $process.StandardInput.Close()
        $warned = $false
        while (-not ($exitTask.IsCompleted -and $stdout.IsCompleted -and $stderr.IsCompleted -and $owner.ActiveProcesses -eq 0)) {
            foreach ($task in @($exitTask, $stdout, $stderr)) {
                if ($task.IsFaulted -or $task.IsCanceled) { [void]$task.GetAwaiter().GetResult() }
            }
            if ($exitTask.IsCompleted) {
                [void]$exitTask.GetAwaiter().GetResult()
                $exit = $process.ExitCode
                if ($null -eq $cleanupClock) { $cleanupClock = [Diagnostics.Stopwatch]::StartNew() }
                Require-FullCoverage ($exit -eq 0) 'full-coverage-phase-target-failed'
                Require-FullCoverage ($cleanupClock.ElapsedMilliseconds -lt 10000) 'full-coverage-phase-exit-drain-deadline'
            }
            if ($PhaseTimeoutMilliseconds -gt 0) {
                Require-FullCoverage ($clock.ElapsedMilliseconds -lt $PhaseTimeoutMilliseconds) 'full-coverage-phase-hard-deadline'
            }
            if (-not $warned -and $clock.ElapsedMilliseconds -ge 20000) {
                Write-FullCoverageText (Join-Path $directory 'slow-warning.txt') 'full-coverage-phase-slow'
                $warned = $true
            }
            [Threading.Thread]::Sleep(25)
        }
        if ($PhaseTimeoutMilliseconds -gt 0) {
            Require-FullCoverage ($clock.ElapsedMilliseconds -lt $PhaseTimeoutMilliseconds) 'full-coverage-phase-hard-deadline'
        }
        [void]$exitTask.GetAwaiter().GetResult()
        [void]$stdout.GetAwaiter().GetResult()
        [void]$stderr.GetAwaiter().GetResult()
        $drained = $true
        $childrenExited = $true
        $exit = $process.ExitCode
        Require-FullCoverage ($exit -eq 0) 'full-coverage-phase-target-failed'
        $boundaryVerified = $true
    } catch {
        $primary = $_
        $primaryCode = $script:reason
        try { Write-FullCoverageText (Join-Path $directory 'private-failure.txt') ($_ | Out-String) } catch { }
    } finally {
        # Incomplete execution also enters cleanup if interruption bypasses catch.
        # Termination, original exit/drains and tree observation retain the same
        # single ten-second allowance. A secondary failure never skips a sibling.
        if (-not $boundaryVerified) {
            if ($null -eq $cleanupClock) { $cleanupClock = [Diagnostics.Stopwatch]::StartNew() }
            $terminated = $false
            if ($assigned) {
                try { $owner.Terminate(); $terminated = $true } catch { $cleanupFailures.Add($_) }
            }
            if ($started -and -not $terminated) {
                try { if (-not $process.HasExited) { $process.Kill($true) } } catch { $cleanupFailures.Add($_) }
            }
            if ($started) {
                try { $process.StandardInput.Close() } catch { $cleanupFailures.Add($_) }
            }
            $exitQueryFailed = $false
            $treeQueryFailed = $false
            $exited = -not $started
            $treeExited = -not $assigned
            while ($true) {
                if ($started -and -not $exitQueryFailed) {
                    try { $exited = $process.HasExited } catch { $cleanupFailures.Add($_); $exitQueryFailed = $true }
                }
                if ($assigned -and -not $treeQueryFailed) {
                    try { $treeExited = $owner.ActiveProcesses -eq 0 } catch { $cleanupFailures.Add($_); $treeQueryFailed = $true }
                }
                $exitCompleted = $null -eq $exitTask -or $exitTask.IsCompleted
                $outputCompleted = $null -eq $stdout -or $stdout.IsCompleted
                $errorCompleted = $null -eq $stderr -or $stderr.IsCompleted
                if (($exited -or $exitQueryFailed) -and ($treeExited -or $treeQueryFailed) -and $exitCompleted -and $outputCompleted -and $errorCompleted) { break }
                if ($cleanupClock.ElapsedMilliseconds -ge 10000) {
                    try { Require-FullCoverage $false 'full-coverage-phase-cleanup-deadline' } catch { $cleanupFailures.Add($_) }
                    break
                }
                try { [Threading.Thread]::Sleep(25) } catch { $cleanupFailures.Add($_); break }
            }
            if ($started) {
                try { if ($process.HasExited) { $exit = $process.ExitCode } } catch { $cleanupFailures.Add($_) }
            }
            foreach ($task in @($exitTask, $stdout, $stderr)) {
                if ($null -ne $task -and $task.IsCompleted) {
                    try { [void]$task.GetAwaiter().GetResult() } catch { $cleanupFailures.Add($_) }
                }
            }
            $drained = $null -ne $stdout -and $null -ne $stderr -and $stdout.IsCompletedSuccessfully -and $stderr.IsCompletedSuccessfully
            $childrenExited = $assigned -and -not $treeQueryFailed -and $treeExited
        }
        # Closing the Job remains the final kill-on-close fallback. All releases
        # are attempted before this function can return to frozen-input release.
        foreach ($resource in @($outStream, $errStream, $owner, $process)) {
            if ($null -ne $resource) {
                try { $resource.Dispose() } catch { $cleanupFailures.Add($_) }
            }
        }
        if ($cleanupFailures.Count -gt 0) {
            try { Write-FullCoverageText (Join-Path $directory 'private-cleanup-failure.txt') ($cleanupFailures | Out-String) } catch { }
        }
        $processRecord = [ordered]@{
            schemaVersion = 1; kind = 'owned-process'
            complete = ($null -eq $primary -and $boundaryVerified -and $drained -and $childrenExited -and $cleanupFailures.Count -eq 0)
            startedUtc = $startedUtc; finishedUtc = [DateTimeOffset]::UtcNow
            elapsedMilliseconds = $clock.Elapsed.TotalMilliseconds; exitCode = $exit
            outputDrained = $drained; stdoutDrained = ($null -ne $stdout -and $stdout.IsCompletedSuccessfully)
            stderrDrained = ($null -ne $stderr -and $stderr.IsCompletedSuccessfully); allChildrenExited = $childrenExited
            ownership = 'windows-job-before-target-go'; cleanupFailed = ($cleanupFailures.Count -gt 0)
            phaseTimeoutMilliseconds = $PhaseTimeoutMilliseconds; rootProcessId = $rootProcessId
        }
        try { Write-FullCoverageJson (Join-Path $directory 'process.json') $processRecord }
        catch {
            $processRecord.complete = $false
            if ($null -eq $primary) { $primary = $_; $primaryCode = 'full-coverage-phase-evidence-write-failed' }
        }
        try { Write-FullCoverageText (Join-Path $directory 'exit-code.txt') $(if ($null -eq $exit) { 'not-observed' } else { [string]$exit }) }
        catch {
            $processRecord.complete = $false
            if ($null -eq $primary) { $primary = $_; $primaryCode = 'full-coverage-phase-evidence-write-failed' }
        }
        $phases.Add([ordered]@{ name = $Label; directory = $relative; complete = $processRecord.complete; exitCode = $exit })
    }
    if ($null -ne $primary) { $script:reason = $primaryCode; throw $primary }
    if ($cleanupFailures.Count -gt 0) { $script:reason = 'full-coverage-phase-cleanup-failed'; throw $cleanupFailures[0] }
    Require-FullCoverage ($boundaryVerified -and $exit -eq 0 -and $drained -and $childrenExited) 'full-coverage-phase-incomplete'
    return $directory
}
function Invoke-FullCoverageMetadata {
    param([string]$Label, [string[]]$Arguments, [int]$PhaseTimeoutMilliseconds = 120000)
    return Invoke-FullCoverageOwnedPhase $Label $dotnet (@($metadataTool) + $Arguments) -PhaseTimeoutMilliseconds $PhaseTimeoutMilliseconds
}
function Read-FullCoverageJson([string]$Path) {
    [void](Pin-FullCoverageFile $Path)
    return [IO.File]::ReadAllText($Path, $utf8) | ConvertFrom-Json -Depth 64
}
function New-FullCoveragePreparedExpectation([string]$Path, [string]$Sha256) {
    return [ordered]@{ schemaVersion = 1; preparedPath = [IO.Path]::GetFullPath($Path); preparedSha256 = $Sha256
        sourceDirectory = $root; snapshotContextPath = [IO.Path]::GetFullPath($env:FISHAUDIO_SNAPSHOT_CONTEXT)
        metadataToolFile = $metadataTool; dotnetFile = $dotnet; shellFile = $shell; dotCoverToolFile = $DotCoverToolFile }
}
function FullCoverage-FilePin([string]$Path) {
    Require-FullCoverageRegular $Path
    return [ordered]@{ path = [IO.Path]::GetFullPath($Path); sha256 = FullCoverage-Digest $Path; length = (Get-Item -LiteralPath $Path).Length }
}

try {
    # ValidateSet accepts mixed case. Canonicalize once before exact dispatch.
    $Operation = @{ full = 'Full'; prepare = 'Prepare'; shard = 'Shard'; compose = 'Compose' }[$Operation.ToLowerInvariant()]
    Require-FullCoverage $IsWindows 'full-coverage-windows-required'
    if ($Operation -cin @('Full','Prepare')) {
        Require-FullCoverage (-not $PreparedPath -and -not $ExpectedPreparedSha256 -and $ShardIndex -eq 0 -and $ShardCount -eq 1 -and
            $ShardManifestPath.Count -eq 0 -and $ExpectedShardSha256.Count -eq 0) 'full-coverage-operation-options'
    } else {
        Require-FullCoverage ($PreparedPath -and [IO.Path]::IsPathFullyQualified($PreparedPath) -and
            $ExpectedPreparedSha256 -cmatch '^[a-f0-9]{64}$' -and $env:FISHAUDIO_SNAPSHOT_CONTEXT -and $ShardIndex -lt $ShardCount) 'full-coverage-prepared-input-required'
        if ($Operation -ceq 'Shard') {
            Require-FullCoverage ($ShardManifestPath.Count -eq 0 -and $ExpectedShardSha256.Count -eq 0) 'full-coverage-operation-options'
        } else {
            Require-FullCoverage ($ShardIndex -eq 0 -and $ShardManifestPath.Count -eq $ShardCount -and
                $ExpectedShardSha256.Count -eq $ShardCount) 'full-coverage-complete-shard-pins-required'
        }
    }
    Require-FullCoverage (Same-FullCoveragePath $PSScriptRoot (Join-Path $root 'tools')) 'full-coverage-entry-root-mismatch'
    Require-FullCoverageRegular $root $true
    if (-not $ResultsDirectory) { $ResultsDirectory = Join-Path ([IO.Path]::GetTempPath()) ('fa-full-coverage-' + $runId) }
    Require-FullCoverage ([IO.Path]::IsPathFullyQualified($ResultsDirectory)) 'full-coverage-results-absolute-required'
    $candidate = [IO.Path]::GetFullPath($ResultsDirectory)
    Require-FullCoverage (-not [IO.File]::Exists($candidate) -and -not [IO.Directory]::Exists($candidate) -and
        -not (Same-FullCoveragePath $candidate $root) -and -not (Within-FullCoveragePath $candidate $root)) 'full-coverage-fresh-external-results-required'
    [void][IO.Directory]::CreateDirectory($candidate)
    Require-FullCoverageRegular $candidate $true
    $runRoot = $candidate
    Initialize-ProcessOwner
    [void][IO.Directory]::CreateDirectory((Join-Path $runRoot 'phases'))
    Write-FullCoverageJson (Join-Path $runRoot 'owner.json') @{ schemaVersion = 1; runId = $runId; kind = 'full-coverage-coordinator'; baselineClaim = $false }
    if (-not $env:FISHAUDIO_SNAPSHOT_CONTEXT) {
        $stage = 'freeze-source'
        $snapshot = Join-Path $runRoot 'source'
        $contextPath = Join-Path $runRoot 'snapshot-context.json'
        $prepareArgs = @('-NoLogo','-NoProfile','-NonInteractive','-File',(Join-Path $PSScriptRoot 'New-TestSnapshot.ps1'),
            '-RepositoryRoot',$root,'-SnapshotDirectory',$snapshot,'-ContextPath',$contextPath)
        $snapshotPhase = Invoke-FullCoverageOwnedPhase 'snapshot' $shell $prepareArgs -PhaseTimeoutMilliseconds 120000
        $prepared = [IO.File]::ReadAllText((Join-Path $snapshotPhase 'stdout.log'), $utf8) | ConvertFrom-Json -Depth 16
        Require-FullCoverage ($prepared.complete -eq $true -and $prepared.kind -ceq 'git-tree-snapshot' -and
            (Same-FullCoveragePath $prepared.root $snapshot) -and (Same-FullCoveragePath $prepared.contextPath $contextPath) -and
            $prepared.contextSha256 -ceq (FullCoverage-Digest $contextPath)) 'full-coverage-snapshot-receipt-mismatch'
        $childArgs = @('-NoLogo','-NoProfile','-NonInteractive','-File',(Join-Path $snapshot 'tools/Invoke-FullCoverage.ps1'),
            '-RepositoryRoot',$snapshot,'-ResultsDirectory',(Join-Path $runRoot 'coverage'),'-Operation',$Operation)
        if ($DotCoverToolFile) { $childArgs += @('-DotCoverToolFile',$DotCoverToolFile) }
        [void](Invoke-FullCoverageOwnedPhase 'frozen-full-coverage' $shell $childArgs -PhaseTimeoutMilliseconds 0 -Environment @{
            FISHAUDIO_SNAPSHOT_CONTEXT = $contextPath
        })
        $childExit = $phases[$phases.Count - 1].exitCode
        Write-FullCoverageJson (Join-Path $runRoot 'dispatch.json') @{ schemaVersion = 1; kind = 'frozen-full-coverage-dispatch'
            snapshotContextSha256 = $prepared.contextSha256; sourceTree = $prepared.tree; exitCode = $childExit
            operation = $Operation; preparedPath = $(if ($Operation -ceq 'Prepare') { Join-Path $runRoot 'coverage/prepared.json' } else { $null })
            resultsDirectory = 'coverage'; phases = $phases.ToArray(); baselineClaim = $false; publicationAuthorized = $false }
        Require-FullCoverage ($childExit -eq 0) 'full-coverage-frozen-run-failed'
        $delegated = $true
        $operationComplete = $true
    }
    if (-not $delegated) {
    $stage = 'frozen-source-admission'
    Require-FullCoverage ([IO.Path]::IsPathFullyQualified($env:FISHAUDIO_SNAPSHOT_CONTEXT)) 'full-coverage-snapshot-context-required'
    $context = Read-FullCoverageJson $env:FISHAUDIO_SNAPSHOT_CONTEXT
    Require-FullCoverage ($context.schemaVersion -eq 1 -and (Same-FullCoveragePath $context.root $root) -and
        $context.tree -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$' -and $context.sourceCommit -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$' -and
        @($context.files).Count -gt 0 -and @($context.files).Count -le 100000) 'full-coverage-frozen-context-mismatch'
    $sourcePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $sourceOutputs = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$sourceOutputs.Add((Join-Path $root '.artifacts'))
    foreach ($file in $context.files) {
        Require-FullCoverage ($file.path -is [string] -and -not [IO.Path]::IsPathRooted($file.path) -and
            $file.sha256 -cmatch '^[a-f0-9]{64}$') 'full-coverage-source-inventory-invalid'
        $full = [IO.Path]::GetFullPath((Join-Path $root $file.path))
        Require-FullCoverage ((Within-FullCoveragePath $full $root) -and $sourcePaths.Add($full)) 'full-coverage-source-inventory-alias'
        [void](Pin-FullCoverageFile $full $file.sha256)
        if ([IO.Path]::GetExtension($full).Equals('.csproj', [StringComparison]::OrdinalIgnoreCase)) {
            $projectDirectory = [IO.Path]::GetDirectoryName($full)
            [void]$sourceOutputs.Add((Join-Path $projectDirectory 'bin'))
            [void]$sourceOutputs.Add((Join-Path $projectDirectory 'obj'))
        }
    }
    # Whole source inventory is required; outputs cannot supplement a source gap.
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($root)
    $physicalSourceCount = 0
    while ($pending.Count -gt 0) {
        foreach ($entry in Get-ChildItem -LiteralPath ($pending.Pop()) -Force) {
            $physicalSourceCount++
            Require-FullCoverage ($physicalSourceCount -le 200000) 'full-coverage-physical-source-limit'
            Require-FullCoverageRegular $entry.FullName $entry.PSIsContainer
            if ($entry.PSIsContainer -and $sourceOutputs.Contains($entry.FullName)) { continue }
            if ($entry.PSIsContainer) { $pending.Push($entry.FullName) }
            else { Require-FullCoverage ($sourcePaths.Contains($entry.FullName)) 'full-coverage-unlisted-source-input' }
        }
    }
    # One nonblocking, source-keyed lease covers build, profile and composition
    # across different preparation directories. It never lives in frozen S.
    $leaseRoot = Join-Path ([IO.Path]::GetTempPath()) 'fa-full-coverage-source-leases'
    [void][IO.Directory]::CreateDirectory($leaseRoot)
    Require-FullCoverageRegular $leaseRoot $true
    $leaseKey = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($utf8.GetBytes($root.ToUpperInvariant()))).ToLowerInvariant()
    $leasePath = Join-Path $leaseRoot ($leaseKey + '.lock')
    if ([IO.File]::Exists($leasePath)) { Require-FullCoverageRegular $leasePath }
    $sourceLease = [IO.File]::Open($leasePath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    if ($Operation -cin @('Full','Prepare')) {
    foreach ($path in @('tests/STranslate.Plugin.Tts.FishAudio.Tests/bin/coverage', 'tests/FishAudio.Testing.Tests/bin/coverage',
        'tools/FishAudio.Testing/bin/coverage', '.artifacts/coverage', 'STranslate.Plugin.Tts.FishAudio/obj/coverage',
        'tests/STranslate.Plugin.Tts.FishAudio.Tests/obj/coverage', 'tests/FishAudio.Testing.Tests/obj/coverage', 'tools/FishAudio.Testing/obj/coverage')) {
        Require-FullCoverage (-not [IO.Directory]::Exists((Join-Path $root $path)) -and
            -not [IO.File]::Exists((Join-Path $root $path))) 'full-coverage-fresh-build-output-required'
    }
    $stage = 'coverage-build-and-both-discoveries'
    $discoveryRoot = Join-Path $runRoot 'discovery'
    $discoveryArgs = @('-NoLogo','-NoProfile','-NonInteractive','-File',(Join-Path $root 'tools/Test.ps1'),
        '-Mode','DiscoverCoverage','-RepositoryRoot',$root,'-ResultsDirectory',$discoveryRoot)
    [void](Invoke-FullCoverageOwnedPhase 'coverage-build-discovery' $shell $discoveryArgs -PhaseTimeoutMilliseconds 0)
    $manifestPath = Join-Path $discoveryRoot 'manifest.json'
    $manifest = Read-FullCoverageJson $manifestPath
    Require-FullCoverage ($manifest.schemaVersion -eq 1 -and $manifest.kind -ceq 'unified-test-session' -and
        $manifest.complete -eq $true -and $manifest.mode -ceq 'DiscoverCoverage' -and $manifest.configuration -ceq 'Release' -and
        $manifest.selection -ceq 'all-discovered' -and @($manifest.assemblies).Count -eq 2 -and
        $manifest.source.snapshot -eq $true -and $manifest.source.snapshotRootValidated -eq $true -and
        $manifest.source.tree -ceq $context.tree -and $manifest.source.commit -ceq $context.sourceCommit -and
        $manifest.source.contextSha256 -ceq (FullCoverage-Digest $env:FISHAUDIO_SNAPSHOT_CONTEXT)) 'full-coverage-discovery-source-or-mode-mismatch'
    $source = @{ commit = $manifest.source.commit; tree = $manifest.source.tree; fingerprint = $manifest.source.fingerprint }
    Require-FullCoverage ($source.fingerprint -cmatch '^[a-f0-9]{64}$') 'full-coverage-source-fingerprint-missing'
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $metadataTool = Join-Path $root 'tools/FishAudio.Testing/bin/coverage/Release/net10.0-windows/FishAudio.Testing.dll'
    [void](Pin-FullCoverageFile $metadataTool $manifest.toolSha256)
    $vstest = Join-Path ([IO.Path]::GetDirectoryName($dotnet)) 'sdk/10.0.403/vstest.console.dll'
    if (-not $DotCoverToolFile) {
        $packageRoot = $env:NUGET_PACKAGES
        if (-not $packageRoot) { $packageRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
        $DotCoverToolFile = Join-Path $packageRoot 'jetbrains.dotcover.commandlinetools/2026.2.3.1/tools/net10.0/any/dotCover.dll'
    }
    Require-FullCoverage ([IO.Path]::IsPathFullyQualified($DotCoverToolFile) -and
        [IO.Path]::GetFileName($DotCoverToolFile) -ceq 'dotCover.dll') 'full-coverage-pinned-tool-path-required'
    $DotCoverToolFile = [IO.Path]::GetFullPath($DotCoverToolFile)
    $toolSha256 = Pin-FullCoverageFile $DotCoverToolFile
    $vstestSha256 = Pin-FullCoverageFile $vstest
    [void](Pin-FullCoverageFile $dotnet)
    $sdkVersionPhase = Invoke-FullCoverageOwnedPhase 'sdk-version' $dotnet @('--version') -PhaseTimeoutMilliseconds 120000
    $observedSdkVersion = [IO.File]::ReadAllText((Join-Path $sdkVersionPhase 'stdout.log'), $utf8).Trim()
    Require-FullCoverage ($observedSdkVersion -ceq '10.0.403') 'full-coverage-sdk-version-mismatch'
    $versionPath = Join-Path $runRoot 'dotcover-version.txt'
    $toolVersionPhase = Invoke-FullCoverageOwnedPhase 'tool-version' $dotnet @($DotCoverToolFile, 'version', '--text-output', $versionPath) -PhaseTimeoutMilliseconds 120000
    $observedDotCoverVersion = [IO.File]::ReadAllText($versionPath, $utf8).Trim()
    Require-FullCoverage ($observedDotCoverVersion -ceq '2026.2.3.1') 'full-coverage-tool-version-mismatch'
    $runtimePath = Join-Path $runRoot 'runtime-environment.json'
    [void](Invoke-FullCoverageMetadata 'runtime-environment' @('coverage-full-environment', '--output', $runtimePath))
    $runtime = Read-FullCoverageJson $runtimePath
    Require-FullCoverage ($runtime.complete -eq $true -and $runtime.value.isWindows -eq $true) 'full-coverage-runtime-environment-rejected'
    $stage = 'independent-build-authority'
    $catalogs = [Collections.Generic.List[object]]::new()
    $planCatalogs = [Collections.Generic.List[object]]::new()
    $seenKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($assembly in $manifest.assemblies) {
        Require-FullCoverage ($assembly.key -cin @('plugin', 'testing-tool') -and $seenKeys.Add($assembly.key) -and
            $assembly.executed -eq $false -and $assembly.discoveredCount -gt 0) 'full-coverage-current-catalog-set'
        $testDll = [IO.Path]::GetFullPath((Join-Path $root $assembly.assembly))
        Require-FullCoverage ((Within-FullCoveragePath $testDll $root) -and
            $testDll.Replace('\','/').Contains('/bin/coverage/Release/net10.0-windows/')) 'full-coverage-test-output-binding'
        [void](Pin-FullCoverageFile $testDll $assembly.sha256)
        $catalogPath = [IO.Path]::GetFullPath((Join-Path $discoveryRoot $assembly.catalog))
        Require-FullCoverage (Within-FullCoveragePath $catalogPath $discoveryRoot) 'full-coverage-catalog-path'
        $catalog = Read-FullCoverageJson $catalogPath
        [void](Pin-FullCoverageFile $catalogPath $assembly.catalogSha256)
        Require-FullCoverage ($catalog.kind -ceq 'nunit-catalog' -and $catalog.complete -eq $true -and
            @($catalog.cases).Count -eq $assembly.discoveredCount) 'full-coverage-catalog-output'
        foreach ($case in $catalog.cases) { $knownIds.Add([string]$case.canonicalId) }
        $discoveries = @($manifest.phases | Where-Object name -CEQ ('discover-' + $assembly.key))
        Require-FullCoverage ($discoveries.Count -eq 1 -and $discoveries[0].complete -eq $true -and $discoveries[0].exitCode -eq 0) 'full-coverage-discovery-phase'
        $discovery = [IO.Path]::GetFullPath((Join-Path $discoveryRoot ($discoveries[0].directory + '/discovery.dump')))
        Require-FullCoverage (Within-FullCoveragePath $discovery $discoveryRoot) 'full-coverage-discovery-path'
        $discoverySha256 = Pin-FullCoverageFile $discovery
        $productDll = $(if ($assembly.key -ceq 'plugin') {
            Join-Path $root '.artifacts/coverage/Release/STranslate.Plugin.Tts.FishAudio.dll'
        } else { Join-Path ([IO.Path]::GetDirectoryName($testDll)) 'FishAudio.Testing.dll' })
        $binaries = @()
        foreach ($pair in @(@('Product',$productDll), @('Tests',$testDll))) {
            $dll = $pair[1]; $pdb = [IO.Path]::ChangeExtension($dll, '.pdb')
            [void](Pin-FullCoverageFile $dll); [void](Pin-FullCoverageFile $pdb)
            $binaryPath = Join-Path $runRoot ($assembly.key + '-' + $pair[0].ToLowerInvariant() + '-binary.json')
            [void](Invoke-FullCoverageMetadata ($assembly.key + '-binary-' + $pair[0].ToLowerInvariant()) @(
                'evidence-binary', '--assembly', $dll, '--pdb', $pdb, '--output', $binaryPath))
            $binary = Read-FullCoverageJson $binaryPath
            Require-FullCoverage ($binary.complete -eq $true -and $binary.kind -ceq 'binary-identity') 'full-coverage-binary-pdb-rejected'
            $binaries += [ordered]@{ role = $pair[0]; dllRelativePath = [IO.Path]::GetRelativePath($root, $dll).Replace('\','/')
                pdbRelativePath = [IO.Path]::GetRelativePath($root, $pdb).Replace('\','/'); identity = $binary.binary }
        }
        # Existing collectors own this version-one encoding. Recompute it from
        # the current build rather than reading collector.json's build ID.
        $buildBytes = $utf8.GetBytes((@($source.fingerprint, $binaries) | ConvertTo-Json -Depth 12 -Compress))
        $buildId = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($buildBytes)).ToLowerInvariant()
        $bindings = @($binaries | ForEach-Object { [ordered]@{ role = $_.role; dllRelativePath = $_.dllRelativePath
            pdbRelativePath = $_.pdbRelativePath; expectedIdentity = $_.identity } })
        $catalogs.Add([ordered]@{ key = $assembly.key; discoveryPath = $discovery; discoverySha256 = $discoverySha256
            sourceDirectory = $root; buildDirectory = $root; buildId = $buildId; binaries = $bindings })
        $planCatalogs.Add(@{ key = $assembly.key; discoveryPath = $discovery; discoverySha256 = $discoverySha256 })
    }
    # Hold all runtime build inputs; discovery dumps and output lock files stay
    # with their existing collector owners.
    $binaryDirectories = @($catalogs | ForEach-Object { Join-Path $root ([IO.Path]::GetDirectoryName($_.binaries[1].dllRelativePath)) })
    $binaryDirectories += @([IO.Path]::GetDirectoryName($metadataTool), (Join-Path $root '.artifacts/coverage/Release'))
    $binaryDirectories = @($binaryDirectories | Sort-Object -Unique)
    foreach ($directory in $binaryDirectories) {
        foreach ($file in Get-ChildItem -LiteralPath $directory -File) {
            if ($file.Extension -cin @('.dll','.pdb') -or $file.Name.EndsWith('.deps.json', [StringComparison]::OrdinalIgnoreCase) -or
                $file.Name.EndsWith('.runtimeconfig.json', [StringComparison]::OrdinalIgnoreCase)) { [void](Pin-FullCoverageFile $file.FullName) }
        }
    }
    $authorityPath = Join-Path $runRoot 'build-authority.json'
    Write-FullCoverageJson $authorityPath @{ schemaVersion = 1; kind = 'current-frozen-coverage-build'; runId = $runId
        source = $source; contextSha256 = FullCoverage-Digest $env:FISHAUDIO_SNAPSHOT_CONTEXT
        discoveryManifestSha256 = FullCoverage-Digest $manifestPath; catalogs = $catalogs.ToArray()
        toolSha256 = $toolSha256; vstestConsoleSha256 = $vstestSha256; configuration = 'Release'
        sdkVersion = $observedSdkVersion; dotCoverVersion = $observedDotCoverVersion; targetFramework = 'net10.0-windows'
        environment = $runtime.value; environmentEvidenceSha256 = FullCoverage-Digest $runtimePath
        sdkVersionEvidenceSha256 = FullCoverage-Digest (Join-Path $sdkVersionPhase 'stdout.log')
        sdkVersionInvocationSha256 = FullCoverage-Digest (Join-Path $sdkVersionPhase 'argv.json')
        sdkVersionProcessSha256 = FullCoverage-Digest (Join-Path $sdkVersionPhase 'process.json')
        dotCoverVersionEvidenceSha256 = FullCoverage-Digest $versionPath
        dotCoverVersionInvocationSha256 = FullCoverage-Digest (Join-Path $toolVersionPhase 'argv.json')
        dotCoverVersionProcessSha256 = FullCoverage-Digest (Join-Path $toolVersionPhase 'process.json')
        buildProperties = @{ configuration = 'Release'; fishAudioCoverage = $true; restoreLockedMode = $true
            useSharedCompilation = $false; nodeReuse = $false; debugType = 'portable'; optimize = $true; constants = 'TRACE' }
        pinnedSourceInputs = @($context.files | Where-Object { $_.path -ceq 'global.json' -or $_.path -ceq '.config/dotnet-tools.json' -or
            $_.path.EndsWith('/packages.lock.json', [StringComparison]::Ordinal) -or $_.path -ceq 'Directory.Build.props' })
        baselineClaim = $false }
    $authoritySha256 = Pin-FullCoverageFile $authorityPath
    $admissionPath = Join-Path $runRoot 'composition-expectation.json'
    Write-FullCoverageJson $admissionPath @{ schemaVersion = 1; source = $source; catalogs = $catalogs.ToArray()
        buildAuthority = @{ evidenceId = $runId; evidenceSha256 = $authoritySha256; sdkVersion = $observedSdkVersion
            configuration = 'Release'; targetFramework = 'net10.0-windows'; dotCoverToolSha256 = $toolSha256; vstestConsoleSha256 = $vstestSha256
            instrumentationPolicyVersion = 'framework-infrastructure-exclusions-v2' } }
    [void](Pin-FullCoverageFile $admissionPath)
    $planRequest = Join-Path $runRoot 'plan-request.json'
    Write-FullCoverageJson $planRequest @{ schemaVersion = 1; catalogs = $planCatalogs.ToArray(); shardIndex = 0; shardCount = 1 }
    $planPath = Join-Path $runRoot 'plan.json'
    [void](Invoke-FullCoverageMetadata 'plan' @('coverage-full-plan', '--input', $planRequest, '--output', $planPath))
    $planDocument = Read-FullCoverageJson $planPath
    Require-FullCoverage ($planDocument.complete -eq $true -and $planDocument.kind -ceq 'coverage-full-plan' -and
        $planDocument.value.isWholeCatalogSelection -eq $true -and @($planDocument.value.owners).Count -gt 0) 'full-coverage-plan-rejected'
    $plan = $planDocument.value
    foreach ($owner in $plan.owners) {
        $owners.Add([pscustomobject]@{ id = $owner.id; catalogKey = $owner.catalogKey; kind = $owner.kind; profile = $owner.profile
            canonicalIds = @($owner.cases | ForEach-Object canonicalId); status = 'not-run'; directory = 'sessions/' + $owner.id; exitCode = $null })
    }
    $templatePath = Join-Path $runRoot 'runner-metadata-template.json'
    $templateAssemblies = @(foreach ($entry in $catalogs) {
        $tests = $entry.binaries[1]
        [ordered]@{ key = $entry.key; assembly = Join-Path $root $tests.dllRelativePath; assemblySha256 = $tests.expectedIdentity.dllSha256
            pdb = Join-Path $root $tests.pdbRelativePath; pdbSha256 = $tests.expectedIdentity.pdbSha256
            discovery = $entry.discoveryPath; discoverySha256 = $entry.discoverySha256
            catalog = Join-Path $discoveryRoot ($entry.key + '/catalog.json')
            catalogSha256 = FullCoverage-Digest (Join-Path $discoveryRoot ($entry.key + '/catalog.json')) }
    })
    Write-FullCoverageJson $templatePath @{ schemaVersion = 1; kind = 'runner-readonly-metadata'; runId = $runId
        repositoryRoot = $root; sessionDirectory = $discoveryRoot; coverageCatalogSessionDirectory = $discoveryRoot
        sourceTree = $source.tree; sourceFingerprint = $source.fingerprint
        sourcePath = Join-Path $discoveryRoot 'source.json'; sourceSha256 = FullCoverage-Digest (Join-Path $discoveryRoot 'source.json')
        snapshotContextPath = $env:FISHAUDIO_SNAPSHOT_CONTEXT; snapshotContextSha256 = FullCoverage-Digest $env:FISHAUDIO_SNAPSHOT_CONTEXT
        assemblies = $templateAssemblies
        tool = @{ assembly = $metadataTool; assemblySha256 = FullCoverage-Digest $metadataTool
            pdb = [IO.Path]::ChangeExtension($metadataTool, '.pdb'); pdbSha256 = FullCoverage-Digest ([IO.Path]::ChangeExtension($metadataTool, '.pdb')) } }
    [void](Pin-FullCoverageFile $templatePath)
    # Retain the preparation's actual build/discovery/tool evidence as well as
    # immutable source and runtime inputs. No result flag authenticates itself.
    foreach ($path in @($shell, $manifestPath, (Join-Path $discoveryRoot 'source.json'), $versionPath,
        (Join-Path $sdkVersionPhase 'stdout.log'), (Join-Path $sdkVersionPhase 'argv.json'), (Join-Path $sdkVersionPhase 'process.json'),
        (Join-Path $toolVersionPhase 'stdout.log'), (Join-Path $toolVersionPhase 'argv.json'), (Join-Path $toolVersionPhase 'process.json'))) {
        [void](Pin-FullCoverageFile $path)
    }
    $preparedPathValue = Join-Path $runRoot 'prepared.json'
    $preparedFiles = @($pins.GetEnumerator() | Sort-Object Key | ForEach-Object { FullCoverage-FilePin $_.Key })
    Write-FullCoverageJson $preparedPathValue ([ordered]@{ schemaVersion = 1; runId = $runId; sourceDirectory = $root
        preparationDirectory = $runRoot; snapshotContextPath = [IO.Path]::GetFullPath($env:FISHAUDIO_SNAPSHOT_CONTEXT)
        source = $source; admission = Read-FullCoverageJson $admissionPath; metadataToolFile = $metadataTool
        runtimeEnvironmentPath = $runtimePath; runnerMetadataTemplatePath = $templatePath
        buildDirectories = $binaryDirectories; files = $preparedFiles })
    $preparedSha256 = Pin-FullCoverageFile $preparedPathValue
    $preparedExpectationPath = Join-Path $runRoot 'prepared-expectation.json'
    Write-FullCoverageJson $preparedExpectationPath (New-FullCoveragePreparedExpectation $preparedPathValue $preparedSha256)
    [void](Invoke-FullCoverageMetadata 'prepared-check' @('coverage-full-prepared-check', '--expectation', $preparedExpectationPath,
        '--output', (Join-Path $runRoot 'prepared-check.json')) -PhaseTimeoutMilliseconds 0)
    if ($Operation -ceq 'Prepare') { $operationComplete = $true }
    } else {
        # A caller supplies the original independently observed digest. Read no
        # bundle-selected executable before matching the fixed current paths.
        $stage = 'prepared-reuse-admission'
        $preparedPathValue = [IO.Path]::GetFullPath($PreparedPath)
        [void](Pin-FullCoverageFile $preparedPathValue $ExpectedPreparedSha256)
        $preparedDocument = Read-FullCoverageJson $preparedPathValue
        $preparationRoot = [IO.Path]::GetDirectoryName($preparedPathValue)
        Require-FullCoverage ($preparedDocument.schemaVersion -eq 1 -and
            [IO.Path]::GetFileName($preparedPathValue) -ceq 'prepared.json' -and
            (Same-FullCoveragePath $preparedDocument.sourceDirectory $root) -and
            (Same-FullCoveragePath $preparedDocument.preparationDirectory $preparationRoot) -and
            -not (Within-FullCoveragePath $preparationRoot $root) -and -not (Same-FullCoveragePath $preparationRoot $root) -and
            (Same-FullCoveragePath $preparedDocument.snapshotContextPath $env:FISHAUDIO_SNAPSHOT_CONTEXT) -and
            $preparedDocument.runId -cmatch '^[a-f0-9]{32}$' -and $preparedDocument.source.tree -ceq $context.tree -and
            $preparedDocument.source.commit -ceq $context.sourceCommit -and
            $preparedDocument.admission.buildAuthority.instrumentationPolicyVersion -ceq 'framework-infrastructure-exclusions-v2') 'full-coverage-preparation-identity'
        $runId = $preparedDocument.runId
        $source = $preparedDocument.source
        $metadataTool = Join-Path $root 'tools/FishAudio.Testing/bin/coverage/Release/net10.0-windows/FishAudio.Testing.dll'
        $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        $sdkRoot = Join-Path ([IO.Path]::GetDirectoryName($dotnet)) 'sdk/10.0.403'
        if (-not $DotCoverToolFile) {
            $packageRoot = $env:NUGET_PACKAGES
            if (-not $packageRoot) { $packageRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
            $DotCoverToolFile = Join-Path $packageRoot 'jetbrains.dotcover.commandlinetools/2026.2.3.1/tools/net10.0/any/dotCover.dll'
        }
        Require-FullCoverage ([IO.Path]::IsPathFullyQualified($DotCoverToolFile) -and
            [IO.Path]::GetFileName($DotCoverToolFile) -ceq 'dotCover.dll' -and
            (Same-FullCoveragePath $preparedDocument.metadataToolFile $metadataTool)) 'full-coverage-prepared-fixed-tool'
        $DotCoverToolFile = [IO.Path]::GetFullPath($DotCoverToolFile)
        $toolDirectory = [IO.Path]::GetDirectoryName($DotCoverToolFile)
        $externalPaths = @($env:FISHAUDIO_SNAPSHOT_CONTEXT, $dotnet, $shell, $DotCoverToolFile)
        Require-FullCoverage (@($preparedDocument.files).Count -gt 0 -and @($preparedDocument.files).Count -le 200000) 'full-coverage-prepared-pin-count'
        $pinNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        [long]$totalBytes = 0
        foreach ($file in $preparedDocument.files) {
            Require-FullCoverage ([IO.Path]::IsPathFullyQualified($file.path) -and $file.sha256 -cmatch '^[a-f0-9]{64}$' -and
                $file.length -ge 0 -and $file.length -le 268435456 -and $pinNames.Add([IO.Path]::GetFullPath($file.path))) 'full-coverage-prepared-pin-invalid'
            $externalExact = @($externalPaths | Where-Object { Same-FullCoveragePath $_ $file.path }).Count -gt 0
            Require-FullCoverage ((Within-FullCoveragePath $file.path $root) -or (Within-FullCoveragePath $file.path $preparationRoot) -or
                (Within-FullCoveragePath $file.path $sdkRoot) -or (Within-FullCoveragePath $file.path $toolDirectory) -or $externalExact) 'full-coverage-prepared-pin-scope'
            $totalBytes += [long]$file.length
            Require-FullCoverage ($totalBytes -le 8589934592) 'full-coverage-prepared-byte-limit'
            [void](Pin-FullCoverageFile $file.path $file.sha256)
            Require-FullCoverage ((Get-Item -LiteralPath $file.path).Length -eq $file.length) 'full-coverage-prepared-pin-length'
        }
        $catalogs = @($preparedDocument.admission.catalogs)
        Require-FullCoverage ($catalogs.Count -eq 2 -and @($catalogs | Where-Object {
            -not (Same-FullCoveragePath $_.sourceDirectory $root) -or -not (Same-FullCoveragePath $_.buildDirectory $root)
        }).Count -eq 0) 'full-coverage-prepared-catalog-root'
        $planCatalogs = @($catalogs | ForEach-Object { @{ key = $_.key; discoveryPath = $_.discoveryPath; discoverySha256 = $_.discoverySha256 } })
        $templatePath = $preparedDocument.runnerMetadataTemplatePath
        $preparedSha256 = $ExpectedPreparedSha256
        $preparedExpectationPath = Join-Path $runRoot 'prepared-expectation.json'
        Write-FullCoverageJson $preparedExpectationPath (New-FullCoveragePreparedExpectation $preparedPathValue $preparedSha256)
        [void](Invoke-FullCoverageMetadata 'prepared-check' @('coverage-full-prepared-check', '--expectation', $preparedExpectationPath,
            '--output', (Join-Path $runRoot 'prepared-check.json')) -PhaseTimeoutMilliseconds 0)
        $runtimePath = Join-Path $runRoot 'runtime-environment.json'
        [void](Invoke-FullCoverageMetadata 'runtime-environment' @('coverage-full-environment', '--output', $runtimePath))
        $currentRuntime = Read-FullCoverageJson $runtimePath
        $preparedRuntime = Read-FullCoverageJson $preparedDocument.runtimeEnvironmentPath
        Require-FullCoverage (($currentRuntime.value | ConvertTo-Json -Depth 16 -Compress) -ceq
            ($preparedRuntime.value | ConvertTo-Json -Depth 16 -Compress)) 'full-coverage-prepared-runtime-drift'
        $planRequest = Join-Path $runRoot 'plan-request.json'
        $planIndex = $(if ($Operation -ceq 'Shard') { $ShardIndex } else { 0 })
        $planCount = $(if ($Operation -ceq 'Shard') { $ShardCount } else { 1 })
        Write-FullCoverageJson $planRequest @{ schemaVersion = 1; catalogs = $planCatalogs; shardIndex = $planIndex; shardCount = $planCount }
        $planPath = Join-Path $runRoot 'plan.json'
        [void](Invoke-FullCoverageMetadata 'plan' @('coverage-full-plan', '--input', $planRequest, '--output', $planPath))
        $planDocument = Read-FullCoverageJson $planPath
        Require-FullCoverage ($planDocument.complete -eq $true -and $planDocument.kind -ceq 'coverage-full-plan' -and
            $planDocument.value.shardIndex -eq $planIndex -and $planDocument.value.shardCount -eq $planCount) 'full-coverage-plan-rejected'
        $plan = $planDocument.value
        foreach ($owner in $plan.owners) {
            $owners.Add([pscustomobject]@{ id = $owner.id; catalogKey = $owner.catalogKey; kind = $owner.kind; profile = $owner.profile
                canonicalIds = @($owner.cases | ForEach-Object canonicalId)
                status = $(if ($owner.id -cin @($plan.selectedOwnerIds)) { 'not-run' } else { 'not-selected' })
                directory = 'sessions/' + $owner.id; exitCode = $null })
        }
    }
    if ($Operation -cin @('Full','Shard')) {
    $stage = 'serial-raw-collection'
    [void][IO.Directory]::CreateDirectory((Join-Path $runRoot 'sessions'))
    foreach ($owner in $plan.owners) {
        if ($owner.id -cnotin @($plan.selectedOwnerIds)) { continue }
        Assert-FullCoveragePins
        $record = @($owners | Where-Object id -CEQ $owner.id)[0]
        $record.status = 'attempted-unresolved'
        $catalog = @($catalogs | Where-Object key -CEQ $owner.catalogKey)[0]
        $testDll = Join-Path $root $catalog.binaries[1].dllRelativePath
        $productDll = Join-Path $root $catalog.binaries[0].dllRelativePath
        $sessionDirectory = Join-Path $runRoot $record.directory
        $arguments = @('-NoLogo','-NoProfile','-NonInteractive','-File')
        if ($owner.kind -ceq 'ordered-profile') {
            $arguments += @((Join-Path $root 'tools/Invoke-OrderedProfileCoverage.ps1'), '-Profile', [string]$owner.profile)
        } else {
            Require-FullCoverage ($owner.kind -ceq 'isolated-case' -and @($owner.cases).Count -eq 1) 'full-coverage-invalid-owner-kind'
            $arguments += @((Join-Path $root 'tools/Invoke-IsolatedCoverage.ps1'), '-CaseId', [string]$owner.cases[0].canonicalId,
                '-PreparedPath',$preparedPathValue,'-ExpectedPreparedSha256',$preparedSha256)
        }
        $arguments += @('-TestAssembly',$testDll,'-ProductAssembly',$productDll,'-DotCoverToolFile',$DotCoverToolFile,
            '-MetadataToolAssembly',$metadataTool,'-RunnerMetadataTemplatePath',$templatePath,'-ResultsDirectory',$sessionDirectory,'-RepositoryRoot',$root,'-RunId',$runId)
        # No retry, warmup or replacement session. The invoked owner preserves
        # its root/startup/cleanup budgets, Job and original output drains.
        $phaseLabel = 'collect-' + $owner.id
        try { [void](Invoke-FullCoverageOwnedPhase $phaseLabel $shell $arguments -PhaseTimeoutMilliseconds 0) }
        finally {
            $observedPhases = @($phases | Where-Object name -CEQ $phaseLabel)
            if ($observedPhases.Count -eq 1) { $record.exitCode = $observedPhases[0].exitCode }
        }
        $rawInput = Read-FullCoverageJson (Join-Path $sessionDirectory 'coverage-input.json')
        $expectation = Read-FullCoverageJson (Join-Path $sessionDirectory 'admission-expectation.json')
        Require-FullCoverage ($expectation.identity.runId -ceq $runId -and $expectation.identity.buildId -ceq $catalog.buildId -and
            $expectation.identity.source.commit -ceq $source.commit -and $expectation.identity.source.tree -ceq $source.tree -and
            $expectation.identity.source.fingerprint -ceq $source.fingerprint) 'full-coverage-owner-current-build-mismatch'
        $item = @{ catalogKey = $owner.catalogKey; inputPath = Join-Path $sessionDirectory 'coverage-input.json'
            expectationPath = Join-Path $sessionDirectory 'admission-expectation.json' }
        if ($owner.kind -ceq 'ordered-profile') { $groups.Add($item) } else { $isolated.Add($item) }
        $record.status = 'raw-owner-returned'
        Assert-FullCoveragePins
    }
    $allCollectorsReturned = $true
    if ($Operation -ceq 'Shard') {
        $stage = 'independent-raw-selected-shard-admission'
        $inputPath = Join-Path $runRoot 'shard-input.json'
        Write-FullCoverageJson $inputPath @{ schemaVersion = 1; shardIndex = $ShardIndex; shardCount = $ShardCount
            shardDirectory = $runRoot; runtimeEnvironmentPath = $runtimePath
            isolatedOwners = $isolated.ToArray(); orderedGroups = $groups.ToArray() }
        $receiptPath = Join-Path $runRoot 'shard-receipt.json'
        $validationPhase = Invoke-FullCoverageMetadata 'shard-check' @('coverage-full-shard-check', '--input', $inputPath,
            '--expectation', $preparedExpectationPath, '--output', $receiptPath) -PhaseTimeoutMilliseconds 0
        $receipt = Read-FullCoverageJson $receiptPath
        Require-FullCoverage ($receipt.complete -eq $true -and $receipt.kind -ceq 'coverage-full-shard-check' -and
            $receipt.value.selectedOwnersAdmitted -eq $true -and $receipt.value.dependencyRecordsComplete -eq $true -and
            $receipt.value.coverageUnionComplete -eq $false -and $receipt.value.shardIndex -eq $ShardIndex -and
            $receipt.value.shardCount -eq $ShardCount) 'full-coverage-shard-incomplete'
        $pendingShard = @{ input = $inputPath; expectation = $preparedExpectationPath; receipt = $receiptPath
            validationInvocation = Join-Path $validationPhase 'argv.json'; validationProcess = Join-Path $validationPhase 'process.json' }
        $operationComplete = $true
    } else {
    $stage = 'independent-raw-union-composition'
    $inputPath = Join-Path $runRoot 'composition-input.json'
    Write-FullCoverageJson $inputPath @{ schemaVersion = 1; isolatedOwners = $isolated.ToArray(); orderedGroups = $groups.ToArray() }
    $candidatePath = Join-Path $runRoot 'candidate.json'
    [void](Invoke-FullCoverageMetadata 'compose' @('coverage-full-compose', '--input', $inputPath, '--expectation', $admissionPath, '--output', $candidatePath) -PhaseTimeoutMilliseconds 0)
    $candidate = Read-FullCoverageJson $candidatePath
    Require-FullCoverage ($candidate.complete -eq $true -and $candidate.kind -ceq 'coverage-full-compose' -and
        $candidate.value.coverageUnionComplete -eq $true -and $candidate.value.dependencyRecordsComplete -eq $true -and
        $candidate.value.completeBaseline -eq $false -and $candidate.value.publicationAuthorized -eq $false) 'full-coverage-union-incomplete'
    Assert-FullCoveragePins
    $unionAdmitted = $true
    $operationComplete = $true
    }
    } elseif ($Operation -ceq 'Compose') {
        $stage = 'independent-all-shard-raw-union-composition'
        $manifestPins = [Collections.Generic.List[object]]::new()
        for ($index = 0; $index -lt $ShardCount; $index++) {
            Require-FullCoverage ([IO.Path]::IsPathFullyQualified($ShardManifestPath[$index]) -and
                $ExpectedShardSha256[$index] -cmatch '^[a-f0-9]{64}$') 'full-coverage-shard-pin-required'
            [void](Pin-FullCoverageFile $ShardManifestPath[$index] $ExpectedShardSha256[$index])
            $manifestPins.Add(@{ path = [IO.Path]::GetFullPath($ShardManifestPath[$index]); sha256 = $ExpectedShardSha256[$index] })
        }
        $inputPath = Join-Path $runRoot 'shards-composition-input.json'
        Write-FullCoverageJson $inputPath @{ schemaVersion = 1; shardCount = $ShardCount; shards = $manifestPins.ToArray() }
        $candidatePath = Join-Path $runRoot 'candidate.json'
        [void](Invoke-FullCoverageMetadata 'shards-compose' @('coverage-full-shards-compose', '--input', $inputPath,
            '--expectation', $preparedExpectationPath, '--output', $candidatePath) -PhaseTimeoutMilliseconds 0)
        $candidate = Read-FullCoverageJson $candidatePath
        Require-FullCoverage ($candidate.complete -eq $true -and $candidate.kind -ceq 'coverage-full-shards-compose' -and
            $candidate.value.coverageUnionComplete -eq $true -and $candidate.value.dependencyRecordsComplete -eq $true -and
            $candidate.value.completeBaseline -eq $false -and $candidate.value.publicationAuthorized -eq $false) 'full-coverage-union-incomplete'
        foreach ($record in $owners) { $record.status = 'raw-union-reimported' }
        $unionAdmitted = $true
        $operationComplete = $true
    }
    Assert-FullCoveragePins
    }
} catch { $failure = $_ }
finally {
    foreach ($guard in $locks) {
        try { $guard.Dispose() } catch { if ($null -eq $failure) { $failure = $_; $reason = 'full-coverage-input-release-failed' } }
    }
    if ($null -ne $sourceLease) {
        try { $sourceLease.Dispose() } catch { if ($null -eq $failure) { $failure = $_; $reason = 'full-coverage-source-lease-release-failed' } }
    }
    if ($runRoot -and -not $delegated) {
        try {
            Write-FullCoverageJson (Join-Path $runRoot 'coordinator.json') @{ schemaVersion = 1; kind = 'full-coverage-local-outcome'
                operation = $Operation; operationComplete = ($operationComplete -and $null -eq $failure)
                runId = $runId; source = $source; allCollectorsReturned = $allCollectorsReturned; coverageUnionAdmitted = ($unionAdmitted -and $null -eq $failure)
                completeBaseline = $false; publicationAuthorized = $false; tiaSelectionAuthorized = $false
                stage = $stage; reason = $(if ($failure) { $reason } else { $null }); owners = $owners.ToArray(); phases = $phases.ToArray()
                planningUnresolvedIds = $(if ($null -eq $plan) { $knownIds.ToArray() } else { @() })
                failureType = $(if ($failure) { $failure.Exception.GetType().Name } else { $null }) }
        } catch { if ($null -eq $failure) { $failure = $_; $reason = 'full-coverage-outcome-write-failed' } }
        if ($null -eq $failure -and $null -ne $pendingShard) {
            try {
                $manifestPendingPath = Join-Path $runRoot 'shard.pending.json'
                Write-FullCoverageJson $manifestPendingPath ([ordered]@{ schemaVersion = 1
                    preparedPath = $preparedPathValue; preparedSha256 = $preparedSha256; shardIndex = $ShardIndex; shardCount = $ShardCount
                    shardDirectory = $runRoot; input = FullCoverage-FilePin $pendingShard.input
                    expectation = FullCoverage-FilePin $pendingShard.expectation; receipt = FullCoverage-FilePin $pendingShard.receipt
                    validationInvocation = FullCoverage-FilePin $pendingShard.validationInvocation
                    validationProcess = FullCoverage-FilePin $pendingShard.validationProcess
                    coordinator = FullCoverage-FilePin (Join-Path $runRoot 'coordinator.json') })
                # Publish only after the fresh pending file was flushed and
                # disposed successfully. Failed pending bytes remain private
                # evidence and are not an admissible final manifest name.
                [IO.File]::Move($manifestPendingPath, (Join-Path $runRoot 'shard.json'), $false)
            } catch { $failure = $_; $reason = 'full-coverage-shard-manifest-write-failed' }
        }
    }
}
if ($null -ne $failure -or -not $operationComplete) {
    [Console]::Error.WriteLine('full-coverage: incomplete stage=' + $stage + ' reason=' + $reason + '; raw outcomes and not-run owners retained.')
    exit 1
}
if ($Operation -ceq 'Prepare') {
    Write-Output ('full-coverage: prepared immutable source/build/catalogs only; no cases executed. prepared=' +
        $(if ($delegated) { Join-Path $runRoot 'coverage/prepared.json' } else { $preparedPathValue }))
} elseif ($Operation -ceq 'Shard') {
    Write-Output 'full-coverage: exact selected raw owners admitted; unselected owners retained; complete union not claimed.'
} else {
    Write-Output 'full-coverage: complete current local raw union admitted; package verification and formal baseline publication remain separate.'
}
exit 0
