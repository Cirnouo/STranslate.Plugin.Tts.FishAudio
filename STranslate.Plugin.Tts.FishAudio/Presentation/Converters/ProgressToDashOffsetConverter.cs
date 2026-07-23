namespace STranslate.Plugin.Tts.FishAudio.Converter;

using System.Globalization;
using System.Windows.Data;

public class ProgressToDashOffsetConverter : IValueConverter
{
    public double TotalDashUnits { get; set; } = 72.26;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double progress)
            return (1.0 - Math.Clamp(progress, 0, 1)) * TotalDashUnits;
        return TotalDashUnits;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
