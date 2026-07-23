using System.Globalization;

namespace STranslate.Plugin.Tts.FishAudio;

internal static class PreviewAudioUrlValidator
{
    private const string PlatformHost = "platform.r2.fish.audio";
    private const string CloudflareStorageSuffix = ".r2.cloudflarestorage.com";
    private static readonly TimeSpan RefreshLeadTime = TimeSpan.FromSeconds(30);

    public static bool TryCreateAllowedUri(string? url, out Uri? uri)
    {
        uri = null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var candidate))
            return false;

        if (!string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!IsAllowedHost(candidate.Host))
            return false;

        uri = candidate;
        return true;
    }

    public static bool RequiresRefresh(string? url, DateTimeOffset nowUtc)
    {
        if (!TryCreateAllowedUri(url, out var uri))
            return false;

        string? signedAtValue = null;
        string? expiresValue = null;
        var signedAtCount = 0;
        var expiresCount = 0;
        var hasSigningMarker = false;

        foreach (var parameter in uri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = parameter.IndexOf('=');
            var name = separatorIndex >= 0 ? parameter[..separatorIndex] : parameter;
            var value = separatorIndex >= 0 ? parameter[(separatorIndex + 1)..] : "";
            hasSigningMarker |= name.StartsWith("X-Amz-", StringComparison.OrdinalIgnoreCase);

            if (string.Equals(name, "X-Amz-Date", StringComparison.OrdinalIgnoreCase))
            {
                signedAtCount++;
                signedAtValue = value;
            }
            else if (string.Equals(name, "X-Amz-Expires", StringComparison.OrdinalIgnoreCase))
            {
                expiresCount++;
                expiresValue = value;
            }
        }

        if (signedAtCount == 0 && expiresCount == 0)
            return hasSigningMarker;

        if (signedAtCount != 1
            || expiresCount != 1
            || !DateTimeOffset.TryParseExact(
                signedAtValue,
                "yyyyMMdd'T'HHmmss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var signedAt)
            || !long.TryParse(expiresValue, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresSeconds)
            || expiresSeconds < 0)
        {
            return true;
        }

        try
        {
            var refreshAt = signedAt.AddSeconds(expiresSeconds) - RefreshLeadTime;
            return nowUtc.ToUniversalTime() >= refreshAt;
        }
        catch (ArgumentOutOfRangeException)
        {
            return true;
        }
    }

    private static bool IsAllowedHost(string host) =>
        string.Equals(host, PlatformHost, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(CloudflareStorageSuffix, StringComparison.OrdinalIgnoreCase)
            && host.Length > CloudflareStorageSuffix.Length;
}
