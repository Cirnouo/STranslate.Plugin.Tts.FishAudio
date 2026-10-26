using System.Text.Json;
using System.Diagnostics;

internal static class RunnerContractSuite
{
    internal static async Task RunAsync()
    {
        await SelectionUnionsInCatalogOrderAndConstrainsPartitionsAsync();
        await InvalidAndEmptySelectionsCannotExecuteAsync();
        await ListingNeverExecutesAsync();
        await FailureIsDurableAndReplayDoesNotRewriteItAsync();
        await IncrementalResultsPrecedeDelegatesAsync();
        ImpactSelectionKeepsSharedAndUnknownDependenciesConservative();
        LeafImpactRulesAvoidUnrelatedLayoutAndExplainSharedExpansion();
        RecoveryMapsCleanRepairsAndRetainsRevertedDirtyScope();
        ProviderAndPreviewLeafRulesRetainDirectConsumers();
        await SourceIdentityAndAffectedScopeIgnoreForeignWorkingDirectoryAsync();
    }

    private static async Task SelectionUnionsInCatalogOrderAndConstrainsPartitionsAsync()
    {
        using var fixture = new RunnerFixture();
        var calls = new List<string>();
        var cases = Registry(calls);
        var exit = await fixture.RunAsync(["--group", "settings", "--case", "cards.layout/basic", "--group", "settings.storage", "--partition", "core"], cases);
        Require(exit == 0, "A valid union succeeds.");
        Require(calls.SequenceEqual(["settings.storage/read", "settings.storage/write"]), "Union selection is deduplicated, retains catalog order and applies partition constraints.");
        using var report = JsonDocument.Parse(File.ReadAllBytes(fixture.ReportPath));
        Require(report.RootElement.GetProperty("complete").GetBoolean(), "A completed selection is durable.");
        Require(report.RootElement.GetProperty("cases").GetArrayLength() == 2, "Only selected cases enter the report.");
        calls.Clear();
        Require(await fixture.RunAsync([], cases) == 0 && calls.Count == 3, "No arguments retain the complete default run.");
    }

    private static async Task InvalidAndEmptySelectionsCannotExecuteAsync()
    {
        foreach (var args in new string[][]
        {
            ["--case", "missing/case"], ["--group", "settings.stor"], ["--partition", "missing"],
            ["--group", "settings", "--partition", "native-cards"], ["--case"], ["--unknown"],
            ["--all", "--case", "settings.storage/read"], ["--base", "main"]
        })
        {
            using var fixture = new RunnerFixture();
            var calls = new List<string>();
            Require(await fixture.RunAsync(args, Registry(calls)) != 0, "Unknown, malformed and empty selections fail.");
            Require(calls.Count == 0, "An invalid selection cannot execute a partial match.");
        }
    }

    private static async Task ListingNeverExecutesAsync()
    {
        using var fixture = new RunnerFixture();
        var calls = new List<string>();
        Require(await fixture.RunAsync(["--list", "--group", "settings"], Registry(calls)) == 0, "Listing a valid selection succeeds.");
        Require(calls.Count == 0, "Metadata listing never invokes a registered delegate.");
        Require(fixture.Output.Contains("settings.storage/read", StringComparison.Ordinal), "Listing exposes stable IDs.");
        Require(!File.Exists(fixture.ReportPath), "Listing does not create a misleading execution report.");
    }

    private static async Task FailureIsDurableAndReplayDoesNotRewriteItAsync()
    {
        using var fixture = new RunnerFixture();
        var calls = new List<string>();
        var original = new RunnerOwnedException("private-user-text-and-key");
        var failure = new[]
        {
            new RegressionCase("runner.sample/pass", "core", () => { calls.Add("pass"); return Task.CompletedTask; }),
            new RegressionCase("runner.sample/fail", "core", () => { calls.Add("fail"); return Task.FromException(original); }),
            new RegressionCase("runner.sample/not-run", "core", () => { calls.Add("not-run"); return Task.CompletedTask; })
        };
        Require(await fixture.RunAsync([], failure) == 1, "A case failure fails the command.");
        Require(calls.SequenceEqual(["pass", "fail"]), "Sequential failure stops before the remaining case.");
        var originalBytes = File.ReadAllBytes(fixture.ReportPath);
        using (var report = JsonDocument.Parse(originalBytes))
        {
            var root = report.RootElement;
            Require(root.GetProperty("schemaVersion").GetInt32() == 1, "Reports carry a version.");
            Require(!root.GetProperty("complete").GetBoolean(), "A failed selection remains incomplete.");
            var outcomes = root.GetProperty("cases").EnumerateArray().ToArray();
            Require(outcomes.Select(item => item.GetProperty("outcome").GetString()).SequenceEqual(["passed", "failed", "not-run"]), "Reports enumerate passed, failed and not-run cases.");
            Require(outcomes[1].GetProperty("exceptionType").GetString() == typeof(RunnerOwnedException).FullName, "The original exception type remains distinguishable.");
            Require(root.GetProperty("source").GetProperty("fingerprint").GetString()?.Length == 64, "A source fingerprint accompanies raw outcomes.");
            Require(root.GetProperty("configuration").GetString() is "Debug" or "Release", "The build configuration accompanies outcomes.");
            Require(root.GetProperty("sdk").GetString()?.Length > 0, "The SDK identity accompanies outcomes.");
        }
        var json = System.Text.Encoding.UTF8.GetString(originalBytes);
        Require(!json.Contains("private-user-text-and-key", StringComparison.Ordinal) && !json.Contains(fixture.DirectoryPath, StringComparison.OrdinalIgnoreCase), "Reports omit exception messages and machine paths.");
        var replayPath = Path.Combine(fixture.DirectoryPath, "replay.json");
        calls.Clear();
        var replay = failure.Select(item => item with { Run = () => { calls.Add(item.Id); return Task.CompletedTask; } }).ToArray();
        Require(await fixture.RunAsync(["--rerun-failed", fixture.ReportPath, "--results", replayPath], replay) == 0, "A failed selection can be replayed to a separate report.");
        Require(calls.SequenceEqual(["runner.sample/fail", "runner.sample/not-run"]), "Replay includes failed and not-run work without replaying unchanged successes.");
        Require(File.ReadAllBytes(fixture.ReportPath).SequenceEqual(originalBytes), "Passing recovery cannot rewrite the original failed report.");
        calls.Clear();
        Require(await fixture.RunAsync(["--rerun-failed", fixture.ReportPath, "--results", fixture.ReportPath], replay) != 0 && calls.Count == 0, "Replay refuses to overwrite its input evidence.");
        Require(await fixture.RunAsync(["--results", replayPath], replay) != 0 && calls.Count == 0, "A new invocation cannot overwrite previous result evidence.");
        var differentSource = System.Text.Json.Nodes.JsonNode.Parse(originalBytes)!;
        differentSource["source"]!["fingerprint"] = new string('0', 64);
        var changedPath = Path.Combine(fixture.DirectoryPath, "changed-source.json");
        File.WriteAllText(changedPath, differentSource.ToJsonString());
        Require(await fixture.RunAsync(["--rerun-failed", changedPath, "--results", Path.Combine(fixture.DirectoryPath, "changed-replay.json")], replay) == 0, "Changed-source recovery succeeds with conservative coverage.");
        Require(calls.Count == 3, "A source mismatch cannot silently reuse a prior success.");
    }

    private static async Task IncrementalResultsPrecedeDelegatesAsync()
    {
        using var fixture = new RunnerFixture();
        var remainingCalled = false;
        RegressionCase[] registry =
        [
            new("runner.incremental/first", "core", () =>
            {
                using var report = JsonDocument.Parse(File.ReadAllBytes(fixture.ReportPath));
                var outcomes = report.RootElement.GetProperty("cases").EnumerateArray().Select(item => item.GetProperty("outcome").GetString()).ToArray();
                Require(outcomes.SequenceEqual(["running", "not-run"]), "The current action and outstanding coverage are flushed before execution.");
                return Task.FromException(new TimeoutException("private-deadline-details"));
            }),
            new("runner.incremental/remaining", "core", () => { remainingCalled = true; return Task.CompletedTask; })
        ];
        Require(await fixture.RunAsync([], registry) == 1 && !remainingCalled, "A timeout retains fail-fast behavior.");
        using var final = JsonDocument.Parse(File.ReadAllBytes(fixture.ReportPath));
        Require(final.RootElement.GetProperty("cases")[0].GetProperty("outcome").GetString() == "timed-out", "Timeouts retain their distinct raw outcome.");
        var recovered = new List<string>();
        var replay = registry.Select(item => item with { Run = () => { recovered.Add(item.Id); return Task.CompletedTask; } }).ToArray();
        Require(await fixture.RunAsync(["--rerun-failed", fixture.ReportPath, "--results", Path.Combine(fixture.DirectoryPath, "timeout-replay.json")], replay) == 0 && recovered.Count == 2, "Timeout recovery includes the original timed-out and not-run IDs.");
    }

    private static void ImpactSelectionKeepsSharedAndUnknownDependenciesConservative()
    {
        var registry = new[]
        {
            new RegressionCase("settings.storage/read", "core", () => Task.CompletedTask),
            new RegressionCase("cards.layout/basic", "native-cards", () => Task.CompletedTask),
            new RegressionCase("repository.contract/readme", "core", () => Task.CompletedTask)
        };
        foreach (var path in new[] { "new-unclassified/component.cs", "tests/STranslate.Plugin.Tts.FishAudio.Tests/TestInfrastructure/StaTestHost.cs", "STranslate.Plugin.Tts.FishAudio/Configuration/Settings.cs" })
        {
            var impact = RegressionImpact.Select(registry, [path]);
            Require(impact.Ids.Count == 3 && impact.ConservativeFallback, "Unknown and shared dependencies cannot narrow coverage.");
        }
        var docs = RegressionImpact.Select(registry, ["docs/specs/build-and-release.md"]);
        Require(docs.Ids.SetEquals(["repository.contract/readme"]) && !docs.ConservativeFallback, "Documentation maps to repository contracts without invoking native delegates.");
        Require(RegressionImpact.Select(registry, []).Ids.Count == 0, "An unchanged source set selects no affected cases.");
        Require(RegressionImpact.Select(registry, null).Ids.Count == 3, "Unavailable Git/base evidence conservatively selects all cases.");
        var staleMap = RegressionImpact.Select(registry, ["docs/specs/build-and-release.md", "STranslate.Plugin.Tts.FishAudio/Presentation/PreviewPlaybackController.cs"]);
        Require(staleMap.ConservativeFallback && staleMap.Ids.Count == 3, "A matched documentation path cannot hide an unmatched later rule category.");
    }

    private static void LeafImpactRulesAvoidUnrelatedLayoutAndExplainSharedExpansion()
    {
        var ids = new[] { "preview.ownership/failure", "preview.search-retention/selection", "preview.accepted-continuity/owner", "cards.avatar/ring", "cards.layout/header", "cards.description/text", "popup.direction/placement", "input.cursor/hand", "numeric.bottom/value" };
        var registry = ids.Select(id => new RegressionCase(id, "core", () => Task.CompletedTask)).ToArray();
        var preview = RegressionImpact.Select(registry, ["STranslate.Plugin.Tts.FishAudio/Presentation/PreviewPlaybackController.cs"]);
        Require(preview.Ids.SetEquals(["preview.ownership/failure", "preview.search-retention/selection", "preview.accepted-continuity/owner", "cards.avatar/ring"]), "A preview ownership leaf includes retained/accepted avatar compatibility without unrelated card layout.");
        Require(preview.RuleIds.SequenceEqual(["preview-ownership"]) && preview.RecommendedGroups.Contains("cards.avatar", StringComparer.Ordinal), "A leaf recommendation carries a fixed safe rule and logical groups.");
        var popup = RegressionImpact.Select(registry, ["STranslate.Plugin.Tts.FishAudio/Presentation/View/PopupViewport.cs"]);
        Require(popup.Ids.SetEquals(["popup.direction/placement", "input.cursor/hand"]), "A shared popup helper expands to cursor compatibility.");
        var header = RegressionImpact.Select(registry, ["STranslate.Plugin.Tts.FishAudio/Presentation/View/VoiceHeaderPanel.cs"]);
        Require(header.Ids.SetEquals(["cards.layout/header", "cards.description/text"]), "Header layout includes description compatibility.");
        var shared = RegressionImpact.Select(registry, ["STranslate.Plugin.Tts.FishAudio/Presentation/View/SettingsView.xaml"]);
        Require(shared.Ids.Count == ids.Length && shared.RuleIds.SequenceEqual(["shared-input"]), "The shared compiled settings root conservatively expands to all partitions.");
    }

    private static void RecoveryMapsCleanRepairsAndRetainsRevertedDirtyScope()
    {
        var registry = new[]
        {
            new RegressionCase("preview.ownership/failure", "core", () => Task.CompletedTask),
            new RegressionCase("cards.avatar/ring", "native-cards", () => Task.CompletedTask),
            new RegressionCase("cards.layout/header", "native-cards", () => Task.CompletedTask)
        };
        var clean = new RegressionSourceIdentity { Available = true, Commit = new string('a', 40) };
        var repair = RegressionImpact.Recover(registry, clean, ["STranslate.Plugin.Tts.FishAudio/Presentation/PreviewPlaybackController.cs"]);
        Require(repair.Ids.SetEquals(["preview.ownership/failure", "cards.avatar/ring"]) && !repair.ConservativeFallback, "A clean-baseline preview repair adds its ownership/compatibility groups without unrelated layout.");
        var dirty = new RegressionSourceIdentity
        {
            Available = true, Commit = new string('a', 40), Dirty = true, DirtyScopeRecorded = true,
            DirtyAffectedIds = ["preview.ownership/failure", "cards.avatar/ring"]
        };
        var reverted = RegressionImpact.Recover(registry, dirty, []);
        Require(reverted.Ids.SetEquals(["preview.ownership/failure", "cards.avatar/ring"]), "Reverted dirty preview edits retain the previous affected scope even when the current tree matches its commit.");
        dirty.DirtyScopeRecorded = false;
        Require(RegressionImpact.Recover(registry, dirty, []).ConservativeFallback, "Old dirty reports without scope metadata fall back to all coverage.");
    }

    private static void ProviderAndPreviewLeafRulesRetainDirectConsumers()
    {
        // These are the actual catalog owners of direct PreviewRefreshCoordinator/GetCredit/PostTts consumers.
        var ids = new[]
        {
            "preview.ownership/display-preview-refreshes-cached-voice-and-plays-latest-url",
            "voice.language/run-all",
            "settings.credit/manual-credit-refresh-success-shows-balance-and-latency",
            "numeric.top-p/raw-settings-cannot-send-zero-through-http-or-native-payloads",
            "cards.layout/header"
        };
        var registry = ids.Select(id => new RegressionCase(id, "core", () => Task.CompletedTask)).ToArray();
        var preview = RegressionImpact.Select(registry, ["STranslate.Plugin.Tts.FishAudio/Presentation/PreviewRefreshCoordinator.cs"]);
        Require(preview.Ids.Contains("voice.language/run-all"), "A preview coordinator change retains its direct language-publication consumer.");
        Require(!preview.Ids.Contains("cards.layout/header"), "Preview ownership does not expand into unrelated layout.");
        var provider = RegressionImpact.Select(registry, ["STranslate.Plugin.Tts.FishAudio/FishAudio/FishAudioApi.cs"]);
        Require(provider.Ids.Contains("settings.credit/manual-credit-refresh-success-shows-balance-and-latency"), "Provider changes include direct GetCredit workflow compatibility.");
        Require(provider.Ids.Contains("numeric.top-p/raw-settings-cannot-send-zero-through-http-or-native-payloads"), "Provider changes include direct PostTts numeric compatibility.");
    }

    private static async Task SourceIdentityAndAffectedScopeIgnoreForeignWorkingDirectoryAsync()
    {
        using var fixture = new RunnerFixture();
        Directory.CreateDirectory(fixture.DirectoryPath);
        await RunGitAsync(fixture.DirectoryPath, "init", "--quiet");
        var readme = Path.Combine(fixture.DirectoryPath, "README.md");
        await File.WriteAllTextAsync(readme, "# Foreign fixture\n");
        await RunGitAsync(fixture.DirectoryPath, "add", "README.md");
        await RunGitAsync(fixture.DirectoryPath, "-c", "user.name=Runner fixture", "-c", "user.email=runner@example.invalid", "commit", "--quiet", "-m", "fixture");
        await File.AppendAllTextAsync(readme, "Documentation-only foreign change.\n");
        var expected = await RegressionSource.CaptureAsync();
        Require(expected.Identity.Available, "The runner fixture is bound to its source checkout.");
        var registry = Registry([]);
        var expectedScope = RegressionImpact.Select(registry, await expected.ChangedPathsAsync("HEAD"));
        var originalDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = fixture.DirectoryPath;
            var actual = await RegressionSource.CaptureAsync();
            Require(string.Equals(expected.Root, actual.Root, StringComparison.OrdinalIgnoreCase) && expected.Identity.Commit == actual.Identity.Commit,
                "A foreign checkout as current directory cannot replace the executable's source provenance.");
            var actualScope = RegressionImpact.Select(registry, await actual.ChangedPathsAsync("HEAD"));
            Require(actualScope.Ids.SetEquals(expectedScope.Ids) && actualScope.ConservativeFallback == expectedScope.ConservativeFallback,
                "Affected selection follows the executable's checkout rather than foreign documentation changes.");
        }
        finally { Environment.CurrentDirectory = originalDirectory; }
    }

    private static async Task RunGitAsync(string directory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        Require(process.Start(), "A local Git fixture starts.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(stdout, stderr);
        Require(process.ExitCode == 0, "A local Git fixture operation succeeds.");
    }

    private static RegressionCase[] Registry(List<string> calls) =>
    [
        new("settings.storage/read", "core", () => { calls.Add("settings.storage/read"); return Task.CompletedTask; }),
        new("cards.layout/basic", "native-cards", () => { calls.Add("cards.layout/basic"); return Task.CompletedTask; }),
        new("settings.storage/write", "core", () => { calls.Add("settings.storage/write"); return Task.CompletedTask; })
    ];

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RunnerOwnedException(string message) : IOException(message);

    private sealed class RunnerFixture : IDisposable
    {
        internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "fishaudio-runner-" + Guid.NewGuid().ToString("N"));
        internal string ReportPath { get; private set; }
        internal string Output { get; private set; } = "";

        internal RunnerFixture() => ReportPath = Path.Combine(DirectoryPath, "result.json");

        internal async Task<int> RunAsync(string[] args, IReadOnlyList<RegressionCase> cases)
        {
            var output = Console.Out;
            var error = Console.Error;
            using var capture = new StringWriter();
            Console.SetOut(capture);
            Console.SetError(capture);
            try
            {
                if (!args.Contains("--results", StringComparer.Ordinal) && File.Exists(ReportPath)) ReportPath = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".json");
                var actual = args.Contains("--results", StringComparer.Ordinal) ? args : [.. args, "--results", ReportPath];
                return await RegressionRunner.RunAsync(actual, cases);
            }
            finally
            {
                Console.SetOut(output);
                Console.SetError(error);
                Output = capture.ToString();
            }
        }

        public void Dispose()
        {
            if (!System.IO.Directory.Exists(DirectoryPath)) return;
            // Git marks its object files read-only on Windows; these are owned fixture files.
            foreach (var file in System.IO.Directory.EnumerateFiles(DirectoryPath, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            System.IO.Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
