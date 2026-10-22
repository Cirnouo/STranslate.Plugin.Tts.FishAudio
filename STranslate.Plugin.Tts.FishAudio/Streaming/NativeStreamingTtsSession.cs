using System.Buffers;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Playback;

namespace STranslate.Plugin.Tts.FishAudio.Streaming;

internal sealed class NativeStreamingTtsSession : IStreamingTtsSession
{
    private readonly Func<SynthesisRequest, IFishAudioSocket> _socketFactory;
    private readonly Func<IStreamingAudioPlayer> _playerFactory;
    private readonly TimeProvider _clock;
    private readonly Action<StreamingObservation>? _observer;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private bool _running;
    private int _used;
    private int _disposed;

    internal NativeStreamingTtsSession(
        Func<SynthesisRequest, IFishAudioSocket>? socketFactory = null,
        Func<IStreamingAudioPlayer>? playerFactory = null,
        TimeProvider? timeProvider = null,
        Action<StreamingObservation>? observer = null)
    {
        _socketFactory = socketFactory ?? (request => new FishAudioSocket(request));
        _playerFactory = playerFactory ?? (() => new StreamingAudioPlayer());
        _clock = timeProvider ?? TimeProvider.System;
        _observer = observer;
    }

    public Task PlayAsync(SynthesisRequest request, string text, CancellationToken cancellationToken) =>
        PlayAsync(request, new StringStreamingTextSource(text), cancellationToken);

    internal async Task PlayAsync(SynthesisRequest request, IStreamingTextSource source, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (_used != 0) throw new InvalidOperationException("A streaming session can synthesize only one request.");
            _used = 1;
            _running = true;
        }
        var activity = new StreamingActivity(_clock);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = cancellation.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            ValidateRequest(request);
            using var socket = _socketFactory(request);
            using var abortOnCancellation = token.Register(() => Abort(socket));
            await ConnectAsync(socket, token).ConfigureAwait(false);
            var connectedAt = _clock.GetTimestamp();
            activity.ReportStage(StreamingStage.Connected);
            IStreamingAudioPlayer player;
            try { player = _playerFactory(); }
            catch { Abort(socket); throw; }
            using var ownedPlayer = player;
            var state = new SessionState(socket, cancellation, activity, connectedAt);
            var channel = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(32)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
            });

            // Every worker is owned and observed here. Receiver and player start before
            // the sender, so first audio can arrive while the input tail is still unread.
            var receiver = GuardAsync(() => ReceiveAsync(state, channel.Writer, token), state, token);
            var audio = GuardAsync(async () =>
            {
                await player.PlayAsync(channel.Reader.ReadAllAsync(token), request.AudioFormat, () =>
                {
                    activity.ReportStage(StreamingStage.PlaybackStarted);
                    Interlocked.Exchange(ref state.LastAudioActiveTicks, activity.ActiveElapsed.Ticks);
                    state.PlaybackStarted.TrySetResult();
                }, activity, token).ConfigureAwait(false);
                if (!state.Finished.Task.IsCompletedSuccessfully || !activity.FirstPlayablePcm.IsCompletedSuccessfully
                    || !state.PlaybackStarted.Task.IsCompletedSuccessfully)
                    throw new InvalidDataException("Fish Audio streaming ended without playable audio.");
                activity.ReportStage(StreamingStage.Drained);
            }, state, token);
            var sender = GuardAsync(() => SendAsync(request, source, state, token), state, token);
            var watchdog = GuardAsync(() => WatchAsync(request, state, token), state, token);
            await Task.WhenAll(receiver, audio, sender, watchdog).ConfigureAwait(false);
            state.Failure?.Throw();
            token.ThrowIfCancellationRequested();
        }
        finally
        {
            lock (_gate)
            {
                _running = false;
                if (_disposed != 0) _lifetime.Dispose();
            }
            if (_observer is not null)
                foreach (var observation in activity.Observations)
                    try { _observer(observation); } catch { /* Diagnostics cannot alter playback. */ }
        }
    }

    private void ValidateRequest(SynthesisRequest request)
    {
        ValidateModel(request);
        if (request.AudioFormat is not "pcm" and not "mp3")
            throw new InvalidOperationException("The Fish Audio streaming audio format is unsupported.");
    }

    private void ValidateModel(SynthesisRequest request)
    {
        // The provider silently selects a paid default for a missing or unknown model.
        // Exact policy membership is therefore a pre-connection invariant, never a fallback.
        if (!FishAudioModelPolicy.GetAvailableModels(_clock.GetUtcNow()).Contains(request.Model, StringComparer.Ordinal))
            throw new InvalidOperationException("The selected Fish Audio streaming model is unavailable.");
    }

    private async Task ConnectAsync(IFishAudioSocket socket, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15), _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        using var abort = linked.Token.Register(() => Abort(socket));
        try { await socket.ConnectAsync(linked.Token).ConfigureAwait(false); }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        catch (Exception) when (deadline.IsCancellationRequested) { throw new TimeoutException("Fish Audio streaming connection timed out."); }
        catch { Abort(socket); throw; }
    }

    private async Task SendAsync(SynthesisRequest request, IStreamingTextSource source, SessionState state, CancellationToken token)
    {
        async ValueTask Send(ReadOnlyMemory<byte> message)
        {
            token.ThrowIfCancellationRequested();
            ValidateModel(request); // A long free session cannot cross the UTC cutoff.
            try { await state.Socket.SendBinaryAsync(message, token).ConfigureAwait(false); }
            catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        }
        await Send(FishAudioStreamingProtocol.Start(request)).ConfigureAwait(false);
        var reader = new StreamingTextReader(source);
        var firstWindow = true;
        var prefixSent = false;
        while (reader.HasRemaining)
        {
            token.ThrowIfCancellationRequested();
            var chunk = reader.ReadNext(firstWindow ? 100 : 300);
            firstWindow = false;
            await Send(FishAudioStreamingProtocol.Text(chunk.Text)).ConfigureAwait(false);
            if (chunk.FlushSafe)
            {
                await Send(FishAudioStreamingProtocol.Control("flush")).ConfigureAwait(false);
                if (!prefixSent)
                {
                    prefixSent = true;
                    state.Activity.ReportStage(StreamingStage.PrefixSent);
                    if (!chunk.IsFinal) await state.PlaybackStarted.Task.WaitAsync(token).ConfigureAwait(false);
                }
            }
        }
        Interlocked.Exchange(ref state.StopSent, 1);
        await Send(FishAudioStreamingProtocol.Control("stop")).ConfigureAwait(false);
    }

    private async Task ReceiveAsync(SessionState state, ChannelWriter<ReadOnlyMemory<byte>> writer, CancellationToken token)
    {
        var message = new byte[FishAudioStreamingProtocol.MaximumMessageBytes];
        var fragment = new byte[FishAudioStreamingProtocol.AudioBlockBytes];
        var length = 0;
        while (true)
        {
                ValueWebSocketReceiveResult result;
                try { result = await state.Socket.ReceiveAsync(fragment, token).ConfigureAwait(false); }
                catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new InvalidDataException("Fish Audio streaming closed before a successful finish.");
                if (result.MessageType != WebSocketMessageType.Binary || result.Count > message.Length - length)
                    throw new InvalidDataException("Fish Audio returned an invalid or oversized streaming message.");
                fragment.AsMemory(0, result.Count).CopyTo(message.AsMemory(length));
                length += result.Count;
                if (!result.EndOfMessage) continue;
                var received = FishAudioStreamingProtocol.Decode(message.AsMemory(0, length));
                length = 0;
                switch (received.Event)
                {
                    case "audio":
                        if (received.Audio.Length == 0) break;
                        state.Activity.ReportStage(StreamingStage.FirstAudio);
                        Interlocked.Exchange(ref state.LastAudioActiveTicks, state.Activity.ActiveElapsed.Ticks);
                        var audio = received.Audio;
                        while (!audio.IsEmpty)
                        {
                            var count = (int)Math.Min(audio.Length, FishAudioStreamingProtocol.AudioBlockBytes);
                            var owned = audio.Slice(0, count).ToArray();
                            audio = audio.Slice(count);
                            if (!writer.TryWrite(owned))
                            {
                                using var pressure = state.Activity.EnterBackpressure(StreamingBackpressure.EncodedQueue);
                                await writer.WriteAsync(owned, token).ConfigureAwait(false);
                            }
                        }
                        break;
                    case "finish":
                        if (received.Reason != "stop" || Volatile.Read(ref state.StopSent) == 0)
                            throw new InvalidDataException("Fish Audio streaming returned an unsuccessful or unexpected finish.");
                        state.Activity.ReportStage(StreamingStage.Finish);
                        state.Finished.TrySetResult();
                        writer.TryComplete();
                        return;
                    default:
                        throw new InvalidDataException("Fish Audio streaming reported a remote error.");
                }
        }
    }

    private async Task WatchAsync(SynthesisRequest request, SessionState state, CancellationToken token)
    {
        while (!state.Finished.Task.IsCompleted)
        {
            token.ThrowIfCancellationRequested();
            ValidateModel(request);
            if (!state.Activity.FirstPlayablePcm.IsCompletedSuccessfully
                && _clock.GetElapsedTime(state.ConnectedAt) >= TimeSpan.FromSeconds(120))
                throw new TimeoutException("Fish Audio streaming produced no playable PCM within 120 seconds.");
            if (state.PlaybackStarted.Task.IsCompletedSuccessfully
                && state.Activity.ActiveElapsed - TimeSpan.FromTicks(Interlocked.Read(ref state.LastAudioActiveTicks)) >= TimeSpan.FromSeconds(30))
                throw new TimeoutException("Fish Audio streaming received no audio for 30 active seconds.");
            try { await state.Finished.Task.WaitAsync(TimeSpan.FromMilliseconds(100), _clock, token).ConfigureAwait(false); }
            catch (TimeoutException) { }
        }
    }

    private static async Task GuardAsync(Func<Task> worker, SessionState state, CancellationToken token)
    {
        try { await worker().ConfigureAwait(false); }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException || !token.IsCancellationRequested)
                Interlocked.CompareExchange(ref state.Failure, ExceptionDispatchInfo.Capture(ex), null);
            Abort(state.Socket);
            try { state.Cancellation.Cancel(); }
            catch (AggregateException) { /* A cancellation callback cannot replace the recorded terminal cause. */ }
        }
    }

    private static void Abort(IFishAudioSocket socket)
    {
        try { socket.Abort(); } catch { /* Cleanup cannot replace the original failure. */ }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed != 0) return;
            _disposed = 1;
            try { _lifetime.Cancel(); }
            finally { if (!_running) _lifetime.Dispose(); }
        }
    }

    private sealed class SessionState(IFishAudioSocket socket, CancellationTokenSource cancellation, StreamingActivity activity, long connectedAt)
    {
        internal IFishAudioSocket Socket { get; } = socket;
        internal CancellationTokenSource Cancellation { get; } = cancellation;
        internal StreamingActivity Activity { get; } = activity;
        internal long ConnectedAt { get; } = connectedAt;
        internal TaskCompletionSource PlaybackStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal long LastAudioActiveTicks = activity.ActiveElapsed.Ticks;
        internal int StopSent;
        internal ExceptionDispatchInfo? Failure;
    }
}
