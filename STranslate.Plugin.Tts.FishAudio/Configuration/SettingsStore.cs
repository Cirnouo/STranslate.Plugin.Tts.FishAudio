using Microsoft.Extensions.Logging;

namespace STranslate.Plugin.Tts.FishAudio.Configuration;

public static class SettingsStore
{
    public static Settings Load(IPluginContext context, DateTimeOffset nowUtc)
    {
        var settings = context.LoadSettingStorage<Settings>() ?? new Settings();
        if (SettingsNormalizer.Normalize(settings, nowUtc))
            settings.NeedsCanonicalSave = true;

        if (settings.NeedsCanonicalSave)
            Save(context, settings);

        return settings;
    }

    public static void Save(IPluginContext context, Settings settings)
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
}
