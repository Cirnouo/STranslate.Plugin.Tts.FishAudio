using NAudio.Wave;
using STranslate.Plugin.Tts.FishAudio.Playback;
using System.Threading.Channels;

internal sealed class StreamingAudioInput
{
    private readonly Channel<ReadOnlyMemory<byte>> _channel = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
    internal IAsyncEnumerable<ReadOnlyMemory<byte>> Chunks => _channel.Reader.ReadAllAsync();
    internal void Send(byte[] bytes) => _channel.Writer.TryWrite(bytes);
    internal void Complete() => _channel.Writer.TryComplete();
}

// Replaces only the physical driver. The production ring and timing logic run unchanged.
internal sealed class StreamingAudioOutputDouble : IStreamingAudioOutput
{
    private readonly CancellationTokenSource _failure = new();
    private Exception? _error;
    private IWaveProvider? _provider;
    private long _played;
    internal int PlayCount;
    internal int InitCount;
    internal int StopCount;
    internal int DisposeCount;
    internal bool FailInit;
    internal bool FailPlay;
    internal Exception? PlayFailure;
    internal Exception? StopFailure;
    internal Exception? DisposeFailure;
    internal Action? OnPlay;
    internal readonly List<byte> DeviceBytes = [];
    internal int BufferedBytes => (_provider as StreamingPcmBuffer)?.BufferedBytes ?? 0;
    public CancellationToken FailureToken => _failure.Token;
    public void Init(IWaveProvider provider)
    {
        if (FailInit) throw new IOException("init failed");
        _provider = provider;
        Interlocked.Increment(ref InitCount);
    }
    public void Play()
    {
        if (PlayFailure is { } error) throw error;
        if (FailPlay) throw new IOException("play failed");
        OnPlay?.Invoke();
        Interlocked.Increment(ref PlayCount);
    }
    public long GetPlayedBytes() => Interlocked.Read(ref _played);
    public void ThrowIfFailed() { if (_error is { } error) throw error; }
    public void Stop()
    {
        Interlocked.Increment(ref StopCount);
        if (StopFailure is { } error) throw error;
    }
    public void Dispose()
    {
        Interlocked.Increment(ref DisposeCount);
        if (DisposeFailure is { } error) throw error;
    }
    internal byte[] Read(int bytes)
    {
        var buffer = new byte[bytes];
        var count = _provider!.Read(buffer, 0, bytes);
        if (count != bytes) throw new InvalidOperationException("Streaming source must pad underrun reads.");
        lock (DeviceBytes) DeviceBytes.AddRange(buffer);
        return buffer;
    }
    internal void SetPlayed(long bytes) => Interlocked.Exchange(ref _played, bytes);
    internal void Fail(Exception? error = null)
    {
        _error = error ?? new IOException("unexpected device stop");
        _failure.Cancel();
    }
}

internal sealed class StreamingWavePlayerDouble : IWavePlayer
{
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;
    public PlaybackState PlaybackState { get; private set; }
    public float Volume { get; set; }
    public WaveFormat OutputWaveFormat { get; } = new(44100, 16, 1);
    internal long Position;
    public void Init(IWaveProvider provider) { }
    public void Play() => PlaybackState = PlaybackState.Playing;
    public void Pause() => PlaybackState = PlaybackState.Paused;
    public void Stop() { PlaybackState = PlaybackState.Stopped; PlaybackStopped?.Invoke(this, new StoppedEventArgs()); }
    public void Dispose() { }
    internal void UnexpectedStop(Exception? error = null)
    {
        PlaybackState = PlaybackState.Stopped;
        PlaybackStopped?.Invoke(this, new StoppedEventArgs(error));
    }
}

internal sealed class StreamingMp3FrameDecoderDouble : IMp3FrameDecompressor
{
    internal readonly List<int> BitRates = [];
    internal int Calls;
    internal int DisposeCount;
    internal int FrameBytes = 4;
    public WaveFormat OutputFormat { get; } = new(44100, 16, 2);
    public int DecompressFrame(Mp3Frame frame, byte[] buffer, int offset)
    {
        Calls++;
        BitRates.Add(frame.BitRate);
        buffer[offset] = (byte)Calls;
        buffer[offset + 1] = 0;
        buffer[offset + 2] = (byte)Calls;
        buffer[offset + 3] = 0;
        return FrameBytes;
    }
    public void Reset() { }
    public void Dispose() => DisposeCount++;
}

internal sealed class UncooperativeStreamingAudioInput : IAsyncEnumerable<ReadOnlyMemory<byte>>, IAsyncEnumerator<ReadOnlyMemory<byte>>
{
    internal readonly TaskCompletionSource<bool> Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool Waiting;
    public ReadOnlyMemory<byte> Current => default;
    public IAsyncEnumerator<ReadOnlyMemory<byte>> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;
    public ValueTask<bool> MoveNextAsync() { Waiting = true; return new(Pending.Task); }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class StreamingCleanupDecoderDouble : IStreamingPcmDecoder
{
    private readonly PcmStreamingDecoder _decoder = new();
    internal Exception? DecodeFailure;
    internal Exception? DisposeFailure;
    internal int DisposeCount;
    public IEnumerable<PcmBlock> Decode(ReadOnlyMemory<byte> bytes)
        => DecodeFailure is { } error ? throw error : _decoder.Decode(bytes);
    public void Complete() => _decoder.Complete();
    public void Dispose()
    {
        DisposeCount++;
        _decoder.Dispose();
        if (DisposeFailure is { } error) throw error;
    }
}

internal sealed class StreamingThrowingDisposeInput(IAsyncEnumerable<ReadOnlyMemory<byte>> source)
    : IAsyncEnumerable<ReadOnlyMemory<byte>>
{
    internal int DisposeCount;
    public IAsyncEnumerator<ReadOnlyMemory<byte>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => new Enumerator(this, source.GetAsyncEnumerator(cancellationToken));

    private sealed class Enumerator(StreamingThrowingDisposeInput owner, IAsyncEnumerator<ReadOnlyMemory<byte>> source)
        : IAsyncEnumerator<ReadOnlyMemory<byte>>
    {
        public ReadOnlyMemory<byte> Current => source.Current;
        public ValueTask<bool> MoveNextAsync() => source.MoveNextAsync();
        public async ValueTask DisposeAsync()
        {
            owner.DisposeCount++;
            await source.DisposeAsync();
            throw new ApplicationException("input dispose failed");
        }
    }
}

internal sealed class StreamingTimedFrameDecoderDouble(ManualStreamingTimeProvider clock) : IStreamingPcmDecoder
{
    internal int Frames;
    public IEnumerable<PcmBlock> Decode(ReadOnlyMemory<byte> encoded)
    {
        for (var i = 0; i < 100; i++)
        {
            Frames++;
            clock.Advance(TimeSpan.FromMilliseconds(25));
            yield return new PcmBlock(new byte[4410], new NAudio.Wave.WaveFormat(44100, 16, 1)); // 50ms PCM.
        }
    }
    public void Complete() { }
    public void Dispose() { }
}

internal sealed class StreamingThrowingCancellationInput(IAsyncEnumerable<ReadOnlyMemory<byte>> source)
    : IAsyncEnumerable<ReadOnlyMemory<byte>>, IDisposable
{
    private CancellationTokenRegistration _registration;
    internal int DisposeCount;
    internal int CancellationCallbacks;
    public IAsyncEnumerator<ReadOnlyMemory<byte>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var enumerator = source.GetAsyncEnumerator(cancellationToken);
        _registration = cancellationToken.Register(() =>
        {
            Interlocked.Increment(ref CancellationCallbacks);
            throw new InvalidOperationException("input cancellation callback failed");
        });
        return new Enumerator(this, enumerator);
    }
    // Keep the callback registered until the whole operation ends; otherwise
    // concurrent iterator cleanup could unregister it before Cancel visits it.
    public void Dispose() => _registration.Dispose();

    private sealed class Enumerator(StreamingThrowingCancellationInput owner,
        IAsyncEnumerator<ReadOnlyMemory<byte>> source)
        : IAsyncEnumerator<ReadOnlyMemory<byte>>
    {
        public ReadOnlyMemory<byte> Current => source.Current;
        public ValueTask<bool> MoveNextAsync() => source.MoveNextAsync();
        public async ValueTask DisposeAsync()
        {
            owner.DisposeCount++;
            await source.DisposeAsync();
        }
    }
}
