#requires -Version 7.0
<#
.SYNOPSIS
    Collect one isolated NUnit leaf with the pinned modern dotCover CLI.
.DESCRIPTION
    Requires an existing immutable snapshot and prebuilt coverage Release files.
    Preserves the raw collection before reviewing diagnostics and importing one
    case against this live owner's observations. -CollectOnly stops before that
    admission. Neither mode publishes or claims a complete baseline.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CaseId,
    [Parameter(Mandatory)][string]$TestAssembly,
    [Parameter(Mandatory)][string]$ProductAssembly,
    [Parameter(Mandatory)][string]$DotCoverToolFile,
    [Parameter(Mandatory)][string]$ResultsDirectory,
    [string]$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')),
    [string]$RunId = [Guid]::NewGuid().ToString('N'),
    [switch]$CollectOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false)
$root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RepositoryRoot))
$sessionId = [Guid]::NewGuid().ToString('N')
$runRoot = $null
$stage = 'preflight'
$failureCode = 'coverage-collector-operation-failed'
$failure = $null
$source = $null
$identity = $null
$binaries = @()
$binaryFiles = @()
$artifacts = [Collections.Generic.List[object]]::new()
$phases = [Collections.Generic.List[object]]::new()
$locks = [Collections.Generic.List[IO.FileStream]]::new()
$phaseOrdinal = 0
$startedUtc = [DateTimeOffset]::UtcNow
$collectionComplete = $false
$testingVerified = $false
$finalIdentityVerified = $false
$hostLifecycle = $null
$observedSdkVersion = $null
$observedDotCoverVersion = $null
$toolSha256 = $null
$caseMetadata = $null
$frameworkDirectory = $null
$frameworkRequest = $null
$frameworkRecord = $null
$frameworkEnvelope = $null
$snapshotRelative = 'coverage.dcvr'
$coverLogRelative = 'cover.log'
$coverErrorLog = $null
$nunitRelative = 'nunit.xml'
$trxRelative = 'trx/results.trx'
$shell = (Get-Process -Id $PID).Path

function Require-Coverage([bool]$Condition, [string]$Code) {
    if (-not $Condition) { $script:failureCode = $Code; throw $Code }
}

function Write-NewText([string]$Path, [string]$Text) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $bytes = $utf8.GetBytes($Text); $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
}

function Write-NewJson([string]$Path, $Value) { Write-NewText $Path ($Value | ConvertTo-Json -Depth 40) }
function Get-Digest([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Same-Path([string]$Left, [string]$Right) {
    return [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Left)).Equals(
        [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Right)), [StringComparison]::OrdinalIgnoreCase)
}
function Within-Root([string]$Path, [string]$Parent) {
    return [IO.Path]::GetFullPath($Path).StartsWith(
        [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Parent)) + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)
}
function Require-RegularFile([string]$Path, [string]$Parent) {
    Require-Coverage ([IO.Path]::IsPathFullyQualified($Path) -and (Within-Root $Path $Parent)) 'coverage-collector-file-outside-owner'
    Require-Coverage ([IO.File]::Exists($Path)) 'coverage-collector-file-missing'
    $item = Get-Item -LiteralPath $Path -Force
    Require-Coverage (-not $item.PSIsContainer) 'coverage-collector-input-not-file'
    while ($null -ne $item) {
        Require-Coverage (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'coverage-collector-input-reparse'
        if (Same-Path $item.FullName $Parent) { return }
        $item = $(if ($item -is [IO.DirectoryInfo]) { $item.Parent } else { $item.Directory })
    }
    Require-Coverage $false 'coverage-collector-file-owner-missing'
}

function Initialize-CoverageProcessOwner {
    if ('FishAudio.Testing.Coverage.JobOwner' -as [type]) { return }
    Add-Type -TypeDefinition @'
#nullable enable
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace FishAudio.Testing.Coverage;
public sealed class JobOwner : IDisposable
{
    private readonly SafeFileHandle _job;
    public JobOwner()
    {
        _job = CreateJobObjectW(IntPtr.Zero, null);
        if (_job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var limits = new ExtendedLimits();
        limits.Basic.LimitFlags = 0x00002000;
        if (!SetInformationJobObject(_job, 9, ref limits, Marshal.SizeOf<ExtendedLimits>()))
        {
            var error = Marshal.GetLastWin32Error(); _job.Dispose(); throw new Win32Exception(error);
        }
    }
    public void Assign(IntPtr handle)
    {
        if (!AssignProcessToJobObject(_job, handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public uint ActiveProcesses
    {
        get
        {
            if (!QueryInformationJobObject(_job, 1, out Accounting value, Marshal.SizeOf<Accounting>(), IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return value.ActiveProcesses;
        }
    }
    public void Terminate() { if (!TerminateJobObject(_job, 1)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    public void Dispose() => _job.Dispose();
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters
    {
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    {
        public BasicLimits Basic; public IoCounters Io;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Accounting
    {
        public long TotalUserTime, TotalKernelTime, PeriodUserTime, PeriodKernelTime;
        public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimits value, int size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(SafeFileHandle job, int kind, out Accounting value, int size, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
}
'@
}

function Invoke-CoveragePhase {
    param([string]$Name, [string]$Executable, [string[]]$Arguments, [int]$MetadataTimeoutMilliseconds = 0)
    # The phase includes creation of its fresh invocation and output files.
    # Capturing this after the prologue left empty-file NTFS timestamps just
    # before the envelope due to the clocks' different sub-millisecond precision.
    $phaseStarted = [DateTimeOffset]::UtcNow
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $script:phaseOrdinal++
    $relative = 'phases/{0:D3}-{1}' -f $phaseOrdinal, $Name
    $directory = Join-Path $runRoot $relative
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $invocation = Join-Path $directory 'argv.json'
    Write-NewJson $invocation ([ordered]@{
        schemaVersion = 1; executable = $Executable; arguments = @($Arguments); workingDirectory = $root
        sessionRunId = $sessionId; canonicalCaseId = $CaseId
    })
    $start = [Diagnostics.ProcessStartInfo]::new($shell)
    foreach ($argument in @('-NoLogo', '-NoProfile', '-NonInteractive', '-File',
        (Join-Path $PSScriptRoot 'Invoke-TestProcess.ps1'), '-ArgumentsPath', $invocation)) { $start.ArgumentList.Add($argument) }
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($key in @('GIT_INDEX_FILE','GIT_DIR','GIT_WORK_TREE','GIT_COMMON_DIR','GIT_PREFIX','FISHAUDIO_EXPECTED_TREE')) {
        [void]$start.Environment.Remove($key)
    }
    $start.Environment['DOTNET_CLI_UI_LANGUAGE'] = 'en'
    $start.Environment['VSLANG'] = '1033'
    $start.Environment['FISHAUDIO_COMPAT_PARALLEL_PROOF'] = '0'
    $start.Environment['FISHAUDIO_COMPAT_EVIDENCE_ROOT'] = $runtimeEvidence
    $start.Environment['FISHAUDIO_TEST_REPOSITORY_ROOT'] = $root
    $start.Environment['FISHAUDIO_PWSH_EXECUTABLE'] = $shell
    $start.Environment['FISHAUDIO_DOTNET_EXECUTABLE'] = $dotnet
    foreach ($key in @('FISHAUDIO_COVERAGE_SESSION_ID', 'FISHAUDIO_COVERAGE_RUN_ID',
        'FISHAUDIO_COVERAGE_CASE_ID', 'FISHAUDIO_COVERAGE_SOURCE_TREE', 'FISHAUDIO_COVERAGE_EVIDENCE_ROOT')) {
        [void]$start.Environment.Remove($key)
    }
    if ($null -ne $identity) {
        $start.Environment['FISHAUDIO_COVERAGE_SESSION_ID'] = $sessionId
        $start.Environment['FISHAUDIO_COVERAGE_RUN_ID'] = $RunId
        $start.Environment['FISHAUDIO_COVERAGE_CASE_ID'] = $CaseId
        $start.Environment['FISHAUDIO_COVERAGE_SOURCE_TREE'] = $source.tree
        $start.Environment['FISHAUDIO_COVERAGE_EVIDENCE_ROOT'] = $runtimeEvidence
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $owner = $null; $outStream = $null; $errStream = $null; $stdout = $null; $stderr = $null
    $started = $false; $assigned = $false; $outDrained = $false; $errDrained = $false; $treeExited = $false
    $exitCode = $null; $primary = $null; $primaryCode = $null; $cleanup = $null; $cleanupClock = $null
    try {
        $owner = [FishAudio.Testing.Coverage.JobOwner]::new()
        $outStream = [IO.FileStream]::new((Join-Path $directory 'stdout.log'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read, 65536, $true)
        $errStream = [IO.FileStream]::new((Join-Path $directory 'stderr.log'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read, 65536, $true)
        $started = $process.Start()
        Require-Coverage $started 'coverage-collector-process-start'
        $stdout = $process.StandardOutput.BaseStream.CopyToAsync($outStream)
        $stderr = $process.StandardError.BaseStream.CopyToAsync($errStream)
        $owner.Assign($process.Handle)
        $assigned = $true
        $process.StandardInput.WriteLine('GO')
        $process.StandardInput.Close()
        $exitTask = $process.WaitForExitAsync()
        $warned = $false
        while (-not ($exitTask.IsCompleted -and $stdout.IsCompleted -and $stderr.IsCompleted -and $owner.ActiveProcesses -eq 0)) {
            # Observe a failed transfer immediately: its unread pipe can block the
            # target, so waiting for all drains before surfacing it can deadlock.
            foreach ($task in @($exitTask, $stdout, $stderr)) {
                if ($task.IsFaulted -or $task.IsCanceled) { [void]$task.GetAwaiter().GetResult() }
            }
            if ($exitTask.IsCompleted) {
                [void]$exitTask.GetAwaiter().GetResult()
                $exitCode = $process.ExitCode
                if ($null -eq $cleanupClock) { $cleanupClock = [Diagnostics.Stopwatch]::StartNew() }
                Require-Coverage ($exitCode -eq 0) 'coverage-collector-target-failed'
                Require-Coverage ($cleanupClock.ElapsedMilliseconds -lt 10000) 'coverage-collector-exit-drain-deadline'
            }
            if ($MetadataTimeoutMilliseconds -gt 0) {
                Require-Coverage ($clock.ElapsedMilliseconds -lt $MetadataTimeoutMilliseconds) 'coverage-collector-metadata-deadline'
            }
            if (-not $warned -and $clock.ElapsedMilliseconds -ge 20000) {
                Write-NewText (Join-Path $directory 'slow-warning.txt') 'coverage-collector-same-process-slow'
                $warned = $true
            }
            [Threading.Thread]::Sleep(25)
        }
        if ($MetadataTimeoutMilliseconds -gt 0) {
            Require-Coverage ($clock.ElapsedMilliseconds -lt $MetadataTimeoutMilliseconds) 'coverage-collector-metadata-deadline'
        }
        [void]$exitTask.GetAwaiter().GetResult()
        [void]$stdout.GetAwaiter().GetResult(); $outDrained = $true
        [void]$stderr.GetAwaiter().GetResult(); $errDrained = $true
        $treeExited = $true
        $exitCode = $process.ExitCode
    } catch {
        $primary = $_; $primaryCode = $script:failureCode
        try { Write-NewText (Join-Path $directory 'private-failure.txt') ($_ | Out-String) } catch { }
    } finally {
        # A failed phase stops the collector. Termination and both original
        # drains share one ten-second allowance; there is no second attempt.
        if ($null -ne $primary) {
            if ($null -eq $cleanupClock) { $cleanupClock = [Diagnostics.Stopwatch]::StartNew() }
            try {
                if ($assigned) { $owner.Terminate() }
                elseif ($started -and -not $process.HasExited) { $process.Kill($true) }
                while (($started -and -not $process.HasExited) -or
                    ($null -ne $stdout -and -not $stdout.IsCompleted) -or
                    ($null -ne $stderr -and -not $stderr.IsCompleted) -or ($assigned -and $owner.ActiveProcesses -ne 0)) {
                    Require-Coverage ($cleanupClock.ElapsedMilliseconds -lt 10000) 'coverage-collector-cleanup-deadline'
                    [Threading.Thread]::Sleep(25)
                }
                if ($started -and $process.HasExited) { $exitCode = $process.ExitCode }
                if ($null -ne $stdout) { [void]$stdout.GetAwaiter().GetResult(); $outDrained = $true }
                if ($null -ne $stderr) { [void]$stderr.GetAwaiter().GetResult(); $errDrained = $true }
                $treeExited = $assigned -and $owner.ActiveProcesses -eq 0
            } catch { $cleanup = $_ }
        }
        try {
            if ($null -ne $outStream) { $outStream.Dispose() }
            if ($null -ne $errStream) { $errStream.Dispose() }
            if ($null -ne $cleanup) {
                try { Write-NewText (Join-Path $directory 'private-cleanup-failure.txt') ($cleanup | Out-String) } catch { }
            }
            $record = [ordered]@{
                startedUtc = $phaseStarted; finishedUtc = [DateTimeOffset]::UtcNow
                completed = ($null -eq $primary -and $outDrained -and $errDrained -and $treeExited)
                exitCode = $exitCode; elapsedMilliseconds = $clock.Elapsed.TotalMilliseconds
                stdoutDrained = $outDrained; stderrDrained = $errDrained; outputDrained = ($outDrained -and $errDrained)
                ownedTreeCompleted = $treeExited; ownership = 'windows-job-before-target-go'
                rootProcessId = $(if ($started) { $process.Id } else { $null })
                cleanupFailed = ($null -ne $cleanup); metadataTimeoutMilliseconds = $MetadataTimeoutMilliseconds
                individualChildExitObservation = 'not-collected'
            }
            Write-NewJson (Join-Path $directory 'process.json') $record
            $phases.Add([ordered]@{ name = $Name; directory = $relative; process = $record })
        } catch { if ($null -eq $primary) { $primary = $_; $primaryCode = $script:failureCode } }
        if ($null -ne $owner) { $owner.Dispose() }
        $process.Dispose()
    }
    if ($null -ne $primary) { $script:failureCode = $primaryCode; throw $primary }
    Require-Coverage ($exitCode -eq 0 -and $outDrained -and $errDrained -and $treeExited) 'coverage-collector-phase-failed'
    return $directory
}

function Get-SnapshotIdentity {
    Require-Coverage (-not [string]::IsNullOrWhiteSpace($env:FISHAUDIO_SNAPSHOT_CONTEXT) -and
        [IO.Path]::IsPathFullyQualified($env:FISHAUDIO_SNAPSHOT_CONTEXT)) 'coverage-collector-snapshot-context-required'
    $context = [IO.File]::ReadAllText($env:FISHAUDIO_SNAPSHOT_CONTEXT) | ConvertFrom-Json
    Require-Coverage ($context.schemaVersion -eq 1 -and (Same-Path $context.root $root) -and
        $context.tree -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$' -and
        $context.sourceCommit -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$' -and @($context.files).Count -gt 0) 'coverage-collector-snapshot-identity'
    $entries = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $context.files) {
        Require-Coverage ($file.path -is [string] -and -not [IO.Path]::IsPathRooted($file.path) -and
            -not $file.path.Contains('\') -and -not $file.path.Contains(':') -and @($file.path.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -eq 0 -and
            $file.sha256 -cmatch '^[a-f0-9]{64}$' -and $entries.TryAdd($file.path, $file)) 'coverage-collector-snapshot-file-identity'
    }
    foreach ($required in @('global.json', '.config/dotnet-tools.json', 'tools/Invoke-IsolatedCoverage.ps1', 'tools/Invoke-TestProcess.ps1')) {
        Require-Coverage ($entries.ContainsKey($required)) 'coverage-collector-snapshot-input-missing'
    }
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        [string[]]$paths = @($entries.Keys)
        [Array]::Sort($paths, [StringComparer]::Ordinal)
        foreach ($path in $paths) {
            $full = [IO.Path]::GetFullPath((Join-Path $root $path))
            Require-RegularFile $full $root
            $bytes = [IO.File]::ReadAllBytes($full)
            Require-Coverage ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() -ceq $entries[$path].sha256) 'coverage-collector-snapshot-file-changed'
            $name = $utf8.GetBytes($path)
            $hash.AppendData([BitConverter]::GetBytes([long]$name.Length)); $hash.AppendData($name)
            $hash.AppendData([BitConverter]::GetBytes([long]$bytes.Length)); $hash.AppendData($bytes)
        }
        return [ordered]@{
            commit = $context.sourceCommit; tree = $context.tree
            fingerprint = [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
            fingerprintKind = 'sha256-source-path-and-bytes-ordinal-v1'
            contextSha256 = Get-Digest $env:FISHAUDIO_SNAPSHOT_CONTEXT; snapshotRoot = $root
        }
    } finally { $hash.Dispose() }
}

function Write-CoverageSettings([string]$Path, [string]$Output, [string]$FullName = '') {
    $settings = [Xml.XmlWriterSettings]::new(); $settings.Encoding = $utf8; $settings.Indent = $true
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $writer = [Xml.XmlWriter]::Create($stream, $settings)
    try {
        $writer.WriteStartDocument(); $writer.WriteStartElement('RunSettings'); $writer.WriteStartElement('RunConfiguration')
        $writer.WriteElementString('MaxCpuCount', '1'); $writer.WriteElementString('TreatNoTestsAsError', 'true'); $writer.WriteEndElement()
        $writer.WriteStartElement('NUnit')
        foreach ($pair in @(@('NumberOfTestWorkers','0'), @('PreFilter','false'), @('UseParentFQNForParametrizedTests','false'),
            @('DiscoveryMethod','Current'), @('DumpXmlTestDiscovery','true'), @('DumpXmlTestResults','true'),
            @('IncludeStackTrace','false'), @('IncludeStackTraceForSuites','false'),
            @('TestOutputXml',$Output), @('OutputXmlFolderMode','AsSpecified'))) { $writer.WriteElementString($pair[0], $pair[1]) }
        if ($FullName.Length -gt 0) {
            $regex = '\A' + [Text.RegularExpressions.Regex]::Escape($FullName) + '\z'
            $literal = $regex.Replace('\', '\\').Replace('"', '\"')
            $writer.WriteElementString('Where', 'test =~ "' + $literal + '"')
        }
        $writer.WriteEndElement(); $writer.WriteEndElement(); $writer.WriteEndDocument()
    } finally { $writer.Dispose(); $stream.Dispose() }
}

function Add-Artifact([string]$Kind, [string]$RelativePath) {
    $path = Join-Path $runRoot $RelativePath
    if (-not [IO.File]::Exists($path)) { return }
    Require-RegularFile $path $runRoot
    $item = Get-Item -LiteralPath $path
    $artifacts.Add([ordered]@{ kind = $Kind; relativePath = $RelativePath; sha256 = Get-Digest $path
        length = $item.Length; lastWriteUtc = [DateTimeOffset]::new($item.LastWriteTimeUtc) })
}

function Seal-FrameworkFile([string]$Path) {
    Require-RegularFile $Path $runRoot
    $locks.Add([IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read))
    $file = Get-Item -LiteralPath $Path
    return [ordered]@{ relativePath = [IO.Path]::GetRelativePath($runRoot, $Path).Replace('\','/')
        sha256 = Get-Digest $Path; length = $file.Length; lastWriteUtc = [DateTimeOffset]::new($file.LastWriteTimeUtc) }
}

try {
    Require-Coverage $IsWindows 'coverage-collector-windows-required'
    $parsedRun = [Guid]::Empty
    Require-Coverage ([Guid]::TryParseExact($RunId, 'N', [ref]$parsedRun) -and $parsedRun -ne [Guid]::Empty -and
        -not [string]::IsNullOrWhiteSpace($CaseId) -and $CaseId.Length -le 16384 -and -not @($CaseId.ToCharArray() | Where-Object { [char]::IsControl($_) }).Count) 'coverage-collector-input-identity'
    Require-Coverage (Same-Path $PSScriptRoot (Join-Path $root 'tools')) 'coverage-collector-entry-root-mismatch'
    foreach ($path in @($TestAssembly, $ProductAssembly, $DotCoverToolFile, $ResultsDirectory)) {
        Require-Coverage ([IO.Path]::IsPathFullyQualified($path)) 'coverage-collector-absolute-path-required'
    }
    $TestAssembly = [IO.Path]::GetFullPath($TestAssembly); $ProductAssembly = [IO.Path]::GetFullPath($ProductAssembly)
    $DotCoverToolFile = [IO.Path]::GetFullPath($DotCoverToolFile)
    $candidate = [IO.Path]::GetFullPath($ResultsDirectory)
    Require-Coverage (-not [IO.File]::Exists($candidate) -and -not [IO.Directory]::Exists($candidate) -and
        -not (Within-Root $candidate $root) -and -not (Same-Path $candidate $root)) 'coverage-collector-fresh-external-results-required'
    [IO.Directory]::CreateDirectory($candidate) | Out-Null
    $runRoot = $candidate
    Write-NewJson (Join-Path $runRoot 'owner.json') @{ schemaVersion = 1; runId = $RunId; sessionId = $sessionId }
    foreach ($name in @('phases','runtime-evidence','discovery-xml','result-xml','trx')) { [IO.Directory]::CreateDirectory((Join-Path $runRoot $name)) | Out-Null }
    $runtimeEvidence = Join-Path $runRoot 'runtime-evidence'
    foreach ($name in @('COR_ENABLE_PROFILING','CORECLR_ENABLE_PROFILING')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        Require-Coverage ([string]::IsNullOrEmpty($value) -or $value -eq '0') 'coverage-collector-inherited-profiler'
    }
    Initialize-CoverageProcessOwner
    $stage = 'snapshot-identity'
    $source = Get-SnapshotIdentity
    Write-NewJson (Join-Path $runRoot 'source.json') $source
    $sdkManifest = [IO.File]::ReadAllText((Join-Path $root 'global.json')) | ConvertFrom-Json
    $toolsManifest = [IO.File]::ReadAllText((Join-Path $root '.config/dotnet-tools.json')) | ConvertFrom-Json
    Require-Coverage ($sdkManifest.sdk.version -ceq '10.0.403' -and $sdkManifest.sdk.rollForward -ceq 'disable' -and
        $toolsManifest.tools.'jetbrains.dotcover.commandlinetools'.version -ceq '2026.2.3.1') 'coverage-collector-toolchain-pin-mismatch'
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $vstest = Join-Path ([IO.Path]::GetDirectoryName($dotnet)) 'sdk/10.0.403/vstest.console.dll'
    Require-Coverage ([IO.File]::Exists($vstest) -and [IO.File]::Exists($DotCoverToolFile) -and
        [IO.Path]::GetFileName($DotCoverToolFile).Equals('dotCover.dll', [StringComparison]::OrdinalIgnoreCase)) 'coverage-collector-fixed-tool-missing'
    $metadataTool = Join-Path $root 'tools/FishAudio.Testing/bin/Release/net10.0-windows/FishAudio.Testing.dll'
    $stage = 'binary-input'
    Require-RegularFile $metadataTool $root
    foreach ($dll in @($TestAssembly, $ProductAssembly)) {
        Require-RegularFile $dll $root
        Require-RegularFile ([IO.Path]::ChangeExtension($dll, '.pdb')) $root
    }
    Require-Coverage ($TestAssembly.Replace('\','/').Contains('/bin/coverage/Release/net10.0-windows/')) 'coverage-collector-coverage-release-required'
    $productInProjectOutput = $ProductAssembly.Replace('\','/').Contains('/bin/coverage/Release/net10.0-windows/')
    $productInPluginOutput = Same-Path ([IO.Path]::GetDirectoryName($ProductAssembly)) (Join-Path $root '.artifacts/coverage/Release')
    Require-Coverage ($productInProjectOutput -or $productInPluginOutput) 'coverage-collector-coverage-release-required'
    # NUnit's raw discovery dump lives beside the test DLL. Serial ownership of
    # that coverage output prevents another collector from replacing the dump.
    $locks.Add([IO.File]::Open((Join-Path ([IO.Path]::GetDirectoryName($TestAssembly)) 'fish-audio-isolated-coverage.lock'),
        [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None))
    foreach ($suffix in @('.deps.json', '.runtimeconfig.json')) {
        Require-RegularFile (Join-Path ([IO.Path]::GetDirectoryName($TestAssembly)) ([IO.Path]::GetFileNameWithoutExtension($TestAssembly) + $suffix)) $root
    }
    $loadedProduct = Join-Path ([IO.Path]::GetDirectoryName($TestAssembly)) ([IO.Path]::GetFileName($ProductAssembly))
    if ([IO.File]::Exists($loadedProduct)) {
        Require-RegularFile $loadedProduct $root
        Require-RegularFile ([IO.Path]::ChangeExtension($loadedProduct, '.pdb')) $root
        Require-Coverage ((Get-Digest $loadedProduct) -ceq (Get-Digest $ProductAssembly) -and
            (Get-Digest ([IO.Path]::ChangeExtension($loadedProduct, '.pdb'))) -ceq (Get-Digest ([IO.Path]::ChangeExtension($ProductAssembly, '.pdb')))) 'coverage-collector-test-product-copy-mismatch'
    }
    # Tool/repository cases may never load the product. Absence of this copy is
    # retained as an observation; empty-product admission belongs to the importer.
    $tracked = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($directory in @([IO.Path]::GetDirectoryName($TestAssembly), [IO.Path]::GetDirectoryName($ProductAssembly), [IO.Path]::GetDirectoryName($metadataTool))) {
        foreach ($file in Get-ChildItem -LiteralPath $directory -File) {
            if ($file.Extension -cin @('.dll','.pdb') -or $file.Name.EndsWith('.deps.json', [StringComparison]::OrdinalIgnoreCase) -or
                $file.Name.EndsWith('.runtimeconfig.json', [StringComparison]::OrdinalIgnoreCase)) { [void]$tracked.Add($file.FullName) }
        }
    }
    foreach ($path in @($DotCoverToolFile, $dotnet, $vstest, (Join-Path $PSScriptRoot 'Invoke-TestProcess.ps1'))) { [void]$tracked.Add($path) }
    $binaryFiles = @($tracked | Sort-Object -CaseSensitive | ForEach-Object {
        $locks.Add([IO.File]::Open($_, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read))
        [ordered]@{ path = $_; sha256 = Get-Digest $_ }
    })
    $toolSha256 = @($binaryFiles | Where-Object path -CEQ $DotCoverToolFile)[0].sha256
    Write-NewJson (Join-Path $runRoot 'binary-files.json') $binaryFiles
    $stage = 'tool-versions'
    $sdkPhase = Invoke-CoveragePhase 'sdk-version' $dotnet @('--version') 120000
    $observedSdkVersion = [IO.File]::ReadAllText((Join-Path $sdkPhase 'stdout.log'), $utf8).Trim()
    Require-Coverage ($observedSdkVersion -ceq '10.0.403') 'coverage-collector-sdk-version-mismatch'
    $toolVersionPath = Join-Path $runRoot 'dotcover-version.txt'
    [void](Invoke-CoveragePhase 'dotcover-version' $dotnet @($DotCoverToolFile, 'version', '--text-output', $toolVersionPath) 120000)
    $observedDotCoverVersion = [IO.File]::ReadAllText($toolVersionPath, $utf8).Trim()
    Require-Coverage ($observedDotCoverVersion -ceq '2026.2.3.1') 'coverage-collector-dotcover-version-mismatch'
    $stage = 'binary-identity'
    foreach ($pair in @(@('Product',$ProductAssembly), @('Tests',$TestAssembly))) {
        $output = Join-Path $runRoot ($pair[0].ToLowerInvariant() + '-binary.json')
        [void](Invoke-CoveragePhase ('binary-' + $pair[0].ToLowerInvariant()) $dotnet @($metadataTool, 'evidence-binary', '--assembly', $pair[1], '--pdb', [IO.Path]::ChangeExtension($pair[1], '.pdb'), '--output', $output) 120000)
        $record = [IO.File]::ReadAllText($output) | ConvertFrom-Json
        Require-Coverage ($record.complete -eq $true -and $record.kind -ceq 'binary-identity') 'coverage-collector-binary-identity-rejected'
        $binaries += [ordered]@{ role = $pair[0]; dllRelativePath = [IO.Path]::GetRelativePath($root, $pair[1]).Replace('\','/')
            pdbRelativePath = [IO.Path]::GetRelativePath($root, [IO.Path]::ChangeExtension($pair[1], '.pdb')).Replace('\','/'); identity = $record.binary }
    }
    $buildBytes = $utf8.GetBytes((@($source.fingerprint, $binaries) | ConvertTo-Json -Depth 12 -Compress))
    $identity = [ordered]@{ runId = $RunId; sessionId = $sessionId; canonicalId = $CaseId
        source = @{ commit = $source.commit; tree = $source.tree; fingerprint = $source.fingerprint }
        buildId = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($buildBytes)).ToLowerInvariant() }
    $stage = 'metadata-discovery'
    $discoverySettings = Join-Path $runRoot 'discovery.runsettings'
    Write-CoverageSettings $discoverySettings (Join-Path $runRoot 'discovery-xml')
    $dump = Join-Path ([IO.Path]::GetDirectoryName($TestAssembly)) ('Dump/D_' + [IO.Path]::GetFileName($TestAssembly) + '.dump')
    if ([IO.File]::Exists($dump)) { [IO.File]::Move($dump, (Join-Path $runRoot 'preexisting-discovery.dump')) }
    [void](Invoke-CoveragePhase 'discovery' $dotnet @('exec', $vstest, $TestAssembly, '--ListTests', ('--Settings:' + $discoverySettings)) 120000)
    Require-Coverage ([IO.File]::Exists($dump) -and @(Get-ChildItem -LiteralPath $runtimeEvidence -File -Filter 'fixture-created-*.txt').Count -eq 0 -and
        @(Get-ChildItem -LiteralPath $runtimeEvidence -File -Filter 'host-*.json').Count -eq 0) 'coverage-collector-discovery-evidence-missing-or-constructed'
    [IO.File]::Copy($dump, (Join-Path $runRoot 'discovery.dump'), $false)
    $catalogPath = Join-Path $runRoot 'catalog.json'
    [void](Invoke-CoveragePhase 'catalog' $dotnet @($metadataTool, 'evidence-catalog', '--discovery', (Join-Path $runRoot 'discovery.dump'), '--output', $catalogPath) 120000)
    $catalog = [IO.File]::ReadAllText($catalogPath) | ConvertFrom-Json
    Require-Coverage ($catalog.schemaVersion -eq 1 -and $catalog.kind -ceq 'nunit-catalog' -and $catalog.complete -eq $true) 'coverage-collector-catalog-rejected'
    $selected = @($catalog.cases | Where-Object canonicalId -CEQ $CaseId)
    Require-Coverage ($selected.Count -eq 1) 'coverage-collector-case-not-unique'
    $metadataRequest = Join-Path $runRoot 'case-metadata-request.json'
    Write-NewJson $metadataRequest @{ schemaVersion = 1; discovery = (Join-Path $runRoot 'discovery.dump'); canonicalId = $CaseId }
    $metadataOutput = Join-Path $runRoot 'case-metadata.json'
    [void](Invoke-CoveragePhase 'case-metadata' $dotnet @($metadataTool, 'coverage-case-metadata', '--input', $metadataRequest, '--output', $metadataOutput) 120000)
    $caseMetadata = ([IO.File]::ReadAllText($metadataOutput) | ConvertFrom-Json -AsHashtable).value
    Require-Coverage ($caseMetadata.canonicalId -ceq $CaseId) 'coverage-collector-case-metadata-mismatch'
    $selection = Join-Path $runRoot 'selection.json'
    Write-NewJson $selection @{ schemaVersion = 1; ids = @($CaseId) }
    $executionSettings = Join-Path $runRoot 'execution.runsettings'
    $frameworkDirectory = Join-Path $runRoot 'framework-cover'
    $nunitRelative = 'framework-cover/result-xml/' + [IO.Path]::GetFileNameWithoutExtension($TestAssembly) + '.xml'
    $trxRelative = 'framework-cover/trx/results.trx'
    $snapshotRelative = 'framework-cover/coverage.dcvr'
    $coverLogRelative = 'framework-cover/cover.log'
    Write-CoverageSettings $executionSettings (Join-Path $frameworkDirectory 'result-xml') $selected[0].fullName
    $stage = 'cover'
    $snapshotPath = Join-Path $runRoot $snapshotRelative
    $coverLog = Join-Path $runRoot $coverLogRelative
    $coverErrorLog = Join-Path $frameworkDirectory 'cover.err.log'
    $frameworkRequest = Join-Path $runRoot 'framework-cover-request.json'
    Write-NewJson $frameworkRequest ([ordered]@{
        schemaVersion = 1; runId = $RunId; sourceTree = $source.tree; sourceFingerprint = $source.fingerprint
        workingDirectory = $root; shellExecutable = $shell; dotnetExecutable = $dotnet
        launcherScript = Join-Path $PSScriptRoot 'Invoke-TestProcess.ps1'
        testAssembly = $TestAssembly; testAssemblySha256 = Get-Digest $TestAssembly
        runSettings = $executionSettings; discoveryPath = Join-Path $runRoot 'discovery.dump'
        resultXmlPath = Join-Path $runRoot $nunitRelative; trxPath = Join-Path $runRoot $trxRelative
        outputDirectory = $frameworkDirectory; cases = @(@{ canonicalId = $CaseId; fullName = $selected[0].fullName })
        maximumConcurrency = 1; requireOrder = $true
        environment = @{
            DOTNET_CLI_UI_LANGUAGE = 'en'; VSLANG = '1033'; FISHAUDIO_COMPAT_PARALLEL_PROOF = '0'
            FISHAUDIO_COMPAT_EVIDENCE_ROOT = $runtimeEvidence; FISHAUDIO_TEST_REPOSITORY_ROOT = $root
            FISHAUDIO_PWSH_EXECUTABLE = $shell; FISHAUDIO_DOTNET_EXECUTABLE = $dotnet
            FISHAUDIO_COVERAGE_SESSION_ID = $sessionId; FISHAUDIO_COVERAGE_RUN_ID = $RunId
            FISHAUDIO_COVERAGE_CASE_ID = $CaseId; FISHAUDIO_COVERAGE_SOURCE_TREE = $source.tree
            FISHAUDIO_COVERAGE_EVIDENCE_ROOT = $runtimeEvidence
        }
        profiledLaunch = @{ sessionId = $sessionId; toolAssembly = $DotCoverToolFile; toolSha256 = $toolSha256
            vstestConsoleAssembly = $vstest; vstestConsoleSha256 = Get-Digest $vstest
            snapshotPath = $snapshotPath; logPath = $coverLog }
    })
    [void](Invoke-CoveragePhase 'cover' $dotnet @($metadataTool, 'framework-run', '--request', $frameworkRequest))
    $stage = 'framework-supervision-verification'
    $nunitPath = Join-Path $runRoot $nunitRelative
    $trxPath = Join-Path $runRoot $trxRelative
    & (Join-Path $PSScriptRoot 'Assert-FrameworkSupervisor.ps1') -Path (Join-Path $frameworkDirectory 'supervisor.json') `
        -RunId $RunId -SourceTree $source.tree -SourceFingerprint $source.fingerprint `
        -AssemblySha256 (Get-Digest $TestAssembly) -CaseIds @($CaseId) `
        -ResultXmlSha256 (Get-Digest $nunitPath) -TrxSha256 (Get-Digest $trxPath)
    $frameworkRecord = [IO.File]::ReadAllText((Join-Path $frameworkDirectory 'supervisor.json')) | ConvertFrom-Json
    Require-Coverage ($frameworkRecord.launch.kind -ceq 'dotcover-sdk-vstest' -and
        $frameworkRecord.launch.sessionId -ceq $sessionId -and
        $frameworkRecord.launch.invocationSha256 -ceq (Get-Digest (Join-Path $frameworkDirectory 'invocation.json')) -and
        $frameworkRecord.launch.toolSha256 -ceq $toolSha256 -and
        $frameworkRecord.launch.vstestConsoleSha256 -ceq (Get-Digest $vstest)) 'coverage-collector-framework-launch-mismatch'
    $stage = 'test-verification'
    $xml = @(Get-ChildItem -LiteralPath (Join-Path $frameworkDirectory 'result-xml') -File -Filter '*.xml')
    Require-Coverage ($xml.Count -eq 1 -and [IO.File]::Exists($snapshotPath) -and [IO.File]::Exists($coverLog)) 'coverage-collector-cover-output-missing'
    Require-Coverage (Same-Path $xml[0].FullName $nunitPath) 'coverage-collector-result-path-mismatch'
    [void](Invoke-CoveragePhase 'test-verification' $dotnet @($metadataTool, 'evidence-verify', '--catalog', $catalogPath,
        '--selection', $selection, '--result', $nunitPath, '--trx', $trxPath, '--output', (Join-Path $runRoot 'terminal.json')) 120000)
    $terminal = [IO.File]::ReadAllText((Join-Path $runRoot 'terminal.json')) | ConvertFrom-Json
    Require-Coverage ($terminal.complete -eq $true -and $terminal.passedCount -eq 1 -and @($terminal.cases).Count -eq 1 -and $terminal.cases[0].canonicalId -ceq $CaseId) 'coverage-collector-test-result-not-one-passed-leaf'
    $testingVerified = $true
    $stage = 'host-lifecycle-verification'
    $hostStart = @(Get-ChildItem -LiteralPath $runtimeEvidence -File -Filter 'host-*.start.json')
    $hostEnd = @(Get-ChildItem -LiteralPath $runtimeEvidence -File -Filter 'host-*.end.json')
    Require-Coverage ($hostStart.Count -eq 1 -and $hostEnd.Count -eq 1) 'coverage-collector-host-pair-not-unique'
    $coverPhase = @($phases | Where-Object name -CEQ 'cover')
    Require-Coverage ($coverPhase.Count -eq 1) 'coverage-collector-cover-phase-missing'
    $hostRequest = Join-Path $runRoot 'host-review-request.json'
    Write-NewJson $hostRequest ([ordered]@{
        schemaVersion = 1; start = $hostStart[0].FullName; end = $hostEnd[0].FullName
        expectation = [ordered]@{
            schemaVersion = 1; runId = $RunId; sessionId = $sessionId; canonicalId = $CaseId; sourceTree = $source.tree
            testAssembly = $TestAssembly; testAssemblySha256 = ($binaries | Where-Object role -CEQ 'Tests').identity.dllSha256
            coverStartedUtc = $coverPhase[0].process.startedUtc; coverFinishedUtc = $coverPhase[0].process.finishedUtc
        }
    })
    $hostOutput = Join-Path $runRoot 'host-lifecycle.json'
    [void](Invoke-CoveragePhase 'host-lifecycle-verification' $dotnet @($metadataTool, 'coverage-host-envelope',
        '--input', $hostRequest, '--output', $hostOutput) 120000)
    $hostDocument = [IO.File]::ReadAllText($hostOutput) | ConvertFrom-Json
    Require-Coverage ($hostDocument.complete -eq $true -and $hostDocument.kind -ceq 'coverage-host-envelope' -and
        $hostDocument.value.processId -gt 0) 'coverage-collector-host-evidence-rejected'
    $hostLifecycle = $hostDocument.value
    $frameworkHosts = @($frameworkRecord.events | Where-Object role -CEQ 'nunit-host' | ForEach-Object pid | Sort-Object -Unique)
    Require-Coverage ($frameworkHosts.Count -eq 1 -and $frameworkHosts[0] -eq $hostLifecycle.processId) 'coverage-collector-framework-host-mismatch'
    $coverPhaseRoot = Join-Path $runRoot $coverPhase[0].directory
    $frameworkEnvelope = [ordered]@{
        schemaVersion = 1
        driverInvocation = Seal-FrameworkFile (Join-Path $coverPhaseRoot 'argv.json')
        driverStdout = Seal-FrameworkFile (Join-Path $coverPhaseRoot 'stdout.log')
        driverStderr = Seal-FrameworkFile (Join-Path $coverPhaseRoot 'stderr.log')
        executionRequest = Seal-FrameworkFile $frameworkRequest
        runSettings = Seal-FrameworkFile $executionSettings
        controlContext = Seal-FrameworkFile (Join-Path $frameworkDirectory 'control-context.json')
        supervisor = Seal-FrameworkFile (Join-Path $frameworkDirectory 'supervisor.json')
        hostStart = Seal-FrameworkFile $hostStart[0].FullName
        hostEnd = Seal-FrameworkFile $hostEnd[0].FullName
        driverAssemblyRelativePath = [IO.Path]::GetRelativePath($root, $metadataTool).Replace('\','/')
        driverAssemblySha256 = Get-Digest $metadataTool
    }
    $stage = 'export'
    [void](Invoke-CoveragePhase 'export' $dotnet @($DotCoverToolFile, 'report', '--snapshot-source', $snapshotPath,
        '--json-report-output', (Join-Path $runRoot 'coverage.json'), '--xml-report-output', (Join-Path $runRoot 'coverage.xml'), '--log-file', (Join-Path $runRoot 'export.log')) 120000)
    foreach ($name in @('coverage.json','coverage.xml','export.log')) {
        Require-Coverage ([IO.File]::Exists((Join-Path $runRoot $name)) -and (Get-Item -LiteralPath (Join-Path $runRoot $name)).Length -gt 0) 'coverage-collector-export-output-missing'
    }
    $stage = 'final-identity'
    $finalSource = Get-SnapshotIdentity
    Require-Coverage (($source | ConvertTo-Json -Compress) -ceq ($finalSource | ConvertTo-Json -Compress)) 'coverage-collector-source-changed'
    foreach ($file in $binaryFiles) { Require-Coverage ((Get-Digest $file.path) -ceq $file.sha256) 'coverage-collector-binary-changed' }
    $finalIdentityVerified = $true
    $collectionComplete = $true
} catch {
    $failure = $_
    if ($runRoot) { try { Write-NewText (Join-Path $runRoot 'private-failure.txt') ($_ | Out-String) } catch { } }
} finally {
    # Every raw output, including failed phases, remains owned by the caller.
    # No cleanup removes the snapshot, reports, logs or fixture evidence here.
    if ($runRoot) {
        try {
            foreach ($pair in @(@('Discovery','discovery.dump'), @('NUnitXml',$nunitRelative), @('Trx',$trxRelative),
                @('Snapshot',$snapshotRelative), @('AggregateJson','coverage.json'), @('CoverLog',$coverLogRelative), @('ExportLog','export.log'))) { Add-Artifact $pair[0] $pair[1] }
            foreach ($phaseName in @('cover','export')) {
                $phase = @($phases | Where-Object name -CEQ $phaseName)
                if ($phase.Count -eq 1) {
                    $prefix = $(if ($phaseName -ceq 'cover') { 'Cover' } else { 'Export' })
                    foreach ($pair in @(@('Invocation','argv.json'), @('Process','process.json'), @('Stdout','stdout.log'), @('Stderr','stderr.log'))) {
                        if ($phaseName -ceq 'cover' -and $pair[0] -cne 'Process') {
                            $file = switch -CaseSensitive ($pair[0]) {
                                'Invocation' { 'invocation.json' }
                                'Stdout' { 'stdout.bin' }
                                'Stderr' { 'stderr.bin' }
                            }
                            Add-Artifact ($prefix + $pair[0]) ('framework-cover/' + $file)
                        } else { Add-Artifact ($prefix + $pair[0]) ($phase[0].directory + '/' + $pair[1]) }
                    }
                }
            }
            if ($collectionComplete -and $artifacts.Count -ne 15) { $collectionComplete = $false; $failureCode = 'coverage-collector-artifact-set-incomplete' }
            $status = @{}
            foreach ($name in @('cover','export','test-verification','host-lifecycle-verification')) {
                $phase = @($phases | Where-Object name -CEQ $name)
                $status[$name] = $(if ($phase.Count -eq 0) { 'not-started' } elseif ($phase[0].process.completed -and $phase[0].process.exitCode -eq 0) { 'completed' } else { 'failed' })
            }
            Write-NewJson (Join-Path $runRoot 'collector.json') ([ordered]@{
                schemaVersion = 1; kind = 'isolated-coverage-collection'; identity = $identity; artifactDirectory = $runRoot
                collectionComplete = $collectionComplete; testingVerified = $testingVerified; finalIdentityVerified = $finalIdentityVerified
                baselineClaim = $false; validCaseCoverage = $false; admissionPending = $true
                diagnostics = 'Unreviewed'; diagnosticReviewId = $null; sourceMappingsResolved = $false
                startedUtc = $startedUtc; sealedUtc = [DateTimeOffset]::UtcNow; source = $source; binaries = $binaries
                tool = @{ requiredVersion = '2026.2.3.1'; version = $observedDotCoverVersion; file = $DotCoverToolFile; sha256 = $toolSha256 }
                sdk = @{ requiredVersion = '10.0.403'; version = $observedSdkVersion }
                hostLifecycle = $hostLifecycle; frameworkEnvelope = $frameworkEnvelope
                statuses = $status; phases = $phases.ToArray(); artifacts = $artifacts.ToArray(); emptyProduct = $null
                failure = $(if ($null -ne $failure -or -not $collectionComplete) { @{ stage = $stage; reason = $failureCode; exceptionType = $(if ($failure) { $failure.Exception.GetType().Name } else { $null }) } } else { $null })
                supervisorBoundary = 'job-tree-and-original-pipes-observed; individual-child-exit-records-not-collected'
            })
        } catch {
            $collectionComplete = $false
            if ($null -eq $failure) { $failure = $_ }
        }
    }
    foreach ($fileLock in $locks) { $fileLock.Dispose() }
}
if (-not $collectionComplete) {
    [Console]::Error.WriteLine('coverage-collector: failed stage=' + $stage + ' reason=' + $failureCode + '; inspect retained private evidence.')
    exit 1
}
if ($CollectOnly) {
    Write-Output ('coverage-collector: collected session ' + $sessionId + '; diagnostics and admission remain pending.')
    exit 0
}

# Admission receives the facts held by this live owner, rather than treating a
# session's self-authored collector.json as an independent supervisor receipt.
# collector.json and every raw byte remain unchanged if review/import fails.
$admitted = $false
$admissionFailure = $null
$diagnosticRules = @()
$ownedProcesses = @()
$childReceipt = $null
$admissionLocks = [Collections.Generic.List[IO.FileStream]]::new()
$reviewInputs = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
$coverReview = $null
$exportReview = $null
$consoleReview = $null
function Lock-AdmissionInput([string]$Path, [string]$ExpectedSha256) {
    $full = [IO.Path]::GetFullPath($Path)
    if ($reviewInputs.ContainsKey($full)) {
        Require-Coverage ($reviewInputs[$full] -ceq $ExpectedSha256) 'coverage-admission-conflicting-input'
        return
    }
    $admissionLocks.Add([IO.File]::Open($full, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read))
    Require-Coverage ((Get-Digest $full) -ceq $ExpectedSha256) 'coverage-admission-input-changed'
    $reviewInputs.Add($full, $ExpectedSha256)
}
function Assert-ReviewedInputs($Document, [string]$Kind, [string[]]$ExpectedPaths) {
    Require-Coverage ($Document.complete -eq $true -and $Document.kind -ceq $Kind -and
        @($Document.value.evidence).Count -eq $ExpectedPaths.Count) 'coverage-admission-review-set-mismatch'
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($item in $Document.value.evidence) {
        Require-Coverage ([IO.Path]::IsPathFullyQualified($item.path)) 'coverage-admission-review-path-invalid'
        $path = [IO.Path]::GetFullPath($item.path)
        Require-Coverage ($seen.Add($path) -and $reviewInputs.ContainsKey($path) -and
            $reviewInputs[$path] -ceq $item.sha256 -and (Get-Digest $path) -ceq $item.sha256 -and
            @($ExpectedPaths | Where-Object { Same-Path $_ $path }).Count -eq 1) 'coverage-admission-review-digest-mismatch'
    }
}
try {
    $stage = 'admission-identity'
    $admissionSource = Get-SnapshotIdentity
    Require-Coverage (($source | ConvertTo-Json -Compress) -ceq ($admissionSource | ConvertTo-Json -Compress)) 'coverage-admission-source-changed'
    foreach ($file in $binaryFiles) { Lock-AdmissionInput $file.path $file.sha256 }
    foreach ($artifact in $artifacts) { Lock-AdmissionInput (Join-Path $runRoot $artifact.relativePath) $artifact.sha256 }
    foreach ($artifact in $hostLifecycle.evidence) { Lock-AdmissionInput $artifact.path $artifact.sha256 }
    Require-Coverage ($null -ne $frameworkEnvelope) 'coverage-admission-framework-envelope-required'
    foreach ($name in @('driverInvocation','driverStdout','driverStderr','executionRequest','runSettings','controlContext','supervisor','hostStart','hostEnd')) {
        $pin = $frameworkEnvelope[$name]
        Lock-AdmissionInput (Join-Path $runRoot $pin.relativePath) $pin.sha256
    }
    $sdkEngine = Join-Path ([IO.Path]::GetDirectoryName($vstest)) 'Microsoft.TestPlatform.CrossPlatEngine.dll'
    $hostEngine = Join-Path ([IO.Path]::GetDirectoryName($TestAssembly)) 'Microsoft.TestPlatform.CrossPlatEngine.dll'
    foreach ($path in @($coverErrorLog, (Join-Path $runRoot 'export.err.log'), $sdkEngine, $hostEngine)) {
        Lock-AdmissionInput $path (Get-Digest $path)
    }
    $coverProcess = @($phases | Where-Object name -CEQ 'cover')[0].process
    $requiredPids = @([int]$hostLifecycle.processId)
    $properties = $caseMetadata.properties
    $requiresOwnedChild = $properties.ContainsKey('FishAudioOwnedChild')
    if ($requiresOwnedChild) {
        Require-Coverage (@($properties.FishAudioOwnedChild).Count -eq 1 -and $properties.FishAudioOwnedChild[0] -ceq 'true') 'coverage-admission-invalid-child-declaration'
        $stage = 'admission-child'
        $childDirectories = @(Get-ChildItem -LiteralPath $runtimeEvidence -Directory -Filter 'fa-c-*')
        Require-Coverage ($childDirectories.Count -eq 1) 'coverage-admission-child-fixture-not-unique'
        $childOutput = Join-Path $runRoot 'child-receipt.json'
        [void](Invoke-CoveragePhase 'child-verification' $dotnet @($TestAssembly, '--verify-child-coverage',
            '--fixture-directory', $childDirectories[0].FullName, '--canonical-id', $CaseId, '--tree', $source.tree, '--output', $childOutput) 120000)
        $childReceipt = [IO.File]::ReadAllText($childOutput) | ConvertFrom-Json -AsHashtable
        Require-Coverage ($childReceipt.complete -eq $true -and $childReceipt.canonicalId -ceq $CaseId -and
            $childReceipt.parentProcessId -eq $hostLifecycle.processId -and $childReceipt.source.commit -ceq $source.commit -and
            $childReceipt.source.tree -ceq $source.tree -and $childReceipt.childExitCode -eq 0 -and
            $childReceipt.childStdoutDrained -eq $true -and $childReceipt.childStderrDrained -eq $true) 'coverage-admission-child-identity-mismatch'
        $proofs = @($childReceipt.artifacts | ForEach-Object {
            $path = Join-Path $childDirectories[0].FullName $_.relativePath
            Require-RegularFile $path $runRoot
            Lock-AdmissionInput $path $_.sha256
            $written = [DateTimeOffset]::new([IO.File]::GetLastWriteTimeUtc($path))
            Require-Coverage ($written -ge $coverProcess.startedUtc -and $written -le $coverProcess.finishedUtc) 'coverage-admission-child-proof-outside-cover'
            [ordered]@{ relativePath = [IO.Path]::GetRelativePath($runRoot, $path).Replace('\','/'); sha256 = $_.sha256
                length = (Get-Item -LiteralPath $path).Length; lastWriteUtc = $written }
        })
        $ownedProcesses = @(@{ processId = [int]$childReceipt.childProcessId; role = 'OwnedAppHost'; completed = $true
            exitCode = 0; stdoutDrained = $true; stderrDrained = $true; evidence = $proofs })
        $requiredPids += [int]$childReceipt.childProcessId
    } else {
        Require-Coverage (@(Get-ChildItem -LiteralPath $runtimeEvidence -Directory -Filter 'fa-c-*').Count -eq 0) 'coverage-admission-undeclared-child'
    }
    $stage = 'admission-cover-diagnostics'
    $coverRequest = Join-Path $runRoot 'cover-review-request.json'
    Write-NewJson $coverRequest @{ schemaVersion = 1; log = $coverLog; errorLog = $coverErrorLog
        requiredProfiledProcessIds = $requiredPids; dotCoverVersion = $observedDotCoverVersion }
    $coverOutput = Join-Path $runRoot 'cover-review.json'
    [void](Invoke-CoveragePhase 'cover-diagnostic-review' $dotnet @($metadataTool, 'coverage-review-cover', '--input', $coverRequest, '--output', $coverOutput) 120000)
    $coverDocument = [IO.File]::ReadAllText($coverOutput) | ConvertFrom-Json
    Assert-ReviewedInputs $coverDocument 'coverage-review-cover' @($coverLog, $coverErrorLog)
    $coverReview = $coverDocument.value
    $stage = 'admission-export-diagnostics'
    $exportRequest = Join-Path $runRoot 'export-review-request.json'
    Write-NewJson $exportRequest @{ schemaVersion = 1; log = (Join-Path $runRoot 'export.log'); errorLog = (Join-Path $runRoot 'export.err.log')
        aggregate = (Join-Path $runRoot 'coverage.json'); sdkEngine = $sdkEngine; hostEngine = $hostEngine
        sdkVersion = $observedSdkVersion; testHostVersion = '18.11.0'; dotCoverVersion = $observedDotCoverVersion }
    $exportOutput = Join-Path $runRoot 'export-review.json'
    [void](Invoke-CoveragePhase 'export-diagnostic-review' $dotnet @($metadataTool, 'coverage-review-export', '--input', $exportRequest, '--output', $exportOutput) 120000)
    $exportDocument = [IO.File]::ReadAllText($exportOutput) | ConvertFrom-Json
    $exportPaths = @((Join-Path $runRoot 'export.log'), (Join-Path $runRoot 'export.err.log'), (Join-Path $runRoot 'coverage.json'))
    if ($exportDocument.value.reviewedWarningCount -gt 0) { $exportPaths += @($sdkEngine, $hostEngine) }
    Assert-ReviewedInputs $exportDocument 'coverage-review-export' $exportPaths
    $exportReview = $exportDocument.value
    $stage = 'admission-console-diagnostics'
    $consolePaths = @('CoverStdout','CoverStderr','ExportStdout','ExportStderr') | ForEach-Object {
        $kind = $_
        Join-Path $runRoot (@($artifacts | Where-Object { $_.kind -ceq $kind })[0].relativePath)
    }
    $consoleRequest = Join-Path $runRoot 'console-review-request.json'
    Write-NewJson $consoleRequest @{ schemaVersion = 1; coverStdout = $consolePaths[0]; coverStderr = $consolePaths[1]
        exportStdout = $consolePaths[2]; exportStderr = $consolePaths[3]; dotCoverVersion = $observedDotCoverVersion }
    $consoleOutput = Join-Path $runRoot 'console-review.json'
    [void](Invoke-CoveragePhase 'console-diagnostic-review' $dotnet @($metadataTool, 'coverage-review-console', '--input', $consoleRequest, '--output', $consoleOutput) 120000)
    $consoleDocument = [IO.File]::ReadAllText($consoleOutput) | ConvertFrom-Json
    Assert-ReviewedInputs $consoleDocument 'coverage-review-console' $consolePaths
    $consoleReview = $consoleDocument.value
    $diagnosticRules = @('tools/FishAudio.Testing/Evidence/DotCoverDiagnostics.cs', 'tools/FishAudio.Testing/Evidence/DotCoverProcessDiagnostics.cs',
        'tools/FishAudio.Testing/Evidence/DotCoverConsoleDiagnostics.cs') | ForEach-Object {
        [ordered]@{ path = $_; sha256 = Get-Digest (Join-Path $root $_) }
    }
    $ruleSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($utf8.GetBytes(($diagnosticRules | ConvertTo-Json -Compress)))).ToLowerInvariant()
    $completion = @{}
    foreach ($name in @('cover','export')) {
        $phase = @($phases | Where-Object name -CEQ $name)[0].process
        $phaseOwnedProcesses = @()
        if ($name -ceq 'cover') { $phaseOwnedProcesses = @($ownedProcesses) }
        $completion[$name] = [ordered]@{ startedUtc = $phase.startedUtc; finishedUtc = $phase.finishedUtc
            completed = $phase.completed; exitCode = $phase.exitCode; stdoutDrained = $phase.stdoutDrained
            stderrDrained = $phase.stderrDrained; ownedTreeCompleted = $phase.ownedTreeCompleted
            ownedProcesses = $phaseOwnedProcesses }
    }
    $diagnosticKinds = @('CoverLog','CoverStdout','CoverStderr','ExportLog','ExportStdout','ExportStderr')
    $supervisor = [ordered]@{ schemaVersion = 1; identity = $identity; startedUtc = $startedUtc; sealedUtc = [DateTimeOffset]::UtcNow
        cover = $completion.cover; export = $completion.export; artifacts = $artifacts.ToArray(); frameworkEnvelope = $frameworkEnvelope
        diagnostics = @{ disposition = $(if ($exportReview.reviewedWarningCount -gt 0) { 'ReviewedWarnings' } else { 'Clean' })
            ruleId = $coverReview.ruleId + ';' + $exportReview.ruleId + ';' + $consoleReview.ruleId; ruleSourceSha256 = $ruleSha256
            evidence = @($artifacts | Where-Object kind -CIn $diagnosticKinds | ForEach-Object { @{ kind = $_.kind; sha256 = $_.sha256 } }) } }
    $emptyProduct = $null
    if ($properties.ContainsKey('FishAudioEmptyProductCoverage')) {
        Require-Coverage (@($properties.FishAudioEmptyProductCoverage).Count -eq 1 -and
            $properties.FishAudioEmptyProductCoverage[0] -ceq 'RepositoryResourcesOnly' -and
            $properties.ContainsKey('FishAudioRepositoryDependency')) 'coverage-admission-invalid-empty-declaration'
        $dependencies = @($properties.FishAudioRepositoryDependency | ForEach-Object {
            $path = [IO.Path]::GetFullPath((Join-Path $root $_)); Require-RegularFile $path $root
            @{ relativePath = $_; sha256 = Get-Digest $path }
        })
        $emptyProduct = @{ reason = 'RepositoryResourcesOnly'; dependencies = $dependencies }
    }
    $inputPath = Join-Path $runRoot 'coverage-input.json'
    Write-NewJson $inputPath @{ schemaVersion = 1; identity = $identity; artifactDirectory = $runRoot
        artifacts = $artifacts.ToArray(); emptyProduct = $emptyProduct }
    $expectationPath = Join-Path $runRoot 'admission-expectation.json'
    Write-NewJson $expectationPath @{ schemaVersion = 1; identity = $identity; sourceDirectory = $root; buildDirectory = $root
        binaries = @($binaries | ForEach-Object { @{ role = $_.role; dllRelativePath = $_.dllRelativePath
            pdbRelativePath = $_.pdbRelativePath; expectedIdentity = $_.identity } })
        discoverySha256 = ($artifacts | Where-Object kind -CEQ 'Discovery').sha256; dotCoverVersion = $observedDotCoverVersion
        toolFile = $DotCoverToolFile; toolSha256 = $toolSha256; requireOwnedAppHost = $requiresOwnedChild
        requireFrameworkSupervision = $true; supervisor = $supervisor }
    $stage = 'admission-import'
    $caseCoveragePath = Join-Path $runRoot 'case-coverage.json'
    [void](Invoke-CoveragePhase 'case-admission' $dotnet @($metadataTool, 'coverage-import', '--input', $inputPath, '--expectation', $expectationPath, '--output', $caseCoveragePath) 120000)
    $stage = 'admission-final-identity'
    $admissionSource = Get-SnapshotIdentity
    Require-Coverage (($source | ConvertTo-Json -Compress) -ceq ($admissionSource | ConvertTo-Json -Compress)) 'coverage-admission-source-changed'
    foreach ($file in $binaryFiles) { Require-Coverage ((Get-Digest $file.path) -ceq $file.sha256) 'coverage-admission-binary-changed' }
    $admitted = $true
} catch {
    $admissionFailure = @{ stage = $stage; reason = $failureCode; exceptionType = $_.Exception.GetType().Name }
    try { Write-NewText (Join-Path $runRoot 'private-admission-failure.txt') ($_ | Out-String) } catch { }
} finally {
    try {
        Write-NewJson (Join-Path $runRoot 'admission.json') ([ordered]@{
            schemaVersion = 1; identity = $identity; testingVerified = $testingVerified; collectionComplete = $collectionComplete
            validCaseCoverage = $admitted; baselineClaim = $false; sourceMappingsResolved = $false
            collectorSha256 = Get-Digest (Join-Path $runRoot 'collector.json'); sourceContextSha256 = $source.contextSha256
            diagnosticRuleSources = $diagnosticRules; hostLifecycle = $hostLifecycle; childReceipt = $childReceipt
            coverReview = $coverReview; exportReview = $exportReview; consoleReview = $consoleReview
            caseCoverageSha256 = $(if ($admitted) { Get-Digest $caseCoveragePath } else { $null })
            failure = $admissionFailure; phases = $phases.ToArray()
        })
    } finally { foreach ($fileLock in $admissionLocks) { $fileLock.Dispose() } }
}
if (-not $admitted) {
    [Console]::Error.WriteLine('coverage-admission: rejected stage=' + $stage + '; raw collection preserved; no baseline claim.')
    exit 1
}
Write-Output ('coverage-admission: isolated case ' + $CaseId + ' admitted; source mappings and complete baseline remain pending.')
exit 0
