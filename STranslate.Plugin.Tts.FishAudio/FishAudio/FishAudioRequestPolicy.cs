using Microsoft.Extensions.Logging;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using System.Net.NetworkInformation;

namespace STranslate.Plugin.Tts.FishAudio.FishAudio;

internal static class FishAudioRequestPolicy
{
    internal const string NetworkUnavailableKey = "STranslate_Plugin_Tts_FishAudio_Network_Unavailable";
    internal const string ApiKeyEmptyKey = "STranslate_Plugin_Tts_FishAudio_ApiKey_Empty";
    internal const string ApiKeyInvalidFormatKey = "STranslate_Plugin_Tts_FishAudio_ApiKey_InvalidFormat";
    internal const string RequestTimeoutKey = "STranslate_Plugin_Tts_FishAudio_Request_Timeout";
    internal static Func<bool>? NetworkAvailableOverride { get; set; }

    internal static bool IsNetworkAvailable() =>
        NetworkAvailableOverride?.Invoke() ?? NetworkInterface.GetIsNetworkAvailable();

    internal static bool TryPreflightApiKey(
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

    internal static string GetUserFacingError(IPluginContext context, Exception exception) =>
        IsTimeout(exception)
            ? context.GetTranslation(RequestTimeoutKey)
            : exception.Message;

    internal static bool IsTimeout(Exception exception) =>
        exception is TimeoutException
        || exception is TaskCanceledException
        || exception.InnerException is not null && IsTimeout(exception.InnerException);

    internal static void LogRequestFailure(IPluginContext context, string operation, Exception exception)
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
