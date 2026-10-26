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
            var index = cases.ToList().FindIndex(item => item.Id == id);
            AssertEqual(true, index > previous, "Ink and numeric native first-use anchors retain their original dispatch order");
            previous = index;
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
