using System.Diagnostics;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

public sealed partial class FrameworkCiEvidenceTests
{
    [Test]
    [Property("FishAudioCaseId", "infra.hybrid/unborn-snapshot-keeps-null-commit")]
    public async Task HybridFirstCommitSnapshotKeepsGenuineNullProvenance()
    {
        var result = await RunGateReceiptBoundaryAsync("unborn");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain("unborn-source-identity-retained"));
    }

    [Test]
    [Property("FishAudioCaseId", "infra.hybrid/application-multiplicity-keeps-actual-first")]
    public async Task HybridMetadataUsesOneActualApplicationWhenResolutionHasMultipleItems()
    {
        var result = await RunGateReceiptBoundaryAsync("app-multiple");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain("multiple-application-metadata-reparsed"));
        Assert.That(File.Exists(Path.Combine(_root, "multiple-application-catalog.json")), Is.True);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.hybrid/final-receipt-survives-deleted-snapshot")]
    public async Task HybridFinalGuardUsesOnlyRetainedRawEvidence()
    {
        var result = await RunGateReceiptBoundaryAsync("stable");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain("retained-receipt-admitted"));
    }

    [TestCase("new-attempt", "hybrid-attempt-set", TestName = "Hybrid_final_guard_rejects_new_unlisted_physical_attempt")]
    [TestCase("hidden-new-attempt", "hybrid-attempt-set", TestName = "Hybrid_retained_receipt_rejects_Windows_hidden_unlisted_failed_attempt")]
    [TestCase("new-generation", "hybrid-generation-inventory-changed", TestName = "Hybrid_final_guard_rejects_new_failed_committed_generation")]
    [TestCase("new-slot", "gate-hybrid-receipt-slot-set", TestName = "Hybrid_final_guard_rejects_new_unlisted_slot")]
    [TestCase("changed-raw", "hybrid-artifact-digest", TestName = "Hybrid_final_guard_rejects_changed_old_raw_bytes")]
    public async Task HybridFinalGuardRejectsEvidenceOutsideItsFrozenAdmission(string mode, string expected)
    {
        var result = await RunGateReceiptBoundaryAsync(mode, expected);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain("specific-receipt-rejection:" + expected));
    }

    [Test]
    [Property("FishAudioCaseId", "infra.hybrid/nonzero-parent-keeps-primary-before-job-cleanup")]
    public async Task HybridNonzeroParentFailurePrecedesDescendantCleanup()
    {
        Process? descendant = null;
        Exception? primary = null;
        try
        {
            var result = await RunGateReceiptBoundaryAsync("parent-failure");
            var identity = JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "parent-descendant.json")))!;
            try
            {
                descendant = Process.GetProcessById(identity["pid"]!.GetValue<int>());
                if (descendant.StartTime.ToUniversalTime().Ticks != identity["startedUtcTicks"]!.GetValue<long>())
                {
                    descendant.Dispose(); descendant = null;
                    Assert.Fail("The original descendant must not be replaced by a reused PID.");
                }
            }
            catch (ArgumentException) { }
            Assert.That(result.ExitCode, Is.Zero, result.Error);
            var report = JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "parent-process/process.json")))!;
            Assert.That(report["complete"]!.GetValue<bool>(), Is.False);
            Assert.That(report["reason"]!.GetValue<string>(), Is.EqualTo("hybrid-process-target-failed"));
            Assert.That(report["targetExitCode"]!.GetValue<int>(), Is.EqualTo(5));
            Assert.That(report["cleanupAnchorTimestamp"]!.GetValue<long>(), Is.EqualTo(report["targetExitTimestamp"]!.GetValue<long>()));
            Assert.That(report["allChildrenExited"]!.GetValue<bool>(), Is.True);
            Assert.That(report["activeProcesses"]!.GetValue<int>(), Is.Zero);
            Assert.That(report["stdoutDrained"]!.GetValue<bool>(), Is.True);
            Assert.That(report["stderrDrained"]!.GetValue<bool>(), Is.True);
            if (descendant is not null) Assert.That(descendant.HasExited, Is.True, "Actual original descendant exits before independent rescue.");
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            try
            {
                if (descendant is not null)
                {
                    if (!descendant.HasExited) descendant.Kill(entireProcessTree: true);
                    await descendant.WaitForExitAsync().WaitAsync(RemainingRoot());
                }
            }
            catch (Exception cleanup) when (primary is not null)
            { TestContext.Error.WriteLine("hybrid-parent-fixture: rescue failed (" + cleanup.GetType().Name + ")."); }
            finally { descendant?.Dispose(); }
        }
    }

    private async Task<ProcessResult> RunGateReceiptBoundaryAsync(string mode, string expected = "")
    {
        var script = Path.Combine(_root, "gate-receipt-boundary.ps1");
        Write(script, GateReceiptBoundary);
        var start = new ProcessStartInfo(FindExecutable("pwsh.exe"))
        {
            WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        var discovery = _manifest["phases"]!.AsArray().Single(item => item!["name"]!.GetValue<string>() == "discover-plugin")!;
        var dump = Artifact(discovery["directory"]!.GetValue<string>() + "/discovery.dump");
        foreach (var argument in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-File", script, "-Mode", mode, "-Expected", expected,
            "-Common", Path.Combine(_repositoryRoot, "tools/HybridSession.Common.ps1"),
            "-ReceiptHelper", Path.Combine(_repositoryRoot, "tools/HybridReceipt.ps1"), "-Root", _root,
            "-Tool", _actualTool, "-Dump", dump,
        }) start.ArgumentList.Add(argument);
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase) ||
                     key.StartsWith("FISHAUDIO_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        return await RunOwnedProcessAsync(start);
    }

    private const string GateReceiptBoundary = """
        param([string]$Mode,[string]$Expected,[string]$Common,[string]$ReceiptHelper,[string]$Root,[string]$Tool,[string]$Dump)
        try {
            if($Mode -ceq 'app-multiple'){
                . $Common
                $actualApplication=@(Microsoft.PowerShell.Core\Get-Command dotnet -CommandType Application -ErrorAction Stop)[0]
                function Get-Command{
                    param([string]$Name,[string]$CommandType,[string]$ErrorAction)
                    if($Name -ceq 'dotnet'){return @($actualApplication,$actualApplication)}
                    Microsoft.PowerShell.Core\Get-Command -Name $Name -CommandType $CommandType -ErrorAction Stop
                }
                $catalog=Join-Path $Root 'multiple-application-catalog.json'
                Invoke-HybridTool $Tool @('evidence-catalog','--discovery',$Dump,'--output',$catalog) (Join-Path $Root 'multiple-application-process') $Root
                if(@((Read-HybridJson $catalog).cases).Count -eq 0){throw 'fixture-actual-catalog-empty'}
                Write-Output 'multiple-application-metadata-reparsed'
            }elseif($Mode -ceq 'unborn'){
                . $Common
                [IO.File]::WriteAllText((Join-Path $Root 'owned-source.txt'),'owned')
                $context=Join-Path $Root 'unborn-context.json'
                Write-HybridJson $context @{schemaVersion=1;root=$Root;tree=('a'*40);sourceCommit=$null;files=@(@{path='owned-source.txt';sha256=(Get-HybridDigest (Join-Path $Root 'owned-source.txt'))})}
                $env:FISHAUDIO_SNAPSHOT_CONTEXT=$context
                $source=Get-HybridSource $Root (Join-Path $Root 'source-read')
                if(-not $source.identity.snapshot -or $null -ne $source.identity.sourceCommit -or $null -ne $source.identity.commit -or $source.legacyTree -cne ('a'*40)){throw 'fixture-unborn-provenance'}
                Write-Output 'unborn-source-identity-retained'
            }elseif($Mode -ceq 'parent-failure'){
                . $Common
                $target=Join-Path $Root 'parent-target.ps1'
                $child=Join-Path $Root 'parent-descendant.ps1'
                [IO.File]::WriteAllText($child,'[Threading.Thread]::Sleep(60000)')
                $body=@'
        param([string]$Root,[string]$ChildScript)
        $start=[Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
        $start.UseShellExecute=$false;$start.CreateNoWindow=$true
        foreach($argument in @('-NoLogo','-NoProfile','-NonInteractive','-File',$ChildScript)){$start.ArgumentList.Add($argument)}
        $owned=[Diagnostics.Process]::new();$owned.StartInfo=$start
        if(-not $owned.Start()){throw 'fixture-descendant-start'}
        [IO.File]::WriteAllText((Join-Path $Root 'parent-descendant.json'),(@{pid=$owned.Id;startedUtcTicks=$owned.StartTime.ToUniversalTime().Ticks}|ConvertTo-Json))
        $owned.Dispose()
        exit 5
        '@
                [IO.File]::WriteAllText($target,$body)
                $rejected=$false
                try{[void](Invoke-HybridProcess (Get-Process -Id $PID).Path @('-NoLogo','-NoProfile','-NonInteractive','-File',$target,'-Root',$Root,'-ChildScript',$child) (Join-Path $Root 'parent-process') $Root)}
                catch{$rejected=$true;if($_.Exception.Message -cne 'hybrid-process-target-failed'){throw}}
                if(-not $rejected){throw 'fixture-parent-failure-admitted'}
            }else{
                . $ReceiptHelper
                $evidence=Join-Path $Root 'retained'
                $session=Join-Path $evidence 'hybrid'
                $admissionRoot=Join-Path $evidence 'admission'
                $attempt=Join-Path $session 'slots/slot-0001/attempt-0001'
                [IO.Directory]::CreateDirectory($attempt)|Out-Null
                [IO.Directory]::CreateDirectory($admissionRoot)|Out-Null
                $runId='11111111111111111111111111111111';$sessionId='22222222222222222222222222222222'
                $report=Join-Path $attempt 'legacy.json';$generation=$report+'.generations/'+$runId
                [IO.Directory]::CreateDirectory($generation)|Out-Null
                function Write-UnitJson([string]$Path,$Value){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 32),[Text.UTF8Encoding]::new($false))}
                Write-UnitJson (Join-Path $generation '00000000000000000000.json') @{schemaVersion=1;runId=$runId;generation=0;complete=$false}
                Write-UnitJson (Join-Path $generation '00000000000000000001.json') @{schemaVersion=1;runId=$runId;generation=1;complete=$true}
                [IO.File]::Copy((Join-Path $generation '00000000000000000001.json'),$report)
                $attemptPath=Join-Path $attempt 'attempt.json'
                Write-UnitJson $attemptPath @{ordinal=1;complete=$true;exitCode=0}
                Write-UnitJson (Join-Path $session 'manifest.json') @{schemaVersion=1;kind='hybrid-test-session';complete=$true;runId=$sessionId
                    slots=@(@{id='slot-0001';owner='legacy';attempts=@(@{ordinal=1;sha256=(Get-HybridDigest $attemptPath)})})}
                $context=Join-Path $evidence 'snapshot.json'
                Write-UnitJson $context @{schemaVersion=1;root=(Join-Path $Root 'deleted-snapshot');tree=('a'*40);sourceCommit=$null;files=@()}
                $inventory=Read-HybridLegacyInventory $report $session
                $inventoryPath=Join-Path $admissionRoot 'inventory.json';Write-UnitJson $inventoryPath $inventory
                $admissionPath=Join-Path $admissionRoot 'admission.json'
                Write-UnitJson $admissionPath @{schemaVersion=1;kind='hybrid-session-admission';complete=$true;baselineClaim=$false;runId=$sessionId;legacySourceTree=('a'*40)
                    source=@{snapshot=$true;snapshotRootValidated=$true;tree=('a'*40);sourceCommit=$null;contextSha256=(Get-HybridDigest $context)}
                    owners=@(@{slotId='slot-0001';owner='legacy';report=$inventory.report;reportRunId=$runId;reportGeneration=1})
                    generationInventories=@(@{slotId='slot-0001';path='inventory.json';sha256=(Get-HybridDigest $inventoryPath)})}
                $pins=@(Get-ChildItem -LiteralPath $evidence -Recurse -File|ForEach-Object{@{path=[IO.Path]::GetRelativePath($evidence,$_.FullName).Replace('\','/');sha256=(Get-HybridDigest $_.FullName)}})
                $gate=[pscustomobject]@{schemaVersion=1;gateProtocolVersion=3;mode='bootstrap/full/hybrid';complete=$true;exitCode=0;hybridVerified=$true
                    snapshotTree=('a'*40);sourceCommit=$null;hybridArtifacts=$pins;hybrid=@{session='hybrid';admission='admission/admission.json';admissionSha256=(Get-HybridDigest $admissionPath)}}
                Close-HybridLocks
                if($Mode -cin @('new-attempt','hidden-new-attempt')){
                    $new=Join-Path $session 'slots/slot-0001/attempt-0002';[IO.Directory]::CreateDirectory($new)|Out-Null
                    Write-UnitJson (Join-Path $new 'attempt.json') @{ordinal=2;complete=$false;exitCode=1}
                    if($Mode -ceq 'hidden-new-attempt'){
                        [IO.File]::SetAttributes($new,([IO.File]::GetAttributes($new) -bor [IO.FileAttributes]::Hidden))
                        if(([IO.File]::GetAttributes($new) -band [IO.FileAttributes]::Hidden) -eq 0){throw 'fixture-hidden-attempt-attribute-not-set'}
                        foreach($pin in $pins){
                            if((Get-HybridDigest (Join-Path $evidence $pin.path)) -cne $pin.sha256){throw 'fixture-old-pinned-bytes-changed'}
                        }
                    }
                }
                if($Mode -ceq 'new-generation'){Write-UnitJson (Join-Path $generation '00000000000000000002.json') @{schemaVersion=1;runId=$runId;generation=2;complete=$false}}
                if($Mode -ceq 'new-slot'){[IO.Directory]::CreateDirectory((Join-Path $session 'slots/slot-0002'))|Out-Null}
                if($Mode -ceq 'changed-raw'){[IO.File]::AppendAllText($report,' ')}
                $rejected=$false
                try{Assert-GateHybridReceipt $evidence $gate}
                catch{$rejected=$true;if($_.Exception.Message -cne $Expected){throw};Write-Output ('specific-receipt-rejection:'+$Expected)}
                if($Mode -ceq 'stable'){
                    if($rejected -or [IO.Directory]::Exists((Join-Path $Root 'deleted-snapshot'))){throw 'fixture-retained-receipt'}
                    Write-Output 'retained-receipt-admitted'
                }elseif(-not $rejected){throw 'fixture-unlisted-evidence-admitted'}
            }
        }catch{[Console]::Error.WriteLine($_.Exception.Message);exit 1}
        finally{if(Get-Command Close-HybridLocks -CommandType Function -ErrorAction SilentlyContinue){Close-HybridLocks}}
        exit 0
        """;
}
