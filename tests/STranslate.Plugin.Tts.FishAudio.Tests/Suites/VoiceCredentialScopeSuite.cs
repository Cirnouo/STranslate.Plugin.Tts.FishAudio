using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Caching;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using static RuntimeOverrideScopes;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static TestAssertions;

internal static class VoiceCredentialScopeSuite
{
    private const string Id = "fedcba9876543210fedcba9876543210";

    internal static void CacheWritesAndQueuedEventsKeepCapturedCredentialScope()
    {
        var cache = new VoiceMetadataCache();
        var scopeA = VoiceMetadataCache.CredentialScope("account-a");
        var scopeB = VoiceMetadataCache.CredentialScope("account-b");
        var settings = new Settings { ApiKey = "account-a", VoiceId = Id, CachedVoice = new() { Title = "Private A" } };
        using var view = new SettingsViewModel(CreateContext(settings: settings), settings, null, null, voiceMetadataCache: cache);
        var queued = new List<Action>();
        SettingsViewModel.UiThreadInvokerOverride = queued.Add;
        try
        {
            cache.Put(VoiceMetadata.FromCachedVoice(Id, new() { Title = "Queued A", SampleAudioUrl = "https://platform.r2.fish.audio/a.mp3" }), scopeA);
            view.ApiKey = "account-b";
            view.ByIdResults = [new VoiceSearchItem { Id = Id, HasMetadata = false }];
            // This represents A reaching its cache write after B's facade has already been constructed.
            using var replacement = new SettingsViewModel(CreateContext(), new Settings { ApiKey = "account-b" }, null, null, voiceMetadataCache: cache);
            cache.Put(VoiceMetadata.FromCachedVoice(Id, new() { Title = "Late A" }), scopeA);
            foreach (var action in queued.ToArray()) action();
            AssertEqual(false, cache.TryGet(Id, out _, scopeB), "A's captured cache scope cannot populate B's cache partition");
            AssertEqual(false, view.ByIdResults.Single().HasMetadata, "Queued A events must recheck accepted credentials before UI publication");
            AssertEqual(Id, view.CachedVoiceId, "Accepted key edits preserve selected identity");
            AssertEqual("Private A", view.CachedVoiceTitle, "Queued old-account events cannot replace retained selected metadata");
            AssertEqual(null, view.CachedVoiceSampleUrl, "Queued old-account events cannot add a sample to the retained snapshot");
        }
        finally { SettingsViewModel.UiThreadInvokerOverride = null; }
    }

    internal static async Task RejectedCredentialSavesRetainAcceptedKeyAsync()
    {
        foreach (var retired in new[] { false, true })
        {
            var settings = new Settings { ApiKey = "accepted-account", VoiceId = Id, CachedVoice = new() { Title = "Accepted voice" } };
            var (service, http) = TestHttpServiceProxy.Create();
            http.GetResponseJson = "{\"total\":0,\"items\":[]}";
            var context = CreateContext(settings: settings, httpService: service);
            var runtime = SettingsStore.Load(context, DateTimeOffset.UtcNow, out var lease);
            using var view = new SettingsViewModel(context, runtime, null, null, settingsWriteLease: lease);
            var credentialIdentity = runtime.CredentialIdentity;
            if (retired) SettingsStore.Retire(runtime, lease);
            else ((ContextProxy)(object)context).SaveSettings = _ => throw new IOException("Synthetic save failure");
            try { view.ApiKey = "rejected-account"; }
            catch (IOException) { }
            AssertEqual("accepted-account", runtime.ApiKey, "Rejected key persistence must preserve accepted settings");
            AssertEqual("accepted-account", view.ApiKey, "Rejected key persistence must restore the visible draft");
            AssertEqual("Accepted voice", view.CachedVoiceTitle, "Rejected key edits cannot clear accepted metadata");
            AssertEqual(true, ReferenceEquals(credentialIdentity, runtime.CredentialIdentity), "Failed or retired key saves cannot change publication identity");
            await view.SearchVoicesCommand.ExecuteAsync(null);
            AssertEqual("Bearer accepted-account", AssertHeaders(http.LastGetOptions, "Accepted metadata request")["Authorization"], "Metadata must derive credentials from accepted settings");
        }
    }

    internal static async Task FailedReplacementConstructionPreservesAcceptedCacheAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var cache = new VoiceMetadataCache();
        var calls = 0;
        using var plugin = new Main((context, settings, lease, cycle) =>
        {
            var view = new SettingsViewModel(context, settings, null, null, startupCreditRefreshCycle: cycle,
                settingsWriteLease: lease, voiceMetadataCache: cache);
            if (++calls == 2)
            {
                view.Dispose();
                throw new InvalidOperationException("Synthetic failure after replacement construction");
            }
            return view;
        });
        var (service, http) = TestHttpServiceProxy.Create();
        plugin.Init(CreateContext(settings: new Settings { ApiKey = "account-a" }, httpService: service), FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var original = plugin.GetOrCreateSettingsViewModel();
        cache.Put(VoiceMetadata.FromCachedVoice(Id, new() { Title = "Cached A" }), VoiceMetadataCache.CredentialScope("account-a"));
        try
        {
            plugin.Init(CreateContext(settings: new Settings { ApiKey = "account-b" }), FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
            throw new Exception("Expected replacement construction to fail");
        }
        catch (InvalidOperationException exception) when (exception.Message == "Synthetic failure after replacement construction") { }
        AssertEqual(original, plugin.GetOrCreateSettingsViewModel(), "A failed replacement preserves the previous accepted facade");
        original.VoiceIdInput = Id;
        await original.SubmitVoiceIdCommand.ExecuteAsync(null);
        AssertEqual("Cached A", original.ByIdResults.Single().Title, "An uncommitted B constructor must not clear A's accepted metadata");
        AssertEqual(0, http.GetCallCount, "The restored A facade should keep its credential-scoped cache hit");
    }

    internal static async Task ReentrantPreviewMetadataEventCannotRestoreOldAccountAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        foreach (var earlierSubscriber in new[] { false, true })
        foreach (var favorite in new[] { false, true })
        {
            var cache = new VoiceMetadataCache();
            var settings = new Settings { ApiKey = "preview-a", VoiceId = Id,
                CachedVoice = new() { Title = "Selected A", SampleAudioUrl = PreviewSuite.CreateExpiredPreviewUrl("a.mp3") } };
            var (service, http) = TestHttpServiceProxy.Create();
            http.GetResponseJson = "{\"_id\":\"" + Id + "\",\"title\":\"Refreshed A\",\"samples\":[{\"audio\":\"https://platform.r2.fish.audio/a.mp3\"}]}";
            var player = new TestPreviewAudioPlayer();
            var card = new VoiceSearchItem { Id = Id, Title = "Favorite A", SampleAudioUrl = PreviewSuite.CreateExpiredPreviewUrl("favorite-a.mp3") };
            SettingsViewModel? view = null;
            string? cardAtKeyChange = null;
            Action<string, VoiceMetadata> changeKey = (_, _) =>
            {
                view!.ApiKey = "preview-b";
                view.ApiKey = "preview-a";
                cardAtKeyChange = System.Text.Json.JsonSerializer.Serialize(card);
            };
            if (earlierSubscriber) cache.MetadataUpdated += changeKey;
            using var ownedView = view = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings, null, null,
                previewAudioPlayerFactory: () => player, voiceMetadataCache: cache);
            view.FavoriteResults = [card];
            if (!earlierSubscriber) cache.MetadataUpdated += changeKey;
            if (favorite) await view.ToggleSearchItemPreviewCommand.ExecuteAsync(card);
            else await view.ToggleDisplayPreviewCommand.ExecuteAsync(null);
            AssertEqual("preview-a", settings.ApiKey, "The cache event must reach both reentrant accepted-key edits");
            AssertEqual(cardAtKeyChange, System.Text.Json.JsonSerializer.Serialize(card), "An old preview continuation cannot modify the card after the accepted-key boundary");
            AssertEqual("Selected A", settings.CachedVoice?.Title, "Old selected preview cannot overwrite the retained snapshot under B");
            AssertEqual("Selected A", view.CachedVoiceTitle, "Selected display survives the key edit without late preview publication");
            AssertEqual(0, player.PlayCallCount, "An invalidated credential preview cannot start playback");
        }
    }

    internal static void QueuedStartupApplyRechecksCredentialInsideDispatcher()
    {
        var settings = new Settings { ApiKey = "startup-a", VoiceId = Id, CachedVoice = new() { Title = "Old" } };
        using var view = new SettingsViewModel(CreateContext(settings: settings), settings);
        var queued = new List<Action>();
        SettingsViewModel.UiThreadInvokerOverride = queued.Add;
        try
        {
            view.ApplyRefreshedCachedVoice(Id, new() { Title = "Late startup A", SampleAudioUrl = "https://platform.r2.fish.audio/a.mp3" }, "startup-a");
            view.ApiKey = "startup-b";
            foreach (var action in queued.ToArray()) action();
            AssertEqual("Old", view.CachedVoiceTitle, "Deferred startup UI publication must reject replaced credentials while retaining the existing display");
            AssertEqual(null, view.CachedVoiceSampleUrl, "Deferred startup UI publication cannot restore an old sample");
        }
        finally { SettingsViewModel.UiThreadInvokerOverride = null; }
    }
}
