#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('IsolatedCase', 'ChildAttribution', 'SameHost', 'Performance')][string]$Scenario = 'IsolatedCase',
    [string[]]$CaseId = @(),
    [string]$ResultsDirectory,
    [string]$EvidenceDirectory,
    [string]$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false)
$root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RepositoryRoot))
$runId = [Guid]::NewGuid().ToString('N')
$shell = (Get-Process -Id $PID).Path
$runRoot = $null
$stage = 'preflight'
$cases = [Collections.Generic.List[object]]::new()
$failure = $null
$source = $null
$delegated = $null

function Require-Compatibility([bool]$Condition, [string]$Code) { if (-not $Condition) { throw $Code } }
function Digest([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Write-CompatibilityJson([string]$Path, $Value) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $bytes = $utf8.GetBytes(($Value | ConvertTo-Json -Depth 40)); $stream.Write($bytes); $stream.Flush($true) }
    finally { $stream.Dispose() }
}
function Read-CompatibilityJson([string]$Path) { [IO.File]::ReadAllText($Path) | ConvertFrom-Json -AsHashtable }

function Measure-CollectedCase([string]$Directory) {
    $admission = Read-CompatibilityJson (Join-Path $Directory 'admission.json')
    Require-Compatibility ($admission.schemaVersion -eq 1 -and $admission.validCaseCoverage -eq $true -and
        $admission.testingVerified -eq $true -and $admission.collectionComplete -eq $true -and
        $admission.baselineClaim -eq $false -and $admission.caseCoverageSha256 -ceq (Digest (Join-Path $Directory 'case-coverage.json'))) 'compatibility-admission-invalid'
    $case = (Read-CompatibilityJson (Join-Path $Directory 'case-coverage.json')).value
    Require-Compatibility ($case.identity.runId -ceq $admission.identity.runId -and
        $case.identity.sessionId -ceq $admission.identity.sessionId -and $case.identity.canonicalId -ceq $admission.identity.canonicalId -and
        $case.attributionMode -ceq 'isolated-case' -and $case.completeBaseline -eq $false) 'compatibility-case-identity-invalid'
    $cover = $case.supervisor.cover
    $export = $case.supervisor.export
    $coverStart = [DateTimeOffset]$cover.startedUtc
    $coverFinish = [DateTimeOffset]$cover.finishedUtc
    $hostStart = [DateTimeOffset]$admission.hostLifecycle.startedUtc
    $hostFinish = [DateTimeOffset]$admission.hostLifecycle.finishedUtc
    $allBytes = [long]0
    foreach ($file in Get-ChildItem -LiteralPath $Directory -File -Recurse) { $allBytes += $file.Length }
    $coveredMethods = @($case.aggregate.members | Where-Object { $_.kind -ceq 'Method' -and $_.coveredStatements -gt 0 } | ForEach-Object {
        $owner = @($_.symbolSegments | Where-Object kind -CIn @('Namespace', 'Type') | ForEach-Object name) -join '.'
        $owner + '::' + $_.name
    })
    $configuration = 'STranslate.Plugin.Tts.FishAudio.Configuration.'
    $exclusiveA = @(($configuration + 'SettingsValidation::IsValidVoiceIdDraft(string):bool'),
        ($configuration + 'SettingsValidation::TryGetBoolean(JsonElement,out bool):bool'),
        ($configuration + 'SettingsNormalizer::RoundToPrecision(Decimal,int):double'))
    $exclusiveB = @(($configuration + 'FishAudioModelPolicy::SupportsNormalizeLoudness(string):bool'),
        ($configuration + 'SettingsValidation::TryGetInt32(JsonElement,out int):bool'),
        ($configuration + 'SettingsNormalizer::NormalizeTopP(double):double'))
    $shared = @(($configuration + 'SettingsValidation::TryGetDouble(JsonElement,out double):bool'),
        ($configuration + 'SettingsValidation::TryGetNullableString(JsonElement,out string):bool'))
    $expectedHits = @()
    $expectedAbsent = @()
    switch -CaseSensitive ($case.identity.canonicalId) {
        'compat.sync/api-key-format' { $expectedHits = @($configuration + 'SettingsValidation::IsValidApiKeyFormat(string):bool') }
        'compat.async/json-string-after-yield' { $expectedHits = @($configuration + 'SettingsValidation::TryGetString(JsonElement,out string):bool') }
        { $_ -cin @('compat.source/voice-id/lowercase-32', 'compat.source/voice-id/uppercase-32') } { $expectedHits = @($configuration + 'SettingsValidation::IsValidVoiceIdFormat(string):bool') }
        'compat.same-host/a-draft-boolean-rounding' { $expectedHits = @($exclusiveA) + @($shared); $expectedAbsent = @($exclusiveB) }
        'compat.same-host/b-loudness-integer-top-p' { $expectedHits = @($exclusiveB) + @($shared); $expectedAbsent = @($exclusiveA) }
        'compat.native/settings-view-construction' { $expectedHits = @('STranslate.Plugin.Tts.FishAudio.View.SettingsView::InitializeComponent():void') }
        'compat.child/streaming-text-first-window' { $expectedHits = @('STranslate.Plugin.Tts.FishAudio.Streaming.StreamingTextReader::ReadNext(int):StreamingTextChunk') }
        'compat.repository/pinned-coverage-tool' {
            Require-Compatibility ($coveredMethods.Count -eq 0 -and $case.emptyProduct.reason -ceq 'RepositoryResourcesOnly' -and
                @($case.emptyProduct.dependencies | Where-Object relativePath -CEQ '.config/dotnet-tools.json').Count -eq 1) 'compatibility-resource-coverage-control-failed'
        }
    }
    foreach ($name in $expectedHits) { Require-Compatibility (@($coveredMethods | Where-Object { $_ -ceq $name }).Count -eq 1) 'compatibility-required-product-path-missing' }
    foreach ($name in $expectedAbsent) { Require-Compatibility (@($coveredMethods | Where-Object { $_ -ceq $name }).Count -eq 0) 'compatibility-exclusive-product-path-contaminated' }
    return [ordered]@{
        canonicalId = $case.identity.canonicalId; sessionId = $case.identity.sessionId
        source = $case.identity.source; buildId = $case.identity.buildId; attributionMode = $case.attributionMode
        testDurationMilliseconds = [TimeSpan]::Parse($case.testResult.duration).TotalMilliseconds
        coverMilliseconds = ($coverFinish - $coverStart).TotalMilliseconds
        coverPrologueToHostStartMilliseconds = ($hostStart - $coverStart).TotalMilliseconds
        hostCallbackEnvelopeMilliseconds = ($hostFinish - $hostStart).TotalMilliseconds
        hostEndToCoverCompletionMilliseconds = ($coverFinish - $hostFinish).TotalMilliseconds
        exportMilliseconds = ([DateTimeOffset]$export.finishedUtc - [DateTimeOffset]$export.startedUtc).TotalMilliseconds
        supervisedPhaseMilliseconds = [double](($admission.phases | ForEach-Object { $_.process.elapsedMilliseconds } | Measure-Object -Sum).Sum)
        snapshotBytes = (Get-Item -LiteralPath (Join-Path $Directory 'coverage.dcvr')).Length
        aggregateBytes = (Get-Item -LiteralPath (Join-Path $Directory 'coverage.json')).Length
        allRetainedArtifactBytes = $allBytes; validCaseCoverage = $true; completeBaseline = $false
        coveredProductReportMethods = $coveredMethods
        representativeControls = @{ requiredHits = $expectedHits; requiredAbsent = $expectedAbsent; sourceMappingsClaim = $false }
    }
}

try {
    Require-Compatibility $IsWindows 'compatibility-windows-required'
    Require-Compatibility ($PSScriptRoot.Equals((Join-Path $root 'tools'), [StringComparison]::OrdinalIgnoreCase)) 'compatibility-entry-root-mismatch'
    if ([string]::IsNullOrEmpty($ResultsDirectory)) { $ResultsDirectory = Join-Path ([IO.Path]::GetTempPath()) ('fa-compat-results-' + $runId) }
    Require-Compatibility ([IO.Path]::IsPathFullyQualified($ResultsDirectory)) 'compatibility-results-must-be-absolute'
    Require-Compatibility (-not $ResultsDirectory.StartsWith('\\?\', [StringComparison]::Ordinal) -and
        -not $ResultsDirectory.StartsWith('\\.\', [StringComparison]::Ordinal) -and
        -not $ResultsDirectory.StartsWith('\??\', [StringComparison]::Ordinal)) 'compatibility-results-device-namespace'
    $candidate = [IO.Path]::GetFullPath($ResultsDirectory)
    Require-Compatibility (-not [IO.Directory]::Exists($candidate) -and -not [IO.File]::Exists($candidate) -and
        -not $candidate.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
        -not $candidate.Equals($root, [StringComparison]::OrdinalIgnoreCase)) 'compatibility-results-must-be-fresh-and-external'
    for ($ancestor = [IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($candidate)); $null -ne $ancestor; $ancestor = $ancestor.Parent) {
        if ($ancestor.Exists) { Require-Compatibility (($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'compatibility-results-parent-reparse' }
    }
    [void][IO.Directory]::CreateDirectory($candidate)
    $runRoot = $candidate

    if ($Scenario -ceq 'Performance') {
        $stage = 'measure-existing-evidence'
        Require-Compatibility ($CaseId.Count -eq 0 -and [IO.Path]::IsPathFullyQualified($EvidenceDirectory) -and
            [IO.Directory]::Exists($EvidenceDirectory)) 'compatibility-performance-evidence-required'
        foreach ($directory in Get-ChildItem -LiteralPath $EvidenceDirectory -Directory -Filter 'session-*' | Sort-Object Name) {
            $cases.Add((Measure-CollectedCase $directory.FullName))
        }
        Require-Compatibility ($cases.Count -gt 0) 'compatibility-no-admitted-sessions'
    } elseif (-not $env:FISHAUDIO_SNAPSHOT_CONTEXT) {
        $stage = 'freeze-index'
        $snapshotRoot = Join-Path ([IO.Path]::GetTempPath()) ('fa-k-' + $runId)
        $contextPath = Join-Path $runRoot 'snapshot.json'
        $preparedText = & $shell -NoLogo -NoProfile -File (Join-Path $PSScriptRoot 'New-TestSnapshot.ps1') -RepositoryRoot $root -SnapshotDirectory $snapshotRoot -ContextPath $contextPath
        Require-Compatibility ($LASTEXITCODE -eq 0) 'compatibility-snapshot-failed'
        $prepared = ($preparedText -join "`n") | ConvertFrom-Json -AsHashtable
        Require-Compatibility ($prepared.schemaVersion -eq 1 -and $prepared.root -ceq $snapshotRoot -and $prepared.contextPath -ceq $contextPath) 'compatibility-snapshot-owner-invalid'
        Write-CompatibilityJson (Join-Path $runRoot 'snapshot-owner.json') $prepared
        $previousContext = $env:FISHAUDIO_SNAPSHOT_CONTEXT
        try {
            $env:FISHAUDIO_SNAPSHOT_CONTEXT = $contextPath
            $options = @{ Scenario = $Scenario; RepositoryRoot = $snapshotRoot; ResultsDirectory = (Join-Path $runRoot 'execution'); CaseId = $CaseId }
            & (Join-Path $snapshotRoot 'tools/TestCompatibility.ps1') @options
            $nestedExit = $LASTEXITCODE
        } finally { $env:FISHAUDIO_SNAPSHOT_CONTEXT = $previousContext }
        Require-Compatibility ($nestedExit -eq 0) 'compatibility-snapshot-execution-failed'
        $nestedPath = Join-Path $runRoot 'execution/compatibility.json'
        $nested = Read-CompatibilityJson $nestedPath
        Require-Compatibility ($nested.complete -eq $true -and $nested.sourceTree -ceq $prepared.tree -and
            @($nested.cases).Count -gt 0) 'compatibility-delegated-evidence-invalid'
        foreach ($case in $nested.cases) { $cases.Add($case) }
        $source = @{ tree = $prepared.tree }
        $delegated = @{ relativePath = 'execution/compatibility.json'; sha256 = Digest $nestedPath }
        Require-Compatibility ((Digest $prepared.indexPath) -ceq $prepared.indexSha256) 'compatibility-index-changed'
        Write-CompatibilityJson (Join-Path $runRoot 'completion.json') @{ schemaVersion = 1; complete = $true; sourceTree = $prepared.tree
            execution = 'execution/compatibility.json'; baselineClaim = $false; commitReceiptClaim = $false }
        Write-Output 'compatibility: frozen index execution completed; no commit receipt or baseline claim.'
        exit 0
    } else {
        $source = Read-CompatibilityJson $env:FISHAUDIO_SNAPSHOT_CONTEXT
        Require-Compatibility ($source.schemaVersion -eq 1 -and $source.root -ceq $root -and $source.files.Count -gt 0) 'compatibility-snapshot-context-mismatch'
        $stage = 'normal-discovery-and-build'
        & $shell -NoLogo -NoProfile -File (Join-Path $root 'tools/Test.ps1') -Mode Discover -RepositoryRoot $root -ResultsDirectory (Join-Path $runRoot 'normal-discovery')
        Require-Compatibility ($LASTEXITCODE -eq 0) 'compatibility-normal-discovery-failed'
        $representatives = @(
            'compat.sync/api-key-format', 'compat.async/json-string-after-yield',
            'compat.source/voice-id/lowercase-32', 'compat.source/voice-id/uppercase-32',
            'compat.same-host/a-draft-boolean-rounding', 'compat.same-host/b-loudness-integer-top-p',
            'compat.native/settings-view-construction', 'compat.child/streaming-text-first-window', 'compat.repository/pinned-coverage-tool'
        )
        if ($Scenario -ceq 'ChildAttribution') { $representatives = @('compat.native/settings-view-construction', 'compat.child/streaming-text-first-window') }
        if ($Scenario -ceq 'SameHost') { $representatives = @('compat.same-host/a-draft-boolean-rounding', 'compat.same-host/b-loudness-integer-top-p') }
        if ($CaseId.Count -gt 0) { $representatives = @($CaseId) }
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($id in $representatives) {
            Require-Compatibility (-not [string]::IsNullOrWhiteSpace($id) -and $seen.Add($id)) 'compatibility-invalid-selected-case'
            $cases.Add([ordered]@{ canonicalId = $id; outcome = 'not-run'; metrics = $null; failure = $null })
        }
        $stage = 'normal-exact-execution'
        $normalIds = @($representatives)
        if ($CaseId.Count -eq 0) { $normalIds += @('compat.parallel/api-key-format', 'compat.parallel/voice-id-format') }
        $normalOptions = @{ Mode = 'Full'; RepositoryRoot = $root; ResultsDirectory = (Join-Path $runRoot 'normal-execution'); NoBuild = $true; CaseId = $normalIds; Workers = 4 }
        & (Join-Path $root 'tools/Test.ps1') @normalOptions
        Require-Compatibility ($LASTEXITCODE -eq 0 -and [IO.File]::Exists((Join-Path $runRoot 'normal-execution/manifest.json'))) 'compatibility-normal-execution-failed'
        $stage = 'coverage-discovery-and-build'
        & $shell -NoLogo -NoProfile -File (Join-Path $root 'tools/Test.ps1') -Mode DiscoverCoverage -RepositoryRoot $root -ResultsDirectory (Join-Path $runRoot 'coverage-discovery')
        Require-Compatibility ($LASTEXITCODE -eq 0) 'compatibility-coverage-discovery-failed'
        $packageRoot = $env:NUGET_PACKAGES
        if ([string]::IsNullOrEmpty($packageRoot)) { $packageRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
        $toolFile = Join-Path $packageRoot 'jetbrains.dotcover.commandlinetools/2026.2.3.1/tools/net10.0/any/dotCover.dll'
        Require-Compatibility ([IO.File]::Exists($toolFile)) 'compatibility-pinned-dotcover-missing'
        $ordinal = 0
        foreach ($case in $cases) {
            $ordinal++
            $stage = 'isolated-case-' + $ordinal
            $directory = Join-Path $runRoot ('session-{0:D3}' -f $ordinal)
            & $shell -NoLogo -NoProfile -File (Join-Path $root 'tools/Invoke-IsolatedCoverage.ps1') -CaseId $case.canonicalId -RunId $runId -RepositoryRoot $root -ResultsDirectory $directory -DotCoverToolFile $toolFile -TestAssembly (Join-Path $root 'tests/STranslate.Plugin.Tts.FishAudio.Tests/bin/coverage/Release/net10.0-windows/STranslate.Plugin.Tts.FishAudio.Tests.dll') -ProductAssembly (Join-Path $root '.artifacts/coverage/Release/STranslate.Plugin.Tts.FishAudio.dll')
            if ($LASTEXITCODE -eq 0) {
                $case.metrics = Measure-CollectedCase $directory
                Require-Compatibility ($case.metrics.canonicalId -ceq $case.canonicalId) 'compatibility-collected-case-mismatch'
                $case.outcome = 'passed'
            } else { $case.outcome = 'failed'; $case.failure = 'collection-or-admission-rejected' }
        }
        Require-Compatibility (@($cases | Where-Object outcome -CNE 'passed').Count -eq 0) 'compatibility-required-case-rejected'
    }
} catch {
    $failure = @{ stage = $stage; code = 'compatibility-rejected'; exceptionType = $_.Exception.GetType().Name }
    if ($runRoot) {
        try {
            $privateFailure = ($_ | Out-String)
            $stream = [IO.File]::Open((Join-Path $runRoot 'private-failure.txt'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
            try { $stream.Write($utf8.GetBytes($privateFailure)) } finally { $stream.Dispose() }
        } catch { }
    }
} finally {
    if ($runRoot) {
        Write-CompatibilityJson (Join-Path $runRoot 'compatibility.json') ([ordered]@{
            schemaVersion = 1; runId = $runId; scenario = $Scenario; platform = 'VSTest'
            sourceTree = $(if ($source) { $source.tree } else { $null }); complete = ($null -eq $failure)
            baselineClaim = $false; commitReceiptClaim = $false; attributionMode = 'isolated-case'
            executionPerformed = ($Scenario -cne 'Performance')
            coverageConcurrency = $(if ($Scenario -ceq 'Performance') { $null } else { 1 })
            coverageConcurrencyScope = $(if ($Scenario -ceq 'Performance') { 'not established by this existing-evidence report' } else { 'observed serial sessions sharing one immutable build' })
            measurementNote = 'Callback/prologue intervals are observed envelopes; their subtraction is not a pure startup or execution measurement. No new performance budget.'
            cases = $cases.ToArray(); failure = $failure
            delegatedExecution = $delegated
        })
    }
}
if ($failure) { [Console]::Error.WriteLine('compatibility: rejected stage=' + $failure.stage + '; inspect retained evidence.'); exit 1 }
if ($Scenario -ceq 'Performance') {
    Write-Output ('compatibility: reported ' + $cases.Count + ' existing admitted sessions; no test execution or complete baseline claim.')
} else { Write-Output ('compatibility: passed ' + $cases.Count + ' isolated cases; no complete baseline claim.') }
exit 0
