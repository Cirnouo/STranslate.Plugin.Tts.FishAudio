using Microsoft.Extensions.Logging;

namespace STranslate.Plugin.Tts.FishAudio.Configuration;

public static class SettingsStore
{
    private static readonly object Gate = new();
    private static long _lastWriteLease;

    public static Settings Load(IPluginContext context, DateTimeOffset nowUtc)
    {
        lock (Gate)
            return LoadCore(context, nowUtc);
    }

    internal static Settings Load(
        IPluginContext context,
        DateTimeOffset nowUtc,
        out SettingsWriteLease writeLease)
    {
        lock (Gate)
        {
            var storageSettings = context.LoadSettingStorage<Settings>() ?? new Settings();
            var settings = Clone(storageSettings);
            if (SettingsNormalizer.Normalize(settings, nowUtc))
                settings.NeedsCanonicalSave = true;

            writeLease = new SettingsWriteLease(NextWriteLease(), storageSettings);
            settings.ActiveWriteLease = writeLease.Value;
            return settings;
        }
    }

    private static Settings LoadCore(IPluginContext context, DateTimeOffset nowUtc)
    {
        var settings = context.LoadSettingStorage<Settings>() ?? new Settings();
        if (SettingsNormalizer.Normalize(settings, nowUtc))
            settings.NeedsCanonicalSave = true;

        if (settings.NeedsCanonicalSave)
            SaveCore(context, settings);

        return settings;
    }

    public static void Save(IPluginContext context, Settings settings)
    {
        lock (Gate)
        {
            if (settings.ActiveWriteLease != 0)
            {
                context.Logger?.LogDebug(
                    "Fish Audio settings save skipped because a write lease is required");
                return;
            }

            SaveCore(context, settings);
        }
    }

    internal static bool Save(
        IPluginContext context,
        Settings settings,
        SettingsWriteLease writeLease)
    {
        lock (Gate)
        {
            if (!writeLease.IsValid || settings.ActiveWriteLease != writeLease.Value)
            {
                context.Logger?.LogDebug(
                    "Fish Audio settings save skipped because its write lease is no longer active");
                return false;
            }

            var storageSettings = writeLease.StorageSettings!;
            var storageSnapshot = Clone(storageSettings);
            Copy(settings, storageSettings);
            try
            {
                SaveCore(context, settings);
                storageSettings.NeedsCanonicalSave = settings.NeedsCanonicalSave;
            }
            catch
            {
                Copy(storageSnapshot, storageSettings);
                throw;
            }

            return true;
        }
    }

    internal static void Commit(
        IPluginContext context,
        Settings settings,
        SettingsWriteLease writeLease)
    {
        lock (Gate)
        {
            if (!writeLease.IsValid || settings.ActiveWriteLease != writeLease.Value)
                throw new InvalidOperationException("The Fish Audio settings write lease is no longer active.");

            var storageSettings = writeLease.StorageSettings!;
            var storageSnapshot = Clone(storageSettings);
            Copy(settings, storageSettings);
            try
            {
                if (settings.NeedsCanonicalSave)
                    SaveCore(context, settings);
                storageSettings.NeedsCanonicalSave = settings.NeedsCanonicalSave;
            }
            catch
            {
                Copy(storageSnapshot, storageSettings);
                throw;
            }
        }
    }

    internal static bool TryUpdate(
        Settings settings,
        SettingsWriteLease writeLease,
        Action update)
    {
        lock (Gate)
        {
            if (!writeLease.IsValid || settings.ActiveWriteLease != writeLease.Value)
                return false;

            update();
            return true;
        }
    }

    internal static bool TryUpdateAndSave(
        IPluginContext context,
        Settings settings,
        SettingsWriteLease writeLease,
        Func<Settings, bool> update)
    {
        lock (Gate)
        {
            if (!writeLease.IsValid || settings.ActiveWriteLease != writeLease.Value)
            {
                context.Logger?.LogDebug(
                    "Fish Audio settings update skipped because its write lease is no longer active");
                return false;
            }

            var updatedSettings = Clone(settings);
            var storageSettings = writeLease.StorageSettings!;
            var storageSnapshot = Clone(storageSettings);
            try
            {
                if (!update(updatedSettings))
                    return false;

                Copy(updatedSettings, storageSettings);
                SaveCore(context, updatedSettings);
                storageSettings.NeedsCanonicalSave = updatedSettings.NeedsCanonicalSave;
                Copy(updatedSettings, settings);
            }
            catch
            {
                Copy(storageSnapshot, storageSettings);
                throw;
            }

            return true;
        }
    }

    internal static void Retire(Settings settings, SettingsWriteLease writeLease)
    {
        lock (Gate)
        {
            if (settings.ActiveWriteLease == writeLease.Value)
                settings.ActiveWriteLease = 0;
        }
    }

    internal static void Restore(
        Settings? previousSettings,
        SettingsWriteLease previousWriteLease,
        Settings? replacementSettings,
        SettingsWriteLease replacementWriteLease)
    {
        lock (Gate)
        {
            if (replacementSettings is not null
                && replacementSettings.ActiveWriteLease == replacementWriteLease.Value)
            {
                replacementSettings.ActiveWriteLease = 0;
            }

            if (previousSettings is not null && previousWriteLease.IsValid)
                previousSettings.ActiveWriteLease = previousWriteLease.Value;
        }
    }

    private static void SaveCore(IPluginContext context, Settings settings)
    {
        if (settings.IsReadOnly)
        {
            context.Logger?.LogWarning(
                "Fish Audio settings are read-only; preserving host settings file because {Reason}",
                settings.ReadOnlyReason);
            return;
        }

        context.SaveSettingStorage<Settings>();
        settings.NeedsCanonicalSave = false;
    }

    private static long NextWriteLease()
    {
        _lastWriteLease++;
        if (_lastWriteLease == 0)
            _lastWriteLease++;
        return _lastWriteLease;
    }

    private static Settings Clone(Settings settings)
    {
        var clone = new Settings();
        Copy(settings, clone);
        return clone;
    }

    private static void Copy(Settings source, Settings target)
    {
        target.SchemaVersion = source.SchemaVersion;
        target.ApiKey = source.ApiKey;
        target.VoiceId = source.VoiceId;
        target.SelectedModel = source.SelectedModel;
        target.Speed = source.Speed;
        target.Volume = source.Volume;
        target.NormalizeLoudness = source.NormalizeLoudness;
        target.Temperature = source.Temperature;
        target.TopP = source.TopP;
        target.Latency = source.Latency;
        target.Normalize = source.Normalize;
        target.Mp3Bitrate = source.Mp3Bitrate;
        target.ConditionOnPreviousChunks = source.ConditionOnPreviousChunks;
        target.CachedVoice = source.CachedVoice is null
            ? null
            : new CachedVoiceInfo
            {
                Title = source.CachedVoice.Title,
                Description = source.CachedVoice.Description,
                CoverImage = source.CachedVoice.CoverImage,
                AuthorName = source.CachedVoice.AuthorName,
                TaskCount = source.CachedVoice.TaskCount,
                SampleAudioUrl = source.CachedVoice.SampleAudioUrl,
            };
        target.IsS21ProFreePromoDismissed = source.IsS21ProFreePromoDismissed;
        target.IsReadOnly = source.IsReadOnly;
        target.NeedsCanonicalSave = source.NeedsCanonicalSave;
        target.ReadOnlyReason = source.ReadOnlyReason;
    }
}

internal readonly record struct SettingsWriteLease(long Value, Settings? StorageSettings)
{
    public bool IsValid => Value != 0 && StorageSettings is not null;
}
