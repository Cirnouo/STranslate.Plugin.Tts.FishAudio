using System.Text.Json;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static FavoriteVoicesSuite;
using static TestAssertions;

internal static class VoiceSelectionToggleSuite
{
    internal static void MatchingCardsToggleSelectionWithoutChangingFavoriteIds()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(FilePath(directory), JsonSerializer.Serialize(new[] { Id(1) }));
        var settings = SelectedSettings();
        using var view = new SettingsViewModel(CreateContext(settings: settings, pluginSettingsDirectoryPath: directory), settings);
        view.SearchResults = [new() { Id = Id(1), Title = "Search" }, new() { Id = Id(99), Title = "Previous" }];
        view.ByIdResults = [new() { Id = Id(1), Title = "By ID" }];
        var favorite = view.FavoriteResults.Single();
        AssertEqual(false, CanToggle(favorite), "An unselected ID-only placeholder cannot Apply");
        view.SelectVoiceCommand.Execute(view.SearchResults[0]);
        AssertEqual(Id(1), settings.VoiceId, "An unapplied Tick applies its voice");
        AssertEqual(true, Applied(view.SearchResults[0]) && Applied(view.ByIdResults[0]) && Applied(favorite),
            "Matching Search, By-ID and Favorites cards must all show the applied state");
        AssertEqual(true, CanToggle(favorite), "An applied placeholder must allow the Cross despite missing metadata");
        AssertEqual(Id(1), view.CachedVoiceId, "The selected card must show the applied voice");
        view.SelectVoiceCommand.Execute(favorite);
        AssertEqual("", settings.VoiceId, "A matching placeholder Cross clears the selected Voice ID");
        AssertEqual<CachedVoiceInfo?>(null, settings.CachedVoice, "Cancel Apply clears selected cached metadata");
        AssertEqual(false, Applied(view.SearchResults[0]) || Applied(view.ByIdResults[0]) || Applied(favorite),
            "Cancel Apply must reset every matching browsing card");
        AssertEqual(false, CanToggle(favorite), "A cleared placeholder becomes an unavailable Tick again");
        using var store = new FavoriteVoiceStore(directory);
        AssertEnumerableEqual(new[] { Id(1) }, store.Read(), "Apply and Cancel Apply must leave persistent bookmarks unchanged");
        view.SelectVoiceCommand.Execute(view.ByIdResults[0]);
        view.ClearVoiceCommand.Execute(null);
        AssertEqual(false, Applied(view.SearchResults[0]) || Applied(view.ByIdResults[0]),
            "The selected card's existing Cross must publish the same clear state");
    }

    internal static void RemovedOriginsCannotApplyOrClearMatchingCurrentCards()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(FilePath(directory), JsonSerializer.Serialize(new[] { Id(1) }));
        var settings = SelectedSettings();
        using var view = new SettingsViewModel(CreateContext(settings: settings, pluginSettingsDirectoryPath: directory), settings);
        var stale = new VoiceSearchItem { Id = Id(1), Title = "Replaced result" };
        view.SearchResults = [stale];
        var current = new VoiceSearchItem { Id = Id(1), Title = "Current result" };
        view.SearchResults = [current];
        view.SelectVoiceCommand.Execute(stale);
        AssertEqual(Id(99), settings.VoiceId, "A replaced card cannot Apply merely because its Voice ID still exists");
        view.SelectVoiceCommand.Execute(current);
        view.SelectVoiceCommand.Execute(stale);
        AssertEqual(Id(1), settings.VoiceId, "A stale matching card cannot Cancel Apply on behalf of its replacement");
        var removedFavorite = view.FavoriteResults.Single();
        view.ToggleFavoriteCommand.Execute(removedFavorite);
        AssertEqual(Id(1), settings.VoiceId, "Removing a favorite must not Cancel Apply");
        view.SelectVoiceCommand.Execute(removedFavorite);
        AssertEqual(Id(1), settings.VoiceId, "A removed favorite card cannot clear a still-current matching Search selection");
        var replacement = new VoiceSearchItem { Id = Id(1), Title = "New page" };
        view.SearchResults = [replacement];
        AssertEqual(true, Applied(replacement), "A replacement card must inherit applied state from the selected Voice ID");
        view.SelectVoiceCommand.Execute(replacement);
        AssertEqual("", settings.VoiceId, "The actual current card still owns its Cancel Apply action");
    }

    internal static void FailedSelectionWritesKeepAppliedIdentityAndStars()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(FilePath(directory), JsonSerializer.Serialize(new[] { Id(99) }));
        var storage = SelectedSettings();
        var context = CreateContext(settings: storage, pluginSettingsDirectoryPath: directory);
        var runtime = SettingsStore.Load(context, DateTimeOffset.UtcNow, out var lease);
        using var view = new SettingsViewModel(context, runtime, null, null, settingsWriteLease: lease);
        var current = view.FavoriteResults.Single();
        view.SearchResults = [new() { Id = Id(1), Title = "Other voice" }];
        var before = JsonSerializer.Serialize(runtime);
        ((ContextProxy)(object)context).OnSave = () => throw new IOException("Synthetic selection save failure");
        foreach (var command in new Action[] { () => view.SelectVoiceCommand.Execute(view.SearchResults[0]), () => view.ClearVoiceCommand.Execute(null) })
        {
            try { command(); throw new InvalidOperationException("Expected selection persistence to fail"); }
            catch (IOException) { }
            AssertEqual(before, JsonSerializer.Serialize(runtime), "A failed selection transaction preserves its runtime settings");
            AssertEqual(Id(99), view.VoiceId, "A failed selection transaction must preserve the applied UI Voice ID");
            AssertEqual(true, Applied(current) && view.IsSelectedVoiceFavorite,
                "A failed Apply/Clear must preserve the current Cross and selected-card favorite star");
        }
        ((ContextProxy)(object)context).OnSave = null;
        view.SelectVoiceCommand.Execute(current);
        AssertEqual("", runtime.VoiceId, "A successful applied-placeholder Cross may clear after a prior write failure");
        using var store = new FavoriteVoiceStore(directory);
        AssertEnumerableEqual(new[] { Id(99) }, store.Read(), "Selection failures and recovery cannot mutate favorite IDs");
    }

    internal static async Task MatchingByIdQueriesInspectWithoutCancelingSelectionAsync()
    {
        var settings = SelectedSettings();
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = Detail(99, "Matching selected voice");
        using var view = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings) { VoiceIdInput = Id(99) };
        await view.SubmitVoiceIdCommand.ExecuteAsync(null);
        var oldCard = view.ByIdResults.Single();
        AssertEqual(Id(99), settings.VoiceId, "Looking up an already-applied ID cannot implicitly Cancel Apply");
        AssertEqual(true, oldCard.IsApplied, "A matching By-ID result must display the current Cross state");
        await view.SubmitVoiceIdCommand.ExecuteAsync(null);
        view.SelectVoiceCommand.Execute(oldCard);
        AssertEqual(Id(99), settings.VoiceId, "An older lookup card cannot clear selection after its replacement is published");
        view.SelectVoiceCommand.Execute(view.ByIdResults.Single());
        AssertEqual("", settings.VoiceId, "Only the current inspected card's explicit Cross cancels selection");
    }

    private static bool Applied(VoiceSearchItem item) => item.IsApplied;
    private static bool CanToggle(VoiceSearchItem item) => item.CanToggleSelection;
}
