using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace STranslate.Plugin.Tts.FishAudio.View;

internal static class PopupViewport
{
    internal static DpiScale ScreenScale(Visual anchor)
    {
        var transform = PresentationSource.FromVisual(anchor)?.CompositionTarget?.TransformToDevice;
        return transform is { } matrix ? new DpiScale(matrix.M11, matrix.M22) : VisualTreeHelper.GetDpi(anchor);
    }

    internal static Rect VisibleBounds(FrameworkElement anchor, FrameworkElement? owner, IEnumerable<ScrollViewer> ancestors)
    {
        var bounds = VoiceLanguageTags.MonitorWorkArea(anchor.PointToScreen(new Point()));
        if (owner is { ActualWidth: > 0, ActualHeight: > 0 }) bounds.Intersect(ScreenBounds(owner));
        foreach (var viewer in ancestors)
        {
            // ViewportHeight can be logical item units; use the rendered presenter.
            var presenter = viewer.Template?.FindName("PART_ScrollContentPresenter", viewer) as FrameworkElement
                ?? Descendants(viewer).OfType<ScrollContentPresenter>().FirstOrDefault() ?? (FrameworkElement)viewer;
            bounds.Intersect(ScreenBounds(presenter));
        }
        var dpi = ScreenScale(anchor);
        if (bounds.IsEmpty || bounds.Width <= 16 * dpi.DpiScaleX || bounds.Height <= 16 * dpi.DpiScaleY) return Rect.Empty;
        bounds.Inflate(-8 * dpi.DpiScaleX, -8 * dpi.DpiScaleY);
        return bounds;
    }

    internal static bool Prepare(FrameworkElement anchor, FrameworkElement? owner, IEnumerable<ScrollViewer> ancestors,
        Border surface, ScrollViewer body, IEnumerable<FrameworkElement> fixedWidthChrome,
        double maximumWidth, double maximumBodyHeight = double.PositiveInfinity)
    {
        var bounds = VisibleBounds(anchor, owner, ancestors);
        return PrepareWithinBounds(anchor, surface, body, fixedWidthChrome, maximumWidth, maximumBodyHeight, bounds);
    }

    // Bounds are the already-inset visible rectangle, in native device pixels.
    internal static bool PrepareWithinBounds(FrameworkElement anchor, Border surface, ScrollViewer body,
        IEnumerable<FrameworkElement> fixedWidthChrome, double maximumWidth, double maximumBodyHeight, Rect bounds)
    {
        if (bounds.IsEmpty) return false;
        var dpi = ScreenScale(anchor);
        surface.Width = Math.Min(maximumWidth, bounds.Width / dpi.DpiScaleX);
        // A retired PopupRoot suspends layout and retains constrained DesiredSize.
        // Bound source creation first; the caller rechecks its actual new source
        // synchronously before completing Open, using current native metrics.
        if (PresentationSource.FromVisual(surface) is null)
        {
            surface.MaxHeight = bounds.Height / dpi.DpiScaleY;
            body.MaxHeight = Math.Min(maximumBodyHeight, surface.MaxHeight);
            return true;
        }
        var minimumContentWidth = 0d;
        foreach (var chrome in fixedWidthChrome)
        {
            // Include native margins/gaps, while allowing header titles to wrap.
            chrome.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            minimumContentWidth = Math.Max(minimumContentWidth, chrome.DesiredSize.Width);
        }
        surface.InvalidateMeasure();
        var shellWidth = surface.Padding.Left + surface.Padding.Right
            + surface.BorderThickness.Left + surface.BorderThickness.Right;
        if (surface.Width < minimumContentWidth + shellWidth) return false;
        surface.MaxHeight = double.PositiveInfinity;
        body.MaxHeight = maximumBodyHeight;
        // Measure localized fixed chrome independently: no estimated popup height
        // may conceal a wrapped title or an unreachable Apply button.
        // A zero measure constraint preserves visibility/keyboard focus while
        // excluding the body. Never collapse a focused, already open body.
        body.MaxHeight = 0;
        MeasureSurface(surface, body);
        var chromeHeight = surface.DesiredSize.Height;
        body.MaxHeight = maximumBodyHeight;
        MeasureSurface(surface, body);
        var naturalHeight = surface.DesiredSize.Height * dpi.DpiScaleY;
        var top = anchor.PointToScreen(new Point()).Y;
        var bottom = anchor.PointToScreen(new Point(0, anchor.ActualHeight)).Y;
        var below = Math.Max(0, bounds.Bottom - bottom - 8 * dpi.DpiScaleY);
        var above = Math.Max(0, top - 8 * dpi.DpiScaleY - bounds.Top);
        var available = naturalHeight <= below ? below : naturalHeight <= above ? above : Math.Max(above, below);
        var usefulHeight = (chromeHeight + 32) * dpi.DpiScaleY;
        if (available < usefulHeight) available = bounds.Height;
        if (bounds.Height <= chromeHeight * dpi.DpiScaleY || available <= chromeHeight * dpi.DpiScaleY) return false;
        surface.MaxHeight = Math.Min(bounds.Height, available) / dpi.DpiScaleY;
        body.MaxHeight = Math.Min(maximumBodyHeight, surface.MaxHeight - chromeHeight);
        return true;
    }

    private static void MeasureSurface(Border surface, ScrollViewer body)
    {
        // Changing MaxHeight invalidates the body, but ancestor invalidation is
        // deferred. Refresh its complete owning path before each same-constraint
        // synchronous Measure so neither chrome nor natural size can be reused.
        for (DependencyObject? current = body; current is UIElement element; current = VisualTreeHelper.GetParent(current))
        {
            element.InvalidateMeasure();
            if (ReferenceEquals(current, surface)) break;
        }
        surface.Measure(new Size(surface.Width, double.PositiveInfinity));
    }

    internal static CustomPopupPlacement[] Place(FrameworkElement anchor, FrameworkElement? owner,
        IEnumerable<ScrollViewer> ancestors, Size popupSize, Size targetSize, bool alignRight)
    {
        var bounds = VisibleBounds(anchor, owner, ancestors);
        return PlaceWithinBounds(anchor, popupSize, targetSize, alignRight, bounds);
    }

    internal static CustomPopupPlacement[] PlaceWithinBounds(FrameworkElement anchor, Size popupSize,
        Size targetSize, bool alignRight, Rect bounds)
    {
        if (bounds.IsEmpty) return [];
        var origin = anchor.PointToScreen(new Point());
        var dpi = ScreenScale(anchor);
        // WPF custom-placement sizes/offsets are already in device pixels.
        var left = alignRight ? origin.X + targetSize.Width - popupSize.Width : origin.X - 12 * dpi.DpiScaleX;
        left = Math.Clamp(left, bounds.Left, Math.Max(bounds.Left, bounds.Right - popupSize.Width));
        var below = bounds.Bottom - origin.Y - targetSize.Height - 8 * dpi.DpiScaleY;
        var above = origin.Y - 8 * dpi.DpiScaleY - bounds.Top;
        var useAbove = popupSize.Height > below && (popupSize.Height <= above || above > below);
        var top = useAbove ? origin.Y - 8 * dpi.DpiScaleY - popupSize.Height : origin.Y + targetSize.Height + 8 * dpi.DpiScaleY;
        top = Math.Clamp(top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - popupSize.Height));
        return [new CustomPopupPlacement(new Point(left - origin.X, top - origin.Y), PopupPrimaryAxis.None)];
    }

    private static Rect ScreenBounds(FrameworkElement element) => new(element.PointToScreen(new Point()),
        element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight)));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
