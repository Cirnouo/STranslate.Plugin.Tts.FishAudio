namespace STranslate.Plugin.Tts.FishAudio.Configuration;

public static class SettingsNormalizer
{
    public const double MinimumTopP = 0.05;
    public static IReadOnlyList<string> Latencies { get; } = ["normal", "balanced", "low"];
    public static IReadOnlyList<int> Mp3Bitrates { get; } = [64, 128, 192];
    public static IReadOnlyList<string> AudioFormats { get; } = ["pcm", "mp3"];

    public static bool Normalize(Settings settings, DateTimeOffset nowUtc)
    {
        var changed = NormalizeSelectedModel(settings, nowUtc);

        if (!AudioFormats.Contains(settings.AudioFormat, StringComparer.Ordinal))
        {
            settings.AudioFormat = Settings.DefaultAudioFormat;
            changed = true;
        }

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

        changed |= NormalizeRangeAndPrecision(settings.Speed, 0.5, 2.0, 2, Settings.DefaultSpeed, out var speed);
        settings.Speed = speed;
        changed |= NormalizeRangeAndPrecision(settings.Volume, -10.0, 10.0, 1, Settings.DefaultVolume, out var volume);
        settings.Volume = volume;
        changed |= NormalizeRangeAndPrecision(settings.Temperature, 0.0, 1.0, 2, Settings.DefaultTemperature, out var temperature);
        settings.Temperature = temperature;
        NormalizeRangeAndPrecision(settings.TopP, 0.0, 1.0, 2, Settings.DefaultTopP, out var topP);
        topP = NormalizeTopP(topP);
        changed |= topP != settings.TopP;
        settings.TopP = topP;

        return changed;
    }

    // Client compatibility floor; the provider's published numeric domain still includes zero.
    public static double NormalizeTopP(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
            return Settings.DefaultTopP;
        return Math.Max(MinimumTopP, value);
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

    private static bool NormalizeRangeAndPrecision(
        double value,
        double min,
        double max,
        int decimalPlaces,
        double defaultValue,
        out double normalized)
    {
        if (!double.IsFinite(value) || value < min || value > max)
        {
            normalized = defaultValue;
            return true;
        }

        var decimalValue = (decimal)value;
        normalized = (double)Math.Round(decimalValue, decimalPlaces, MidpointRounding.AwayFromZero);
        return normalized != value;
    }
}
