using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace STranslate.Plugin.Tts.FishAudio.View;

// The host's native ScrollContentPresenter spans the overlay scrollbar column.
// Keep that template, but exclude the complete realized part (not its thin thumb).
internal sealed class PopupScrollbarLane(ScrollViewer viewer)
{
    private bool _active;
    private double _reserved;
    private Thickness _originalPadding;

    internal void Begin()
    {
        if (_active) return;
        _active = true;
        _reserved = 0;
        _originalPadding = viewer.Padding;
        viewer.LayoutUpdated += LayoutUpdated;
        viewer.Unloaded += Unloaded;
        Update();
    }

    internal void End()
    {
        if (!_active) return;
        _active = false;
        viewer.LayoutUpdated -= LayoutUpdated;
        viewer.Unloaded -= Unloaded;
        viewer.Padding = _originalPadding;
        _reserved = 0;
    }

    private void Unloaded(object sender, RoutedEventArgs args) => End();
    private void LayoutUpdated(object? sender, EventArgs args) => Update();
    private void Update()
    {
        if (!_active) return;
        // Resolve every layout: template/theme replacement can retire the old part.
        if (viewer.Template?.FindName("PART_VerticalScrollBar", viewer) is not ScrollBar { IsVisible: true, ActualWidth: > 0 } bar) return;
        var bounds = bar.TransformToAncestor(viewer).TransformBounds(new Rect(bar.RenderSize));
        var lane = Math.Max(bounds.Width, viewer.ActualWidth - bounds.Left) + 4;
        // A lane can grow for an expanded/replaced native part, but cannot oscillate
        // around the overflow threshold during this open lifetime.
        if (!double.IsFinite(lane) || lane <= _reserved + .01) return;
        _reserved = lane;
        viewer.Padding = new Thickness(_originalPadding.Left, _originalPadding.Top,
            Math.Max(_originalPadding.Right, lane), _originalPadding.Bottom);
    }
}
