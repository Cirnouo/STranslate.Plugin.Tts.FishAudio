using NAudio.Wave;

namespace STranslate.Plugin.Tts.FishAudio.Playback;

// A bounded software ring. Its device cursor includes silence returned on an underrun.
internal sealed class StreamingPcmBuffer : IWaveProvider
{
    private readonly object _gate = new();
    private readonly byte[] _bytes;
    private readonly Action _changed;
    private int _read;
    private int _count;
    private long _sourceCursor;
    private long _lastRealPcmEnd;

    internal StreamingPcmBuffer(WaveFormat format, Action changed)
    {
        WaveFormat = format;
        _changed = changed;
        _bytes = new byte[checked(format.AverageBytesPerSecond * 5)];
    }

    public WaveFormat WaveFormat { get; }
    internal int BufferedBytes { get { lock (_gate) return _count; } }
    internal long LastRealPcmEnd { get { lock (_gate) return _lastRealPcmEnd; } }

    internal int Write(ReadOnlyMemory<byte> pcm)
    {
        int count;
        lock (_gate)
        {
            count = Math.Min(pcm.Length, _bytes.Length - _count);
            count -= count % WaveFormat.BlockAlign;
            var write = (_read + _count) % _bytes.Length;
            var first = Math.Min(count, _bytes.Length - write);
            pcm.Span[..first].CopyTo(_bytes.AsSpan(write));
            pcm.Span.Slice(first, count - first).CopyTo(_bytes);
            _count += count;
        }
        if (count != 0) _changed();
        return count;
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        lock (_gate)
        {
            var actual = Math.Min(count, _count);
            var first = Math.Min(actual, _bytes.Length - _read);
            _bytes.AsSpan(_read, first).CopyTo(buffer.AsSpan(offset));
            _bytes.AsSpan(0, actual - first).CopyTo(buffer.AsSpan(offset + first));
            Array.Clear(buffer, offset + actual, count - actual);
            _read = (_read + actual) % _bytes.Length;
            _count -= actual;
            if (actual != 0) _lastRealPcmEnd = checked(_sourceCursor + actual);
            _sourceCursor = checked(_sourceCursor + count);
        }
        _changed();
        return count;
    }

    internal bool TryGetDrainTail(out long tail)
    {
        lock (_gate) { tail = _lastRealPcmEnd; return _count == 0; }
    }
}
