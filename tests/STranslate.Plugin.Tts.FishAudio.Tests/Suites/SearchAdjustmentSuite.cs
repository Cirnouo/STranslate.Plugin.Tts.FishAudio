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
        await MultipleLanguagesAreOneDetachedRequestAsync();
        await RequestsCaptureLanguageAndExplicitSortAsync();
        await AppliedCriteriaSurviveFailureAndDraftEditsAsync();
        await RetiredCriteriaCannotPublishAsync();
        await QueuedPublicationRechecksItsOwnerAsync();
        await MalformedApplyRetainsSuccessfulSnapshotAsync();
        await CoverFailureCannotSplitSuccessfulSnapshotAsync();
    }

    // A scalar boundary, duplicate-key overwrite, input aliasing or all-ten => All
    // would lose requested filters at the real HTTP/publication boundary.
    private static async Task MultipleLanguagesAreOneDetachedRequestAsync()
    {
        var settings = new Settings();
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = Response;
        using var vm = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings) { SearchQuery = "a&language=ru + 日本語" };
        var draft = new List<string> { "zh", "en", "zh" };
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        http.GetAsyncHandler = (_, _, _) => pending.Task;
        var apply = Apply(vm, draft, "created_at");
        draft.Clear(); draft.Add("ko");
        pending.SetResult(Response);
        await apply;
        AssertEqual("https://api.fish.audio/model?page_size=6&page_number=1&sort_by=created_at&title=a%26language%3Dru%20%2B%20%E6%97%A5%E6%9C%AC%E8%AA%9E&language=en&language=zh", http.LastGetUrl, "One encoded title and repeated keys retain canonical deduplicated languages");
        AssertEqual(1, http.GetCallCount, "Multiple languages use exactly one GET");
        var values = new Uri(http.LastGetUrl!).Query.TrimStart('?').Split('&')
            .Select(pair => pair.Split('=', 2)).Where(pair => pair[0] == "language").Select(pair => Uri.UnescapeDataString(pair[1]));
        AssertEnumerableEqual(new[] { "en", "zh" }, values, "Decoded repeated values retain both specifics in canonical wire order");
        var applied = vm.SearchLanguages;
        AssertEnumerableEqual(new[] { "en", "zh" }, applied, "Pending Apply owns an immutable detached snapshot");
        AssertEqual(true, applied is not string[] && applied is not List<string>, "Applied languages cannot expose mutable backing storage");
        if (applied is IList<string> list)
        {
            try { list[0] = "ko"; throw new InvalidOperationException("Language snapshot allowed mutation"); }
            catch (NotSupportedException) { }
        }
        http.GetAsyncHandler = null;
        await vm.NextPageCommand.ExecuteAsync(null);
        AssertEqual(true, http.LastGetUrl!.Contains("page_number=2", StringComparison.Ordinal) && http.LastGetUrl.EndsWith("&language=en&language=zh", StringComparison.Ordinal), "Paging owns the complete captured set");
        vm.SearchQuery = "new";
        await vm.SearchVoicesCommand.ExecuteAsync(null);
        AssertEqual(true, http.LastGetUrl!.Contains("title=new", StringComparison.Ordinal) && http.LastGetUrl.EndsWith("&language=en&language=zh", StringComparison.Ordinal), "WithQuery preserves the immutable language set");
        var allTen = new[] { "pt", "ru", "ar", "ko", "es", "fr", "ja", "de", "zh", "en" };
        await Apply(vm, allTen, "score");
        AssertEqual("https://api.fish.audio/model?page_size=6&page_number=1&sort_by=score&title=new&language=en&language=zh&language=de&language=ja&language=fr&language=es&language=ko&language=ar&language=ru&language=pt", http.LastGetUrl, "All ten is a filtered preset set, not All languages");
        foreach (var invalid in new[] { "", "EN", " en", "uk", null! })
        {
            var before = http.GetCallCount;
            try { await Apply(vm, new[] { "en", invalid }, "score"); throw new InvalidOperationException("Unsupported language was accepted"); }
            catch (ArgumentOutOfRangeException) { }
            AssertEqual(before, http.GetCallCount, "An unsupported language fails before requesting or publishing");
        }
        await Apply(vm, Array.Empty<string>(), "score");
        AssertEqual(false, http.LastGetUrl!.Contains("&language=", StringComparison.Ordinal), "An empty set omits language entirely");
    }

    // Dropping a filter, translating a wire value, double-escaping text, or reusing
    // a credential draft must fail at the actual fake HTTP boundary.
    private static async Task RequestsCaptureLanguageAndExplicitSortAsync()
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
            await Apply(vm, language is null ? null : [language], sort);
            var expected = "https://api.fish.audio/model?page_size=6&page_number=1&sort_by=" + sort
                + "&title=visible%20%26%20%E6%97%A5%E6%9C%AC%E8%AA%9E%2F%2B" + (language is null ? "" : "&language=" + language);
            AssertEqual(expected, http.LastGetUrl, "Each Apply emits one explicit sort and at most one language");
            AssertEqual(before + 1, http.GetCallCount, "Every Apply, including an unchanged Apply, issues exactly one page-one GET");
            AssertEqual("Bearer accepted-key", http.LastGetOptions!.Headers!["Authorization"], "Search captures accepted credentials separately");
            AssertEqual(TimeSpan.FromSeconds(15), http.LastGetOptions.Timeout, "Metadata timeout remains fifteen seconds");
        }
        await Apply(vm, ["pt"], "created_at");
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
        await Apply(vm, ["ja", "zh"], "task_count");
        await vm.NextPageCommand.ExecuteAsync(null);
        var card = vm.SearchResults.Single();
        vm.SearchQuery = "edited";
        http.GetException = new TimeoutException();
        await Apply(vm, ["de"], "created_at");
        AssertEqual(2, vm.SearchPage, "Failed Apply retains the successful page");
        AssertEqual(true, ReferenceEquals(card, vm.SearchResults.Single()), "Failed Apply retains result identities");
        AssertEqual("zh,ja", string.Join(",", vm.SearchLanguages), "Failed Apply retains the successful language");
        AssertEqual("task_count", vm.SearchSort, "Failed Apply retains the successful sort");
        http.GetException = null;
        await vm.NextPageCommand.ExecuteAsync(null);
        AssertEqual("https://api.fish.audio/model?page_size=6&page_number=3&sort_by=task_count&title=first&language=zh&language=ja", http.LastGetUrl, "Paging keeps the complete committed snapshot");
        await vm.SearchVoicesCommand.ExecuteAsync(null);
        AssertEqual("https://api.fish.audio/model?page_size=6&page_number=1&sort_by=task_count&title=edited&language=zh&language=ja", http.LastGetUrl, "Search captures visible text and applied options");
        await Apply(vm, null, "score");
        AssertEqual("score", vm.SearchSort, "Successful Reset/Apply commits relevance");
        AssertEqual(0, vm.SearchLanguages.Count, "All clears specific languages");
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
            await Apply(vm, ["zh", "en"], "task_count");
            var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken captured = default;
            http.GetAsyncHandler = (_, _, ct) => { captured = ct; return pending.Task; };
            var old = Apply(vm, ["de"], "created_at");
            if (retirement == "replacement")
            {
                http.GetAsyncHandler = null;
                await Apply(vm, ["pt", "ko"], "score");
            }
            else if (retirement == "credentials") vm.ApiKey = "replacement-key";
            else vm.Dispose();
            AssertEqual(true, captured.IsCancellationRequested, "Retiring search cancels its captured request token");
            pending.SetResult("""{"total":900,"items":[{"_id":"22222222222222222222222222222222","title":"Retired"}]}""");
            await old.WaitAsync(TimeSpan.FromSeconds(2));
            AssertEqual(retirement == "replacement" ? "ko,pt" : "en,zh", string.Join(",", vm.SearchLanguages), "Retired completion cannot replace applied criteria");
            AssertEqual("Voice", vm.SearchResults.Single().Title, "Retired completion cannot replace successful results");
            AssertEqual(1, vm.SearchPage, "Retired completion cannot replace navigation");
            if (retirement == "credentials")
            {
                http.GetAsyncHandler = null;
                await vm.SearchVoicesCommand.ExecuteAsync(null);
                AssertEqual("Bearer replacement-key", http.LastGetOptions!.Headers!["Authorization"], "The next explicit search captures the newly accepted credential");
                AssertEqual(true, http.LastGetUrl!.EndsWith("&language=en&language=zh", StringComparison.Ordinal), "Credential retirement retains successful options");
            }
        }
    }

    internal static Task Apply(SettingsViewModel vm, IEnumerable<string>? languages, string sort) =>
        vm.ApplySearchOptionsAsync(languages, sort);

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
            await Apply(vm, ["zh", "en"], "task_count");
            var queued = new List<Action>();
            var previous = SettingsViewModel.UiThreadInvokerOverride;
            try
            {
                SettingsViewModel.UiThreadInvokerOverride = queued.Add;
                await Apply(vm, ["de"], "created_at");
            }
            finally { SettingsViewModel.UiThreadInvokerOverride = previous; }
            AssertEqual(true, queued.Count > 0, "Search response publication must cross the owning UI boundary");
            AssertEqual("en,zh", string.Join(",", vm.SearchLanguages), "Queued response does not publish options before its UI unit runs");
            if (retirement == "replacement") await Apply(vm, ["pt", "ko"], "score");
            else if (retirement == "credentials") vm.ApiKey = "replacement-key";
            else vm.Dispose();
            var cards = vm.SearchResults;
            foreach (var action in queued) action();
            AssertEqual(retirement == "replacement" ? "ko,pt" : "en,zh", string.Join(",", vm.SearchLanguages), "The queued unit rechecks current ownership before publishing criteria");
            AssertEqual(true, ReferenceEquals(cards, vm.SearchResults), "Retired queued publication cannot replace results");
            AssertEqual(false, vm.IsSearching, "A queued retired operation cannot reclaim busy state");
        }
    }
    private static async Task MalformedApplyRetainsSuccessfulSnapshotAsync()
    {
        var settings = new Settings();
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = Response;
        using var vm = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings);
        await Apply(vm, ["ja"], "task_count");
        await vm.NextPageCommand.ExecuteAsync(null);
        var cards = vm.SearchResults;
        foreach (var invalid in new[] { "null", """{"total":"invalid","items":[]}""", """{"total":1,"items":[null]}""" })
        {
            http.GetResponseJson = invalid;
            var before = http.GetCallCount;
            await Apply(vm, ["de"], "created_at");
            AssertEqual(before + 1, http.GetCallCount, "Malformed search responses are not retried");
            AssertEqual(2, vm.SearchPage, "Invalid provider data cannot publish a new page before mapping completes");
            AssertEqual(true, ReferenceEquals(cards, vm.SearchResults), "Invalid provider data retains the successful result list");
            AssertEqual("ja", string.Join(",", vm.SearchLanguages), "Invalid provider data retains the successful language");
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
            setAppliedSearchOptions: criteria => language = string.Join(",", criteria.Languages));
        await coordinator.ApplySearchOptionsAsync(["ko"], "created_at");
        AssertEqual(1, page, "Valid metadata commits page one before optional cover enrichment");
        AssertEqual("ko", language, "An enrichment failure cannot partially fail applied options");
        AssertEqual("Voice", results.Single().Title, "An enrichment failure retains the complete valid metadata snapshot");
        AssertEqual(true, synchronized, "Preview synchronization belongs to successful metadata publication");
    }
}
