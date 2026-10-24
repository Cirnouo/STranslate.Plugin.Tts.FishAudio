using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Controls;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static StaTestHost;

internal static class NumericTypographySuite
{
    internal static async Task RunAsync(string? proofDirectory)
    {
        foreach (var dark in new[] { false, true })
            await RunOnStaThreadAsync(() => Measure(dark, proofDirectory));
    }

    private static void Measure(bool dark, string? proofDirectory)
    {
        var settings = new Settings { Speed = .87, Volume = -10 };
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light);
        SettingsLayoutSuite.Layout(view);
        var editors = Visuals(view).OfType<InlineNumericEditor>().ToArray();
        foreach (var editor in editors)
        {
            Click((Button)editor.FindName("DisplayButton"));
            SettingsLayoutSuite.Layout(view);
            PressKey((TextBox)editor.FindName("EditorBox"), System.Windows.Input.Key.Escape);
        }
        typeof(VisualTreeHelper).GetMethod("SetRootDpi", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [view, new DpiScale(1.25, 1.25)]);
        SettingsLayoutSuite.Layout(view);
        foreach (var editor in editors)
        {
            var property = BindingOperations.GetBinding(editor, InlineNumericEditor.ValueProperty)!.Path.Path;
            var button = (Button)editor.FindName("DisplayButton");
            var unit = Visuals(editor).OfType<TextBlock>().Single(block => block.Name == "UnitText");
            var label = Visuals(button).OfType<TextBlock>().Single();
            Console.WriteLine($"METRICS {(dark ? "dark" : "light")}/{property} button={Font(button)} label={Font(label)} unit={Font(unit)} unitMargin={unit.Margin} labelBox={Bounds(label, editor)} unitBox={Bounds(unit, editor)}");
            DumpGlyphs(label, editor, "resting");
            DumpGlyphs(unit, editor, "unit");
            AssertResting(editor, label, unit, property);
            if (proofDirectory is not null) Save(editor, Path.Combine(proofDirectory, $"{(dark ? "dark" : "light")}-{property}-static.png"));
            Click(button);
            SettingsLayoutSuite.Layout(view);
            var input = (TextBox)editor.FindName("EditorBox");
            var viewport = Visuals(input).OfType<FrameworkElement>().Single(element => element.GetType().Name == "TextBoxView");
            Console.WriteLine($"METRICS input={Font(input)} inputBox={Bounds(input, editor)} textView={Bounds(viewport, editor)} textAlignment={input.TextAlignment} dpi={VisualTreeHelper.GetDpi(viewport).DpiScaleX}");
            DumpGlyphs(viewport, editor, "editing");
            AssertEditing(editor, input, unit, property);
            if (proofDirectory is not null) Save(editor, Path.Combine(proofDirectory, $"{(dark ? "dark" : "light")}-{property}-edit.png"));
            PressKey(input, System.Windows.Input.Key.Escape);
        }
    }

    // Catches an oversized multiplication sign, a separately centered smaller
    // unit line, a font mismatch or a gap larger than ordinary word spacing.
    internal static void AssertResting(InlineNumericEditor editor, TextBlock number, TextBlock unit, string context)
    {
        if (unit.Visibility != Visibility.Visible) return;
        var numberRun = Glyphs(VisualTreeHelper.GetDrawing(number)).Single();
        var unitRun = Glyphs(VisualTreeHelper.GetDrawing(unit)).Single();
        var numberBaseline = number.TransformToAncestor(editor).Transform(numberRun.BaselineOrigin);
        var unitBaseline = unit.TransformToAncestor(editor).Transform(unitRun.BaselineOrigin);
        TestAssertions.AssertEqual(number.FontFamily, unit.FontFamily, $"{context}: number and unit must resolve the same font family");
        TestAssertions.AssertEqual(number.FontWeight, unit.FontWeight, $"{context}: number and unit must resolve the same font weight");
        TestAssertions.AssertEqual(numberRun.GlyphTypeface.FamilyNames.Values.First(), unitRun.GlyphTypeface.FamilyNames.Values.First(),
            $"{context}: actual number and unit glyphs must resolve a compatible font family");
        TestAssertions.AssertEqual(numberRun.GlyphTypeface.Weight, unitRun.GlyphTypeface.Weight,
            $"{context}: actual number and unit glyphs must resolve a compatible font weight");
        TestAssertions.AssertEqual(true, Math.Abs(numberBaseline.Y - unitBaseline.Y) < .01,
            $"{context}: unit and number must share a typographic baseline; number={numberBaseline.Y:F4}, unit={unitBaseline.Y:F4}");
        if (unit.Text == "×")
        {
            TestAssertions.AssertEqual((double)editor.FindResource("CaptionTextBlockFontSize"), unitRun.FontRenderingEmSize,
                $"{context}: multiplication sign must use the smaller native caption size");
            TestAssertions.AssertEqual(true, unitRun.FontRenderingEmSize < numberRun.FontRenderingEmSize,
                $"{context}: multiplication sign must be smaller than the number");
        }
        else
            TestAssertions.AssertEqual(numberRun.FontRenderingEmSize, unitRun.FontRenderingEmSize,
                $"{context}: dB must retain the numeric font size and natural glyph baseline");
        var space = new FormattedText(" ", CultureInfo.CurrentCulture, number.FlowDirection,
            new Typeface(number.FontFamily, number.FontStyle, number.FontWeight, number.FontStretch), number.FontSize,
            number.Foreground, VisualTreeHelper.GetDpi(number).PixelsPerDip).WidthIncludingTrailingWhitespace;
        var gap = unitBaseline.X - Bounds(number, editor).Right;
        TestAssertions.AssertEqual(true, gap > 0 && gap <= space + .01,
            $"{context}: number and separate unit must have at most an ordinary same-font space; gap={gap:F3}, space={space:F3}");
        var hit = VisualTreeHelper.HitTest(editor, unit.TranslatePoint(new Point(unit.ActualWidth / 2, unit.ActualHeight / 2), editor));
        TestAssertions.AssertEqual(unit, hit?.VisualHit, $"{context}: the unit's visual hit must remain outside the numeric button");
    }

    internal static void AssertEditing(InlineNumericEditor editor, TextBox input, TextBlock unit, string context)
    {
        if (unit.Visibility != Visibility.Visible) return;
        var viewport = Visuals(input).OfType<Visual>().Single(element => element.GetType().Name == "TextBoxView");
        var number = new[] { viewport }.Concat(Visuals(viewport).OfType<Visual>())
            .SelectMany(owner => Glyphs(VisualTreeHelper.GetDrawing(owner)).Select(run => (Owner: owner, Run: run))).Single();
        var numberBaseline = number.Owner.TransformToAncestor(editor).Transform(number.Run.BaselineOrigin);
        var unitBaseline = unit.TransformToAncestor(editor).Transform(Glyphs(VisualTreeHelper.GetDrawing(unit)).Single().BaselineOrigin);
        TestAssertions.AssertEqual(true, Math.Abs(numberBaseline.Y - unitBaseline.Y) < .01,
            $"{context}: centering the native edit draft must preserve the separate unit baseline");
    }

    private static string Font(DependencyObject element) => $"{element.GetValue(TextBlock.FontFamilyProperty)}/{element.GetValue(TextBlock.FontSizeProperty)}/{element.GetValue(TextBlock.FontWeightProperty)}/{element.GetValue(TextBlock.FontStyleProperty)}";
    private static Rect Bounds(FrameworkElement element, UIElement root) => new(element.TranslatePoint(new Point(), root), element.RenderSize);

    private static void DumpGlyphs(Visual visual, UIElement root, string name)
    {
        foreach (var glyph in Glyphs(VisualTreeHelper.GetDrawing(visual)))
            Console.WriteLine($"GLYPH {name} baseline={visual.TransformToAncestor(root).Transform(glyph.BaselineOrigin)} em={glyph.FontRenderingEmSize} family={glyph.GlyphTypeface.FamilyNames.Values.First()} weight={glyph.GlyphTypeface.Weight} lineBounds={glyph.ComputeAlignmentBox()} inkBounds={glyph.ComputeInkBoundingBox()}");
        foreach (var child in Visuals(visual).OfType<Visual>())
            foreach (var glyph in Glyphs(VisualTreeHelper.GetDrawing(child)))
                Console.WriteLine($"GLYPH {name} baseline={child.TransformToAncestor(root).Transform(glyph.BaselineOrigin)} em={glyph.FontRenderingEmSize} family={glyph.GlyphTypeface.FamilyNames.Values.First()} weight={glyph.GlyphTypeface.Weight} lineBounds={glyph.ComputeAlignmentBox()} inkBounds={glyph.ComputeInkBoundingBox()}");
    }

    private static IEnumerable<GlyphRun> Glyphs(Drawing? drawing)
    {
        if (drawing is GlyphRunDrawing glyph) yield return glyph.GlyphRun;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var run in Glyphs(child)) yield return run;
    }

    private static void Save(FrameworkElement element, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var width = element.ActualWidth + 16;
        var height = element.ActualHeight + 16;
        const double dpi = 1.25;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi), (int)Math.Ceiling(height * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle((Brush)element.FindResource("SolidBackgroundFillColorBaseBrush"), null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.Fill }, null, new Rect(8, 8, element.ActualWidth, element.ActualHeight));
        }
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);

        var button = (Button)element.FindName("DisplayButton");
        var unit = (TextBlock)element.FindName("UnitText");
        var text = button.Content?.ToString() ?? "";
        var typeface = new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch);
        var reference = new FormattedText($"{text} {unit.Text}".TrimEnd(), CultureInfo.CurrentCulture, button.FlowDirection,
            typeface, button.FontSize, button.Foreground, dpi);
        if (unit.Text == "×") reference.SetFontSize((double)element.FindResource("CaptionTextBlockFontSize"), text.Length + 1, 1);
        var numberWidth = new FormattedText(text, CultureInfo.CurrentCulture, button.FlowDirection, typeface,
            button.FontSize, button.Foreground, dpi).Width;
        var comparison = new DrawingVisual();
        using (var context = comparison.RenderOpen())
        {
            context.DrawRectangle((Brush)element.FindResource("SolidBackgroundFillColorBaseBrush"), null, new Rect(0, 0, 250, 92));
            context.DrawText(new FormattedText("Compiled control", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, 12, button.Foreground, dpi), new Point(8, 8));
            context.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.Fill }, null, new Rect(130, 1, element.ActualWidth, element.ActualHeight));
            context.DrawText(new FormattedText("Single text phrase", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, 12, button.Foreground, dpi), new Point(8, 55));
            context.DrawText(reference, new Point(130 + 49 - numberWidth, 48 + (32 - reference.Height) / 2));
        }
        var comparisonBitmap = new RenderTargetBitmap(313, 115, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
        comparisonBitmap.Render(comparison);
        var comparisonEncoder = new PngBitmapEncoder();
        comparisonEncoder.Frames.Add(BitmapFrame.Create(comparisonBitmap));
        using var comparisonOutput = File.Create(Path.ChangeExtension(path, ".reference.png"));
        comparisonEncoder.Save(comparisonOutput);
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void PressKey(TextBox input, System.Windows.Input.Key key) => input.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, new FakePresentationSource(), 0, key) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
    private sealed class FakePresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
