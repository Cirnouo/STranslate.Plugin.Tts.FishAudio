using System.Reflection;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using STranslate.Plugin.Tts.FishAudio.Presentation;
using static ContextProxy;
using static TestAssertions;

internal static class SearchAdjustmentSuite
{
    private const string Response = """{"total":24,"items":[{"_id":"11111111111111111111111111111111","title":"Voice"}]}""";

    internal static async Task RunRequestsAsync()
    {
        await RequestsCaptureScalarLanguageAndExplicitSortAsync();
        await AppliedCriteriaSurviveFailureAndDraftEditsAsync();
        await RetiredCriteriaCannotPublishAsync();
        await QueuedPublicationRechecksItsOwnerAsync();
        await MalformedApplyRetainsSuccessfulSnapshotAsync();
        await CoverFailureCannotSplitSuccessfulSnapshotAsync();
    }

    // Dropping a filter, translating a wire value, double-escaping text, or reusing
    // a credential draft must fail at the actual fake HTTP boundary.
    private static async Task RequestsCaptureScalarLanguageAndExplicitSortAsync()
    {
        var settings = new Settings { ApiKey = "accepted-key" };
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = Response;
        var context = CreateContext(settings: settings, httpService: service);
        using var vm = new SettingsViewModel(context, settings) { SearchQuery = "visible & 日本語/+" };
        foreach (var language in new string?[] { null, "en", "zh", "de", "ja", "fr", "es", "ko", "ar", "ru", "pt" })
        foreach (var sort in new[] { "score", "task_count", "created_at" })
        {
            var before = http.GetCallCount;
            await Apply(vm, language, sort);
            var expected = "https://api.fish.audio/model?page_size=6&page_number=1&sort_by=" + sort
                + "&title=visible%20%26%20%E6%97%A5%E6%9C%AC%E8%AA%9E%2F%2B" + (language is null ? "" : "&language=" + language);
            AssertEqual(expected, http.LastGetUrl, "Each Apply emits one explicit sort and at most one scalar language");
            AssertEqual(before + 1, http.GetCallCount, "Every Apply, including an unchanged Apply, issues exactly one page-one GET");
            AssertEqual("Bearer accepted-key", http.LastGetOptions!.Headers!["Authorization"], "Search captures accepted credentials separately");
            AssertEqual(TimeSpan.FromSeconds(15), http.LastGetOptions.Timeout, "Metadata timeout remains fifteen seconds");
        }
        await Apply(vm, "pt", "created_at");
        AssertEqual(34, http.GetCallCount, "Applying unchanged options explicitly refreshes exactly once");
        AssertEqual(0, ((ContextProxy)(object)context).SaveCount, "Session search options never persist settings");
    }

    // Paging must use all successful criteria; failed replacements cannot partially
    // publish options/page/results, and Search must use visible text with applied options.
    private static async Task AppliedCriteriaSurviveFailureAndDraftEditsAsync()
    {
        var settings = new Settings();
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = Response;
        using var vm = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings) { SearchQuery = "first" };
        await Apply(vm, "ja", "task_count");
        await vm.NextPageCommand.ExecuteAsync(null);
        var card = vm.SearchResults.Single();
        vm.SearchQuery = "edited";
        http.GetException = new TimeoutException();
        await Apply(vm, "de", "created_at");
        AssertEqual(2, vm.SearchPage, "Failed Apply retains the successful page");
        AssertEqual(true, ReferenceEquals(card, vm.SearchResults.Single()), "Failed Apply retains result identities");
        AssertEqual("ja", Applied(vm, "SearchLanguage"), "Failed Apply retains the successful language");
        AssertEqual("task_count", Applied(vm, "SearchSort"), "Failed Apply retains the successful sort");
        http.GetException = null;
        await vm.NextPageCommand.ExecuteAsync(null);
        AssertEqual("https://api.fish.audio/model?page_size=6&page_number=3&sort_by=task_count&title=first&language=ja", http.LastGetUrl, "Paging keeps the complete committed snapshot");
        await vm.SearchVoicesCommand.ExecuteAsync(null);
        AssertEqual("https://api.fish.audio/model?page_size=6&page_number=1&sort_by=task_count&title=edited&language=ja", http.LastGetUrl, "Search captures visible text and applied options");
        await Apply(vm, null, "score");
        AssertEqual("score", Applied(vm, "SearchSort"), "Successful Reset/Apply commits relevance");
        AssertEqual<string?>(null, Applied(vm, "SearchLanguage"), "All clears the scalar language");
    }

    // Cancellation-ignoring responses must not restore retired criteria after a
    // replacement, accepted credential change, or disposal.
    private static async Task RetiredCriteriaCannotPublishAsync()
    {
        foreach (var retirement in new[] { "replacement", "credentials", "dispose" })
        {
            var settings = new Settings { ApiKey = "accepted-key" };
            var (service, http) = TestHttpServiceProxy.Create();
            http.GetResponseJson = Response;
            using var vm = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings);
            await Apply(vm, "en", "task_count");
            var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken captured = default;
            http.GetAsyncHandler = (_, _, ct) => { captured = ct; return pending.Task; };
            var old = Apply(vm, "de", "created_at");
            if (retirement == "replacement")
            {
                http.GetAsyncHandler = null;
                await Apply(vm, "ko", "score");
            }
            else if (retirement == "credentials") vm.ApiKey = "replacement-key";
            else vm.Dispose();
            AssertEqual(true, captured.IsCancellationRequested, "Retiring search cancels its captured request token");
            pending.SetResult("""{"total":900,"items":[{"_id":"22222222222222222222222222222222","title":"Retired"}]}""");
            await old.WaitAsync(TimeSpan.FromSeconds(2));
            AssertEqual(retirement == "replacement" ? "ko" : "en", Applied(vm, "SearchLanguage"), "Retired completion cannot replace applied criteria");
            AssertEqual("Voice", vm.SearchResults.Single().Title, "Retired completion cannot replace successful results");
            AssertEqual(1, vm.SearchPage, "Retired completion cannot replace navigation");
            if (retirement == "credentials")
            {
                http.GetAsyncHandler = null;
                await vm.SearchVoicesCommand.ExecuteAsync(null);
                AssertEqual("Bearer replacement-key", http.LastGetOptions!.Headers!["Authorization"], "The next explicit search captures the newly accepted credential");
                AssertEqual(true, http.LastGetUrl!.EndsWith("&language=en", StringComparison.Ordinal), "Credential retirement retains successful options");
            }
        }
    }

    internal static Task Apply(SettingsViewModel vm, string? language, string sort)
    {
        var method = typeof(SettingsViewModel).GetMethod("ApplySearchOptionsAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        AssertNotNull(method, "Search options must expose one internal immediate-Apply boundary");
        return (Task)method!.Invoke(vm, [language, sort])!;
    }

    // Checking the request generation before queueing WPF work is insufficient:
    // replacement/credential/disposal may retire it before that unit runs.
    private static async Task QueuedPublicationRechecksItsOwnerAsync()
    {
        foreach (var retirement in new[] { "replacement", "credentials", "dispose" })
        {
            var settings = new Settings { ApiKey = "accepted-key" };
            var (service, http) = TestHttpServiceProxy.Create();
            http.GetResponseJson = Response;
            using var vm = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings);
            await Apply(vm, "en", "task_count");
            var queued = new List<Action>();
            var previous = SettingsViewModel.UiThreadInvokerOverride;
            try
            {
                SettingsViewModel.UiThreadInvokerOverride = queued.Add;
                await Apply(vm, "de", "created_at");
            }
            finally { SettingsViewModel.UiThreadInvokerOverride = previous; }
            AssertEqual(true, queued.Count > 0, "Search response publication must cross the owning UI boundary");
            AssertEqual("en", Applied(vm, "SearchLanguage"), "Queued response does not publish options before its UI unit runs");
            if (retirement == "replacement") await Apply(vm, "ko", "score");
            else if (retirement == "credentials") vm.ApiKey = "replacement-key";
            else vm.Dispose();
            var cards = vm.SearchResults;
            foreach (var action in queued) action();
            AssertEqual(retirement == "replacement" ? "ko" : "en", Applied(vm, "SearchLanguage"), "The queued unit rechecks current ownership before publishing criteria");
            AssertEqual(true, ReferenceEquals(cards, vm.SearchResults), "Retired queued publication cannot replace results");
            AssertEqual(false, vm.IsSearching, "A queued retired operation cannot reclaim busy state");
        }
    }
    private static string? Applied(SettingsViewModel vm, string property)
    {
        var member = typeof(SettingsViewModel).GetProperty(property);
        AssertNotNull(member, "Applied option presentation is available without exposing internal criteria types");
        return (string?)member!.GetValue(vm);
    }

    private static async Task MalformedApplyRetainsSuccessfulSnapshotAsync()
    {
        var settings = new Settings();
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = Response;
        using var vm = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings);
        await Apply(vm, "ja", "task_count");
        await vm.NextPageCommand.ExecuteAsync(null);
        var cards = vm.SearchResults;
        foreach (var invalid in new[] { "null", """{"total":"invalid","items":[]}""", """{"total":1,"items":[null]}""" })
        {
            http.GetResponseJson = invalid;
            var before = http.GetCallCount;
            await Apply(vm, "de", "created_at");
            AssertEqual(before + 1, http.GetCallCount, "Malformed search responses are not retried");
            AssertEqual(2, vm.SearchPage, "Invalid provider data cannot publish a new page before mapping completes");
            AssertEqual(true, ReferenceEquals(cards, vm.SearchResults), "Invalid provider data retains the successful result list");
            AssertEqual("ja", vm.SearchLanguage, "Invalid provider data retains the successful language");
            AssertEqual("task_count", vm.SearchSort, "Invalid provider data retains the successful sort");
        }
    }

    private static async Task CoverFailureCannotSplitSuccessfulSnapshotAsync()
    {
        var settings = new Settings();
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = Response;
        var page = 0;
        string? language = null;
        var results = new List<VoiceSearchItem>();
        var synchronized = false;
        using var coordinator = new VoiceDiscoveryCoordinator(
            CreateContext(settings: settings, httpService: service), () => "visible", () => "1", () => "",
            _ => { }, pagination => page = pagination.Page, _ => { }, _ => { }, value => results = value,
            (_, _, _, _) => throw new InvalidOperationException("private resource detail"),
            () => synchronized = true, _ => { }, (_, _) => true, _ => { }, _ => { }, _ => { }, () => false,
            setAppliedSearchOptions: criteria => language = criteria.Language);
        await coordinator.ApplySearchOptionsAsync("ko", "created_at");
        AssertEqual(1, page, "Valid metadata commits page one before optional cover enrichment");
        AssertEqual("ko", language, "An enrichment failure cannot partially fail applied options");
        AssertEqual("Voice", results.Single().Title, "An enrichment failure retains the complete valid metadata snapshot");
        AssertEqual(true, synchronized, "Preview synchronization belongs to successful metadata publication");
    }
}
