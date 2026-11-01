using System.Diagnostics;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

// Real generic outer owner, real waiting launcher and real Windows Job. These
// process-boundary fixtures make no native scenario or runtime-baseline claim.
public sealed partial class FrameworkCiEvidenceTests
{
    [Test]
    [Property("FishAudioCaseId", "infra.hybrid/process-helper-returns-one-int")]
    public async Task HybridProcessReturnsOnlyActualExitCode()
    {
        var result = await RunHybridProcessBoundaryAsync("return-int");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var report = ReadHybridProcessTerminal();
        Assert.That(report["complete"]!.GetValue<bool>(), Is.True);
        Assert.That(report["controlEvents"]!.GetValue<int>(), Is.EqualTo(2));
        Assert.That(report["assigned"]!.GetValue<bool>(), Is.True);
        Assert.That(report["activeProcesses"]!.GetValue<int>(), Is.Zero);
        Assert.That(report["allChildrenExited"]!.GetValue<bool>(), Is.True);
        Assert.That(report["stdoutDrained"]!.GetValue<bool>(), Is.True);
        Assert.That(report["stderrDrained"]!.GetValue<bool>(), Is.True);
    }

    [TestCase("pipes", TestName = "Hybrid_owner_bounds_original_drains_after_target_exit")]
    [TestCase("silent", TestName = "Hybrid_owner_bounds_silent_descendant_after_parent_exit")]
    public async Task HybridExitedTargetCannotLeaveOwnedDescendantOrRenewDrainBudget(string variable)
    {
        var result = await RunHybridProcessBoundaryAsync(variable);
        Process? descendant = null;
        Exception? primary = null;
        try
        {
        var identity = JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "hybrid-descendant.json")))!;
        try
        {
            descendant = Process.GetProcessById(identity["pid"]!.GetValue<int>());
            if (descendant.StartTime.ToUniversalTime().Ticks != identity["startedUtcTicks"]!.GetValue<long>())
            {
                descendant.Dispose();
                descendant = null;
                Assert.Fail("The original descendant identity cannot be replaced by a reused PID.");
            }
        }
        catch (ArgumentException) { } // Original PID is already absent.
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var report = ReadHybridProcessTerminal();
        Assert.That(report["complete"]!.GetValue<bool>(), Is.False);
        Assert.That(report["reason"]!.GetValue<string>(), Is.EqualTo("hybrid-process-exit-drain-deadline"));
        Assert.That(report["targetExitObserved"]!.GetValue<bool>(), Is.True);
        Assert.That(report["targetExitCode"]!.GetValue<int>(), Is.Zero);
        var frequency = report["frequency"]!.GetValue<long>();
        var anchor = report["cleanupAnchorTimestamp"]!.GetValue<long>();
        Assert.That(anchor, Is.EqualTo(report["targetExitTimestamp"]!.GetValue<long>()));
        var elapsed = TimeSpan.FromSeconds((report["endedTimestamp"]!.GetValue<long>() - anchor) / (double)frequency);
        // This is the existing ten-second finalizer, not a new action budget.
        // A second renewed ten-second cleanup would exceed the upper bound.
        Assert.That(elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(9)));
        Assert.That(elapsed, Is.LessThan(TimeSpan.FromSeconds(15)));
        if (descendant is not null)
        {
            Assert.That(descendant.HasExited, Is.True, "The owner must terminate its actual descendant before independent rescue.");
            await descendant.WaitForExitAsync().WaitAsync(RemainingRoot());
            Assert.That(descendant.HasExited, Is.True, "The original owned descendant must actually exit.");
        }
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            try
            {
                // Independent rescue of the actual original descendant. It
                // never invokes the candidate owner's completion helper.
                if (descendant is not null)
                {
                    if (!descendant.HasExited) descendant.Kill(entireProcessTree: true);
                    await descendant.WaitForExitAsync().WaitAsync(RemainingRoot());
                }
            }
            catch (Exception cleanup) when (primary is not null)
            {
                TestContext.Error.WriteLine("hybrid-process-fixture: rescue failed (" + cleanup.GetType().Name + ").");
            }
            finally { descendant?.Dispose(); }
        }
    }

    [Test]
    [Property("FishAudioCaseId", "infra.hybrid/poisoned-target-retains-failed-terminal")]
    public async Task HybridUnstartableTargetCannotPublishCompletedOwner()
    {
        var result = await RunHybridProcessBoundaryAsync("poisoned-target");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var report = ReadHybridProcessTerminal();
        Assert.That(report["complete"]!.GetValue<bool>(), Is.False);
        Assert.That(report["assigned"]!.GetValue<bool>(), Is.True);
        Assert.That(report["targetExitObserved"]!.GetValue<bool>(), Is.False);
        Assert.That(report["failureType"]!.GetValue<string>(), Is.Not.Empty);
        Assert.That(report["allChildrenExited"]!.GetValue<bool>(), Is.True);
        Assert.That(report["activeProcesses"]!.GetValue<int>(), Is.Zero);
        Assert.That(report["stdoutDrained"]!.GetValue<bool>(), Is.True);
        Assert.That(report["stderrDrained"]!.GetValue<bool>(), Is.True);
    }

    private JsonNode ReadHybridProcessTerminal() => JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "hybrid-process/process.json")))!;

    private async Task<ProcessResult> RunHybridProcessBoundaryAsync(string mode)
    {
        var script = Path.Combine(_root, "hybrid-process-boundary.ps1");
        Write(script, HybridProcessBoundary);
        var start = new ProcessStartInfo(FindExecutable("pwsh.exe"))
        {
            WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-File", script, "-Mode", mode,
            "-Common", Path.Combine(_repositoryRoot, "tools/HybridSession.Common.ps1"),
            "-Root", _root, "-Dotnet", FindExecutable("dotnet.exe"),
        }) start.ArgumentList.Add(argument);
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase) ||
                     key.StartsWith("FISHAUDIO_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        return await RunOwnedProcessAsync(start);
    }

    private const string HybridProcessBoundary = """
        param([string]$Mode,[string]$Common,[string]$Root,[string]$Dotnet)
        . $Common
        try {
            $directory = Join-Path $Root 'hybrid-process'
            if ($Mode -ceq 'return-int') {
                $result = @(Invoke-HybridProcess $Dotnet @('--version') $directory $Root 120000)
                if ($result.Count -ne 1 -or $result[0] -isnot [int] -or $result[0] -ne 0) { throw 'hybrid-fixture-return-stream' }
                $terminalPath = Join-Path $directory 'process.json'
                Assert-HybridProcessProof $terminalPath (Read-HybridJson $terminalPath)
            } elseif ($Mode -ceq 'poisoned-target') {
                $failed = $false
                try { [void](Invoke-HybridProcess (Join-Path $Root 'not-a-real-executable.exe') @() $directory $Root 120000) }
                catch { $failed = $true }
                if (-not $failed) { throw 'hybrid-fixture-poison-accepted' }
            } elseif ($Mode -cin @('pipes','silent')) {
                $shell = (Get-Process -Id $PID).Path
                $childScript = Join-Path $Root 'hybrid-descendant.ps1'
                [IO.File]::WriteAllText($childScript,'[Threading.Thread]::Sleep(60000)')
                $parentScript = Join-Path $Root 'hybrid-target.ps1'
                $parent = @'
        param([string]$Shell,[string]$Child,[string]$Mode,[string]$Root)
        $start = [Diagnostics.ProcessStartInfo]::new($Shell)
        $start.UseShellExecute = $false; $start.CreateNoWindow = $true
        foreach ($argument in @('-NoLogo','-NoProfile','-NonInteractive','-File',$Child)) { $start.ArgumentList.Add($argument) }
        if ($Mode -ceq 'silent') { $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true }
        $descendantProcess = [Diagnostics.Process]::new(); $descendantProcess.StartInfo = $start
        if (-not $descendantProcess.Start()) { throw 'hybrid-fixture-descendant-not-started' }
        [IO.File]::WriteAllText((Join-Path $Root 'hybrid-descendant.json'),(@{ pid = $descendantProcess.Id; startedUtcTicks = $descendantProcess.StartTime.ToUniversalTime().Ticks } | ConvertTo-Json))
        $descendantProcess.Dispose()
        exit 0
        '@
                [IO.File]::WriteAllText($parentScript,$parent)
                $failed = $false
                try { [void](Invoke-HybridProcess $shell @('-NoLogo','-NoProfile','-NonInteractive','-File',$parentScript,'-Shell',$shell,'-Child',$childScript,'-Mode',$Mode,'-Root',$Root) $directory $Root) }
                catch {
                    $failed = $true
                    if ($_.Exception.Message -cne 'hybrid-process-exit-drain-deadline') { throw }
                }
                if (-not $failed) { throw 'hybrid-fixture-descendant-accepted' }
            } else { throw 'hybrid-fixture-process-mode' }
        } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
        finally { Close-HybridLocks }
        exit 0
        """;
}
