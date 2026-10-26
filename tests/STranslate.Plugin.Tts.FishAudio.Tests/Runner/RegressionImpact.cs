internal sealed record RegressionImpactSelection(HashSet<string> Ids, bool ConservativeFallback, string[] RuleIds, string[] RecommendedGroups);

internal static class RegressionImpact
{
    // Exact leaves precede shared-root fallback. Keep these explicit boundaries aligned with the catalog.
    private static readonly ImpactRule[] Rules =
    [
        new("documentation-contracts", path => path.StartsWith("docs/", StringComparison.Ordinal) || path.EndsWith(".md", StringComparison.OrdinalIgnoreCase), ["repository"]),
        new("preview-ownership", path => IsPresentationLeaf(path, "PreviewAudioDownloader.cs", "PreviewAudioUrlPolicy.cs", "PreviewPlaybackController.cs", "PreviewRefreshCoordinator.cs", "NAudioPreviewAudioPlayer.cs") || IsSuite(path, "PreviewSuite.cs", "SearchPreviewRetentionSuite.cs", "AcceptedPreviewContinuitySuite.cs"),
            ["preview", "cards.avatar", "voice.favorites", "voice.credentials", "voice.credential-display", "voice.language", "lifecycle.activity-dispatcher", "repository.architecture"]),
        new("avatar-presentation", path => IsPresentationLeaf(path, "AvatarPreviewGlyphSizeConverter.cs", "AvatarPreviewRingConverter.cs", "ProgressToDashOffsetConverter.cs") || IsSuite(path, "AvatarPreviewSuite.cs"),
            ["cards.avatar", "preview", "repository.presentation"]),
        new("card-header-description", path => IsPresentationLeaf(path, "VoiceHeaderPanel.cs", "VoiceDescriptionVisibilityConverter.cs") || IsSuite(path, "VoiceDescriptionSuite.cs", "VoiceCardLayoutSuite.cs"),
            ["cards.layout", "cards.description", "cards.like", "repository.presentation"]),
        new("numeric-input", path => IsPresentationLeaf(path, "InlineNumericEditor.xaml", "InlineNumericEditor.xaml.cs", "NumericSlider.cs", "NumericWheelAdjustment.cs", "NumericTextLineMetricConverter.cs", "NativeTextOutline.cs") || IsSuite(path, "NumericBottomSuite.cs", "NumericAlignmentLifecycleSuite.cs", "NumericGlyphObservationSuite.cs", "TopPCompatibilitySuite.cs"),
            ["numeric", "input.settings", "input.cursor", "settings.layout", "settings.binding", "settings.alignment", "repository.presentation"]),
        new("popup-shared", path => IsPresentationLeaf(path, "PopupMaterial.cs", "PopupNativeHeading.cs", "PopupScrollbarLane.cs", "PopupViewport.cs", "SearchAdjustmentControl.xaml", "SearchAdjustmentControl.xaml.cs") || IsSuite(path, "PopupDirectionSuite.cs", "PopupHeadingSuite.cs", "SearchAdjustmentControlSuite.cs", "NativeKeyboardStateSuite.cs"),
            ["popup", "input.cursor", "voice.search-adjustment", "repository.presentation"]),
        new("language-tags", path => IsPresentationLeaf(path, "VoiceLanguageTags.xaml", "VoiceLanguageTags.xaml.cs", "VoiceLanguagePresentation.cs", "LanguageMoreButton.cs", "LanguageWrapPanel.cs") || IsSuite(path, "VoiceLanguageTagsSuite.cs", "VoiceLanguageSuite.cs"),
            ["popup", "input.cursor", "voice.language", "cards.layout", "cards.description", "cards.like", "repository.presentation"]),
        new("streaming-runtime", path => path.StartsWith("STranslate.Plugin.Tts.FishAudio/Streaming/", StringComparison.Ordinal) || path.StartsWith("STranslate.Plugin.Tts.FishAudio/Playback/", StringComparison.Ordinal),
            ["streaming", "playback", "settings.streaming", "repository.architecture"]),
        new("voice-provider-cache", path => path.StartsWith("STranslate.Plugin.Tts.FishAudio/FishAudio/", StringComparison.Ordinal) || path.StartsWith("STranslate.Plugin.Tts.FishAudio/Caching/", StringComparison.Ordinal),
            ["voice", "preview", "streaming", "playback", "settings.api-key", "settings.credit", "numeric.top-p", "lifecycle", "cards.avatar", "repository.architecture"])
    ];

    internal static RegressionImpactSelection Select(IReadOnlyList<RegressionCase> registry, IReadOnlyList<string>? changedPaths)
    {
        if (changedPaths is null) return All(registry, "source-unavailable-or-changed");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        var groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawPath in changedPaths)
        {
            var path = rawPath.Replace('\\', '/');
            var rule = Rules.FirstOrDefault(candidate => candidate.Matches(path));
            if (rule is null) return All(registry, IsSharedInput(path) ? "shared-input" : "unclassified-path");
            var pathIds = registry.Where(item => rule.Groups.Any(prefix => RegressionSelection.MatchesGroup(item.Id, prefix)))
                .Select(item => item.Id).ToArray();
            // An earlier match cannot hide an unmatched category for a later path.
            if (pathIds.Length == 0) return All(registry, "unmatched-rule-category");
            ids.UnionWith(pathIds);
            ruleIds.Add(rule.Id);
            groups.UnionWith(rule.Groups);
        }
        return new(ids, false, ruleIds.Order(StringComparer.Ordinal).ToArray(), groups.Order(StringComparer.Ordinal).ToArray());
    }

    internal static RegressionImpactSelection Recover(IReadOnlyList<RegressionCase> registry, RegressionSourceIdentity previous, IReadOnlyList<string>? changedPaths)
    {
        if (!previous.Available || changedPaths is null) return All(registry, "previous-source-baseline-unavailable");
        var current = Select(registry, changedPaths);
        if (!previous.Dirty) return current;
        if (!previous.DirtyScopeRecorded || previous.DirtyAffectedIds is not { Length: > 0 } ||
            previous.DirtyAffectedIds.Any(id => !registry.Any(item => item.Id == id)))
            return All(registry, "previous-dirty-scope-unavailable");
        current.Ids.UnionWith(previous.DirtyAffectedIds);
        // Derive displayed groups from current registered IDs, never from untrusted report prose.
        var previousGroups = previous.DirtyAffectedIds.Select(id => id.Split('/')[0]);
        return current with
        {
            RuleIds = [.. current.RuleIds, "previous-dirty-scope"],
            RecommendedGroups = current.RecommendedGroups.Concat(previousGroups).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
        };
    }

    private static bool IsPresentationLeaf(string path, params string[] names) =>
        path.StartsWith("STranslate.Plugin.Tts.FishAudio/Presentation/", StringComparison.Ordinal) && names.Contains(path[(path.LastIndexOf('/') + 1)..], StringComparer.Ordinal);

    private static bool IsSuite(string path, params string[] names) =>
        path.StartsWith("tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/", StringComparison.Ordinal) && names.Contains(path[(path.LastIndexOf('/') + 1)..], StringComparer.Ordinal);

    private static bool IsSharedInput(string path) => path.StartsWith("tests/STranslate.Plugin.Tts.FishAudio.Tests/TestInfrastructure/", StringComparison.Ordinal) ||
        path.StartsWith("tests/STranslate.Plugin.Tts.FishAudio.Tests/Runner/", StringComparison.Ordinal) ||
        path.StartsWith("STranslate.Plugin.Tts.FishAudio/Configuration/", StringComparison.Ordinal) ||
        path.StartsWith("STranslate.Plugin.Tts.FishAudio/Lifecycle/", StringComparison.Ordinal) ||
        path.StartsWith("STranslate.Plugin.Tts.FishAudio/Languages/", StringComparison.Ordinal) ||
        path.EndsWith("SettingsView.xaml", StringComparison.Ordinal) || path.EndsWith("SettingsViewModel.cs", StringComparison.Ordinal) ||
        path.EndsWith(".csproj", StringComparison.Ordinal) || path is "build.ps1" or "Directory.Build.props" or "Directory.Build.targets";

    private static RegressionImpactSelection All(IReadOnlyList<RegressionCase> registry, string reason) =>
        new(registry.Select(item => item.Id).ToHashSet(StringComparer.Ordinal), true, [reason], ["all"]);

    private sealed record ImpactRule(string Id, Func<string, bool> Matches, string[] Groups);
}
