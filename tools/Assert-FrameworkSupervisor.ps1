#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][string]$RunId,
    [AllowNull()][string]$SourceTree,
    [Parameter(Mandatory)][string]$SourceFingerprint,
    [Parameter(Mandatory)][string]$AssemblySha256,
    [Parameter(Mandatory)][string[]]$CaseIds,
    [Parameter(Mandatory)][string]$ResultXmlSha256,
    [Parameter(Mandatory)][string]$TrxSha256,
    [AllowNull()][string]$ExecutionRequestPath,
    [AllowNull()][string]$ExecutionRequestSha256,
    [Nullable[int]]$ExpectedMaximumConcurrency,
    [Nullable[bool]]$ExpectedRequireOrder
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Require-Supervisor([bool]$Condition) {
    if (-not $Condition) { throw 'framework-supervisor-evidence-rejected' }
}
function Assert-SupervisorIds($Actual, [string[]]$Expected) {
    Require-Supervisor ($Actual -is [Array])
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($id in @($Actual)) {
        Require-Supervisor ($id -is [string] -and -not [string]::IsNullOrWhiteSpace($id) -and $ids.Add($id))
    }
    Require-Supervisor ($ids.Count -eq $Expected.Count)
    foreach ($id in $Expected) { Require-Supervisor ($ids.Contains($id)) }
}
function Test-SupervisorPath([string]$Left, [string]$Right) {
    return [IO.Path]::IsPathFullyQualified($Left) -and [IO.Path]::IsPathFullyQualified($Right) -and
        [IO.Path]::GetFullPath($Left).Equals([IO.Path]::GetFullPath($Right), [StringComparison]::OrdinalIgnoreCase)
}
function Assert-SupervisorPolicy($Record) {
    $requestStream = $null
    try {
        Require-Supervisor ([IO.Path]::IsPathFullyQualified($ExecutionRequestPath) -and
            $ExecutionRequestSha256 -cmatch '^[a-f0-9]{64}$' -and
            $ExpectedMaximumConcurrency -is [int] -and $ExpectedMaximumConcurrency -ge 1 -and
            $ExpectedMaximumConcurrency -le 4 -and $ExpectedRequireOrder -is [bool] -and
            (-not $ExpectedRequireOrder -or $ExpectedMaximumConcurrency -eq 1))
        $file = Get-Item -LiteralPath $ExecutionRequestPath -Force
        Require-Supervisor (-not $file.PSIsContainer -and $file.Length -gt 0 -and $file.Length -le 16777216 -and
            ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0)
        $requestStream = [IO.File]::Open($ExecutionRequestPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $digest = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($requestStream))
        Require-Supervisor ($digest -ceq $ExecutionRequestSha256 -and $Record.requestSha256 -is [string] -and
            $Record.requestSha256 -cmatch '^[a-f0-9]{64}$' -and $Record.requestSha256 -ceq $digest)
        $requestStream.Position = 0
        $reader = [IO.StreamReader]::new($requestStream, [Text.UTF8Encoding]::new($false, $true), $false, 4096, $true)
        try { $request = $reader.ReadToEnd() | ConvertFrom-Json -Depth 40 } finally { $reader.Dispose() }
        Require-Supervisor ($request.schemaVersion -eq 1 -and $request.runId -ceq $RunId -and
            $request.sourceFingerprint -ceq $SourceFingerprint -and $request.testAssemblySha256 -ceq $AssemblySha256 -and
            ($request.maximumConcurrency -is [int] -or $request.maximumConcurrency -is [long]) -and
            $request.maximumConcurrency -eq $ExpectedMaximumConcurrency -and
            $request.requireOrder -is [bool] -and $request.requireOrder -eq $ExpectedRequireOrder -and
            ($Record.policy.maximumConcurrency -is [int] -or $Record.policy.maximumConcurrency -is [long]) -and
            $Record.policy.maximumConcurrency -eq $ExpectedMaximumConcurrency -and
            $Record.policy.requireOrder -is [bool] -and $Record.policy.requireOrder -eq $ExpectedRequireOrder)
        if ([string]::IsNullOrEmpty($SourceTree)) { Require-Supervisor ($null -eq $request.sourceTree) }
        else { Require-Supervisor ($request.sourceTree -ceq $SourceTree) }
        Require-Supervisor ($request.cases -is [Array] -and $request.cases.Count -eq $CaseIds.Count -and
            $request.cases.Count -le 100000)
        $bindings = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        for ($index = 0; $index -lt $CaseIds.Count; $index++) {
            $case = $request.cases[$index]
            Require-Supervisor ($case.canonicalId -ceq $CaseIds[$index] -and $case.fullName -is [string] -and
                $case.fullName.Length -gt 0 -and $case.fullName.Length -le 4096 -and $case.fullName -cnotmatch '\p{Cc}' -and
                $bindings.TryAdd($case.canonicalId, $case.fullName) -and $names.Add($case.fullName))
        }
        Require-Supervisor ([IO.Path]::IsPathFullyQualified($request.outputDirectory))
        $prefix = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($request.outputDirectory)) + [IO.Path]::DirectorySeparatorChar
        foreach ($pair in @(@($request.resultXmlPath, $Record.rawEvidence.resultXmlPath), @($request.trxPath, $Record.rawEvidence.trxPath))) {
            Require-Supervisor ((Test-SupervisorPath $pair[0] $pair[1]) -and
                [IO.Path]::GetFullPath($pair[0]).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase))
        }
        Require-Supervisor ($Record.launch.invocationSha256 -cmatch '^[a-f0-9]{64}$' -and
            $Record.events -is [Array] -and $Record.events.Count -ge 6 -and $Record.events.Count -le 1000000)
        $active = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $starts = [Collections.Generic.List[string]]::new()
        $finished = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $launcherKinds = [Collections.Generic.List[string]]::new()
        $hostPid = 0; $ready = 0; $cleanup = 0; $nonce = $null
        foreach ($event in $Record.events) {
            Require-Supervisor ($event.schemaVersion -eq 1 -and $event.runId -ceq $RunId -and
                $event.nonce -cmatch '^[a-f0-9]{64}$' -and $event.pid -gt 0)
            if ([string]::IsNullOrEmpty($SourceTree)) { Require-Supervisor ($null -eq $event.sourceTree) }
            else { Require-Supervisor ($event.sourceTree -ceq $SourceTree) }
            if ($null -eq $nonce) { $nonce = $event.nonce }
            Require-Supervisor ($event.nonce -ceq $nonce)
            if ($event.role -ceq 'execution-launcher') {
                Require-Supervisor ($event.pid -eq $Record.process.launcherPid -and $event.targetPid -eq $Record.target.pid -and
                    (($event.kind -ceq 'target-started' -and $null -eq $event.exitCode) -or
                     ($event.kind -ceq 'target-exited' -and $event.exitCode -eq 0)))
                $launcherKinds.Add($event.kind)
                continue
            }
            Require-Supervisor ($event.role -ceq 'nunit-host' -and $event.assemblySha256 -ceq $AssemblySha256)
            if ($hostPid -eq 0) { $hostPid = $event.pid }
            Require-Supervisor ($event.pid -eq $hostPid)
            switch -CaseSensitive ($event.kind) {
                'ready' { $ready++; Require-Supervisor ($ready -eq 1 -and $starts.Count -eq 0 -and $cleanup -eq 0) }
                'cleanup-complete' { $cleanup++; Require-Supervisor ($ready -eq 1 -and $cleanup -eq 1 -and $active.Count -eq 0 -and $finished.Count -eq $CaseIds.Count) }
                'case-start' {
                    Require-Supervisor ($ready -eq 1 -and $cleanup -eq 0 -and $bindings.ContainsKey($event.caseId) -and
                        $event.fullName -ceq $bindings[$event.caseId] -and -not $finished.Contains($event.caseId) -and $active.Add($event.caseId))
                    $starts.Add($event.caseId)
                    Require-Supervisor ($active.Count -le $ExpectedMaximumConcurrency)
                }
                'case-finished' {
                    Require-Supervisor ($bindings.ContainsKey($event.caseId) -and $event.fullName -ceq $bindings[$event.caseId] -and
                        $event.outcome -ceq 'passed' -and $active.Remove($event.caseId) -and $finished.Add($event.caseId))
                }
            }
        }
        Require-Supervisor ($ready -eq 1 -and $cleanup -eq 1 -and $active.Count -eq 0 -and
            $starts.Count -eq $CaseIds.Count -and $finished.Count -eq $CaseIds.Count -and
            $launcherKinds.Count -eq 2 -and $launcherKinds[0] -ceq 'target-started' -and $launcherKinds[1] -ceq 'target-exited')
        if ($ExpectedRequireOrder) {
            for ($index = 0; $index -lt $CaseIds.Count; $index++) { Require-Supervisor ($starts[$index] -ceq $CaseIds[$index]) }
        }
    } catch { throw 'framework-supervisor-evidence-rejected' }
    finally { if ($null -ne $requestStream) { $requestStream.Dispose() } }
}

$bindingCount = @('ExecutionRequestPath','ExecutionRequestSha256','ExpectedMaximumConcurrency','ExpectedRequireOrder' |
    Where-Object { $PSBoundParameters.ContainsKey($_) }).Count
Require-Supervisor ($bindingCount -eq 0 -or $bindingCount -eq 4)

Require-Supervisor ([IO.Path]::IsPathFullyQualified($Path) -and
    [IO.Path]::GetFileName($Path) -ceq 'supervisor.json' -and $CaseIds.Count -gt 0 -and
    $SourceFingerprint -cmatch '^[a-f0-9]{64}$')
$expectedUnique = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($id in $CaseIds) {
    Require-Supervisor (-not [string]::IsNullOrWhiteSpace($id) -and $expectedUnique.Add($id))
}
$file = Get-Item -LiteralPath $Path -Force
Require-Supervisor (-not $file.PSIsContainer -and $file.Length -gt 0 -and $file.Length -le 268435456 -and
    ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0)
$inputStream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try {
    $reader = [IO.StreamReader]::new($inputStream, [Text.UTF8Encoding]::new($false, $true), $false, 4096, $true)
    try { $record = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    foreach ($value in @($record.state.complete, $record.target.started, $record.process.launcherExited,
        $record.process.stdoutDrained, $record.process.stderrDrained, $record.rawEvidence.verified)) {
        Require-Supervisor ($value -is [bool] -and $value -eq $true)
    }
    foreach ($value in @($record.baselineClaim, $record.state.terminationRequired)) {
        Require-Supervisor ($value -is [bool] -and $value -eq $false)
    }
    foreach ($value in @($record.schemaVersion, $record.target.pid, $record.target.exitCode,
        $record.process.launcherPid, $record.process.ownedActiveProcesses)) {
        Require-Supervisor ($value -is [int] -or $value -is [long])
    }
    Require-Supervisor ($record.schemaVersion -eq 1 -and $record.kind -ceq 'framework-execution' -and
        $record.runId -ceq $RunId -and $record.sourceFingerprint -ceq $SourceFingerprint -and
        $record.testAssemblySha256 -ceq $AssemblySha256 -and $record.baselineClaim -eq $false -and
        $null -eq $record.failureCode -and $record.state.complete -eq $true -and
        $record.state.stage -ceq 'child-exit' -and $null -eq $record.state.failureStage -and
        $null -eq $record.state.failureCaseId -and $record.state.terminationRequired -eq $false)
    if ([string]::IsNullOrEmpty($SourceTree)) { Require-Supervisor ($null -eq $record.sourceTree) }
    else { Require-Supervisor ($SourceTree -cmatch '^([a-f0-9]{40}|[a-f0-9]{64})$' -and $record.sourceTree -ceq $SourceTree) }
    Assert-SupervisorIds $record.selectedIds $CaseIds
    Assert-SupervisorIds $record.state.passedIds $CaseIds
    foreach ($name in @('failedIds','timedOutIds','activeIds','interruptedIds','notRunIds')) {
        Require-Supervisor ($record.state.$name -is [Array] -and @($record.state.$name).Count -eq 0)
    }
    Require-Supervisor ($record.target.started -eq $true -and $record.target.pid -gt 0 -and
        $record.target.exitCode -eq 0 -and $record.process.launcherPid -gt 0 -and
        $record.process.launcherExited -eq $true -and $record.process.stdoutDrained -eq $true -and
        $record.process.stderrDrained -eq $true -and $null -ne $record.process.ownedActiveProcesses -and
        $record.process.ownedActiveProcesses -eq 0 -and $record.rawEvidence.verified -eq $true -and
        $ResultXmlSha256 -cmatch '^[a-f0-9]{64}$' -and $TrxSha256 -cmatch '^[a-f0-9]{64}$' -and
        $record.rawEvidence.resultXmlSha256 -ceq $ResultXmlSha256 -and $record.rawEvidence.trxSha256 -ceq $TrxSha256)
    if ($bindingCount -eq 4) { Assert-SupervisorPolicy $record }
} finally { $inputStream.Dispose() }
