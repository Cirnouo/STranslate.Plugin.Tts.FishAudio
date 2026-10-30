using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using NUnit.Framework;
using static TestAssertions;

namespace NUnitCases.Runner;

[TestFixture]
[NonParallelizable]
public sealed class RetiredNativeInputSelectorCases
{
    [Test]
    [Property("FishAudioCaseId", "infra.runner/retired-native-input/all-original-owner-identities")]
    public void EveryOriginalNativeInputOwnerRejectsWithoutExecutingOrWarmingSta()
    {
        var dispatcher = (Lazy<Task<Dispatcher>>)typeof(StaTestHost).GetField("SharedDispatcher", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var wasCreated = dispatcher.IsValueCreated;
        var events = new List<RegressionActionEvent>();
        using var owner = RegressionActionScope.BeginCase("runner.probe/retired-native-input-metadata", events.Add);
        var current = RegressionCatalog.Create();
        using var reference = JsonDocument.Parse(File.ReadAllBytes(FindRepoFile("tests/Migration/legacy-catalog.json")));
        var original = reference.RootElement.GetProperty("cases").EnumerateArray()
            .Where(item => item.GetProperty("partition").GetString() == "native-input")
            .Select(item => item.GetProperty("id").GetString()!).ToArray();
        Assert.That(original, Has.Length.EqualTo(175));
        Assert.That(current.Any(item => item.Partition == "native-input"), Is.False);
        foreach (var id in original)
        {
            Assert.That(current.Any(item => item.Id == id), Is.False);
            foreach (var selector in new[] { "--case", "--group" })
            {
                var rejected = Assert.Throws<RegressionSelectionException>(() =>
                    RegressionSelection.Parse([selector, id]).ExplicitIds(current));
                Assert.That(rejected!.Message, Is.EqualTo("retired-case"));
            }
            var synthetic = new[] { new RegressionCase(id, "native-input", () => throw new InvalidOperationException("synthetic-descriptor-must-not-run")) };
            Assert.That(RegressionCaseAliases.Resolve(id, synthetic), Is.EqualTo(new[] { id }),
                "A retained explicit current descriptor keeps exact-ID precedence; resolution never runs its callback.");
        }
        foreach (var alias in new[] { "input.settings/run-all", "input.cursor/run" })
        {
            Assert.That(RegressionCaseAliases.Contains(alias), Is.True);
            Assert.That(Assert.Throws<RegressionSelectionException>(() => RegressionCaseAliases.Resolve(alias, current))!.Message,
                Is.EqualTo("empty-case-alias"), "The retired aggregate cannot choose unrelated legacy work.");
        }
        Assert.That(events, Is.Empty);
        Assert.That(dispatcher.IsValueCreated, Is.EqualTo(wasCreated));
    }

    [Test]
    [Property("FishAudioCaseId", "infra.runner/retired-native-input/direct-selectors-reject-before-source-or-child")]
    public async Task EveryDirectRouteAndOriginalFilterRejectsBeforeSourceCaptureAndChildLaunch()
    {
        var source = Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT");
        var stderr = Console.Error;
        using var errors = new StringWriter();
        var launches = 0;
        using var process = new RegressionProcessHost(_ => { launches++; throw new InvalidOperationException("retired-input-selector-launched-child"); });
        try
        {
            Environment.SetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT", Path.Combine(Path.GetTempPath(), "absent-retired-input-context-" + Guid.NewGuid().ToString("N") + ".json"));
            Console.SetError(errors);
            var shapes = DirectArgumentShapes().ToArray();
            foreach (var arguments in shapes)
            {
                Assert.That(LegacyRegressionSelectors.ContainsRetiredSelector(arguments), Is.True);
                Assert.That(LegacyRegressionSelectors.AreArgumentsValid(arguments), Is.False);
                Assert.That(await RegressionProcessHost.TryRunLegacyAsync(arguments, process), Is.EqualTo(2));
            }
            Assert.That(launches, Is.Zero);
            Assert.That(errors.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries),
                Is.EqualTo(shapes.Select(_ => "Retired legacy regression selector; use NUnit case selection.")));
        }
        finally
        {
            Console.SetError(stderr);
            Environment.SetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT", source);
        }
    }

    private static IEnumerable<string[]> DirectArgumentShapes()
    {
        foreach (var selector in new[]
        {
            "--settings-input-tests", "--repository-link-tests", "--control-cursor-tests",
            "--control-cursor-late-combo-tests", "--control-cursor-lifecycle-tests", "--page-entry-tests",
        })
            yield return [selector];
        foreach (var filter in new[] { "tabs", "baseline", "wheel", "tooltip", "precision", "favorites", "copy", "fold-focus", "zero", "hover", "point" })
            yield return ["--settings-input-tests", filter];
        foreach (var filter in new[] { "fields", "search", "favorites" })
            yield return ["--page-entry-tests", filter];
        yield return ["--favorite-order-tests", "--settings-input-tests"];
        yield return ["--case", "input.settings/native/track-point/speed", "--control-cursor-tests"];
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
            if (string.IsNullOrEmpty(parent)) parent = Path.Combine(Path.GetTempPath(), "FishAudioRetiredNativeInputSelectors");
            if (!Path.IsPathFullyQualified(parent)) throw new InvalidOperationException("retired-native-evidence-root-invalid");
            var directory = Path.Combine(parent, "native-input-selector-" + Guid.NewGuid().ToString("N"));
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
                    Cases = [new() { Id = selection[0], Partition = "native-input", Outcome = selection[1] }],
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
            if (!started) throw new IOException("retired-native-input-selector-process-start-failed");
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
                return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException("retired-native-input-selector-drain-deadline");
            }
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(Remaining());
            if (output is not null && error is not null) await Task.WhenAll(output, error).WaitAsync(Remaining());
        }, () => { process?.Dispose(); return Task.CompletedTask; });
    }

    private static IEnumerable<TestCaseData> CliRows()
    {
        foreach (var (name, id) in new[] { ("settings", "input.settings/run-all"), ("cursor", "input.cursor/run") })
        foreach (var selector in new[] { "--case", "--group" })
            yield return Row(name + "-" + selector[2..], ["--list", selector, id], "Runner selection error: empty-case-alias.");
        foreach (var (name, id) in new[]
        {
            ("settings-exact", "input.settings/native/track-point/speed"), ("cursor-exact", "input.cursor/construction"),
            ("repository-exact", "repository.link/run-all"), ("page-exact", "input.page-entry/run-all"),
        })
        foreach (var selector in new[] { "--case", "--group" })
            yield return Row(name + "-" + selector[2..], ["--list", selector, id], "Runner selection error: retired-case.");
        yield return Row("empty-partition", ["--list", "--partition", "native-input"], "Runner selection error: unknown-partition.");
        foreach (var selector in new[]
        {
            "--settings-input-tests", "--repository-link-tests", "--control-cursor-tests",
            "--control-cursor-late-combo-tests", "--control-cursor-lifecycle-tests", "--page-entry-tests",
        })
            yield return Row("direct-" + selector[2..^6], [selector], "Retired legacy regression selector; use NUnit case selection.");
        foreach (var outcome in new[] { "failed", "not-run", "passed" })
            yield return Row("replay-aggregate-" + outcome, ["input.settings/run-all", outcome], "Runner selection error: empty-case-alias.");
        yield return Row("replay-aggregate-drift-passed", ["input.cursor/run", "passed"], "Runner selection error: empty-case-alias.");
        yield return Row("replay-exact-drift-passed", ["input.page-entry/run-all", "passed"], "Runner selection error: retired-case.");
    }

    private static TestCaseData Row(string key, string[] selection, string expected) =>
        new TestCaseData(key, selection, expected).SetName("RetiredNativeInput_" + key)
            .SetProperty("FishAudioCaseId", "infra.runner/retired-native-input/" + key);
}
