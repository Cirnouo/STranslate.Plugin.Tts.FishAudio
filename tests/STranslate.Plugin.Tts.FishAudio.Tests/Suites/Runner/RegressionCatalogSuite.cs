using static TestAssertions;

internal static class RegressionCatalogSuite
{
    internal static async Task RunAsync()
    {
        var cases = RegressionCatalog.Create();
        AssertEqual(cases.Count, cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count(), "Catalog IDs are unique");
        var partitions = new[] { "core", "native-settings", "native-input", "native-popups", "native-cards" };
        AssertEqual(true, partitions.All(partition => cases.Any(item => item.Partition == partition)), "Every supported partition has registered work");
        AssertEqual(true, cases.All(item => partitions.Contains(item.Partition, StringComparer.Ordinal)), "Every case belongs to one supported partition");

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

        foreach (var (oldId, count, partition) in new[]
        {
            ("cards.like/run-all", 87, "native-cards"),
            ("cards.avatar/run-all", 36, "native-cards"),
            ("cards.description/run", 25, "native-cards"),
            ("input.cursor/run", 31, "native-input"),
            ("input.settings/run-all", 142, "native-input"),
            ("numeric.geometry/compact-numeric-geometry", 48, "native-settings"),
            ("numeric.bottom/run", 12, "native-settings"),
            ("cards.ink/run", 25, "native-settings"),
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

        var anchors = new[]
        {
            "cards.ink/run",
            "numeric.glyph/equivalent-native-run-grouping",
            "numeric.geometry/compact-numeric-geometry",
            "numeric.bottom/run",
            "numeric.lifecycle/run",
            "input.clear/run-requests",
            "input.clear/run",
        };
        var previous = -1;
        foreach (var id in anchors)
        {
            var owned = RegressionCaseAliases.Resolve(id, cases);
            var indices = owned.Select(owner => cases.ToList().FindIndex(item => item.Id == owner)).ToArray();
            AssertEqual(true, indices.Min() > previous, "Ink and numeric native first-use anchors retain their original dispatch order");
            previous = indices.Max();
        }
        AssertEqual(true, cases.Single(item => item.Id == "lifecycle.activity-dispatcher/deadline-diagnostics-preserve-original-failure-without-retry").Partition == "core", "Injected dispatcher diagnostics stay offline core work");
        AssertEqual(true, cases.Single(item => item.Id == "lifecycle.activity-dispatcher/playback-failure-and-cancellation-retire-bound-commands-on-ui-thread").Partition == "native-settings", "Real command listeners retain native-settings ownership");

        foreach (var valid in new[]
        {
            new[] { "--accepted-preview-continuity-tests", "selection" },
            new[] { "--accepted-preview-continuity-tests", "lease" },
            new[] { "--page-entry-tests", "fields" },
            new[] { "--search-adjustment-control-tests", "titles" },
            new[] { "--settings-input-tests", "fold-focus" },
            new[] { "--popup-proof-tests", "private-proof-directory" },
            new[] { "--voice-card-layout-tests", "private-proof-directory" },
        })
            AssertEqual(true, LegacyRegressionSelectors.AreArgumentsValid(valid), "Original focused groups and private proof-directory parameters stay valid without executing fixtures");

        void AddBehavior(string group, bool[] themes, string[] scales, string[] widths)
        {
            foreach (var dark in themes)
            foreach (var scale in scales)
            foreach (var width in widths)
            foreach (var mode in new[] { "search", "by-id", "favorites" })
                behaviorIds.Add($"cards.layout.{group}/en/{(dark ? "dark" : "light")}/dpi-{scale}/width-{width}/{mode}");
        }

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
    }
}
