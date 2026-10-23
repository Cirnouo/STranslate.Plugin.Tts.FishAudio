using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace STranslate.Plugin.Tts.FishAudio.View;

/// <summary>A native slider with fine wheel input and its native value bubble.</summary>
public sealed class NumericSlider : Slider
{
    private readonly NumericWheelAdjustment _wheel = new();
    private readonly DispatcherTimer _wheelIdle;
    private Thumb? _wheelThumb;
    private Thumb? _draggingThumb;
    private object? _originalThumbToolTip;

    public NumericSlider()
    {
        // Implicit styles use the exact runtime type; retain the host's Slider
        // style, including dynamic theme changes, for this narrow subclass.
        SetResourceReference(StyleProperty, typeof(Slider));
        _wheelIdle = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _wheelIdle.Tick += (_, _) => CloseWheelToolTip();
        Unloaded += (_, _) => CloseToolTips();
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs args)
    {
        if (args.Delta == 0) return;
        args.Handled = true;
        SetCurrentValue(ValueProperty, _wheel.Adjust(Value, Minimum, Maximum, AutoToolTipPrecision, args.Delta));
        if (Template?.FindName("PART_Track", this) is not Track { Thumb: { } thumb }) return;
        if (_wheelThumb is null && _draggingThumb is null)
            _originalThumbToolTip = thumb.ToolTip;
        // These protected hooks only manage WPF's own value tooltip. Refresh
        // through them without a DragDelta, which would re-snap and save Value.
        thumb.ToolTip = _originalThumbToolTip;
        base.OnThumbDragStarted(new DragStartedEventArgs(0, 0) { Source = thumb });
        if (thumb.ToolTip is ToolTip toolTip) toolTip.PlacementTarget = thumb;
        if (_draggingThumb is null)
        {
            _wheelThumb = thumb;
            _wheelIdle.Stop();
            _wheelIdle.Start();
        }
    }

    protected override void OnThumbDragStarted(DragStartedEventArgs args)
    {
        _wheelIdle.Stop();
        _wheel.Reset();
        if (_wheelThumb is null && _draggingThumb is null && args.OriginalSource is Thumb thumb)
            _originalThumbToolTip = thumb.ToolTip;
        _wheelThumb = null;
        _draggingThumb = args.OriginalSource as Thumb;
        if (_draggingThumb is not null) _draggingThumb.ToolTip = _originalThumbToolTip;
        base.OnThumbDragStarted(args);
        if (_draggingThumb?.ToolTip is ToolTip toolTip) toolTip.PlacementTarget = _draggingThumb;
    }

    protected override void OnThumbDragCompleted(DragCompletedEventArgs args)
    {
        base.OnThumbDragCompleted(args);
        _draggingThumb = null;
        _originalThumbToolTip = null;
        _wheel.Reset();
    }

    protected override void OnMouseLeave(MouseEventArgs args)
    {
        _wheel.Reset();
        CloseWheelToolTip();
        base.OnMouseLeave(args);
    }

    public override void OnApplyTemplate()
    {
        CloseToolTips();
        base.OnApplyTemplate();
    }

    private void CloseWheelToolTip()
    {
        _wheelIdle.Stop();
        if (_wheelThumb is not { } thumb) return;
        base.OnThumbDragCompleted(new DragCompletedEventArgs(0, 0, false) { Source = thumb });
        _wheelThumb = null;
        _originalThumbToolTip = null;
    }

    private void CloseToolTips()
    {
        CloseWheelToolTip();
        if (_draggingThumb is { } thumb)
            base.OnThumbDragCompleted(new DragCompletedEventArgs(0, 0, true) { Source = thumb });
        _draggingThumb = null;
        _originalThumbToolTip = null;
        _wheel.Reset();
    }
}
