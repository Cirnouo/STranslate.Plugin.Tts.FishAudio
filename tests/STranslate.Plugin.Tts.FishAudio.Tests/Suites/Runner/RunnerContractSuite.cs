using System.Diagnostics;
using System.Runtime.ExceptionServices;

internal static class RunnerContractSuite
{
    internal static async Task RunAsync()
    {
        await RunStageAsync("source-checkout-process-budget", GitFixtureWaitsForTheSameProcessBeyondFiveSecondsAsync);
        await RunStageAsync("source-checkout-cleanup", SourceCheckoutOwnerPreservesFailuresAndOneRootAsync);
        await RunStageAsync("native-owner-aliases", NativeOwnerAliasesPreserveBoundariesAndRecoveryAsync);
        await RunStageAsync("streaming-owner-aliases", StreamingOwnerAliasesPreserveSelectionAndReplayAsync);
        await RunStageAsync("safe-final-failures", FinalFailureSummaryIncludesEveryFailedOwnerAsync);
        await RunStageAsync("durable-report-generations", RegressionReportSuite.RunAsync);
        await RunStageAsync("selection-union", SelectionUnionsInCatalogOrderAndConstrainsPartitionsAsync);
        await RunStageAsync("invalid-selection", InvalidAndEmptySelectionsCannotExecuteAsync);
        await RunStageAsync("metadata-list", ListingNeverExecutesAsync);
        RunStage("clean-source-identity", CleanSourceIdentityConsistencyDoesNotDependOnCallerState);
        await RunStageAsync("durable-replay", FailureIsDurableAndReplayDoesNotRewriteItAsync);
        await RunStageAsync("incremental-results", IncrementalResultsPrecedeDelegatesAsync);
        RunStage("shared-impact", ImpactSelectionKeepsSharedAndUnknownDependenciesConservative);
        RunStage("leaf-impact", LeafImpactRulesAvoidUnrelatedLayoutAndExplainSharedExpansion);
        RunStage("scoped-recovery", RecoveryMapsCleanRepairsAndRetainsRevertedDirtyScope);
        RunStage("direct-consumers", ProviderAndPreviewLeafRulesRetainDirectConsumers);
        await RunStageAsync("source-checkout-owner", SourceIdentityAndAffectedScopeIgnoreForeignWorkingDirectoryAsync);
    }

    private static async Task RunStageAsync(string stage, Func<Task> run)
    {
        try { await run(); }
        catch { ReportFailedStage(stage); throw; }
    }

    private static void RunStage(string stage, Action run)
    {
        try { run(); }
        catch { ReportFailedStage(stage); throw; }
    }

    private static void ReportFailedStage(string stage)
    {
        try { Console.Error.WriteLine($"Runner contract stage failed: {stage}."); }
        catch { /* Failure-only safe metadata cannot replace the original exception. */ }
    }

    private static async Task SelectionUnionsInCatalogOrderAndConstrainsPartitionsAsync()
    {
        using var fixture = new RunnerFixture();
        var calls = new List<string>();
        var cases = Registry(calls);
        var exit = await fixture.RunAsync(["--group", "settings", "--case", "cards.layout/basic", "--group", "settings.storage", "--partition", "core"], cases);
        Require(exit == 0, "A valid union succeeds.");
        Require(calls.SequenceEqual(["settings.storage/read", "settings.storage/write"]), "Union selection is deduplicated, retains catalog order and applies partition constraints.");
        var report = RegressionReport.Read(fixture.ReportPath);
        Require(report.Complete, "A completed selection is durable.");
        Require(report.Cases.Count == 2, "Only selected cases enter the report.");
        File.Delete(fixture.ReportPath);
        calls.Clear();
        Require(await fixture.RunAsync([], cases) == 0 && calls.Count == 3, "No arguments retain the complete default run.");
    }

    private static async Task NativeOwnerAliasesPreserveBoundariesAndRecoveryAsync()
    {
        using var fixture = new RunnerFixture();
        var calls = new List<string>();
        RegressionCase[] cases =
        [
            Fake("cards.like/shared-card-template-is-compiled", "native-cards"),
            Fake("cards.like/presentation/first", "native-cards"),
            Fake("popup.search-adjustment/matrix-diagnostics-preserve-failures-without-retry", "core"),
            Fake("cards.like/presentation/second", "native-cards"),
            Fake("popup.search-adjustment/native/first", "native-popups"),
            Fake("popup.search-adjustment/native/second", "native-popups"),
            Fake("preview.accepted-continuity/owner", "native-cards")
        ];
        Require(await fixture.RunAsync(["--case", "cards.like/run-all", "--case", cases[3].Id,
            "--group", "popup.search-adjustment/run-all"], cases) == 0,
            "Former native aggregates remain exact case/group aliases.");
        Require(calls.SequenceEqual(cases.Where((_, index) => index is 1 or 3 or 4 or 5).Select(item => item.Id)),
            "Native alias union preserves catalog order and excludes the earlier template and core diagnostic siblings.");
        foreach (var args in new string[][]
        {
            ["--case", "cards.like/run-all", "--partition", "core"],
            ["--group", "popup.search-adjustment/run-all.extra"],
            ["--case", "cards.like/removed"]
        })
        {
            calls.Clear();
            Require(await fixture.RunAsync(args, cases) != 0 && calls.Count == 0,
                "Empty partition constraints and unknown native aliases fail before executing delegates.");
        }
        calls.Clear();
        Require(await fixture.RunAsync(["--case", "cards.like/run-all"], [cases[0]]) != 0 && calls.Count == 0,
            "A known native alias with no matching owned leaves fails.");

        var source = await RegressionSource.CaptureAsync();
        source.RecordDirtyScope(cases);
        var previous = new RegressionReport
        {
            Source = source.Identity,
            Cases =
            [
                new() { Id = "cards.like/run-all", Partition = "native-cards", Outcome = "passed" },
                new() { Id = "popup.search-adjustment/run-all", Partition = "native-popups", Outcome = "failed", ExceptionType = typeof(IOException).FullName },
                new() { Id = "preview.accepted-continuity/run-all", Partition = "native-cards", Outcome = "not-run" }
            ]
        };
        var input = Path.Combine(fixture.DirectoryPath, "native-previous.json");
        previous.Create(input);
        previous.Write(input);
        var originalFiles = new[] { input }.Concat(Directory.EnumerateFiles(RegressionReport.GenerationsRoot(input), "*.json", SearchOption.AllDirectories))
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
        var replayPath = Path.Combine(fixture.DirectoryPath, "native-replay.json");
        calls.Clear();
        Require(await fixture.RunAsync(["--rerun-failed", input, "--results", replayPath], cases) == 0,
            "Old failed and unexecuted native aggregates can recover into current owner leaves.");
        Require(calls.SequenceEqual(cases.Skip(4).Select(item => item.Id)),
            "Recovery expands only failed/not-run aliases and does not infer leaf passes from the prior passed aggregate.");
        var replay = RegressionReport.Read(replayPath);
        Require(replay.PreviousRunId == previous.RunId && replay.Cases.All(item => item.Outcome == "passed") && replay.Cases.Count == 3,
            "The new report owns exactly the current recovered scope and retains the prior run identity.");
        Require(originalFiles.All(item => File.ReadAllBytes(item.Key).SequenceEqual(item.Value)),
            "Native recovery leaves the original failed report and every immutable generation unchanged.");

        RegressionCase Fake(string id, string partition) => new(id, partition, () => { calls.Add(id); return Task.CompletedTask; });
    }

    private static async Task StreamingOwnerAliasesPreserveSelectionAndReplayAsync()
    {
        using var fixture = new RunnerFixture();
        var telemetry = new List<RegressionActionEvent>();
        using (RegressionActionScope.BeginCase("runner.probe/metadata", telemetry.Add))
        {
            var registry = RegressionCatalog.Create();
            var selected = RegressionSelection.Parse(["--list", "--case", "playback.streaming/run-all"]).ExplicitIds(registry);
            Require(selected.Count == 22 && !registry.Any(item => item.Id == "playback.streaming/run-all"), "Default registration contains exactly the independent streaming leaves, with the old aggregate reserved as an alias.");
            Require(telemetry.Count == 0, "Creating and selecting streaming descriptors never runs action telemetry or fixtures.");
        }
        var calls = new List<string>();
        RegressionCase[] cases =
        [
            new("playback.streaming/first", "core", () => { calls.Add("first"); return Task.CompletedTask; }),
            new("playback.streaming/second", "core", () => { calls.Add("second"); return Task.CompletedTask; }),
            new("playback.streaming.extra/not-owned", "native-settings", () => { calls.Add("sibling"); return Task.CompletedTask; }),
            new("runner.sample/other", "native-settings", () => { calls.Add("other"); return Task.CompletedTask; })
        ];
        Require(await fixture.RunAsync(["--case", "playback.streaming/run-all", "--case", cases[1].Id, "--group", "playback.streaming/run-all", "--partition", "core"], cases) == 0,
            "The former aggregate remains an exact case/group alias.");
        Require(calls.SequenceEqual(["first", "second"]), "Aliases union in catalog order without duplicate execution.");
        foreach (var arguments in new string[][]
        {
            ["--case", "playback.streaming/run"], ["--group", "playback.streaming/run-all.extra"],
            ["--case", "playback.streaming/run-all", "--partition", "native-settings"]
        })
        {
            calls.Clear();
            Require(await fixture.RunAsync(arguments, cases) != 0 && calls.Count == 0, "Unknown or empty aliases cannot execute a partial match.");
        }
        calls.Clear();
        Require(await fixture.RunAsync(["--case", "playback.streaming/run-all"], [cases[2]]) != 0 && calls.Count == 0,
            "A known alias with no registered leaves fails.");
        foreach (var outcome in new[] { "failed", "not-run", "passed" })
        {
            var source = await RegressionSource.CaptureAsync();
            source.RecordDirtyScope(cases);
            var previous = new RegressionReport
            {
                Source = source.Identity,
                Cases = [new() { Id = "playback.streaming/run-all", Partition = "core", Outcome = outcome,
                    ExceptionType = outcome == "failed" ? typeof(IOException).FullName : null }]
            };
            var input = Path.Combine(fixture.DirectoryPath, "old-aggregate-" + outcome + ".json");
            previous.Create(input);
            var originalBytes = File.ReadAllBytes(input);
            calls.Clear();
            var replay = Path.Combine(fixture.DirectoryPath, "replay-" + outcome + ".json");
            var exit = await fixture.RunAsync(["--rerun-failed", input, "--results", replay], cases);
            Require(outcome == "passed" ? exit != 0 && calls.Count == 0 : exit == 0 && calls.SequenceEqual(["first", "second"]),
                "Incomplete old aggregates expand to leaves; old passed aggregates cannot synthesize leaf coverage.");
            Require(File.ReadAllBytes(input).SequenceEqual(originalBytes), "Alias replay preserves the original report bytes.");
        }
        calls.Clear();
        RegressionCase[] exactRegistry =
        [
            new("playback.streaming/run-all", "core", () => { calls.Add("exact"); return Task.CompletedTask; }),
            cases[0]
        ];
        Require(await fixture.RunAsync(["--case", "playback.streaming/run-all", "--group", "playback.streaming/run-all"], exactRegistry) == 0 && calls.SequenceEqual(["exact"]),
            "An explicitly registered current case takes precedence over its former aggregate alias for case/group selection.");
        var exactSource = await RegressionSource.CaptureAsync();
        exactSource.RecordDirtyScope(exactRegistry);
        var exactPrevious = new RegressionReport
        {
            Source = exactSource.Identity,
            Cases = [new() { Id = "playback.streaming/run-all", Partition = "core", Outcome = "failed", ExceptionType = typeof(IOException).FullName }]
        };
        var exactInput = Path.Combine(fixture.DirectoryPath, "current-exact.json");
        exactPrevious.Create(exactInput);
        calls.Clear();
        Require(await fixture.RunAsync(["--rerun-failed", exactInput, "--results", Path.Combine(fixture.DirectoryPath, "current-exact-replay.json")], exactRegistry) == 0 && calls.SequenceEqual(["exact"]),
            "Failed-report replay also preserves an explicitly registered current exact case instead of expanding its alias.");
    }

    private static async Task FinalFailureSummaryIncludesEveryFailedOwnerAsync()
    {
        using var fixture = new RunnerFixture();
        var calls = new List<string>();
        var cases = Registry(calls);
        var output = Console.Out;
        var error = Console.Error;
        using var capture = new StringWriter();
        try
        {
            Console.SetOut(capture);
            Console.SetError(capture);
            var exit = await RegressionRunner.RunAsync(["--results", fixture.ReportPath], cases, (_, report, _) =>
            {
                report.Cases[0].Outcome = "timed-out";
                report.Cases[0].ExceptionType = typeof(RegressionExecutionTimeoutException).FullName;
                report.Cases[0].FailureStage = "action";
                report.Cases[0].ActionOrdinal = 1;
                report.Cases[1].Outcome = "failed";
                report.Cases[1].ExceptionType = typeof(IOException).FullName;
                report.Cases[1].FailureStage = "case";
                return Task.CompletedTask;
            });
            Require(exit == 1 && calls.Count == 0, "A final child outcome fails without invoking parent delegates.");
            var text = capture.ToString();
            Require(text.Contains($"FAIL {cases[0].Id}: {typeof(RegressionExecutionTimeoutException).FullName}; stage=action; action=1.", StringComparison.Ordinal), "Hard failures remain visible without a child completion line.");
            Require(text.Contains($"FAIL {cases[1].Id}: {typeof(IOException).FullName}; stage=case; action=0.", StringComparison.Ordinal), "Every failed child owner appears in the final safe summary.");
            Require(text.Contains("1 failed; 1 timed out; 1 not run", StringComparison.Ordinal), "Final counts account explicitly for all non-passing owners.");
            Require(!text.Contains(fixture.DirectoryPath, StringComparison.OrdinalIgnoreCase), "Final diagnostics contain no private paths.");
        }
        finally { Console.SetOut(output); Console.SetError(error); }
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
        var report = RegressionReport.Read(fixture.ReportPath);
        var originalGenerationPath = RegressionReport.GenerationPath(fixture.ReportPath, report.RunId, report.Generation);
        var originalBytes = File.ReadAllBytes(originalGenerationPath);
        var originalProjection = File.Exists(fixture.ReportPath) ? File.ReadAllBytes(fixture.ReportPath) : null;
        Require(report.SchemaVersion == 1, "Reports carry a version.");
        Require(!report.Complete, "A failed selection remains incomplete.");
        Require(report.Cases.Select(item => item.Outcome).SequenceEqual(["passed", "failed", "not-run"]), "Reports enumerate passed, failed and not-run cases.");
        Require(report.Cases[1].ExceptionType == typeof(RunnerOwnedException).FullName, "The original exception type remains distinguishable.");
        Require(report.Source.Fingerprint.Length == 64, "A source fingerprint accompanies raw outcomes.");
        Require(report.Configuration is "Debug" or "Release", "The build configuration accompanies outcomes.");
        Require(report.Sdk.Length > 0, "The SDK identity accompanies outcomes.");
        var json = System.Text.Encoding.UTF8.GetString(originalBytes);
        Require(!json.Contains("private-user-text-and-key", StringComparison.Ordinal) && !json.Contains(fixture.DirectoryPath, StringComparison.OrdinalIgnoreCase), "Reports omit exception messages and machine paths.");
        var replayPath = Path.Combine(fixture.DirectoryPath, "replay.json");
        calls.Clear();
        var replay = failure.Select(item => item with { Run = () => { calls.Add(item.Id); return Task.CompletedTask; } }).ToArray();
        Require(await fixture.RunAsync(["--rerun-failed", fixture.ReportPath, "--results", replayPath], replay) == 0, "A failed selection can be replayed to a separate report.");
        Require(calls.SequenceEqual(report.Source.Available ? ["runner.sample/fail", "runner.sample/not-run"] : ["runner.sample/pass", "runner.sample/fail", "runner.sample/not-run"]), "Replay includes failed and not-run work and expands conservatively when projection identity is unavailable.");
        Require(File.ReadAllBytes(originalGenerationPath).SequenceEqual(originalBytes), "Passing recovery cannot rewrite the original failed generation.");
        Require(originalProjection is null ? !File.Exists(fixture.ReportPath) : File.ReadAllBytes(fixture.ReportPath).SequenceEqual(originalProjection), "Passing recovery cannot rewrite or recreate the original projection.");
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
                File.Delete(fixture.ReportPath);
                var report = RegressionReport.Read(fixture.ReportPath);
                var outcomes = report.Cases.Select(item => item.Outcome).ToArray();
                Require(outcomes.SequenceEqual(["running", "not-run"]), "The current action and outstanding coverage are flushed before execution.");
                Require(!report.Source.Available, "A missing projection retains durable outcomes while requiring conservative source recovery.");
                return Task.FromException(new TimeoutException("private-deadline-details"));
            }),
            new("runner.incremental/remaining", "core", () => { remainingCalled = true; return Task.CompletedTask; })
        ];
        Require(await fixture.RunAsync([], registry) == 1 && !remainingCalled, "A timeout retains fail-fast behavior.");
        var final = RegressionReport.Read(fixture.ReportPath);
        Require(final.Cases[0].Outcome == "timed-out", "Timeouts retain their distinct raw outcome.");
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
        var clean = CleanSourceFixture();
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

    private static void CleanSourceIdentityConsistencyDoesNotDependOnCallerState()
    {
        var registry = Registry([]);
        var consistent = CleanSourceFixture();
        var unchanged = RegressionImpact.Recover(registry, consistent, []);
        Require(unchanged.Ids.Count == 0 && !unchanged.ConservativeFallback, "A consistent clean identity can retain unchanged successful coverage.");
        var tampered = CleanSourceFixture();
        tampered.Fingerprint = new string('0', 64);
        var inconsistent = RegressionImpact.Recover(registry, tampered, []);
        Require(inconsistent.ConservativeFallback && inconsistent.Ids.Count == registry.Length,
            "An inconsistent clean commit/tree/fingerprint cannot retain prior successes even with an empty changed-path set.");
    }

    private static RegressionSourceIdentity CleanSourceFixture() => new()
    {
        Available = true, Commit = new string('a', 40), Tree = new string('b', 40),
        // Independently captured SHA-256 of the two canonical Git hash lines, with no dirty bytes.
        Fingerprint = "63bdfe090637cf9ff5d40017247b9958fa898fc5c95978a0d28594eb745d27a5"
    };

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

    private static async Task GitFixtureWaitsForTheSameProcessBeyondFiveSecondsAsync()
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            }
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command",
            "[Console]::Out.WriteLine('stdout-before'); [Console]::Error.WriteLine('stderr-before'); [void][Console]::ReadLine(); [Console]::Out.WriteLine('stdout-after'); [Console]::Error.WriteLine('stderr-after')" })
            process.StartInfo.ArgumentList.Add(argument);
        var started = false;
        var stdout = Task.FromResult("");
        var stderr = Task.FromResult("");
        var cancellation = new CancellationTokenSource();
        Task release = Task.CompletedTask;
        await RunSourceCheckoutOwnerAsync(async () =>
        {
            started = process.Start();
            Require(started, "The controlled local fixture starts exactly one process.");
            stdout = process.StandardOutput.ReadToEndAsync();
            stderr = process.StandardError.ReadToEndAsync();
            release = ReleaseAsync();
            await WaitForGitProcessAsync(process, stdout, stderr);
            await release;
            Require(stdout.Result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).SequenceEqual(["stdout-before", "stdout-after"]) &&
                stderr.Result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).SequenceEqual(["stderr-before", "stderr-after"]),
                "The same process exits after six seconds and both complete output streams are observed without retry.");
        }, async () =>
        {
            cancellation.Cancel();
            try { await release; }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }, () => CleanupGitProcessAsync(process, started, stdout, stderr),
            () => { process.Dispose(); return Task.CompletedTask; },
            () => { cancellation.Dispose(); return Task.CompletedTask; });

        async Task ReleaseAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(6), cancellation.Token);
            await process.StandardInput.WriteLineAsync("continue");
        }
    }

    private static async Task SourceCheckoutOwnerPreservesFailuresAndOneRootAsync()
    {
        foreach (var hasPrimary in new[] { true, false })
        {
            var primary = new RunnerOwnedException("private-owned-source-failure");
            var cleanupFailure = new IOException("private-cleanup-failure");
            var failingCleanupCalls = 0;
            var followingCleanupCalls = 0;
            var completedAfterCleanup = false;
            var telemetry = new List<RegressionActionEvent>();
            using var owner = RegressionActionScope.BeginCase("runner.probe/source-owner", action =>
            {
                telemetry.Add(action);
                if (action.Kind == "completed") completedAfterCleanup = failingCleanupCalls == 1 && followingCleanupCalls == 1;
            });
            Exception? actual = null;
            try
            {
                await RunSourceCheckoutOwnerAsync(async () =>
                {
                    Require(RegressionActionScope.QueueRoot() is null, "The source sequence already owns its queued root.");
                    await Task.Yield();
                    await RunSourceCheckoutOwnerAsync(() =>
                    {
                        Require(RegressionActionScope.QueueRoot() is null, "Nested Git work inherits the same root after an await.");
                        if (hasPrimary) throw primary;
                        return Task.CompletedTask;
                    });
                }, () =>
                {
                    failingCleanupCalls++;
                    Require(RegressionActionScope.QueueRoot() is null, "Cleanup retains the original root rather than renewing its deadline.");
                    throw cleanupFailure;
                }, () => { followingCleanupCalls++; return Task.CompletedTask; });
            }
            catch (Exception error) { actual = error; }
            Require(ReferenceEquals(actual, hasPrimary ? primary : cleanupFailure),
                "Cleanup preserves the original exception instance and cleanup-only failure remains a failure.");
            Require(failingCleanupCalls == 1 && followingCleanupCalls == 1 && completedAfterCleanup,
                "Every cleanup runs exactly once before the owner publishes completion, including after cleanup failure.");
            Require(telemetry.Select(action => action.Kind).SequenceEqual(["queued", "entered", "completed"]) &&
                telemetry.All(action => action.Ordinal == 1 && action.QueuedTimestamp == telemetry[0].QueuedTimestamp),
                "The complete async/nested source sequence has one queued timestamp and one root without renewal.");
            if (hasPrimary)
                Require((string?)primary.Data["RunnerFixtureCleanupExceptionType"] == typeof(IOException).FullName,
                    "Secondary cleanup diagnostics retain only the safe exception type.");
        }
    }

    private static async Task SourceIdentityAndAffectedScopeIgnoreForeignWorkingDirectoryAsync()
    {
        var fixture = new RunnerFixture();
        var originalDirectory = Environment.CurrentDirectory;
        await RunSourceCheckoutOwnerAsync(async () =>
        {
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
            Environment.CurrentDirectory = fixture.DirectoryPath;
            var actual = await RegressionSource.CaptureAsync();
            Require(string.Equals(expected.Root, actual.Root, StringComparison.OrdinalIgnoreCase) && expected.Identity.Commit == actual.Identity.Commit,
                "A foreign checkout as current directory cannot replace the executable's source provenance.");
            var actualScope = RegressionImpact.Select(registry, await actual.ChangedPathsAsync("HEAD"));
            Require(actualScope.Ids.SetEquals(expectedScope.Ids) && actualScope.ConservativeFallback == expectedScope.ConservativeFallback,
                "Affected selection follows the executable's checkout rather than foreign documentation changes.");
        }, () => { Environment.CurrentDirectory = originalDirectory; return Task.CompletedTask; },
            () => { fixture.Dispose(); return Task.CompletedTask; });
    }

    private static async Task RunSourceCheckoutOwnerAsync(Func<Task> work, params Func<Task>[] cleanup)
    {
        var root = RegressionActionScope.QueueRoot();
        using var entered = root?.Enter();
        Exception? failure = null;
        try
        {
            try { await work(); }
            catch (Exception error) { failure = error; }
            foreach (var release in cleanup)
            {
                try { await release(); }
                catch (Exception error)
                {
                    if (failure is null) failure = error;
                    else
                    {
                        try { failure.Data["RunnerFixtureCleanupExceptionType"] ??= error.GetType().FullName; }
                        catch { /* Safe secondary diagnostics cannot replace the original failure. */ }
                    }
                }
            }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
        finally { root?.Complete(); }
    }

    private static async Task RunGitAsync(string directory, params string[] arguments)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }
        };
        var started = false;
        Task stdout = Task.CompletedTask;
        Task stderr = Task.CompletedTask;
        await RunSourceCheckoutOwnerAsync(async () =>
        {
            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            started = process.Start();
            Require(started, "A local Git fixture starts.");
            stdout = process.StandardOutput.ReadToEndAsync();
            stderr = process.StandardError.ReadToEndAsync();
            await WaitForGitProcessAsync(process, stdout, stderr);
        }, () => CleanupGitProcessAsync(process, started, stdout, stderr),
            () => { process.Dispose(); return Task.CompletedTask; });
    }

    private static async Task WaitForGitProcessAsync(Process process, Task stdout, Task stderr)
    {
        await Task.WhenAll(process.WaitForExitAsync(), stdout, stderr);
        Require(process.ExitCode == 0, "A local Git fixture operation succeeds.");
    }

    private static async Task CleanupGitProcessAsync(Process process, bool started, Task stdout, Task stderr)
    {
        if (!started) return;
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await Task.WhenAll(process.WaitForExitAsync(), stdout, stderr).WaitAsync(RegressionExecution.DiagnosticTimeout);
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
                if (!args.Contains("--results", StringComparer.Ordinal) &&
                    (File.Exists(ReportPath) || Directory.Exists(RegressionReport.GenerationsRoot(ReportPath))))
                    ReportPath = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".json");
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
