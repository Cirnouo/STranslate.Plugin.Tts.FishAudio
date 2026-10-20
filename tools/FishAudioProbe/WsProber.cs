using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;

namespace FishAudioProbe;

internal sealed class WsProber(EvidenceStore store, Redactor redactor, string apiKey, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    internal async Task RunAsync(string id, string voiceId, string format, bool cancelAfterAudio, bool missingAuthentication, CancellationToken cancellationToken, bool segmented = false)
    {
        var evidence = store.Begin(id, "ws-tts");
        using var timeout = new WsSynthesisLifetime(_timeProvider, cancellationToken);
        var clock = Stopwatch.StartNew();
        var trace = new List<object>();
        var traceLock = new object();
        var order = 0;
        var messageNumber = 0;
        var sentNumber = 0;
        var receivedBytes = 0;
        var audioNumber = 0;
        var firstAudio = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sequence = new WsSequence();
        var cancelledByCase = false;
        using var audio = new MemoryStream();
        using var socket = new ClientWebSocket();
        Task? receive = null;
        using var handshake = new HandshakeCaptureHandler(redactor);
        using var invoker = new HttpMessageInvoker(handshake);
        var uri = new Uri("wss://api.fish.audio/v1/tts/live");
        string[] tail = segmented
            ? [" This first section checks that several short text messages can continue one speaking session.",
               " The second section keeps the same voice while the receiver consumes earlier audio.",
               " The final section ends the stream normally after the remaining words have been sent."]
            : [" Good day."];
        void Event(string direction, string name, object? details = null)
        {
            lock (traceLock) trace.Add(new { order = ++order, utc = _timeProvider.GetUtcNow(), elapsedMs = clock.Elapsed.TotalMilliseconds, direction, @event = name, details });
        }
        async Task Send(Dictionary<string, object?> message)
        {
            timeout.EnsureAllowed();
            var bytes = WsCodec.Encode(message);
            var name = message["event"]?.ToString() ?? "unknown";
            var path = $"sent-{++sentNumber:D4}.msgpack";
            store.WriteBody(evidence.Directory, path, bytes, true, null, "application/msgpack");
            await timeout.SendAsync(token => socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Binary, true, token));
            Event("send", name, new { file = path, bytes = bytes.Length, sha256 = EvidenceStore.Hash(bytes) });
        }
        async Task Receive()
        {
            while (true)
            {
                using var message = new MemoryStream();
                var path = $"received-{++messageNumber:D4}.msgpack";
                var messageComplete = false;
                try
                {
                    var buffer = new byte[16 * 1024];
                    ValueWebSocketReceiveResult fragment;
                    do
                    {
                        var remaining = ProbePolicy.MaximumBytes - receivedBytes;
                        if (remaining <= 0) throw new InvalidOperationException("WS received-byte limit reached.");
                        fragment = await socket.ReceiveAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), timeout.Token);
                        if (fragment.MessageType == WebSocketMessageType.Close)
                        {
                            Event("receive", "socket-close", new { status = socket.CloseStatus?.ToString() });
                            throw new WebSocketException("Socket closed before finish.");
                        }
                        receivedBytes += fragment.Count;
                        message.Write(buffer, 0, fragment.Count);
                        Event("receive", "fragment", new { file = path, bytes = fragment.Count, fragment.EndOfMessage, type = fragment.MessageType.ToString() });
                    } while (!fragment.EndOfMessage);
                    messageComplete = true;
                    var bytes = message.ToArray();
                    if (fragment.MessageType != WebSocketMessageType.Binary) throw new WebSocketException("Expected a binary MessagePack message.");
                    var data = WsCodec.Decode(bytes);
                    var stored = store.WriteBody(evidence.Directory, path, bytes, true, null, "application/msgpack", WsCodec.Sanitize(data, bytes, redactor));
                    store.WriteJson(Path.Combine(evidence.Directory, path + ".json"), stored);
                    var name = data.GetValueOrDefault("event")?.ToString() ?? "unknown";
                    Event("receive", name, new { frame = stored });
                    if (name == "audio" && data.GetValueOrDefault("audio") is byte[] chunk)
                    {
                        var chunkPath = $"audio-{++audioNumber:D4}.bin";
                        var artifact = store.WriteBody(evidence.Directory, chunkPath, chunk, true, null, $"audio/{format}");
                        audio.Write(chunk);
                        Event("audio", "audio-mapping", new { message = path, audio = artifact, audioIndex = audioNumber });
                        sequence.ObserveAudio(chunk.Length);
                        if (sequence.CanSendTail) firstAudio.TrySetResult();
                    }
                    else if (name == "finish")
                    {
                        var reason = data.GetValueOrDefault("reason")?.ToString();
                        evidence.Details["finishReason"] = reason;
                        if (reason != "stop") throw new WebSocketException("Server reported a failed session.");
                        firstAudio.TrySetException(new InvalidOperationException("Finish arrived without nonempty audio."));
                        return;
                    }
                    else if (name == "error") throw new WebSocketException("Server sent an error event; inspect the sanitized message.");
                }
                catch (Exception ex)
                {
                    if (message.Length > 0 && !File.Exists(Path.Combine(evidence.Directory, path)))
                    {
                        var partial = store.WriteBody(evidence.Directory, path, message.ToArray(), messageComplete, ex.GetType().Name, "application/msgpack");
                        store.WriteJson(Path.Combine(evidence.Directory, path + ".json"), partial);
                        Event("receive", "interrupted-message", new { frame = partial });
                    }
                    firstAudio.TrySetException(new InvalidOperationException("Receive ended before the first audio gate completed."));
                    throw;
                }
            }
        }
        try
        {
            ProbePolicy.EnsureApiUri(uri);
            timeout.EnsureAllowed();
            socket.Options.SetRequestHeader("model", ProbePolicy.FreeModel);
            if (!missingAuthentication) socket.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            var request = new Dictionary<string, object?>
            {
                ["text"] = "", ["reference_id"] = voiceId, ["format"] = format, ["sample_rate"] = 44100,
                ["latency"] = "balanced", ["chunk_length"] = 100,
            };
            if (format == "mp3") request["mp3_bitrate"] = 128;
            store.WriteJson(Path.Combine(evidence.Directory, "request.json"), new
            {
                method = "WS", url = uri.AbsoluteUri,
                headers = new { model = ProbePolicy.FreeModel, Authorization = missingAuthentication ? null : "[REDACTED]" },
                start = request, prefix = "Hello.", tail, cancelAfterFirstAudio = cancelAfterAudio,
                tailGate = "first nonempty audio event", expectedAuthenticationFailure = missingAuthentication,
            });
            timeout.EnsureAllowed();
            evidence.RequestSent = true;
            await socket.ConnectAsync(uri, invoker, timeout.Token);
            evidence.HttpStatus = 101;
            Event("connection", "open");
            receive = Receive();
            await Send(new() { ["event"] = "start", ["request"] = request });
            await Send(new() { ["event"] = "text", ["text"] = "Hello." });
            await Send(new() { ["event"] = "flush" });
            await firstAudio.Task.WaitAsync(timeout.Token);
            if (!sequence.CanSendTail) throw new InvalidOperationException("Tail gate violated.");
            Event("local", "first-audio-gate-open");
            if (cancelAfterAudio)
            {
                cancelledByCase = true;
                Event("local", "cancel-requested");
                timeout.Cancel();
                socket.Abort();
                await receive;
            }
            else
            {
                foreach (var text in tail) await Send(new() { ["event"] = "text", ["text"] = text });
                await Send(new() { ["event"] = "stop" });
                await receive;
                evidence.Outcome = "success";
            }
        }
        catch (Exception ex)
        {
            evidence.Outcome = cancelledByCase ? "cancelled-by-case"
                : missingAuthentication && handshake.Status is >= 400 and < 500 ? "expected-http-error"
                : _timeProvider.GetUtcNow() >= ProbePolicy.CutoffUtc ? "free-window-ended"
                : timeout.IsCancellationRequested ? "timeout-or-cancellation" : "ws-error";
            evidence.Failure = cancelledByCase ? null : ex.GetType().Name;
            evidence.Details[cancelledByCase ? "clientCancellationException" : "error"] = cancelledByCase
                ? ex.GetType().Name : redactor.Clean(ex.Message);
        }
        finally
        {
            timeout.Cancel();
            socket.Abort();
            if (receive is not null) { try { await receive; } catch { } }
            if (handshake.Status is not null) evidence.HttpStatus = handshake.Status;
            store.WriteJson(Path.Combine(evidence.Directory, "response-headers.json"), new { status = evidence.HttpStatus, headers = handshake.Headers });
            if (handshake.Body is { } body)
                store.WriteBody(evidence.Directory, "handshake-body.json", body.Bytes, body.Complete, body.Failure, handshake.ContentType);
            evidence.Details["handshakeBodyCapture"] = handshake.Body is not null ? "captured"
                : evidence.HttpStatus == 101 ? "not-applicable-after-successful-upgrade"
                : "not-captured: no HTTP response body was exposed by the handshake transport";
            evidence.Body = store.WriteBody(evidence.Directory, "audio.bin", audio.ToArray(), evidence.Outcome == "success", evidence.Failure, $"audio/{format}");
            evidence.Details["format"] = format;
            evidence.Details["sampleRate"] = 44100;
            evidence.Details["receivedMessageBytes"] = receivedBytes;
            evidence.Details["messageCount"] = messageNumber;
            evidence.Details["audioEventCount"] = audioNumber;
            evidence.Details["firstAudioGateOpened"] = sequence.CanSendTail;
            evidence.Details["tailWithheldUntilFirstAudio"] = true;
            evidence.Details["tailTextEvents"] = tail.Length;
            evidence.Details["sessionDeadlineUtc"] = timeout.DeadlineUtc;
            store.WriteJson(Path.Combine(evidence.Directory, "events.json"), trace);
            store.Finish(evidence);
        }
    }

    private sealed class HandshakeCaptureHandler(Redactor redactor) : DelegatingHandler(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        internal int? Status { get; private set; }
        internal Dictionary<string, string[]> Headers { get; private set; } = [];
        internal Capture? Body { get; private set; }
        internal string? ContentType { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            Status = (int)response.StatusCode;
            Headers = redactor.CleanHeaders(response.Headers.Concat(response.Content.Headers));
            ContentType = response.Content.Headers.ContentType?.ToString();
            if (response.StatusCode != HttpStatusCode.SwitchingProtocols)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                Body = await BoundedCapture.ReadAsync(stream, ProbePolicy.MaximumBytes, cancellationToken);
            }
            return response;
        }
    }
}
