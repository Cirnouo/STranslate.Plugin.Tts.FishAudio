namespace STranslate.Plugin.Tts.FishAudio.Converter;

using System.Globalization;
using System.Windows;
using System.Windows.Data;

public class AvatarPreviewGlyphSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double diameter && double.IsFinite(diameter) && diameter >= 0
            ? diameter * .3
            : DependencyProperty.UnsetValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
