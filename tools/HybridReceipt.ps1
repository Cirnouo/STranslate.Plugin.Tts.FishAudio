#requires -Version 7.0
# Static preparation-time extraction of the read-only receipt functions from
# HybridSession.Common.ps1. No process runner, build or snapshot lookup belongs
# to this installed final-guard module.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:HybridLocks = [Collections.Generic.List[IO.FileStream]]::new()
$script:HybridArtifacts = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
$script:failureCode = 'hybrid-receipt-rejected'

function Require-Hybrid([bool]$Condition, [string]$Code) {
    if (-not $Condition) { $script:failureCode = $Code; throw $Code }
}

function Get-HybridDigest([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

function Test-HybridSamePath([string]$Left, [string]$Right) {
    [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Left)).Equals(
        [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Right)), [StringComparison]::OrdinalIgnoreCase)
}

function Test-HybridContained([string]$Path, [string]$Parent) {
    [IO.Path]::GetFullPath($Path).StartsWith([IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Parent)) +
        [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-HybridRegular([string]$Path) {
    Require-Hybrid ([IO.Path]::IsPathFullyQualified($Path)) 'hybrid-path-not-absolute'
    $file = [IO.FileInfo]::new($Path)
    Require-Hybrid ($file.Exists -and ($file.Attributes -band
        ([IO.FileAttributes]::Directory -bor [IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Device)) -eq 0) 'hybrid-file-not-regular'
    for ($parent = $file.Directory; $null -ne $parent; $parent = $parent.Parent) {
        Require-Hybrid ($parent.Exists -and ($parent.Attributes -band
            ([IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Device)) -eq 0) 'hybrid-parent-not-regular'
    }
}

function Lock-HybridFile([string]$Path, [string]$Digest = '', [switch]$Retained) {
    Assert-HybridRegular $Path
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $script:HybridLocks.Add($stream)
    Assert-HybridRegular $Path
    $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()
    Require-Hybrid ([string]::IsNullOrEmpty($Digest) -or ($Digest -cmatch '^[a-f0-9]{64}$' -and $actual -ceq $Digest)) 'hybrid-artifact-digest'
    if ($Retained) {
        if ($script:HybridArtifacts.ContainsKey($Path)) { Require-Hybrid ($script:HybridArtifacts[$Path] -ceq $actual) 'hybrid-artifact-changed' }
        else { $script:HybridArtifacts.Add($Path, $actual) }
    }
    return $Path
}

function Resolve-HybridArtifact([string]$Parent, [string]$Relative, [string]$Digest) {
    Require-Hybrid (-not [string]::IsNullOrWhiteSpace($Relative) -and -not [IO.Path]::IsPathRooted($Relative) -and $Digest -cmatch '^[a-f0-9]{64}$') 'hybrid-artifact-path'
    $path = [IO.Path]::GetFullPath((Join-Path $Parent $Relative))
    Require-Hybrid (Test-HybridContained $path $Parent) 'hybrid-artifact-escape'
    return Lock-HybridFile $path $Digest -Retained
}

function Assert-HybridJsonElement([Text.Json.JsonElement]$Element) {
    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            Require-Hybrid ($seen.Add($property.Name)) 'hybrid-json-duplicate-property'
            Assert-HybridJsonElement $property.Value
        }
    } elseif ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Element.EnumerateArray()) { Assert-HybridJsonElement $item }
    }
}

function Read-HybridJson([string]$Path) {
    Assert-HybridRegular $Path
    Require-Hybrid (([IO.FileInfo]::new($Path)).Length -gt 0 -and ([IO.FileInfo]::new($Path)).Length -le 67108864) 'hybrid-json-size'
    $stream = [IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $script:HybridLocks.Add($stream)
    Require-Hybrid ($stream.Length -gt 0 -and $stream.Length -le 67108864) 'hybrid-json-size'
    $document = [Text.Json.JsonDocument]::Parse($stream)
    try {
        Assert-HybridJsonElement $document.RootElement
        return ($document.RootElement.GetRawText() | ConvertFrom-Json -Depth 64)
    } finally { $document.Dispose() }
}

function Assert-HybridIds($Actual, [string[]]$Expected, [switch]$Ordered, [switch]$AllowEmpty) {
    Require-Hybrid ($Actual -is [Array] -and ($AllowEmpty -or $Actual.Count -gt 0)) 'hybrid-case-set-shape'
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($id in $Actual) {
        Require-Hybrid ($id -is [string] -and -not [string]::IsNullOrWhiteSpace($id) -and $seen.Add($id)) 'hybrid-case-set-duplicate'
    }
    Require-Hybrid ($seen.Count -eq $Expected.Count -and $seen.SetEquals($Expected)) 'hybrid-case-set-mismatch'
    if ($Ordered) {
        for ($index = 0; $index -lt $Expected.Count; $index++) { Require-Hybrid ($Actual[$index] -ceq $Expected[$index]) 'hybrid-case-order' }
    }
}

function Read-HybridLatestAttempt([string]$SlotDirectory, $Records) {
    $physical = @(Get-ChildItem -LiteralPath $SlotDirectory -Directory -Force | Where-Object Name -CMatch '^attempt-[0-9]{4}$' | Sort-Object Name)
    Require-Hybrid ($Records -is [Array] -and $Records.Count -gt 0 -and $physical.Count -eq $Records.Count -and
        @(Get-ChildItem -LiteralPath $SlotDirectory -Directory -Force).Count -eq $physical.Count) 'hybrid-attempt-set'
    for ($index = 0; $index -lt $Records.Count; $index++) {
        Require-Hybrid ($physical[$index].Name -ceq ('attempt-{0:D4}' -f ($index+1)) -and $Records[$index].ordinal -eq ($index+1)) 'hybrid-attempt-order'
        $path = Join-Path $physical[$index].FullName 'attempt.json'
        Require-Hybrid ($Records[$index].sha256 -cmatch '^[a-f0-9]{64}$') 'hybrid-attempt-digest'
        [void](Lock-HybridFile $path ([string]$Records[$index].sha256) -Retained)
        $record = Read-HybridJson $path
        Require-Hybrid ($record.ordinal -eq ($index+1)) 'hybrid-attempt-identity'
    }
    Require-Hybrid ($record.complete -eq $true -and $record.exitCode -eq 0) 'hybrid-latest-attempt-failed-or-incomplete'
    return [pscustomobject]@{ directory = $physical[-1].FullName; record = $record }
}

function Close-HybridLocks {
    foreach ($stream in $script:HybridLocks) { $stream.Dispose() }
    $script:HybridLocks.Clear()
}

function Read-HybridLegacyInventory([string]$ReportPath,[string]$EvidenceRoot) {
    Require-Hybrid (Test-HybridContained $ReportPath $EvidenceRoot) 'hybrid-generation-report-escape'
    [void](Lock-HybridFile $ReportPath -Retained)
    $canonical = Read-HybridJson $ReportPath
    Require-Hybrid ($canonical.schemaVersion -eq 1 -and $canonical.runId -cmatch '^[a-f0-9]{32}$' -and
        ($canonical.generation -is [int] -or $canonical.generation -is [long]) -and $canonical.generation -ge 0) 'hybrid-generation-canonical-identity'
    $generationRoot = $ReportPath+'.generations'
    $directories = @(Get-ChildItem -LiteralPath $generationRoot -Directory -Force)
    Require-Hybrid ($directories.Count -eq 1 -and $directories[0].Name -ceq $canonical.runId) 'hybrid-generation-owner-set'
    # Assert regular parent chains before enumerating or opening sidecars.
    $initialPath = Join-Path $directories[0].FullName '00000000000000000000.json'
    Assert-HybridRegular $initialPath
    $committed = @(Get-ChildItem -LiteralPath $directories[0].FullName -File -Force -Filter '*.json' | Sort-Object Name -CaseSensitive)
    Require-Hybrid ($committed.Count -gt 0 -and $committed.Count -le 2000000) 'hybrid-generation-empty-or-limit'
    $entries = [Collections.Generic.List[object]]::new()
    $paths = @($ReportPath)+@($committed | ForEach-Object FullName)
    foreach ($path in $paths) {
        [void](Lock-HybridFile $path -Retained)
        $length = ([IO.FileInfo]::new($path)).Length
        Require-Hybrid ($length -gt 0 -and $length -le 16777216) 'hybrid-generation-file-size'
        $entries.Add([pscustomobject]@{ path = [IO.Path]::GetRelativePath($EvidenceRoot,$path).Replace('\','/'); sha256 = Get-HybridDigest $path; length = $length })
    }
    $latest = [long]-1; $projection = $null; $hasInitial = $false
    for ($index = 0; $index -lt $committed.Count; $index++) {
        $file = $committed[$index]
        $number = [long]0
        Require-Hybrid ($file.Name -cmatch '^[0-9]{20}\.json$' -and
            [long]::TryParse($file.BaseName,[Globalization.NumberStyles]::None,[Globalization.CultureInfo]::InvariantCulture,[ref]$number) -and
            $number -ge 0 -and $file.BaseName -ceq $number.ToString('D20',[Globalization.CultureInfo]::InvariantCulture) -and $number -gt $latest) 'hybrid-generation-name'
        $record = Read-HybridJson $file.FullName
        Require-Hybrid ($record.schemaVersion -eq 1 -and $record.runId -ceq $canonical.runId -and $record.generation -eq $number) 'hybrid-generation-record-identity'
        if ($number -eq 0) { $hasInitial = $true }
        if ($number -eq $canonical.generation) { $projection = $entries[$index+1] }
        $latest = $number
    }
    Require-Hybrid ($hasInitial -and $null -ne $projection -and $entries[0].length -eq $projection.length -and
        $entries[0].sha256 -ceq $projection.sha256) 'hybrid-generation-projection-mismatch'
    return [pscustomobject]@{ schemaVersion = 1; kind = 'hybrid-legacy-generation-inventory'
        report = $entries[0].path; generationsDirectory = [IO.Path]::GetRelativePath($EvidenceRoot,$generationRoot).Replace('\','/')
        reportRunId = $canonical.runId; canonicalGeneration = [long]$canonical.generation; reportGeneration = $latest
        entries = $entries.ToArray() }
}

function Assert-HybridLegacyInventory([string]$EvidenceRoot,$Inventory) {
    $fields = @('schemaVersion','kind','report','generationsDirectory','reportRunId','canonicalGeneration','reportGeneration','entries')
    Require-Hybrid ($Inventory.schemaVersion -eq 1 -and $Inventory.kind -ceq 'hybrid-legacy-generation-inventory' -and
        @($Inventory.PSObject.Properties).Count -eq $fields.Count -and
        @($Inventory.PSObject.Properties | Where-Object { $fields -cnotcontains $_.Name }).Count -eq 0 -and
        $Inventory.report -is [string] -and -not [IO.Path]::IsPathRooted($Inventory.report) -and
        $Inventory.reportRunId -cmatch '^[a-f0-9]{32}$' -and
        ($Inventory.canonicalGeneration -is [int] -or $Inventory.canonicalGeneration -is [long]) -and $Inventory.canonicalGeneration -ge 0 -and
        ($Inventory.reportGeneration -is [int] -or $Inventory.reportGeneration -is [long]) -and $Inventory.reportGeneration -ge $Inventory.canonicalGeneration -and
        $Inventory.entries -is [Array] -and $Inventory.entries.Count -gt 1) 'hybrid-generation-inventory-shape'
    $report = [IO.Path]::GetFullPath((Join-Path $EvidenceRoot $Inventory.report))
    Require-Hybrid (Test-HybridContained $report $EvidenceRoot) 'hybrid-generation-report-escape'
    $current = Read-HybridLegacyInventory $report $EvidenceRoot
    foreach ($name in @('report','generationsDirectory','reportRunId','canonicalGeneration','reportGeneration')) {
        Require-Hybrid ($current.$name -ceq $Inventory.$name) 'hybrid-generation-inventory-changed'
    }
    Require-Hybrid ($current.entries.Count -eq $Inventory.entries.Count) 'hybrid-generation-inventory-changed'
    for ($index=0;$index -lt $current.entries.Count;$index++) {
        $expected = $Inventory.entries[$index]; $actual = $current.entries[$index]
        Require-Hybrid (@($expected.PSObject.Properties).Count -eq 3 -and
            @($expected.PSObject.Properties | Where-Object { @('path','sha256','length') -cnotcontains $_.Name }).Count -eq 0 -and
            $expected.path -is [string] -and $expected.sha256 -cmatch '^[a-f0-9]{64}$' -and
            ($expected.length -is [long] -or $expected.length -is [int]) -and $expected.length -gt 0 -and
            $actual.path -ceq $expected.path -and $actual.sha256 -ceq $expected.sha256 -and $actual.length -eq $expected.length) 'hybrid-generation-inventory-changed'
    }
}

function Assert-HybridLegacyExport($Verified,$Inventory) {
    Require-Hybrid ($Verified.reportRunId -ceq $Inventory.reportRunId -and
        ($Verified.reportGeneration -is [long] -or $Verified.reportGeneration -is [int]) -and
        $Verified.reportGeneration -eq $Inventory.reportGeneration) 'hybrid-generation-export-mismatch'
}

function Assert-HybridGenerationReceipt([string]$SessionDirectory,[string]$AdmissionDirectory,$Admission) {
    # Gate must first lock admission.json at its receipt digest. This function
    # rechecks committed generation sets, not just already enumerated bytes.
    [void](Lock-HybridFile (Join-Path $SessionDirectory 'manifest.json') -Retained)
    $session = Read-HybridJson (Join-Path $SessionDirectory 'manifest.json')
    Require-Hybrid ($Admission.schemaVersion -eq 1 -and $Admission.kind -ceq 'hybrid-session-admission' -and
        $Admission.complete -eq $true -and $Admission.baselineClaim -eq $false -and
        $session.schemaVersion -eq 1 -and $session.kind -ceq 'hybrid-test-session' -and
        $session.complete -eq $true -and $session.runId -ceq $Admission.runId) 'hybrid-generation-receipt-session'
    foreach ($slot in $session.slots) {
        [void](Read-HybridLatestAttempt (Join-Path $SessionDirectory ('slots/'+$slot.id)) $slot.attempts)
    }
    $legacy = @($Admission.owners | Where-Object owner -CEQ 'legacy')
    $inventories = @($Admission.generationInventories)
    Require-Hybrid ($inventories.Count -eq $legacy.Count) 'hybrid-generation-receipt-owner-set'
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($item in $inventories) {
        $owner = @($legacy | Where-Object slotId -CEQ $item.slotId)
        Require-Hybrid (@($item.PSObject.Properties).Count -eq 3 -and
            @($item.PSObject.Properties | Where-Object { @('slotId','path','sha256') -cnotcontains $_.Name }).Count -eq 0 -and
            $seen.Add([string]$item.slotId) -and $owner.Count -eq 1) 'hybrid-generation-receipt-owner-set'
        $path = Resolve-HybridArtifact $AdmissionDirectory $item.path $item.sha256
        $inventory = Read-HybridJson $path
        Require-Hybrid ($owner[0].report -ceq $inventory.report) 'hybrid-generation-receipt-report-binding'
        Assert-HybridLegacyExport $owner[0] $inventory
        Assert-HybridLegacyInventory $SessionDirectory $inventory
    }
}

function Assert-GateReceiptPath([string]$Root,[string]$Relative) {
    Require-Hybrid (-not [string]::IsNullOrWhiteSpace($Relative) -and -not [IO.Path]::IsPathRooted($Relative) -and
        -not $Relative.Contains('\') -and @($Relative.Split('/') | Where-Object { $_ -cin @('','.','..') }).Count -eq 0) 'gate-receipt-path'
    $path = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    Require-Hybrid (Test-HybridContained $path $Root) 'gate-receipt-path'
    return $path
}
function Assert-GateHybridReceipt([string]$EvidenceRoot,$Gate) {
    Require-Hybrid ($Gate.schemaVersion -eq 1 -and $Gate.gateProtocolVersion -eq 3 -and
        $Gate.mode -ceq 'bootstrap/full/hybrid' -and $Gate.hybridVerified -eq $true -and
        $Gate.complete -eq $true -and $Gate.exitCode -eq 0 -and $Gate.hybridArtifacts -is [Array] -and
        $Gate.hybridArtifacts.Count -gt 0) 'gate-hybrid-receipt-envelope'
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($artifact in $Gate.hybridArtifacts) {
        Require-Hybrid ($seen.Add([string]$artifact.path)) 'gate-hybrid-receipt-duplicate-artifact'
        $path = Assert-GateReceiptPath $EvidenceRoot $artifact.path
        [void](Lock-HybridFile $path ([string]$artifact.sha256) -Retained)
    }
    $sessionRoot = Assert-GateReceiptPath $EvidenceRoot $Gate.hybrid.session
    $admissionPath = Assert-GateReceiptPath $EvidenceRoot $Gate.hybrid.admission
    Require-Hybrid ($seen.Contains($Gate.hybrid.admission)) 'gate-hybrid-admission-not-receipted'
    [void](Lock-HybridFile $admissionPath ([string]$Gate.hybrid.admissionSha256) -Retained)
    $admission = Read-HybridJson $admissionPath
    $contextPath = Assert-GateReceiptPath $EvidenceRoot 'snapshot.json'
    Require-Hybrid ($seen.Contains('snapshot.json') -and $admission.source.snapshot -eq $true -and
        $admission.source.snapshotRootValidated -eq $true -and $admission.source.tree -ceq $Gate.snapshotTree -and
        $admission.source.sourceCommit -ceq $Gate.sourceCommit -and $admission.legacySourceTree -ceq $Gate.snapshotTree -and
        $admission.source.contextSha256 -ceq (Get-HybridDigest $contextPath)) 'gate-hybrid-receipt-source'
    $session = Read-HybridJson (Join-Path $sessionRoot 'manifest.json')
    Require-Hybrid ($session.runId -ceq $admission.runId -and $session.slots -is [Array] -and
        @($admission.owners).Count -eq $session.slots.Count) 'gate-hybrid-receipt-owner-set'
    $physicalSlots = @(Get-ChildItem -LiteralPath (Join-Path $sessionRoot 'slots') -Directory -Force)
    Require-Hybrid ($physicalSlots.Count -eq $session.slots.Count) 'gate-hybrid-receipt-slot-set'
    $ownerIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($slot in $session.slots) {
        $owner = @($admission.owners | Where-Object slotId -CEQ $slot.id)
        Require-Hybrid ($ownerIds.Add([string]$slot.id) -and $owner.Count -eq 1 -and $owner[0].owner -ceq $slot.owner -and
            @($physicalSlots | Where-Object Name -CEQ $slot.id).Count -eq 1) 'gate-hybrid-receipt-owner-set'
    }
    # This re-enumerates actual attempt directories and committed generations;
    # hashing only the old artifact list cannot establish current admission.
    Assert-HybridGenerationReceipt $sessionRoot ([IO.Path]::GetDirectoryName($admissionPath)) $admission
}
