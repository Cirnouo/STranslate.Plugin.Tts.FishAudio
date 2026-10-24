using System.Windows.Input;

namespace STranslate.Plugin.Tts.FishAudio.View;

/// <summary>Accumulates fine wheel steps within one numeric hover session.</summary>
internal sealed class NumericWheelAdjustment
{
    private int _remainder;
    private long? _hoverStarted;
    private TimeProvider _timeProvider = TimeProvider.System;

    internal TimeProvider TimeProvider
    {
        set { _timeProvider = value; EndHover(); }
    }

    internal void BeginHover() => _hoverStarted ??= _timeProvider.GetTimestamp();
    internal void EndHover() { _hoverStarted = null; Reset(); }
    internal void RestartHover(bool isMouseOver)
    {
        EndHover();
        if (isMouseOver) BeginHover();
    }
    internal bool IsHoverReady => _hoverStarted is { } started
        && _timeProvider.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(350);

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
        return Configuration.SettingsNormalizer.RoundToPrecision(next, decimalPlaces);
    }
}
