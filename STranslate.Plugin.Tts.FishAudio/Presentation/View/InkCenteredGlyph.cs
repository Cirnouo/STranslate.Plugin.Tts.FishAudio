using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace STranslate.Plugin.Tts.FishAudio.View;

/// <summary>Centers the original symbol outline, independently of a font line box.</summary>
public sealed class InkCenteredGlyph : Control
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(InkCenteredGlyph),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly ConcurrentDictionary<(Typeface Face, double Size, string Glyph), Geometry> Outlines = new();

    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }

    public InkCenteredGlyph()
    {
        Focusable = false;
        IsHitTestVisible = false;
        UseLayoutRounding = false;
    }

    protected override Size MeasureOverride(Size constraint) => new(FontSize, FontSize);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var face = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var outline = Outlines.GetOrAdd((face, FontSize, Glyph ?? ""), static key =>
        {
            if (key.Glyph.Length != 1 || !key.Face.TryGetGlyphTypeface(out var font)
                || !font.CharacterToGlyphMap.TryGetValue(key.Glyph[0], out var index)) return Geometry.Empty;
            var geometry = font.GetGlyphOutline(index, key.Size, key.Size);
            geometry.Freeze();
            return geometry;
        });
        var ink = outline.Bounds;
        if (ink.IsEmpty) return;
        drawingContext.PushTransform(new TranslateTransform(
            (ActualWidth - ink.Width) / 2 - ink.Left,
            (ActualHeight - ink.Height) / 2 - ink.Top));
        drawingContext.DrawGeometry(Foreground, null, outline);
        drawingContext.Pop();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.Property == ForegroundProperty) InvalidateVisual();
        if (args.Property == FontFamilyProperty || args.Property == FontSizeProperty || args.Property == FontWeightProperty
            || args.Property == FontStyleProperty || args.Property == FontStretchProperty)
        {
            InvalidateMeasure();
            InvalidateVisual();
        }
    }
}
