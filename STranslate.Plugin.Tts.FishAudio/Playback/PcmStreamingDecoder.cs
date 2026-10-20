using NAudio.Wave;
using System.IO;

namespace STranslate.Plugin.Tts.FishAudio.Playback;

// Fish Audio's raw PCM response is interpreted as signed little-endian 44.1kHz s16 mono.
internal sealed class PcmStreamingDecoder : IStreamingPcmDecoder
{
    private static readonly WaveFormat Format = new(44100, 16, 1);
    private byte? _carry;
    private bool _complete;

    public IEnumerable<PcmBlock> Decode(ReadOnlyMemory<byte> encoded)
    {
        if (_complete) throw new InvalidOperationException("PCM decoding has completed.");
        var offset = 0;
        if (_carry is { } first && encoded.Length != 0)
        {
            _carry = null;
            yield return new PcmBlock(new byte[] { first, encoded.Span[0] }, Format);
            offset = 1;
        }
        var aligned = (encoded.Length - offset) & ~1;
        if (aligned != 0) yield return new PcmBlock(encoded.Slice(offset, aligned), Format);
        if (offset + aligned < encoded.Length) _carry = encoded.Span[^1];
    }

    public void Complete()
    {
        _complete = true;
        if (_carry is not null) throw new InvalidDataException("PCM ended in an incomplete 16-bit sample.");
    }

    public void Dispose() { _complete = true; _carry = null; }
}
