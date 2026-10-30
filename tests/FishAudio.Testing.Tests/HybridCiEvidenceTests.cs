using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class HybridCiEvidenceTests
{
    // These are CI transport/provenance controls. The independent hybrid tests
    // own raw result admission. No Git repository, test key or product body is
    // created or executed by these fixtures.
    private readonly Stopwatch _clock = new();
    private string _root = "";

    [TestCase("actual", "accepted:1", TestName = "HybridCi_CurrentActualExecutionOwnsItsArtifact")]
    [TestCase("cached", "accepted:1", TestName = "HybridCi_CachedSiblingNeedsUniqueEarlierTimeWitness")]
    [TestCase("rerun", "accepted:2", TestName = "HybridCi_ActualRerunOwnsNewAttempt")]
    [TestCase("failed", "rejected:hybrid-ci-unsuccessful-latest-job", TestName = "HybridCi_NewerFailureCannotBorrowGreen")]
    [TestCase("cancelled", "rejected:hybrid-ci-unsuccessful-latest-job", TestName = "HybridCi_NewerCancellationCannotBorrowGreen")]
    [TestCase("skipped", "rejected:hybrid-ci-unsuccessful-latest-job", TestName = "HybridCi_SkippedJobCannotEstablishExecution")]
    [TestCase("neutral", "rejected:hybrid-ci-unsuccessful-latest-job", TestName = "HybridCi_NeutralJobCannotEstablishExecution")]
    [TestCase("unfinished", "rejected:hybrid-ci-unsuccessful-latest-job", TestName = "HybridCi_UnfinishedJobCannotBorrowGreen")]
    [TestCase("missing-latest", "rejected:hybrid-ci-missing-latest-evidence", TestName = "HybridCi_MissingLatestArtifactCannotBorrowGreen")]
    [TestCase("orphan-artifact", "rejected:hybrid-ci-missing-latest-evidence", TestName = "HybridCi_ArtifactCannotInventExecutedOwner")]
    [TestCase("cached-runner", "rejected:hybrid-ci-unmatched-or-ambiguous-cached-job", TestName = "HybridCi_CachedRunnerMutationFails")]
    [TestCase("cached-step", "rejected:hybrid-ci-unmatched-or-ambiguous-cached-job", TestName = "HybridCi_CachedOrderedStepMutationFails")]
    [TestCase("overlap", "rejected:hybrid-ci-overlapping-cached-job", TestName = "HybridCi_CachedExecutionCannotCrossAttemptFence")]
    [TestCase("foreign-head", "rejected:hybrid-ci-execution-sha", TestName = "HybridCi_ApiHeadMustMatchHistoricalEvent")]
    [TestCase("ambiguous-name", "rejected:hybrid-ci-ambiguous-job-name", TestName = "HybridCi_JobNameCannotImpersonateHybridOwner")]
    [TestCase("missing-step", "rejected:hybrid-ci-required-step", TestName = "HybridCi_IndependentAdmissionStepIsRequired")]
    [TestCase("pr", "accepted:pr", TestName = "HybridCi_PullRequestSeparatesMergeCheckoutAndApiHead")]
    [TestCase("pr-reversed", "rejected:hybrid-ci-pull-request-parents", TestName = "HybridCi_PullRequestParentOrderIsExact")]
    [TestCase("pr-foreign", "rejected:hybrid-ci-pull-request-context", TestName = "HybridCi_PullRequestTargetRepositoryIsExact")]
    [TestCase("legacy-null", "accepted:legacy-owner", TestName = "HybridCi_OuterSessionOwnershipAcceptsNullLegacyStamp")]
    [TestCase("legacy-foreign", "rejected:hybrid-ci-legacy-ci-owner", TestName = "HybridCi_NonnullRetiredLegacyJobStampIsRejected")]
    [TestCase("legacy-alias", "rejected:hybrid-ci-legacy-ci-owner", TestName = "HybridCi_LegacyStampCannotHideBehindCasingAlias")]
    [TestCase("legacy-file-null", "accepted:legacy-file", TestName = "HybridCi_InventoriedUppercaseJsonWithNullStampPreservesBytes")]
    [TestCase("legacy-file-foreign", "rejected:hybrid-ci-legacy-ci-owner", TestName = "HybridCi_InventoriedUppercaseJsonCannotHideForeignStamp")]
    [TestCase("legacy-file-alias", "rejected:hybrid-ci-legacy-ci-owner", TestName = "HybridCi_InventoriedUppercaseJsonCannotHideStampAlias")]
    [TestCase("legacy-mixed-parent-null", "accepted:legacy-mixed-parent", TestName = "HybridCi_InventoriedMixedParentJsonWithNullStampPreservesBytes")]
    [TestCase("legacy-mixed-parent-foreign", "rejected:hybrid-ci-legacy-ci-owner", TestName = "HybridCi_InventoriedMixedParentJsonCannotHideForeignStamp")]
    [TestCase("legacy-mixed-parent-alias", "rejected:hybrid-ci-legacy-ci-owner", TestName = "HybridCi_InventoriedMixedParentJsonCannotHideStampAlias")]
    [TestCase("flat", "accepted:layout", TestName = "HybridCi_PinnedDownloaderFlatSingleBundleIsAdmitted")]
    [TestCase("wrapped", "accepted:layout", TestName = "HybridCi_PinnedDownloaderWrappedAttemptsAreAdmitted")]
    [TestCase("mixed", "rejected:hybrid-ci-foreign-artifact", TestName = "HybridCi_MixedDownloadLayoutsFail")]
    [TestCase("loose", "rejected:hybrid-ci-foreign-artifact", TestName = "HybridCi_UnknownDownloadEntryFails")]
    [TestCase("restore", "accepted:bytes", TestName = "HybridCi_RestorePreservesExactRawBytesAtTrustedFixedPath")]
    [TestCase("foreign-layout", "rejected:hybrid-ci-unsupported-runner-layout", TestName = "HybridCi_ForeignAbsoluteOwnerPathCannotChooseDestination")]
    [TestCase("extra-file", "rejected:hybrid-ci-inventory-set", TestName = "HybridCi_UninventoriedRawFileCannotJoinBundle")]
    [TestCase("changed-file", "rejected:hybrid-ci-inventory-digest", TestName = "HybridCi_ChangedRawFileCannotReuseInventory")]
    [TestCase("occupied", "rejected:hybrid-ci-restore-not-fresh", TestName = "HybridCi_RestoreCannotOverwriteExistingBundle")]
    public async Task CiOwnershipAndTransport(string scenario, string expected)
    {
        _clock.Start();
        _root = Path.Combine(Path.GetTempPath(), "fa-hybrid-ci-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var scriptPath = Path.Combine(_root, "boundary.ps1");
        File.WriteAllText(scriptPath, Boundary, new UTF8Encoding(false));
        var start = new ProcessStartInfo(FindExecutable("pwsh.exe"))
        {
            WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", scriptPath,
                     "-Helper", Path.Combine(FindRepositoryRoot(), "tools/HybridCi.Common.ps1"), "-Root", _root, "-Scenario", scenario })
            start.ArgumentList.Add(argument);
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase) ||
                     key.StartsWith("GITHUB_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("FISHAUDIO_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        using var process = new Process { StartInfo = start };
        Task<string>? stdout = null; Task<string>? stderr = null;
        var started = false;
        Exception? primary = null;
        try
        {
            started = process.Start();
            if (!started) throw new InvalidOperationException("hybrid-ci-fixture-process-start");
            stdout = process.StandardOutput.ReadToEndAsync(); stderr = process.StandardError.ReadToEndAsync();
            var completion = Task.WhenAll(process.WaitForExitAsync(), stdout, stderr);
            var warning = TimeSpan.FromSeconds(20) - _clock.Elapsed;
            if (warning <= TimeSpan.Zero || await Task.WhenAny(completion, Task.Delay(warning)) != completion)
                TestContext.Progress.WriteLine("hybrid-ci-fixture: same child still running after twenty seconds.");
            await completion.WaitAsync(RemainingRoot());
            _ = RemainingRoot();
            File.WriteAllText(Path.Combine(_root, "stdout.log"), stdout.Result, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(_root, "stderr.log"), stderr.Result, new UTF8Encoding(false));
            Assert.Multiple(() =>
            {
                Assert.That(process.ExitCode, Is.Zero, "The fixture boundary itself must complete.");
                Assert.That(stderr.Result, Is.Empty);
                Assert.That(stdout.Result.Trim(), Is.EqualTo(expected));
            });
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            if (primary is not null && started)
            {
                var drain = Stopwatch.StartNew();
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(RemainingDrain(drain));
                    if (stdout is not null && stderr is not null) await Task.WhenAll(stdout, stderr).WaitAsync(RemainingDrain(drain));
                    _ = RemainingDrain(drain);
                }
                catch (Exception cleanup) { TestContext.Error.WriteLine("hybrid-ci-fixture: bounded cleanup failed (" + cleanup.GetType().Name + ")."); }
            }
        }
    }

    [TearDown]
    public void CleanupOwnedRoot()
    {
        if (string.IsNullOrEmpty(_root)) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            var full = Path.GetFullPath(_root);
            if (!Path.GetDirectoryName(full)!.Equals(Path.TrimEndingDirectorySeparator(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(full).StartsWith("fa-hybrid-ci-", StringComparison.Ordinal) ||
                !Guid.TryParseExact(Path.GetFileName(full)[13..], "N", out _))
                throw new InvalidOperationException("hybrid-ci-fixture-owner-changed");
            if (Directory.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("hybrid-ci-fixture-reparse");
            if (failed) { TestContext.Error.WriteLine("hybrid-ci-fixture: failure evidence retained as " + Path.GetFileName(full) + "."); return; }
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
            _ = RemainingRoot();
        }
        catch (Exception error) when (failed) { TestContext.Error.WriteLine("hybrid-ci-fixture: cleanup failed (" + error.GetType().Name + ")."); }
    }

    private TimeSpan RemainingRoot()
    {
        var remaining = TimeSpan.FromSeconds(120) - _clock.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException("hybrid-ci-fixture-root-deadline");
    }
    private static TimeSpan RemainingDrain(Stopwatch clock)
    {
        var remaining = TimeSpan.FromSeconds(10) - clock.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException("hybrid-ci-fixture-drain-deadline");
    }

    private static string FindRepositoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("FISHAUDIO_TEST_REPOSITORY_ROOT");
        string? snapshot = null;
        if (Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT") is { Length: > 0 } contextPath)
        {
            using var context = JsonDocument.Parse(File.ReadAllText(contextPath));
            snapshot = context.RootElement.GetProperty("root").GetString();
        }
        if (configured is not null && snapshot is not null && !Path.GetFullPath(configured).Equals(Path.GetFullPath(snapshot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("hybrid-ci-fixture-repository-context-mismatch");
        var root = configured ?? snapshot;
        if (root is null || !Path.IsPathFullyQualified(root) || !File.Exists(Path.Combine(root, "tools/HybridCi.Common.ps1")))
            throw new InvalidOperationException("hybrid-ci-fixture-explicit-repository-required");
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("hybrid-ci-fixture-reparse");
        return Path.GetFullPath(root);
    }

    private static string FindExecutable(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim('"'), name);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        throw new InvalidOperationException("hybrid-ci-fixture-executable-unavailable");
    }

    private const string Boundary = """
        param([string]$Helper,[string]$Root,[string]$Scenario)
        . $Helper
        $result = $null
        try {
            $commit = 'a'*40; $tree = 'b'*40; $head = 'c'*40
            $context = [pscustomobject]@{ repository = 'example/fish-audio'; commit = $commit; tree = $tree; executionHead = $head
                runId = 901L; attempt = 1; tempRoot = $Root; checkoutRoot = (Join-Path $Root 'checkout'); dotnet = (Join-Path $Root 'dotnet.exe'); shell = (Join-Path $Root 'pwsh.exe'); sdk = '10.0.403' }
            $stepNames = @('Run complete hybrid validation','Independently admit complete hybrid session','Record this hybrid attempt owner')
            function Job([int]$Attempt) {
                $steps = @(for ($i=0;$i -lt $stepNames.Count;$i++) {
                    [pscustomobject]@{ name = $stepNames[$i]; status = 'completed'; conclusion = 'success'; number = $i+1
                        started_at = '2026-10-27T00:01:00Z'; completed_at = '2026-10-27T00:02:00Z' }
                })
                [pscustomobject]@{ id = 100+$Attempt; run_id = 901L; run_attempt = $Attempt; name = 'Verify release / Hybrid validation'
                    status = 'completed'; conclusion = 'success'; head_sha = $head; runner_id = 7; runner_name = 'runner'
                    started_at = '2026-10-27T00:01:00Z'; completed_at = '2026-10-27T00:02:00Z'; steps = $steps }
            }
            if ($Scenario.StartsWith('pr',[StringComparison]::Ordinal)) {
                $base = 'd'*40
                $event = [pscustomobject]@{ repository = @{ full_name = $context.repository }; pull_request = @{ base = @{ sha = $base; repo = @{ full_name = $context.repository } }; head = @{ sha = $head } } }
                $parents = @($commit,$base,$head)
                if ($Scenario -ceq 'pr-reversed') { $parents = @($commit,$head,$base) }
                if ($Scenario -ceq 'pr-foreign') { $event.pull_request.base.repo.full_name = 'foreign/repo' }
                $actual = Assert-HybridCiPullRequest $event $context.repository $commit $parents
                if ($actual -cne $head) { throw 'fixture-api-head' }
                $result = 'accepted:pr'
            } elseif ($Scenario.StartsWith('legacy-mixed-parent-',[StringComparison]::Ordinal)) {
                $bundle = Join-Path $Root 'download'
                $slotRoot = Join-Path $bundle 'session/Slots/slot-0001'
                [IO.Directory]::CreateDirectory($slotRoot) | Out-Null
                $path = Join-Path $slotRoot 'child.JSON'
                $json = '{"ci":null}'
                if ($Scenario -ceq 'legacy-mixed-parent-foreign') { $json = '{"ci":{"repository":"foreign/repo","job":"regression","attempt":1}}' }
                if ($Scenario -ceq 'legacy-mixed-parent-alias') { $json = '{"Ci":null}' }
                $raw = [Text.UTF8Encoding]::new($false).GetBytes($json+"`r`n")
                [IO.File]::WriteAllBytes($path,$raw)
                $owner = [pscustomobject]@{ files = @(Get-HybridCiInventory $bundle) }
                if (@($owner.files).Count -ne 1 -or $owner.files[0].path -cne 'session/Slots/slot-0001/child.JSON') { throw 'fixture-mixed-parent-inventory-spelling' }
                $session = [pscustomobject]@{ slots = @([pscustomobject]@{ id = 'slot-0001'; owner = 'legacy' }) }
                # The actual Windows spelling from inventory must reach the
                # same legacy stamp check as the consumer's slots lookup.
                Assert-HybridCiLegacyRecords $session $owner $bundle
                if ([Convert]::ToHexString([IO.File]::ReadAllBytes($path)) -cne [Convert]::ToHexString($raw)) { throw 'fixture-mixed-parent-json-bytes' }
                $result = 'accepted:legacy-mixed-parent'
            } elseif ($Scenario.StartsWith('legacy-file-',[StringComparison]::Ordinal)) {
                $bundle = Join-Path $Root 'download'
                $slotRoot = Join-Path $bundle 'session/slots/slot-0001'
                [IO.Directory]::CreateDirectory($slotRoot) | Out-Null
                $path = Join-Path $slotRoot 'child.JSON'
                $json = '{"ci":null}'
                if ($Scenario -ceq 'legacy-file-foreign') { $json = '{"ci":{"repository":"foreign/repo","job":"regression","attempt":1}}' }
                if ($Scenario -ceq 'legacy-file-alias') { $json = '{"Ci":null}' }
                $raw = [Text.UTF8Encoding]::new($false).GetBytes($json+"`r`n")
                [IO.File]::WriteAllBytes($path,$raw)
                $owner = [pscustomobject]@{ files = @(Get-HybridCiInventory $bundle) }
                $session = [pscustomobject]@{ slots = @([pscustomobject]@{ id = 'slot-0001'; owner = 'legacy' }) }
                # Invoke the same inventory/file-selection boundary used by the
                # real CI consumer, not just its per-document stamp predicate.
                Assert-HybridCiLegacyRecords $session $owner $bundle
                if ([Convert]::ToHexString([IO.File]::ReadAllBytes($path)) -cne [Convert]::ToHexString($raw)) { throw 'fixture-legacy-json-bytes' }
                $result = 'accepted:legacy-file'
            } elseif ($Scenario.StartsWith('legacy-',[StringComparison]::Ordinal)) {
                $record = [pscustomobject]@{ ci = $null }
                if ($Scenario -ceq 'legacy-foreign') { $record.ci = @{ repository = 'foreign/repo'; job = 'regression'; attempt = 1 } }
                if ($Scenario -ceq 'legacy-alias') { $record = [pscustomobject]@{ Ci = $null } }
                Assert-HybridCiLegacyCi $record
                $result = 'accepted:legacy-owner'
            } elseif ($Scenario -cin @('flat','wrapped','mixed','loose')) {
                $artifacts = Join-Path $Root 'artifacts'; [IO.Directory]::CreateDirectory($artifacts) | Out-Null
                if ($Scenario -cin @('flat','mixed')) { Write-HybridCiJson (Join-Path $artifacts 'ci-owner.json') @{ attempt = 1 } }
                if ($Scenario -cin @('wrapped','mixed')) { [IO.Directory]::CreateDirectory((Join-Path $artifacts ('hybrid-report-'+$commit+'-901-1'))) | Out-Null }
                if ($Scenario -ceq 'loose') { [IO.File]::WriteAllText((Join-Path $artifacts 'unexpected.txt'),'raw') }
                $bundles = Get-HybridCiBundles $artifacts $context
                if ($bundles.Count -ne 1 -or -not $bundles.ContainsKey(1)) { throw 'fixture-layout' }
                $result = 'accepted:layout'
            } elseif ($Scenario -cin @('restore','foreign-layout','extra-file','changed-file','occupied')) {
                $bundle = Join-Path $Root 'download'; [IO.Directory]::CreateDirectory((Join-Path $bundle 'session')) | Out-Null
                [IO.Directory]::CreateDirectory((Join-Path $bundle 'producer-admission')) | Out-Null
                $raw = [byte[]]@(0,255,13,10,10,128,65)
                [IO.File]::WriteAllBytes((Join-Path $bundle 'session/manifest.json'),$raw)
                [IO.File]::WriteAllText((Join-Path $bundle 'producer-admission/admission.json'),'{}')
                $owner = [pscustomobject]@{ schemaVersion = 1; kind = 'hybrid-ci-owner'; repository = $context.repository; commit = $commit; head = $commit; tree = $tree
                    workflowRunId = '901'; attempt = 1; job = 'hybrid-validation'; sdk = $context.sdk; checkoutRoot = $context.checkoutRoot; tempRoot = $Root
                    bundleRoot = (Get-HybridCiBundlePath $context 1); dotnet = $context.dotnet; shell = $context.shell; finishedUtc = '2026-10-27T00:01:30Z'; files = @(Get-HybridCiInventory $bundle) }
                Write-HybridCiJson (Join-Path $bundle 'ci-owner.json') $owner
                if ($Scenario -ceq 'foreign-layout') { $owner.bundleRoot = Join-Path $Root 'foreign' }
                if ($Scenario -ceq 'extra-file') { [IO.File]::WriteAllText((Join-Path $bundle 'session/extra.json'),'{}') }
                if ($Scenario -ceq 'changed-file') { Close-HybridCiLocks; [IO.File]::WriteAllBytes((Join-Path $bundle 'session/manifest.json'),[byte[]]@(1,2,3)) }
                Assert-HybridCiOwner $owner $context 1 $bundle (Job 1)
                if ($Scenario -ceq 'occupied') { [IO.Directory]::CreateDirectory((Get-HybridCiBundlePath $context 1)) | Out-Null }
                $restored = Restore-HybridCiBundle $bundle $owner $context 1
                if ([Convert]::ToHexString([IO.File]::ReadAllBytes((Join-Path $restored 'session/manifest.json'))) -cne [Convert]::ToHexString($raw)) { throw 'fixture-raw-bytes' }
                $result = 'accepted:bytes'
            } else {
                $jobs = [pscustomobject]@{ repository = $context.repository; runId = 901L; filter = 'all'; totalCount = 1
                    attempts = @([pscustomobject]@{ runAttempt = 1; runStartedAt = '2026-10-27T00:00:00Z' }); jobs = @((Job 1)) }
                $artifacts = @(1)
                if ($Scenario -cnotin @('actual','foreign-head','ambiguous-name','missing-step')) {
                    $context.attempt = 2
                    $jobs.attempts += [pscustomobject]@{ runAttempt = 2; runStartedAt = '2026-10-27T01:00:00Z' }
                    if ($Scenario -cne 'orphan-artifact') { $jobs.jobs += Job 2; $jobs.totalCount = 2 }
                    if ($Scenario -cnotin @('cached','cached-runner','cached-step','overlap','missing-latest')) { $artifacts += 2 }
                    if ($Scenario -cin @('rerun','missing-latest')) {
                        $jobs.jobs[1].started_at = '2026-10-27T01:01:00Z'; $jobs.jobs[1].completed_at = '2026-10-27T01:02:00Z'
                        foreach ($step in $jobs.jobs[1].steps) { $step.started_at = '2026-10-27T01:01:00Z'; $step.completed_at = '2026-10-27T01:02:00Z' }
                    }
                    if ($Scenario -cin @('failed','cancelled','skipped','neutral')) { $jobs.jobs[1].conclusion = $(if ($Scenario -ceq 'failed') { 'failure' } else { $Scenario }) }
                    if ($Scenario -ceq 'unfinished') { $jobs.jobs[1].status = 'in_progress'; $jobs.jobs[1].conclusion = $null }
                    if ($Scenario -ceq 'cached-runner') { $jobs.jobs[1].runner_name = 'different' }
                    if ($Scenario -ceq 'cached-step') { $jobs.jobs[1].steps[0].name = 'Different execution' }
                    if ($Scenario -ceq 'overlap') { $jobs.jobs[1].completed_at = '2026-10-27T01:00:00Z' }
                }
                if ($Scenario -ceq 'foreign-head') { $jobs.jobs[0].head_sha = 'e'*40 }
                if ($Scenario -ceq 'ambiguous-name') { $jobs.jobs[0].name = 'Other Hybrid validation extra' }
                if ($Scenario -ceq 'missing-step') { $jobs.jobs[0].steps = @($jobs.jobs[0].steps | Where-Object name -CNE 'Independently admit complete hybrid session') }
                $selected = Select-HybridCiExecution $jobs $context $artifacts
                $result = 'accepted:'+$selected.attempt
            }
        } catch { $result = 'rejected:'+$script:HybridCiFailure }
        finally { Close-HybridCiLocks }
        Write-Output $result
        """;
}
