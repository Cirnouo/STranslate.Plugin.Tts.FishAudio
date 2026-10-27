#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')),
    [string]$ResultsPath,
    [string[]]$Scenario = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# These are process-level acceptance tests for the installed hook. The only fake
# is the tracked build.ps1 in each disposable repository; no gate bypass is used.
$sourceRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$scriptPaths = @('tools/Install-TestHooks.ps1', 'tools/TestGate.ps1', '.githooks/pre-commit', '.githooks/commit-msg')
$scenarioNames = @(
    'validator-failure',
    'partial-stage',
    'staged-success',
    'alternate-index',
    'missing-hook',
    'fresh-checkout',
    'linked-worktree',
    'previous-hook-order',
    'previous-hook-failure',
    'untracked-output',
    'raw-bytes',
    'index-mutation',
    'verifier-rejects-report',
    'late-commit-message-mutation'
)
$utf8 = [Text.UTF8Encoding]::new($false)
$shellPath = (Get-Process -Id $PID).Path
$gitPath = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$runId = [Guid]::NewGuid().ToString('N')
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$runRoot = Join-Path $temporaryRoot ('fishaudio-hook-acceptance-' + $runId)
$fixturesRoot = Join-Path $runRoot 'fixtures'
$evidenceRoot = Join-Path $runRoot 'evidence'
$emptyGitConfig = Join-Path $runRoot 'empty.gitconfig'
$emptyGitTemplate = Join-Path $runRoot 'empty-template'
$results = [Collections.Generic.List[object]]::new()
$selected = @(if ($Scenario.Count -eq 0) { $scenarioNames } else { $scenarioNames | Where-Object { $Scenario -ccontains $_ } })

function Assert-Condition {
    param([bool]$Condition, [string]$Code)
    if (-not $Condition) { throw ('hook-acceptance-' + $Code) }
}

function Test-ContainedPath {
    param([string]$Path, [string]$Root)
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    return $fullPath.StartsWith($fullRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Write-FixtureText {
    param([string]$Path, [string]$Text)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Text, $utf8)
}

function Write-PrivateFailure {
    param([Management.Automation.ErrorRecord]$Failure, [string]$Path)
    Assert-Condition (Test-ContainedPath $Path $evidenceRoot) 'failure-diagnostics-must-be-private'
    $detail = [Text.StringBuilder]::new()
    $detail.AppendLine('ErrorRecord: ' + [string]$Failure) | Out-Null
    $detail.AppendLine('FullyQualifiedErrorId: ' + $Failure.FullyQualifiedErrorId) | Out-Null
    if ($null -ne $Failure.InvocationInfo) {
        $detail.AppendLine('ScriptName: ' + $Failure.InvocationInfo.ScriptName) | Out-Null
        $detail.AppendLine('ScriptLineNumber: ' + $Failure.InvocationInfo.ScriptLineNumber) | Out-Null
        $detail.AppendLine('PositionMessage: ' + $Failure.InvocationInfo.PositionMessage) | Out-Null
    }
    $detail.AppendLine('ScriptStackTrace: ' + $Failure.ScriptStackTrace) | Out-Null
    $exceptions = [Collections.Generic.Stack[Exception]]::new()
    $seen = [Collections.Generic.HashSet[Exception]]::new()
    $exceptions.Push($Failure.Exception)
    while ($exceptions.Count -gt 0) {
        $exception = $exceptions.Pop()
        if (-not $seen.Add($exception)) { continue }
        $detail.AppendLine('ExceptionType: ' + $exception.GetType().FullName) | Out-Null
        $detail.AppendLine('ExceptionMessage: ' + $exception.Message) | Out-Null
        $detail.AppendLine('ExceptionStackTrace: ' + $exception.StackTrace) | Out-Null
        if ($null -ne $exception.InnerException) { $exceptions.Push($exception.InnerException) }
        if ($exception -is [AggregateException]) {
            foreach ($innerException in $exception.InnerExceptions) { $exceptions.Push($innerException) }
        }
    }
    Write-FixtureText $Path $detail.ToString()
}

function Get-RemainingMilliseconds {
    param($Owner)
    $remaining = 120000L - $Owner.Clock.ElapsedMilliseconds
    Assert-Condition ($remaining -gt 0) 'owner-deadline'
    return [int]$remaining
}

function Invoke-OwnedProcess {
    param(
        $Owner,
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [string]$Label,
        [hashtable]$Environment = @{}
    )
    $remaining = Get-RemainingMilliseconds $Owner
    $Owner.CommandOrdinal++
    $logPrefix = Join-Path $Owner.Evidence ('{0:D3}-{1}' -f $Owner.CommandOrdinal, $Label)
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $FilePath
    $start.WorkingDirectory = $WorkingDirectory
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.CreateNoWindow = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    # Isolate fixtures from the caller's repository/index and personal Git setup.
    # These variables belong to the test process, never to the source checkout.
    $environmentKeys = [Collections.Generic.List[string]]::new()
    foreach ($key in $start.Environment.Keys) { $environmentKeys.Add($key) }
    foreach ($key in $environmentKeys) {
        if ($key.StartsWith('GIT_', [StringComparison]::OrdinalIgnoreCase) -or
            $key.StartsWith('FISHAUDIO_', [StringComparison]::OrdinalIgnoreCase)) {
            $start.Environment.Remove($key) | Out-Null
        }
    }
    $start.Environment['GIT_CONFIG_NOSYSTEM'] = '1'
    $start.Environment['GIT_CONFIG_GLOBAL'] = $emptyGitConfig
    $start.Environment['GIT_TERMINAL_PROMPT'] = '0'
    $start.Environment['FISHAUDIO_ACCEPTANCE_EVIDENCE'] = $Owner.Invocations
    $start.Environment['FISHAUDIO_ACCEPTANCE_REPOSITORY'] = $WorkingDirectory
    foreach ($key in $Environment.Keys) { $start.Environment[$key] = [string]$Environment[$key] }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $stdout = $null
    $stderr = $null
    $primary = $null
    $processStarted = $false
    try {
        $processStarted = $process.Start()
        Assert-Condition $processStarted 'process-not-started'
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        Assert-Condition ($process.WaitForExit($remaining)) 'process-deadline'
        $remaining = Get-RemainingMilliseconds $Owner
        $drains = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($stdout, $stderr))
        Assert-Condition ($drains.Wait($remaining)) 'output-deadline'
        [IO.File]::WriteAllText($logPrefix + '.stdout.log', $stdout.Result, $utf8)
        [IO.File]::WriteAllText($logPrefix + '.stderr.log', $stderr.Result, $utf8)
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $stdout.Result; Error = $stderr.Result }
    } catch {
        $primary = $_
        try { Write-PrivateFailure $primary ($logPrefix + '.failure.txt') } catch { }
        throw
    } finally {
        # The same owner retains the process through termination and both drains.
        # Failure diagnostics share one ten-second allowance, without a retry.
        $cleanupError = $null
        try {
            if ($null -ne $primary) {
                $failureClock = [Diagnostics.Stopwatch]::StartNew()
                if ($processStarted -and -not $process.HasExited) { $process.Kill($true) }
                if ($processStarted) {
                    $failureRemaining = [Math]::Max(1, 10000 - [int]$failureClock.ElapsedMilliseconds)
                    Assert-Condition ($process.WaitForExit($failureRemaining)) 'process-termination-deadline'
                }
                foreach ($drain in @($stdout, $stderr)) {
                    if ($null -eq $drain) { continue }
                    $failureRemaining = 10000 - [int]$failureClock.ElapsedMilliseconds
                    Assert-Condition ($failureRemaining -gt 0 -and $drain.Wait([Math]::Max(1, $failureRemaining))) 'failure-output-deadline'
                }
                if ($null -ne $stdout -and $stdout.IsCompletedSuccessfully) {
                    [IO.File]::WriteAllText($logPrefix + '.stdout.log', $stdout.Result, $utf8)
                }
                if ($null -ne $stderr -and $stderr.IsCompletedSuccessfully) {
                    [IO.File]::WriteAllText($logPrefix + '.stderr.log', $stderr.Result, $utf8)
                }
            }
        } catch { $cleanupError = $_ } finally { $process.Dispose() }
        if ($null -ne $cleanupError) {
            try { [IO.File]::WriteAllText($logPrefix + '.cleanup.log', $cleanupError.Exception.GetType().FullName, $utf8) } catch { }
            if ($null -eq $primary) { throw $cleanupError }
        }
    }
}

function Invoke-FixtureGit {
    param($Owner, [string]$Root, [string[]]$Arguments, [hashtable]$Environment = @{}, [switch]$AllowFailure)
    $result = Invoke-OwnedProcess $Owner $gitPath (@('-C', $Root) + $Arguments) $Root 'git' $Environment
    if (-not $AllowFailure) { Assert-Condition ($result.ExitCode -eq 0) 'git-command-failed' }
    return $result
}

function Invoke-FixtureInstaller {
    param($Owner, [string]$Root, [ValidateSet('Install', 'Check')][string]$Mode, [switch]$AllowFailure)
    $result = Invoke-OwnedProcess $Owner $shellPath @('-NoLogo', '-NoProfile', '-File', (Join-Path $Root 'tools/Install-TestHooks.ps1'), ('-' + $Mode), '-RepositoryRoot', $Root) $Root ('installer-' + $Mode.ToLowerInvariant())
    if (-not $AllowFailure) { Assert-Condition ($result.ExitCode -eq 0) 'installer-failed' }
    return $result
}

function Invoke-FixtureCommit {
    param($Owner, [string]$Root, [hashtable]$Environment = @{})
    return Invoke-FixtureGit $Owner $Root @('-c', 'user.name=Hook Acceptance', '-c', 'user.email=hook-acceptance@example.invalid', 'commit', '--no-gpg-sign', '-m', 'test: exercise installed commit hook') $Environment -AllowFailure
}

function Get-InvocationPaths {
    param($Owner)
    return @([IO.Directory]::GetFiles($Owner.Invocations, 'invocation-*.json'))
}

function Get-VerifierPaths {
    param($Owner)
    return @([IO.Directory]::GetFiles($Owner.Invocations, 'verifier-*.json'))
}

function Get-NewVerifierInvocation {
    param($Owner, [string[]]$Before)
    $new = @(Get-VerifierPaths $Owner | Where-Object { $Before -cnotcontains $_ })
    Assert-Condition ($new.Count -eq 1) 'expected-one-verifier-invocation'
    return [IO.File]::ReadAllText($new[0]) | ConvertFrom-Json
}

function Get-NewInvocation {
    param($Owner, [string[]]$Before)
    $new = @(Get-InvocationPaths $Owner | Where-Object { $Before -cnotcontains $_ })
    Assert-Condition ($new.Count -eq 1) 'expected-one-build-invocation'
    return [IO.File]::ReadAllText($new[0]) | ConvertFrom-Json
}

function Assert-NoNewInvocation {
    param($Owner, [string[]]$Before)
    $new = @(Get-InvocationPaths $Owner | Where-Object { $Before -cnotcontains $_ })
    Assert-Condition ($new.Count -eq 0) 'unexpected-build-invocation'
}

function Assert-NoCommit {
    param($Owner, [string]$Root)
    $head = Invoke-FixtureGit $Owner $Root @('rev-parse', '--verify', 'HEAD') -AllowFailure
    Assert-Condition ($head.ExitCode -ne 0) 'rejected-commit-created-head'
}

function Assert-Snapshot {
    param($Invocation, [string]$Root, [string]$Tree, [string]$Value = 'good')
    Assert-Condition ($Invocation.value -ceq $Value) 'wrong-snapshot-value'
    Assert-Condition ($Invocation.release -and $Invocation.test) 'wrong-build-arguments'
    Assert-Condition ($Invocation.context.schemaVersion -eq 1) 'wrong-context-schema'
    Assert-Condition ($Invocation.context.tree -ceq $Tree) 'wrong-snapshot-tree'
    Assert-Condition ($Invocation.rootMatchesContext) 'context-root-mismatch'
    Assert-Condition (-not ([IO.Path]::GetFullPath($Invocation.snapshotRoot)).Equals([IO.Path]::GetFullPath($Root), [StringComparison]::OrdinalIgnoreCase)) 'build-used-working-tree'
    Assert-Condition ($Invocation.externalResults) 'results-outside-private-state-or-inside-snapshot'
    Assert-Condition (Test-ContainedPath $Invocation.resultsPath $Invocation.privateStateRoot) 'results-outside-private-state'
    Assert-Condition (-not (Test-ContainedPath $Invocation.resultsPath $Invocation.snapshotRoot)) 'results-inside-snapshot'
}

$fixtureBuild = @'
[CmdletBinding()]
param([switch]$Release, [switch]$Test, [string]$TestResults)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$encoding = [Text.UTF8Encoding]::new($false)
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$context = [IO.File]::ReadAllText($env:FISHAUDIO_SNAPSHOT_CONTEXT) | ConvertFrom-Json
$value = [IO.File]::ReadAllText((Join-Path $root 'value.txt')).Trim()
function Test-Under([string]$path, [string]$parent) {
    $prefix = [IO.Path]::GetFullPath($parent).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    return [IO.Path]::GetFullPath($path).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}
$unexpectedSource = [IO.File]::Exists((Join-Path $root 'untracked-source.cs'))
$unexpectedOutput = [IO.File]::Exists((Join-Path $root 'stale-output/old.spkg'))
$rootMatches = [IO.Path]::GetFullPath($context.root).Equals($root, [StringComparison]::OrdinalIgnoreCase)
$privateState = (& git -C $env:FISHAUDIO_ACCEPTANCE_REPOSITORY rev-parse --path-format=absolute --git-path fish-audio-testing) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'fixture-private-state-unavailable' }
$privateState = $privateState.Trim()
$externalResults = -not [string]::IsNullOrWhiteSpace($TestResults) -and
    -not (Test-Under $TestResults $root) -and
    (Test-Under $TestResults $privateState)
$rawBase64 = $null
$rawSha256 = $null
$rawMatches = $true
$rawPath = Join-Path $root 'raw-evidence.bin'
if ([IO.File]::Exists($rawPath)) {
    $bytes = [IO.File]::ReadAllBytes($rawPath)
    $rawBase64 = [Convert]::ToBase64String($bytes)
    $rawSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    $expected = [IO.File]::ReadAllText((Join-Path $root 'raw.expected.sha256')).Trim()
    $rawMatches = $rawSha256 -ceq $expected
}
$passed = $Release -and $Test -and $rootMatches -and $externalResults -and $value -ceq 'good' -and
    -not $unexpectedSource -and -not $unexpectedOutput -and $rawMatches
$verifierProject = Join-Path $root 'tests/STranslate.Plugin.Tts.FishAudio.Tests/STranslate.Plugin.Tts.FishAudio.Tests.csproj'
& dotnet build $verifierProject --configuration Release --nologo --verbosity quiet --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$mutationPerformed = $false
if (-not [string]::IsNullOrWhiteSpace($env:FISHAUDIO_ACCEPTANCE_MUTATION_CONTEXT)) {
    $mutation = [IO.File]::ReadAllText($env:FISHAUDIO_ACCEPTANCE_MUTATION_CONTEXT) | ConvertFrom-Json
    $priorIndex = $env:GIT_INDEX_FILE
    try {
        $env:GIT_INDEX_FILE = $mutation.index
        & git --git-dir $mutation.gitDir --work-tree $mutation.repository update-index --cacheinfo ('100644,' + $mutation.blob + ',value.txt')
        if ($LASTEXITCODE -ne 0) { throw 'fixture-index-mutation-failed' }
        $mutationPerformed = $true
    } finally { $env:GIT_INDEX_FILE = $priorIndex }
}
$invocation = [ordered]@{
    context = $context
    snapshotRoot = $root
    value = $value
    release = [bool]$Release
    test = [bool]$Test
    rootMatchesContext = $rootMatches
    externalResults = $externalResults
    resultsPath = $TestResults
    privateStateRoot = $privateState
    unexpectedSource = $unexpectedSource
    unexpectedOutput = $unexpectedOutput
    rawBase64 = $rawBase64
    rawSha256 = $rawSha256
    mutationPerformed = $mutationPerformed
    passed = [bool]$passed
}
$evidence = Join-Path $env:FISHAUDIO_ACCEPTANCE_EVIDENCE ('invocation-' + [Guid]::NewGuid().ToString('N') + '.json')
[IO.File]::WriteAllText($evidence, ($invocation | ConvertTo-Json -Depth 15), $encoding)
$report = [ordered]@{
    schemaVersion = 1
    complete = $true
    configuration = 'Release'
    source = [ordered]@{ tree = $context.tree; available = $true; snapshot = $true }
    cases = @([ordered]@{ id = 'fixture/pass'; outcome = $(if ($passed) { 'passed' } else { 'failed' }) })
}
$reportMode = Join-Path $root 'fixture-report-mode.txt'
if ([IO.File]::Exists($reportMode) -and [IO.File]::ReadAllText($reportMode).Trim() -ceq 'wrong-case') {
    $report.cases = @([ordered]@{ id = 'fixture/unexpected'; outcome = 'passed' })
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($TestResults))) | Out-Null
[IO.File]::WriteAllText($TestResults, ($report | ConvertTo-Json -Depth 10), $encoding)
if (-not $passed) { exit 17 }
'@

$fixtureVerifierProject = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
'@

$fixtureVerifierProgram = @'
using System.Text.Json;

var valid = false;
var tree = args.Length == 3 ? args[2] : string.Empty;
try
{
    if (args.Length != 3 || args[0] != "--verify-snapshot-report")
        return 1;

    using var document = JsonDocument.Parse(File.ReadAllBytes(args[1]));
    var report = document.RootElement;
    var source = report.GetProperty("source");
    var cases = report.GetProperty("cases");
    valid = report.GetProperty("schemaVersion").GetInt32() == 1 &&
        report.GetProperty("complete").GetBoolean() &&
        report.GetProperty("configuration").GetString() == "Release" &&
        source.GetProperty("available").GetBoolean() &&
        source.GetProperty("snapshot").GetBoolean() &&
        source.GetProperty("tree").GetString() == tree &&
        cases.GetArrayLength() == 1 &&
        cases[0].GetProperty("id").GetString() == "fixture/pass" &&
        cases[0].GetProperty("outcome").GetString() == "passed";
}
catch
{
    valid = false;
}
finally
{
    var evidence = Environment.GetEnvironmentVariable("FISHAUDIO_ACCEPTANCE_EVIDENCE");
    if (!string.IsNullOrWhiteSpace(evidence))
    {
        var path = Path.Combine(evidence, "verifier-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { tree, valid }));
    }
}

return valid ? 0 : 1;
'@

function Register-FixtureRepository {
    param($Owner, [string]$Root)
    $state = (Invoke-FixtureGit $Owner $Root @('rev-parse', '--path-format=absolute', '--git-path', 'fish-audio-testing')).Output.Trim()
    Assert-Condition (Test-ContainedPath $state $Owner.FixtureRoot) 'foreign-fixture-git-state'
    if (-not $Owner.GitStateRoots.Contains($state)) {
        $gitDir = (Invoke-FixtureGit $Owner $Root @('rev-parse', '--absolute-git-dir')).Output.Trim()
        $commonDir = (Invoke-FixtureGit $Owner $Root @('rev-parse', '--path-format=absolute', '--git-common-dir')).Output.Trim()
        Assert-Condition (Test-ContainedPath $gitDir $Owner.FixtureRoot) 'foreign-fixture-git-directory'
        Assert-Condition (Test-ContainedPath $commonDir $Owner.FixtureRoot) 'foreign-fixture-common-directory'
        $Owner.GitStateRoots.Add($state)
        $Owner.GitMetadata.Add($state, [pscustomobject]@{ GitDir = $gitDir; CommonDir = $commonDir })
    }
}

function New-FixtureRepository {
    param($Owner, [string]$Name = 'repository', [string]$Value = 'good', [switch]$WithoutInstall)
    $root = Join-Path $Owner.FixtureRoot $Name
    [IO.Directory]::CreateDirectory($root) | Out-Null
    Invoke-FixtureGit $Owner $root @('init', '--template', $emptyGitTemplate, '--initial-branch', 'fixture-main', $root) | Out-Null
    Register-FixtureRepository $Owner $root
    foreach ($relative in $scriptPaths) {
        $target = Join-Path $root $relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        [IO.File]::Copy((Join-Path $sourceRoot $relative), $target)
    }
    Write-FixtureText (Join-Path $root 'build.ps1') $fixtureBuild
    Write-FixtureText (Join-Path $root 'tests/STranslate.Plugin.Tts.FishAudio.Tests/STranslate.Plugin.Tts.FishAudio.Tests.csproj') $fixtureVerifierProject
    Write-FixtureText (Join-Path $root 'tests/STranslate.Plugin.Tts.FishAudio.Tests/Program.cs') $fixtureVerifierProgram
    Write-FixtureText (Join-Path $root 'value.txt') ($Value + "`n")
    Write-FixtureText (Join-Path $root '.gitignore') "stale-output/`n"
    Invoke-FixtureGit $Owner $root @('add', '--', '.') | Out-Null
    if (-not $WithoutInstall) {
        Invoke-FixtureInstaller $Owner $root Install | Out-Null
        Invoke-FixtureInstaller $Owner $root Check | Out-Null
    }
    return $root
}

function Initialize-FixtureCommit {
    param($Owner, [string]$Root)
    $before = @(Get-InvocationPaths $Owner)
    $tree = (Invoke-FixtureGit $Owner $Root @('write-tree')).Output.Trim()
    $commit = Invoke-FixtureCommit $Owner $Root
    Assert-Condition ($commit.ExitCode -eq 0) 'initial-commit-rejected'
    $invocation = Get-NewInvocation $Owner $before
    Assert-Snapshot $invocation $Root $tree
    $committedTree = (Invoke-FixtureGit $Owner $Root @('rev-parse', 'HEAD^{tree}')).Output.Trim()
    Assert-Condition ($committedTree -ceq $tree) 'initial-commit-tree-mismatch'
}

function Get-EffectiveHookPath {
    param($Owner, [string]$Root)
    return (Invoke-FixtureGit $Owner $Root @('rev-parse', '--path-format=absolute', '--git-path', 'hooks/pre-commit')).Output.Trim()
}

function Invoke-AcceptanceScenario {
    param($Owner, [string]$Name)
    switch -CaseSensitive ($Name) {
        'validator-failure' {
            $root = New-FixtureRepository $Owner -Value bad
            $tree = (Invoke-FixtureGit $Owner $root @('write-tree')).Output.Trim()
            $before = @(Get-InvocationPaths $Owner)
            $commit = Invoke-FixtureCommit $Owner $root
            Assert-Condition ($commit.ExitCode -ne 0) 'bad-validator-allowed-commit'
            Assert-NoCommit $Owner $root
            $invocation = Get-NewInvocation $Owner $before
            Assert-Snapshot $invocation $root $tree bad
            Assert-Condition (-not $invocation.passed) 'bad-validator-reported-pass'
        }
        'partial-stage' {
            $root = New-FixtureRepository $Owner -Value bad
            $tree = (Invoke-FixtureGit $Owner $root @('write-tree')).Output.Trim()
            Write-FixtureText (Join-Path $root 'value.txt') "good`n"
            $before = @(Get-InvocationPaths $Owner)
            $commit = Invoke-FixtureCommit $Owner $root
            Assert-Condition ($commit.ExitCode -ne 0) 'unstaged-good-masked-staged-bad'
            Assert-NoCommit $Owner $root
            $failedInvocation = Get-NewInvocation $Owner $before
            Assert-Snapshot $failedInvocation $root $tree bad
            Assert-Condition ([IO.File]::ReadAllText((Join-Path $root 'value.txt')).Trim() -ceq 'good') 'working-value-changed'
            $failedRun = [IO.Path]::GetDirectoryName($failedInvocation.resultsPath)
            $failedGate = [IO.File]::ReadAllText((Join-Path $failedRun 'gate.json')) | ConvertFrom-Json
            Assert-Condition (-not $failedGate.complete -and $failedGate.exitCode -ne 0 -and $failedGate.snapshotTree -ceq $tree) 'failed-gate-outcome-not-retained'
            $failedEvidence = @{}
            foreach ($name in @('gate.json', 'regression.json', 'build.stdout.log', 'build.stderr.log')) {
                $failedEvidence[$name] = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $failedRun $name)))
            }

            # Continue the same owner: stage the already-good working file at
            # the same path and require a fresh ordinary commit validation.
            Invoke-FixtureGit $Owner $root @('add', '--', 'value.txt') | Out-Null
            $repairedTree = (Invoke-FixtureGit $Owner $root @('write-tree')).Output.Trim()
            Assert-Condition ($repairedTree -cne $tree) 'repair-did-not-change-staged-tree'
            $beforeRepair = @(Get-InvocationPaths $Owner)
            $beforeVerifier = @(Get-VerifierPaths $Owner)
            $repairedCommit = Invoke-FixtureCommit $Owner $root @{ FISHAUDIO_EXPECTED_TREE = $repairedTree }
            Assert-Condition ($repairedCommit.ExitCode -eq 0) 'same-path-repair-rejected'
            $repairedInvocation = Get-NewInvocation $Owner $beforeRepair
            Assert-Snapshot $repairedInvocation $root $repairedTree
            $verifier = Get-NewVerifierInvocation $Owner $beforeVerifier
            Assert-Condition ($verifier.valid -and $verifier.tree -ceq $repairedTree) 'same-path-repair-not-freshly-verified'
            Assert-Condition ((Invoke-FixtureGit $Owner $root @('rev-parse', 'HEAD^{tree}')).Output.Trim() -ceq $repairedTree) 'repaired-commit-tree-mismatch'
            $repairedRun = [IO.Path]::GetDirectoryName($repairedInvocation.resultsPath)
            Assert-Condition (-not $repairedRun.Equals($failedRun, [StringComparison]::OrdinalIgnoreCase)) 'repair-reused-failed-run'
            $repairedGate = [IO.File]::ReadAllText((Join-Path $repairedRun 'gate.json')) | ConvertFrom-Json
            Assert-Condition ($repairedGate.complete -and $repairedGate.exitCode -eq 0 -and $repairedGate.snapshotTree -ceq $repairedTree) 'repaired-gate-not-complete-for-current-tree'
            $consumedReceipt = [IO.File]::ReadAllText((Join-Path $repairedRun 'commit-final.json')) | ConvertFrom-Json
            Assert-Condition ($consumedReceipt.runId -ceq $repairedGate.runId -and $consumedReceipt.tree -ceq $repairedTree -and
                -not [IO.File]::Exists((Join-Path $repairedInvocation.privateStateRoot 'commit-receipt.json'))) 'repair-receipt-not-consumed'
            foreach ($name in $failedEvidence.Keys) {
                Assert-Condition ([Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $failedRun $name))) -ceq $failedEvidence[$name]) 'repair-rewrote-original-failed-evidence'
            }
        }
        'staged-success' {
            $root = New-FixtureRepository $Owner
            $tree = (Invoke-FixtureGit $Owner $root @('write-tree')).Output.Trim()
            Write-FixtureText (Join-Path $root 'value.txt') "bad`n"
            $before = @(Get-InvocationPaths $Owner)
            $beforeVerifier = @(Get-VerifierPaths $Owner)
            $commit = Invoke-FixtureCommit $Owner $root
            Assert-Condition ($commit.ExitCode -eq 0) 'staged-good-rejected'
            Assert-Snapshot (Get-NewInvocation $Owner $before) $root $tree
            $verifier = Get-NewVerifierInvocation $Owner $beforeVerifier
            Assert-Condition ($verifier.valid -and $verifier.tree -ceq $tree) 'snapshot-verifier-not-passed'
            Assert-Condition ((Invoke-FixtureGit $Owner $root @('rev-parse', 'HEAD^{tree}')).Output.Trim() -ceq $tree) 'committed-tree-differs-from-validated-tree'
            Assert-Condition ([IO.File]::ReadAllText((Join-Path $root 'value.txt')).Trim() -ceq 'bad') 'unstaged-change-lost'
        }
        'alternate-index' {
            $root = New-FixtureRepository $Owner
            Initialize-FixtureCommit $Owner $root
            Write-FixtureText (Join-Path $root 'value.txt') "bad`n"
            Invoke-FixtureGit $Owner $root @('add', '--', 'value.txt') | Out-Null
            $defaultTree = (Invoke-FixtureGit $Owner $root @('write-tree')).Output.Trim()
            $alternateIndex = Join-Path $Owner.Evidence 'alternate.index'
            $environment = @{ GIT_INDEX_FILE = $alternateIndex }
            Invoke-FixtureGit $Owner $root @('read-tree', 'HEAD') $environment | Out-Null
            Write-FixtureText (Join-Path $root 'alternate-only.txt') "tracked by the alternate index`n"
            Invoke-FixtureGit $Owner $root @('add', '--', 'alternate-only.txt') $environment | Out-Null
            $tree = (Invoke-FixtureGit $Owner $root @('write-tree') $environment).Output.Trim()
            Assert-Condition ($tree -cne $defaultTree) 'alternate-index-fixture-not-distinct'
            $before = @(Get-InvocationPaths $Owner)
            $commit = Invoke-FixtureCommit $Owner $root $environment
            Assert-Condition ($commit.ExitCode -eq 0) 'alternate-index-rejected'
            Assert-Snapshot (Get-NewInvocation $Owner $before) $root $tree
            Assert-Condition ((Invoke-FixtureGit $Owner $root @('rev-parse', 'HEAD^{tree}')).Output.Trim() -ceq $tree) 'alternate-commit-tree-mismatch'
            Assert-Condition ((Invoke-FixtureGit $Owner $root @('write-tree')).Output.Trim() -ceq $defaultTree) 'default-index-changed'
        }
        'missing-hook' {
            $root = New-FixtureRepository $Owner -WithoutInstall
            Assert-Condition ((Invoke-FixtureInstaller $Owner $root Check -AllowFailure).ExitCode -ne 0) 'missing-hook-check-passed'
            Invoke-FixtureInstaller $Owner $root Install | Out-Null
            Invoke-FixtureInstaller $Owner $root Check | Out-Null
            $hook = Get-EffectiveHookPath $Owner $root
            Assert-Condition ([IO.File]::Exists($hook)) 'effective-hook-missing'
            [IO.File]::Delete($hook)
            Assert-Condition ((Invoke-FixtureInstaller $Owner $root Check -AllowFailure).ExitCode -ne 0) 'deleted-hook-check-passed'
        }
        'fresh-checkout' {
            $root = New-FixtureRepository $Owner
            Initialize-FixtureCommit $Owner $root
            $clone = Join-Path $Owner.FixtureRoot 'fresh-checkout'
            Invoke-FixtureGit $Owner $root @('clone', '--no-local', '--template', $emptyGitTemplate, $root, $clone) | Out-Null
            Register-FixtureRepository $Owner $clone
            Assert-Condition ((Invoke-FixtureInstaller $Owner $clone Check -AllowFailure).ExitCode -ne 0) 'new-checkout-claimed-installation'
            Invoke-FixtureInstaller $Owner $clone Install | Out-Null
            Invoke-FixtureInstaller $Owner $clone Check | Out-Null
            Write-FixtureText (Join-Path $clone 'checkout-only.txt') "fresh checkout`n"
            Invoke-FixtureGit $Owner $clone @('add', '--', 'checkout-only.txt') | Out-Null
            $tree = (Invoke-FixtureGit $Owner $clone @('write-tree')).Output.Trim()
            $before = @(Get-InvocationPaths $Owner)
            Assert-Condition ((Invoke-FixtureCommit $Owner $clone).ExitCode -eq 0) 'installed-checkout-rejected'
            Assert-Snapshot (Get-NewInvocation $Owner $before) $clone $tree
        }
        'linked-worktree' {
            $root = New-FixtureRepository $Owner
            Initialize-FixtureCommit $Owner $root
            $originalHead = (Invoke-FixtureGit $Owner $root @('rev-parse', 'HEAD')).Output.Trim()
            $linked = Join-Path $Owner.FixtureRoot 'linked-worktree'
            Invoke-FixtureGit $Owner $root @('worktree', 'add', '-b', 'fixture-linked', $linked, 'HEAD') | Out-Null
            Register-FixtureRepository $Owner $linked
            Invoke-FixtureInstaller $Owner $linked Install | Out-Null
            Invoke-FixtureInstaller $Owner $linked Check | Out-Null
            Write-FixtureText (Join-Path $linked 'linked-only.txt') "linked worktree`n"
            Invoke-FixtureGit $Owner $linked @('add', '--', 'linked-only.txt') | Out-Null
            $tree = (Invoke-FixtureGit $Owner $linked @('write-tree')).Output.Trim()
            $before = @(Get-InvocationPaths $Owner)
            Assert-Condition ((Invoke-FixtureCommit $Owner $linked).ExitCode -eq 0) 'linked-worktree-rejected'
            Assert-Snapshot (Get-NewInvocation $Owner $before) $linked $tree
            Assert-Condition ((Invoke-FixtureGit $Owner $linked @('rev-parse', 'HEAD^{tree}')).Output.Trim() -ceq $tree) 'linked-commit-tree-mismatch'
            Assert-Condition ((Invoke-FixtureGit $Owner $root @('rev-parse', 'HEAD')).Output.Trim() -ceq $originalHead) 'main-worktree-head-changed'
        }
        'previous-hook-order' {
            $root = New-FixtureRepository $Owner -Value bad -WithoutInstall
            $oldHook = Get-EffectiveHookPath $Owner $root
            Write-FixtureText $oldHook "#!/bin/sh`nprintf 'good\n' > value.txt`ngit add -- value.txt || exit 29`nprintf 'previous-hook-ran\n' > previous-hook.marker`nexit 0`n"
            Invoke-FixtureInstaller $Owner $root Install | Out-Null
            $before = @(Get-InvocationPaths $Owner)
            Assert-Condition ((Invoke-FixtureCommit $Owner $root).ExitCode -eq 0) 'previous-hook-order-rejected'
            Assert-Condition ([IO.File]::Exists((Join-Path $root 'previous-hook.marker'))) 'previous-hook-not-invoked'
            $tree = (Invoke-FixtureGit $Owner $root @('rev-parse', 'HEAD^{tree}')).Output.Trim()
            Assert-Snapshot (Get-NewInvocation $Owner $before) $root $tree
        }
        'previous-hook-failure' {
            $root = New-FixtureRepository $Owner -WithoutInstall
            $oldHook = Get-EffectiveHookPath $Owner $root
            Write-FixtureText $oldHook "#!/bin/sh`nprintf 'previous-hook-ran\n' > previous-hook.marker`nexit 23`n"
            Invoke-FixtureInstaller $Owner $root Install | Out-Null
            $before = @(Get-InvocationPaths $Owner)
            Assert-Condition ((Invoke-FixtureCommit $Owner $root).ExitCode -ne 0) 'previous-hook-failure-allowed-commit'
            Assert-NoCommit $Owner $root
            Assert-NoNewInvocation $Owner $before
            Assert-Condition ([IO.File]::Exists((Join-Path $root 'previous-hook.marker'))) 'failing-previous-hook-not-invoked'
            # Git commit folds a rejected hook into its own status. Invoke the
            # installed entry too, to verify preservation of the original 23.
            $execPath = (Invoke-FixtureGit $Owner $root @('--exec-path')).Output.Trim()
            $gitShell = [IO.Path]::GetFullPath((Join-Path $execPath '../../../usr/bin/sh.exe'))
            Assert-Condition ([IO.File]::Exists($gitShell)) 'git-shell-missing'
            $hook = Get-EffectiveHookPath $Owner $root
            $direct = Invoke-OwnedProcess $Owner $gitShell @($hook) $root 'direct-hook'
            Assert-Condition ($direct.ExitCode -eq 23) 'previous-hook-exit-not-preserved'
            Assert-NoNewInvocation $Owner $before
        }
        'untracked-output' {
            $root = New-FixtureRepository $Owner
            Write-FixtureText (Join-Path $root 'untracked-source.cs') 'untracked source must not enter validation'
            Write-FixtureText (Join-Path $root 'stale-output/old.spkg') 'an old package is not current build evidence'
            $tree = (Invoke-FixtureGit $Owner $root @('write-tree')).Output.Trim()
            $before = @(Get-InvocationPaths $Owner)
            Assert-Condition ((Invoke-FixtureCommit $Owner $root).ExitCode -eq 0) 'untracked-files-entered-validation'
            $invocation = Get-NewInvocation $Owner $before
            Assert-Snapshot $invocation $root $tree
            Assert-Condition (-not $invocation.unexpectedSource -and -not $invocation.unexpectedOutput) 'untracked-files-materialized'
            $paths = @($invocation.context.files | ForEach-Object { $_.path })
            Assert-Condition ($paths -cnotcontains 'untracked-source.cs' -and $paths -cnotcontains 'stale-output/old.spkg') 'untracked-files-in-context'
        }
        'raw-bytes' {
            $root = New-FixtureRepository $Owner
            [byte[]]$bytes = @(0x7b, 0x0d, 0x0a, 0x22, 0x76, 0x22, 0x3a, 0x22, 0xff, 0x00, 0x22, 0x0a, 0x7d, 0x0d, 0x0a)
            $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
            [IO.File]::WriteAllBytes((Join-Path $root 'raw-evidence.bin'), $bytes)
            Write-FixtureText (Join-Path $root 'raw.expected.sha256') ($hash + "`n")
            Write-FixtureText (Join-Path $root '.gitattributes') "* text=auto`nraw-evidence.bin -text`n"
            Invoke-FixtureGit $Owner $root @('-c', 'core.autocrlf=true', 'add', '--', '.gitattributes', 'raw-evidence.bin', 'raw.expected.sha256') | Out-Null
            $tree = (Invoke-FixtureGit $Owner $root @('write-tree')).Output.Trim()
            $before = @(Get-InvocationPaths $Owner)
            Assert-Condition ((Invoke-FixtureCommit $Owner $root).ExitCode -eq 0) 'raw-bytes-rejected'
            $invocation = Get-NewInvocation $Owner $before
            Assert-Snapshot $invocation $root $tree
            Assert-Condition ($invocation.rawBase64 -ceq [Convert]::ToBase64String($bytes)) 'raw-blob-bytes-changed'
            $rawFiles = @($invocation.context.files | Where-Object { $_.path -ceq 'raw-evidence.bin' })
            Assert-Condition ($rawFiles.Count -eq 1 -and $rawFiles[0].sha256 -ceq $hash) 'raw-context-hash-mismatch'
        }
        'index-mutation' {
            $root = New-FixtureRepository $Owner
            $tree = (Invoke-FixtureGit $Owner $root @('write-tree')).Output.Trim()
            Write-FixtureText (Join-Path $root 'mutation-input.txt') "bad`n"
            $blob = (Invoke-FixtureGit $Owner $root @('hash-object', '-w', '--', 'mutation-input.txt')).Output.Trim()
            $index = (Invoke-FixtureGit $Owner $root @('rev-parse', '--path-format=absolute', '--git-path', 'index')).Output.Trim()
            $gitDir = (Invoke-FixtureGit $Owner $root @('rev-parse', '--absolute-git-dir')).Output.Trim()
            $mutation = Join-Path $Owner.Evidence 'mutation-context.json'
            Write-FixtureText $mutation (@{ repository = $root; gitDir = $gitDir; index = $index; blob = $blob } | ConvertTo-Json)
            $before = @(Get-InvocationPaths $Owner)
            $commit = Invoke-FixtureCommit $Owner $root @{ FISHAUDIO_ACCEPTANCE_MUTATION_CONTEXT = $mutation }
            Assert-Condition ($commit.ExitCode -ne 0) 'index-mutation-allowed-commit'
            Assert-NoCommit $Owner $root
            $invocation = Get-NewInvocation $Owner $before
            Assert-Snapshot $invocation $root $tree
            Assert-Condition ($invocation.passed -and $invocation.mutationPerformed) 'index-mutation-fixture-did-not-complete'
            Assert-Condition ((Invoke-FixtureGit $Owner $root @('write-tree')).Output.Trim() -cne $tree) 'index-did-not-mutate'
        }
        'verifier-rejects-report' {
            $root = New-FixtureRepository $Owner
            Write-FixtureText (Join-Path $root 'fixture-report-mode.txt') "wrong-case`n"
            Invoke-FixtureGit $Owner $root @('add', '--', 'fixture-report-mode.txt') | Out-Null
            $tree = (Invoke-FixtureGit $Owner $root @('write-tree')).Output.Trim()
            $before = @(Get-InvocationPaths $Owner)
            $beforeVerifier = @(Get-VerifierPaths $Owner)
            $commit = Invoke-FixtureCommit $Owner $root
            Assert-Condition ($commit.ExitCode -ne 0) 'wrong-catalog-report-allowed-commit'
            Assert-NoCommit $Owner $root
            $invocation = Get-NewInvocation $Owner $before
            Assert-Snapshot $invocation $root $tree
            Assert-Condition ($invocation.passed) 'wrong-catalog-fixture-build-did-not-pass'
            $verifier = Get-NewVerifierInvocation $Owner $beforeVerifier
            Assert-Condition (-not $verifier.valid -and $verifier.tree -ceq $tree) 'wrong-catalog-report-not-verified'
        }
        'late-commit-message-mutation' {
            $root = New-FixtureRepository $Owner -WithoutInstall
            $oldMessageHook = (Invoke-FixtureGit $Owner $root @('rev-parse', '--path-format=absolute', '--git-path', 'hooks/commit-msg')).Output.Trim()
            Write-FixtureText $oldMessageHook "#!/bin/sh`nprintf 'bad\n' > value.txt`ngit add -- value.txt || exit 29`nprintf 'late-hook-ran\n' > late-hook.marker`nexit 0`n"
            Invoke-FixtureInstaller $Owner $root Install | Out-Null
            $tree = (Invoke-FixtureGit $Owner $root @('write-tree')).Output.Trim()
            $before = @(Get-InvocationPaths $Owner)
            $beforeVerifier = @(Get-VerifierPaths $Owner)
            $commit = Invoke-FixtureCommit $Owner $root
            Assert-Condition ($commit.ExitCode -ne 0) 'late-message-hook-mutation-allowed-commit'
            Assert-NoCommit $Owner $root
            Assert-Condition ([IO.File]::Exists((Join-Path $root 'late-hook.marker'))) 'previous-message-hook-not-invoked'
            $invocation = Get-NewInvocation $Owner $before
            Assert-Snapshot $invocation $root $tree
            Assert-Condition ($invocation.passed) 'late-message-fixture-build-did-not-pass'
            $verifier = Get-NewVerifierInvocation $Owner $beforeVerifier
            Assert-Condition ($verifier.valid -and $verifier.tree -ceq $tree) 'late-message-fixture-verifier-did-not-pass'
            Assert-Condition ((Invoke-FixtureGit $Owner $root @('write-tree')).Output.Trim() -cne $tree) 'late-message-hook-did-not-mutate-index'
        }
        default { throw 'hook-acceptance-unknown-scenario' }
    }
}

function Copy-FixtureGateEvidence {
    param($Owner)
    $stateOrdinal = 0
    foreach ($state in $Owner.GitStateRoots) {
        $stateOrdinal++
        Assert-Condition (Test-ContainedPath $state $Owner.FixtureRoot) 'foreign-gate-evidence'
        $stateDestination = Join-Path $Owner.Evidence ('gate-runs/{0:D2}' -f $stateOrdinal)
        [IO.Directory]::CreateDirectory($stateDestination) | Out-Null
        $metadata = $Owner.GitMetadata[$state]
        $privateFiles = [ordered]@{
            'installation.json' = Join-Path $state 'hooks/installation.json'
            'git-dir.config' = Join-Path $metadata.GitDir 'config'
            'git-dir.config.worktree' = Join-Path $metadata.GitDir 'config.worktree'
            'git-common.config' = Join-Path $metadata.CommonDir 'config'
            'git-common.config.worktree' = Join-Path $metadata.CommonDir 'config.worktree'
            'git-dir.commondir' = Join-Path $metadata.GitDir 'commondir'
        }
        foreach ($name in $privateFiles.Keys) {
            $source = $privateFiles[$name]
            Assert-Condition (Test-ContainedPath $source $Owner.FixtureRoot) 'foreign-git-configuration-evidence'
            if ([IO.File]::Exists($source)) { [IO.File]::Copy($source, (Join-Path $stateDestination $name), $false) }
        }
        $runs = Join-Path $state 'runs'
        if (-not [IO.Directory]::Exists($runs)) { continue }
        foreach ($run in [IO.Directory]::GetDirectories($runs)) {
            $runName = [IO.Path]::GetFileName($run)
            Assert-Condition ($runName -cmatch '^[a-f0-9]{32}$') 'invalid-gate-evidence-owner'
            $destination = Join-Path $Owner.Evidence ('gate-runs/{0:D2}/{1}' -f $stateOrdinal, $runName)
            [IO.Directory]::CreateDirectory($destination) | Out-Null
            foreach ($name in @('gate.json', 'snapshot.json', 'regression.json')) {
                $source = Join-Path $run $name
                if ([IO.File]::Exists($source)) { [IO.File]::Copy($source, (Join-Path $destination $name), $false) }
            }
            foreach ($pattern in @('build*.log', 'verify*.log')) {
                foreach ($source in [IO.Directory]::GetFiles($run, $pattern)) {
                    [IO.File]::Copy($source, (Join-Path $destination ([IO.Path]::GetFileName($source))), $false)
                }
            }
        }
    }
}

function Remove-OwnedFixture {
    param($Owner)
    # Resolve and check the final targets before any recursive removal. Logs and
    # reports remain outside this owned disposable fixture root.
    $resolvedRun = (Resolve-Path -LiteralPath $runRoot).ProviderPath
    $resolvedFixtures = (Resolve-Path -LiteralPath $fixturesRoot).ProviderPath
    $resolvedOwner = (Resolve-Path -LiteralPath $Owner.FixtureRoot).ProviderPath
    Assert-Condition (Test-ContainedPath $resolvedRun $temporaryRoot) 'unsafe-run-cleanup'
    Assert-Condition ([IO.Path]::GetFileName($resolvedRun) -ceq ('fishaudio-hook-acceptance-' + $runId)) 'wrong-run-owner'
    Assert-Condition (Test-ContainedPath $resolvedFixtures $resolvedRun) 'unsafe-fixture-root-cleanup'
    Assert-Condition (Test-ContainedPath $resolvedOwner $resolvedFixtures) 'unsafe-owner-cleanup'
    Assert-Condition (-not (Test-ContainedPath $Owner.Evidence $resolvedOwner)) 'evidence-inside-cleanup-target'
    if ([IO.Directory]::Exists($resolvedOwner)) { Remove-Item -LiteralPath $resolvedOwner -Recurse -Force }
}

$overallFailure = $null
try {
    foreach ($requested in $Scenario) { Assert-Condition ($scenarioNames -ccontains $requested) 'unknown-scenario' }
    Assert-Condition ($selected.Count -gt 0) 'empty-selection'
    # Keep this preflight before fixture creation so a missing installer is a
    # useful initial RED result, instead of a fixture bootstrap failure.
    foreach ($relative in $scriptPaths) {
        Assert-Condition ([IO.File]::Exists((Join-Path $sourceRoot $relative))) ('missing-' + ($relative -replace '[^A-Za-z0-9]', '-'))
    }
    if ([string]::IsNullOrWhiteSpace($ResultsPath)) { $ResultsPath = Join-Path $evidenceRoot 'results.json' }
    $ResultsPath = [IO.Path]::GetFullPath($ResultsPath)
    Assert-Condition (-not (Test-ContainedPath $ResultsPath $sourceRoot)) 'results-must-be-private'
    [IO.Directory]::CreateDirectory($fixturesRoot) | Out-Null
    [IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null
    [IO.Directory]::CreateDirectory($emptyGitTemplate) | Out-Null
    Write-FixtureText $emptyGitConfig ''
    foreach ($name in $selected) {
        $owner = [pscustomobject]@{
            FixtureRoot = Join-Path $fixturesRoot $name
            Evidence = Join-Path $evidenceRoot $name
            Invocations = Join-Path $evidenceRoot ($name + '/build-invocations')
            GitStateRoots = [Collections.Generic.List[string]]::new()
            GitMetadata = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
            Clock = [Diagnostics.Stopwatch]::StartNew()
            CommandOrdinal = 0
        }
        [IO.Directory]::CreateDirectory($owner.FixtureRoot) | Out-Null
        [IO.Directory]::CreateDirectory($owner.Invocations) | Out-Null
        $failure = $null
        $originalLocation = Get-Location
        try {
            Invoke-AcceptanceScenario $owner $name
        } catch {
            $failure = $_
            try { Write-PrivateFailure $failure (Join-Path $owner.Evidence 'failure.txt') } catch { }
        } finally {
            $cleanupFailure = $null
            try { Copy-FixtureGateEvidence $owner } catch { $cleanupFailure = $_ }
            try {
                Set-Location -LiteralPath $originalLocation.Path
                Remove-OwnedFixture $owner
                Assert-Condition ($owner.Clock.ElapsedMilliseconds -lt 120000) 'owner-deadline'
            } catch { if ($null -eq $cleanupFailure) { $cleanupFailure = $_ } }
            if ($null -ne $cleanupFailure) {
                try { Write-PrivateFailure $cleanupFailure (Join-Path $owner.Evidence 'fixture-cleanup.failure.txt') } catch { }
                if ($null -eq $failure) { $failure = $cleanupFailure }
            }
            $owner.Clock.Stop()
        }
        $outcome = if ($null -eq $failure) { 'passed' } else { 'failed' }
        $code = if ($null -eq $failure) { $null } elseif ($failure.Exception.Message -cmatch '^hook-acceptance-[a-z0-9-]+$') { $failure.Exception.Message } else { $failure.Exception.GetType().FullName }
        $results.Add([pscustomobject]@{ id = $name; outcome = $outcome; durationMilliseconds = $owner.Clock.ElapsedMilliseconds; failure = $code })
        Write-Output ('{0}: {1}' -f $name, $outcome)
    }
} catch { $overallFailure = $_ } finally {
    if ([string]::IsNullOrWhiteSpace($ResultsPath)) { $ResultsPath = Join-Path $evidenceRoot 'results.json' }
    # Preflight failures also produce a private report without inventing cases.
    try {
        if (-not (Test-ContainedPath ([IO.Path]::GetFullPath($ResultsPath)) $sourceRoot)) {
            $completedNames = @($results | ForEach-Object { $_.id })
            $failureCode = if ($null -eq $overallFailure) { $null } elseif ($overallFailure.Exception.Message -cmatch '^hook-acceptance-[a-z0-9-]+$') { $overallFailure.Exception.Message } else { $overallFailure.Exception.GetType().FullName }
            $report = [ordered]@{
                schemaVersion = 1
                complete = $null -eq $overallFailure -and $results.Count -eq $selected.Count
                selected = @($selected)
                cases = @($results.ToArray())
                notRun = @($selected | Where-Object { $completedNames -cnotcontains $_ })
                failure = $failureCode
                evidence = $evidenceRoot
            }
            Write-FixtureText ([IO.Path]::GetFullPath($ResultsPath)) ($report | ConvertTo-Json -Depth 10)
        }
    } catch { if ($null -eq $overallFailure) { $overallFailure = $_ } }
}

if ($null -ne $overallFailure) { throw $overallFailure }
Write-Output ('Acceptance report: ' + [IO.Path]::GetFullPath($ResultsPath))
if (@($results | Where-Object { $_.outcome -cne 'passed' }).Count -gt 0) { exit 1 }
