using NUnit.Framework;
using System.Net.WebSockets;
using System.Text.Json;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Streaming;
using static TestAssertions;

[TestFixture]
[NonParallelizable]
internal static class StreamingSessionSuite
{
    [Test]
    [Property("FishAudioCaseId", "streaming.session/tail-waits-for-successful-playback-and-finish-waits-for-drain")]
    [Order(43)]
    public static async Task TailWaitsForSuccessfulPlaybackAndFinishWaitsForDrainAsync()
    {
        var socket = new FishAudioSocketDouble();
        var player = new SessionPlayerDouble { HoldStart = true, HoldDrain = true };
        var clock = new ManualStreamingTimeProvider();
        var source = new StreamingTextSuite.ReadSentinelSource(new string('a', 100) + new string('b', 600), 100);
        socket.OnSend = message =>
        {
            if (Event(message) == "flush") socket.QueueAudio([1, 2, 3, 4], 1);
            if (Event(message) == "stop") socket.QueueFinish();
        };
        var observations = new List<StreamingObservation>();
        using var session = new NativeStreamingTtsSession(_ => socket, () => player, clock, observations.Add);
        var operation = session.PlayAsync(Request(), source, CancellationToken.None);
        await player.FirstBlock.Task.WaitAsync(RegressionExecution.Timeout);
        AssertEqual(1, socket.Sent.Count(m => Event(m) == "text"), "Only the first prefix may be sent before playback starts");
        AssertEqual(false, socket.Sent.Any(m => Event(m) == "stop"), "Long input must withhold stop before playback starts");
        AssertEqual(100, source.CharactersRead, "The source tail must remain completely unread");
        source.PermittedCharacters = source.Length;
        player.AllowStart.TrySetResult();
        await player.InputEnded.Task.WaitAsync(RegressionExecution.Timeout);
        clock.Advance(TimeSpan.FromSeconds(121));
        AssertEqual(false, operation.IsCompleted, "Validated finish cannot complete before output drains");
        player.AllowDrain.TrySetResult();
        await operation.WaitAsync(RegressionExecution.Timeout);
        AssertEqual(1, socket.MaximumConcurrentSends, "One session must own exactly one sender");
        AssertEqual(1, socket.MaximumConcurrentReceives, "One session must own exactly one receiver");
        AssertEqual(8, observations.Count, "Telemetry must contain at most the eight static first-stage observations");
        AssertEqual(StreamingStage.Drained, observations[^1].Stage, "Drain is the final successful stage");
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.session/short-input-stops-without-waiting-for-playback")]
    [Order(44)]
    public static async Task ShortInputStopsWithoutWaitingForPlaybackAsync()
    {
        var socket = new FishAudioSocketDouble();
        var player = new SessionPlayerDouble();
        socket.OnSend = m => { if (Event(m) == "stop") { socket.QueueAudio([1, 2]); socket.QueueFinish(); } };
        using var session = new NativeStreamingTtsSession(_ => socket, () => player);
        await session.PlayAsync(Request(), "Hello.", CancellationToken.None).WaitAsync(RegressionExecution.Timeout);
        AssertEnumerableEqual(new[] { "start", "text", "flush", "stop" }, socket.Sent.Select(Event), "Short final text may stop immediately");
        AssertEqual(2L, player.Bytes, "The successful stream must reach the player");
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.session/protocol-rejects-malformed-oversize-and-missing-finish")]
    [Order(45)]
    public static async Task ProtocolRejectsMalformedOversizeAndMissingFinishAsync()
    {
        foreach (var inject in new Action<FishAudioSocketDouble>[]
        {
            s => s.QueueFrame([0xc1]),
            s => s.QueueFrame([1], WebSocketMessageType.Text),
            s => s.QueueFrame([], WebSocketMessageType.Close),
            s => s.QueueFinish("unknown"),
            s => s.QueueFinish("error"),
            s => s.QueueEvent(new() { ["event"] = "error", ["message"] = "provider supplied secret text" }),
            s => s.QueueFrame(new byte[FishAudioStreamingProtocol.MaximumMessageBytes], end: false),
            s => s.QueueEvent(new() { ["event"] = "audio", ["audio"] = "not binary" }),
        })
        {
            var socket = new FishAudioSocketDouble();
            socket.OnSend = m => { if (Event(m) == "stop") { inject(socket); if (!socket.Sent.IsEmpty) socket.QueueFrame([0]); } };
            using var session = new NativeStreamingTtsSession(_ => socket, () => new SessionPlayerDouble());
            var failure = await CaptureFailureAsync(session.PlayAsync(Request(), "short", CancellationToken.None));
            AssertEqual(true, failure is InvalidDataException or IOException, "A bad provider event must fail as a protocol error");
            AssertEqual(true, socket.AbortCount > 0, "Protocol failure must abort the transport");
            AssertEqual(false, failure.Message.Contains("provider supplied", StringComparison.Ordinal), "Provider payloads cannot leak through failures");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.session/fragmented-audio-uses-owned-bounded-ordered-blocks")]
    [Order(46)]
    public static async Task FragmentedAudioUsesOwnedBoundedOrderedBlocksAsync()
    {
        var bytes = Enumerable.Range(0, 120_000).Select(i => (byte)i).ToArray();
        var socket = new FishAudioSocketDouble();
        var player = new SessionPlayerDouble { CaptureBytes = true };
        socket.OnSend = m => { if (Event(m) == "stop") { socket.QueueAudio(bytes, 7); socket.QueueFinish(); } };
        using var session = new NativeStreamingTtsSession(_ => socket, () => player);
        await session.PlayAsync(Request(), "short", CancellationToken.None).WaitAsync(RegressionExecution.Timeout);
        AssertSequenceEqual(bytes, player.Captured.ToArray(), "Fragmentation and queue slicing must preserve exact audio order");
        AssertEqual(true, player.MaximumBlock <= 16 * 1024, "Each owned queue block must be at most 16KiB");
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.session/model-and-request-snapshot-are-exact-before-connect")]
    [Order(47)]
    public static async Task ModelAndRequestSnapshotAreExactBeforeConnectAsync()
    {
        foreach (var model in new[] { "", "typo", "S2.1-PRO" })
        {
            var socket = new FishAudioSocketDouble();
            using var session = new NativeStreamingTtsSession(_ => socket, () => new SessionPlayerDouble());
            await CaptureFailureAsync(session.PlayAsync(Request(model), "hello", CancellationToken.None));
            AssertEqual(0, socket.ConnectCount, "Missing or unknown native models must fail before any connection");
        }
        foreach (var model in new[] { "s1", "s2-pro", "s2.1-pro", "s2.1-pro-free", "drama-3" })
        foreach (var format in new[] { "pcm", "mp3" })
        {
            var socket = new FishAudioSocketDouble();
            socket.OnSend = m => { if (Event(m) == "stop") { socket.QueueAudio([1, 2]); socket.QueueFinish(); } };
            var settings = new Settings { SelectedModel = model, AudioFormat = format, ApiKey = "offline-key", VoiceId = "voice", Mp3Bitrate = 128 };
            var request = new SynthesisRequest(settings);
            settings.SelectedModel = "changed";
            settings.Mp3Bitrate = 192;
            using var session = new NativeStreamingTtsSession(_ => socket, () => new SessionPlayerDouble(), new ManualStreamingTimeProvider());
            await session.PlayAsync(request, "hello", CancellationToken.None).WaitAsync(RegressionExecution.Timeout);
            var body = socket.Sent.First().GetProperty("request");
            AssertEqual("", body.GetProperty("text").GetString(), "Start must never upload the full input");
            AssertEqual(44100, body.GetProperty("sample_rate").GetInt32(), "Native rate must stay explicitly 44.1kHz");
            AssertEqual(format == "mp3", body.TryGetProperty("mp3_bitrate", out var bitrate), "MP3 bitrate must be format-specific");
            if (format == "mp3") AssertEqual(128, bitrate.GetInt32(), "The captured snapshot cannot observe later mutation");
            AssertEqual(FishAudioModelPolicy.SupportsNormalizeLoudness(model), body.GetProperty("prosody").TryGetProperty("normalize_loudness", out _), "Prosody must use explicit model capability");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.session/cancellation-and-faults-abort-every-worker")]
    [Order(48)]
    public static async Task CancellationAndFaultsAbortEveryWorkerAsync()
    {
        foreach (var phase in new[] { "connect", "send-wait", "receive", "prefix", "queue", "drain", "device", "send" })
        {
            var socket = new FishAudioSocketDouble();
            var player = new SessionPlayerDouble { HoldStart = phase is "prefix" or "queue", HoldDrain = phase == "drain" };
            using var cancellation = new CancellationTokenSource();
            if (phase == "connect") socket.ConnectBehavior = token => Task.Delay(Timeout.Infinite, token);
            if (phase == "send-wait") socket.SendBehavior = token => Task.Delay(Timeout.Infinite, token);
            if (phase == "device") player.Failure = new InvalidOperationException("synthetic device failure");
            if (phase == "send") socket.SendFailure = new IOException("synthetic send failure");
            socket.OnSend = m =>
            {
                if (Event(m) == "flush" && phase != "receive") socket.QueueAudio(new byte[phase == "queue" ? 1024 * 1024 : 2]);
                if (Event(m) == "stop" && phase != "receive") socket.QueueFinish();
            };
            using var session = new NativeStreamingTtsSession(_ => socket, () => player);
            var operation = session.PlayAsync(Request(), new string('a', phase is "prefix" or "queue" ? 1000 : 10), cancellation.Token);
            if (phase == "connect") await socket.Connected.Task.WaitAsync(RegressionExecution.Timeout);
            else if (phase == "send-wait") await socket.SendEntered.Task.WaitAsync(RegressionExecution.Timeout);
            else if (phase == "receive") await WaitUntilAsync(() => socket.Sent.Any(m => Event(m) == "stop"));
            else if (phase == "drain") await player.InputEnded.Task.WaitAsync(RegressionExecution.Timeout);
            else if (phase is "prefix" or "queue")
            {
                await player.FirstBlock.Task.WaitAsync(RegressionExecution.Timeout);
                if (phase == "queue") await WaitUntilAsync(() => player.Activity!.IsBackpressured);
            }
            if (phase is not "device" and not "send") cancellation.Cancel();
            var failure = await CaptureFailureAsync(operation);
            AssertEqual(true, socket.AbortCount > 0, "Every local failure/cancellation phase must abort socket waits");
            if (phase == "device") AssertEqual("synthetic device failure", failure.Message, "Device failure must survive sibling cancellation");
            if (phase == "send") AssertEqual("synthetic send failure", failure.Message, "Sender failure must survive sibling cancellation");
            if (phase is not "device" and not "send") AssertEqual(true, failure is OperationCanceledException, "Caller cancellation must remain cancellation");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.session/connect-first-pcm-and-idle-use-independent-deadlines")]
    [Order(49)]
    public static async Task ConnectFirstPcmAndIdleUseIndependentDeadlinesAsync()
    {
        var clock = new ManualStreamingTimeProvider();
        var socket = new FishAudioSocketDouble { ConnectBehavior = token => Task.Delay(Timeout.Infinite, token) };
        using (var session = new NativeStreamingTtsSession(_ => socket, () => new SessionPlayerDouble(), clock))
        {
            var operation = session.PlayAsync(Request(), "short", CancellationToken.None);
            await socket.Connected.Task.WaitAsync(RegressionExecution.Timeout);
            clock.Advance(TimeSpan.FromSeconds(15));
            AssertEqual(true, await CaptureFailureAsync(operation) is TimeoutException, "Connect has its own 15-second deadline");
        }
        clock = new ManualStreamingTimeProvider();
        socket = new FishAudioSocketDouble();
        var player = new SessionPlayerDouble { ReportPcm = false, HoldStart = true };
        socket.OnSend = m => { if (Event(m) == "flush") socket.QueueAudio([1, 2]); };
        using (var session = new NativeStreamingTtsSession(_ => socket, () => player, clock))
        {
            var operation = session.PlayAsync(Request(), new string('a', 1000), CancellationToken.None);
            await player.FirstBlock.Task.WaitAsync(RegressionExecution.Timeout);
            clock.Advance(TimeSpan.FromSeconds(119));
            await Task.Yield();
            AssertEqual(false, operation.IsCompleted, "Encoded audio cannot satisfy or shorten the first-PCM deadline");
            clock.Advance(TimeSpan.FromSeconds(1));
            AssertEqual(true, await CaptureFailureAsync(operation) is TimeoutException, "First playable PCM must arrive within 120 seconds of connection");
        }
        clock = new ManualStreamingTimeProvider();
        socket = new FishAudioSocketDouble();
        player = new SessionPlayerDouble();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var audioSent = false;
        socket.OnSend = m =>
        {
            if (Event(m) == "flush" && !audioSent) { audioSent = true; socket.QueueAudio([1, 2]); }
            if (Event(m) == "stop") stopped.TrySetResult();
        };
        using (var session = new NativeStreamingTtsSession(_ => socket, () => player, clock))
        {
            var operation = session.PlayAsync(Request(), new string('a', 1000), CancellationToken.None);
            await player.Started.Task.WaitAsync(RegressionExecution.Timeout);
            await stopped.Task.WaitAsync(RegressionExecution.Timeout);
            clock.Advance(TimeSpan.FromSeconds(30));
            AssertEqual(true, await CaptureFailureAsync(operation) is TimeoutException, "After playback, non-backpressured idle has its own 30-second deadline");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.session/idle-excludes-union-of-actual-queue-and-pcm-pressure")]
    [Order(50)]
    public static async Task IdleExcludesUnionOfActualQueueAndPcmPressureAsync()
    {
        var clock = new ManualStreamingTimeProvider();
        var socket = new FishAudioSocketDouble();
        var player = new SessionPlayerDouble { HoldAfterStart = true };
        var emitted = false;
        socket.OnSend = m =>
        {
            if (Event(m) == "flush" && !emitted) { emitted = true; socket.QueueAudio([1, 2]); }
        };
        using var session = new NativeStreamingTtsSession(_ => socket, () => player, clock);
        var operation = session.PlayAsync(Request(), new string('a', 1000), CancellationToken.None);
        await player.Started.Task.WaitAsync(RegressionExecution.Timeout);
        // Hold the reader before filling the queue so its pressure write cannot
        // already have completed while the receiver still owns its first lease.
        socket.QueueAudio(new byte[1024 * 1024]);
        await WaitUntilAsync(() => player.Activity!.IsBackpressured);
        clock.Advance(TimeSpan.FromSeconds(10));
        using (player.Activity!.EnterBackpressure(StreamingBackpressure.PcmBuffer))
            clock.Advance(TimeSpan.FromSeconds(20));
        clock.Advance(TimeSpan.FromSeconds(10));
        AssertEqual(TimeSpan.Zero, player.Activity.ActiveElapsed, "Overlapping queue/PCM pressure must exclude their union exactly once");
        AssertEqual(false, operation.IsCompleted, "A blocked bounded queue cannot trigger the network idle watchdog");
        player.AllowContinue.TrySetResult();
        await WaitUntilAsync(() => Interlocked.Read(ref player.Bytes) == 2 + 1024 * 1024 && !player.Activity.IsBackpressured);
        clock.Advance(TimeSpan.FromSeconds(29));
        await Task.Delay(10);
        AssertEqual(false, operation.IsCompleted, "Only active time after pressure releases counts toward idle");
        clock.Advance(TimeSpan.FromSeconds(1));
        AssertEqual(true, await CaptureFailureAsync(operation) is TimeoutException, "Thirty active idle seconds still fail after pressure releases");
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.session/free-cutoff-rejects-before-connect-and-aborts-long-session")]
    [Order(51)]
    public static async Task FreeCutoffRejectsBeforeConnectAndAbortsLongSessionAsync()
    {
        var expiredClock = new OffsetClock(FishAudioModelPolicy.FreeModelCutoffUtc);
        var socket = new FishAudioSocketDouble();
        using (var session = new NativeStreamingTtsSession(_ => socket, () => new SessionPlayerDouble(), expiredClock))
        {
            AssertEqual(true, await CaptureFailureAsync(session.PlayAsync(Request("s2.1-pro-free"), "short", CancellationToken.None)) is InvalidOperationException,
                "Expired free selection must fail rather than select a paid model");
            AssertEqual(0, socket.ConnectCount, "Expired free selection must be rejected before network connection");
        }
        var clock = new OffsetClock(FishAudioModelPolicy.FreeModelCutoffUtc.AddSeconds(-1));
        socket = new FishAudioSocketDouble();
        var player = new SessionPlayerDouble { HoldStart = true };
        socket.OnSend = m => { if (Event(m) == "flush") socket.QueueAudio([1, 2]); };
        using (var session = new NativeStreamingTtsSession(_ => socket, () => player, clock))
        {
            var operation = session.PlayAsync(Request("s2.1-pro-free"), new string('a', 1000), CancellationToken.None);
            await player.FirstBlock.Task.WaitAsync(RegressionExecution.Timeout);
            clock.Advance(TimeSpan.FromSeconds(1));
            AssertEqual(true, await CaptureFailureAsync(operation) is InvalidOperationException, "A session crossing the cutoff must abort locally");
            AssertEqual(1, socket.ConnectCount, "A cutoff failure must never retry on a paid engine");
            AssertEqual(true, socket.AbortCount > 0, "A cutoff failure must abort all native transport waits");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.session/receiver-and-audio-failures-keep-their-cause-and-dispose-cancels")]
    [Order(52)]
    public static async Task ReceiverAndAudioFailuresKeepTheirCauseAndDisposeCancelsAsync()
    {
        foreach (var mode in new[] { "receiver", "audio", "dispose" })
        {
            var socket = new FishAudioSocketDouble();
            var player = new SessionPlayerDouble { HoldStart = true };
            if (mode == "audio") player.Failure = new InvalidOperationException("synthetic decoder failure");
            socket.OnSend = m => { if (Event(m) == "flush") socket.QueueAudio([1, 2]); };
            using var session = new NativeStreamingTtsSession(_ => socket, () => player);
            var operation = session.PlayAsync(Request(), new string('a', 1000), CancellationToken.None);
            if (mode != "audio") await player.FirstBlock.Task.WaitAsync(RegressionExecution.Timeout);
            if (mode == "receiver") socket.QueueEvent(new() { ["event"] = "error", ["message"] = "not for logs" });
            if (mode == "dispose") session.Dispose();
            var failure = await CaptureFailureAsync(operation);
            AssertEqual(true, socket.AbortCount > 0, "A worker failure or disposal must abort blocked siblings");
            if (mode == "receiver") AssertEqual("Fish Audio streaming reported a remote error.", failure.Message, "A receiver failure cannot be replaced by sibling cancellation");
            if (mode == "audio") AssertEqual("synthetic decoder failure", failure.Message, "An audio failure cannot be replaced by sibling cancellation");
            if (mode == "dispose") AssertEqual(true, failure is OperationCanceledException, "Disposal must cancel a live request");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "streaming.session/cancellation-callback-cannot-replace-primary-audio-failure")]
    [Order(53)]
    public static async Task CancellationCallbackCannotReplacePrimaryAudioFailureAsync()
    {
        var primaryFailure = new InvalidDataException("synthetic primary decoder failure");
        var callbackFailure = new InvalidOperationException("synthetic cancellation callback failure");
        var socket = new FishAudioSocketDouble();
        var trackedSocket = new TrackingSessionSocket(socket);
        var player = new SessionPlayerDouble
        {
            Failure = primaryFailure,
            CancellationCallbackFailure = callbackFailure,
        };
        var source = new StreamingTextSuite.ReadSentinelSource(new string('a', 1000), 100);
        socket.OnSend = message => { if (Event(message) == "flush") socket.QueueAudio([1, 2]); };
        using var session = new NativeStreamingTtsSession(_ => trackedSocket, () => player);
        var operation = session.PlayAsync(Request(), source, CancellationToken.None);
        var failure = await CaptureFailureAsync(operation);

        AssertEqual(true, ReferenceEquals(primaryFailure, failure), "Cancellation callback failure must preserve the original audio exception instance");
        AssertEqual(1, player.CancellationCallbackCount, "The regression must execute the throwing cancellation callback");
        AssertEqual(true, socket.AbortCount > 0, "The primary failure must abort the socket even when cancellation callbacks throw");
        AssertEqual(0, trackedSocket.ActiveReceives, "The receiver sibling must exit before the operation completes");
        AssertEqual(true, trackedSocket.Disposed && player.Disposed, "Owned transport and audio resources must retire after all workers exit");
        AssertEqual(100, source.CharactersRead, "The sender sibling must exit without reading the withheld tail");
        AssertEqual(false, socket.Sent.Any(message => Event(message) == "stop"), "A failed first playback cannot release stop");
    }

    private static SynthesisRequest Request(string model = "s2.1-pro") => new(new Settings { ApiKey = "offline-key", SelectedModel = model, AudioFormat = "pcm" });
    private static string Event(JsonElement message) => message.GetProperty("event").GetString()!;
    private static async Task<Exception> CaptureFailureAsync(Task operation)
    {
        try { await operation.WaitAsync(RegressionExecution.Timeout); }
        catch (Exception ex) when (ex is not TimeoutException || operation.IsCompleted) { return ex; }
        throw new InvalidOperationException("The streaming operation did not fail promptly as expected.");
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(RegressionExecution.Timeout);
        while (!predicate()) await Task.Delay(1, timeout.Token);
    }

    private sealed class OffsetClock(DateTimeOffset epoch) : TimeProvider
    {
        private readonly ManualStreamingTimeProvider _clock = new();
        public override long TimestampFrequency => _clock.TimestampFrequency;
        public override long GetTimestamp() => _clock.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => epoch.AddTicks(GetTimestamp());
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => _clock.CreateTimer(callback, state, dueTime, period);
        internal void Advance(TimeSpan duration) => _clock.Advance(duration);
    }

    private sealed class TrackingSessionSocket(FishAudioSocketDouble inner) : IFishAudioSocket
    {
        internal int ActiveReceives;
        internal bool Disposed;
        public Task ConnectAsync(CancellationToken cancellationToken) => inner.ConnectAsync(cancellationToken);
        public ValueTask SendBinaryAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken) => inner.SendBinaryAsync(message, cancellationToken);
        public async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref ActiveReceives);
            try { return await inner.ReceiveAsync(buffer, cancellationToken); }
            finally { Interlocked.Decrement(ref ActiveReceives); }
        }
        public void Abort() => inner.Abort();
        public void Dispose() { Disposed = true; inner.Dispose(); }
    }

    private sealed class SessionPlayerDouble : IStreamingAudioPlayer
    {
        internal bool HoldStart;
        internal bool HoldDrain;
        internal bool HoldAfterStart;
        internal bool ReportPcm = true;
        internal bool CaptureBytes;
        internal Exception? Failure;
        internal Exception? CancellationCallbackFailure;
        internal int CancellationCallbackCount;
        internal bool Disposed;
        private CancellationTokenRegistration _cancellationRegistration;
        internal long Bytes;
        internal int MaximumBlock;
        internal StreamingActivity? Activity;
        internal List<byte> Captured { get; } = [];
        internal TaskCompletionSource FirstBlock { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource AllowStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource InputEnded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource AllowDrain { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource AllowContinue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task PlayAsync(IAsyncEnumerable<ReadOnlyMemory<byte>> chunks, string format, Action playbackStarted, StreamingActivity activity, CancellationToken cancellationToken)
        {
            Activity = activity;
            if (CancellationCallbackFailure is { } callbackFailure)
                _cancellationRegistration = cancellationToken.Register(() =>
                {
                    Interlocked.Increment(ref CancellationCallbackCount);
                    throw callbackFailure;
                });
            var started = false;
            await foreach (var block in chunks.WithCancellation(cancellationToken))
            {
                if (Failure is { } failure) throw failure;
                Interlocked.Add(ref Bytes, block.Length);
                MaximumBlock = Math.Max(MaximumBlock, block.Length);
                if (CaptureBytes) Captured.AddRange(block.ToArray());
                if (ReportPcm) activity.ReportFirstPlayablePcm();
                FirstBlock.TrySetResult();
                if (!started)
                {
                    if (HoldStart) await AllowStart.Task.WaitAsync(cancellationToken);
                    playbackStarted();
                    started = true;
                    Started.TrySetResult();
                    if (HoldAfterStart) await AllowContinue.Task.WaitAsync(cancellationToken);
                }
            }
            InputEnded.TrySetResult();
            if (HoldDrain) await AllowDrain.Task.WaitAsync(cancellationToken);
        }
        public void Dispose() { Disposed = true; _cancellationRegistration.Dispose(); }
    }
}
