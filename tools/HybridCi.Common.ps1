#requires -Version 7.0
# CI provenance and byte-preserving transport only. Raw test admission remains
# exclusively owned by Assert-HybridSession and its current-source metadata tools.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:HybridCiLocks = [Collections.Generic.List[IO.FileStream]]::new()
$script:HybridCiFailure = 'hybrid-ci-operation-failed'
function Require-HybridCi([bool]$Condition,[string]$Code) {
    if (-not $Condition) { $script:HybridCiFailure = $Code; throw $Code }
}
function Test-HybridCiSamePath([string]$Left,[string]$Right) {
    Require-HybridCi ([IO.Path]::IsPathFullyQualified($Left) -and [IO.Path]::IsPathFullyQualified($Right)) 'hybrid-ci-path-not-absolute'
    [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Left)).Equals(
        [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Right)),[StringComparison]::OrdinalIgnoreCase)
}
function Assert-HybridCiRegular([string]$Path,[switch]$Directory) {
    Require-HybridCi ([IO.Path]::IsPathFullyQualified($Path)) 'hybrid-ci-path-not-absolute'
    $entry = Get-Item -LiteralPath $Path -Force
    Require-HybridCi ($entry.PSIsContainer -eq [bool]$Directory) 'hybrid-ci-entry-kind'
    while ($null -ne $entry) {
        Require-HybridCi (($entry.Attributes -band ([IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Device)) -eq 0) 'hybrid-ci-reparse'
        $entry = $(if ($entry -is [IO.DirectoryInfo]) { $entry.Parent } else { $entry.Directory })
    }
}
function Lock-HybridCiFile([string]$Path,[string]$Digest = '') {
    Assert-HybridCiRegular $Path
    $stream = [IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $script:HybridCiLocks.Add($stream)
    Assert-HybridCiRegular $Path
    $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()
    Require-HybridCi ([string]::IsNullOrEmpty($Digest) -or ($Digest -cmatch '^[a-f0-9]{64}$' -and $Digest -ceq $actual)) 'hybrid-ci-digest'
    [pscustomobject]@{ sha256 = $actual; length = $stream.Length }
}
function Assert-HybridCiJson([Text.Json.JsonElement]$Element) {
    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            Require-HybridCi ($seen.Add($property.Name)) 'hybrid-ci-json-duplicate-property'
            Assert-HybridCiJson $property.Value
        }
    } elseif ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Element.EnumerateArray()) { Assert-HybridCiJson $item }
    }
}
function Read-HybridCiJson([string]$Path) {
    $pin = Lock-HybridCiFile $Path
    Require-HybridCi ($pin.length -gt 0 -and $pin.length -le 67108864) 'hybrid-ci-json-size'
    $stream = [IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $document = $null
    try {
        $document = [Text.Json.JsonDocument]::Parse($stream)
        Assert-HybridCiJson $document.RootElement
        $parameters = @{ InputObject = $document.RootElement.GetRawText(); Depth = 64 }
        if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $parameters.DateKind = 'String' }
        ConvertFrom-Json @parameters
    } finally { if ($null -ne $document) { $document.Dispose() }; $stream.Dispose() }
}
function Write-HybridCiJson([string]$Path,$Value) {
    $stream = [IO.FileStream]::new($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 64))
        $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true)
    } finally { $stream.Dispose() }
}
function Read-HybridCiNumber($Value) {
    Require-HybridCi (($Value -is [int] -or $Value -is [long]) -and $Value -ge 0) 'hybrid-ci-invalid-number'
    [long]$Value
}
function Read-HybridCiAttempt($Value,[int]$Maximum) {
    Require-HybridCi ($Value -is [string] -or $Value -is [int] -or $Value -is [long]) 'hybrid-ci-artifact-attempt'
    $text = [string]$Value; $number = 0
    Require-HybridCi ($text -cmatch '^[1-9][0-9]*\z' -and [int]::TryParse($text,[Globalization.NumberStyles]::None,
        [Globalization.CultureInfo]::InvariantCulture,[ref]$number) -and $number -le $Maximum -and
        $text -ceq $number.ToString([Globalization.CultureInfo]::InvariantCulture)) 'hybrid-ci-artifact-attempt'
    $number
}
function Read-HybridCiUtc($Value) {
    if ($Value -is [DateTime]) {
        Require-HybridCi ($Value.Kind -eq [DateTimeKind]::Utc) 'hybrid-ci-invalid-time'
        return [DateTimeOffset]::new($Value)
    }
    if ($Value -is [DateTimeOffset]) {
        Require-HybridCi ($Value.Offset -eq [TimeSpan]::Zero) 'hybrid-ci-invalid-time'
        return $Value
    }
    $time = [DateTimeOffset]::MinValue
    Require-HybridCi ($Value -is [string] -and
        $Value -cmatch '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|\+00:00)\z' -and
        [DateTimeOffset]::TryParse($Value,[Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind,[ref]$time) -and $time.Offset -eq [TimeSpan]::Zero) 'hybrid-ci-invalid-time'
    $time
}
function Assert-HybridCiPullRequest($Event,[string]$Repository,[string]$Checkout,[string[]]$Parents) {
    Require-HybridCi ($Event.repository.full_name -ceq $Repository -and $Event.pull_request.base.repo.full_name -ceq $Repository -and
        $Event.pull_request.base.sha -is [string] -and $Event.pull_request.base.sha -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$' -and
        $Event.pull_request.head.sha -is [string] -and $Event.pull_request.head.sha -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$') 'hybrid-ci-pull-request-context'
    Require-HybridCi ($Parents.Count -eq 3 -and $Parents[0] -ceq $Checkout -and
        $Parents[1] -ceq $Event.pull_request.base.sha -and $Parents[2] -ceq $Event.pull_request.head.sha) 'hybrid-ci-pull-request-parents'
    $Event.pull_request.head.sha
}
function Assert-HybridCiLegacyCi($Record) {
    # This producer has outer session ownership. The legacy compatibility stamp
    # recognizes only the retired independent 'regression' job, so accepting any
    # nonnull stamp here would silently introduce a second ownership authority.
    $properties = @($Record.PSObject.Properties | Where-Object { $_.Name.Equals('ci',[StringComparison]::OrdinalIgnoreCase) })
    Require-HybridCi ($properties.Count -eq 0 -or ($properties.Count -eq 1 -and
        $properties[0].Name -ceq 'ci' -and $null -eq $properties[0].Value)) 'hybrid-ci-legacy-ci-owner'
}
function Assert-HybridCiLegacyRecords($Session,$Owner,[string]$Restored) {
    foreach ($slot in @($Session.slots | Where-Object owner -CEQ 'legacy')) {
        $prefix = 'session/slots/'+$slot.id+'/'
        foreach ($file in @($Owner.files | Where-Object { $_.path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -and
                $_.path.EndsWith('.json',[StringComparison]::OrdinalIgnoreCase) })) {
            # Windows slot-parent casing and JSON suffix spellings enter the same stamp guard;
            # raw filenames/bytes stay unchanged. This includes canonical and
            # committed parents, children/requests and additional retained JSON.
            $record = Read-HybridCiJson (Resolve-HybridCiRelative $Restored $file.path)
            Assert-HybridCiLegacyCi $record
        }
    }
}
function Get-HybridCiContext([string]$RepositoryRoot) {
    Require-HybridCi ($IsWindows -and [IO.Path]::IsPathFullyQualified($RepositoryRoot) -and
        $env:GITHUB_REPOSITORY -cmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -and
        $env:GITHUB_EVENT_NAME -cin @('pull_request','push') -and
        $env:GITHUB_SHA -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$' -and
        $env:GITHUB_RUN_ID -cmatch '^[1-9][0-9]*$' -and $env:GITHUB_RUN_ATTEMPT -cmatch '^[1-9][0-9]*$' -and
        [string]::IsNullOrEmpty($env:FISHAUDIO_SNAPSHOT_CONTEXT)) 'hybrid-ci-context'
    $runId = 0L; $attempt = 0
    Require-HybridCi ([long]::TryParse($env:GITHUB_RUN_ID,[ref]$runId) -and $runId -gt 0 -and
        [int]::TryParse($env:GITHUB_RUN_ATTEMPT,[ref]$attempt) -and $attempt -gt 0) 'hybrid-ci-context'
    Assert-HybridCiRegular $RepositoryRoot -Directory
    Assert-HybridCiRegular $env:RUNNER_TEMP -Directory
    $root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RepositoryRoot))
    Require-HybridCi (Test-HybridCiSamePath $env:GITHUB_WORKSPACE $root) 'hybrid-ci-checkout-layout'
    $head = (& git -C $root rev-parse --verify HEAD).Trim()
    Require-HybridCi ($LASTEXITCODE -eq 0 -and $head -ceq $env:GITHUB_SHA) 'hybrid-ci-checkout'
    $tree = (& git -C $root rev-parse 'HEAD^{tree}').Trim()
    Require-HybridCi ($LASTEXITCODE -eq 0 -and $tree -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$') 'hybrid-ci-checkout-tree'
    $executionHead = $head
    if ($env:GITHUB_EVENT_NAME -ceq 'pull_request') {
        Require-HybridCi ([IO.Path]::IsPathFullyQualified($env:GITHUB_EVENT_PATH)) 'hybrid-ci-pull-request-context'
        $event = Read-HybridCiJson $env:GITHUB_EVENT_PATH
        $lines = @(& git -C $root rev-list --parents -n 1 HEAD)
        Require-HybridCi ($LASTEXITCODE -eq 0 -and $lines.Count -eq 1) 'hybrid-ci-pull-request-parents'
        $executionHead = Assert-HybridCiPullRequest $event $env:GITHUB_REPOSITORY $head ($lines[0].Split([char[]]" `t",[StringSplitOptions]::RemoveEmptyEntries))
    }
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $shell = (Get-Process -Id $PID).Path
    Assert-HybridCiRegular $dotnet; Assert-HybridCiRegular $shell
    $sdk = (& $dotnet --version).Trim()
    Require-HybridCi ($LASTEXITCODE -eq 0 -and $sdk -ceq '10.0.403') 'hybrid-ci-sdk'
    [pscustomobject]@{ repository = $env:GITHUB_REPOSITORY; commit = $head; tree = $tree; executionHead = $executionHead
        runId = $runId; attempt = $attempt; checkoutRoot = $root; tempRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($env:RUNNER_TEMP))
        dotnet = $dotnet; shell = $shell; sdk = $sdk }
}
function Get-HybridCiBundlePath($Context,[int]$Attempt) {
    Require-HybridCi ($Attempt -gt 0 -and $Attempt -le $Context.attempt -and
        $Context.commit -cmatch '^[a-f0-9]{40}([a-f0-9]{24})?$' -and $Context.runId -gt 0) 'hybrid-ci-bundle-context'
    Join-Path $Context.tempRoot ('fish-audio-hybrid-ci-'+$Context.commit+'-'+$Context.runId+'-'+$Attempt)
}
function New-HybridCiDirectory([string]$Path,$Context) {
    Require-HybridCi ([IO.Path]::IsPathFullyQualified($Path)) 'hybrid-ci-path-not-absolute'
    $full = [IO.Path]::GetFullPath($Path)
    Require-HybridCi ($full.StartsWith($Context.tempRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and
        -not $full.StartsWith($Context.checkoutRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and
        -not (Test-HybridCiSamePath $full $Context.checkoutRoot) -and
        -not [IO.Directory]::Exists($full) -and -not [IO.File]::Exists($full)) 'hybrid-ci-output-not-fresh'
    for ($parent = [IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($full)); $null -ne $parent; $parent = $parent.Parent) {
        if ($parent.Exists) { Assert-HybridCiRegular $parent.FullName -Directory }
    }
    [IO.Directory]::CreateDirectory($full) | Out-Null
    Assert-HybridCiRegular $full -Directory
}
function Read-HybridCiExecution($Job,[string]$ExecutionHead) {
    Require-HybridCi ($Job.head_sha -is [string] -and $Job.head_sha -cmatch '^[A-Fa-f0-9]{40}([A-Fa-f0-9]{24})?$' -and
        $Job.head_sha.ToLowerInvariant() -ceq $ExecutionHead) 'hybrid-ci-execution-sha'
    $started = Read-HybridCiUtc $Job.started_at; $completed = Read-HybridCiUtc $Job.completed_at
    $runner = Read-HybridCiNumber $Job.runner_id
    Require-HybridCi ($completed -ge $started -and $runner -gt 0 -and $Job.runner_name -is [string] -and
        -not [string]::IsNullOrWhiteSpace($Job.runner_name) -and @($Job.steps).Count -gt 0) 'hybrid-ci-execution-identity'
    $steps = [Collections.Generic.List[object]]::new(); $previous = 0L
    foreach ($step in $Job.steps) {
        $number = Read-HybridCiNumber $step.number
        Require-HybridCi ($step.name -is [string] -and -not [string]::IsNullOrWhiteSpace($step.name) -and
            $step.status -is [string] -and ($null -eq $step.conclusion -or $step.conclusion -is [string]) -and $number -gt $previous) 'hybrid-ci-execution-step'
        $start = $null; $end = $null
        if ($null -eq $step.started_at -and $null -eq $step.completed_at) {
            Require-HybridCi ($step.conclusion -ceq 'skipped') 'hybrid-ci-execution-step-time'
        } else {
            Require-HybridCi ($null -ne $step.started_at -and $null -ne $step.completed_at) 'hybrid-ci-execution-step-time'
            $start = Read-HybridCiUtc $step.started_at; $end = Read-HybridCiUtc $step.completed_at
            Require-HybridCi ($start -ge $started -and $end -ge $start -and $end -le $completed) 'hybrid-ci-execution-step-time'
        }
        $steps.Add([ordered]@{ name = $step.name; status = $step.status; conclusion = $step.conclusion; number = $number
            started = $(if ($null -eq $start) { $null } else { $start.UtcTicks }); completed = $(if ($null -eq $end) { $null } else { $end.UtcTicks }) })
        $previous = $number
    }
    $stamp = [ordered]@{ name = $Job.name; headSha = $Job.head_sha.ToLowerInvariant(); status = $Job.status; conclusion = $Job.conclusion
        started = $started.UtcTicks; completed = $completed.UtcTicks; runnerId = $runner; runnerName = $Job.runner_name; steps = $steps.ToArray() } | ConvertTo-Json -Depth 10 -Compress
    [pscustomobject]@{ started = $started; completed = $completed; stamp = $stamp }
}
function Select-HybridCiExecution($Jobs,$Context,[int[]]$ArtifactAttempts) {
    Require-HybridCi ($Jobs.repository -ceq $Context.repository -and (Read-HybridCiNumber $Jobs.runId) -eq $Context.runId -and
        $Jobs.filter -ceq 'all' -and (Read-HybridCiNumber $Jobs.totalCount) -eq @($Jobs.jobs).Count -and
        @($Jobs.attempts).Count -eq $Context.attempt) 'hybrid-ci-jobs-snapshot'
    $fences = @()
    foreach ($attempt in $Jobs.attempts) {
        Require-HybridCi ((Read-HybridCiNumber $attempt.runAttempt) -eq $fences.Count+1) 'hybrid-ci-attempt-fence'
        $time = Read-HybridCiUtc $attempt.runStartedAt
        Require-HybridCi ($fences.Count -eq 0 -or $time -gt $fences[-1]) 'hybrid-ci-attempt-fence'
        $fences += $time
    }
    $ids = [Collections.Generic.HashSet[long]]::new(); $advertised = [Collections.Generic.HashSet[int]]::new()
    $views = [Collections.Generic.List[object]]::new()
    foreach ($job in $Jobs.jobs) {
        $number = Read-HybridCiNumber $job.run_attempt; $id = Read-HybridCiNumber $job.id; $run = Read-HybridCiNumber $job.run_id
        Require-HybridCi ($id -gt 0 -and $ids.Add($id) -and $run -eq $Context.runId -and $number -gt 0 -and
            $number -le $Context.attempt -and $job.name -is [string] -and $job.status -is [string] -and
            ($null -eq $job.conclusion -or $job.conclusion -is [string])) 'hybrid-ci-job-owner'
        $marker = 'Hybrid validation'; $start = $job.name.LastIndexOf($marker,[StringComparison]::Ordinal)
        if ($start -lt 0) { continue }
        Require-HybridCi (($start -eq 0 -or $job.name.Substring(0,$start).EndsWith(' / ',[StringComparison]::Ordinal)) -and
            $job.name.EndsWith($marker,[StringComparison]::Ordinal) -and $job.name.IndexOf($marker,[StringComparison]::Ordinal) -eq $start -and
            $advertised.Add([int]$number)) 'hybrid-ci-ambiguous-job-name'
        $views.Add([pscustomobject]@{ attempt = [int]$number; job = $job })
    }
    $owners = @{}; $executions = @{}
    foreach ($view in $views | Sort-Object attempt) {
        $number = $view.attempt; $job = $view.job
        # Failed, cancelled, skipped, neutral and unfinished actual executions
        # remain owners and block fallback even when no timestamps are supplied.
        if ($job.status -cne 'completed' -or $job.conclusion -cne 'success') { $owners[$number] = $job; continue }
        $execution = Read-HybridCiExecution $job $Context.executionHead
        if ($execution.started -ge $fences[$number-1]) { $owners[$number] = $job; $executions[$number] = $execution; continue }
        Require-HybridCi ($execution.completed -lt $fences[$number-1]) 'hybrid-ci-overlapping-cached-job'
        $prior = @($executions.Keys | Where-Object { $_ -lt $number -and $executions[$_].stamp -ceq $execution.stamp })
        Require-HybridCi ($prior.Count -eq 1) 'hybrid-ci-unmatched-or-ambiguous-cached-job'
    }
    $attempts = @($owners.Keys)+@($ArtifactAttempts)
    Require-HybridCi ($attempts.Count -gt 0) 'hybrid-ci-missing-attempt'
    $latest = [int]($attempts | Measure-Object -Maximum).Maximum
    Require-HybridCi ($owners.ContainsKey($latest) -and $ArtifactAttempts -contains $latest) 'hybrid-ci-missing-latest-evidence'
    $selected = $owners[$latest]
    Require-HybridCi ($selected.status -ceq 'completed' -and $selected.conclusion -ceq 'success') 'hybrid-ci-unsuccessful-latest-job'
    foreach ($name in @('Run complete hybrid validation','Independently admit complete hybrid session','Record this hybrid attempt owner')) {
        $steps = @($selected.steps | Where-Object name -CEQ $name)
        Require-HybridCi ($steps.Count -eq 1 -and $steps[0].status -ceq 'completed' -and $steps[0].conclusion -ceq 'success') 'hybrid-ci-required-step'
    }
    [pscustomobject]@{ attempt = $latest; job = $selected }
}
function Get-HybridCiBundles([string]$ArtifactDirectory,$Context) {
    Assert-HybridCiRegular $ArtifactDirectory -Directory
    $entries = @(Get-ChildItem -LiteralPath $ArtifactDirectory -Force); $bundles = @{}
    $prefix = 'hybrid-report-'+$Context.commit+'-'+$Context.runId+'-'
    if (@($entries | Where-Object Name -CEQ 'ci-owner.json').Count -gt 0) {
        foreach ($entry in $entries) {
            Require-HybridCi ($(if ($entry.PSIsContainer) { $entry.Name -cin @('session','producer-admission') } else { $entry.Name -ceq 'ci-owner.json' })) 'hybrid-ci-foreign-artifact'
            Assert-HybridCiRegular $entry.FullName -Directory:$entry.PSIsContainer
        }
        $owner = Read-HybridCiJson (Join-Path $ArtifactDirectory 'ci-owner.json')
        $number = Read-HybridCiAttempt $owner.attempt $Context.attempt
        $bundles[$number] = $ArtifactDirectory
    } else {
        foreach ($entry in $entries) {
            Require-HybridCi ($entry.PSIsContainer -and $entry.Name.StartsWith($prefix,[StringComparison]::Ordinal)) 'hybrid-ci-foreign-artifact'
            Assert-HybridCiRegular $entry.FullName -Directory
            $number = Read-HybridCiAttempt $entry.Name.Substring($prefix.Length) $Context.attempt
            Require-HybridCi (-not $bundles.ContainsKey($number)) 'hybrid-ci-artifact-attempt'
            $bundles[$number] = $entry.FullName
        }
    }
    $bundles
}
function Resolve-HybridCiRelative([string]$Parent,[string]$Relative) {
    Require-HybridCi (-not [string]::IsNullOrWhiteSpace($Relative) -and -not [IO.Path]::IsPathRooted($Relative) -and
        -not $Relative.Contains('\') -and @($Relative.Split('/') | Where-Object { $_ -cin @('','.','..') }).Count -eq 0) 'hybrid-ci-inventory-path'
    $path = [IO.Path]::GetFullPath((Join-Path $Parent $Relative))
    Require-HybridCi ($path.StartsWith([IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Parent))+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'hybrid-ci-inventory-escape'
    $path
}
function Get-HybridCiInventory([string]$Bundle) {
    Assert-HybridCiRegular $Bundle -Directory
    $paths = [Collections.Generic.List[string]]::new()
    foreach ($entry in Get-ChildItem -LiteralPath $Bundle -Recurse -Force) {
        Assert-HybridCiRegular $entry.FullName -Directory:$entry.PSIsContainer
        $relative = [IO.Path]::GetRelativePath($Bundle,$entry.FullName).Replace('\','/')
        if ($relative -ceq 'ci-owner.json' -and -not $entry.PSIsContainer) { continue }
        Require-HybridCi ($relative -cin @('session','producer-admission') -or $relative.StartsWith('session/',[StringComparison]::Ordinal) -or
            $relative.StartsWith('producer-admission/',[StringComparison]::Ordinal)) 'hybrid-ci-foreign-artifact'
        if (-not $entry.PSIsContainer) { $paths.Add($relative) }
    }
    $paths.Sort([StringComparer]::Ordinal)
    @(foreach ($relative in $paths) {
        $pin = Lock-HybridCiFile (Resolve-HybridCiRelative $Bundle $relative)
        [pscustomobject]@{ path = $relative; sha256 = $pin.sha256; length = $pin.length }
    })
}
function Assert-HybridCiOwner($Owner,$Context,[int]$Attempt,[string]$Bundle,$Job) {
    $expected = Get-HybridCiBundlePath $Context $Attempt
    Require-HybridCi ($Owner.schemaVersion -eq 1 -and $Owner.kind -ceq 'hybrid-ci-owner' -and
        $Owner.repository -ceq $Context.repository -and $Owner.commit -ceq $Context.commit -and $Owner.head -ceq $Context.commit -and
        $Owner.tree -ceq $Context.tree -and $Owner.workflowRunId -ceq [string]$Context.runId -and
        (Read-HybridCiAttempt $Owner.attempt $Context.attempt) -eq $Attempt -and $Owner.job -ceq 'hybrid-validation' -and
        $Owner.sdk -ceq $Context.sdk) 'hybrid-ci-artifact-owner'
    # No path inside downloaded JSON chooses a restoration destination. The
    # exact expected path is computed from this runner's trusted event context.
    Require-HybridCi ((Test-HybridCiSamePath $Owner.checkoutRoot $Context.checkoutRoot) -and
        (Test-HybridCiSamePath $Owner.tempRoot $Context.tempRoot) -and (Test-HybridCiSamePath $Owner.bundleRoot $expected) -and
        (Test-HybridCiSamePath $Owner.dotnet $Context.dotnet) -and (Test-HybridCiSamePath $Owner.shell $Context.shell)) 'hybrid-ci-unsupported-runner-layout'
    $finished = Read-HybridCiUtc $Owner.finishedUtc
    Require-HybridCi ($finished -ge (Read-HybridCiUtc $Job.started_at) -and $finished -le (Read-HybridCiUtc $Job.completed_at)) 'hybrid-ci-owner-time'
    Require-HybridCi ($Owner.files -is [Array] -and $Owner.files.Count -gt 0) 'hybrid-ci-inventory-shape'
    $actual = @(Get-HybridCiInventory $Bundle)
    Require-HybridCi ($actual.Count -eq $Owner.files.Count) 'hybrid-ci-inventory-set'
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    for ($index=0;$index -lt $actual.Count;$index++) {
        $file = $Owner.files[$index]
        Require-HybridCi ($file.path -is [string] -and $seen.Add($file.path) -and $file.sha256 -cmatch '^[a-f0-9]{64}$' -and
            (Read-HybridCiNumber $file.length) -eq $actual[$index].length -and $file.path -ceq $actual[$index].path -and
            $file.sha256 -ceq $actual[$index].sha256) 'hybrid-ci-inventory-digest'
        [void](Resolve-HybridCiRelative $Bundle $file.path)
    }
    Require-HybridCi ($seen.Contains('session/manifest.json') -and $seen.Contains('producer-admission/admission.json')) 'hybrid-ci-missing-admission'
}
function Restore-HybridCiBundle([string]$Bundle,$Owner,$Context,[int]$Attempt) {
    $destination = Get-HybridCiBundlePath $Context $Attempt
    Require-HybridCi (-not [IO.File]::Exists($destination) -and -not [IO.Directory]::Exists($destination)) 'hybrid-ci-restore-not-fresh'
    Assert-HybridCiRegular $Context.tempRoot -Directory
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    Assert-HybridCiRegular $destination -Directory
    foreach ($file in @($Owner.files)+@([pscustomobject]@{ path = 'ci-owner.json'; sha256 = (Lock-HybridCiFile (Join-Path $Bundle 'ci-owner.json')).sha256 })) {
        $source = Resolve-HybridCiRelative $Bundle $file.path
        [void](Lock-HybridCiFile $source $file.sha256)
        $target = Resolve-HybridCiRelative $destination $file.path
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        Assert-HybridCiRegular ([IO.Path]::GetDirectoryName($target)) -Directory
        [IO.File]::Copy($source,$target,$false)
        [void](Lock-HybridCiFile $target $file.sha256)
    }
    $destination
}
function Close-HybridCiLocks {
    $primary = $null
    for ($index=$script:HybridCiLocks.Count-1;$index -ge 0;$index--) {
        try { $script:HybridCiLocks[$index].Dispose() } catch { if ($null -eq $primary) { $primary = $_ } }
    }
    $script:HybridCiLocks.Clear()
    if ($null -ne $primary) { throw $primary }
}
