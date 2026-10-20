using STranslate.Plugin.Tts.FishAudio.Configuration;

namespace STranslate.Plugin.Tts.FishAudio.Streaming;

// Captured before the first await; streaming never reads mutable Settings.
internal sealed class SynthesisRequest(Settings settings)
{
    internal string ApiKey { get; } = settings.ApiKey;
    internal string VoiceId { get; } = settings.VoiceId;
    internal string Model { get; } = settings.SelectedModel;
    internal string AudioFormat { get; } = settings.AudioFormat;
    internal int Mp3Bitrate { get; } = settings.Mp3Bitrate;
    internal double Speed { get; } = settings.Speed;
    internal double Volume { get; } = settings.Volume;
    internal bool NormalizeLoudness { get; } = settings.NormalizeLoudness;
    internal double Temperature { get; } = settings.Temperature;
    internal double TopP { get; } = settings.TopP;
    internal bool Normalize { get; } = settings.Normalize;
    internal string Latency { get; } = settings.Latency;
    internal bool ConditionOnPreviousChunks { get; } = settings.ConditionOnPreviousChunks;
}
