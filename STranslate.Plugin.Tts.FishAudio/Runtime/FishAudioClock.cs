using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace STranslate.Plugin.Tts.FishAudio.Runtime;

internal static class FishAudioClock
{
    internal const string TimeApiUrl = "https://timeapi.io/api/Time/current/zone?timeZone=UTC";
    private static readonly TimeSpan TimeApiTimeout = TimeSpan.FromSeconds(3);

    internal static Func<DateTimeOffset>? LocalUtcNowOverride { get; set; }

    internal static DateTimeOffset LocalUtcNow() => LocalUtcNowOverride?.Invoke() ?? DateTimeOffset.UtcNow;

    internal static async Task<DateTimeOffset?> TryGetOnlineUtcNowAsync(
        IPluginContext context,
        Func<bool> isNetworkAvailable,
        CancellationToken ct)
    {
        try
        {
            if (!isNetworkAvailable())
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
}
