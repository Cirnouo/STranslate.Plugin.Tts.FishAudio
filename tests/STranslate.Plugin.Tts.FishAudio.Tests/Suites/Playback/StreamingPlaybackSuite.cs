using STranslate.Plugin.Tts.FishAudio.Playback;
using STranslate.Plugin.Tts.FishAudio.Streaming;
using NAudio.Wave;

internal static class StreamingPlaybackSuite
{
    internal static async Task RunAllAsync()
    {
        foreach (var owner in CreateCases()) await owner.Run();
    }

    // Metadata construction cannot run fixtures, clocks or action telemetry.
    // Each descriptor keeps one complete original method, including its cleanup.
    internal static IReadOnlyList<(string Id, Func<Task> Run)> CreateCases()
    {
        var cases = new List<(string Id, Func<Task> Run)>();
        AddSync("waveout-provides-200ms-in-four-50ms-buffers", WaveOutProvidesTwoHundredMillisecondsInFourFiftyMillisecondBuffers);
        Add("short-pcm-starts-after-100ms-with-blocked-input", StartsShortPcmAfter100MillisecondsWhileNextChunkBlocksAsync);
        Add("starts-at-250ms-without-timer", StartsAt250MillisecondsWithoutWaitingForTimerAsync);
        Add("pcm-byte-carry-and-odd-tail", CarriesSinglePcmBytesAndRejectsOddTailAsync);
        Add("device-position-drain-including-underrun-silence", DrainsActualDevicePositionIncludingUnderrunSilenceAsync);
        Add("large-pcm-bounds-and-low-water-resume", BoundsLargePcmAndResumesOnlyAtLowWaterAsync);
        Add("device-failure-interrupts-waiting-phases", DeviceFailureInterruptsAllWaitingPhasesAsync);
        Add("cancellation-and-disposal-interrupt-waiting-phases", CancellationAndDisposalInterruptAllWaitingPhasesAsync);
        Add("startup-failures-never-notify", StartupFailuresNeverNotifyAsync);
        Add("stalled-real-data-and-idle-input", DetectsStalledRealDataWithoutTimingOutIdleInputAsync);
        AddSync("waveout-position-wrap-and-unexpected-stops", WaveOutTracks32BitPositionWrapAndUnexpectedStops);
        AddSync("mp3-split-frames-and-vbr-metadata", Mp3DecodesSplitFramesAndVbrMetadataIncrementally);
        AddSync("mp3-truncation-and-oversized-metadata", Mp3RejectsTruncationAndOversizedMetadata);
        Add("mp3-frame-backpressure", StopsDecodingLargeMp3EventAtPcmBackpressureAsync);
        Add("uncooperative-input-interruption", CancellationInterruptsUncooperativeInputAsync);
        AddSync("pcm-ring-cap-and-wraparound", PcmRingCapsFiveSecondsAndTracksWraparound);
        Add("cancellation-during-play-never-notifies", CancellationDuringPlayNeverPublishesStartedAsync);
        Add("cleanup-preserves-primary-and-attempts-all-resources", CleanupPreservesOriginalFailureAndAttemptsEveryResourceAsync);
        Add("cleanup-failure-after-success", CleanupFailureSurfacesWhenPlaybackSucceedsAsync);
        Add("input-cleanup-preserves-decoder-failure", InputCleanupCannotReplaceDecoderFailureAsync);
        Add("synchronous-large-input-starts-before-further-production", SynchronousLargeInputStartsBeforeFurtherProductionAsync);
        Add("cancellation-callback-preserves-playback-failure", CancellationCallbackCannotReplacePlaybackFailureAsync);
        return cases;

        void Add(string id, Func<Task> run) => cases.Add(("playback.streaming/" + id, () => RunOwnedAsync(run)));
        void AddSync(string id, Action run) => Add(id, () => { run(); return Task.CompletedTask; });
    }

    internal static async Task RunOwnedAsync(Func<Task> run)
    {
        var root = RegressionActionScope.QueueRoot();
        using var entered = root?.Enter();
        try { await run(); }
        finally { root?.Complete(); }
    }

    internal static void WaveOutProvidesTwoHundredMillisecondsInFourFiftyMillisecondBuffers()
    {
        var originalContext = SynchronizationContext.Current;
        var callerContext = new SynchronizationContext();
        try
        {
            SynchronizationContext.SetSynchronizationContext(callerContext);
            // Construction configures NAudio only; Init is never called, so no device opens.
            using var output = WaveOutStreamingAudioOutput.CreateWaveOut();
            Equal(200, output.DesiredLatency, "Output queue provides measured scheduling headroom");
            Equal(4, output.NumberOfBuffers, "Output queue uses four buffers");
            var perBufferMilliseconds = (output.DesiredLatency + output.NumberOfBuffers - 1) / output.NumberOfBuffers;
            Equal(50, perBufferMilliseconds, "Increasing queue headroom preserves the refill interval");
            Equal(4410, new WaveFormat(44100, 16, 1).ConvertLatencyToByteSize(perBufferMilliseconds),
                "Each device buffer preserves 50ms of aligned PCM");
            Equal(PlaybackState.Stopped, output.PlaybackState, "Configuration inspection never starts output");
            Equal(true, ReferenceEquals(callerContext, SynchronizationContext.Current), "Output construction restores the caller context");
        }
        finally { SynchronizationContext.SetSynchronizationContext(originalContext); }
    }

    internal static async Task CancellationCallbackCannotReplacePlaybackFailureAsync()
    {
        var primary = new IOException("play primary failure");
        var output = new StreamingAudioOutputDouble
        {
            PlayFailure = primary,
            StopFailure = new InvalidOperationException("stop cleanup failure"),
            DisposeFailure = new ApplicationException("output cleanup failure"),
        };
        var decoder = new StreamingCleanupDecoderDouble { DisposeFailure = new NotSupportedException("decoder cleanup failure") };
        var input = new StreamingAudioInput();
        using var throwingInput = new StreamingThrowingCancellationInput(input.Chunks);
        using var player = new StreamingAudioPlayer(() => output, _ => decoder);
        var play = player.PlayAsync(throwingInput, "pcm", () => { },
            new StreamingActivity(new ManualStreamingTimeProvider()), default);
        input.Send(new byte[22050]);
        var failure = await FailureAsync(play);
        Equal(1, throwingInput.CancellationCallbacks, "Input cancellation callback actually runs");
        Equal(1, throwingInput.DisposeCount, "Input cleanup runs after throwing cancellation callback");
        Equal(1, output.StopCount, "Stop is attempted after throwing cancellation callback");
        Equal(1, output.DisposeCount, "Output cleanup runs after throwing callback and Stop failure");
        Equal(1, decoder.DisposeCount, "Decoder cleanup runs after throwing callback and output cleanup failures");
        Equal(true, ReferenceEquals(primary, failure), "Cancellation callback cannot replace the original Play IOException instance");
    }

    internal static async Task CleanupPreservesOriginalFailureAndAttemptsEveryResourceAsync()
    {
        foreach (var phase in new[] { "play", "decode", "device", "cancel" })
        {
            var primary = new IOException(phase + " primary failure");
            var output = new StreamingAudioOutputDouble
            {
                PlayFailure = phase == "play" ? primary : null,
                StopFailure = new InvalidOperationException("stop failed"),
                DisposeFailure = new NotSupportedException("output dispose failed"),
            };
            var decoder = new StreamingCleanupDecoderDouble
            {
                DecodeFailure = phase == "decode" ? primary : null,
                DisposeFailure = new ApplicationException("decoder dispose failed"),
            };
            var input = new StreamingAudioInput();
            using var cancel = new CancellationTokenSource();
            using var player = new StreamingAudioPlayer(() => output, _ => decoder);
            var play = player.PlayAsync(input.Chunks, "pcm", () => { },
                new StreamingActivity(new ManualStreamingTimeProvider()), cancel.Token);
            if (phase is "play" or "decode") input.Send(new byte[22050]);
            else if (phase == "device") output.Fail(primary);
            else cancel.Cancel();
            var failure = await FailureAsync(play);
            Equal(true, phase == "cancel" ? failure is OperationCanceledException : ReferenceEquals(primary, failure),
                "Cleanup cannot replace original " + phase + " failure");
            Equal(1, output.StopCount, "Stop is attempted despite primary failure");
            Equal(1, output.DisposeCount, "Output Dispose is attempted despite Stop failure");
            Equal(1, decoder.DisposeCount, "Decoder Dispose is attempted despite output cleanup failure");
        }
    }

    internal static async Task CleanupFailureSurfacesWhenPlaybackSucceedsAsync()
    {
        foreach (var stopFails in new[] { true, false })
        {
            var firstCleanupFailure = new IOException("first cleanup failure");
            var output = new StreamingAudioOutputDouble
            {
                StopFailure = stopFails ? firstCleanupFailure : null,
                DisposeFailure = stopFails ? new InvalidOperationException("later output dispose failure") : firstCleanupFailure,
            };
            var decoder = new StreamingCleanupDecoderDouble { DisposeFailure = new ApplicationException("later decoder dispose failure") };
            var input = new StreamingAudioInput();
            var clock = new ManualStreamingTimeProvider();
            using var player = new StreamingAudioPlayer(() => output, _ => decoder);
            var play = player.PlayAsync(input.Chunks, "pcm", () => { }, new StreamingActivity(clock), default);
            input.Send([1, 2]);
            input.Complete();
            await UntilAsync(() => output.PlayCount == 1, "successful playback before cleanup failure");
            output.Read(2);
            output.SetPlayed(2);
            clock.Advance(TimeSpan.FromMilliseconds(10));
            Equal(true, ReferenceEquals(firstCleanupFailure, await FailureAsync(play)), "First cleanup failure surfaces when no primary failure exists");
            Equal(1, output.StopCount, "Successful playback attempts Stop once");
            Equal(1, output.DisposeCount, "Successful playback attempts output Dispose once");
            Equal(1, decoder.DisposeCount, "Successful playback attempts decoder Dispose once");
        }
    }

    internal static async Task InputCleanupCannotReplaceDecoderFailureAsync()
    {
        var primary = new IOException("decoder primary failure");
        var decoder = new StreamingCleanupDecoderDouble { DecodeFailure = primary };
        var input = new StreamingAudioInput();
        var throwingInput = new StreamingThrowingDisposeInput(input.Chunks);
        var output = new StreamingAudioOutputDouble();
        using var player = new StreamingAudioPlayer(() => output, _ => decoder);
        var play = player.PlayAsync(throwingInput, "pcm", () => { },
            new StreamingActivity(new ManualStreamingTimeProvider()), default);
        input.Send([1, 2]);
        Equal(true, ReferenceEquals(primary, await FailureAsync(play)), "Input DisposeAsync cannot replace decoder failure");
        Equal(1, throwingInput.DisposeCount, "Input enumerator cleanup is attempted once");
        Equal(1, output.DisposeCount, "Output still disposes after input cleanup throws");
        Equal(1, decoder.DisposeCount, "Decoder still disposes after input cleanup throws");
    }

    internal static async Task SynchronousLargeInputStartsBeforeFurtherProductionAsync()
    {
        var input = new StreamingAudioInput();
        input.Send([1, 2]); // MoveNext completes synchronously when PlayAsync begins.
        var output = new StreamingAudioOutputDouble();
        var clock = new ManualStreamingTimeProvider();
        var decoder = new StreamingTimedFrameDecoderDouble(clock);
        var framesAtStart = 0;
        output.OnPlay = () => framesAtStart = decoder.Frames;
        using var cancel = new CancellationTokenSource();
        using var player = new StreamingAudioPlayer(() => output, _ => decoder);
        var play = player.PlayAsync(input.Chunks, "mp3", () => { }, new StreamingActivity(clock), cancel.Token);
        await UntilAsync(() => output.PlayCount == 1, "startup during synchronous frame production");
        Equal(true, framesAtStart <= 5, "Playback must start by five 50ms PCM frames, before decoding more of a large synchronous event");
        cancel.Cancel();
        await CancelledAsync(play);
    }

    private static async Task<Exception> FailureAsync(Task task)
    {
        try { await task.WaitAsync(RegressionExecution.Timeout); }
        catch (Exception error) { return error; }
        throw new InvalidOperationException("Expected failure.");
    }

    internal static void PcmRingCapsFiveSecondsAndTracksWraparound()
    {
        var buffer = new StreamingPcmBuffer(new WaveFormat(44100, 16, 1), () => { });
        var pcm = Enumerable.Range(0, 500000).Select(i => (byte)(i % 251)).ToArray();
        Equal(441000, buffer.Write(pcm), "The mono s16 PCM ring accepts exactly five seconds");
        Equal(441000, buffer.BufferedBytes, "Full buffer cannot exceed five seconds");
        var first = new byte[4];
        buffer.Read(first, 0, 4);
        Equal("0,1,2,3", string.Join(',', first), "Initial ring bytes preserve sample order");
        Equal(4, buffer.Write(new byte[] { 240, 241, 242, 243, 244, 245 }), "Only freed aligned capacity is accepted");
        var remaining = new byte[441000];
        buffer.Read(remaining, 0, remaining.Length);
        Equal(true, pcm.AsSpan(4, 440996).SequenceEqual(remaining.AsSpan(0, 440996)), "Ring wrap preserves queued PCM");
        Equal("240,241,242,243", string.Join(',', remaining[^4..]), "Ring wrap appends accepted bytes exactly once");
        var silence = new byte[8];
        buffer.Read(silence, 0, 8);
        Equal(true, silence.All(value => value == 0), "Empty reads return silence");
        Equal(true, buffer.TryGetDrainTail(out var tail), "Drained software ring exposes tail");
        Equal(441004L, tail, "Trailing underrun silence does not extend the real PCM tail");
    }

    internal static async Task CancellationDuringPlayNeverPublishesStartedAsync()
    {
        var input = new StreamingAudioInput();
        using var cancel = new CancellationTokenSource();
        var output = new StreamingAudioOutputDouble { OnPlay = cancel.Cancel };
        using var player = new StreamingAudioPlayer(() => output);
        var calls = 0;
        var play = player.PlayAsync(input.Chunks, "pcm", () => calls++,
            new StreamingActivity(new ManualStreamingTimeProvider()), cancel.Token);
        input.Send(new byte[22050]);
        await CancelledAsync(play);
        Equal(0, calls, "Play returning after cancellation cannot publish playback started");
    }

    internal static async Task StopsDecodingLargeMp3EventAtPcmBackpressureAsync()
    {
        using var source = new MemoryStream(ReadAudioFixture("tone-44100-stereo.mp3"));
        var frame = Mp3Frame.LoadFromStream(source) ?? throw new InvalidOperationException("Fixture has no MP3 frame.");
        var hugeEvent = Enumerable.Range(0, 500).SelectMany(_ => frame.RawData).ToArray();
        var frameDecoder = new StreamingMp3FrameDecoderDouble { FrameBytes = 4608 };
        var input = new StreamingAudioInput();
        var output = new StreamingAudioOutputDouble();
        var clock = new ManualStreamingTimeProvider();
        var activity = new StreamingActivity(clock);
        using var player = new StreamingAudioPlayer(() => output, _ => new IncrementalMp3Decoder(_ => frameDecoder));
        using var cancel = new CancellationTokenSource();
        var play = player.PlayAsync(input.Chunks, "mp3", () => { }, activity, cancel.Token);
        input.Send(hugeEvent);
        await UntilAsync(() => activity.IsBackpressured, "MP3 frame-level PCM backpressure");
        Equal(true, frameDecoder.Calls < 160, "One large encoded event cannot decode all 500 frames before bounded PCM writes");
        Equal(true, output.BufferedBytes <= 882000, "Stereo PCM remains below five seconds of capacity");
        cancel.Cancel();
        await CancelledAsync(play);
        Equal(1, frameDecoder.DisposeCount, "Cancellation disposes persistent frame decompressor");
    }

    internal static async Task CancellationInterruptsUncooperativeInputAsync()
    {
        foreach (var mode in new[] { "cancel", "dispose", "device" })
        {
            var input = new UncooperativeStreamingAudioInput();
            var output = new StreamingAudioOutputDouble();
            using var cancel = new CancellationTokenSource();
            using var player = new StreamingAudioPlayer(() => output);
            var play = player.PlayAsync(input, "pcm", () => { }, new StreamingActivity(new ManualStreamingTimeProvider()), cancel.Token);
            await UntilAsync(() => input.Waiting, "blocked input MoveNext");
            try
            {
                if (mode == "cancel") cancel.Cancel();
                else if (mode == "dispose") player.Dispose();
                else output.Fail();
                if (mode == "device") await ThrowsAsync<IOException>(play, "Device failure interrupts uncooperative MoveNext");
                else await CancelledAsync(play);
            }
            finally { input.Pending.TrySetResult(false); }
        }
    }

    internal static void WaveOutTracks32BitPositionWrapAndUnexpectedStops()
    {
        var wave = new StreamingWavePlayerDouble();
        using (var output = new WaveOutStreamingAudioOutput(wave, () => wave.Position))
        {
            output.Init(new SilenceProvider(new WaveFormat(44100, 16, 1)));
            output.Play();
            wave.Position = 0xFFFF_FFF0L;
            Equal(4294967280L, output.GetPlayedBytes(), "Position starts with unsigned 32-bit byte count");
            wave.Position = 0x30;
            Equal(4294967344L, output.GetPlayedBytes(), "Position wrap extends to 64 bits");
            wave.UnexpectedStop();
            Equal(true, output.FailureToken.IsCancellationRequested, "Exceptionless unexpected stop cancels all waits");
            Throws<IOException>(output.ThrowIfFailed, "Unexpected stop is an error");
        }
        var intentional = new StreamingWavePlayerDouble();
        using var quiet = new WaveOutStreamingAudioOutput(intentional, () => 0);
        quiet.Play();
        quiet.Stop();
        Equal(false, quiet.FailureToken.IsCancellationRequested, "Intentional cleanup stop is quiet");
        quiet.ThrowIfFailed();
    }

    internal static void Mp3DecodesSplitFramesAndVbrMetadataIncrementally()
    {
        var frameDecoder = new StreamingMp3FrameDecoderDouble();
        var factories = 0;
        using var decoder = new IncrementalMp3Decoder(_ => { factories++; return frameDecoder; });
        var mp3 = ReadAudioFixture("tone-vbr-44100-stereo.mp3");
        var output = new List<byte>();
        foreach (var value in mp3)
            foreach (var block in decoder.Decode(new byte[] { value })) output.AddRange(block.Bytes.ToArray());
        decoder.Complete();
        Equal(1, factories, "All MP3 frames share one persistent decompressor");
        Equal(true, frameDecoder.Calls > 5, "Split input still decodes complete MP3 frames");
        Equal(true, frameDecoder.BitRates.Distinct().Count() > 1, "VBR frame bit rates may change");
        Equal(frameDecoder.Calls * 4, output.Count, "Every split frame emits exactly one PCM block");

        var batchDecoder = new StreamingMp3FrameDecoderDouble();
        using var batch = new IncrementalMp3Decoder(_ => batchDecoder);
        using var iterator = batch.Decode(mp3).GetEnumerator();
        Equal(0, batchDecoder.Calls, "MP3 decode iterator is lazy");
        Equal(true, iterator.MoveNext(), "First MP3 frame is available");
        Equal(1, batchDecoder.Calls, "Large MP3 event decodes only the consumed frame");
        while (iterator.MoveNext()) { }
        batch.Complete();
    }

    internal static void Mp3RejectsTruncationAndOversizedMetadata()
    {
        var mp3 = ReadAudioFixture("tone-44100-stereo.mp3");
        using var truncated = new IncrementalMp3Decoder(_ => new StreamingMp3FrameDecoderDouble());
        foreach (var block in truncated.Decode(mp3.AsMemory(0, mp3.Length - 3))) { }
        Throws<InvalidDataException>(truncated.Complete, "Truncated final frame fails at finish");
        using var oversized = new IncrementalMp3Decoder(_ => new StreamingMp3FrameDecoderDouble());
        Throws<InvalidDataException>(() =>
        {
            // ID3 syncsafe length 0x7f7f7f7f is nearly 256 MiB.
            foreach (var block in oversized.Decode(new byte[] { 73, 68, 51, 4, 0, 0, 127, 127, 127, 127 })) { }
        }, "Oversized ID3 metadata is rejected before allocating its declared size");
    }

    private static byte[] ReadAudioFixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static void Throws<T>(Action action, string reason) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException(reason);
    }

    internal static async Task StartsAt250MillisecondsWithoutWaitingForTimerAsync()
    {
        var clock = new ManualStreamingTimeProvider();
        var input = new StreamingAudioInput();
        var output = new StreamingAudioOutputDouble();
        using var player = new StreamingAudioPlayer(() => output);
        using var cancel = new CancellationTokenSource();
        var activity = new StreamingActivity(clock);
        var play = player.PlayAsync(input.Chunks, "pcm", () => { }, activity, cancel.Token);
        input.Send(new byte[22050]); // 44100 * 2 * .25, signed 16-bit mono.
        await UntilAsync(() => output.PlayCount == 1, "250ms threshold startup");
        Equal(TimeSpan.Zero, clock.GetElapsedTime(0), "Threshold startup needs no timer advance");
        cancel.Cancel();
        await CancelledAsync(play);
    }

    internal static async Task CarriesSinglePcmBytesAndRejectsOddTailAsync()
    {
        var input = new StreamingAudioInput();
        var output = new StreamingAudioOutputDouble();
        using var player = new StreamingAudioPlayer(() => output);
        var clock = new ManualStreamingTimeProvider();
        var activity = new StreamingActivity(clock);
        var play = player.PlayAsync(input.Chunks, "pcm", () => { }, activity, default);
        input.Send([1]);
        await SettleAsync();
        Equal(false, activity.FirstPlayablePcm.IsCompleted, "Half a sample is not playable");
        input.Send([2]);
        input.Send([3]);
        input.Send([4]);
        input.Complete();
        await UntilAsync(() => output.PlayCount == 1, "completed short PCM startup");
        var read = output.Read(4);
        Equal("1,2,3,4", string.Join(',', read), "Odd boundaries preserve byte order");
        output.SetPlayed(4);
        clock.Advance(TimeSpan.FromMilliseconds(10));
        await play.WaitAsync(RegressionExecution.Timeout);

        var odd = new StreamingAudioInput();
        using var oddPlayer = new StreamingAudioPlayer(() => new StreamingAudioOutputDouble());
        var oddPlay = oddPlayer.PlayAsync(odd.Chunks, "pcm", () => { },
            new StreamingActivity(new ManualStreamingTimeProvider()), default);
        odd.Send([1]);
        odd.Complete();
        await ThrowsAsync<InvalidDataException>(oddPlay, "Odd final PCM byte must fail");
    }

    internal static async Task DrainsActualDevicePositionIncludingUnderrunSilenceAsync()
    {
        var clock = new ManualStreamingTimeProvider();
        var input = new StreamingAudioInput();
        var output = new StreamingAudioOutputDouble();
        using var player = new StreamingAudioPlayer(() => output);
        var activity = new StreamingActivity(clock);
        var play = player.PlayAsync(input.Chunks, "pcm", () => { }, activity, default);
        input.Send([1, 2]);
        await UntilAsync(() => activity.FirstPlayablePcm.IsCompleted, "drain test first PCM");
        clock.Advance(TimeSpan.FromMilliseconds(100));
        await UntilAsync(() => output.PlayCount == 1, "drain test startup");
        Equal("1,2,0,0,0,0,0,0", string.Join(',', output.Read(8)), "Underrun fills source cursor with silence");
        output.SetPlayed(8);
        input.Send([3, 4]);
        input.Complete();
        await UntilAsync(() => output.BufferedBytes == 2, "second real PCM after underrun");
        output.Read(8); // Real tail is at source position 10, not real-byte count 4 or source position 16.
        output.SetPlayed(9);
        clock.Advance(TimeSpan.FromSeconds(1));
        await SettleAsync();
        Equal(false, play.IsCompleted, "Empty software ring is not hardware drain");
        output.SetPlayed(10);
        clock.Advance(TimeSpan.FromMilliseconds(10));
        await play.WaitAsync(RegressionExecution.Timeout);
        Equal(1, output.StopCount, "Drain stops output once");
        Equal(1, output.DisposeCount, "Drain disposes output once");
    }

    internal static async Task BoundsLargePcmAndResumesOnlyAtLowWaterAsync()
    {
        var clock = new ManualStreamingTimeProvider();
        var input = new StreamingAudioInput();
        var output = new StreamingAudioOutputDouble();
        using var player = new StreamingAudioPlayer(() => output);
        var activity = new StreamingActivity(clock);
        var play = player.PlayAsync(input.Chunks, "pcm", () => { }, activity, default);
        var payload = Enumerable.Range(0, 900000).Select(i => (byte)(i % 251 + 1)).ToArray();
        input.Send(payload);
        input.Complete();
        await UntilAsync(() => activity.IsBackpressured && output.PlayCount == 1, "bounded PCM backpressure");
        output.Read(88200); // One second leaves just above three seconds if aligned writes crossed high water.
        await SettleAsync();
        Equal(true, activity.IsBackpressured, "High-water wait persists above three-second low water");
        var consumed = 88200;
        while (!play.IsCompleted)
        {
            output.Read(8820);
            consumed += 8820;
            output.SetPlayed(consumed);
            clock.Advance(TimeSpan.FromMilliseconds(10));
            await Task.Delay(1);
        }
        await play;
        var actual = output.DeviceBytes.Where(b => b != 0).ToArray();
        Equal(payload.Length, actual.Length, "A large decoded chunk is neither duplicated nor truncated");
        Equal(true, payload.SequenceEqual(actual), "Backpressure preserves sample bytes and order");
        Equal(false, activity.IsBackpressured, "Completed wait releases activity lease");
    }

    internal static async Task DeviceFailureInterruptsAllWaitingPhasesAsync()
    {
        foreach (var phase in new[] { "input", "startup", "backpressure", "drain" })
        {
            var clock = new ManualStreamingTimeProvider();
            var input = new StreamingAudioInput();
            var output = new StreamingAudioOutputDouble();
            using var player = new StreamingAudioPlayer(() => output);
            var activity = new StreamingActivity(clock);
            var play = player.PlayAsync(input.Chunks, "pcm", () => { }, activity, default);
            if (phase == "startup") input.Send([1, 2]);
            if (phase == "backpressure") input.Send(new byte[900000]);
            if (phase == "drain") { input.Send(new byte[22050]); input.Complete(); }
            if (phase == "startup") await UntilAsync(() => activity.FirstPlayablePcm.IsCompleted, phase);
            if (phase == "backpressure") await UntilAsync(() => activity.IsBackpressured, phase);
            if (phase == "drain") { await UntilAsync(() => output.PlayCount == 1, phase); output.Read(22050); }
            output.Fail(); // Models even an exceptionless PlaybackStopped notification.
            await ThrowsAsync<IOException>(play, "Device failure must interrupt " + phase);
            Equal(false, activity.IsBackpressured, "Device failure releases PCM backpressure");
        }
    }

    internal static async Task CancellationAndDisposalInterruptAllWaitingPhasesAsync()
    {
        foreach (var dispose in new[] { false, true })
        foreach (var phase in new[] { "input", "startup", "backpressure", "drain" })
        {
            var input = new StreamingAudioInput();
            var output = new StreamingAudioOutputDouble();
            using var player = new StreamingAudioPlayer(() => output);
            using var cancel = new CancellationTokenSource();
            var activity = new StreamingActivity(new ManualStreamingTimeProvider());
            var play = player.PlayAsync(input.Chunks, "pcm", () => { }, activity, cancel.Token);
            if (phase == "startup") input.Send([1, 2]);
            if (phase == "backpressure") input.Send(new byte[900000]);
            if (phase == "drain") { input.Send(new byte[22050]); input.Complete(); }
            if (phase == "startup") await UntilAsync(() => activity.FirstPlayablePcm.IsCompleted, phase);
            if (phase == "backpressure") await UntilAsync(() => activity.IsBackpressured, phase);
            if (phase == "drain") { await UntilAsync(() => output.PlayCount == 1, phase); output.Read(22050); }
            if (dispose) player.Dispose(); else cancel.Cancel();
            await CancelledAsync(play);
            Equal(false, activity.IsBackpressured, "Cancellation releases PCM backpressure");
        }
    }

    internal static async Task StartupFailuresNeverNotifyAsync()
    {
        foreach (var init in new[] { false, true })
        {
            var input = new StreamingAudioInput();
            var output = new StreamingAudioOutputDouble { FailInit = init, FailPlay = !init };
            using var player = new StreamingAudioPlayer(() => output);
            var calls = 0;
            var play = player.PlayAsync(input.Chunks, "pcm", () => calls++,
                new StreamingActivity(new ManualStreamingTimeProvider()), default);
            input.Send(new byte[22050]);
            await ThrowsAsync<IOException>(play, "Startup driver failure propagates");
            Equal(0, calls, "Failed Init/Play never publishes playback started");
        }
    }

    internal static async Task DetectsStalledRealDataWithoutTimingOutIdleInputAsync()
    {
        var clock = new ManualStreamingTimeProvider();
        var input = new StreamingAudioInput();
        var output = new StreamingAudioOutputDouble();
        using var player = new StreamingAudioPlayer(() => output);
        using var cancel = new CancellationTokenSource();
        var play = player.PlayAsync(input.Chunks, "pcm", () => { }, new StreamingActivity(clock), cancel.Token);
        input.Send(new byte[22050]);
        await UntilAsync(() => output.PlayCount == 1, "stall test startup");
        output.Read(22050); // Queued real data is entirely inside driver's read-ahead.
        clock.Advance(TimeSpan.FromSeconds(10));
        await ThrowsAsync<TimeoutException>(play, "Driver read-ahead with no played position progress must time out");

        var idleClock = new ManualStreamingTimeProvider();
        var idleInput = new StreamingAudioInput();
        var idleOutput = new StreamingAudioOutputDouble();
        using var idlePlayer = new StreamingAudioPlayer(() => idleOutput);
        var idle = idlePlayer.PlayAsync(idleInput.Chunks, "pcm", () => { }, new StreamingActivity(idleClock), cancel.Token);
        idleInput.Send(new byte[22050]);
        await UntilAsync(() => idleOutput.PlayCount == 1, "idle test startup");
        idleOutput.Read(22050);
        idleOutput.SetPlayed(22050);
        idleClock.Advance(TimeSpan.FromMilliseconds(10));
        await SettleAsync();
        idleClock.Advance(TimeSpan.FromMinutes(5));
        await SettleAsync();
        Equal(false, idle.IsCompleted, "Input gap with no outstanding real PCM has no playback timeout");
        cancel.Cancel();
        await CancelledAsync(idle);
    }

    // A player that only checks startup while receiving the next chunk misses this deadline.
    internal static async Task StartsShortPcmAfter100MillisecondsWhileNextChunkBlocksAsync()
    {
        var clock = new ManualStreamingTimeProvider();
        var input = new StreamingAudioInput();
        var output = new StreamingAudioOutputDouble();
        using var player = new StreamingAudioPlayer(() => output);
        using var cancel = new CancellationTokenSource();
        var activity = new StreamingActivity(clock);
        var callbacks = 0;
        var playbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var play = player.PlayAsync(input.Chunks, "pcm", () =>
        {
            Interlocked.Increment(ref callbacks);
            playbackStarted.TrySetResult();
        }, activity, cancel.Token);
        try
        {
            input.Send([1, 2]);
            await UntilAsync(() => activity.FirstPlayablePcm.IsCompleted, "first complete PCM sample");
            Equal(0, Volatile.Read(ref output.PlayCount), "Two bytes must wait for startup timer");
            clock.Advance(TimeSpan.FromMilliseconds(99));
            await SettleAsync();
            Equal(0, Volatile.Read(ref output.PlayCount), "Startup must not run before 100ms");
            clock.Advance(TimeSpan.FromMilliseconds(1));
            // PlayCount can be visible before Play returns and the player publishes startup.
            await playbackStarted.Task.WaitAsync(RegressionExecution.Timeout);
            Equal(1, Volatile.Read(ref output.PlayCount), "100ms startup with blocked input plays once");
        }
        finally
        {
            cancel.Cancel();
            await CancelledAsync(play);
        }
        Equal(1, Volatile.Read(ref callbacks), "Startup notification occurs exactly once");
    }

    private static async Task UntilAsync(Func<bool> predicate, string reason)
    {
        var limit = DateTime.UtcNow.Add(RegressionExecution.Timeout);
        while (!predicate() && DateTime.UtcNow < limit) await Task.Delay(1);
        if (!predicate()) throw new InvalidOperationException("Timed out waiting for " + reason);
    }
    private static async Task SettleAsync() { for (var i = 0; i < 10; i++) await Task.Yield(); }
    private static void Equal<T>(T expected, T actual, string reason)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{reason}: expected {expected}, actual {actual}");
    }
    private static async Task CancelledAsync(Task task)
    {
        try { await task.WaitAsync(RegressionExecution.Timeout); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Expected cancellation.");
    }

    private static async Task ThrowsAsync<T>(Task task, string reason) where T : Exception
    {
        try { await task.WaitAsync(RegressionExecution.Timeout); }
        catch (T) { return; }
        throw new InvalidOperationException(reason);
    }
}
