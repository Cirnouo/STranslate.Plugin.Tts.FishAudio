using System.Diagnostics;
using System.Text.Json;

internal static class HookAcceptanceSuite
{
    // Temporary executable-runner integration. These descriptors migrate to a
    // local NUnit TestCaseSource with the rest of the suite, not a second catalog.
    internal static IReadOnlyList<RegressionCase> CreateCases() => new[]
    {
        "validator-failure", "partial-stage", "staged-success", "alternate-index",
        "missing-hook", "fresh-checkout", "linked-worktree", "previous-hook-order",
        "previous-hook-failure", "untracked-output", "raw-bytes", "index-mutation",
        "verifier-rejects-report", "late-commit-message-mutation"
    }.Select(scenario => new RegressionCase("runner.hooks/" + scenario, "core", () => RunAsync(scenario))).ToArray();

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
