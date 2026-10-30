using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using NUnit.Framework;
using static TestAssertions;

namespace NUnitCases.Runner;

[TestFixture]
[NonParallelizable]
public sealed class RetiredNativeSettingsSelectorCases
{
    [Test]
    [Property("FishAudioCaseId", "infra.runner/retired-offline-core/exact-owner-selection-rejects-before-sta")]
    public void TransferredOfflineCoreOwnersRejectBeforeAnyActionOrStaWarmup()
    {
        var dispatcher = (Lazy<Task<Dispatcher>>)typeof(StaTestHost).GetField("SharedDispatcher", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var wasCreated = dispatcher.IsValueCreated;
        var events = new List<RegressionActionEvent>();
        using var owner = RegressionActionScope.BeginCase("runner.probe/retired-offline-metadata", events.Add);
        var current = RegressionCatalog.Create();
        using var reference = JsonDocument.Parse(File.ReadAllBytes(FindRepoFile("tests/Migration/legacy-catalog.json")));
        var original = reference.RootElement.GetProperty("cases").EnumerateArray()
            .Where(item => item.GetProperty("partition").GetString() == "core")
            .Select(item => item.GetProperty("id").GetString()!).ToArray();
        var retired = new[]
        {
            "lifecycle.activity/overlapping-startup-manual-and-tts-operations-unlock-after-final-completion",
            "lifecycle.activity-dispatcher/deadline-diagnostics-preserve-original-failure-without-retry",
            "input.settings/native-tool-tip-diagnostics-preserve-failures-without-retry",
            "popup.search-adjustment/matrix-diagnostics-preserve-failures-without-retry",
            "popup.search-adjustment/viewport-diagnostics-preserve-async-completion-and-failures",
            "preview.ownership/preview-preparation-transitions-to-real-playback",
            "preview.ownership/preview-same-voice-across-entries-cancels-preparation",
            "preview.ownership/preview-old-completion-cannot-clear-new-preparation",
            "preview.ownership/preview-preparation-failure-and-reset-clear-state",
            "preview.ownership/preview-queued-publications-read-current-preparation-owner",
            "preview.ownership/run-bounded-preview-regression-tests",
            "preview.ownership/search-preview-removed-result-ignores-late-refresh",
            "preview.ownership/search-preview-second-click-cancels-pending-refresh",
            "preview.ownership/display-preview-second-click-cancels-pending-refresh-without-restart",
            "preview.ownership/search-preview-invalidates-pending-display-refresh",
            "preview.ownership/display-preview-voice-switch-ignores-late-refresh",
            "preview.ownership/display-preview-dispose-cancels-and-ignores-late-refresh",
            "input.clear/run-requests",
            "voice.search-adjustment/run-requests",
        };
        Assert.That(retired, Has.Length.EqualTo(19));
        Assert.That(retired, Is.Unique);
        foreach (var id in retired)
        {
            Assert.That(original.Contains(id, StringComparer.Ordinal), Is.True);
            Assert.That(current.Any(item => item.Id == id), Is.False);
            foreach (var selector in new[] { "--case", "--group" })
            {
                var rejected = Assert.Throws<RegressionSelectionException>(() =>
                    RegressionSelection.Parse([selector, id]).ExplicitIds(current));
                Assert.That(rejected!.Message, Is.EqualTo("retired-case"));
            }
        }
        Assert.That(events, Is.Empty);
        Assert.That(dispatcher.IsValueCreated, Is.EqualTo(wasCreated));
    }

    [Test]
    [Property("FishAudioCaseId", "infra.runner/retired-native-settings/all-original-owner-identities")]
    public void EveryOriginalNativeSettingsOwnerRejectsWithoutExecutingOrWarmingSta()
    {
        var dispatcher = (Lazy<Task<Dispatcher>>)typeof(StaTestHost).GetField("SharedDispatcher", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var wasCreated = dispatcher.IsValueCreated;
        var events = new List<RegressionActionEvent>();
        using var owner = RegressionActionScope.BeginCase("runner.probe/retired-metadata", events.Add);
        var current = RegressionCatalog.Create();
        using var reference = JsonDocument.Parse(File.ReadAllBytes(FindRepoFile("tests/Migration/legacy-catalog.json")));
        var original = reference.RootElement.GetProperty("cases").EnumerateArray()
            .Where(item => item.GetProperty("partition").GetString() == "native-settings")
            .Select(item => item.GetProperty("id").GetString()!).ToArray();
        Assert.That(original, Has.Length.EqualTo(94));
        foreach (var id in original)
        {
            Assert.That(current.Any(item => item.Id == id), Is.False, "A migrated native owner cannot remain default legacy work: " + id);
            foreach (var selector in new[] { "--case", "--group" })
            {
                var rejected = Assert.Throws<RegressionSelectionException>(() =>
                    RegressionSelection.Parse([selector, id]).ExplicitIds(current));
                Assert.That(rejected!.Message, Is.EqualTo("retired-case"),
                    "A retired exact owner must reject before selection/replay source expansion: " + id);
            }
        }
        Assert.That(events, Is.Empty, "Constructing metadata and rejecting selectors cannot queue an action.");
        Assert.That(dispatcher.IsValueCreated, Is.EqualTo(wasCreated));
    }

    [Test]
    [Property("FishAudioCaseId", "infra.runner/retired-native-settings/direct-selectors-reject-before-source-or-child")]
    public async Task RetiredDirectSelectorsRejectBeforeSourceCaptureAndChildLaunch()
    {
        var source = Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT");
        var stderr = Console.Error;
        using var errors = new StringWriter();
        var launches = 0;
        using var process = new RegressionProcessHost(_ => { launches++; throw new InvalidOperationException("retired-selector-launched-child"); });
        try
        {
            // A source capture would fail; a retired direct request must reject first.
            Environment.SetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT", Path.Combine(Path.GetTempPath(), "absent-retired-context-" + Guid.NewGuid().ToString("N") + ".json"));
            Console.SetError(errors);
            foreach (var selector in DirectSelectors)
            {
                Assert.That(LegacyRegressionSelectors.AreArgumentsValid([selector]), Is.False);
                Assert.That(await RegressionProcessHost.TryRunLegacyAsync([selector], process), Is.EqualTo(2));
            }
            Assert.That(launches, Is.Zero);
            Assert.That(errors.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries),
                Is.EqualTo(DirectSelectors.Select(_ => "Retired legacy regression selector; use NUnit case selection.")));
        }
        finally
        {
            Console.SetError(stderr);
            Environment.SetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT", source);
        }
    }

    [Test]
    [Property("FishAudioCaseId", "infra.runner/retired-native-settings/narrow-proof-diagnostic-dispatch")]
    public async Task NarrowProofSelectorPreservesOnlyTheOriginalOptionalProofOperation()
    {
        var calls = new List<string?>();
        var stdout = Console.Out;
        var stderr = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        var owned = new IOException("private-proof-owned-failure");
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            foreach (var arguments in new string[][]
            {
                ["--numeric-typography-proof-tests"],
                ["--numeric-typography-proof-tests", "private-proof-directory"],
            })
            {
                Assert.That(LegacyRegressionSelectors.AreArgumentsValid(arguments), Is.True);
                Assert.That(await LegacyRegressionSelectors.TryRunAsync(arguments, value =>
                {
                    calls.Add(value);
                    return Task.CompletedTask;
                }), Is.EqualTo(0));
            }
            Assert.That(calls, Is.EqualTo(new string?[] { null, "private-proof-directory" }));
            Assert.That(output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries),
                Is.EqualTo(Enumerable.Repeat("Numeric typography proof diagnostics passed (manual diagnostic only).", 2)),
                "The proof selector makes only its narrow manual diagnostic claim.");
            foreach (var arguments in new string[][]
            {
                ["--numeric-typography-proof-tests", "--list"],
                ["--numeric-typography-proof-tests", "proof", "extra"],
                ["--numeric-typography-tests"],
            })
                Assert.That(await LegacyRegressionSelectors.TryRunAsync(arguments, _ =>
                {
                    calls.Add("unexpected");
                    return Task.CompletedTask;
                }), Is.EqualTo(2));
            Assert.That(calls, Has.Count.EqualTo(2), "Malformed or retired mixed selectors cannot execute even the narrow proof callback.");
            var actual = Assert.ThrowsAsync<IOException>(async () =>
            {
                await LegacyRegressionSelectors.TryRunAsync(["--numeric-typography-proof-tests"], _ => throw owned);
            });
            Assert.That(actual, Is.SameAs(owned), "The original proof callback's failure is not translated into success or replaced.");
        }
        finally { Console.SetOut(stdout); Console.SetError(stderr); }
    }

    [TestCaseSource(nameof(CliRows))]
    public Task ActualEntrypointRejectsRetiredSelectionsAndPreservesReplay(string key, string[] selection, string expected)
    {
        Process? process = null;
        Task<string>? output = null;
        Task<string>? error = null;
        var started = false;
        return RegressionFixtureOwner.RunAsync(async () =>
        {
            var parent = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
            if (string.IsNullOrEmpty(parent)) parent = Path.Combine(Path.GetTempPath(), "FishAudioRetiredNativeSelectors");
            if (!Path.IsPathFullyQualified(parent)) throw new InvalidOperationException("retired-native-evidence-root-invalid");
            var directory = Path.Combine(parent, "native-selector-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var arguments = new List<string> { typeof(RegressionReport).Assembly.Location, "--legacy" };
            var originals = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (key.StartsWith("replay-", StringComparison.Ordinal))
            {
                var oldSource = await RegressionSource.CaptureAsync();
                oldSource.RecordDirtyScope(RegressionCatalog.Create());
                var previous = new RegressionReport
                {
                    Source = key.Contains("drift", StringComparison.Ordinal)
                        ? new RegressionSourceIdentity { Available = false, Fingerprint = new string('f', 64) }
                        : oldSource.Identity,
                    Cases = [new() { Id = selection[0], Partition = "native-settings", Outcome = selection[1] }],
                };
                var path = Path.Combine(directory, "previous.json");
                previous.Create(path);
                foreach (var input in new[] { path, RegressionReport.GenerationPath(path, previous.RunId, 0) })
                    originals.Add(input, await File.ReadAllBytesAsync(input));
                arguments.AddRange(["--list", "--rerun-failed", path]);
            }
            else arguments.AddRange(selection);
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.GetDirectoryName(typeof(RegressionReport).Assembly.Location)!,
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            process = new Process { StartInfo = start };
            started = process.Start();
            if (!started) throw new IOException("retired-native-selector-process-start-failed");
            output = process.StandardOutput.ReadToEndAsync();
            error = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(process.WaitForExitAsync(), output, error);
            await File.WriteAllTextAsync(Path.Combine(directory, "stdout.log"), output.Result);
            await File.WriteAllTextAsync(Path.Combine(directory, "stderr.log"), error.Result);
            Assert.That(process.ExitCode, Is.EqualTo(2), "A retired request must fail rather than widen/narrow the current legacy set.");
            Assert.That(error.Result.Trim(), Is.EqualTo(expected));
            Assert.That(output.Result.Contains("Selected ", StringComparison.Ordinal), Is.False);
            foreach (var input in originals)
                Assert.That(await File.ReadAllBytesAsync(input.Key), Is.EqualTo(input.Value),
                    "Rejected replay preserves both the requested report and its immutable generation verbatim.");
        }, async () =>
        {
            if (!started || process is null) return;
            var clock = Stopwatch.StartNew();
            TimeSpan Remaining()
            {
                var remaining = RegressionExecution.DiagnosticTimeout - clock.Elapsed;
                return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException("retired-native-selector-drain-deadline");
            }
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(Remaining());
            if (output is not null && error is not null) await Task.WhenAll(output, error).WaitAsync(Remaining());
        }, () => { process?.Dispose(); return Task.CompletedTask; });
    }

    private static readonly string[] DirectSelectors =
    [
        "--execution-guard-tests", "--runner-contract-tests", "--input-clear-compatibility-tests",
        "--input-clear-control-tests", "--numeric-bottom-tests", "--numeric-bottom-lifecycle-tests",
        "--numeric-typography-tests", "--numeric-glyph-observation-tests", "--activity-dispatcher-tests",
        "--voice-card-ink-tests", "--settings-layout-tests", "--settings-control-tests", "--inline-numeric-tests",
        "--input-clear-request-tests",
        "--search-adjustment-request-tests",
        "--preview-ownership-compatibility-tests",
        "--voice-auth-tests",
        "--search-adjustment-diagnostic-tests",
        "--numeric-tooltip-diagnostics-tests",
        "--search-adjustment-control-tests",
    ];

    private static IEnumerable<TestCaseData> CliRows()
    {
        foreach (var (name, id) in new[]
        {
            ("compact", "numeric.geometry/compact-numeric-geometry"),
            ("bottom", "numeric.bottom/run"), ("ink", "cards.ink/run"),
        })
        foreach (var selector in new[] { "--case", "--group" })
            yield return Row(name + "-" + selector[2..], ["--list", selector, id], "Runner selection error: empty-case-alias.");
        yield return Row("empty-partition", ["--list", "--partition", "native-settings"], "Runner selection error: unknown-partition.");
        yield return Row("direct-native", ["--input-clear-control-tests"], "Retired legacy regression selector; use NUnit case selection.");
        yield return Row("direct-mixed", ["--activity-dispatcher-tests"], "Retired legacy regression selector; use NUnit case selection.");
        yield return Row("direct-runner", ["--runner-contract-tests"], "Retired legacy regression selector; use NUnit case selection.");
        yield return Row("direct-offline-1", ["--input-clear-request-tests"], "Retired legacy regression selector; use NUnit case selection.");
        yield return Row("direct-offline-2", ["--search-adjustment-request-tests"], "Retired legacy regression selector; use NUnit case selection.");
        yield return Row("direct-offline-3", ["--preview-ownership-compatibility-tests"], "Retired legacy regression selector; use NUnit case selection.");
        yield return Row("direct-offline-4", ["--voice-auth-tests"], "Retired legacy regression selector; use NUnit case selection.");
        yield return Row("direct-offline-5", ["--search-adjustment-diagnostic-tests"], "Retired legacy regression selector; use NUnit case selection.");
        yield return Row("direct-offline-6", ["--numeric-tooltip-diagnostics-tests"], "Retired legacy regression selector; use NUnit case selection.");
        yield return Row("direct-offline-7", ["--search-adjustment-control-tests"], "Retired legacy regression selector; use NUnit case selection.");
        yield return Row("direct-offline-search-filter", ["--search-adjustment-control-tests", "titles"], "Retired legacy regression selector; use NUnit case selection.");
        foreach (var outcome in new[] { "failed", "not-run", "passed" })
            yield return Row("replay-aggregate-" + outcome, ["numeric.geometry/compact-numeric-geometry", outcome], "Runner selection error: empty-case-alias.");
        yield return Row("replay-aggregate-drift-passed", ["numeric.geometry/compact-numeric-geometry", "passed"], "Runner selection error: empty-case-alias.");
        yield return Row("replay-exact-drift-passed", ["settings.api-key-card/run-all", "passed"], "Runner selection error: retired-case.");
    }

    private static TestCaseData Row(string key, string[] selection, string expected) =>
        new TestCaseData(key, selection, expected).SetName("RetiredNativeSettings_" + key)
            .SetProperty("FishAudioCaseId", "infra.runner/retired-native-settings/" + key);
}
