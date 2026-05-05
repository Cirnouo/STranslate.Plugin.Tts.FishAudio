using System.Text.RegularExpressions;

namespace STranslate.Plugin.Tts.FishAudio;

public class Settings
{
    private static readonly Regex HexId32Regex = new(@"^[0-9a-f]{32}$", RegexOptions.Compiled);

    public string ApiKey { get; set; } = "";
    public string VoiceId { get; set; } = "";
    public string SelectedModel { get; set; } = "s2-pro";
    public double Speed { get; set; } = 1.0;
    public double Volume { get; set; }
    public bool NormalizeLoudness { get; set; } = true;
    public double Temperature { get; set; } = 0.7;
    public double TopP { get; set; } = 0.7;
    public string Latency { get; set; } = "normal";
    public bool Normalize { get; set; }
    public int Mp3Bitrate { get; set; } = 192;
    public bool ConditionOnPreviousChunks { get; set; } = true;
    public CachedVoiceInfo? CachedVoice { get; set; }

    public static bool IsValidApiKeyFormat(string key) => HexId32Regex.IsMatch(key);
    public static bool IsValidVoiceIdFormat(string id) => HexId32Regex.IsMatch(id);

    // Migration shims: populated by deserializer when reading old config
    public string? ReferenceId { get; set; }
    public CachedVoiceInfo? CachedModel { get; set; }

    public bool NeedsMigration => ReferenceId is not null || CachedModel is not null;

    public void Migrate()
    {
        if (!string.IsNullOrEmpty(ReferenceId) && string.IsNullOrEmpty(VoiceId))
            VoiceId = ReferenceId;
        if (CachedModel is not null && CachedVoice is null)
            CachedVoice = CachedModel;
        ReferenceId = null;
        CachedModel = null;
    }
}

public class CachedVoiceInfo
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string CoverImage { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public int TaskCount { get; set; }
    public string? SampleAudioUrl { get; set; }
}
