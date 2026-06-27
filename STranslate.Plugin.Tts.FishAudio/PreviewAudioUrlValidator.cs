namespace STranslate.Plugin.Tts.FishAudio;

internal static class PreviewAudioUrlValidator
{
    private const string PlatformHost = "platform.r2.fish.audio";
    private const string CloudflareStorageSuffix = ".r2.cloudflarestorage.com";

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

    private static bool IsAllowedHost(string host) =>
        string.Equals(host, PlatformHost, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(CloudflareStorageSuffix, StringComparison.OrdinalIgnoreCase)
            && host.Length > CloudflareStorageSuffix.Length;
}
