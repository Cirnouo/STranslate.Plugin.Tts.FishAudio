#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Mode = 'Full',
    [string[]]$CaseId = @(),
    [string]$SelectionPath,
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
    if ($PSBoundParameters.ContainsKey('SelectionPath')) {
        [Console]::Error.WriteLine('test-driver: file selection requires Full mode.'); exit 2
    }
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
$selectionInput = $null
$hasExplicitSelection = $PSBoundParameters.ContainsKey('CaseId') -or $PSBoundParameters.ContainsKey('SelectionPath')
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

function Read-FileSelection([string]$Path) {
    $previousFailureCode = $script:failureCode
    $script:failureCode = 'test-driver-invalid-selection-input'
    $inputPath = Lock-FrameworkEvidence $Path
    $stream = $locks[$locks.Count - 1]
    Require-Condition ($stream.Length -gt 0 -and $stream.Length -le 67108864) 'test-driver-selection-input-size'
    $options = [System.Text.Json.JsonDocumentOptions]::new()
    $options.MaxDepth = 4
    $document = [System.Text.Json.JsonDocument]::Parse($stream, $options)
    try {
        $element = $document.RootElement
        Require-Condition ($element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) 'test-driver-selection-input-object'
        $names = @($element.EnumerateObject() | ForEach-Object Name)
        Require-Condition ($names.Count -eq 2 -and @($names | Where-Object { $_ -ceq 'schemaVersion' }).Count -eq 1 -and
            @($names | Where-Object { $_ -ceq 'ids' }).Count -eq 1) 'test-driver-selection-input-properties'
        Require-Condition ($element.GetProperty('schemaVersion').GetInt32() -eq 1) 'test-driver-selection-input-version'
        $items = $element.GetProperty('ids')
        Require-Condition ($items.ValueKind -eq [System.Text.Json.JsonValueKind]::Array -and
            $items.GetArrayLength() -gt 0 -and $items.GetArrayLength() -le 100000) 'test-driver-empty-or-invalid-selection'
        $ids = [Collections.Generic.List[string]]::new()
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($item in $items.EnumerateArray()) {
            Require-Condition ($item.ValueKind -eq [System.Text.Json.JsonValueKind]::String) 'test-driver-selection-input-id-type'
            $id = $item.GetString()
            Require-Condition (-not [string]::IsNullOrWhiteSpace($id) -and $id.Length -le 16384 -and
                @($id.ToCharArray() | Where-Object { [char]::IsControl($_) }).Count -eq 0 -and $seen.Add($id)) 'test-driver-invalid-or-duplicate-case-id'
            $ids.Add($id)
        }
        $result = [pscustomobject]@{ path = $inputPath; sha256 = Get-Digest $inputPath; ids = $ids.ToArray() }
        $script:failureCode = $previousFailureCode
        return $result
    } finally { $document.Dispose() }
}
. (Join-Path $PSScriptRoot 'TestProcessOwner.ps1')

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

function Write-RunSettings([string]$Path, [string]$OutputDirectory, [string[]]$FullNames = @(),
    [bool]$StopOnError = $false, [int]$ExecutionWorkers = $Workers) {
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
            @('NumberOfTestWorkers', [string]$ExecutionWorkers), @('PreFilter', 'false'),
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
    if ($PSBoundParameters.ContainsKey('SelectionPath')) {
        Require-Condition ($Mode -ceq 'Full' -and -not $PSBoundParameters.ContainsKey('CaseId')) 'test-driver-conflicting-selection-input'
        $selectionInput = Read-FileSelection $SelectionPath
        $CaseId = [string[]]$selectionInput.ids
    }
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
    if ($null -ne $selectionInput) { [IO.File]::Copy($selectionInput.path, (Join-Path $runRoot 'selection-input.json'), $false) }
    [IO.Directory]::CreateDirectory((Join-Path $runRoot 'phases')) | Out-Null
    $runtimeEvidence = Join-Path $runRoot 'runtime-evidence'
    [IO.Directory]::CreateDirectory($runtimeEvidence) | Out-Null
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $git = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    Initialize-ProcessOwner
    $stage = 'source-identity'
    $source = Get-SourceIdentity
    Write-NewJson (Join-Path $runRoot 'source.json') $source
    [void](Lock-FrameworkEvidence (Join-Path $runRoot 'source.json'))
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
            executionPlan = $null; executionPhases = [Collections.Generic.List[object]]::new()
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
        [void](Lock-FrameworkEvidence $raw)
        Require-Condition (@(Get-ChildItem -LiteralPath $runtimeEvidence -File -Filter 'fixture-created-*.txt').Count -eq 0) 'test-driver-discovery-constructed-fixture'
        $catalogPath = Join-Path $assemblyDirectory 'catalog.json'
        [void](Invoke-OwnedPhase ('catalog-' + $assembly.key) $dotnet @($toolDll, 'evidence-catalog',
            '--discovery', $raw, '--output', $catalogPath) -PhaseTimeoutMilliseconds 120000)
        $assembly.catalog = [IO.File]::ReadAllText($catalogPath) | ConvertFrom-Json
        [void](Lock-FrameworkEvidence $catalogPath)
        Require-Condition ($assembly.catalog.schemaVersion -eq 1 -and $assembly.catalog.kind -ceq 'nunit-catalog' -and
            $assembly.catalog.complete -and @($assembly.catalog.cases).Count -gt 0) 'test-driver-invalid-catalog'
    }
    # Test-only, read-only input capability. It neither selects nor registers
    # cases. All bytes stay in the existing digest-bound raw request/invocation.
    # Missing optional PDBs do not change ordinary NoBuild preflight; the one
    # source/PDB contract that consumes this capability requires actual PDBs.
    $runnerMetadataJson = $null
    if ($Mode -ceq 'Full') {
        $metadataAssemblies = @(foreach ($assembly in $assemblies) {
            $pdb = [IO.Path]::ChangeExtension($assembly.dll, '.pdb')
            if ([IO.File]::Exists($pdb)) { [void](Lock-FrameworkEvidence $pdb) }
            [ordered]@{
                key = $assembly.key; assembly = $assembly.dll; assemblySha256 = $assembly.sha256
                pdb = $pdb; pdbSha256 = $(if ([IO.File]::Exists($pdb)) { Get-Digest $pdb } else { $null })
                discovery = $assembly.discovery; discoverySha256 = Get-Digest $assembly.discovery
                catalog = Join-Path (Join-Path $runRoot $assembly.key) 'catalog.json'
                catalogSha256 = Get-Digest (Join-Path (Join-Path $runRoot $assembly.key) 'catalog.json')
            }
        })
        $metadataToolPdb = [IO.Path]::ChangeExtension($toolDll, '.pdb')
        if ([IO.File]::Exists($metadataToolPdb)) { [void](Lock-FrameworkEvidence $metadataToolPdb) }
        $runnerMetadataJson = ([ordered]@{
            schemaVersion = 1; kind = 'runner-readonly-metadata'; runId = $runId
            repositoryRoot = $root; sessionDirectory = $runRoot
            sourceTree = $source.tree; sourceFingerprint = $source.fingerprint
            sourcePath = Join-Path $runRoot 'source.json'; sourceSha256 = Get-Digest (Join-Path $runRoot 'source.json')
            snapshotContextPath = $(if ($env:FISHAUDIO_SNAPSHOT_CONTEXT) { $env:FISHAUDIO_SNAPSHOT_CONTEXT } else { $null })
            snapshotContextSha256 = $(if ($env:FISHAUDIO_SNAPSHOT_CONTEXT) { Get-Digest $env:FISHAUDIO_SNAPSHOT_CONTEXT } else { $null })
            assemblies = $metadataAssemblies
            tool = [ordered]@{ assembly = $toolDll; assemblySha256 = $toolDigest; pdb = $metadataToolPdb
                pdbSha256 = $(if ([IO.File]::Exists($metadataToolPdb)) { Get-Digest $metadataToolPdb } else { $null }) }
        } | ConvertTo-Json -Depth 8 -Compress)
        Require-Condition ($runnerMetadataJson.Length -le 32767) 'test-driver-runner-metadata-environment-limit'
    }
    $stage = 'selection'
    $allCases = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($assembly in $assemblies) {
        foreach ($case in $assembly.catalog.cases) {
            Require-Condition ($allCases.TryAdd([string]$case.canonicalId, $case)) 'test-driver-ambiguous-case-id'
        }
    }
    $requested = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    if ($hasExplicitSelection) {
        Require-Condition ($CaseId.Count -gt 0) 'test-driver-empty-selection'
        foreach ($id in $CaseId) {
            Require-Condition (-not [string]::IsNullOrWhiteSpace($id) -and $requested.Add($id)) 'test-driver-invalid-or-duplicate-case-id'
            Require-Condition ($allCases.ContainsKey($id)) 'test-driver-unknown-case-id'
        }
    }
    foreach ($assembly in $assemblies) {
        $directory = Join-Path $runRoot $assembly.key
        $assembly.selection = @($assembly.catalog.cases | Where-Object { $requested.Count -eq 0 -or $requested.Contains($_.canonicalId) })
        if ($Mode -ceq 'Full') {
            $requestSelection = Join-Path $directory 'requested-selection.json'
            Write-NewJson $requestSelection @{
                schemaVersion = 1; ids = @($assembly.selection | ForEach-Object canonicalId)
                maximumConcurrency = [Math]::Max(1, $Workers)
            }
            [void](Lock-FrameworkEvidence $requestSelection)
            if ($assembly.selection.Count -gt 0) {
                $planPath = Join-Path $directory 'execution-plan.json'
                [void](Invoke-OwnedPhase ('plan-' + $assembly.key) $dotnet @($toolDll, 'evidence-execution-plan',
                    '--discovery', $assembly.discovery, '--selection', $requestSelection, '--output', $planPath) -PhaseTimeoutMilliseconds 120000)
                $assembly.executionPlan = [IO.File]::ReadAllText($planPath) | ConvertFrom-Json -Depth 40
                [void](Lock-FrameworkEvidence $planPath)
                Require-Condition ($assembly.executionPlan.schemaVersion -eq 1 -and
                    $assembly.executionPlan.kind -ceq 'nunit-execution-plan' -and $assembly.executionPlan.complete -eq $true -and
                    $assembly.executionPlan.plan.rawDiscoverySha256 -ceq (Get-Digest $assembly.discovery)) 'test-driver-invalid-execution-plan'
                $expanded = [Collections.Generic.HashSet[string]]::new([string[]]$assembly.executionPlan.plan.expandedIds, [StringComparer]::Ordinal)
                $assembly.selection = @($assembly.catalog.cases | Where-Object { $expanded.Contains($_.canonicalId) })
                Require-ExactFrameworkIds $assembly.executionPlan.plan.expandedIds @($assembly.selection | ForEach-Object canonicalId) 'test-driver-invalid-expanded-selection'
                foreach ($group in $assembly.executionPlan.plan.phases) {
                    $assembly.executionPhases.Add([pscustomobject]@{
                        id = $group.id; kind = $group.kind; profile = $group.profile
                        requireOrder = $group.requireOrder; maximumConcurrency = $group.maximumConcurrency
                        exclusiveResources = @($group.exclusiveResources); selection = @($group.cases)
                        executionAttempted = $false; executed = $false; terminal = $null
                        markerRequired = $false; markerObserved = $false; parallelProof = $false
                        directory = $assembly.key + '/execution/' + $group.id
                    })
                }
            }
        }
        Write-NewJson (Join-Path $directory 'selection.json') @{
            schemaVersion = 1; ids = @($assembly.selection | ForEach-Object canonicalId)
        }
        [void](Lock-FrameworkEvidence (Join-Path $directory 'selection.json'))
    }
    if ($Mode -ceq 'Full') {
        $stage = 'execution'
        foreach ($assembly in $assemblies) {
            if ($assembly.selection.Count -eq 0) { continue }
            $directory = Join-Path $runRoot $assembly.key
            $passed = [Collections.Generic.List[object]]::new()
            foreach ($group in $assembly.executionPhases) {
                Require-Condition ((Get-Digest $assembly.dll) -ceq $assembly.sha256 -and (Get-Digest $toolDll) -ceq $toolDigest) 'test-driver-binary-changed'
                $phaseDirectory = Join-Path $runRoot $group.directory
                [void][IO.Directory]::CreateDirectory($phaseDirectory)
                $phaseRuntime = Join-Path $phaseDirectory 'runtime-evidence'
                [void][IO.Directory]::CreateDirectory($phaseRuntime)
                $supervised = Join-Path $phaseDirectory 'supervised'
                $output = Join-Path $supervised 'nunit'
                $resultPath = Join-Path $output ([IO.Path]::GetFileNameWithoutExtension($assembly.dll) + '.xml')
                $trxPath = Join-Path $supervised 'trx/results.trx'
                $settingsPath = Join-Path $phaseDirectory 'execution.runsettings'
                # Every phase has its own exact filter, including all-discovered runs.
                Write-RunSettings $settingsPath $output @($group.selection | ForEach-Object fullName) -StopOnError $true `
                    -ExecutionWorkers $(if ($Workers -eq 0) { 0 } else { $group.maximumConcurrency })
                $ids = @($group.selection | ForEach-Object canonicalId)
                Write-NewJson (Join-Path $phaseDirectory 'selection.json') @{ schemaVersion = 1; ids = $ids }
                $group.parallelProof = $assembly.key -ceq 'plugin' -and $group.maximumConcurrency -ge 2 -and
                    $ids -ccontains 'compat.parallel/api-key-format' -and $ids -ccontains 'compat.parallel/voice-id-format'
                $group.markerRequired = $assembly.key -ceq 'plugin' -and
                    @($group.selection | Where-Object className -CEQ 'NUnitCompatibility.CompatibilityCases').Count -gt 0
                $dump = Join-Path ([IO.Path]::GetDirectoryName($assembly.dll)) ('Dump/D_' + [IO.Path]::GetFileName($assembly.dll) + '.dump')
                if ([IO.File]::Exists($dump)) { [IO.File]::Move($dump, (Join-Path $phaseDirectory 'preexisting-execution.dump')) }
                $executionEnvironment = @{
                    FISHAUDIO_COMPAT_EVIDENCE_ROOT = $phaseRuntime
                    FISHAUDIO_COMPAT_PARALLEL_PROOF = $(if ($group.parallelProof) { '1' } else { '0' })
                    FISHAUDIO_TEST_REPOSITORY_ROOT = $root
                    FISHAUDIO_PWSH_EXECUTABLE = $shell; FISHAUDIO_DOTNET_EXECUTABLE = $dotnet
                    DOTNET_CLI_UI_LANGUAGE = 'en'; VSLANG = '1033'
                }
                $requestPath = Join-Path $phaseDirectory 'execution-request.json'
                $executionEnvironment['FISHAUDIO_RUNNER_METADATA_JSON'] = $runnerMetadataJson
                $executionEnvironment['FISHAUDIO_RUNNER_REQUEST_PATH'] = $requestPath
                # Bound the effective inherited + explicit Unicode block without
                # serializing inherited credentials or diagnostics into evidence.
                $metadataEnvironment = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
                foreach ($entry in [Environment]::GetEnvironmentVariables().GetEnumerator()) {
                    $metadataEnvironment[[string]$entry.Key] = [string]$entry.Value
                }
                foreach ($name in $executionEnvironment.Keys) { $metadataEnvironment[$name] = [string]$executionEnvironment[$name] }
                $metadataEnvironment['FISHAUDIO_FRAMEWORK_CONTROL_CONTEXT'] = Join-Path $supervised 'control-context.json'
                $metadataEnvironmentCharacters = 1
                foreach ($entry in $metadataEnvironment.GetEnumerator()) {
                    $metadataEnvironmentCharacters += $entry.Key.Length + $entry.Value.Length + 2
                }
                Require-Condition ($metadataEnvironmentCharacters -le 32767) 'test-driver-runner-metadata-environment-limit'
                Write-NewJson $requestPath ([ordered]@{
                    schemaVersion = 1; runId = $runId; sourceTree = $source.tree; sourceFingerprint = $source.fingerprint
                    workingDirectory = $root; shellExecutable = $shell; dotnetExecutable = $dotnet
                    launcherScript = Join-Path $root 'tools/Invoke-TestProcess.ps1'
                    testAssembly = $assembly.dll; testAssemblySha256 = $assembly.sha256
                    runSettings = $settingsPath; discoveryPath = $assembly.discovery
                    resultXmlPath = $resultPath; trxPath = $trxPath; outputDirectory = $supervised
                    cases = @($group.selection | ForEach-Object { [ordered]@{ canonicalId = $_.canonicalId; fullName = $_.fullName } })
                    maximumConcurrency = $group.maximumConcurrency; requireOrder = $group.requireOrder; environment = $executionEnvironment
                })
                $requestDigest = Get-Digest (Lock-FrameworkEvidence $requestPath)
                [void](Lock-FrameworkEvidence $settingsPath)
                Require-Condition (-not [IO.Directory]::Exists($supervised) -and -not [IO.File]::Exists($supervised)) 'test-driver-supervisor-output-not-fresh'
                $group.executionAttempted = $true; $assembly.executionAttempted = $true
                [void](Invoke-AssemblyPhase $assembly ('execute-' + $assembly.key + '-' + $group.id) @($toolDll, 'framework-run', '--request', $requestPath) $executionEnvironment)
                $supervisorPath = Lock-FrameworkEvidence (Join-Path $supervised 'supervisor.json')
                [void](Lock-FrameworkEvidence $resultPath); [void](Lock-FrameworkEvidence $trxPath)
                & (Join-Path $root 'tools/Assert-FrameworkSupervisor.ps1') -Path $supervisorPath -RunId $runId `
                    -SourceTree $source.tree -SourceFingerprint $source.fingerprint -AssemblySha256 $assembly.sha256 `
                    -CaseIds $ids -ResultXmlSha256 (Get-Digest $resultPath) -TrxSha256 (Get-Digest $trxPath) `
                    -ExecutionRequestPath $requestPath -ExecutionRequestSha256 $requestDigest `
                    -ExpectedMaximumConcurrency $group.maximumConcurrency -ExpectedRequireOrder $group.requireOrder
                $xmlFiles = @(Get-ChildItem -LiteralPath $output -File -Filter '*.xml')
                Require-Condition ($xmlFiles.Count -eq 1 -and (Test-SamePath $xmlFiles[0].FullName $resultPath)) 'test-driver-terminal-xml-count'
                $terminalPath = Join-Path $phaseDirectory 'terminal.json'
                [void](Invoke-OwnedPhase ('verify-' + $assembly.key + '-' + $group.id) $dotnet @($toolDll, 'evidence-verify',
                    '--catalog', (Join-Path $directory 'catalog.json'), '--selection', (Join-Path $phaseDirectory 'selection.json'),
                    '--result', $resultPath, '--trx', $trxPath, '--output', $terminalPath) -PhaseTimeoutMilliseconds 120000)
                $group.terminal = [IO.File]::ReadAllText($terminalPath) | ConvertFrom-Json
                Require-Condition ($group.terminal.schemaVersion -eq 1 -and $group.terminal.kind -ceq 'nunit-terminal' -and
                    $group.terminal.complete -and $group.terminal.passedCount -eq $group.selection.Count) 'test-driver-invalid-terminal'
                if ($group.markerRequired) {
                    $markers = @(Get-ChildItem -LiteralPath $phaseRuntime -File -Filter 'fixture-created-*.txt')
                    Require-Condition ($markers.Count -eq 1 -and $markers[0].Name -cmatch '^fixture-created-[1-9][0-9]*\.txt$' -and
                        [IO.File]::ReadAllText($markers[0].FullName) -ceq 'constructed') 'test-driver-positive-execution-marker-missing'
                    $group.markerObserved = $true
                }
                $group.executed = $true
                foreach ($case in $group.terminal.cases) { $passed.Add($case) }
            }
            Require-ExactFrameworkIds @($passed | ForEach-Object canonicalId) @($assembly.selection | ForEach-Object canonicalId) 'test-driver-phase-union-mismatch'
            # This is a derived case union, never a manufactured merged raw XML/TRX.
            $assembly.terminal = [pscustomobject]@{ passedCount = $passed.Count; cases = $passed.ToArray() }
            $assembly.executed = $true
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
        $assembly = $_
        if ($Mode -ceq 'Full') {
            $groupRecords = @(foreach ($group in $assembly.executionPhases) {
                $directory = Join-Path $runRoot $group.directory
                $supervised = Join-Path $directory 'supervised'
                $xmlName = [IO.Path]::GetFileNameWithoutExtension($assembly.dll) + '.xml'
                $paths = [ordered]@{
                    selection = 'selection.json'; request = 'execution-request.json'; runSettings = 'execution.runsettings'
                    terminal = 'terminal.json'; resultXml = 'supervised/nunit/' + $xmlName
                    trx = 'supervised/trx/results.trx'; supervisor = 'supervised/supervisor.json'
                }
                $record = [ordered]@{
                    id = $group.id; kind = $group.kind; profile = $group.profile
                    requireOrder = $group.requireOrder; maximumConcurrency = $group.maximumConcurrency
                    exclusiveResources = @($group.exclusiveResources)
                    selectedIds = @($group.selection | ForEach-Object canonicalId); executed = $group.executed
                    passedCount = $(if ($group.executed) { $group.terminal.passedCount } else { 0 })
                    runtimeEvidence = $group.directory + '/runtime-evidence'
                    markerRequired = $group.markerRequired; markerObserved = $group.markerObserved; parallelProof = $group.parallelProof
                }
                foreach ($name in $paths.Keys) {
                    $relative = $group.directory + '/' + $paths[$name]
                    $record[$name] = $relative
                    $record[$name + 'Sha256'] = Get-Digest (Join-Path $runRoot $relative)
                }
                $record
            })
            [ordered]@{
                key = $assembly.key; project = $assembly.project
                assembly = [IO.Path]::GetRelativePath($root, $assembly.dll).Replace('\', '/'); sha256 = $assembly.sha256
                catalog = $assembly.key + '/catalog.json'; catalogSha256 = Get-Digest (Join-Path (Join-Path $runRoot $assembly.key) 'catalog.json')
                requestedSelection = $assembly.key + '/requested-selection.json'
                requestedSelectionSha256 = Get-Digest (Join-Path (Join-Path $runRoot $assembly.key) 'requested-selection.json')
                selection = $assembly.key + '/selection.json'; selectionSha256 = Get-Digest (Join-Path (Join-Path $runRoot $assembly.key) 'selection.json')
                executionPlan = $(if ($null -ne $assembly.executionPlan) { $assembly.key + '/execution-plan.json' } else { $null })
                executionPlanSha256 = $(if ($null -ne $assembly.executionPlan) { Get-Digest (Join-Path (Join-Path $runRoot $assembly.key) 'execution-plan.json') } else { $null })
                discoveredCount = @($assembly.catalog.cases).Count; selectedCount = $assembly.selection.Count
                selectedIds = @($assembly.selection | ForEach-Object canonicalId); executed = $assembly.executed
                passedCount = $(if ($assembly.executed) { $assembly.terminal.passedCount } else { 0 })
                executionPhases = $groupRecords
            }
        } else {
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
        }
    })
    Write-NewJson (Join-Path $runRoot 'manifest.json') ([ordered]@{
        schemaVersion = $(if ($Mode -ceq 'Full') { 2 } else { 1 }); kind = 'unified-test-session'; runId = $runId; mode = $Mode; complete = $true
        platform = 'VSTest'; configuration = 'Release'; workers = $Workers; baselineClaim = $false
        selection = $(if ($requested.Count -eq 0) { 'all-discovered' } else { 'focused' })
        selectionInput = $(if ($null -ne $selectionInput) { @{ path = 'selection-input.json'; sha256 = $selectionInput.sha256 } } else { $null })
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
                $assembly = $_
                $groups = @(foreach ($group in $assembly.executionPhases) {
                    $observed = $group.directory + '/supervised/supervisor.json'
                    [ordered]@{
                        id = $group.id; kind = $group.kind; profile = $group.profile
                        selectedIds = @($group.selection | ForEach-Object canonicalId)
                        executionAttempted = $group.executionAttempted; executed = $group.executed
                        verifiedPassedIds = @(if ($group.executed) { $group.terminal.cases | ForEach-Object canonicalId })
                        # Failed producer state remains unadmitted evidence, even if it contains some passing cases.
                        unadmittedSupervisor = $(if (-not $group.executed -and [IO.File]::Exists((Join-Path $runRoot $observed))) { $observed } else { $null })
                        notStartedIds = @(if (-not $group.executionAttempted) { $group.selection | ForEach-Object canonicalId })
                        unresolvedIds = @(if ($group.executionAttempted -and -not $group.executed) { $group.selection | ForEach-Object canonicalId })
                    }
                })
                $planned = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach ($group in $groups) { foreach ($id in $group.selectedIds) { [void]$planned.Add($id) } }
                # A rejected plan must not erase the already known selection.
                $unplanned = @($assembly.selection | Where-Object { -not $planned.Contains($_.canonicalId) } | ForEach-Object canonicalId)
                [ordered]@{
                    key = $assembly.key; catalogAvailable = $null -ne $assembly.catalog
                    selectedIds = @($assembly.selection | ForEach-Object canonicalId)
                    executionAttempted = $assembly.executionAttempted; executionPhases = $groups
                    verifiedPassedIds = @($groups | ForEach-Object verifiedPassedIds)
                    notStartedIds = @($groups | ForEach-Object notStartedIds) + $unplanned
                    planningUnresolvedIds = $unplanned
                    unresolvedIds = @($groups | ForEach-Object unresolvedIds)
                }
            })
            Write-NewJson (Join-Path $runRoot 'failure.json') ([ordered]@{
                schemaVersion = $(if ($Mode -ceq 'Full') { 2 } else { 1 }); kind = 'unified-test-session-failure'; runId = $runId; complete = $false
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
