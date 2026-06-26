using System.Text.RegularExpressions;

namespace STranslate.Plugin.Tts.FishAudio;

public class Settings
{
    private static readonly Regex HexId32Regex = new(@"^[0-9a-f]{32}$", RegexOptions.Compiled);

    public const double DefaultSpeed = 1.0;
    public const double DefaultVolume = 0.0;
    public const bool DefaultNormalizeLoudness = true;
    public const double DefaultTemperature = 0.7;
    public const double DefaultTopP = 0.7;
    public const string DefaultLatency = "normal";
    public const bool DefaultNormalize = false;
    public const int DefaultMp3Bitrate = 192;
    public const bool DefaultConditionOnPreviousChunks = true;

    public string ApiKey { get; set; } = "";
    public string VoiceId { get; set; } = "";
    public string SelectedModel { get; set; } = FishAudioRuntime.GetDefaultModel(FishAudioRuntime.LocalUtcNow());
    public double Speed { get; set; } = DefaultSpeed;
    public double Volume { get; set; }
    public bool NormalizeLoudness { get; set; } = DefaultNormalizeLoudness;
    public double Temperature { get; set; } = DefaultTemperature;
    public double TopP { get; set; } = DefaultTopP;
    public string Latency { get; set; } = DefaultLatency;
    public bool Normalize { get; set; }
    public int Mp3Bitrate { get; set; } = DefaultMp3Bitrate;
    public bool ConditionOnPreviousChunks { get; set; } = DefaultConditionOnPreviousChunks;
    public CachedVoiceInfo? CachedVoice { get; set; }
    public bool IsS21ProFreePromoDismissed { get; set; }

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
