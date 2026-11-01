using System.Diagnostics;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

public sealed partial class FrameworkCiEvidenceTests
{
    [TestCase("new-failed", "hybrid-generation-inventory-changed", TestName = "Hybrid_inventory_rejects_later_committed_failed_generation")]
    [TestCase("missing", "hybrid-generation-projection-mismatch", TestName = "Hybrid_inventory_rejects_removed_committed_generation")]
    [TestCase("changed", "hybrid-artifact-changed", TestName = "Hybrid_inventory_rejects_changed_committed_generation")]
    [TestCase("canonical", "hybrid-artifact-changed", TestName = "Hybrid_inventory_rejects_changed_canonical_projection")]
    [TestCase("owner", "hybrid-generation-owner-set", TestName = "Hybrid_inventory_rejects_added_generation_owner")]
    [TestCase("export", "hybrid-generation-export-mismatch", TestName = "Hybrid_inventory_rejects_older_export_generation")]
    public async Task HybridReceiptCannotBorrowAnEarlierLegacyGeneration(string mode, string expected)
    {
        var result = await RunHybridGenerationBoundaryAsync(mode, expected);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain("inventory-specific-rejection:" + expected));
    }

    [Test]
    [Property("FishAudioCaseId", "infra.hybrid/generation-inventory-excludes-uncommitted-transients")]
    public async Task HybridInventoryKeepsCommittedNamesAndAllowsUncommittedTransient()
    {
        var result = await RunHybridGenerationBoundaryAsync("transient", "");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain("inventory-committed-set-stable"));
    }

    private async Task<ProcessResult> RunHybridGenerationBoundaryAsync(string mode, string expected)
    {
        var script = Path.Combine(_root, "hybrid-generation-boundary.ps1");
        Write(script, HybridGenerationBoundary);
        var start = new ProcessStartInfo(FindExecutable("pwsh.exe"))
        {
            WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-File", script, "-Mode", mode, "-Expected", expected,
            "-Common", Path.Combine(_repositoryRoot, "tools/HybridSession.Common.ps1"), "-Root", _root,
        }) start.ArgumentList.Add(argument);
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase) ||
                     key.StartsWith("FISHAUDIO_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        return await RunOwnedProcessAsync(start);
    }

    // These unit inputs exercise the actual inventory/receipt reader, not a
    // replacement validator. The separate partition-verifier fixture owns
    // full real RegressionReport parsing, source/catalog and outcome admission.
    private const string HybridGenerationBoundary = """
        param([string]$Mode,[string]$Expected,[string]$Common,[string]$Root)
        . $Common
        try {
            $evidence = Join-Path $Root 'hybrid-generation-input'
            $attemptRoot = Join-Path $evidence 'slots/slot-0001/attempt-0001'
            $report = Join-Path $attemptRoot 'legacy.json'
            $runId = '11111111111111111111111111111111'
            $run = $report+'.generations/'+$runId
            [IO.Directory]::CreateDirectory($run) | Out-Null
            $initial = @{schemaVersion=1;runId=$runId;generation=0;complete=$false;cases=@(@{id='fixture/case';partition='core';outcome='not-run'})}
            $passed = @{schemaVersion=1;runId=$runId;generation=1;complete=$true;cases=@(@{id='fixture/case';partition='core';outcome='passed'})}
            Write-HybridJson (Join-Path $run '00000000000000000000.json') $initial
            Write-HybridJson (Join-Path $run '00000000000000000001.json') $passed
            [IO.File]::Copy((Join-Path $run '00000000000000000001.json'),$report)
            $inventory = Read-HybridLegacyInventory $report $evidence
            $attemptPath = Join-Path $attemptRoot 'attempt.json'
            Write-HybridJson $attemptPath @{ordinal=1;complete=$true;exitCode=0}
            $sessionId = '33333333333333333333333333333333'
            Write-HybridJson (Join-Path $evidence 'manifest.json') @{schemaVersion=1;kind='hybrid-test-session';complete=$true;runId=$sessionId
                slots=@(@{id='slot-0001';attempts=@(@{ordinal=1;sha256=(Get-HybridDigest $attemptPath)})})}
            $admissionRoot = Join-Path $Root 'hybrid-generation-admission'
            [IO.Directory]::CreateDirectory($admissionRoot) | Out-Null
            $inventoryPath = Join-Path $admissionRoot 'inventory.json'
            Write-HybridJson $inventoryPath $inventory
            $admission = [pscustomobject]@{schemaVersion=1;kind='hybrid-session-admission';complete=$true;baselineClaim=$false;runId=$sessionId
                owners=@([pscustomobject]@{slotId='slot-0001';owner='legacy';report=$inventory.report;reportRunId=$runId;reportGeneration=1})
                generationInventories=@([pscustomobject]@{slotId='slot-0001';path='inventory.json';sha256=(Get-HybridDigest $inventoryPath)})}
            # The new failed generation can publish while canonical and old
            # generations are genuinely held open with write/delete denied.
            if ($Mode -ceq 'new-failed') {
                Write-HybridJson (Join-Path $run '00000000000000000002.json') @{schemaVersion=1;runId=$runId;generation=2;complete=$false;cases=@(@{id='fixture/case';partition='core';outcome='failed'})}
            } elseif ($Mode -ceq 'owner') {
                [IO.Directory]::CreateDirectory($report+'.generations/22222222222222222222222222222222') | Out-Null
            } elseif ($Mode -ceq 'transient') {
                [IO.File]::WriteAllText((Join-Path $run '00000000000000000002.json.uncommitted.tmp'),'unfinished')
            } elseif ($Mode -cin @('missing','changed','canonical')) {
                # Model the later commit-msg process after the original
                # consumer released its handles, retaining the exact inventory.
                Close-HybridLocks
                if ($Mode -ceq 'missing') { [IO.File]::Delete((Join-Path $run '00000000000000000001.json')) }
                if ($Mode -ceq 'changed') { [IO.File]::AppendAllText((Join-Path $run '00000000000000000001.json'),' ') }
                if ($Mode -ceq 'canonical') { [IO.File]::AppendAllText($report,' ') }
            }
            $failed = $false
            try {
                if ($Mode -ceq 'export') { $admission.owners[0].reportGeneration=0 }
                Assert-HybridGenerationReceipt $evidence $admissionRoot $admission
            } catch {
                $failed = $true
                if ($_.Exception.Message -cne $Expected) { throw }
                Write-Output ('inventory-specific-rejection:'+$Expected)
            }
            if ($Mode -ceq 'transient') {
                if ($failed) { throw 'hybrid-fixture-transient-became-committed' }
                Write-Output 'inventory-committed-set-stable'
            } elseif (-not $failed) { throw 'hybrid-fixture-generation-drift-admitted' }
        } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
        finally { Close-HybridLocks }
        exit 0
        """;
}
