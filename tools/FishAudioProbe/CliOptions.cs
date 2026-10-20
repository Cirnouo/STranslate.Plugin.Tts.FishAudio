namespace FishAudioProbe;

internal sealed record CliOptions(string Output, string Group, HashSet<string> Cases)
{
    internal static CliOptions Parse(string[] args)
    {
        string? output = null;
        var group = "public";
        var cases = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var option = args[i];
            if (option is not ("--output" or "--group" or "--case") || i + 1 >= args.Length)
                throw new InvalidOperationException("Unsupported or incomplete CLI option. Use --help; credentials are environment-only.");
            var value = args[++i];
            if (option == "--output") output = Path.GetFullPath(value);
            else if (option == "--group") group = value;
            else cases.Add(value);
        }
        if (output is null || group is not ("public" or "authenticated" or "http-tts" or "ws-tts" or "all"))
            throw new InvalidOperationException("Specify --output PATH and a documented group.");
        return new(output, group, cases);
    }
}
