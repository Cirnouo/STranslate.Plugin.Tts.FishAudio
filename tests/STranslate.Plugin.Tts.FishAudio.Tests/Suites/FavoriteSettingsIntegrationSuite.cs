using System.Text.Json;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Caching;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static TestAssertions;
using static FavoriteVoicesSuite;

internal static class FavoriteSettingsIntegrationSuite
{
    internal static void IdFileTransactionsLeaveStreamingSettingsAndSnapshotsUntouched()
    {
        var directory = CreateTempDirectory();
        var storage = new Settings
        {
            ApiKey = AppliedKey, VoiceId = Id(99), SelectedModel = "s2-pro", AudioFormat = "mp3", Mp3Bitrate = 64,
            Speed = 1.25, Volume = -1.3, Temperature = 0.4, TopP = 0.8, Latency = "low", Normalize = true,
            ConditionOnPreviousChunks = false, CachedVoice = new() { Title = "Selected metadata" },
        };
        var context = CreateContext(settings: storage, pluginSettingsDirectoryPath: directory);
        var runtime = SettingsStore.Load(context, new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), out var lease);
        var before = JsonSerializer.Serialize(runtime);
        var snapshot = SettingsStore.CaptureSnapshot(runtime);
        var cache = new VoiceMetadataCache();
        using (var view = new SettingsViewModel(context, runtime, null, null, settingsWriteLease: lease, voiceMetadataCache: cache))
        {
            view.ToggleFavoriteCommand.Execute(new VoiceSearchItem { Id = Id(1), Title = "Favorite metadata", CoverImage = "remote-cover-source" });
            AssertEqual(1, view.FavoriteCount, "A favorite transaction should publish its durable ID");
        }
        using var failedStore = new FavoriteVoiceStore(directory, (_, _) => throw new IOException("Synthetic favorite file failure"));
        using var failedView = new SettingsViewModel(context, runtime, null, null, settingsWriteLease: lease, voiceMetadataCache: cache, favoriteVoiceStore: failedStore);
        var durableBefore = File.ReadAllText(FilePath(directory));
        failedView.ToggleFavoriteCommand.Execute(failedView.FavoriteResults[0]);
        AssertEqual(durableBefore, File.ReadAllText(FilePath(directory)), "Failed removal preserves the last durable ID file");
        AssertEqual(1, failedView.FavoriteCount, "Failed removal preserves the displayed favorite");
        AssertEqual(before, JsonSerializer.Serialize(runtime), "Favorite success/failure must leave format, bitrate and all runtime settings untouched");
        AssertEqual(before, JsonSerializer.Serialize(storage), "Favorite success/failure must not mutate host Settings");
        AssertEqual(before, JsonSerializer.Serialize(snapshot), "Captured synthesis settings must not gain favorite state");
        runtime.CachedVoice!.Title = "Later selected edit";
        AssertEqual("Selected metadata", snapshot.CachedVoice!.Title, "Existing selected metadata snapshot deep-copy behavior must remain intact");
        AssertEqual(0, ((ContextProxy)(object)context).SaveCount, "Favorite persistence must never call host Settings Save");
        AssertEqual(false, File.ReadAllText(FilePath(directory)).Contains("Favorite metadata", StringComparison.Ordinal), "Favorite metadata must stay out of durable ID storage");
    }

    internal static void LegacySettingsAndFavoriteIdsRestartIndependently()
    {
        var legacy = SettingsStorageSuite.DeserializeSettings(JsonSerializer.Serialize(new
        {
            SchemaVersion = 1, ApiKey = AppliedKey, VoiceId = Id(99), SelectedModel = "s2-pro", Speed = 1.35,
            Volume = -2.4, NormalizeLoudness = false, Temperature = 0.55, TopP = 0.85, Latency = "balanced",
            Normalize = true, Mp3Bitrate = 128, ConditionOnPreviousChunks = false,
            CachedVoice = new CachedVoiceInfo { Title = "Saved selection", Description = "Selected description", TaskCount = 42 },
            IsS21ProFreePromoDismissed = true,
        }));
        AssertEqual(2, legacy.SchemaVersion, "Existing schema-1 Settings still migrate to schema 2");
        AssertEqual("mp3", legacy.AudioFormat, "Independent favorites must not change the legacy MP3 fallback");
        var expected = JsonSerializer.Serialize(legacy);
        var directory = CreateTempDirectory();
        File.WriteAllText(FilePath(directory), JsonSerializer.Serialize(new[] { Id(1), Id(2) }));
        using (var view = new SettingsViewModel(CreateContext(settings: legacy, pluginSettingsDirectoryPath: directory), legacy))
        {
            AssertEqual(2, view.FavoriteCount, "Independent IDs must load alongside migrated Settings");
            AssertEqual(false, view.FavoriteResults[0].HasMetadata, "A restart without memory cache must show a removable ID placeholder");
            AssertEqual(Id(1), view.FavoriteResults[0].Title, "A cache miss displays the ID instead of dropping the favorite");
        }
        AssertEqual(expected, JsonSerializer.Serialize(legacy), "Loading favorites must preserve the key, selected voice/cache and every synthesis parameter");
        using var restarted = new SettingsViewModel(CreateContext(settings: legacy, pluginSettingsDirectoryPath: directory), legacy);
        AssertEqual(2, restarted.FavoriteCount, "ID persistence is independent of in-memory metadata lifetime");
        AssertEqual(false, JsonSerializer.Serialize(legacy).Contains("FavoriteVoices", StringComparison.Ordinal), "No new settings field or unshipped settings-metadata migration should remain");
    }
}
