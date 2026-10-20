using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MessagePack;
using NAudio.Wave;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Playback;
using STranslate.Plugin.Tts.FishAudio.Streaming;

internal static class Program
{
    private const string Model = "s2.1-pro-free";
    private static readonly DateTimeOffset Cutoff = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
    private const string ShortText = "The morning light falls softly across the quiet room.";
    private const string LongText = "The morning light falls softly across the quiet room. A small clock marks the steady passage of time. "
        + "Outside the window, a gentle breeze moves through the trees. A notebook rests on the table, ready for a new page.";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 3 || args[0] is not ("pcm" or "mp3") || args[1] is not ("short" or "long" or "cancel"))
        {
            Console.WriteLine("{\"summaryPath\":null,\"outcome\":\"invalid-arguments\"}");
            return 2;
        }
        string? summaryPath = null;
        SafeWriter? artifactWriter = null;
        string? keyForScrub = null;
        try
        {
            var format = args[0];
            var name = args[1];
            var directory = Path.GetFullPath(args[2]);
            if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException();
            Directory.CreateDirectory(directory);
            summaryPath = Path.Combine(directory, "summary.json");

            // This is the only credential lookup. It occurs only when a real case is run.
            var key = keyForScrub = Environment.GetEnvironmentVariable("FISH_API_KEY", EnvironmentVariableTarget.Process);
            if (string.IsNullOrWhiteSpace(key)) throw new MissingApiKeyException();
            if (key.Any(char.IsControl)) throw new InvalidApiKeyException();
            var writer = artifactWriter = new SafeWriter(directory, key);
            var beganUtc = DateTimeOffset.UtcNow;
            var deadlineUtc = beganUtc.AddSeconds(120) < Cutoff ? beganUtc.AddSeconds(120) : Cutoff;
            var state = new CaptureState(beganUtc, deadlineUtc);
            var request = new SynthesisRequest(new Settings
            {
                ApiKey = key,
                SelectedModel = Model,
                AudioFormat = format,
                VoiceId = "",
                StreamingProxyMode = "system",
                StreamingProxyAddress = "",
            });
            var guard = new Guard(request, deadlineUtc);
            string? failureType = null;
            string? outcome = null;
            var text = name == "short" ? ShortText : LongText;
            var source = new IndexedSource(text, state);
            using var cancellation = new CancellationTokenSource();
            using var deadline = new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, deadline.Token);
            var remaining = deadlineUtc - DateTimeOffset.UtcNow;
            deadline.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
            var cancellationRequested = 0;
            using var deadlineLog = deadline.Token.Register(() => state.Add("deadlineCancellationRequested"));
            using var externalLog = cancellation.Token.Register(() => state.Add("playbackCancellationRequested"));

            try
            {
                guard.Check(linked.Token);
                using var session = new NativeStreamingTtsSession(
                    socketFactory: actualRequest => new CapturingSocket(new FishAudioSocket(actualRequest), guard, state),
                    playerFactory: () => new CapturingPlayer(state, () =>
                    {
                        if (name != "cancel") return;
                        Interlocked.Exchange(ref cancellationRequested, 1);
                        state.Add("cancelAfterActualPlaybackCallback");
                        cancellation.Cancel();
                    }),
                    observer: state.Observe);
                await session.PlayAsync(request, source, linked.Token).ConfigureAwait(false);
                outcome = name == "cancel" ? "cancel-case-completed-without-cancellation" : "completed";
            }
            catch (Exception error)
            {
                failureType = error.GetType().FullName;
                outcome = error is OperationCanceledException && Volatile.Read(ref cancellationRequested) != 0
                    && state.PlaybackStarted ? "cancelled-after-playback" : deadline.IsCancellationRequested ? "deadline" : "failed";
            }
            finally { state.Add("nativeSessionCompleted", new { outcome, failureType }); }

            var nativeObservations = state.Observations();
            var stageSlots = Enum.GetValues<StreamingStage>().Select(stage =>
            {
                var observation = nativeObservations.Where(item => item.Stage == stage).Cast<StreamingObservation?>().FirstOrDefault();
                return new { stage = stage.ToString(), elapsedMs = observation?.Elapsed.TotalMilliseconds,
                    origin = observation is null ? "not-observed" : "native-completion-observer" };
            }).ToArray();
            var verification = state.Verify(name, text.Length, outcome!, stageSlots.Count(item => item.elapsedMs is not null));
            var artifacts = new List<Artifact>();
            artifacts.Add(writer.WriteJson("request-summary.json", new
            {
                utc = beganUtc,
                method = "GET",
                endpoint = "wss://api.fish.audio/v1/tts/live",
                endpointParameters = new Dictionary<string, string>(),
                headers = new Dictionary<string, string> { ["Authorization"] = "[REDACTED]", ["model"] = request.Model },
                modelGuard = new { exact = Model, strictlyBeforeUtc = Cutoff, checkedBeforeConnectAndEverySend = true },
                deadlineUtc,
                format,
                caseName = name,
                text,
                textUtf16Length = text.Length,
                parameters = new { sample_rate = 44100, request.Temperature, request.TopP, request.Normalize,
                    request.Latency, request.ConditionOnPreviousChunks, request.Mp3Bitrate,
                    prosody = new { request.Speed, request.Volume, request.NormalizeLoudness }, chunk_length = 100, voiceReference = "default" },
                proxy = "system",
                retryCount = 0,
                productionAssembly = AssemblyIdentity(typeof(NativeStreamingTtsSession).Assembly),
                audioAssembly = AssemblyIdentity(typeof(WaveOutEvent).Assembly),
                harnessAssembly = AssemblyIdentity(typeof(Program).Assembly),
            }, allowFixedEndpoint: true));
            artifacts.Add(writer.WriteJson("timeline.json", state.Timeline()));
            artifacts.Add(writer.WriteJson("observations.json", stageSlots));
            foreach (var capture in state.Captures()) artifacts.Add(writer.WriteBytes(capture.Name, capture.Bytes));
            artifacts.Add(writer.WriteBytes("raw-audio." + format, state.Audio()));
            var finalOutcome = verification.Passed ? outcome! : "verification-failed";
            var summary = new
            {
                schema = 1,
                beganUtc,
                completedUtc = DateTimeOffset.UtcNow,
                format,
                caseName = name,
                outcome = finalOutcome,
                nativeOutcome = outcome,
                failureType,
                verification,
                stageObservations = stageSlots,
                counters = state.Counters(),
                deadlineUtc,
                captureLimitBytes = CaptureState.MaximumCaptureBytes,
                captureBytes = state.CaptureBytes,
                anyArtifactRedacted = artifacts.Any(item => item.Redacted),
                artifacts,
                persistence = "Exact key bytes are scanned before every write; untrusted URLs cause whole-artifact redaction; no exception messages or stack traces.",
            };
            var summaryArtifact = writer.WriteJson("summary.json", summary);
            if (summaryArtifact.Redacted) finalOutcome = "summary-redacted";
            Console.WriteLine(JsonSerializer.Serialize(new { summaryPath, outcome = finalOutcome }));
            return verification.Passed && !artifacts.Any(item => item.Redacted) && !summaryArtifact.Redacted ? 0 : 1;
        }
        catch (Exception error)
        {
            // Never print paths from exceptions, exception messages, or credential material.
            if (summaryPath is not null)
            {
                try
                {
                    artifactWriter ??= new SafeWriter(Path.GetDirectoryName(summaryPath)!, keyForScrub ?? "");
                    artifactWriter.WriteJson("summary.json", new { outcome = "harness-failed", failureType = error.GetType().FullName });
                }
                catch { summaryPath = null; }
            }
            Console.WriteLine(JsonSerializer.Serialize(new { summaryPath, outcome = "harness-failed" }));
            return 1;
        }
    }

    private static object AssemblyIdentity(Assembly assembly)
    {
        var bytes = File.ReadAllBytes(assembly.Location);
        return new { name = assembly.GetName().Name, version = assembly.GetName().Version?.ToString(),
            bytes = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() };
    }

    private sealed class MissingApiKeyException : Exception { }
    private sealed class InvalidApiKeyException : Exception { }

    private sealed class Guard(SynthesisRequest request, DateTimeOffset deadline)
    {
        internal void Check(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!string.Equals(request.Model, Model, StringComparison.Ordinal)
                || request.Model != FishAudioModelPolicy.S21ProFreeModel) throw new InvalidOperationException();
            var now = DateTimeOffset.UtcNow;
            if (now >= Cutoff || now >= FishAudioModelPolicy.FreeModelCutoffUtc || now >= deadline)
                throw new OperationCanceledException(token);
        }
    }

    private sealed class IndexedSource(string text, CaptureState state) : IStreamingTextSource
    {
        public int Length => text.Length;
        public char this[int index] { get { state.SourceRead(index); return text[index]; } }
    }

    private sealed class CapturingSocket(IFishAudioSocket inner, Guard guard, CaptureState state) : IFishAudioSocket
    {
        private readonly MemoryStream _incoming = new();
        private int _incomingIndex;
        private int _outgoingIndex;
        private int _disposed;

        public async Task ConnectAsync(CancellationToken token)
        {
            guard.Check(token);
            state.Add("connectAttempt");
            await inner.ConnectAsync(token).ConfigureAwait(false);
            state.Add("connectCompleted");
        }

        public async ValueTask SendBinaryAsync(ReadOnlyMemory<byte> message, CancellationToken token)
        {
            guard.Check(token);
            var index = _outgoingIndex++;
            var bytes = message.ToArray();
            state.Capture($"outgoing-{index:D4}.msgpack", bytes);
            var description = DescribeOutgoing(bytes);
            state.Outgoing("sendAttempt", index, description.Event, description.TextLength);
            await inner.SendBinaryAsync(message, token).ConfigureAwait(false);
            state.Outgoing("sendCompleted", index, description.Event, description.TextLength);
        }

        public async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken token)
        {
            var result = await inner.ReceiveAsync(buffer, token).ConfigureAwait(false);
            state.Add("receiveFragment", new { messageIndex = _incomingIndex, result.Count, result.EndOfMessage,
                messageType = result.MessageType.ToString() });
            if (result.MessageType != WebSocketMessageType.Binary) return result;
            state.Reserve(result.Count);
            _incoming.Write(buffer.Span[..result.Count]);
            if (!result.EndOfMessage) return result;
            var message = _incoming.ToArray();
            _incoming.SetLength(0);
            state.Capture($"incoming-{_incomingIndex++:D4}.msgpack", message, alreadyReserved: true);
            try
            {
                var decoded = FishAudioStreamingProtocol.Decode(message);
                state.Incoming(decoded.Event, decoded.Audio.Length, decoded.Reason == "stop");
                if (decoded.Event == "audio" && !decoded.Audio.IsEmpty) state.AddAudio(decoded.Audio.ToArray());
            }
            catch (CaptureLimitException) { throw; }
            catch (Exception error) { state.Add("diagnosticDecodeFailure", new { failureType = error.GetType().FullName }); }
            return result;
        }

        public void Abort() { state.Add("socketAbort"); inner.Abort(); }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try
            {
                if (_incoming.Length != 0)
                    state.Capture($"incoming-{_incomingIndex:D4}-incomplete.msgpack", _incoming.ToArray(), alreadyReserved: true);
            }
            finally { _incoming.Dispose(); inner.Dispose(); state.Add("socketDisposed"); }
        }

        private static (string Event, int TextLength) DescribeOutgoing(byte[] bytes)
        {
            var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));
            var count = reader.ReadMapHeader();
            string eventName = "unknown";
            var textLength = 0;
            for (var i = 0; i < count; i++)
            {
                switch (reader.ReadString())
                {
                    case "event":
                        var value = reader.ReadString();
                        eventName = value is "start" or "text" or "flush" or "stop" ? value : "unknown";
                        break;
                    case "text": textLength = reader.ReadString()?.Length ?? 0; break;
                    default: reader.Skip(); break;
                }
            }
            return (eventName, textLength);
        }
    }

    private sealed class CapturingPlayer(CaptureState state, Action cancelAfterPlayback) : IStreamingAudioPlayer
    {
        private readonly StreamingAudioPlayer _inner = new(() => new CapturingOutput(state));
        public Task PlayAsync(IAsyncEnumerable<ReadOnlyMemory<byte>> chunks, string format, Action nativeCallback,
            StreamingActivity activity, CancellationToken token) => _inner.PlayAsync(chunks, format, () =>
            {
                // Snapshot at actual callback entry before native callback releases the sender.
                var snapshot = state.ActualPlaybackCallbackEntry();
                nativeCallback();
                state.Add("nativePlaybackCallbackReturned", snapshot);
                cancelAfterPlayback();
            }, activity, token);
        public void Dispose() { _inner.Dispose(); state.Add("playerDisposed"); }
    }

    private sealed class CapturingOutput(CaptureState state) : IStreamingAudioOutput
    {
        private readonly WaveOutStreamingAudioOutput _inner = new();
        public CancellationToken FailureToken => _inner.FailureToken;
        public void Init(IWaveProvider provider) { state.Add("waveOutInit", new { provider.WaveFormat.SampleRate,
            provider.WaveFormat.BitsPerSample, provider.WaveFormat.Channels }); _inner.Init(provider); }
        public void Play() { state.Add("waveOutPlayEntry"); _inner.Play(); state.WaveOutPlayed(); }
        public long GetPlayedBytes() { var bytes = _inner.GetPlayedBytes(); state.DevicePosition(bytes); return bytes; }
        public void ThrowIfFailed() => _inner.ThrowIfFailed();
        public void Stop() { state.Add("waveOutStop"); _inner.Stop(); }
        public void Dispose() { _inner.Dispose(); state.Add("waveOutDisposed"); }
    }
}

internal sealed class CaptureLimitException : Exception { }
internal readonly record struct SourceSnapshot(int HighestIndex, int ReadCount, int SentTextUtf16, int CompletedTextEvents, bool StopCompleted);
internal readonly record struct RawCapture(string Name, byte[] Bytes);
internal readonly record struct Verification(bool Passed, string[] Failures);

internal sealed class CaptureState(DateTimeOffset beganUtc, DateTimeOffset deadlineUtc)
{
    internal const long MaximumCaptureBytes = 8L * 1024 * 1024;
    private const int MaximumTimelineEntries = 10000;
    private readonly object _gate = new();
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly List<object> _timeline = [];
    private readonly List<RawCapture> _captures = [];
    private readonly List<byte[]> _audio = [];
    private readonly List<StreamingObservation> _observations = [];
    private int _highest = -1, _reads, _sentText, _textEvents, _audioMessages, _allMessages, _fragments;
    private int _prePlaybackHighest = -1, _postPlaybackReads, _stopCount;
    private long _captureBytes, _audioBytes, _deviceBytes;
    private double? _firstAudioMs, _playbackMs, _playReturnedMs, _stopMs;
    private SourceSnapshot? _playbackSnapshot;
    private bool _stopCompleted, _captureLimit;

    internal bool PlaybackStarted { get { lock (_gate) return _playbackMs is not null; } }
    internal long CaptureBytes { get { lock (_gate) return _captureBytes; } }
    private SourceSnapshot Snapshot() => new(_highest, _reads, _sentText, _textEvents, _stopCompleted);

    internal void Add(string kind, object? details = null)
    {
        lock (_gate) Append(kind, details);
    }

    private void Append(string kind, object? details = null)
    {
        if (_timeline.Count >= MaximumTimelineEntries) { _captureLimit = true; throw new CaptureLimitException(); }
        if (kind == "receiveFragment") _fragments++;
        _timeline.Add(new { sequence = _timeline.Count, elapsedMs = _watch.Elapsed.TotalMilliseconds,
            utc = beganUtc + _watch.Elapsed, kind, sourceReadHighestIndex = _highest, details });
    }

    internal void SourceRead(int index)
    {
        lock (_gate)
        {
            _highest = Math.Max(_highest, index);
            _reads++;
            if (_playbackMs is null) _prePlaybackHighest = Math.Max(_prePlaybackHighest, index);
            else _postPlaybackReads++;
            Append("sourceRead", new { index, beforeActualPlayback = _playbackMs is null });
        }
    }

    internal void Reserve(int bytes)
    {
        lock (_gate)
        {
            if (bytes < 0 || bytes > MaximumCaptureBytes - _captureBytes)
            { _captureLimit = true; throw new CaptureLimitException(); }
            _captureBytes += bytes;
        }
    }

    internal void Capture(string name, byte[] bytes, bool alreadyReserved = false)
    {
        lock (_gate)
        {
            if (!alreadyReserved) Reserve(bytes.Length);
            _captures.Add(new RawCapture(name, bytes));
        }
    }

    internal void AddAudio(byte[] bytes)
    {
        lock (_gate) { Reserve(bytes.Length); _audio.Add(bytes); _audioBytes += bytes.Length; }
    }

    internal void Outgoing(string kind, int index, string eventName, int textLength)
    {
        lock (_gate)
        {
            if (kind == "sendCompleted")
            {
                if (eventName == "text") { _sentText += textLength; _textEvents++; }
                if (eventName == "stop") { _stopCompleted = true; _stopCount++; _stopMs = _watch.Elapsed.TotalMilliseconds; }
            }
            Append(kind, new { index, @event = eventName, textUtf16Length = textLength,
                cumulativeTextUtf16 = _sentText, textEvents = _textEvents, stopCompleted = _stopCompleted });
        }
    }

    internal void Incoming(string eventName, long audioBytes, bool stopReason)
    {
        lock (_gate)
        {
            _allMessages++;
            if (eventName == "audio" && audioBytes > 0) { _audioMessages++; _firstAudioMs ??= _watch.Elapsed.TotalMilliseconds; }
            Append("decodedIncomingEvent", new { @event = eventName, audioBytes,
                finishReason = eventName == "finish" ? stopReason ? "stop" : "other" : null });
        }
    }

    internal void WaveOutPlayed() { lock (_gate) { _playReturnedMs = _watch.Elapsed.TotalMilliseconds; Append("waveOutPlayReturned"); } }
    internal void DevicePosition(long bytes) { lock (_gate) _deviceBytes = Math.Max(_deviceBytes, bytes); }

    internal SourceSnapshot ActualPlaybackCallbackEntry()
    {
        lock (_gate)
        {
            _playbackMs = _watch.Elapsed.TotalMilliseconds;
            _playbackSnapshot = Snapshot();
            Append("actualPlaybackCallbackEntry", _playbackSnapshot);
            return _playbackSnapshot.Value;
        }
    }

    internal void Observe(StreamingObservation observation)
    {
        lock (_gate) { _observations.Add(observation); Append("nativeObservationDeliveredAfterCompletion",
            new { stage = observation.Stage.ToString(), stageElapsedMs = observation.Elapsed.TotalMilliseconds }); }
    }

    internal object[] Timeline() { lock (_gate) return _timeline.ToArray(); }
    internal StreamingObservation[] Observations() { lock (_gate) return _observations.ToArray(); }
    internal RawCapture[] Captures() { lock (_gate) return _captures.ToArray(); }
    internal byte[] Audio()
    {
        lock (_gate)
        {
            var bytes = new byte[checked((int)_audioBytes)];
            var offset = 0;
            foreach (var part in _audio) { part.CopyTo(bytes, offset); offset += part.Length; }
            return bytes;
        }
    }

    internal object Counters()
    {
        lock (_gate) return new { sourceReadHighestIndex = _highest, sourceReadCount = _reads,
            highestReadIndexBeforeActualPlayback = _prePlaybackHighest, indexedReadsAfterActualPlayback = _postPlaybackReads,
            sourceAtActualPlayback = _playbackSnapshot, sentTextUtf16 = _sentText, completedTextEvents = _textEvents,
            completedStopEvents = _stopCount, receivedBinaryMessages = _allMessages, receivedFragments = _fragments,
            receivedAudioMessages = _audioMessages, receivedAudioBytes = _audioBytes, highestDevicePlayedBytes = _deviceBytes,
            firstAudioMs = _firstAudioMs, actualWaveOutPlayReturnedMs = _playReturnedMs,
            actualPlaybackCallbackMs = _playbackMs, stopSendCompletedMs = _stopMs,
            playbackBeforeStopSendCompleted = _playbackMs is not null && _stopMs is not null && _playbackMs < _stopMs,
            captureLimitReached = _captureLimit, deadlineUtc };
    }

    internal Verification Verify(string name, int textLength, string outcome, int stageCount)
    {
        lock (_gate)
        {
            var failures = new List<string>();
            if (_captureLimit) failures.Add("capture-limit-reached");
            if (_audioBytes == 0) failures.Add("no-raw-audio");
            if (_playReturnedMs is null || _playbackMs is null) failures.Add("actual-waveout-playback-not-observed");
            if (name == "cancel")
            {
                if (outcome != "cancelled-after-playback") failures.Add("cancellation-after-playback-not-observed");
            }
            else
            {
                if (outcome != "completed") failures.Add("native-session-did-not-complete");
                if (stageCount != 8) failures.Add("missing-native-stage-observations");
                if (_sentText != textLength) failures.Add("sent-text-length-mismatch");
                if (_stopCount != 1) failures.Add("expected-one-stop");
            }
            if (name is "long" or "cancel")
            {
                if (_playbackSnapshot is not { } snapshot || snapshot.HighestIndex >= textLength - 1)
                    failures.Add("source-tail-was-already-read-at-actual-playback");
                if (_playbackSnapshot is not { } before || before.SentTextUtf16 >= textLength || before.StopCompleted)
                    failures.Add("tail-or-stop-was-already-sent-at-actual-playback");
            }
            return new Verification(failures.Count == 0, failures.ToArray());
        }
    }
}

internal readonly record struct Artifact(string Name, long OriginalBytes, string OriginalSha256, long PersistedBytes,
    string PersistedSha256, bool Redacted, string? RedactionReason);

internal sealed partial class SafeWriter(string directory, string key)
{
    private static readonly byte[] Redacted = Encoding.UTF8.GetBytes("[REDACTED: sensitive content detected before persistence]\n");
    private readonly byte[][] _keyPatterns = [Encoding.UTF8.GetBytes(key), Encoding.Unicode.GetBytes(key), Encoding.BigEndianUnicode.GetBytes(key)];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    internal Artifact WriteJson(string name, object value, bool allowFixedEndpoint = false) =>
        WriteBytes(name, JsonSerializer.SerializeToUtf8Bytes(value, Json), allowFixedEndpoint);

    internal Artifact WriteBytes(string name, byte[] original, bool allowFixedEndpoint = false)
    {
        // Scan the exact bytes immediately before every write, including generated JSON.
        string? reason = null;
        foreach (var pattern in _keyPatterns)
            if (pattern.Length != 0 && original.AsSpan().IndexOf(pattern) >= 0) { reason = "exact-key-bytes"; break; }
        var asText = Encoding.Latin1.GetString(original);
        foreach (Match match in Url().Matches(asText))
        {
            if (allowFixedEndpoint && match.Value == "wss://api.fish.audio/v1/tts/live") continue;
            reason ??= "untrusted-url";
        }
        var persisted = reason is null ? original : Redacted;
        // The fixed marker is also scanned, so even an unusual key cannot leak.
        if (_keyPatterns.Any(pattern => pattern.Length != 0 && persisted.AsSpan().IndexOf(pattern) >= 0)) persisted = [];
        File.WriteAllBytes(Path.Combine(directory, name), persisted);
        return new(name, original.LongLength, Hash(original), persisted.LongLength, Hash(persisted), reason is not null, reason);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    [GeneratedRegex(@"(?:https?|wss?)://[^\s\x00-\x20<>""'\\]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Url();
}
