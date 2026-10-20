using System.Text.Json;
using System.Text.RegularExpressions;

namespace STranslate.Plugin.Tts.FishAudio.Configuration;

public static class SettingsValidation
{
    private static readonly Regex ApiKeyRegex = new(@"\A(?:[0-9a-f]{32}|sk-fish-[A-Za-z0-9_-]{43})\z", RegexOptions.Compiled);
    private static readonly Regex HexId32Regex = new(@"\A[0-9a-f]{32}\z", RegexOptions.Compiled);

    public static bool IsValidApiKeyFormat(string key) => !string.IsNullOrEmpty(key) && ApiKeyRegex.IsMatch(key);

    public static bool IsValidVoiceIdFormat(string id) => HexId32Regex.IsMatch(id);

    public static bool IsValidStreamingProxyConfiguration(string? mode, string? address) =>
        mode is "system" or "direct"
        || (mode == "custom" && IsValidStreamingProxyAddress(address));

    public static bool IsValidStreamingProxyAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)
            || address.Any(char.IsWhiteSpace)
            || address.Contains('\\')
            || address.Contains('?')
            || address.Contains('#')
            || !Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https" or "socks5")
            || string.IsNullOrEmpty(uri.Host)
            || uri.AbsolutePath is not ("" or "/")
            || uri.Port is < 1 or > 65535)
            return false;

        // Uri.Port supplies a default for HTTP(S); inspect the original authority
        // as well so an omitted port is never mistaken for an explicit one.
        var schemeEnd = address.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
            return false;
        var authorityStart = schemeEnd + 3;
        var authorityEnd = address.IndexOf('/', authorityStart);
        if (authorityEnd >= 0 && authorityEnd != address.Length - 1)
            return false;
        var authority = address.AsSpan(authorityStart, (authorityEnd < 0 ? address.Length : authorityEnd) - authorityStart);
        var portSeparator = authority.LastIndexOf(':');
        if (portSeparator < 0)
            return false;
        var portText = authority[(portSeparator + 1)..];
        return !portText.IsEmpty && portText.IndexOfAnyExceptInRange('0', '9') < 0
            && int.TryParse(portText, out var port) && port is >= 1 and <= 65535;
    }

    internal static bool TryGetString(JsonElement element, out string value)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString()!;
            return true;
        }

        value = "";
        return false;
    }

    internal static bool TryGetNullableString(JsonElement element, out string? value)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            value = null;
            return true;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString();
            return true;
        }

        value = null;
        return false;
    }

    internal static bool TryGetBoolean(JsonElement element, out bool value)
    {
        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = element.GetBoolean();
            return true;
        }

        value = default;
        return false;
    }

    internal static bool TryGetDouble(JsonElement element, out double value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value))
            return true;

        value = default;
        return false;
    }

    internal static bool TryGetInt32(JsonElement element, out int value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
            return true;

        value = default;
        return false;
    }
}
