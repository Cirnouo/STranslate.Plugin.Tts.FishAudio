using Microsoft.Extensions.Logging;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static TestAssertions;

internal static class ApiKeyCompatibilitySuite
{
    // Synthetic fixture: eight five-character groups followed by three characters.
    private const string PrefixedKey = "sk-fish-Ab0_-Ab0_-Ab0_-Ab0_-Ab0_-Ab0_-Ab0_-Ab0_-Ab0";

    internal static void ApiKeyFormatAcceptsLegacyAndPrefixedKeys()
    {
        foreach (var key in new[] { AppliedKey, DraftKey, PrefixedKey, "sk-fish-" + new string('A', 43) })
        {
            AssertEqual(true, SettingsValidation.IsValidApiKeyFormat(key),
                "Both legacy and prefixed API Key formats should be accepted");
        }
    }

    internal static void ApiKeyFormatRejectsMalformedKeys()
    {
        string[] invalidKeys =
        [
            "", " ", "\t", "\r\n", "ABC",
            AppliedKey[..31], AppliedKey + "0", AppliedKey.ToUpperInvariant(),
            "sk-fish-", "sk-fish-" + new string('A', 42), "sk-fish-" + new string('A', 44),
            "SK-fish-" + new string('A', 43), "sk-FISH-" + new string('A', 43),
            "sk-other-" + new string('A', 43),
        ];
        foreach (var key in invalidKeys)
            AssertEqual(false, SettingsValidation.IsValidApiKeyFormat(key), "Malformed API Keys should be rejected");

        AssertEqual(false, SettingsValidation.IsValidApiKeyFormat(null!), "Null API Keys should be rejected safely");

        foreach (var key in new[] { AppliedKey, PrefixedKey })
        {
            foreach (var whitespace in new[] { " ", "\t", "\r", "\n", "\r\n" })
            {
                foreach (var malformed in new[] { whitespace + key, key + whitespace, key.Insert(10, whitespace) })
                    AssertEqual(false, SettingsValidation.IsValidApiKeyFormat(malformed),
                        "API Key matching should reject leading, trailing, and embedded whitespace");
            }
        }

        foreach (var invalidCharacter in new[] { '+', '/', '=', '.', '中' })
            AssertEqual(false, SettingsValidation.IsValidApiKeyFormat(PrefixedKey[..^1] + invalidCharacter),
                "Prefixed API Keys should allow only the specified suffix characters");
    }

    internal static void VoiceIdFormatRemainsLegacyOnly()
    {
        AssertEqual(true, SettingsValidation.IsValidVoiceIdFormat(AppliedKey), "32-hex Voice IDs should remain valid");
        AssertEqual(false, SettingsValidation.IsValidVoiceIdFormat(AppliedKey.ToUpperInvariant()),
            "Voice IDs should retain their lowercase requirement");
        AssertEqual(false, SettingsValidation.IsValidVoiceIdFormat(PrefixedKey),
            "Supporting prefixed API Keys should not broaden Voice ID validation");
    }

    internal static async Task RequestPathsPreservePreflightAndBearerAsync()
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
        using var plugin = new Main();
        SettingsViewModel? viewModel = null;
        try
        {
            if (path == RequestPath.Tts)
            {
                plugin.Init(context);
                await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
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
                    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
                    break;
            }

            var shouldRequest = expectedError is null;
            if (path == RequestPath.Tts)
            {
                AssertEqual(shouldRequest ? 1 : 0, http.PostAsBytesCallCount,
                    "TTS should send audio requests only after preflight passes");
                AssertEqual(shouldRequest ? 1 : 0, audio.PlayBytesCallCount,
                    "TTS should play audio only after preflight passes");
                AssertEqual(0, http.GetCallCount, "TTS should not introduce a separate credit validation request");
                if (shouldRequest)
                    AssertEqual($"Bearer {apiKey}", AssertHeaders(http.LastPostOptions, "TTS headers required")["Authorization"],
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
