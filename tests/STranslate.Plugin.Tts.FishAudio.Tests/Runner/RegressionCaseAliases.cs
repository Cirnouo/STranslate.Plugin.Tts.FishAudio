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
    internal static bool Contains(string id) => Prefixes.ContainsKey(id) || IsRetiredNativeSettingsOwner(id);

    private static bool IsRetiredNativeSettingsOwner(string id) =>
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
            or "input.clear/run";

    internal static string[] Resolve(string id, IReadOnlyList<RegressionCase> registry, string unknownCode = "unknown-case")
    {
        if (registry.Any(item => item.Id == id)) return [id];
        if (IsRetiredNativeSettingsOwner(id) && !Prefixes.ContainsKey(id))
            throw new RegressionSelectionException("retired-case");
        if (!Prefixes.TryGetValue(id, out var prefix))
            throw new RegressionSelectionException(unknownCode);
        var leaves = registry.Where(item => item.Id != id && item.Id.StartsWith(prefix, StringComparison.Ordinal)).Select(item => item.Id).ToArray();
        if (leaves.Length == 0) throw new RegressionSelectionException("empty-case-alias");
        return leaves;
    }
}
