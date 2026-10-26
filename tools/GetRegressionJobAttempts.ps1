param([string]$OutputPath)

$ErrorActionPreference = 'Stop'

function Get-RegressionJobSnapshot {
    param([string]$Repository, [long]$RunId, [int]$CurrentAttempt, [scriptblock]$FetchPage, [scriptblock]$FetchAttempt)
    if ($Repository -cnotmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or $RunId -le 0 -or $CurrentAttempt -le 0 -or
        $null -eq $FetchPage -or $null -eq $FetchAttempt) {
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
    $attempts = [Collections.Generic.List[object]]::new()
    $previousStart = [DateTimeOffset]::MinValue
    foreach ($number in 1..$CurrentAttempt) {
        $record = & $FetchAttempt $number
        $recordRun = 0L
        $recordAttempt = 0
        if ($null -eq $record -or -not [long]::TryParse([string]$record.id, [ref]$recordRun) -or $recordRun -ne $RunId -or
            -not [int]::TryParse([string]$record.run_attempt, [ref]$recordAttempt) -or $recordAttempt -ne $number) {
            throw 'ci-jobs-invalid-attempt-owner'
        }
        # REST JSON dates may already be materialized by PowerShell. Only an
        # explicit UTC kind/offset can retain the attempt's time boundary.
        $rawStart = $record.run_started_at
        if ($rawStart -is [DateTime]) {
            if ($rawStart.Kind -ne [DateTimeKind]::Utc) { throw 'ci-jobs-invalid-attempt-start' }
            $startText = $rawStart.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
        } elseif ($rawStart -is [DateTimeOffset]) {
            if ($rawStart.Offset -ne [TimeSpan]::Zero) { throw 'ci-jobs-invalid-attempt-start' }
            $startText = $rawStart.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
        } elseif ($rawStart -is [string]) { $startText = $rawStart }
        else { throw 'ci-jobs-invalid-attempt-start' }
        $started = [DateTimeOffset]::MinValue
        if ($startText -cnotmatch '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|\+00:00)\z' -or
            -not [DateTimeOffset]::TryParse($startText, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$started) -or
            $started.Offset -ne [TimeSpan]::Zero -or $started -le $previousStart) {
            throw 'ci-jobs-invalid-attempt-start'
        }
        $attempts.Add([pscustomobject]@{ runAttempt = $number; runStartedAt = $startText })
        $previousStart = $started
    }
    [pscustomobject]@{ repository = $Repository; runId = $RunId; filter = 'all'; totalCount = $expected; jobs = @($all.ToArray()); attempts = @($attempts.ToArray()) }
}

if ($MyInvocation.InvocationName -ne '.') {
    if ([string]::IsNullOrWhiteSpace($OutputPath) -or [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        throw 'ci-jobs-missing-output-or-token'
    }
    $repository = $env:GITHUB_REPOSITORY
    $runId = 0L
    if (-not [long]::TryParse($env:GITHUB_RUN_ID, [ref]$runId)) { throw 'ci-jobs-invalid-run' }
    $currentAttempt = 0
    if (-not [int]::TryParse($env:GITHUB_RUN_ATTEMPT, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref]$currentAttempt) -or $currentAttempt -le 0) {
        throw 'ci-jobs-invalid-attempt'
    }
    $snapshot = Get-RegressionJobSnapshot $repository $runId $currentAttempt {
        param($page)
        Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/actions/runs/$runId/jobs?filter=all&per_page=100&page=$page" `
            -Headers @{ Authorization = "Bearer $env:GITHUB_TOKEN"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' } `
            -TimeoutSec 30 -MaximumRedirection 0
    } {
        param($number)
        Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/actions/runs/$runId/attempts/$number" `
            -Headers @{ Authorization = "Bearer $env:GITHUB_TOKEN"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' } `
            -TimeoutSec 30 -MaximumRedirection 0
    }
    $path = [IO.Path]::GetFullPath($OutputPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($snapshot | ConvertTo-Json -Depth 30 -Compress))
    $file = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $file.Write($bytes, 0, $bytes.Length); $file.Flush($true) } finally { $file.Dispose() }
}
