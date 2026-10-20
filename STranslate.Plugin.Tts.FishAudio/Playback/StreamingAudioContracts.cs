using NAudio.Wave;

namespace STranslate.Plugin.Tts.FishAudio.Playback;

internal readonly record struct PcmBlock(ReadOnlyMemory<byte> Bytes, WaveFormat Format);

internal interface IStreamingPcmDecoder : IDisposable
{
    IEnumerable<PcmBlock> Decode(ReadOnlyMemory<byte> encoded);
    void Complete();
}

internal interface IStreamingAudioOutput : IDisposable
{
    CancellationToken FailureToken { get; }
    void Init(IWaveProvider provider);
    void Play();
    long GetPlayedBytes();
    void ThrowIfFailed();
    void Stop();
}
