using System.Net;
using System.Text.Json;
using System.Text;

namespace FishAudioProbe;

internal static class SelfTests
{
    internal static int Run()
    {
        var failures = 0;
        var tests = new (string Name, Action Test)[]
        {
            ("paid and misspelled models are refused before synthesis", () =>
            {
                foreach (var model in new[] { "s2.1-pro", "drama-3", "", "s2.1-pro-free " })
                    Throws(() => ProbePolicy.EnsureSynthesisAllowed(model, new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero)));
            }),
            ("free synthesis fails closed at the UTC cutoff", () =>
            {
                ProbePolicy.EnsureSynthesisAllowed("s2.1-pro-free", new(2026, 11, 30, 23, 59, 59, TimeSpan.Zero));
                Throws(() => ProbePolicy.EnsureSynthesisAllowed("s2.1-pro-free", new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero)));
            }),
            ("WS preflight before cutoff cannot authorize later text", () =>
            {
                var clock = new ManualTimeProvider(new(2026, 11, 30, 23, 59, 58, TimeSpan.Zero));
                using var lifetime = new WsSynthesisLifetime(clock, CancellationToken.None);
                lifetime.EnsureAllowed(); // The same preflight used immediately before ConnectAsync.
                var sends = 0;
                ValueTask Send(CancellationToken token) { token.ThrowIfCancellationRequested(); sends++; return ValueTask.CompletedTask; }
                lifetime.SendAsync(Send).GetAwaiter().GetResult();
                clock.Advance(TimeSpan.FromSeconds(1));
                lifetime.SendAsync(Send).GetAwaiter().GetResult();
                clock.Advance(TimeSpan.FromSeconds(1));
                Throws(() => lifetime.SendAsync(Send).GetAwaiter().GetResult());
                Check(sends == 2 && lifetime.IsCancellationRequested, "a pre-cutoff connection permitted a post-cutoff send");
            }),
            ("WS cutoff cancels pending connection and first-audio waits", () =>
            {
                var clock = new ManualTimeProvider(new(2026, 11, 30, 23, 59, 59, TimeSpan.Zero));
                using var lifetime = new WsSynthesisLifetime(clock, CancellationToken.None);
                var connection = Task.Delay(Timeout.InfiniteTimeSpan, lifetime.Token);
                var firstAudio = new TaskCompletionSource();
                var audioWait = firstAudio.Task.WaitAsync(lifetime.Token);
                clock.Advance(TimeSpan.FromSeconds(1));
                Check(connection.IsCanceled && audioWait.IsCanceled, "the cutoff left connection/audio work pending");
            }),
            ("WS lifetime retains the case timeout and caller cancellation", () =>
            {
                var clock = new ManualTimeProvider(new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero));
                using var caller = new CancellationTokenSource();
                using var byCaller = new WsSynthesisLifetime(clock, caller.Token);
                using var byTimeout = new WsSynthesisLifetime(clock, CancellationToken.None);
                caller.Cancel();
                Check(byCaller.IsCancellationRequested && !byTimeout.IsCancellationRequested, "caller cancellation did not stay scoped");
                clock.Advance(ProbePolicy.CaseTimeout);
                Check(byTimeout.IsCancellationRequested, "the original 120-second case bound was lost");
            }),
            ("secrets and signed URI credentials cannot survive logs or errors", () =>
            {
                var redactor = new Redactor("synthetic-secret");
                var text = redactor.Clean("Authorization: Bearer synthetic-secret; failed https://user:password@example.com/audio.mp3?X-Amz-Credential=credential&X-Amz-Signature=signature&X-Amz-Security-Token=session-token");
                foreach (var forbidden in new[] { "synthetic-secret", "password", "credential", "signature", "session-token" })
                    Check(!text.Contains(forbidden, StringComparison.Ordinal), "sensitive log value survived");
            }),
            ("nested JSON and header dictionaries are redacted", () =>
            {
                var redactor = new Redactor("synthetic-secret");
                var json = redactor.CleanJson("{\"headers\":{\"Authorization\":\"Bearer another-secret\",\"Set-Cookie\":\"session=private\"},\"nested\":[{\"audio\":\"https://a:b@example.com/a?X-Amz-Signature=hidden\"}],\"error\":\"synthetic-secret\"}");
                foreach (var forbidden in new[] { "another-secret", "session=private", "hidden", "synthetic-secret", "a:b@" })
                    Check(!json.Contains(forbidden, StringComparison.Ordinal), "sensitive JSON value survived");
            }),
            ("partial wallet JSON cannot expose private account identifiers", () =>
            {
                var clean = new Redactor("").CleanWalletJson("{\"_id\":\"private-wallet-id\",\"user_id\":\"private-user-id");
                Check(!clean.Contains("private-wallet-id", StringComparison.Ordinal) && !clean.Contains("private-user-id", StringComparison.Ordinal), "truncated wallet identifier survived");
            }),
            ("WS tail remains gated until a nonempty audio event", () =>
            {
                var sequence = new WsSequence();
                Check(!sequence.CanSendTail, "tail was allowed before audio");
                sequence.ObserveAudio(0);
                Check(!sequence.CanSendTail, "empty audio opened the gate");
                sequence.ObserveAudio(2);
                Check(sequence.CanSendTail, "nonempty audio did not open the gate");
            }),
            ("CLI defaults to public and refuses credential arguments", () =>
            {
                Check(CliOptions.Parse(["--output", AppContext.BaseDirectory]).Group == "public", "default group is not anonymous");
                Throws(() => CliOptions.Parse(["--output", AppContext.BaseDirectory, "--api-key", "synthetic-secret"]));
            }),
            ("failed response reads preserve bytes already received", () =>
            {
                using var stream = new BrokenStream();
                var capture = BoundedCapture.ReadAsync(stream, 100, CancellationToken.None).GetAwaiter().GetResult();
                Check(capture.Bytes.SequenceEqual(new byte[] { 97, 98, 99 }), "partial body was lost");
                Check(!capture.Complete && capture.Failure == "IOException", "read failure was hidden");
            }),
            ("response byte limits preserve the exact bounded prefix", () =>
            {
                using var stream = new MemoryStream([1, 2, 3, 4]);
                var capture = BoundedCapture.ReadAsync(stream, 3, CancellationToken.None).GetAwaiter().GetResult();
                Check(capture.Bytes.SequenceEqual(new byte[] { 1, 2, 3 }) && !capture.Complete, "body limit was ignored");
            }),
            ("MessagePack redaction retains binary audio and removes URI credentials", () =>
            {
                var original = WsCodec.Encode(new() { ["event"] = "audio", ["audio"] = new byte[] { 1, 2, 0, 255 }, ["url"] = "https://name:password@example.com/a?X-Amz-Signature=secret-value" });
                var clean = WsCodec.Sanitize(WsCodec.Decode(original), original, new Redactor("synthetic-secret"));
                var decoded = WsCodec.Decode(clean);
                Check(decoded["audio"] is byte[] bytes && bytes.SequenceEqual(new byte[] { 1, 2, 0, 255 }), "audio bytes changed");
                Check(decoded["url"]?.ToString() == "https://example.com/a", "signed URL survived the MessagePack capture");
            }),
            ("MessagePack binary fields remove exact secret bytes while retaining ordinary audio", () => WithTemporaryDirectory(directory =>
            {
                const string secret = "synthetic-secret";
                var ordinaryAudio = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
                var original = WsCodec.Encode(new()
                {
                    ["event"] = "audio", ["audio"] = ordinaryAudio,
                    ["nested"] = new object[] { new Dictionary<string, object?> { ["binary"] = Encoding.UTF8.GetBytes("before-" + secret + "-after") } },
                });
                var redactor = new Redactor(secret);
                var sanitized = WsCodec.Sanitize(WsCodec.Decode(original), original, redactor);
                var store = new EvidenceStore(directory, "offline-self-test", redactor);
                var artifact = store.WriteBody(store.RunDirectory, "frame.msgpack", original, true, null, "application/msgpack", sanitized);
                var stored = File.ReadAllBytes(Path.Combine(store.RunDirectory, artifact.File));
                Check(stored.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret)) < 0, "exact secret survived a binary MessagePack field");
                Check(artifact.Sanitized && artifact.RawSha256 != artifact.StoredSha256, "binary scrubbing was not disclosed");
                Check(WsCodec.Decode(stored)["audio"] is byte[] audio && audio.SequenceEqual(ordinaryAudio), "ordinary audio bytes were changed");
                var benign = WsCodec.Encode(new() { ["event"] = "audio", ["audio"] = ordinaryAudio });
                Check(WsCodec.Sanitize(WsCodec.Decode(benign), benign, redactor).SequenceEqual(benign), "benign MessagePack was unnecessarily repacked");
            })),
            ("binary body capture also removes UTF-8 secret bytes", () =>
            {
                const string secret = "synthetic-非ASCII-secret";
                var redactor = new Redactor(secret);
                var bytes = Encoding.UTF8.GetBytes("before-" + secret + "-after");
                Check(redactor.CleanBinary(bytes).AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret)) < 0, "UTF-8 secret survived a binary capture");
                var benign = new byte[] { 0, 255, 128, 1, 2, 3 };
                Check(redactor.CleanBinary(benign).SequenceEqual(benign), "ordinary binary bytes changed");
            }),
            ("real HTTP evidence captures error bodies and never forwards authorization to assets", () => WithTemporaryDirectory(directory =>
            {
                var redactor = new Redactor("synthetic-secret");
                var store = new EvidenceStore(directory, "offline-self-test", redactor);
                using var client = new HttpClient(new SyntheticHandler());
                var probe = new HttpProber(store, redactor, "synthetic-secret", client);
                var error = probe.GetAsync("auth-error", "balance", new("https://api.fish.audio/wallet/self/api-credit"), true).GetAwaiter().GetResult();
                Check(error.Evidence.HttpStatus == 401 && error.Evidence.Body is { RawBytes: > 0, Complete: true }, "error body was not captured");
                var saved = File.ReadAllText(Path.Combine(error.Evidence.Directory, "body.json"));
                Check(!saved.Contains("synthetic-secret", StringComparison.Ordinal) && saved.Contains("denied", StringComparison.Ordinal), "error body was lost or unsafe");
                Check(!saved.Contains("private-wallet-id", StringComparison.Ordinal) && !saved.Contains("private-user-id", StringComparison.Ordinal), "wallet account identifiers survived");
                Check(error.Evidence.Body!.RawSha256 != error.Evidence.Body.StoredSha256, "sanitized wallet lost its original hash distinction");
                var asset = probe.GetAsync("asset", "preview-assets", new("https://platform.r2.fish.audio/a.mp3"), false, asset: true).GetAwaiter().GetResult();
                using var data = asset.ParseJson();
                Check(data?.RootElement.GetProperty("hadAuthorization").GetBoolean() == false, "asset received account authorization");
            })),
            ("repeated invocations append root indexes without overwriting a run", () => WithTemporaryDirectory(directory =>
            {
                var first = new EvidenceStore(directory, "offline-self-test", new Redactor(""));
                var second = new EvidenceStore(directory, "offline-self-test", new Redactor(""));
                Check(first.RunDirectory != second.RunDirectory && File.Exists(Path.Combine(first.RunDirectory, "manifest.json")), "existing run was overwritten");
                using var root = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
                Check(root.RootElement.GetProperty("runs").GetArrayLength() == 2, "root did not retain both runs");
            })),
        };
        foreach (var (name, test) in tests)
        {
            try { test(); Console.WriteLine($"PASS {name}"); }
            catch { failures++; Console.WriteLine($"FAIL {name}"); }
        }
        Console.WriteLine($"Self-test: {tests.Length - failures}/{tests.Length} passed; offline only.");
        return failures == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Expected the operation to be refused.");
    }

    private sealed class BrokenStream : MemoryStream
    {
        private bool _read;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_read) throw new IOException("Synthetic failure, not a live response.");
            _read = true;
            new byte[] { 97, 98, 99 }.CopyTo(buffer);
            return ValueTask.FromResult(3);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        private readonly List<ManualTimer> _timers = [];
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            _timers.Add(timer);
            return timer;
        }
        internal void Advance(TimeSpan elapsed)
        {
            _utcNow += elapsed;
            foreach (var timer in _timers.ToArray()) timer.FireIfDue();
        }
        private sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset? _due;
            private bool _disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed) return false;
                if (period != Timeout.InfiniteTimeSpan) throw new InvalidOperationException("The offline clock supports one-shot timers only.");
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.GetUtcNow() + dueTime;
                return true;
            }
            internal void FireIfDue()
            {
                if (_disposed || _due is null || clock.GetUtcNow() < _due) return;
                _due = null;
                callback(state);
            }
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private static void WithTemporaryDirectory(Action<string> test)
    {
        var root = Path.GetFullPath(AppContext.BaseDirectory);
        var directory = Path.GetFullPath(Path.Combine(root, "self-test-" + Guid.NewGuid().ToString("N")));
        try { test(directory); }
        finally
        {
            if (directory.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class SyntheticHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var asset = request.RequestUri?.Host == "platform.r2.fish.audio";
            return Task.FromResult(new HttpResponseMessage(asset ? HttpStatusCode.OK : HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(asset ? JsonSerializer.Serialize(new { hadAuthorization = request.Headers.Contains("Authorization") })
                    : "{\"_id\":\"private-wallet-id\",\"user_id\":\"private-user-id\",\"message\":\"denied\",\"nested\":{\"Authorization\":\"Bearer synthetic-secret\"}}", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
