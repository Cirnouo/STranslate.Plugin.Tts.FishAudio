#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ArtifactDirectory,[Parameter(Mandatory)][string]$JobAttemptsPath,
    [Parameter(Mandatory)][string]$ResultsDirectory,[string]$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')))
. (Join-Path $PSScriptRoot 'HybridCi.Common.ps1')
. (Join-Path $PSScriptRoot 'HybridReceipt.ps1')
$primary = $null; $verified = $false
try {
    $context = Get-HybridCiContext $RepositoryRoot
    foreach ($path in @($ArtifactDirectory,$JobAttemptsPath,$ResultsDirectory)) {
        Require-HybridCi ([IO.Path]::IsPathFullyQualified($path)) 'hybrid-ci-path-not-absolute'
    }
    $jobsPin = Lock-HybridCiFile $JobAttemptsPath
    $jobs = Read-HybridCiJson $JobAttemptsPath
    $bundles = Get-HybridCiBundles $ArtifactDirectory $context
    $selected = Select-HybridCiExecution $jobs $context ([int[]]@($bundles.Keys))
    $bundle = $bundles[$selected.attempt]
    $ownerPath = Join-Path $bundle 'ci-owner.json'
    $ownerPin = Lock-HybridCiFile $ownerPath
    $owner = Read-HybridCiJson $ownerPath
    Assert-HybridCiOwner $owner $context $selected.attempt $bundle $selected.job
    New-HybridCiDirectory $ResultsDirectory $context
    # Raw request/process records bind absolute paths. Admit the current runner
    # layout before restoring; never rewrite evidence or follow JSON paths.
    $restored = Restore-HybridCiBundle $bundle $owner $context $selected.attempt
    $sessionRoot = Join-Path $restored 'session'
    $producerRoot = Join-Path $restored 'producer-admission'
    $producerPath = Join-Path $producerRoot 'admission.json'
    $producerPin = Lock-HybridCiFile $producerPath
    $producer = Read-HybridCiJson $producerPath
    $session = Read-HybridCiJson (Join-Path $sessionRoot 'manifest.json')
    foreach ($record in @($session,$producer)) {
        Require-HybridCi ($record.complete -eq $true -and $record.baselineClaim -eq $false -and
            $record.source.snapshot -eq $false -and $record.source.dirty -eq $false -and $null -eq $record.source.tree -and
            $record.source.commit -ceq $context.commit -and $record.source.sourceCommit -ceq $context.commit -and
            $record.legacySourceTree -ceq $context.tree) 'hybrid-ci-session-source'
    }
    # Build current assemblies/tool through the standard driver. Discover never
    # invokes case delegates; the independent consumer performs its own fresh
    # discovery again and compares every exact build/catalog/raw-result owner.
    & (Join-Path $context.checkoutRoot 'tools/Test.ps1') -Mode Discover -RepositoryRoot $context.checkoutRoot `
        -ResultsDirectory (Join-Path $ResultsDirectory 'current-build-discovery')
    Require-HybridCi ($LASTEXITCODE -eq 0) 'hybrid-ci-current-build-discovery'
    $admissionRoot = Join-Path $ResultsDirectory 'independent-admission'
    & (Join-Path $context.checkoutRoot 'tools/Assert-HybridSession.ps1') -RepositoryRoot $context.checkoutRoot `
        -SessionDirectory $sessionRoot -OutputDirectory $admissionRoot
    Require-HybridCi ($LASTEXITCODE -eq 0) 'hybrid-ci-raw-admission-rejected'
    $admissionPath = Join-Path $admissionRoot 'admission.json'
    $admissionPin = Lock-HybridCiFile $admissionPath
    $admission = Read-HybridCiJson $admissionPath
    Require-HybridCi ($admission.schemaVersion -eq 1 -and $admission.kind -ceq 'hybrid-session-admission' -and
        $admission.complete -eq $true -and $admission.runId -ceq $session.runId -and
        $admission.source.snapshot -eq $false -and $admission.source.dirty -eq $false -and
        $admission.source.commit -ceq $context.commit -and $admission.legacySourceTree -ceq $context.tree -and
        $admission.baselineClaim -eq $false) 'hybrid-ci-independent-admission'
    Assert-HybridCiLegacyRecords $session $owner $restored
    # Freeze and recheck all admitted bytes, including the separate producer
    # admission, then re-enumerate physical attempts and committed generations.
    foreach ($item in @(@{ directory = $producerRoot; admission = $producer },@{ directory = $admissionRoot; admission = $admission })) {
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        Require-HybridCi ($item.admission.receiptArtifacts -is [Array] -and $item.admission.receiptArtifacts.Count -gt 0) 'hybrid-ci-receipt-artifacts'
        foreach ($artifact in $item.admission.receiptArtifacts) {
            Require-HybridCi ($artifact.path -is [string] -and [IO.Path]::IsPathFullyQualified($artifact.path) -and
                $seen.Add($artifact.path) -and ($artifact.path.StartsWith($restored+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
                $artifact.path.StartsWith($admissionRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase))) 'hybrid-ci-receipt-path'
            [void](Lock-HybridCiFile $artifact.path ([string]$artifact.sha256))
        }
        Assert-HybridGenerationReceipt $sessionRoot $item.directory $item.admission
    }
    Assert-HybridCiOwner $owner $context $selected.attempt $bundle $selected.job
    Assert-HybridCiOwner $owner $context $selected.attempt $restored $selected.job
    Write-HybridCiJson (Join-Path $ResultsDirectory 'verification.json') ([ordered]@{
        schemaVersion = 1; kind = 'hybrid-ci-verification'; complete = $true; repository = $context.repository; commit = $context.commit
        tree = $context.tree; workflowRunId = [string]$context.runId; attempt = $selected.attempt; jobId = $selected.job.id
        apiHead = $context.executionHead; sdk = $context.sdk; ownerSha256 = $ownerPin.sha256; jobsSha256 = $jobsPin.sha256
        sessionRunId = $session.runId; producerAdmissionSha256 = $producerPin.sha256; admissionSha256 = $admissionPin.sha256; baselineClaim = $false
    })
    $verified = $true
} catch { $primary = $_ }
finally {
    foreach ($close in @({ Close-HybridLocks },{ Close-HybridCiLocks })) {
        try { & $close } catch { if ($null -eq $primary) { $primary = $_; $script:HybridCiFailure = 'hybrid-ci-cleanup' } }
    }
}
if ($null -ne $primary -or -not $verified) {
    $reason = $(if ($script:HybridCiFailure -ceq 'hybrid-ci-operation-failed') { $script:failureCode } else { $script:HybridCiFailure })
    [Console]::Error.WriteLine('hybrid-ci: '+$reason+' ('+$primary.Exception.GetType().Name+').'); exit 1
}
Write-Output ('hybrid-ci: verified complete hybrid attempt '+$selected.attempt+'.')
exit 0
