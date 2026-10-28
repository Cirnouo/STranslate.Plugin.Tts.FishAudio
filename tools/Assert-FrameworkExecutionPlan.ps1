#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PlanPath,
    [Parameter(Mandatory)][string]$ExpectedPlanPath,
    [Parameter(Mandatory)][string]$RawDiscoverySha256,
    [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$RequestedIds,
    [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$SelectionIds,
    [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$PhaseRecords
)

# Plan/group admission only. The caller owns the independently rebuilt expected
# plan, source/build/artifact hashes and each phase's raw terminal/supervisor
# admission. Matching these structures proves neither execution nor a baseline.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require-Plan([bool]$Condition) {
    if (-not $Condition) { throw 'framework-execution-plan-rejected' }
}
function Assert-PlanText($Value) {
    Require-Plan ($Value -is [string] -and -not [string]::IsNullOrWhiteSpace($Value) -and
        $Value.Length -le 16384 -and $Value -cnotmatch '\p{Cc}')
}
function Assert-PlanName($Value) {
    Require-Plan ($Value -is [string] -and $Value.Length -le 128 -and
        $Value -cmatch '^[a-z0-9][a-z0-9._-]*$')
}
function Assert-PlanInteger($Value, [long]$Minimum, [long]$Maximum) {
    Require-Plan (($Value -is [int] -or $Value -is [long]) -and $Value -ge $Minimum -and $Value -le $Maximum)
}
function Assert-PlanStrings($Values, [bool]$AllowEmpty, [bool]$SafeNames = $false) {
    Require-Plan ($Values -is [Array] -and $Values.Rank -eq 1 -and
        ($AllowEmpty -or $Values.Count -gt 0) -and $Values.Count -le 2000000)
    $unique = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $previous = $null
    foreach ($value in $Values) {
        Assert-PlanText $value
        if ($SafeNames) {
            Assert-PlanName $value
            if ($null -ne $previous) { Require-Plan ([StringComparer]::Ordinal.Compare($previous, $value) -lt 0) }
            $previous = $value
        }
        Require-Plan ($unique.Add($value))
    }
}
function Assert-PlanStringSequence($Actual, $Expected) {
    Require-Plan ($Actual.Count -eq $Expected.Count)
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        Require-Plan ($Actual[$index].Equals($Expected[$index], [StringComparison]::Ordinal))
    }
}
function Get-PlanJsonFields([Text.Json.JsonElement]$Element, [string[]]$Names) {
    Require-Plan ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object)
    $fields = [Collections.Generic.Dictionary[string,Text.Json.JsonElement]]::new([StringComparer]::Ordinal)
    foreach ($property in $Element.EnumerateObject()) {
        Require-Plan ($fields.Count -lt $Names.Count -and $Names -ccontains $property.Name -and
            $fields.TryAdd($property.Name, $property.Value))
    }
    Require-Plan ($fields.Count -eq $Names.Count)
    return ,$fields
}
function Get-PlanJsonText([Text.Json.JsonElement]$Element) {
    Require-Plan ($Element.ValueKind -eq [Text.Json.JsonValueKind]::String)
    $value = $Element.GetString()
    Assert-PlanText $value
    return $value
}
function Get-PlanJsonInteger([Text.Json.JsonElement]$Element, [long]$Minimum, [long]$Maximum) {
    $value = [long]0
    Require-Plan ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Number -and
        $Element.TryGetInt64([ref]$value) -and $value -ge $Minimum -and $value -le $Maximum)
    return $value
}
function Get-PlanJsonBoolean([Text.Json.JsonElement]$Element) {
    Require-Plan ($Element.ValueKind -eq [Text.Json.JsonValueKind]::True -or
        $Element.ValueKind -eq [Text.Json.JsonValueKind]::False)
    return $Element.GetBoolean()
}
function Get-PlanJsonProfile([Text.Json.JsonElement]$Element) {
    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Null) { return $null }
    $value = Get-PlanJsonText $Element
    Assert-PlanName $value
    return $value
}
function Get-PlanJsonStrings([Text.Json.JsonElement]$Element, [bool]$AllowEmpty, [bool]$SafeNames = $false) {
    Require-Plan ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array -and $Element.GetArrayLength() -le 2000000)
    $values = [Collections.Generic.List[string]]::new()
    foreach ($item in $Element.EnumerateArray()) { $values.Add((Get-PlanJsonText $item)) }
    $result = $values.ToArray()
    Assert-PlanStrings $result $AllowEmpty $SafeNames
    return ,$result
}
function Assert-PlanRegularPath([string]$Path) {
    Require-Plan ([IO.Path]::IsPathFullyQualified($Path) -and
        -not $Path.StartsWith('\\?\', [StringComparison]::Ordinal) -and
        -not $Path.StartsWith('\\.\', [StringComparison]::Ordinal))
    $full = [IO.Path]::GetFullPath($Path)
    # Reject alternate data streams as well as filesystem device paths.
    Require-Plan ($full.IndexOf(':', 2) -lt 0)
    $file = Get-Item -LiteralPath $full -Force
    Require-Plan ($file -is [IO.FileInfo] -and
        ($file.Attributes -band ([IO.FileAttributes]::Directory -bor [IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Device)) -eq 0)
    $directory = $file.Directory
    while ($null -ne $directory) {
        Require-Plan ($directory.Exists -and
            ($directory.Attributes -band ([IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Device)) -eq 0)
        $directory = $directory.Parent
    }
    return $full
}
function Read-PlanDocument([Text.Json.JsonElement]$Element) {
    $root = Get-PlanJsonFields $Element @('schemaVersion','kind','complete','plan')
    Require-Plan ((Get-PlanJsonInteger $root['schemaVersion'] 1 1) -eq 1 -and
        (Get-PlanJsonText $root['kind']) -ceq 'nunit-execution-plan' -and
        (Get-PlanJsonBoolean $root['complete']))
    $plan = Get-PlanJsonFields $root['plan'] @('rawDiscoverySha256','requestedIds','expandedIds','expansionReasons','phases')
    $rawHash = Get-PlanJsonText $plan['rawDiscoverySha256']
    Require-Plan ($rawHash -cmatch '^[a-f0-9]{64}$')
    $requested = Get-PlanJsonStrings $plan['requestedIds'] $false
    $expanded = Get-PlanJsonStrings $plan['expandedIds'] $false
    Require-Plan ($requested.Count -le 100000 -and $expanded.Count -le 100000)
    $requestedSet = [Collections.Generic.HashSet[string]]::new($requested, [StringComparer]::Ordinal)
    $expandedSet = [Collections.Generic.HashSet[string]]::new($expanded, [StringComparer]::Ordinal)
    Require-Plan ($expandedSet.IsSupersetOf($requestedSet))
    $work = [long]$requested.Count + $expanded.Count
    $reasonsElement = $plan['expansionReasons']
    Require-Plan ($reasonsElement.ValueKind -eq [Text.Json.JsonValueKind]::Array -and $reasonsElement.GetArrayLength() -le 100000)
    $reasons = [Collections.Generic.List[object]]::new()
    $reasonProfiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($item in $reasonsElement.EnumerateArray()) {
        $fields = Get-PlanJsonFields $item @('code','profile','selectedIds','addedIds')
        $code = Get-PlanJsonText $fields['code']
        $profile = Get-PlanJsonText $fields['profile']
        Assert-PlanName $profile
        Require-Plan ($code -ceq 'complete-profile' -and $reasonProfiles.Add($profile))
        $selected = Get-PlanJsonStrings $fields['selectedIds'] $false
        $added = Get-PlanJsonStrings $fields['addedIds'] $false
        $work += $selected.Count + $added.Count
        Require-Plan ($work -le 2000000 -and $selected.Count -le 100000 -and $added.Count -le 100000)
        foreach ($id in $selected) { Require-Plan ($requestedSet.Contains($id)) }
        foreach ($id in $added) { Require-Plan ($expandedSet.Contains($id) -and -not $requestedSet.Contains($id)) }
        $reasons.Add([pscustomobject]@{ Code = $code; Profile = $profile; SelectedIds = $selected; AddedIds = $added })
    }
    $phasesElement = $plan['phases']
    Require-Plan ($phasesElement.ValueKind -eq [Text.Json.JsonValueKind]::Array -and
        $phasesElement.GetArrayLength() -gt 0 -and $phasesElement.GetArrayLength() -le 100000)
    $phases = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $fullNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $platformIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $profiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $ordinaryCount = 0
    foreach ($item in $phasesElement.EnumerateArray()) {
        $fields = Get-PlanJsonFields $item @('id','kind','profile','cases','requireOrder','maximumConcurrency','exclusiveResources')
        $id = Get-PlanJsonText $fields['id']
        Require-Plan ($id -ceq ('phase-' + ($phases.Count + 1).ToString('D4', [Globalization.CultureInfo]::InvariantCulture)))
        $kind = Get-PlanJsonText $fields['kind']
        $profile = Get-PlanJsonProfile $fields['profile']
        $ordered = Get-PlanJsonBoolean $fields['requireOrder']
        $maximum = Get-PlanJsonInteger $fields['maximumConcurrency'] 1 4
        $resources = Get-PlanJsonStrings $fields['exclusiveResources'] $true $true
        $work += $resources.Count + 1
        Require-Plan ($work -le 2000000)
        if ($kind -ceq 'ordinary') {
            $ordinaryCount++
            Require-Plan ($ordinaryCount -eq 1 -and $null -eq $profile -and -not $ordered -and
                ($resources.Count -eq 0 -or $maximum -eq 1))
        } elseif ($kind -ceq 'fresh-case') {
            Require-Plan ($null -eq $profile -and -not $ordered -and $maximum -eq 1)
        } elseif ($kind -ceq 'ordered-profile') {
            Require-Plan ($null -ne $profile -and $ordered -and $maximum -eq 1 -and $profiles.Add($profile))
        } else { Require-Plan $false }
        $casesElement = $fields['cases']
        Require-Plan ($casesElement.ValueKind -eq [Text.Json.JsonValueKind]::Array -and
            $casesElement.GetArrayLength() -gt 0 -and $casesElement.GetArrayLength() -le 100000 -and
            ($kind -cne 'fresh-case' -or $casesElement.GetArrayLength() -eq 1))
        $cases = [Collections.Generic.List[object]]::new()
        $caseIds = [Collections.Generic.List[string]]::new()
        foreach ($leaf in $casesElement.EnumerateArray()) {
            $binding = Get-PlanJsonFields $leaf @('canonicalId','fullName','platformId','className','methodName')
            $canonical = Get-PlanJsonText $binding['canonicalId']
            $fullName = Get-PlanJsonText $binding['fullName']
            $platform = Get-PlanJsonText $binding['platformId']
            $null = Get-PlanJsonText $binding['className']
            $null = Get-PlanJsonText $binding['methodName']
            $work++
            Require-Plan ($work -le 2000000 -and $seen.Count -lt 100000 -and
                $seen.Add($canonical) -and $expandedSet.Contains($canonical) -and $fullNames.Add($fullName) -and $platformIds.Add($platform))
            $cases.Add([pscustomobject]@{ CanonicalId = $canonical; FullName = $fullName })
            $caseIds.Add($canonical)
        }
        $phases.Add([pscustomobject]@{ Id = $id; Kind = $kind; Profile = $profile; RequireOrder = $ordered;
            MaximumConcurrency = $maximum; ExclusiveResources = $resources; Cases = $cases.ToArray(); SelectedIds = $caseIds.ToArray() })
    }
    Require-Plan ($seen.SetEquals($expandedSet))
    foreach ($reason in $reasons) { Require-Plan ($profiles.Contains($reason.Profile)) }
    return [pscustomobject]@{ RawDiscoverySha256 = $rawHash; RequestedIds = $requested; ExpandedIds = $expanded;
        ExpansionReasons = $reasons.ToArray(); Phases = $phases.ToArray() }
}
function Assert-PlanProfile($Actual, $Expected) {
    if ($null -eq $Expected) { Require-Plan ($null -eq $Actual) }
    else { Assert-PlanName $Actual; Require-Plan ($Actual.Equals($Expected, [StringComparison]::Ordinal)) }
}
function Assert-PlanPhase($Actual, $Expected) {
    Assert-PlanText $Actual.Id
    Assert-PlanText $Actual.Kind
    Require-Plan ($Actual.Id.Equals($Expected.Id, [StringComparison]::Ordinal) -and
        $Actual.Kind.Equals($Expected.Kind, [StringComparison]::Ordinal) -and
        $Actual.RequireOrder -is [bool] -and $Actual.RequireOrder -eq $Expected.RequireOrder)
    Assert-PlanProfile $Actual.Profile $Expected.Profile
    Assert-PlanInteger $Actual.MaximumConcurrency 1 4
    Require-Plan ($Actual.MaximumConcurrency -eq $Expected.MaximumConcurrency)
    Assert-PlanStrings $Actual.ExclusiveResources $true $true
    Assert-PlanStringSequence $Actual.ExclusiveResources $Expected.ExclusiveResources
}
function Get-PlanRecordFields($Record) {
    $fields = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    if ($Record -is [Collections.IDictionary]) {
        Require-Plan ($Record.Count -le 64)
        foreach ($key in $Record.Keys) {
            Require-Plan ($key -is [string] -and $key.Length -gt 0 -and $key.Length -le 128 -and $fields.TryAdd($key, $Record[$key]))
        }
    } else {
        Require-Plan ($Record -is [pscustomobject])
        foreach ($property in $Record.PSObject.Properties) {
            Require-Plan ($fields.Count -lt 64 -and $property.MemberType -eq [Management.Automation.PSMemberTypes]::NoteProperty -and
                $property.Name.Length -gt 0 -and $property.Name.Length -le 128 -and $fields.TryAdd($property.Name, $property.Value))
        }
    }
    foreach ($name in @('id','kind','profile','requireOrder','maximumConcurrency','exclusiveResources','selectedIds','executed','passedCount')) {
        Require-Plan ($fields.ContainsKey($name))
    }
    # Other bounded record fields hold caller-owned artifact references; this
    # helper never consumes them as evidence of execution or raw-file admission.
    return ,$fields
}

$streams = [Collections.Generic.List[IO.FileStream]]::new()
$documents = [Collections.Generic.List[Text.Json.JsonDocument]]::new()
try {
    Require-Plan ($RawDiscoverySha256 -cmatch '^[a-f0-9]{64}$')
    Assert-PlanStrings $RequestedIds $false
    Assert-PlanStrings $SelectionIds $false
    Require-Plan ($RequestedIds.Count -le 100000 -and $SelectionIds.Count -le 100000 -and
        $PhaseRecords.Count -gt 0 -and $PhaseRecords.Count -le 100000)
    # Both original UTF-8 inputs stay open with write/delete sharing denied until
    # every comparison completes. No producer or expected digest is synthesized.
    foreach ($path in @($PlanPath, $ExpectedPlanPath)) {
        $full = Assert-PlanRegularPath $path
        $stream = [IO.File]::Open($full, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $streams.Add($stream)
        Require-Plan ($stream.Length -gt 0 -and $stream.Length -le 67108864)
        $null = Assert-PlanRegularPath $full
    }
    $options = [Text.Json.JsonDocumentOptions]::new()
    $options.MaxDepth = 40
    $options.AllowTrailingCommas = $false
    $options.CommentHandling = [Text.Json.JsonCommentHandling]::Disallow
    foreach ($stream in $streams) { $documents.Add([Text.Json.JsonDocument]::Parse($stream, $options)) }
    $producer = Read-PlanDocument $documents[0].RootElement
    $expected = Read-PlanDocument $documents[1].RootElement
    Require-Plan ($producer.RawDiscoverySha256.Equals($RawDiscoverySha256, [StringComparison]::Ordinal))
    Assert-PlanStringSequence $producer.RequestedIds $RequestedIds
    Assert-PlanStringSequence $expected.RequestedIds $RequestedIds
    Assert-PlanStringSequence $producer.ExpandedIds $SelectionIds
    Assert-PlanStringSequence $expected.ExpandedIds $SelectionIds
    Require-Plan ($producer.ExpansionReasons.Count -eq $expected.ExpansionReasons.Count)
    for ($index = 0; $index -lt $expected.ExpansionReasons.Count; $index++) {
        $actual = $producer.ExpansionReasons[$index]; $authority = $expected.ExpansionReasons[$index]
        Require-Plan ($actual.Code.Equals($authority.Code, [StringComparison]::Ordinal) -and
            $actual.Profile.Equals($authority.Profile, [StringComparison]::Ordinal))
        Assert-PlanStringSequence $actual.SelectedIds $authority.SelectedIds
        Assert-PlanStringSequence $actual.AddedIds $authority.AddedIds
    }
    Require-Plan ($producer.Phases.Count -eq $expected.Phases.Count -and $PhaseRecords.Count -eq $expected.Phases.Count)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $recordWork = [long]0
    for ($index = 0; $index -lt $expected.Phases.Count; $index++) {
        $actual = $producer.Phases[$index]; $authority = $expected.Phases[$index]
        Assert-PlanPhase $actual $authority
        Require-Plan ($actual.Cases.Count -eq $authority.Cases.Count)
        for ($leaf = 0; $leaf -lt $authority.Cases.Count; $leaf++) {
            Require-Plan ($actual.Cases[$leaf].CanonicalId.Equals($authority.Cases[$leaf].CanonicalId, [StringComparison]::Ordinal) -and
                $actual.Cases[$leaf].FullName.Equals($authority.Cases[$leaf].FullName, [StringComparison]::Ordinal))
        }
        $record = Get-PlanRecordFields $PhaseRecords[$index]
        $policy = [pscustomobject]@{ Id = $record['id']; Kind = $record['kind']; Profile = $record['profile'];
            RequireOrder = $record['requireOrder']; MaximumConcurrency = $record['maximumConcurrency'];
            ExclusiveResources = $record['exclusiveResources'] }
        Assert-PlanPhase $policy $authority
        Assert-PlanStrings $record['selectedIds'] $false
        Require-Plan ($record['selectedIds'].Count -le 100000)
        Assert-PlanStringSequence $record['selectedIds'] $authority.SelectedIds
        Assert-PlanInteger $record['passedCount'] 1 100000
        Require-Plan ($record['executed'] -is [bool] -and $record['executed'] -eq $true -and
            $record['passedCount'] -eq $authority.Cases.Count)
        $recordWork += $record['selectedIds'].Count + $record['exclusiveResources'].Count + 1
        Require-Plan ($recordWork -le 2000000)
        foreach ($id in $record['selectedIds']) { Require-Plan ($seen.Add($id)) }
    }
    Require-Plan ($seen.SetEquals($SelectionIds))
} catch { throw 'framework-execution-plan-rejected' }
finally {
    $releaseFailed = $false
    foreach ($document in $documents) { try { $document.Dispose() } catch { $releaseFailed = $true } }
    foreach ($stream in $streams) { try { $stream.Dispose() } catch { $releaseFailed = $true } }
    if ($releaseFailed) { throw 'framework-execution-plan-rejected' }
}
