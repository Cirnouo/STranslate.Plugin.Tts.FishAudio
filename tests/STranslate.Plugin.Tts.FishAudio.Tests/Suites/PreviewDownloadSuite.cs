using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Presentation;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static TestAssertions;

internal static partial class PreviewSuite
{
    internal static async Task RunBoundedPreviewRegressionTestsAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        foreach (var search in new[] { false, true })
        {
            foreach (var size in new[] { 0, PreviewAudioDownloader.MaximumBytes + 128 })
            {
                using var scenario = new PreviewScenario(search);
                var stream = new CountingPreviewStream(size);
                scenario.Http.GetStreamResponse = stream;
                await scenario.PlayAsync();
                AssertEqual(1, scenario.Http.GetAsStreamCallCount, "A valid visible preview target must reach the host downloader");
                AssertEqual(0, scenario.Player.PlayCallCount, "Empty or oversized audio must not create playback");
                AssertEqual(true, stream.Disposed, "Rejected resource stream must be disposed");
                AssertEqual(true, stream.BytesRead <= PreviewAudioDownloader.MaximumBytes + 1, "Copy must read at most cap plus one byte");
                AssertEqual(SettingsViewModel.PreviewPlaybackFailedKey, scenario.Snackbar.LastError, "Download rejection should use the playback error");
                AssertEqual(null, scenario.ViewModel.PreparingPreviewVoiceId, "Rejected downloads must clear the preparation indicator");
            }
            using (var scenario = new PreviewScenario(search))
            {
                var stream = new CountingPreviewStream(PreviewAudioDownloader.MaximumBytes);
                scenario.Http.GetStreamResponse = stream;
                scenario.Player.OnOpen = () =>
                {
                    AssertEqual(true, scenario.Player.LastOpenedStream?.CanSeek, "Local playback requires a seekable buffer");
                    AssertEqual(0L, scenario.Player.LastOpenedStream?.Position, "Local buffer must rewind before Open");
                };
                await scenario.PlayAsync();
                AssertEqual(1, scenario.Player.PlayCallCount, "An exact-cap resource must remain accepted");
                AssertEqual(true, stream.Disposed, "Network stream should close before local playback");
            }
            foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound })
            {
                using var scenario = new PreviewScenario(search);
                scenario.Http.GetResponseJson = RefreshedPreviewJson(scenario.Id);
                scenario.Http.GetAsStreamAsyncHandler = (_, _, _) => scenario.Http.GetAsStreamCallCount == 1
                    ? Task.FromException<Stream>(new HttpRequestException("Private signed URL must not be logged", null, status))
                    : Task.FromResult<Stream>(new MemoryStream([7, 8, 9]));
                await scenario.PlayAsync();
                AssertEqual(2, scenario.Http.GetAsStreamCallCount, "403/404 should retry the refreshed resource once");
                AssertEqual(1, scenario.Http.GetCallCount, "403/404 should refresh voice detail once");
                AssertEqual(1, scenario.Player.PlayCallCount, "Refreshed sample should play locally");
                AssertEqual("Bearer private-preview-key", AssertHeaders(scenario.Http.LastGetOptions, "Detail refresh headers")["Authorization"], "Metadata refresh should use the configured value while resources stay unauthenticated");
                AssertEqual(true, scenario.Http.GetStreamOptionsByUrl.All(call => call.Options?.Headers is null || !call.Options.Headers.ContainsKey("Authorization")), "No resource request may receive authorization");
            }
            foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.InternalServerError, HttpStatusCode.Forbidden })
            {
                using var scenario = new PreviewScenario(search);
                scenario.Http.GetResponseJson = RefreshedPreviewJson(scenario.Id);
                scenario.Http.GetAsStreamAsyncHandler = (_, _, _) => Task.FromException<Stream>(
                    new HttpRequestException("https://user:private@platform.r2.fish.audio/a?X-Amz-Signature=private-signature", null, status));
                await scenario.PlayAsync();
                AssertEqual(status == HttpStatusCode.Forbidden ? 2 : 1, scenario.Http.GetAsStreamCallCount, "Only 403/404 may retry, without a loop");
                AssertEqual(status == HttpStatusCode.Forbidden ? 1 : 0, scenario.Http.GetCallCount, "Refresh must be bounded");
                AssertEqual(0, scenario.Player.PlayCallCount, "Failed download must not use stale bytes");
                AssertEqual(false, scenario.Logger.Contains(LogLevel.Warning, "private-signature"), "Credential exception details must not enter plugin logs");
            }
            await PreviewPendingCancellationAsync(search, "second-click");
            await PreviewPendingCancellationAsync(search, "voice-change");
            await PreviewPendingCancellationAsync(search, "dispose");
        }
        await PreviewPendingCancellationAsync(true, "results-replaced");
        await PreviewSwitchEntryRejectsLateDownloadAsync();
        await PreviewPreparationUsesOneDeadlineAsync();
        PreviewLocalPlayerStateAndFailures();
        using (var scenario = new PreviewScenario(true))
        {
            await scenario.PlayAsync();
            scenario.ViewModel.SearchResults = [];
            AssertEqual(null, scenario.ViewModel.PreviewingVoiceId, "Removing an active search result must stop its preview");
        }
    }

    private static async Task PreviewPendingCancellationAsync(bool search, string trigger)
    {
        using var scenario = new PreviewScenario(search);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        scenario.Http.GetAsStreamAsyncHandler = (_, _, token) =>
        {
            token.Register(() => cancelled = true);
            started.SetResult();
            return release.Task; // Deliberately ignore cancellation, then return a late stream.
        };
        var first = scenario.PlayAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(scenario.Id, scenario.ViewModel.PreparingPreviewVoiceId, "Pending downloads must show the preparation indicator");
        if (trigger == "second-click") await scenario.PlayAsync();
        else if (trigger == "voice-change") scenario.ViewModel.VoiceId = "22222222222222222222222222222222";
        else if (trigger == "results-replaced") scenario.ViewModel.SearchResults = [];
        else scenario.ViewModel.Dispose();
        AssertEqual(true, cancelled, "Stop/switch/removal/dispose must cancel the pending host request");
        AssertEqual(null, scenario.ViewModel.PreparingPreviewVoiceId, "Stop/switch/removal/dispose must clear preparation before the held response returns");
        AssertEqual(false, scenario.ViewModel.IsDisplayVoicePreparing, "Stop/switch/removal/dispose must clear selected preparation");
        AssertEqual(false, scenario.Item.IsPreparingPreview, "Stop/switch/removal/dispose must clear search preparation");
        var late = new CountingPreviewStream(3);
        release.SetResult(late);
        await first.WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(true, late.Disposed, "A late resource returned after cancellation must be disposed");
        AssertEqual(0, scenario.Player.PlayCallCount, "A stale download must never start playback");
        AssertEqual(null, scenario.Snackbar.LastError, "User cancellation must remain quiet");
    }

    private static async Task PreviewSwitchEntryRejectsLateDownloadAsync()
    {
        using var scenario = new PreviewScenario(false);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        scenario.Http.GetAsStreamAsyncHandler = (_, _, _) =>
        {
            if (scenario.Http.GetAsStreamCallCount == 1) { started.SetResult(); return release.Task; }
            return Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
        };
        var first = scenario.PlayAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var item = new VoiceSearchItem { Id = "22222222222222222222222222222222", SampleAudioUrl = PreviewScenario.Url };
        scenario.ViewModel.SearchResults = [item];
        await scenario.ViewModel.ToggleSearchItemPreviewCommand.ExecuteAsync(item);
        var late = new CountingPreviewStream(3);
        release.SetResult(late);
        await first.WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(item.Id, scenario.ViewModel.PreviewingVoiceId, "Late selected download must not replace the new search preview");
        AssertEqual(1, scenario.Player.PlayCallCount, "Only the current entry may play");
        AssertEqual(true, late.Disposed, "Stale cross-entry response must close");
    }

    private static async Task PreviewPreparationUsesOneDeadlineAsync()
    {
        var clock = new PreviewManualTimeProvider();
        var settings = new Settings { VoiceId = "11111111111111111111111111111111", CachedVoice = new() { SampleAudioUrl = CreateExpiredPreviewUrl("budget.mp3") } };
        var (service, http) = TestHttpServiceProxy.Create();
        var snackbar = new TestSnackbar();
        var body = new BlockingPreviewStream();
        CancellationToken detailToken = default;
        http.GetAsyncHandler = (_, _, token) =>
        {
            detailToken = token;
            clock.Advance(TimeSpan.FromSeconds(6));
            return Task.FromResult(RefreshedPreviewJson(settings.VoiceId));
        };
        http.GetAsStreamAsyncHandler = (_, _, token) =>
        {
            AssertEqual(detailToken, token, "Refresh and download must share the same cancellation token");
            clock.Advance(TimeSpan.FromSeconds(3));
            return Task.FromResult<Stream>(body);
        };
        var plays = 0;
        string? preparingVoiceId = null;
        using var coordinator = new PreviewRefreshCoordinator(CreateContext(snackbar, settings, service),
            () => settings.VoiceId, () => settings.CachedVoice?.SampleAudioUrl,
            () => settings.VoiceId, () => settings.VoiceId, () => [], () => null, () => { },
            (_, buffer) => { plays++; buffer.Dispose(); }, update => update(settings), _ => { }, (_, _, _, _) => "", () => false, clock,
            applyPreparingState: id => preparingVoiceId = id);
        var pending = coordinator.ToggleDisplayPreviewAsync();
        await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(settings.VoiceId, preparingVoiceId, "The shared refresh/download deadline must keep preparation visible while its body is pending");
        clock.Advance(TimeSpan.FromSeconds(5));
        AssertEqual(false, body.Token.IsCancellationRequested, "The whole budget should still have one second remaining");
        clock.Advance(TimeSpan.FromSeconds(1));
        await pending.WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(true, body.Token.IsCancellationRequested, "Body reading must stop at the original 15-second deadline");
        AssertEqual(true, body.Disposed, "Timed-out body must close");
        AssertEqual(0, plays, "A timed-out preparation must not start a player");
        AssertEqual(null, preparingVoiceId, "The operation deadline must clear preparation after failure");
        AssertEqual(SettingsViewModel.PreviewPlaybackFailedKey, snackbar.LastError, "A still-current timeout should report failure");
    }

    private static void PreviewLocalPlayerStateAndFailures()
    {
        foreach (var failure in new[] { "none", "factory", "open", "play", "synchronous-end" })
        {
            var player = new TestPreviewAudioPlayer();
            var logger = new TestLogger();
            var failures = 0;
            PreviewPlaybackController? controller = null;
            player.OnOpen = () =>
            {
                AssertEqual(null, controller!.PreviewingVoiceId, "Open must not activate the playing state");
                if (failure == "open") throw new InvalidOperationException("X-Amz-Signature=private");
            };
            player.OnPlay = () =>
            {
                AssertEqual(null, controller!.PreviewingVoiceId, "Play must return before state becomes active");
                if (failure == "play") throw new InvalidOperationException("X-Amz-Signature=private");
                if (failure == "synchronous-end") player.RaiseEnded();
            };
            using (controller = new PreviewPlaybackController(logger, (_, _) => { }, _ => failures++, () => failure == "factory"
                ? throw new InvalidOperationException("X-Amz-Signature=private") : player))
            {
                var buffer = new MemoryStream([1, 2, 3]);
                controller.Toggle("voice", buffer);
                AssertEqual(failure == "none" ? "voice" : null, controller.PreviewingVoiceId, "Only successful Play may leave an active preview");
                AssertEqual(failure is "factory" or "open" or "play" ? 1 : 0, failures, "Failure notification must occur once");
                if (failure != "none") AssertEqual(false, buffer.CanRead, "A failed or ended player must release the local buffer");
                AssertEqual(false, logger.Contains(LogLevel.Warning, "X-Amz"), "Player exception credentials must not be logged");
            }
        }
        var oldPlayer = new TestPreviewAudioPlayer();
        var current = new TestPreviewAudioPlayer();
        var queue = new Queue<Action>();
        var created = 0;
        using var playback = new PreviewPlaybackController(null, (_, _) => { }, _ => throw new Exception("Stale failure surfaced"),
            () => created++ == 0 ? oldPlayer : current, queue.Enqueue);
        playback.Toggle("old", new MemoryStream([1]));
        oldPlayer.RaiseFailed(new Exception("late"));
        playback.Toggle("new", new MemoryStream([2]));
        while (queue.TryDequeue(out var action)) action();
        AssertEqual("new", playback.PreviewingVoiceId, "Queued events from a disposed player must not reset a new player");
    }

    private static string RefreshedPreviewJson(string id) => JsonSerializer.Serialize(new
    { _id = id, title = "Fresh voice", description = "Fresh", cover_image = "", samples = new[] { new { audio = PreviewScenario.FreshUrl } }, task_count = 2 });

    private sealed class PreviewScenario : IDisposable
    {
        internal const string Url = "https://platform.r2.fish.audio/audio/preview.mp3";
        internal const string FreshUrl = "https://platform.r2.fish.audio/audio/fresh.mp3";
        internal readonly string Id = "11111111111111111111111111111111";
        internal TestHttpServiceProxy Http { get; }
        internal TestPreviewAudioPlayer Player { get; } = new();
        internal TestSnackbar Snackbar { get; } = new();
        internal TestLogger Logger { get; } = new();
        internal SettingsViewModel ViewModel { get; }
        private readonly bool _search;
        private readonly VoiceSearchItem _item;
        internal VoiceSearchItem Item => _item;
        internal PreviewScenario(bool search)
        {
            _search = search;
            var (service, http) = TestHttpServiceProxy.Create();
            Http = http;
            var settings = new Settings { ApiKey = "private-preview-key", VoiceId = Id, CachedVoice = new() { Title = "Preview voice", SampleAudioUrl = Url } };
            ViewModel = new SettingsViewModel(CreateContext(Snackbar, settings, service, logger: Logger), settings,
                clearCoverImageCacheAsync: null, clearCoverImageCacheTimeout: null, previewAudioPlayerFactory: () => Player);
            _item = new VoiceSearchItem { Id = Id, SampleAudioUrl = Url };
            ViewModel.SearchResults = [_item];
        }
        internal Task PlayAsync() => (_search ? ViewModel.ToggleSearchItemPreviewCommand.ExecuteAsync(_item)
            : ViewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null)).WaitAsync(TimeSpan.FromSeconds(2));
        public void Dispose() => ViewModel.Dispose();
    }

    private class CountingPreviewStream(int length) : Stream
    {
        internal int BytesRead { get; private set; }
        internal bool Disposed { get; private set; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(buffer.Length, length - BytesRead);
            buffer.Span[..count].Fill(7);
            BytesRead += count;
            return ValueTask.FromResult(count);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).Result;
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class BlockingPreviewStream() : CountingPreviewStream(1)
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken Token { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private sealed class PreviewManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        private readonly List<ManualTimer> _timers = [];
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan elapsed) { _now += elapsed; foreach (var timer in _timers.ToArray()) timer.Fire(); }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            _timers.Add(timer);
            return timer;
        }
        private sealed class ManualTimer(PreviewManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset? _due;
            public bool Change(TimeSpan dueTime, TimeSpan period) { _due = clock.GetUtcNow() + dueTime; return true; }
            internal void Fire() { if (_due is { } due && clock.GetUtcNow() >= due) { _due = null; callback(state); } }
            public void Dispose() => _due = null;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
