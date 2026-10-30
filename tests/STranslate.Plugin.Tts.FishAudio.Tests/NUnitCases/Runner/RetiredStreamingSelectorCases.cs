using System.Diagnostics;
using NUnit.Framework;

namespace NUnitCases.Runner;

[TestFixture]
[NonParallelizable]
public sealed class RetiredStreamingSelectorCases
{
    [TestCaseSource(nameof(SelectorRows))]
    public Task RealLegacyEntrypointRejectsRetiredStreamingSelection(string scenario, string expectedCode)
    {
        Process? process = null;
        Task<string>? output = null;
        Task<string>? error = null;
        var started = false;
        return RegressionFixtureOwner.RunAsync(async () =>
        {
            var parent = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
            if (string.IsNullOrEmpty(parent))
                parent = Path.Combine(Path.GetTempPath(), "FishAudioLegacySelectorEvidence");
            if (!Path.IsPathFullyQualified(parent))
                throw new InvalidOperationException("retired-selector-evidence-root-invalid");
            var directory = Path.Combine(parent, "retired-selector-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var arguments = new List<string> { typeof(RegressionReport).Assembly.Location, "--legacy", "--list" };
            var originals = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var childTarget = scenario.StartsWith("child-", StringComparison.Ordinal);
            var route = childTarget ? scenario["child-".Length..] : scenario;
            if (route.StartsWith("replay-", StringComparison.Ordinal))
            {
                var outcome = route == "replay-not-run" ? "not-run" : route == "replay-failed" ? "failed" : "passed";
                var source = await RegressionSource.CaptureAsync();
                source.RecordDirtyScope(RegressionCatalog.Create());
                var previous = new RegressionReport
                {
                    // A synthetic old report is an input witness, never a baseline.
                    Source = route == "replay-drift-passed"
                        ? new RegressionSourceIdentity { Available = false, Fingerprint = new string('f', 64) }
                        : source.Identity,
                    Cases = [new() { Id = childTarget ? StreamingTextChildCompatibilityTarget.HistoricalScenarioId : "playback.streaming/run-all", Partition = "core", Outcome = outcome }]
                };
                var path = Path.Combine(directory, "previous.json");
                previous.Create(path);
                foreach (var input in new[] { path, RegressionReport.GenerationPath(path, previous.RunId, 0) })
                    originals.Add(input, await File.ReadAllBytesAsync(input));
                arguments.AddRange(["--rerun-failed", path]);
            }
            else
            {
                arguments.Add(route == "group" ? "--group" : "--case");
                arguments.Add(childTarget ? StreamingTextChildCompatibilityTarget.HistoricalScenarioId : route == "leaf"
                    ? "playback.streaming/pcm-byte-carry-and-odd-tail"
                    : "playback.streaming/run-all");
            }
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(typeof(RegressionReport).Assembly.Location)!
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            process = new Process { StartInfo = start };
            started = process.Start();
            if (!started) throw new IOException("retired-selector-process-start-failed");
            output = process.StandardOutput.ReadToEndAsync();
            error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Task.WhenAll(output, error);
            await File.WriteAllTextAsync(Path.Combine(directory, "stdout.log"), output.Result);
            await File.WriteAllTextAsync(Path.Combine(directory, "stderr.log"), error.Result);
            Assert.That(process.ExitCode, Is.EqualTo(2), "retired-selector-must-not-fallback-to-another-selection");
            Assert.That(error.Result.Trim(), Is.EqualTo("Runner selection error: " + expectedCode + "."),
                "retired-selector-must-reach-the-real-selection-boundary");
            Assert.That(output.Result.Contains("Selected ", StringComparison.Ordinal), Is.False,
                "A rejected legacy selector must not list or execute an unrelated set.");
            foreach (var input in originals)
                Assert.That(await File.ReadAllBytesAsync(input.Key), Is.EqualTo(input.Value),
                    "Selector rejection must preserve the old report and its committed generation.");
        },
        async () =>
        {
            if (started && process is not null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        },
        async () => { if (output is not null) await output; },
        async () => { if (error is not null) await error; },
        () => { process?.Dispose(); return Task.CompletedTask; });
    }

    private static IEnumerable<TestCaseData> SelectorRows()
    {
        foreach (var row in new (string Key, string Code)[]
        {
            ("case", "empty-case-alias"), ("group", "empty-case-alias"), ("leaf", "unknown-case"),
            ("replay-failed", "empty-case-alias"), ("replay-not-run", "empty-case-alias"),
            ("replay-passed", "empty-case-alias"), ("replay-drift-passed", "empty-case-alias"),
            ("child-case", "retired-case"), ("child-group", "retired-case"),
            ("child-replay-failed", "retired-case"), ("child-replay-not-run", "retired-case"),
            ("child-replay-passed", "retired-case"), ("child-replay-drift-passed", "retired-case")
        })
            yield return new TestCaseData(row.Key, row.Code)
                .SetName("RetiredStreamingSelector_" + row.Key)
                .SetProperty("FishAudioCaseId", "infra.runner/retired-streaming-selector/" + row.Key);
    }
}