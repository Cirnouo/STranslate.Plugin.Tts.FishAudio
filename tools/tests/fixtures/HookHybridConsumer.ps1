param([string]$RepositoryRoot,[string]$SessionDirectory,[string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$encoding=[Text.UTF8Encoding]::new($false)
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$snapshotRoot=$root
$contextPath=$env:FISHAUDIO_SNAPSHOT_CONTEXT
$context=[IO.File]::ReadAllText($contextPath)|ConvertFrom-Json
$tree=$context.tree;$sourceCommit=$context.sourceCommit
$runRoot=[IO.Path]::GetDirectoryName($contextPath)
if($RepositoryRoot -cne $root -or [IO.Directory]::Exists($OutputDirectory) -or
    -not $SessionDirectory.StartsWith($runRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'fixture-hybrid-consumer-contract'}
[IO.Directory]::CreateDirectory($OutputDirectory)|Out-Null
. (Join-Path $PSScriptRoot 'HybridSession.Common.ps1')
function Invoke-GatePhase([string]$Name,[string]$Executable,[string[]]$Arguments,[int]$TimeoutMilliseconds=0){
    if(-not [IO.Path]::IsPathFullyQualified($Executable)){$Executable=(Get-Command $Executable -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source}
    $code=Invoke-HybridProcess $Executable $Arguments (Join-Path $runRoot $Name) $root $TimeoutMilliseconds
    if($code -ne 0){throw 'fixture-metadata-rejected'}
}
function Resolve-GateArtifact {
    param([string]$Parent, [string]$Relative, [string]$Digest)
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or
        $Relative.Contains('\') -or @($Relative.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -ne 0 -or
        $Digest -cnotmatch '^[a-f0-9]{64}$') { throw 'gate-platform-invalid-artifact' }
    $parentPath = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Parent))
    $path = [IO.Path]::GetFullPath((Join-Path $parentPath $Relative))
    if (-not $path.StartsWith($parentPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.File]::Exists($path)) { throw 'gate-platform-artifact-mismatch' }
    $current = Get-Item -LiteralPath $path -Force
    if ($current.PSIsContainer) { throw 'gate-platform-artifact-mismatch' }
    while ($null -ne $current) {
        if (($current.Attributes -band ([IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Device)) -ne 0) {
            throw 'gate-platform-artifact-mismatch'
        }
        if ($current.FullName.Equals($parentPath, [StringComparison]::OrdinalIgnoreCase)) { break }
        $current = $(if ($current -is [IO.DirectoryInfo]) { $current.Parent } else { $current.Directory })
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Digest) {
        throw 'gate-platform-artifact-mismatch'
    }
    return $path
}

function Assert-GateCaseSet {
    param([object[]]$Actual, [object[]]$Expected)
    $set = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($id in $Actual) {
        if ([string]::IsNullOrWhiteSpace($id) -or -not $set.Add([string]$id)) { throw 'gate-platform-invalid-case-set' }
    }
    if ($set.Count -eq 0 -or $set.Count -ne $Expected.Count -or -not $set.SetEquals([string[]]$Expected)) { throw 'gate-platform-case-set-mismatch' }
}

$manifest=@{platformArtifacts=@();platformVerified=$false}
$reportPath=Join-Path $SessionDirectory 'slots/slot-0001/attempt-0001/legacy.json'
$runner=Join-Path $root 'tests/STranslate.Plugin.Tts.FishAudio.Tests/bin/Release/net10.0-windows/STranslate.Plugin.Tts.FishAudio.Tests.dll'
$verifiedLegacyCatalog=Join-Path $runRoot 'verified-legacy-catalog.json'
$legacyCatalogGuard=$null
try {
    $inventory=Read-HybridLegacyInventory $reportPath $SessionDirectory
    Invoke-GatePhase 'legacy-verifier' (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source @($runner,'--verify-snapshot-report',$reportPath,$tree,'--catalog-output',$verifiedLegacyCatalog) 120000
    $legacyCatalogGuard=[IO.File]::Open($verifiedLegacyCatalog,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $platformRoot=Join-Path $runRoot 'platform'

    $stage = 'verify-framework-evidence'
    $platformManifestPath = Join-Path $platformRoot 'manifest.json'
    $platform = [IO.File]::ReadAllText($platformManifestPath) | ConvertFrom-Json
    $contextDigest = (Get-FileHash -LiteralPath $contextPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($platform.schemaVersion -ne 2 -or $platform.kind -cne 'unified-test-session' -or
        $platform.mode -cne 'Full' -or $platform.platform -cne 'VSTest' -or $platform.configuration -cne 'Release' -or
        $platform.selection -cne 'all-discovered' -or -not $platform.complete -or $platform.baselineClaim -ne $false -or
        -not $platform.source.snapshot -or -not $platform.source.snapshotRootValidated -or
        $platform.source.tree -cne $tree -or $platform.source.sourceCommit -cne $sourceCommit -or
        $platform.source.contextSha256 -cne $contextDigest -or @($platform.assemblies).Count -ne 2) {
        throw 'gate-platform-session-mismatch'
    }
    $tool = Join-Path $snapshotRoot 'tools/FishAudio.Testing/bin/Release/net10.0-windows/FishAudio.Testing.dll'
    if ((Get-FileHash -LiteralPath $tool -Algorithm SHA256).Hash.ToLowerInvariant() -cne $platform.toolSha256) { throw 'gate-platform-tool-mismatch' }
    $artifacts = [Collections.Generic.List[object]]::new()
    $artifacts.Add(@{ path = 'platform/manifest.json'; sha256 = (Get-FileHash -LiteralPath $platformManifestPath -Algorithm SHA256).Hash.ToLowerInvariant() })
    $assemblyKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $globalIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $migrationDiscoveries = [Collections.Generic.List[string]]::new()
    $expectedAssemblies = @{
        'plugin' = 'tests/STranslate.Plugin.Tts.FishAudio.Tests/bin/Release/net10.0-windows/STranslate.Plugin.Tts.FishAudio.Tests.dll'
        'testing-tool' = 'tests/FishAudio.Testing.Tests/bin/Release/net10.0-windows/FishAudio.Testing.Tests.dll'
    }
    foreach ($phase in $platform.phases) {
        if (-not $phase.complete -or $phase.exitCode -ne 0) { throw 'gate-platform-incomplete-phase' }
    }
    foreach ($assembly in $platform.assemblies) {
        $key = [string]$assembly.key
        if (-not $expectedAssemblies.ContainsKey($key) -or -not $assemblyKeys.Add($key) -or
            $assembly.assembly -cne $expectedAssemblies[$key] -or -not $assembly.executed -or
            $assembly.discoveredCount -le 0 -or $assembly.discoveredCount -ne $assembly.selectedCount -or
            $assembly.passedCount -ne $assembly.selectedCount) { throw 'gate-platform-assembly-mismatch' }
        [void](Resolve-GateArtifact $snapshotRoot $assembly.assembly $assembly.sha256)
        $paths = @{}
        foreach ($name in @('catalog','selection','requestedSelection','executionPlan')) {
            $relative = [string]$assembly.$name
            $digest = [string]$assembly.($name + 'Sha256')
            $paths[$name] = Resolve-GateArtifact $platformRoot $relative $digest
            $artifacts.Add(@{ path = 'platform/' + $relative; sha256 = $digest })
        }
        $catalog = [IO.File]::ReadAllText($paths.catalog) | ConvertFrom-Json
        $selection = [IO.File]::ReadAllText($paths.selection) | ConvertFrom-Json
        $requestedSelection = [IO.File]::ReadAllText($paths.requestedSelection) | ConvertFrom-Json
        if ($catalog.schemaVersion -ne 1 -or $catalog.kind -cne 'nunit-catalog' -or -not $catalog.complete -or
            $selection.schemaVersion -ne 1 -or $requestedSelection.schemaVersion -ne 1 -or
            @($catalog.cases).Count -ne $assembly.discoveredCount) { throw 'gate-platform-invalid-catalog' }
        if (($platform.workers -isnot [int] -and $platform.workers -isnot [long]) -or $platform.workers -lt 0 -or $platform.workers -gt 4 -or
            ($requestedSelection.maximumConcurrency -isnot [int] -and $requestedSelection.maximumConcurrency -isnot [long]) -or
            $requestedSelection.maximumConcurrency -ne [Math]::Max(1, $platform.workers)) { throw 'gate-platform-invalid-workers' }
        $ids = @($catalog.cases | ForEach-Object canonicalId)
        Assert-GateCaseSet @($assembly.selectedIds) $ids
        Assert-GateCaseSet @($selection.ids) $ids
        Assert-GateCaseSet @($requestedSelection.ids) $ids
        if ((@($requestedSelection.ids) -join "`n") -cne ($ids -join "`n")) { throw 'gate-platform-request-order' }
        foreach ($case in $catalog.cases) {
            if (-not $globalIds.Add([string]$case.canonicalId)) { throw 'gate-platform-ambiguous-canonical-id' }
        }
        foreach ($name in @('discover','catalog','plan')) {
            $matching = @($platform.phases | Where-Object name -CEQ ($name + '-' + $key))
            if ($matching.Count -ne 1) { throw 'gate-platform-missing-required-phase' }
        }
        $discovery = @($platform.phases | Where-Object name -CEQ ('discover-' + $key))[0]
        $dump = Join-Path $platformRoot ($discovery.directory + '/discovery.dump')
        $migrationDiscoveries.Add($dump)
        $dumpDigest = (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash.ToLowerInvariant()
        $dumpRelative = [IO.Path]::GetRelativePath($platformRoot, $dump).Replace('\', '/')
        [void](Resolve-GateArtifact $platformRoot $dumpRelative $dumpDigest)
        $artifacts.Add(@{ path = 'platform/' + $dumpRelative; sha256 = $dumpDigest })
        $verifiedCatalogPath = Join-Path $runRoot ('framework-catalog-' + $key + '.json')
        Invoke-GatePhase ('framework-catalog-' + $key) 'dotnet' @($tool, 'evidence-catalog', '--discovery', $dump, '--output', $verifiedCatalogPath) 120000
        $verifiedCatalog = [IO.File]::ReadAllText($verifiedCatalogPath) | ConvertFrom-Json
        Assert-GateCaseSet @($verifiedCatalog.cases | ForEach-Object canonicalId) $ids
        $currentNames = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
        foreach ($case in $catalog.cases) {
            $other = @($verifiedCatalog.cases | Where-Object canonicalId -CEQ $case.canonicalId)
            if ($other.Count -ne 1 -or $other[0].fullName -cne $case.fullName -or
                $other[0].className -cne $case.className -or $other[0].methodName -cne $case.methodName) { throw 'gate-platform-discovery-mismatch' }
            $currentNames.Add([string]$case.canonicalId, [string]$other[0].fullName)
        }
        $expectedSelectionPath = Join-Path $runRoot ('framework-requested-' + $key + '.json')
        [IO.File]::WriteAllText($expectedSelectionPath, (@{ schemaVersion = 1; ids = $ids;
            maximumConcurrency = [Math]::Max(1, $platform.workers) } | ConvertTo-Json -Depth 4), $encoding)
        $expectedPlanPath = Join-Path $runRoot ('framework-plan-' + $key + '.json')
        Invoke-GatePhase ('framework-plan-' + $key) 'dotnet' @($tool, 'evidence-execution-plan', '--discovery', $dump,
            '--selection', $expectedSelectionPath, '--output', $expectedPlanPath) 120000
        & (Join-Path $snapshotRoot 'tools/Assert-FrameworkExecutionPlan.ps1') -PlanPath $paths.executionPlan -ExpectedPlanPath $expectedPlanPath `
            -RawDiscoverySha256 $dumpDigest -RequestedIds $ids -SelectionIds ([string[]]$selection.ids) -PhaseRecords ([object[]]$assembly.executionPhases)
        $passed = [Collections.Generic.List[string]]::new()
        foreach ($group in $assembly.executionPhases) {
            $prefix = $key + '/execution/' + $group.id + '/'
            if ($group.runtimeEvidence -cne ($prefix + 'runtime-evidence')) { throw 'gate-platform-phase-directory' }
            $phasePaths = @{}
            foreach ($name in @('selection','request','runSettings','terminal','resultXml','trx','supervisor')) {
                if ($group.$name -isnot [string] -or -not $group.$name.StartsWith($prefix, [StringComparison]::Ordinal)) { throw 'gate-platform-phase-directory' }
                $relative = [string]$group.$name; $digest = [string]$group.($name + 'Sha256')
                $phasePaths[$name] = Resolve-GateArtifact $platformRoot $relative $digest
                $artifacts.Add(@{ path = 'platform/' + $relative; sha256 = $digest })
            }
            foreach ($name in @('execute','verify')) {
                if (@($platform.phases | Where-Object name -CEQ ($name + '-' + $key + '-' + $group.id)).Count -ne 1) {
                    throw 'gate-platform-missing-execution-phase'
                }
            }
            $phaseSelection = [IO.File]::ReadAllText($phasePaths.selection) | ConvertFrom-Json
            $terminal = [IO.File]::ReadAllText($phasePaths.terminal) | ConvertFrom-Json
            $phaseIds = [string[]]$group.selectedIds
            $phaseRequest = [IO.File]::ReadAllText($phasePaths.request) | ConvertFrom-Json
            if ($phaseRequest.cases -isnot [Array] -or $phaseRequest.cases.Count -ne $phaseIds.Count) {
                throw 'gate-platform-phase-request-binding'
            }
            for ($index = 0; $index -lt $phaseIds.Count; $index++) {
                $binding = $phaseRequest.cases[$index]
                if (-not $currentNames.ContainsKey($phaseIds[$index]) -or
                    $binding.canonicalId -isnot [string] -or $binding.fullName -isnot [string] -or
                    -not $binding.canonicalId.Equals($phaseIds[$index], [StringComparison]::Ordinal) -or
                    -not $binding.fullName.Equals($currentNames[$phaseIds[$index]], [StringComparison]::Ordinal)) {
                    throw 'gate-platform-phase-request-binding'
                }
            }
            if ($phaseSelection.schemaVersion -ne 1 -or $terminal.schemaVersion -ne 1 -or $terminal.kind -cne 'nunit-terminal' -or
                -not $terminal.complete -or $terminal.passedCount -ne $phaseIds.Count) { throw 'gate-platform-invalid-phase-terminal' }
            Assert-GateCaseSet @($phaseSelection.ids) $phaseIds
            if ((@($phaseSelection.ids) -join "`n") -cne ($phaseIds -join "`n")) { throw 'gate-platform-phase-selection-order' }
            Assert-GateCaseSet @($terminal.cases | ForEach-Object canonicalId) $phaseIds
            & (Join-Path $snapshotRoot 'tools/Assert-FrameworkSupervisor.ps1') -Path $phasePaths.supervisor -RunId $platform.runId `
                -SourceTree $platform.source.tree -SourceFingerprint $platform.source.fingerprint -AssemblySha256 $assembly.sha256 `
                -CaseIds $phaseIds -ResultXmlSha256 $group.resultXmlSha256 -TrxSha256 $group.trxSha256 `
                -ExecutionRequestPath $phasePaths.request -ExecutionRequestSha256 $group.requestSha256 `
                -ExpectedMaximumConcurrency $group.maximumConcurrency -ExpectedRequireOrder $group.requireOrder
            $verifiedPath = Join-Path $runRoot ('framework-terminal-' + $key + '-' + $group.id + '.json')
            Invoke-GatePhase ('framework-verify-' + $key + '-' + $group.id) 'dotnet' @($tool, 'evidence-verify', '--catalog', $verifiedCatalogPath,
                '--selection', $phasePaths.selection, '--result', $phasePaths.resultXml, '--trx', $phasePaths.trx, '--output', $verifiedPath) 120000
            $verified = [IO.File]::ReadAllText($verifiedPath) | ConvertFrom-Json
            if ($verified.schemaVersion -ne 1 -or $verified.kind -cne 'nunit-terminal' -or -not $verified.complete -or
                $verified.passedCount -ne $phaseIds.Count) { throw 'gate-platform-unverified-phase-results' }
            Assert-GateCaseSet @($verified.cases | ForEach-Object canonicalId) $phaseIds
            $markerRequired = $key -ceq 'plugin' -and @($catalog.cases | Where-Object {
                $_.className -ceq 'NUnitCompatibility.CompatibilityCases' -and $_.canonicalId -cin $phaseIds
            }).Count -gt 0
            if ($group.markerRequired -ne $markerRequired -or ($markerRequired -and $group.markerObserved -ne $true)) {
                throw 'gate-platform-execution-marker'
            }
            if ($markerRequired) {
                $markers = @(Get-ChildItem -LiteralPath (Join-Path $platformRoot $group.runtimeEvidence) -File -Filter 'fixture-created-*.txt')
                if ($markers.Count -ne 1 -or $markers[0].Name -cnotmatch '^fixture-created-[1-9][0-9]*\.txt$' -or
                    [IO.File]::ReadAllText($markers[0].FullName) -cne 'constructed') { throw 'gate-platform-execution-marker' }
                $relative = $group.runtimeEvidence + '/' + $markers[0].Name
                $digest = (Get-FileHash -LiteralPath $markers[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                [void](Resolve-GateArtifact $platformRoot $relative $digest)
                $artifacts.Add(@{ path = 'platform/' + $relative; sha256 = $digest })
            }
            foreach ($id in $phaseIds) { $passed.Add($id) }
            $artifacts.Add(@{ path = [IO.Path]::GetRelativePath($runRoot, $verifiedPath).Replace('\', '/');
                sha256 = (Get-FileHash -LiteralPath $verifiedPath -Algorithm SHA256).Hash.ToLowerInvariant() })
        }
        Assert-GateCaseSet $passed.ToArray() $ids
        foreach ($name in @($verifiedCatalogPath, $expectedSelectionPath, $expectedPlanPath)) {
            $artifacts.Add(@{ path = [IO.Path]::GetRelativePath($runRoot, $name).Replace('\', '/');
                sha256 = (Get-FileHash -LiteralPath $name -Algorithm SHA256).Hash.ToLowerInvariant() })
        }
    }
    # A retired legacy descriptor must have an independently discovered NUnit
    # successor. The pinned historical bytes cannot be rewritten to hide a loss.
    # This is migration identity conservation, not a new test registry or proof
    # that a moved assertion body retained its meaning.
    $stage = 'verify-migration-conservation'
    $migrationInputs = Join-Path $runRoot 'migration-discoveries.json'
    [IO.File]::WriteAllText($migrationInputs, (@{ schemaVersion = 1; paths = $migrationDiscoveries.ToArray() } |
        ConvertTo-Json -Depth 4), $encoding)
    $migrationOutput = Join-Path $runRoot 'migration-conservation.json'
    Invoke-GatePhase 'migration-conservation' 'dotnet' @($tool, 'evidence-migration',
        '--reference', (Join-Path $snapshotRoot 'tests/Migration/legacy-catalog.json'),
        '--legacy', $verifiedLegacyCatalog, '--discoveries', $migrationInputs,
        '--migrations', (Join-Path $snapshotRoot 'tests/Migration/migrated-cases.json'), '--output', $migrationOutput) 120000
    $migrationEvidence = [IO.File]::ReadAllText($migrationOutput) | ConvertFrom-Json
    if ($migrationEvidence.schemaVersion -ne 1 -or $migrationEvidence.kind -cne 'migration-conservation' -or
        -not $migrationEvidence.complete -or $migrationEvidence.baselineClaim -ne $false -or
        $migrationEvidence.legacyCount -ne @($migrationEvidence.evidence.checkedOwners | Where-Object owner -EQ 0).Count) {
        throw 'gate-migration-conservation-invalid'
    }
    foreach ($name in @($verifiedLegacyCatalog, $migrationInputs, $migrationOutput)) {
        $artifacts.Add(@{ path = [IO.Path]::GetRelativePath($runRoot, $name).Replace('\', '/');
            sha256 = (Get-FileHash -LiteralPath $name -Algorithm SHA256).Hash.ToLowerInvariant() })
    }
    foreach ($artifact in $artifacts) { [void](Resolve-GateArtifact $runRoot $artifact.path $artifact.sha256) }
    $manifest.platformArtifacts = $artifacts.ToArray()
    $manifest.platformVerified = $true
    $manifest['frameworkCaseCount'] = $globalIds.Count

    $session=Read-HybridJson (Join-Path $SessionDirectory 'manifest.json')
    $inventoryPath=Join-Path $OutputDirectory 'slot-0001-generation-inventory.json'
    Write-HybridJson $inventoryPath $inventory
    Assert-HybridLegacyInventory $SessionDirectory $inventory
    $pins=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($artifact in $artifacts){
        $path=Join-Path $runRoot $artifact.path
        $pins[$path]=$artifact.sha256
    }
    foreach($parent in @($SessionDirectory,$OutputDirectory)){
        foreach($file in Get-ChildItem -LiteralPath $parent -Recurse -File){$pins[$file.FullName]=Get-HybridDigest $file.FullName}
    }
    foreach($name in @('regression.json','snapshot.json')){$path=Join-Path $runRoot $name;$pins[$path]=Get-HybridDigest $path}
    # Keep every raw parser/admission artifact, including actual request and
    # supervisor inputs, through the installed protocol-3 final guard.
    $owners=@(
        @{slotId='slot-0001';owner='legacy';report=$inventory.report;reportRunId=$inventory.reportRunId;reportGeneration=$inventory.reportGeneration},
        @{slotId='slot-0002';owner='framework';runId=$platform.runId})
    Write-HybridJson (Join-Path $OutputDirectory 'admission.json') @{schemaVersion=1;kind='hybrid-session-admission';complete=$true;baselineClaim=$false;syntheticBoundaryControl=$true
        runId=$session.runId;source=$platform.source;legacySourceTree=$tree;owners=$owners
        generationInventories=@(@{slotId='slot-0001';path='slot-0001-generation-inventory.json';sha256=(Get-HybridDigest $inventoryPath)})
        receiptArtifacts=@($pins.GetEnumerator()|Sort-Object Key|ForEach-Object{@{path=$_.Key;sha256=$_.Value}})}
} finally {
    if($null -ne $legacyCatalogGuard){$legacyCatalogGuard.Dispose()}
    Close-HybridLocks
}
exit 0
