using System.Text.Json;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Caching;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Runtime;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static TestAssertions;

internal static class CredentialDisplayPreservationSuite
{
    private const string Id = "fedcba9876543210fedcba9876543210";

    internal static async Task AcceptedEditsPreserveCardsSnapshotsAndNavigationAsync()
    {
        var directory = CreateTempDirectory();
        var ids = Enumerable.Range(1, 6).Select(number => number.ToString("x32")).Append(Id).ToArray();
        File.WriteAllText(Path.Combine(directory, "favorite-voices.json"), JsonSerializer.Serialize(ids));
        var settings = new Settings { ApiKey = "accepted-a" };
        var cache = new VoiceMetadataCache();
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = """
            {"total":18,"items":[{"_id":"fedcba9876543210fedcba9876543210","title":"Retained voice",
            "description":"Full description","author":{"nickname":"Author"},"task_count":23,
            "samples":[{"audio":"https://platform.r2.fish.audio/retained.mp3"}]}]}
            """;
        var context = CreateContext(settings: settings, httpService: service, pluginSettingsDirectoryPath: directory);
        using var view = new SettingsViewModel(context, settings, null, null, voiceMetadataCache: cache) { SearchQuery = "committed query" };
        await view.SearchVoicesCommand.ExecuteAsync(null);
        await view.NextPageCommand.ExecuteAsync(null);
        view.VoiceIdInput = Id;
        await view.SubmitVoiceIdCommand.ExecuteAsync(null);
        view.SelectVoiceCommand.Execute(view.ByIdResults.Single());
        view.SwitchToFavoritesCommand.Execute(null);
        view.FavoriteNextPageCommand.Execute(null);
        AssertEqual(2, view.SearchPage, "Fixture must retain a noninitial applied search page");
        AssertEqual(2, view.FavoritePage, "Fixture must retain a noninitial favorite page");
        // A resolved local cover must remain exactly as displayed, with its canonical source intact.
        var localCover = Path.Combine(directory, "displayed-cover.png");
        foreach (var card in view.SearchResults.Concat(view.ByIdResults).Concat(view.FavoriteResults))
        {
            card.CoverImage = "https://public-platform.r2.fish.audio/retained.png";
            card.CoverUrl = localCover;
        }
        view.CachedVoiceCoverUrl = localCover;
        view.SearchQuery = "unapplied draft";
        view.PageInput = "page draft";
        view.FavoritePageInput = "favorite draft";
        var search = view.SearchResults;
        var byId = view.ByIdResults;
        var favorites = view.FavoriteResults;
        var cards = search.Concat(byId).Concat(favorites).ToArray();
        var cardStates = cards.Select(card => JsonSerializer.Serialize(card)).ToArray();
        var snapshot = JsonSerializer.Serialize(settings.CachedVoice);
        var selected = SelectedDisplay(view);
        var navigation = Navigation(view);
        var calls = http.GetCallCount;
        var saves = ((ContextProxy)(object)context).SaveCount;

        foreach (var key in new[] { "accepted-ax", "accepted-a", "accepted-", "", " \t" })
        {
            view.ApiKey = key;
            AssertEqual(key, settings.ApiKey, "Each accepted character edit must save the new key");
            AssertEqual(snapshot, JsonSerializer.Serialize(settings.CachedVoice), "Key edits must preserve the selected persisted snapshot");
            AssertEqual(selected, SelectedDisplay(view), "Key edits must preserve every selected display field and favorite state");
            AssertEqual(navigation, Navigation(view), "Key edits must preserve applied pagination, input drafts and browse mode");
            AssertEqual(true, ReferenceEquals(search, view.SearchResults) && ReferenceEquals(byId, view.ByIdResults)
                && ReferenceEquals(favorites, view.FavoriteResults), "Key edits must preserve result collections and card identities");
            for (var index = 0; index < cards.Length; index++)
                AssertEqual(cardStates[index], JsonSerializer.Serialize(cards[index]), "Key edits must preserve card metadata, local cover, preview entry, star and Apply state");
            AssertEqual(calls, http.GetCallCount, "Editing credentials must not start metadata requests");
        }
        AssertEqual(saves + 5, ((ContextProxy)(object)context).SaveCount, "Every distinct accepted draft is persisted once");
        await view.NextPageCommand.ExecuteAsync(null);
        AssertEqual(3, view.SearchPage, "The preserved coordinator must continue from the committed page");
        AssertEqual(true, http.LastGetUrl!.Contains("title=committed%20query", StringComparison.Ordinal), "Paging must retain the applied query rather than consume a new draft");
        AssertEqual("Bearer dummy", AssertHeaders(http.LastGetOptions, "Explicit paging after blank key")["Authorization"], "A later explicit request must use the new accepted blank-key policy");
    }

    internal static async Task PendingMetadataCannotReplaceRetainedDisplayAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        foreach (var operation in new[] { "search", "by-id", "favorite", "preview" })
        {
            var directory = CreateTempDirectory();
            File.WriteAllText(Path.Combine(directory, "favorite-voices.json"), JsonSerializer.Serialize(new[] { Id }));
            var settings = new Settings { ApiKey = "pending-a", VoiceId = Id,
                CachedVoice = new() { Title = "Selected before edit", SampleAudioUrl = PreviewSuite.CreateExpiredPreviewUrl("pending.mp3") } };
            var cache = new VoiceMetadataCache();
            var (service, http) = TestHttpServiceProxy.Create();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken capturedToken = default;
            http.GetAsyncHandler = (_, _, token) =>
            {
                capturedToken = token;
                started.TrySetResult();
                return response.Task; // Deliberately ignores cancellation.
            };
            var context = CreateContext(settings: settings, httpService: service, pluginSettingsDirectoryPath: directory);
            var player = new TestPreviewAudioPlayer();
            using var view = new SettingsViewModel(context, settings, null, null, voiceMetadataCache: cache,
                previewAudioPlayerFactory: () => player);
            view.SearchResults = [new() { Id = Id, Title = "Existing search", Description = "Retain me" }];
            Task pending;
            switch (operation)
            {
                case "search":
                    pending = view.SearchVoicesCommand.ExecuteAsync(null);
                    break;
                case "by-id":
                    view.VoiceIdInput = Id;
                    pending = view.SubmitVoiceIdCommand.ExecuteAsync(null);
                    break;
                case "preview":
                    pending = view.ToggleDisplayPreviewCommand.ExecuteAsync(null);
                    break;
                default:
                    view.SwitchToFavoritesCommand.Execute(null);
                    pending = view.PendingFavoriteMetadataTask;
                    break;
            }
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var search = view.SearchResults;
            var favorite = view.FavoriteResults.Single();
            view.ApiKey = "pending-b";
            AssertEqual(true, capturedToken.IsCancellationRequested, "Accepted key edits cancel the previous metadata operation");
            AssertEqual(false, view.IsSearching || view.IsSubmittingVoiceId || favorite.IsLoadingMetadata, "Canceled metadata operations release busy indicators");
            var display = JsonSerializer.Serialize(new { Search = search, ById = view.ByIdResults, Favorite = favorite, Selected = SelectedDisplay(view) });
            var saves = ((ContextProxy)(object)context).SaveCount;
            response.SetResult(operation == "search"
                ? "{\"total\":1,\"items\":[{\"_id\":\"" + Id + "\",\"title\":\"Late A\"}]}"
                : "{\"_id\":\"" + Id + "\",\"title\":\"Late A\"}");
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
            AssertEqual(true, ReferenceEquals(search, view.SearchResults) && ReferenceEquals(favorite, view.FavoriteResults.Single()), "Late responses cannot replace retained card objects");
            AssertEqual(display, JsonSerializer.Serialize(new { Search = view.SearchResults, ById = view.ByIdResults, Favorite = favorite, Selected = SelectedDisplay(view) }), "Late responses cannot publish metadata into retained displays");
            AssertEqual("Selected before edit", settings.CachedVoice?.Title, "Late metadata cannot overwrite the retained persisted selection");
            AssertEqual(saves, ((ContextProxy)(object)context).SaveCount, "Late metadata cannot write Settings after the accepted-key change");
            AssertEqual(false, cache.TryGet(Id, out _, VoiceMetadataCache.CredentialScope("pending-a")), "An invalidated response cannot become a reusable cache entry");
            AssertEqual(false, cache.TryGet(Id, out _, VoiceMetadataCache.CredentialScope("pending-b")), "An invalidated response cannot cross credential cache scopes");
            AssertEqual(0, player.PlayCallCount, "A canceled old-key preview cannot start playback from a late response");
        }
    }

    internal static async Task ReinitializationDoesNotSeedNewCredentialCacheFromRetainedSnapshotAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var cache = new VoiceMetadataCache();
        var stored = new Settings { ApiKey = "reinit-a", VoiceId = Id, CachedVoice = new() { Title = "Persisted display" } };
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = "{\"_id\":\"" + Id + "\",\"title\":\"Remote A\"}";
        var context = CreateContext(settings: stored, httpService: service);
        using var plugin = new Main((owner, settings, lease, cycle) => new SettingsViewModel(owner, settings, null, null,
            startupCreditRefreshCycle: cycle, settingsWriteLease: lease, voiceMetadataCache: cache));
        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var first = plugin.GetOrCreateSettingsViewModel();
        AssertEqual(false, cache.TryGet(Id, out _, VoiceMetadataCache.CredentialScope("reinit-a")), "A persisted selected snapshot is display state, not an authenticated cache observation");
        first.VoiceIdInput = Id;
        await first.SubmitVoiceIdCommand.ExecuteAsync(null);
        first.ApiKey = "reinit-b";
        AssertEqual("Persisted display", stored.CachedVoice?.Title, "Accepted key persistence must retain the stored display for restart");
        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var replacement = plugin.GetOrCreateSettingsViewModel();
        AssertEqual("Persisted display", replacement.CachedVoiceTitle, "Reinitialization displays the preserved selected snapshot");
        AssertEqual(false, cache.TryGet(Id, out _, VoiceMetadataCache.CredentialScope("reinit-b")), "Constructing B cannot relabel A's persisted snapshot into B's cache partition");
        var calls = http.GetCallCount;
        http.GetResponseJson = "{\"_id\":\"" + Id + "\",\"title\":\"Remote B\"}";
        replacement.VoiceIdInput = Id;
        await replacement.SubmitVoiceIdCommand.ExecuteAsync(null);
        AssertEqual(calls + 1, http.GetCallCount, "An explicit lookup after restart must fetch B metadata on its cache miss");
        AssertEqual("Bearer reinit-b", AssertHeaders(http.LastGetOptions, "Reinitialized request")["Authorization"], "The new facade requests metadata using the newly persisted key");
        AssertEqual("Remote B", replacement.ByIdResults.Single().Title, "Explicit lookup may publish newly accepted metadata");
        AssertEqual("Persisted display", replacement.CachedVoiceTitle, "By-ID lookup still must not Apply the newly fetched voice");
        AssertEqual(true, cache.TryGet(Id, out var previous, VoiceMetadataCache.CredentialScope("reinit-a")), "The old partition remains independently available");
        AssertEqual("Remote A", previous.Title, "The old partition must not be overwritten with B's metadata");
        calls = http.GetCallCount;
        replacement.ApiKey = "reinit-a";
        await replacement.SubmitVoiceIdCommand.ExecuteAsync(null);
        AssertEqual(calls, http.GetCallCount, "Returning to A may explicitly reuse its still-valid digest-scoped cache entry");
        AssertEqual("Remote A", replacement.ByIdResults.Single().Title, "Publication identity must not replace cache credential scoping");
    }

    internal static void CredentialRoundTripRejectsQueuedPublications()
    {
        foreach (var publication in new[] { "cache", "startup" })
        {
            var cache = new VoiceMetadataCache();
            var settings = new Settings { ApiKey = "round-trip-a", VoiceId = Id, CachedVoice = new() { Title = "Retained selected" } };
            using var view = new SettingsViewModel(CreateContext(settings: settings), settings, null, null, voiceMetadataCache: cache);
            var card = new VoiceSearchItem { Id = Id, Title = "Retained card" };
            view.SearchResults = [card];
            var queued = new List<Action>();
            SettingsViewModel.UiThreadInvokerOverride = queued.Add;
            try
            {
                if (publication == "cache")
                    cache.Put(VoiceMetadata.FromCachedVoice(Id, new() { Title = "Queued old A" }), VoiceMetadataCache.CredentialScope(settings.ApiKey));
                else
                    view.ApplyRefreshedCachedVoice(Id, new() { Title = "Queued old A" }, settings.ApiKey);
                AssertEqual(1, queued.Count, "The old publication must actually be queued before the credential edit");
                view.ApiKey = "round-trip-ax";
                view.ApiKey = "round-trip-a";
                foreach (var action in queued.ToArray()) action();
                AssertEqual("Retained card", card.Title, "A queued old cache publication cannot revive after A to B to A");
                AssertEqual("Retained selected", view.CachedVoiceTitle, "A queued old startup publication cannot revive after A to B to A");
            }
            finally { SettingsViewModel.UiThreadInvokerOverride = null; }
        }
    }

    internal static async Task FailedSavesPreserveIdentitySubscriptionAndPendingWorkAsync()
    {
        foreach (var leased in new[] { false, true })
        {
            var stored = new Settings { ApiKey = "accepted-a", VoiceId = Id, CachedVoice = new() { Title = "Retained selected" } };
            var (service, http) = TestHttpServiceProxy.Create();
            var context = CreateContext(settings: stored, httpService: service);
            var lease = default(SettingsWriteLease);
            var settings = leased ? SettingsStore.Load(context, DateTimeOffset.UtcNow, out lease) : stored;
            var cache = new VoiceMetadataCache();
            using var view = new SettingsViewModel(context, settings, null, null, voiceMetadataCache: cache,
                settingsWriteLease: leased ? lease : null);
            var card = new VoiceSearchItem { Id = Id, Title = "Retained card" };
            view.SearchResults = [card];
            var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken capturedToken = default;
            http.GetAsyncHandler = (_, _, token) => { capturedToken = token; return response.Task; };
            var pending = view.SearchVoicesCommand.ExecuteAsync(null);
            var credentialIdentity = settings.CredentialIdentity;
            var queued = new List<Action>();
            SettingsViewModel.UiThreadInvokerOverride = queued.Add;
            try
            {
                cache.Put(VoiceMetadata.FromCachedVoice(Id, new() { Title = "Accepted queued metadata" }), VoiceMetadataCache.CredentialScope(settings.ApiKey));
                ((ContextProxy)(object)context).SaveSettings = _ => throw new IOException("Synthetic key save failure");
                try { view.ApiKey = "rejected-key"; throw new Exception("Expected key save failure"); }
                catch (IOException) { }
                AssertEqual("accepted-a", settings.ApiKey, "Failed saves restore the accepted key");
                AssertEqual("accepted-a", view.ApiKey, "Failed saves restore the visible key draft");
                AssertEqual(true, ReferenceEquals(credentialIdentity, settings.CredentialIdentity), "Failed saves cannot rotate credential publication identity");
                AssertEqual(false, capturedToken.IsCancellationRequested, "Failed key saves cannot cancel accepted in-flight work");
                AssertEqual(true, ReferenceEquals(card, view.SearchResults.Single()), "Failed saves preserve displayed card objects");
                AssertEqual("Retained selected", view.CachedVoiceTitle, "Failed saves preserve the selected display");
                foreach (var action in queued.ToArray()) action();
                AssertEqual("Accepted queued metadata", card.Title, "Failed saves keep the accepted cache subscription and queued publication valid");
            }
            finally { SettingsViewModel.UiThreadInvokerOverride = null; }
            response.SetResult("{\"total\":1,\"items\":[{\"_id\":\"" + Id + "\",\"title\":\"Accepted pending response\"}]}");
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
            AssertEqual("Accepted pending response", view.SearchResults.Single().Title, "Accepted pending requests remain publishable after failed key persistence");
            AssertEqual("Bearer accepted-a", AssertHeaders(http.LastGetOptions, "Pending accepted request")["Authorization"], "Failed persistence cannot change request credentials");
        }
    }

    internal static void AcceptedPublicationOwnsIdentityAcrossCopiesAndReentrantSaves()
    {
        var stored = new Settings { ApiKey = "accepted-a", VoiceId = Id, CachedVoice = new() { Title = "Retained" } };
        var context = CreateContext(settings: stored);
        var settings = SettingsStore.Load(context, DateTimeOffset.UtcNow, out var lease);
        using var view = new SettingsViewModel(context, settings, null, null, settingsWriteLease: lease);
        var firstIdentity = settings.CredentialIdentity;
        var snapshot = SettingsStore.CaptureSnapshot(settings);
        AssertEqual(true, ReferenceEquals(firstIdentity, snapshot.CredentialIdentity), "Operation snapshots preserve accepted credential identity");
        AssertEqual(false, JsonSerializer.Serialize(snapshot).Contains("CredentialIdentity", StringComparison.Ordinal), "Runtime identity must never become a persisted settings field");
        var queued = new List<Action>();
        SettingsViewModel.UiThreadInvokerOverride = queued.Add;
        try
        {
            view.ApplyRefreshedCachedVoice(Id, new() { Title = "Still current publication" }, settings.ApiKey);
            view.Speed = 1.25;
            SettingsStore.TryUpdateAndSave(context, settings, lease, candidate => { candidate.CachedVoice = new() { Title = "Ordinary metadata save" }; return true; });
            foreach (var action in queued.ToArray()) action();
            AssertEqual("Still current publication", view.CachedVoiceTitle, "Ordinary saves must leave a queued authenticated publication valid");
        }
        finally { SettingsViewModel.UiThreadInvokerOverride = null; }
        AssertEqual(true, ReferenceEquals(firstIdentity, settings.CredentialIdentity), "Numeric and metadata saves cannot invalidate credential publications");
        object? nestedIdentity = null;
        var reentered = false;
        ((ContextProxy)(object)context).SaveSettings = _ =>
        {
            if (reentered) return;
            reentered = true;
            AssertEqual(true, ReferenceEquals(firstIdentity, settings.CredentialIdentity), "A pending host save cannot publish a new credential identity");
            view.ApiKey = "accepted-ax";
            view.ApiKey = "accepted-a";
            nestedIdentity = settings.CredentialIdentity;
        };
        view.Speed = 1.5;
        AssertEqual(true, reentered, "The fixture must actually reenter the non-key host save");
        AssertEqual(false, ReferenceEquals(firstIdentity, nestedIdentity), "Two accepted key edits create a fresh identity even when the key returns to A");
        AssertEqual(true, ReferenceEquals(nestedIdentity, settings.CredentialIdentity), "An outer ordinary save cannot restore the candidate's historical token");
        AssertEqual(true, ReferenceEquals(nestedIdentity, stored.CredentialIdentity), "The in-memory storage copy retains the accepted runtime token for subsequent initialization");
        AssertEqual(1.5, settings.Speed, "The outer ordinary setting save still publishes its requested value");
    }

    internal static async Task StartupCredentialRoundTripAtSaveBoundaryCannotWriteAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var stored = new Settings { ApiKey = "startup-a", VoiceId = Id, CachedVoice = new() { Title = "Retained startup display" } };
        var (service, http) = TestHttpServiceProxy.Create();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        http.GetAsyncHandler = (url, _, _) =>
        {
            if (url == FishAudioClock.TimeApiUrl) return Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
            started.TrySetResult();
            return response.Task;
        };
        SettingsViewModel? view = null;
        var editedAtBoundary = false;
        using var plugin = new Main((context, settings, lease, cycle) => new SettingsViewModel(context, settings, null, null,
            startupCreditRefreshCycle: cycle, settingsWriteLease: lease), startupSettingsRevisionPublished: () =>
        {
            editedAtBoundary = true;
            view!.ApiKey = "startup-ax";
            view.ApiKey = "startup-a";
        });
        var context = CreateContext(settings: stored, httpService: service);
        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        view = plugin.GetOrCreateSettingsViewModel();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var saves = ((ContextProxy)(object)context).SaveCount;
        response.SetResult("{\"_id\":\"" + Id + "\",\"title\":\"Late startup A\"}");
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(true, editedAtBoundary, "The fixture must change credentials after response validation and before the startup save predicate");
        AssertEqual("Retained startup display", stored.CachedVoice?.Title, "The startup save predicate must reject its old token even when the key returns to A");
        AssertEqual("Retained startup display", view.CachedVoiceTitle, "Rejected startup persistence cannot publish a new selected display");
        AssertEqual(saves + 2, ((ContextProxy)(object)context).SaveCount, "Only the two accepted key edits may save at the boundary");
    }

    internal static void SubscriptionRebindingAndDisposalKeepOnlyTheCurrentOwner()
    {
        var settings = new Settings { ApiKey = "owner-a", VoiceId = Id };
        var cache = new VoiceMetadataCache();
        using var view = new SettingsViewModel(CreateContext(settings: settings), settings, null, null, voiceMetadataCache: cache);
        var card = new VoiceSearchItem { Id = Id, Title = "Retained" };
        view.SearchResults = [card];
        var queued = new List<Action>();
        SettingsViewModel.UiThreadInvokerOverride = queued.Add;
        try
        {
            cache.Put(VoiceMetadata.FromCachedVoice(Id, new() { Title = "Old queued A" }), VoiceMetadataCache.CredentialScope(settings.ApiKey));
            var oldPublication = queued.Single();
            view.ApiKey = "owner-b";
            view.ApiKey = "owner-a";
            queued.Clear();
            cache.Put(VoiceMetadata.FromCachedVoice(Id, new() { Title = "Current A" }), VoiceMetadataCache.CredentialScope(settings.ApiKey));
            AssertEqual(1, queued.Count, "Rebinding must retain exactly one current cache subscriber");
            var currentPublication = queued.Single();
            currentPublication();
            oldPublication();
            AssertEqual("Current A", card.Title, "The new subscriber accepts current observations and rejects the queued old A event");
            view.Dispose();
            var callbacksAfterDisposal = queued.Count;
            cache.Put(VoiceMetadata.FromCachedVoice(Id, new() { Title = "After disposal" }), VoiceMetadataCache.CredentialScope(settings.ApiKey));
            AssertEqual(callbacksAfterDisposal, queued.Count, "Disposal must unsubscribe the actual current delegate");
            currentPublication();
            AssertEqual("Current A", card.Title, "Previously queued callbacks cannot publish after facade disposal");
        }
        finally { SettingsViewModel.UiThreadInvokerOverride = null; }
    }

    internal static async Task RetainedPreviewEntryUsesResourceWithoutAuthAndRefreshesWithAcceptedKeyAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string sample = "https://platform.r2.fish.audio/retained.mp3";
        var settings = new Settings { ApiKey = "preview-a", VoiceId = Id,
            CachedVoice = new() { Title = "Retained selected", SampleAudioUrl = sample } };
        var (service, http) = TestHttpServiceProxy.Create();
        var player = new TestPreviewAudioPlayer();
        using var view = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings, null, null,
            previewAudioPlayerFactory: () => player);
        var card = new VoiceSearchItem { Id = Id, Title = "Retained browse", SampleAudioUrl = PreviewSuite.CreateExpiredPreviewUrl("old.mp3") };
        view.SearchResults = [card];
        view.ApiKey = "preview-b";
        await view.ToggleDisplayPreviewCommand.ExecuteAsync(null);
        AssertEqual(sample, http.LastGetStreamUrl, "A preserved selected preview entry remains usable after key editing");
        AssertEqual(0, http.GetCallCount, "Unsigned retained preview URLs do not trigger metadata refresh merely because the key changed");
        AssertEqual(1, player.PlayCallCount, "Explicit preview may start through the preserved entry");
        AssertEqual(true, http.LastGetStreamOptions?.Headers is null || !http.LastGetStreamOptions.Headers.ContainsKey("Authorization"), "Retained resource downloads must remain unauthenticated");
        view.ApiKey = "preview-bx";
        http.GetResponseJson = "{\"_id\":\"" + Id + "\",\"title\":\"Fresh B\",\"samples\":[{\"audio\":\"https://platform.r2.fish.audio/fresh.mp3\"}]}";
        await view.ToggleSearchItemPreviewCommand.ExecuteAsync(card);
        AssertEqual("Bearer preview-bx", AssertHeaders(http.LastGetOptions, "Explicit expired preview refresh")["Authorization"], "Later preview detail requests use the newly accepted key");
        AssertEqual(true, http.LastGetStreamOptions?.Headers is null || !http.LastGetStreamOptions.Headers.ContainsKey("Authorization"), "Refreshed resource downloads also remain unauthenticated");
        AssertEqual("Fresh B", card.Title, "An explicit preview refresh may update its current browse card");
        AssertEqual("Retained selected", settings.CachedVoice?.Title, "Refreshing a browse preview still cannot implicitly Apply its metadata");
        AssertEqual("Retained selected", view.CachedVoiceTitle, "The selected display remains independent of browse preview refresh");
    }

    private static string SelectedDisplay(SettingsViewModel view) => JsonSerializer.Serialize(new
    {
        view.VoiceId, view.CachedVoiceId, view.CachedVoiceTitle, view.CachedVoiceDescription,
        view.CachedVoiceCoverUrl, view.CachedVoiceAuthor, view.CachedVoiceTaskCount,
        view.CachedVoiceSampleUrl, view.IsSelectedVoiceFavorite,
    });

    private static string Navigation(SettingsViewModel view) => JsonSerializer.Serialize(new
    {
        view.SearchQuery, view.VoiceIdInput, view.SearchPage, view.SearchTotalPages, view.SearchResultCount,
        view.SearchResultCountText, view.SearchPageLimitText, view.SearchHasNextPage, view.SearchHasUncertainExtent,
        view.HasSearched, view.ShowPagination, view.PageInput, view.BrowseMode, view.FavoritePage,
        view.FavoriteTotalPages, view.FavoriteCount, view.FavoritePageInput,
    });
}
