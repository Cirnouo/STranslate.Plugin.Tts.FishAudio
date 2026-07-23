namespace STranslate.Plugin.Tts.FishAudio.Configuration;

public static class SettingsNormalizer
{
    public static IReadOnlyList<string> Latencies { get; } = ["normal", "balanced", "low"];
    public static IReadOnlyList<int> Mp3Bitrates { get; } = [64, 128, 192];

    public static bool Normalize(Settings settings, DateTimeOffset nowUtc)
    {
        var changed = NormalizeSelectedModel(settings, nowUtc);

        if (!Latencies.Contains(settings.Latency, StringComparer.Ordinal))
        {
            settings.Latency = Settings.DefaultLatency;
            changed = true;
        }

        if (!Mp3Bitrates.Contains(settings.Mp3Bitrate))
        {
            settings.Mp3Bitrate = Settings.DefaultMp3Bitrate;
            changed = true;
        }

        changed |= NormalizeRangeAndStep(settings.Speed, 0.5, 2.0, 0.05m, Settings.DefaultSpeed, out var speed);
        settings.Speed = speed;
        changed |= NormalizeRangeAndStep(settings.Volume, -10.0, 10.0, 0.1m, Settings.DefaultVolume, out var volume);
        settings.Volume = volume;
        changed |= NormalizeRangeAndStep(settings.Temperature, 0.0, 1.0, 0.05m, Settings.DefaultTemperature, out var temperature);
        settings.Temperature = temperature;
        changed |= NormalizeRangeAndStep(settings.TopP, 0.0, 1.0, 0.05m, Settings.DefaultTopP, out var topP);
        settings.TopP = topP;

        return changed;
    }

    public static bool NormalizeSelectedModel(Settings settings, DateTimeOffset nowUtc)
    {
        if (!NeedsSelectedModelNormalization(settings, nowUtc))
            return false;

        settings.SelectedModel = FishAudioModelPolicy.GetDefaultModel(nowUtc);
        return true;
    }

    internal static bool NeedsSelectedModelNormalization(Settings settings, DateTimeOffset nowUtc) =>
        !FishAudioModelPolicy.GetAvailableModels(nowUtc).Contains(settings.SelectedModel, StringComparer.Ordinal);

    private static bool NormalizeRangeAndStep(
        double value,
        double min,
        double max,
        decimal step,
        double defaultValue,
        out double normalized)
    {
        if (!double.IsFinite(value) || value < min || value > max)
        {
            normalized = defaultValue;
            return true;
        }

        var decimalValue = (decimal)value;
        normalized = (double)(Math.Round(decimalValue / step, 0, MidpointRounding.AwayFromZero) * step);
        return normalized != value;
    }
}
