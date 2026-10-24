using System.Text.Json;
using System.Text.Json.Serialization;

namespace STranslate.Plugin.Tts.FishAudio.Configuration;

internal static class VoiceCreationDate
{
    internal static bool TryRead(JsonElement element, out DateTimeOffset? value)
    {
        value = null;
        if (element.ValueKind == JsonValueKind.Null)
            return true;
        if (element.ValueKind != JsonValueKind.String)
            return false;
        var text = element.GetString()!;
        // An explicit offset is required; an offset-less timestamp must never
        // acquire this machine's local zone during JSON deserialization.
        if (text.Length < 20 || (text[^1] != 'Z'
            && (text.Length < 6 || text[^6] is not ('+' or '-') || text[^3] != ':'))
            || !element.TryGetDateTimeOffset(out var parsed))
            return false;
        value = parsed.ToUniversalTime();
        return true;
    }
}

internal sealed class VoiceCreationDateJsonConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // ParseValue consumes the complete malformed metadata value, including
        // arrays/objects, while still rejecting truncated or invalid whole JSON.
        using var document = JsonDocument.ParseValue(ref reader);
        VoiceCreationDate.TryRead(document.RootElement, out var value);
        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is { } date) writer.WriteStringValue(date.ToUniversalTime());
        else writer.WriteNullValue();
    }
}
