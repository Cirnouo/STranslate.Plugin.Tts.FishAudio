using Microsoft.Extensions.Logging;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace STranslate.Plugin.Tts.FishAudio;

internal static class FishAudioRuntime
{
    internal const string S21ProFreeModel = "s2.1-pro-free";
    internal const string S21ProModel = "s2.1-pro";
    internal const string S2ProModel = "s2-pro";
    internal const string S1Model = "s1";

    internal const string NetworkUnavailableKey = "STranslate_Plugin_Tts_FishAudio_Network_Unavailable";
    internal const string ApiKeyEmptyKey = "STranslate_Plugin_Tts_FishAudio_ApiKey_Empty";
    internal const string ApiKeyInvalidFormatKey = "STranslate_Plugin_Tts_FishAudio_ApiKey_InvalidFormat";
    internal const string RequestTimeoutKey = "STranslate_Plugin_Tts_FishAudio_Request_Timeout";
    internal const string PreviewUnavailableKey = "STranslate_Plugin_Tts_FishAudio_Preview_Unavailable";
    internal const string PreviewPlaybackFailedKey = "STranslate_Plugin_Tts_FishAudio_Preview_PlaybackFailed";
    internal const string PreviewRefreshFailedKey = "STranslate_Plugin_Tts_FishAudio_Preview_RefreshFailed";
    internal const string TimeApiUrl = "https://timeapi.io/api/Time/current/zone?timeZone=UTC";
    private static readonly TimeSpan TimeApiTimeout = TimeSpan.FromSeconds(3);

    internal static readonly DateTimeOffset FreeModelCutoffUtc = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    internal static Func<bool>? NetworkAvailableOverride { get; set; }
    internal static Func<DateTimeOffset>? LocalUtcNowOverride { get; set; }

    public static bool IsNetworkAvailable() =>
        NetworkAvailableOverride?.Invoke() ?? NetworkInterface.GetIsNetworkAvailable();

    internal static DateTimeOffset LocalUtcNow() => LocalUtcNowOverride?.Invoke() ?? DateTimeOffset.UtcNow;

    internal static IReadOnlyList<string> GetAvailableModels(DateTimeOffset nowUtc) =>
        IsS21ProFreeAvailable(nowUtc)
            ? [S21ProFreeModel, S21ProModel, S2ProModel, S1Model]
            : [S21ProModel, S2ProModel, S1Model];

    internal static bool IsS21ProFreeAvailable(DateTimeOffset nowUtc) =>
        nowUtc.ToUniversalTime() < FreeModelCutoffUtc;

    internal static string GetDefaultModel(DateTimeOffset nowUtc) =>
        IsS21ProFreeAvailable(nowUtc) ? S21ProFreeModel : S21ProModel;

    internal static bool SupportsNormalizeLoudness(string model) =>
        string.Equals(model, S21ProFreeModel, StringComparison.Ordinal)
        || string.Equals(model, S21ProModel, StringComparison.Ordinal)
        || string.Equals(model, S2ProModel, StringComparison.Ordinal);

    internal static async Task<DateTimeOffset?> TryGetOnlineUtcNowAsync(IPluginContext context, CancellationToken ct)
    {
        try
        {
            if (!IsNetworkAvailable())
            {
                context.Logger?.LogWarning("Online time check skipped: Network unavailable");
                return null;
            }

            var option = new Options { Timeout = TimeApiTimeout };
            var json = await context.HttpService.GetAsync(TimeApiUrl, option, ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("dateTime", out var dateTimeElement)
                && DateTimeOffset.TryParse(
                    dateTimeElement.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return parsed.ToUniversalTime();
            }

            context.Logger?.LogWarning("Online time check failed: dateTime field missing or invalid");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            context.Logger?.LogWarning(ex, "Online time check failed");
        }

        return null;
    }

    public static bool TryPreflightApiKey(
        IPluginContext context,
        string apiKey,
        string operation,
        bool showError,
        out string validApiKey)
    {
        validApiKey = "";

        if (!IsNetworkAvailable())
        {
            LogPreflightWarning(context, operation, "Network unavailable");
            ShowTranslatedError(context, showError, NetworkUnavailableKey);
            return false;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            LogPreflightWarning(context, operation, "API Key is empty");
            ShowTranslatedError(context, showError, ApiKeyEmptyKey);
            return false;
        }

        if (!SettingsValidation.IsValidApiKeyFormat(apiKey))
        {
            LogPreflightWarning(context, operation, "API Key format is invalid");
            ShowTranslatedError(context, showError, ApiKeyInvalidFormatKey);
            return false;
        }

        validApiKey = apiKey;
        return true;
    }

    public static string GetUserFacingError(IPluginContext context, Exception exception) =>
        IsTimeout(exception)
            ? context.GetTranslation(RequestTimeoutKey)
            : exception.Message;

    public static bool IsTimeout(Exception exception) =>
        exception is TimeoutException
        || exception is TaskCanceledException
        || exception.InnerException is not null && IsTimeout(exception.InnerException);

    public static void LogRequestFailure(IPluginContext context, string operation, Exception exception)
    {
        context.Logger?.LogError(exception, "{Operation} failed", operation);
    }

    private static void LogPreflightWarning(IPluginContext context, string operation, string reason)
    {
        context.Logger?.LogWarning("{Operation} preflight failed: {Reason}", operation, reason);
    }

    private static void ShowTranslatedError(IPluginContext context, bool showError, string key)
    {
        if (showError)
            context.Snackbar.ShowError(context.GetTranslation(key));
    }
}
