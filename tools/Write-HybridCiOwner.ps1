#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$RepositoryRoot)
. (Join-Path $PSScriptRoot 'HybridCi.Common.ps1')
$primary = $null
try {
    $context = Get-HybridCiContext $RepositoryRoot
    Require-HybridCi ($env:GITHUB_JOB -ceq 'hybrid-validation') 'hybrid-ci-producer-job'
    $bundle = Get-HybridCiBundlePath $context $context.attempt
    Assert-HybridCiRegular $bundle -Directory
    $files = @(Get-HybridCiInventory $bundle)
    Write-HybridCiJson (Join-Path $bundle 'ci-owner.json') ([ordered]@{
        schemaVersion = 1; kind = 'hybrid-ci-owner'; repository = $context.repository; commit = $context.commit
        workflowRunId = [string]$context.runId; attempt = $context.attempt; job = $env:GITHUB_JOB
        head = $context.commit; tree = $context.tree; sdk = $context.sdk; checkoutRoot = $context.checkoutRoot
        tempRoot = $context.tempRoot; bundleRoot = $bundle; dotnet = $context.dotnet; shell = $context.shell
        finishedUtc = [DateTimeOffset]::UtcNow.ToString('O',[Globalization.CultureInfo]::InvariantCulture)
        files = $files; baselineClaim = $false
    })
} catch { $primary = $_ }
finally {
    try { Close-HybridCiLocks } catch { if ($null -eq $primary) { $primary = $_; $script:HybridCiFailure = 'hybrid-ci-owner-cleanup' } }
}
if ($null -ne $primary) { [Console]::Error.WriteLine('hybrid-ci: '+$script:HybridCiFailure+' ('+$primary.Exception.GetType().Name+').'); exit 1 }
exit 0
