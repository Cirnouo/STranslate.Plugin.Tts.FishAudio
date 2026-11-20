using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class TestAssertions
{
    internal const string AppliedKey = "0123456789abcdef0123456789abcdef";
    internal const string DraftKey = "abcdef0123456789abcdef0123456789";

    internal static Settings CreateTtsSettings(string selectedModel) => new()
    {
        ApiKey = AppliedKey,
        VoiceId = "fedcba9876543210fedcba9876543210",
        SelectedModel = selectedModel,
        Speed = 1.25,
        Volume = -2.5,
        NormalizeLoudness = false,
        Mp3Bitrate = 128,
        Temperature = 0.41,
        TopP = 0.82,
        Latency = "low",
        Normalize = true,
        ConditionOnPreviousChunks = false,
    };

    internal static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {relativePath}");
    }

    internal static string FindRepoPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate) || Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.Parent is not null)
            root = root.Parent;
        return Path.Combine(root.FullName, relativePath);
    }

    internal static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "FishAudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    internal static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    internal static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}. Expected: {expected}; Actual: {actual}");
    }

    internal static void AssertNotNull(object? value, string message)
    {
        if (value is null)
            throw new InvalidOperationException(message);
    }

    internal static void AssertSequenceEqual(byte[] expected, byte[]? actual, string message)
    {
        if (actual is null || !expected.SequenceEqual(actual))
            throw new InvalidOperationException($"{message}. Expected: [{string.Join(", ", expected)}]; Actual: [{(actual is null ? "null" : string.Join(", ", actual))}]");
    }

    internal static void AssertEnumerableEqual<T>(IEnumerable<T> expected, IEnumerable<T>? actual, string message)
    {
        if (actual is null || !expected.SequenceEqual(actual))
            throw new InvalidOperationException($"{message}. Expected: [{string.Join(", ", expected)}]; Actual: [{(actual is null ? "null" : string.Join(", ", actual))}]");
    }

    internal static Dictionary<string, object> AssertDictionary(object? value, string message)
    {
        if (value is Dictionary<string, object> dictionary)
            return dictionary;

        throw new InvalidOperationException(message);
    }

    internal static Dictionary<string, string> AssertHeaders(Options? options, string message)
    {
        if (options?.Headers is { } headers)
            return headers;

        throw new InvalidOperationException(message);
    }
}
