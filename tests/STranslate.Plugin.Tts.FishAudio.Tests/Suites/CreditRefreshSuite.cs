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

internal static class CreditRefreshSuite
{
    internal static async Task ManualCreditRefreshUsesPreflightAndLocksApiKeyInputAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var snackbar = new TestSnackbar();
        var logger = new TestLogger();
        var (httpService, http) = TestHttpServiceProxy.Create();
        var settings = new Settings { ApiKey = AppliedKey };
        var viewModel = new SettingsViewModel(
            CreateContext(snackbar, settings, httpService, logger: logger),
            settings);

        await viewModel.RefreshCreditCommand.ExecuteAsync(null);
        AssertEqual(
            "STranslate_Plugin_Tts_FishAudio_Network_Unavailable",
            snackbar.LastError,
            "Manual credit refresh should use the shared offline preflight");
        AssertEqual(0, http.GetCallCount, "Manual credit refresh should not call the API when offline");
        AssertEqual(true, logger.Contains(LogLevel.Warning, "Network unavailable"), "Offline manual refresh should be logged");

        network.Set(true);
        snackbar.Clear();
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        http.GetAsyncHandler = (_, _, _) =>
        {
            requestStarted.SetResult();
            return releaseRequest.Task;
        };

        var commandTask = viewModel.RefreshCreditCommand.ExecuteAsync(null);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(true, viewModel.IsApiKeyInputLocked, "Manual credit refresh should lock API Key input while the request is running");
        AssertEqual(false, viewModel.PasteApiKeyCommand.CanExecute(null), "Paste API Key should be disabled while API Key input is locked");

        releaseRequest.SetResult("{\"credit\":\"4.56\"}");
        await commandTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(false, viewModel.IsApiKeyInputLocked, "Manual credit refresh should unlock API Key input after the request completes");
        AssertEqual(true, viewModel.PasteApiKeyCommand.CanExecute(null), "Paste API Key should be enabled after API Key input unlocks");
    }

    internal static async Task ManualCreditRefreshSuccessShowsBalanceAndLatencyAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings { ApiKey = AppliedKey };
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = "{\"credit\":\"7.89\"}";
        var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings);

        await viewModel.RefreshCreditCommand.ExecuteAsync(null);

        AssertEqual("7.89", viewModel.UserCredit, "Manual credit refresh should update credit balance");
        AssertEqual(true, viewModel.LatencyText.EndsWith(" ms", StringComparison.Ordinal), "Manual credit refresh should show latency text");
        AssertNotNull(viewModel.LatencyBrush, "Manual credit refresh should color the latency text");
        AssertEqual(TimeSpan.FromSeconds(15), http.LastGetOptions?.Timeout, "Credit refresh should set an explicit request timeout");
    }

    internal static async Task ManualCreditRefreshTimeoutShowsLocalizedErrorAndLogsAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings { ApiKey = AppliedKey };
        var snackbar = new TestSnackbar();
        var logger = new TestLogger();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetException = new TimeoutException("request timed out");
        var viewModel = new SettingsViewModel(
            CreateContext(snackbar, settings, httpService, logger: logger),
            settings);

        await viewModel.RefreshCreditCommand.ExecuteAsync(null);

        AssertEqual(
            "STranslate_Plugin_Tts_FishAudio_Request_Timeout",
            snackbar.LastError,
            "Manual credit refresh timeout should show a localized timeout error");
        AssertEqual("", viewModel.LatencyText, "Manual credit refresh timeout should clear latency text");
        AssertEqual(false, viewModel.IsApiKeyInputLocked, "Manual credit refresh timeout should unlock API Key input");
        AssertEqual(true, logger.Contains(LogLevel.Error, "Credit refresh failed"), "Manual credit refresh timeout should be logged");
    }

    internal static async Task SilentCreditRefreshPreflightAndTimeoutOnlyLogAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var settings = new Settings { ApiKey = AppliedKey };
        var snackbar = new TestSnackbar();
        var logger = new TestLogger();
        var (httpService, http) = TestHttpServiceProxy.Create();
        var viewModel = new SettingsViewModel(
            CreateContext(snackbar, settings, httpService, logger: logger),
            settings);

        await viewModel.RefreshCreditSilentlyAsync();

        AssertEqual(null, snackbar.LastError, "Silent credit refresh preflight failure should not show a snackbar");
        AssertEqual(0, http.GetCallCount, "Silent credit refresh should not call the API when offline");
        AssertEqual(true, logger.Contains(LogLevel.Warning, "Network unavailable"), "Silent offline refresh should be logged");

        network.Set(true);
        http.GetException = new TimeoutException("request timed out");
        await viewModel.RefreshCreditSilentlyAsync();

        AssertEqual(null, snackbar.LastError, "Silent credit refresh timeout should not show a snackbar");
        AssertEqual(true, logger.Contains(LogLevel.Error, "Credit refresh failed"), "Silent credit refresh timeout should be logged");
    }
}
