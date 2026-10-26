param([string]$ReportPath, [string]$Destination)

$ErrorActionPreference = 'Stop'

function Copy-RegressionEvidence {
    param([string]$ReportPath, [string]$Destination)
    $report = [IO.Path]::GetFullPath($ReportPath)
    $source = [IO.Path]::GetDirectoryName($report)
    $destinationRoot = [IO.Path]::GetFullPath($Destination)
    $sourcePrefix = $source.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($destinationRoot.Equals($source, [StringComparison]::OrdinalIgnoreCase) -or
        $destinationRoot.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Directory]::Exists($destinationRoot) -or [IO.File]::Exists($destinationRoot)) {
        throw 'ci-evidence-invalid-destination'
    }
    if (-not [IO.File]::Exists($report) -and -not [IO.Directory]::Exists($report + '.generations')) {
        throw 'ci-evidence-missing-report'
    }
    [IO.Directory]::CreateDirectory($destinationRoot) | Out-Null
    $copied = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    function Copy-OwnedFile([string]$path) {
        if (-not $path.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'ci-evidence-foreign-file' }
        if ($copied.Add($path)) {
            $relative = [IO.Path]::GetRelativePath($source, $path)
            $target = [IO.Path]::Combine($destinationRoot, $relative)
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            [IO.File]::Copy($path, $target, $false)
        }
    }
    if ([IO.File]::Exists($report)) { Copy-OwnedFile $report }
    $generationRoot = $report + '.generations'
    if (-not [IO.Directory]::Exists($generationRoot)) { return }
    [string[]]$owners = [IO.Directory]::GetDirectories($generationRoot)
    if ($owners.Count -ne 1 -or [IO.Path]::GetFileName($owners[0]) -cnotmatch '^[a-f0-9]{32}$') {
        throw 'ci-evidence-invalid-generation-owner'
    }
    $runId = [IO.Path]::GetFileName($owners[0])
    [string[]]$generations = [IO.Directory]::GetFiles($owners[0], '*.json')
    foreach ($file in $generations) {
        $name = [IO.Path]::GetFileName($file)
        $number = 0L
        if ($name -cnotmatch '^[0-9]{20}\.json$' -or -not [long]::TryParse($name.Substring(0, 20), [ref]$number)) {
            throw 'ci-evidence-invalid-generation-name'
        }
    }
    if ($generations.Count -eq 0) { throw 'ci-evidence-missing-generations' }
    [Array]::Sort($generations, [StringComparer]::Ordinal)
    Copy-OwnedFile ([IO.Path]::Combine($owners[0], '00000000000000000000.json'))
    # Determine the committed authority by filename, never by parse success.
    Copy-OwnedFile $generations[-1]
    if ([IO.File]::Exists($report)) {
        $projection = $null
        try { $projection = [IO.File]::ReadAllText($report) | ConvertFrom-Json -ErrorAction Stop } catch { }
        if ($null -ne $projection -and $projection.PSObject.Properties.Name -ccontains 'generation') {
            $number = 0L
            if (-not [long]::TryParse([string]$projection.generation, [ref]$number) -or $number -lt 0) {
                throw 'ci-evidence-invalid-projection-generation'
            }
            Copy-OwnedFile ([IO.Path]::Combine($owners[0], $number.ToString('D20', [Globalization.CultureInfo]::InvariantCulture) + '.json'))
        }
    }
    $children = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in [IO.Directory]::GetFiles($source, "$runId.*.child.json*")) {
        $name = [IO.Path]::GetFileName($file)
        if ($name.EndsWith('.request.json', [StringComparison]::Ordinal) -or $name.EndsWith('.tmp', [StringComparison]::Ordinal)) { continue }
        if ($name -cmatch "^($runId\.[a-f0-9]{32}\.child\.json)(?:\.snapshot-[0-9]{6}\.json)?$") {
            $children.Add([IO.Path]::Combine($source, $Matches[1])) | Out-Null
        } else { throw 'ci-evidence-invalid-child-name' }
    }
    foreach ($child in $children) {
        if ([IO.File]::Exists($child)) { Copy-OwnedFile $child }
        [string[]]$snapshots = [IO.Directory]::GetFiles($source, [IO.Path]::GetFileName($child) + '.snapshot-*.json')
        if ($snapshots.Count -gt 0) {
            [Array]::Sort($snapshots, [StringComparer]::Ordinal)
            Copy-OwnedFile $snapshots[-1]
        }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    if ([string]::IsNullOrWhiteSpace($ReportPath) -or [string]::IsNullOrWhiteSpace($Destination)) { throw 'ci-evidence-missing-paths' }
    Copy-RegressionEvidence $ReportPath $Destination
}
