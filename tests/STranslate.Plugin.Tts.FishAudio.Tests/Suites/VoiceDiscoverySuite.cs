using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.Presentation;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using static AsyncTestWait;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static StaTestHost;
using static TestAssertions;

internal static class VoiceDiscoverySuite
{
    internal static async Task SearchAndByIdUseDummyTokenAsync()
    {
        var settings = new Settings { ApiKey = AppliedKey };
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = "{\"total\":0,\"items\":[]}";
        var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings)
        {
            SearchQuery = "voice",
        };

        await viewModel.SearchVoicesCommand.ExecuteAsync(null);

        var headers = AssertHeaders(http.LastGetOptions, "Voice search should send Authorization headers");
        AssertEqual("Bearer dummy", headers["Authorization"], "Voice search should use the dummy token by default");

        http.GetResponseJson = "{\"_id\":\"fedcba9876543210fedcba9876543210\",\"title\":\"Voice\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}";
        viewModel.VoiceIdInput = "fedcba9876543210fedcba9876543210";

        await viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);

        headers = AssertHeaders(http.LastGetOptions, "Voice by-ID lookup should send Authorization headers");
        AssertEqual("Bearer dummy", headers["Authorization"], "Voice by-ID lookup should use the dummy token by default");
    }

    internal static async Task VoiceLookupRequestsUseTimeoutAndPreserveFailureSemanticsAsync()
    {
        var settings = new Settings();
        var (httpService, http) = TestHttpServiceProxy.Create();
        var snackbar = new TestSnackbar();
        var context = CreateContext(snackbar, settings, httpService);
        var viewModel = new SettingsViewModel(context, settings)
        {
            SearchQuery = "voice",
        };

        http.GetResponseJson = "{\"total\":0,\"items\":[]}";
        await viewModel.SearchVoicesCommand.ExecuteAsync(null);
        AssertEqual(TimeSpan.FromSeconds(15), http.LastGetOptions?.Timeout, "Voice search should set a 15 second timeout");

        http.GetResponseJson = "{\"_id\":\"fedcba9876543210fedcba9876543210\",\"title\":\"Voice\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}";
        viewModel.VoiceIdInput = "fedcba9876543210fedcba9876543210";
        await viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
        AssertEqual(TimeSpan.FromSeconds(15), http.LastGetOptions?.Timeout, "Voice by-ID lookup should set a 15 second timeout");

        http.GetException = new HttpRequestException("Response status code does not indicate success: 404 (Not Found).", null, HttpStatusCode.NotFound);
        viewModel.VoiceIdInput = "00000000000000000000000000000000";
        await viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
        AssertEqual(
            "STranslate_Plugin_Tts_FishAudio_Voice_NotFound",
            viewModel.VoiceIdError,
            "Only HTTP 404 should be shown as voice not found");

        http.GetException = new TimeoutException("lookup timed out");
        viewModel.VoiceIdInput = "11111111111111111111111111111111";
        await viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
        AssertEqual(
            "STranslate_Plugin_Tts_FishAudio_Request_Timeout",
            viewModel.VoiceIdError,
            "Voice by-ID timeout should show the localized timeout error instead of not found");

        http.GetException = new InvalidOperationException("server unavailable");
        viewModel.VoiceIdInput = "22222222222222222222222222222222";
        await viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
        AssertEqual("server unavailable", viewModel.VoiceIdError, "Non-404 lookup failures should preserve failure details");

        http.GetException = null;
        http.GetResponseJson = "null";
        viewModel.VoiceIdInput = "44444444444444444444444444444444";
        await viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
        AssertEqual(
            "Fish Audio model lookup returned an empty response.",
            viewModel.VoiceIdError,
            "Successful non-404 lookup responses that deserialize to null should be treated as response errors");
    }

    internal static async Task VoiceLookupRequestsCancelPreviousAndDisposeWorkAsync()
    {
        var settings = new Settings();
        var (httpService, http) = TestHttpServiceProxy.Create();
        var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        http.GetAsyncHandler = (_, _, ct) =>
        {
            calls++;
            if (calls == 1)
            {
                firstStarted.SetResult();
                return Task.Delay(TimeSpan.FromSeconds(30), ct).ContinueWith<string>(task =>
                {
                    if (task.IsCanceled)
                        throw new OperationCanceledException(ct);
                    return "{\"total\":0,\"items\":[]}";
                }, CancellationToken.None);
            }

            return releaseSecond.Task;
        };

        var firstSearch = viewModel.SearchVoicesCommand.ExecuteAsync(null);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondSearch = viewModel.SearchVoicesCommand.ExecuteAsync(null);
        releaseSecond.SetResult("{\"total\":0,\"items\":[]}");

        await secondSearch.WaitAsync(TimeSpan.FromSeconds(2));
        await firstSearch.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(2, calls, "A new voice search should start even when a previous search is still pending");
        AssertEqual(false, viewModel.IsSearching, "Search busy state should recover after replacing a pending search");

        var byIdStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        http.GetAsyncHandler = (_, _, ct) =>
        {
            byIdStarted.SetResult();
            return Task.Delay(TimeSpan.FromSeconds(30), ct).ContinueWith<string>(task =>
            {
                if (task.IsCanceled)
                    throw new OperationCanceledException(ct);
                return "{\"_id\":\"33333333333333333333333333333333\",\"title\":\"Voice\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}";
            }, CancellationToken.None);
        };

        viewModel.VoiceIdInput = "33333333333333333333333333333333";
        var submitTask = viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
        await byIdStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.Dispose();
        await submitTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(false, viewModel.IsSubmittingVoiceId, "Disposing the view model should cancel by-ID lookup and restore busy state");
    }

    internal static async Task VoiceLookupCompletionsAfterDisposeDoNotMutateStateAsync()
    {
        var settings = new Settings();
        var (httpService, http) = TestHttpServiceProxy.Create();
        var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings)
        {
            SearchQuery = "late",
        };
        var searchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSearch = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        http.GetAsyncHandler = (_, _, _) =>
        {
            searchStarted.SetResult();
            return releaseSearch.Task;
        };

        var searchTask = viewModel.SearchVoicesCommand.ExecuteAsync(null);
        await searchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.Dispose();
        releaseSearch.SetResult("{\"total\":1,\"items\":[{\"_id\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"title\":\"Late Search\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":1}]}");
        await searchTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(false, viewModel.IsSearching, "Disposing during search should restore search busy state");
        AssertEqual(false, viewModel.HasSearched, "Search completion after dispose should not mark search as completed");
        AssertEqual(0, viewModel.SearchResults.Count, "Search completion after dispose should not apply stale results");

        const string originalVoiceId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        settings = new Settings
        {
            VoiceId = originalVoiceId,
            CachedVoice = new CachedVoiceInfo { Title = "Existing Voice" },
        };
        (httpService, http) = TestHttpServiceProxy.Create();
        var context = CreateContext(settings: settings, httpService: httpService);
        var proxy = (ContextProxy)(object)context;
        viewModel = new SettingsViewModel(context, settings)
        {
            VoiceIdInput = "cccccccccccccccccccccccccccccccc",
        };
        var byIdStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseById = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        http.GetAsyncHandler = (_, _, _) =>
        {
            byIdStarted.SetResult();
            return releaseById.Task;
        };

        var submitTask = viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
        await byIdStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.Dispose();
        releaseById.SetResult("{\"_id\":\"cccccccccccccccccccccccccccccccc\",\"title\":\"Late Voice\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}");
        await submitTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(false, viewModel.IsSubmittingVoiceId, "Disposing during by-ID lookup should restore submit busy state");
        AssertEqual(originalVoiceId, settings.VoiceId, "By-ID completion after dispose should not update saved voice ID");
        AssertEqual("Existing Voice", settings.CachedVoice?.Title, "By-ID completion after dispose should not update cached voice");
        AssertEqual(0, proxy.SaveCount, "Dispose and late by-ID completion should not issue redundant or stale saves");
    }

    internal static async Task SearchPaginationUpdatesVisiblePageAfterSuccessOnlyAsync()
    {
        var settings = new Settings();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = "{\"total\":12,\"items\":[{\"_id\":\"11111111111111111111111111111111\",\"title\":\"Page 1\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":1}]}";
        var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings);

        await viewModel.SearchVoicesCommand.ExecuteAsync(null);

        AssertEqual(1, viewModel.SearchPage, "Initial search should load page 1");
        AssertEqual("1", viewModel.PageInput, "Initial search should show page input 1");
        AssertEqual("Page 1", viewModel.SearchResults[0].Title, "Initial search should display page 1 results");

        http.GetException = new TimeoutException("page 2 timed out");
        await viewModel.NextPageCommand.ExecuteAsync(null);

        AssertEqual(1, viewModel.SearchPage, "Failed next page load should keep previous visible page");
        AssertEqual("1", viewModel.PageInput, "Failed next page load should keep previous page input");
        AssertEqual(2, viewModel.SearchTotalPages, "Failed next page load should keep previous total pages");
        AssertEqual("Page 1", viewModel.SearchResults[0].Title, "Failed next page load should keep previous result cards");

        http.GetException = null;
        http.GetResponseJson = "{\"total\":12,\"items\":[{\"_id\":\"22222222222222222222222222222222\",\"title\":\"Page 2\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":2}]}";
        await viewModel.NextPageCommand.ExecuteAsync(null);

        AssertEqual(2, viewModel.SearchPage, "Successful next page load should update visible page after results arrive");
        AssertEqual("2", viewModel.PageInput, "Successful next page load should update page input after results arrive");
        AssertEqual("Page 2", viewModel.SearchResults[0].Title, "Successful next page load should display new result cards");
    }

    internal static async Task SearchPublishesPaginationBeforeResolvingCoversAsync()
    {
        var settings = new Settings();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = "{\"total\":12,\"items\":[{\"_id\":\"11111111111111111111111111111111\",\"title\":\"Voice\",\"description\":\"\",\"cover_image\":\"cover\",\"samples\":[],\"task_count\":1}]}";
        var events = new List<string>();
        var searchPage = 0;
        var searchTotalPages = 1;
        var pageInput = "0";
        using var coordinator = new VoiceDiscoveryCoordinator(
            context: CreateContext(settings: settings, httpService: httpService),
            getSearchQuery: () => "voice",
            getSearchPage: () => searchPage,
            getSearchTotalPages: () => searchTotalPages,
            getPageInput: () => pageInput,
            getVoiceIdInput: () => "",
            setSearching: _ => { },
            setSearchPage: value =>
            {
                searchPage = value;
                events.Add("page");
            },
            setPageInput: value =>
            {
                pageInput = value;
                events.Add("input");
            },
            setHasSearched: _ => events.Add("searched"),
            setSearchResultCount: _ => events.Add("count"),
            setSearchTotalPages: value =>
            {
                searchTotalPages = value;
                events.Add("total");
            },
            setSearchResults: _ => events.Add("results"),
            resolveCoverImageUrl: (_, _, _, _) =>
            {
                events.Add("cover");
                return "cover";
            },
            syncPreviewStateToResults: () => events.Add("sync"),
            setVoiceIdWithoutSaving: _ => { },
            persistSelectedVoice: _ => true,
            applyCachedVoice: _ => { },
            setVoiceIdError: _ => { },
            setSubmittingVoiceId: _ => { },
            isFacadeDisposed: () => false);

        await coordinator.SearchVoicesAsync();

        AssertEnumerableEqual(
            new[] { "page", "input", "searched", "count", "total", "cover", "results", "sync" },
            events,
            "Search should publish pagination state before resolving result covers");
    }

    internal static async Task DisposeRestoresDiscoveryBusyStateBeforeCancelCallbacksAsync()
    {
        var settings = new Settings();
        var (httpService, http) = TestHttpServiceProxy.Create();
        var searchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var byIdStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSearch = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseById = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        SettingsViewModel? viewModel = null;
        var cancellationCallbackInvoked = false;
        var observedIsSearching = true;
        var observedIsSubmittingVoiceId = true;
        http.GetAsyncHandler = (url, _, ct) =>
        {
            if (url.Contains("/model?", StringComparison.Ordinal))
            {
                searchStarted.SetResult();
                ct.Register(() =>
                {
                    cancellationCallbackInvoked = true;
                    observedIsSearching = viewModel!.IsSearching;
                    observedIsSubmittingVoiceId = viewModel.IsSubmittingVoiceId;
                    throw new InvalidOperationException("Cancellation callback failure.");
                });
                return releaseSearch.Task;
            }

            byIdStarted.SetResult();
            return releaseById.Task;
        };
        viewModel = new SettingsViewModel(
            CreateContext(settings: settings, httpService: httpService),
            settings)
        {
            SearchQuery = "voice",
            VoiceIdInput = "11111111111111111111111111111111",
        };

        var searchTask = viewModel.SearchVoicesCommand.ExecuteAsync(null);
        var byIdTask = viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
        await searchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await byIdStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(true, viewModel.IsSearching, "Search should be busy before disposal");
        AssertEqual(true, viewModel.IsSubmittingVoiceId, "By-ID lookup should be busy before disposal");

        Exception? disposeException = null;
        try
        {
            viewModel.Dispose();
        }
        catch (Exception ex)
        {
            disposeException = ex;
        }

        AssertNotNull(disposeException, "The throwing cancellation callback should propagate from disposal");
        AssertEqual(true, cancellationCallbackInvoked, "Disposal should cancel the active search request");
        AssertEqual(false, observedIsSearching, "Cancellation callbacks should observe restored search busy state");
        AssertEqual(false, observedIsSubmittingVoiceId, "Cancellation callbacks should observe restored by-ID busy state");
        AssertEqual(false, viewModel.IsSearching, "Search busy state should remain restored when cancellation throws");
        AssertEqual(false, viewModel.IsSubmittingVoiceId, "By-ID busy state should remain restored when cancellation throws");

        releaseSearch.SetResult("{\"total\":0,\"items\":[]}");
        releaseById.SetResult("{\"_id\":\"11111111111111111111111111111111\",\"title\":\"Voice\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}");
        await searchTask.WaitAsync(TimeSpan.FromSeconds(2));
        await byIdTask.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.Dispose();
    }
}
