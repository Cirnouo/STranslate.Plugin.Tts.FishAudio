using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using STranslate.Plugin.Tts.FishAudio.Caching;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Streaming;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static FavoriteVoicesSuite;
using static RuntimeOverrideScopes;
using static TestAssertions;

internal static class SystemProxyAndSelectedFavoritesSuite
{
    internal static async Task RetiredProxyInputsCannotBlockFormalReadingAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        foreach (var mode in new[] { "direct", "custom", "unknown" })
        {
            var settings = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new
            {
                SchemaVersion = 2, ApiKey = AppliedKey, VoiceId = Id(1), SelectedModel = "s2-pro",
                StreamingProxyMode = mode, StreamingProxyAddress = "invalid-retired-private-address",
            }))!;
            var session = new StreamingSessionDouble();
            using var plugin = StreamingEntrySuite.CreatePlugin(() => session);
            plugin.Init(CreateContext(settings: settings));
            await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
            network.Set(true);
            await plugin.PlayAudioAsync("Retired routing preferences cannot block reading.");
            AssertEqual(1, session.Calls, "Obsolete direct/custom/unknown settings cannot block the formal entry point");
            AssertEqual(Id(1), session.Request!.VoiceId, "Proxy removal must preserve the selected voice in the synthesis snapshot");
            network.Set(false);
        }
    }

    internal static void RetiredProxyInputsAreDiscardedAndSocketUsesSystemProxy()
    {
        var previous = HttpClient.DefaultProxy;
        var systemProxy = new WebProxy("http://system-fixture.invalid:3128");
        HttpClient.DefaultProxy = systemProxy;
        try
        {
            foreach (var mode in new[] { "direct", "custom", "unknown" })
            {
                var settings = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new
                {
                    SchemaVersion = 2, ApiKey = AppliedKey, VoiceId = Id(1), SelectedModel = "s2-pro",
                    AudioFormat = "mp3", Mp3Bitrate = 64, Speed = 1.25,
                    StreamingProxyMode = mode, StreamingProxyAddress = "invalid-retired-private-address",
                }))!;
                var context = CreateContext(settings: settings);
                var loaded = SettingsStore.Load(context, DateTimeOffset.UtcNow);
                var canonical = JsonSerializer.Serialize(loaded);
                AssertEqual(false, canonical.Contains("StreamingProxy", StringComparison.Ordinal),
                    "An ordinary canonical save must discard retired proxy settings without a schema bump");
                AssertEqual(1, ((ContextProxy)(object)context).SaveCount, "Retired fields require one canonical cleanup save");
                AssertEqual(2, loaded.SchemaVersion, "Proxy removal retains settings schema 2");
                AssertEqual(64, loaded.Mp3Bitrate, "Discarding proxy fields must preserve remembered bitrate");
                AssertEqual(1.25, loaded.Speed, "Discarding proxy fields must preserve synthesis parameters");
                using var socket = new FishAudioSocket(new SynthesisRequest(SettingsStore.CaptureSnapshot(loaded)));
                var transport = (ClientWebSocket)typeof(FishAudioSocket).GetField("_socket", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(socket)!;
                AssertEqual(true, ReferenceEquals(systemProxy, transport.Options.Proxy),
                    "Legacy direct/custom fields must not override the actual WebSocket's system proxy");
            }
        }
        finally { HttpClient.DefaultProxy = previous; }
    }

    internal static void SelectedFavoriteTracksApplyClearBrowseMutationsAndFailedWrites()
    {
        var directory = CreateTempDirectory();
        var settings = SelectedSettings();
        var cache = new VoiceMetadataCache();
        cache.Put(Metadata(99));
        cache.Put(Metadata(1));
        var rejectWrite = false;
        using var store = new FavoriteVoiceStore(directory, (temporary, destination) =>
        {
            if (rejectWrite) throw new IOException("Synthetic favorite commit failure");
            File.Move(temporary, destination, true);
        });
        var context = CreateContext(settings: settings, pluginSettingsDirectoryPath: directory);
        using var view = new SettingsViewModel(context, settings, null, null, voiceMetadataCache: cache, favoriteVoiceStore: store);
        view.SearchResults = [new() { Id = Id(99), Title = "Selected" }, new() { Id = Id(1), Title = "Other" }];
        view.ByIdResults = [new() { Id = Id(99), Title = "Same ID" }];
        var command = view.ToggleSelectedVoiceFavoriteCommand;
        AssertEqual(false, view.IsSelectedVoiceFavorite, "An unbookmarked selected voice starts with an empty star");
        var selectionBefore = JsonSerializer.Serialize(settings);
        command.Execute(null);
        AssertEqual(true, view.IsSelectedVoiceFavorite, "The selected-card star must bookmark the applied Voice ID");
        AssertEqual(true, view.SearchResults[0].IsFavorite && view.ByIdResults[0].IsFavorite,
            "Selected-card changes must synchronize matching cards in the other panels");
        AssertEqual(selectionBefore, JsonSerializer.Serialize(settings), "Bookmarking selected voice must not mutate selection or cached metadata");
        AssertEnumerableEqual(new[] { Id(99) }, store.Read(), "The selected-card action persists only its Voice ID");
        AssertEqual(false, File.ReadAllText(FilePath(directory)).Contains("Selected", StringComparison.Ordinal), "No selected metadata is persisted as a bookmark");
        view.ToggleFavoriteCommand.Execute(view.ByIdResults[0]);
        AssertEqual(false, view.IsSelectedVoiceFavorite, "Removing a matching browse favorite updates the selected-card star immediately");
        view.ToggleFavoriteCommand.Execute(view.SearchResults[1]);
        view.SelectVoiceCommand.Execute(view.SearchResults[1]);
        AssertEqual(true, view.IsSelectedVoiceFavorite, "Applying an already-bookmarked voice updates the selected-card star");
        view.SelectVoiceCommand.Execute(view.SearchResults[0]);
        AssertEqual(false, view.IsSelectedVoiceFavorite, "Applying an unbookmarked voice must reset the former selected favorite state");
        view.SelectVoiceCommand.Execute(view.SearchResults[1]);
        view.ClearVoiceCommand.Execute(null);
        AssertEqual(false, view.IsSelectedVoiceFavorite, "Clearing the selected voice clears its displayed favorite state");
        AssertEnumerableEqual(new[] { Id(1) }, store.Read(), "Clearing selection must retain independent favorite IDs");
        view.SelectVoiceCommand.Execute(view.SearchResults[1]);
        var saved = File.ReadAllBytes(FilePath(directory));
        rejectWrite = true;
        command.Execute(null);
        AssertEqual(true, view.IsSelectedVoiceFavorite, "A failed selected-card removal preserves the existing star");
        AssertEqual(true, view.SearchResults[1].IsFavorite, "A failed selected-card write preserves all matching stars");
        AssertEnumerableEqual(saved, File.ReadAllBytes(FilePath(directory)), "A failed selected-card write must preserve file bytes");
        rejectWrite = false;
        command.Execute(null);
        AssertEqual(false, view.IsSelectedVoiceFavorite, "A successful selected-card removal updates every shared ID state");
        AssertEqual(0, store.Read().Count, "Selected-card removal must persist the removal");
    }

}
