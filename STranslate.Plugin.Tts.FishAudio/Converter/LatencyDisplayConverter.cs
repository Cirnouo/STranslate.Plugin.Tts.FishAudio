using System.Globalization;
using System.Windows.Data;

namespace STranslate.Plugin.Tts.FishAudio.Converter;

public sealed class LatencyDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s ? s switch
        {
            "normal" => "质量优先",
            "balanced" => "平衡",
            "low" => "低延迟优先",
            _ => s,
        } : "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
