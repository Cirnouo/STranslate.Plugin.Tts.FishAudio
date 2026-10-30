#requires -Version 7.0
# Shared admission plumbing, not a scenario runner or a registry.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:HybridToolsRoot = $PSScriptRoot
. (Join-Path $script:HybridToolsRoot 'TestProcessOwner.ps1')
$script:HybridLocks = [Collections.Generic.List[IO.FileStream]]::new()
$script:HybridArtifacts = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
$script:failureCode = 'hybrid-rejected'
$HybridDefinitions = @(
    @{ key = 'plugin'; assembly = 'tests/STranslate.Plugin.Tts.FishAudio.Tests/bin/Release/net10.0-windows/STranslate.Plugin.Tts.FishAudio.Tests.dll' },
    @{ key = 'testing-tool'; assembly = 'tests/FishAudio.Testing.Tests/bin/Release/net10.0-windows/FishAudio.Testing.Tests.dll' }
)
function Require-Hybrid([bool]$Condition, [string]$Code) {
    if (-not $Condition) { $script:failureCode = $Code; throw $Code }
}
function Write-HybridJson([string]$Path, $Value) {
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 64))
        $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true)
    } finally { $stream.Dispose() }
}
function Get-HybridDigest([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Test-HybridSamePath([string]$Left, [string]$Right) {
    [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Left)).Equals(
        [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Right)), [StringComparison]::OrdinalIgnoreCase)
}
function Test-HybridContained([string]$Path, [string]$Parent) {
    [IO.Path]::GetFullPath($Path).StartsWith([IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Parent)) +
        [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}
function Assert-HybridRegular([string]$Path) {
    Require-Hybrid ([IO.Path]::IsPathFullyQualified($Path)) 'hybrid-path-not-absolute'
    $file = [IO.FileInfo]::new($Path)
    Require-Hybrid ($file.Exists -and ($file.Attributes -band
        ([IO.FileAttributes]::Directory -bor [IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Device)) -eq 0) 'hybrid-file-not-regular'
    for ($parent = $file.Directory; $null -ne $parent; $parent = $parent.Parent) {
        Require-Hybrid ($parent.Exists -and ($parent.Attributes -band
            ([IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Device)) -eq 0) 'hybrid-parent-not-regular'
    }
}
function Lock-HybridFile([string]$Path, [string]$Digest = '', [switch]$Retained) {
    Assert-HybridRegular $Path
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $script:HybridLocks.Add($stream)
    Assert-HybridRegular $Path
    $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()
    Require-Hybrid ([string]::IsNullOrEmpty($Digest) -or ($Digest -cmatch '^[a-f0-9]{64}$' -and $actual -ceq $Digest)) 'hybrid-artifact-digest'
    if ($Retained) {
        if ($script:HybridArtifacts.ContainsKey($Path)) { Require-Hybrid ($script:HybridArtifacts[$Path] -ceq $actual) 'hybrid-artifact-changed' }
        else { $script:HybridArtifacts.Add($Path, $actual) }
    }
    return $Path
}
function Resolve-HybridArtifact([string]$Parent, [string]$Relative, [string]$Digest) {
    Require-Hybrid (-not [string]::IsNullOrWhiteSpace($Relative) -and -not [IO.Path]::IsPathRooted($Relative) -and $Digest -cmatch '^[a-f0-9]{64}$') 'hybrid-artifact-path'
    $path = [IO.Path]::GetFullPath((Join-Path $Parent $Relative))
    Require-Hybrid (Test-HybridContained $path $Parent) 'hybrid-artifact-escape'
    return Lock-HybridFile $path $Digest -Retained
}
function Assert-HybridJsonElement([Text.Json.JsonElement]$Element) {
    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            Require-Hybrid ($seen.Add($property.Name)) 'hybrid-json-duplicate-property'
            Assert-HybridJsonElement $property.Value
        }
    } elseif ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Element.EnumerateArray()) { Assert-HybridJsonElement $item }
    }
}
function Read-HybridJson([string]$Path) {
    Assert-HybridRegular $Path
    Require-Hybrid (([IO.FileInfo]::new($Path)).Length -gt 0 -and ([IO.FileInfo]::new($Path)).Length -le 67108864) 'hybrid-json-size'
    $stream = [IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $script:HybridLocks.Add($stream)
    Require-Hybrid ($stream.Length -gt 0 -and $stream.Length -le 67108864) 'hybrid-json-size'
    $document = [Text.Json.JsonDocument]::Parse($stream)
    try {
        Assert-HybridJsonElement $document.RootElement
        return ($document.RootElement.GetRawText() | ConvertFrom-Json -Depth 64)
    } finally { $document.Dispose() }
}
function New-HybridDirectory([string]$Path, [string]$RepositoryRoot) {
    Require-Hybrid ([IO.Path]::IsPathFullyQualified($Path) -and -not (Test-HybridContained $Path $RepositoryRoot) -and
        -not (Test-HybridSamePath $Path $RepositoryRoot) -and -not [IO.Directory]::Exists($Path) -and -not [IO.File]::Exists($Path)) 'hybrid-output-not-fresh-or-overlaps-source'
    for ($ancestor = [IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($Path)); $null -ne $ancestor; $ancestor = $ancestor.Parent) {
        if ($ancestor.Exists) { Require-Hybrid (($ancestor.Attributes -band ([IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Device)) -eq 0) 'hybrid-output-parent-reparse' }
    }
    [IO.Directory]::CreateDirectory($Path) | Out-Null
    for ($parent = [IO.DirectoryInfo]::new($Path); $null -ne $parent; $parent = $parent.Parent) {
        Require-Hybrid (($parent.Attributes -band ([IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Device)) -eq 0) 'hybrid-output-reparse'
    }
}
function Assert-HybridIds($Actual, [string[]]$Expected, [switch]$Ordered, [switch]$AllowEmpty) {
    Require-Hybrid ($Actual -is [Array] -and ($AllowEmpty -or $Actual.Count -gt 0)) 'hybrid-case-set-shape'
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($id in $Actual) {
        Require-Hybrid ($id -is [string] -and -not [string]::IsNullOrWhiteSpace($id) -and $seen.Add($id)) 'hybrid-case-set-duplicate'
    }
    Require-Hybrid ($seen.Count -eq $Expected.Count -and $seen.SetEquals($Expected)) 'hybrid-case-set-mismatch'
    if ($Ordered) {
        for ($index = 0; $index -lt $Expected.Count; $index++) { Require-Hybrid ($Actual[$index] -ceq $Expected[$index]) 'hybrid-case-order' }
    }
}
function Initialize-HybridLauncherIdentity {
    if ('FishAudio.Testing.Driver.LauncherPipeIdentity' -as [type]) { return }
    # The existing launcher waits for ACK before target start. Bind ACK to the
    # real OS pipe client PID already assigned to this invocation's Job.
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace FishAudio.Testing.Driver;
public static class LauncherPipeIdentity
{
    public static uint GetClientProcessId(SafePipeHandle pipe)
    {
        if (!GetNamedPipeClientProcessId(pipe, out uint pid))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return pid;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
}
'@
}
function Invoke-HybridProcess {
    param([string]$Executable, [string[]]$Arguments, [string]$Directory, [string]$WorkingDirectory,
        [int]$MetadataTimeoutMilliseconds = 0)
    [IO.Directory]::CreateDirectory($Directory) | Out-Null
    $queued = [Diagnostics.Stopwatch]::GetTimestamp()
    $nonce = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
    $controlRunId = [Guid]::NewGuid().ToString('N')
    $pipeName = 'fa-fw-'+$controlRunId+'-launcher'
    $invocationPath = Join-Path $Directory 'invocation.json'
    # This is the existing launcher's optional control contract, unchanged. Its
    # target-exited event is emitted before pipes held by descendants are joined.
    Write-HybridJson $invocationPath @{ schemaVersion = 1; executable = $Executable; arguments = $Arguments
        workingDirectory = $WorkingDirectory; driverControl = @{ pipeName = $pipeName; nonce = $nonce
            runId = $controlRunId; sourceTree = $null } }
    $shell = (Get-Process -Id $PID).Path
    $process = [Diagnostics.Process]::new()
    $start = [Diagnostics.ProcessStartInfo]::new($shell)
    $start.WorkingDirectory = $WorkingDirectory; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    foreach ($argument in @('-NoLogo','-NoProfile','-NonInteractive','-File',
        (Join-Path $script:HybridToolsRoot 'Invoke-TestProcess.ps1'),'-ArgumentsPath',$invocationPath)) { $start.ArgumentList.Add($argument) }
    foreach ($key in @('GIT_INDEX_FILE','GIT_DIR','GIT_WORK_TREE','GIT_COMMON_DIR','GIT_PREFIX','FISHAUDIO_EXPECTED_TREE')) { [void]$start.Environment.Remove($key) }
    $start.Environment['FISHAUDIO_TEST_REPOSITORY_ROOT'] = $WorkingDirectory
    $process.StartInfo = $start
    $streams = @(); $tasks = @(); $started = $false; $assigned = $false
    $owner = $null; $pipe = $null; $reader = $null; $connection = $null; $eventRead = $null
    $acknowledged = $false; $eventCount = 0; $targetPid = $null; $targetExit = $null
    $targetExitTimestamp = $null; $cleanupAnchor = $null; $ended = $null; $pidValue = $null
    $exitCode = $null; $failure = $null; $primaryCode = $null; $activeProcesses = $null
    $stdoutDrained = $false; $stderrDrained = $false; $childrenExited = $false
    $cleanupTypes = [Collections.Generic.List[string]]::new()
    try {
        Initialize-ProcessOwner
        Initialize-HybridLauncherIdentity
        $owner = [FishAudio.Testing.Driver.JobOwner]::new()
        $pipeOptions = [IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::CurrentUserOnly
        $pipe = [IO.Pipes.NamedPipeServerStream]::new($pipeName,[IO.Pipes.PipeDirection]::InOut,1,
            [IO.Pipes.PipeTransmissionMode]::Byte,$pipeOptions,4096,4096)
        $connection = $pipe.WaitForConnectionAsync()
        foreach ($name in @('stdout.bin','stderr.bin')) {
            $streams += [IO.FileStream]::new((Join-Path $Directory $name),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read,65536,$true)
        }
        $started = $process.Start()
        Require-Hybrid $started 'hybrid-process-not-started'
        $pidValue = $process.Id
        $tasks = @($process.WaitForExitAsync(),$process.StandardOutput.BaseStream.CopyToAsync($streams[0]),
            $process.StandardError.BaseStream.CopyToAsync($streams[1]))
        $owner.Assign($process.Handle)
        $assigned = $true
        $process.StandardInput.WriteLine('GO')
        $process.StandardInput.Close()
        while ($true) {
            foreach ($task in $tasks) {
                if ($task.IsFaulted -or $task.IsCanceled) { [void]$task.GetAwaiter().GetResult() }
            }
            if (-not $acknowledged -and $connection.IsCompleted) {
                [void]$connection.GetAwaiter().GetResult()
                Require-Hybrid ($assigned -and [FishAudio.Testing.Driver.LauncherPipeIdentity]::GetClientProcessId($pipe.SafePipeHandle) -eq $pidValue) 'hybrid-launcher-unowned-pipe-client'
                $ack = [Text.UTF8Encoding]::new($false).GetBytes('framework-owner-ready:'+$nonce+[char]10)
                # One fixed small acknowledgement fits the new pipe's buffer.
                $pipe.Write($ack,0,$ack.Length)
                $reader = [IO.StreamReader]::new($pipe,[Text.UTF8Encoding]::new($false,$true),$false,4096,$true)
                $eventRead = $reader.ReadLineAsync()
                $acknowledged = $true
            }
            while ($null -ne $eventRead -and $eventRead.IsCompleted) {
                $line = $eventRead.GetAwaiter().GetResult()
                if ($null -eq $line) {
                    Require-Hybrid ($eventCount -eq 2) 'hybrid-launcher-control-truncated'
                    $eventRead = $null
                    break
                }
                Require-Hybrid ($line.Length -gt 0 -and $line.Length -le 16384 -and $eventCount -lt 2) 'hybrid-launcher-control-size'
                $document = [Text.Json.JsonDocument]::Parse([string]$line)
                try { Assert-HybridJsonElement $document.RootElement } finally { $document.Dispose() }
                $record = $line | ConvertFrom-Json -Depth 8
                $fields = @('schemaVersion','role','nonce','runId','sourceTree','pid','kind','targetPid','timestamp','frequency','exitCode')
                Require-Hybrid (@($record.PSObject.Properties).Count -eq $fields.Count -and
                    @($record.PSObject.Properties | Where-Object { $fields -cnotcontains $_.Name }).Count -eq 0 -and
                    $record.schemaVersion -eq 1 -and $record.role -ceq 'execution-launcher' -and $record.nonce -ceq $nonce -and
                    $record.runId -ceq $controlRunId -and $null -eq $record.sourceTree -and $record.pid -eq $pidValue -and
                    ($record.targetPid -is [int] -or $record.targetPid -is [long]) -and $record.targetPid -gt 0 -and
                    $record.frequency -eq [Diagnostics.Stopwatch]::Frequency -and
                    ($record.timestamp -is [long] -or $record.timestamp -is [int]) -and $record.timestamp -ge $queued -and
                    $record.timestamp -le [Diagnostics.Stopwatch]::GetTimestamp()) 'hybrid-launcher-control-identity'
                if ($eventCount -eq 0) {
                    Require-Hybrid ($record.kind -ceq 'target-started' -and $null -eq $record.exitCode) 'hybrid-launcher-control-order'
                    $targetPid = $record.targetPid
                } else {
                    Require-Hybrid ($record.kind -ceq 'target-exited' -and $record.targetPid -eq $targetPid -and
                        ($record.exitCode -is [int] -or $record.exitCode -is [long])) 'hybrid-launcher-control-order'
                    $targetExit = [int]$record.exitCode
                    $targetExitTimestamp = [long]$record.timestamp
                    $cleanupAnchor = $targetExitTimestamp
                }
                Write-HybridJson (Join-Path $Directory ('launcher-event-'+$eventCount+'.json')) $record
                $eventCount++
                $eventRead = if ($eventCount -lt 2) { $reader.ReadLineAsync() } else { $null }
                if ($null -ne $targetExit -and $targetExit -ne 0) {
                    # Latch the actual parent failure before descendant/drain
                    # cleanup. The same original target-exit anchor remains.
                    $exitCode = $targetExit
                    Require-Hybrid $false 'hybrid-process-target-failed'
                }
            }
            $activeProcesses = $owner.ActiveProcesses
            if ($tasks[0].IsCompleted -and $tasks[1].IsCompleted -and $tasks[2].IsCompleted -and $activeProcesses -eq 0) {
                Require-Hybrid ($eventCount -eq 2) 'hybrid-launcher-target-exit-missing'
                break
            }
            if ($tasks[0].IsCompleted -and $eventCount -lt 2) {
                # The unmodified shim either reports its real target or fails.
                Require-Hybrid ($process.ExitCode -eq 0) 'hybrid-launcher-target-not-observed'
            }
            $now = [Diagnostics.Stopwatch]::GetTimestamp()
            if ($null -ne $cleanupAnchor) {
                Require-Hybrid (($now-$cleanupAnchor)/[double][Diagnostics.Stopwatch]::Frequency -lt 10) 'hybrid-process-exit-drain-deadline'
            }
            if ($MetadataTimeoutMilliseconds -gt 0) {
                Require-Hybrid (($now-$queued)*1000/[double][Diagnostics.Stopwatch]::Frequency -lt $MetadataTimeoutMilliseconds) 'hybrid-metadata-process-deadline'
            }
            [Threading.Thread]::Sleep(25)
        }
        [void]$tasks[0].GetAwaiter().GetResult()
        [void]$tasks[1].GetAwaiter().GetResult()
        [void]$tasks[2].GetAwaiter().GetResult()
        $exitCode = $process.ExitCode
        Require-Hybrid ($exitCode -eq $targetExit) 'hybrid-launcher-exit-code'
        if ($MetadataTimeoutMilliseconds -gt 0) {
            Require-Hybrid (([Diagnostics.Stopwatch]::GetTimestamp()-$queued)*1000/[double][Diagnostics.Stopwatch]::Frequency -lt $MetadataTimeoutMilliseconds) 'hybrid-metadata-process-deadline'
        }
    } catch { $failure = $_; $primaryCode = $script:failureCode }
    finally {
        # One allowance from actual target exit, or first failure when no target
        # exit exists. Cleanup never creates a second ten-second wait.
        if ($null -ne $failure) {
            if ($null -eq $cleanupAnchor) { $cleanupAnchor = [Diagnostics.Stopwatch]::GetTimestamp() }
            try {
                if ($assigned) { $owner.Terminate() }
                elseif ($started -and -not $process.HasExited) { $process.Kill($true) }
                while (($started -and -not $process.HasExited) -or
                    @($tasks | Where-Object { -not $_.IsCompleted }).Count -ne 0 -or
                    ($assigned -and $owner.ActiveProcesses -ne 0)) {
                    Require-Hybrid (([Diagnostics.Stopwatch]::GetTimestamp()-$cleanupAnchor)/[double][Diagnostics.Stopwatch]::Frequency -lt 10) 'hybrid-process-cleanup-deadline'
                    [Threading.Thread]::Sleep(25)
                }
                foreach ($task in $tasks) { [void]$task.GetAwaiter().GetResult() }
                if ($started -and $process.HasExited) { $exitCode = $process.ExitCode }
            } catch { $cleanupTypes.Add($_.Exception.GetType().FullName) }
        }
        if ($tasks.Count -eq 3) { $stdoutDrained = $tasks[1].IsCompletedSuccessfully; $stderrDrained = $tasks[2].IsCompletedSuccessfully }
        try { if ($assigned) { $activeProcesses = $owner.ActiveProcesses; $childrenExited = $activeProcesses -eq 0 } }
        catch { if ($null -eq $failure) { $failure = $_; $primaryCode = $script:failureCode } else { $cleanupTypes.Add($_.Exception.GetType().FullName) } }
        # Closing a pending pipe before the reader releases its async operation
        # cannot flush a failed publication into a blocked output transport.
        foreach ($resource in @($pipe,$reader)+@($streams)+@($owner,$process)) {
            if ($null -ne $resource) {
                try { $resource.Dispose() }
                catch { if ($null -eq $failure) { $failure = $_; $primaryCode = $script:failureCode } else { $cleanupTypes.Add($_.Exception.GetType().FullName) } }
            }
        }
        # Observe original control tasks after closing their owned pipe. They
        # have no independent allowance and do not establish target completion.
        foreach ($task in @($connection,$eventRead)) {
            if ($null -ne $task) {
                try {
                    while (-not $task.IsCompleted) {
                        Require-Hybrid ($null -ne $cleanupAnchor -and
                            ([Diagnostics.Stopwatch]::GetTimestamp()-$cleanupAnchor)/[double][Diagnostics.Stopwatch]::Frequency -lt 10) 'hybrid-process-control-cleanup-deadline'
                        [Threading.Thread]::Sleep(25)
                    }
                    [void]$task.GetAwaiter().GetResult()
                }
                catch { if ($null -eq $failure) { $failure = $_; $primaryCode = $script:failureCode } else { $cleanupTypes.Add($_.Exception.GetType().FullName) } }
            }
        }
        $ended = [Diagnostics.Stopwatch]::GetTimestamp()
        if ($null -eq $failure) {
            try {
                Require-Hybrid ($null -ne $cleanupAnchor -and
                    ($ended-$cleanupAnchor)/[double][Diagnostics.Stopwatch]::Frequency -lt 10) 'hybrid-process-exit-drain-deadline'
                if ($MetadataTimeoutMilliseconds -gt 0) {
                    Require-Hybrid (($ended-$queued)*1000/[double][Diagnostics.Stopwatch]::Frequency -lt $MetadataTimeoutMilliseconds) 'hybrid-metadata-process-deadline'
                }
            } catch { $failure = $_; $primaryCode = $script:failureCode }
        }
        try { Write-HybridJson (Join-Path $Directory 'process.json') @{ schemaVersion = 1; kind = 'hybrid-owner-process'; pid = $pidValue
            executable = $Executable; arguments = $Arguments; workingDirectory = $WorkingDirectory
            started = $started; exitCode = $exitCode; complete = ($null -eq $failure -and $stdoutDrained -and $stderrDrained -and $childrenExited -and $eventCount -eq 2)
            ownership = 'windows-job-before-target-go'; assigned = $assigned; allChildrenExited = $childrenExited; activeProcesses = $activeProcesses
            stdoutDrained = $stdoutDrained; stderrDrained = $stderrDrained; queuedTimestamp = $queued; endedTimestamp = $ended
            targetProcessId = $targetPid; targetExitObserved = ($null -ne $targetExit); targetExitCode = $targetExit; controlEvents = $eventCount
            targetExitTimestamp = $targetExitTimestamp; cleanupAnchorTimestamp = $cleanupAnchor; metadataTimeoutMilliseconds = $MetadataTimeoutMilliseconds
            frequency = [Diagnostics.Stopwatch]::Frequency; failureType = $(if ($null -ne $failure) { $failure.Exception.GetType().Name } else { $null })
            reason = $(if ($null -ne $failure) { $primaryCode } else { $null }); cleanupExceptionTypes = $cleanupTypes.ToArray() }
        } catch { if ($null -eq $failure) { $failure = $_; $primaryCode = $script:failureCode } }
    }
    if ($null -ne $failure) { $script:failureCode = $primaryCode; throw $failure }
    return [int]$exitCode
}

function Invoke-HybridTool([string]$Tool, [string[]]$Arguments, [string]$Directory, [string]$RepositoryRoot) {
    $code = Invoke-HybridProcess (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source (@($Tool) + $Arguments) $Directory $RepositoryRoot 120000
    Require-Hybrid ($code -eq 0) 'hybrid-metadata-tool-failed'
}
function Get-HybridSource([string]$RepositoryRoot, [string]$Directory) {
    [IO.Directory]::CreateDirectory($Directory) | Out-Null
    $paths = [Collections.Generic.List[string]]::new()
    $context = $null; $legacyTree = $null
    if ($env:FISHAUDIO_SNAPSHOT_CONTEXT) {
        [void](Lock-HybridFile $env:FISHAUDIO_SNAPSHOT_CONTEXT)
        $context = Read-HybridJson $env:FISHAUDIO_SNAPSHOT_CONTEXT
        Require-Hybrid ($context.schemaVersion -eq 1 -and (Test-HybridSamePath $context.root $RepositoryRoot) -and
            $context.tree -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$' -and ($null -eq $context.sourceCommit -or $context.sourceCommit -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$')) 'hybrid-snapshot-context'
        foreach ($item in $context.files) { $paths.Add([string]$item.path) }
        $legacyTree = $context.tree
        $identity = [ordered]@{ snapshot = $true; tree = $context.tree; sourceCommit = $context.sourceCommit; commit = $context.sourceCommit
            snapshotRootValidated = $true; contextSha256 = Get-HybridDigest $env:FISHAUDIO_SNAPSHOT_CONTEXT }
    } else {
        $git = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        $queries = @(@('rev-parse','--show-toplevel'), @('rev-parse','--verify','HEAD'), @('rev-parse','HEAD^{tree}'),
            @('ls-files','-z','--cached','--others','--exclude-standard'), @('status','--porcelain=v1','--untracked-files=all'))
        $answers = @()
        for ($index = 0; $index -lt $queries.Count; $index++) {
            $phase = Join-Path $Directory ('git-' + $index)
            $code = Invoke-HybridProcess $git (@('--no-optional-locks','-C',$RepositoryRoot) + $queries[$index]) $phase $RepositoryRoot 120000
            Require-Hybrid ($code -eq 0) 'hybrid-source-git-failed'
            $answers += [Text.UTF8Encoding]::new($false,$true).GetString([IO.File]::ReadAllBytes((Join-Path $phase 'stdout.bin')))
        }
        Require-Hybrid (Test-HybridSamePath $answers[0].Trim() $RepositoryRoot) 'hybrid-source-root-fallback'
        $head = $answers[1].Trim(); $legacyTree = $answers[2].Trim()
        Require-Hybrid ($head -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$' -and $legacyTree -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$') 'hybrid-source-git-identity'
        foreach ($path in $answers[3].Split([char]0,[StringSplitOptions]::RemoveEmptyEntries)) { $paths.Add($path) }
        $identity = [ordered]@{ snapshot = $false; tree = $null; sourceCommit = $head; commit = $head; dirty = ($answers[4].Length -ne 0) }
    }
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    try {
        $paths.Sort([StringComparer]::Ordinal)
        foreach ($path in $paths) {
            Require-Hybrid (-not [IO.Path]::IsPathRooted($path) -and $seen.Add($path)) 'hybrid-source-file-list'
            $full = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $path))
            Require-Hybrid (Test-HybridContained $full $RepositoryRoot) 'hybrid-source-file-escape'
            [void](Lock-HybridFile $full)
            $bytes = [IO.File]::ReadAllBytes($full)
            if ($null -ne $context) {
                $entry = @($context.files | Where-Object path -CEQ $path)
                Require-Hybrid ($entry.Count -eq 1 -and (Get-HybridDigest $full) -ceq $entry[0].sha256) 'hybrid-snapshot-file-changed'
            }
            $name = [Text.UTF8Encoding]::new($false).GetBytes($path.Replace('\','/'))
            $hash.AppendData([BitConverter]::GetBytes([long]$name.Length)); $hash.AppendData($name)
            $hash.AppendData([BitConverter]::GetBytes([long]$bytes.Length)); $hash.AppendData($bytes)
        }
        Require-Hybrid ($paths.Count -gt 0) 'hybrid-source-empty'
        $identity['fingerprint'] = [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
        $identity['fingerprintKind'] = 'sha256-source-path-and-bytes-ordinal-v1'
        return [pscustomobject]@{ identity = $identity; legacyTree = $legacyTree }
    } finally { $hash.Dispose() }
}
function Assert-HybridSource($Actual, $Expected) {
    foreach ($name in @('snapshot','tree','sourceCommit','commit','fingerprint','fingerprintKind')) {
        Require-Hybrid ($Actual.$name -ceq $Expected.$name) 'hybrid-source-identity-mismatch'
    }
    if ($Expected.snapshot) { Require-Hybrid ($Actual.snapshotRootValidated -eq $true -and $Actual.contextSha256 -ceq $Expected.contextSha256) 'hybrid-snapshot-identity-mismatch' }
    else { Require-Hybrid ($Actual.dirty -eq $Expected.dirty) 'hybrid-live-source-dirty-mismatch' }
}
function Get-HybridBuildPins([string]$RepositoryRoot) {
    $paths = @($HybridDefinitions | ForEach-Object assembly) + @('tools/FishAudio.Testing/bin/Release/net10.0-windows/FishAudio.Testing.dll')
    return @(foreach ($path in $paths) {
        foreach ($suffix in @('.dll','.deps.json','.runtimeconfig.json')) {
            $relative = [IO.Path]::ChangeExtension($path,$suffix); $full = Join-Path $RepositoryRoot $relative
            [void](Lock-HybridFile $full)
            [pscustomobject]@{ path = $relative; sha256 = Get-HybridDigest $full }
        }
    })
}
function Assert-HybridBuildPins($Actual, $Expected) {
    Require-Hybrid ($Actual.Count -eq $Expected.Count) 'hybrid-build-set'
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        Require-Hybrid ($Actual[$index].path -ceq $Expected[$index].path -and $Actual[$index].sha256 -ceq $Expected[$index].sha256) 'hybrid-build-changed'
    }
}
function Read-HybridDiscovery([string]$Directory, $ExpectedSource, [string]$RepositoryRoot) {
    [void](Lock-HybridFile (Join-Path $Directory 'manifest.json') -Retained)
    $manifest = Read-HybridJson (Join-Path $Directory 'manifest.json')
    Require-Hybrid ($manifest.schemaVersion -eq 1 -and $manifest.kind -ceq 'unified-test-session' -and $manifest.mode -ceq 'Discover' -and
        $manifest.complete -eq $true -and $manifest.baselineClaim -eq $false -and @($manifest.assemblies).Count -eq 2) 'hybrid-fresh-discovery'
    Assert-HybridSource $manifest.source $ExpectedSource
    $records = @{}
    foreach ($definition in $HybridDefinitions) {
        $matches = @($manifest.assemblies | Where-Object key -CEQ $definition.key)
        Require-Hybrid ($matches.Count -eq 1 -and $matches[0].assembly -ceq $definition.assembly -and
            $matches[0].sha256 -ceq (Get-HybridDigest (Join-Path $RepositoryRoot $definition.assembly))) 'hybrid-discovery-assembly'
        $record = $matches[0]
        $phase = @($manifest.phases | Where-Object name -CEQ ('discover-' + $definition.key))
        Require-Hybrid ($phase.Count -eq 1 -and $phase[0].complete -eq $true -and $phase[0].exitCode -eq 0) 'hybrid-discovery-phase'
        $dump = Join-Path $Directory ($phase[0].directory + '/discovery.dump')
        Require-Hybrid (Test-HybridContained $dump $Directory) 'hybrid-discovery-escape'
        [void](Lock-HybridFile $dump -Retained)
        $catalog = Resolve-HybridArtifact $Directory $record.catalog $record.catalogSha256
        $records[$definition.key] = [pscustomobject]@{ assembly = $record; dump = $dump; catalog = Read-HybridJson $catalog }
    }
    foreach ($phase in $manifest.phases) { Require-Hybrid ($phase.complete -eq $true -and $phase.exitCode -eq 0) 'hybrid-discovery-incomplete-phase' }
    return [pscustomobject]@{ manifest = $manifest; records = $records }
}
function New-HybridPlan([string]$RepositoryRoot, [string]$Directory, $Discovery, [string]$Tool) {
    [IO.Directory]::CreateDirectory($Directory) | Out-Null
    $legacy = Read-HybridLegacyCatalog (Join-Path $RepositoryRoot $HybridDefinitions[0].assembly) (Get-HybridDigest (Join-Path $RepositoryRoot $HybridDefinitions[0].assembly)) $Directory
    $paths = @($HybridDefinitions | ForEach-Object { $Discovery.records[$_.key].dump })
    $discoveryInput = Join-Path $Directory 'discoveries.json'; $plan = Join-Path $Directory 'plan.json'
    Write-HybridJson $discoveryInput @{ schemaVersion = 1; paths = $paths }
    Invoke-HybridTool $Tool @('evidence-hybrid-plan','--reference',(Join-Path $RepositoryRoot 'tests/Migration/legacy-catalog.json'),
        '--legacy',$legacy,'--discoveries',$discoveryInput,'--migrations',(Join-Path $RepositoryRoot 'tests/Migration/migrated-cases.json'),'--output',$plan) (Join-Path $Directory 'plan-process') $RepositoryRoot
    [void](Lock-HybridFile $plan -Retained)
    $document = Read-HybridJson $plan
    Require-Hybrid ($document.schemaVersion -eq 1 -and $document.kind -ceq 'hybrid-execution-slot-plan' -and $document.complete -eq $true -and $document.baselineClaim -eq $false) 'hybrid-invalid-derived-plan'
    return [pscustomobject]@{ path = $plan; document = $document; legacy = $legacy; discoveries = $discoveryInput }
}
function Assert-HybridSlotIndex($Slots, $ExpectedSlots) {
    Require-Hybrid ($Slots -is [Array] -and $Slots.Count -eq $ExpectedSlots.Count -and $Slots.Count -in @(5,6)) 'hybrid-slot-count'
    $partitions = @('core','native-settings','native-input','native-popups','native-cards')
    $all = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for ($index = 0; $index -lt $Slots.Count; $index++) {
        $slot = $Slots[$index]; $expected = $ExpectedSlots[$index]
        Require-Hybrid ($slot.ordinal -eq ($index+1) -and $slot.id -ceq ('slot-{0:D4}' -f ($index+1)) -and
            $slot.id -ceq $expected.id -and $slot.owner -ceq $expected.owner -and $slot.partition -ceq $expected.partition -and
            $slot.owner -cin @('legacy','framework')) 'hybrid-slot-owner-or-order'
        Require-Hybrid ($(if ($index -lt 5) { $slot.partition -ceq $partitions[$index] } else { $null -eq $slot.partition -and $slot.owner -ceq 'framework' })) 'hybrid-fixed-partition-order'
        Assert-HybridIds $slot.selectedIds ([string[]]$expected.selectedIds) -Ordered
        foreach ($id in $slot.selectedIds) { Require-Hybrid ($all.Add($id)) 'hybrid-owner-overlap' }
    }
}
function Read-HybridLatestAttempt([string]$SlotDirectory, $Records) {
    $physical = @(Get-ChildItem -LiteralPath $SlotDirectory -Directory -Force | Where-Object Name -CMatch '^attempt-[0-9]{4}$' | Sort-Object Name)
    Require-Hybrid ($Records -is [Array] -and $Records.Count -gt 0 -and $physical.Count -eq $Records.Count -and
        @(Get-ChildItem -LiteralPath $SlotDirectory -Directory -Force).Count -eq $physical.Count) 'hybrid-attempt-set'
    for ($index = 0; $index -lt $Records.Count; $index++) {
        Require-Hybrid ($physical[$index].Name -ceq ('attempt-{0:D4}' -f ($index+1)) -and $Records[$index].ordinal -eq ($index+1)) 'hybrid-attempt-order'
        $path = Join-Path $physical[$index].FullName 'attempt.json'
        Require-Hybrid ($Records[$index].sha256 -cmatch '^[a-f0-9]{64}$') 'hybrid-attempt-digest'
        [void](Lock-HybridFile $path ([string]$Records[$index].sha256) -Retained)
        $record = Read-HybridJson $path
        Require-Hybrid ($record.ordinal -eq ($index+1)) 'hybrid-attempt-identity'
    }
    Require-Hybrid ($record.complete -eq $true -and $record.exitCode -eq 0) 'hybrid-latest-attempt-failed-or-incomplete'
    return [pscustomobject]@{ directory = $physical[-1].FullName; record = $record }
}
function Close-HybridLocks {
    foreach ($stream in $script:HybridLocks) { $stream.Dispose() }
    $script:HybridLocks.Clear()
}

function Read-HybridLegacyCatalog([string]$Assembly, [string]$AssemblySha256, [string]$Directory) {
    $previousLegacyFailureCode = $script:failureCode
    $script:failureCode = 'framework-ci-legacy-list-process'
    $executable = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    Require-Hybrid ([IO.Path]::IsPathFullyQualified($executable)) 'framework-ci-legacy-list-executable'
    $stdoutPath = Join-Path $Directory 'legacy-catalog.stdout.bin'
    $stderrPath = Join-Path $Directory 'legacy-catalog.stderr.bin'
    $process = [Diagnostics.Process]::new()
    $start = [Diagnostics.ProcessStartInfo]::new($executable)
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @($Assembly, '--legacy', '--list')) { $start.ArgumentList.Add($argument) }
    $process.StartInfo = $start
    $stdoutFile = $null; $stderrFile = $null; $assemblyGuard = $null
    $stdout = $null; $stderr = $null; $exitTask = $null
    $started = $false; $exited = $false; $drained = $false; $exitCode = $null
    $primary = $null; $primaryCode = $null; $cleanupFailed = $false; $cleanupClock = $null
    $clock = [Diagnostics.Stopwatch]::StartNew()
    try {
        $assemblyGuard = [IO.File]::Open($Assembly, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        Require-Hybrid ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($assemblyGuard)).ToLowerInvariant() -ceq
            $AssemblySha256) 'framework-ci-legacy-list-assembly'
        $stdoutFile = [IO.FileStream]::new($stdoutPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read, 65536, $true)
        $stderrFile = [IO.FileStream]::new($stderrPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read, 65536, $true)
        $started = $process.Start()
        Require-Hybrid $started 'framework-ci-legacy-list-start'
        $stdout = $process.StandardOutput.BaseStream.CopyToAsync($stdoutFile)
        $stderr = $process.StandardError.BaseStream.CopyToAsync($stderrFile)
        $exitTask = $process.WaitForExitAsync()
        while (-not $exitTask.IsCompleted) {
            foreach ($task in @($stdout, $stderr)) {
                if ($task.IsFaulted -or $task.IsCanceled) { [void]$task.GetAwaiter().GetResult() }
            }
            Require-Hybrid ($clock.ElapsedMilliseconds -lt 120000) 'framework-ci-legacy-list-deadline'
            [Threading.Thread]::Sleep(25)
        }
        [void]$exitTask.GetAwaiter().GetResult()
        $exited = $true
        $exitCode = $process.ExitCode
        Require-Hybrid ($clock.ElapsedMilliseconds -lt 120000) 'framework-ci-legacy-list-deadline'
        $cleanupClock = [Diagnostics.Stopwatch]::StartNew()
        $script:failureCode = 'framework-ci-legacy-list-drain'
        [void][Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($stdout, $stderr)).WaitAsync([TimeSpan]::FromSeconds(10)).GetAwaiter().GetResult()
        $drained = $true
        Require-Hybrid ($exitCode -eq 0) 'framework-ci-legacy-list-exit'
    } catch { $primary = $_; $primaryCode = $script:failureCode }
    finally {
        if ($null -ne $primary -and $started) {
            if ($null -eq $cleanupClock) { $cleanupClock = [Diagnostics.Stopwatch]::StartNew() }
            try {
                if (-not $process.HasExited) { $process.Kill($true) }
                $remaining = [TimeSpan]::FromSeconds(10) - $cleanupClock.Elapsed
                Require-Hybrid ($remaining -gt [TimeSpan]::Zero) 'framework-ci-legacy-list-cleanup-deadline'
                $tasks = [Threading.Tasks.Task[]]@(@($exitTask, $stdout, $stderr) | Where-Object { $null -ne $_ })
                [void][Threading.Tasks.Task]::WhenAll($tasks).WaitAsync($remaining).GetAwaiter().GetResult()
                $exited = $process.HasExited
                $exitCode = $process.ExitCode
                $drained = $stdout.IsCompletedSuccessfully -and $stderr.IsCompletedSuccessfully
            } catch { $cleanupFailed = $true }
        }
        foreach ($resource in @($stdoutFile, $stderrFile, $assemblyGuard, $process)) {
            if ($null -ne $resource) {
                try { $resource.Dispose() }
                catch { if ($null -eq $primary) { $primary = $_; $primaryCode = 'framework-ci-legacy-list-dispose' } }
            }
        }
        try {
            Write-HybridJson (Join-Path $Directory 'legacy-catalog-process.json') ([ordered]@{
                schemaVersion = 1; kind = 'legacy-catalog-metadata-process'; complete = ($null -eq $primary -and $exited -and $drained)
                assemblySha256 = $AssemblySha256; arguments = @('--legacy', '--list'); exitCode = $exitCode
                exited = $exited; stdoutDrained = $drained; stderrDrained = $drained; cleanupFailed = $cleanupFailed
                elapsedMilliseconds = $clock.ElapsedMilliseconds; scenarioExecutionClaim = $false; baselineClaim = $false
            })
        } catch { if ($null -eq $primary) { $primary = $_; $primaryCode = 'framework-ci-legacy-list-evidence' } }
    }
    if ($null -ne $primary) { $script:failureCode = $primaryCode; throw $primary }
    Require-Hybrid (([IO.FileInfo]::new($stderrPath)).Length -eq 0) 'framework-ci-legacy-list-stderr'
    Require-Hybrid (([IO.FileInfo]::new($stdoutPath)).Length -gt 0 -and
        ([IO.FileInfo]::new($stdoutPath)).Length -le 67108864) 'framework-ci-legacy-list-size'
    $script:failureCode = 'framework-ci-legacy-list-invalid-utf8'
    $reader = [IO.StreamReader]::new($stdoutPath, [Text.UTF8Encoding]::new($false, $true), $false)
    $cases = [Collections.Generic.List[object]]::new()
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $footer = $false
    try {
        while ($null -ne ($line = $reader.ReadLine())) {
            Require-Hybrid (-not $footer) 'framework-ci-legacy-list-trailing-output'
            if ($line -cmatch '^Selected ([1-9][0-9]{0,5}) case\(s\)\.\z') {
                Require-Hybrid ([int]$Matches[1] -eq $cases.Count) 'framework-ci-legacy-list-count'
                $footer = $true
            } else {
                Require-Hybrid ($line -cmatch '^([a-z0-9][a-z0-9./_-]{0,239})\t([a-z0-9][a-z0-9./_-]{0,239})\z' -and
                    $cases.Count -lt 100000) 'framework-ci-legacy-list-row'
                $id = [string]$Matches[1]; $partition = [string]$Matches[2]
                Require-Hybrid ($ids.Add($id)) 'framework-ci-legacy-list-duplicate'
                $cases.Add([ordered]@{ id = $id; partition = $partition })
            }
        }
        Require-Hybrid ($footer -and $cases.Count -gt 0) 'framework-ci-legacy-list-missing-footer'
    } finally { $reader.Dispose() }
    $path = Join-Path $Directory 'current-legacy-catalog.json'
    # Identity-only metadata: no outcome or completed regression report is made.
    Write-HybridJson $path ([ordered]@{ schemaVersion = 1; cases = $cases.ToArray() })
    $script:failureCode = $previousLegacyFailureCode
    return $path
}

function Assert-HybridFrameworkOwner {
    param([string]$OwnerRoot, [string]$FreshRoot, $ExpectedSource, [string[]]$ExpectedIds,
        [string]$VerificationRoot, [string]$Tool, [string]$RepositoryRoot)
    [IO.Directory]::CreateDirectory($VerificationRoot) | Out-Null
    [void](Lock-HybridFile (Join-Path $OwnerRoot 'manifest.json') -Retained)
    $platform = Read-HybridJson (Join-Path $OwnerRoot 'manifest.json')
    Require-Hybrid ($platform.schemaVersion -eq 2 -and $platform.kind -ceq 'unified-test-session' -and $platform.mode -ceq 'Full' -and
        $platform.complete -eq $true -and $platform.selection -ceq 'focused' -and $platform.configuration -ceq 'Release' -and
        $platform.platform -ceq 'VSTest' -and $platform.baselineClaim -eq $false -and $platform.runId -cmatch '^[a-f0-9]{32}$' -and
        ($platform.workers -is [int] -or $platform.workers -is [long]) -and $platform.workers -in @(0,1,2,3,4) -and $platform.toolSha256 -ceq (Get-HybridDigest $Tool) -and @($platform.assemblies).Count -eq 2) 'hybrid-framework-session'
    Assert-HybridSource $platform.source $ExpectedSource
    $inputPath = Resolve-HybridArtifact $OwnerRoot $platform.selectionInput.path $platform.selectionInput.sha256
    $inputSelection = Read-HybridJson $inputPath
    Require-Hybrid ($inputSelection.schemaVersion -eq 1) 'hybrid-framework-input-selection'
    Assert-HybridIds $inputSelection.ids $ExpectedIds -Ordered
    $fresh = Read-HybridDiscovery $FreshRoot $ExpectedSource $RepositoryRoot
    $phaseNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($phase in $platform.phases) {
        Require-Hybrid ($phase.complete -eq $true -and $phase.exitCode -eq 0) 'hybrid-framework-incomplete-phase'
        if ($phase.name -cnotmatch '^source-(head|files|status)$') { Require-Hybrid ($phaseNames.Add([string]$phase.name)) 'hybrid-framework-duplicate-phase' }
    }
    $admitted = [Collections.Generic.List[string]]::new()
    $requiredPhases = [Collections.Generic.List[string]]::new()
    foreach ($definition in $HybridDefinitions) {
        $key = $definition.key
        $matching = @($platform.assemblies | Where-Object key -CEQ $key)
        Require-Hybrid ($matching.Count -eq 1) 'hybrid-framework-assembly-duplicate'
        $assembly = $matching[0]; $current = $fresh.records[$key]
        Require-Hybrid ($assembly.assembly -ceq $definition.assembly -and $assembly.sha256 -ceq (Get-HybridDigest (Join-Path $RepositoryRoot $definition.assembly))) 'hybrid-framework-assembly-hash'
        $ids = [string[]]@($current.catalog.cases | Where-Object { $ExpectedIds -ccontains $_.canonicalId } | ForEach-Object canonicalId)
        Assert-HybridIds $assembly.selectedIds $ids -Ordered -AllowEmpty
        $paths = @{}
        foreach ($name in @('catalog','selection','requestedSelection')) { $paths[$name] = Resolve-HybridArtifact $OwnerRoot ([string]$assembly.$name) ([string]$assembly.($name+'Sha256')) }
        $catalog = Read-HybridJson $paths.catalog; $selection = Read-HybridJson $paths.selection; $requestedSelection = Read-HybridJson $paths.requestedSelection
        Require-Hybrid ($catalog.schemaVersion -eq 1 -and $catalog.kind -ceq 'nunit-catalog' -and $catalog.complete -eq $true -and
            $selection.schemaVersion -eq 1 -and $requestedSelection.schemaVersion -eq 1 -and
            $assembly.discoveredCount -eq @($current.catalog.cases).Count -and $assembly.selectedCount -eq $ids.Count -and
            $requestedSelection.maximumConcurrency -eq [Math]::Max(1,[int]$platform.workers)) 'hybrid-framework-catalog-or-workers'
        Assert-HybridIds $selection.ids $ids -Ordered -AllowEmpty
        Assert-HybridIds $requestedSelection.ids $ids -Ordered -AllowEmpty
        foreach ($name in @('discover','catalog')) {
            Require-Hybrid ($phaseNames.Contains($name+'-'+$key)) 'hybrid-framework-missing-discovery-phase'
            $requiredPhases.Add($name+'-'+$key)
        }
        $discoveryPhase = @($platform.phases | Where-Object name -CEQ ('discover-'+$key))[0]
        $dump = Join-Path $OwnerRoot ($discoveryPhase.directory+'/discovery.dump')
        Require-Hybrid (Test-HybridContained $dump $OwnerRoot) 'hybrid-framework-discovery-escape'
        [void](Lock-HybridFile $dump -Retained); $dumpDigest = Get-HybridDigest $dump
        $verifiedCatalogPath = Join-Path $VerificationRoot ($key+'-catalog.json')
        Invoke-HybridTool $Tool @('evidence-catalog','--discovery',$dump,'--output',$verifiedCatalogPath) (Join-Path $VerificationRoot ($key+'-catalog-process')) $RepositoryRoot
        $verifiedCatalog = Read-HybridJson $verifiedCatalogPath
        $allIds = [string[]]@($current.catalog.cases | ForEach-Object canonicalId)
        Assert-HybridIds @($catalog.cases | ForEach-Object canonicalId) $allIds -Ordered
        Assert-HybridIds @($verifiedCatalog.cases | ForEach-Object canonicalId) $allIds -Ordered
        $currentNames = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
        foreach ($case in $current.catalog.cases) {
            foreach ($observedCatalog in @($catalog,$verifiedCatalog)) {
                $other = @($observedCatalog.cases | Where-Object canonicalId -CEQ $case.canonicalId)
                Require-Hybrid ($other.Count -eq 1 -and $other[0].fullName -ceq $case.fullName -and
                    $other[0].className -ceq $case.className -and $other[0].methodName -ceq $case.methodName) 'hybrid-framework-current-fullname'
            }
            $currentNames.Add([string]$case.canonicalId,[string]$case.fullName)
        }
        if ($ids.Count -eq 0) {
            Require-Hybrid ($assembly.executed -eq $false -and $assembly.passedCount -eq 0 -and
                $null -eq $assembly.executionPlan -and $null -eq $assembly.executionPlanSha256 -and @($assembly.executionPhases).Count -eq 0 -and
                -not $phaseNames.Contains('plan-'+$key) -and @($platform.phases | Where-Object { $_.name.StartsWith('execute-'+$key+'-',[StringComparison]::Ordinal) }).Count -eq 0) 'hybrid-framework-empty-assembly-claimed-execution'
            continue
        }
        Require-Hybrid ($assembly.executed -eq $true -and $assembly.passedCount -eq $ids.Count -and $phaseNames.Contains('plan-'+$key)) 'hybrid-framework-assembly-incomplete'
        $requiredPhases.Add('plan-'+$key)
        $paths.executionPlan = Resolve-HybridArtifact $OwnerRoot $assembly.executionPlan $assembly.executionPlanSha256
        $expectedSelectionPath = Join-Path $VerificationRoot ($key+'-requested.json')
        Write-HybridJson $expectedSelectionPath @{ schemaVersion = 1; ids = $ids; maximumConcurrency = [Math]::Max(1,[int]$platform.workers) }
        $expectedPlanPath = Join-Path $VerificationRoot ($key+'-raw-plan.json')
        Invoke-HybridTool $Tool @('evidence-execution-plan','--discovery',$dump,'--selection',$expectedSelectionPath,'--output',$expectedPlanPath) (Join-Path $VerificationRoot ($key+'-raw-plan-process')) $RepositoryRoot
        $currentPlanPath = Join-Path $VerificationRoot ($key+'-current-plan.json')
        Invoke-HybridTool $Tool @('evidence-execution-plan','--discovery',$current.dump,'--selection',$expectedSelectionPath,'--output',$currentPlanPath) (Join-Path $VerificationRoot ($key+'-current-plan-process')) $RepositoryRoot
        # Raw and independently fresh plans are admitted by the same existing
        # policy reader. Adapter platformId is transient; canonical/name/order,
        # complete-profile expansion and resource policy remain exact.
        foreach ($authority in @($expectedPlanPath,$currentPlanPath)) {
            $options = @{ PlanPath = $paths.executionPlan; ExpectedPlanPath = $authority; RawDiscoverySha256 = $dumpDigest
                RequestedIds = $ids; SelectionIds = [string[]]$selection.ids; PhaseRecords = [object[]]$assembly.executionPhases }
            & (Join-Path $RepositoryRoot 'tools/Assert-FrameworkExecutionPlan.ps1') @options
        }
        $passed = [Collections.Generic.List[string]]::new()
        foreach ($group in $assembly.executionPhases) {
            $prefix = $key + '/execution/' + $group.id + '/'
            if ($group.runtimeEvidence -cne ($prefix + 'runtime-evidence')) { throw 'hybrid-framework-phase-directory' }
            $phasePaths = @{}
            foreach ($name in @('selection','request','runSettings','terminal','resultXml','trx','supervisor')) {
                if ($group.$name -isnot [string] -or -not $group.$name.StartsWith($prefix, [StringComparison]::Ordinal)) { throw 'hybrid-framework-phase-directory' }
                $relative = [string]$group.$name; $digest = [string]$group.($name + 'Sha256')
                $phasePaths[$name] = Resolve-HybridArtifact $OwnerRoot $relative $digest
                [void]$relative
            }
            foreach ($name in @('execute','verify')) {
                $requiredPhases.Add($name + '-' + $key + '-' + $group.id)
                if (@($platform.phases | Where-Object name -CEQ ($name + '-' + $key + '-' + $group.id)).Count -ne 1) {
                    throw 'hybrid-framework-missing-execution-phase'
                }
            }
            $phaseSelection = [IO.File]::ReadAllText($phasePaths.selection) | ConvertFrom-Json
            $terminal = [IO.File]::ReadAllText($phasePaths.terminal) | ConvertFrom-Json
            $phaseIds = [string[]]$group.selectedIds
            $phaseRequest = [IO.File]::ReadAllText($phasePaths.request) | ConvertFrom-Json
            if ($phaseRequest.cases -isnot [Array] -or $phaseRequest.cases.Count -ne $phaseIds.Count) {
                throw 'hybrid-framework-phase-request-binding'
            }
            for ($index = 0; $index -lt $phaseIds.Count; $index++) {
                $binding = $phaseRequest.cases[$index]
                if (-not $currentNames.ContainsKey($phaseIds[$index]) -or
                    $binding.canonicalId -isnot [string] -or $binding.fullName -isnot [string] -or
                    -not $binding.canonicalId.Equals($phaseIds[$index], [StringComparison]::Ordinal) -or
                    -not $binding.fullName.Equals($currentNames[$phaseIds[$index]], [StringComparison]::Ordinal)) {
                    throw 'hybrid-framework-phase-request-binding'
                }
            }
            if ($phaseSelection.schemaVersion -ne 1 -or $terminal.schemaVersion -ne 1 -or $terminal.kind -cne 'nunit-terminal' -or
                -not $terminal.complete -or $terminal.passedCount -ne $phaseIds.Count) { throw 'hybrid-framework-invalid-phase-terminal' }
            Assert-HybridIds @($phaseSelection.ids) $phaseIds
            if ((@($phaseSelection.ids) -join "`n") -cne ($phaseIds -join "`n")) { throw 'hybrid-framework-phase-selection-order' }
            Assert-HybridIds @($terminal.cases | ForEach-Object canonicalId) $phaseIds
            & (Join-Path $RepositoryRoot 'tools/Assert-FrameworkSupervisor.ps1') -Path $phasePaths.supervisor -RunId $platform.runId `
                -SourceTree $platform.source.tree -SourceFingerprint $platform.source.fingerprint -AssemblySha256 $assembly.sha256 `
                -CaseIds $phaseIds -ResultXmlSha256 $group.resultXmlSha256 -TrxSha256 $group.trxSha256 `
                -ExecutionRequestPath $phasePaths.request -ExecutionRequestSha256 $group.requestSha256 `
                -ExpectedMaximumConcurrency $group.maximumConcurrency -ExpectedRequireOrder $group.requireOrder
            $verifiedPath = Join-Path $VerificationRoot ('framework-terminal-' + $key + '-' + $group.id + '.json')
            Invoke-HybridTool $Tool @('evidence-verify', '--catalog', $verifiedCatalogPath,
                '--selection', $phasePaths.selection, '--result', $phasePaths.resultXml, '--trx', $phasePaths.trx, '--output', $verifiedPath) (Join-Path $VerificationRoot ('verify-process-' + $key + '-' + $group.id)) $RepositoryRoot
            $verified = [IO.File]::ReadAllText($verifiedPath) | ConvertFrom-Json
            if ($verified.schemaVersion -ne 1 -or $verified.kind -cne 'nunit-terminal' -or -not $verified.complete -or
                $verified.passedCount -ne $phaseIds.Count) { throw 'hybrid-framework-unverified-phase-results' }
            Assert-HybridIds @($verified.cases | ForEach-Object canonicalId) $phaseIds
            $markerRequired = $key -ceq 'plugin' -and @($catalog.cases | Where-Object {
                $_.className -ceq 'NUnitCompatibility.CompatibilityCases' -and $_.canonicalId -cin $phaseIds
            }).Count -gt 0
            if ($group.markerRequired -ne $markerRequired -or ($markerRequired -and $group.markerObserved -ne $true)) {
                throw 'hybrid-framework-execution-marker'
            }
            if ($markerRequired) {
                $markers = @(Get-ChildItem -LiteralPath (Join-Path $OwnerRoot $group.runtimeEvidence) -File -Filter 'fixture-created-*.txt')
                if ($markers.Count -ne 1 -or $markers[0].Name -cnotmatch '^fixture-created-[1-9][0-9]*\.txt$' -or
                    [IO.File]::ReadAllText($markers[0].FullName) -cne 'constructed') { throw 'hybrid-framework-execution-marker' }
                $relative = $group.runtimeEvidence + '/' + $markers[0].Name
                $digest = (Get-FileHash -LiteralPath $markers[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                [void](Resolve-HybridArtifact $OwnerRoot $relative $digest)
                [void]$relative
            }
            foreach ($id in $phaseIds) { $passed.Add($id) }
            [void](Lock-HybridFile $verifiedPath -Retained)
        }
        Assert-HybridIds $passed.ToArray() $ids
        foreach ($id in $passed) { $admitted.Add($id) }
    }
    Assert-HybridIds $admitted.ToArray() $ExpectedIds
    Assert-HybridIds @($phaseNames) $requiredPhases.ToArray()
}

function Assert-HybridProcessProof([string]$ProcessPath,$Process) {
    $directory = [IO.Path]::GetDirectoryName($ProcessPath)
    [void](Lock-HybridFile (Join-Path $directory 'invocation.json') -Retained)
    $invocation = Read-HybridJson (Join-Path $directory 'invocation.json')
    Require-Hybrid ($invocation.schemaVersion -eq 1 -and $invocation.executable -ceq $Process.executable -and
        (Test-HybridSamePath $invocation.workingDirectory $Process.workingDirectory) -and
        $invocation.driverControl.runId -cmatch '^[a-f0-9]{32}$' -and
        $invocation.driverControl.nonce -cmatch '^[a-f0-9]{64}$' -and
        $invocation.driverControl.pipeName -ceq ('fa-fw-'+$invocation.driverControl.runId+'-launcher') -and
        $null -eq $invocation.driverControl.sourceTree) 'hybrid-process-invocation-proof'
    Assert-HybridIds $invocation.arguments ([string[]]$Process.arguments) -Ordered
    $previous = [long]$Process.queuedTimestamp
    foreach ($ordinal in @(0,1)) {
        $path = Join-Path $directory ('launcher-event-'+$ordinal+'.json')
        [void](Lock-HybridFile $path -Retained)
        $event = Read-HybridJson $path
        Require-Hybrid ($event.schemaVersion -eq 1 -and $event.role -ceq 'execution-launcher' -and
            $event.nonce -ceq $invocation.driverControl.nonce -and $event.runId -ceq $invocation.driverControl.runId -and
            $null -eq $event.sourceTree -and $event.pid -eq $Process.pid -and $event.targetPid -eq $Process.targetProcessId -and
            $event.frequency -eq $Process.frequency -and $event.timestamp -ge $previous -and
            $event.timestamp -le $Process.endedTimestamp) 'hybrid-process-launcher-proof'
        if ($ordinal -eq 0) { Require-Hybrid ($event.kind -ceq 'target-started' -and $null -eq $event.exitCode) 'hybrid-process-launcher-proof' }
        else { Require-Hybrid ($event.kind -ceq 'target-exited' -and $event.exitCode -eq $Process.targetExitCode -and
            $event.timestamp -eq $Process.targetExitTimestamp -and $event.timestamp -eq $Process.cleanupAnchorTimestamp) 'hybrid-process-launcher-proof' }
        $previous = [long]$event.timestamp
    }
}

function Read-HybridLegacyInventory([string]$ReportPath,[string]$EvidenceRoot) {
    Require-Hybrid (Test-HybridContained $ReportPath $EvidenceRoot) 'hybrid-generation-report-escape'
    [void](Lock-HybridFile $ReportPath -Retained)
    $canonical = Read-HybridJson $ReportPath
    Require-Hybrid ($canonical.schemaVersion -eq 1 -and $canonical.runId -cmatch '^[a-f0-9]{32}$' -and
        ($canonical.generation -is [int] -or $canonical.generation -is [long]) -and $canonical.generation -ge 0) 'hybrid-generation-canonical-identity'
    $generationRoot = $ReportPath+'.generations'
    $directories = @(Get-ChildItem -LiteralPath $generationRoot -Directory -Force)
    Require-Hybrid ($directories.Count -eq 1 -and $directories[0].Name -ceq $canonical.runId) 'hybrid-generation-owner-set'
    # Assert regular parent chains before enumerating or opening sidecars.
    $initialPath = Join-Path $directories[0].FullName '00000000000000000000.json'
    Assert-HybridRegular $initialPath
    $committed = @(Get-ChildItem -LiteralPath $directories[0].FullName -File -Force -Filter '*.json' | Sort-Object Name -CaseSensitive)
    Require-Hybrid ($committed.Count -gt 0 -and $committed.Count -le 2000000) 'hybrid-generation-empty-or-limit'
    $entries = [Collections.Generic.List[object]]::new()
    $paths = @($ReportPath)+@($committed | ForEach-Object FullName)
    foreach ($path in $paths) {
        [void](Lock-HybridFile $path -Retained)
        $length = ([IO.FileInfo]::new($path)).Length
        Require-Hybrid ($length -gt 0 -and $length -le 16777216) 'hybrid-generation-file-size'
        $entries.Add([pscustomobject]@{ path = [IO.Path]::GetRelativePath($EvidenceRoot,$path).Replace('\','/'); sha256 = Get-HybridDigest $path; length = $length })
    }
    $latest = [long]-1; $projection = $null; $hasInitial = $false
    for ($index = 0; $index -lt $committed.Count; $index++) {
        $file = $committed[$index]
        $number = [long]0
        Require-Hybrid ($file.Name -cmatch '^[0-9]{20}\.json$' -and
            [long]::TryParse($file.BaseName,[Globalization.NumberStyles]::None,[Globalization.CultureInfo]::InvariantCulture,[ref]$number) -and
            $number -ge 0 -and $file.BaseName -ceq $number.ToString('D20',[Globalization.CultureInfo]::InvariantCulture) -and $number -gt $latest) 'hybrid-generation-name'
        $record = Read-HybridJson $file.FullName
        Require-Hybrid ($record.schemaVersion -eq 1 -and $record.runId -ceq $canonical.runId -and $record.generation -eq $number) 'hybrid-generation-record-identity'
        if ($number -eq 0) { $hasInitial = $true }
        if ($number -eq $canonical.generation) { $projection = $entries[$index+1] }
        $latest = $number
    }
    Require-Hybrid ($hasInitial -and $null -ne $projection -and $entries[0].length -eq $projection.length -and
        $entries[0].sha256 -ceq $projection.sha256) 'hybrid-generation-projection-mismatch'
    return [pscustomobject]@{ schemaVersion = 1; kind = 'hybrid-legacy-generation-inventory'
        report = $entries[0].path; generationsDirectory = [IO.Path]::GetRelativePath($EvidenceRoot,$generationRoot).Replace('\','/')
        reportRunId = $canonical.runId; canonicalGeneration = [long]$canonical.generation; reportGeneration = $latest
        entries = $entries.ToArray() }
}
function Assert-HybridLegacyInventory([string]$EvidenceRoot,$Inventory) {
    $fields = @('schemaVersion','kind','report','generationsDirectory','reportRunId','canonicalGeneration','reportGeneration','entries')
    Require-Hybrid ($Inventory.schemaVersion -eq 1 -and $Inventory.kind -ceq 'hybrid-legacy-generation-inventory' -and
        @($Inventory.PSObject.Properties).Count -eq $fields.Count -and
        @($Inventory.PSObject.Properties | Where-Object { $fields -cnotcontains $_.Name }).Count -eq 0 -and
        $Inventory.report -is [string] -and -not [IO.Path]::IsPathRooted($Inventory.report) -and
        $Inventory.reportRunId -cmatch '^[a-f0-9]{32}$' -and
        ($Inventory.canonicalGeneration -is [int] -or $Inventory.canonicalGeneration -is [long]) -and $Inventory.canonicalGeneration -ge 0 -and
        ($Inventory.reportGeneration -is [int] -or $Inventory.reportGeneration -is [long]) -and $Inventory.reportGeneration -ge $Inventory.canonicalGeneration -and
        $Inventory.entries -is [Array] -and $Inventory.entries.Count -gt 1) 'hybrid-generation-inventory-shape'
    $report = [IO.Path]::GetFullPath((Join-Path $EvidenceRoot $Inventory.report))
    Require-Hybrid (Test-HybridContained $report $EvidenceRoot) 'hybrid-generation-report-escape'
    $current = Read-HybridLegacyInventory $report $EvidenceRoot
    foreach ($name in @('report','generationsDirectory','reportRunId','canonicalGeneration','reportGeneration')) {
        Require-Hybrid ($current.$name -ceq $Inventory.$name) 'hybrid-generation-inventory-changed'
    }
    Require-Hybrid ($current.entries.Count -eq $Inventory.entries.Count) 'hybrid-generation-inventory-changed'
    for ($index=0;$index -lt $current.entries.Count;$index++) {
        $expected = $Inventory.entries[$index]; $actual = $current.entries[$index]
        Require-Hybrid (@($expected.PSObject.Properties).Count -eq 3 -and
            @($expected.PSObject.Properties | Where-Object { @('path','sha256','length') -cnotcontains $_.Name }).Count -eq 0 -and
            $expected.path -is [string] -and $expected.sha256 -cmatch '^[a-f0-9]{64}$' -and
            ($expected.length -is [long] -or $expected.length -is [int]) -and $expected.length -gt 0 -and
            $actual.path -ceq $expected.path -and $actual.sha256 -ceq $expected.sha256 -and $actual.length -eq $expected.length) 'hybrid-generation-inventory-changed'
    }
}
function Assert-HybridLegacyExport($Verified,$Inventory) {
    Require-Hybrid ($Verified.reportRunId -ceq $Inventory.reportRunId -and
        ($Verified.reportGeneration -is [long] -or $Verified.reportGeneration -is [int]) -and
        $Verified.reportGeneration -eq $Inventory.reportGeneration) 'hybrid-generation-export-mismatch'
}
function Assert-HybridGenerationReceipt([string]$SessionDirectory,[string]$AdmissionDirectory,$Admission) {
    # Gate must first lock admission.json at its receipt digest. This function
    # rechecks committed generation sets, not just already enumerated bytes.
    [void](Lock-HybridFile (Join-Path $SessionDirectory 'manifest.json') -Retained)
    $session = Read-HybridJson (Join-Path $SessionDirectory 'manifest.json')
    Require-Hybrid ($Admission.schemaVersion -eq 1 -and $Admission.kind -ceq 'hybrid-session-admission' -and
        $Admission.complete -eq $true -and $Admission.baselineClaim -eq $false -and
        $session.schemaVersion -eq 1 -and $session.kind -ceq 'hybrid-test-session' -and
        $session.complete -eq $true -and $session.runId -ceq $Admission.runId) 'hybrid-generation-receipt-session'
    foreach ($slot in $session.slots) {
        [void](Read-HybridLatestAttempt (Join-Path $SessionDirectory ('slots/'+$slot.id)) $slot.attempts)
    }
    $legacy = @($Admission.owners | Where-Object owner -CEQ 'legacy')
    $inventories = @($Admission.generationInventories)
    Require-Hybrid ($inventories.Count -eq $legacy.Count) 'hybrid-generation-receipt-owner-set'
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($item in $inventories) {
        $owner = @($legacy | Where-Object slotId -CEQ $item.slotId)
        Require-Hybrid (@($item.PSObject.Properties).Count -eq 3 -and
            @($item.PSObject.Properties | Where-Object { @('slotId','path','sha256') -cnotcontains $_.Name }).Count -eq 0 -and
            $seen.Add([string]$item.slotId) -and $owner.Count -eq 1) 'hybrid-generation-receipt-owner-set'
        $path = Resolve-HybridArtifact $AdmissionDirectory $item.path $item.sha256
        $inventory = Read-HybridJson $path
        Require-Hybrid ($owner[0].report -ceq $inventory.report) 'hybrid-generation-receipt-report-binding'
        Assert-HybridLegacyExport $owner[0] $inventory
        Assert-HybridLegacyInventory $SessionDirectory $inventory
    }
}
