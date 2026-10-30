internal static class RegressionCaseAliases
{
    // Only exact former aggregate IDs expand. Default registration contains leaves.
    private static readonly IReadOnlyDictionary<string, string> Prefixes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["playback.streaming/run-all"] = "playback.streaming/",
        ["cards.like/run-all"] = "cards.like/presentation/",
        ["cards.avatar/run-all"] = "cards.avatar/",
        ["cards.description/run"] = "cards.description/",
        ["input.cursor/run"] = "input.cursor/",
        ["input.settings/run-all"] = "input.settings/native/",
        ["numeric.geometry/compact-numeric-geometry"] = "numeric.geometry/compact/",
        ["numeric.bottom/run"] = "numeric.bottom/",
        ["cards.ink/run"] = "cards.ink/",
        ["popup.search-adjustment/run-all"] = "popup.search-adjustment/native/",
        ["popup.language-tags/run-all"] = "popup.language-tags/",
        ["preview.search-retention/run-all"] = "preview.search-retention/",
        ["preview.accepted-continuity/run-all"] = "preview.accepted-continuity/",
    };

    // Exact retired identities are selection compatibility, not registrations.
    // Retained synthetic/current exact descriptors still win in Resolve.
    internal static bool Contains(string id) => Prefixes.ContainsKey(id) || IsRetiredMigratedOwner(id);

    private static bool IsRetiredMigratedOwner(string id) =>
        id.StartsWith("input.settings/native/", StringComparison.Ordinal) ||
        id.StartsWith("input.cursor/", StringComparison.Ordinal) ||
        id is "repository.link/run-all" or "input.page-entry/run-all" ||
        id.StartsWith("cards.ink/", StringComparison.Ordinal) ||
        id.StartsWith("numeric.geometry/compact/", StringComparison.Ordinal) ||
        id.StartsWith("numeric.bottom/", StringComparison.Ordinal) ||
        id is "lifecycle.activity-dispatcher/playback-failure-and-cancellation-retire-bound-commands-on-ui-thread"
            or "lifecycle.activity-dispatcher/queued-retirement-cannot-unlock-new-ui-operation"
            or "settings.binding/api-key-password-input-persists-before-focus-leaves"
            or "settings.api-key-card/run-all"
            or "settings.layout/compiled-view-audio-network-and-fold-interactions"
            or "settings.alignment/run-all"
            or "numeric.glyph/equivalent-native-run-grouping"
            or "numeric.lifecycle/run"
            or "input.clear/run"
            or "lifecycle.activity/overlapping-startup-manual-and-tts-operations-unlock-after-final-completion"
            or "lifecycle.activity-dispatcher/deadline-diagnostics-preserve-original-failure-without-retry"
            or "input.settings/native-tool-tip-diagnostics-preserve-failures-without-retry"
            or "popup.search-adjustment/matrix-diagnostics-preserve-failures-without-retry"
            or "popup.search-adjustment/viewport-diagnostics-preserve-async-completion-and-failures"
            or "preview.ownership/preview-preparation-transitions-to-real-playback"
            or "preview.ownership/preview-same-voice-across-entries-cancels-preparation"
            or "preview.ownership/preview-old-completion-cannot-clear-new-preparation"
            or "preview.ownership/preview-preparation-failure-and-reset-clear-state"
            or "preview.ownership/preview-queued-publications-read-current-preparation-owner"
            or "preview.ownership/run-bounded-preview-regression-tests"
            or "preview.ownership/search-preview-removed-result-ignores-late-refresh"
            or "preview.ownership/search-preview-second-click-cancels-pending-refresh"
            or "preview.ownership/display-preview-second-click-cancels-pending-refresh-without-restart"
            or "preview.ownership/search-preview-invalidates-pending-display-refresh"
            or "preview.ownership/display-preview-voice-switch-ignores-late-refresh"
            or "preview.ownership/display-preview-dispose-cancels-and-ignores-late-refresh"
            or "input.clear/run-requests"
            or "voice.search-adjustment/run-requests"
            or "streaming.text/first-window-is-lazy-and-cuts-ordinary-long-sentences";

    internal static string[] Resolve(string id, IReadOnlyList<RegressionCase> registry, string unknownCode = "unknown-case")
    {
        if (registry.Any(item => item.Id == id)) return [id];
        if (IsRetiredMigratedOwner(id) && !Prefixes.ContainsKey(id))
            throw new RegressionSelectionException("retired-case");
        if (!Prefixes.TryGetValue(id, out var prefix))
            throw new RegressionSelectionException(unknownCode);
        var leaves = registry.Where(item => item.Id != id && item.Id.StartsWith(prefix, StringComparison.Ordinal)).Select(item => item.Id).ToArray();
        if (leaves.Length == 0) throw new RegressionSelectionException("empty-case-alias");
        return leaves;
    }
}
