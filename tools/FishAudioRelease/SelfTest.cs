namespace FishAudioRelease;

internal static class SelfTest
{
    public static void Run()
    {
        Equal("1.2.3", Protocol.Version("v1.2.3"));
        foreach (var value in new[]
        {
            "1.2.3",
            "v01.2.3",
            "v1.2.3-rc.1",
            "v1.2.3+meta",
            "v1.2.3.4",
            "v2147483648.0.0",
            "v1.2.3\n"
        })
        {
            Reject(() => Protocol.Version(value));
        }

        Equal("### Added\n\n- Real change", Protocol.Changes("```md\n## [1.2.3] - 2026-01-01\n- fake\n```\n## [1.2.3] - 2026-10-23\n\n### Added\n\n- Real change\n\n## Other\n- excluded", "1.2.3"));
        foreach (var body in new[]
        {
            "## [1.2.3] - 2026-02-30\n- x",
            "## [1.2.3]\n- x",
            "## [1.2.3] - 2026-01-01\n```\n- fake\n```",
            "## [1.2.3] - 2026-01-01\n- x\n## [1.2.3] - 2026-01-02\n- y"
        })
        {
            Reject(() => Protocol.Changes(body, "1.2.3"));
        }

        var notes = Protocol.Notes("### Fixed\n- curated", "v1.2.3", "## What's Changed\n- unwanted PR\n## New Contributors\n- @person made first contribution\n**Full Changelog**: fake", "owner/repo", "v1.2.2");
        Equal("## What's Changed\n\n### Fixed\n- curated\n\n## New Contributors\n\n- @person made first contribution\n\n**Full Changelog**: https://github.com/owner/repo/compare/v1.2.2...v1.2.3\n", notes);
        Console.WriteLine("Release parser tests passed.");
    }

    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception($"Expected {expected}; got {actual}");
        }
    }

    internal static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new Exception("Expected rejection");
    }
}
