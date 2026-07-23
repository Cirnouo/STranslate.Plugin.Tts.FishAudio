using System.Text.Json.Serialization;

namespace STranslate.Plugin.Tts.FishAudio.Configuration;

[JsonConverter(typeof(SettingsJsonConverter))]
public sealed class Settings
{
    public const double DefaultSpeed = 1.0;
    public const double DefaultVolume = 0.0;
    public const bool DefaultNormalizeLoudness = true;
    public const double DefaultTemperature = 0.7;
    public const double DefaultTopP = 0.7;
    public const string DefaultLatency = "normal";
    public const bool DefaultNormalize = false;
    public const int DefaultMp3Bitrate = 192;
    public const bool DefaultConditionOnPreviousChunks = true;

    public int SchemaVersion { get; internal set; } = SettingsSchema.Current;
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

    internal bool IsReadOnly { get; set; }
    internal bool NeedsCanonicalSave { get; set; }
    internal string ReadOnlyReason { get; set; } = "";
}
