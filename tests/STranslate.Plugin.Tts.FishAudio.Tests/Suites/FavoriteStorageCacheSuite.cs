using System.Text.Json;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Caching;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static FavoriteVoicesSuite;
using static RuntimeOverrideScopes;
using static TestAssertions;

internal static class FavoriteStorageCacheSuite
{
    internal static async Task SamePathWritersKeepIdsAndCorruptFilesRemainUntouchedAsync()
    {
        var directory = CreateTempDirectory();
        using var first = new FavoriteVoiceStore(directory);
        using var second = new FavoriteVoiceStore(directory);
        await Task.WhenAll(Task.Run(() => first.SetFavorite(Id(1), true)), Task.Run(() => second.SetFavorite(Id(2), true)));
        AssertEqual(2, first.Read().Count, "Separate profiles sharing a host data directory must not lose concurrent additions");
        first.SetFavorite(Id(1), false);
        second.SetFavorite(Id(3), true);
        AssertEnumerableEqual(new[] { Id(2), Id(3) }, first.Read(), "Explicit intents must re-read the latest ordered ID list");
        foreach (var corrupt in new[] { "{broken", "{}", "[17]", new string(' ', FavoriteVoiceStore.MaximumFileBytes + 1), JsonSerializer.Serialize(Enumerable.Range(1, 301).Select(Id)) })
        {
            File.WriteAllText(FilePath(directory), corrupt);
            var before = File.ReadAllBytes(FilePath(directory));
            using var view = new SettingsViewModel(CreateContext(pluginSettingsDirectoryPath: directory), new Settings());
            AssertEqual(true, view.FavoritesError is not null, "Corrupt/oversized storage must be unavailable rather than look empty");
            view.ToggleFavoriteCommand.Execute(new VoiceSearchItem { Id = Id(1), Title = "New" });
            AssertEnumerableEqual(before, File.ReadAllBytes(FilePath(directory)), "A failed read must never overwrite a damaged or oversized ID file");
            AssertEqual(0, view.FavoriteCount, "Damaged persistence cannot report memory-only success");
        }
    }

    internal static async Task LeaseRetirementLinearizesIdWritesWithoutBlockingSnapshotsAsync()
    {
        var directory = CreateTempDirectory();
        var context = CreateContext();
        var runtime = SettingsStore.Load(context, DateTimeOffset.UtcNow, out var lease);
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var retireStarted = new ManualResetEventSlim();
        using var store = new FavoriteVoiceStore(directory, (temporary, destination) =>
        {
            started.Set();
            if (!release.Wait(RegressionExecution.Timeout)) throw new TimeoutException("Test release missing");
            File.Move(temporary, destination, true);
        });
        var writer = Task.Run(() => SettingsStore.TryExecuteWithWriteLease(runtime, lease, () => store.SetFavorite(Id(1), true)));
        AssertEqual(true, started.Wait(RegressionExecution.Timeout), "The authorized file transaction should reach its commit boundary");
        try
        {
            var snapshot = await Task.Run(() => SettingsStore.CaptureSnapshot(runtime)).WaitAsync(RegressionExecution.Timeout);
            AssertEqual(runtime.ApiKey, snapshot.ApiKey, "A slow favorite file write must not acquire the short settings snapshot lock");
            var retirement = Task.Run(() => { retireStarted.Set(); SettingsStore.Retire(runtime, lease); });
            AssertEqual(true, retireStarted.Wait(RegressionExecution.Timeout), "Retirement should be attempting the persistence gate");
            AssertEqual(false, retirement.IsCompleted, "Retirement must wait for an already-authorized atomic commit");
            release.Set();
            AssertEqual(true, await writer.WaitAsync(RegressionExecution.Timeout), "An already-authorized write should complete before retirement");
            await retirement.WaitAsync(RegressionExecution.Timeout);
            AssertEqual(false, SettingsStore.TryExecuteWithWriteLease(runtime, lease, () => store.SetFavorite(Id(2), true)), "Retirement that wins first must reject the next file write");
            AssertEnumerableEqual(new[] { Id(1) }, store.Read(), "Retired generation must not add another ID");
        }
        finally { release.Set(); }
    }

    internal static void MetadataCacheIsBoundedDetachedAndUsesAnIndependentClientTtl()
    {
        var clock = new MetadataTestClock();
        var cache = new VoiceMetadataCache(clock);
        var input = new CachedVoiceInfo { Title = "Detached", CoverImage = "remote-source", SampleAudioUrl = "https://platform.r2.fish.audio/sample.mp3?private=fixture" };
        cache.Put(VoiceMetadata.FromCachedVoice(Id(1), input));
        input.Title = "Later mutation";
        AssertEqual(true, cache.TryGet(Id(1), out var detached), "A fresh metadata entry is available");
        AssertEqual("Detached", detached.Title, "Cache entries must be detached from mutable selected/card metadata");
        AssertEqual("remote-source", detached.CoverImage, "Cache must retain the canonical remote cover source");
        AssertEqual(false, detached.ToString().Contains("private=fixture", StringComparison.Ordinal), "Metadata value printing must not expose signed-resource details");
        for (var number = 2; number <= VoiceMetadataCache.MaximumCount; number++) cache.Put(Metadata(number));
        cache.TryGet(Id(1), out _);
        cache.Put(Metadata(301));
        AssertEqual(true, cache.TryGet(Id(1), out _), "Recently used entries survive LRU eviction");
        AssertEqual(false, cache.TryGet(Id(2), out _), "LRU capacity should evict the least-recently-used entry");
        clock.Advance(VoiceMetadataCache.Lifetime);
        AssertEqual(false, cache.TryGet(Id(1), out _), "The explicit fifteen-minute client TTL must expire metadata independently of sample URL shape");
    }

    internal static async Task FavoriteMissesLoadOnlyVisiblePageAndRejectRemovedCardResultsAsync()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(FilePath(directory), JsonSerializer.Serialize(Enumerable.Range(1, 7).Select(Id)));
        var (service, http) = TestHttpServiceProxy.Create();
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new List<CancellationToken>();
        http.GetAsyncHandler = (_, _, token) => { tokens.Add(token); return pending.Task; };
        var settings = SelectedSettings();
        var cache = new VoiceMetadataCache();
        using var view = new SettingsViewModel(CreateContext(settings: settings, httpService: service, pluginSettingsDirectoryPath: directory), settings, null, null, voiceMetadataCache: cache);
        AssertEqual(0, http.GetCallCount, "Opening Search must not fetch all bookmarked voices");
        view.SwitchToFavoritesCommand.Execute(null);
        var oldPage = view.PendingFavoriteMetadataTask;
        AssertEqual(6, http.GetCallCount, "Only the six visible cache misses should issue public detail requests");
        var removed = view.FavoriteResults[0];
        AssertEqual(true, removed.IsLoadingMetadata && !removed.HasMetadata && removed.IsFavorite, "A miss must be a loading, removable ID placeholder");
        view.SelectVoiceCommand.Execute(removed);
        AssertEqual(Id(99), settings.VoiceId, "A metadata placeholder cannot Apply a selection");
        view.FavoriteNextPageCommand.Execute(null);
        var newPage = view.PendingFavoriteMetadataTask;
        AssertEqual(true, tokens.Take(6).All(token => token.IsCancellationRequested), "Changing pages must cancel the previous page's requests");
        AssertEqual(7, http.GetCallCount, "The new page should fetch only its single visible cache miss");
        view.ToggleFavoriteCommand.Execute(view.FavoriteResults[0]);
        var afterRemoval = view.PendingFavoriteMetadataTask;
        AssertEqual(1, view.FavoritePage, "Removing the only final-page placeholder must immediately clamp");
        pending.SetResult(Detail(7, "Late detail"));
        await Task.WhenAll(oldPage, newPage, afterRemoval).WaitAsync(RegressionExecution.Timeout);
        AssertEqual(false, cache.TryGet(Id(7), out _), "Removed originating-card results may not publish metadata into the shared cache");
        using var store = new FavoriteVoiceStore(directory);
        AssertEqual(false, store.Read().Contains(Id(7)), "Late metadata cannot restore a removed bookmark");
        http.GetAsyncHandler = null;
        http.GetException = new HttpRequestException("Synthetic unavailable voice", null, System.Net.HttpStatusCode.NotFound);
        view.SwitchToSearchCommand.Execute(null);
        view.SwitchToFavoritesCommand.Execute(null);
        await view.PendingFavoriteMetadataTask;
        AssertEqual(6, view.FavoriteCount, "A metadata failure must not delete favorite IDs");
        // The prior successful current-page responses populated entries 1..6. Use a cold cache below.
        using var cold = new SettingsViewModel(CreateContext(httpService: service, pluginSettingsDirectoryPath: directory), new Settings());
        cold.SwitchToFavoritesCommand.Execute(null);
        await cold.PendingFavoriteMetadataTask;
        AssertEqual(true, cold.FavoriteResults.All(card => !card.HasMetadata && card.MetadataError is not null && card.IsFavorite), "Failed metadata keeps unavailable but removable ID cards");
    }

    internal static void BookmarkMutationsCannotRefreshOrReplaceRemoteMetadata()
    {
        var clock = new MetadataTestClock();
        var cache = new VoiceMetadataCache(clock);
        cache.Put(VoiceMetadata.FromCachedVoice(Id(1), new() { Title = "Remote T0" }));
        var staleCard = new VoiceSearchItem { Id = Id(1), Title = "Retained T0 card" };
        clock.Advance(TimeSpan.FromMinutes(5));
        cache.Put(VoiceMetadata.FromCachedVoice(Id(1), new() { Title = "Remote T5" }));
        var directory = CreateTempDirectory();
        using var view = new SettingsViewModel(CreateContext(pluginSettingsDirectoryPath: directory), new Settings(), null, null, voiceMetadataCache: cache);
        clock.Advance(TimeSpan.FromMinutes(5));
        view.ToggleFavoriteCommand.Execute(staleCard);
        AssertEqual(true, cache.TryGet(Id(1), out var newer), "A bookmark should retain a fresh remote entry");
        AssertEqual("Remote T5", newer.Title, "A retained old card cannot overwrite newer remote metadata");
        clock.Advance(TimeSpan.FromMinutes(10));
        AssertEqual(false, cache.TryGet(Id(1), out _), "The remote T5 entry must expire at T20 rather than being renewed by the T10 bookmark");
        view.ToggleFavoriteCommand.Execute(staleCard);
        AssertEqual(false, cache.TryGet(Id(1), out _), "Removing a favorite must not revive expired metadata from a retained card");
        view.ToggleFavoriteCommand.Execute(staleCard);
        AssertEqual(false, cache.TryGet(Id(1), out _), "Adding a favorite without a cache hit must not promote stale card metadata");
    }

    internal static async Task PluginCacheIsSharedAcrossSearchByIdAndReinitializationAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var directory = CreateTempDirectory();
        var (firstService, firstHttp) = TestHttpServiceProxy.Create();
        firstHttp.GetResponseJson = JsonSerializer.Serialize(new { total = 1, items = new[] { new { _id = Id(1), title = "Shared search", samples = Array.Empty<object>() } } });
        using var plugin = new Main();
        plugin.Init(CreateContext(httpService: firstService, pluginSettingsDirectoryPath: directory));
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
        var original = plugin.GetOrCreateSettingsViewModel();
        await original.SearchVoicesCommand.ExecuteAsync(null);
        original.VoiceIdInput = Id(1);
        await original.SubmitVoiceIdCommand.ExecuteAsync(null);
        AssertEqual(1, firstHttp.GetCallCount, "By-ID must reuse the Search metadata cache hit");
        original.ToggleFavoriteCommand.Execute(original.SearchResults[0]);
        var (nextService, nextHttp) = TestHttpServiceProxy.Create();
        plugin.Init(CreateContext(httpService: nextService, pluginSettingsDirectoryPath: directory));
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
        var replacement = plugin.GetOrCreateSettingsViewModel();
        replacement.SwitchToFavoritesCommand.Execute(null);
        await replacement.PendingFavoriteMetadataTask;
        AssertEqual("Shared search", replacement.FavoriteResults.Single().Title, "The plugin-owned cache must outlive a settings generation");
        AssertEqual(0, nextHttp.GetCallCount, "Reinitialized Favorites should not refetch a live cache hit");
        original.ToggleFavoriteCommand.Execute(new VoiceSearchItem { Id = Id(2), Title = "Retired" });
        using var store = new FavoriteVoiceStore(directory);
        AssertEnumerableEqual(new[] { Id(1) }, store.Read(), "Retired views cannot mutate the shared persistent ID file");
    }

    internal static async Task SearchAndFavoritesReuseExistingCoverCacheAndClearKeepsIdsAsync()
    {
        var directory = CreateTempDirectory();
        var cacheDirectory = CreateTempDirectory();
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = JsonSerializer.Serialize(new { total = 1, items = new[] { new { _id = Id(1), title = "Shared cover", cover_image = "remote-cover", samples = Array.Empty<object>() } } });
        http.GetAsStreamAsyncHandler = (_, _, _) => Task.FromResult<Stream>(new MemoryStream([0xFF, 0xD8, 0xFF, 1, 2, 3]));
        using var view = new SettingsViewModel(CreateContext(httpService: service, pluginSettingsDirectoryPath: directory, pluginCacheDirectoryPath: cacheDirectory), new Settings());
        await view.SearchVoicesCommand.ExecuteAsync(null);
        var coverPath = Path.Combine(cacheDirectory, "cover_images", Id(1) + ".jpg");
        await WaitUntilAsync(() => File.Exists(coverPath));
        var before = http.GetAsStreamCallCount;
        view.ToggleFavoriteCommand.Execute(view.SearchResults[0]);
        view.SwitchToFavoritesCommand.Execute(null);
        AssertEqual(before, http.GetAsStreamCallCount, "Search and Favorites must reuse the same local cover file without another download");
        AssertEqual(new Uri(coverPath).AbsoluteUri, view.FavoriteResults.Single().CoverUrl, "Favorite cover display must resolve through the existing cover cache");
        await view.ClearCoverImageCacheCommand.ExecuteAsync(null);
        AssertEqual(false, File.Exists(coverPath), "Clear covers removes the shared image file");
        AssertEqual(1, view.FavoriteCount, "Clearing covers must keep favorite IDs");
        using var store = new FavoriteVoiceStore(directory);
        AssertEnumerableEqual(new[] { Id(1) }, store.Read(), "Clear covers cannot touch the persistent data file");
        view.SwitchToSearchCommand.Execute(null);
        view.SwitchToFavoritesCommand.Execute(null);
        await WaitUntilAsync(() => File.Exists(coverPath));
        AssertEqual(before + 1, http.GetAsStreamCallCount, "The next favorite display should redownload through the existing image cache");
        AssertEqual(1, Directory.GetDirectories(cacheDirectory).Length, "Favorites must not create a duplicate image-cache namespace");
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.Add(RegressionExecution.Timeout);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Offline cache work did not finish");
            await Task.Delay(10);
        }
    }

    private sealed class MetadataTestClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        internal void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
