using System.Text.Json;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Caching;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.Presentation;
using static RuntimeOverrideScopes;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static TestAssertions;

internal static class VoiceLanguageSuite
{
    internal static async Task SearchSelectionPersistsProviderLanguagesAsync()
    {
        var settings = new Settings();
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = """
            {"total":1,"items":[{"_id":"11111111111111111111111111111111","title":"Voice","languages":[" EN ","zh_CN","en",null,"","xx-Unknown","ja","ko"]}]}
            """;
        using var view = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings);
        await view.SearchVoicesCommand.ExecuteAsync(null);
        view.SelectVoiceCommand.Execute(view.SearchResults.Single());
        using var saved = JsonDocument.Parse(JsonSerializer.Serialize(settings));
        var cached = saved.RootElement.GetProperty("CachedVoice");
        AssertEqual(true, cached.TryGetProperty("Languages", out var languages), "Applying a searched voice must persist provider language metadata");
        AssertEnumerableEqual(new[] { "en", "zh-cn", "xx-unknown", "ja", "ko" }, languages.EnumerateArray().Select(item => item.GetString()!), "Language codes preserve provider order and all valid remaining entries");
    }

    internal static async Task RunAllAsync()
    {
        await SearchSelectionPersistsProviderLanguagesAsync();
        await ByIdAndFavoriteRefreshPreserveAndClearLanguagesAsync();
        SettingsAndCacheDetachAndNormalizeLanguages();
        await StartupAndSelectedPreviewReplaceLanguagesAsync();
        await DirectPreviewRefreshAssignsLanguagesAsync();
    }

    internal static async Task ByIdAndFavoriteRefreshPreserveAndClearLanguagesAsync()
    {
        var settings = new Settings();
        var cache = new VoiceMetadataCache();
        var directory = CreateTempDirectory();
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = """{"_id":"22222222222222222222222222222222","title":"By ID","languages":["FR","de","tr","ru"]}""";
        using var view = new SettingsViewModel(CreateContext(settings: settings, httpService: service,
            pluginSettingsDirectoryPath: directory), settings, null, null, voiceMetadataCache: cache);
        view.VoiceIdInput = "22222222222222222222222222222222";
        await view.SubmitVoiceIdCommand.ExecuteAsync(null);
        var card = view.ByIdResults.Single();
        AssertEnumerableEqual(new[] { "fr", "de", "tr", "ru" }, card.Languages, "Independent By-ID HTTP result retains every normalized language");
        view.SelectVoiceCommand.Execute(card);
        AssertEnumerableEqual(new[] { "fr", "de", "tr", "ru" }, view.CachedVoiceLanguages, "Selected facade follows applied card metadata");
        view.ToggleFavoriteCommand.Execute(card);
        view.SwitchToFavoritesCommand.Execute(null);
        AssertEnumerableEqual(new[] { "fr", "de", "tr", "ru" }, view.FavoriteResults.Single().Languages, "Favorite cache hit retains languages");
        cache.Put(VoiceMetadata.FromCachedVoice(card.Id, new() { Title = "Refresh", Languages = new[] { "JA" } }));
        AssertEnumerableEqual(new[] { "ja" }, view.FavoriteResults.Single().Languages, "Cache update replaces favorite languages");
        cache.Put(VoiceMetadata.FromCachedVoice(card.Id, new() { Title = "No languages" }));
        AssertEqual(0, card.Languages.Count, "Authoritative empty metadata clears a By-ID card's former languages");
        AssertEqual(0, view.FavoriteResults.Single().Languages.Count, "Authoritative empty metadata clears favorite languages");
        view.ClearVoiceCommand.Execute(null);
        AssertEqual(0, view.CachedVoiceLanguages.Count, "Explicit selection clearing clears selected language metadata");
        AssertEqual<CachedVoiceInfo?>(null, settings.CachedVoice, "Clearing selection removes the saved snapshot");

        // A new credential has no cache entry. Refreshing the page first resets the reused card.
        view.ApiKey = "another-account";
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        http.GetAsyncHandler = (_, _, _) => pending.Task;
        view.FavoriteResults.Single().Languages = new[] { "stale" };
        view.SwitchToFavoritesCommand.Execute(null);
        AssertEqual(0, view.FavoriteResults.Single().Languages.Count, "A reused favorite placeholder clears old language tags");
        pending.SetResult("""{"title":"Fresh favorite","languages":["KO","en"]}""");
        await view.PendingFavoriteMetadataTask;
        AssertEnumerableEqual(new[] { "ko", "en" }, view.FavoriteResults.Single().Languages, "A favorite detail miss maps provider languages");
    }

    internal static void SettingsAndCacheDetachAndNormalizeLanguages()
    {
        var constructorLanguages = new[] { "en" };
        var directMetadata = new VoiceMetadata("11111111111111111111111111111111", "Voice", "", "", "", 0, null, constructorLanguages);
        constructorLanguages[0] = "changed";
        AssertEnumerableEqual(new[] { "en" }, directMetadata.Languages, "Metadata construction detaches mutable language arrays");
        var raw = new List<string> { " EN ", "zh_CN", "EN", null!, "", " x-New ", "ja", "ko" };
        var cached = new CachedVoiceInfo { Title = "Selected", Languages = raw };
        var metadata = VoiceMetadata.FromCachedVoice("11111111111111111111111111111111", cached);
        var settings = new Settings { VoiceId = metadata.VoiceId, CachedVoice = cached };
        var snapshot = SettingsStore.CaptureSnapshot(settings);
        raw[0] = "mutated";
        cached.Languages = new[] { "fr" };
        AssertEnumerableEqual(new[] { "en", "zh-cn", "x-new", "ja", "ko" }, metadata.Languages, "Cache values detach provider collections and preserve unknown codes");
        AssertEnumerableEqual(new[] { "en", "zh-cn", "x-new", "ja", "ko" }, snapshot.CachedVoice!.Languages, "Settings snapshot deep copies nested metadata");
        var copy = metadata.ToCachedVoice();
        copy.Languages = new[] { "de" };
        AssertEnumerableEqual(new[] { "en", "zh-cn", "x-new", "ja", "ko" }, metadata.ToCachedVoice().Languages, "Cache outputs cannot mutate retained metadata");
        AssertEqual(false, metadata.Languages is string[] || metadata.Languages is List<string>, "Published metadata must not expose a mutable backing collection");
        var context = CreateContext(settings: settings);
        var runtime = SettingsStore.Load(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1), out var lease);
        runtime.CachedVoice!.Languages = new[] { "PT_BR" };
        SettingsStore.Commit(context, runtime, lease);
        runtime.CachedVoice.Languages = new[] { "ja" };
        AssertEnumerableEqual(new[] { "pt-br" }, settings.CachedVoice!.Languages, "Leased commit detaches runtime and host-storage languages");

        foreach (var field in new[] { "", ",\"Languages\":null", ",\"Languages\":42", ",\"Languages\":[\"en\"],\"Languages\":[\"ja\"]" })
        {
            var legacy = JsonSerializer.Deserialize<Settings>("{\"SchemaVersion\":2,\"CachedVoice\":{\"Title\":\"Legacy\"" + field + "}}")!;
            AssertEqual(0, legacy.CachedVoice!.Languages.Count, "Missing/null/malformed/duplicate metadata safely defaults independently");
            AssertEqual("Legacy", legacy.CachedVoice.Title, "Malformed language field preserves other selected metadata");
        }
        var parsed = JsonSerializer.Deserialize<Settings>("""{"SchemaVersion":2,"CachedVoice":{"Title":"Voice","Languages":[" EN ",null,42,"zh_CN","en","","XX_zz","ja","ko"]}}""")!;
        AssertEnumerableEqual(new[] { "en", "zh-cn", "xx-zz", "ja", "ko" }, parsed.CachedVoice!.Languages, "Parser salvages valid entries in order independently of malformed entries");
        AssertEqual(true, parsed.NeedsCanonicalSave, "Malformed/normalized language metadata requests canonical cleanup");
        var reloaded = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(parsed))!;
        AssertEnumerableEqual(new[] { "en", "zh-cn", "xx-zz", "ja", "ko" }, reloaded.CachedVoice!.Languages, "Selected snapshot survives canonical save/reload without a display cap");
        AssertEqual(2, reloaded.SchemaVersion, "Language metadata remains schema-2 compatible");
        var nullDto = VoiceMetadata.FromModel(new ModelEntity { Languages = null! });
        AssertEqual(0, nullDto.Languages.Count, "Explicit null provider metadata has a safe empty default");
    }

    internal static async Task StartupAndSelectedPreviewReplaceLanguagesAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings { VoiceId = "33333333333333333333333333333333",
            CachedVoice = new() { Title = "Old", Languages = new[] { "en" } } };
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = """{"title":"Startup","languages":["ZH_cn","JA"]}""";
        using (var plugin = new Main())
        {
            plugin.Init(CreateContext(settings: settings, httpService: service), FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
            await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
            AssertEnumerableEqual(new[] { "zh-cn", "ja" }, settings.CachedVoice!.Languages, "Startup refresh persists normalized languages");
            AssertEnumerableEqual(new[] { "zh-cn", "ja" }, plugin.GetOrCreateSettingsViewModel().CachedVoiceLanguages, "Startup selected display exposes refreshed languages");
        }
        settings = new Settings { VoiceId = "33333333333333333333333333333333",
            CachedVoice = new() { Title = "Old", Languages = new[] { "en" }, SampleAudioUrl = ExpiredUrl } };
        http.GetResponseJson = """{"title":"Preview","languages":[],"samples":[{"audio":"https://platform.r2.fish.audio/new.mp3"}]}""";
        using var view = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings, null, null,
            previewAudioPlayerFactory: () => new TestPreviewAudioPlayer());
        await view.ToggleDisplayPreviewCommand.ExecuteAsync(null);
        AssertEqual(0, settings.CachedVoice!.Languages.Count, "Selected preview authoritative empty languages clear the saved snapshot");
        AssertEqual(0, view.CachedVoiceLanguages.Count, "Selected preview authoritative empty languages clear visible tags");
    }

    internal static async Task DirectPreviewRefreshAssignsLanguagesAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var card = new VoiceSearchItem { Id = "44444444444444444444444444444444", Title = "Old", Languages = new[] { "en" }, SampleAudioUrl = ExpiredUrl };
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = """{"title":"Refresh","languages":["KO","ja"],"samples":[{"audio":"https://platform.r2.fish.audio/new.mp3"}]}""";
        var changed = false;
        card.PropertyChanged += (_, args) => changed |= args.PropertyName == nameof(card.Languages);
        // No ViewModel cache subscriber: this proves the coordinator's direct assignment path.
        using var coordinator = new PreviewRefreshCoordinator(CreateContext(httpService: service), () => null, () => null,
            () => "", () => "", () => new() { card }, () => null, () => { }, (_, stream) => stream.Dispose(),
            _ => false, _ => { }, (_, _, _, _) => "", () => false);
        await coordinator.ToggleSearchItemPreviewAsync(card);
        AssertEnumerableEqual(new[] { "ko", "ja" }, card.Languages, "Direct search preview refresh replaces language metadata without cache event assistance");
        AssertEqual(true, changed, "Replacing card languages notifies bindings");
        card.SampleAudioUrl = ExpiredUrl;
        http.GetResponseJson = """{"title":"Empty","languages":null,"samples":[{"audio":"https://platform.r2.fish.audio/new.mp3"}]}""";
        coordinator.OnSearchResultsChanged();
        await coordinator.ToggleSearchItemPreviewAsync(card); // Stops the previous playback owner.
        await coordinator.ToggleSearchItemPreviewAsync(card);
        AssertEqual(0, card.Languages.Count, "Direct preview null language metadata clears former tags");
    }

    private const string ExpiredUrl = "https://platform.r2.fish.audio/old.mp3?X-Amz-Date=20200101T000000Z&X-Amz-Expires=1";
}
