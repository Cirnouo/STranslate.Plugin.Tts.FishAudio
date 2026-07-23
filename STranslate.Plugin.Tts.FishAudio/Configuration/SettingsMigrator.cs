using System.Text.Json;

namespace STranslate.Plugin.Tts.FishAudio.Configuration;

internal static class SettingsMigrator
{
    private static readonly HashSet<string> KnownSettingsProperties =
    [
        "SchemaVersion",
        "ApiKey",
        "VoiceId",
        "ReferenceId",
        "SelectedModel",
        "Speed",
        "Volume",
        "NormalizeLoudness",
        "Temperature",
        "TopP",
        "Latency",
        "Normalize",
        "Mp3Bitrate",
        "ConditionOnPreviousChunks",
        "CachedVoice",
        "CachedModel",
        "IsS21ProFreePromoDismissed",
    ];

    private static readonly HashSet<string> KnownCachedVoiceProperties =
    [
        "Title",
        "Description",
        "CoverImage",
        "AuthorName",
        "TaskCount",
        "SampleAudioUrl",
    ];

    internal static Settings Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Fish Audio settings must be a JSON object.");

        var properties = CollectProperties(root);
        var settings = new Settings();
        ReadSchema(properties, settings);

        if (properties.Keys.Any(name => !KnownSettingsProperties.Contains(name)))
            settings.NeedsCanonicalSave = true;

        ReadString(properties, "ApiKey", value => settings.ApiKey = value, settings);
        ReadVoiceId(properties, settings);
        ReadString(properties, "SelectedModel", value => settings.SelectedModel = value, settings);
        ReadDouble(properties, "Speed", value => settings.Speed = value, settings);
        ReadDouble(properties, "Volume", value => settings.Volume = value, settings);
        ReadBoolean(properties, "NormalizeLoudness", value => settings.NormalizeLoudness = value, settings);
        ReadDouble(properties, "Temperature", value => settings.Temperature = value, settings);
        ReadDouble(properties, "TopP", value => settings.TopP = value, settings);
        ReadString(properties, "Latency", value => settings.Latency = value, settings);
        ReadBoolean(properties, "Normalize", value => settings.Normalize = value, settings);
        ReadInt32(properties, "Mp3Bitrate", value => settings.Mp3Bitrate = value, settings);
        ReadBoolean(properties, "ConditionOnPreviousChunks", value => settings.ConditionOnPreviousChunks = value, settings);
        ReadCachedVoice(properties, settings);
        ReadBoolean(properties, "IsS21ProFreePromoDismissed", value => settings.IsS21ProFreePromoDismissed = value, settings);

        if (settings.SchemaVersion <= SettingsSchema.Legacy && !settings.IsReadOnly)
        {
            settings.SchemaVersion = SettingsSchema.Current;
            settings.NeedsCanonicalSave = true;
        }

        return settings;
    }

    private static Dictionary<string, List<JsonElement>> CollectProperties(JsonElement element)
    {
        var properties = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!properties.TryGetValue(property.Name, out var values))
            {
                values = [];
                properties.Add(property.Name, values);
            }

            values.Add(property.Value.Clone());
        }

        return properties;
    }

    private static void ReadSchema(Dictionary<string, List<JsonElement>> properties, Settings settings)
    {
        if (!properties.TryGetValue("SchemaVersion", out var values))
        {
            settings.SchemaVersion = SettingsSchema.Legacy;
            settings.NeedsCanonicalSave = true;
            return;
        }

        if (values.Count != 1)
        {
            settings.IsReadOnly = true;
            settings.ReadOnlyReason = "SchemaVersion is duplicated";
            if (values.Count > 0 && SettingsValidation.TryGetInt32(values[0], out var duplicateVersion))
                settings.SchemaVersion = duplicateVersion;
            return;
        }

        if (!SettingsValidation.TryGetInt32(values[0], out var schemaVersion) || schemaVersion < SettingsSchema.Legacy)
        {
            settings.SchemaVersion = SettingsSchema.Legacy;
            settings.NeedsCanonicalSave = true;
            return;
        }

        settings.SchemaVersion = schemaVersion;
        if (schemaVersion > SettingsSchema.Current)
        {
            settings.IsReadOnly = true;
            settings.ReadOnlyReason = $"SchemaVersion {schemaVersion} is newer than supported version {SettingsSchema.Current}";
        }
    }

    private static void ReadVoiceId(Dictionary<string, List<JsonElement>> properties, Settings settings)
    {
        var currentState = ReadCandidate(properties, "VoiceId", SettingsValidation.TryGetString, out string current, settings);
        if (currentState == CandidateState.Valid)
        {
            settings.VoiceId = current;
            MarkLegacyPropertyForCleanup(properties, "ReferenceId", settings);
            return;
        }

        if (currentState == CandidateState.Duplicate)
        {
            MarkLegacyPropertyForCleanup(properties, "ReferenceId", settings);
            return;
        }

        var legacyState = ReadCandidate(properties, "ReferenceId", SettingsValidation.TryGetString, out string legacy, settings);
        if (legacyState == CandidateState.Valid)
            settings.VoiceId = legacy;
    }

    private static void ReadCachedVoice(Dictionary<string, List<JsonElement>> properties, Settings settings)
    {
        var currentState = ReadCachedVoiceCandidate(properties, "CachedVoice", out var current, settings);
        if (currentState == CandidateState.Valid)
        {
            settings.CachedVoice = current;
            MarkLegacyPropertyForCleanup(properties, "CachedModel", settings);
            return;
        }

        if (currentState == CandidateState.Duplicate)
        {
            MarkLegacyPropertyForCleanup(properties, "CachedModel", settings);
            return;
        }

        var legacyState = ReadCachedVoiceCandidate(properties, "CachedModel", out var legacy, settings);
        if (legacyState == CandidateState.Valid)
            settings.CachedVoice = legacy;
    }

    private static CandidateState ReadCachedVoiceCandidate(
        Dictionary<string, List<JsonElement>> properties,
        string name,
        out CachedVoiceInfo? value,
        Settings settings)
    {
        value = null;
        if (!properties.TryGetValue(name, out var values))
            return CandidateState.Missing;

        settings.NeedsCanonicalSave |= name == "CachedModel";
        if (values.Count != 1)
        {
            settings.NeedsCanonicalSave = true;
            return CandidateState.Duplicate;
        }

        var element = values[0];
        if (element.ValueKind == JsonValueKind.Null)
            return CandidateState.Valid;

        if (element.ValueKind != JsonValueKind.Object)
        {
            settings.NeedsCanonicalSave = true;
            return CandidateState.Invalid;
        }

        value = ReadCachedVoiceObject(element, settings);
        return CandidateState.Valid;
    }

    private static CachedVoiceInfo ReadCachedVoiceObject(JsonElement element, Settings settings)
    {
        var properties = CollectProperties(element);
        var cached = new CachedVoiceInfo();
        if (properties.Keys.Any(name => !KnownCachedVoiceProperties.Contains(name)))
            settings.NeedsCanonicalSave = true;

        ReadString(properties, "Title", value => cached.Title = value, settings);
        ReadString(properties, "Description", value => cached.Description = value, settings);
        ReadString(properties, "CoverImage", value => cached.CoverImage = value, settings);
        ReadString(properties, "AuthorName", value => cached.AuthorName = value, settings);
        ReadInt32(properties, "TaskCount", value => cached.TaskCount = value, settings);
        ReadNullableString(properties, "SampleAudioUrl", value => cached.SampleAudioUrl = value, settings);
        return cached;
    }

    private static void ReadString(
        Dictionary<string, List<JsonElement>> properties,
        string name,
        Action<string> assign,
        Settings settings)
    {
        if (ReadCandidate(properties, name, SettingsValidation.TryGetString, out string value, settings) == CandidateState.Valid)
            assign(value);
    }

    private static void ReadNullableString(
        Dictionary<string, List<JsonElement>> properties,
        string name,
        Action<string?> assign,
        Settings settings)
    {
        if (ReadCandidate(properties, name, SettingsValidation.TryGetNullableString, out string? value, settings) == CandidateState.Valid)
            assign(value);
    }

    private static void ReadBoolean(
        Dictionary<string, List<JsonElement>> properties,
        string name,
        Action<bool> assign,
        Settings settings)
    {
        if (ReadCandidate(properties, name, SettingsValidation.TryGetBoolean, out bool value, settings) == CandidateState.Valid)
            assign(value);
    }

    private static void ReadDouble(
        Dictionary<string, List<JsonElement>> properties,
        string name,
        Action<double> assign,
        Settings settings)
    {
        if (ReadCandidate(properties, name, SettingsValidation.TryGetDouble, out double value, settings) == CandidateState.Valid)
            assign(value);
    }

    private static void ReadInt32(
        Dictionary<string, List<JsonElement>> properties,
        string name,
        Action<int> assign,
        Settings settings)
    {
        if (ReadCandidate(properties, name, SettingsValidation.TryGetInt32, out int value, settings) == CandidateState.Valid)
            assign(value);
    }

    private static CandidateState ReadCandidate<T>(
        Dictionary<string, List<JsonElement>> properties,
        string name,
        TryReadValue<T> tryRead,
        out T value,
        Settings settings)
    {
        value = default!;
        if (!properties.TryGetValue(name, out var values))
            return CandidateState.Missing;

        if (name is "ReferenceId")
            settings.NeedsCanonicalSave = true;

        if (values.Count != 1)
        {
            settings.NeedsCanonicalSave = true;
            return CandidateState.Duplicate;
        }

        if (!tryRead(values[0], out value))
        {
            settings.NeedsCanonicalSave = true;
            return CandidateState.Invalid;
        }

        return CandidateState.Valid;
    }

    private static void MarkLegacyPropertyForCleanup(
        Dictionary<string, List<JsonElement>> properties,
        string name,
        Settings settings)
    {
        if (properties.ContainsKey(name))
            settings.NeedsCanonicalSave = true;
    }

    private delegate bool TryReadValue<T>(JsonElement element, out T value);

    private enum CandidateState
    {
        Missing,
        Valid,
        Invalid,
        Duplicate,
    }
}
