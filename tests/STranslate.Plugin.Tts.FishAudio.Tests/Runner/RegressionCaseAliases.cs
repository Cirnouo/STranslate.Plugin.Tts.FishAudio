internal static class RegressionCaseAliases
{
    // Only exact former aggregate IDs expand. Default registration contains leaves.
    private static readonly IReadOnlyDictionary<string, string> Prefixes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["playback.streaming/run-all"] = "playback.streaming/"
    };

    internal static bool Contains(string id) => Prefixes.ContainsKey(id);

    internal static string[] Resolve(string id, IReadOnlyList<RegressionCase> registry, string unknownCode = "unknown-case")
    {
        if (registry.Any(item => item.Id == id)) return [id];
        if (!Prefixes.TryGetValue(id, out var prefix))
            throw new RegressionSelectionException(unknownCode);
        var leaves = registry.Where(item => item.Id != id && item.Id.StartsWith(prefix, StringComparison.Ordinal)).Select(item => item.Id).ToArray();
        if (leaves.Length == 0) throw new RegressionSelectionException("empty-case-alias");
        return leaves;
    }
}
