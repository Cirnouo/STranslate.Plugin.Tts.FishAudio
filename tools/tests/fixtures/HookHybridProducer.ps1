param([string]$RepositoryRoot,[string]$ResultsDirectory,[switch]$NoBuild,[int]$Workers=4)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$context=[IO.File]::ReadAllText($env:FISHAUDIO_SNAPSHOT_CONTEXT)|ConvertFrom-Json
if($RepositoryRoot -cne $root -or -not $NoBuild -or $Workers -ne 4 -or [IO.Directory]::Exists($ResultsDirectory)){throw 'fixture-hybrid-producer-contract'}
$runRoot=[IO.Path]::GetDirectoryName($env:FISHAUDIO_SNAPSHOT_CONTEXT)
if(-not $ResultsDirectory.StartsWith($runRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'fixture-hybrid-private-root'}
[IO.Directory]::CreateDirectory($ResultsDirectory)|Out-Null
$sessionId=[Guid]::NewGuid().ToString('N')
$legacyRoot=Join-Path $ResultsDirectory 'slots/slot-0001/attempt-0001'
$frameworkRoot=Join-Path $ResultsDirectory 'slots/slot-0002/attempt-0001'
[IO.Directory]::CreateDirectory($legacyRoot)|Out-Null
[IO.Directory]::CreateDirectory($frameworkRoot)|Out-Null
$sourceReport=Join-Path $runRoot 'regression.json'
$report=Join-Path $legacyRoot 'legacy.json'
[IO.File]::Copy($sourceReport,$report,$false)
Copy-Item -LiteralPath ($sourceReport+'.generations') -Destination ($report+'.generations') -Recurse
$rawLegacy=[IO.File]::ReadAllText($report)|ConvertFrom-Json
$platformRoot=Join-Path $runRoot 'platform'
& (Get-Process -Id $PID).Path -NoLogo -NoProfile -NonInteractive -File (Join-Path $root 'tools/Test.ps1') -Mode Full -NoBuild -Workers 4 -RepositoryRoot $root -ResultsDirectory $platformRoot
$platformExit=$LASTEXITCODE
$source=if([IO.File]::Exists((Join-Path $platformRoot 'manifest.json'))){([IO.File]::ReadAllText((Join-Path $platformRoot 'manifest.json'))|ConvertFrom-Json).source}else{@{snapshot=$true;tree=$context.tree;sourceCommit=$context.sourceCommit}}
function Write-FixtureJson([string]$Path,$Value){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 32),[Text.UTF8Encoding]::new($false))}
$slots=@()
foreach($ordinal in @(1,2)){
    $attemptRoot=if($ordinal -eq 1){$legacyRoot}else{$frameworkRoot}
    $passed=$ordinal -eq 1 -or $platformExit -eq 0
    $attempt=Join-Path $attemptRoot 'attempt.json'
    Write-FixtureJson $attempt @{ordinal=1;complete=$passed;exitCode=$(if($passed){0}else{$platformExit})}
    $slots+=@{ordinal=$ordinal;id=('slot-{0:D4}' -f $ordinal);owner=$(if($ordinal -eq 1){'legacy'}else{'framework'});partition=$(if($ordinal -eq 1){'core'}else{$null})
        selectedIds=$(if($ordinal -eq 1){@('fixture/pass')}else{@('fixture.platform/plugin','fixture.platform/testing-tool')})
        attempts=@(@{ordinal=1;sha256=(Get-FileHash -LiteralPath $attempt -Algorithm SHA256).Hash.ToLowerInvariant()})}
}
# Two synthetic boundary owners stand for this fixture's complete tiny catalog.
# Product fixed-slot planning is exercised by the real producer/CLI elsewhere.
Write-FixtureJson (Join-Path $ResultsDirectory 'manifest.json') @{schemaVersion=1;kind='hybrid-test-session';runId=$sessionId;complete=($platformExit -eq 0);baselineClaim=$false;workers=4
    syntheticBoundaryControl=$true;source=$source;legacySourceTree=$context.tree;slots=$slots}
exit $platformExit
