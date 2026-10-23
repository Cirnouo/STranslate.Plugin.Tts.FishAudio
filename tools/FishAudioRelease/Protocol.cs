using System.Globalization;
using System.Text.RegularExpressions;

namespace FishAudioRelease;

internal static class Protocol
{
    internal static string Version(string tag)
    {
        Require(Regex.IsMatch(tag, @"\Av(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z") &&
            System.Version.TryParse(tag[1..], out _), "Tag must be canonical vM.m.p with Int32 components.");
        return tag[1..];
    }

    internal sealed record Line(string Text, bool Plain, int Level, string Heading);
    internal static List<Line> Lines(string source)
    {
        var result = new List<Line>();
        char fence = '\0';
        int length = 0;
        foreach (var text in source.Replace("\r\n", "\n").Split('\n'))
        {
            if (fence != '\0')
            {
                if (Regex.IsMatch(text, "^ {0,3}" + Regex.Escape(new string(fence, length)) + Regex.Escape(fence.ToString()) + @"*\s*$"))
                {
                    fence = '\0';
                }

                result.Add(new(text, false, 0, ""));
                continue;
            }

            var opening = Regex.Match(text, @"^ {0,3}(`{3,}|~{3,})");
            if (opening.Success)
            {
                fence = opening.Groups[1].Value[0];
                length = opening.Groups[1].Length;
                result.Add(new(text, false, 0, ""));
                continue;
            }

            var heading = Regex.Match(text, @"^ {0,3}(#{1,6})(?:[ \t]+|$)(.*)$");
            result.Add(new(text, true, heading.Success ? heading.Groups[1].Length : 0, heading.Success ? Regex.Replace(heading.Groups[2].Value, @"\s+#+\s*$", "").Trim() : ""));
        }

        return result;
    }

    internal static string Changes(string source, string version)
    {
        var lines = Lines(source);
        var matches = lines.Select((line, index) => (line, index)).Where(x => x.line.Level == 2 &&
            Regex.IsMatch(x.line.Heading, @"^\[" + Regex.Escape(version) + @"\]")).ToArray();
        Require(matches.Length == 1, "Changelog must have exactly one matching release entry.");
        var (entry, start) = matches[0];
        var match = Regex.Match(entry.Heading, @"^\[" + Regex.Escape(version) + @"\] - (\d{4}-\d{2}-\d{2})$");
        Require(match.Success &&
            DateOnly.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _), "Release requires real ISO calendar date.");
        var contents = lines.Skip(start + 1).TakeWhile(x => x.Level != 2).ToArray();
        Require(contents.Any(x => x.Plain &&
            Regex.IsMatch(x.Text, @"^\s*[-*+]\s+\S")), "Release requires nonempty bullet changes.");
        return string.Join('\n', contents.Select(x => x.Text)).Trim();
    }

    internal static string Notes(string changes, string tag, string generated, string repository, string? previous)
    {
        var lines = Lines(generated);
        int start = lines.FindIndex(x => x.Level == 2 &&
            x.Heading == "New Contributors");
        var contributors = start < 0 ? "" : string.Join('\n', lines.Skip(start + 1).TakeWhile(x => x.Level != 2 &&
            !(x.Plain &&
            x.Text.StartsWith("**Full Changelog**:", StringComparison.Ordinal))).Where(x => x.Plain &&
            Regex.IsMatch(x.Text, @"^\s*[-*+]\s+@\S+")).Select(x => x.Text)).Trim();
        var history = previous is null ? $"https://github.com/{repository}/commits/{Uri.EscapeDataString(tag)}" : $"https://github.com/{repository}/compare/{Uri.EscapeDataString(previous)}...{Uri.EscapeDataString(tag)}";
        return $"## What's Changed\n\n{changes}\n\n" + (contributors.Length > 0 ? $"## New Contributors\n\n{contributors}\n\n" : "") + $"**Full Changelog**: {history}\n";
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
