using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using STranslate.Plugin.Tts.FishAudio.View;
using static TestAssertions;

// Independent native mixed-run line: no production line-metric converter,
// guessed suffix offset or phase search. The baseline-native reference remains
// historical characterization; only its suffix is independently contour-aligned.
internal sealed class NumericPhraseReference
{
    private readonly TextBlock _line;
    private readonly Canvas _root;
    private readonly TextBlock _number;
    private readonly TextBlock _unit;
    private readonly InlineNumericEditor _editor;
    private readonly Point _numberOrigin;
    private readonly double _dpi;

    internal NumericPhraseReference(InlineNumericEditor editor, TextBlock number, TextBlock unit, Action<TextBlock>? groupNative = null)
    {
        _editor = editor;
        _number = number;
        _unit = unit;
        _dpi = VisualTreeHelper.GetDpi(number).PixelsPerDip;
        AssertEqual(_dpi, VisualTreeHelper.GetDpi(unit).PixelsPerDip, "Actual number and unit children must share DPI");
        _numberOrigin = number.TranslatePoint(new Point(), editor);
        _line = new TextBlock
        {
            FontFamily = number.FontFamily, FontSize = number.FontSize,
            FontWeight = number.FontWeight, FontStyle = number.FontStyle, FontStretch = number.FontStretch,
            FlowDirection = number.FlowDirection, Language = number.Language,
            Foreground = number.Foreground, UseLayoutRounding = number.UseLayoutRounding,
            SnapsToDevicePixels = number.SnapsToDevicePixels,
        };
        TextOptions.SetTextFormattingMode(_line, TextOptions.GetTextFormattingMode(number));
        TextOptions.SetTextRenderingMode(_line, TextOptions.GetTextRenderingMode(number));
        TextOptions.SetTextHintingMode(_line, TextOptions.GetTextHintingMode(number));
        _line.Inlines.Add(new Run(number.Text));
        _line.Inlines.Add(new Run(" "));
        _line.Inlines.Add(new Run(unit.Text) { FontSize = 11, Foreground = (Brush)editor.FindResource("TextFillColorSecondaryBrush") });
        groupNative?.Invoke(_line);
        _root = new Canvas { Width = 160, Height = 48, UseLayoutRounding = number.UseLayoutRounding };
        _root.Children.Add(_line);
        // Match the child's actual inherited DPI before measuring native text.
        typeof(VisualTreeHelper).GetMethod("SetRootDpi", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [_root, new DpiScale(_dpi, _dpi)]);
        Canvas.SetLeft(_line, _numberOrigin.X);
        Canvas.SetTop(_line, _numberOrigin.Y);
        _root.Measure(new Size(160, 48));
        _root.Arrange(new Rect(0, 0, 160, 48));
        _root.UpdateLayout();
        AssertEqual(_dpi, VisualTreeHelper.GetDpi(_line).PixelsPerDip, "Native phrase child must match the actual numeric child DPI");
    }

    internal void AssertMatches(string context)
    {
        var actualNumbers = NumericGlyphObservation.Read(_number, _editor);
        var actualUnits = NumericGlyphObservation.Read(_unit, _editor);
        var (nativeNumbers, nativeUnits) = NativeGlyphs(context);
        AssertEqual(true, actualNumbers.Length > 0 && actualUnits.Length > 0 && nativeNumbers.Length > 0 && nativeUnits.Length > 0,
            $"{context}: actual and literal native phrase must expose number and unit glyph runs");
        NumericGlyphObservation.AssertText(actualNumbers, _number.Text, context + "/number");
        NumericGlyphObservation.AssertText(actualUnits, _unit.Text, context + "/unit");
        NumericGlyphObservation.Compare(actualNumbers, nativeNumbers, new Vector(), context + "/number");
        var actualOrigin = actualUnits[0].Baseline;
        var nativeOrigin = nativeUnits[0].Baseline;
        var suffixShift = NumericGlyphObservation.OutlineBottom(nativeNumbers) - NumericGlyphObservation.OutlineBottom(nativeUnits);
        NumericGlyphObservation.Compare(actualUnits, nativeUnits, new Vector(actualOrigin.X - nativeOrigin.X, suffixShift), context + "/unit");
        var numberEnd = actualNumbers.Max(glyph => glyph.Baseline.X + glyph.Advance.X);
        var nativeNumberEnd = nativeNumbers.Max(glyph => glyph.Baseline.X + glyph.Advance.X);
        Near(actualNumbers[0].Baseline.X, nativeNumbers[0].Baseline.X, context + ": numeric origin and physical horizontal phase must match");
        Near(numberEnd, nativeNumberEnd, context + ": actual numeric trailing advance must match the native phrase prefix");
        Near(actualUnits.Sum(glyph => glyph.Advance.X), nativeUnits.Sum(glyph => glyph.Advance.X), context + ": native suffix content extent");
        var compactGap = actualOrigin.X - numberEnd;
        var literalSpace = nativeOrigin.X - nativeNumberEnd;
        // The allocation supplies 3 DIP, which is not the literal space advance.
        // Compare the suffix against the independently measured ordinary-space
        // range. Keep the prior .01 DIP geometry/baseline allowance unchanged.
        AssertEqual(true, compactGap > 0 && compactGap <= literalSpace + .01,
            $"{context}: compact gap must fit within one native ordinary space; gap={compactGap:F5}, space={literalSpace:F5}");
        Near(actualNumbers[0].Baseline.Y, nativeNumbers[0].Baseline.Y, context + ": native prefix baseline");
        Near(nativeNumbers[0].Baseline.Y, nativeOrigin.Y, context + ": historical baseline-native reference characterization");
        var alignedOrigin = nativeOrigin + new Vector(0, suffixShift);
        Near(actualOrigin.Y, alignedOrigin.Y, context + ": independently contour-aligned native suffix");
        AssertEqual(false, MatchesSuffix(actualOrigin, alignedOrigin + new Vector(0, 3 / _dpi), compactGap, literalSpace),
            $"{context}: fixed +3-physical-pixel native reference suffix displacement must fail the native comparison");
        AssertEqual(false, MatchesSuffix(actualOrigin + new Vector(3 / _dpi, 0), alignedOrigin, compactGap + 3 / _dpi, literalSpace),
            $"{context}: fixed +3-physical-pixel horizontal suffix displacement must fail the native comparison");
        AssertEqual(true, MatchesSuffix(actualOrigin, alignedOrigin, compactGap, literalSpace), context + ": actual suffix must satisfy the independently aligned native phrase comparison");
        Console.WriteLine($"PHRASE {context} dpi={_dpi} number={_number.Text} unit={_unit.Text} compactAdvanceGap={compactGap:F5} literalSpace={literalSpace:F5} baselineActual={actualOrigin.Y:F5} baselineNative={nativeOrigin.Y:F5} suffixXActual={actualOrigin.X:F5} suffixXNative={nativeOrigin.X:F5} runs={Runs(_line, _root).Count()} formatting={TextOptions.GetTextFormattingMode(_line)} rounding={_line.UseLayoutRounding}");
    }

    internal void AssertEditingMatches(TextBox input, Visual viewport, string context)
    {
        var actual = NumericGlyphObservation.Read(viewport, _editor);
        var (nativeNumbers, nativeUnits) = NativeGlyphs(context);
        NumericGlyphObservation.AssertText(actual, input.Text, context + "/editing-number");
        AssertEqual(_number.Text, input.Text, context + ": initial native editing draft must retain the full displayed number");
        // The editor centers its number in the same field. Preserve the native
        // baseline and every relative glyph position while allowing that X shift.
        NumericGlyphObservation.Compare(actual, nativeNumbers, new Vector(actual[0].Baseline.X - nativeNumbers[0].Baseline.X, 0), context + "/editing-number");
        var units = NumericGlyphObservation.Read(_unit, _editor);
        NumericGlyphObservation.AssertText(units, _unit.Text, context + "/editing-unit");
        var suffixShift = NumericGlyphObservation.OutlineBottom(nativeNumbers) - NumericGlyphObservation.OutlineBottom(nativeUnits);
        NumericGlyphObservation.Compare(units, nativeUnits, new Vector(units[0].Baseline.X - nativeUnits[0].Baseline.X, suffixShift), context + "/editing-unit");
    }

    private (NumericGlyphObservation.Glyph[] Numbers, NumericGlyphObservation.Glyph[] Units) NativeGlyphs(string context)
    {
        var native = NumericGlyphObservation.Read(_line, _root);
        NumericGlyphObservation.AssertText(native, _number.Text + " " + _unit.Text, context + "/native-phrase");
        var numbers = NumericGlyphObservation.Slice(native, 0, _number.Text.Length, _number.Text, context + "/native-number");
        NumericGlyphObservation.Slice(native, _number.Text.Length, 1, " ", context + "/native-space");
        var units = NumericGlyphObservation.Slice(native, _number.Text.Length + 1, _unit.Text.Length, _unit.Text, context + "/native-unit");
        return (numbers, units);
    }

    private static bool MatchesSuffix(Point actual, Point native, double compactGap, double literalSpace) =>
        Math.Abs(actual.Y - native.Y) < .01 && compactGap > 0 && compactGap <= literalSpace + .01;

    internal void SavePair(string path, bool editing)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var visual = new DrawingVisual();
        var foreground = (Brush)_editor.FindResource("TextFillColorPrimaryBrush");
        var background = (Brush)_editor.FindResource("SolidBackgroundFillColorBaseBrush");
        var typeface = new Typeface(_number.FontFamily, _number.FontStyle, _number.FontWeight, _number.FontStretch);
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(background, null, new Rect(0, 0, 320, 112));
            drawing.DrawText(new FormattedText($"Compiled fixture ({(editing ? "edit" : "rest")})", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 12, foreground, _dpi), new Point(8, 8));
            drawing.DrawText(new FormattedText("Historical baseline-native line", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 12, foreground, _dpi), new Point(8, 64));
            drawing.PushTransform(new TranslateTransform(210, 0));
            drawing.DrawRectangle(new VisualBrush(_editor) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(0, 0, _editor.ActualWidth, _editor.ActualHeight));
            drawing.Pop();
            AssertEqual(Math.Round(56 * _dpi), 56 * _dpi, "Paired rows must retain identical physical vertical phase");
            // 56 DIP is an integral physical translation at 100/125/150%.
            // Both rows retain the same relative phase and actual child DPI.
            drawing.PushTransform(new TranslateTransform(210, 56));
            foreach (var run in Runs(_line, _root))
            {
                drawing.PushTransform(new MatrixTransform(run.Transform));
                drawing.DrawDrawing(run.Drawing);
                drawing.Pop();
            }
            drawing.Pop();
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(320 * _dpi), (int)Math.Ceiling(112 * _dpi), 96 * _dpi, 96 * _dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private sealed record GlyphPlacement(GlyphRunDrawing Drawing, Matrix Transform)
    {
        internal Point Origin => Transform.Transform(Drawing.GlyphRun.BaselineOrigin);
    }

    private static IEnumerable<GlyphPlacement> Runs(Visual owner, Visual root)
    {
        var mapping = owner.TransformToAncestor(root);
        var origin = mapping.Transform(new Point());
        var x = mapping.Transform(new Point(1, 0)) - origin;
        var y = mapping.Transform(new Point(0, 1)) - origin;
        var transform = new Matrix(x.X, x.Y, y.X, y.Y, origin.X, origin.Y);
        foreach (var drawing in Drawings(VisualTreeHelper.GetDrawing(owner), transform)) yield return drawing;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(owner); i++)
            if (VisualTreeHelper.GetChild(owner, i) is Visual child)
                foreach (var drawing in Runs(child, root)) yield return drawing;
    }

    private static IEnumerable<GlyphPlacement> Drawings(Drawing? drawing, Matrix transform)
    {
        if (drawing is GlyphRunDrawing glyph) yield return new(glyph, transform);
        if (drawing is DrawingGroup group)
        {
            var childTransform = group.Transform?.Value ?? Matrix.Identity;
            childTransform.Append(transform);
            foreach (var child in group.Children)
                foreach (var placement in Drawings(child, childTransform)) yield return placement;
        }
    }

    private static void Near(double expected, double actual, string context) =>
        AssertEqual(true, Math.Abs(expected - actual) < .01, $"{context}: expected={expected:F5}, actual={actual:F5}");
}
