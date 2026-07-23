using System.Text.Json;
using System.Text.RegularExpressions;

namespace STranslate.Plugin.Tts.FishAudio.Configuration;

public static class SettingsValidation
{
    private static readonly Regex HexId32Regex = new(@"^[0-9a-f]{32}$", RegexOptions.Compiled);

    public static bool IsValidApiKeyFormat(string key) => HexId32Regex.IsMatch(key);

    public static bool IsValidVoiceIdFormat(string id) => HexId32Regex.IsMatch(id);

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
