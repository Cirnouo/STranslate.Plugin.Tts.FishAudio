namespace STranslate.Plugin.Tts.FishAudio.Configuration;

internal static class VoiceLanguageMetadata
{
    internal static IReadOnlyList<string> Normalize(IEnumerable<string?>? languages)
    {
        if (languages is null)
            return Array.Empty<string>();

        var codes = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var language in languages)
        {
            var code = language?.Trim().Replace('_', '-').ToLowerInvariant();
            if (!string.IsNullOrEmpty(code) && seen.Add(code))
                codes.Add(code);
        }
        return codes.AsReadOnly();
    }
}
