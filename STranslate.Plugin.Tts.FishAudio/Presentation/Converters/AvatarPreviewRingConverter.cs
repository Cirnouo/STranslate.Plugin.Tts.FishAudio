namespace STranslate.Plugin.Tts.FishAudio.Converter;

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

public class AvatarPreviewRingConverter : IMultiValueConverter
{
    public bool IsIndeterminate { get; set; }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double diameter || values[1] is not double strokeThickness ||
            !double.IsFinite(diameter) || !double.IsFinite(strokeThickness) || strokeThickness <= 0 || diameter <= strokeThickness)
            return DependencyProperty.UnsetValue;

        var circumferenceUnits = (diameter - strokeThickness) * Math.PI / strokeThickness;
        if (targetType == typeof(DoubleCollection))
        {
            var dashes = IsIndeterminate
                ? new DoubleCollection { circumferenceUnits / 4, circumferenceUnits * 3 / 4 }
                : new DoubleCollection { circumferenceUnits, circumferenceUnits };
            dashes.Freeze();
            return dashes;
        }

        if (targetType == typeof(double) && values.Length > 2 && values[2] is double progress && double.IsFinite(progress))
            return (1 - Math.Clamp(progress, 0, 1)) * circumferenceUnits;

        return DependencyProperty.UnsetValue;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
