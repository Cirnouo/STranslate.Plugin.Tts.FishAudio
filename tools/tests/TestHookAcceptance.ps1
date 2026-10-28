#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')),
    [string]$ResultsPath,
    [string[]]$Scenario = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# These are process-level acceptance tests for the installed hook. Disposable
# build, platform-driver and metadata-verifier controls exercise the outer hook
# boundary; they do not execute NUnit scenarios or establish product coverage.
$sourceRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$signingSource = 'tools/FishAudio.Testing/Fixtures/GitFixtureSigning.cs'
$scriptPaths = @('tools/Install-TestHooks.ps1', 'tools/TestGate.ps1', 'tools/Assert-FrameworkSupervisor.ps1',
    'tools/Assert-FrameworkExecutionPlan.ps1', $signingSource, '.githooks/pre-commit', '.githooks/commit-msg')
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
    'late-commit-message-mutation',
    'platform-command-failure',
    'platform-missing-terminal',
    'platform-wrong-case',
    'late-framework-evidence-mutation'
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
    $start.Environment.Remove('SSH_AUTH_SOCK') | Out-Null
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

function Get-FixtureSigning {
    param($Owner)
    if ($null -ne $Owner.Signing) { return $Owner.Signing }
    Assert-Condition (-not $Owner.SigningAttempted) 'signing-already-attempted'
    $Owner.SigningAttempted = $true
    Get-RemainingMilliseconds $Owner | Out-Null
    if ($null -eq ('FishAudio.Testing.Fixtures.GitFixtureSigning' -as [type])) {
        Add-Type -Path (Join-Path $sourceRoot $signingSource)
    }
    Get-RemainingMilliseconds $Owner | Out-Null
    # This direct Temp child is deliberately outside both the repository and
    # archived evidence. Generate owns creation; cleanup also covers keygen failure.
    $signingId = [Guid]::NewGuid().ToString('N')
    $signingRoot = [IO.Path]::GetFullPath((Join-Path $temporaryRoot ('fa-hook-sign-' + $signingId)))
    Assert-Condition (-not [IO.Directory]::Exists($signingRoot) -and -not [IO.File]::Exists($signingRoot)) 'signing-root-exists'
    $Owner.SigningId = $signingId
    $Owner.SigningRoot = $signingRoot
    $keygen = [FishAudio.Testing.Fixtures.GitFixtureSigning]::ResolveSshKeygenExecutable($gitPath)
    $invoke = [Action[string, string[]]]{
        param([string]$Executable, [string[]]$Arguments)
        $generated = Invoke-OwnedProcess $Owner $Executable $Arguments $Owner.FixtureRoot 'signing-keygen'
        Assert-Condition ($generated.ExitCode -eq 0) 'signing-keygen-failed'
    }
    $Owner.Signing = [FishAudio.Testing.Fixtures.GitFixtureSigning]::Generate(
        $Owner.SigningRoot, $keygen, 'hook-acceptance@example.invalid', $invoke)
    Write-FixtureText (Join-Path $Owner.Evidence 'signing.json') ([ordered]@{
        schemaVersion = 1
        format = 'ssh'
        identity = $Owner.Signing.UserIdentity
        publicKey = $Owner.Signing.SshPublicKey
    } | ConvertTo-Json)
    return $Owner.Signing
}

function Invoke-FixtureCommit {
    param($Owner, [string]$Root, [hashtable]$Environment = @{})
    $beforePlatform = @(Get-PlatformInvocationPaths $Owner)
    $beforeCatalog = @(Get-PlatformCatalogPaths $Owner)
    $beforePlatformVerifier = @(Get-PlatformVerifierPaths $Owner)
    $beforePlan = @(Get-PlatformPlanPaths $Owner)
    $signing = Get-FixtureSigning $Owner
    $arguments = @($signing.ConfigurationArguments()) + @('-c', 'user.name=Hook Acceptance', '-c', 'user.email=hook-acceptance@example.invalid', 'commit', '-S', '-m', 'test: exercise installed commit hook')
    $result = Invoke-FixtureGit $Owner $Root $arguments $Environment -AllowFailure
    if ($result.ExitCode -eq 0) {
        $objectId = (Invoke-FixtureGit $Owner $Root @('rev-parse', '--verify', 'HEAD')).Output.Trim()
        Assert-Condition ($objectId -cmatch '^(?:[a-f0-9]{40}|[a-f0-9]{64})$') 'signed-commit-object-invalid'
        Invoke-FixtureGit $Owner $Root @($signing.VerifyCommitArguments($objectId)) $Environment | Out-Null
        $tree = (Invoke-FixtureGit $Owner $Root @('rev-parse', ($objectId + '^{tree}'))).Output.Trim()
        Assert-HybridSuccess $Owner $Root $tree $beforePlatform $beforeCatalog $beforePlatformVerifier $beforePlan
    }
    return $result
}

function Get-InvocationPaths {
    param($Owner)
    return @([IO.Directory]::GetFiles($Owner.Invocations, 'invocation-*.json'))
}

function Get-VerifierPaths {
    param($Owner)
    return @([IO.Directory]::GetFiles($Owner.Invocations, 'verifier-*.json'))
}

function Get-PlatformInvocationPaths($Owner) {
    return @([IO.Directory]::GetFiles($Owner.Invocations, 'platform-invocation-*.json'))
}

function Get-PlatformCatalogPaths($Owner) {
    return @([IO.Directory]::GetFiles($Owner.Invocations, 'platform-catalog-*.json'))
}

function Get-PlatformVerifierPaths($Owner) {
    return @([IO.Directory]::GetFiles($Owner.Invocations, 'platform-verifier-*.json'))
}

function Get-PlatformPlanPaths($Owner) {
    return @([IO.Directory]::GetFiles($Owner.Invocations, 'platform-plan-*.json'))
}

function Get-NewPlatformInvocation($Owner, [string[]]$Before) {
    $new = @(Get-PlatformInvocationPaths $Owner | Where-Object { $Before -cnotcontains $_ })
    Assert-Condition ($new.Count -eq 1) 'expected-one-platform-invocation'
    return [IO.File]::ReadAllText($new[0]) | ConvertFrom-Json
}

function Assert-HybridSuccess {
    param($Owner, [string]$Root, [string]$Tree, [string[]]$BeforePlatform,
        [string[]]$BeforeCatalog, [string[]]$BeforeVerifier, [string[]]$BeforePlan)
    $invocation = Get-NewPlatformInvocation $Owner $BeforePlatform
    Assert-Condition ($invocation.syntheticBoundaryControl -and $invocation.mode -ceq 'Full' -and
        $invocation.rootMatchesContext -and $invocation.externalResults -and $invocation.passed -and
        $invocation.context.tree -ceq $Tree) 'platform-not-run-for-current-snapshot'
    Assert-Condition (-not (Test-ContainedPath $invocation.resultsDirectory $invocation.snapshotRoot) -and
        -not (Test-ContainedPath $invocation.snapshotRoot $Root)) 'platform-used-working-tree-or-internal-results'
    $catalogs = @(Get-PlatformCatalogPaths $Owner | Where-Object { $BeforeCatalog -cnotcontains $_ })
    $verifiers = @(Get-PlatformVerifierPaths $Owner | Where-Object { $BeforeVerifier -cnotcontains $_ })
    $plans = @(Get-PlatformPlanPaths $Owner | Where-Object { $BeforePlan -cnotcontains $_ })
    Assert-Condition ($catalogs.Count -eq 2 -and $plans.Count -eq 2 -and $verifiers.Count -eq 2) 'platform-not-independently-reparsed'
    foreach ($kind in @($catalogs, $plans, $verifiers)) {
        $keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($path in $kind) {
            $record = [IO.File]::ReadAllText($path) | ConvertFrom-Json
            Assert-Condition ($record.syntheticBoundaryControl -and $record.valid -and
                $record.tree -ceq $Tree -and $keys.Add([string]$record.key)) 'platform-metadata-verification-not-current'
        }
        Assert-Condition ($keys.SetEquals([string[]]@('plugin', 'testing-tool'))) 'platform-metadata-assembly-set-mismatch'
    }
    $manifest = [IO.File]::ReadAllText((Join-Path $invocation.resultsDirectory 'manifest.json')) | ConvertFrom-Json
    Assert-Condition ($manifest.schemaVersion -eq 2 -and $manifest.workers -eq 4) 'platform-phase-schema-mismatch'
    $run = [IO.Path]::GetDirectoryName($invocation.resultsDirectory)
    $gate = [IO.File]::ReadAllText((Join-Path $run 'gate.json')) | ConvertFrom-Json
    $requiredArtifacts = [Collections.Generic.List[string]]::new()
    $requiredArtifacts.Add('platform/manifest.json')
    foreach ($assembly in $manifest.assemblies) {
        $key = [string]$assembly.key
        $groups = @($assembly.executionPhases)
        Assert-Condition ($groups.Count -eq 1 -and $groups[0].id -ceq 'phase-0001' -and
            $groups[0].kind -ceq 'ordinary' -and $null -eq $groups[0].profile -and
            -not $groups[0].requireOrder -and $groups[0].maximumConcurrency -eq 4 -and
            @($groups[0].exclusiveResources).Count -eq 0 -and $groups[0].executed -and
            $groups[0].passedCount -eq 1 -and @($groups[0].selectedIds).Count -eq 1) 'platform-phase-fixture-shape'
        foreach ($name in @('catalog', 'selection', 'requestedSelection', 'executionPlan')) {
            $requiredArtifacts.Add('platform/' + $assembly.$name)
        }
        foreach ($name in @('selection', 'request', 'runSettings', 'terminal', 'resultXml', 'trx', 'supervisor')) {
            $requiredArtifacts.Add('platform/' + $groups[0].$name)
        }
        $discovery = @($manifest.phases | Where-Object name -CEQ ('discover-' + $key))
        Assert-Condition ($discovery.Count -eq 1) 'platform-discovery-phase-missing'
        $requiredArtifacts.Add('platform/' + $discovery[0].directory + '/discovery.dump')
        foreach ($label in @('catalog', 'requested', 'plan')) {
            $requiredArtifacts.Add('framework-' + $label + '-' + $key + '.json')
        }
        $requiredArtifacts.Add('framework-terminal-' + $key + '-phase-0001.json')
    }
    foreach ($relative in $requiredArtifacts) {
        $pins = @($gate.platformArtifacts | Where-Object path -CEQ $relative)
        Assert-Condition ($pins.Count -eq 1 -and
            (Get-FileHash -LiteralPath (Join-Path $run $relative) -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $pins[0].sha256) 'platform-phase-artifact-not-receipted'
    }
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
param([switch]$Release, [switch]$Test, [switch]$LegacyOnly, [string]$TestResults)
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
& dotnet build $verifierProject --configuration Release --nologo --verbosity quiet --ignore-failed-sources --disable-build-servers
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
# One compiled boundary control supplies the legacy verifier and the independent
# platform metadata commands. These copied binaries do not contain NUnit tests.
$binaryBase = Join-Path $root 'tests/STranslate.Plugin.Tts.FishAudio.Tests/bin/Release/net10.0-windows/STranslate.Plugin.Tts.FishAudio.Tests'
foreach ($relative in @(
    'tools/FishAudio.Testing/bin/Release/net10.0-windows/FishAudio.Testing',
    'tests/FishAudio.Testing.Tests/bin/Release/net10.0-windows/FishAudio.Testing.Tests'
)) {
    $destinationBase = Join-Path $root $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destinationBase)) | Out-Null
    foreach ($extension in @('.dll', '.runtimeconfig.json', '.deps.json')) {
        [IO.File]::Copy($binaryBase + $extension, $destinationBase + $extension, $false)
    }
}
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
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

if (args.Length != 0 && args[0] == "evidence-catalog")
    return RunCatalog(args);
if (args.Length != 0 && args[0] == "evidence-execution-plan")
    return RunExecutionPlan(args);
if (args.Length != 0 && args[0] == "evidence-verify")
    return RunPlatformVerifier(args);
if (args.Length != 0 && args[0] == "evidence-migration")
    return RunMigrationVerifier(args);

var legacyValid = false;
var legacyTree = args.Length is 3 or 5 ? args[2] : string.Empty;
try
{
    if ((args.Length != 3 && (args.Length != 5 || args[3] != "--catalog-output")) || args[0] != "--verify-snapshot-report")
        return 1;

    using var document = JsonDocument.Parse(File.ReadAllBytes(args[1]));
    var report = document.RootElement;
    var source = report.GetProperty("source");
    var cases = report.GetProperty("cases");
    legacyValid = report.GetProperty("schemaVersion").GetInt32() == 1 &&
        report.GetProperty("complete").GetBoolean() &&
        report.GetProperty("configuration").GetString() == "Release" &&
        source.GetProperty("available").GetBoolean() &&
        source.GetProperty("snapshot").GetBoolean() &&
        source.GetProperty("tree").GetString() == legacyTree &&
        cases.GetArrayLength() == 1 &&
        cases[0].GetProperty("id").GetString() == "fixture/pass" &&
        cases[0].GetProperty("outcome").GetString() == "passed";
    if (legacyValid && args.Length == 5)
        WriteNew(args[4], new { schemaVersion = 1, kind = "verified-legacy-catalog", sourceTree = legacyTree,
            cases = new[] { new { id = "fixture/pass", partition = "core" } } });
}
catch
{
    legacyValid = false;
}
finally
{
    var evidence = Environment.GetEnvironmentVariable("FISHAUDIO_ACCEPTANCE_EVIDENCE");
    if (!string.IsNullOrWhiteSpace(evidence))
    {
        var path = Path.Combine(evidence, "verifier-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { tree = legacyTree, valid = legacyValid }));
    }
}

return legacyValid ? 0 : 1;

static Dictionary<string, string> ReadOptions(string[] arguments, params string[] required)
{
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 1; index < arguments.Length; index += 2)
    {
        if (index + 1 >= arguments.Length || !Path.IsPathFullyQualified(arguments[index + 1]) ||
            !values.TryAdd(arguments[index], arguments[index + 1])) throw new InvalidDataException();
    }
    if (values.Count != required.Length || required.Any(key => !values.ContainsKey(key)))
        throw new InvalidDataException();
    return values;
}

static string ReadKey(string canonicalId)
{
    return canonicalId switch
    {
        "fixture.platform/plugin" => "plugin",
        "fixture.platform/testing-tool" => "testing-tool",
        _ => throw new InvalidDataException(),
    };
}

static int RunMigrationVerifier(string[] arguments)
{
    // This executable is a synthetic hook-boundary producer, not the real
    // pinned-reference reader or evidence of product assertion conservation.
    try
    {
        var values = ReadOptions(arguments, "--reference", "--legacy", "--discoveries", "--migrations", "--output");
        using var reference = JsonDocument.Parse(File.ReadAllBytes(values["--reference"]));
        using var legacy = JsonDocument.Parse(File.ReadAllBytes(values["--legacy"]));
        using var discoveries = JsonDocument.Parse(File.ReadAllBytes(values["--discoveries"]));
        using var migrations = JsonDocument.Parse(File.ReadAllBytes(values["--migrations"]));
        if (reference.RootElement.GetProperty("referenceKind").GetString() != "synthetic-hook-fixture" ||
            reference.RootElement.GetProperty("cases")[0].GetProperty("id").GetString() != "fixture/pass" ||
            legacy.RootElement.GetProperty("cases").GetArrayLength() != 1 ||
            legacy.RootElement.GetProperty("cases")[0].GetProperty("id").GetString() != "fixture/pass" ||
            migrations.RootElement.GetProperty("migrations").GetArrayLength() != 0) return 1;
        var paths = discoveries.RootElement.GetProperty("paths").EnumerateArray().Select(item => item.GetString()!).ToArray();
        if (paths.Length != 2 || paths.Any(path => !Path.IsPathFullyQualified(path) || !File.Exists(path))) return 1;
        WriteNew(values["--output"], new { schemaVersion = 1, kind = "migration-conservation", complete = true,
            originalCount = 1, legacyCount = 1, migratedCount = 0, baselineClaim = false,
            syntheticBoundaryControl = true, evidence = new { checkedOwners = new[] { new { originalId = "fixture/pass", owner = 0 } } } });
        return 0;
    }
    catch { return 1; }
}

static object CaseRecord(string key)
{
    var name = key == "plugin" ? "Plugin" : "TestingTool";
    return new { canonicalId = "fixture.platform/" + key, fullName = "Fixture.Platform." + name,
        platformId = key == "plugin" ? "0-1" : "0-2", className = "Fixture.Platform", methodName = name };
}

static void WriteNew(string path, object value)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    JsonSerializer.Serialize(output, value);
}

static void RecordPlatformCommand(string command, string key, bool valid)
{
    var evidence = Environment.GetEnvironmentVariable("FISHAUDIO_ACCEPTANCE_EVIDENCE");
    if (string.IsNullOrWhiteSpace(evidence)) return;
    var tree = string.Empty;
    var contextPath = Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT");
    if (!string.IsNullOrWhiteSpace(contextPath))
    {
        using var context = JsonDocument.Parse(File.ReadAllBytes(contextPath));
        tree = context.RootElement.GetProperty("tree").GetString()!;
    }
    WriteNew(Path.Combine(evidence, "platform-" + command + "-" + Guid.NewGuid().ToString("N") + ".json"),
        new { syntheticBoundaryControl = true, command, key, tree, valid });
}

static int RunCatalog(string[] arguments)
{
    var valid = false;
    var key = "unavailable";
    try
    {
        var values = ReadOptions(arguments, "--discovery", "--output");
        var discovery = XDocument.Load(values["--discovery"]).Root!;
        var leaf = discovery.Descendants("test-case").Single();
        key = ReadKey(leaf.Descendants("property").Single(item => (string?)item.Attribute("name") == "FishAudioCaseId")
            .Attribute("value")!.Value);
        var name = key == "plugin" ? "Plugin" : "TestingTool";
        valid = discovery.Name == "test-run" && discovery.Attribute("result") is null &&
            (string?)discovery.Attribute("runstate") == "Runnable" &&
            (string?)discovery.Attribute("testcasecount") == "1" &&
            (string?)leaf.Attribute("fullname") == "Fixture.Platform." + name &&
            (string?)leaf.Attribute("classname") == "Fixture.Platform" &&
            (string?)leaf.Attribute("methodname") == name;
        if (valid) WriteNew(values["--output"], new { schemaVersion = 1, kind = "nunit-catalog", complete = true,
            cases = new[] { CaseRecord(key) } });
    }
    catch { valid = false; }
    finally { RecordPlatformCommand("catalog", key, valid); }
    return valid ? 0 : 1;
}

static int RunPlatformVerifier(string[] arguments)
{
    var valid = false;
    var key = "unavailable";
    try
    {
        var values = ReadOptions(arguments, "--catalog", "--selection", "--result", "--trx", "--output");
        using var catalog = JsonDocument.Parse(File.ReadAllBytes(values["--catalog"]));
        using var selection = JsonDocument.Parse(File.ReadAllBytes(values["--selection"]));
        var cases = catalog.RootElement.GetProperty("cases");
        var item = cases[0];
        var id = item.GetProperty("canonicalId").GetString()!;
        key = ReadKey(id);
        var fullName = item.GetProperty("fullName").GetString()!;
        var ids = selection.RootElement.GetProperty("ids");
        var result = XDocument.Load(values["--result"]).Root!;
        var leaf = result.Descendants("test-case").Single();
        var trx = XDocument.Load(values["--trx"]).Root!;
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var platformResult = trx.Descendants(ns + "UnitTestResult").Single();
        valid = catalog.RootElement.GetProperty("schemaVersion").GetInt32() == 1 &&
            catalog.RootElement.GetProperty("kind").GetString() == "nunit-catalog" &&
            catalog.RootElement.GetProperty("complete").GetBoolean() && cases.GetArrayLength() == 1 &&
            selection.RootElement.GetProperty("schemaVersion").GetInt32() == 1 &&
            ids.GetArrayLength() == 1 && ids[0].GetString() == id &&
            result.Name == "test-run" && (string?)result.Attribute("result") == "Passed" &&
            (string?)result.Attribute("total") == "1" && (string?)result.Attribute("passed") == "1" &&
            (string?)leaf.Attribute("fullname") == fullName && (string?)leaf.Attribute("result") == "Passed" &&
            leaf.Descendants("property").Single(node => (string?)node.Attribute("name") == "FishAudioCaseId")
                .Attribute("value")!.Value == id &&
            (string?)platformResult.Attribute("testName") == fullName &&
            (string?)platformResult.Attribute("outcome") == "Passed";
        if (valid) WriteNew(values["--output"], new { schemaVersion = 1, kind = "nunit-terminal", complete = true,
            passedCount = 1, cases = new[] { new { canonicalId = id, fullName, outcome = "passed" } } });
    }
    catch { valid = false; }
    finally { RecordPlatformCommand("verifier", key, valid); }
    return valid ? 0 : 1;
}

static int RunExecutionPlan(string[] arguments)
{
    var valid = false;
    var key = "unavailable";
    try
    {
        // This supports only the declared single ordinary case. It reads raw
        // bytes and selection independently; it is not a general NUnit planner.
        var values = ReadOptions(arguments, "--discovery", "--selection", "--output");
        using var raw = new FileStream(values["--discovery"], FileMode.Open, FileAccess.Read, FileShare.Read);
        var discovery = XDocument.Load(raw).Root!;
        var leaf = discovery.Descendants("test-case").Single();
        var properties = leaf.Elements("properties").Elements("property").ToArray();
        var canonicalId = properties.Single(item => (string?)item.Attribute("name") == "FishAudioCaseId").Attribute("value")!.Value;
        key = ReadKey(canonicalId);
        var name = key == "plugin" ? "Plugin" : "TestingTool";
        var platformId = key == "plugin" ? "0-1" : "0-2";
        var fixture = leaf.Ancestors("test-suite").FirstOrDefault(item => (string?)item.Attribute("type") == "TestFixture");
        using var selection = JsonDocument.Parse(File.ReadAllBytes(values["--selection"]));
        var request = selection.RootElement;
        var ids = request.GetProperty("ids");
        var maximum = request.GetProperty("maximumConcurrency").GetInt32();
        valid = discovery.Name == "test-run" && discovery.Attribute("result") is null &&
            (string?)discovery.Attribute("runstate") == "Runnable" && (string?)discovery.Attribute("testcasecount") == "1" &&
            (string?)leaf.Attribute("id") == platformId && (string?)leaf.Attribute("runstate") == "Runnable" &&
            (string?)leaf.Attribute("fullname") == "Fixture.Platform." + name &&
            (string?)leaf.Attribute("classname") == "Fixture.Platform" && (string?)leaf.Attribute("methodname") == name &&
            (string?)fixture?.Attribute("fullname") == "Fixture.Platform" && properties.Length == 1 &&
            !discovery.Descendants("property").Any(item => (string?)item.Attribute("name") is
                "FishAudioFreshHost" or "FishAudioExecutionProfile" or "FishAudioProfileOrdinal" or "FishAudioExclusiveResource") &&
            request.EnumerateObject().Count() == 3 && request.GetProperty("schemaVersion").GetInt32() == 1 &&
            ids.ValueKind == JsonValueKind.Array && ids.GetArrayLength() == 1 && ids[0].GetString() == canonicalId &&
            maximum is >= 1 and <= 4;
        if (valid)
        {
            raw.Position = 0;
            var digest = Convert.ToHexStringLower(SHA256.HashData(raw));
            WriteNew(values["--output"], new { schemaVersion = 1, kind = "nunit-execution-plan", complete = true,
                plan = new { rawDiscoverySha256 = digest, requestedIds = new[] { canonicalId }, expandedIds = new[] { canonicalId },
                    expansionReasons = Array.Empty<object>(), phases = new[] { new { id = "phase-0001", kind = "ordinary",
                        profile = (string?)null, cases = new[] { CaseRecord(key) }, requireOrder = false,
                        maximumConcurrency = maximum, exclusiveResources = Array.Empty<string>() } } } });
        }
    }
    catch { valid = false; }
    finally { RecordPlatformCommand("plan", key, valid); }
    return valid ? 0 : 1;
}
'@

$fixturePlatformDriver = @'
[CmdletBinding()]
param(
    [string]$Mode = 'Full', [string]$ResultsDirectory, [string]$RepositoryRoot,
    [switch]$NoBuild, [int]$Workers = 4, [string[]]$CaseId = @()
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$encoding = [Text.UTF8Encoding]::new($false)
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($RepositoryRoot -and -not $root.Equals([IO.Path]::GetFullPath($RepositoryRoot), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'fixture-platform-root-mismatch'
}
$context = [IO.File]::ReadAllText($env:FISHAUDIO_SNAPSHOT_CONTEXT) | ConvertFrom-Json
function Test-Under([string]$path, [string]$parent) {
    $prefix = [IO.Path]::GetFullPath($parent).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    return [IO.Path]::GetFullPath($path).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}
function Write-NewJson([string]$path, $value) {
    $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try {
        $bytes = $encoding.GetBytes(($value | ConvertTo-Json -Depth 20))
        $stream.Write($bytes, 0, $bytes.Length)
    } finally { $stream.Dispose() }
}
function Write-NewText([string]$path, [string]$value) {
    $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $bytes = $encoding.GetBytes($value); $stream.Write($bytes, 0, $bytes.Length) }
    finally { $stream.Dispose() }
}
function Get-Hash([string]$path) { return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
$privateState = (& git -C $env:FISHAUDIO_ACCEPTANCE_REPOSITORY rev-parse --path-format=absolute --git-path fish-audio-testing) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'fixture-platform-private-state-unavailable' }
$privateState = $privateState.Trim()
$rootMatches = [IO.Path]::GetFullPath($context.root).Equals($root, [StringComparison]::OrdinalIgnoreCase)
$externalResults = -not [string]::IsNullOrWhiteSpace($ResultsDirectory) -and
    [IO.Path]::IsPathFullyQualified($ResultsDirectory) -and (Test-Under $ResultsDirectory $privateState) -and
    -not (Test-Under $ResultsDirectory $root)
$fault = 'none'
$faultPath = Join-Path $root 'fixture-platform-mode.txt'
if ([IO.File]::Exists($faultPath)) { $fault = [IO.File]::ReadAllText($faultPath).Trim() }
$passed = $Mode -ceq 'Full' -and $Workers -eq 4 -and $CaseId.Count -eq 0 -and $rootMatches -and $externalResults -and $fault -cne 'command-failure'
$invocation = [ordered]@{
    syntheticBoundaryControl = $true; mode = $Mode; snapshotRoot = $root; context = $context
    rootMatchesContext = $rootMatches; externalResults = $externalResults; resultsDirectory = $ResultsDirectory
    passed = [bool]$passed; fault = $fault
}
$event = Join-Path $env:FISHAUDIO_ACCEPTANCE_EVIDENCE ('platform-invocation-' + [Guid]::NewGuid().ToString('N') + '.json')
Write-NewJson $event $invocation
if (-not $externalResults -or [IO.Directory]::Exists($ResultsDirectory) -or [IO.File]::Exists($ResultsDirectory)) {
    throw 'fixture-platform-results-not-fresh-or-private'
}
[IO.Directory]::CreateDirectory($ResultsDirectory) | Out-Null
if (-not $passed) {
    Write-NewJson (Join-Path $ResultsDirectory 'failure.json') @{ schemaVersion = 1; complete = $false; syntheticBoundaryControl = $true }
    exit 19
}
$assemblyRecords = [Collections.Generic.List[object]]::new()
$phaseRecords = [Collections.Generic.List[object]]::new()
$ordinal = 0
$platformRunId = [Guid]::NewGuid().ToString('N')
$platformFingerprint = Get-Hash $env:FISHAUDIO_SNAPSHOT_CONTEXT
$controlNonce = 'ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff'
function New-HostEvent($Kind, [long]$Timestamp, $AssemblySha256, $CanonicalId = $null, $FullName = $null, $Outcome = $null) {
    return [ordered]@{
        schemaVersion = 1; role = 'nunit-host'; nonce = $controlNonce; runId = $platformRunId; sourceTree = $context.tree
        pid = 1237; kind = $Kind; timestamp = $Timestamp; frequency = 1000L; assemblySha256 = $AssemblySha256
        caseId = $CanonicalId; fullName = $FullName; ordinal = 0; queuedTimestamp = 0L; outcome = $Outcome
        targetPid = $null; exitCode = $null
    }
}
function New-LauncherEvent($Kind, [long]$Timestamp, [Nullable[int]]$ExitCode = $null) {
    return [ordered]@{
        schemaVersion = 1; role = 'execution-launcher'; nonce = $controlNonce; runId = $platformRunId; sourceTree = $context.tree
        pid = 1236; kind = $Kind; timestamp = $Timestamp; frequency = 1000L; assemblySha256 = $null
        caseId = $null; fullName = $null; ordinal = 0; queuedTimestamp = 0L; outcome = $null
        targetPid = 1235; exitCode = $ExitCode
    }
}
foreach ($key in @('plugin', 'testing-tool')) {
    $directory = Join-Path $ResultsDirectory $key
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $name = if ($key -ceq 'plugin') { 'Plugin' } else { 'TestingTool' }
    $canonicalId = 'fixture.platform/' + $key
    $fullName = 'Fixture.Platform.' + $name
    $platformId = if ($key -ceq 'plugin') { '0-1' } else { '0-2' }
    $case = [ordered]@{ canonicalId = $canonicalId; fullName = $fullName; platformId = $platformId; className = 'Fixture.Platform'; methodName = $name }
    Write-NewJson (Join-Path $directory 'catalog.json') @{ schemaVersion = 1; kind = 'nunit-catalog'; complete = $true; cases = @($case) }
    Write-NewJson (Join-Path $directory 'selection.json') @{ schemaVersion = 1; ids = @($canonicalId) }
    Write-NewJson (Join-Path $directory 'requested-selection.json') @{ schemaVersion = 1; ids = @($canonicalId); maximumConcurrency = 4 }
    $phaseRelative = $key + '/execution/phase-0001'
    $executionDirectory = Join-Path $ResultsDirectory $phaseRelative
    $supervised = Join-Path $executionDirectory 'supervised'
    $runtimeDirectory = Join-Path $executionDirectory 'runtime-evidence'
    foreach ($child in @('supervised/nunit', 'supervised/trx', 'runtime-evidence')) {
        [IO.Directory]::CreateDirectory((Join-Path $executionDirectory $child)) | Out-Null
    }
    $assemblyPath = if ($key -ceq 'plugin') {
        'tests/STranslate.Plugin.Tts.FishAudio.Tests/bin/Release/net10.0-windows/STranslate.Plugin.Tts.FishAudio.Tests.dll'
    } else { 'tests/FishAudio.Testing.Tests/bin/Release/net10.0-windows/FishAudio.Testing.Tests.dll' }
    $projectPath = if ($key -ceq 'plugin') {
        'tests/STranslate.Plugin.Tts.FishAudio.Tests/STranslate.Plugin.Tts.FishAudio.Tests.csproj'
    } else { 'tests/FishAudio.Testing.Tests/FishAudio.Testing.Tests.csproj' }
    $xmlName = [IO.Path]::GetFileNameWithoutExtension($assemblyPath) + '.xml'
    $resultPath = [IO.Path]::GetFullPath((Join-Path $supervised ('nunit/' + $xmlName)))
    $trxPath = [IO.Path]::GetFullPath((Join-Path $supervised 'trx/results.trx'))
    Write-NewJson (Join-Path $executionDirectory 'selection.json') @{ schemaVersion = 1; ids = @($canonicalId) }
    Write-NewJson (Join-Path $executionDirectory 'terminal.json') @{
        schemaVersion = 1; kind = 'nunit-terminal'; complete = $true; passedCount = 1
        cases = @(@{ canonicalId = $canonicalId; fullName = $fullName; outcome = 'passed'; duration = '00:00:00.0010000'; platformDuration = '00:00:00.0010000' })
    }
    $actualId = if ($fault -ceq 'wrong-case' -and $key -ceq 'plugin') { 'fixture.platform/unexpected' } else { $canonicalId }
    $actualName = if ($fault -ceq 'wrong-case' -and $key -ceq 'plugin') { 'Fixture.Platform.Unexpected' } else { $fullName }
    $resultXml = '<test-run result="Passed" total="1" passed="1"><test-case fullname="' + $actualName + '" result="Passed"><properties><property name="FishAudioCaseId" value="' + $actualId + '" /></properties></test-case></test-run>'
    $trx = '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult testName="' + $fullName + '" outcome="Passed" /></Results></TestRun>'
    Write-NewText $resultPath $resultXml
    Write-NewText $trxPath $trx
    $discoveryPath = $null
    foreach ($phaseName in @('discover', 'catalog', 'plan', 'execute', 'verify')) {
        $ordinal++
        $label = $phaseName + '-' + $key
        if ($phaseName -cin @('execute', 'verify')) { $label += '-phase-0001' }
        $relative = 'phases/{0:D3}-{1}' -f $ordinal, $label
        $phase = Join-Path $ResultsDirectory $relative
        [IO.Directory]::CreateDirectory($phase) | Out-Null
        if ($phaseName -ceq 'discover') {
            $discovery = '<test-run runstate="Runnable" testcasecount="1"><test-suite type="TestFixture" fullname="Fixture.Platform" runstate="Runnable" testcasecount="1"><test-case id="' + $platformId + '" name="' + $name + '" fullname="' + $fullName + '" classname="Fixture.Platform" methodname="' + $name + '" runstate="Runnable"><properties><property name="FishAudioCaseId" value="' + $canonicalId + '" /></properties></test-case></test-suite></test-run>'
            $discoveryPath = [IO.Path]::GetFullPath((Join-Path $phase 'discovery.dump'))
            Write-NewText $discoveryPath $discovery
        }
        Write-NewJson (Join-Path $phase 'argv.json') @{ syntheticBoundaryControl = $true; phase = $phaseName; key = $key }
        Write-NewJson (Join-Path $phase 'process.json') @{
            schemaVersion = 1; syntheticBoundaryControl = $true; complete = $true; exitCode = 0
            outputDrained = $true; allChildrenExited = $true
        }
        Write-NewText (Join-Path $phase 'stdout.log') 'synthetic hook boundary control'
        Write-NewText (Join-Path $phase 'stderr.log') ''
        Write-NewText (Join-Path $phase 'exit-code.txt') '0'
        $phaseRecords.Add(@{ name = $label; directory = $relative; complete = $true; exitCode = 0 })
    }
    # The plan has only the real API fields. Synthetic provenance belongs to
    # the surrounding fixture manifest, not the strict plan/request contracts.
    Write-NewJson (Join-Path $directory 'execution-plan.json') @{
        schemaVersion = 1; kind = 'nunit-execution-plan'; complete = $true
        plan = @{ rawDiscoverySha256 = Get-Hash $discoveryPath; requestedIds = @($canonicalId); expandedIds = @($canonicalId)
            expansionReasons = @(); phases = @(@{ id = 'phase-0001'; kind = 'ordinary'; profile = $null; cases = @($case)
                requireOrder = $false; maximumConcurrency = 4; exclusiveResources = @() }) }
    }
    $settingsPath = [IO.Path]::GetFullPath((Join-Path $executionDirectory 'execution.runsettings'))
    $regex = '\A' + [Text.RegularExpressions.Regex]::Escape($fullName) + '\z'
    $filter = '(test =~ "' + $regex.Replace('\', '\\').Replace('"', '\"') + '")'
    $settings = '<RunSettings><RunConfiguration><MaxCpuCount>1</MaxCpuCount><TreatNoTestsAsError>true</TreatNoTestsAsError></RunConfiguration><NUnit><NumberOfTestWorkers>4</NumberOfTestWorkers><PreFilter>false</PreFilter><UseParentFQNForParametrizedTests>false</UseParentFQNForParametrizedTests><IncludeStackTrace>false</IncludeStackTrace><DumpXmlTestDiscovery>true</DumpXmlTestDiscovery><DumpXmlTestResults>false</DumpXmlTestResults><TestOutputXml>' +
        [Security.SecurityElement]::Escape([IO.Path]::GetFullPath((Join-Path $supervised 'nunit'))) + '</TestOutputXml><OutputXmlFolderMode>AsSpecified</OutputXmlFolderMode><StopOnError>true</StopOnError><Where>' +
        [Security.SecurityElement]::Escape($filter) + '</Where></NUnit></RunSettings>'
    Write-NewText $settingsPath $settings
    $assemblySha256 = Get-Hash (Join-Path $root $assemblyPath)
    $requestPath = [IO.Path]::GetFullPath((Join-Path $executionDirectory 'execution-request.json'))
    Write-NewJson $requestPath ([ordered]@{
        schemaVersion = 1; runId = $platformRunId; sourceTree = $context.tree; sourceFingerprint = $platformFingerprint
        workingDirectory = $root; shellExecutable = (Get-Process -Id $PID).Path
        dotnetExecutable = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
        launcherScript = Join-Path $root 'tools/Invoke-TestProcess.ps1'
        testAssembly = [IO.Path]::GetFullPath((Join-Path $root $assemblyPath)); testAssemblySha256 = $assemblySha256
        runSettings = $settingsPath; discoveryPath = $discoveryPath; resultXmlPath = $resultPath; trxPath = $trxPath
        outputDirectory = [IO.Path]::GetFullPath($supervised)
        cases = @(@{ canonicalId = $canonicalId; fullName = $fullName })
        maximumConcurrency = 4; requireOrder = $false
        environment = @{ FISHAUDIO_COMPAT_EVIDENCE_ROOT = [IO.Path]::GetFullPath($runtimeDirectory) }
    })
    $executionRecord = [ordered]@{
        id = 'phase-0001'; kind = 'ordinary'; profile = $null; requireOrder = $false; maximumConcurrency = 4
        exclusiveResources = @(); selectedIds = @($canonicalId); executed = $true; passedCount = 1
        runtimeEvidence = $phaseRelative + '/runtime-evidence'; markerRequired = $false; markerObserved = $false; parallelProof = $false
    }
    $phasePaths = [ordered]@{
        selection = 'selection.json'; request = 'execution-request.json'; runSettings = 'execution.runsettings'; terminal = 'terminal.json'
        resultXml = 'supervised/nunit/' + $xmlName; trx = 'supervised/trx/results.trx'; supervisor = 'supervised/supervisor.json'
    }
    foreach ($name in $phasePaths.Keys) {
        $executionRecord[$name] = $phaseRelative + '/' + $phasePaths[$name]
        if ($name -cne 'supervisor') { $executionRecord[$name + 'Sha256'] = Get-Hash (Join-Path $executionDirectory $phasePaths[$name]) }
    }
    $record = [ordered]@{
        key = $key; project = $projectPath; assembly = $assemblyPath; sha256 = $assemblySha256
        catalog = $key + '/catalog.json'; catalogSha256 = Get-Hash (Join-Path $directory 'catalog.json')
        selection = $key + '/selection.json'; selectionSha256 = Get-Hash (Join-Path $directory 'selection.json')
        requestedSelection = $key + '/requested-selection.json'; requestedSelectionSha256 = Get-Hash (Join-Path $directory 'requested-selection.json')
        executionPlan = $key + '/execution-plan.json'; executionPlanSha256 = Get-Hash (Join-Path $directory 'execution-plan.json')
        discoveredCount = 1; selectedCount = 1; passedCount = 1; selectedIds = @($canonicalId); executed = $true
        executionPhases = @($executionRecord)
    }
    Write-NewJson (Join-Path $supervised 'supervisor.json') @{
        schemaVersion = 1; kind = 'framework-execution'; syntheticBoundaryControl = $true
        runId = $platformRunId; sourceTree = $context.tree; sourceFingerprint = $platformFingerprint
        testAssemblySha256 = $record.sha256; baselineClaim = $false; failureCode = $null
        requestSha256 = $executionRecord.requestSha256; policy = @{ maximumConcurrency = 4; requireOrder = $false }
        launch = @{ kind = 'vstest'; sessionId = $null; invocationSha256 = '9999999999999999999999999999999999999999999999999999999999999999'
            toolSha256 = $null; vstestConsoleSha256 = $null }
        selectedIds = @($canonicalId)
        state = @{ complete = $true; stage = 'child-exit'; failureStage = $null; failureCaseId = $null
            terminationRequired = $false; passedIds = @($canonicalId); failedIds = @(); timedOutIds = @()
            activeIds = @(); interruptedIds = @(); notRunIds = @() }
        target = @{ started = $true; pid = 1235; exitCode = 0 }
        process = @{ launcherPid = 1236; launcherExited = $true; stdoutDrained = $true; stderrDrained = $true; ownedActiveProcesses = 0 }
        rawEvidence = @{ verified = $true; resultXmlPath = $resultPath; trxPath = $trxPath
            resultXmlSha256 = $executionRecord.resultXmlSha256; trxSha256 = $executionRecord.trxSha256 }
        events = @(
            (New-LauncherEvent 'target-started' 1000)
            (New-HostEvent 'ready' 1001 $assemblySha256)
            (New-HostEvent 'case-start' 1010 $assemblySha256 $canonicalId $fullName)
            (New-HostEvent 'case-finished' 1020 $assemblySha256 $canonicalId $fullName 'passed')
            (New-HostEvent 'cleanup-complete' 1030 $assemblySha256)
            (New-LauncherEvent 'target-exited' 1031 0)
        )
    }
    $executionRecord['supervisorSha256'] = Get-Hash (Join-Path $supervised 'supervisor.json')
    if ($fault -ceq 'missing-terminal' -and $key -ceq 'plugin') {
        [IO.File]::Delete((Join-Path $executionDirectory 'terminal.json'))
    }
    $assemblyRecords.Add($record)
}
Write-NewJson (Join-Path $ResultsDirectory 'manifest.json') ([ordered]@{
    schemaVersion = 2; kind = 'unified-test-session'; syntheticBoundaryControl = $true
    runId = $platformRunId; mode = 'Full'; platform = 'VSTest'; complete = $true
    configuration = 'Release'; workers = 4; selection = 'all-discovered'; baselineClaim = $false
    source = @{ snapshot = $true; tree = $context.tree; sourceCommit = $context.sourceCommit; commit = $context.sourceCommit
        snapshotRootValidated = $true; contextSha256 = Get-Hash $env:FISHAUDIO_SNAPSHOT_CONTEXT; fingerprint = $platformFingerprint }
    assemblies = $assemblyRecords.ToArray(); phases = $phaseRecords.ToArray()
    toolSha256 = Get-Hash (Join-Path $root 'tools/FishAudio.Testing/bin/Release/net10.0-windows/FishAudio.Testing.dll')
})
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
    Write-FixtureText (Join-Path $root 'tools/Test.ps1') $fixturePlatformDriver
    Write-FixtureText (Join-Path $root 'tests/Migration/legacy-catalog.json') '{"schemaVersion":1,"referenceKind":"synthetic-hook-fixture","cases":[{"id":"fixture/pass","partition":"core"}]}'
    Write-FixtureText (Join-Path $root 'tests/Migration/migrated-cases.json') '{"schemaVersion":1,"migrations":[]}'
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

function Invoke-PlatformFailureScenario {
    param($Owner, [ValidateSet('command-failure', 'missing-terminal', 'wrong-case')][string]$Fault)
    $root = New-FixtureRepository $Owner
    Write-FixtureText (Join-Path $root 'fixture-platform-mode.txt') ($Fault + "`n")
    Invoke-FixtureGit $Owner $root @('add', '--', 'fixture-platform-mode.txt') | Out-Null
    $tree = (Invoke-FixtureGit $Owner $root @('write-tree')).Output.Trim()
    $beforeBuild = @(Get-InvocationPaths $Owner)
    $beforeLegacyVerifier = @(Get-VerifierPaths $Owner)
    $beforePlatform = @(Get-PlatformInvocationPaths $Owner)
    $beforeCatalog = @(Get-PlatformCatalogPaths $Owner)
    $beforeVerifier = @(Get-PlatformVerifierPaths $Owner)
    $beforePlan = @(Get-PlatformPlanPaths $Owner)
    $commit = Invoke-FixtureCommit $Owner $root
    Assert-Condition ($commit.ExitCode -ne 0) ('platform-' + $Fault + '-allowed-commit')
    Assert-NoCommit $Owner $root
    $build = Get-NewInvocation $Owner $beforeBuild
    Assert-Snapshot $build $root $tree
    Assert-Condition $build.passed 'platform-failure-did-not-pass-legacy-build'
    $legacy = Get-NewVerifierInvocation $Owner $beforeLegacyVerifier
    Assert-Condition ($legacy.valid -and $legacy.tree -ceq $tree) 'platform-failure-did-not-pass-legacy-verifier'
    $platform = Get-NewPlatformInvocation $Owner $beforePlatform
    Assert-Condition ($platform.syntheticBoundaryControl -and $platform.fault -ceq $Fault -and
        $platform.context.tree -ceq $tree -and $platform.rootMatchesContext -and $platform.externalResults) 'platform-failure-boundary-not-reached'
    $run = [IO.Path]::GetDirectoryName($build.resultsPath)
    $gate = [IO.File]::ReadAllText((Join-Path $run 'gate.json')) | ConvertFrom-Json
    Assert-Condition (-not $gate.complete -and $gate.exitCode -ne 0 -and $gate.snapshotTree -ceq $tree) 'platform-failure-gate-outcome-not-retained'
    Assert-Condition (-not [IO.File]::Exists((Join-Path $build.privateStateRoot 'commit-receipt.json'))) 'platform-failure-created-receipt'
    $catalogs = @(Get-PlatformCatalogPaths $Owner | Where-Object { $beforeCatalog -cnotcontains $_ })
    $verifiers = @(Get-PlatformVerifierPaths $Owner | Where-Object { $beforeVerifier -cnotcontains $_ })
    $plans = @(Get-PlatformPlanPaths $Owner | Where-Object { $beforePlan -cnotcontains $_ })
    $manifestPath = Join-Path $platform.resultsDirectory 'manifest.json'
    switch -CaseSensitive ($Fault) {
        'command-failure' {
            Assert-Condition (-not $platform.passed -and -not [IO.File]::Exists($manifestPath) -and
                [IO.File]::Exists((Join-Path $platform.resultsDirectory 'failure.json'))) 'platform-command-failure-not-retained'
            Assert-Condition ($catalogs.Count -eq 0 -and $plans.Count -eq 0 -and $verifiers.Count -eq 0) 'failed-platform-command-reached-metadata-admission'
        }
        'missing-terminal' {
            $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
            $plugin = @($manifest.assemblies | Where-Object key -CEQ 'plugin')
            $groups = @($plugin[0].executionPhases)
            Assert-Condition ($platform.passed -and $manifest.complete -and $plugin.Count -eq 1 -and
                $groups.Count -eq 1 -and $groups[0].id -ceq 'phase-0001' -and
                -not [IO.File]::Exists((Join-Path $platform.resultsDirectory $groups[0].terminal))) 'missing-platform-terminal-fixture-invalid'
            Assert-Condition ($catalogs.Count -eq 1 -and $plans.Count -eq 1 -and $verifiers.Count -eq 0) 'missing-terminal-did-not-stop-at-phase-admission'
        }
        'wrong-case' {
            $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
            $plugin = @($manifest.assemblies | Where-Object key -CEQ 'plugin')
            $groups = @($plugin[0].executionPhases)
            Assert-Condition ($platform.passed -and $manifest.complete -and $plugin.Count -eq 1 -and
                $plugin[0].discoveredCount -eq 1 -and $plugin[0].selectedCount -eq 1 -and $plugin[0].passedCount -eq 1 -and
                $groups.Count -eq 1 -and $groups[0].executed -and $groups[0].passedCount -eq 1) 'wrong-platform-case-fixture-not-claiming-pass'
            $resultPath = Join-Path $platform.resultsDirectory $groups[0].resultXml
            Assert-Condition ((Get-FileHash -LiteralPath $resultPath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq
                $groups[0].resultXmlSha256) 'wrong-platform-case-hash-did-not-match-manifest'
            $resultXml = [xml][IO.File]::ReadAllText($resultPath)
            Assert-Condition ($resultXml.SelectSingleNode('/test-run/test-case').GetAttribute('fullname') -ceq
                'Fixture.Platform.Unexpected') 'wrong-platform-case-not-in-actual-result'
            $rejected = @($verifiers | ForEach-Object { [IO.File]::ReadAllText($_) | ConvertFrom-Json } |
                Where-Object { -not $_.valid -and $_.key -ceq 'plugin' -and $_.tree -ceq $tree })
            Assert-Condition ($catalogs.Count -ge 1 -and $plans.Count -ge 1 -and $rejected.Count -eq 1) 'wrong-platform-case-not-independently-rejected'
        }
    }
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
        'platform-command-failure' { Invoke-PlatformFailureScenario $Owner 'command-failure' }
        'platform-missing-terminal' { Invoke-PlatformFailureScenario $Owner 'missing-terminal' }
        'platform-wrong-case' { Invoke-PlatformFailureScenario $Owner 'wrong-case' }
        'late-framework-evidence-mutation' {
            $root = New-FixtureRepository $Owner -WithoutInstall
            $oldMessageHook = (Invoke-FixtureGit $Owner $root @('rev-parse', '--path-format=absolute', '--git-path', 'hooks/commit-msg')).Output.Trim()
            Write-FixtureText (Join-Path $root 'mutate-evidence.ps1') @'
$ErrorActionPreference = 'Stop'
$state = (git rev-parse --path-format=absolute --git-path fish-audio-testing).Trim()
if ($LASTEXITCODE -ne 0) { exit 29 }
$receipt = Get-Content -LiteralPath (Join-Path $state 'commit-receipt.json') -Raw | ConvertFrom-Json
$runRoot = Join-Path $state ('runs/' + $receipt.runId)
$gatePath = Join-Path $runRoot 'gate.json'
$gate = Get-Content -LiteralPath $gatePath -Raw | ConvertFrom-Json
$terminalPins = @($gate.platformArtifacts | Where-Object path -CEQ 'platform/plugin/execution/phase-0001/terminal.json')
if ($terminalPins.Count -ne 1) { throw 'fixture-phase-terminal-receipt-missing' }
$artifact = $terminalPins[0]
$path = Join-Path $runRoot $artifact.path
[IO.File]::AppendAllText($path, "`n ", [Text.UTF8Encoding]::new($false))
$artifact.sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($gatePath, ($gate | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText('evidence-hook-ran.marker', 'mutated', [Text.UTF8Encoding]::new($false))
exit 0
'@
            Write-FixtureText $oldMessageHook "#!/bin/sh`nexec pwsh -NoLogo -NoProfile -File ./mutate-evidence.ps1`n"
            Invoke-FixtureInstaller $Owner $root Install | Out-Null
            $tree = (Invoke-FixtureGit $Owner $root @('write-tree')).Output.Trim()
            $before = @(Get-InvocationPaths $Owner)
            $commit = Invoke-FixtureCommit $Owner $root
            Assert-Condition ($commit.ExitCode -ne 0) 'late-evidence-mutation-allowed-commit'
            Assert-NoCommit $Owner $root
            Assert-Condition ([IO.File]::Exists((Join-Path $root 'evidence-hook-ran.marker'))) 'evidence-message-hook-not-invoked'
            Assert-Snapshot (Get-NewInvocation $Owner $before) $root $tree
            Assert-Condition ((Invoke-FixtureGit $Owner $root @('write-tree')).Output.Trim() -ceq $tree) 'evidence-fixture-changed-index'
        }
        default { throw 'hook-acceptance-unknown-scenario' }
    }
}

function Copy-OwnedEvidenceDirectory {
    param($Owner, [string]$Source, [string]$Destination)
    Assert-Condition (Test-ContainedPath $Source $Owner.FixtureRoot) 'foreign-platform-evidence-source'
    Assert-Condition (Test-ContainedPath $Destination $Owner.Evidence) 'foreign-platform-evidence-destination'
    $pending = [Collections.Generic.Queue[string]]::new()
    $pending.Enqueue($Source)
    while ($pending.Count -gt 0) {
        $directory = $pending.Dequeue()
        Assert-Condition (((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'platform-evidence-directory-reparse'
        $relative = [IO.Path]::GetRelativePath($Source, $directory)
        $target = if ($relative -ceq '.') { $Destination } else { Join-Path $Destination $relative }
        [IO.Directory]::CreateDirectory($target) | Out-Null
        foreach ($file in [IO.Directory]::GetFiles($directory)) {
            Assert-Condition (((Get-Item -LiteralPath $file -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'platform-evidence-file-reparse'
            [IO.File]::Copy($file, (Join-Path $target ([IO.Path]::GetFileName($file))), $false)
        }
        foreach ($child in [IO.Directory]::GetDirectories($directory)) { $pending.Enqueue($child) }
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
            foreach ($name in @('gate.json', 'snapshot.json', 'regression.json', 'commit-final.json',
                'verified-legacy-catalog.json', 'migration-discoveries.json', 'migration-conservation.json')) {
                $source = Join-Path $run $name
                if ([IO.File]::Exists($source)) { [IO.File]::Copy($source, (Join-Path $destination $name), $false) }
            }
            foreach ($pattern in @('build*.log', 'verify*.log', 'platform*.log', 'framework-*')) {
                foreach ($source in [IO.Directory]::GetFiles($run, $pattern)) {
                    [IO.File]::Copy($source, (Join-Path $destination ([IO.Path]::GetFileName($source))), $false)
                }
            }
            $platform = Join-Path $run 'platform'
            if ([IO.Directory]::Exists($platform)) {
                Copy-OwnedEvidenceDirectory $Owner $platform (Join-Path $destination 'platform')
            }
            foreach ($pattern in @('framework-*', 'migration-conservation')) {
                foreach ($phase in [IO.Directory]::GetDirectories($run, $pattern)) {
                    Copy-OwnedEvidenceDirectory $Owner $phase (Join-Path $destination ([IO.Path]::GetFileName($phase)))
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

function Remove-OwnedSigningRoot {
    param($Owner)
    if ($null -eq $Owner.SigningRoot) { return }
    Assert-Condition ([IO.Path]::IsPathFullyQualified($Owner.SigningRoot)) 'signing-cleanup-not-absolute'
    $root = [IO.Path]::GetFullPath($Owner.SigningRoot)
    $temp = $temporaryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    Assert-Condition ($root -ceq $Owner.SigningRoot -and $Owner.SigningId -cmatch '^[a-f0-9]{32}$') 'signing-cleanup-owner-invalid'
    Assert-Condition ([IO.Path]::GetDirectoryName($root).Equals($temp, [StringComparison]::OrdinalIgnoreCase)) 'signing-cleanup-not-temp-child'
    Assert-Condition ([IO.Path]::GetFileName($root) -ceq ('fa-hook-sign-' + $Owner.SigningId)) 'signing-cleanup-wrong-owner'
    $ancestor = [IO.DirectoryInfo]::new($root).Parent
    while ($null -ne $ancestor) {
        $attributes = [IO.File]::GetAttributes($ancestor.FullName)
        Assert-Condition (($attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and
            ($attributes -band [IO.FileAttributes]::Directory) -ne 0) 'signing-cleanup-ancestor-invalid'
        $ancestor = $ancestor.Parent
    }
    try { $attributes = [IO.File]::GetAttributes($root) }
    catch [IO.FileNotFoundException] { return }
    catch [IO.DirectoryNotFoundException] { return }
    Assert-Condition (($attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and
        ($attributes -band [IO.FileAttributes]::Directory) -ne 0) 'signing-cleanup-root-invalid'
    $materialFailure = $null
    foreach ($name in @('fixture-ed25519', 'fixture-ed25519.pub', 'allowed_signers')) {
        $path = Join-Path $root $name
        try {
            try { $attributes = [IO.File]::GetAttributes($path) }
            catch [IO.FileNotFoundException] { continue }
            catch [IO.DirectoryNotFoundException] { continue }
            Assert-Condition (($attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and
                ($attributes -band [IO.FileAttributes]::Directory) -eq 0) 'signing-cleanup-material-invalid'
            [IO.File]::Delete($path)
        } catch { if ($null -eq $materialFailure) { $materialFailure = $_ } }
    }
    if ($null -ne $materialFailure) { throw $materialFailure }
    Assert-Condition ([IO.Directory]::GetFileSystemEntries($root).Length -eq 0) 'signing-cleanup-unexpected-entry'
    [IO.Directory]::Delete($root, $false)
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
            SigningAttempted = $false
            SigningId = $null
            SigningRoot = $null
            Signing = $null
        }
        [IO.Directory]::CreateDirectory($owner.FixtureRoot) | Out-Null
        [IO.Directory]::CreateDirectory($owner.Invocations) | Out-Null
        $failure = $null
        $fixtureRetained = $false
        $originalLocation = Get-Location
        try {
            Invoke-AcceptanceScenario $owner $name
        } catch {
            $failure = $_
            try { Write-PrivateFailure $failure (Join-Path $owner.Evidence 'failure.txt') } catch { }
        } finally {
            $cleanupFailure = $null
            try { Copy-FixtureGateEvidence $owner } catch { $cleanupFailure = $_; $fixtureRetained = $true }
            try { Remove-OwnedSigningRoot $owner } catch {
                $signingCleanupFailure = $_
                try { Write-PrivateFailure $signingCleanupFailure (Join-Path $owner.Evidence 'signing-cleanup.failure.txt') } catch { }
                if ($null -eq $cleanupFailure) { $cleanupFailure = $signingCleanupFailure }
            }
            try {
                Set-Location -LiteralPath $originalLocation.Path
                if (-not $fixtureRetained) { Remove-OwnedFixture $owner }
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
        $results.Add([pscustomobject]@{ id = $name; outcome = $outcome; durationMilliseconds = $owner.Clock.ElapsedMilliseconds; failure = $code
            retainedFixture = $(if ($fixtureRetained) { $owner.FixtureRoot } else { $null }) })
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
