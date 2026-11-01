using static TestAssertions;

internal static class RegressionCatalogSuite
{
    internal static void RemainingLegacyIdsAndPartitionsAreComplete()
    {
        var cases = RegressionCatalog.Create();
        AssertEqual(cases.Count, cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count(), "Catalog IDs are unique");
        AssertEqual(0, cases.Count, "Every original responsibility has one mapped framework owner and no current legacy registration");
        var historical = DeclaredOriginalPopupDescriptors();
        AssertEqual(147, historical.Count, "The immutable reference retains every original popup owner");
        AssertEqual(true, historical.All(item => item.Partition == "native-popups"),
            "Historical descriptors retain their original partition without becoming default work");

    }

    internal static void StaticCardContextsKeepEveryOriginalBoundary()
    {
        var cases = DeclaredOriginalCardDescriptors();
        var cards = cases.Where(item => item.Id.StartsWith("cards.layout/", StringComparison.Ordinal)).ToArray();
        AssertEqual(505, cards.Length, "Voice card layout exposes its cold action and all 504 independent contexts");
        var expected = new List<string> { "cards.layout/cold-construction" };
        foreach (var locale in new[] { "en", "zh-cn", "zh-tw", "ja", "ko", "ru", "tr" })
        foreach (var theme in new[] { "light", "dark" })
        foreach (var dpi in new[] { "1", "1-25", "1-5" })
        foreach (var width in new[] { "320", "400", "480", "640" })
        foreach (var mode in new[] { "search", "by-id", "favorites" })
            expected.Add($"cards.layout/{locale}/{theme}/dpi-{dpi}/width-{width}/{mode}");
        AssertEqual(true, expected.SequenceEqual(cards.Select(item => item.Id), StringComparer.Ordinal), "The card context coverage and native first-use order remain complete");
        AssertEqual(true, cards.All(item => item.Partition == "native-cards"), "Each card context retains native-card ownership");

    }

    internal static void DynamicCardBehaviorsKeepEveryOriginalBoundary()
    {
        var cases = DeclaredOriginalCardDescriptors();
        var cards = cases.Where(item => item.Id.StartsWith("cards.layout/", StringComparison.Ordinal)).ToArray();
        var behaviorIds = new List<string>();
        AddBehavior("description", [false, true], ["1", "1-25", "1-5"], ["320", "400", "480", "640"]);
        AddBehavior("headers", [false, true], ["1", "1-25", "1-5"], ["320", "400", "480", "640"]);
        AddBehavior("inheritance", [false, true], ["1", "1-25", "1-5"], ["400"]);
        AddBehavior("date", [false, true], ["1-25"], ["400"]);
        AddBehavior("tooltip", [false], ["1"], ["400"]);
        AddBehavior("loading", [false, true], ["1-25"], ["320"]);
        var behaviors = cases.Where(item => item.Id.StartsWith("cards.layout.", StringComparison.Ordinal)).ToArray();
        AssertEqual(true, behaviorIds.SequenceEqual(behaviors.Select(item => item.Id), StringComparer.Ordinal),
            "Dynamic card ownership retains every declared theme/DPI/width/mode boundary in catalog order");
        AssertEqual(177, behaviors.Length, "Independent card behavior owners supplement the unchanged static matrix");
        AssertEqual(true, behaviors.All(item => item.Partition == "native-cards") &&
            cases.ToList().FindIndex(item => item.Id == behaviors[0].Id) > cases.ToList().FindIndex(item => item.Id == cards[^1].Id),
            "Behavior owners remain after the complete original static contexts in native-cards");

        void AddBehavior(string group, bool[] themes, string[] scales, string[] widths)
        {
            foreach (var dark in themes)
            foreach (var scale in scales)
            foreach (var width in widths)
            foreach (var mode in new[] { "search", "by-id", "favorites" })
                behaviorIds.Add($"cards.layout.{group}/en/{(dark ? "dark" : "light")}/dpi-{scale}/width-{width}/{mode}");
        }

    }

    internal static void RemainingNativeAliasesKeepTheirExactOrderedOwners()
    {
        var current = RegressionCatalog.Create();
        var historical = DeclaredOriginalCardDescriptors().Concat(DeclaredOriginalPopupDescriptors()).ToArray();
        // Explicit historical descriptors retain their independent alias oracle;
        // they are never default registrations or executed by these checks.
        var cases = current.Concat(historical).ToArray();
        foreach (var original in historical)
        foreach (var selector in new[] { "--case", "--group" })
        {
            AssertEqual(false, current.Any(item => item.Id == original.Id), "A transferred owner has no second legacy registration");
            AssertRetired(() => RegressionSelection.Parse([selector, original.Id]).ExplicitIds(current));
            AssertEqual(true, RegressionCaseAliases.Resolve(original.Id, [original]).SequenceEqual([original.Id]),
                "An explicit historical descriptor still wins exact-ID resolution without invoking its callback");
        }
        foreach (var alias in new[]
        {
            "cards.like/run-all", "cards.avatar/run-all", "cards.description/run",
            "preview.search-retention/run-all", "preview.accepted-continuity/run-all",
            "cards.layout", "cards.description", "cards.like", "cards.avatar",
            "preview.search-retention", "preview.accepted-continuity",
            "popup.language-tags/run-all", "popup.search-adjustment/run-all",
            "popup.keyboard-state/run", "popup.heading/run", "popup.direction/run",
        })
            AssertRetired(() => RegressionSelection.Parse(["--group", alias]).ExplicitIds(current));
        foreach (var (oldId, count, partition) in new[]
        {
            ("cards.like/run-all", 87, "native-cards"),
            ("cards.avatar/run-all", 36, "native-cards"),
            ("cards.description/run", 25, "native-cards"),
            ("popup.search-adjustment/run-all", 60, "native-popups"),
            ("popup.language-tags/run-all", 84, "native-popups"),
            ("preview.search-retention/run-all", 7, "native-cards"),
            ("preview.accepted-continuity/run-all", 17, "native-cards")
        })
        {
            AssertEqual(false, cases.Any(item => item.Id == oldId), "A former native aggregate is an alias rather than duplicate default work");
            var owners = RegressionCaseAliases.Resolve(oldId, cases);
            AssertEqual(count, owners.Length, "The native aggregate alias retains every original independently dispatched owner");
            AssertEqual(true, owners.All(id => cases.Single(item => item.Id == id).Partition == partition),
                "Native leaves retain their existing process partition");
        }
        AssertEqual(false, RegressionCaseAliases.Resolve("cards.like/run-all", cases).Contains("cards.like/shared-card-template-is-compiled", StringComparer.Ordinal),
            "Like traversal excludes the earlier standalone template invocation");
        AssertEqual(true, RegressionCaseAliases.Resolve("popup.search-adjustment/run-all", cases)
            .All(id => id.StartsWith("popup.search-adjustment/native/", StringComparison.Ordinal)),
            "Search native traversal excludes separate core observer contracts");
        var searchLast = RegressionCaseAliases.Resolve("popup.search-adjustment/run-all", cases)[^1];
        AssertEqual(true, cases.ToList().FindIndex(item => item.Id == searchLast) < cases.ToList().FindIndex(item => item.Id == "popup.keyboard-state/run"),
            "The complete Search owner sequence remains before native keyboard first use");

    }

    internal static void OriginalLegacyArgumentShapesRemainValid()
    {
        foreach (var valid in new[]
        {
            new[] { "--popup-proof-tests", "private-proof-directory" },
        })
            AssertEqual(false, LegacyRegressionSelectors.AreArgumentsValid(valid), "The original popup proof route retires with its complete framework owner");

        foreach (var retired in RetiredCardArgumentShapes())
            AssertEqual(false, LegacyRegressionSelectors.AreArgumentsValid(retired),
                "Every original Cards direct route and filter rejects rather than choosing partial legacy work");

        AssertEqual(false, LegacyRegressionSelectors.AreArgumentsValid(["--search-adjustment-control-tests", "titles"]),
            "The former mixed Search selector retires when its diagnostic responsibility moves to NUnit; native owners remain selectable through current case IDs");
        AssertEqual(false, LegacyRegressionSelectors.AreArgumentsValid(["--page-entry-tests", "fields"]),
            "The former PageEntry fields shape retires with its complete five-workflow NUnit owner");
        AssertEqual(false, LegacyRegressionSelectors.AreArgumentsValid(["--settings-input-tests", "fold-focus"]),
            "The former settings fold-focus shape retires with its complete native-input profile");

    }

    internal static async Task InvalidLegacyArgumentCombinationsRejectBeforeFixtureAsync()
    {
        using var diagnostics = new StringWriter();
        var previousError = Console.Error;
        try
        {
            Console.SetError(diagnostics);
            foreach (var invalid in new[]
            {
                new[] { "--execution-guard-tests", "--native-key-state-tests" },
                new[] { "--voice-card-layout-tests", "--list" },
                new[] { "--favorite-order-tests", "unexpected" },
                new[] { "--popup-proof-tests" },
                new[] { "--settings-input-tests", "wheel", "extra" },
                new[] { "--accepted-preview-continuity-tests", "nonsense" },
                new[] { "--page-entry-tests", "nonsense" },
                new[] { "--search-adjustment-control-tests", "nonsense" },
                new[] { "--settings-input-tests", "nonsense" },
            })
                AssertEqual<int?>(2, await LegacyRegressionSelectors.TryRunAsync(invalid), "Invalid legacy combinations fail before executing a fixture");
        }
        finally { Console.SetError(previousError); }
        await RetiredCardsRejectBeforeSourceOrChildAsync();
    }

    // Read-only historical declarations, deliberately separate from registration.
    // These constructors return IDs/delegates; no delegate is invoked here.
    private static IReadOnlyList<RegressionCase> DeclaredOriginalCardDescriptors()
    {
        var descriptors = new List<(string Id, Func<Task> Run)>
        {
            ("cards.like/shared-card-template-is-compiled", VoiceLikeSuite.SharedCardTemplateIsCompiledAsync),
        };
        descriptors.AddRange(VoiceCardLayoutSuite.CreateCases());
        descriptors.AddRange(VoiceDescriptionSuite.CreateCases());
        descriptors.AddRange(VoiceLikeSuite.CreateCases());
        descriptors.AddRange(AvatarPreviewSuite.CreateCases());
        descriptors.AddRange(SearchPreviewRetentionSuite.CreateCases());
        descriptors.AddRange(AcceptedPreviewContinuitySuite.CreateCases());
        using var reference = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(FindRepoFile("tests/Migration/legacy-catalog.json")));
        var originals = reference.RootElement.GetProperty("cases").EnumerateArray()
            .Where(item => item.GetProperty("partition").GetString() == "native-cards")
            .Select(item => item.GetProperty("id").GetString()!).ToArray();
        AssertEqual(855, originals.Length, "The immutable reference retains every historical Cards responsibility");
        AssertEqual(true, originals.SequenceEqual(descriptors.Select(item => item.Id), StringComparer.Ordinal),
            "Original scene declarations retain all exact IDs and cold-prefix order independently of default registration");
        return descriptors.Select(item => new RegressionCase(item.Id, "native-cards", item.Run)).ToArray();
    }

    // Internal contract input only. Neither default registration nor an execution
    // selector can acquire these descriptors, and this method invokes no callback.
    internal static IReadOnlyList<RegressionCase> DeclaredOriginalPopupDescriptors()
    {
        var descriptors = new List<(string Id, Func<Task> Run)>();
        descriptors.AddRange(VoiceLanguageTagsSuite.CreateCases());
        descriptors.AddRange(SearchAdjustmentControlSuite.CreateCases());
        descriptors.Add(("popup.keyboard-state/run", () => NativeKeyboardStateSuite.RunAsync()));
        descriptors.Add(("popup.heading/run", () => PopupHeadingSuite.RunAsync()));
        descriptors.Add(("popup.direction/run", () => PopupDirectionSuite.RunAsync()));
        using var reference = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(FindRepoFile("tests/Migration/legacy-catalog.json")));
        var originals = reference.RootElement.GetProperty("cases").EnumerateArray()
            .Where(item => item.GetProperty("partition").GetString() == "native-popups")
            .Select(item => item.GetProperty("id").GetString()!).ToArray();
        AssertEqual(147, originals.Length, "The immutable reference retains every original Popup responsibility");
        AssertEqual(true, originals.SequenceEqual(descriptors.Select(item => item.Id), StringComparer.Ordinal),
            "Explicit original descriptors retain all exact IDs and native first-use order without default registration");
        return descriptors.Select(item => new RegressionCase(item.Id, "native-popups", item.Run)).ToArray();
    }

    private static void AssertRetired(Action select)
    {
        RegressionSelectionException? rejected = null;
        try { select(); }
        catch (RegressionSelectionException error) { rejected = error; }
        AssertEqual("retired-case", rejected?.Message, "A retired Cards selector must reject without selecting another owner");
    }

    private static IEnumerable<string[]> RetiredCardArgumentShapes()
    {
        foreach (var selector in new[]
        {
            "--voice-description-tests", "--search-preview-retention-tests",
            "--accepted-preview-continuity-tests", "--avatar-preview-tests",
            "--voice-like-tests", "--voice-card-layout-tests",
        })
            yield return [selector];
        foreach (var filter in new[] { "selection", "unlike", "browsing", "handoff", "credentials", "lifecycle", "lease" })
            yield return ["--accepted-preview-continuity-tests", filter];
        foreach (var selector in new[] { "--voice-description-tests", "--avatar-preview-tests", "--voice-card-layout-tests" })
            yield return [selector, "private-proof-directory"];
        yield return ["--favorite-order-tests", "--voice-like-tests"];
        yield return ["--case", "popup.keyboard-state/run", "--avatar-preview-tests"];
    }

    private static async Task RetiredCardsRejectBeforeSourceOrChildAsync()
    {
        // Prepare the registry while the real snapshot context is valid; below,
        // the rejection checks deliberately make source capture unusable.
        var historicalPopups = DeclaredOriginalPopupDescriptors();
        var source = Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT");
        var previousError = Console.Error;
        using var diagnostics = new StringWriter();
        var launches = 0;
        using var process = new RegressionProcessHost(_ =>
        {
            launches++;
            throw new InvalidOperationException("retired-cards-launched-child");
        });
        try
        {
            // Capturing source would fail. Only rejection before that boundary
            // can produce the exact diagnostic asserted below.
            Environment.SetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT",
                Path.Combine(Path.GetTempPath(), "absent-cards-context-" + Guid.NewGuid().ToString("N") + ".json"));
            Console.SetError(diagnostics);
            var shapes = RetiredCardArgumentShapes().ToArray();
            foreach (var shape in shapes)
                AssertEqual<int?>(2, await RegressionProcessHost.TryRunLegacyAsync(shape, process),
                    "Transferred Cards direct selectors reject before source capture or child creation");
            AssertEqual(true, diagnostics.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .SequenceEqual(shapes.Select(_ => "Retired legacy regression selector; use NUnit case selection.")),
                "Every direct rejection reaches the fixed retired boundary");
            diagnostics.GetStringBuilder().Clear();
            AssertEqual(2, await RegressionRunner.RunAsync(["--list", "--affected"], historicalPopups,
                process.ExecuteAsync, allowCoarseImpact: false), "Production legacy coarse impact is explicitly retired");
            AssertEqual("Runner selection error: retired-legacy-impact." + Environment.NewLine, diagnostics.ToString(),
                "The explicit historical registry still rejects coarse impact before source capture");
            AssertEqual(0, launches, "No direct or coarse retired route can launch a child");
        }
        finally
        {
            Console.SetError(previousError);
            Environment.SetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT", source);
        }
    }
}