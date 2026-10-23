using System.Windows;
using System.Windows.Controls;

namespace STranslate.Plugin.Tts.FishAudio.View;

// Unlike horizontal StackPanel/WrapPanel measurement, each pill receives a finite
// width, so localized or unknown names can wrap inside the remaining card column.
public sealed class LanguageWrapPanel : Panel
{
    public double HorizontalGap { get; set; } = 4;
    public double VerticalGap { get; set; } = 4;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return Layout(availableSize.Width, false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize.Width, true);
        return finalSize;
    }

    private Size Layout(double width, bool arrange)
    {
        double x = 0, y = 0, lineHeight = 0, usedWidth = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > width)
            {
                x = 0;
                y += lineHeight + VerticalGap;
                lineHeight = 0;
            }
            if (arrange) child.Arrange(new Rect(x, y, size.Width, size.Height));
            usedWidth = Math.Max(usedWidth, x + size.Width);
            x += size.Width + HorizontalGap;
            lineHeight = Math.Max(lineHeight, size.Height);
        }
        return new Size(usedWidth, y + lineHeight);
    }
}
