#requires -Version 7.0
# This boundary binds caller-acquired GitHub observations. It does not acquire
# or authenticate an event, API response, artifact origin or publication right.
# GetRegressionJobAttempts.ps1 owns authenticated jobs/fence acquisition.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../HybridSession.Common.ps1')

function Require-CoverageCi([bool]$Condition, [string]$Code) {
    if (-not $Condition) { throw ('full-coverage-ci-orchestration-' + $Code) }
}
function CoverageCi-FailureCode($Failure) {
    # Only these fixed repository tokens may cross the public stderr/diagnostic
    # boundary. Never print raw exception text, paths, argv or input fragments.
    # The strict launcher preserves a nonzero target as a failed raw process.
    # Classify only its exact fixed token; never suppress or rewrite that failure.
    if ($Failure.Exception.Message -ceq 'hybrid-process-target-failed') { return 'tool-rejected' }
    $codes = @('request-shape','request-role','request-path','driver-shape','artifact-request-shape','action-role',
        'action','fresh-output-required','output-path-alias','output-parent-missing','output-parent-reparse',
        'fresh-operation-root-required','external-driver-required','current-role','tool-rejected','operation-evidence',
        'prepare-root','failed-preparation-outcome','original-operation-failed','context','context-shape',
        'runner-context','api-run-context','source-context','numeric-current-job','current-job-observation',
        'jobs-snapshot','attempt-fences','job-owner','role-plan','unsuccessful-latest-job','missing-latest-job',
        'duplicate-role-attempt-job','unmatched-or-ambiguous-cached-job','overlapping-cached-job','required-step',
        'artifact-snapshot','download-set','artifact-api-owner','artifact-owner','artifact-byte-binding',
        'owner-shape','owner-time','prepared-owner','mixed-preparation','shard-before-preparation','shard-before-smoke',
        'smoke-selection','smoke-manifest','smoke-file-pin','smoke-raw-input','preparation-binding',
        'nonempty-smoke-required','shard-operation-failed','compose-operation-failed','selection-binding',
        'utc-time','json-case-collision','json-size','json-number')
    foreach ($code in $codes) {
        if ($Failure.Exception.Message -ceq ('full-coverage-ci-orchestration-' + $code)) { return $code }
    }
    return 'operation-rejected'
}
function CoverageCi-Utc($Value) {
    if ($Value -is [DateTime]) {
        Require-CoverageCi ($Value.Kind -eq [DateTimeKind]::Utc) 'utc-time'
        $Value = $Value.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    } elseif ($Value -is [DateTimeOffset]) {
        Require-CoverageCi ($Value.Offset -eq [TimeSpan]::Zero) 'utc-time'
        $Value = $Value.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    }
    $time = [DateTimeOffset]::MinValue
    Require-CoverageCi ($Value -is [string] -and $Value -cmatch
        '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|\+00:00)\z' -and
        [DateTimeOffset]::TryParse($Value, [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind, [ref]$time) -and $time.Offset -eq [TimeSpan]::Zero) 'utc-time'
    return $time.UtcTicks
}
function CoverageCi-PositiveInteger($Value) {
    return (($Value -is [int] -or $Value -is [long]) -and $Value -gt 0)
}
function CoverageCi-Hash($Value) { return ($Value -is [string] -and $Value -cmatch '^[a-f0-9]{64}$') }
function CoverageCi-Json($Value) { return ($Value | ConvertTo-Json -Depth 64 -Compress) }
function CoverageCi-Equal($Left, $Right) { return ((CoverageCi-Json $Left) -ceq (CoverageCi-Json $Right)) }
function CoverageCi-AssertKeys($Value, [string[]]$Keys, [string]$Code) {
    Require-CoverageCi ($null -ne $Value) $Code
    $actual = @($(if ($Value -is [Collections.IDictionary]) { $Value.Keys } else { $Value.PSObject.Properties.Name }))
    Require-CoverageCi ($actual.Count -eq $Keys.Count -and
        @($actual | Where-Object { $Keys -cnotcontains $_ }).Count -eq 0) $Code
}
function CoverageCi-Integer($Value, [long]$Minimum, [long]$Maximum) {
    return (($Value -is [int] -or $Value -is [long]) -and $Value -ge $Minimum -and $Value -le $Maximum)
}
function CoverageCi-AssertPath($Value) {
    Require-CoverageCi ($Value -is [string] -and $Value -cmatch '^[A-Za-z]:\\' -and
        $Value.Length -le 32767 -and -not @($Value.ToCharArray() | Where-Object { [char]::IsControl($_) }).Count -and
        [IO.Path]::GetFullPath($Value) -ceq $Value -and -not $Value.EndsWith('\')) 'request-path'
    foreach ($part in $Value.Substring(3).Split('\')) {
        $stem = $part.Split('.')[0]
        Require-CoverageCi ($part.Length -gt 0 -and $part -cnotin @('.', '..') -and
            -not $part.EndsWith('.') -and -not $part.EndsWith(' ') -and $part -cnotmatch '[<>:"/|?*]' -and
            $part -cnotmatch '~[0-9]' -and $stem -inotmatch '^(?:CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[0-9¹²³]|LPT[0-9¹²³])$') 'request-path'
    }
}
function CoverageCi-AssertDriver($Driver, [switch]$Pin) {
    CoverageCi-AssertKeys $Driver @('toolFile','toolSha256','dotnetFile') 'driver-shape'
    CoverageCi-AssertPath $Driver.toolFile; CoverageCi-AssertPath $Driver.dotnetFile
    Require-CoverageCi (CoverageCi-Hash $Driver.toolSha256) 'driver-shape'
    if ($Pin) { [void](Lock-HybridFile $Driver.toolFile $Driver.toolSha256); Assert-HybridRegular $Driver.dotnetFile }
}
function CoverageCi-AssertArtifact($Context, $Artifact, [switch]$Pin) {
    CoverageCi-AssertKeys $Artifact @('ownerRecordPath','ownerRecordSha256','archivePath','archiveSha256',
        'artifactId','artifactName','artifactRunId','artifactCommit') 'artifact-request-shape'
    CoverageCi-AssertPath $Artifact.ownerRecordPath; CoverageCi-AssertPath $Artifact.archivePath
    Require-CoverageCi ((CoverageCi-PositiveInteger $Artifact.artifactId) -and
        (CoverageCi-PositiveInteger $Artifact.artifactRunId) -and $Artifact.artifactRunId -eq $Context.runId -and
        $Artifact.artifactCommit -ceq $Context.commit -and $Artifact.artifactName -is [string] -and
        $Artifact.artifactName -cmatch '^coverage-(?:prepare-|shard-)' -and
        (CoverageCi-Hash $Artifact.ownerRecordSha256) -and (CoverageCi-Hash $Artifact.archiveSha256)) 'artifact-request-shape'
    if ($Pin) {
        [void](Lock-HybridFile $Artifact.ownerRecordPath $Artifact.ownerRecordSha256)
        [void](Lock-HybridFile $Artifact.archivePath $Artifact.archiveSha256)
    }
}
function CoverageCi-AssertProducerRequest($Context, $Request, [string]$Action) {
    Require-CoverageCi ($Request -is [pscustomobject]) 'request-shape'
    $names = @($Request.PSObject.Properties.Name)
    $required = @('schemaVersion','role','shardIndex','diagnosticPath')
    Require-CoverageCi (@($required | Where-Object { $names -cnotcontains $_ }).Count -eq 0) 'request-shape'
    Require-CoverageCi ((CoverageCi-Integer $Request.schemaVersion 1 1) -and
        (CoverageCi-Integer $Request.role 0 1) -and (($Request.role -eq 0 -and $null -eq $Request.shardIndex) -or
        ($Request.role -eq 1 -and (CoverageCi-Integer $Request.shardIndex 0 ($Context.shardCount - 1))))) 'request-role'
    CoverageCi-AssertPath $Request.diagnosticPath
    if ($Action -ceq 'Diagnostic') {
        CoverageCi-AssertKeys $Request @('schemaVersion','role','shardIndex','diagnosticPath','category','stage') 'request-shape'
        [void](CoverageCi-Diagnostic $Context $Request.role $Request.shardIndex $Request.category $Request.stage)
        return
    }
    Require-CoverageCi (($Action -ceq 'SealPrepare' -and $Request.role -eq 0) -or
        ($Action -ceq 'RunShard' -and $Request.role -eq 1)) 'action-role'
    $keys = @('schemaVersion','role','shardIndex','workDirectory','archivePath','ownerRecordPath','diagnosticPath','driver')
    if ($Action -ceq 'RunShard') {
        $keys += 'preparationArtifact'
        if ($Request.shardIndex -gt 0) { $keys += 'smokeArtifact' }
    }
    CoverageCi-AssertKeys $Request $keys 'request-shape'
    foreach ($path in @($Request.workDirectory, $Request.archivePath, $Request.ownerRecordPath)) { CoverageCi-AssertPath $path }
    CoverageCi-AssertDriver $Request.driver
    if ($Action -ceq 'RunShard') {
        CoverageCi-AssertArtifact $Context $Request.preparationArtifact
        if ($Request.shardIndex -gt 0) { CoverageCi-AssertArtifact $Context $Request.smokeArtifact }
    }
}
function CoverageCi-AssertConsumerRequest($Context, $Request, [string]$Action) {
    Require-CoverageCi ($Request -is [pscustomobject]) 'request-shape'
    $keys = @('schemaVersion','workDirectory','diagnosticPath','resultPath','driver')
    $names = @($Request.PSObject.Properties.Name)
    Require-CoverageCi (@($keys | Where-Object { $names -cnotcontains $_ }).Count -eq 0) 'request-shape'
    Require-CoverageCi (CoverageCi-Integer $Request.schemaVersion 1 1) 'request-shape'
    if ($Action -ceq 'RestorePrerequisites') {
        Require-CoverageCi ($names -ccontains 'shardIndex' -and
            (CoverageCi-Integer $Request.shardIndex 0 ($Context.shardCount - 1))) 'request-role'
        $keys += @('shardIndex','preparationArtifact')
        if ($Request.shardIndex -gt 0) { $keys += 'smokeArtifact' }
    } else {
        Require-CoverageCi ($Action -ceq 'SelectAndCompose') 'action'
        $keys += @('composeDirectory','artifactSnapshot','artifacts')
    }
    CoverageCi-AssertKeys $Request $keys 'request-shape'
    foreach ($path in @($Request.workDirectory, $Request.diagnosticPath, $Request.resultPath)) { CoverageCi-AssertPath $path }
    CoverageCi-AssertDriver $Request.driver
    if ($Action -ceq 'RestorePrerequisites') {
        CoverageCi-AssertArtifact $Context $Request.preparationArtifact
        if ($Request.shardIndex -gt 0) { CoverageCi-AssertArtifact $Context $Request.smokeArtifact }
    } else {
        CoverageCi-AssertPath $Request.composeDirectory
        CoverageCi-AssertKeys $Request.artifactSnapshot @('repository','runId','totalCount','artifacts') 'artifact-snapshot'
        Require-CoverageCi ($Request.artifacts -is [Array] -and $Request.artifactSnapshot.artifacts -is [Array] -and
            (CoverageCi-Integer $Request.artifactSnapshot.totalCount 0 200000)) 'artifact-snapshot'
        foreach ($artifact in $Request.artifacts) { CoverageCi-AssertArtifact $Context $artifact }
        CoverageCi-AssertDownloads $Context $Request.artifactSnapshot $Request.artifacts
    }
}
function CoverageCi-AssertFresh($Context, $Paths) {
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $Paths) {
        CoverageCi-AssertPath $path
        Require-CoverageCi ($seen.Add($path) -and -not (Test-HybridContained $path $Context.operationRoot) -and
            -not (Test-HybridSamePath $path $Context.operationRoot) -and -not [IO.File]::Exists($path) -and
            -not [IO.Directory]::Exists($path)) 'fresh-output-required'
        for ($parent = [IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($path)); $null -ne $parent; $parent = $parent.Parent) {
            if ($parent.Exists) { Require-CoverageCi (($parent.Attributes -band
                ([IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Device)) -eq 0) 'output-parent-reparse' }
        }
    }
}
function CoverageCi-AssertOutputLayout([string]$Work, $Files) {
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $Files) {
        Require-CoverageCi ($seen.Add($file) -and -not (Test-HybridSamePath $file $Work) -and
            -not (Test-HybridContained $Work $file)) 'output-path-alias'
        $parent = [IO.Path]::GetDirectoryName($file)
        Require-CoverageCi ([IO.Directory]::Exists($parent) -or (Test-HybridSamePath $parent $Work)) 'output-parent-missing'
        foreach ($other in $Files) {
            if ($file -cne $other) { Require-CoverageCi (-not (Test-HybridContained $file $other)) 'output-path-alias' }
        }
    }
}
function CoverageCi-Role($Context, [int]$Role, $ShardIndex) {
    $matches = @($Context.roles | Where-Object { $_.role -eq $Role -and $_.shardIndex -eq $ShardIndex })
    Require-CoverageCi ($matches.Count -eq 1) 'role-plan'
    return $matches[0]
}
function CoverageCi-RoleKey([int]$Role, $ShardIndex) {
    Require-CoverageCi (($Role -eq 0 -and $null -eq $ShardIndex) -or
        ($Role -eq 1 -and ($ShardIndex -is [int] -or $ShardIndex -is [long]) -and $ShardIndex -ge 0)) 'role'
    if ($Role -eq 0) { return 'prepare' }
    return ('shard-{0:D6}' -f [int]$ShardIndex)
}
function CoverageCi-Root($Context, [int]$Role, $ShardIndex, [int]$Attempt) {
    Require-CoverageCi ($Attempt -ge 1 -and $Attempt -le $Context.currentAttempt) 'attempt'
    return Join-Path (Join-Path $Context.operationRoot (CoverageCi-RoleKey $Role $ShardIndex)) ('attempt-' + $Attempt)
}
function CoverageCi-ArtifactName($Context, [int]$Role, $ShardIndex, [int]$Attempt, [switch]$Diagnostic) {
    $prefix = $(if ($Diagnostic) { 'coverage-diagnostics-' } else { 'coverage-' })
    return $prefix + (CoverageCi-RoleKey $Role $ShardIndex) + '-' + $Context.commit + '-' + $Context.runId + '-' + $Attempt
}
function CoverageCi-AssertJsonMemberNames([Text.Json.JsonElement]$Element) {
    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        # PowerShell member lookup is case insensitive. Validate the complete
        # JSON tree before projection so Foo/foo can never overwrite each other.
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($property in $Element.EnumerateObject()) {
            Require-CoverageCi ($names.Add($property.Name)) 'json-case-collision'
            CoverageCi-AssertJsonMemberNames $property.Value
        }
    } elseif ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Element.EnumerateArray()) { CoverageCi-AssertJsonMemberNames $item }
    }
}
function CoverageCi-ProjectJsonValue([Text.Json.JsonElement]$Element) {
    switch ($Element.ValueKind.ToString()) {
        'Object' {
            $properties = [Collections.Specialized.OrderedDictionary]::new([StringComparer]::Ordinal)
            foreach ($property in $Element.EnumerateObject()) {
                $value = CoverageCi-ProjectJsonValue $property.Value
                $properties.Add($property.Name, $value)
            }
            return [pscustomobject]$properties
        }
        'Array' {
            $items = [Collections.Generic.List[object]]::new()
            foreach ($item in $Element.EnumerateArray()) {
                $value = CoverageCi-ProjectJsonValue $item
                $items.Add($value)
            }
            # The outer singleton is consumed by PowerShell's return pipeline;
            # the actual empty/singleton/many-item JSON array remains an array.
            return ,$items.ToArray()
        }
        'String' { return $Element.GetString() }
        'Number' {
            $integer = 0L
            if ($Element.TryGetInt64([ref]$integer)) { return $integer }
            $number = $Element.GetDouble()
            Require-CoverageCi (-not [double]::IsNaN($number) -and -not [double]::IsInfinity($number)) 'json-number'
            return $number
        }
        'True' { return $true }
        'False' { return $false }
        'Null' { return $null }
        default { throw 'full-coverage-ci-orchestration-json-number' }
    }
}
function CoverageCi-ReadJson([string]$Path) {
    # ConvertFrom-Json materializes offset strings as local DateTime values on
    # supported PowerShell versions. Preserve JSON string values so UTC schema
    # checks see the original Z/+00:00/+08:00 offset, without a PS version bump.
    Assert-HybridRegular $Path
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $script:HybridLocks.Add($stream)
    Require-CoverageCi ($stream.Length -gt 0 -and $stream.Length -le 67108864) 'json-size'
    $options = [Text.Json.JsonDocumentOptions]::new()
    $options.MaxDepth = 64
    $document = $null; $primary = $null; $value = $null
    try {
        $document = [Text.Json.JsonDocument]::Parse($stream, $options)
        Assert-HybridJsonElement $document.RootElement
        CoverageCi-AssertJsonMemberNames $document.RootElement
        $value = CoverageCi-ProjectJsonValue $document.RootElement
    } catch { $primary = $_ }
    finally {
        if ($null -ne $document) { try { $document.Dispose() } catch { if ($null -eq $primary) { $primary = $_ } } }
    }
    if ($null -ne $primary) { throw $primary }
    return ,$value
}
function CoverageCi-Read([string]$Path, [string]$Sha256) {
    Require-CoverageCi (CoverageCi-Hash $Sha256) 'independent-digest-required'
    [void](Lock-HybridFile $Path $Sha256)
    $value = CoverageCi-ReadJson $Path
    return ,$value
}
function CoverageCi-Publish([string]$Path, $Value) {
    Require-CoverageCi ([IO.Path]::IsPathFullyQualified($Path) -and -not [IO.File]::Exists($Path) -and
        -not [IO.Directory]::Exists($Path)) 'fresh-output-required'
    $pending = $Path + '.pending-' + [Guid]::NewGuid().ToString('N')
    $stream = $null; $primary = $null
    try {
        $stream = [IO.FileStream]::new($pending, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 64))
        $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true)
    } catch { $primary = $_ }
    finally { if ($null -ne $stream) { try { $stream.Dispose() } catch { if ($null -eq $primary) { $primary = $_ } } } }
    if ($null -ne $primary) { throw $primary }
    [IO.File]::Move($pending, $Path, $false)
}
function CoverageCi-Close($Primary) {
    # Attempt every release. An already latched operation/test/collection failure
    # remains primary, and a release failure blocks an otherwise successful call.
    foreach ($guard in $script:HybridLocks) {
        try { $guard.Dispose() } catch { if ($null -eq $Primary) { $Primary = $_ } }
    }
    $script:HybridLocks.Clear()
    return $Primary
}
function CoverageCi-AssertJobs($Context, $Jobs) {
    Require-CoverageCi ($Jobs.repository -ceq $Context.repository -and $Jobs.runId -eq $Context.runId -and
        $Jobs.filter -ceq 'all' -and @($Jobs.jobs).Count -le 200000 -and $Jobs.totalCount -eq @($Jobs.jobs).Count -and
        @($Jobs.attempts).Count -eq $Context.currentAttempt) 'jobs-snapshot'
    $previous = 0L
    $number = 0
    foreach ($fence in $Jobs.attempts) {
        $number++
        $start = CoverageCi-Utc $fence.runStartedAt
        Require-CoverageCi ($fence.runAttempt -eq $number -and $start -gt $previous) 'attempt-fences'
        $previous = $start
    }
    $ids = [Collections.Generic.HashSet[long]]::new()
    foreach ($job in $Jobs.jobs) {
        Require-CoverageCi ((CoverageCi-PositiveInteger $job.id) -and $ids.Add($job.id) -and
            $job.run_id -eq $Context.runId -and (CoverageCi-PositiveInteger $job.run_attempt) -and
            $job.run_attempt -le $Context.currentAttempt -and -not [string]::IsNullOrWhiteSpace($job.name)) 'job-owner'
    }
}
function CoverageCi-AssertCaller($Context, $Observations, $Jobs, [string]$JobsSha256) {
    CoverageCi-AssertKeys $Context @('schemaVersion','repository','eventName','ref','commit','tree','workflowName','runId',
        'currentAttempt','operationRoot','jobsSha256','preparation','shardCount','maximumConcurrentMachines','roles') 'context-shape'
    CoverageCi-AssertKeys $Context.preparation @('source','prepared','runId','snapshotContextSha256','metadataToolSha256',
        'buildAuthoritySha256','dotCoverToolSha256','vstestConsoleSha256','sdkVersion','configuration','targetFramework',
        'instrumentationPolicyVersion') 'preparation-context-shape'
    CoverageCi-AssertKeys $Context.preparation.source @('commit','tree','fingerprint') 'preparation-context-shape'
    CoverageCi-AssertKeys $Context.preparation.prepared @('schemaVersion','preparedPath','preparedSha256','sourceDirectory',
        'snapshotContextPath','metadataToolFile','dotnetFile','shellFile','dotCoverToolFile') 'preparation-context-shape'
    Require-CoverageCi ($Context.schemaVersion -eq 1 -and $Observations.schemaVersion -eq 1 -and
        $Context.repository -cmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -and
        $Context.eventName -ceq 'push' -and $Context.ref -ceq 'refs/heads/main' -and
        $Context.commit -cmatch '^(?:[a-f0-9]{40}|[a-f0-9]{64})$' -and
        $Context.tree -cmatch '^(?:[a-f0-9]{40}|[a-f0-9]{64})$' -and
        (CoverageCi-PositiveInteger $Context.runId) -and (CoverageCi-PositiveInteger $Context.currentAttempt) -and
        $Context.currentAttempt -le 200000 -and $Context.shardCount -ge 2 -and $Context.shardCount -le 100000 -and
        $Context.maximumConcurrentMachines -ge 1 -and $Context.maximumConcurrentMachines -le 4 -and
        [IO.Path]::IsPathFullyQualified($Context.operationRoot) -and
        (CoverageCi-Hash $JobsSha256) -and $Context.jobsSha256 -ceq $JobsSha256) 'context'
    $environment = $Observations.environment
    Require-CoverageCi ($environment.GITHUB_REPOSITORY -ceq $Context.repository -and
        $environment.GITHUB_EVENT_NAME -ceq $Context.eventName -and $environment.GITHUB_REF -ceq $Context.ref -and
        $environment.GITHUB_SHA -ceq $Context.commit -and $environment.GITHUB_WORKFLOW -ceq $Context.workflowName -and
        [string]$environment.GITHUB_RUN_ID -ceq [string]$Context.runId -and
        [string]$environment.GITHUB_RUN_ATTEMPT -ceq [string]$Context.currentAttempt -and
        $environment.GITHUB_JOB -is [string] -and -not [string]::IsNullOrWhiteSpace($environment.GITHUB_JOB)) 'runner-context'
    $api = $Observations.apiRun
    Require-CoverageCi ($api.id -eq $Context.runId -and $api.run_attempt -eq $Context.currentAttempt -and
        $api.event -ceq 'push' -and $api.head_branch -ceq 'main' -and $api.head_sha -ceq $Context.commit -and
        $api.name -ceq $Context.workflowName -and $api.repository.full_name -ceq $Context.repository -and
        $api.head_repository.full_name -ceq $Context.repository) 'api-run-context'
    Require-CoverageCi ($Observations.git.commit -ceq $Context.commit -and $Observations.git.tree -ceq $Context.tree -and
        $Context.preparation.source.commit -ceq $Context.commit -and $Context.preparation.source.tree -ceq $Context.tree) 'source-context'
    CoverageCi-AssertJobs $Context $Jobs
    $actual = $Observations.apiJob
    # GITHUB_JOB is a workflow logical string. Only the caller's actual numeric
    # REST job.id identifies the current machine execution.
    Require-CoverageCi ((CoverageCi-PositiveInteger $actual.id) -and $actual.run_id -eq $Context.runId -and
        $actual.run_attempt -eq $Context.currentAttempt -and $actual.head_sha -ceq $Context.commit -and
        $actual.status -ceq 'in_progress' -and (CoverageCi-PositiveInteger $actual.runner_id) -and
        -not [string]::IsNullOrWhiteSpace($actual.runner_name)) 'numeric-current-job'
    $matches = @($Jobs.jobs | Where-Object id -EQ $actual.id)
    Require-CoverageCi ($matches.Count -eq 1 -and $matches[0].name -ceq $actual.name -and
        $matches[0].run_attempt -eq $actual.run_attempt -and $matches[0].head_sha -ceq $actual.head_sha -and
        $matches[0].runner_id -eq $actual.runner_id -and $matches[0].runner_name -ceq $actual.runner_name -and
        (CoverageCi-Utc $matches[0].started_at) -eq (CoverageCi-Utc $actual.started_at) -and
        (CoverageCi-Utc $actual.started_at) -ge (CoverageCi-Utc $Jobs.attempts[$Context.currentAttempt - 1].runStartedAt)) 'current-job-observation'
    Require-CoverageCi (@($Context.roles).Count -eq $Context.shardCount + 1) 'role-plan'
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($role in $Context.roles) {
        CoverageCi-AssertKeys $role @('role','shardIndex','jobName','requiredSteps','evidenceRelativePath') 'role-plan'
        [void](CoverageCi-RoleKey $role.role $role.shardIndex)
        Require-CoverageCi ($names.Add($role.jobName) -and @($role.requiredSteps).Count -gt 0 -and
            @($role.requiredSteps).Count -eq @($role.requiredSteps | Select-Object -Unique).Count -and
            $role.evidenceRelativePath -is [string] -and $role.evidenceRelativePath -cnotmatch '[\\:]' -and
            -not [IO.Path]::IsPathRooted($role.evidenceRelativePath) -and
            @($role.evidenceRelativePath.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -eq 0) 'role-plan'
    }
    [void](CoverageCi-Role $Context 0 $null)
    for ($index = 0; $index -lt $Context.shardCount; $index++) { [void](CoverageCi-Role $Context 1 $index) }
    $prepareRole = CoverageCi-Role $Context 0 $null
    $suffix = $prepareRole.evidenceRelativePath.Replace('/', '\')
    Require-CoverageCi ($Context.preparation.prepared.preparedPath.EndsWith('\' + $suffix,
        [StringComparison]::Ordinal)) 'preparation-layout'
    $prepareRoot = $Context.preparation.prepared.preparedPath.Substring(0,
        $Context.preparation.prepared.preparedPath.Length - $suffix.Length - 1)
    # Every preparation archive must contain the original snapshot as well as
    # its context/build evidence; a later restore cannot borrow an external S.
    Require-CoverageCi ((Test-HybridContained $Context.preparation.prepared.sourceDirectory $prepareRoot) -and
        (Test-HybridContained $Context.preparation.prepared.snapshotContextPath $prepareRoot)) 'preparation-layout'
    return [pscustomobject]@{ numericJobId = $actual.id; logicalJob = $environment.GITHUB_JOB; apiEventAuthenticated = $false }
}
function CoverageCi-Execution($Job, [string]$Commit) {
    Require-CoverageCi ($Job.head_sha -ceq $Commit -and (CoverageCi-PositiveInteger $Job.runner_id) -and
        -not [string]::IsNullOrWhiteSpace($Job.runner_name) -and @($Job.steps).Count -gt 0) 'execution-identity'
    $start = CoverageCi-Utc $Job.started_at
    $end = CoverageCi-Utc $Job.completed_at
    Require-CoverageCi ($end -ge $start) 'execution-time'
    $steps = [Collections.Generic.List[object]]::new()
    $previous = 0L
    foreach ($step in $Job.steps) {
        Require-CoverageCi ((CoverageCi-PositiveInteger $step.number) -and $step.number -gt $previous -and
            -not [string]::IsNullOrWhiteSpace($step.name)) 'execution-step'
        $a = $null; $b = $null
        if ($null -eq $step.started_at -and $null -eq $step.completed_at) {
            Require-CoverageCi ($step.conclusion -ceq 'skipped') 'execution-step-time'
        } else {
            $a = CoverageCi-Utc $step.started_at; $b = CoverageCi-Utc $step.completed_at
            Require-CoverageCi ($a -ge $start -and $b -ge $a -and $b -le $end) 'execution-step-time'
        }
        $steps.Add([ordered]@{ number = $step.number; name = $step.name; status = $step.status
            conclusion = $step.conclusion; started = $a; completed = $b })
        $previous = $step.number
    }
    return [pscustomobject]@{ started = $start; completed = $end; stamp = CoverageCi-Json ([ordered]@{
        name = $Job.name; headSha = $Commit; status = $Job.status; conclusion = $Job.conclusion; started = $start
        completed = $end; runnerId = $Job.runner_id; runnerName = $Job.runner_name; steps = $steps.ToArray() }) }
}
function CoverageCi-Latest($Context, $Jobs, $Role, [switch]$RequireSuccess) {
    CoverageCi-AssertJobs $Context $Jobs
    $history = @($Jobs.jobs | Where-Object name -CEQ $Role.jobName | Sort-Object run_attempt)
    $attempts = [Collections.Generic.HashSet[int]]::new()
    $actual = [Collections.Generic.List[object]]::new()
    foreach ($job in $history) {
        Require-CoverageCi ($attempts.Add($job.run_attempt)) 'duplicate-role-attempt-job'
        if ($job.status -cne 'completed' -or $job.conclusion -cne 'success') {
            $actual.Add([pscustomobject]@{ job = $job; actualAttempt = $job.run_attempt; execution = $null }); continue
        }
        $execution = CoverageCi-Execution $job $Context.commit
        $fence = CoverageCi-Utc $Jobs.attempts[$job.run_attempt - 1].runStartedAt
        if ($execution.started -ge $fence) {
            $actual.Add([pscustomobject]@{ job = $job; actualAttempt = $job.run_attempt; execution = $execution }); continue
        }
        Require-CoverageCi ($execution.completed -lt $fence) 'overlapping-cached-job'
        $witnesses = @($actual | Where-Object { $null -ne $_.execution -and $_.execution.stamp -ceq $execution.stamp })
        Require-CoverageCi ($witnesses.Count -eq 1) 'unmatched-or-ambiguous-cached-job'
    }
    Require-CoverageCi ($actual.Count -gt 0) 'missing-latest-job'
    $selected = $actual[$actual.Count - 1]
    if ($RequireSuccess) {
        Require-CoverageCi ($selected.job.status -ceq 'completed' -and $selected.job.conclusion -ceq 'success') 'unsuccessful-latest-job'
        foreach ($name in $Role.requiredSteps) {
            $steps = @($selected.job.steps | Where-Object name -CEQ $name)
            Require-CoverageCi ($steps.Count -eq 1 -and $steps[0].status -ceq 'completed' -and $steps[0].conclusion -ceq 'success') 'required-step'
        }
    }
    return $selected
}
function CoverageCi-AssertOwner($Context, $Role, $Selected, $Owner, $Inventory, [string]$ArchiveSha256, [string]$OwnerSha256) {
    CoverageCi-AssertKeys $Owner @('schemaVersion','kind','role','shardIndex','shardCount','repository','commit','tree',
        'workflowName','workflowRunId','attempt','jobId','finishedUtc','preparationJobId','preparationAttempt','preparation',
        'originalRoot','artifactName','archiveSha256','evidenceSha256','inventory') 'owner-shape'
    Require-CoverageCi (($Owner.role -is [int] -or $Owner.role -is [long]) -and
        ($Owner.role -eq 0 -or $Owner.role -eq 1) -and (CoverageCi-PositiveInteger $Owner.jobId) -and
        (CoverageCi-PositiveInteger $Owner.attempt) -and (CoverageCi-PositiveInteger $Owner.preparationJobId) -and
        (CoverageCi-PositiveInteger $Owner.preparationAttempt)) 'owner-shape'
    $root = CoverageCi-Root $Context $Role.role $Role.shardIndex $Selected.actualAttempt
    Require-CoverageCi ($Owner.schemaVersion -eq 1 -and $Owner.kind -ceq 'coverage-ci-operation-owner' -and
        $Owner.role -eq $Role.role -and $Owner.shardIndex -eq $Role.shardIndex -and $Owner.shardCount -eq $Context.shardCount -and
        $Owner.repository -ceq $Context.repository -and $Owner.commit -ceq $Context.commit -and $Owner.tree -ceq $Context.tree -and
        $Owner.workflowName -ceq $Context.workflowName -and $Owner.workflowRunId -eq $Context.runId -and
        $Owner.attempt -eq $Selected.actualAttempt -and $Owner.jobId -eq $Selected.job.id -and $Owner.originalRoot -ceq $root -and
        $Owner.artifactName -ceq (CoverageCi-ArtifactName $Context $Role.role $Role.shardIndex $Selected.actualAttempt) -and
        (CoverageCi-Hash $OwnerSha256)) 'artifact-owner'
    Require-CoverageCi ((CoverageCi-Equal $Owner.preparation $Context.preparation) -and
        $Owner.archiveSha256 -ceq $ArchiveSha256 -and (CoverageCi-Hash $ArchiveSha256) -and
        (CoverageCi-Equal $Owner.inventory $Inventory)) 'artifact-byte-binding'
    $evidence = @($Inventory.files | Where-Object path -CEQ $Role.evidenceRelativePath)
    Require-CoverageCi ($evidence.Count -eq 1 -and $evidence[0].length -gt 0 -and
        $Owner.evidenceSha256 -ceq $evidence[0].sha256 -and (CoverageCi-Hash $Owner.evidenceSha256)) 'operation-evidence'
    $finished = CoverageCi-Utc $Owner.finishedUtc
    Require-CoverageCi ($finished -ge $Selected.execution.started -and $finished -le $Selected.execution.completed) 'owner-time'
    if ($Role.role -eq 0) {
        Require-CoverageCi ($Owner.preparationJobId -eq $Owner.jobId -and $Owner.preparationAttempt -eq $Owner.attempt -and
            $Owner.evidenceSha256 -ceq $Context.preparation.prepared.preparedSha256 -and
            (Join-Path $root $Role.evidenceRelativePath) -ceq $Context.preparation.prepared.preparedPath) 'prepared-owner'
    }
}
function CoverageCi-AssertChain($Preparation, $Shard, $ShardOwner, $Smoke = $null) {
    Require-CoverageCi ($ShardOwner.preparationJobId -eq $Preparation.job.id -and
        $ShardOwner.preparationAttempt -eq $Preparation.actualAttempt) 'mixed-preparation'
    Require-CoverageCi ($Shard.execution.started -ge $Preparation.execution.completed) 'shard-before-preparation'
    if ($null -ne $Smoke) {
        Require-CoverageCi ($Smoke.execution.started -ge $Preparation.execution.completed -and
            $Shard.execution.started -ge $Smoke.execution.completed) 'shard-before-smoke'
    }
}
function CoverageCi-AssertPrepareOutcome($Context, $Outcome) {
    # Rejection only: a failed coordinator may leave prepared.json behind. Its
    # flags cannot authorize source/build evidence; fresh raw prepared-check is
    # still mandatory after this guard and API completion is checked downstream.
    Require-CoverageCi ($Outcome.schemaVersion -eq 1 -and $Outcome.kind -ceq 'full-coverage-local-outcome' -and
        $Outcome.operation -ceq 'Prepare' -and $Outcome.runId -ceq $Context.preparation.runId -and
        $Outcome.operationComplete -is [bool] -and $Outcome.operationComplete -and
        $null -eq $Outcome.failureType -and $null -eq $Outcome.reason) 'failed-preparation-outcome'
}
function CoverageCi-AssertSmoke($PreparedValue, $ShardValue, [int]$ShardCount) {
    # Production supplies only the fresh outputs of independently executed
    # prepared-check and shard-check. Stored receipt flags are never this input.
    $expected = @($PreparedValue.wholePlan.smokeCanonicalIds)
    $actual = @($ShardValue.selectedCanonicalIds)
    Require-CoverageCi ($expected.Count -gt 0 -and @($PreparedValue.wholePlan.smokeOwnerIds).Count -gt 0 -and
        $ShardValue.shardIndex -eq 0 -and $ShardValue.shardCount -eq $ShardCount -and $actual.Count -gt 0 -and
        @($actual | Sort-Object -CaseSensitive -Unique).Count -eq $actual.Count -and
        (CoverageCi-Equal @($actual | Sort-Object -CaseSensitive) @($expected | Sort-Object -CaseSensitive)) -and
        (CoverageCi-Equal @($ShardValue.selectedOwnerIds | Sort-Object -CaseSensitive) `
            @($PreparedValue.wholePlan.smokeOwnerIds | Sort-Object -CaseSensitive))) 'smoke-selection'
}
function CoverageCi-Diagnostic($Context, [int]$Role, $ShardIndex, [string]$Category, [string]$Stage) {
    Require-CoverageCi ($Category -cin @('TestFailed','CollectionFailed','Cancelled','OperationFailed') -and
        $Stage -cmatch '^[a-z][a-z0-9-]{0,79}$') 'failure-classification'
    return [ordered]@{ schemaVersion = 1; kind = 'coverage-ci-diagnostic'; category = $Category; stage = $Stage
        artifactName = CoverageCi-ArtifactName $Context $Role $ShardIndex $Context.currentAttempt -Diagnostic
        testResult = $(if ($Category -ceq 'TestFailed') { 'failed' } else { 'not-established' })
        collectionResult = $(if ($Category -ceq 'CollectionFailed') { 'failed' } else { 'not-established' })
        operationComplete = $false; ownerSelectionComplete = $false; coverageUnionComplete = $false
        completeBaseline = $false; publicationAuthorized = $false; tiaSelectionAuthorized = $false }
}
function CoverageCi-Tool($Context, [string]$ToolFile, [string]$ToolSha256, [string]$DotnetFile,
    [string[]]$Arguments, [string]$Directory, [string]$WorkingDirectory) {
    [void](Lock-HybridFile $ToolFile $ToolSha256)
    Assert-HybridRegular $DotnetFile
    # Reuse the existing launcher, Windows Job and original stdout/stderr Tasks.
    # The outer Actions job retains its existing timeout; no new coverage time
    # cutoff or measured concurrency claim is introduced by these helpers.
    $exitCode = Invoke-HybridProcess $DotnetFile (@($ToolFile) + $Arguments) $Directory $WorkingDirectory 0
    Require-CoverageCi ($exitCode -eq 0) 'tool-rejected'
}
function CoverageCi-TransportExpected($Context, [string]$Root) {
    return [ordered]@{ schemaVersion = 1; originalRoot = $Root
        sourceDirectory = $Context.preparation.prepared.sourceDirectory; sourceTree = $Context.tree }
}
function CoverageCi-Inspect($Context, $Artifact, $Role, $Selected, [string]$Work, $Driver) {
    $owner = CoverageCi-Read $Artifact.ownerRecordPath $Artifact.ownerRecordSha256
    [void](Lock-HybridFile $Artifact.archivePath $Artifact.archiveSha256)
    Require-CoverageCi ((CoverageCi-PositiveInteger $Artifact.artifactId) -and
        $Artifact.artifactRunId -eq $Context.runId -and $Artifact.artifactCommit -ceq $Context.commit -and
        $Artifact.artifactName -ceq (CoverageCi-ArtifactName $Context $Role.role $Role.shardIndex $Selected.actualAttempt)) 'download-owner'
    $root = CoverageCi-Root $Context $Role.role $Role.shardIndex $Selected.actualAttempt
    $input = Join-Path $Work 'inspect-input.json'; $expectation = Join-Path $Work 'transport-expectation.json'
    $output = Join-Path $Work 'inspection.json'
    Write-HybridJson $input @{ schemaVersion = 1; archivePath = $Artifact.archivePath; expectedArchiveSha256 = $Artifact.archiveSha256 }
    Write-HybridJson $expectation (CoverageCi-TransportExpected $Context $root)
    CoverageCi-Tool $Context $Driver.toolFile $Driver.toolSha256 $Driver.dotnetFile @(
        'coverage-ci-transport-inspect','--input',$input,'--expectation',$expectation,'--output',$output) (Join-Path $Work 'inspect-process') $Work
    $inspection = CoverageCi-ReadJson $output
    $inventory = [ordered]@{ directories = @($inspection.manifest.directories); files = @($inspection.manifest.files) }
    CoverageCi-AssertOwner $Context $Role $Selected $owner $inventory $Artifact.archiveSha256 $Artifact.ownerRecordSha256
    return [pscustomobject]@{ owner = $owner; root = $root; artifact = $Artifact; transportExpectation = $expectation }
}
function CoverageCi-Restore($Context, $Inspected, [string]$Work, $Driver) {
    $input = Join-Path $Work 'restore-input.json'
    Write-HybridJson $input @{ schemaVersion = 1; archivePath = $Inspected.artifact.archivePath
        expectedArchiveSha256 = $Inspected.artifact.archiveSha256 }
    CoverageCi-Tool $Context $Driver.toolFile $Driver.toolSha256 $Driver.dotnetFile @(
        'coverage-ci-transport-restore','--input',$input,'--expectation',$Inspected.transportExpectation,
        '--output',(Join-Path $Work 'restore-receipt.json')) (Join-Path $Work 'restore-process') $Work
}
function CoverageCi-PreparedCheck($Context, [string]$Work) {
    $binding = $Context.preparation
    $document = CoverageCi-Read $binding.prepared.preparedPath $binding.prepared.preparedSha256
    Require-CoverageCi ($document.runId -ceq $binding.runId -and (CoverageCi-Equal $document.source $binding.source) -and
        $document.admission.buildAuthority.evidenceSha256 -ceq $binding.buildAuthoritySha256 -and
        $binding.sdkVersion -ceq '10.0.403' -and $binding.configuration -ceq 'Release' -and
        $binding.targetFramework -ceq 'net10.0-windows' -and
        $document.admission.buildAuthority.instrumentationPolicyVersion -ceq $binding.instrumentationPolicyVersion) 'preparation-binding'
    [void](Lock-HybridFile $binding.prepared.snapshotContextPath $binding.snapshotContextSha256)
    [void](Lock-HybridFile (Join-Path $document.preparationDirectory 'build-authority.json') $binding.buildAuthoritySha256)
    [void](Lock-HybridFile $binding.prepared.dotCoverToolFile $binding.dotCoverToolSha256)
    [void](Lock-HybridFile (Join-Path (Split-Path -Parent $binding.prepared.dotnetFile) 'sdk/10.0.403/vstest.console.dll') $binding.vstestConsoleSha256)
    $expected = Join-Path $Work 'prepared-expectation.json'; $output = Join-Path $Work 'prepared-check.json'
    Write-HybridJson $expected $Context.preparation.prepared
    CoverageCi-Tool $Context $Context.preparation.prepared.metadataToolFile $Context.preparation.metadataToolSha256 `
        $Context.preparation.prepared.dotnetFile @('coverage-full-prepared-check','--expectation',$expected,'--output',$output) `
        (Join-Path $Work 'prepared-check-process') $Context.preparation.prepared.sourceDirectory
    return [pscustomobject]@{ expectation = $expected; value = (CoverageCi-ReadJson $output).value }
}
function CoverageCi-SmokeCheck($Context, $SmokeOwner, $Prepared, [string]$Work) {
    $role = CoverageCi-Role $Context 1 0
    $path = Join-Path $SmokeOwner.originalRoot $role.evidenceRelativePath
    $manifest = CoverageCi-Read $path $SmokeOwner.evidenceSha256
    Require-CoverageCi ($manifest.schemaVersion -eq 1 -and $manifest.shardIndex -eq 0 -and
        $manifest.shardCount -eq $Context.shardCount -and $manifest.shardDirectory -ceq $SmokeOwner.originalRoot -and
        $manifest.preparedPath -ceq $Context.preparation.prepared.preparedPath -and
        $manifest.preparedSha256 -ceq $Context.preparation.prepared.preparedSha256) 'smoke-manifest'
    foreach ($pin in @($manifest.input, $manifest.expectation, $manifest.receipt, $manifest.validationInvocation,
        $manifest.validationProcess, $manifest.coordinator)) {
        Require-CoverageCi ((Test-HybridContained $pin.path $SmokeOwner.originalRoot) -and
            [IO.FileInfo]::new($pin.path).Length -eq $pin.length) 'smoke-file-pin'
        [void](Lock-HybridFile $pin.path $pin.sha256)
    }
    $raw = CoverageCi-ReadJson $manifest.input.path
    Require-CoverageCi ($raw.shardIndex -eq 0 -and $raw.shardCount -eq $Context.shardCount -and
        $raw.shardDirectory -ceq $SmokeOwner.originalRoot -and @($raw.orderedGroups).Count -eq 0 -and
        @($raw.isolatedOwners).Count -gt 0 -and
        (CoverageCi-Equal (CoverageCi-ReadJson $manifest.expectation.path) $Context.preparation.prepared)) 'smoke-raw-input'
    $output = Join-Path $Work 'smoke-reimport.json'
    CoverageCi-Tool $Context $Context.preparation.prepared.metadataToolFile $Context.preparation.metadataToolSha256 `
        $Context.preparation.prepared.dotnetFile @('coverage-full-shard-check','--input',$manifest.input.path,
            '--expectation',$Prepared.expectation,'--output',$output) (Join-Path $Work 'smoke-reimport-process') `
        $Context.preparation.prepared.sourceDirectory
    CoverageCi-AssertSmoke $Prepared.value (CoverageCi-ReadJson $output).value $Context.shardCount
}
function CoverageCi-AssertDownloads($Context, $Snapshot, $Downloads) {
    Require-CoverageCi ($Snapshot.repository -ceq $Context.repository -and $Snapshot.runId -eq $Context.runId -and
        $Snapshot.totalCount -eq @($Snapshot.artifacts).Count -and @($Snapshot.artifacts).Count -le 200000) 'artifact-snapshot'
    $apiIds = [Collections.Generic.HashSet[long]]::new()
    $required = [Collections.Generic.Dictionary[long,object]]::new()
    foreach ($artifact in $Snapshot.artifacts) {
        Require-CoverageCi ((CoverageCi-PositiveInteger $artifact.id) -and $apiIds.Add($artifact.id)) 'artifact-snapshot'
        # Diagnostics retain their own namespace and are never candidates for
        # owner selection, even when their upload/API result was successful.
        if ($artifact.name -cmatch '^coverage-(?:prepare-|shard-)') {
            Require-CoverageCi ($artifact.expired -eq $false -and $artifact.workflow_run.id -eq $Context.runId -and
                $artifact.workflow_run.head_sha -ceq $Context.commit) 'artifact-api-owner'
            $required.Add($artifact.id, $artifact)
        }
    }
    $seen = [Collections.Generic.HashSet[long]]::new()
    foreach ($download in $Downloads) {
        Require-CoverageCi ((CoverageCi-PositiveInteger $download.artifactId) -and $seen.Add($download.artifactId) -and
            $required.ContainsKey($download.artifactId) -and $download.artifactName -ceq $required[$download.artifactId].name -and
            $download.artifactRunId -eq $Context.runId -and $download.artifactCommit -ceq $Context.commit -and
            (CoverageCi-Hash $download.ownerRecordSha256) -and (CoverageCi-Hash $download.archiveSha256)) 'download-set'
    }
    Require-CoverageCi ($seen.Count -gt 0 -and $seen.Count -eq $required.Count) 'download-set'
}
function CoverageCi-DownloadFor($Downloads, $Selected) {
    $matches = @($Downloads | Where-Object artifactName -CEQ $Selected.artifactName)
    Require-CoverageCi ($matches.Count -eq 1 -and $matches[0].ownerRecordSha256 -ceq $Selected.ownerRecordSha256 -and
        $matches[0].archiveSha256 -ceq $Selected.archiveSha256) 'selected-download'
    return $matches[0]
}
function CoverageCi-ComposeInvocation($Context, $Shards, [string]$ResultsDirectory) {
    Require-CoverageCi ($null -ne $Shards -and $Shards.Count -eq $Context.shardCount) 'compose-shard-count'
    $paths = [Collections.Generic.List[string]]::new(); $hashes = [Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $Context.shardCount; $index++) {
        $selected = $Shards[$index].selected
        Require-CoverageCi ($selected.shardIndex -eq $index -and (CoverageCi-Hash $selected.evidenceSha256)) 'compose-shard-order'
        $paths.Add($selected.evidencePath); $hashes.Add($selected.evidenceSha256)
    }
    return [ordered]@{ entry = Join-Path $Context.preparation.prepared.sourceDirectory 'tools/Invoke-FullCoverage.ps1'
        parameters = [ordered]@{ Operation = 'Compose'; RepositoryRoot = $Context.preparation.prepared.sourceDirectory
            ResultsDirectory = $ResultsDirectory; PreparedPath = $Context.preparation.prepared.preparedPath
            ExpectedPreparedSha256 = $Context.preparation.prepared.preparedSha256; ShardCount = $Context.shardCount
            ShardManifestPath = $paths.ToArray(); ExpectedShardSha256 = $hashes.ToArray() } }
}
function CoverageCi-ComposeLauncher([string]$Path) {
    # pwsh -File cannot carry native string-array parameter binding. This small
    # adapter only splats our freshly written, locked JSON into the public entry.
    $body = @'
param([Parameter(Mandatory)][string]$InputPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$invocation = [IO.File]::ReadAllText($InputPath) | ConvertFrom-Json -Depth 64
$parameters = @{}
foreach ($property in $invocation.parameters.PSObject.Properties) { $parameters.Add($property.Name, $property.Value) }
& $invocation.entry @parameters
exit $LASTEXITCODE
'@
    $stream = $null; $primary = $null
    try {
        $stream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($body)
        $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true)
    } catch { $primary = $_ }
    finally { if ($null -ne $stream) { try { $stream.Dispose() } catch { if ($null -eq $primary) { $primary = $_ } } } }
    if ($null -ne $primary) { throw $primary }
}
