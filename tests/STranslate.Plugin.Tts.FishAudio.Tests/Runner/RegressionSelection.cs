internal sealed class RegressionSelectionException(string code) : Exception(code);

internal sealed class RegressionSelection
{
    internal bool List { get; private set; }
    internal bool All { get; private set; }
    internal bool Affected { get; private set; }
    internal string BaseRef { get; private set; } = "origin/main";
    internal string? ResultsPath { get; private set; }
    internal string? RerunPath { get; private set; }
    internal List<string> Groups { get; } = [];
    internal List<string> Cases { get; } = [];
    internal List<string> Partitions { get; } = [];
    internal List<string> SafeInvocation { get; } = [];

    internal static RegressionSelection Parse(string[] args)
    {
        var result = new RegressionSelection();
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            result.SafeInvocation.Add(option is "--list" or "--all" or "--affected" or "--group" or "--case" or "--partition" or "--results" or "--rerun-failed" or "--base" ? option : "<unknown>");
            if (option == "--list") result.List = true;
            else if (option == "--all") result.All = true;
            else if (option == "--affected") result.Affected = true;
            else
            {
                if (option is not ("--group" or "--case" or "--partition" or "--results" or "--rerun-failed" or "--base"))
                    throw new RegressionSelectionException("unknown-option");
                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("--", StringComparison.Ordinal))
                    throw new RegressionSelectionException("missing-option-value");
                var value = args[index];
                if (option is "--group" or "--case" or "--partition")
                {
                    if (!IsSafeId(value)) throw new RegressionSelectionException("invalid-selector");
                    (option == "--group" ? result.Groups : option == "--case" ? result.Cases : result.Partitions).Add(value);
                    result.SafeInvocation.Add(value);
                }
                else if (option == "--base")
                {
                    if (value.StartsWith('-') || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('/' or '.' or '_' or '-' or '~' or '^')))
                        throw new RegressionSelectionException("invalid-base");
                    result.BaseRef = value;
                    result.SafeInvocation.Add("<ref>");
                }
                else
                {
                    if (option == "--results")
                    {
                        if (result.ResultsPath is not null) throw new RegressionSelectionException("duplicate-results");
                        result.ResultsPath = value;
                    }
                    else
                    {
                        if (result.RerunPath is not null) throw new RegressionSelectionException("duplicate-rerun");
                        result.RerunPath = value;
                    }
                    result.SafeInvocation.Add(option == "--results" ? "<results>" : "<report>");
                }
            }
        }
        if (result.All && (result.Groups.Count != 0 || result.Cases.Count != 0 || result.Affected || result.RerunPath is not null))
            throw new RegressionSelectionException("all-with-selection");
        if (args.Contains("--base", StringComparer.Ordinal) && !result.Affected && result.RerunPath is null)
            throw new RegressionSelectionException("base-without-affected");
        return result;
    }

    internal static bool IsSafeId(string value) => value.Length is > 0 and <= 240 &&
        char.IsAsciiLetterOrDigit(value[0]) && value.All(character => char.IsAsciiLetterOrDigit(character) && !char.IsAsciiLetterUpper(character) || character is '.' or '/' or '_' or '-');

    internal static bool MatchesGroup(string id, string group) => id == group ||
        id.StartsWith(group + "/", StringComparison.Ordinal) || id.StartsWith(group + ".", StringComparison.Ordinal);

    internal HashSet<string> ExplicitIds(IReadOnlyList<RegressionCase> registry)
    {
        foreach (var group in Groups)
            if (!registry.Any(item => MatchesGroup(item.Id, group))) throw new RegressionSelectionException("unknown-group");
        foreach (var id in Cases)
            if (!registry.Any(item => item.Id == id)) throw new RegressionSelectionException("unknown-case");
        foreach (var partition in Partitions)
            if (!registry.Any(item => item.Partition == partition)) throw new RegressionSelectionException("unknown-partition");
        return registry.Where(item => Cases.Contains(item.Id, StringComparer.Ordinal) || Groups.Any(group => MatchesGroup(item.Id, group)))
            .Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
    }
}
