using System.Diagnostics;
using System.Text;
using System.Text.Json;
using static TestAssertions;

internal static class RegressionCiInfrastructureSuite
{
    internal static async Task RunAsync()
    {
        CaptureUsesOnlyTheExplicitProducerEnvironment();
        await RunnerAndChildForwardTheSameImmutableOwnerAsync();
        await RealPowerShellHelpersPreserveEvidenceAndCompletePaginationAsync();
    }

    private static void CaptureUsesOnlyTheExplicitProducerEnvironment()
    {
        var environment = new Dictionary<string, string?>
        {
            ["REGRESSION_PARTITION"] = "core", ["GITHUB_REPOSITORY"] = "owner/repository",
            ["GITHUB_RUN_ID"] = "431", ["GITHUB_RUN_ATTEMPT"] = "2", ["GITHUB_JOB"] = "regression"
        };
        string? Read(string name) => environment.GetValueOrDefault(name);
        var captured = RegressionCiIdentity.Capture(Read)!;
        Require(captured.Repository == "owner/repository" && captured.WorkflowRunId == 431 && captured.Attempt == 2 &&
            captured.Job == "regression" && captured.Partition == "core", "Capture retains the explicit CI producer identity.");
        foreach (var (name, invalid) in new[]
        {
            ("GITHUB_REPOSITORY", "owner/../repository"), ("GITHUB_RUN_ID", "0"),
            ("GITHUB_RUN_ATTEMPT", "-1"), ("GITHUB_JOB", "build-test-package"), ("REGRESSION_PARTITION", "unknown")
        })
        {
            var prior = environment[name];
            environment[name] = invalid;
            Reject(() => RegressionCiIdentity.Capture(Read), "Invalid producer environment cannot create CI evidence.");
            environment[name] = prior;
        }
        environment.Remove("REGRESSION_PARTITION");
        environment["GITHUB_RUN_ID"] = "invalid";
        Require(RegressionCiIdentity.Capture(Read) is null && new RegressionReport().Ci is null,
            "Unmarked and internal fake runs never borrow ambient GitHub identity.");
    }

    private static async Task RunnerAndChildForwardTheSameImmutableOwnerAsync()
    {
        using var fixture = new Fixture();
        var ci = new RegressionCiIdentity { Repository = "owner/repository", WorkflowRunId = 431, Attempt = 2, Job = "regression", Partition = "core" };
        var path = Path.Combine(fixture.Root, "forwarded.json");
        var executed = 0;
        var result = await RegressionRunner.RunAsync(["--all", "--partition", "core", "--results", path],
            [new("ci.infrastructure/fake", "core", () => throw new InvalidOperationException("The executor owns this fake boundary."))],
            (selected, report, reportPath) =>
            {
                Require(RegressionCiIdentity.Same(report.Ci, ci), "The actual runner receives its explicitly supplied CI identity.");
                var request = RegressionProcessHost.CreateRequest(report, "core", selected.Select(item => item.Id).ToArray(), reportPath);
                RegressionProcessProtocol.ValidateRequest(request);
                var requestPath = Path.Combine(fixture.Root, "request.json");
                RegressionProcessProtocol.WriteNew(requestPath, request);
                var received = RegressionProcessProtocol.Read<RegressionChildRequest>(requestPath);
                var manifest = RegressionChildManifest.Create(received);
                Require(RegressionCiIdentity.Same(request.Ci, ci) && RegressionCiIdentity.Same(received.Ci, ci) && RegressionCiIdentity.Same(manifest.Ci, ci),
                    "Parent creation, serialized child request and manifest creation forward the same producer owner.");
                RegressionProcessProtocol.ValidateManifest(manifest, request);
                manifest.Ci!.Attempt++;
                Reject(() => RegressionProcessProtocol.ValidateManifest(manifest, request), "A child cannot change the parent CI attempt.");
                report.Cases[0].Outcome = "passed";
                executed++;
                return Task.CompletedTask;
            }, ci);
        Require(result == 0 && executed == 1 && RegressionCiIdentity.Same(RegressionReport.Read(path).Ci, ci),
            "One fake execution persists the explicitly forwarded CI owner without invoking a native case.");

        var owned = new RegressionReport { Ci = ci, Cases = [new() { Id = "ci.infrastructure/immutable", Partition = "core" }] };
        var ownedPath = Path.Combine(fixture.Root, "immutable.json");
        owned.Create(ownedPath);
        owned.Ci!.Attempt++;
        Reject(() => owned.Write(ownedPath), "Immutable report generations cannot change their CI owner.");
        Require(RegressionReport.Read(ownedPath).Ci!.Attempt == 2, "Rejected owner mutation leaves the original generation unchanged.");
    }

    private static async Task RealPowerShellHelpersPreserveEvidenceAndCompletePaginationAsync()
    {
        using var fixture = new Fixture();
        var script = Path.Combine(fixture.Root, "helpers.ps1");
        File.WriteAllText(script, PowerShellContracts);
        var info = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(script);
        info.Environment["REGRESSION_HELPER_FIXTURE"] = fixture.Root;
        info.Environment["REGRESSION_HELPER_REPO"] = Path.GetDirectoryName(FindRepoFile("AGENTS.md"))!;
        using var process = Process.Start(info) ?? throw new InvalidOperationException("CI helper process did not start.");
        var output = DrainBoundedAsync(process.StandardOutput);
        var error = DrainBoundedAsync(process.StandardError);
        try { await RegressionExecution.WaitAsync(Task.WhenAll(process.WaitForExitAsync(), output, error)); }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            try { await Task.WhenAll(process.WaitForExitAsync(), output, error).WaitAsync(RegressionExecution.DiagnosticTimeout); } catch { }
            throw;
        }
        Require(process.ExitCode == 0 && (await output).Contains("ci-helper-contracts-passed", StringComparison.Ordinal),
            "Real PowerShell evidence collection and fake API pagination contracts must pass.");
        foreach (var name in new[] { "normal", "zero" })
        {
            var read = RegressionReport.Read(Path.Combine(fixture.Root, "bundles", name, "report.json"));
            Require(read.Generation == 3 && read.Complete && read.Cases.Single().Outcome == "passed" && read.Source.Available,
                "The real collector retains a readable authoritative latest generation behind a stale projection.");
        }
        var missing = RegressionReport.Read(Path.Combine(fixture.Root, "bundles", "missing", "report.json"));
        Require(missing.Generation == 3 && missing.Complete && !missing.Source.Available,
            "Missing canonical remains missing and follows conservative recovery rather than manufactured source authority.");
        Reject(() => RegressionReport.Read(Path.Combine(fixture.Root, "bundles", "corrupt", "report.json")),
            "A corrupt highest committed generation stays corrupt after compaction; no older pass rescues it.");
    }

    private static async Task<string> DrainBoundedAsync(TextReader reader)
    {
        var buffer = new char[2048];
        var captured = new StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory())) != 0)
            if (captured.Length < 16 * 1024) captured.Append(buffer, 0, Math.Min(count, 16 * 1024 - captured.Length));
        return captured.ToString();
    }

    private static void Reject(Action action, string reason)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or RegressionSelectionException or JsonException) { return; }
        throw new InvalidOperationException(reason);
    }
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "FishAudio-CiInfrastructure-" + Guid.NewGuid().ToString("N"));
        internal Fixture() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private const string PowerShellContracts = """
        $ErrorActionPreference = 'Stop'
        . (Join-Path $env:REGRESSION_HELPER_REPO 'tools/CollectRegressionEvidence.ps1')
        . (Join-Path $env:REGRESSION_HELPER_REPO 'tools/GetRegressionJobAttempts.ps1')
        $root = $env:REGRESSION_HELPER_FIXTURE
        $run = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
        $child = "$run.bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.child.json"
        $partial = "$run.cccccccccccccccccccccccccccccccc.child.json"
        function Require($value, $reason) { if (-not $value) { throw $reason } }
        function Reject([scriptblock]$action, $reason) {
            $rejected = $false
            try { & $action | Out-Null } catch { $rejected = $true }
            Require $rejected $reason
        }
        function New-Fixture($name) {
            $source = Join-Path $root "sources/$name"
            $owner = Join-Path $source "report.json.generations/$run"
            [IO.Directory]::CreateDirectory($owner) | Out-Null
            foreach ($generation in 0..3) {
                $report = [ordered]@{
                    schemaVersion = 1; generation = $generation; runId = $run; startedUtc = '2026-01-01T00:00:00+00:00'
                    source = @{ available = $true; dirty = $false; commit = ('a' * 40); tree = ('b' * 40); fingerprint = ('c' * 64) }
                    configuration = 'Release'; sdk = '10.0.402'; complete = ($generation -eq 3)
                    cases = @(@{ id = 'ci.infrastructure/fixture'; partition = 'core'; outcome = $(if ($generation -eq 3) { 'passed' } else { 'not-run' }) })
                }
                if ($generation -eq 3) { $report.finishedUtc = '2026-01-01T00:00:01+00:00' }
                [IO.File]::WriteAllText((Join-Path $owner ($generation.ToString('D20') + '.json')), ($report | ConvertTo-Json -Depth 10))
            }
            [IO.File]::Copy((Join-Path $owner '00000000000000000001.json'), (Join-Path $source 'report.json'))
            [IO.File]::WriteAllText((Join-Path $source $child), '{"final":true}')
            foreach ($index in 1..3) { [IO.File]::WriteAllText((Join-Path $source "$child.snapshot-$($index.ToString('D6')).json"), "{`"index`":$index}") }
            foreach ($index in 1..2) { [IO.File]::WriteAllText((Join-Path $source "$partial.snapshot-$($index.ToString('D6')).json"), "{`"index`":$index}") }
            [IO.File]::WriteAllText((Join-Path $source "$child.request.json"), 'private-request')
            [IO.File]::WriteAllText((Join-Path $source "$child.unfinished.tmp"), 'unfinished')
            [IO.File]::WriteAllText((Join-Path $source 'unrelated-private.txt'), 'unrelated')
            return $source
        }
        foreach ($name in @('normal', 'zero', 'missing', 'corrupt', 'bad-name', 'no-zero')) { $null = New-Fixture $name }
        $zero = Join-Path $root 'sources/zero'
        [IO.File]::Copy((Join-Path $zero "report.json.generations/$run/00000000000000000000.json"), (Join-Path $zero 'report.json'), $true)
        [IO.File]::Delete((Join-Path $root 'sources/missing/report.json'))
        [IO.File]::WriteAllText((Join-Path $root "sources/corrupt/report.json.generations/$run/00000000000000000003.json"), '{corrupt-latest')
        [IO.File]::WriteAllText((Join-Path $root "sources/bad-name/report.json.generations/$run/0000000000000000000x.json"), '{}')
        [IO.File]::Delete((Join-Path $root "sources/no-zero/report.json.generations/$run/00000000000000000000.json"))
        $original = @{}
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'sources') -File -Recurse) { $original[$file.FullName] = [Convert]::ToBase64String([IO.File]::ReadAllBytes($file.FullName)) }
        foreach ($name in @('normal', 'zero', 'missing', 'corrupt')) {
            Copy-RegressionEvidence (Join-Path $root "sources/$name/report.json") (Join-Path $root "bundles/$name")
        }
        $normal = Join-Path $root 'bundles/normal'
        $expected = @('report.json', "report.json.generations/$run/00000000000000000000.json", "report.json.generations/$run/00000000000000000001.json", "report.json.generations/$run/00000000000000000003.json", $child, "$child.snapshot-000003.json", "$partial.snapshot-000002.json")
        $files = @(Get-ChildItem -LiteralPath $normal -File -Recurse)
        Require ($files.Count -eq $expected.Count) 'collector-exact-compact-file-count'
        foreach ($relative in $expected) {
            $source = Join-Path $root "sources/normal/$relative"
            $copy = Join-Path $normal $relative
            Require ([Convert]::ToBase64String([IO.File]::ReadAllBytes($source)) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($copy))) 'collector-byte-preservation'
        }
        Require (-not (Test-Path -LiteralPath (Join-Path $root 'bundles/missing/report.json'))) 'collector-must-not-manufacture-canonical'
        Require (@(Get-ChildItem -LiteralPath (Join-Path $root "bundles/zero/report.json.generations/$run") -File).Count -eq 2) 'collector-deduplicates-zero-reference'
        Reject { Copy-RegressionEvidence (Join-Path $root 'sources/bad-name/report.json') (Join-Path $root 'bundles/bad-name') } 'collector-cannot-drop-invalid-committed-name'
        Reject { Copy-RegressionEvidence (Join-Path $root 'sources/no-zero/report.json') (Join-Path $root 'bundles/no-zero') } 'collector-requires-zero'
        Reject { Copy-RegressionEvidence (Join-Path $root 'sources/normal/report.json') $normal } 'collector-no-overwrite'
        Reject { Copy-RegressionEvidence (Join-Path $root 'sources/normal/report.json') (Join-Path $root 'sources/normal/nested') } 'collector-no-self-copy'
        foreach ($path in $original.Keys) { Require ([Convert]::ToBase64String([IO.File]::ReadAllBytes($path)) -ceq $original[$path]) 'collector-original-bytes-unchanged' }

        $fixtureJobs = @(foreach ($id in 1..101) { [pscustomobject]@{ id = $id; run_id = 431; run_attempt = 2; name = "Regression (core) $id"; status = 'completed'; conclusion = 'success'; head_sha = ('c' * 40); raw = 'retained' } })
        $script:pages = 0
        $snapshot = Get-RegressionJobSnapshot 'owner/repository' 431 {
            param($page)
            $script:pages++
            if ($page -eq 1) { [pscustomobject]@{ total_count = 101; jobs = @($fixtureJobs[0..99]) } }
            elseif ($page -eq 2) { [pscustomobject]@{ total_count = 101; jobs = @($fixtureJobs[100]) } }
            else { throw 'unexpected-page' }
        }
        Require ($script:pages -eq 2 -and $snapshot.repository -ceq 'owner/repository' -and $snapshot.runId -eq 431 -and $snapshot.filter -ceq 'all' -and $snapshot.totalCount -eq 101 -and $snapshot.jobs.Count -eq 101 -and $snapshot.jobs[100].raw -ceq 'retained') 'jobs-complete-raw-two-page-snapshot'
        foreach ($fault in @('truncated', 'duplicate', 'changed-total', 'unavailable', 'foreign-run')) {
            Reject {
                Get-RegressionJobSnapshot 'owner/repository' 431 {
                    param($page)
                    if ($fault -eq 'unavailable') { throw 'fake-api-unavailable' }
                    if ($page -eq 1) { return [pscustomobject]@{ total_count = 101; jobs = @($fixtureJobs[0..99]) } }
                    $tail = if ($fault -eq 'truncated') { @() } elseif ($fault -eq 'duplicate') { @($fixtureJobs[99]) } elseif ($fault -eq 'foreign-run') { @([pscustomobject]@{ id = 101; run_id = 999 }) } else { @($fixtureJobs[100]) }
                    [pscustomobject]@{ total_count = $(if ($fault -eq 'changed-total') { 102 } else { 101 }); jobs = $tail }
                }
            } "jobs-reject-$fault"
        }
        Write-Output 'ci-helper-contracts-passed'
        """;
}
