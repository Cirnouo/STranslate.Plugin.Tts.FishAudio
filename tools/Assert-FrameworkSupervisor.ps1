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
    [Parameter(Mandatory)][string]$TrxSha256
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
} finally { $inputStream.Dispose() }
