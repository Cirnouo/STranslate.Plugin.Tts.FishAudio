#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$RepositoryRoot, [Parameter(Mandatory)][string]$ResultsDirectory,
    [ValidateRange(0,4)][int]$Workers = 4, [switch]$NoBuild)
. (Join-Path $PSScriptRoot 'HybridSession.Common.ps1')
$root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RepositoryRoot))
$runRoot = [IO.Path]::GetFullPath($ResultsDirectory)
$runId = [Guid]::NewGuid().ToString('N')
$shell = (Get-Process -Id $PID).Path
$dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$slots = [Collections.Generic.List[object]]::new()
$runOwned = $false
$source = $null; $build = @(); $derived = $null; $complete = $false
try {
    Require-Hybrid ($IsWindows -and [IO.Path]::IsPathFullyQualified($RepositoryRoot) -and (Test-HybridSamePath $PSScriptRoot (Join-Path $root 'tools'))) 'hybrid-entry-root'
    New-HybridDirectory $runRoot $root
    $runOwned = $true
    Write-HybridJson (Join-Path $runRoot 'owner.json') @{ schemaVersion = 1; runId = $runId; kind = 'hybrid-session-owner' }
    $source = Get-HybridSource $root (Join-Path $runRoot 'source-initial')
    $discoveryRoot = Join-Path $runRoot 'discovery'
    $arguments = @('-NoLogo','-NoProfile','-NonInteractive','-File',(Join-Path $root 'tools/Test.ps1'),'-Mode','Discover','-ResultsDirectory',$discoveryRoot,'-RepositoryRoot',$root)
    if ($NoBuild) { $arguments += '-NoBuild' }
    $code = Invoke-HybridProcess $shell $arguments (Join-Path $runRoot 'discovery-process') $root
    Require-Hybrid ($code -eq 0) 'hybrid-discovery-failed'
    $build = Get-HybridBuildPins $root
    $discovery = Read-HybridDiscovery $discoveryRoot $source.identity $root
    $tool = Join-Path $root 'tools/FishAudio.Testing/bin/Release/net10.0-windows/FishAudio.Testing.dll'
    $derived = New-HybridPlan $root (Join-Path $runRoot 'planning') $discovery $tool
    $ordinal = 0
    foreach ($slot in $derived.document.plan.slots) {
        $ordinal++
        $attemptRoot = Join-Path $runRoot ('slots/' + $slot.id + '/attempt-0001')
        [IO.Directory]::CreateDirectory($attemptRoot) | Out-Null
        $selectionPath = Join-Path $attemptRoot 'selection.json'
        Write-HybridJson $selectionPath @{ schemaVersion = 1; ids = [string[]]$slot.selectedIds }
        [void](Lock-HybridFile $selectionPath -Retained)
        $exitCode = 1; $attemptComplete = $false; $evidence = $null; $errorType = $null
        try {
            Assert-HybridBuildPins (Get-HybridBuildPins $root) $build
            if ($slot.owner -ceq 'legacy') {
                $evidence = 'legacy.json'
                $arguments = @((Join-Path $root $HybridDefinitions[0].assembly),'--legacy','--all','--partition',[string]$slot.partition,'--results',(Join-Path $attemptRoot $evidence))
                $exitCode = Invoke-HybridProcess $dotnet $arguments (Join-Path $attemptRoot 'process') $root
            } else {
                Require-Hybrid ($slot.owner -ceq 'framework') 'hybrid-plan-owner'
                $evidence = 'framework/manifest.json'
                $arguments = @('-NoLogo','-NoProfile','-NonInteractive','-File',(Join-Path $root 'tools/Test.ps1'),'-Mode','Full','-NoBuild','-RepositoryRoot',$root,'-ResultsDirectory',(Join-Path $attemptRoot 'framework'),'-SelectionPath',$selectionPath,'-Workers',[string]$Workers)
                $exitCode = Invoke-HybridProcess $shell $arguments (Join-Path $attemptRoot 'process') $root
            }
            $attemptComplete = $exitCode -eq 0 -and [IO.File]::Exists((Join-Path $attemptRoot $evidence))
        } catch { $errorType = $_.Exception.GetType().Name }
        $attemptPath = Join-Path $attemptRoot 'attempt.json'
        Write-HybridJson $attemptPath ([ordered]@{
            schemaVersion = 1; kind = 'hybrid-slot-attempt'; outerRunId = $runId; ordinal = 1
            slotId = $slot.id; owner = $slot.owner; partition = $slot.partition; selectedIds = [string[]]$slot.selectedIds
            source = $source.identity; legacySourceTree = $source.legacyTree; buildPins = $build
            complete = $attemptComplete; exitCode = $exitCode; failureType = $errorType
            process = 'process/process.json'; processSha256 = $(if ([IO.File]::Exists((Join-Path $attemptRoot 'process/process.json'))) { Get-HybridDigest (Join-Path $attemptRoot 'process/process.json') } else { $null })
            selection = 'selection.json'; selectionSha256 = Get-HybridDigest $selectionPath
            evidence = $evidence; evidenceSha256 = $(if ($null -ne $evidence -and [IO.File]::Exists((Join-Path $attemptRoot $evidence))) { Get-HybridDigest (Join-Path $attemptRoot $evidence) } else { $null })
        })
        $slots.Add([ordered]@{ ordinal = $ordinal; id = $slot.id; owner = $slot.owner; partition = $slot.partition; selectedIds = [string[]]$slot.selectedIds
            attempts = @(@{ ordinal = 1; sha256 = Get-HybridDigest $attemptPath }) })
        # A failed owner remains unresolved; prescribed later slots still run.
    }
    Assert-HybridSlotIndex $slots.ToArray() $derived.document.plan.slots
    $final = Get-HybridSource $root (Join-Path $runRoot 'source-final')
    Assert-HybridSource $final.identity $source.identity
    Require-Hybrid ($final.legacyTree -ceq $source.legacyTree) 'hybrid-source-tree-changed'
    Assert-HybridBuildPins (Get-HybridBuildPins $root) $build
    $complete = @($slots | Where-Object { -not (Read-HybridJson (Join-Path $runRoot ('slots/' + $_.id + '/attempt-0001/attempt.json'))).complete }).Count -eq 0
} catch {
    if ($runOwned) {
        Write-HybridJson (Join-Path $runRoot 'failure.json') @{ schemaVersion = 1; kind = 'hybrid-session-failure'; runId = $runId; complete = $false; reason = $script:failureCode; exceptionType = $_.Exception.GetType().Name; baselineClaim = $false }
    }
} finally {
    if ($runOwned) {
        Write-HybridJson (Join-Path $runRoot 'manifest.json') ([ordered]@{
            schemaVersion = 1; kind = 'hybrid-test-session'; runId = $runId; complete = $complete; baselineClaim = $false
            source = $(if ($null -ne $source) { $source.identity } else { $null }); legacySourceTree = $(if ($null -ne $source) { $source.legacyTree } else { $null })
            buildPins = $build; workers = $Workers; discovery = 'discovery/manifest.json'
            discoverySha256 = $(if ([IO.File]::Exists((Join-Path $runRoot 'discovery/manifest.json'))) { Get-HybridDigest (Join-Path $runRoot 'discovery/manifest.json') } else { $null })
            plan = 'planning/plan.json'; planSha256 = $(if ($null -ne $derived) { Get-HybridDigest $derived.path } else { $null }); slots = $slots.ToArray()
        })
    }
    Close-HybridLocks
}
if (-not $complete) { [Console]::Error.WriteLine('hybrid: incomplete session.'); exit 1 }
Write-Output ('hybrid: producer completed ' + $runId + '; independent admission remains required.')
exit 0
