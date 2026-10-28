#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SnapshotRoot,
    [Parameter(Mandatory)][string]$SourceTree,
    [Parameter(Mandatory)][string]$EvidenceRoot,
    [Parameter(Mandatory)][string]$DiscoveryManifest,
    [ValidateSet('Compatibility', 'Envelope', 'BodyHang', 'TeardownHang', 'ConstructorFailure',
        'OneTimeSetupFailure', 'OneTimeCleanupFailure', 'FinalizationWriteHang')][string]$Scenario = 'Compatibility'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Consume one caller-built, metadata-only Test.ps1 Discover invocation. This
# script never builds, rediscovers tests, fabricates a supervisor result or
# converts an incomplete execution into a passing execution. Expected faults
# have a separate acceptance oracle and retain the failed supervisor evidence.
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$runId = [Guid]::NewGuid().ToString('N')
$runRoot = $null
$stage = 'preflight'
$reason = 'preflight-failed'
$driverExit = $null
$locks = [Collections.Generic.List[IO.FileStream]]::new()
$envelope = $Scenario -ne 'Compatibility'
$expectedPositive = $Scenario -in @('Compatibility', 'Envelope')
$finalizationWriteHang = $Scenario -ceq 'FinalizationWriteHang'
$expectedCasesPass = $expectedPositive -or $finalizationWriteHang
$driverExitTimestamp = $null
$finalizationMarkerPath = $null
$finalizationMarkerObservedTimestamp = $null
$finalizationSafetyWatchTriggered = $false
$fault = switch ($Scenario) {
    'BodyHang' { 'body-hang' }
    'TeardownHang' { 'teardown-hang' }
    'ConstructorFailure' { 'constructor-failure' }
    'OneTimeSetupFailure' { 'one-time-setup-failure' }
    'OneTimeCleanupFailure' { 'one-time-cleanup-failure' }
    'FinalizationWriteHang' { 'block-final-write' }
    default { $null }
}
$selectedIds = if ($envelope) { @(
    'compat.envelope/owned-sync', 'compat.envelope/owned-async', 'compat.envelope/remaining-suffix'
) } else { @(
    'compat.sync/api-key-format',
    'compat.async/json-string-after-yield',
    'compat.native/settings-view-construction'
) }
$phaseEvidence = @()
$terminalReference = $null
$faultOracle = $null
$supervisorReference = 'execution/supervisor.json'

function Require-Condition([bool]$Condition, [string]$Code) {
    if (-not $Condition) { $script:reason = $Code; throw $Code }
}

function Test-DevicePath([string]$Path) {
    $value = $Path.Replace('/', '\')
    return $value.StartsWith('\\?\', [StringComparison]::Ordinal) -or
        $value.StartsWith('\\.\', [StringComparison]::Ordinal) -or
        $value.StartsWith('\??\', [StringComparison]::Ordinal)
}

function Get-AbsolutePath([string]$Path) {
    Require-Condition ([IO.Path]::IsPathFullyQualified($Path) -and -not (Test-DevicePath $Path)) 'path-must-be-absolute-and-regular'
    $full = [IO.Path]::GetFullPath($Path)
    Require-Condition (-not (Test-DevicePath $full)) 'device-path-rejected'
    return $full
}

function Require-Directory([string]$Path) {
    $full = [IO.Path]::TrimEndingDirectorySeparator((Get-AbsolutePath $Path))
    for ($directory = [IO.DirectoryInfo]::new($full); $null -ne $directory; $directory = $directory.Parent) {
        Require-Condition ($directory.Exists -and ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'directory-missing-or-reparse'
    }
    return $full
}

function Require-File([string]$Path) {
    $full = Get-AbsolutePath $Path
    $file = [IO.FileInfo]::new($full)
    Require-Condition ($file.Exists -and ($file.Attributes -band
        ([IO.FileAttributes]::Directory -bor [IO.FileAttributes]::ReparsePoint)) -eq 0) 'file-missing-or-reparse'
    [void](Require-Directory $file.DirectoryName)
    return $full
}

function Test-ContainedPath([string]$Path, [string]$Root) {
    return $Path.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Resolve-File([string]$Root, [string]$Relative) {
    Require-Condition (-not [string]::IsNullOrWhiteSpace($Relative) -and -not [IO.Path]::IsPathRooted($Relative)) 'artifact-path-not-relative'
    $full = Get-AbsolutePath (Join-Path $Root $Relative)
    Require-Condition (Test-ContainedPath $full $Root) 'artifact-escaped-owner'
    return (Require-File $full)
}

function Lock-Input([string]$Path) {
    $full = Require-File $Path
    $stream = [IO.FileStream]::new($full, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $locks.Add($stream)
    [void](Require-File $full)
    return $full
}

function Get-Digest([string]$Path) {
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try { return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
    finally { $stream.Dispose() }
}

function Read-Json([string]$Path) {
    [void](Require-File $Path)
    Require-Condition (([IO.FileInfo]::new($Path)).Length -le 16MB) 'json-too-large'
    return ([IO.File]::ReadAllText($Path, $utf8) | ConvertFrom-Json -Depth 40)
}

function Write-NewText([string]$Path, [string]$Text) {
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $bytes = $utf8.GetBytes($Text); $stream.Write($bytes, 0, $bytes.Length) }
    finally { $stream.Dispose() }
}

function Write-NewJson([string]$Path, $Value) {
    Write-NewText $Path ($Value | ConvertTo-Json -Depth 40)
}

function Require-ExactIds($Actual, [string[]]$Expected, [string]$Code) {
    $values = @($Actual)
    $set = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    Require-Condition ($values.Count -eq $Expected.Count) $Code
    foreach ($id in $values) {
        Require-Condition ($id -is [string] -and $set.Add($id) -and $Expected -ccontains $id) $Code
    }
}

function Read-EnvelopePhases([string]$Directory, $Supervisor) {
    $ready = @($Supervisor.events | Where-Object { $_.role -ceq 'nunit-host' -and $_.kind -ceq 'ready' })
    Require-Condition ($ready.Count -eq 1 -and $ready[0].pid -gt 0) 'envelope-host-ready-missing'
    $fixture = 'compat.envelope|fixture|'
    $normal = [Collections.Generic.List[string]]::new()
    foreach ($name in @('constructor', 'one-time-setup-entered', 'one-time-setup-completed')) { $normal.Add($fixture + $name) }
    foreach ($id in $selectedIds) {
        $names = @('setup-entered', 'setup-after-await', 'body-entered')
        if ($id -ceq $selectedIds[1]) { $names += 'body-after-await' }
        $names += @('body-completed', 'teardown-entered', 'teardown-after-await', 'teardown-completed')
        foreach ($name in $names) { $normal.Add($id + '|case|' + $name) }
    }
    $cleanup = @(($fixture + 'one-time-cleanup-entered'), ($fixture + 'one-time-cleanup-completed'))
    $normal.AddRange([string[]]$cleanup)
    $required = $normal.ToArray()
    $optional = @()
    switch ($Scenario) {
        'ConstructorFailure' { $required = @($normal[0]) }
        'OneTimeSetupFailure' { $required = @($normal[0], $normal[1]) }
        'OneTimeCleanupFailure' { $required = @($normal.ToArray() | Select-Object -SkipLast 1) }
        'BodyHang' {
            $stop = $normal.IndexOf($selectedIds[1] + '|case|body-completed')
            $required = @($normal.GetRange(0, $stop).ToArray()) + @($selectedIds[1] + '|case|body-hang-entered')
            $optional = @(@('teardown-entered', 'teardown-after-await', 'teardown-completed') |
                ForEach-Object { $selectedIds[1] + '|case|' + $_ }) + $cleanup
        }
        'TeardownHang' {
            $stop = $normal.IndexOf($selectedIds[1] + '|case|teardown-completed')
            $required = @($normal.GetRange(0, $stop).ToArray()) + @($selectedIds[1] + '|case|teardown-hang-entered')
            $optional = $cleanup
        }
    }
    $entries = @([IO.Directory]::EnumerateFileSystemEntries((Require-Directory $Directory)) | Sort-Object -CaseSensitive)
    Require-Condition ($entries.Count -ge $required.Count -and $entries.Count -le ($required.Count + $optional.Count) -and
        $entries.Count -le 100) 'envelope-phase-count-mismatch'
    $records = [Collections.Generic.List[object]]::new()
    $lastTimestamp = [long]$ready[0].timestamp
    $staThread = $null
    for ($index = 0; $index -lt $entries.Count; $index++) {
        $path = Lock-Input $entries[$index]
        $record = Read-Json $path
        Require-ExactIds @($record.PSObject.Properties.Name) @('schemaVersion', 'caseId', 'scope', 'phase', 'pid',
            'timestamp', 'frequency', 'sequence', 'threadId', 'apartment') 'envelope-phase-shape-mismatch'
        $expected = if ($index -lt $required.Count) { $required[$index] } else { $optional[$index - $required.Count] }
        Require-Condition ($record.schemaVersion -eq 1 -and $record.pid -eq $ready[0].pid -and
            $record.sequence -eq ($index + 1) -and $record.frequency -eq [Diagnostics.Stopwatch]::Frequency -and
            $record.timestamp -ge $lastTimestamp -and $record.threadId -gt 0 -and
            ($record.caseId + '|' + $record.scope + '|' + $record.phase) -ceq $expected -and
            [IO.Path]::GetFileName($path) -ceq ('phase-{0:D6}-{1}.json' -f ($index + 1), $record.phase)) 'envelope-phase-identity-or-order-mismatch'
        if ($record.scope -ceq 'case') {
            if ($null -eq $staThread) { $staThread = $record.threadId }
            Require-Condition ($record.apartment -ceq 'STA' -and $record.threadId -eq $staThread) 'envelope-phase-sta-mismatch'
            $ordinal = if ($record.phase.StartsWith('setup-', [StringComparison]::Ordinal)) { 1 }
                elseif ($record.phase.StartsWith('body-', [StringComparison]::Ordinal)) { 2 } else { 3 }
            $queued = @($Supervisor.events | Where-Object { $_.role -ceq 'nunit-host' -and $_.caseId -ceq $record.caseId -and
                $_.ordinal -eq $ordinal -and $_.kind -ceq 'queued' })
            $entered = @($Supervisor.events | Where-Object { $_.role -ceq 'nunit-host' -and $_.caseId -ceq $record.caseId -and
                $_.ordinal -eq $ordinal -and $_.kind -ceq 'entered' })
            $completed = @($Supervisor.events | Where-Object { $_.role -ceq 'nunit-host' -and $_.caseId -ceq $record.caseId -and
                $_.ordinal -eq $ordinal -and $_.kind -ceq 'completed' })
            Require-Condition ($queued.Count -eq 1 -and $entered.Count -eq 1 -and $completed.Count -le 1 -and
                $queued[0].queuedTimestamp -eq $entered[0].queuedTimestamp -and $record.timestamp -ge $entered[0].timestamp -and
                ($completed.Count -eq 0 -or $record.timestamp -le $completed[0].timestamp)) 'envelope-phase-outside-owned-action'
        }
        $lastTimestamp = [long]$record.timestamp
        $records.Add($record)
        $script:phaseEvidence += [ordered]@{ path = 'acceptance-phases/' + [IO.Path]::GetFileName($path); sha256 = Get-Digest $path }
    }
    return $records.ToArray()
}

function Require-CompletedCase($Supervisor, $Records, [string]$Id) {
    $events = @($Supervisor.events | Where-Object { $_.role -ceq 'nunit-host' -and $_.caseId -ceq $Id })
    $starts = @($events | Where-Object kind -CEQ 'case-start')
    $finishes = @($events | Where-Object kind -CEQ 'case-finished')
    $phases = @($Records | Where-Object caseId -CEQ $Id)
    Require-Condition ($starts.Count -eq 1 -and $finishes.Count -eq 1 -and $finishes[0].outcome -ceq 'passed' -and
        $phases.Count -gt 0 -and $starts[0].timestamp -le $phases[0].timestamp -and
        $finishes[0].timestamp -ge $phases[-1].timestamp) 'envelope-case-lifecycle-not-enclosed'
    foreach ($kind in @('queued', 'entered', 'completed')) {
        $actions = @($events | Where-Object kind -CEQ $kind)
        Require-Condition ($actions.Count -eq 3 -and (@($actions | Sort-Object ordinal | ForEach-Object ordinal) -join ',') -ceq '1,2,3') 'envelope-case-action-set-mismatch'
    }
}

function Read-OracleXml([string]$Path, $Embedded = $null) {
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.MaxCharactersInDocument = 16MB
    $xmlSource = if ($null -ne $Embedded) { [IO.StringReader]::new($Embedded) } else {
        [IO.FileStream]::new((Lock-Input $Path), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    }
    $reader = [Xml.XmlReader]::Create($xmlSource, $settings)
    try {
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
        return ,$document
    } finally { $reader.Dispose(); $xmlSource.Dispose() }
}

function Require-CleanupRawFailure {
    $document = Read-OracleXml $resultXml
    $runs = @($document.SelectNodes('/test-run[@result] | /NUnitXml/test-run[@result]'))
    foreach ($entry in @($document.SelectNodes('/NUnitXml/NUnitTestEvent'))) {
        $embedded = Read-OracleXml '' $entry.InnerText
        if ($embedded.DocumentElement.Name -ceq 'test-run' -and $embedded.DocumentElement.HasAttribute('result')) { $runs += $embedded.DocumentElement }
    }
    Require-Condition ($runs.Count -eq 1 -and $runs[0].GetAttribute('result') -ceq 'Failed' -and
        $runs[0].InnerText.Contains('framework-envelope-one-time-cleanup-failure', [StringComparison]::Ordinal)) 'cleanup-fault-nunit-run-not-failed'
    $leaves = @($runs[0].SelectNodes('.//test-case'))
    Require-ExactIds @($leaves | ForEach-Object { $_.GetAttribute('fullname') }) @($bindings | ForEach-Object fullName) 'cleanup-fault-nunit-case-set-mismatch'
    foreach ($leaf in $leaves) {
        $binding = @($bindings | Where-Object { $_.fullName -ceq $leaf.GetAttribute('fullname') })
        $ids = @($leaf.SelectNodes("properties/property[@name='FishAudioCaseId']"))
        Require-Condition ($leaf.GetAttribute('result') -ceq 'Passed' -and $ids.Count -le 1 -and
            ($ids.Count -eq 0 -or $ids[0].GetAttribute('value') -ceq $binding[0].canonicalId)) 'cleanup-fault-nunit-leaf-not-passed'
    }
    $platform = Read-OracleXml $trx
    $namespace = [Xml.XmlNamespaceManager]::new($platform.NameTable)
    $namespace.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $summaries = @($platform.SelectNodes('/t:TestRun/t:ResultSummary', $namespace))
    $rows = @($platform.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $namespace))
    $definitions = @($platform.SelectNodes('/t:TestRun/t:TestDefinitions/t:UnitTest', $namespace))
    Require-Condition ($summaries.Count -eq 1 -and $rows.Count -eq 3 -and $definitions.Count -eq 3) 'cleanup-fault-trx-shape-mismatch'
    $runErrors = @($summaries[0].SelectNodes('.//t:RunInfo', $namespace) |
        Where-Object { $_.GetAttribute('outcome') -cnotin @('', 'Passed', 'Completed') })
    Require-Condition ($summaries[0].GetAttribute('outcome') -cin @('Completed', 'Failed', 'Error', 'Aborted') -and
        $summaries[0].InnerText.Contains('framework-envelope-one-time-cleanup-failure', [StringComparison]::Ordinal) -and
        ($summaries[0].GetAttribute('outcome') -cne 'Completed' -or
            @($summaries[0].SelectNodes('.//t:ErrorInfo', $namespace)).Count -gt 0 -or $runErrors.Count -gt 0)) 'cleanup-fault-trx-run-not-failed'
    # Adapter 6.3.1 emits an exact fixture-summary error and a separate detailed
    # exception for this injected OneTimeTearDown failure. The summary has no
    # exception marker. Admit only that observed pair in THIS fault oracle;
    # ordinary NUnit/TRX admission still rejects the entire failed run.
    $fixtureErrors = @($runErrors | Where-Object {
        $_.GetAttribute('outcome') -ceq 'Error' -and
        $_.InnerText -ceq 'TearDown failed for test fixture NUnitCompatibility.FrameworkEnvelopeCases'
    })
    $injectedErrors = @($runErrors | Where-Object {
        $_.GetAttribute('outcome') -ceq 'Error' -and $_.InnerText.StartsWith(
            'TearDown : System.InvalidOperationException : framework-envelope-one-time-cleanup-failure',
            [StringComparison]::Ordinal)
    })
    Require-Condition ($runErrors.Count -eq 2 -and $fixtureErrors.Count -eq 1 -and $injectedErrors.Count -eq 1) 'cleanup-fault-unrelated-trx-run-error'
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $testIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $executionIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($definition in $definitions) {
        $methods = @($definition.SelectNodes('t:TestMethod', $namespace))
        $executions = @($definition.SelectNodes('t:Execution', $namespace))
        Require-Condition ($methods.Count -eq 1 -and $executions.Count -eq 1) 'cleanup-fault-trx-definition-invalid'
        $fullName = $methods[0].GetAttribute('className') + '.' + $definition.GetAttribute('name')
        $matches = @($rows | Where-Object { $_.GetAttribute('testId') -ceq $definition.GetAttribute('id') })
        Require-Condition ($seen.Add($fullName) -and $testIds.Add($definition.GetAttribute('id')) -and
            $executionIds.Add($executions[0].GetAttribute('id')) -and
            -not [string]::IsNullOrWhiteSpace($definition.GetAttribute('id')) -and
            -not [string]::IsNullOrWhiteSpace($executions[0].GetAttribute('id')) -and
            @($bindings | Where-Object fullName -CEQ $fullName).Count -eq 1 -and
            $matches.Count -eq 1 -and $matches[0].GetAttribute('outcome') -ceq 'Passed' -and
            $matches[0].GetAttribute('executionId') -ceq $executions[0].GetAttribute('id') -and
            $matches[0].GetAttribute('testName') -ceq $definition.GetAttribute('name')) 'cleanup-fault-trx-leaf-not-passed'
    }
}

function Write-RunSettings([string]$Path, [string]$OutputDirectory, [string[]]$FullNames) {
    $settings = [Xml.XmlWriterSettings]::new()
    $settings.Encoding = $utf8
    $settings.Indent = $true
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
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
            @('NumberOfTestWorkers', '1'), @('StopOnError', 'true'), @('PreFilter', 'false'),
            @('UseParentFQNForParametrizedTests', 'false'), @('IncludeStackTrace', 'false'),
            @('DumpXmlTestDiscovery', 'true'), @('DumpXmlTestResults', 'false'),
            @('TestOutputXml', $OutputDirectory), @('OutputXmlFolderMode', 'AsSpecified')
        )) { $writer.WriteElementString($pair[0], $pair[1]) }
        $terms = foreach ($fullName in $FullNames) {
            $regex = '\A' + [Text.RegularExpressions.Regex]::Escape($fullName) + '\z'
            $literal = $regex.Replace('\', '\\').Replace('"', '\"')
            'test =~ "' + $literal + '"'
        }
        $writer.WriteElementString('Where', '(' + ($terms -join ' or ') + ')')
        $writer.WriteEndElement()
        $writer.WriteEndElement()
        $writer.WriteEndDocument()
    } finally { $writer.Dispose(); $stream.Dispose() }
}

function Invoke-RawCommand([string]$Label, [string]$Executable, [string[]]$Arguments) {
    $directory = Join-Path $runRoot $Label
    [void][IO.Directory]::CreateDirectory($directory)
    Write-NewJson (Join-Path $directory 'argv.json') ([ordered]@{
        executable = $Executable; arguments = @($Arguments); workingDirectory = $snapshot
    })
    $process = [Diagnostics.Process]::new()
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.WorkingDirectory = $snapshot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    if ($envelope) {
        # The positive envelope has no fault variable, including an inherited
        # one. Fault scenarios pass their explicit value through the request.
        [void]$start.Environment.Remove('FISHAUDIO_FRAMEWORK_ACCEPTANCE_FAULT')
        [void]$start.Environment.Remove('FISHAUDIO_FRAMEWORK_ACCEPTANCE_EVIDENCE')
    }
    [void]$start.Environment.Remove('FISHAUDIO_FRAMEWORK_FINALIZATION_TEST_FAULT')
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    foreach ($key in $environment.Keys) { $start.Environment[$key] = [string]$environment[$key] }
    $watchFinalization = $finalizationWriteHang -and $Label -ceq 'driver-cli'
    if ($watchFinalization) { $start.Environment['FISHAUDIO_FRAMEWORK_FINALIZATION_TEST_FAULT'] = 'block-final-write' }
    $process.StartInfo = $start
    $outStream = $null
    $errStream = $null
    $stdout = $null
    $stderr = $null
    $exitTask = $null
    $started = $false
    $primary = $null
    $primaryCode = $null
    $cleanupFailed = $false
    $cleanupClock = $null
    $exitCode = $null
    $drained = $false
    $startedUtc = [DateTimeOffset]::UtcNow
    $exitTimestamp = $null
    $markerObservedTimestamp = $null
    try {
        $outStream = [IO.FileStream]::new((Join-Path $directory 'stdout.log'), [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write, [IO.FileShare]::Read, 65536, $true)
        $errStream = [IO.FileStream]::new((Join-Path $directory 'stderr.log'), [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write, [IO.FileShare]::Read, 65536, $true)
        $started = $process.Start()
        Require-Condition $started 'acceptance-process-not-started'
        $stdout = $process.StandardOutput.BaseStream.CopyToAsync($outStream)
        $stderr = $process.StandardError.BaseStream.CopyToAsync($errStream)
        $exitTask = $process.WaitForExitAsync()
        # The real driver's case/action/startup watchdog owns execution bounds.
        # Normal execution has no harness performance deadline. The fault-only
        # stop-loss starts after the real final-write marker; reaching it fails
        # acceptance and kills this CLI tree, rather than granting its owner a
        # new execution or cleanup allowance.
        while (-not ($exitTask.IsCompleted -and $stdout.IsCompleted -and $stderr.IsCompleted)) {
            foreach ($task in @($exitTask, $stdout, $stderr)) {
                if ($task.IsFaulted -or $task.IsCanceled) { [void]$task.GetAwaiter().GetResult() }
            }
            if ($exitTask.IsCompleted) {
                [void]$exitTask.GetAwaiter().GetResult()
                if ($null -eq $exitTimestamp) { $exitTimestamp = [Diagnostics.Stopwatch]::GetTimestamp() }
                if ($null -eq $cleanupClock) { $cleanupClock = [Diagnostics.Stopwatch]::StartNew() }
                Require-Condition ($cleanupClock.ElapsedMilliseconds -lt 10000) 'acceptance-process-drain-deadline'
            }
            if ($watchFinalization -and [IO.File]::Exists($finalizationMarkerPath)) {
                if ($null -eq $markerObservedTimestamp) { $markerObservedTimestamp = [Diagnostics.Stopwatch]::GetTimestamp() }
                if (([Diagnostics.Stopwatch]::GetTimestamp() - $markerObservedTimestamp) * 1000.0 /
                    [Diagnostics.Stopwatch]::Frequency -ge 20000) {
                    $script:finalizationSafetyWatchTriggered = $true
                    Require-Condition $false 'finalization-fault-producer-stop-loss'
                }
            }
            [Threading.Thread]::Sleep(25)
        }
        [void]$exitTask.GetAwaiter().GetResult()
        if ($null -eq $exitTimestamp) { $exitTimestamp = [Diagnostics.Stopwatch]::GetTimestamp() }
        [void]$stdout.GetAwaiter().GetResult()
        [void]$stderr.GetAwaiter().GetResult()
        $exitCode = $process.ExitCode
        $drained = $true
    } catch { $primary = $_; $primaryCode = $script:reason }
    finally {
        if ($null -ne $primary -and $started) {
            if ($null -eq $cleanupClock) { $cleanupClock = [Diagnostics.Stopwatch]::StartNew() }
            try {
                if (-not $process.HasExited) { $process.Kill($true) }
                $remaining = [TimeSpan]::FromSeconds(10) - $cleanupClock.Elapsed
                Require-Condition ($remaining -gt [TimeSpan]::Zero) 'acceptance-process-cleanup-deadline'
                $tasks = [Threading.Tasks.Task[]]@(@($exitTask, $stdout, $stderr) | Where-Object { $null -ne $_ })
                [void][Threading.Tasks.Task]::WhenAll($tasks).WaitAsync($remaining).GetAwaiter().GetResult()
                $exitCode = $process.ExitCode
                $drained = $null -ne $stdout -and $null -ne $stderr
            } catch { $cleanupFailed = $true }
        }
        try {
            if ($null -ne $outStream) { $outStream.Dispose() }
            if ($null -ne $errStream) { $errStream.Dispose() }
            if ($Label -ceq 'driver-cli') {
                $script:driverExitTimestamp = $exitTimestamp
                $script:finalizationMarkerObservedTimestamp = $markerObservedTimestamp
            }
            Write-NewJson (Join-Path $directory 'process.json') ([ordered]@{
                schemaVersion = 1; kind = 'acceptance-cli-process'; complete = ($null -eq $primary -and $drained)
                startedUtc = $startedUtc; finishedUtc = [DateTimeOffset]::UtcNow; exitCode = $exitCode
                stdoutDrained = $drained; stderrDrained = $drained; cleanupFailed = $cleanupFailed
                exitTimestamp = $exitTimestamp; frequency = [Diagnostics.Stopwatch]::Frequency
                finalizationStopLossEnabled = $watchFinalization; finalizationMarkerObservedTimestamp = $markerObservedTimestamp
                finalizationStopLossTriggered = $finalizationSafetyWatchTriggered
                # These facts describe this CLI process and its pipes only.
                # The driver must supply its separate owned-Job completion proof.
                jobOwnershipClaim = $false
            })
        } catch { if ($null -eq $primary) { $primary = $_ } }
        try { $process.Dispose() } catch { if ($null -eq $primary) { $primary = $_; $primaryCode = $script:reason } }
    }
    if ($null -ne $primary) { $script:reason = $primaryCode; throw $primary }
    return $exitCode
}

try {
    Require-Condition ($IsWindows -and $PSVersionTable.PSEdition -ceq 'Core') 'windows-core-powershell-required'
    Require-Condition ($SourceTree -cmatch '^([a-f0-9]{40}|[a-f0-9]{64})$') 'invalid-source-tree'
    $snapshot = Require-Directory $SnapshotRoot
    $manifestPath = Lock-Input $DiscoveryManifest
    $discoveryRoot = [IO.Path]::GetDirectoryName($manifestPath)
    $manifest = Read-Json $manifestPath
    Require-Condition ($manifest.schemaVersion -eq 1 -and $manifest.kind -ceq 'unified-test-session' -and
        $manifest.complete -eq $true -and $manifest.mode -ceq 'Discover' -and $manifest.platform -ceq 'VSTest' -and
        $manifest.configuration -ceq 'Release' -and $manifest.baselineClaim -eq $false -and
        $manifest.source.snapshot -eq $true -and $manifest.source.tree -ceq $SourceTree) 'discovery-manifest-source-or-mode-mismatch'
    if ($env:FISHAUDIO_SNAPSHOT_CONTEXT) {
        $context = Read-Json (Lock-Input $env:FISHAUDIO_SNAPSHOT_CONTEXT)
        Require-Condition ($context.schemaVersion -eq 1 -and $context.tree -ceq $SourceTree -and
            [string]::Equals((Require-Directory $context.root), $snapshot, [StringComparison]::OrdinalIgnoreCase)) 'snapshot-context-mismatch'
    }
    $plugin = @($manifest.assemblies | Where-Object key -CEQ 'plugin')
    Require-Condition ($plugin.Count -eq 1 -and -not $plugin[0].executed) 'discovery-plugin-definition-missing'
    $assembly = Lock-Input (Resolve-File $snapshot ([string]$plugin[0].assembly))
    Require-Condition ((Get-Digest $assembly) -ceq $plugin[0].sha256) 'discovery-assembly-hash-mismatch'
    $catalogPath = Lock-Input (Resolve-File $discoveryRoot ([string]$plugin[0].catalog))
    Require-Condition ((Get-Digest $catalogPath) -ceq $plugin[0].catalogSha256) 'discovery-catalog-hash-mismatch'
    $catalog = Read-Json $catalogPath
    Require-Condition ($catalog.schemaVersion -eq 1 -and $catalog.kind -ceq 'nunit-catalog' -and $catalog.complete -eq $true) 'discovery-catalog-incomplete'
    $phase = @($manifest.phases | Where-Object name -CEQ 'discover-plugin')
    Require-Condition ($phase.Count -eq 1 -and $phase[0].complete -eq $true -and $phase[0].exitCode -eq 0) 'discovery-phase-incomplete'
    $rawDiscovery = Lock-Input (Resolve-File $discoveryRoot (Join-Path ([string]$phase[0].directory) 'discovery.dump'))
    $tool = Lock-Input (Resolve-File $snapshot 'tools/FishAudio.Testing/bin/Release/net10.0-windows/FishAudio.Testing.dll')
    Require-Condition ((Get-Digest $tool) -ceq $manifest.toolSha256) 'discovery-tool-hash-mismatch'
    $shell = Lock-Input ((Get-Process -Id $PID).Path)
    $dotnetCandidate = $env:FISHAUDIO_DOTNET_EXECUTABLE
    if ([string]::IsNullOrWhiteSpace($dotnetCandidate)) {
        $dotnetCandidate = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    }
    $dotnet = Lock-Input $dotnetCandidate
    $launcher = Lock-Input (Resolve-File $snapshot 'tools/Invoke-TestProcess.ps1')
    $bindings = foreach ($id in $selectedIds) {
        $matches = @($catalog.cases | Where-Object canonicalId -CEQ $id)
        Require-Condition ($matches.Count -eq 1 -and -not [string]::IsNullOrWhiteSpace($matches[0].fullName)) 'required-compatibility-case-missing-or-ambiguous'
        [ordered]@{ canonicalId = $id; fullName = [string]$matches[0].fullName }
    }

    $destination = Get-AbsolutePath $EvidenceRoot
    Require-Condition (-not [IO.File]::Exists($destination) -and -not [IO.Directory]::Exists($destination)) 'evidence-root-not-fresh'
    [void](Require-Directory ([IO.Path]::GetDirectoryName($destination)))
    Require-Condition (-not [string]::Equals($destination, $snapshot, [StringComparison]::OrdinalIgnoreCase) -and
        -not (Test-ContainedPath $destination $snapshot)) 'evidence-root-inside-snapshot'
    [void][IO.Directory]::CreateDirectory($destination)
    $runRoot = Require-Directory $destination
    $runtimeEvidence = Join-Path $runRoot 'runtime-evidence'
    [void][IO.Directory]::CreateDirectory($runtimeEvidence)
    $outputDirectory = Join-Path $runRoot 'execution'
    $finalizationMarkerPath = Join-Path $outputDirectory 'finalization-write-entered.json'
    $xmlDirectory = Join-Path $outputDirectory 'nunit'
    $resultXml = Join-Path $xmlDirectory ([IO.Path]::GetFileNameWithoutExtension($assembly) + '.xml')
    $trx = Join-Path $outputDirectory 'trx/results.trx'
    $settings = Join-Path $runRoot 'execution.runsettings'
    Write-RunSettings $settings $xmlDirectory @($bindings | ForEach-Object { $_.fullName })
    $environment = [ordered]@{
        FISHAUDIO_TEST_REPOSITORY_ROOT = $snapshot
        FISHAUDIO_COMPAT_EVIDENCE_ROOT = $runtimeEvidence
        FISHAUDIO_COMPAT_PARALLEL_PROOF = '0'
        FISHAUDIO_PWSH_EXECUTABLE = $shell
        FISHAUDIO_DOTNET_EXECUTABLE = $dotnet
        DOTNET_CLI_UI_LANGUAGE = 'en'
        VSLANG = '1033'
    }
    if ($envelope) {
        $phaseDirectory = Join-Path $runRoot 'acceptance-phases'
        [void][IO.Directory]::CreateDirectory($phaseDirectory)
        $environment['FISHAUDIO_FRAMEWORK_ACCEPTANCE_EVIDENCE'] = $phaseDirectory
        if (-not $expectedCasesPass) { $environment['FISHAUDIO_FRAMEWORK_ACCEPTANCE_FAULT'] = $fault }
    }
    $requestPath = Join-Path $runRoot 'request.json'
    Write-NewJson $requestPath ([ordered]@{
        schemaVersion = 1; runId = $runId; sourceTree = $SourceTree; workingDirectory = $snapshot
        shellExecutable = $shell; dotnetExecutable = $dotnet; launcherScript = $launcher
        testAssembly = $assembly; testAssemblySha256 = [string]$plugin[0].sha256
        runSettings = $settings; discoveryPath = $rawDiscovery; resultXmlPath = $resultXml; trxPath = $trx
        outputDirectory = $outputDirectory; cases = @($bindings); maximumConcurrency = 1; requireOrder = $true
        environment = $environment
    })
    Write-NewJson (Join-Path $runRoot 'selection.json') @{ schemaVersion = 1; ids = $selectedIds }
    Require-Condition (-not [IO.Directory]::Exists($outputDirectory) -and -not [IO.File]::Exists($outputDirectory)) 'driver-output-must-be-absent'
    $stage = 'driver-exit'
    $driverExit = Invoke-RawCommand 'driver-cli' $dotnet @($tool, 'framework-run', '--request', $requestPath)
    $driverFinishedTimestamp = [Diagnostics.Stopwatch]::GetTimestamp()
    if ($expectedPositive) { Require-Condition ($driverExit -eq 0) 'positive-driver-exit' }
    else { Require-Condition ($driverExit -ne 0) 'expected-fault-driver-passed' }

    $stage = 'supervisor-evidence'
    $finalizationMarker = $null
    if ($finalizationWriteHang) {
        $stage = 'finalization-fault-marker'
        $finalizationMarker = Read-Json (Lock-Input $finalizationMarkerPath)
        Require-Condition ($finalizationMarker.schemaVersion -eq 1 -and
            $finalizationMarker.kind -ceq 'framework-finalization-test-entered' -and
            $finalizationMarker.testFault -ceq 'block-final-write' -and
            $finalizationMarker.runId -ceq $runId -and $finalizationMarker.sourceTree -ceq $SourceTree -and
            $finalizationMarker.baselineClaim -eq $false) 'finalization-fault-marker-foreign'
        # This is the actual execution snapshot at the write seam, NOT a
        # published supervisor or independently admissible execution receipt.
        $supervisor = $finalizationMarker.execution
        $supervisorReference = $null
    } else {
        $supervisor = Read-Json (Join-Path $outputDirectory 'supervisor.json')
    }
    Require-Condition ($supervisor.schemaVersion -eq 1 -and $supervisor.kind -ceq 'framework-execution' -and
        $supervisor.runId -ceq $runId -and $supervisor.sourceTree -ceq $SourceTree -and
        $supervisor.testAssemblySha256 -ceq $plugin[0].sha256 -and
        $supervisor.baselineClaim -eq $false) 'supervisor-incomplete-or-foreign'
    Require-ExactIds $supervisor.selectedIds $selectedIds 'supervisor-selection-mismatch'
    Require-Condition ($supervisor.target.started -eq $true -and $supervisor.target.pid -gt 0 -and
        $supervisor.process.launcherPid -gt 0 -and
        $supervisor.process.launcherExited -eq $true -and $supervisor.process.stdoutDrained -eq $true -and
        $supervisor.process.stderrDrained -eq $true -and $supervisor.process.ownedActiveProcesses -eq 0) 'supervisor-process-envelope-incomplete'
    Require-Condition ([string]::Equals((Get-AbsolutePath $supervisor.rawEvidence.resultXmlPath), $resultXml, [StringComparison]::OrdinalIgnoreCase) -and
        [string]::Equals((Get-AbsolutePath $supervisor.rawEvidence.trxPath), $trx, [StringComparison]::OrdinalIgnoreCase)) 'supervisor-raw-evidence-mismatch'
    $phaseRecords = @()
    if ($envelope) {
        $stage = 'lifecycle-phases'
        $reason = 'envelope-phase-evidence-invalid'
        $phaseRecords = @(Read-EnvelopePhases $phaseDirectory $supervisor)
    }
    if ($expectedCasesPass) {
        Require-Condition ($supervisor.state.complete -eq $true -and $null -eq $supervisor.state.failureStage -and
            $null -eq $supervisor.failureCode -and $supervisor.target.exitCode -eq 0 -and
            $supervisor.rawEvidence.verified -eq $true) 'positive-supervisor-incomplete'
        Require-ExactIds $supervisor.state.passedIds $selectedIds 'supervisor-passed-set-mismatch'
        foreach ($field in @('failedIds', 'timedOutIds', 'interruptedIds', 'notRunIds', 'activeIds')) {
            Require-Condition (@($supervisor.state.$field).Count -eq 0) 'supervisor-nonpassing-case-state'
        }
        if ($envelope) {
            foreach ($id in $selectedIds) { Require-CompletedCase $supervisor $phaseRecords $id }
            $cleanupEvents = @($supervisor.events | Where-Object { $_.role -ceq 'nunit-host' -and $_.kind -ceq 'cleanup-complete' })
            Require-Condition ($cleanupEvents.Count -eq 1 -and $cleanupEvents[0].timestamp -ge $phaseRecords[-1].timestamp) 'envelope-fixture-cleanup-not-enclosed'
        }
        $stage = 'independent-raw-verification'
        [void](Lock-Input $resultXml)
        [void](Lock-Input $trx)
        Require-Condition ((Get-Digest $resultXml) -ceq $supervisor.rawEvidence.resultXmlSha256 -and
            (Get-Digest $trx) -ceq $supervisor.rawEvidence.trxSha256) 'supervisor-raw-digest-mismatch'
        $terminalPath = Join-Path $runRoot 'verified-terminal.json'
        $verifyExit = Invoke-RawCommand 'raw-verification-cli' $dotnet @($tool, 'evidence-verify', '--catalog', $catalogPath,
            '--selection', (Join-Path $runRoot 'selection.json'), '--result', $resultXml, '--trx', $trx, '--output', $terminalPath)
        Require-Condition ($verifyExit -eq 0) 'independent-raw-verification-failed'
        $terminal = Read-Json $terminalPath
        Require-Condition ($terminal.schemaVersion -eq 1 -and $terminal.kind -ceq 'nunit-terminal' -and
            $terminal.complete -eq $true -and $terminal.passedCount -eq $selectedIds.Count) 'independent-terminal-incomplete'
        Require-ExactIds @($terminal.cases | ForEach-Object canonicalId) $selectedIds 'independent-terminal-set-mismatch'
        $terminalReference = 'verified-terminal.json'
        if ($finalizationWriteHang) {
            $stage = 'finalization-fault-oracle'
            $anchor = [long]$finalizationMarker.cleanupAnchorTimestamp
            $frequency = [long]$finalizationMarker.frequency
            $entered = [long]$finalizationMarker.enteredTimestamp
            Require-Condition ($frequency -eq [Diagnostics.Stopwatch]::Frequency -and $anchor -ge 0 -and
                $supervisor.finalization.cleanupAnchorTimestamp -eq $anchor -and
                $supervisor.finalization.frequency -eq $frequency -and $entered -ge $anchor -and
                $null -ne $driverExitTimestamp -and $null -ne $finalizationMarkerObservedTimestamp -and
                $driverExitTimestamp -ge $entered -and -not $finalizationSafetyWatchTriggered) 'finalization-fault-clock-or-stop-loss'
            $targetExits = @($supervisor.events | Where-Object { $_.role -ceq 'execution-launcher' -and $_.kind -ceq 'target-exited' })
            Require-Condition ($targetExits.Count -eq 1 -and $targetExits[0].exitCode -eq 0 -and
                $targetExits[0].targetPid -eq $supervisor.target.pid -and
                $targetExits[0].timestamp -le $anchor) 'finalization-fault-anchor-before-real-exit'
            $anchorToEnteredMilliseconds = ($entered - $anchor) * 1000.0 / $frequency
            $anchorToExitMilliseconds = ($driverExitTimestamp - $anchor) * 1000.0 / $frequency
            # Observed CLI exit includes OS scheduling and is not a new
            # performance deadline or an extension of the producer's original
            # ten-second allowance. Fake-clock cases prove the exact remainder;
            # this real CLI control proves that final-write uses that envelope.
            Require-Condition ($anchorToEnteredMilliseconds -ge 0 -and $anchorToEnteredMilliseconds -lt 10000 -and
                $anchorToExitMilliseconds -ge 10000 -and
                $driverExit -eq 1) 'finalization-fault-original-ten-second-anchor-not-observed'
            $finalPath = Join-Path $outputDirectory 'supervisor.json'
            Require-Condition (-not [IO.File]::Exists($finalPath) -and -not [IO.Directory]::Exists($finalPath)) 'finalization-fault-published-supervisor'
            Write-NewJson (Join-Path $runRoot 'fault-oracle.json') ([ordered]@{
                schemaVersion = 1; kind = 'framework-expected-fault-oracle'; complete = $true; scenario = $Scenario
                expectedFault = $fault; underlyingExecutionPassed = $false; underlyingCasesPassed = $true
                driverExitCode = $driverExit; publishedSupervisor = $false; independentRawVerificationExitCode = $verifyExit
                enteredMarker = 'execution/finalization-write-entered.json'; enteredMarkerSha256 = Get-Digest $finalizationMarkerPath
                cleanupAnchorTimestamp = $anchor; frequency = $frequency; enteredTimestamp = $entered
                cliExitObservedTimestamp = $driverExitTimestamp; anchorToEnteredMilliseconds = $anchorToEnteredMilliseconds
                anchorToCliExitMilliseconds = $anchorToExitMilliseconds; requiredCleanupAllowanceMilliseconds = 10000
                outerStopLossTriggered = $finalizationSafetyWatchTriggered
                outerStopLossAfterMarkerMilliseconds = 20000; baselineClaim = $false
            })
            $faultOracle = 'fault-oracle.json'
        }
    } else {
        $stage = 'expected-fault-oracle'
        Require-Condition ($supervisor.state.complete -eq $false -and $supervisor.rawEvidence.verified -eq $false -and
            -not [string]::IsNullOrWhiteSpace($supervisor.failureCode) -and
            @($supervisor.state.activeIds).Count -eq 0) 'expected-fault-execution-not-rejected'
        $actionToExitMilliseconds = $null
        $rawRejectionExit = $null
        if ($Scenario -in @('BodyHang', 'TeardownHang')) {
            Require-ExactIds $supervisor.state.passedIds @($selectedIds[0]) 'hang-passed-prefix-mismatch'
            Require-ExactIds $supervisor.state.timedOutIds @($selectedIds[1]) 'hang-timeout-owner-mismatch'
            Require-ExactIds $supervisor.state.notRunIds @($selectedIds[2]) 'hang-not-run-suffix-mismatch'
            foreach ($field in @('failedIds', 'interruptedIds')) {
                Require-Condition (@($supervisor.state.$field).Count -eq 0) 'hang-other-case-outcome'
            }
            Require-Condition ($supervisor.state.failureStage -ceq 'action' -and
                $supervisor.state.failureCaseId -ceq $selectedIds[1] -and $supervisor.state.terminationRequired -eq $true -and
                $supervisor.state.warningCount -ge 1) 'hang-hard-action-timeout-not-observed'
            Require-CompletedCase $supervisor $phaseRecords $selectedIds[0]
            $ordinal = if ($Scenario -eq 'BodyHang') { 2 } else { 3 }
            $openRoot = @($supervisor.events | Where-Object { $_.role -ceq 'nunit-host' -and $_.caseId -ceq $selectedIds[1] -and
                $_.kind -ceq 'queued' -and $_.ordinal -eq $ordinal })
            Require-Condition ($openRoot.Count -eq 1 -and @($supervisor.events | Where-Object {
                $_.role -ceq 'nunit-host' -and $_.caseId -ceq $selectedIds[1] -and
                (($_.kind -ceq 'completed' -and $_.ordinal -eq $ordinal) -or
                    ($_.kind -ceq 'case-finished' -and $_.outcome -ceq 'passed')) }).Count -eq 0 -and
                @($supervisor.events | Where-Object { $_.role -ceq 'nunit-host' -and $_.caseId -ceq $selectedIds[2] }).Count -eq 0) 'hang-open-root-or-suffix-mismatch'
            $actionToExitMilliseconds = ($driverFinishedTimestamp - [long]$openRoot[0].queuedTimestamp) * 1000.0 / [Diagnostics.Stopwatch]::Frequency
            Require-Condition ($actionToExitMilliseconds -ge 120000 -and $supervisor.elapsedMilliseconds -ge 120000 -and
                @($supervisor.warnings | Where-Object { $_.stage -ceq 'action' -and
                    $_.timestamp -ge ([long]$openRoot[0].queuedTimestamp + 20L * [Diagnostics.Stopwatch]::Frequency) }).Count -ge 1) 'hang-real-deadline-or-warning-not-observed'
        } elseif ($Scenario -in @('ConstructorFailure', 'OneTimeSetupFailure')) {
            Require-ExactIds $supervisor.state.notRunIds $selectedIds 'fixture-fault-not-run-set-mismatch'
            foreach ($field in @('passedIds', 'failedIds', 'timedOutIds', 'interruptedIds')) {
                Require-Condition (@($supervisor.state.$field).Count -eq 0) 'fixture-fault-fabricated-case-outcome'
            }
            Require-Condition (@($supervisor.events | Where-Object { $_.role -ceq 'nunit-host' -and
                $_.kind -cin @('case-start', 'case-finished') }).Count -eq 0) 'fixture-fault-fabricated-case-lifecycle'
        } else {
            Require-ExactIds $supervisor.state.passedIds $selectedIds 'cleanup-fault-leaf-pass-set-mismatch'
            foreach ($field in @('failedIds', 'timedOutIds', 'interruptedIds', 'notRunIds')) {
                Require-Condition (@($supervisor.state.$field).Count -eq 0) 'cleanup-fault-case-outcome-mismatch'
            }
            foreach ($id in $selectedIds) { Require-CompletedCase $supervisor $phaseRecords $id }
            $reason = 'cleanup-fault-raw-evidence-invalid'
            Require-CleanupRawFailure
            $terminalPath = Join-Path $runRoot 'rejected-terminal.json'
            $rawRejectionExit = Invoke-RawCommand 'raw-verification-cli' $dotnet @($tool, 'evidence-verify', '--catalog', $catalogPath,
                '--selection', (Join-Path $runRoot 'selection.json'), '--result', $resultXml, '--trx', $trx, '--output', $terminalPath)
            Require-Condition ($rawRejectionExit -ne 0 -and -not [IO.File]::Exists($terminalPath)) 'cleanup-fault-raw-parser-accepted-run'
        }
        Write-NewJson (Join-Path $runRoot 'fault-oracle.json') ([ordered]@{
            schemaVersion = 1; kind = 'framework-expected-fault-oracle'; complete = $true; scenario = $Scenario
            expectedFault = $fault; underlyingExecutionPassed = $false; driverExitCode = $driverExit
            supervisorComplete = $supervisor.state.complete; rawEvidenceVerified = $supervisor.rawEvidence.verified
            failureStage = $supervisor.state.failureStage; failureCaseId = $supervisor.state.failureCaseId
            actionToCliExitMilliseconds = $actionToExitMilliseconds; requiredActionDeadlineMilliseconds = $(if ($Scenario -in @('BodyHang', 'TeardownHang')) { 120000 } else { $null })
            independentRawRejectionExitCode = $rawRejectionExit; baselineClaim = $false
        })
        $faultOracle = 'fault-oracle.json'
    }
    Write-NewJson (Join-Path $runRoot 'acceptance.json') ([ordered]@{
        schemaVersion = 1; kind = 'framework-execution-acceptance'; complete = $true; runId = $runId
        sourceTree = $SourceTree; selectedIds = $selectedIds; driverExitCode = $driverExit; scenario = $Scenario
        expectedPositive = $expectedPositive; underlyingExecutionPassed = $expectedPositive
        supervisor = $supervisorReference; terminal = $terminalReference; faultOracle = $faultOracle
        phaseEvidence = $phaseEvidence; baselineClaim = $false
        discoveryManifestSha256 = Get-Digest $manifestPath; finishedUtc = [DateTimeOffset]::UtcNow
    })
    if ($Scenario -eq 'Compatibility') { Write-Output 'framework-acceptance: passed (three existing compatibility leaves; no baseline claim).' }
    elseif ($expectedPositive) { Write-Output 'framework-acceptance: passed (three ordered lifecycle leaves; no baseline claim).' }
    else { Write-Output ('framework-acceptance: passed expected fault=' + $fault + ' (failed execution retained; no baseline claim).') }
} catch {
    if ($null -ne $runRoot) {
        try { Write-NewText (Join-Path $runRoot 'private-failure.txt') ($_ | Out-String) } catch { }
        try {
            Write-NewJson (Join-Path $runRoot 'failure.json') ([ordered]@{
                schemaVersion = 1; kind = 'framework-execution-acceptance-failure'; complete = $false
                runId = $runId; sourceTree = $SourceTree; selectedIds = $selectedIds
                scenario = $Scenario; expectedPositive = $expectedPositive; stage = $stage; reason = $reason; driverExitCode = $driverExit
                baselineClaim = $false; finishedUtc = [DateTimeOffset]::UtcNow
            })
        } catch { }
    }
    [Console]::Error.WriteLine('framework-acceptance: failed stage=' + $stage + ' reason=' + $reason + '.')
    exit 1
} finally {
    $releaseFailed = $false
    foreach ($stream in $locks) {
        try { $stream.Dispose() } catch { $releaseFailed = $true }
    }
    if ($releaseFailed) {
        [Console]::Error.WriteLine('framework-acceptance: input-lock-release-failed.')
        exit 1
    }
}
# Expected-fault scenarios deliberately preserve a nonzero child exit. The
# acceptance contract has its own result after the oracle and lock cleanup.
exit 0
