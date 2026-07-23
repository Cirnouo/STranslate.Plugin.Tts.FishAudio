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

public sealed class LimitGuardStream : Stream
{
    private readonly long _length;
    private readonly long _maxAllowedReadBytes;
    private long _position;

    public LimitGuardStream(long length, long maxAllowedReadBytes)
    {
        _length = length;
        _maxAllowedReadBytes = maxAllowedReadBytes;
    }

    public long TotalBytesRead { get; private set; }
    public int ReadCallCount { get; private set; }
    public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_position >= _length)
            return 0;

        var remainingBeforeLimit = _maxAllowedReadBytes - TotalBytesRead;
        if (remainingBeforeLimit <= 0)
            throw new InvalidOperationException("Stream was read past the configured cache limit.");

        var count = (int)Math.Min(buffer.Length, Math.Min(_length - _position, remainingBeforeLimit));
        FillImageLikeBytes(buffer[..count], _position);
        _position += count;
        TotalBytesRead += count;
        ReadCallCount++;
        return count;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(buffer.Span));
    }

    protected override void Dispose(bool disposing)
    {
        Disposed.TrySetResult();
        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        Disposed.TrySetResult();
        return base.DisposeAsync();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private static void FillImageLikeBytes(Span<byte> buffer, long absoluteOffset)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var sourceIndex = absoluteOffset + i;
            buffer[i] = sourceIndex switch
            {
                0 => 0xFF,
                1 => 0xD8,
                2 => 0xFF,
                3 => 0xE0,
                _ => 0x20,
            };
        }
    }
}
