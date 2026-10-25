using System.Windows.Input;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static TestAssertions;

internal static class InputClearSuite
{
    private const string Id = "11111111111111111111111111111111";
    private const string Response = """{"total":24,"items":[{"_id":"11111111111111111111111111111111","title":"Retained"}]}""";

    internal static async Task RunRequestsAsync()
    {
        await ApiClearUsesAcceptedSettingsOwnerAsync();
        await RejectedLeaseClearRetainsCredentialOwnerAsync();
        await SearchClearOnlyEditsDraftAsync();
        await ByIdClearRetiresItsInputOwnerAsync();
    }

    internal static ICommand Command(SettingsViewModel vm, string name) =>
        (ICommand)(vm.GetType().GetProperty(name)?.GetValue(vm)
            ?? throw new InvalidOperationException($"Missing internal input action: {name}"));

    private static async Task RejectedLeaseClearRetainsCredentialOwnerAsync()
    {
        foreach (var retired in new[] { false, true })
        {
            var stored = new Settings { ApiKey = "accepted" };
            var (service, http) = TestHttpServiceProxy.Create();
            var context = CreateContext(settings: stored, httpService: service);
            var settings = SettingsStore.Load(context, DateTimeOffset.UtcNow, out var lease);
            using var vm = new SettingsViewModel(context, settings, null, null, settingsWriteLease: lease);
            var identity = settings.CredentialIdentity;
            var held = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken token = default;
            http.GetAsyncHandler = (_, _, ct) => { token = ct; return held.Task; };
            var pending = vm.SearchVoicesCommand.ExecuteAsync(null);
            if (retired) SettingsStore.Retire(settings, lease);
            else ((ContextProxy)(object)context).SaveSettings = _ => throw new IOException("Fake leased save failure");
            try
            {
                try { Command(vm, "ClearApiKeyCommand").Execute(null); }
                catch (IOException) when (!retired) { }
                AssertEqual("accepted", vm.ApiKey, "Rejected leased clear restores the visible credential");
                AssertEqual("accepted", settings.ApiKey, "Rejected leased clear retains accepted settings");
                AssertEqual(true, ReferenceEquals(identity, settings.CredentialIdentity), "Rejected leased clear cannot advance credential identity");
                AssertEqual(false, token.IsCancellationRequested, "Rejected leased clear cannot retire pending accepted work");
            }
            finally { held.TrySetResult(Response); await pending.WaitAsync(TimeSpan.FromSeconds(2)); }
        }
    }

    private static async Task ApiClearUsesAcceptedSettingsOwnerAsync()
    {
        foreach (var failure in new[] { false, true })
        {
            var settings = new Settings { ApiKey = "accepted", VoiceId = Id, CachedVoice = new() { Title = "Selected" } };
            var (service, http) = TestHttpServiceProxy.Create();
            http.GetResponseJson = Response;
            var context = CreateContext(settings: settings, httpService: service);
            var storage = (ContextProxy)(object)context;
            using var vm = new SettingsViewModel(context, settings) { SearchQuery = "committed" };
            await vm.SearchVoicesCommand.ExecuteAsync(null);
            var cards = vm.SearchResults;
            var held = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken token = default;
            http.GetAsyncHandler = (_, _, ct) => { token = ct; return held.Task; };
            var pending = vm.SearchVoicesCommand.ExecuteAsync(null);
            var clear = Command(vm, "ClearApiKeyCommand");
            vm.IsApiKeyInputLocked = true;
            AssertEqual(false, clear.CanExecute(null), "An API operation disables clear");
            clear.Execute(null);
            AssertEqual("accepted", vm.ApiKey, "Direct command execution also honors the API lock");
            AssertEqual(0, storage.SaveCount, "A locked clear cannot persist");
            vm.IsApiKeyInputLocked = false;
            storage.SaveSettings = _ => { if (failure) throw new IOException("Fake storage failure"); };
            try
            {
                AssertEqual(true, clear.CanExecute(null), "Unlock restores clear availability");
                try { clear.Execute(null); }
                catch (IOException) when (failure) { }
                AssertEqual(failure ? "accepted" : "", vm.ApiKey, "Clear uses the accepted setter and restores a failed save");
                AssertEqual(failure ? "accepted" : "", settings.ApiKey, "Persistence failure restores runtime credentials");
                AssertEqual(!failure, token.IsCancellationRequested, "Only accepted clear retires credential work");
                AssertEqual(true, ReferenceEquals(cards, vm.SearchResults), "Credential clear preserves displayed card identity");
                AssertEqual(Id, settings.VoiceId, "Credential clear preserves selected Voice ID");
                AssertEqual("Selected", settings.CachedVoice?.Title, "Credential clear preserves selected snapshot");
                if (!failure)
                {
                    var saves = storage.SaveCount;
                    clear.Execute(null);
                    AssertEqual(saves, storage.SaveCount, "Clearing an already empty key has no second save");
                    AssertEqual(1, saves, "An effective accepted clear saves exactly once");
                }
            }
            finally { held.TrySetResult(Response); await pending.WaitAsync(TimeSpan.FromSeconds(2)); }
        }
    }

    private static async Task SearchClearOnlyEditsDraftAsync()
    {
        var settings = new Settings();
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = Response;
        var context = CreateContext(settings: settings, httpService: service);
        using var vm = new SettingsViewModel(context, settings) { SearchQuery = "successful" };
        await vm.SearchVoicesCommand.ExecuteAsync(null);
        await vm.NextPageCommand.ExecuteAsync(null);
        var cards = vm.SearchResults;
        var held = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        http.GetAsyncHandler = (_, _, ct) => { token = ct; return held.Task; };
        var pending = vm.ApplySearchOptionsAsync(["en"], "created_at");
        vm.SearchQuery = "edited draft";
        var calls = http.GetCallCount;
        try
        {
            Command(vm, "ClearSearchQueryCommand").Execute(null);
            AssertEqual("", vm.SearchQuery, "Search clear changes the current draft immediately");
            AssertEqual(calls, http.GetCallCount, "Search clear never sends GET");
            AssertEqual(0, ((ContextProxy)(object)context).SaveCount, "Search clear never saves settings");
            AssertEqual(false, token.IsCancellationRequested, "Search clear never cancels the pending request");
            AssertEqual(true, vm.IsSearching, "Search clear preserves the active request");
            AssertEqual(true, ReferenceEquals(cards, vm.SearchResults), "Search clear preserves result objects");
            AssertEqual(2, vm.SearchPage, "Search clear preserves successful navigation");
            AssertEqual("en", string.Join(",", vm.SearchLanguages), "Search clear preserves newly applied menu intent");
            http.GetAsyncHandler = null;
            await vm.NextPageCommand.ExecuteAsync(null);
            AssertEqual("https://api.fish.audio/model?page_size=6&page_number=3&sort_by=score&title=successful", http.LastGetUrl, "Paging after clear still uses last successful full criteria");
            await vm.SearchVoicesCommand.ExecuteAsync(null);
            AssertEqual("https://api.fish.audio/model?page_size=6&page_number=1&sort_by=created_at&language=en", http.LastGetUrl, "Explicit Search uses empty visible query and current applied options");
        }
        finally { held.TrySetResult(Response); await pending.WaitAsync(TimeSpan.FromSeconds(2)); }
    }

    private static async Task ByIdClearRetiresItsInputOwnerAsync()
    {
        foreach (var lateFailure in new[] { false, true })
        {
            var clock = new ManualStreamingTimeProvider();
            var settings = new Settings { VoiceId = Id };
            var (service, http) = TestHttpServiceProxy.Create();
            var context = CreateContext(settings: settings, httpService: service);
            using var vm = new SettingsViewModel(context, settings, null, null, voiceIdErrorTimeProvider: clock) { VoiceIdInput = "bad" };
            var clear = Command(vm, "ClearVoiceIdInputCommand");
            await vm.SubmitVoiceIdCommand.ExecuteAsync(null);
            clock.Advance(TimeSpan.FromMilliseconds(4999));
            AssertNotNull(vm.VoiceIdError, "The pre-clear occurrence still owns its full error lifetime");
            clear.Execute(null);
            AssertEqual<string?>(null, vm.VoiceIdError, "Clear immediately retires validation errors");
            vm.VoiceIdInput = "new bad";
            await vm.SubmitVoiceIdCommand.ExecuteAsync(null);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            AssertNotNull(vm.VoiceIdError, "The old 5000ms expiry cannot clear a newer occurrence");
            clear.Execute(null);
            vm.VoiceIdInput = Id;
            http.GetResponseJson = """{"_id":"11111111111111111111111111111111","title":"Found"}""";
            await vm.SubmitVoiceIdCommand.ExecuteAsync(null);
            AssertEqual(1, vm.ByIdResults.Count, "The fixture starts with an independent result");
            clear.Execute(null);
            AssertEqual(0, vm.ByIdResults.Count, "Clear retires an already displayed independent result");
            vm.VoiceIdInput = "22222222222222222222222222222222";
            var held = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken token = default;
            http.GetAsyncHandler = (_, _, ct) => { token = ct; return held.Task; };
            var pending = vm.SubmitVoiceIdCommand.ExecuteAsync(null);
            var calls = http.GetCallCount;
            clear.Execute(null);
            AssertEqual("", vm.VoiceIdInput, "By-ID clear edits its draft");
            AssertEqual(true, token.IsCancellationRequested, "By-ID clear cancels pending lookup");
            AssertEqual(false, vm.IsSubmittingVoiceId, "By-ID clear retires submitting state");
            AssertEqual(0, vm.ByIdResults.Count, "By-ID clear retires the independent result");
            if (lateFailure) held.SetException(new TimeoutException()); else held.SetResult(http.GetResponseJson);
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
            clock.Advance(TimeSpan.FromSeconds(5));
            AssertEqual(0, vm.ByIdResults.Count, "Cancellation-ignoring success cannot resurrect a result");
            AssertEqual<string?>(null, vm.VoiceIdError, "Cancellation-ignoring failure and old expiry cannot restore an error");
            AssertEqual(calls, http.GetCallCount, "By-ID clear sends no replacement GET");
            AssertEqual(0, ((ContextProxy)(object)context).SaveCount, "By-ID clear saves nothing");
            AssertEqual(Id, settings.VoiceId, "By-ID clear preserves selection");
        }
    }
}
