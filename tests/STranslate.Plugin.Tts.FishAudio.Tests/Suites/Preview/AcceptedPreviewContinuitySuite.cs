using STranslate.Plugin.Tts.FishAudio.Caching;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Presentation;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using System.Reflection;
using System.Text.Json;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static TestAssertions;

internal static class AcceptedPreviewContinuitySuite
{
    private const string Id = "11111111111111111111111111111111";
    private const string OtherId = "22222222222222222222222222222222";
    private const string Url = "https://platform.r2.fish.audio/audio/accepted.mp3";

    internal static async Task RunAllAsync(string? focus = null)
    {
        foreach (var testCase in CreateCases(focus))
            await testCase.Run();
    }

    internal static IReadOnlyList<(string Id, Func<Task> Run)> CreateCases(string? focus = null)
    {
        var cases = new List<(string Id, Func<Task> Run)>();
        if (focus is null or "selection")
            foreach (var origin in Enum.GetValues<Origin>())
                cases.Add(($"preview.accepted-continuity/selection/{OriginId(origin)}", () => StaTestHost.RunOnStaThreadAsync(() => SelectionAsync(origin))));
        if (focus is null or "unlike")
            foreach (var applied in new[] { false, true })
                cases.Add(($"preview.accepted-continuity/unlike/{(applied ? "applied" : "unapplied")}", () => StaTestHost.RunOnStaThreadAsync(() => UnlikeAsync(applied))));
        if (focus is null or "browsing")
        {
            cases.Add(("preview.accepted-continuity/browsing/favorites", () => StaTestHost.RunOnStaThreadAsync(FavoriteBrowsingAsync)));
            cases.Add(("preview.accepted-continuity/browsing/by-id", () => StaTestHost.RunOnStaThreadAsync(ByIdBrowsingAsync)));
        }
        if (focus is null or "handoff")
        {
            foreach (var origin in new[] { Origin.ById, Origin.Favorite })
                cases.Add(($"preview.accepted-continuity/handoff/collection/{OriginId(origin)}", () => StaTestHost.RunOnStaThreadAsync(() => CollectionHandoffAsync(origin))));
            foreach (var origin in Enum.GetValues<Origin>())
                cases.Add(($"preview.accepted-continuity/handoff/management/{OriginId(origin)}", () => StaTestHost.RunOnStaThreadAsync(() => ManagementHandoffAsync(origin))));
        }
        if (focus is null or "credentials")
            cases.Add(("preview.accepted-continuity/credentials", () => StaTestHost.RunOnStaThreadAsync(CredentialsAsync)));
        if (focus is null or "lifecycle")
            cases.Add(("preview.accepted-continuity/lifecycle", () => StaTestHost.RunOnStaThreadAsync(LifecycleAsync)));
        if (focus is null or "lease")
            cases.Add(("preview.accepted-continuity/rejected-saves", () => StaTestHost.RunOnStaThreadAsync(RejectedSavesAsync)));
        return cases;
    }

    private static string OriginId(Origin origin) => origin == Origin.ById ? "by-id" : origin.ToString().ToLowerInvariant();

    private static async Task SelectionAsync(Origin origin)
    {
        using var network = OverrideNetworkAvailability(true);
        using var scenario = new Scenario(origin);
        await scenario.PlayAsync(); // Await successful local acceptance, not a visible fake Play counter.
        var buffer = scenario.Player.LastOpenedStream!;
        Tick(scenario, 2);
        var card = scenario.View.SearchResults.Single(item => item.Id == Id);
        if (origin == Origin.Selected)
        {
            scenario.View.SelectVoiceCommand.Execute(scenario.View.SearchResults.Single(item => item.Id == OtherId));
            AssertEqual(OtherId, scenario.Settings.VoiceId, "Apply B persists before checking playback");
            AssertRetained(scenario, buffer);
            scenario.View.ClearVoiceCommand.Execute(null);
            AssertEqual("", scenario.Settings.VoiceId, "Selected Clear persists before checking playback");
            AssertRetained(scenario, buffer);
        }
        scenario.View.SelectVoiceCommand.Execute(card);
        AssertEqual(Id, scenario.Settings.VoiceId, "Apply playing A persists selection without stopping it");
        AssertRetained(scenario, buffer);
        scenario.View.SelectVoiceCommand.Execute(card);
        AssertEqual("", scenario.Settings.VoiceId, "Matching Cross clears applied A");
        AssertRetained(scenario, buffer);
        scenario.View.SelectVoiceCommand.Execute(scenario.View.SearchResults.Single(item => item.Id == OtherId));
        AssertEqual(OtherId, scenario.Settings.VoiceId, "Apply another B persists B");
        AssertEqual("Other", scenario.Settings.CachedVoice?.Title, "Selection saves the matching metadata");
        AssertRetained(scenario, buffer);
        scenario.View.ClearVoiceCommand.Execute(null);
        AssertEqual("", scenario.Settings.VoiceId, "Selected Clear persists the empty selection");
        AssertEqual<CachedVoiceInfo?>(null, scenario.Settings.CachedVoice, "Clear removes the selected snapshot");
        scenario.View.Speed = 1.2;
        scenario.View.Volume = 80;
        scenario.View.BrowseMode = VoiceBrowseMode.ById;
        scenario.View.BrowseMode = VoiceBrowseMode.Search;
        Tick(scenario, 6);
        AssertRetained(scenario, buffer);
        AssertEqual(.6, scenario.View.PreviewProgress, "The same accepted output continues device progress");
    }

    private static async Task UnlikeAsync(bool applied)
    {
        using var network = OverrideNetworkAvailability(true);
        using var scenario = new Scenario(Origin.Favorite, applied ? Id : OtherId);
        var origin = scenario.Card!;
        await scenario.PlayAsync();
        var buffer = scenario.Player.LastOpenedStream!;
        // No other matching presentation remains when the actual Unlike command removes this card.
        scenario.View.SearchResults = [];
        scenario.View.ToggleFavoriteCommand.Execute(origin);
        AssertEqual(0, scenario.View.FavoriteCount, "Unlike persists removal of the final bookmark");
        AssertEqual("[]", File.ReadAllText(scenario.FavoritePath), "The durable IDs reflect Unlike");
        AssertEqual(applied ? Id : OtherId, scenario.Settings.VoiceId, "Bookmark removal never changes selection");
        AssertEqual(false, origin.IsPreviewActive, "The removed card becomes idle");
        AssertEqual(0d, origin.PreviewProgress, "The removed card releases progress");
        AssertRetained(scenario, buffer);
        await scenario.View.ToggleSearchItemPreviewCommand.ExecuteAsync(origin);
        AssertRetained(scenario, buffer);
        Tick(scenario, 5);
        var replacement = Card(Id);
        replacement.SampleAudioUrl = null;
        scenario.View.SearchResults = [replacement];
        AssertEqual(true, replacement.IsBeingPreviewed, "A later matching card projects accepted audio without sample metadata");
        AssertEqual(.5, replacement.PreviewProgress, "A later card receives current output progress");
        await scenario.View.ToggleSearchItemPreviewCommand.ExecuteAsync(replacement);
        AssertReleased(scenario, buffer);
    }

    private static async Task FavoriteBrowsingAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        using var scenario = new Scenario(Origin.Favorite, favoriteIds: Enumerable.Range(1, 6).Select(i => i.ToString("x32")).ToArray());
        var origin = scenario.View.FavoriteResults.Single(item => item.Id == "00000000000000000000000000000001");
        await scenario.View.ToggleSearchItemPreviewCommand.ExecuteAsync(origin);
        scenario.CaptureAcceptedTimer();
        var buffer = scenario.Player.LastOpenedStream!;
        var playingId = origin.Id;
        var detailRequests = scenario.Http.GetCallCount;
        scenario.View.ToggleFavoriteCommand.Execute(scenario.View.SearchResults.Single(item => item.Id == OtherId));
        AssertEqual(1, scenario.View.FavoritePage, "Liking another voice retains the current page");
        AssertEqual(OtherId, scenario.View.FavoriteResults[0].Id, "The newly liked other voice appears first");
        AssertEqual(false, scenario.View.FavoriteResults.Contains(origin), "The global reorder shifts the accepted origin off page one");
        AssertEqual(false, origin.IsPreviewActive, "The shifted-off-page object releases its presentation state");
        AssertEqual(detailRequests, scenario.Http.GetCallCount, "Reordering alone does not refresh preview metadata");
        AssertRetained(scenario, buffer, playingId);
        scenario.View.FavoriteNextPageCommand.Execute(null);
        var final = scenario.View.FavoriteResults.Single();
        AssertEqual(playingId, final.Id, "The accepted origin is now the only second-page item");
        AssertEqual(true, final.IsBeingPreviewed, "The new current presentation projects the existing accepted output");
        scenario.View.FavoritePrevPageCommand.Execute(null);
        scenario.View.FavoritePageInput = "2";
        scenario.View.CommitFavoritePageInputCommand.Execute(null);
        final = scenario.View.FavoriteResults.Single(item => item.Id == final.Id);
        scenario.View.FavoritePrevPageCommand.Execute(null);
        AssertEqual(false, final.IsPreviewActive, "Leaving a Favorite page retires the old presentation object");
        AssertRetained(scenario, buffer, playingId);
        scenario.View.FavoritePageInput = "2";
        scenario.View.CommitFavoritePageInputCommand.Execute(null);
        AssertRetained(scenario, buffer, playingId);
        scenario.View.FavoritePrevPageCommand.Execute(null);
        scenario.View.FavoriteNextPageCommand.Execute(null);
        AssertRetained(scenario, buffer, playingId);
        scenario.View.ToggleFavoriteCommand.Execute(scenario.View.FavoriteResults.Single(item => item.Id == playingId));
        AssertEqual(1, scenario.View.FavoritePage, "Removing the final-page card clamps paging");
        AssertRetained(scenario, buffer, playingId);
        File.WriteAllText(scenario.FavoritePath, "[]");
        scenario.View.SwitchToFavoritesCommand.Execute(null);
        AssertEqual(0, scenario.View.FavoriteCount, "Favorites entry reloads external bookmark changes");
        AssertRetained(scenario, buffer, playingId);
        scenario.Player.RaiseEnded();
        AssertReleased(scenario, buffer);
    }

    private static async Task ByIdBrowsingAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        using var scenario = new Scenario(Origin.ById);
        await scenario.PlayAsync();
        var buffer = scenario.Player.LastOpenedStream!;
        var removed = scenario.Card!;
        InputClearSuite.Command(scenario.View, "ClearVoiceIdInputCommand").Execute(null);
        AssertEqual(0, scenario.View.ByIdResults.Count, "By-ID clear removes its result");
        AssertEqual(false, removed.IsPreviewActive, "By-ID removed object is idle");
        AssertRetained(scenario, buffer);
        scenario.View.VoiceIdInput = Id;
        await scenario.View.SubmitVoiceIdCommand.ExecuteAsync(null);
        var current = scenario.View.ByIdResults.Single(item => item.Id == Id);
        AssertEqual(false, ReferenceEquals(current, removed), "Requery publishes a new object");
        AssertEqual(true, current.IsBeingPreviewed, "Requery projects accepted playback");
        await scenario.View.ToggleSearchItemPreviewCommand.ExecuteAsync(removed);
        AssertRetained(scenario, buffer);
        current.SampleAudioUrl = null;
        await scenario.View.ToggleSearchItemPreviewCommand.ExecuteAsync(current);
        AssertReleased(scenario, buffer);
    }

    private static async Task CollectionHandoffAsync(Origin origin)
    {
        using var network = OverrideNetworkAvailability(true);
        foreach (var boundary in new[] { "open", "play", "accepted" })
        {
            using var scenario = new Scenario(origin);
            var removed = scenario.Card!;
            void Remove()
            {
                if (origin == Origin.Favorite) scenario.View.ToggleFavoriteCommand.Execute(removed);
                else InputClearSuite.Command(scenario.View, "ClearVoiceIdInputCommand").Execute(null);
            }
            Hook(scenario, boundary, Remove);
            await scenario.PlayAsync();
            AssertEqual(boundary == "accepted" ? Id : null, scenario.View.PreviewingVoiceId,
                "Only successful Play return detaches accepted audio from collection membership");
            AssertEqual(false, removed.IsPreviewActive, "Removed handoff card releases its state");
            if (boundary == "accepted") AssertRetained(scenario, scenario.Player.LastOpenedStream!);
            else AssertReleased(scenario, scenario.Player.LastOpenedStream!);
        }
        using (var scenario = new Scenario(origin))
        {
            var held = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken token = default;
            scenario.Http.GetAsStreamAsyncHandler = (_, _, ct) => { token = ct; return held.Task; };
            var pending = scenario.PlayAsync();
            if (origin == Origin.Favorite) scenario.View.ToggleFavoriteCommand.Execute(scenario.Card);
            else InputClearSuite.Command(scenario.View, "ClearVoiceIdInputCommand").Execute(null);
            AssertEqual(true, token.IsCancellationRequested, "Removing the exact pending nonsearch card cancels its resource request");
            using var late = new MemoryStream([8, 9]);
            held.SetResult(late);
            await pending;
            AssertEqual(false, late.CanRead, "Canceled nonsearch resource streams are disposed");
            AssertEqual(0, scenario.Player.PlayCallCount, "Late nonsearch resources cannot start output");
            AssertEqual<string?>(null, scenario.View.PreparingPreviewVoiceId, "Late completion cannot restore preparing state");
        }
    }

    private static async Task ManagementHandoffAsync(Origin origin)
    {
        using var network = OverrideNetworkAvailability(true);
        foreach (var boundary in new[] { "open", "play", "accepted" })
            foreach (var change in new[] { "apply", "clear", "key" })
            {
                using var scenario = new Scenario(origin, Id);
                Hook(scenario, boundary, () =>
                {
                    if (change == "key") scenario.View.ApiKey = "next-accepted-key";
                    else if (change == "clear") scenario.View.ClearVoiceCommand.Execute(null);
                    else scenario.View.SelectVoiceCommand.Execute(scenario.View.SearchResults.Single(item => item.Id == OtherId));
                });
                await scenario.PlayAsync();
                AssertEqual(boundary == "accepted" ? Id : null, scenario.View.PreviewingVoiceId,
                    "Settings changes retire preparation but preserve already accepted local output");
                if (boundary == "accepted")
                {
                    AssertRetained(scenario, scenario.Player.LastOpenedStream!);
                    Tick(scenario, 3);
                    AssertEqual(.3, scenario.View.PreviewProgress, "Accepted reentrancy retains the current progress timer owner");
                }
                else AssertReleased(scenario, scenario.Player.LastOpenedStream!);
            }
        using (var scenario = new Scenario(origin, Id))
        {
            var held = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? successor = null;
            scenario.Http.GetAsStreamAsyncHandler = (_, _, token) =>
            {
                token.Register(() =>
                {
                    scenario.Http.GetAsStreamAsyncHandler = null;
                    successor = scenario.View.ToggleSearchItemPreviewCommand.ExecuteAsync(scenario.View.SearchResults.Single(item => item.Id == OtherId));
                });
                return held.Task;
            };
            var pending = scenario.PlayAsync();
            scenario.View.ClearVoiceCommand.Execute(null);
            if (successor is not null) await successor;
            AssertEqual(OtherId, scenario.View.PreviewingVoiceId, "A retiring pending cancellation callback cannot Stop its accepted successor");
            using var late = new MemoryStream([8, 9]);
            held.SetResult(late);
            await pending;
            AssertEqual(false, late.CanRead, "A predecessor still releases its late resource after reentrant replacement");
            AssertEqual(OtherId, scenario.View.PreviewingVoiceId, "The predecessor continuation cannot retire its successor");
            AssertEqual(0, scenario.Player.DisposeCallCount, "Only the successor owns its accepted player");
        }
    }

    private static async Task CredentialsAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        using var scenario = new Scenario(Origin.Search, apiKey: "key-a");
        await scenario.PlayAsync();
        var buffer = scenario.Player.LastOpenedStream!;
        foreach (var key in new[] { "key-b", "key-a", "" })
        {
            var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken token = default;
            scenario.Http.GetAsyncHandler = (_, _, ct) => { token = ct; return response.Task; };
            var search = scenario.View.SearchVoicesCommand.ExecuteAsync(null);
            scenario.View.ApiKey = key;
            AssertEqual(key, scenario.Settings.ApiKey, "The new accepted credential persists");
            AssertEqual(true, token.IsCancellationRequested, "Credential changes cancel pending old-key work");
            response.SetResult("{\"total\":0,\"items\":[]}");
            await search;
            AssertEqual(2, scenario.View.SearchResults.Count, "Canceled old-key completion cannot publish");
            AssertRetained(scenario, buffer);
        }
        Tick(scenario, 7);
        var card = scenario.View.SearchResults.Single(item => item.Id == Id);
        card.SampleAudioUrl = null;
        await scenario.View.ToggleSearchItemPreviewCommand.ExecuteAsync(card);
        AssertReleased(scenario, buffer);
        AssertEqual(true, scenario.Http.GetStreamOptionsByUrl.All(call => call.Options?.Headers is null || !call.Options.Headers.ContainsKey("Authorization")),
            "Accepted audio and public samples carry no reusable credential authority");
    }

    private static async Task LifecycleAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        foreach (var terminal in new[] { "end", "failure", "progress", "dispose" })
        {
            using var scenario = new Scenario(Origin.Favorite);
            await scenario.PlayAsync();
            var buffer = scenario.Player.LastOpenedStream!;
            scenario.View.ToggleFavoriteCommand.Execute(scenario.Card);
            AssertRetained(scenario, buffer);
            if (terminal == "end") scenario.Player.RaiseEnded();
            else if (terminal == "failure") scenario.Player.RaiseFailed(new IOException("Fake output failure"));
            else if (terminal == "progress") { scenario.Player.ThrowOnDuration = true; Tick(scenario, 4); }
            else scenario.View.Dispose();
            AssertReleased(scenario, buffer);
            scenario.Player.RaiseEnded();
            scenario.Player.RaiseFailed();
            AssertEqual(1, scenario.Player.DisposeCallCount, "Late old device events cannot release twice");
        }
    }

    private static async Task RejectedSavesAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        foreach (var retired in new[] { false, true })
        {
            using var scenario = new Scenario(Origin.Search, apiKey: "accepted", leased: true);
            await scenario.PlayAsync();
            var buffer = scenario.Player.LastOpenedStream!;
            var identity = scenario.Settings.CredentialIdentity;
            if (retired) SettingsStore.Retire(scenario.Settings, scenario.Lease!.Value);
            else scenario.Context.SaveSettings = _ => throw new IOException("Fake storage failure");
            try { scenario.View.ApiKey = "rejected"; }
            catch (IOException) when (!retired) { }
            try { scenario.View.SelectVoiceCommand.Execute(scenario.View.SearchResults.Single(item => item.Id == OtherId)); }
            catch (IOException) when (!retired) { }
            AssertEqual("", scenario.Settings.VoiceId, "Rejected Apply preserves accepted selection");
            if (retired)
            {
                scenario.View.ToggleFavoriteCommand.Execute(scenario.View.FavoriteResults.Single(item => item.Id == Id));
                AssertEqual(1, scenario.View.FavoriteCount, "Retired bookmark leases preserve the existing membership");
            }
            else
            {
                File.Delete(scenario.FavoritePath);
                Directory.CreateDirectory(scenario.FavoritePath);
                scenario.View.ToggleFavoriteCommand.Execute(scenario.View.FavoriteResults.Single(item => item.Id == Id));
                AssertEqual(1, scenario.View.FavoriteCount, "Failed bookmark persistence preserves displayed membership");
                AssertEqual(true, scenario.View.FavoritesError is not null, "A failed bookmark save is reported");
            }
            AssertEqual("accepted", scenario.Settings.ApiKey, "Rejected saves preserve accepted credentials");
            AssertEqual(true, ReferenceEquals(identity, scenario.Settings.CredentialIdentity), "Rejected saves preserve authorization identity");
            AssertRetained(scenario, buffer);
        }
    }

    private static void Hook(Scenario scenario, string boundary, Action action)
    {
        if (boundary == "open") scenario.Player.OnOpen = action;
        else if (boundary == "play") scenario.Player.OnPlay = action;
        else
        {
            var invoked = false;
            scenario.View.PropertyChanged += (_, e) =>
            {
                if (invoked || e.PropertyName != nameof(SettingsViewModel.PreviewingVoiceId) || scenario.View.PreviewingVoiceId != Id) return;
                invoked = true;
                action();
            };
        }
    }

    private static void Tick(Scenario scenario, int seconds)
    {
        scenario.Player.Position = TimeSpan.FromSeconds(seconds);
        var controller = typeof(SettingsViewModel).GetField("_previewPlayback", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scenario.View)!;
        typeof(PreviewPlaybackController).GetMethod("OnPreviewTick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, [null, EventArgs.Empty]);
    }

    private static void AssertRetained(Scenario scenario, Stream buffer, string id = Id)
    {
        AssertEqual(id, scenario.View.PreviewingVoiceId, "Voice management preserves accepted playback");
        AssertEqual(1, scenario.Player.PlayCallCount, "Accepted output never restarts");
        AssertEqual(0, scenario.Player.StopCallCount, "Voice management never stops accepted output");
        AssertEqual(0, scenario.Player.DisposeCallCount, "Voice management never disposes accepted output");
        AssertEqual(true, ReferenceEquals(buffer, scenario.Player.LastOpenedStream) && buffer.CanRead, "The exact accepted buffer remains readable");
        AssertEqual(true, ((MemoryStream)buffer).ToArray().SequenceEqual(scenario.Player.LastOpenedBytes!), "Accepted bytes remain intact");
        AssertEqual(1, scenario.Http.GetAsStreamCallCount, "Voice management never requests replacement audio");
        var timer = scenario.CurrentTimer;
        AssertEqual(true, timer is not null && timer.IsEnabled && ReferenceEquals(scenario.AcceptedTimer, timer),
            "The exact accepted progress timer remains active");
    }

    private static void AssertReleased(Scenario scenario, Stream buffer)
    {
        AssertEqual<string?>(null, scenario.View.PreviewingVoiceId, "Retired output clears playback");
        AssertEqual(1, scenario.Player.StopCallCount, "Retired output stops once");
        AssertEqual(1, scenario.Player.DisposeCallCount, "Retired output disposes once");
        AssertEqual(false, buffer.CanRead, "Retired output releases its buffer");
    }

    private static VoiceSearchItem Card(string id) => new() { Id = id, Title = id == Id ? "Accepted" : "Other", SampleAudioUrl = Url };
    private enum Origin { Selected, Search, ById, Favorite }

    private sealed class Scenario : IDisposable
    {
        internal Settings Settings { get; }
        internal SettingsWriteLease? Lease { get; }
        internal ContextProxy Context { get; }
        internal TestHttpServiceProxy Http { get; }
        internal TestPreviewAudioPlayer Player { get; } = new() { Duration = TimeSpan.FromSeconds(10) };
        internal SettingsViewModel View { get; }
        internal VoiceSearchItem? Card { get; }
        internal string FavoritePath { get; }
        internal System.Windows.Threading.DispatcherTimer? AcceptedTimer { get; private set; }
        internal System.Windows.Threading.DispatcherTimer? CurrentTimer
        {
            get
            {
                var controller = typeof(SettingsViewModel).GetField("_previewPlayback", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(View)!;
                return (System.Windows.Threading.DispatcherTimer?)typeof(PreviewPlaybackController).GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller);
            }
        }
        private readonly Origin _origin;

        internal Scenario(Origin origin, string selectedId = "", string apiKey = "", bool leased = false, string[]? favoriteIds = null)
        {
            _origin = origin;
            var directory = CreateTempDirectory();
            FavoritePath = Path.Combine(directory, FavoriteVoiceStore.FileName);
            File.WriteAllText(FavoritePath, JsonSerializer.Serialize(favoriteIds ?? [Id]));
            var settings = new Settings { ApiKey = apiKey, VoiceId = origin == Origin.Selected ? Id : selectedId,
                CachedVoice = new() { Title = "Accepted", SampleAudioUrl = Url } };
            var (service, http) = TestHttpServiceProxy.Create();
            Http = http;
            var context = CreateContext(settings: settings, httpService: service, pluginSettingsDirectoryPath: directory);
            Context = (ContextProxy)(object)context;
            if (leased) { settings = SettingsStore.Load(context, DateTimeOffset.UtcNow, out var lease); Lease = lease; }
            Settings = settings;
            var cache = new VoiceMetadataCache();
            foreach (var id in (favoriteIds ?? [Id]).Append(Id).Distinct())
                cache.Put(VoiceMetadata.FromCachedVoice(id, new() { Title = "Accepted", SampleAudioUrl = Url }), VoiceMetadataCache.CredentialScope(apiKey));
            View = new SettingsViewModel(context, settings, null, null, settingsWriteLease: Lease,
                previewAudioPlayerFactory: () => Player, voiceMetadataCache: cache);
            View.SearchResults = [AcceptedPreviewContinuitySuite.Card(Id), AcceptedPreviewContinuitySuite.Card(OtherId)];
            if (origin == Origin.ById) { View.VoiceIdInput = Id; View.SubmitVoiceIdCommand.ExecuteAsync(null).GetAwaiter().GetResult(); }
            Card = origin switch
            {
                Origin.Search => View.SearchResults.Single(item => item.Id == Id),
                Origin.ById => View.ByIdResults.Single(item => item.Id == Id),
                Origin.Favorite when favoriteIds is null => View.FavoriteResults.Single(item => item.Id == Id),
                _ => null,
            };
        }

        internal async Task PlayAsync()
        {
            if (_origin == Origin.Selected) await View.ToggleDisplayPreviewCommand.ExecuteAsync(null);
            else await View.ToggleSearchItemPreviewCommand.ExecuteAsync(Card);
            CaptureAcceptedTimer();
        }
        internal void CaptureAcceptedTimer() => AcceptedTimer = CurrentTimer;
        public void Dispose() => View.Dispose();
    }
}
