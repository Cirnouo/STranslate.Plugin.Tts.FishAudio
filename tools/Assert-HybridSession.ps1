#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$RepositoryRoot, [Parameter(Mandatory)][string]$SessionDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory)
. (Join-Path $PSScriptRoot 'HybridSession.Common.ps1')
$root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RepositoryRoot))
$sessionRoot = [IO.Path]::GetFullPath($SessionDirectory)
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$passed = $false; $outputOwned = $false
try {
    Require-Hybrid ($IsWindows -and [IO.Path]::IsPathFullyQualified($RepositoryRoot) -and [IO.Path]::IsPathFullyQualified($SessionDirectory) -and
        (Test-HybridSamePath $PSScriptRoot (Join-Path $root 'tools')) -and -not (Test-HybridContained $sessionRoot $root)) 'hybrid-admission-root'
    New-HybridDirectory $outputRoot $root
    $outputOwned = $true
    Require-Hybrid (-not (Test-HybridSamePath $outputRoot $sessionRoot)) 'hybrid-admission-overwrites-session'
    $manifestPath = Lock-HybridFile (Join-Path $sessionRoot 'manifest.json') -Retained
    $session = Read-HybridJson $manifestPath
    Require-Hybrid ($session.schemaVersion -eq 1 -and $session.kind -ceq 'hybrid-test-session' -and $session.complete -eq $true -and
        $session.runId -cmatch '^[a-f0-9]{32}$' -and $session.baselineClaim -eq $false -and
        ($session.workers -is [int] -or $session.workers -is [long]) -and $session.workers -ge 0 -and $session.workers -le 4) 'hybrid-session-envelope'
    $source = Get-HybridSource $root (Join-Path $outputRoot 'source-initial')
    Assert-HybridSource $session.source $source.identity
    Require-Hybrid ($session.legacySourceTree -ceq $source.legacyTree) 'hybrid-legacy-tree'
    $build = Get-HybridBuildPins $root
    Assert-HybridBuildPins $session.buildPins $build
    $tool = Join-Path $root 'tools/FishAudio.Testing/bin/Release/net10.0-windows/FishAudio.Testing.dll'
    $runner = Join-Path $root $HybridDefinitions[0].assembly
    $shell = (Get-Process -Id $PID).Path; $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $freshRoot = Join-Path $outputRoot 'fresh-discovery'
    $code = Invoke-HybridProcess $shell @('-NoLogo','-NoProfile','-NonInteractive','-File',(Join-Path $root 'tools/Test.ps1'),
        '-Mode','Discover','-NoBuild','-ResultsDirectory',$freshRoot,'-RepositoryRoot',$root) (Join-Path $outputRoot 'fresh-discovery-process') $root
    Require-Hybrid ($code -eq 0) 'hybrid-independent-discovery-failed'
    $fresh = Read-HybridDiscovery $freshRoot $source.identity $root
    $expected = New-HybridPlan $root (Join-Path $outputRoot 'fresh-planning') $fresh $tool
    Assert-HybridSlotIndex $session.slots $expected.document.plan.slots
    $slotDirectories = @(Get-ChildItem -LiteralPath (Join-Path $sessionRoot 'slots') -Directory -Force)
    Require-Hybrid ($slotDirectories.Count -eq $session.slots.Count) 'hybrid-slot-directory-set'
    $initialManifest = Resolve-HybridArtifact $sessionRoot $session.discovery $session.discoverySha256
    $initialRoot = [IO.Path]::GetDirectoryName($initialManifest)
    $initial = Read-HybridDiscovery $initialRoot $source.identity $root
    $producerPlanPath = Resolve-HybridArtifact $sessionRoot $session.plan $session.planSha256
    $producerPlan = Read-HybridJson $producerPlanPath
    $reparseInputs = Join-Path $outputRoot 'producer-discoveries.json'
    Write-HybridJson $reparseInputs @{ schemaVersion = 1; paths = @($HybridDefinitions | ForEach-Object { $initial.records[$_.key].dump }) }
    $reparsedPlanPath = Join-Path $outputRoot 'producer-plan-reparsed.json'
    Invoke-HybridTool $tool @('evidence-hybrid-plan','--reference',(Join-Path $root 'tests/Migration/legacy-catalog.json'),
        '--legacy',$expected.legacy,'--discoveries',$reparseInputs,'--migrations',(Join-Path $root 'tests/Migration/migrated-cases.json'),
        '--output',$reparsedPlanPath) (Join-Path $outputRoot 'producer-plan-reparse-process') $root
    $reparsed = Read-HybridJson $reparsedPlanPath
    Require-Hybrid ($producerPlan.schemaVersion -eq 1 -and $producerPlan.kind -ceq 'hybrid-execution-slot-plan' -and
        $producerPlan.complete -eq $true -and $producerPlan.baselineClaim -eq $false -and
        ($producerPlan.plan | ConvertTo-Json -Depth 64 -Compress) -ceq ($reparsed.plan | ConvertTo-Json -Depth 64 -Compress)) 'hybrid-producer-plan-reparse'
    foreach ($pair in @(
        @('referenceSha256',(Join-Path $root 'tests/Migration/legacy-catalog.json')),
        @('migrationsSha256',(Join-Path $root 'tests/Migration/migrated-cases.json')),
        @('legacyCatalogSha256',(Join-Path $sessionRoot 'planning/current-legacy-catalog.json')),
        @('discoveriesSha256',(Join-Path $sessionRoot 'planning/discoveries.json')))) {
        Require-Hybrid ($producerPlan.inputs.($pair[0]) -cmatch '^[a-f0-9]{64}$') 'hybrid-plan-input-digest'
        if ($pair[0] -cin @('referenceSha256','migrationsSha256')) {
            # Snapshot sources are independently pinned, then cleaned by Gate.
            # Retained receipt paths must survive that cleanup.
            [void](Lock-HybridFile $pair[1] ([string]$producerPlan.inputs.($pair[0])))
        } else {
            [void](Lock-HybridFile $pair[1] ([string]$producerPlan.inputs.($pair[0])) -Retained)
        }
    }
    $previousEnd = [long]0
    $admittedOwners = [Collections.Generic.List[object]]::new()
    $pendingOwners = [Collections.Generic.List[object]]::new()
    $generationInventories = [Collections.Generic.List[object]]::new()
    foreach ($slot in $session.slots) {
        $slotRoot = Join-Path $sessionRoot ('slots/'+$slot.id)
        $latest = Read-HybridLatestAttempt $slotRoot $slot.attempts
        $attempt = $latest.record; $attemptRoot = $latest.directory
        Require-Hybrid ($attempt.schemaVersion -eq 1 -and $attempt.kind -ceq 'hybrid-slot-attempt' -and
            $attempt.outerRunId -ceq $session.runId -and $attempt.slotId -ceq $slot.id -and
            $attempt.owner -ceq $slot.owner -and $attempt.partition -ceq $slot.partition -and
            $attempt.legacySourceTree -ceq $source.legacyTree) 'hybrid-attempt-owner'
        Assert-HybridSource $attempt.source $source.identity
        Assert-HybridBuildPins $attempt.buildPins $build
        Assert-HybridIds $attempt.selectedIds ([string[]]$slot.selectedIds) -Ordered
        $selectionPath = Resolve-HybridArtifact $attemptRoot $attempt.selection $attempt.selectionSha256
        $selection = Read-HybridJson $selectionPath
        Require-Hybrid ($selection.schemaVersion -eq 1) 'hybrid-attempt-selection'
        Assert-HybridIds $selection.ids ([string[]]$slot.selectedIds) -Ordered
        $processPath = Resolve-HybridArtifact $attemptRoot $attempt.process $attempt.processSha256
        $process = Read-HybridJson $processPath
        Require-Hybrid ($process.schemaVersion -eq 1 -and $process.kind -ceq 'hybrid-owner-process' -and
            $process.complete -eq $true -and $process.started -eq $true -and $process.exitCode -eq 0 -and
            $process.stdoutDrained -eq $true -and $process.stderrDrained -eq $true -and
            $process.ownership -ceq 'windows-job-before-target-go' -and $process.assigned -eq $true -and
            $process.allChildrenExited -eq $true -and $process.activeProcesses -eq 0 -and
            $process.targetExitObserved -eq $true -and $process.targetProcessId -gt 0 -and
            $process.targetExitCode -eq 0 -and $process.controlEvents -eq 2 -and
            $process.metadataTimeoutMilliseconds -eq 0 -and $null -eq $process.failureType -and
            @($process.cleanupExceptionTypes).Count -eq 0 -and
            $process.frequency -eq [Diagnostics.Stopwatch]::Frequency -and $process.queuedTimestamp -ge $previousEnd -and
            $process.endedTimestamp -gt $process.queuedTimestamp -and (Test-HybridSamePath $process.workingDirectory $root)) 'hybrid-process-terminal-or-slot-order'
        Assert-HybridProcessProof $processPath $process
        $previousEnd = [long]$process.endedTimestamp
        $evidence = Resolve-HybridArtifact $attemptRoot $attempt.evidence $attempt.evidenceSha256
        # Validate every outer owner terminal before admitting any report.
        # Its source implementation assigns the waiting shim before GO and
        # records actual target exit, original drains and zero Job membership.
        $pendingOwners.Add([pscustomobject]@{ slot = $slot; attempt = $attempt; attemptRoot = $attemptRoot
            selectionPath = $selectionPath; process = $process; evidence = $evidence; inventory = $null })
    }
    # Fence legitimate writers with all genuine completed/empty Job terminals,
    # then lock canonical and ALL committed generations for EVERY legacy slot.
    foreach ($owner in $pendingOwners) {
        if ($owner.slot.owner -ceq 'legacy') {
            $owner.inventory = Read-HybridLegacyInventory $owner.evidence $sessionRoot
            $relative = $owner.slot.id+'-generation-inventory.json'
            $path = Join-Path $outputRoot $relative
            Write-HybridJson $path $owner.inventory
            [void](Lock-HybridFile $path -Retained)
            $generationInventories.Add(@{ slotId = $owner.slot.id; path = $relative; sha256 = Get-HybridDigest $path })
        }
    }
    foreach ($owner in $pendingOwners) {
        $slot = $owner.slot; $attempt = $owner.attempt; $attemptRoot = $owner.attemptRoot
        $selectionPath = $owner.selectionPath; $process = $owner.process; $evidence = $owner.evidence
        if ($slot.owner -ceq 'legacy') {
            Require-Hybrid ($attempt.evidence -ceq 'legacy.json' -and (Test-HybridSamePath $process.executable $dotnet)) 'hybrid-legacy-process'
            $arguments = @($runner,'--legacy','--all','--partition',[string]$slot.partition,'--results',$evidence)
            Assert-HybridIds $process.arguments $arguments -Ordered
            $catalogOutput = Join-Path $outputRoot ($slot.id+'-verified-legacy.json')
            $verify = @($runner,'--verify-snapshot-partition-report',$evidence,$source.legacyTree,'--partition',[string]$slot.partition,'--catalog-output',$catalogOutput)
            $code = Invoke-HybridProcess $dotnet $verify (Join-Path $outputRoot ($slot.id+'-legacy-verification')) $root 120000
            Require-Hybrid ($code -eq 0) 'hybrid-legacy-partition-rejected'
            $verified = Read-HybridJson $catalogOutput
            Require-Hybrid ($verified.schemaVersion -eq 1 -and $verified.kind -ceq 'verified-legacy-partition-catalog' -and
                $verified.sourceTree -ceq $source.legacyTree -and $verified.partition -ceq $slot.partition -and
                $verified.reportRunId -cmatch '^[a-f0-9]{32}$' -and $verified.reportGeneration -gt 0 -and
                $null -ne $verified.source -and $verified.source.available -eq $true -and $verified.source.tree -ceq $source.legacyTree) 'hybrid-legacy-export-identity'
            Assert-HybridLegacyExport $verified $owner.inventory
            Assert-HybridLegacyInventory $sessionRoot $owner.inventory
            Assert-HybridIds @($verified.cases | ForEach-Object id) ([string[]]$slot.selectedIds) -Ordered
            foreach ($case in $verified.cases) { Require-Hybrid ($case.partition -ceq $slot.partition) 'hybrid-legacy-partition-drift' }
            $admittedOwners.Add(@{ slotId = $slot.id; owner = 'legacy'; report = $owner.inventory.report; reportRunId = $verified.reportRunId; reportGeneration = $verified.reportGeneration })
        } else {
            Require-Hybrid ($attempt.evidence -ceq 'framework/manifest.json' -and (Test-HybridSamePath $process.executable $shell)) 'hybrid-framework-process'
            $ownerRoot = Join-Path $attemptRoot 'framework'
            $arguments = @('-NoLogo','-NoProfile','-NonInteractive','-File',(Join-Path $root 'tools/Test.ps1'),'-Mode','Full','-NoBuild','-RepositoryRoot',$root,'-ResultsDirectory',$ownerRoot,'-SelectionPath',$selectionPath,'-Workers',[string]$session.workers)
            Assert-HybridIds $process.arguments $arguments -Ordered
            Assert-HybridFrameworkOwner $ownerRoot $freshRoot $source.identity ([string[]]$slot.selectedIds) (Join-Path $outputRoot ($slot.id+'-framework')) $tool $root
            $admittedOwners.Add(@{ slotId = $slot.id; owner = 'framework'; runId = (Read-HybridJson $evidence).runId })
        }
    }
    # A newly published failed/incomplete later attempt cannot borrow an earlier pass.
    foreach ($slot in $session.slots) { [void](Read-HybridLatestAttempt (Join-Path $sessionRoot ('slots/'+$slot.id)) $slot.attempts) }
    $finalSource = Get-HybridSource $root (Join-Path $outputRoot 'source-final')
    Assert-HybridSource $finalSource.identity $source.identity
    Require-Hybrid ($finalSource.legacyTree -ceq $source.legacyTree) 'hybrid-source-tree-changed'
    Assert-HybridBuildPins (Get-HybridBuildPins $root) $build
    # Retain raw generation sidecars, requests and logs verbatim; never synthesize
    # a combined legacy report or merged XML/TRX.
    foreach ($parent in @($sessionRoot,$outputRoot)) {
        foreach ($entry in Get-ChildItem -LiteralPath $parent -Recurse -Force) {
            Require-Hybrid (($entry.Attributes -band ([IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Device)) -eq 0) 'hybrid-retained-tree-reparse'
            if (-not $entry.PSIsContainer) { [void](Lock-HybridFile $entry.FullName -Retained) }
        }
    }
    # No new/changed committed generation can be silently added to the final
    # digest list after its authoritative admission. Gate repeats this guard
    # against these exact inventories during final commit-msg receipt checks.
    foreach ($owner in $pendingOwners) {
        if ($null -ne $owner.inventory) { Assert-HybridLegacyInventory $sessionRoot $owner.inventory }
    }
    # Final physical attempt admission shares the generation fence, after source
    # checks and recursive retention; newly found attempts never gain approval
    # merely because their files were captured by the retained digest walk.
    foreach ($slot in $session.slots) {
        [void](Read-HybridLatestAttempt (Join-Path $sessionRoot ('slots/'+$slot.id)) $slot.attempts)
    }
    $artifacts = @($script:HybridArtifacts.GetEnumerator() | Sort-Object Key | ForEach-Object { @{ path = $_.Key; sha256 = $_.Value } })
    Write-HybridJson (Join-Path $outputRoot 'admission.json') @{ schemaVersion = 1; kind = 'hybrid-session-admission'; complete = $true
        runId = $session.runId; source = $source.identity; legacySourceTree = $source.legacyTree; buildPins = $build
        owners = $admittedOwners.ToArray(); generationInventories = $generationInventories.ToArray(); receiptArtifacts = $artifacts; baselineClaim = $false }
    $passed = $true
} catch {
    if ($outputOwned) {
        Write-HybridJson (Join-Path $outputRoot 'failure.json') @{ schemaVersion = 1; kind = 'hybrid-session-admission-failure'
            complete = $false; reason = $script:failureCode; exceptionType = $_.Exception.GetType().Name; baselineClaim = $false }
    }
    [Console]::Error.WriteLine('hybrid-admission: rejected '+$script:failureCode)
} finally { Close-HybridLocks }
if (-not $passed) { exit 1 }
Write-Output 'hybrid-admission: verified exact owner union; baseline=false.'
exit 0
