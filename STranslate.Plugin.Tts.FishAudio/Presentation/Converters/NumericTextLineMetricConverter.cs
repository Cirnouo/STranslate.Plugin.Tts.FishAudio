using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace STranslate.Plugin.Tts.FishAudio.Converter;

/// <summary>Keeps separate units on the numeric font's line, including smaller caption glyphs.</summary>
public sealed class NumericTextLineMetricConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [FontFamily family, double size]) return DependencyProperty.UnsetValue;
        return size * (parameter is "Baseline" ? family.Baseline : family.LineSpacing);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
