using NUnit.Framework;
using Microsoft.Extensions.Logging;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static TestAssertions;

[TestFixture]
[NonParallelizable]
internal static class ApiKeyCompatibilitySuite
{
    // Synthetic fixture: eight five-character groups followed by three characters.
    private const string PrefixedKey = "sk-fish-Ab0_-Ab0_-Ab0_-Ab0_-Ab0_-Ab0_-Ab0_-Ab0_-Ab0";

    [Test]
    [Property("FishAudioCaseId", "settings.api-key/request-paths-preserve-preflight-and-bearer")]
    [Order(57)]
    public static async Task RequestPathsPreservePreflightAndBearerAsync()
    {
        (string Key, bool Online, string? Error)[] cases =
        [
            (AppliedKey, true, null),
            (PrefixedKey, true, null),
            (AppliedKey, false, FishAudioRequestPolicy.NetworkUnavailableKey),
            (PrefixedKey, false, FishAudioRequestPolicy.NetworkUnavailableKey),
            ("", false, FishAudioRequestPolicy.NetworkUnavailableKey),
            ("", true, FishAudioRequestPolicy.ApiKeyEmptyKey),
            (" \t", true, FishAudioRequestPolicy.ApiKeyEmptyKey),
            ("ABC", true, FishAudioRequestPolicy.ApiKeyInvalidFormatKey),
            (PrefixedKey[..^1], true, FishAudioRequestPolicy.ApiKeyInvalidFormatKey),
            (PrefixedKey + "\n", true, FishAudioRequestPolicy.ApiKeyInvalidFormatKey),
        ];

        foreach (var path in Enum.GetValues<RequestPath>())
        {
            foreach (var testCase in cases)
                await VerifyRequestPathAsync(path, testCase.Key, testCase.Online, testCase.Error);
        }
    }

    private static async Task VerifyRequestPathAsync(RequestPath path, string apiKey, bool online, string? expectedError)
    {
        using var network = OverrideNetworkAvailability(false);
        var settings = CreateTtsSettings("s2-pro");
        settings.ApiKey = apiKey;
        var snackbar = new TestSnackbar();
        var logger = new TestLogger();
        var audio = new TestAudioPlayer();
        var (httpService, http) = TestHttpServiceProxy.Create();
        var context = CreateContext(snackbar, settings, httpService, audio, logger);
        var session = new StreamingSessionDouble();
        using var plugin = StreamingEntrySuite.CreatePlugin(() => session);
        SettingsViewModel? viewModel = null;
        try
        {
            if (path == RequestPath.Tts)
            {
                plugin.Init(context);
                await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
            }
            else if (path != RequestPath.StartupCredit)
            {
                viewModel = new SettingsViewModel(context, settings);
                http.GetAsyncHandler = (_, _, _) =>
                {
                    AssertEqual(true, viewModel.IsApiKeyInputLocked,
                        $"{path} should lock API Key input before sending the request");
                    return Task.FromResult("{\"credit\":\"1.00\"}");
                };
            }

            network.Set(online);
            switch (path)
            {
                case RequestPath.Tts:
                    await plugin.PlayAudioAsync("hello");
                    break;
                case RequestPath.ManualCredit:
                    await viewModel!.RefreshCreditCommand.ExecuteAsync(null);
                    break;
                case RequestPath.SilentCredit:
                    await viewModel!.RefreshCreditSilentlyAsync();
                    break;
                case RequestPath.StartupCredit:
                    plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
                    await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
                    break;
            }

            var shouldRequest = expectedError is null;
            if (path == RequestPath.Tts)
            {
                AssertEqual(shouldRequest ? 1 : 0, session.Calls,
                    "TTS should start a native streaming session only after preflight passes");
                AssertEqual(0, http.PostAsBytesCallCount, "TTS should not download whole-buffer HTTP audio");
                AssertEqual(0, audio.PlayBytesCallCount, "TTS should not use host byte-array playback");
                AssertEqual(0, http.GetCallCount, "TTS should not introduce a separate credit validation request");
                if (shouldRequest)
                    AssertEqual(apiKey, session.Request!.ApiKey,
                        "TTS should forward the complete API Key unchanged");
            }
            else
            {
                var creditRequests = http.GetOptionsByUrl.Where(pair =>
                    pair.Url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)).ToArray();
                AssertEqual(shouldRequest ? 1 : 0, creditRequests.Length,
                    $"{path} should request credit only after preflight passes");
                if (shouldRequest)
                    AssertEqual($"Bearer {apiKey}", AssertHeaders(creditRequests.Single().Options, "Credit headers required")["Authorization"],
                        $"{path} should forward the complete API Key unchanged");
                if (viewModel is not null)
                {
                    AssertEqual(shouldRequest ? "1.00" : "", viewModel.UserCredit, "Credit display should reflect successful requests");
                    AssertEqual(false, viewModel.IsApiKeyInputLocked, "Credit completion should release the API Key input lock");
                }
            }

            var interactive = path is RequestPath.Tts or RequestPath.ManualCredit;
            AssertEqual(interactive ? expectedError : null, snackbar.LastError,
                $"{path} should preserve its interactive or silent error policy");
            if (!shouldRequest)
                AssertEqual(true, logger.Count(LogLevel.Warning) > 0, "Preflight failures should remain logged");
            if (!string.IsNullOrWhiteSpace(apiKey))
                AssertEqual(false, logger.Contains(apiKey), "Preflight logs should not disclose the API Key");
            AssertEqual(apiKey, settings.ApiKey, "Preflight should not modify the configured API Key");
        }
        finally
        {
            viewModel?.Dispose();
        }
    }

    private enum RequestPath
    {
        Tts,
        ManualCredit,
        SilentCredit,
        StartupCredit,
    }
}
