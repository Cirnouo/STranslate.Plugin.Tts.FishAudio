using NAudio.Wave;
using System.IO;
using System.Runtime.ExceptionServices;

namespace STranslate.Plugin.Tts.FishAudio.Playback;

internal sealed class WaveOutStreamingAudioOutput : IStreamingAudioOutput
{
    private readonly object _gate = new();
    private readonly IWavePlayer _waveOut;
    private readonly Func<long> _getPosition;
    private readonly CancellationTokenSource _failure = new();
    private Exception? _error;
    private bool _intentionalStop;
    private bool _disposed;
    private int _failureCallbacks;
    private bool _positionInitialized;
    private uint _lastPosition;
    private long _played;

    internal WaveOutStreamingAudioOutput() : this(CreateWaveOut()) { }
    private WaveOutStreamingAudioOutput(WaveOutEvent waveOut) : this(waveOut, waveOut.GetPosition) { }

    internal WaveOutStreamingAudioOutput(IWavePlayer waveOut, Func<long> getPosition)
    {
        _waveOut = waveOut;
        _getPosition = getPosition;
        waveOut.PlaybackStopped += OnPlaybackStopped;
    }

    public CancellationToken FailureToken { get { lock (_gate) return _failure.Token; } }
    public void Init(IWaveProvider provider) => _waveOut.Init(provider);
    public void Play() => _waveOut.Play();

    public long GetPlayedBytes()
    {
        lock (_gate)
        {
            // WinMM's TIME_BYTES position wraps at 2^32, including in a long session.
            var current = unchecked((uint)_getPosition());
            _played = _positionInitialized ? checked(_played + unchecked(current - _lastPosition)) : current;
            _lastPosition = current;
            _positionInitialized = true;
            return _played;
        }
    }

    public void ThrowIfFailed()
    {
        if (Volatile.Read(ref _error) is { } error) ExceptionDispatchInfo.Capture(error).Throw();
    }

    public void Stop()
    {
        lock (_gate) _intentionalStop = true;
        _waveOut.Stop();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _intentionalStop = true;
        }
        try { _waveOut.PlaybackStopped -= OnPlaybackStopped; }
        finally
        {
            try { _waveOut.Dispose(); }
            finally { lock (_gate) if (_failureCallbacks == 0) _failure.Dispose(); }
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs args)
    {
        lock (_gate)
        {
            if (_disposed || _intentionalStop) return;
            _error ??= args.Exception ?? new IOException("The audio device stopped before playback drained.");
            _failureCallbacks++;
        }
        try { _failure.Cancel(); }
        catch { /* A cancellation callback cannot escape the device's event thread. */ }
        finally
        {
            lock (_gate)
            {
                _failureCallbacks--;
                if (_disposed && _failureCallbacks == 0) _failure.Dispose();
            }
        }
    }

    internal static WaveOutEvent CreateWaveOut()
    {
        var context = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            return new WaveOutEvent { DesiredLatency = 200, NumberOfBuffers = 4 };
        }
        finally { SynchronizationContext.SetSynchronizationContext(context); }
    }
}
