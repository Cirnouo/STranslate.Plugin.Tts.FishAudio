param([string]$OutputPath)

$ErrorActionPreference = 'Stop'

function Get-RegressionJobSnapshot {
    param([string]$Repository, [long]$RunId, [scriptblock]$FetchPage)
    if ($Repository -cnotmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or $RunId -le 0 -or $null -eq $FetchPage) {
        throw 'ci-jobs-invalid-context'
    }
    $all = [Collections.Generic.List[object]]::new()
    $ids = [Collections.Generic.HashSet[long]]::new()
    $expected = $null
    for ($page = 1; ; $page++) {
        $data = & $FetchPage $page
        $total = 0L
        if ($null -eq $data -or $data.PSObject.Properties.Name -cnotcontains 'jobs' -or $null -eq $data.jobs -or
            -not [long]::TryParse([string]$data.total_count, [ref]$total) -or $total -lt 0 -or $total -gt [int]::MaxValue) {
            throw 'ci-jobs-invalid-page'
        }
        if ($null -ne $expected -and $total -ne $expected) { throw 'ci-jobs-total-changed' }
        $expected = $total
        $jobs = @($data.jobs)
        if ($jobs.Count -gt 100) { throw 'ci-jobs-invalid-page-size' }
        foreach ($job in $jobs) {
            $id = 0L
            $jobRun = 0L
            if ($null -eq $job -or -not [long]::TryParse([string]$job.id, [ref]$id) -or $id -le 0 -or
                -not [long]::TryParse([string]$job.run_id, [ref]$jobRun) -or $jobRun -ne $RunId -or -not $ids.Add($id)) {
                throw 'ci-jobs-invalid-or-duplicate-job'
            }
            $all.Add($job)
        }
        if ($all.Count -gt $expected) { throw 'ci-jobs-pagination-mismatch' }
        if ($all.Count -eq $expected -or $jobs.Count -lt 100) { break }
    }
    if ($all.Count -ne $expected) { throw 'ci-jobs-pagination-mismatch' }
    [pscustomobject]@{ repository = $Repository; runId = $RunId; filter = 'all'; totalCount = $expected; jobs = @($all.ToArray()) }
}

if ($MyInvocation.InvocationName -ne '.') {
    if ([string]::IsNullOrWhiteSpace($OutputPath) -or [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        throw 'ci-jobs-missing-output-or-token'
    }
    $repository = $env:GITHUB_REPOSITORY
    $runId = 0L
    if (-not [long]::TryParse($env:GITHUB_RUN_ID, [ref]$runId)) { throw 'ci-jobs-invalid-run' }
    $snapshot = Get-RegressionJobSnapshot $repository $runId {
        param($page)
        Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/actions/runs/$runId/jobs?filter=all&per_page=100&page=$page" `
            -Headers @{ Authorization = "Bearer $env:GITHUB_TOKEN"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' } `
            -TimeoutSec 30 -MaximumRedirection 0
    }
    $path = [IO.Path]::GetFullPath($OutputPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($snapshot | ConvertTo-Json -Depth 30 -Compress))
    $file = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $file.Write($bytes, 0, $bytes.Length); $file.Flush($true) } finally { $file.Dispose() }
}
