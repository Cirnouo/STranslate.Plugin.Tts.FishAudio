using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;
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

internal static class StartupCreditSuite
{
    internal static async Task StartupCreditRefreshPreflightSkipsRequestAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        foreach (var apiKey in new[] { AppliedKey, "", "ABC", AppliedKey.ToUpperInvariant() })
        {
            network.Set(apiKey != AppliedKey);
            var settings = new Settings { ApiKey = apiKey };
            var (httpService, http) = TestHttpServiceProxy.Create();
            var logger = new TestLogger();
            var plugin = new Main();

            plugin.Init(CreateContext(settings: settings, httpService: httpService, logger: logger));
            await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

            AssertEqual(
                0,
                http.GetUrls.Count(url => url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)),
                $"Startup credit refresh should skip the endpoint for API Key '{apiKey}'");
            AssertEqual(
                true,
                logger.Contains(LogLevel.Warning, "Startup credit refresh"),
                "Skipped startup credit refresh should be logged safely");
            if (!string.IsNullOrEmpty(apiKey))
                AssertEqual(false, logger.Contains(apiKey), "Startup credit refresh logs should not contain the API Key");
        }
    }

    internal static async Task StartupCreditRefreshRequestsOncePerInitializationAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings { ApiKey = AppliedKey };
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetAsyncHandler = (url, _, _) => Task.FromResult(
            url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
                ? "{\"credit\":\"12.34\"}"
                : "{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
        var context = CreateContext(settings: settings, httpService: httpService);
        var plugin = new Main();

        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(
            1,
            http.GetUrls.Count(url => url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)),
            "A valid API Key should request credit once during initialization");
        AssertEqual(TimeSpan.FromSeconds(15), http.GetOptionsByUrl.Single(pair => pair.Url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)).Options?.Timeout, "Startup credit refresh should reuse the 15 second credit timeout");
        var headers = AssertHeaders(
            http.GetOptionsByUrl.Single(pair => pair.Url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)).Options,
            "Startup credit refresh should send Authorization headers");
        AssertEqual($"Bearer {AppliedKey}", headers["Authorization"], "Startup credit refresh should use the API Key snapshot");

        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(
            2,
            http.GetUrls.Count(url => url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)),
            "Each initialization cycle should request credit exactly once");
    }

    internal static async Task CompletedStartupCreditRefreshAppliesWhenSettingsViewModelIsCreatedAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings { ApiKey = AppliedKey };
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetAsyncHandler = (url, _, _) => Task.FromResult(
            url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
                ? "{\"credit\":\"23.45\"}"
                : "{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
        var plugin = new Main();

        plugin.Init(CreateContext(settings: settings, httpService: httpService), FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        var viewModel = plugin.GetOrCreateSettingsViewModel();

        AssertEqual("23.45", viewModel.UserCredit, "A completed startup credit result should be visible when settings first opens");
        AssertEqual("", viewModel.LatencyText, "Startup credit refresh should not display latency");
        AssertEqual(false, viewModel.IsLoadingCredit, "A completed startup credit result should not leave the refresh state busy");
    }

    internal static async Task PendingStartupCreditRefreshLocksSettingsUntilCompletionAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings { ApiKey = AppliedKey };
        var creditStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCredit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetAsyncHandler = (url, _, _) =>
        {
            if (url.Contains("/wallet/self/api-credit", StringComparison.Ordinal))
            {
                creditStarted.TrySetResult();
                return releaseCredit.Task;
            }

            return Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
        };
        var plugin = new Main();

        plugin.Init(CreateContext(settings: settings, httpService: httpService), FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        await creditStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(false, plugin.PendingStartupTask.IsCompleted, "Main.Init should return without waiting for startup credit refresh");

        var viewModel = plugin.GetOrCreateSettingsViewModel();
        AssertEqual(true, viewModel.IsLoadingCredit, "Pending startup credit refresh should mark credit loading");
        AssertEqual(true, viewModel.IsApiKeyInputLocked, "Pending startup credit refresh should lock API Key input");
        AssertEqual(false, viewModel.IsApiKeyInputEnabled, "Pending startup credit refresh should disable API Key input");
        AssertEqual(false, viewModel.PasteApiKeyCommand.CanExecute(null), "Pending startup credit refresh should disable API Key paste");
        AssertEqual(false, viewModel.RefreshCreditCommand.CanExecute(null), "Pending startup credit refresh should disable manual credit refresh");

        releaseCredit.SetResult("{\"credit\":\"34.56\"}");
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !viewModel.IsLoadingCredit, "startup credit attachment to complete");

        AssertEqual("34.56", viewModel.UserCredit, "Pending startup credit success should update the balance");
        AssertEqual(false, viewModel.IsApiKeyInputLocked, "Startup credit completion should release the API Key lock");
        AssertEqual(true, viewModel.RefreshCreditCommand.CanExecute(null), "Startup credit completion should re-enable manual refresh");
        AssertEqual("", viewModel.LatencyText, "Startup credit completion should not display latency");
    }

    internal static async Task FailedStartupCreditRefreshKeepsPlaceholderWithoutSnackbarAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings { ApiKey = AppliedKey };
        var snackbar = new TestSnackbar();
        var logger = new TestLogger();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetAsyncHandler = (url, _, _) =>
            url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
                ? Task.FromException<string>(new TimeoutException($"request failed for {AppliedKey}"))
                : Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
        var plugin = new Main();

        plugin.Init(
            CreateContext(snackbar, settings, httpService, logger: logger),
            FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        var viewModel = plugin.GetOrCreateSettingsViewModel();

        AssertEqual("", viewModel.UserCredit, "Failed startup credit refresh should keep the not-loaded placeholder state");
        AssertEqual("", viewModel.LatencyText, "Failed startup credit refresh should not display latency");
        AssertEqual(null, snackbar.LastError, "Failed startup credit refresh should not show a snackbar");
        AssertEqual(true, logger.Contains(LogLevel.Error, "Startup credit refresh failed"), "Failed startup credit refresh should be logged");
        AssertEqual(false, logger.Contains(AppliedKey), "Startup credit failure logs should not contain the API Key");
    }

    internal static async Task NullStartupCreditKeepsNotLoadedStateAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings { ApiKey = AppliedKey };
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetAsyncHandler = (url, _, _) => Task.FromResult(
            url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
                ? "{\"credit\":null}"
                : "{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
        var plugin = new Main();

        plugin.Init(CreateContext(settings: settings, httpService: httpService), FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        var viewModel = plugin.GetOrCreateSettingsViewModel();

        AssertEqual("", viewModel.UserCredit, "A null startup credit value should keep the not-loaded placeholder state");
    }

    internal static async Task StartupCreditRefreshIgnoresResultAfterApiKeyChangesAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings { ApiKey = AppliedKey };
        var releaseCredit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetAsyncHandler = (url, _, _) =>
            url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
                ? releaseCredit.Task
                : Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
        var plugin = new Main();
        plugin.Init(CreateContext(settings: settings, httpService: httpService), FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var viewModel = plugin.GetOrCreateSettingsViewModel();

        viewModel.ApiKey = DraftKey;
        releaseCredit.SetResult("{\"credit\":\"45.67\"}");
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !viewModel.IsLoadingCredit, "changed-key startup credit attachment to complete");

        AssertEqual("", viewModel.UserCredit, "Startup credit result should be ignored after the API Key changes");
        AssertEqual(false, viewModel.IsApiKeyInputLocked, "Ignored startup credit result should still release the API Key lock");
    }

    internal static async Task ReinitializedStartupCreditRefreshIgnoresOldCycleAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings { ApiKey = AppliedKey };
        var releaseOldCredit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (oldHttpService, oldHttp) = TestHttpServiceProxy.Create();
        oldHttp.GetAsyncHandler = (url, _, _) =>
            url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
                ? releaseOldCredit.Task
                : Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
        var plugin = new Main();
        plugin.Init(CreateContext(settings: settings, httpService: oldHttpService), FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var oldStartupTask = plugin.PendingStartupTask;
        var oldViewModel = plugin.GetOrCreateSettingsViewModel();

        var (newHttpService, newHttp) = TestHttpServiceProxy.Create();
        newHttp.GetAsyncHandler = (url, _, _) => Task.FromResult(
            url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
                ? "{\"credit\":\"56.78\"}"
                : "{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
        plugin.Init(CreateContext(settings: settings, httpService: newHttpService), FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        var newViewModel = plugin.GetOrCreateSettingsViewModel();
        AssertEqual(false, ReferenceEquals(oldViewModel, newViewModel), "Reinitialization should replace the old settings ViewModel");
        AssertEqual("56.78", newViewModel.UserCredit, "The current initialization cycle should apply its credit result");

        releaseOldCredit.SetResult("{\"credit\":\"99.99\"}");
        await oldStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !oldViewModel.IsLoadingCredit, "old startup credit attachment to release its lock");

        AssertEqual("56.78", newViewModel.UserCredit, "An old initialization cycle should not overwrite current credit");
        AssertEqual(false, oldViewModel.IsApiKeyInputLocked, "The old initialization cycle should release its counted lock");
        AssertEqual(false, newViewModel.IsApiKeyInputLocked, "The current initialization cycle should release its counted lock");
    }

    internal static async Task ReinitializedStartupCreditRefreshRemainsActiveDuringReloadAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings { ApiKey = AppliedKey };
        var releaseOldCredit = new TaskCompletionSource<string>();
        var (oldHttpService, oldHttp) = TestHttpServiceProxy.Create();
        oldHttp.GetAsyncHandler = (url, _, _) =>
            url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
                ? releaseOldCredit.Task
                : Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
        var plugin = new Main();
        plugin.Init(CreateContext(settings: settings, httpService: oldHttpService), FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var oldStartupTask = plugin.PendingStartupTask;
        var oldViewModel = plugin.GetOrCreateSettingsViewModel();

        var (newHttpService, newHttp) = TestHttpServiceProxy.Create();
        newHttp.GetAsyncHandler = (url, _, _) =>
            url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
                ? Task.FromException<string>(new TimeoutException("new startup credit failed"))
                : Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
        var newContext = CreateContext(settings: settings, httpService: newHttpService);
        ((ContextProxy)(object)newContext).OnLoad = () => releaseOldCredit.SetResult("{\"credit\":\"99.99\"}");

        plugin.Init(newContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        await oldStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        var newViewModel = plugin.GetOrCreateSettingsViewModel();
        await WaitUntilAsync(() => !oldViewModel.IsLoadingCredit, "old startup credit lock to release during reload");

        AssertEqual("99.99", oldViewModel.UserCredit, "The old initialization should remain active until the replacement settings finish loading");
        AssertEqual("", newViewModel.UserCredit, "The old credit result should not flow into the replacement initialization");
    }

    internal static async Task DisposedSettingsViewModelIgnoresStartupCreditResultAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings { ApiKey = AppliedKey };
        var releaseCredit = new TaskCompletionSource<string>();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetAsyncHandler = (url, _, cancellationToken) =>
        {
            if (!url.Contains("/wallet/self/api-credit", StringComparison.Ordinal))
                return Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");

            cancellationToken.Register(() => releaseCredit.SetResult("{\"credit\":\"67.89\"}"));
            return releaseCredit.Task;
        };
        var plugin = new Main();
        plugin.Init(CreateContext(settings: settings, httpService: httpService), FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var viewModel = plugin.GetOrCreateSettingsViewModel();

        plugin.Dispose();
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !viewModel.IsLoadingCredit, "disposed startup credit attachment to complete");

        AssertEqual("", viewModel.UserCredit, "Disposed settings ViewModel should ignore startup credit results");
        AssertEqual(false, viewModel.IsApiKeyInputLocked, "Disposed startup credit attachment should still release its counted lock");
    }

    internal static async Task PublishedReplacementInvalidatesOldStartupCreditImmediatelyAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var releaseOldCredit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePublicationWindow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (oldHttpService, oldHttp) = TestHttpServiceProxy.Create();
        oldHttp.GetAsyncHandler = (url, _, _) =>
            url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
                ? releaseOldCredit.Task
                : Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
        var plugin = new Main(
            (context, settings, settingsWriteLease, startupCreditRefreshCycle) =>
                new SettingsViewModel(
                    context,
                    settings,
                    clearCoverImageCacheAsync: null,
                    clearCoverImageCacheTimeout: null,
                    startupCreditRefreshCycle: startupCreditRefreshCycle,
                    settingsWriteLease: settingsWriteLease),
            initializationPublished: generation =>
            {
                if (generation != 2)
                    return;

                replacementPublished.TrySetResult();
                releasePublicationWindow.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            });
        plugin.Init(
            CreateContext(settings: new Settings { ApiKey = AppliedKey }, httpService: oldHttpService),
            FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var oldStartupTask = plugin.PendingStartupTask;
        var oldViewModel = plugin.GetOrCreateSettingsViewModel();

        var releaseNewCredit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (newHttpService, newHttp) = TestHttpServiceProxy.Create();
        newHttp.GetAsyncHandler = (url, _, _) =>
            url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
                ? releaseNewCredit.Task
                : Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
        var reinitializeTask = Task.Run(() =>
            plugin.Init(
                CreateContext(settings: new Settings { ApiKey = DraftKey }, httpService: newHttpService),
                FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1)));
        await replacementPublished.Task.WaitAsync(TimeSpan.FromSeconds(2));

        releaseOldCredit.SetResult("{\"credit\":\"99.99\"}");
        await oldStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !oldViewModel.IsLoadingCredit, "old startup credit observer to finish inside the publication window");
        var oldCreditDuringPublicationWindow = oldViewModel.UserCredit;

        releasePublicationWindow.SetResult();
        await reinitializeTask.WaitAsync(TimeSpan.FromSeconds(2));
        var replacementInitCompletedWithCreditPending = !plugin.PendingStartupTask.IsCompleted;
        var newViewModel = plugin.GetOrCreateSettingsViewModel();
        releaseNewCredit.SetResult("{\"credit\":\"12.34\"}");
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !newViewModel.IsLoadingCredit, "replacement startup credit observer to finish");

        AssertEqual("", oldCreditDuringPublicationWindow, "Publishing a replacement state should immediately invalidate the old startup credit result");
        AssertEqual(true, replacementInitCompletedWithCreditPending, "Replacement initialization should not wait for its startup credit request");
        AssertEqual("12.34", newViewModel.UserCredit, "The replacement startup credit result should remain active");
    }
}
