using STranslate.Plugin.Tts.FishAudio.Streaming;
using System.IO;
using System.Runtime.ExceptionServices;

namespace STranslate.Plugin.Tts.FishAudio.Playback;

internal sealed class StreamingAudioPlayer : IStreamingAudioPlayer
{
    private readonly object _gate = new();
    private readonly Func<IStreamingAudioOutput> _outputFactory;
    private readonly Func<string, IStreamingPcmDecoder> _decoderFactory;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _used;
    private bool _running;
    private bool _disposed;

    internal StreamingAudioPlayer(Func<IStreamingAudioOutput>? outputFactory = null,
        Func<string, IStreamingPcmDecoder>? decoderFactory = null)
    {
        _outputFactory = outputFactory ?? (() => new WaveOutStreamingAudioOutput());
        _decoderFactory = decoderFactory ?? (format => format switch
        {
            "pcm" => new PcmStreamingDecoder(),
            "mp3" => new IncrementalMp3Decoder(),
            _ => throw new NotSupportedException("Unsupported streaming audio format: " + format),
        });
    }

    public Task PlayAsync(IAsyncEnumerable<ReadOnlyMemory<byte>> chunks, string format,
        Action playbackStarted, StreamingActivity activity, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_used) throw new InvalidOperationException("A streaming audio player supports one session.");
            _used = true;
            _running = true;
        }
        return RunAsync(chunks, format, playbackStarted, activity, cancellationToken);
    }

    private async Task RunAsync(IAsyncEnumerable<ReadOnlyMemory<byte>> chunks, string format,
        Action playbackStarted, StreamingActivity activity, CancellationToken cancellationToken)
    {
        IStreamingPcmDecoder? decoder = null;
        IStreamingAudioOutput? output = null;
        CancellationTokenSource? cancel = null;
        Exception? failure = null;
        try
        {
            decoder = _decoderFactory(format);
            output = _outputFactory();
            cancel = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _lifetime.Token, output.FailureToken);
            var session = new PlaybackSession(output, playbackStarted, activity, cancel);
            try
            {
                // Install the clock/device pump before a synchronously available
                // input can begin decoding a large event.
                var pumping = session.PumpAsync();
                var producing = session.ProduceAsync(chunks, decoder);
                await Task.WhenAll(pumping, producing).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                output.ThrowIfFailed();
                throw;
            }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            // Each owned resource gets an independent cleanup attempt. A release
            // failure is observable only when playback had no earlier failure.
            if (cancel is not null) CaptureCleanupFailure(ref failure, cancel.Dispose);
            if (output is not null)
            {
                CaptureCleanupFailure(ref failure, output.Stop);
                CaptureCleanupFailure(ref failure, output.Dispose);
            }
            if (decoder is not null) CaptureCleanupFailure(ref failure, decoder.Dispose);
            lock (_gate)
            {
                _running = false;
                if (_disposed) _lifetime.Dispose();
            }
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void CaptureCleanupFailure(ref Exception? failure, Action cleanup)
    {
        try { cleanup(); }
        catch (Exception error) { failure ??= error; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            if (!_running) _lifetime.Dispose();
        }
    }

    private sealed class PlaybackSession(IStreamingAudioOutput output, Action playbackStarted,
        StreamingActivity activity, CancellationTokenSource cancel)
    {
        private readonly Pulse _changed = new();
        private readonly TaskCompletionSource _playbackReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private StreamingPcmBuffer? _buffer;
        private long _firstPcm;
        private bool _hasPcm;
        private volatile bool _complete;
        private bool _started;

        internal async Task ProduceAsync(IAsyncEnumerable<ReadOnlyMemory<byte>> chunks, IStreamingPcmDecoder decoder)
        {
            IAsyncEnumerator<ReadOnlyMemory<byte>>? enumerator = null;
            Task<bool>? pending = null;
            Exception? failure = null;
            try
            {
                enumerator = chunks.GetAsyncEnumerator(cancel.Token);
                while (await (pending = enumerator.MoveNextAsync().AsTask()).WaitAsync(cancel.Token).ConfigureAwait(false))
                {
                    cancel.Token.ThrowIfCancellationRequested();
                    var chunk = enumerator.Current;
                    foreach (var block in decoder.Decode(chunk))
                    {
                        cancel.Token.ThrowIfCancellationRequested();
                        if (block.Bytes.IsEmpty) continue;
                        if (block.Format.BitsPerSample != 16 || block.Bytes.Length % block.Format.BlockAlign != 0)
                            throw new InvalidDataException("Decoder returned unaligned PCM samples.");
                        var buffer = _buffer;
                        if (buffer is null)
                        {
                            buffer = new StreamingPcmBuffer(block.Format, _changed.Signal);
                            _firstPcm = activity.Clock.GetTimestamp();
                            Volatile.Write(ref _buffer, buffer);
                        }
                        else if (!buffer.WaveFormat.Equals(block.Format))
                            throw new InvalidDataException("Streaming PCM format changed during playback.");

                        var offset = 0;
                        while (offset < block.Bytes.Length)
                        {
                            if (buffer.BufferedBytes >= buffer.WaveFormat.AverageBytesPerSecond * 4)
                            {
                                // The lease exists only while waiting, so startup/decode time stays active.
                                IDisposable? lease = null;
                                try
                                {
                                    while (true)
                                    {
                                        var changed = _changed.WaitTask;
                                        if (buffer.BufferedBytes <= buffer.WaveFormat.AverageBytesPerSecond * 3) break;
                                        lease ??= activity.EnterBackpressure(StreamingBackpressure.PcmBuffer);
                                        await changed.WaitAsync(cancel.Token).ConfigureAwait(false);
                                    }
                                }
                                finally { lease?.Dispose(); }
                            }
                            cancel.Token.ThrowIfCancellationRequested();
                            var count = Math.Min(16384, block.Bytes.Length - offset);
                            if (!_playbackReady.Task.IsCompleted)
                                count = Math.Min(count, Math.Max(buffer.WaveFormat.BlockAlign,
                                    StartupBytes(buffer) - buffer.BufferedBytes));
                            count -= count % buffer.WaveFormat.BlockAlign;
                            var written = buffer.Write(block.Bytes.Slice(offset, count));
                            if (written == 0) throw new InvalidOperationException("PCM ring failed to accept an aligned write.");
                            offset += written;
                            if (!_hasPcm)
                            {
                                _hasPcm = true;
                                activity.ReportFirstPlayablePcm();
                            }
                            _changed.Signal();
                            if (!_playbackReady.Task.IsCompleted && ShouldStart(buffer))
                                await _playbackReady.Task.WaitAsync(cancel.Token).ConfigureAwait(false);
                        }
                    }
                }
                decoder.Complete();
                if (!_hasPcm) throw new InvalidDataException("Streaming audio contained no playable PCM.");
                _complete = true;
                _changed.Signal();
            }
            catch (Exception error)
            {
                failure = error;
                CaptureCleanupFailure(ref failure, cancel.Cancel);
            }
            finally
            {
                // An input that ignores its token must not hold the device or PCM
                // resources alive. Dispose that enumerator once its MoveNext returns.
                if (enumerator is not null)
                {
                    if (pending is { IsCompleted: false }) _ = DisposeInputLaterAsync(enumerator, pending);
                    else
                    {
                        try { await DisposeInputAsync(enumerator, cancel.Token).ConfigureAwait(false); }
                        catch (Exception error) { failure ??= error; }
                    }
                }
                if (failure is not null) CaptureCleanupFailure(ref failure, cancel.Cancel);
            }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static int StartupBytes(StreamingPcmBuffer buffer)
        {
            var align = buffer.WaveFormat.BlockAlign;
            return ((buffer.WaveFormat.AverageBytesPerSecond + 3) / 4 + align - 1) / align * align;
        }

        private bool ShouldStart(StreamingPcmBuffer buffer) => buffer.BufferedBytes >= StartupBytes(buffer)
            || _complete || activity.Clock.GetElapsedTime(_firstPcm) >= TimeSpan.FromMilliseconds(100);

        private static async Task DisposeInputAsync(IAsyncEnumerator<ReadOnlyMemory<byte>> enumerator, CancellationToken token)
        {
            var disposal = enumerator.DisposeAsync().AsTask();
            try { await disposal.WaitAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!disposal.IsCompleted)
            {
                _ = ObserveInputDisposalAsync(disposal);
                throw;
            }
        }

        private static async Task ObserveInputDisposalAsync(Task disposal)
        {
            try { await disposal.ConfigureAwait(false); }
            catch { /* Observe cleanup that outlived an interrupted input operation. */ }
        }

        private static async Task DisposeInputLaterAsync(IAsyncEnumerator<ReadOnlyMemory<byte>> enumerator, Task<bool> pending)
        {
            try { await pending.ConfigureAwait(false); }
            catch { /* The operation has already been interrupted; observe the abandoned read. */ }
            try { await enumerator.DisposeAsync().ConfigureAwait(false); }
            catch { /* Late input cleanup cannot replace the session's original result. */ }
        }

        internal async Task PumpAsync()
        {
            var clock = activity.Clock;
            using var timer = clock.CreateTimer(_ => _changed.Signal(), null,
                TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10));
            var lastPlayed = 0L;
            var lastProgress = clock.GetTimestamp();
            var hadOutstandingData = false;
            long? drainTail = null;
            try
            {
                while (true)
                {
                    var changed = _changed.WaitTask;
                    cancel.Token.ThrowIfCancellationRequested();
                    output.ThrowIfFailed();
                    var buffer = Volatile.Read(ref _buffer);
                    if (buffer is not null)
                    {
                        if (!_started && ShouldStart(buffer))
                        {
                            cancel.Token.ThrowIfCancellationRequested();
                            output.Init(buffer);
                            cancel.Token.ThrowIfCancellationRequested();
                            output.Play();
                            output.ThrowIfFailed();
                            cancel.Token.ThrowIfCancellationRequested();
                            _started = true;
                            lastProgress = clock.GetTimestamp();
                            playbackStarted();
                            activity.ReportStage(StreamingStage.PlaybackStarted);
                            _playbackReady.TrySetResult();
                        }
                        if (_started)
                        {
                            var played = output.GetPlayedBytes();
                            var outstanding = buffer.BufferedBytes != 0 || buffer.LastRealPcmEnd > played;
                            if (!outstanding || !hadOutstandingData || played > lastPlayed)
                                lastProgress = clock.GetTimestamp();
                            else if (clock.GetElapsedTime(lastProgress) >= TimeSpan.FromSeconds(10))
                                throw new TimeoutException("Audio device made no playback progress for 10 seconds.");
                            hadOutstandingData = outstanding;
                            lastPlayed = played;
                            if (_complete && drainTail is null && buffer.TryGetDrainTail(out var tail)) drainTail = tail;
                            if (drainTail is { } finalTail && played >= finalTail)
                            {
                                activity.ReportStage(StreamingStage.Drained);
                                return;
                            }
                        }
                    }
                    await changed.WaitAsync(cancel.Token).ConfigureAwait(false);
                }
            }
            catch
            {
                try { cancel.Cancel(); }
                catch { /* A cancellation callback cannot replace the pump's original failure. */ }
                throw;
            }
        }
    }

    private sealed class Pulse
    {
        private readonly object _gate = new();
        private TaskCompletionSource _next = NewSource();
        internal Task WaitTask { get { lock (_gate) return _next.Task; } }
        internal void Signal()
        {
            TaskCompletionSource previous;
            lock (_gate) { previous = _next; _next = NewSource(); }
            previous.TrySetResult();
        }
        private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
