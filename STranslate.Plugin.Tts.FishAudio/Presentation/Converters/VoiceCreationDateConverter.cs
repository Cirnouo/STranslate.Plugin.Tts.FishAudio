using System.Globalization;
using System.Windows.Data;

namespace STranslate.Plugin.Tts.FishAudio.Converter;

public sealed class VoiceCreationDateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Format(value as DateTimeOffset?, TimeZoneInfo.Local, parameter as string == "Full");

    internal static string Format(DateTimeOffset? value, TimeZoneInfo zone, bool full = false) =>
        value is { } date
            ? TimeZoneInfo.ConvertTime(date, zone).ToString(full ? "O" : "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "—";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
