#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [Parameter(Mandatory)][string]$JobAttemptsPath,
    [Parameter(Mandatory)][string]$ResultsDirectory,
    [string]$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$failureCode = 'framework-ci-operation-failed'
$root = [IO.Path]::GetFullPath($RepositoryRoot)

function Require-Framework([bool]$Condition, [string]$Code) {
    if (-not $Condition) { $script:failureCode = $Code; throw $Code }
}

function Read-FrameworkJson([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    Require-Framework (-not $item.PSIsContainer -and $item.Length -gt 0 -and $item.Length -le 67108864) 'framework-ci-json-size'
    $parameters = @{ InputObject = [IO.File]::ReadAllText($Path) }
    # Preserve authenticated UTC text rather than letting the shell convert an
    # explicit offset to the local machine's time zone before admission.
    if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $parameters.DateKind = 'String' }
    return ConvertFrom-Json @parameters
}

function Read-FrameworkNumber($Value) {
    Require-Framework (($Value -is [int] -or $Value -is [long]) -and $Value -ge 0) 'framework-ci-invalid-number'
    return [long]$Value
}

function Resolve-FrameworkFile([string]$Parent, [string]$Relative, [string]$Digest) {
    Require-Framework (-not [string]::IsNullOrWhiteSpace($Relative) -and -not [IO.Path]::IsPathRooted($Relative) -and
        -not $Relative.Contains('\') -and @($Relative.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -eq 0) 'framework-ci-artifact-path'
    $parentPath = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Parent))
    $full = [IO.Path]::GetFullPath((Join-Path $parentPath $Relative))
    Require-Framework ($full.StartsWith($parentPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'framework-ci-artifact-escape'
    $current = Get-Item -LiteralPath $full -Force
    Require-Framework (-not $current.PSIsContainer) 'framework-ci-artifact-not-file'
    while ($null -ne $current) {
        Require-Framework (($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'framework-ci-artifact-reparse'
        if ($current.FullName.Equals($parentPath, [StringComparison]::OrdinalIgnoreCase)) { break }
        $current = $(if ($current -is [IO.DirectoryInfo]) { $current.Parent } else { $current.Directory })
    }
    if ($Digest) {
        Require-Framework ($Digest -cmatch '^[a-f0-9]{64}$' -and
            (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $Digest) 'framework-ci-artifact-digest'
    }
    return $full
}

function Read-FrameworkUtc($Value) {
    if ($Value -is [DateTime]) {
        Require-Framework ($Value.Kind -eq [DateTimeKind]::Utc) 'framework-ci-invalid-time'
        return [DateTimeOffset]::new($Value)
    }
    if ($Value -is [DateTimeOffset]) {
        Require-Framework ($Value.Offset -eq [TimeSpan]::Zero) 'framework-ci-invalid-time'
        return $Value
    }
    $time = [DateTimeOffset]::MinValue
    Require-Framework ($Value -is [string] -and
        $Value -cmatch '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|\+00:00)\z' -and
        [DateTimeOffset]::TryParse($Value, [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind, [ref]$time) -and $time.Offset -eq [TimeSpan]::Zero) 'framework-ci-invalid-time'
    return $time
}

function Assert-FrameworkCaseSet($Actual, $Expected) {
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($id in @($Actual)) { Require-Framework ($id -is [string] -and -not [string]::IsNullOrWhiteSpace($id) -and $seen.Add($id)) 'framework-ci-duplicate-case' }
    Require-Framework ($seen.Count -eq @($Expected).Count -and $seen.SetEquals([string[]]@($Expected))) 'framework-ci-case-set'
}

function Read-FrameworkExecutionHead([string]$CheckoutCommit) {
    if ($env:GITHUB_EVENT_NAME -cne 'pull_request') { return $CheckoutCommit }
    $script:failureCode = 'framework-ci-pull-request-context'
    Require-Framework (-not [string]::IsNullOrWhiteSpace($env:GITHUB_EVENT_PATH) -and
        [IO.Path]::IsPathFullyQualified($env:GITHUB_EVENT_PATH) -and
        [IO.File]::Exists($env:GITHUB_EVENT_PATH)) 'framework-ci-pull-request-context'
    try { $pullRequestEvent = Read-FrameworkJson $env:GITHUB_EVENT_PATH }
    catch { $script:failureCode = 'framework-ci-pull-request-context'; throw }
    Require-Framework ($pullRequestEvent.repository.full_name -ceq $env:GITHUB_REPOSITORY -and
        $pullRequestEvent.pull_request.base.repo.full_name -ceq $env:GITHUB_REPOSITORY -and
        $pullRequestEvent.pull_request.head.sha -is [string] -and
        $pullRequestEvent.pull_request.head.sha -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$' -and
        $pullRequestEvent.pull_request.base.sha -is [string] -and
        $pullRequestEvent.pull_request.base.sha -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$') 'framework-ci-pull-request-context'

    # The runner event describes this immutable run, including reruns. A moving
    # PR REST head or an unchecked head-or-merge comparison is not a substitute.
    $script:failureCode = 'framework-ci-pull-request-parents'
    $parentLines = @(& git -C $root rev-list --parents -n 1 HEAD)
    Require-Framework ($LASTEXITCODE -eq 0 -and $parentLines.Count -eq 1 -and
        $parentLines[0] -is [string]) 'framework-ci-pull-request-parents'
    $parents = $parentLines[0].Split([char[]]" `t", [StringSplitOptions]::RemoveEmptyEntries)
    Require-Framework ($parents.Count -eq 3 -and $parents[0] -ceq $CheckoutCommit -and
        $parents[1] -ceq $pullRequestEvent.pull_request.base.sha -and
        $parents[2] -ceq $pullRequestEvent.pull_request.head.sha) 'framework-ci-pull-request-parents'
    $script:failureCode = 'framework-ci-operation-failed'
    return $pullRequestEvent.pull_request.head.sha
}

function Read-FrameworkExecution($Job) {
    Require-Framework ($Job.head_sha -is [string] -and $Job.head_sha -cmatch '^[A-Fa-f0-9]{40}([A-Fa-f0-9]{24})?$' -and
        $Job.head_sha.ToLowerInvariant() -ceq $executionHead) 'framework-ci-execution-sha'
    $started = Read-FrameworkUtc $Job.started_at
    $completed = Read-FrameworkUtc $Job.completed_at
    $runnerId = Read-FrameworkNumber $Job.runner_id
    Require-Framework ($completed -ge $started -and $runnerId -gt 0 -and
        $Job.runner_name -is [string] -and -not [string]::IsNullOrWhiteSpace($Job.runner_name) -and
        @($Job.steps).Count -gt 0) 'framework-ci-execution-identity'
    $steps = [Collections.Generic.List[object]]::new()
    $previousNumber = 0L
    foreach ($step in $Job.steps) {
        $number = Read-FrameworkNumber $step.number
        Require-Framework ($step.name -is [string] -and -not [string]::IsNullOrWhiteSpace($step.name) -and
            $step.status -is [string] -and ($null -eq $step.conclusion -or $step.conclusion -is [string]) -and
            $number -gt $previousNumber) 'framework-ci-execution-step'
        $stepStart = $null
        $stepEnd = $null
        if ($null -eq $step.started_at -and $null -eq $step.completed_at) {
            Require-Framework ($step.conclusion -ceq 'skipped') 'framework-ci-execution-step-time'
        } else {
            Require-Framework ($null -ne $step.started_at -and $null -ne $step.completed_at) 'framework-ci-execution-step-time'
            $stepStart = Read-FrameworkUtc $step.started_at
            $stepEnd = Read-FrameworkUtc $step.completed_at
            Require-Framework ($stepStart -ge $started -and $stepEnd -ge $stepStart -and $stepEnd -le $completed) 'framework-ci-execution-step-time'
        }
        $steps.Add([ordered]@{
            name = $step.name; status = $step.status; conclusion = $step.conclusion; number = $number
            started = $(if ($null -eq $stepStart) { $null } else { $stepStart.UtcTicks })
            completed = $(if ($null -eq $stepEnd) { $null } else { $stepEnd.UtcTicks })
        })
        $previousNumber = $number
    }
    $stamp = [ordered]@{
        name = $Job.name; headSha = $Job.head_sha.ToLowerInvariant(); status = $Job.status; conclusion = $Job.conclusion
        started = $started.UtcTicks; completed = $completed.UtcTicks
        runnerId = $runnerId; runnerName = $Job.runner_name; steps = $steps.ToArray()
    } | ConvertTo-Json -Depth 10 -Compress
    return [pscustomobject]@{ started = $started; completed = $completed; stamp = $stamp }
}

function Invoke-FrameworkTool([string[]]$Arguments) {
    & dotnet $script:tool @Arguments
    Require-Framework ($LASTEXITCODE -eq 0) 'framework-ci-parser-rejected'
}

try {
    foreach ($path in @($ArtifactDirectory, $JobAttemptsPath, $ResultsDirectory)) {
        Require-Framework ([IO.Path]::IsPathFullyQualified($path)) 'framework-ci-path-must-be-absolute'
    }
    Require-Framework ($IsWindows -and (dotnet --version).Trim() -ceq '10.0.403') 'framework-ci-sdk-or-platform'
    $commit = $env:GITHUB_SHA
    $runId = 0L
    $currentAttempt = 0
    Require-Framework ($commit -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$' -and
        [long]::TryParse($env:GITHUB_RUN_ID, [ref]$runId) -and $runId -gt 0 -and
        [int]::TryParse($env:GITHUB_RUN_ATTEMPT, [ref]$currentAttempt) -and $currentAttempt -gt 0) 'framework-ci-context'
    $head = (& git -C $root rev-parse --verify HEAD).Trim()
    Require-Framework ($LASTEXITCODE -eq 0 -and $head -ceq $commit) 'framework-ci-checkout'
    $executionHead = Read-FrameworkExecutionHead $commit
    $tree = (& git -C $root rev-parse 'HEAD^{tree}').Trim()
    Require-Framework ($LASTEXITCODE -eq 0 -and $tree -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$') 'framework-ci-checkout-tree'
    $jobs = Read-FrameworkJson $JobAttemptsPath
    Require-Framework ($jobs.repository -ceq $env:GITHUB_REPOSITORY -and [long]$jobs.runId -eq $runId -and
        $jobs.filter -ceq 'all' -and $jobs.totalCount -eq @($jobs.jobs).Count -and @($jobs.attempts).Count -eq $currentAttempt) 'framework-ci-jobs-snapshot'
    $fences = @()
    foreach ($attempt in $jobs.attempts) {
        Require-Framework ($attempt.runAttempt -eq $fences.Count + 1) 'framework-ci-attempt-fence'
        $fence = Read-FrameworkUtc $attempt.runStartedAt
        Require-Framework ($fences.Count -eq 0 -or $fence -gt $fences[-1]) 'framework-ci-attempt-fence'
        $fences += $fence
    }
    $views = [Collections.Generic.List[object]]::new()
    $jobIds = [Collections.Generic.HashSet[long]]::new()
    $advertisedOwners = [Collections.Generic.HashSet[int]]::new()
    foreach ($job in $jobs.jobs) {
        $number = Read-FrameworkNumber $job.run_attempt
        $jobId = Read-FrameworkNumber $job.id
        $jobRun = Read-FrameworkNumber $job.run_id
        Require-Framework ($jobId -gt 0 -and $jobIds.Add($jobId) -and $jobRun -eq $runId -and
            $number -gt 0 -and $number -le $currentAttempt -and $job.name -is [string] -and
            $job.status -is [string] -and ($null -eq $job.conclusion -or $job.conclusion -is [string])) 'framework-ci-job-owner'
        $marker = 'NUnit foundation'
        $start = $job.name.LastIndexOf($marker, [StringComparison]::Ordinal)
        if ($start -lt 0) { continue }
        Require-Framework (($start -eq 0 -or $job.name.Substring(0, $start).EndsWith(' / ', [StringComparison]::Ordinal)) -and
            $job.name.EndsWith($marker, [StringComparison]::Ordinal) -and
            $job.name.IndexOf($marker, [StringComparison]::Ordinal) -eq $start -and $advertisedOwners.Add($number)) 'framework-ci-ambiguous-job-name'
        $views.Add([pscustomobject]@{ attempt = [int]$number; job = $job })
    }
    $owners = @{}
    $executions = @{}
    foreach ($view in $views | Sort-Object attempt) {
        $number = $view.attempt
        $job = $view.job
        # A new unsuccessful execution remains an owner even without timestamps.
        if ($job.status -cne 'completed' -or $job.conclusion -cne 'success') {
            $owners[$number] = $job
            continue
        }
        $execution = Read-FrameworkExecution $job
        if ($execution.started -ge $fences[$number - 1]) {
            $owners[$number] = $job
            $executions[$number] = $execution
            continue
        }
        # Match the legacy verifier's cached-sibling witness. New advertised IDs
        # and attempts alone do not establish a new execution or artifact owner.
        Require-Framework ($execution.completed -lt $fences[$number - 1]) 'framework-ci-overlapping-cached-job'
        $prior = @($executions.Keys | Where-Object { $_ -lt $number -and $executions[$_].stamp -ceq $execution.stamp })
        Require-Framework ($prior.Count -eq 1) 'framework-ci-unmatched-or-ambiguous-cached-job'
    }
    $bundles = @{}
    $prefix = 'framework-report-' + $commit + '-' + $runId + '-'
    $artifactRoot = Get-Item -LiteralPath $ArtifactDirectory -Force
    Require-Framework ($artifactRoot.PSIsContainer -and
        ($artifactRoot.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'framework-ci-foreign-artifact'
    $entries = @(Get-ChildItem -LiteralPath $ArtifactDirectory -Force)
    $flatOwnerEntries = @($entries | Where-Object { -not $_.PSIsContainer -and $_.Name -ceq 'ci-owner.json' })
    $artifactLayout = 'wrapped-attempts'
    if ($flatOwnerEntries.Count -gt 0) {
        # The pinned download action extracts a single pattern match directly
        # into its destination. Its owner locates an attempt, not an authority
        # replacing the job history or the source/content checks below.
        $flatFiles = @('ci-owner.json', 'manifest.json', 'owner.json', 'source.json')
        $flatDirectories = @('phases', 'plugin', 'runtime-evidence', 'testing-tool')
        Require-Framework ($flatOwnerEntries.Count -eq 1) 'framework-ci-foreign-artifact'
        foreach ($entry in $entries) {
            Require-Framework (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and
                $(if ($entry.PSIsContainer) { $flatDirectories -ccontains $entry.Name }
                  else { $flatFiles -ccontains $entry.Name })) 'framework-ci-foreign-artifact'
        }
        $flatOwnerPath = Resolve-FrameworkFile $ArtifactDirectory 'ci-owner.json' ''
        $flatOwner = Read-FrameworkJson $flatOwnerPath
        Require-Framework ($flatOwner.attempt -is [string] -or $flatOwner.attempt -is [int] -or
            $flatOwner.attempt -is [long]) 'framework-ci-artifact-attempt'
        $attemptText = if ($flatOwner.attempt -is [string]) { $flatOwner.attempt }
            else { $flatOwner.attempt.ToString([Globalization.CultureInfo]::InvariantCulture) }
        $number = 0
        Require-Framework ($attemptText -cmatch '^[1-9][0-9]*\z' -and
            [int]::TryParse($attemptText, [Globalization.NumberStyles]::None,
            [Globalization.CultureInfo]::InvariantCulture, [ref]$number) -and $number -gt 0 -and $number -le $currentAttempt -and
            $attemptText -ceq $number.ToString([Globalization.CultureInfo]::InvariantCulture)) 'framework-ci-artifact-attempt'
        $bundles[$number] = $ArtifactDirectory
        $artifactLayout = 'flat-single'
    } else {
        foreach ($directory in $entries) {
            Require-Framework ($directory.PSIsContainer -and
                ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and
                $directory.Name.StartsWith($prefix, [StringComparison]::Ordinal)) 'framework-ci-foreign-artifact'
            $suffix = $directory.Name.Substring($prefix.Length)
            $number = 0
            Require-Framework ([int]::TryParse($suffix, [Globalization.NumberStyles]::None,
                [Globalization.CultureInfo]::InvariantCulture, [ref]$number) -and $number -gt 0 -and $number -le $currentAttempt -and
                $suffix -ceq $number.ToString([Globalization.CultureInfo]::InvariantCulture) -and -not $bundles.ContainsKey($number)) 'framework-ci-artifact-attempt'
            $bundles[$number] = $directory.FullName
        }
    }
    $attempts = @($owners.Keys) + @($bundles.Keys)
    Require-Framework ($attempts.Count -gt 0) 'framework-ci-missing-attempt'
    $latest = [int]($attempts | Measure-Object -Maximum).Maximum
    Require-Framework ($owners.ContainsKey($latest) -and $bundles.ContainsKey($latest)) 'framework-ci-missing-latest-evidence'
    $job = $owners[$latest]
    Require-Framework ($job.status -ceq 'completed' -and $job.conclusion -ceq 'success') 'framework-ci-unsuccessful-latest-job'
    $started = Read-FrameworkUtc $job.started_at
    $completed = Read-FrameworkUtc $job.completed_at
    Require-Framework ($completed -ge $started) 'framework-ci-execution-time'
    $executionSteps = @($job.steps | Where-Object name -CEQ 'Run all discovered NUnit foundation cases in both assemblies')
    Require-Framework ($executionSteps.Count -eq 1 -and $executionSteps[0].status -ceq 'completed' -and
        $executionSteps[0].conclusion -ceq 'success') 'framework-ci-missing-execution-step'

    $bundle = $bundles[$latest]
    $owner = Read-FrameworkJson (Resolve-FrameworkFile $bundle 'ci-owner.json' '')
    Require-Framework ($owner.schemaVersion -eq 1 -and $owner.repository -ceq $env:GITHUB_REPOSITORY -and
        $owner.commit -ceq $commit -and $owner.head -ceq $commit -and $owner.tree -ceq $tree -and
        [long]$owner.workflowRunId -eq $runId -and [int]$owner.attempt -eq $latest -and
        $owner.job -ceq 'nunit-foundation' -and $owner.sdk -ceq '10.0.403') 'framework-ci-artifact-owner'
    $manifest = Read-FrameworkJson (Resolve-FrameworkFile $bundle 'manifest.json' '')
    Require-Framework ($manifest.schemaVersion -eq 1 -and $manifest.kind -ceq 'unified-test-session' -and $manifest.complete -eq $true -and
        $manifest.mode -ceq 'Full' -and $manifest.platform -ceq 'VSTest' -and $manifest.configuration -ceq 'Release' -and
        $manifest.selection -ceq 'all-discovered' -and $manifest.baselineClaim -eq $false -and
        $manifest.source.snapshot -eq $false -and $manifest.source.dirty -eq $false -and $null -eq $manifest.source.tree -and
        $manifest.source.commit -ceq $commit -and $manifest.source.sourceCommit -ceq $commit -and
        @($manifest.assemblies).Count -eq 2) 'framework-ci-session-identity'
    $finished = Read-FrameworkUtc $manifest.finishedUtc
    Require-Framework ($finished -ge $started -and $finished -le $completed) 'framework-ci-session-time'

    Require-Framework (-not [IO.Directory]::Exists($ResultsDirectory) -and -not [IO.File]::Exists($ResultsDirectory)) 'framework-ci-results-must-be-fresh'
    [IO.Directory]::CreateDirectory($ResultsDirectory) | Out-Null
    $freshDirectory = Join-Path $ResultsDirectory 'current-discovery'
    & (Join-Path $root 'tools/Test.ps1') -Mode Discover -ResultsDirectory $freshDirectory -RepositoryRoot $root
    Require-Framework ($LASTEXITCODE -eq 0) 'framework-ci-current-discovery-failed'
    $fresh = Read-FrameworkJson (Join-Path $freshDirectory 'manifest.json')
    Require-Framework ($fresh.schemaVersion -eq 1 -and $fresh.kind -ceq 'unified-test-session' -and $fresh.complete -eq $true -and
        $fresh.mode -ceq 'Discover' -and $fresh.platform -ceq 'VSTest' -and $fresh.configuration -ceq 'Release' -and
        @($fresh.assemblies).Count -eq 2 -and $fresh.source.snapshot -eq $false -and
        $fresh.source.commit -ceq $commit -and $fresh.source.sourceCommit -ceq $commit -and $fresh.source.dirty -eq $false -and
        $null -eq $fresh.source.tree -and
        $fresh.source.fingerprint -ceq $manifest.source.fingerprint -and
        $fresh.source.fingerprintKind -ceq $manifest.source.fingerprintKind -and
        $fresh.toolSha256 -ceq $manifest.toolSha256) 'framework-ci-current-source-or-tool'
    $script:tool = Join-Path $root 'tools/FishAudio.Testing/bin/Release/net10.0-windows/FishAudio.Testing.dll'
    $keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $globalIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($phase in $manifest.phases) {
        Require-Framework ($phase.complete -eq $true -and $phase.exitCode -eq 0) 'framework-ci-incomplete-phase'
    }
    foreach ($assembly in $manifest.assemblies) {
        $key = [string]$assembly.key
        $current = @($fresh.assemblies | Where-Object key -CEQ $key)
        Require-Framework ($key -cin @('plugin', 'testing-tool') -and $keys.Add($key) -and $current.Count -eq 1 -and
            $assembly.project -ceq $current[0].project -and $assembly.assembly -ceq $current[0].assembly -and
            $assembly.sha256 -ceq $current[0].sha256 -and $assembly.executed -eq $true -and
            $assembly.discoveredCount -gt 0 -and $assembly.selectedCount -eq $assembly.discoveredCount -and
            $assembly.passedCount -eq $assembly.discoveredCount -and
            $assembly.discoveredCount -eq $current[0].discoveredCount) 'framework-ci-assembly-identity-or-count'
        $paths = @{}
        foreach ($name in @('catalog', 'selection', 'terminal', 'resultXml', 'trx')) {
            $digest = [string]$assembly.($name + 'Sha256')
            Require-Framework ($digest -cmatch '^[a-f0-9]{64}$') 'framework-ci-missing-artifact-digest'
            $paths[$name] = Resolve-FrameworkFile $bundle ([string]$assembly.$name) $digest
        }
        $catalog = Read-FrameworkJson $paths.catalog
        $currentCatalog = Read-FrameworkJson (Join-Path $freshDirectory $current[0].catalog)
        $selection = Read-FrameworkJson $paths.selection
        $terminal = Read-FrameworkJson $paths.terminal
        Require-Framework ($catalog.schemaVersion -eq 1 -and $catalog.kind -ceq 'nunit-catalog' -and $catalog.complete -eq $true -and
            $selection.schemaVersion -eq 1 -and $terminal.schemaVersion -eq 1 -and $terminal.kind -ceq 'nunit-terminal' -and
            $terminal.complete -eq $true -and $terminal.passedCount -eq $assembly.passedCount) 'framework-ci-catalog-or-terminal'
        $ids = @($currentCatalog.cases | ForEach-Object canonicalId)
        Assert-FrameworkCaseSet @($catalog.cases | ForEach-Object canonicalId) $ids
        Assert-FrameworkCaseSet @($assembly.selectedIds) $ids
        Assert-FrameworkCaseSet @($selection.ids) $ids
        Assert-FrameworkCaseSet @($terminal.cases | ForEach-Object canonicalId) $ids
        foreach ($case in $catalog.cases) {
            $match = @($currentCatalog.cases | Where-Object canonicalId -CEQ $case.canonicalId)
            Require-Framework ($match.Count -eq 1 -and $match[0].fullName -ceq $case.fullName -and
                $match[0].className -ceq $case.className -and $match[0].methodName -ceq $case.methodName -and
                $globalIds.Add([string]$case.canonicalId)) 'framework-ci-current-catalog-mismatch'
        }
        foreach ($name in @('build', 'discover', 'catalog', 'execute', 'verify')) {
            Require-Framework (@($manifest.phases | Where-Object name -CEQ ($name + '-' + $key)).Count -eq 1) 'framework-ci-required-phase'
        }
        $discovery = @($manifest.phases | Where-Object name -CEQ ('discover-' + $key))[0]
        $dump = Resolve-FrameworkFile $bundle ($discovery.directory + '/discovery.dump') ''
        $verifiedCatalogPath = Join-Path $ResultsDirectory ($key + '-catalog.json')
        Invoke-FrameworkTool @('evidence-catalog', '--discovery', $dump, '--output', $verifiedCatalogPath)
        $verifiedCatalog = Read-FrameworkJson $verifiedCatalogPath
        Assert-FrameworkCaseSet @($verifiedCatalog.cases | ForEach-Object canonicalId) $ids
        foreach ($case in $verifiedCatalog.cases) {
            $match = @($currentCatalog.cases | Where-Object canonicalId -CEQ $case.canonicalId)
            Require-Framework ($match.Count -eq 1 -and $match[0].fullName -ceq $case.fullName -and
                $match[0].className -ceq $case.className -and $match[0].methodName -ceq $case.methodName) 'framework-ci-raw-discovery-mismatch'
        }
        $verifiedPath = Join-Path $ResultsDirectory ($key + '-terminal.json')
        $currentCatalogPath = Join-Path $freshDirectory $current[0].catalog
        Invoke-FrameworkTool @('evidence-verify', '--catalog', $currentCatalogPath, '--selection', $paths.selection,
            '--result', $paths.resultXml, '--trx', $paths.trx, '--output', $verifiedPath)
        $verified = Read-FrameworkJson $verifiedPath
        Require-Framework ($verified.complete -eq $true -and $verified.passedCount -eq $ids.Count) 'framework-ci-unverified-terminal'
        Assert-FrameworkCaseSet @($verified.cases | ForEach-Object canonicalId) $ids
        $markerRequired = $key -ceq 'plugin' -and
            @($currentCatalog.cases | Where-Object className -CEQ 'NUnitCompatibility.CompatibilityCases').Count -gt 0
        Require-Framework ($assembly.markerRequired -eq $markerRequired -and
            (-not $markerRequired -or $assembly.markerObserved -eq $true)) 'framework-ci-execution-marker'
        if ($markerRequired) {
            $markers = @(Get-ChildItem -LiteralPath (Join-Path $bundle 'runtime-evidence') -File -Filter 'fixture-created-*.txt')
            Require-Framework ($markers.Count -eq 1 -and $markers[0].Name -cmatch '^fixture-created-[1-9][0-9]*\.txt$') 'framework-ci-execution-marker'
            $marker = Resolve-FrameworkFile $bundle ('runtime-evidence/' + $markers[0].Name) ''
            Require-Framework ([IO.File]::ReadAllText($marker) -ceq 'constructed') 'framework-ci-execution-marker'
        }
    }
    Write-Output ('framework-ci: verified checkout ' + $commit + ' with API job head ' + $executionHead + '.')
    Write-Output ('framework-ci: verified artifact layout ' + $artifactLayout + '.')
    Write-Output ('framework-ci: verified complete NUnit foundation attempt ' + $latest + ' (' + $globalIds.Count + ' cases).')
} catch {
    [Console]::Error.WriteLine('framework-ci: rejected (' + $failureCode + '; ' + $_.Exception.GetType().Name + ').')
    exit 1
}
