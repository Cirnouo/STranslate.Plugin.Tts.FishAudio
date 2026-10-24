using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace STranslate.Plugin.Tts.FishAudio.View;

/// <summary>Allocates one finite, baseline-aligned title · author line beside the card actions.</summary>
public sealed class VoiceHeaderPanel : Panel
{
    // Explicit text inputs invalidate layout even when a fixed child width makes
    // its new DesiredSize equal to the old one. The controls retain their bindings.
    public static readonly DependencyProperty TitleTextProperty = DependencyProperty.Register(
        nameof(TitleText), typeof(string), typeof(VoiceHeaderPanel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty AuthorTextProperty = DependencyProperty.Register(
        nameof(AuthorText), typeof(string), typeof(VoiceHeaderPanel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public string? TitleText { get => (string?)GetValue(TitleTextProperty); set => SetValue(TitleTextProperty, value); }
    public string? AuthorText { get => (string?)GetValue(AuthorTextProperty); set => SetValue(AuthorTextProperty, value); }

    private double _titleWidth;
    private double _separatorWidth;
    private double _authorWidth;
    private double _baseline;
    private double _lineHeight;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count != 3 || InternalChildren[0] is not Button { Content: TextBlock titleText } title ||
            InternalChildren[1] is not TextBlock separator || InternalChildren[2] is not TextBlock author)
            return new Size();

        var hasTitle = titleText.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(TitleText);
        var hasAuthor = !string.IsNullOrWhiteSpace(AuthorText);
        author.SetCurrentValue(VisibilityProperty, hasAuthor ? Visibility.Visible : Visibility.Collapsed);
        separator.SetCurrentValue(VisibilityProperty, hasTitle && hasAuthor ? Visibility.Visible : Visibility.Collapsed);
        var titleNaturalWidth = hasTitle ? Format(titleText).WidthIncludingTrailingWhitespace : 0;
        var authorNaturalWidth = hasAuthor ? Format(author).WidthIncludingTrailingWhitespace : 0;
        var separatorNaturalWidth = hasTitle && hasAuthor ? Format(separator).WidthIncludingTrailingWhitespace + separator.Margin.Left + separator.Margin.Right : 0;
        var width = double.IsFinite(availableSize.Width) ? Math.Max(0, availableSize.Width) :
            titleNaturalWidth + separatorNaturalWidth + authorNaturalWidth;
        _separatorWidth = Math.Min(separatorNaturalWidth, width);
        var remaining = Math.Max(0, width - _separatorWidth);
        _authorWidth = hasTitle ? Math.Min(authorNaturalWidth, remaining * .35) : Math.Min(authorNaturalWidth, remaining);
        _titleWidth = Math.Min(titleNaturalWidth, remaining - _authorWidth);
        // Keep a fitting natural width exact. Subtracting it out and back in can
        // undershoot by a floating-point residue and make native text trim "Li".
        if (_titleWidth == titleNaturalWidth)
            _authorWidth = titleNaturalWidth + authorNaturalWidth <= remaining ? authorNaturalWidth : remaining - _titleWidth;
        // TextBlock's native ellipsis minimum can exceed an extremely short name's
        // allocation. Explicit finite widths cap its native layout clip/viewport
        // inside the reserved line even when native RenderSize stays wider.
        title.SetCurrentValue(WidthProperty, _titleWidth);
        titleText.SetCurrentValue(MaxWidthProperty, _titleWidth);
        separator.SetCurrentValue(WidthProperty, Math.Max(0, _separatorWidth - separator.Margin.Left - separator.Margin.Right));
        author.SetCurrentValue(WidthProperty, _authorWidth);
        author.SetCurrentValue(MaxWidthProperty, _authorWidth);
        title.Measure(new Size(_titleWidth, double.PositiveInfinity));
        separator.Measure(new Size(_separatorWidth, double.PositiveInfinity));
        author.Measure(new Size(_authorWidth, double.PositiveInfinity));

        var texts = new[] { titleText, separator, author }.Where(text => text.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(text.Text)).ToArray();
        _baseline = texts.Select(Baseline).DefaultIfEmpty(0).Max();
        _lineHeight = _baseline + texts.Select(text => Math.Max(0, text.DesiredSize.Height - Baseline(text))).DefaultIfEmpty(0).Max();
        return new Size(_titleWidth + _separatorWidth + _authorWidth, Math.Max(MinHeight, _lineHeight));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count != 3 || InternalChildren[0] is not Button { Content: TextBlock titleText } title ||
            InternalChildren[1] is not TextBlock separator || InternalChildren[2] is not TextBlock author)
            return finalSize;

        var top = Math.Max(0, (finalSize.Height - _lineHeight) / 2);
        Arrange(title, titleText, 0, _titleWidth);
        Arrange(separator, separator, _titleWidth, _separatorWidth);
        Arrange(author, author, _titleWidth + _separatorWidth, _authorWidth);
        return finalSize;

        void Arrange(FrameworkElement element, TextBlock text, double left, double width) =>
            element.Arrange(new Rect(left, top + _baseline - Baseline(text), width, element.DesiredSize.Height));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateMeasure();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == LanguageProperty || e.Property == FlowDirectionProperty ||
            e.Property == System.Windows.Documents.TextElement.FontFamilyProperty ||
            e.Property == System.Windows.Documents.TextElement.FontStyleProperty ||
            e.Property == System.Windows.Documents.TextElement.FontWeightProperty ||
            e.Property == System.Windows.Documents.TextElement.FontStretchProperty ||
            e.Property == TextOptions.TextFormattingModeProperty)
            InvalidateMeasure();
    }

    private static double Baseline(TextBlock text)
    {
        if (text.Visibility != Visibility.Visible || string.IsNullOrWhiteSpace(text.Text)) return 0;
        if (double.IsFinite(text.BaselineOffset)) return text.BaselineOffset;
        // Automatic TextBlock baselines may remain NaN. Resolve the same inherited
        // typeface/culture/flow/DPI instead of treating that value as a zero ascent.
        return Format(text).Baseline;
    }

    private static FormattedText Format(TextBlock text) =>
        new(text.Text, text.Language.GetEquivalentCulture(), text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize,
            text.Foreground, null, TextOptions.GetTextFormattingMode(text), VisualTreeHelper.GetDpi(text).PixelsPerDip);
}
