#requires -Version 7.0
<#
.SYNOPSIS
    Seal a completed preparation or execute and seal one bounded coverage shard.
.DESCRIPTION
    No network or workflow YAML lives here. The caller supplies independently
    acquired main-push env/run/job/source observations, a full jobs+attempt-fence
    snapshot from GetRegressionJobAttempts.ps1, exact context and request digests.
    SealPrepare follows the public Prepare operation at its fixed original root.
    RunShard requires the latest successful actual Prepare and its byte-inspected
    artifact; indices above zero also require a latest successful, raw-reimported,
    nonempty smoke shard zero before the current job's start. The standard public
    coordinator owns execution and raw shard admission. No receipt flag authorizes
    a test pass, complete union, official baseline or publication.
    Request: schemaVersion, role (0 Prepare/1 Shard), shardIndex (null/number),
    workDirectory (fresh external), archivePath, ownerRecordPath, diagnosticPath,
    driver { toolFile, toolSha256, dotnetFile }, preparationArtifact and, for rest
    shards, smokeArtifact. Each artifact has ownerRecordPath/ownerRecordSha256,
    archivePath/archiveSha256 and caller-observed artifactId/artifactName/
    artifactRunId/artifactCommit. Artifact hashes/origin are caller inputs, never
    taken from a downloaded owner's own claims. Diagnostic requests additionally
    carry category and stage; failures retain raw files and never create an owner.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('SealPrepare','RunShard','Diagnostic')][string]$Action,
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
# ValidateSet accepts case variants; normalize before case-sensitive dispatch.
$Action = switch ($Action.ToLowerInvariant()) { 'sealprepare' { 'SealPrepare' }; 'runshard' { 'RunShard' }; 'diagnostic' { 'Diagnostic' } }
$failure = $null; $owner = $null; $request = $null; $context = $null
$stage = 'preflight'; $category = 'OperationFailed'; $diagnostic = $null
$previousSnapshot = $env:FISHAUDIO_SNAPSHOT_CONTEXT
try {
    $context = CoverageCi-Read $ContextPath $ExpectedContextSha256
    $observations = CoverageCi-Read $ObservationsPath $ExpectedObservationsSha256
    $jobs = CoverageCi-Read $JobsPath $ExpectedJobsSha256
    $request = CoverageCi-Read $RequestPath $ExpectedRequestSha256
    [void](CoverageCi-AssertCaller $context $observations $jobs $ExpectedJobsSha256)
    CoverageCi-AssertProducerRequest $context $request $Action
    if ($Action -ceq 'Diagnostic') {
        CoverageCi-AssertFresh $context @($request.diagnosticPath)
        $stage = $request.stage; $category = $request.category
        $diagnostic = CoverageCi-Diagnostic $context $request.role $request.shardIndex $category $stage
        throw 'full-coverage-ci-orchestration-original-operation-failed'
    }
    CoverageCi-AssertOutputLayout $request.workDirectory @($request.archivePath, $request.ownerRecordPath, $request.diagnosticPath)
    CoverageCi-AssertFresh $context @($request.workDirectory, $request.archivePath, $request.ownerRecordPath, $request.diagnosticPath)
    CoverageCi-AssertDriver $request.driver -Pin
    if ($Action -ceq 'RunShard') {
        CoverageCi-AssertArtifact $context $request.preparationArtifact -Pin
        if ($request.shardIndex -gt 0) { CoverageCi-AssertArtifact $context $request.smokeArtifact -Pin }
    }
    $role = CoverageCi-Role $context $request.role $request.shardIndex
    $current = CoverageCi-Latest $context $jobs $role
    Require-CoverageCi ($current.job.id -eq $observations.apiJob.id -and
        $current.actualAttempt -eq $context.currentAttempt -and $current.job.name -ceq $observations.apiJob.name) 'current-role'
    $root = CoverageCi-Root $context $role.role $role.shardIndex $context.currentAttempt
    if ($role.role -eq 1) {
        Require-CoverageCi (-not [IO.File]::Exists($root) -and -not [IO.Directory]::Exists($root)) 'fresh-operation-root-required'
    }
    New-HybridDirectory $request.workDirectory $context.operationRoot
    if ($role.role -eq 0) {
        Require-CoverageCi ((Join-Path $root $role.evidenceRelativePath) -ceq $context.preparation.prepared.preparedPath) 'prepare-root'
        $coordinatorPath = Join-Path (Split-Path -Parent $context.preparation.prepared.preparedPath) 'coordinator.json'
        [void](Lock-HybridFile $coordinatorPath)
        CoverageCi-AssertPrepareOutcome $context (Read-HybridJson $coordinatorPath)
        $prepareJobId = $current.job.id; $prepareAttempt = $current.actualAttempt
    } else {
        $stage = 'preparation-prerequisites'
        $prepareRole = CoverageCi-Role $context 0 $null
        $prepare = CoverageCi-Latest $context $jobs $prepareRole -RequireSuccess
        $prepareWork = Join-Path $request.workDirectory 'prepare-observation'; [void][IO.Directory]::CreateDirectory($prepareWork)
        $inspectedPrepare = CoverageCi-Inspect $context $request.preparationArtifact $prepareRole $prepare $prepareWork $request.driver
        $prepareJobId = $prepare.job.id; $prepareAttempt = $prepare.actualAttempt
        $current.execution = [pscustomobject]@{ started = CoverageCi-Utc $current.job.started_at; completed = $null }
        $chainOwner = [pscustomobject]@{ preparationJobId = $prepareJobId; preparationAttempt = $prepareAttempt }
        CoverageCi-AssertChain $prepare $current $chainOwner
    }
    $stage = 'prepared-raw-admission'
    $preparedWork = Join-Path $request.workDirectory 'prepared-admission'; [void][IO.Directory]::CreateDirectory($preparedWork)
    $prepared = CoverageCi-PreparedCheck $context $preparedWork
    Require-CoverageCi (@($prepared.value.wholePlan.smokeCanonicalIds).Count -gt 0) 'nonempty-smoke-required'
    if ($role.role -eq 1 -and $role.shardIndex -gt 0) {
        $stage = 'smoke-prerequisites'
        $smokeRole = CoverageCi-Role $context 1 0
        $smoke = CoverageCi-Latest $context $jobs $smokeRole -RequireSuccess
        $smokeWork = Join-Path $request.workDirectory 'smoke-observation'; [void][IO.Directory]::CreateDirectory($smokeWork)
        $inspectedSmoke = CoverageCi-Inspect $context $request.smokeArtifact $smokeRole $smoke $smokeWork $request.driver
        CoverageCi-AssertChain $prepare $smoke $inspectedSmoke.owner
        CoverageCi-AssertChain $prepare $current $chainOwner $smoke
        # The caller restores successful smoke at its fixed original root using
        # Consumer RestorePrerequisites. Fresh reimport below proves passing raw
        # owners and the exact current smoke set, not a filename or stored flag.
        CoverageCi-SmokeCheck $context $inspectedSmoke.owner $prepared $smokeWork
    }
    if ($role.role -eq 1) {
        $stage = 'shard-execution'
        $env:FISHAUDIO_SNAPSHOT_CONTEXT = $context.preparation.prepared.snapshotContextPath
        $entry = Join-Path $context.preparation.prepared.sourceDirectory 'tools/Invoke-FullCoverage.ps1'
        [void](Lock-HybridFile $entry)
        $exitCode = Invoke-HybridProcess $context.preparation.prepared.shellFile @('-NoLogo','-NoProfile','-NonInteractive',
            '-File',$entry,'-Operation','Shard','-RepositoryRoot',$context.preparation.prepared.sourceDirectory,
            '-ResultsDirectory',$root,'-PreparedPath',$context.preparation.prepared.preparedPath,
            '-ExpectedPreparedSha256',$context.preparation.prepared.preparedSha256,'-ShardIndex',[string]$role.shardIndex,
            '-ShardCount',[string]$context.shardCount) (Join-Path $request.workDirectory 'shard-process') `
            $context.preparation.prepared.sourceDirectory 0
        # The public coordinator preserves test/collection/admission stages in
        # its raw outcomes. An outer nonzero result is not relabelled a test
        # failure. Explicit Diagnostic can retain a separately observed category.
        Require-CoverageCi ($exitCode -eq 0) 'shard-operation-failed'
    }
    $stage = 'archive-pack-and-inspect'
    $transportExpected = Join-Path $request.workDirectory 'owner-transport-expectation.json'
    $packInput = Join-Path $request.workDirectory 'pack-input.json'
    Write-HybridJson $transportExpected (CoverageCi-TransportExpected $context $root)
    Write-HybridJson $packInput @{ schemaVersion = 1; ownerDirectory = $root; archivePath = $request.archivePath }
    CoverageCi-Tool $context $request.driver.toolFile $request.driver.toolSha256 $request.driver.dotnetFile @(
        'coverage-ci-transport-pack','--input',$packInput,'--expectation',$transportExpected,
        '--output',(Join-Path $request.workDirectory 'pack-receipt.json')) (Join-Path $request.workDirectory 'pack-process') $request.workDirectory
    # Compute our own archive digest and inspect actual payload bytes. The pack
    # receipt is retained as diagnostics and is never read as success authority.
    $archiveSha256 = Get-HybridDigest $request.archivePath
    [void](Lock-HybridFile $request.archivePath $archiveSha256)
    $inspectInput = Join-Path $request.workDirectory 'owner-inspect-input.json'
    $inspectOutput = Join-Path $request.workDirectory 'owner-inspection.json'
    Write-HybridJson $inspectInput @{ schemaVersion = 1; archivePath = $request.archivePath; expectedArchiveSha256 = $archiveSha256 }
    CoverageCi-Tool $context $request.driver.toolFile $request.driver.toolSha256 $request.driver.dotnetFile @(
        'coverage-ci-transport-inspect','--input',$inspectInput,'--expectation',$transportExpected,'--output',$inspectOutput) `
        (Join-Path $request.workDirectory 'owner-inspect-process') $request.workDirectory
    $inspection = Read-HybridJson $inspectOutput
    $evidence = @($inspection.manifest.files | Where-Object path -CEQ $role.evidenceRelativePath)
    Require-CoverageCi ($evidence.Count -eq 1 -and $evidence[0].length -gt 0 -and
        $evidence[0].sha256 -ceq (Get-HybridDigest (Join-Path $root $role.evidenceRelativePath))) 'operation-evidence'
    $owner = [ordered]@{ schemaVersion = 1; kind = 'coverage-ci-operation-owner'; role = $role.role; shardIndex = $role.shardIndex
        shardCount = $context.shardCount; repository = $context.repository; commit = $context.commit; tree = $context.tree
        workflowName = $context.workflowName; workflowRunId = $context.runId; attempt = $context.currentAttempt
        jobId = $current.job.id; finishedUtc = [DateTimeOffset]::UtcNow.ToString('O'); preparationJobId = $prepareJobId
        preparationAttempt = $prepareAttempt; preparation = $context.preparation; originalRoot = $root
        artifactName = CoverageCi-ArtifactName $context $role.role $role.shardIndex $context.currentAttempt
        archiveSha256 = $archiveSha256; evidenceSha256 = $evidence[0].sha256
        inventory = [ordered]@{ directories = @($inspection.manifest.directories); files = @($inspection.manifest.files) } }
} catch { $failure = $_ }
finally {
    try { $env:FISHAUDIO_SNAPSHOT_CONTEXT = $previousSnapshot } catch { if ($null -eq $failure) { $failure = $_ } }
    $failure = CoverageCi-Close $failure
}
if ($null -ne $failure) {
    $reason = CoverageCi-FailureCode $failure
    if ($null -ne $context -and $null -ne $request) {
        try {
            if ($null -eq $diagnostic) { $diagnostic = CoverageCi-Diagnostic $context $request.role $request.shardIndex $category $stage }
            $diagnostic['reason'] = $reason
            CoverageCi-Publish $request.diagnosticPath $diagnostic
        } catch { } # Preserve the original failure. No complete owner was published.
    }
    [Console]::Error.WriteLine('full-coverage-ci-producer: failed stage=' + $stage + ' reason=' + $reason + '; original raw outcomes retained.')
    exit 1
}
# An owner is an upload envelope, never a completed Actions job assertion. The
# later consumer still requires successful exact API job/steps and raw reimport.
try { CoverageCi-Publish $request.ownerRecordPath $owner }
catch { [Console]::Error.WriteLine('full-coverage-ci-producer: failed stage=owner-publication reason=output-publication-failed; pending bytes retained.'); exit 1 }
Write-Output ('full-coverage-ci-producer: upload envelope ready; artifact=' + $owner.artifactName + '; baseline/publication=false.')
exit 0
