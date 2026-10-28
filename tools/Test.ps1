#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Mode = 'Full',
    [string[]]$CaseId = @(),
    [ValidateRange(0, 4)][int]$Workers = 4,
    [string]$ResultsDirectory,
    [switch]$NoBuild,
    [string]$Scenario = 'IsolatedCase',
    [string]$EvidenceDirectory,
    [string]$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Mode -ceq 'Compatibility') {
    if ($PSBoundParameters.ContainsKey('NoBuild') -or $PSBoundParameters.ContainsKey('Workers')) {
        [Console]::Error.WriteLine('test-driver: parameters do not apply to compatibility mode.')
        exit 2
    }
    $options = @{ Scenario = $Scenario; RepositoryRoot = $RepositoryRoot; CaseId = $CaseId }
    if ($ResultsDirectory) { $options.ResultsDirectory = $ResultsDirectory }
    if ($EvidenceDirectory) { $options.EvidenceDirectory = $EvidenceDirectory }
    try {
        & (Join-Path $PSScriptRoot 'TestCompatibility.ps1') @options
        exit $LASTEXITCODE
    } catch {
        [Console]::Error.WriteLine('test-driver: invalid compatibility invocation.')
        exit 2
    }
}
if ($PSBoundParameters.ContainsKey('Scenario') -or $PSBoundParameters.ContainsKey('EvidenceDirectory')) {
    [Console]::Error.WriteLine('test-driver: compatibility parameters require compatibility mode.')
    exit 2
}

$utf8 = [Text.UTF8Encoding]::new($false)
$runId = [Guid]::NewGuid().ToString('N')
$root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RepositoryRoot))
$runRoot = $null
$stage = 'preflight'
$phases = [Collections.Generic.List[object]]::new()
$assemblies = [Collections.Generic.List[object]]::new()
$locks = [Collections.Generic.List[IO.FileStream]]::new()
$source = $null
$failureCode = 'test-driver-failed'
$phaseOrdinal = 0
$shell = (Get-Process -Id $PID).Path
$dotnet = $null
$git = $null

function Require-Condition([bool]$Condition, [string]$Code) {
    if (-not $Condition) { $script:failureCode = $Code; throw $Code }
}

function Write-NewText([string]$Path, [string]$Text) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try {
        $bytes = $utf8.GetBytes($Text)
        $stream.Write($bytes, 0, $bytes.Length)
    } finally { $stream.Dispose() }
}

function Write-NewJson([string]$Path, $Value) {
    Write-NewText $Path ($Value | ConvertTo-Json -Depth 30)
}

function Get-Digest([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-SamePath([string]$First, [string]$Second) {
    return [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($First)).Equals(
        [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Second)), [StringComparison]::OrdinalIgnoreCase)
}

function Test-ContainedPath([string]$Path, [string]$Parent) {
    return [IO.Path]::GetFullPath($Path).StartsWith(
        [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Parent)) + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)
}

function Lock-FrameworkEvidence([string]$Path) {
    Require-Condition ([IO.Path]::IsPathFullyQualified($Path)) 'test-driver-framework-evidence-path'
    $full = [IO.Path]::GetFullPath($Path)
    $file = [IO.FileInfo]::new($full)
    Require-Condition ($file.Exists -and ($file.Attributes -band
        ([IO.FileAttributes]::Directory -bor [IO.FileAttributes]::ReparsePoint)) -eq 0) 'test-driver-framework-evidence-not-regular'
    for ($parent = $file.Directory; $null -ne $parent; $parent = $parent.Parent) {
        Require-Condition ($parent.Exists -and ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'test-driver-framework-evidence-parent'
    }
    $locks.Add([IO.FileStream]::new($full, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read))
    return $full
}

function Require-ExactFrameworkIds($Actual, [string[]]$Expected, [string]$Code) {
    $values = @($Actual)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    Require-Condition ($values.Count -eq $Expected.Count) $Code
    foreach ($id in $values) {
        Require-Condition ($id -is [string] -and $seen.Add($id) -and $Expected -ccontains $id) $Code
    }
}

function Initialize-ProcessOwner {
    if ('FishAudio.Testing.Driver.JobOwner' -as [type]) { return }
    Add-Type -TypeDefinition @'
#nullable enable
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FishAudio.Testing.Driver;

public sealed class JobOwner : IDisposable
{
    private readonly SafeFileHandle _job;

    public JobOwner()
    {
        _job = CreateJobObjectW(IntPtr.Zero, null);
        if (_job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var limits = new ExtendedLimits();
        limits.Basic.LimitFlags = 0x00002000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(_job, 9, ref limits, Marshal.SizeOf<ExtendedLimits>()))
        {
            var error = Marshal.GetLastWin32Error();
            _job.Dispose();
            throw new Win32Exception(error);
        }
    }

    public void Assign(IntPtr processHandle)
    {
        if (!AssignProcessToJobObject(_job, processHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public uint ActiveProcesses
    {
        get
        {
            if (!QueryInformationJobObject(_job, 1, out Accounting value,
                Marshal.SizeOf<Accounting>(), IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return value.ActiveProcesses;
        }
    }

    public void Terminate()
    {
        if (!TerminateJobObject(_job, 1))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose() => _job.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperations, WriteOperations, OtherOperations;
        public ulong ReadBytes, WriteBytes, OtherBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Accounting
    {
        public long TotalUserTime, TotalKernelTime, PeriodUserTime, PeriodKernelTime;
        public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimits value, int size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(SafeFileHandle job, int kind, out Accounting value, int size, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
}
'@
}

function Invoke-OwnedPhase {
    param([string]$Label, [string]$Executable, [string[]]$Arguments, [hashtable]$Environment = @{},
        [int]$PhaseTimeoutMilliseconds = 0)
    $script:phaseOrdinal++
    $relative = 'phases/{0:D3}-{1}' -f $phaseOrdinal, $Label
    $directory = Join-Path $runRoot $relative
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $invocationPath = Join-Path $directory 'argv.json'
    Write-NewJson $invocationPath ([ordered]@{
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
    $stdout = $null
    $stderr = $null
    $outStream = $null
    $errStream = $null
    $started = $false
    $assigned = $false
    $drained = $false
    $childrenExited = $false
    $exit = $null
    $primary = $null
    $primaryCode = $null
    $cleanup = $null
    $cleanupClock = $null
    $startedUtc = [DateTimeOffset]::UtcNow
    $clock = [Diagnostics.Stopwatch]::StartNew()
    try {
        $owner = [FishAudio.Testing.Driver.JobOwner]::new()
        $outStream = [IO.FileStream]::new((Join-Path $directory 'stdout.log'), [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write, [IO.FileShare]::Read, 65536, $true)
        $errStream = [IO.FileStream]::new((Join-Path $directory 'stderr.log'), [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write, [IO.FileShare]::Read, 65536, $true)
        $started = $process.Start()
        Require-Condition $started 'test-driver-process-not-started'
        $stdout = $process.StandardOutput.BaseStream.CopyToAsync($outStream)
        $stderr = $process.StandardError.BaseStream.CopyToAsync($errStream)
        $owner.Assign($process.Handle)
        $assigned = $true
        $process.StandardInput.WriteLine('GO')
        $process.StandardInput.Close()
        $exitTask = $process.WaitForExitAsync()
        $warned = $false
        while (-not ($exitTask.IsCompleted -and $stdout.IsCompleted -and $stderr.IsCompleted -and $owner.ActiveProcesses -eq 0)) {
            foreach ($task in @($exitTask, $stdout, $stderr)) {
                if ($task.IsFaulted -or $task.IsCanceled) { [void]$task.GetAwaiter().GetResult() }
            }
            if ($exitTask.IsCompleted) {
                [void]$exitTask.GetAwaiter().GetResult()
                $exit = $process.ExitCode
                if ($null -eq $cleanupClock) { $cleanupClock = [Diagnostics.Stopwatch]::StartNew() }
                Require-Condition ($exit -eq 0) 'test-driver-target-failed'
                Require-Condition ($cleanupClock.ElapsedMilliseconds -lt 10000) 'test-driver-exit-drain-deadline'
            }
            if ($PhaseTimeoutMilliseconds -gt 0) {
                Require-Condition ($clock.ElapsedMilliseconds -lt $PhaseTimeoutMilliseconds) 'test-driver-process-hard-deadline'
            }
            if (-not $warned -and $clock.ElapsedMilliseconds -ge 20000) {
                Write-NewText (Join-Path $directory 'slow-warning.txt') 'test-driver-process-slow'
                $warned = $true
            }
            [Threading.Thread]::Sleep(25)
        }
        if ($PhaseTimeoutMilliseconds -gt 0) {
            Require-Condition ($clock.ElapsedMilliseconds -lt $PhaseTimeoutMilliseconds) 'test-driver-process-hard-deadline'
        }
        [void]$exitTask.GetAwaiter().GetResult()
        [void]$stdout.GetAwaiter().GetResult()
        [void]$stderr.GetAwaiter().GetResult()
        $drained = $true
        $childrenExited = $true
        $exit = $process.ExitCode
    } catch {
        $primary = $_
        $primaryCode = $script:failureCode
        try { Write-NewText (Join-Path $directory 'private-failure.txt') ($_ | Out-String) } catch { }
    } finally {
        # Termination, the original process exit and both original drains share
        # one ten-second allowance. The Job never replaces NUnit action ownership.
        if ($null -ne $primary) {
            if ($null -eq $cleanupClock) { $cleanupClock = [Diagnostics.Stopwatch]::StartNew() }
            try {
                if ($assigned) { $owner.Terminate() }
                elseif ($started -and -not $process.HasExited) { $process.Kill($true) }
                while (($started -and -not $process.HasExited) -or
                    ($null -ne $stdout -and -not $stdout.IsCompleted) -or
                    ($null -ne $stderr -and -not $stderr.IsCompleted) -or
                    ($assigned -and $owner.ActiveProcesses -ne 0)) {
                    Require-Condition ($cleanupClock.ElapsedMilliseconds -lt 10000) 'test-driver-cleanup-deadline'
                    [Threading.Thread]::Sleep(25)
                }
                if ($started -and $process.HasExited) { $exit = $process.ExitCode }
                if ($null -ne $stdout) { [void]$stdout.GetAwaiter().GetResult() }
                if ($null -ne $stderr) { [void]$stderr.GetAwaiter().GetResult() }
                $drained = $null -ne $stdout -and $null -ne $stderr
                $childrenExited = $assigned -and $owner.ActiveProcesses -eq 0
            } catch { $cleanup = $_ }
        }
        try {
            if ($null -ne $outStream) { $outStream.Dispose() }
            if ($null -ne $errStream) { $errStream.Dispose() }
            if ($null -ne $cleanup) {
                try { Write-NewText (Join-Path $directory 'private-cleanup-failure.txt') ($cleanup | Out-String) } catch { }
            }
            $record = [ordered]@{
                schemaVersion = 1; kind = 'owned-process'; complete = ($null -eq $primary -and $drained -and $childrenExited)
                startedUtc = $startedUtc; finishedUtc = [DateTimeOffset]::UtcNow
                elapsedMilliseconds = $clock.Elapsed.TotalMilliseconds; exitCode = $exit
                outputDrained = $drained; allChildrenExited = $childrenExited
                ownership = 'windows-job-before-target-go'; cleanupFailed = ($null -ne $cleanup)
                phaseTimeoutMilliseconds = $PhaseTimeoutMilliseconds
                rootProcessId = $(if ($started) { $process.Id } else { $null })
            }
            Write-NewJson (Join-Path $directory 'process.json') $record
            Write-NewText (Join-Path $directory 'exit-code.txt') $(if ($null -eq $exit) { 'not-observed' } else { [string]$exit })
            $phases.Add([ordered]@{ name = $Label; directory = $relative; complete = $record.complete; exitCode = $exit })
        } catch { if ($null -eq $primary) { $primary = $_ } }
        if ($null -ne $owner) { $owner.Dispose() }
        $process.Dispose()
    }
    if ($null -ne $primary) { $script:failureCode = $primaryCode; throw $primary }
    if ($null -ne $cleanup) { throw $cleanup }
    Require-Condition ($exit -eq 0 -and $drained -and $childrenExited) 'test-driver-phase-failed'
    return $directory
}

function Invoke-MetadataGit([string]$Label, [string[]]$Arguments) {
    $directory = Invoke-OwnedPhase $Label $git (@('-C', $root) + $Arguments) -PhaseTimeoutMilliseconds 120000
    return [IO.File]::ReadAllText((Join-Path $directory 'stdout.log'), $utf8)
}

function Get-SourceIdentity {
    $paths = [Collections.Generic.List[string]]::new()
    if ($env:FISHAUDIO_SNAPSHOT_CONTEXT) {
        Require-Condition ([IO.Path]::IsPathFullyQualified($env:FISHAUDIO_SNAPSHOT_CONTEXT)) 'test-driver-invalid-snapshot-context'
        $context = [IO.File]::ReadAllText($env:FISHAUDIO_SNAPSHOT_CONTEXT) | ConvertFrom-Json
        Require-Condition ($context.schemaVersion -eq 1 -and (Test-SamePath $context.root $root) -and
            $context.tree -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$') 'test-driver-snapshot-root-or-tree-mismatch'
        foreach ($file in $context.files) {
            Require-Condition ($file.path -is [string] -and $file.sha256 -cmatch '^[a-f0-9]{64}$') 'test-driver-invalid-snapshot-file'
            $paths.Add($file.path)
        }
        $identity = [ordered]@{
            snapshot = $true; tree = $context.tree; sourceCommit = $context.sourceCommit; commit = $context.sourceCommit
            snapshotRootValidated = $true; contextSha256 = Get-Digest $env:FISHAUDIO_SNAPSHOT_CONTEXT
        }
    } else {
        $head = (Invoke-MetadataGit 'source-head' @('rev-parse', '--verify', 'HEAD')).Trim()
        Require-Condition ($head -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$') 'test-driver-invalid-source-commit'
        $listing = Invoke-MetadataGit 'source-files' @('ls-files', '-z', '--cached', '--others', '--exclude-standard')
        foreach ($path in $listing.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries)) { $paths.Add($path) }
        $status = Invoke-MetadataGit 'source-status' @('status', '--porcelain=v1', '--untracked-files=all')
        $identity = [ordered]@{ snapshot = $false; tree = $null; sourceCommit = $head; commit = $head; dirty = ($status.Length -ne 0) }
    }
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        $unique = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $paths.Sort([StringComparer]::Ordinal)
        foreach ($path in $paths) {
            Require-Condition (-not [IO.Path]::IsPathRooted($path) -and $unique.Add($path)) 'test-driver-invalid-source-file'
            $full = [IO.Path]::GetFullPath((Join-Path $root $path))
            Require-Condition (Test-ContainedPath $full $root) 'test-driver-source-file-outside-root'
            if (Test-ContainedPath $full $runRoot) { continue }
            Require-Condition ([IO.File]::Exists($full)) 'test-driver-source-file-missing'
            Require-Condition (((Get-Item -LiteralPath $full -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'test-driver-source-file-reparse'
            $name = $utf8.GetBytes($path.Replace('\', '/'))
            $bytes = [IO.File]::ReadAllBytes($full)
            if ($env:FISHAUDIO_SNAPSHOT_CONTEXT) {
                $entry = @($context.files | Where-Object path -CEQ $path)
                Require-Condition ($entry.Count -eq 1 -and
                    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() -ceq $entry[0].sha256) 'test-driver-snapshot-file-changed'
            }
            $hash.AppendData([BitConverter]::GetBytes([long]$name.Length))
            $hash.AppendData($name)
            $hash.AppendData([BitConverter]::GetBytes([long]$bytes.Length))
            $hash.AppendData($bytes)
        }
        $identity['fingerprint'] = [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
        $identity['fingerprintKind'] = 'sha256-source-path-and-bytes-ordinal-v1'
        return $identity
    } finally { $hash.Dispose() }
}

function Write-RunSettings([string]$Path, [string]$OutputDirectory, [string[]]$FullNames = @(), [bool]$StopOnError = $false) {
    $settings = [Xml.XmlWriterSettings]::new()
    $settings.Encoding = $utf8
    $settings.Indent = $true
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $writer = [Xml.XmlWriter]::Create($stream, $settings)
    try {
        $writer.WriteStartDocument()
        $writer.WriteStartElement('RunSettings')
        $writer.WriteStartElement('RunConfiguration')
        $writer.WriteElementString('MaxCpuCount', '1')
        $writer.WriteElementString('TreatNoTestsAsError', 'true')
        $writer.WriteEndElement()
        $writer.WriteStartElement('NUnit')
        foreach ($pair in @(
            @('NumberOfTestWorkers', [string]$Workers), @('PreFilter', 'false'),
            @('UseParentFQNForParametrizedTests', 'false'), @('IncludeStackTrace', 'false'),
            @('DumpXmlTestDiscovery', 'true'), @('DumpXmlTestResults', 'false'),
            @('TestOutputXml', $OutputDirectory), @('OutputXmlFolderMode', 'AsSpecified')
        )) { $writer.WriteElementString($pair[0], $pair[1]) }
        if ($StopOnError) { $writer.WriteElementString('StopOnError', 'true') }
        if ($FullNames.Count -gt 0) {
            $terms = foreach ($fullName in $FullNames) {
                $regex = '\A' + [Text.RegularExpressions.Regex]::Escape($fullName) + '\z'
                # NUnit TSL quoted literals consume backslashes before regex parsing.
                $literal = $regex.Replace('\', '\\').Replace('"', '\"')
                'test =~ "' + $literal + '"'
            }
            $writer.WriteElementString('Where', '(' + ($terms -join ' or ') + ')')
        }
        $writer.WriteEndElement()
        $writer.WriteEndElement()
        $writer.WriteEndDocument()
    } finally { $writer.Dispose(); $stream.Dispose() }
}

function Save-DiscoveryDump($Assembly, [string]$Phase) {
    $dump = Join-Path ([IO.Path]::GetDirectoryName($Assembly.dll)) ('Dump/D_' + [IO.Path]::GetFileName($Assembly.dll) + '.dump')
    Require-Condition ([IO.File]::Exists($dump)) 'test-driver-discovery-dump-missing'
    [IO.File]::Copy($dump, (Join-Path $Phase 'discovery.dump'), $false)
    return (Join-Path $Phase 'discovery.dump')
}

function Invoke-AssemblyPhase {
    param($Assembly, [string]$Label, [string[]]$Arguments, [hashtable]$Environment,
        [int]$PhaseTimeoutMilliseconds = 0)
    $processFailure = $null
    $processFailureCode = $null
    try { $phase = Invoke-OwnedPhase $Label $dotnet $Arguments $Environment -PhaseTimeoutMilliseconds $PhaseTimeoutMilliseconds }
    catch { $processFailure = $_; $processFailureCode = $script:failureCode }
    # Even a nonzero process may have emitted useful current raw discovery XML.
    # Its phase has just been reserved and cannot refer to a prior invocation.
    $phase = Join-Path $runRoot ('phases/{0:D3}-{1}' -f $phaseOrdinal, $Label)
    try {
        $dump = Join-Path ([IO.Path]::GetDirectoryName($Assembly.dll)) ('Dump/D_' + [IO.Path]::GetFileName($Assembly.dll) + '.dump')
        if ([IO.File]::Exists($dump)) { [void](Save-DiscoveryDump $Assembly $phase) }
        elseif ($null -eq $processFailure -and $Label.StartsWith('discover-', [StringComparison]::Ordinal)) {
            Require-Condition $false 'test-driver-discovery-dump-missing'
        }
    } catch {
        if ($null -eq $processFailure) { throw }
        try { Write-NewText (Join-Path $phase 'private-retention-failure.txt') ($_ | Out-String) } catch { }
    }
    if ($null -ne $processFailure) { $script:failureCode = $processFailureCode; throw $processFailure }
    return $phase
}

try {
    Require-Condition ($Mode -cin @('Discover', 'DiscoverCoverage', 'Full')) 'test-driver-unsupported-mode'
    Require-Condition $IsWindows 'test-driver-windows-required'
    foreach ($name in @('COR_ENABLE_PROFILING', 'CORECLR_ENABLE_PROFILING')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        Require-Condition ([string]::IsNullOrEmpty($value) -or $value -eq '0') 'test-driver-profiler-environment-unsupported'
    }
    Require-Condition ([IO.File]::Exists((Join-Path $root 'AGENTS.md')) -and
        [IO.File]::Exists((Join-Path $root 'tools/Invoke-TestProcess.ps1'))) 'test-driver-repository-input-missing'
    Require-Condition (Test-SamePath $PSScriptRoot (Join-Path $root 'tools')) 'test-driver-entry-root-mismatch'
    if ([string]::IsNullOrEmpty($ResultsDirectory)) {
        $ResultsDirectory = Join-Path ([IO.Path]::GetTempPath()) ('fa-test-' + $runId)
    }
    Require-Condition ([IO.Path]::IsPathFullyQualified($ResultsDirectory)) 'test-driver-results-must-be-absolute'
    $candidate = [IO.Path]::GetFullPath($ResultsDirectory)
    Require-Condition (-not [IO.Directory]::Exists($candidate) -and -not [IO.File]::Exists($candidate)) 'test-driver-results-must-be-fresh'
    [IO.Directory]::CreateDirectory($candidate) | Out-Null
    $runRoot = $candidate
    Write-NewJson (Join-Path $runRoot 'owner.json') @{ schemaVersion = 1; runId = $runId }
    [IO.Directory]::CreateDirectory((Join-Path $runRoot 'phases')) | Out-Null
    $runtimeEvidence = Join-Path $runRoot 'runtime-evidence'
    [IO.Directory]::CreateDirectory($runtimeEvidence) | Out-Null
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $git = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    Initialize-ProcessOwner
    $stage = 'source-identity'
    $source = Get-SourceIdentity
    Write-NewJson (Join-Path $runRoot 'source.json') $source
    $definitions = @(
        @{ key = 'plugin'; project = 'tests/STranslate.Plugin.Tts.FishAudio.Tests/STranslate.Plugin.Tts.FishAudio.Tests.csproj'; name = 'STranslate.Plugin.Tts.FishAudio.Tests' },
        @{ key = 'testing-tool'; project = 'tests/FishAudio.Testing.Tests/FishAudio.Testing.Tests.csproj'; name = 'FishAudio.Testing.Tests' }
    )
    $coverageBuild = $Mode -ceq 'DiscoverCoverage'
    if (-not $NoBuild) {
        $stage = 'build'
        if ($coverageBuild) {
            [void](Invoke-OwnedPhase 'restore-dotcover' $dotnet @('tool', 'restore', '--tool-manifest',
                (Join-Path $root '.config/dotnet-tools.json')) -PhaseTimeoutMilliseconds 120000)
        }
        foreach ($definition in $definitions) {
            [void](Invoke-OwnedPhase ('build-' + $definition.key) $dotnet @('build', (Join-Path $root $definition.project),
                '--configuration', 'Release', '--disable-build-servers', '-p:RestoreLockedMode=true',
                ('-p:FishAudioCoverage=' + $coverageBuild.ToString().ToLowerInvariant()), '-p:UseSharedCompilation=false', '-nodeReuse:false'))
        }
    }
    $stage = 'binary-preflight'
    $outputSuffix = $(if ($coverageBuild) { '/bin/coverage/Release/net10.0-windows' } else { '/bin/Release/net10.0-windows' })
    $toolDll = Join-Path $root ('tools/FishAudio.Testing' + $outputSuffix + '/FishAudio.Testing.dll')
    foreach ($definition in $definitions) {
        $directory = Join-Path $root ([IO.Path]::GetDirectoryName($definition.project) + $outputSuffix)
        $dll = Join-Path $directory ($definition.name + '.dll')
        foreach ($suffix in @('.dll', '.deps.json', '.runtimeconfig.json')) {
            Require-Condition ([IO.File]::Exists((Join-Path $directory ($definition.name + $suffix)))) 'test-driver-normal-release-output-missing'
        }
        $locks.Add([IO.File]::Open((Join-Path $directory 'fish-audio-test-driver.lock'), [IO.FileMode]::OpenOrCreate,
            [IO.FileAccess]::ReadWrite, [IO.FileShare]::None))
        [IO.Directory]::CreateDirectory((Join-Path $runRoot $definition.key)) | Out-Null
        $assemblies.Add([pscustomobject]@{
            key = $definition.key; project = $definition.project; dll = $dll; sha256 = Get-Digest $dll
            catalog = $null; selection = @(); executionAttempted = $false; executed = $false; terminal = $null
            resultXml = $null; trx = $null; discovery = $null; supervisor = $null; supervisorSha256 = $null
            markerRequired = $false; markerObserved = $false; parallelProof = $false
        })
    }
    foreach ($suffix in @('.dll', '.deps.json', '.runtimeconfig.json')) {
        $sidecar = Join-Path ([IO.Path]::GetDirectoryName($toolDll)) ([IO.Path]::GetFileNameWithoutExtension($toolDll) + $suffix)
        Require-Condition ([IO.File]::Exists($sidecar)) 'test-driver-tool-release-output-missing'
    }
    $toolDigest = Get-Digest $toolDll
    $stage = 'discovery'
    foreach ($assembly in $assemblies) {
        $assemblyDirectory = Join-Path $runRoot $assembly.key
        $output = Join-Path $assemblyDirectory 'discovery-xml'
        [IO.Directory]::CreateDirectory($output) | Out-Null
        $settingsPath = Join-Path $assemblyDirectory 'discovery.runsettings'
        Write-RunSettings $settingsPath $output
        $dump = Join-Path ([IO.Path]::GetDirectoryName($assembly.dll)) ('Dump/D_' + [IO.Path]::GetFileName($assembly.dll) + '.dump')
        if ([IO.File]::Exists($dump)) {
            [IO.File]::Move($dump, (Join-Path $assemblyDirectory 'preexisting-discovery.dump'))
        }
        $phase = Invoke-AssemblyPhase $assembly ('discover-' + $assembly.key) @('vstest', $assembly.dll,
            '--ListTests', ('--Settings:' + $settingsPath)) @{ FISHAUDIO_COMPAT_EVIDENCE_ROOT = $runtimeEvidence } -PhaseTimeoutMilliseconds 120000
        $raw = Join-Path $phase 'discovery.dump'
        $assembly.discovery = $raw
        Require-Condition (@(Get-ChildItem -LiteralPath $runtimeEvidence -File -Filter 'fixture-created-*.txt').Count -eq 0) 'test-driver-discovery-constructed-fixture'
        $catalogPath = Join-Path $assemblyDirectory 'catalog.json'
        [void](Invoke-OwnedPhase ('catalog-' + $assembly.key) $dotnet @($toolDll, 'evidence-catalog',
            '--discovery', $raw, '--output', $catalogPath) -PhaseTimeoutMilliseconds 120000)
        $assembly.catalog = [IO.File]::ReadAllText($catalogPath) | ConvertFrom-Json
        Require-Condition ($assembly.catalog.schemaVersion -eq 1 -and $assembly.catalog.kind -ceq 'nunit-catalog' -and
            $assembly.catalog.complete -and @($assembly.catalog.cases).Count -gt 0) 'test-driver-invalid-catalog'
    }
    $stage = 'selection'
    $allCases = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($assembly in $assemblies) {
        foreach ($case in $assembly.catalog.cases) {
            Require-Condition ($allCases.TryAdd([string]$case.canonicalId, $case)) 'test-driver-ambiguous-case-id'
        }
    }
    $requested = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    if ($PSBoundParameters.ContainsKey('CaseId')) {
        Require-Condition ($CaseId.Count -gt 0) 'test-driver-empty-selection'
        foreach ($id in $CaseId) {
            Require-Condition (-not [string]::IsNullOrWhiteSpace($id) -and $requested.Add($id)) 'test-driver-invalid-or-duplicate-case-id'
            Require-Condition ($allCases.ContainsKey($id)) 'test-driver-unknown-case-id'
        }
    }
    foreach ($assembly in $assemblies) {
        $assembly.selection = @($assembly.catalog.cases | Where-Object { $requested.Count -eq 0 -or $requested.Contains($_.canonicalId) })
        Write-NewJson (Join-Path (Join-Path $runRoot $assembly.key) 'selection.json') @{
            schemaVersion = 1; ids = @($assembly.selection | ForEach-Object canonicalId)
        }
    }
    if ($Mode -ceq 'Full') {
        $stage = 'execution'
        foreach ($assembly in $assemblies) {
            if ($assembly.selection.Count -eq 0) { continue }
            Require-Condition ((Get-Digest $assembly.dll) -ceq $assembly.sha256 -and (Get-Digest $toolDll) -ceq $toolDigest) 'test-driver-binary-changed'
            $directory = Join-Path $runRoot $assembly.key
            $supervised = Join-Path $directory 'supervised'
            $output = Join-Path $supervised 'nunit'
            $trxDirectory = Join-Path $supervised 'trx'
            $resultPath = Join-Path $output ([IO.Path]::GetFileNameWithoutExtension($assembly.dll) + '.xml')
            $trxPath = Join-Path $trxDirectory 'results.trx'
            $settingsPath = Join-Path $directory 'execution.runsettings'
            $names = @()
            if ($requested.Count -ne 0) { $names = @($assembly.selection | ForEach-Object fullName) }
            Write-RunSettings $settingsPath $output $names -StopOnError $true
            $ids = @($assembly.selection | ForEach-Object canonicalId)
            $assembly.parallelProof = $assembly.key -ceq 'plugin' -and $Workers -ge 2 -and
                $ids -ccontains 'compat.parallel/api-key-format' -and $ids -ccontains 'compat.parallel/voice-id-format'
            $assembly.markerRequired = $assembly.key -ceq 'plugin' -and
                @($assembly.selection | Where-Object className -CEQ 'NUnitCompatibility.CompatibilityCases').Count -gt 0
            $dump = Join-Path ([IO.Path]::GetDirectoryName($assembly.dll)) ('Dump/D_' + [IO.Path]::GetFileName($assembly.dll) + '.dump')
            if ([IO.File]::Exists($dump)) {
                [IO.File]::Move($dump, (Join-Path $directory 'preexisting-execution.dump'))
            }
            $executionEnvironment = @{
                FISHAUDIO_COMPAT_EVIDENCE_ROOT = $runtimeEvidence
                FISHAUDIO_COMPAT_PARALLEL_PROOF = $(if ($assembly.parallelProof) { '1' } else { '0' })
                FISHAUDIO_TEST_REPOSITORY_ROOT = $root
                FISHAUDIO_PWSH_EXECUTABLE = $shell
                FISHAUDIO_DOTNET_EXECUTABLE = $dotnet
                DOTNET_CLI_UI_LANGUAGE = 'en'
                VSLANG = '1033'
            }
            $requestPath = Join-Path $directory 'execution-request.json'
            Write-NewJson $requestPath ([ordered]@{
                schemaVersion = 1; runId = $runId; sourceTree = $source.tree; sourceFingerprint = $source.fingerprint
                workingDirectory = $root; shellExecutable = $shell; dotnetExecutable = $dotnet
                launcherScript = Join-Path $root 'tools/Invoke-TestProcess.ps1'
                testAssembly = $assembly.dll; testAssemblySha256 = $assembly.sha256
                runSettings = $settingsPath; discoveryPath = $assembly.discovery
                resultXmlPath = $resultPath; trxPath = $trxPath; outputDirectory = $supervised
                cases = @($assembly.selection | ForEach-Object { [ordered]@{ canonicalId = $_.canonicalId; fullName = $_.fullName } })
                maximumConcurrency = [Math]::Max(1, $Workers); requireOrder = $false; environment = $executionEnvironment
            })
            Require-Condition (-not [IO.Directory]::Exists($supervised) -and -not [IO.File]::Exists($supervised)) 'test-driver-supervisor-output-not-fresh'
            $assembly.executionAttempted = $true
            [void](Invoke-AssemblyPhase $assembly ('execute-' + $assembly.key) @($toolDll, 'framework-run', '--request', $requestPath) $executionEnvironment)
            $assembly.executed = $true
            $supervisorPath = Lock-FrameworkEvidence (Join-Path $supervised 'supervisor.json')
            [void](Lock-FrameworkEvidence $resultPath)
            [void](Lock-FrameworkEvidence $trxPath)
            $supervisor = [IO.File]::ReadAllText($supervisorPath) | ConvertFrom-Json -Depth 40
            $sameTree = ($null -eq $source.tree -and $null -eq $supervisor.sourceTree) -or
                ($source.tree -is [string] -and $supervisor.sourceTree -is [string] -and $supervisor.sourceTree -ceq $source.tree)
            Require-Condition ($supervisor.schemaVersion -eq 1 -and $supervisor.kind -ceq 'framework-execution' -and
                $supervisor.runId -ceq $runId -and $supervisor.testAssemblySha256 -ceq $assembly.sha256 -and
                $sameTree -and $supervisor.sourceFingerprint -is [string] -and
                $supervisor.sourceFingerprint -cmatch '^[a-f0-9]{64}$' -and $supervisor.sourceFingerprint -ceq $source.fingerprint -and
                $supervisor.state.complete -is [bool] -and $supervisor.state.complete -and
                $null -eq $supervisor.state.failureStage -and $null -eq $supervisor.failureCode -and
                $supervisor.baselineClaim -is [bool] -and -not $supervisor.baselineClaim) 'test-driver-invalid-supervisor'
            Require-ExactFrameworkIds $supervisor.selectedIds $ids 'test-driver-supervisor-selection-mismatch'
            Require-ExactFrameworkIds $supervisor.state.passedIds $ids 'test-driver-supervisor-passed-set-mismatch'
            foreach ($field in @('failedIds', 'timedOutIds', 'interruptedIds', 'notRunIds', 'activeIds')) {
                Require-Condition (@($supervisor.state.$field).Count -eq 0) 'test-driver-supervisor-nonpassing-state'
            }
            Require-Condition ($supervisor.target.started -is [bool] -and $supervisor.target.started -and
                $supervisor.target.pid -gt 0 -and $supervisor.target.exitCode -eq 0 -and
                $supervisor.process.launcherPid -gt 0 -and $supervisor.process.launcherExited -is [bool] -and
                $supervisor.process.launcherExited -and $supervisor.process.stdoutDrained -is [bool] -and
                $supervisor.process.stdoutDrained -and $supervisor.process.stderrDrained -is [bool] -and
                $supervisor.process.stderrDrained -and $supervisor.process.ownedActiveProcesses -eq 0) 'test-driver-supervisor-process-incomplete'
            Require-Condition ($supervisor.rawEvidence.verified -is [bool] -and $supervisor.rawEvidence.verified -and
                [IO.Path]::IsPathFullyQualified($supervisor.rawEvidence.resultXmlPath) -and
                [IO.Path]::IsPathFullyQualified($supervisor.rawEvidence.trxPath) -and
                (Test-SamePath $supervisor.rawEvidence.resultXmlPath $resultPath) -and
                (Test-SamePath $supervisor.rawEvidence.trxPath $trxPath) -and
                $supervisor.rawEvidence.resultXmlSha256 -ceq (Get-Digest $resultPath) -and
                $supervisor.rawEvidence.trxSha256 -ceq (Get-Digest $trxPath)) 'test-driver-supervisor-raw-evidence-mismatch'
            $assembly.supervisor = [IO.Path]::GetRelativePath($runRoot, $supervisorPath).Replace('\', '/')
            $assembly.supervisorSha256 = Get-Digest $supervisorPath
            $xmlFiles = @(Get-ChildItem -LiteralPath $output -File -Filter '*.xml')
            Require-Condition ($xmlFiles.Count -eq 1 -and (Test-SamePath $xmlFiles[0].FullName $resultPath)) 'test-driver-terminal-xml-count'
            $assembly.resultXml = [IO.Path]::GetRelativePath($runRoot, $xmlFiles[0].FullName).Replace('\', '/')
            $assembly.trx = [IO.Path]::GetRelativePath($runRoot, $trxPath).Replace('\', '/')
            $terminalPath = Join-Path $directory 'terminal.json'
            [void](Invoke-OwnedPhase ('verify-' + $assembly.key) $dotnet @($toolDll, 'evidence-verify',
                '--catalog', (Join-Path $directory 'catalog.json'), '--selection', (Join-Path $directory 'selection.json'),
                '--result', $xmlFiles[0].FullName, '--trx', $trxPath, '--output', $terminalPath) -PhaseTimeoutMilliseconds 120000)
            $assembly.terminal = [IO.File]::ReadAllText($terminalPath) | ConvertFrom-Json
            Require-Condition ($assembly.terminal.schemaVersion -eq 1 -and $assembly.terminal.kind -ceq 'nunit-terminal' -and
                $assembly.terminal.complete -and $assembly.terminal.passedCount -eq $assembly.selection.Count) 'test-driver-invalid-terminal'
            if ($assembly.markerRequired) {
                $markers = @(Get-ChildItem -LiteralPath $runtimeEvidence -File -Filter 'fixture-created-*.txt')
                Require-Condition ($markers.Count -eq 1 -and $markers[0].Name -cmatch '^fixture-created-[1-9][0-9]*\.txt$' -and
                    [IO.File]::ReadAllText($markers[0].FullName) -ceq 'constructed') 'test-driver-positive-execution-marker-missing'
                $assembly.markerObserved = $true
            }
        }
    }
    $stage = 'final-identity'
    $finalSource = Get-SourceIdentity
    Require-Condition (($source | ConvertTo-Json -Compress) -ceq ($finalSource | ConvertTo-Json -Compress)) 'test-driver-source-changed'
    foreach ($assembly in $assemblies) {
        Require-Condition ((Get-Digest $assembly.dll) -ceq $assembly.sha256) 'test-driver-binary-changed'
    }
    Require-Condition ((Get-Digest $toolDll) -ceq $toolDigest) 'test-driver-tool-binary-changed'
    $stage = 'manifest'
    $records = @($assemblies | ForEach-Object {
        [ordered]@{
            key = $_.key; project = $_.project; assembly = [IO.Path]::GetRelativePath($root, $_.dll).Replace('\', '/')
            sha256 = $_.sha256; catalog = $_.key + '/catalog.json'; selection = $_.key + '/selection.json'
            catalogSha256 = Get-Digest (Join-Path (Join-Path $runRoot $_.key) 'catalog.json')
            selectionSha256 = Get-Digest (Join-Path (Join-Path $runRoot $_.key) 'selection.json')
            discoveredCount = @($_.catalog.cases).Count; selectedCount = $_.selection.Count; executed = $_.executed
            selectedIds = @($_.selection | ForEach-Object canonicalId)
            passedCount = $(if ($_.executed) { $_.terminal.passedCount } else { 0 })
            terminal = $(if ($_.executed) { $_.key + '/terminal.json' } else { $null })
            terminalSha256 = $(if ($_.executed) { Get-Digest (Join-Path (Join-Path $runRoot $_.key) 'terminal.json') } else { $null })
            resultXml = $_.resultXml; trx = $_.trx
            supervisor = $_.supervisor; supervisorSha256 = $_.supervisorSha256
            resultXmlSha256 = $(if ($_.executed) { Get-Digest (Join-Path $runRoot $_.resultXml) } else { $null })
            trxSha256 = $(if ($_.executed) { Get-Digest (Join-Path $runRoot $_.trx) } else { $null })
            markerRequired = $_.markerRequired; markerObserved = $_.markerObserved; parallelProof = $_.parallelProof
        }
    })
    Write-NewJson (Join-Path $runRoot 'manifest.json') ([ordered]@{
        schemaVersion = 1; kind = 'unified-test-session'; runId = $runId; mode = $Mode; complete = $true
        platform = 'VSTest'; configuration = 'Release'; workers = $Workers; baselineClaim = $false
        selection = $(if ($requested.Count -eq 0) { 'all-discovered' } else { 'focused' })
        source = $source; toolSha256 = $toolDigest; assemblies = $records; phases = $phases.ToArray()
        finishedUtc = [DateTimeOffset]::UtcNow
    })
    Write-Output ('test-driver: passed (' + $runId + ').')
} catch {
    $failure = $_
    if ($null -ne $runRoot) {
        try { Write-NewText (Join-Path $runRoot 'private-failure.txt') ($failure | Out-String) } catch { }
        try {
            $remaining = @($assemblies | ForEach-Object {
                $observedSupervisor = Join-Path $runRoot ($_.key + '/supervised/supervisor.json')
                [ordered]@{
                    key = $_.key; catalogAvailable = $null -ne $_.catalog
                    selectedIds = @($_.selection | ForEach-Object canonicalId)
                    executionAttempted = $_.executionAttempted
                    verifiedPassedIds = @(if ($_.executed -and $null -ne $_.terminal) { $_.terminal.cases | ForEach-Object canonicalId })
                    # A failed producer's raw state is retained for diagnosis;
                    # it cannot turn this incomplete session into a passing one.
                    unadmittedSupervisor = $(if ([IO.File]::Exists($observedSupervisor)) { [IO.Path]::GetRelativePath($runRoot, $observedSupervisor).Replace('\', '/') } else { $null })
                    notStartedIds = @(if (-not $_.executionAttempted) { $_.selection | ForEach-Object canonicalId })
                    unresolvedIds = @(if ($_.executionAttempted -and -not [IO.File]::Exists($observedSupervisor) -and -not $_.executed) {
                        $_.selection | ForEach-Object canonicalId
                    })
                }
            })
            Write-NewJson (Join-Path $runRoot 'failure.json') ([ordered]@{
                schemaVersion = 1; kind = 'unified-test-session-failure'; runId = $runId; complete = $false
                mode = $Mode; stage = $stage; reason = $failureCode; exceptionType = $failure.Exception.GetType().Name
                baselineClaim = $false; source = $source; assemblies = $remaining
                phases = $phases.ToArray(); finishedUtc = [DateTimeOffset]::UtcNow
            })
        } catch { }
    }
    [Console]::Error.WriteLine('test-driver: failed stage=' + $stage + ' reason=' + $failureCode + ' (' + $failure.Exception.GetType().Name + ').')
    exit 1
} finally {
    foreach ($lock in $locks) { $lock.Dispose() }
}
exit 0
