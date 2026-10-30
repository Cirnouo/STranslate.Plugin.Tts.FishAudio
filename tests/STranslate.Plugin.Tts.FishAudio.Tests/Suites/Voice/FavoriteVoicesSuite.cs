using NUnit.Framework;
using System.Net;
using System.Text.Json;
using STranslate.Plugin.Tts.FishAudio.Caching;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static TestAssertions;

[TestFixture]
[NonParallelizable]
internal static class FavoriteVoicesSuite
{
    [Test]
    [Property("FishAudioCaseId", "voice.favorites/new-likes-and-relikes-display-first-without-rewriting-durable-order")]
    [Order(170)]
    public static void NewLikesAndRelikesDisplayFirstWithoutRewritingDurableOrder()
    {
        var directory = CreateTempDirectory();
        var cache = new VoiceMetadataCache();
        cache.Put(Metadata(1));
        cache.Put(Metadata(2));
        var settings = SelectedSettings();
        var context = CreateContext(settings: settings, pluginSettingsDirectoryPath: directory);
        var commits = 0;
        void Commit(string temporary, string destination) { commits++; File.Move(temporary, destination, true); }
        using var store = new FavoriteVoiceStore(directory, Commit);
        using var view = new SettingsViewModel(context, settings, null, null, voiceMetadataCache: cache, favoriteVoiceStore: store);
        var first = new VoiceSearchItem { Id = Id(1) };
        var second = new VoiceSearchItem { Id = Id(2) };
        view.SearchResults = [first, second];
        view.ToggleFavoriteCommand.Execute(first);
        view.ToggleFavoriteCommand.Execute(second);
        AssertEnumerableEqual(new[] { Id(2), Id(1) }, view.FavoriteResults.Select(card => card.Id), "A later Like must display before the earlier Like");
        AssertEnumerableEqual(new[] { Id(1), Id(2) }, store.Read(), "Presentation must preserve durable addition order");
        var bytes = File.ReadAllBytes(FilePath(directory));
        AssertEnumerableEqual(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { Id(1), Id(2) })), bytes, "Storage remains the exact ID-only UTF-8 array");
        store.SetFavorite(Id(1), true);
        AssertEnumerableEqual(bytes, File.ReadAllBytes(FilePath(directory)), "An already-satisfied explicit Like neither moves nor rewrites an ID");
        view.SwitchToFavoritesCommand.Execute(null);
        using (var restartedStore = new FavoriteVoiceStore(directory, Commit))
        using (var restarted = new SettingsViewModel(context, settings, null, null, voiceMetadataCache: cache, favoriteVoiceStore: restartedStore))
            AssertEnumerableEqual(new[] { Id(2), Id(1) }, restarted.FavoriteResults.Select(card => card.Id), "A fresh facade uses the same newest-first presentation");
        AssertEqual(2, commits, "Already-satisfied Like, reload and fresh facade perform no atomic write");
        AssertEnumerableEqual(bytes, File.ReadAllBytes(FilePath(directory)), "Reload and restart do not rewrite the durable file");
        view.ToggleFavoriteCommand.Execute(first);
        view.ToggleFavoriteCommand.Execute(first);
        AssertEnumerableEqual(new[] { Id(1), Id(2) }, view.FavoriteResults.Select(card => card.Id), "Unlike followed by Like makes that voice globally newest");
        AssertEnumerableEqual(new[] { Id(2), Id(1) }, store.Read(), "Re-like appends through the ordinary durable mutation");
        using var relikedRestart = new SettingsViewModel(context, settings, null, null, voiceMetadataCache: cache);
        AssertEnumerableEqual(new[] { Id(1), Id(2) }, relikedRestart.FavoriteResults.Select(card => card.Id), "Re-like order survives restart");
        AssertEqual(Id(99), settings.VoiceId, "Like ordering preserves selection");
        AssertEqual("Selected", settings.CachedVoice!.Title, "Like ordering preserves the selected metadata snapshot");
        AssertEqual(0, ((ContextProxy)(object)context).SaveCount, "Like ordering never writes host Settings");
    }

    [Test]
    [Property("FishAudioCaseId", "voice.favorites/legacy-favorites-reverse-globally-and-adding-keeps-the-current-page")]
    [Order(171)]
    public static void LegacyFavoritesReverseGloballyAndAddingKeepsTheCurrentPage()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(FilePath(directory), JsonSerializer.Serialize(Enumerable.Range(1, 12).Select(Id)));
        var bytes = File.ReadAllBytes(FilePath(directory));
        var cache = new VoiceMetadataCache();
        for (var number = 1; number <= 13; number++) cache.Put(Metadata(number));
        using var view = new SettingsViewModel(CreateContext(pluginSettingsDirectoryPath: directory), new Settings(), null, null, voiceMetadataCache: cache);
        AssertEnumerableEqual(new[] { 12, 11, 10, 9, 8, 7 }.Select(Id), view.FavoriteResults.Select(card => card.Id), "Legacy page one shows the newest six additions globally");
        view.SwitchToFavoritesCommand.Execute(null);
        view.FavoriteNextPageCommand.Execute(null);
        AssertEnumerableEqual(new[] { 6, 5, 4, 3, 2, 1 }.Select(Id), view.FavoriteResults.Select(card => card.Id), "Legacy page two continues the reversed sequence without duplication");
        AssertEnumerableEqual(bytes, File.ReadAllBytes(FilePath(directory)), "Legacy paging and reload preserve exact storage bytes");
        view.ToggleFavoriteCommand.Execute(new VoiceSearchItem { Id = Id(13) });
        AssertEqual(2, view.FavoritePage, "A new Like must not force navigation to page one");
        AssertEqual("2", view.FavoritePageInput, "A new Like preserves the current page input");
        AssertEqual(13, view.FavoriteCount, "A new Like updates the exact count");
        AssertEnumerableEqual(new[] { 7, 6, 5, 4, 3, 2 }.Select(Id), view.FavoriteResults.Select(card => card.Id), "The retained second page reflects the global insertion");
        view.FavoritePrevPageCommand.Execute(null);
        AssertEnumerableEqual(new[] { 13, 12, 11, 10, 9, 8 }.Select(Id), view.FavoriteResults.Select(card => card.Id), "The new Like is first on page one");
        view.FavoritePageInput = "3";
        view.CommitFavoritePageInputCommand.Execute(null);
        AssertEqual(Id(1), view.FavoriteResults.Single().Id, "The true final-page item remains the oldest surviving addition");
        view.ToggleFavoriteCommand.Execute(view.FavoriteResults.Single());
        AssertEqual(2, view.FavoritePage, "Removing the only final-page item preserves the existing clamp");
        AssertEnumerableEqual(new[] { 7, 6, 5, 4, 3, 2 }.Select(Id), view.FavoriteResults.Select(card => card.Id), "Clamping retains the exact expected global page");
        using var store = new FavoriteVoiceStore(directory);
        AssertEnumerableEqual(Enumerable.Range(2, 12).Select(Id), store.Read(), "Global display reversal never reverses durable writes");
    }

    [Test]
    [Property("FishAudioCaseId", "voice.favorites/favorite-requests-use-current-credential")]
    [Order(201)]
    public static async Task FavoriteRequestsUseCurrentCredentialAsync()
    {
        foreach (var key in new[] { "configured-favorite-key", "", " \t" })
        {
            var directory = CreateTempDirectory();
            using (var store = new FavoriteVoiceStore(directory))
                store.SetFavorite(Id(1), true);
            var settings = new Settings { ApiKey = key };
            var (service, http) = TestHttpServiceProxy.Create();
            http.GetResponseJson = Detail(1, "Favorite");
            using var view = new SettingsViewModel(CreateContext(settings: settings, httpService: service, pluginSettingsDirectoryPath: directory), settings);
            view.SwitchToFavoritesCommand.Execute(null);
            await view.PendingFavoriteMetadataTask;
            AssertEqual($"Bearer {(string.IsNullOrWhiteSpace(key) ? "dummy" : key)}", AssertHeaders(http.LastGetOptions, "Favorite detail headers")["Authorization"], "Favorite metadata uses configured credentials or blank fallback");
            view.ApiKey = "next-favorite-account";
        AssertEqual(true, view.FavoriteResults.Single().HasMetadata, "Key edits preserve already displayed favorite metadata");
            var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            http.GetAsyncHandler = (_, _, _) => pending.Task;
            view.SwitchToFavoritesCommand.Execute(null);
            var oldLoad = view.PendingFavoriteMetadataTask;
            view.ApiKey = "";
            pending.SetResult(Detail(1, "Late private favorite"));
            await oldLoad;
            AssertEqual(false, view.FavoriteResults.Single().HasMetadata, "Old-account favorite responses must not republish metadata");
            AssertEqual(Id(1), view.FavoriteResults.Single().Id, "Key edits preserve favorite IDs");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "voice.favorites/favorite-voice-ids-reject-whitespace-and-retain-canonical-deduplication")]
    [Order(167)]
    public static void FavoriteVoiceIdsRejectWhitespaceAndRetainCanonicalDeduplication()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(FilePath(directory), JsonSerializer.Serialize(new[] { Id(1) + "\n", Id(1), Id(1), Id(2) }));
        using var store = new FavoriteVoiceStore(directory);
        AssertEnumerableEqual(new[] { Id(1), Id(2) }, store.Read(), "ID-file loading should discard invalid IDs, keep canonical first-seen order and deduplicate");
        foreach (var whitespace in new[] { " ", "\t", "\r", "\n", "\r\n" })
            foreach (var invalid in new[] { whitespace + Id(1), Id(1) + whitespace, Id(1).Insert(16, whitespace) })
                AssertEqual(false, SettingsValidation.IsValidVoiceIdFormat(invalid), "Voice IDs require absolute boundaries and reject whitespace");
        AssertEqual(true, SettingsValidation.IsValidVoiceIdFormat(Id(1)), "Canonical IDs remain valid");
    }

    [Test]
    [Property("FishAudioCaseId", "voice.favorites/by-id-lookup-rejects-whitespace-without-request-or-selection-change")]
    [Order(164)]
    public static async Task ByIdLookupRejectsWhitespaceWithoutRequestOrSelectionChangeAsync()
    {
        var settings = SelectedSettings();
        var directory = CreateTempDirectory();
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = Detail(1, "Canonical result");
        using var view = new SettingsViewModel(CreateContext(settings: settings, httpService: service, pluginSettingsDirectoryPath: directory), settings);
        foreach (var invalid in new[] { Id(1) + "\n", " " + Id(1), Id(1) + "\r\n", "https://fish.audio/m/" + Id(1), Id(1) + "0", Id(1).Replace("0", "A"), Id(1)[..31] })
        {
            view.VoiceIdInput = invalid;
            await view.SubmitVoiceIdCommand.ExecuteAsync(null);
            AssertEqual(0, http.GetUrls.Count, "A noncanonical By-ID input must be rejected before any detail GET");
            AssertEqual(0, view.ByIdResults.Count, "Invalid raw input must not produce an extracted or truncated voice card");
            AssertEqual(Id(99), settings.VoiceId, "Rejected By-ID lookup must retain the selected voice");
            AssertEqual(0, view.FavoriteCount, "Rejected By-ID lookup must preserve favorites");
            AssertEqual(true, !string.IsNullOrEmpty(view.VoiceIdError), "Rejected By-ID input must report its format error");
        }
        view.VoiceIdInput = Id(1);
        await view.SubmitVoiceIdCommand.ExecuteAsync(null);
        AssertEqual($"https://api.fish.audio/model/{Id(1)}", http.LastGetUrl, "An exact ID must retain normal By-ID lookup");
        AssertEqual(Id(1), view.ByIdResults.Single().Id, "By-ID card IDs must remain canonical");
        view.ToggleFavoriteCommand.Execute(view.ByIdResults[0]);
        using var store = new FavoriteVoiceStore(directory);
        AssertEqual(Id(1), store.Read().Single(), "ID-file bookmarks must contain canonical IDs only");
        AssertEqual(Id(99), settings.VoiceId, "Querying and bookmarking must not Apply");
        view.SelectVoiceCommand.Execute(view.ByIdResults[0]);
        AssertEqual(Id(1), settings.VoiceId, "Only Apply selects the canonical By-ID result");
    }
    [Test]
    [Property("FishAudioCaseId", "voice.favorites/by-id-lookup-keeps-selected-voice-until-apply")]
    [Order(168)]
    public static async Task ByIdLookupKeepsSelectedVoiceUntilApplyAsync()
    {
        var settings = SelectedSettings();
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = Detail(1, "Looked up");
        var context = CreateContext(settings: settings, httpService: service);
        using var view = new SettingsViewModel(context, settings) { VoiceIdInput = Id(1) };
        await view.SubmitVoiceIdCommand.ExecuteAsync(null);
        AssertEqual(Id(99), settings.VoiceId, "Successful lookup must preserve the selected voice");
        AssertEqual("Selected", view.CachedVoiceTitle, "Lookup must preserve selected metadata");
        AssertEqual(0, ((ContextProxy)(object)context).SaveCount, "Lookup must not save a selection");
        AssertEqual(1, view.ByIdResults.Count, "Lookup should publish one independent card");
        view.SelectVoiceCommand.Execute(view.ByIdResults[0]);
        AssertEqual(Id(1), settings.VoiceId, "Apply should select the inspected voice");
        AssertEqual("Looked up", view.CachedVoiceTitle, "Apply should update selected metadata");
    }

    [Test]
    [Property("FishAudioCaseId", "voice.favorites/favorite-metadata-migrates-bounds-and-round-trips")]
    [Order(169)]
    public static void FavoriteMetadataMigratesBoundsAndRoundTrips()
    {
        var directory = CreateTempDirectory();
        using (var store = new FavoriteVoiceStore(directory))
        {
            AssertEqual(0, store.Read().Count, "A missing ID file means an available empty list");
            store.SetFavorite(Id(1), true);
            store.SetFavorite(Id(2), true);
        }
        using var restarted = new FavoriteVoiceStore(directory);
        AssertEnumerableEqual(new[] { Id(1), Id(2) }, restarted.Read(), "ID-only favorites survive a fresh store instance");
        using var json = JsonDocument.Parse(File.ReadAllBytes(FilePath(directory)));
        AssertEqual(JsonValueKind.Array, json.RootElement.ValueKind, "Favorite storage must be a plain JSON array");
        AssertEqual(true, json.RootElement.EnumerateArray().All(value => value.ValueKind == JsonValueKind.String && value.GetString()!.Length == 32), "The file must contain only IDs, without per-favorite metadata");
        AssertEqual(false, JsonSerializer.Serialize(new Settings()).Contains("FavoriteVoices", StringComparison.Ordinal), "Favorites must not add a settings schema field");
        AssertEqual(2, SettingsSchema.Current, "Independent ID storage must not introduce schema 3");
    }

    [Test]
    [Property("FishAudioCaseId", "voice.favorites/favorites-sync-across-cards-persist-and-clamp-pages")]
    [Order(172)]
    public static void FavoritesSyncAcrossCardsPersistAndClampPages()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(FilePath(directory), JsonSerializer.Serialize(Enumerable.Range(1, 12).Select(Id)));
        var cache = new VoiceMetadataCache();
        for (var number = 1; number <= 13; number++) cache.Put(Metadata(number));
        var settings = SelectedSettings();
        var context = CreateContext(settings: settings, pluginSettingsDirectoryPath: directory);
        using var view = new SettingsViewModel(context, settings, null, null, voiceMetadataCache: cache);
        var item = new VoiceSearchItem { Id = Id(13), Title = "New favorite" };
        var duplicate = new VoiceSearchItem { Id = Id(13), Title = "Duplicate" };
        view.SearchResults = [item, duplicate];
        view.ByIdResults = [new VoiceSearchItem { Id = Id(13), Title = "By ID" }];
        AssertEqual("Favorite 12", view.FavoriteResults[0].Title, "Newest favorites should use the shared cache immediately");
        view.ToggleFavoriteCommand.Execute(item);
        AssertEqual(13, view.FavoriteCount, "Adding a favorite should update local count");
        AssertEqual(true, item.IsFavorite && duplicate.IsFavorite && view.ByIdResults[0].IsFavorite, "Matching cards share favorite state by ID");
        view.SwitchToFavoritesCommand.Execute(null);
        view.FavoritePageInput = "3";
        view.CommitFavoritePageInputCommand.Execute(null);
        AssertEqual(1, view.FavoriteResults.Count, "Favorite pagination includes its partial final page");
        AssertEqual(Id(1), view.FavoriteResults.Single().Id, "The oldest addition is the true final-page item");
        var finalDuplicate = new VoiceSearchItem { Id = Id(1), Title = "Final-page duplicate" };
        var anotherFinalDuplicate = new VoiceSearchItem { Id = Id(1), Title = "Another final-page duplicate" };
        var finalById = new VoiceSearchItem { Id = Id(1), Title = "Final-page By ID" };
        view.SearchResults = [item, duplicate, finalDuplicate, anotherFinalDuplicate];
        view.ByIdResults = [view.ByIdResults[0], finalById];
        AssertEqual(true, finalDuplicate.IsFavorite && anotherFinalDuplicate.IsFavorite && finalById.IsFavorite, "The actual final-page ID starts liked across all duplicate cards");
        view.ToggleFavoriteCommand.Execute(view.FavoriteResults[0]);
        AssertEqual(12, view.FavoriteCount, "Removing a favorite immediately updates count");
        AssertEqual(2, view.FavoritePage, "Removing the last final-page item clamps to the preceding page");
        AssertEqual("2", view.FavoritePageInput, "Clamping updates page input");
        AssertEqual(6, view.FavoriteResults.Count, "Clamping rebuilds the visible six-card page");
        AssertEqual(false, finalDuplicate.IsFavorite || anotherFinalDuplicate.IsFavorite || finalById.IsFavorite, "Removal clears all matching Search/By-ID hearts for the actual final-page ID");
        AssertEqual(true, item.IsFavorite && duplicate.IsFavorite && view.ByIdResults[0].IsFavorite, "Removing the final-page ID preserves all matching hearts for the newer Like");
        view.FavoritePageInput = "999";
        view.CommitFavoritePageInputCommand.Execute(null);
        AssertEqual("2", view.FavoritePageInput, "Invalid page input restores current page");
        view.FavoritePrevPageCommand.Execute(null);
        view.FavoriteNextPageCommand.Execute(null);
        AssertEqual(2, view.FavoritePage, "Local previous/next commands remain coherent");
        AssertEqual(Id(99), settings.VoiceId, "Persistence and rebuilding must never Apply a voice");
        AssertEqual(0, ((ContextProxy)(object)context).SaveCount, "Bookmarking must not write host Settings");
    }

    [Test]
    [Property("FishAudioCaseId", "voice.favorites/favorite-saves-rollback-and-respect-read-only-and-retired-leases")]
    [Order(173)]
    public static void FavoriteSavesRollbackAndRespectReadOnlyAndRetiredLeases()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(FilePath(directory), JsonSerializer.Serialize(new[] { Id(1) }));
        var settings = SelectedSettings();
        var context = CreateContext(settings: settings, pluginSettingsDirectoryPath: directory);
        var runtime = SettingsStore.Load(context, DateTimeOffset.UtcNow, out var lease);
        using var store = new FavoriteVoiceStore(directory, (_, _) => throw new IOException("Synthetic atomic-rename failure"));
        using var view = new SettingsViewModel(context, runtime, null, null, settingsWriteLease: lease, favoriteVoiceStore: store);
        var baseline = File.ReadAllText(FilePath(directory));
        view.ToggleFavoriteCommand.Execute(view.FavoriteResults[0]);
        AssertEqual(baseline, File.ReadAllText(FilePath(directory)), "Rename failure preserves the previous durable file");
        AssertEqual(1, view.FavoriteCount, "Rename failure must not publish a successful removal in memory");
        AssertEqual(true, view.FavoriteResults[0].IsFavorite, "Rename failure must keep the original starred card");
        AssertEqual(0, Directory.GetFiles(directory, "*.tmp").Length, "Failed atomic writes must clean their private temporary file");
        SettingsStore.Retire(runtime, lease);
        view.ToggleFavoriteCommand.Execute(new VoiceSearchItem { Id = Id(2), Title = "New" });
        AssertEqual(baseline, File.ReadAllText(FilePath(directory)), "Retired view models cannot update the independent ID file");
        using var unavailable = new SettingsViewModel(CreateContext(), new Settings());
        AssertEqual(true, unavailable.FavoritesError is not null, "Missing host data directory is unavailable rather than an empty collection");
        unavailable.ToggleFavoriteCommand.Execute(new VoiceSearchItem { Id = Id(1), Title = "New" });
        AssertEqual(0, unavailable.FavoriteCount, "Unavailable persistence cannot silently report memory-only success");
        var fullDirectory = CreateTempDirectory();
        File.WriteAllText(FilePath(fullDirectory), JsonSerializer.Serialize(Enumerable.Range(1, 300).Select(Id)));
        using var full = new FavoriteVoiceStore(fullDirectory);
        try { full.SetFavorite(Id(301), true); throw new InvalidOperationException("Expected favorite cap"); }
        catch (FavoriteVoiceLimitException) { }
        AssertEqual(300, full.Read().Count, "Adding beyond the cap must preserve every existing ID");
    }

    [Test]
    [Property("FishAudioCaseId", "voice.favorites/by-id-replacement-and-input-edits-reject-late-results")]
    [Order(174)]
    public static async Task ByIdReplacementAndInputEditsRejectLateResultsAsync()
    {
        SettingsViewModel? ownedView = null;
        TaskCompletionSource<string>? ownedResponse = null;
        Task? ownedLookup = null;
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            var settings = SelectedSettings();
            var (service, http) = TestHttpServiceProxy.Create();
            var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            ownedResponse = release;
            var canceled = false;
            http.GetAsyncHandler = (_, _, token) => { token.Register(() => canceled = true); return release.Task; };
            var view = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings) { VoiceIdInput = Id(1) };
            ownedView = view;
            var old = view.SubmitVoiceIdCommand.ExecuteAsync(null);
            ownedLookup = old;
            view.VoiceIdInput = Id(2);
            http.GetAsyncHandler = null;
            http.GetResponseJson = Detail(2, "Current");
            await view.SubmitVoiceIdCommand.ExecuteAsync(null);
            release.SetResult(Detail(1, "Late"));
            await old.WaitAsync(RegressionExecution.Timeout);
            AssertEqual(true, canceled, "Editing lookup input must cancel previous metadata work");
            AssertEqual("Current", view.ByIdResults.Single().Title, "A replaced response cannot overwrite the current card");
            view.VoiceIdInput = "invalid";
            await view.SubmitVoiceIdCommand.ExecuteAsync(null);
            AssertEqual(0, view.ByIdResults.Count, "Invalid input removes the prior Apply card");
            AssertEqual(Id(99), settings.VoiceId, "Successful and failed queries preserve current selection");
        },
        () => { ownedView?.Dispose(); return Task.CompletedTask; },
        () => { ownedResponse?.TrySetResult(Detail(1, "Late")); return Task.CompletedTask; },
        () => ownedLookup ?? Task.CompletedTask);
    }

    [Test]
    [Property("FishAudioCaseId", "voice.favorites/favorite-and-by-id-preview-refreshes-metadata-without-applying")]
    [Order(175)]
    public static async Task FavoriteAndByIdPreviewRefreshesMetadataWithoutApplyingAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        foreach (var byId in new[] { false, true })
        foreach (var expired in new[] { false, true })
        {
            var directory = CreateTempDirectory();
            File.WriteAllText(FilePath(directory), JsonSerializer.Serialize(new[] { Id(1) }));
            var cache = new VoiceMetadataCache();
            var oldUrl = expired ? "https://platform.r2.fish.audio/old.mp3?X-Amz-Date=20000101T000000Z&X-Amz-Expires=60" : "https://platform.r2.fish.audio/old.mp3";
            cache.Put(VoiceMetadata.FromCachedVoice(Id(1), new() { Title = "Cached", SampleAudioUrl = oldUrl }));
            var settings = SelectedSettings();
            var (service, http) = TestHttpServiceProxy.Create();
            http.GetResponseJson = Detail(1, "Refreshed", "https://platform.r2.fish.audio/new.mp3");
            http.GetAsStreamAsyncHandler = (_, _, _) => !expired && http.GetAsStreamCallCount == 1
                ? Task.FromException<Stream>(new HttpRequestException("Synthetic sample rejection", null, HttpStatusCode.Forbidden))
                : Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
            var player = new TestPreviewAudioPlayer();
            using var view = new SettingsViewModel(CreateContext(settings: settings, httpService: service, pluginSettingsDirectoryPath: directory), settings, null, null,
                previewAudioPlayerFactory: () => player, voiceMetadataCache: cache);
            if (byId) { view.VoiceIdInput = Id(1); await view.SubmitVoiceIdCommand.ExecuteAsync(null); }
            AssertEqual(0, http.GetCallCount, "A metadata-cache hit must not perform a detail request");
            var item = byId ? view.ByIdResults[0] : view.FavoriteResults[0];
            await view.ToggleSearchItemPreviewCommand.ExecuteAsync(item);
            AssertEqual(1, player.PlayCallCount, "Favorite and By-ID previews must use the existing local player");
            AssertEqual(1, http.GetCallCount, "Sample expiry/403 must force a fresh detail request despite a metadata-cache hit");
            AssertEqual(true, cache.TryGet(Id(1), out var updated), "Preview detail responses must populate the shared cache");
            AssertEqual("Refreshed", updated.Title, "Preview refresh must replace shared metadata");
            AssertEqual("Refreshed", view.FavoriteResults[0].Title, "Shared refresh should update matching favorite cards");
            AssertEqual(Id(99), settings.VoiceId, "Preview refresh must not Apply the browsed voice");
            AssertEqual("Selected", view.CachedVoiceTitle, "Preview refresh preserves selected-card metadata");
            AssertEqual(JsonSerializer.Serialize(new[] { Id(1) }), File.ReadAllText(FilePath(directory)), "Preview must not persist metadata into the ID-only file");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "voice.favorites/unfavorite-cancels-originating-preview-and-rejects-late-work")]
    [Order(176)]
    public static async Task UnfavoriteCancelsOriginatingPreviewAndRejectsLateWorkAsync()
    {
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            using var network = OverrideNetworkAvailability(true);
            foreach (var refresh in new[] { false, true })
            {
                string? ownedDirectory = null;
                SettingsViewModel? ownedView = null;
                TaskCompletionSource<string>? ownedDetail = null;
                TaskCompletionSource<Stream>? ownedAudio = null;
                MemoryStream? ownedLateAudio = null;
                Task? ownedPreview = null;
                await RegressionFixtureOwner.RunAsync(async () =>
                {
                    var directory = CreateTempDirectory();
                    ownedDirectory = directory;
                    File.WriteAllText(FilePath(directory), JsonSerializer.Serialize(new[] { Id(1) }));
                    var cache = new VoiceMetadataCache();
                    var url = refresh ? "https://platform.r2.fish.audio/old.mp3?X-Amz-Date=20000101T000000Z&X-Amz-Expires=60" : "https://platform.r2.fish.audio/fresh.mp3";
                    cache.Put(VoiceMetadata.FromCachedVoice(Id(1), new() { Title = "Cached", SampleAudioUrl = url }));
                    var (service, http) = TestHttpServiceProxy.Create();
                    var detail = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    ownedDetail = detail;
                    var audio = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
                    ownedAudio = audio;
                    var canceled = false;
                    http.GetAsyncHandler = (_, _, token) => { token.Register(() => canceled = true); return detail.Task; };
                    http.GetAsStreamAsyncHandler = (_, _, token) => { token.Register(() => canceled = true); return audio.Task; };
                    var player = new TestPreviewAudioPlayer();
                    var view = new SettingsViewModel(CreateContext(httpService: service, pluginSettingsDirectoryPath: directory), new Settings(), null, null,
                        previewAudioPlayerFactory: () => player, voiceMetadataCache: cache);
                    ownedView = view;
                    view.SearchResults = [new VoiceSearchItem { Id = Id(1), Title = "Matching search", SampleAudioUrl = url }];
                    var source = view.FavoriteResults[0];
                    var pending = view.ToggleSearchItemPreviewCommand.ExecuteAsync(source);
                    ownedPreview = pending;
                    view.ToggleFavoriteCommand.Execute(source);
                    AssertEqual(true, canceled, "Removing the originating favorite must cancel its request even when a matching search card remains");
                    AssertEqual(null, view.PreparingPreviewVoiceId, "Removing the originating card clears preparation");
                    detail.TrySetResult(Detail(1, "Late", "https://platform.r2.fish.audio/late.mp3"));
                    var late = new MemoryStream([1, 2, 3]);
                    ownedLateAudio = late;
                    audio.TrySetResult(late);
                    await pending.WaitAsync(RegressionExecution.Timeout);
                    AssertEqual(0, player.PlayCallCount, "Late removed-card work cannot start playback");
                    AssertEqual(0, view.FavoriteCount, "Late metadata must not restore a deleted favorite ID");
                    if (!refresh) AssertEqual(false, late.CanRead, "Late audio must be disposed");
                    else late.Dispose();
                },
                () => { ownedView?.Dispose(); return Task.CompletedTask; },
                () => { ownedDetail?.TrySetResult(Detail(1, "Late", "https://platform.r2.fish.audio/late.mp3")); return Task.CompletedTask; },
                () =>
                {
                    if (ownedAudio is { Task.IsCompleted: false })
                    {
                        ownedLateAudio ??= new MemoryStream([1, 2, 3]);
                        ownedAudio.TrySetResult(ownedLateAudio);
                    }
                    return Task.CompletedTask;
                },
                () => ownedPreview ?? Task.CompletedTask,
                () => { ownedLateAudio?.Dispose(); return Task.CompletedTask; },
                () => { DeleteOwnedHeldWorkDirectory(ownedDirectory); return Task.CompletedTask; });
            }
        });
    }


    private static void DeleteOwnedHeldWorkDirectory(string? root)
    {
        if (root is null) return;
        var fullRoot = Path.GetFullPath(root);
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FishAudioTests"));
        var name = Path.GetFileName(fullRoot);
        if (!Path.IsPathFullyQualified(root)
            || !string.Equals(Path.GetDirectoryName(fullRoot), parent, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(name, "N", out var owner)
            || !string.Equals(name, owner.ToString("N"), StringComparison.Ordinal))
            throw new InvalidOperationException("held-work-cleanup-owner-invalid");
        for (DirectoryInfo? directory = new(fullRoot); directory is not null; directory = directory.Parent)
        {
            if (!directory.Exists || (directory.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidOperationException("held-work-cleanup-directory-invalid");
        }
        var pending = new Stack<string>();
        pending.Push(fullRoot);
        while (pending.Count != 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                    throw new InvalidOperationException("held-work-cleanup-entry-invalid");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }
        Directory.Delete(fullRoot, recursive: true);
    }

    internal static Settings SelectedSettings() => new() { VoiceId = Id(99), CachedVoice = new() { Title = "Selected" } };
    internal static string Id(int number) => number.ToString("x32");
    internal static string FilePath(string directory) => Path.Combine(directory, FavoriteVoiceStore.FileName);
    internal static VoiceMetadata Metadata(int number, string cover = "") => VoiceMetadata.FromCachedVoice(Id(number), new() { Title = $"Favorite {number}", Description = "Cached details", CoverImage = cover });
    internal static string Detail(int number, string title, string? sample = null, string cover = "") => JsonSerializer.Serialize(new
    {
        _id = Id(number), title, description = "Refreshed details", cover_image = cover, task_count = 12,
        samples = sample is null ? Array.Empty<object>() : new object[] { new { audio = sample } },
    });
}
