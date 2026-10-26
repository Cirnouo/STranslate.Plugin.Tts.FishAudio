using STranslate.Plugin.Tts.FishAudio.Caching;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Presentation;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using System.Reflection;
using System.Text.Json;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static TestAssertions;

internal static class SearchPreviewRetentionSuite
{
    private const string Id = "11111111111111111111111111111111";
    private const string OtherId = "22222222222222222222222222222222";
    private const string Url = "https://platform.r2.fish.audio/audio/retained.mp3";

    internal static async Task RunAllAsync()
    {
        await StaTestHost.RunOnStaThreadAsync(DiscoveryKeepsAcceptedAudioAsync);
        await StaTestHost.RunOnStaThreadAsync(PendingOriginStillRetiresAsync);
        await StaTestHost.RunOnStaThreadAsync(DisplayedPendingOriginMayStillAcceptAsync);
        await StaTestHost.RunOnStaThreadAsync(AcceptanceReentrancyKeepsTheCorrectOwnerAsync);
        await StaTestHost.RunOnStaThreadAsync(AcceptedPublicationCannotRetireItsSuccessorAsync);
        await StaTestHost.RunOnStaThreadAsync(ExplicitReplacementAndRetirementAsync);
        await StaTestHost.RunOnStaThreadAsync(OtherOriginRemovalRetainsAcceptedAudioAsync);
    }

    // Stopping on successful discovery publication, or restarting to project state,
    // would lose the same accepted player/buffer or issue another sample GET.
    private static async Task DiscoveryKeepsAcceptedAudioAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        using var scenario = new Scenario();
        var vm = scenario.View;
        await vm.SearchVoicesCommand.ExecuteAsync(null);
        var origin = vm.SearchResults.Single();
        await vm.ToggleSearchItemPreviewCommand.ExecuteAsync(origin);
        var buffer = scenario.Player.LastOpenedStream!;
        Tick(vm, scenario.Player, 2);
        AssertEqual(.2, origin.PreviewProgress, "Actual output progress projects to the originating card");

        scenario.Http.GetResponseJson = Results(OtherId);
        vm.SearchQuery = "replacement";
        await vm.SearchVoicesCommand.ExecuteAsync(null);
        AssertRetained(scenario, buffer);
        AssertEqual(false, origin.IsPreviewActive, "Removed presentation objects release their active overlay");
        AssertEqual(0d, origin.PreviewProgress, "Removed presentation objects release progress");
        await vm.NextPageCommand.ExecuteAsync(null);
        AssertEqual(2, vm.SearchPage, "Next publishes the actual discovery page");
        AssertRetained(scenario, buffer);
        await vm.PrevPageCommand.ExecuteAsync(null);
        AssertEqual(1, vm.SearchPage, "Previous publishes the actual discovery page");
        AssertRetained(scenario, buffer);
        vm.PageInput = "3";
        await vm.CommitPageInputCommand.ExecuteAsync(null);
        AssertEqual(3, vm.SearchPage, "Page entry uses the real navigation command");
        AssertRetained(scenario, buffer);
        await vm.ApplySearchOptionsAsync(vm.SearchLanguages, vm.SearchSort);
        AssertEqual(1, vm.SearchPage, "Unchanged Apply still refreshes page one");
        AssertRetained(scenario, buffer);

        var prior = vm.SearchResults;
        scenario.Http.GetException = new TimeoutException();
        await vm.SearchVoicesCommand.ExecuteAsync(null);
        AssertEqual(true, ReferenceEquals(prior, vm.SearchResults), "Failed search retains successful discovery cards");
        AssertRetained(scenario, buffer);
        scenario.Http.GetException = null;
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken requestToken = default;
        scenario.Http.GetAsyncHandler = (_, _, ct) => { requestToken = ct; return response.Task; };
        var oldSearch = vm.SearchVoicesCommand.ExecuteAsync(null);
        AssertRetained(scenario, buffer);
        scenario.Http.GetAsyncHandler = null;
        scenario.Http.GetResponseJson = Results();
        await vm.ApplySearchOptionsAsync(vm.SearchLanguages, vm.SearchSort);
        AssertEqual(true, requestToken.IsCancellationRequested, "Replacement cancels only obsolete discovery work");
        response.SetResult(Results(OtherId));
        await oldSearch;
        AssertEqual(0, vm.SearchResults.Count, "Canceled discovery cannot restore old results");
        AssertRetained(scenario, buffer);

        Tick(vm, scenario.Player, 6);
        AssertEqual(.6, vm.PreviewProgress, "Retained audio continues reporting device progress with no matching card");
        AssertEqual(0d, origin.PreviewProgress, "Later progress never revisits the removed presentation object");
        scenario.Http.GetResponseJson = Results(Id);
        await vm.SearchVoicesCommand.ExecuteAsync(null);
        var matching = vm.SearchResults.Single();
        AssertEqual(false, ReferenceEquals(origin, matching), "Reappearing results are distinct objects");
        AssertEqual(true, matching.IsBeingPreviewed, "Reappearing same-ID card shows accepted playback");
        AssertEqual(.6, matching.PreviewProgress, "Reappearing same-ID card shows current progress");
        AssertRetained(scenario, buffer);
        await vm.ToggleSearchItemPreviewCommand.ExecuteAsync(origin);
        AssertRetained(scenario, buffer);
        matching.SampleAudioUrl = null;
        await vm.ToggleSearchItemPreviewCommand.ExecuteAsync(matching);
        AssertEqual(null, vm.PreviewingVoiceId, "A current same-ID card can Stop even without a sample");
        AssertEqual(1, scenario.Player.StopCallCount, "Explicit Stop stops exactly once");
        AssertEqual(1, scenario.Player.DisposeCallCount, "Explicit Stop releases the accepted player");
        AssertEqual(false, buffer.CanRead, "Explicit Stop releases the accepted buffer");
        AssertEqual(1, scenario.Http.GetAsStreamCallCount, "Reappearing Stop never redownloads");
        AssertEqual(10, scenario.Http.GetCallCount, "Only the ten explicit discovery operations issue metadata GETs");
    }

    private static async Task PendingOriginStillRetiresAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        foreach (var refresh in new[] { false, true })
        {
            using var scenario = new Scenario();
            var origin = new VoiceSearchItem { Id = Id, SampleAudioUrl = refresh
                ? "https://platform.r2.fish.audio/old.mp3?X-Amz-Date=20000101T000000Z&X-Amz-Expires=60" : Url };
            var duplicate = new VoiceSearchItem { Id = Id, SampleAudioUrl = Url };
            scenario.View.SearchResults = [origin, duplicate];
            var detail = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var audio = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken captured = default;
            scenario.Http.GetAsyncHandler = (_, _, ct) => { captured = ct; return detail.Task; };
            scenario.Http.GetAsStreamAsyncHandler = (_, _, ct) => { captured = ct; return audio.Task; };
            var pending = scenario.View.ToggleSearchItemPreviewCommand.ExecuteAsync(origin);
            // The other same-ID object is retained: equality must not authorize the origin.
            scenario.View.SearchResults = [duplicate];
            AssertEqual(true, captured.IsCancellationRequested, "Removing the genuine pending origin cancels even with another same-ID card");
            AssertEqual(false, origin.IsPreviewActive, "Removed pending origin releases its overlay");
            AssertEqual(false, duplicate.IsPreviewActive, "The surviving duplicate cannot inherit obsolete preparation");
            detail.SetResult(JsonSerializer.Serialize(new { _id = Id, title = "Late", samples = new[] { new { audio = Url } } }));
            using var late = new MemoryStream([1, 2, 3]);
            audio.SetResult(late);
            await pending;
            AssertEqual(0, scenario.Player.PlayCallCount, "Obsolete pending work never starts playback");
            AssertEqual(refresh ? 0 : 1, scenario.Http.GetAsStreamCallCount, "A stale refresh cannot start a sample download");
            if (!refresh) AssertEqual(false, late.CanRead, "The late resource is disposed");
        }
    }

    private static async Task AcceptanceReentrancyKeepsTheCorrectOwnerAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        foreach (var boundary in new[] { "open", "play", "accepted" })
        {
            using var scenario = new Scenario();
            await scenario.View.SearchVoicesCommand.ExecuteAsync(null);
            var origin = scenario.View.SearchResults.Single();
            var replacement = new VoiceSearchItem { Id = Id, SampleAudioUrl = Url };
            void Replace() => scenario.View.SearchResults = [replacement];
            if (boundary == "open") scenario.Player.OnOpen = Replace;
            else if (boundary == "play") scenario.Player.OnPlay = Replace;
            else scenario.View.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingsViewModel.PreviewingVoiceId) && scenario.View.PreviewingVoiceId == Id) Replace();
            };
            await scenario.View.ToggleSearchItemPreviewCommand.ExecuteAsync(origin);
            AssertEqual(boundary == "accepted" ? Id : null, scenario.View.PreviewingVoiceId,
                "Only a successful Play return permits search origin detachment");
            AssertEqual(boundary == "accepted" ? 0 : 1, scenario.Player.DisposeCallCount,
                "Preacceptance removal releases local output; accepted publication preserves it");
            AssertEqual(false, origin.IsPreviewActive, "Reentrant replacement clears the old presentation");
            AssertEqual(boundary == "accepted", replacement.IsBeingPreviewed, "The current same-ID overlay reflects acceptance");
            if (boundary == "accepted")
            {
                var buffer = scenario.Player.LastOpenedStream!;
                Tick(scenario.View, scenario.Player, 4);
                AssertEqual(.4, replacement.PreviewProgress, "Progress survives the reentrant acceptance handoff");
                scenario.Player.RaiseEnded();
                AssertEqual(null, scenario.View.PreviewingVoiceId, "Natural end clears retained playback");
                AssertEqual(false, replacement.IsPreviewActive, "Natural end clears the matching overlay");
                AssertEqual(false, buffer.CanRead, "Natural end releases the retained buffer");
                AssertEqual(1, scenario.Player.StopCallCount, "Natural end stops once");
                AssertEqual(1, scenario.Player.DisposeCallCount, "Natural end disposes once");
            }
        }
    }

    private static async Task DisplayedPendingOriginMayStillAcceptAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        using var scenario = new Scenario();
        await scenario.View.SearchVoicesCommand.ExecuteAsync(null);
        var origin = scenario.View.SearchResults.Single();
        var resource = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken previewToken = default;
        scenario.Http.GetAsStreamAsyncHandler = (_, _, ct) => { previewToken = ct; return resource.Task; };
        var preview = scenario.View.ToggleSearchItemPreviewCommand.ExecuteAsync(origin);
        scenario.Http.GetAsyncHandler = (_, _, _) => metadata.Task;
        scenario.View.SearchQuery = "pending discovery";
        var search = scenario.View.SearchVoicesCommand.ExecuteAsync(null);
        AssertEqual(true, ReferenceEquals(origin, scenario.View.SearchResults.Single()), "Starting discovery keeps the last successful displayed origin");
        AssertEqual(false, previewToken.IsCancellationRequested, "A still-displayed pending origin is not retired by query start alone");
        resource.SetResult(new MemoryStream([1, 2, 3]));
        await preview;
        AssertEqual(Id, scenario.View.PreviewingVoiceId, "Current displayed preparation may accept while discovery is pending");
        metadata.SetResult(Results(OtherId));
        await search;
        AssertRetained(scenario, scenario.Player.LastOpenedStream!);
    }

    private static async Task ExplicitReplacementAndRetirementAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        foreach (var retirement in new[] { "replacement", "credentials", "dispose" })
        {
            using var scenario = new Scenario();
            await scenario.View.SearchVoicesCommand.ExecuteAsync(null);
            await scenario.View.ToggleSearchItemPreviewCommand.ExecuteAsync(scenario.View.SearchResults.Single());
            var buffer = scenario.Player.LastOpenedStream!;
            scenario.View.SearchResults = [];
            AssertRetained(scenario, buffer);
            if (retirement == "credentials")
            {
                scenario.View.ApiKey = "replacement-key";
                AssertRetained(scenario, buffer);
                scenario.View.Dispose();
            }
            else if (retirement == "dispose") scenario.View.Dispose();
            else
            {
                var next = new VoiceSearchItem { Id = OtherId, SampleAudioUrl = Url };
                scenario.View.SearchResults = [next];
                var response = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
                scenario.Http.GetAsStreamAsyncHandler = (_, _, _) => response.Task;
                var pending = scenario.View.ToggleSearchItemPreviewCommand.ExecuteAsync(next);
                AssertEqual(false, buffer.CanRead, "Intentional replacement stops old audio before new preparation");
                AssertEqual(null, scenario.View.PreviewingVoiceId, "Existing replacement timing permits no old/new coexistence");
                scenario.View.SearchResults = [];
                using var late = new MemoryStream([4, 5, 6]);
                response.SetResult(late);
                await pending;
                AssertEqual(false, late.CanRead, "Invalidated replacement request still releases late audio");
            }
            AssertEqual(null, scenario.View.PreviewingVoiceId, "Intentional retirement clears retained accepted playback");
            AssertEqual(1, scenario.Player.StopCallCount, "Intentional retirement stops once");
            AssertEqual(1, scenario.Player.DisposeCallCount, "Intentional retirement disposes once");
            AssertEqual(false, buffer.CanRead, "Intentional retirement releases retained bytes");
        }
    }

    private static async Task AcceptedPublicationCannotRetireItsSuccessorAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        foreach (var retirement in new[] { "replacement", "credentials", "dispose", "end", "stop" })
        {
            using var scenario = new Scenario();
            await scenario.View.SearchVoicesCommand.ExecuteAsync(null);
            var origin = scenario.View.SearchResults.Single();
            Task? successor = null;
            var retired = false;
            scenario.View.PropertyChanged += (_, e) =>
            {
                if (retired || e.PropertyName != nameof(SettingsViewModel.PreviewingVoiceId) || scenario.View.PreviewingVoiceId != Id) return;
                retired = true;
                scenario.View.SearchResults = [];
                if (retirement == "credentials") scenario.View.ApiKey = "replacement-key";
                else if (retirement == "dispose") scenario.View.Dispose();
                else if (retirement == "end") scenario.Player.RaiseEnded();
                else
                {
                    var card = new VoiceSearchItem { Id = retirement == "stop" ? Id : OtherId, SampleAudioUrl = Url };
                    scenario.View.SearchResults = [card];
                    successor = scenario.View.ToggleSearchItemPreviewCommand.ExecuteAsync(card);
                }
            };
            await scenario.View.ToggleSearchItemPreviewCommand.ExecuteAsync(origin);
            if (successor is not null) await successor;
            AssertEqual(retirement == "replacement" ? OtherId : retirement == "credentials" ? Id : null, scenario.View.PreviewingVoiceId,
                "An obsolete acceptance continuation cannot Stop its accepted successor");
            AssertEqual(retirement == "credentials" ? 0 : 1, scenario.Player.DisposeCallCount, "Only playback retirement releases the old player");
            AssertEqual(retirement == "credentials", scenario.Player.LastOpenedStream!.CanRead, "Credential edits preserve accepted local bytes");
            var controller = Playback(scenario.View);
            var timer = typeof(PreviewPlaybackController).GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller);
            AssertEqual(retirement is "replacement" or "credentials", timer is not null, "Only the current accepted owner can install a timer");
            if (retirement == "replacement")
            {
                scenario.Player.RaiseEnded();
                scenario.Player.RaiseFailed(new IOException("old device"));
                AssertEqual(OtherId, scenario.View.PreviewingVoiceId, "Old device callbacks cannot affect the successor");
                AssertEqual(0, scenario.ReplacementPlayer.DisposeCallCount, "Old cleanup cannot dispose the successor");
                scenario.ReplacementPlayer.RaiseEnded();
                AssertEqual(null, scenario.View.PreviewingVoiceId, "The successor still owns its natural end");
            }
        }
    }

    private static async Task OtherOriginRemovalRetainsAcceptedAudioAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        foreach (var favorite in new[] { false, true })
        {
            var directory = CreateTempDirectory();
            File.WriteAllText(Path.Combine(directory, FavoriteVoiceStore.FileName), JsonSerializer.Serialize(new[] { Id }));
            var settings = new Settings();
            var cache = new VoiceMetadataCache();
            cache.Put(VoiceMetadata.FromCachedVoice(Id, new() { Title = "Origin", SampleAudioUrl = Url }));
            var (service, http) = TestHttpServiceProxy.Create();
            var player = new TestPreviewAudioPlayer();
            using var vm = new SettingsViewModel(CreateContext(settings: settings, httpService: service, pluginSettingsDirectoryPath: directory), settings,
                null, null, previewAudioPlayerFactory: () => player, voiceMetadataCache: cache);
            vm.SearchResults = [new VoiceSearchItem { Id = Id, SampleAudioUrl = Url }];
            if (!favorite) { vm.VoiceIdInput = Id; await vm.SubmitVoiceIdCommand.ExecuteAsync(null); }
            var origin = favorite ? vm.FavoriteResults.Single() : vm.ByIdResults.Single();
            await vm.ToggleSearchItemPreviewCommand.ExecuteAsync(origin);
            AssertEqual(Id, vm.PreviewingVoiceId, "The actual nonsearch originating card starts playback");
            if (favorite) vm.ToggleFavoriteCommand.Execute(origin);
            else vm.VoiceIdInput = OtherId;
            AssertEqual(Id, vm.PreviewingVoiceId, "Favorite/By-ID removal preserves accepted playback");
            AssertEqual(0, player.StopCallCount, "Nonsearch removal leaves accepted output running");
            AssertEqual(0, player.DisposeCallCount, "Nonsearch removal retains the player with a surviving same-ID search card");
            AssertEqual(true, player.LastOpenedStream!.CanRead, "Nonsearch removal retains accepted bytes");
            AssertEqual(false, origin.IsPreviewActive, "Removed nonsearch objects release their active state");
            AssertEqual(true, vm.SearchResults.Single().IsBeingPreviewed, "Surviving matching cards show active playback");
            AssertEqual(1, http.GetAsStreamCallCount, "Nonsearch removal never downloads replacement audio");
        }
    }

    private static void Tick(SettingsViewModel vm, TestPreviewAudioPlayer player, int seconds)
    {
        player.Position = TimeSpan.FromSeconds(seconds);
        var controller = Playback(vm);
        typeof(PreviewPlaybackController).GetMethod("OnPreviewTick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(controller, [null, EventArgs.Empty]);
    }

    private static object Playback(SettingsViewModel vm) =>
        typeof(SettingsViewModel).GetField("_previewPlayback", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;

    private static void AssertRetained(Scenario scenario, Stream buffer)
    {
        AssertEqual(Id, scenario.View.PreviewingVoiceId, "Discovery preserves accepted search playback");
        AssertEqual(1, scenario.Player.PlayCallCount, "Discovery never restarts accepted output");
        AssertEqual(0, scenario.Player.StopCallCount, "Discovery never stops accepted output");
        AssertEqual(0, scenario.Player.DisposeCallCount, "Discovery never disposes the accepted player");
        AssertEqual(true, ReferenceEquals(buffer, scenario.Player.LastOpenedStream) && buffer.CanRead, "Discovery keeps the exact accepted buffer alive");
        AssertEqual(1, scenario.Http.GetAsStreamCallCount, "Discovery never redownloads the sample");
    }

    private static string Results(string? id = null) => JsonSerializer.Serialize(new
    {
        total = 24, items = id is null ? Array.Empty<object>() : new object[] { new { _id = id, title = "Voice", samples = new[] { new { audio = Url } } } },
    });

    private sealed class Scenario : IDisposable
    {
        internal TestHttpServiceProxy Http { get; }
        internal TestPreviewAudioPlayer Player { get; } = new() { Duration = TimeSpan.FromSeconds(10) };
        internal TestPreviewAudioPlayer ReplacementPlayer { get; } = new() { Duration = TimeSpan.FromSeconds(10) };
        internal SettingsViewModel View { get; }
        internal Scenario()
        {
            var (service, http) = TestHttpServiceProxy.Create();
            Http = http;
            Http.GetResponseJson = Results(Id);
            var settings = new Settings();
            var created = 0;
            View = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings, null, null,
                previewAudioPlayerFactory: () => created++ == 0 ? Player : ReplacementPlayer);
        }
        public void Dispose() => View.Dispose();
    }
}
