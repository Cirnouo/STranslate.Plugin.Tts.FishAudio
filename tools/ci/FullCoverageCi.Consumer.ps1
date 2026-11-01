#requires -Version 7.0
<#
.SYNOPSIS
    Restore exact prerequisites, or select, restore and independently compose all shards.
.DESCRIPTION
    Caller-acquired immutable env/API/source observations and all expected hashes
    remain explicit inputs. This helper does not authenticate those observations.
    RestorePrerequisites is used before Producer RunShard on a fresh machine. It
    checks the latest actual Prepare and optionally smoke0 against numeric API
    owners/fences, then restores only the original roots in the independent
    context. Existing roots fail; there is no cleanup, rewrite, retry or fallback.
    SelectAndCompose supplies the complete run artifact inventory/download set to
    the existing byte-observing coverage-ci-select command. It checks the fresh
    selection's exact independent file digests, latest Prepare and every actual
    Shard, restores each whole owner, raw-reimports nonempty smoke0, checks actual
    API start order, and invokes public Compose with all indices including zero.
    Request: schemaVersion, workDirectory, diagnosticPath, resultPath,
    driver { toolFile, toolSha256, dotnetFile }; RestorePrerequisites also has
    shardIndex, preparationArtifact, and smokeArtifact for a nonzero shard.
    SelectAndCompose has artifactSnapshot { repository, runId, totalCount,
    artifacts: raw complete paginated API records }, artifacts (independently
    pinned downloads in the Producer artifact format), and fresh composeDirectory.
    The bootstrap driver is independently supplied outside all restored roots.
    Its transport/selection output grants no source/build/event authentication.
    Official baseline publication requires a separate authenticated publisher
    which re-admits raw Compose and exact main provenance. No publisher is here.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('RestorePrerequisites','SelectAndCompose')][string]$Action,
    [Parameter(Mandatory)][string]$ContextPath,
    [Parameter(Mandatory)][string]$ExpectedContextSha256,
    [Parameter(Mandatory)][string]$ObservationsPath,
    [Parameter(Mandatory)][string]$ExpectedObservationsSha256,
    [Parameter(Mandatory)][string]$JobsPath,
    [Parameter(Mandatory)][string]$ExpectedJobsSha256,
    [Parameter(Mandatory)][string]$RequestPath,
    [Parameter(Mandatory)][string]$ExpectedRequestSha256
)
. (Join-Path $PSScriptRoot 'FullCoverageCi.Common.ps1')
$Action = switch ($Action.ToLowerInvariant()) { 'restoreprerequisites' { 'RestorePrerequisites' }; 'selectandcompose' { 'SelectAndCompose' } }
$failure = $null; $request = $null; $context = $null; $outcome = $null
$stage = 'preflight'; $previousSnapshot = $env:FISHAUDIO_SNAPSHOT_CONTEXT
try {
    $context = CoverageCi-Read $ContextPath $ExpectedContextSha256
    $observations = CoverageCi-Read $ObservationsPath $ExpectedObservationsSha256
    $jobs = CoverageCi-Read $JobsPath $ExpectedJobsSha256
    $request = CoverageCi-Read $RequestPath $ExpectedRequestSha256
    [void](CoverageCi-AssertCaller $context $observations $jobs $ExpectedJobsSha256)
    CoverageCi-AssertConsumerRequest $context $request $Action
    if ($Action -ceq 'RestorePrerequisites') {
        $currentRole = CoverageCi-Role $context 1 $request.shardIndex
        $current = CoverageCi-Latest $context $jobs $currentRole
        Require-CoverageCi ($current.job.id -eq $observations.apiJob.id -and
            $current.actualAttempt -eq $context.currentAttempt) 'current-role'
    }
    CoverageCi-AssertOutputLayout $request.workDirectory @($request.resultPath, $request.diagnosticPath)
    $fresh = @($request.workDirectory, $request.resultPath, $request.diagnosticPath)
    if ($Action -ceq 'SelectAndCompose') {
        $fresh += $request.composeDirectory
        Require-CoverageCi (-not (Test-HybridContained $request.composeDirectory $request.workDirectory) -and
            -not (Test-HybridContained $request.workDirectory $request.composeDirectory)) 'output-path-alias'
        CoverageCi-AssertOutputLayout $request.composeDirectory @($request.resultPath, $request.diagnosticPath)
    }
    CoverageCi-AssertFresh $context $fresh
    Require-CoverageCi (-not (Test-HybridContained $request.driver.toolFile $context.operationRoot)) 'external-driver-required'
    CoverageCi-AssertDriver $request.driver -Pin
    if ($Action -ceq 'RestorePrerequisites') {
        CoverageCi-AssertArtifact $context $request.preparationArtifact -Pin
        if ($request.shardIndex -gt 0) { CoverageCi-AssertArtifact $context $request.smokeArtifact -Pin }
    } else { foreach ($artifact in $request.artifacts) { CoverageCi-AssertArtifact $context $artifact -Pin } }
    # Reject occupied fixed destinations before the byte-observation child too.
    $restoreRoles = @((CoverageCi-Role $context 0 $null))
    if ($Action -ceq 'RestorePrerequisites') {
        if ($request.shardIndex -gt 0) { $restoreRoles += CoverageCi-Role $context 1 0 }
    } else { for ($index = 0; $index -lt $context.shardCount; $index++) { $restoreRoles += CoverageCi-Role $context 1 $index } }
    foreach ($restoreRole in $restoreRoles) {
        $actual = CoverageCi-Latest $context $jobs $restoreRole -RequireSuccess
        $destination = CoverageCi-Root $context $restoreRole.role $restoreRole.shardIndex $actual.actualAttempt
        Require-CoverageCi (-not [IO.File]::Exists($destination) -and -not [IO.Directory]::Exists($destination)) 'fresh-operation-root-required'
    }
    New-HybridDirectory $request.workDirectory $context.operationRoot
    $prepareRole = CoverageCi-Role $context 0 $null
    $prepare = CoverageCi-Latest $context $jobs $prepareRole -RequireSuccess
    $prepareWork = Join-Path $request.workDirectory 'prepare'; [void][IO.Directory]::CreateDirectory($prepareWork)
    if ($Action -ceq 'RestorePrerequisites') {
        $stage = 'prerequisite-selection'
        $role = CoverageCi-Role $context 1 $request.shardIndex
        $current = CoverageCi-Latest $context $jobs $role
        Require-CoverageCi ($current.job.id -eq $observations.apiJob.id -and
            $current.actualAttempt -eq $context.currentAttempt) 'current-role'
        $current.execution = [pscustomobject]@{ started = CoverageCi-Utc $current.job.started_at; completed = $null }
        $chainOwner = [pscustomobject]@{ preparationJobId = $prepare.job.id; preparationAttempt = $prepare.actualAttempt }
        CoverageCi-AssertChain $prepare $current $chainOwner
        $inspected = CoverageCi-Inspect $context $request.preparationArtifact $prepareRole $prepare $prepareWork $request.driver
        if ($request.shardIndex -gt 0) {
            $smokeRole = CoverageCi-Role $context 1 0
            $smoke = CoverageCi-Latest $context $jobs $smokeRole -RequireSuccess
            $smokeWork = Join-Path $request.workDirectory 'smoke'; [void][IO.Directory]::CreateDirectory($smokeWork)
            $inspectedSmoke = CoverageCi-Inspect $context $request.smokeArtifact $smokeRole $smoke $smokeWork $request.driver
            CoverageCi-AssertChain $prepare $smoke $inspectedSmoke.owner
            CoverageCi-AssertChain $prepare $current $chainOwner $smoke
        }
        $stage = 'restore-preparation'
        CoverageCi-Restore $context $inspected $prepareWork $request.driver
        $preparedWork = Join-Path $request.workDirectory 'prepared-admission'; [void][IO.Directory]::CreateDirectory($preparedWork)
        $prepared = CoverageCi-PreparedCheck $context $preparedWork
        Require-CoverageCi (@($prepared.value.wholePlan.smokeCanonicalIds).Count -gt 0) 'nonempty-smoke-required'
        if ($request.shardIndex -gt 0) {
            $stage = 'restore-and-reimport-smoke'
            CoverageCi-Restore $context $inspectedSmoke $smokeWork $request.driver
            CoverageCi-SmokeCheck $context $inspectedSmoke.owner $prepared $smokeWork
        }
        $outcome = [ordered]@{ schemaVersion = 1; kind = 'coverage-ci-prerequisites-restored'
            preparationJobId = $prepare.job.id; preparationAttempt = $prepare.actualAttempt; shardIndex = $request.shardIndex
            rawPrerequisitesReimported = $true; apiEventAuthenticated = $false; mainProvenanceVerified = $false
            coverageUnionComplete = $false; completeBaseline = $false; publicationAuthorized = $false; tiaSelectionAuthorized = $false }
    } else {
        $stage = 'complete-owner-selection'
        CoverageCi-AssertDownloads $context $request.artifactSnapshot $request.artifacts
        # Pass every complete-owner artifact, including earlier attempts. Failure
        # diagnostics have a separate namespace and cannot hide latest job state.
        foreach ($artifact in $request.artifacts) {
            [void](Lock-HybridFile $artifact.ownerRecordPath $artifact.ownerRecordSha256)
            [void](Lock-HybridFile $artifact.archivePath $artifact.archiveSha256)
        }
        $input = Join-Path $request.workDirectory 'selection-input.json'
        $output = Join-Path $request.workDirectory 'selection.json'
        Write-HybridJson $input @{ schemaVersion = 1; jobsPath = $JobsPath
            artifacts = @($request.artifacts | ForEach-Object { @{ ownerRecordPath = $_.ownerRecordPath; archivePath = $_.archivePath } }) }
        CoverageCi-Tool $context $request.driver.toolFile $request.driver.toolSha256 $request.driver.dotnetFile @(
            'coverage-ci-select','--input',$input,'--expectation',$ContextPath,'--output',$output) `
            (Join-Path $request.workDirectory 'selection-process') $request.workDirectory
        $selection = Read-HybridJson $output
        Require-CoverageCi ($selection.preparation.actualJobId -eq $prepare.job.id -and
            $selection.preparation.actualAttempt -eq $prepare.actualAttempt -and
            $selection.preparedSha256 -ceq $context.preparation.prepared.preparedSha256 -and
            @($selection.shards).Count -eq $context.shardCount) 'selection-binding'
        $prepareArtifact = CoverageCi-DownloadFor $request.artifacts $selection.preparation
        $inspected = CoverageCi-Inspect $context $prepareArtifact $prepareRole $prepare $prepareWork $request.driver
        $smokeRole = CoverageCi-Role $context 1 0
        $smoke = CoverageCi-Latest $context $jobs $smokeRole -RequireSuccess
        $shards = [Collections.Generic.List[object]]::new()
        for ($index = 0; $index -lt $context.shardCount; $index++) {
            $role = CoverageCi-Role $context 1 $index
            $actual = CoverageCi-Latest $context $jobs $role -RequireSuccess
            $selected = $selection.shards[$index]
            Require-CoverageCi ($selected.shardIndex -eq $index -and $selected.actualJobId -eq $actual.job.id -and
                $selected.actualAttempt -eq $actual.actualAttempt) 'selection-binding'
            $artifact = CoverageCi-DownloadFor $request.artifacts $selected
            $work = Join-Path $request.workDirectory ('shard-' + $index); [void][IO.Directory]::CreateDirectory($work)
            $inspectedShard = CoverageCi-Inspect $context $artifact $role $actual $work $request.driver
            if ($index -eq 0) { CoverageCi-AssertChain $prepare $actual $inspectedShard.owner }
            else { CoverageCi-AssertChain $prepare $actual $inspectedShard.owner $smoke }
            $shards.Add([pscustomobject]@{ inspected = $inspectedShard; selected = $selected; work = $work })
        }
        $stage = 'restore-all-original-roots'
        CoverageCi-Restore $context $inspected $prepareWork $request.driver
        foreach ($shard in $shards) { CoverageCi-Restore $context $shard.inspected $shard.work $request.driver }
        $preparedWork = Join-Path $request.workDirectory 'prepared-admission'; [void][IO.Directory]::CreateDirectory($preparedWork)
        $prepared = CoverageCi-PreparedCheck $context $preparedWork
        $stage = 'raw-smoke-admission'
        CoverageCi-SmokeCheck $context $shards[0].inspected.owner $prepared $shards[0].work
        $stage = 'independent-complete-raw-compose'
        $env:FISHAUDIO_SNAPSHOT_CONTEXT = $context.preparation.prepared.snapshotContextPath
        $entry = Join-Path $context.preparation.prepared.sourceDirectory 'tools/Invoke-FullCoverage.ps1'
        [void](Lock-HybridFile $entry)
        $invokeInput = Join-Path $request.workDirectory 'compose-invocation.json'
        $invokeScript = Join-Path $request.workDirectory 'invoke-compose.ps1'
        Write-HybridJson $invokeInput (CoverageCi-ComposeInvocation $context $shards $request.composeDirectory)
        CoverageCi-ComposeLauncher $invokeScript
        [void](Lock-HybridFile $invokeInput); [void](Lock-HybridFile $invokeScript)
        $exitCode = Invoke-HybridProcess $context.preparation.prepared.shellFile @('-NoLogo','-NoProfile','-NonInteractive',
            '-File',$invokeScript,'-InputPath',$invokeInput) `
            (Join-Path $request.workDirectory 'compose-process') $context.preparation.prepared.sourceDirectory 0
        Require-CoverageCi ($exitCode -eq 0) 'compose-operation-failed'
        $outcome = [ordered]@{ schemaVersion = 1; kind = 'coverage-ci-local-composition-returned'
            preparationJobId = $prepare.job.id; preparationAttempt = $prepare.actualAttempt
            jobsSha256 = $ExpectedJobsSha256; selectionSha256 = Get-HybridDigest $output
            composeDirectory = $request.composeDirectory; completeShardCount = $shards.Count; smokeIndex = 0
            publicComposeReturnedZero = $true; apiEventAuthenticated = $false; sourceBuildAuthenticated = $false
            machineConcurrencyVerified = $false; mainProvenanceVerified = $false; officialBaseline = $false
            completeBaseline = $false; publicationAuthorized = $false; tiaSelectionAuthorized = $false }
    }
} catch { $failure = $_ }
finally {
    try { $env:FISHAUDIO_SNAPSHOT_CONTEXT = $previousSnapshot } catch { if ($null -eq $failure) { $failure = $_ } }
    $failure = CoverageCi-Close $failure
}
if ($null -ne $failure) {
    $reason = CoverageCi-FailureCode $failure
    if ($null -ne $context -and $null -ne $request) {
        try {
            # Compose is deliberately outside the successful owner namespace.
            $diagnostic = CoverageCi-Diagnostic $context 0 $null 'OperationFailed' $stage
            $diagnostic.artifactName = 'coverage-diagnostics-compose-' + $context.commit + '-' + $context.runId + '-' + $context.currentAttempt
            $diagnostic['reason'] = $reason
            CoverageCi-Publish $request.diagnosticPath $diagnostic
        } catch { } # Retain the primary failure, including test/collection raw outcomes.
    }
    [Console]::Error.WriteLine('full-coverage-ci-consumer: failed stage=' + $stage + ' reason=' + $reason + '; raw artifacts retained; baseline/publication=false.')
    exit 1
}
try { CoverageCi-Publish $request.resultPath $outcome }
catch { [Console]::Error.WriteLine('full-coverage-ci-consumer: failed stage=result-publication reason=output-publication-failed; pending bytes retained.'); exit 1 }
Write-Output 'full-coverage-ci-consumer: bounded operation returned; separate authenticated baseline publisher remains required.'
exit 0
