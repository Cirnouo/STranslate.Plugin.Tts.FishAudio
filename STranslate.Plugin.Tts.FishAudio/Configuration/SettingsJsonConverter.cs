using System.Text.Json;
using System.Text.Json.Serialization;

namespace STranslate.Plugin.Tts.FishAudio.Configuration;

public sealed class SettingsJsonConverter : JsonConverter<Settings>
{
    public override Settings Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return SettingsMigrator.Read(document.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, Settings value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(nameof(Settings.SchemaVersion), SettingsSchema.Current);
        writer.WriteString(nameof(Settings.ApiKey), value.ApiKey);
        writer.WriteString(nameof(Settings.VoiceId), value.VoiceId);
        writer.WriteString(nameof(Settings.SelectedModel), value.SelectedModel);
        writer.WriteNumber(nameof(Settings.Speed), value.Speed);
        writer.WriteNumber(nameof(Settings.Volume), value.Volume);
        writer.WriteBoolean(nameof(Settings.NormalizeLoudness), value.NormalizeLoudness);
        writer.WriteNumber(nameof(Settings.Temperature), value.Temperature);
        writer.WriteNumber(nameof(Settings.TopP), value.TopP);
        writer.WriteString(nameof(Settings.Latency), value.Latency);
        writer.WriteBoolean(nameof(Settings.Normalize), value.Normalize);
        writer.WriteNumber(nameof(Settings.Mp3Bitrate), value.Mp3Bitrate);
        writer.WriteBoolean(nameof(Settings.ConditionOnPreviousChunks), value.ConditionOnPreviousChunks);
        writer.WritePropertyName(nameof(Settings.CachedVoice));
        JsonSerializer.Serialize(writer, value.CachedVoice, options);
        writer.WriteBoolean(nameof(Settings.IsS21ProFreePromoDismissed), value.IsS21ProFreePromoDismissed);
        writer.WriteEndObject();
    }
}
