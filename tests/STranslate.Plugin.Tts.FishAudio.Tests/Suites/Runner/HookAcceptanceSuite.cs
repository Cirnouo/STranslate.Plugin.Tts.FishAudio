using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;

[TestFixture]
[NonParallelizable]
public sealed class HookAcceptanceSuite
{
    [TestCaseSource(nameof(Cases))]
    public Task InstalledHookScenarioAsync(string scenario) => RunAsync(scenario);

    // Pure discovery metadata. OriginalOrdinal records provenance, not scheduling.
    private static IEnumerable<TestCaseData> Cases()
    {
        yield return new TestCaseData("validator-failure")
            .SetName("InstalledHook_ValidatorFailure")
            .SetProperty("FishAudioCaseId", "runner.hooks/validator-failure")
            .SetProperty("OriginalOrdinal", 1567);
        yield return new TestCaseData("partial-stage")
            .SetName("InstalledHook_PartialStage")
            .SetProperty("FishAudioCaseId", "runner.hooks/partial-stage")
            .SetProperty("OriginalOrdinal", 1568);
        yield return new TestCaseData("staged-success")
            .SetName("InstalledHook_StagedSuccess")
            .SetProperty("FishAudioCaseId", "runner.hooks/staged-success")
            .SetProperty("OriginalOrdinal", 1569);
        yield return new TestCaseData("alternate-index")
            .SetName("InstalledHook_AlternateIndex")
            .SetProperty("FishAudioCaseId", "runner.hooks/alternate-index")
            .SetProperty("OriginalOrdinal", 1570);
        yield return new TestCaseData("missing-hook")
            .SetName("InstalledHook_MissingHook")
            .SetProperty("FishAudioCaseId", "runner.hooks/missing-hook")
            .SetProperty("OriginalOrdinal", 1571);
        yield return new TestCaseData("fresh-checkout")
            .SetName("InstalledHook_FreshCheckout")
            .SetProperty("FishAudioCaseId", "runner.hooks/fresh-checkout")
            .SetProperty("OriginalOrdinal", 1572);
        yield return new TestCaseData("linked-worktree")
            .SetName("InstalledHook_LinkedWorktree")
            .SetProperty("FishAudioCaseId", "runner.hooks/linked-worktree")
            .SetProperty("OriginalOrdinal", 1573);
        yield return new TestCaseData("previous-hook-order")
            .SetName("InstalledHook_PreviousHookOrder")
            .SetProperty("FishAudioCaseId", "runner.hooks/previous-hook-order")
            .SetProperty("OriginalOrdinal", 1574);
        yield return new TestCaseData("previous-hook-failure")
            .SetName("InstalledHook_PreviousHookFailure")
            .SetProperty("FishAudioCaseId", "runner.hooks/previous-hook-failure")
            .SetProperty("OriginalOrdinal", 1575);
        yield return new TestCaseData("untracked-output")
            .SetName("InstalledHook_UntrackedOutput")
            .SetProperty("FishAudioCaseId", "runner.hooks/untracked-output")
            .SetProperty("OriginalOrdinal", 1576);
        yield return new TestCaseData("raw-bytes")
            .SetName("InstalledHook_RawBytes")
            .SetProperty("FishAudioCaseId", "runner.hooks/raw-bytes")
            .SetProperty("OriginalOrdinal", 1577);
        yield return new TestCaseData("index-mutation")
            .SetName("InstalledHook_IndexMutation")
            .SetProperty("FishAudioCaseId", "runner.hooks/index-mutation")
            .SetProperty("OriginalOrdinal", 1578);
        yield return new TestCaseData("verifier-rejects-report")
            .SetName("InstalledHook_VerifierRejectsReport")
            .SetProperty("FishAudioCaseId", "runner.hooks/verifier-rejects-report")
            .SetProperty("OriginalOrdinal", 1579);
        yield return new TestCaseData("late-commit-message-mutation")
            .SetName("InstalledHook_LateCommitMessageMutation")
            .SetProperty("FishAudioCaseId", "runner.hooks/late-commit-message-mutation")
            .SetProperty("OriginalOrdinal", 1580);
    }

    internal static Task RunAsync(string scenario)
    {
        Process? process = null;
        Task<string>? output = null;
        Task<string>? error = null;
        var started = false;
        return RegressionFixtureOwner.RunAsync(async () =>
        {
            var script = TestAssertions.FindRepoFile(Path.Combine("tools", "tests", "TestHookAcceptance.ps1"));
            var contextPath = Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT");
            var evidence = string.IsNullOrEmpty(contextPath) ? Path.Combine(Path.GetTempPath(), "FishAudioHookTests") : Path.GetDirectoryName(contextPath)!;
            var result = Path.Combine(evidence, "hook-contracts", scenario + "-" + Guid.NewGuid().ToString("N") + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(result)!);
            var start = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-File", script, "-Scenario", scenario, "-ResultsPath", result })
                start.ArgumentList.Add(argument);
            process = new Process { StartInfo = start };
            started = process.Start();
            if (!started) throw new IOException("Hook acceptance process did not start.");
            output = process.StandardOutput.ReadToEndAsync();
            error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Task.WhenAll(output, error);
            await File.WriteAllTextAsync(result + ".stdout.log", output.Result);
            await File.WriteAllTextAsync(result + ".stderr.log", error.Result);
            if (process.ExitCode != 0) throw new InvalidOperationException("Installed hook acceptance failed.");
            using var report = JsonDocument.Parse(await File.ReadAllBytesAsync(result));
            var root = report.RootElement;
            var cases = root.GetProperty("cases").EnumerateArray().ToArray();
            if (!root.GetProperty("complete").GetBoolean() || root.GetProperty("notRun").GetArrayLength() != 0 ||
                cases.Length != 1 || cases[0].GetProperty("id").GetString() != scenario ||
                cases[0].GetProperty("outcome").GetString() != "passed")
                throw new InvalidDataException("Installed hook acceptance evidence is incomplete.");
        }, async () =>
        {
            if (process is null) return;
            try
            {
                if (started && !process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                if (output is not null && error is not null) await Task.WhenAll(output, error);
            }
            finally { process.Dispose(); }
        });
    }
}
