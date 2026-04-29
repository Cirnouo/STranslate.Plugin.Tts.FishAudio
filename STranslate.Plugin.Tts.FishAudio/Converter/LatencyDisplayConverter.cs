using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace STranslate.Plugin.Tts.FishAudio.Converter;

public sealed class LatencyDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s) return "";

        var key = s switch
        {
            "normal" => "STranslate_Plugin_Tts_FishAudio_Latency_Normal",
            "balanced" => "STranslate_Plugin_Tts_FishAudio_Latency_Balanced",
            "low" => "STranslate_Plugin_Tts_FishAudio_Latency_Low",
            _ => null,
        };

        if (key is null) return s;
        return Application.Current.TryFindResource(key) as string ?? s;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
