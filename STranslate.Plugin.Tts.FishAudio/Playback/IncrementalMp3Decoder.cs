using NAudio.Wave;
using System.IO;

namespace STranslate.Plugin.Tts.FishAudio.Playback;

internal sealed class IncrementalMp3Decoder : IStreamingPcmDecoder
{
    private const int MaximumMetadataBytes = 1024 * 1024;
    private const int MaximumDecodedFrameBytes = 16384 * 4;
    private readonly Func<WaveFormat, IMp3FrameDecompressor> _decompressorFactory;
    private byte[] _carry = new byte[10];
    private int _count;
    private int _metadataRemaining;
    private bool _trailingTag;
    private bool _sawFrame;
    private bool _complete;
    private bool _disposed;
    private int _sampleRate;
    private int _channels;
    private IMp3FrameDecompressor? _decompressor;

    internal IncrementalMp3Decoder() : this(format => new AcmMp3FrameDecompressor(format)) { }
    internal IncrementalMp3Decoder(Func<WaveFormat, IMp3FrameDecompressor> decompressorFactory)
        => _decompressorFactory = decompressorFactory;

    // Decoding advances by one frame per MoveNext. The caller awaits that frame's
    // bounded PCM write before requesting another, even for one huge server event.
    public IEnumerable<PcmBlock> Decode(ReadOnlyMemory<byte> encoded)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_complete) throw new InvalidOperationException("MP3 decoding has completed.");
        var offset = 0;
        while (true)
        {
            if (_metadataRemaining != 0)
            {
                var skip = Math.Min(_metadataRemaining, encoded.Length - offset);
                offset += skip;
                _metadataRemaining -= skip;
                if (_metadataRemaining != 0) yield break;
            }
            if (_trailingTag)
            {
                if (offset != encoded.Length) throw new InvalidDataException("Unexpected data after the MP3 ID3v1 tag.");
                yield break;
            }
            if (!Append(encoded, ref offset, 3)) yield break;
            if (!_sawFrame && _carry[0] == 'I' && _carry[1] == 'D' && _carry[2] == '3')
            {
                if (!Append(encoded, ref offset, 10)) yield break;
                if (_carry[3] is < 2 or > 4 || (_carry[6] | _carry[7] | _carry[8] | _carry[9]) >= 128)
                    throw new InvalidDataException("Invalid MP3 ID3v2 header.");
                var size = (_carry[6] << 21) | (_carry[7] << 14) | (_carry[8] << 7) | _carry[9];
                if (_carry[3] == 4 && (_carry[5] & 0x10) != 0) size += 10; // ID3v2.4 footer.
                if (size > MaximumMetadataBytes) throw new InvalidDataException("MP3 metadata exceeded 1 MiB.");
                _metadataRemaining = size;
                _count = 0;
                continue;
            }
            if (_sawFrame && _carry[0] == 'T' && _carry[1] == 'A' && _carry[2] == 'G')
            {
                if (!Append(encoded, ref offset, 128)) yield break;
                _count = 0;
                _trailingTag = true;
                continue;
            }
            if (!Append(encoded, ref offset, 4)) yield break;
            var frameLength = GetFrameLength(_carry);
            if (!Append(encoded, ref offset, frameLength)) yield break;
            using var stream = new MemoryStream(_carry, 0, frameLength, writable: false);
            var frame = Mp3Frame.LoadFromStream(stream)
                ?? throw new InvalidDataException("Invalid MP3 frame.");
            _count = 0;
            var block = DecodeFrame(frame);
            if (!block.Bytes.IsEmpty) yield return block;
        }
    }

    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _complete = true;
        if (!_sawFrame) throw new InvalidDataException("MP3 contained no complete audio frame.");
        if (_count != 0 || _metadataRemaining != 0)
            throw new InvalidDataException("MP3 ended with incomplete frame or metadata.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _carry = [];
        _decompressor?.Dispose();
        _decompressor = null;
    }

    private bool Append(ReadOnlyMemory<byte> encoded, ref int offset, int wanted)
    {
        if (_count >= wanted) return true;
        if (_carry.Length < wanted) Array.Resize(ref _carry, wanted);
        var take = Math.Min(wanted - _count, encoded.Length - offset);
        encoded.Span.Slice(offset, take).CopyTo(_carry.AsSpan(_count));
        _count += take;
        offset += take;
        return _count == wanted;
    }

    private PcmBlock DecodeFrame(Mp3Frame frame)
    {
        var channels = frame.ChannelMode == ChannelMode.Mono ? 1 : 2;
        if (_decompressor is null)
        {
            _sampleRate = frame.SampleRate;
            _channels = channels;
            _decompressor = _decompressorFactory(new Mp3WaveFormat(frame.SampleRate, channels, frame.FrameLength, frame.BitRate));
        }
        else if (frame.SampleRate != _sampleRate || channels != _channels)
            throw new InvalidDataException("MP3 changed sample rate or channel count.");
        var pcm = new byte[MaximumDecodedFrameBytes];
        var bytes = _decompressor.DecompressFrame(frame, pcm, 0);
        if (bytes < 0 || bytes > pcm.Length || bytes % _decompressor.OutputFormat.BlockAlign != 0)
            throw new InvalidDataException("MP3 decoder returned an invalid PCM length.");
        _sawFrame = true;
        return new PcmBlock(pcm.AsMemory(0, bytes), _decompressor.OutputFormat);
    }

    private static int GetFrameLength(byte[] header)
    {
        var version = (header[1] >> 3) & 3;
        var layer = (header[1] >> 1) & 3;
        var rate = header[2] >> 4;
        var sample = (header[2] >> 2) & 3;
        if (header[0] != 0xFF || (header[1] & 0xE0) != 0xE0 || version == 1 || layer != 1
            || rate is 0 or 15 || sample == 3)
            throw new InvalidDataException("Invalid MPEG Layer III frame header.");
        ReadOnlySpan<int> mpeg1Rates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
        ReadOnlySpan<int> mpeg2Rates = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
        ReadOnlySpan<int> samples = [44100, 48000, 32000];
        var sampleRate = samples[sample] / (version == 3 ? 1 : version == 2 ? 2 : 4);
        var bitRate = (version == 3 ? mpeg1Rates[rate] : mpeg2Rates[rate]) * 1000;
        return (version == 3 ? 144 : 72) * bitRate / sampleRate + ((header[2] >> 1) & 1);
    }
}
