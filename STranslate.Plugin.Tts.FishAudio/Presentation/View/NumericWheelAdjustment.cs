using System.Windows.Input;

namespace STranslate.Plugin.Tts.FishAudio.View;

/// <summary>Accumulates fine wheel steps within one numeric hover session.</summary>
internal sealed class NumericWheelAdjustment
{
    private int _remainder;

    internal void Reset() => _remainder = 0;

    internal double Adjust(double value, double minimum, double maximum, int decimalPlaces, int delta)
    {
        if (delta == 0) return value;
        if ((delta > 0 && value >= maximum) || (delta < 0 && value <= minimum))
        {
            Reset();
            return value;
        }
        var accumulated = (long)_remainder + delta;
        var notches = accumulated / Mouse.MouseWheelDeltaForOneLine;
        _remainder = (int)(accumulated % Mouse.MouseWheelDeltaForOneLine);
        if (notches == 0) return value;
        var next = Math.Clamp((decimal)value + notches * .01m, (decimal)minimum, (decimal)maximum);
        if (next == (decimal)minimum || next == (decimal)maximum) Reset();
        return (double)Math.Round(next, decimalPlaces, MidpointRounding.AwayFromZero);
    }
}
