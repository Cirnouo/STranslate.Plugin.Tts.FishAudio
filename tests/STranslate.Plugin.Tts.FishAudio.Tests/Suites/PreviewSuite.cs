using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
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

internal static class PreviewSuite
{
    internal static void PreviewAudioUrlValidationAllowsOnlyFishAudioStorageHosts()
    {
        AssertPreviewAudioUrlAllowed("https://platform.r2.fish.audio/audio/sample.mp3");
        AssertPreviewAudioUrlAllowed("https://bucket.r2.cloudflarestorage.com/audio/sample.mp3");
        AssertPreviewAudioUrlAllowed("https://nested.bucket.r2.cloudflarestorage.com/audio/sample.mp3");

        AssertPreviewAudioUrlRejected("file:///C:/Windows/win.ini");
        AssertPreviewAudioUrlRejected(@"\\localhost\share\sample.mp3");
        AssertPreviewAudioUrlRejected(@"C:\Windows\win.ini");
        AssertPreviewAudioUrlRejected("http://platform.r2.fish.audio/audio/sample.mp3");
        AssertPreviewAudioUrlRejected("https://localhost/audio/sample.mp3");
        AssertPreviewAudioUrlRejected("https://127.0.0.1/audio/sample.mp3");
        AssertPreviewAudioUrlRejected("https://[::1]/audio/sample.mp3");
        AssertPreviewAudioUrlRejected("https://public-platform.r2.fish.audio/audio/sample.mp3");
        AssertPreviewAudioUrlRejected("https://evil.example/audio/sample.mp3");
        AssertPreviewAudioUrlRejected("not a url");
    }

    internal static void PreviewAudioUrlExpirationRefreshesAtThirtySecondWindow()
    {
        const string url = "https://c97f3361a1c971323738e24f451a0225.r2.cloudflarestorage.com/fish-platform-data/task/f196a6d1769d4a61aa0e48f7e4337f04.mp3?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Date=20260723T094710Z&X-Amz-Expires=3600&X-Amz-SignedHeaders=host&X-Amz-Signature=signature";
        var issuedAt = new DateTimeOffset(2026, 7, 23, 9, 47, 10, TimeSpan.Zero);

        AssertEqual(false, PreviewAudioUrlPolicy.RequiresRefresh(url, issuedAt.AddSeconds(3569)), "Signed preview URL should remain reusable before the 30-second refresh window");
        AssertEqual(true, PreviewAudioUrlPolicy.RequiresRefresh(url, issuedAt.AddSeconds(3570)), "Signed preview URL should refresh at the 30-second boundary");
    }

    internal static void PreviewAudioUrlExpirationTreatsMalformedSignaturesAsExpiring()
    {
        var nowUtc = new DateTimeOffset(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);
        AssertEqual(false, PreviewAudioUrlPolicy.RequiresRefresh("https://platform.r2.fish.audio/audio/public.mp3", nowUtc), "Unsigned preview URLs should remain directly reusable");
        AssertEqual(true, PreviewAudioUrlPolicy.RequiresRefresh("https://platform.r2.fish.audio/audio/missing-both.mp3?X-Amz-Signature=signature", nowUtc), "Signed preview URLs missing both timing parameters should refresh conservatively");
        AssertEqual(true, PreviewAudioUrlPolicy.RequiresRefresh("https://platform.r2.fish.audio/audio/missing.mp3?X-Amz-Date=20260723T094710Z", nowUtc), "Incomplete signed preview URLs should refresh conservatively");
        AssertEqual(true, PreviewAudioUrlPolicy.RequiresRefresh("https://platform.r2.fish.audio/audio/invalid.mp3?X-Amz-Date=invalid&X-Amz-Expires=3600", nowUtc), "Malformed signed preview URLs should refresh conservatively");
        AssertEqual(true, PreviewAudioUrlPolicy.RequiresRefresh("https://platform.r2.fish.audio/audio/duplicate.mp3?X-Amz-Date=20260723T094710Z&X-Amz-Date=20260723T094711Z&X-Amz-Expires=3600", nowUtc), "Duplicate signed preview parameters should refresh conservatively");
    }

    internal static void SelectedPreviewCacheUpdateRejectsChangedVoice()
    {
        const string requestedVoiceId = "11111111111111111111111111111111";
        var currentCache = new CachedVoiceInfo { Title = "Current Voice" };
        var settings = new Settings
        {
            VoiceId = "22222222222222222222222222222222",
            CachedVoice = currentCache,
        };

        var accepted = SettingsViewModel.TryApplyRefreshedCachedVoice(
            settings,
            requestedVoiceId,
            new CachedVoiceInfo { Title = "Stale Voice" });

        AssertEqual(false, accepted, "Selected preview cache update should reject a candidate whose Voice ID changed while waiting to save");
        AssertEqual(true, ReferenceEquals(currentCache, settings.CachedVoice), "Rejected selected preview cache update should preserve the current cache");
    }

    internal static void PreviewAudioRejectsInvalidSearchUrlWithoutStartingPlayback()
    {
        using var network = OverrideNetworkAvailability(true);
        var logger = new TestLogger();
        var player = new TestPreviewAudioPlayer();
        var viewModel = new SettingsViewModel(
            CreateContext(logger: logger),
            new Settings(),
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player);

        viewModel.ToggleSearchItemPreviewCommand.Execute(new VoiceSearchItem
        {
            Id = "0123456789abcdef0123456789abcdef",
            SampleAudioUrl = "file:///C:/Windows/win.ini",
        });

        AssertEqual(null, viewModel.PreviewingVoiceId, "Invalid preview URL should leave preview state stopped");
        AssertEqual(0, player.PlayCallCount, "Invalid preview URL should not start the player abstraction");
        AssertEqual(true, logger.Contains(LogLevel.Warning, "Rejected preview audio URL"), "Invalid preview URL should be logged as a recoverable warning");
    }

    internal static async Task DisplayPreviewValidUrlSkipsDetailRefreshAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string voiceId = "0123456789abcdef0123456789abcdef";
        const string currentUrl = "https://platform.r2.fish.audio/audio/current.mp3";
        var settings = new Settings
        {
            VoiceId = voiceId,
            CachedVoice = new CachedVoiceInfo { Title = "Current Voice", SampleAudioUrl = currentUrl },
        };
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = $$"""
            {"_id":"{{voiceId}}","title":"Unexpected Refresh","description":"","cover_image":"","samples":[{"audio":"https://platform.r2.fish.audio/audio/unexpected.mp3"}],"task_count":0}
            """;
        var player = new TestPreviewAudioPlayer();
        var context = CreateContext(settings: settings, httpService: httpService);
        var contextProxy = (ContextProxy)(object)context;
        var viewModel = new SettingsViewModel(
            context,
            settings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player);

        await viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(0, http.GetCallCount, "Selected voice preview should reuse a URL that is not expiring");
        AssertEqual(0, contextProxy.SaveCount, "Reusing the current selected voice URL should not save settings");
        AssertEqual(currentUrl, player.LastOpenedUri?.AbsoluteUri, "Selected voice preview should play the current reusable URL");
    }

    internal static async Task DisplayPreviewRefreshesCachedVoiceAndPlaysLatestUrlAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string voiceId = "0123456789abcdef0123456789abcdef";
        var oldUrl = CreateExpiredPreviewUrl("old.mp3");
        const string freshUrl = "https://bucket.r2.cloudflarestorage.com/audio/fresh.mp3";
        var settings = new Settings
        {
            VoiceId = voiceId,
            CachedVoice = new CachedVoiceInfo
            {
                Title = "Old Voice",
                Description = "Old description",
                CoverImage = "old-cover",
                AuthorName = "Old Author",
                TaskCount = 1,
                SampleAudioUrl = oldUrl,
            },
        };
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = $$"""
            {"_id":"{{voiceId}}","title":"Fresh Voice","description":"Fresh description","cover_image":"fresh-cover","samples":[{"title":"Sample","text":"Hello","audio":"{{freshUrl}}"}],"task_count":42,"author":{"_id":"author-id","nickname":"Fresh Author","avatar":"avatar"},"tags":[],"languages":[],"like_count":7}
            """;
        var player = new TestPreviewAudioPlayer();
        var context = CreateContext(settings: settings, httpService: httpService);
        var contextProxy = (ContextProxy)(object)context;
        var viewModel = new SettingsViewModel(
            context,
            settings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player);

        await viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(1, http.GetCallCount, "Selected voice preview should refresh model details once");
        AssertEqual($"https://api.fish.audio/model/{voiceId}", http.LastGetUrl, "Selected voice preview should refresh the captured voice ID");
        AssertEqual("Bearer dummy", AssertHeaders(http.LastGetOptions, "Selected voice preview should send headers")["Authorization"], "Selected voice preview should use the dummy token");
        AssertEqual(TimeSpan.FromSeconds(15), http.LastGetOptions?.Timeout, "Selected voice preview refresh should use the model lookup timeout");
        AssertEqual("Fresh Voice", settings.CachedVoice?.Title, "Successful preview refresh should replace cached title");
        AssertEqual("Fresh description", settings.CachedVoice?.Description, "Successful preview refresh should replace cached description");
        AssertEqual("fresh-cover", settings.CachedVoice?.CoverImage, "Successful preview refresh should replace cached cover");
        AssertEqual("Fresh Author", settings.CachedVoice?.AuthorName, "Successful preview refresh should replace cached author");
        AssertEqual(42, settings.CachedVoice?.TaskCount, "Successful preview refresh should replace cached task count");
        AssertEqual(freshUrl, settings.CachedVoice?.SampleAudioUrl, "Successful preview refresh should replace cached sample URL");
        AssertEqual(1, contextProxy.SaveCount, "Successful preview refresh should save cached voice immediately");
        AssertEqual("Fresh Voice", viewModel.CachedVoiceTitle, "Successful preview refresh should update visible title");
        AssertEqual("Fresh description", viewModel.CachedVoiceDescription, "Successful preview refresh should update visible description");
        AssertEqual("Fresh Author", viewModel.CachedVoiceAuthor, "Successful preview refresh should update visible author");
        AssertEqual(42, viewModel.CachedVoiceTaskCount, "Successful preview refresh should update visible task count");
        AssertEqual(freshUrl, viewModel.CachedVoiceSampleUrl, "Successful preview refresh should update visible sample URL");
        AssertEqual(freshUrl, player.LastOpenedUri?.AbsoluteUri, "Successful preview refresh should play the latest sample URL");
        AssertEqual(1, player.PlayCallCount, "Successful preview refresh should start playback once");
        AssertEqual(voiceId, viewModel.PreviewingVoiceId, "Successful preview refresh should expose selected voice playback state");
    }

    internal static async Task DisplayPreviewOfflinePreflightStopsBeforeRefreshAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        const string voiceId = "0123456789abcdef0123456789abcdef";
        var settings = new Settings
        {
            VoiceId = voiceId,
            CachedVoice = new CachedVoiceInfo
            {
                Title = "Offline Voice",
                SampleAudioUrl = "https://platform.r2.fish.audio/audio/offline.mp3",
            },
        };
        var snackbar = new TestSnackbar();
        var (httpService, http) = TestHttpServiceProxy.Create();
        var player = new TestPreviewAudioPlayer();
        var viewModel = new SettingsViewModel(
            CreateContext(snackbar, settings, httpService),
            settings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player);

        await viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(FishAudioRequestPolicy.NetworkUnavailableKey, snackbar.LastError, "Offline selected preview should show the existing network unavailable message");
        AssertEqual(0, http.GetCallCount, "Offline selected preview should not call model details");
        AssertEqual(0, player.PlayCallCount, "Offline selected preview should not start playback");
        AssertEqual(null, viewModel.PreviewingVoiceId, "Offline selected preview should remain stopped");
    }

    internal static async Task DisplayPreviewRechecksNetworkBeforePlaybackAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string voiceId = "0123456789abcdef0123456789abcdef";
        var oldUrl = CreateExpiredPreviewUrl("before-refresh.mp3");
        const string freshUrl = "https://platform.r2.fish.audio/audio/after-refresh.mp3";
        var settings = new Settings
        {
            VoiceId = voiceId,
            CachedVoice = new CachedVoiceInfo { Title = "Before Refresh", SampleAudioUrl = oldUrl },
        };
        var snackbar = new TestSnackbar();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = $$"""
            {"_id":"{{voiceId}}","title":"After Refresh","description":"","cover_image":"","samples":[{"title":"","text":"","audio":"{{freshUrl}}"}],"task_count":0,"author":null,"tags":[],"languages":[],"like_count":0}
            """;
        var player = new TestPreviewAudioPlayer();
        var context = CreateContext(snackbar, settings, httpService);
        var contextProxy = (ContextProxy)(object)context;
        contextProxy.OnSave = () => network.Set(false);
        var viewModel = new SettingsViewModel(
            context,
            settings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player);

        await viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual("After Refresh", settings.CachedVoice?.Title, "Network loss before playback should not discard refreshed metadata");
        AssertEqual(freshUrl, viewModel.CachedVoiceSampleUrl, "Network loss before playback should still synchronize visible refreshed metadata");
        AssertEqual(1, contextProxy.SaveCount, "Network loss before playback should still save refreshed metadata");
        AssertEqual(0, player.PlayCallCount, "Network loss after refresh should stop before playback");
        AssertEqual(FishAudioRequestPolicy.NetworkUnavailableKey, snackbar.LastError, "Network loss after refresh should show the existing network unavailable message");
    }

    internal static void SearchPreviewOfflinePreflightStopsBeforePlayback()
    {
        using var network = OverrideNetworkAvailability(false);
        var snackbar = new TestSnackbar();
        var (httpService, http) = TestHttpServiceProxy.Create();
        var player = new TestPreviewAudioPlayer();
        var viewModel = new SettingsViewModel(
            CreateContext(snackbar, httpService: httpService),
            new Settings(),
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player);
        var item = new VoiceSearchItem
        {
            Id = "fedcba9876543210fedcba9876543210",
            SampleAudioUrl = "https://platform.r2.fish.audio/audio/search.mp3",
        };

        viewModel.ToggleSearchItemPreviewCommand.Execute(item);

        AssertEqual(FishAudioRequestPolicy.NetworkUnavailableKey, snackbar.LastError, "Offline search preview should show the existing network unavailable message");
        AssertEqual(0, http.GetCallCount, "Search preview should never call model details");
        AssertEqual(0, player.PlayCallCount, "Offline search preview should not start playback");
        AssertEqual(null, viewModel.PreviewingVoiceId, "Offline search preview should remain stopped");
    }

    internal static void SearchPreviewUsesListUrlWithoutDetailRefresh()
    {
        using var network = OverrideNetworkAvailability(true);
        const string listUrl = "https://platform.r2.fish.audio/audio/list-result.mp3";
        var (httpService, http) = TestHttpServiceProxy.Create();
        var player = new TestPreviewAudioPlayer();
        var viewModel = new SettingsViewModel(
            CreateContext(httpService: httpService),
            new Settings(),
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player);
        var item = new VoiceSearchItem
        {
            Id = "fedcba9876543210fedcba9876543210",
            SampleAudioUrl = listUrl,
        };

        viewModel.ToggleSearchItemPreviewCommand.Execute(item);

        AssertEqual(0, http.GetCallCount, "Search result preview should not call model details");
        AssertEqual(listUrl, player.LastOpenedUri?.AbsoluteUri, "Search result preview should play its list response URL directly");
        AssertEqual(1, player.PlayCallCount, "Online search result preview should start playback once");
    }

    internal static async Task SearchPreviewExpiredUrlRefreshesVisibleMetadataAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string voiceId = "fedcba9876543210fedcba9876543210";
        const string freshUrl = "https://platform.r2.fish.audio/audio/search-fresh.mp3";
        var item = new VoiceSearchItem
        {
            Id = voiceId,
            Title = "Old Search Voice",
            Description = "Old description",
            AuthorName = "Old Author",
            TaskCount = 1,
            CoverImage = "old-cover",
            CoverUrl = FishAudioApi.BuildCoverUrl("old-cover"),
            SampleAudioUrl = CreateExpiredPreviewUrl("search-expired.mp3"),
        };
        var settings = new Settings();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = $$"""
            {
              "_id": "{{voiceId}}",
              "title": "Fresh Search Voice",
              "description": "Fresh description",
              "cover_image": "fresh-cover",
              "samples": [{ "audio": "{{freshUrl}}" }],
              "task_count": 42,
              "author": { "nickname": "Fresh Author" }
            }
            """;
        var player = new TestPreviewAudioPlayer();
        var context = CreateContext(settings: settings, httpService: httpService);
        var contextProxy = (ContextProxy)(object)context;
        var changedProperties = new HashSet<string>(StringComparer.Ordinal);
        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
                changedProperties.Add(args.PropertyName);
        };
        var viewModel = new SettingsViewModel(
            context,
            settings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player)
        {
            SearchResults = [item],
        };

        await viewModel.ToggleSearchItemPreviewCommand.ExecuteAsync(item).WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(1, http.GetCallCount, "Expired search preview should refresh model details once");
        AssertEqual($"https://api.fish.audio/model/{voiceId}", http.LastGetUrl, "Expired search preview should refresh the clicked voice ID");
        AssertEqual("Fresh Search Voice", item.Title, "Search preview refresh should update the visible title");
        AssertEqual("Fresh description", item.Description, "Search preview refresh should update the visible description");
        AssertEqual("Fresh Author", item.AuthorName, "Search preview refresh should update the visible author");
        AssertEqual(42, item.TaskCount, "Search preview refresh should update the visible task count");
        AssertEqual("fresh-cover", item.CoverImage, "Search preview refresh should update the cover source");
        AssertEqual(FishAudioApi.BuildCoverUrl("fresh-cover"), item.CoverUrl, "Search preview refresh should update the visible cover URL");
        AssertEqual(freshUrl, item.SampleAudioUrl, "Search preview refresh should update the sample URL");
        foreach (var propertyName in new[] { nameof(item.Title), nameof(item.Description), nameof(item.AuthorName), nameof(item.TaskCount), nameof(item.CoverImage), nameof(item.CoverUrl), nameof(item.SampleAudioUrl) })
            AssertEqual(true, changedProperties.Contains(propertyName), $"Search preview refresh should notify the view that {propertyName} changed");
        AssertEqual(0, contextProxy.SaveCount, "Search preview refresh should not persist search-only metadata");
        AssertEqual(freshUrl, player.LastOpenedUri?.AbsoluteUri, "Search preview refresh should play the refreshed URL");
    }

    internal static async Task SearchPreviewExpiredRefreshFailureDoesNotFallbackAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string voiceId = "fedcba9876543210fedcba9876543210";
        var oldUrl = CreateExpiredPreviewUrl("search-failure.mp3");
        var item = new VoiceSearchItem
        {
            Id = voiceId,
            Title = "Keep Search Voice",
            SampleAudioUrl = oldUrl,
        };
        var snackbar = new TestSnackbar();
        var logger = new TestLogger();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetException = new TimeoutException("model lookup timed out");
        var player = new TestPreviewAudioPlayer();
        var viewModel = new SettingsViewModel(
            CreateContext(snackbar, httpService: httpService, logger: logger),
            new Settings(),
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player)
        {
            SearchResults = [item],
        };

        await viewModel.ToggleSearchItemPreviewCommand.ExecuteAsync(item).WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(SettingsViewModel.PreviewRefreshFailedKey, snackbar.LastError, "Expired search preview refresh failure should show the localized refresh error");
        AssertEqual("Keep Search Voice", item.Title, "Search preview refresh failure should preserve existing metadata");
        AssertEqual(oldUrl, item.SampleAudioUrl, "Search preview refresh failure should preserve the old URL without playing it");
        AssertEqual(0, player.PlayCallCount, "Expired search preview refresh failure should not start fallback playback");
        AssertEqual(true, logger.Contains(LogLevel.Warning, "Search result preview refresh failed"), "Search preview refresh failure should be logged safely");
    }

    internal static async Task SearchPreviewRemovedResultIgnoresLateRefreshAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string voiceId = "fedcba9876543210fedcba9876543210";
        const string lateUrl = "https://platform.r2.fish.audio/audio/search-late.mp3";
        var oldUrl = CreateExpiredPreviewUrl("search-removed.mp3");
        var item = new VoiceSearchItem
        {
            Id = voiceId,
            Title = "Original Search Voice",
            SampleAudioUrl = oldUrl,
        };
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetAsyncHandler = (_, _, _) =>
        {
            requestStarted.TrySetResult();
            return releaseResponse.Task;
        };
        var player = new TestPreviewAudioPlayer();
        var viewModel = new SettingsViewModel(
            CreateContext(httpService: httpService),
            new Settings(),
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player)
        {
            SearchResults = [item],
        };

        var previewTask = viewModel.ToggleSearchItemPreviewCommand.ExecuteAsync(item);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.SearchResults = [];
        releaseResponse.SetResult($$"""
            {"_id":"{{voiceId}}","title":"Late Search Voice","description":"late","cover_image":"late-cover","samples":[{"audio":"{{lateUrl}}"}],"task_count":99}
            """);
        await previewTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual("Original Search Voice", item.Title, "A removed search item should ignore late refreshed metadata");
        AssertEqual(oldUrl, item.SampleAudioUrl, "A removed search item should keep its original sample URL after a late response");
        AssertEqual(0, player.PlayCallCount, "A removed search item should not start playback after a late response");
    }

    internal static async Task SearchPreviewSecondClickCancelsPendingRefreshAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string voiceId = "fedcba9876543210fedcba9876543210";
        var oldUrl = CreateExpiredPreviewUrl("search-pending.mp3");
        var item = new VoiceSearchItem
        {
            Id = voiceId,
            Title = "Pending Search Voice",
            SampleAudioUrl = oldUrl,
        };
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetAsyncHandler = (_, _, cancellationToken) =>
        {
            requestStarted.TrySetResult();
            cancellationToken.Register(() => requestCanceled.TrySetResult());
            return releaseResponse.Task;
        };
        var player = new TestPreviewAudioPlayer();
        var viewModel = new SettingsViewModel(
            CreateContext(httpService: httpService),
            new Settings(),
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player)
        {
            SearchResults = [item],
        };

        var firstPreviewTask = viewModel.ToggleSearchItemPreviewCommand.ExecuteAsync(item);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await viewModel.ToggleSearchItemPreviewCommand.ExecuteAsync(item).WaitAsync(TimeSpan.FromSeconds(2));
        await requestCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        releaseResponse.SetResult($$"""
            {"_id":"{{voiceId}}","title":"Late Search Voice","description":"","cover_image":"","samples":[{"audio":"https://platform.r2.fish.audio/audio/search-late-after-cancel.mp3"}],"task_count":0}
            """);
        await firstPreviewTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(1, http.GetCallCount, "Second click on a pending search refresh should cancel without starting another detail request");
        AssertEqual("Pending Search Voice", item.Title, "Canceled search refresh should not apply late metadata");
        AssertEqual(oldUrl, item.SampleAudioUrl, "Canceled search refresh should preserve the original URL");
        AssertEqual(0, player.PlayCallCount, "Canceled search refresh should not start playback");
    }

    internal static void SearchPreviewSecondClickStopsWithoutNetwork()
    {
        using var network = OverrideNetworkAvailability(true);
        var snackbar = new TestSnackbar();
        var player = new TestPreviewAudioPlayer();
        var viewModel = new SettingsViewModel(
            CreateContext(snackbar),
            new Settings(),
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player);
        var item = new VoiceSearchItem
        {
            Id = "fedcba9876543210fedcba9876543210",
            SampleAudioUrl = "https://platform.r2.fish.audio/audio/search-stop.mp3",
        };

        viewModel.ToggleSearchItemPreviewCommand.Execute(item);
        network.Set(false);
        viewModel.ToggleSearchItemPreviewCommand.Execute(item);

        AssertEqual(1, player.StopCallCount, "Second search preview click should stop the active player immediately");
        AssertEqual(null, viewModel.PreviewingVoiceId, "Second search preview click should clear playback state");
        AssertEqual(null, snackbar.LastError, "Stopping search preview while offline should not show a network error");
    }

    internal static async Task DisplayPreviewExpiredRefreshFailureShowsErrorAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string voiceId = "0123456789abcdef0123456789abcdef";
        var oldUrl = CreateExpiredPreviewUrl("fallback.mp3");
        var originalCache = new CachedVoiceInfo
        {
            Title = "Keep Voice",
            Description = "Keep description",
            CoverImage = "keep-cover",
            AuthorName = "Keep Author",
            TaskCount = 9,
            SampleAudioUrl = oldUrl,
        };
        var settings = new Settings { VoiceId = voiceId, CachedVoice = originalCache };
        var snackbar = new TestSnackbar();
        var logger = new TestLogger();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetException = new TimeoutException("model lookup timed out");
        var player = new TestPreviewAudioPlayer();
        var context = CreateContext(snackbar, settings, httpService, logger: logger);
        var contextProxy = (ContextProxy)(object)context;
        var viewModel = new SettingsViewModel(
            context,
            settings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player);

        await viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(SettingsViewModel.PreviewRefreshFailedKey, snackbar.LastError, "Expired selected preview refresh failure should show the localized refresh error");
        AssertEqual(true, ReferenceEquals(originalCache, settings.CachedVoice), "Selected preview refresh failure should preserve the old cache object");
        AssertEqual(0, contextProxy.SaveCount, "Selected preview refresh failure should not save settings");
        AssertEqual(null, player.LastOpenedUri?.AbsoluteUri, "Expired selected preview refresh failure should not open the captured old URL");
        AssertEqual(0, player.PlayCallCount, "Expired selected preview refresh failure should not start fallback playback");
        AssertEqual(true, logger.Contains(LogLevel.Warning, "Selected voice preview refresh failed"), "Selected preview refresh failure should be logged safely");
    }

    internal static async Task DisplayPreviewNotFoundShowsRefreshErrorAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string voiceId = "0123456789abcdef0123456789abcdef";
        var oldUrl = CreateExpiredPreviewUrl("not-found-fallback.mp3");
        var originalCache = new CachedVoiceInfo
        {
            Title = "Keep Missing Voice",
            SampleAudioUrl = oldUrl,
        };
        var settings = new Settings { VoiceId = voiceId, CachedVoice = originalCache };
        var snackbar = new TestSnackbar();
        var logger = new TestLogger();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetException = new HttpRequestException(
            "Response status code does not indicate success: 404 (Not Found).",
            null,
            HttpStatusCode.NotFound);
        var player = new TestPreviewAudioPlayer();
        var context = CreateContext(snackbar, settings, httpService, logger: logger);
        var contextProxy = (ContextProxy)(object)context;
        var viewModel = new SettingsViewModel(
            context,
            settings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player);

        await viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(1, http.GetCallCount, "Selected preview should make one detail request before handling 404");
        AssertEqual(SettingsViewModel.PreviewRefreshFailedKey, snackbar.LastError, "Selected preview 404 should show the localized refresh error");
        AssertEqual(true, ReferenceEquals(originalCache, settings.CachedVoice), "Selected preview 404 should preserve the old cache object");
        AssertEqual(0, contextProxy.SaveCount, "Selected preview 404 should not save settings");
        AssertEqual(null, player.LastOpenedUri?.AbsoluteUri, "Selected preview 404 should not open the expired old URL");
        AssertEqual(0, player.PlayCallCount, "Selected preview 404 should not start fallback playback");
        AssertEqual(true, logger.Contains(LogLevel.Warning, "returned no voice"), "Selected preview 404 should be logged safely");
    }

    internal static async Task DisplayPreviewLatestVoiceWithoutSampleShowsUnavailableAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string voiceId = "0123456789abcdef0123456789abcdef";
        var oldUrl = CreateExpiredPreviewUrl("obsolete.mp3");
        var settings = new Settings
        {
            VoiceId = voiceId,
            CachedVoice = new CachedVoiceInfo { Title = "Old Voice", SampleAudioUrl = oldUrl },
        };
        var snackbar = new TestSnackbar();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = $$"""
            {"_id":"{{voiceId}}","title":"Latest Voice","description":"Latest description","cover_image":"latest-cover","samples":[],"task_count":77,"author":{"_id":"author-id","nickname":"Latest Author","avatar":"avatar"},"tags":[],"languages":[],"like_count":3}
            """;
        var player = new TestPreviewAudioPlayer();
        var context = CreateContext(snackbar, settings, httpService);
        var contextProxy = (ContextProxy)(object)context;
        var viewModel = new SettingsViewModel(
            context,
            settings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player);

        await viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual("Latest Voice", settings.CachedVoice?.Title, "Latest details without a sample should still replace cached metadata");
        AssertEqual("Latest description", settings.CachedVoice?.Description, "Latest details without a sample should preserve the latest description");
        AssertEqual("latest-cover", settings.CachedVoice?.CoverImage, "Latest details without a sample should preserve the latest cover");
        AssertEqual("Latest Author", settings.CachedVoice?.AuthorName, "Latest details without a sample should preserve the latest author");
        AssertEqual(77, settings.CachedVoice?.TaskCount, "Latest details without a sample should preserve the latest task count");
        AssertEqual(null, settings.CachedVoice?.SampleAudioUrl, "Latest details without a sample should replace the obsolete sample URL");
        AssertEqual(1, contextProxy.SaveCount, "Latest details without a sample should be saved immediately");
        AssertEqual("Latest Voice", viewModel.CachedVoiceTitle, "Latest details without a sample should update visible metadata");
        AssertEqual(null, viewModel.CachedVoiceSampleUrl, "Latest details without a sample should remove the visible obsolete sample URL");
        AssertEqual(0, player.PlayCallCount, "Latest details without a sample should not fall back to obsolete playback");
        AssertEqual("STranslate_Plugin_Tts_FishAudio_Preview_Unavailable", snackbar.LastError, "Latest details without a sample should show the localized unavailable message");
    }

    internal static async Task DisplayPreviewRefreshReturningExpiredUrlDoesNotPlayAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string voiceId = "0123456789abcdef0123456789abcdef";
        var oldUrl = CreateExpiredPreviewUrl("selected-old-expired.mp3");
        var refreshedUrl = CreateExpiredPreviewUrl("selected-still-expired.mp3");
        var settings = new Settings
        {
            VoiceId = voiceId,
            CachedVoice = new CachedVoiceInfo { Title = "Old Voice", SampleAudioUrl = oldUrl },
        };
        var snackbar = new TestSnackbar();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = $$"""
            {"_id":"{{voiceId}}","title":"Refreshed Voice","description":"","cover_image":"","samples":[{"audio":"{{refreshedUrl}}"}],"task_count":0}
            """;
        var player = new TestPreviewAudioPlayer();
        var context = CreateContext(snackbar, settings, httpService);
        var contextProxy = (ContextProxy)(object)context;
        var viewModel = new SettingsViewModel(
            context,
            settings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player);

        await viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(1, contextProxy.SaveCount, "Authoritative refreshed metadata should still be saved when its sample is already expiring");
        AssertEqual(refreshedUrl, settings.CachedVoice?.SampleAudioUrl, "Refreshed metadata should preserve the API response sample URL");
        AssertEqual(SettingsViewModel.PreviewRefreshFailedKey, snackbar.LastError, "An already expiring refreshed URL should show the refresh failure message");
        AssertEqual(0, player.PlayCallCount, "An already expiring refreshed URL should not start playback");
    }

    internal static async Task DisplayPreviewSecondClickStopsWithoutNetworkAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string voiceId = "0123456789abcdef0123456789abcdef";
        const string freshUrl = "https://platform.r2.fish.audio/audio/repeat.mp3";
        var settings = new Settings
        {
            VoiceId = voiceId,
            CachedVoice = new CachedVoiceInfo { Title = "Repeat Voice", SampleAudioUrl = freshUrl },
        };
        var snackbar = new TestSnackbar();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = $$"""
            {"_id":"{{voiceId}}","title":"Repeat Voice","description":"","cover_image":"","samples":[{"title":"","text":"","audio":"{{freshUrl}}"}],"task_count":0,"author":null,"tags":[],"languages":[],"like_count":0}
            """;
        var player = new TestPreviewAudioPlayer();
        var viewModel = new SettingsViewModel(
            CreateContext(snackbar, settings, httpService),
            settings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player);

        await viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(voiceId, viewModel.PreviewingVoiceId, "First selected preview click should start playback");
        AssertEqual(0, http.GetCallCount, "First selected preview click should reuse the current URL without refreshing details");

        network.Set(false);
        await viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(0, http.GetCallCount, "Stopping selected preview should not make a detail request");
        AssertEqual(1, player.StopCallCount, "Second selected preview click should stop the active player immediately");
        AssertEqual(null, viewModel.PreviewingVoiceId, "Second selected preview click should clear playback state");
        AssertEqual(null, snackbar.LastError, "Stopping selected preview while offline should not show a network error");
    }

    internal static async Task DisplayPreviewSecondClickCancelsPendingRefreshWithoutRestartAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string voiceId = "0123456789abcdef0123456789abcdef";
        var oldUrl = CreateExpiredPreviewUrl("pending-old.mp3");
        const string lateUrl = "https://platform.r2.fish.audio/audio/pending-late.mp3";
        var originalCache = new CachedVoiceInfo { Title = "Pending Original", SampleAudioUrl = oldUrl };
        var settings = new Settings { VoiceId = voiceId, CachedVoice = originalCache };
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetAsyncHandler = (_, _, ct) =>
        {
            requestStarted.TrySetResult();
            ct.Register(() => requestCanceled.TrySetResult());
            return releaseResponse.Task;
        };
        var player = new TestPreviewAudioPlayer();
        var context = CreateContext(settings: settings, httpService: httpService);
        var contextProxy = (ContextProxy)(object)context;
        var viewModel = new SettingsViewModel(
            context,
            settings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player);

        viewModel.ToggleDisplayPreviewCommand.Execute(null);
        var firstPreviewTask = viewModel.ToggleDisplayPreviewCommand.ExecutionTask
            ?? throw new InvalidOperationException("First selected preview command should expose its execution task");
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.ToggleDisplayPreviewCommand.Execute(null);
        var canceledBeforeRelease = requestCanceled.Task.IsCompleted;
        var getCallsBeforeRelease = http.GetCallCount;
        var savesBeforeRelease = contextProxy.SaveCount;
        var playsBeforeRelease = player.PlayCallCount;

        releaseResponse.SetResult($$"""
            {"_id":"{{voiceId}}","title":"Pending Late","description":"Late","cover_image":"late-cover","samples":[{"title":"","text":"","audio":"{{lateUrl}}"}],"task_count":99,"author":null,"tags":[],"languages":[],"like_count":0}
            """);
        await firstPreviewTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(true, canceledBeforeRelease, "Second selected preview click should cancel the pending request before its response is released");
        AssertEqual(1, getCallsBeforeRelease, "Second selected preview click should not start another model detail request");
        AssertEqual(0, savesBeforeRelease, "Second selected preview click should not save settings before the late response");
        AssertEqual(0, playsBeforeRelease, "Second selected preview click should not start playback before the late response");
        AssertEqual(true, ReferenceEquals(originalCache, settings.CachedVoice), "Late canceled response should not replace cached voice metadata");
        AssertEqual(0, contextProxy.SaveCount, "Late canceled response should not save settings");
        AssertEqual(0, player.PlayCallCount, "Late canceled response should not start playback");
        AssertEqual(null, viewModel.PreviewingVoiceId, "Canceling a pending selected preview should leave playback stopped");
    }

    internal static void PreviewPlaybackFailureUsesNetworkAwareMessage()
    {
        using var network = OverrideNetworkAvailability(true);
        var snackbar = new TestSnackbar();
        var player = new TestPreviewAudioPlayer();
        var viewModel = new SettingsViewModel(
            CreateContext(snackbar),
            new Settings(),
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player);
        var item = new VoiceSearchItem
        {
            Id = "fedcba9876543210fedcba9876543210",
            SampleAudioUrl = "https://platform.r2.fish.audio/audio/failure.mp3",
        };

        viewModel.ToggleSearchItemPreviewCommand.Execute(item);
        player.RaiseFailed(new InvalidOperationException("media load failed"));

        AssertEqual("STranslate_Plugin_Tts_FishAudio_Preview_PlaybackFailed", snackbar.LastError, "Online media failure should show the localized playback failure message");
        AssertEqual(null, viewModel.PreviewingVoiceId, "Media failure should stop preview playback");

        snackbar.Clear();
        viewModel.ToggleSearchItemPreviewCommand.Execute(item);
        network.Set(false);
        player.RaiseFailed(new TimeoutException("media load timed out"));

        AssertEqual(FishAudioRequestPolicy.NetworkUnavailableKey, snackbar.LastError, "Media failure after network loss should show the existing network unavailable message");
        AssertEqual(null, viewModel.PreviewingVoiceId, "Media failure after network loss should stop preview playback");
    }

    internal static async Task SearchPreviewInvalidatesPendingDisplayRefreshAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string selectedVoiceId = "11111111111111111111111111111111";
        const string searchVoiceId = "22222222222222222222222222222222";
        var selectedOldUrl = CreateExpiredPreviewUrl("selected-old.mp3");
        const string selectedLateUrl = "https://platform.r2.fish.audio/audio/selected-late.mp3";
        const string searchUrl = "https://platform.r2.fish.audio/audio/search-current.mp3";
        var originalCache = new CachedVoiceInfo { Title = "Selected Original", SampleAudioUrl = selectedOldUrl };
        var settings = new Settings { VoiceId = selectedVoiceId, CachedVoice = originalCache };
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetAsyncHandler = (_, _, ct) =>
        {
            requestStarted.TrySetResult();
            ct.Register(() => requestCanceled.TrySetResult());
            return releaseResponse.Task;
        };
        var player = new TestPreviewAudioPlayer();
        var context = CreateContext(settings: settings, httpService: httpService);
        var contextProxy = (ContextProxy)(object)context;
        var viewModel = new SettingsViewModel(
            context,
            settings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player);

        var selectedPreviewTask = viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.ToggleSearchItemPreviewCommand.Execute(new VoiceSearchItem
        {
            Id = searchVoiceId,
            SampleAudioUrl = searchUrl,
        });
        releaseResponse.SetResult($$"""
            {"_id":"{{selectedVoiceId}}","title":"Selected Late","description":"Late","cover_image":"late-cover","samples":[{"title":"","text":"","audio":"{{selectedLateUrl}}"}],"task_count":99,"author":null,"tags":[],"languages":[],"like_count":0}
            """);
        await selectedPreviewTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(searchVoiceId, viewModel.PreviewingVoiceId, "Search preview should remain active after the older selected refresh completes");
        AssertEqual(searchUrl, player.LastOpenedUri?.AbsoluteUri, "Late selected refresh should not replace the search preview URL");
        AssertEqual(1, player.PlayCallCount, "Late selected refresh should not start selected voice playback");
        AssertEqual(true, ReferenceEquals(originalCache, settings.CachedVoice), "Late selected refresh should not replace cached voice metadata after search preview starts");
        AssertEqual(0, contextProxy.SaveCount, "Late selected refresh should not save cached voice metadata after search preview starts");
        await requestCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    internal static async Task DisplayPreviewVoiceSwitchIgnoresLateRefreshAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string oldVoiceId = "11111111111111111111111111111111";
        const string newVoiceId = "22222222222222222222222222222222";
        var oldUrl = CreateExpiredPreviewUrl("old-selected.mp3");
        const string newUrl = "https://platform.r2.fish.audio/audio/new-selected.mp3";
        const string lateUrl = "https://platform.r2.fish.audio/audio/late-old.mp3";
        var settings = new Settings
        {
            VoiceId = oldVoiceId,
            CachedVoice = new CachedVoiceInfo { Title = "Old Voice", SampleAudioUrl = oldUrl },
        };
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetAsyncHandler = (_, _, ct) =>
        {
            requestStarted.TrySetResult();
            ct.Register(() => requestCanceled.TrySetResult());
            return releaseResponse.Task;
        };
        var player = new TestPreviewAudioPlayer();
        var context = CreateContext(settings: settings, httpService: httpService);
        var contextProxy = (ContextProxy)(object)context;
        var viewModel = new SettingsViewModel(
            context,
            settings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player);

        var previewTask = viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.SelectVoiceCommand.Execute(new VoiceSearchItem
        {
            Id = newVoiceId,
            Title = "New Voice",
            Description = "New description",
            AuthorName = "New Author",
            TaskCount = 5,
            SampleAudioUrl = newUrl,
        });
        var saveCountAfterSwitch = contextProxy.SaveCount;

        releaseResponse.SetResult($$"""
            {"_id":"{{oldVoiceId}}","title":"Late Old Voice","description":"Late description","cover_image":"late-cover","samples":[{"title":"","text":"","audio":"{{lateUrl}}"}],"task_count":99,"author":{"_id":"","nickname":"Late Author","avatar":""},"tags":[],"languages":[],"like_count":0}
            """);
        await previewTask.WaitAsync(TimeSpan.FromSeconds(2));

        await requestCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(newVoiceId, settings.VoiceId, "Voice switch should keep the new selected voice ID after a late preview refresh");
        AssertEqual("New Voice", settings.CachedVoice?.Title, "Voice switch should keep the new cached voice after a late preview refresh");
        AssertEqual(newUrl, settings.CachedVoice?.SampleAudioUrl, "Voice switch should keep the new sample URL after a late preview refresh");
        AssertEqual(saveCountAfterSwitch, contextProxy.SaveCount, "Late preview refresh should not save settings after the selected voice changes");
        AssertEqual(0, player.PlayCallCount, "Late preview refresh should not start playback after the selected voice changes");
    }

    internal static async Task DisplayPreviewDisposeCancelsAndIgnoresLateRefreshAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string voiceId = "11111111111111111111111111111111";
        var oldUrl = CreateExpiredPreviewUrl("dispose-old.mp3");
        const string lateUrl = "https://platform.r2.fish.audio/audio/dispose-late.mp3";
        var originalCache = new CachedVoiceInfo { Title = "Keep After Dispose", SampleAudioUrl = oldUrl };
        var settings = new Settings { VoiceId = voiceId, CachedVoice = originalCache };
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetAsyncHandler = (_, _, ct) =>
        {
            requestStarted.TrySetResult();
            ct.Register(() => requestCanceled.TrySetResult());
            return releaseResponse.Task;
        };
        var player = new TestPreviewAudioPlayer();
        var context = CreateContext(settings: settings, httpService: httpService);
        var contextProxy = (ContextProxy)(object)context;
        var viewModel = new SettingsViewModel(
            context,
            settings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: () => player);

        var previewTask = viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.Dispose();
        var saveCountAfterDispose = contextProxy.SaveCount;
        await requestCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        releaseResponse.SetResult($$"""
            {"_id":"{{voiceId}}","title":"Late After Dispose","description":"","cover_image":"","samples":[{"title":"","text":"","audio":"{{lateUrl}}"}],"task_count":0,"author":null,"tags":[],"languages":[],"like_count":0}
            """);
        await previewTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(true, ReferenceEquals(originalCache, settings.CachedVoice), "Disposed preview refresh should preserve the original cached voice");
        AssertEqual(saveCountAfterDispose, contextProxy.SaveCount, "Late preview refresh should not save settings after Dispose");
        AssertEqual(0, player.PlayCallCount, "Late preview refresh should not start playback after Dispose");
    }

    internal static string CreateExpiredPreviewUrl(string fileName) =>
        $"https://preview.r2.cloudflarestorage.com/audio/{fileName}?X-Amz-Date=20000101T000000Z&X-Amz-Expires=3600&X-Amz-Signature=expired";

    internal static void AssertPreviewAudioUrlAllowed(string url)
    {
        AssertEqual(true, PreviewAudioUrlPolicy.TryCreateAllowedUri(url, out var uri), $"{url} should be allowed as a preview audio URL");
        AssertEqual(url, uri?.AbsoluteUri, $"{url} should preserve the validated preview URI");
    }

    internal static void AssertPreviewAudioUrlRejected(string url)
    {
        AssertEqual(false, PreviewAudioUrlPolicy.TryCreateAllowedUri(url, out var uri), $"{url} should be rejected as a preview audio URL");
        AssertEqual(null, uri, $"{url} should not return a URI when rejected");
    }
}
