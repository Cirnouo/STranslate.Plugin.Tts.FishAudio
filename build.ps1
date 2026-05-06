<#
.SYNOPSIS
    Build and package the STranslate.Plugin.Tts.FishAudio plugin.

.DESCRIPTION
    Builds the plugin using dotnet build, then copies the .spkg file
    to the repository root.

    The MSBuild targets from the STranslate.Plugin NuGet package
    handle the actual .spkg creation. For Release builds it triggers
    automatically (EnableAutoPackage=true); for Debug builds the script
    invokes the PackageAsSpkg target explicitly.

.PARAMETER Clean
    Remove build artifacts (obj, OutputPath) before building.

.PARAMETER CleanOnly
    Remove build artifacts and exit without building.

.PARAMETER Configuration
    Build configuration. Default: Debug.

.PARAMETER Test
    Run the regression test project after building and verifying the package.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Clean
    .\build.ps1 -CleanOnly
    .\build.ps1 -Configuration Debug
    .\build.ps1 -Clean -Configuration Debug
    .\build.ps1 -Clean -Test
#>
param(
    [switch]$Clean,
    [switch]$CleanOnly,
    [switch]$Test,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Paths ──
$RepoRoot    = $PSScriptRoot
$ProjectName = 'STranslate.Plugin.Tts.FishAudio'
$ProjectDir  = Join-Path $RepoRoot $ProjectName
$CsprojPath  = Join-Path $ProjectDir "$ProjectName.csproj"
$TestProjectName = 'STranslate.Plugin.Tts.FishAudio.Tests'
$TestProjectDir  = Join-Path (Join-Path $RepoRoot 'tests') $TestProjectName
$TestCsprojPath  = Join-Path $TestProjectDir "$TestProjectName.csproj"

if (-not (Test-Path $CsprojPath)) {
    Write-Error "Project file not found: $CsprojPath"
    exit 1
}

if ($Test -and -not (Test-Path $TestCsprojPath)) {
    Write-Error "Test project file not found: $TestCsprojPath"
    exit 1
}

# ── Resolve OutputPath from MSBuild ──
function Get-ResolvedOutputPath {
    $raw = & dotnet msbuild $CsprojPath -getProperty:OutputPath "-p:Configuration=$Configuration" 2>$null
    if (-not $raw) { return $null }
    $raw = $raw.Trim()
    if (-not $raw) { return $null }
    if ([System.IO.Path]::IsPathRooted($raw)) {
        return $raw
    }
    return [System.IO.Path]::GetFullPath((Join-Path $ProjectDir $raw))
}

# ── Clean ──
if ($Clean -or $CleanOnly) {
    Write-Host '[clean] Removing build artifacts...' -ForegroundColor Yellow
    $dirsToClean = @(
        (Join-Path $ProjectDir 'obj'),
        (Join-Path $ProjectDir 'bin')
    )

    if ($Test) {
        $dirsToClean += @(
            (Join-Path $TestProjectDir 'obj'),
            (Join-Path $TestProjectDir 'bin')
        )
    }

    $resolvedOutput = Get-ResolvedOutputPath
    if ($resolvedOutput) {
        $dirsToClean += $resolvedOutput
    }
    else {
        $dirsToClean += (Join-Path $RepoRoot '.artifacts')
    }

    foreach ($dir in $dirsToClean) {
        if (Test-Path $dir) {
            Remove-Item $dir -Recurse -Force
            Write-Host "  Removed $dir"
        }
    }

    if ($CleanOnly) {
        Write-Host '[clean] Done.' -ForegroundColor Green
        return
    }
}

# ── Build ──
Write-Host "[build] dotnet build -c $Configuration" -ForegroundColor Cyan
dotnet build $CsprojPath -c $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# ── Pack (Debug only — Release uses EnableAutoPackage) ──
if ($Configuration -eq 'Debug') {
    Write-Host '[pack] Invoking PackageAsSpkg target for Debug build...' -ForegroundColor Cyan
    dotnet build $CsprojPath -c $Configuration -t:PackageAsSpkg
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Packaging failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }
}

# ── Locate .spkg ──
$resolvedOutput = Get-ResolvedOutputPath
$SearchPaths = @()

if ($resolvedOutput) {
    $SearchPaths += $resolvedOutput
}
$releaseArtifacts = Join-Path $RepoRoot '.artifacts'
if ($releaseArtifacts -ne $resolvedOutput) {
    $SearchPaths += $releaseArtifacts
}

$SpkgFile = $null
foreach ($searchPath in $SearchPaths) {
    if (Test-Path $searchPath) {
        $SpkgFile = Get-ChildItem -Path $searchPath -Filter '*.spkg' -Recurse -ErrorAction SilentlyContinue |
                    Select-Object -First 1
        if ($SpkgFile) { break }
    }
}

if (-not $SpkgFile) {
    Write-Error "No .spkg file found after build. Searched: $($SearchPaths -join ', ')"
    exit 1
}

# ── Copy to repo root ──
$DestPath = Join-Path $RepoRoot $SpkgFile.Name
Copy-Item $SpkgFile.FullName $DestPath -Force

# ── Summary ──
$size = [math]::Round((Get-Item $DestPath).Length / 1KB, 1)
Write-Host ''
Write-Host "=== Build succeeded ===" -ForegroundColor Green
Write-Host "  Plugin:        $ProjectName"
Write-Host "  Configuration: $Configuration"
Write-Host "  Output:        $DestPath ($($size) KB)"
Write-Host ''

# ── Verify .spkg contents ──
Write-Host '[verify] .spkg contents:' -ForegroundColor Cyan
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($DestPath)
try {
    $hasPluginJson = $false
    $hasDll        = $false
    foreach ($entry in $zip.Entries) {
        $sizeKB = [math]::Round($entry.Length / 1KB, 1)
        Write-Host "  $($entry.FullName) ($sizeKB KB)"
        if ($entry.Name -eq 'plugin.json')          { $hasPluginJson = $true }
        if ($entry.Name -eq "$ProjectName.dll")      { $hasDll = $true }
    }
    Write-Host ''
    if (-not $hasPluginJson) { Write-Warning 'Missing plugin.json in .spkg!' }
    if (-not $hasDll)        { Write-Warning "Missing $ProjectName.dll in .spkg!" }
    if ($hasPluginJson -and $hasDll) {
        Write-Host '  All required files present.' -ForegroundColor Green
    }
}
finally {
    $zip.Dispose()
}

if ($Test) {
    Write-Host ''
    Write-Host "[test] dotnet run --project $TestCsprojPath -c $Configuration" -ForegroundColor Cyan
    dotnet run --project $TestCsprojPath -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Tests failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }

    Write-Host '[test] Tests passed.' -ForegroundColor Green
}
