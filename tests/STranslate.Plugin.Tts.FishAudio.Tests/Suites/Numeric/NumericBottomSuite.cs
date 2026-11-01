using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using iNKORE.UI.WPF.Modern;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static TestAssertions;

// Independent observed native contours and opaque native-glyph raster. No product metric calls.
internal static class NumericBottomSuite
{
    internal static async Task RunAsync(string? directory)
    {
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            var assembly = typeof(InlineNumericEditor).Assembly;
            File.WriteAllText(Path.Combine(directory, "assembly.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                boundary = "Offline compiled native controls; not installed-host acceptance",
                sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assembly.Location))),
                mvid = assembly.ManifestModule.ModuleVersionId,
                classifier = "opaque native GlyphRun draws, alpha >0 support; alpha>=128 characterized separately; fixed editor-local 128x48 DIP crop",
            }));
        }
        var failures = new List<string>();
        foreach (var (culture, dark, dpi) in Contexts())
            await StaTestHost.RunOnStaThreadAsync(() => Measure(culture, dark, dpi, directory, failures));
        AssertEqual("", string.Join("\n", failures), "Visible numeric and unit bottoms must align");
    }

    internal static IReadOnlyList<(string Id, Func<Task> Run)> CreateCases()
    {
        var cases = new List<(string Id, Func<Task> Run)>();
        foreach (var (culture, dark, dpi) in Contexts())
        {
            var scale = dpi switch { 1 => "1", 1.25 => "1-25", _ => "1-5" };
            cases.Add(($"numeric.bottom/{culture.ToLowerInvariant()}/{(dark ? "dark" : "light")}/dpi-{scale}",
                () => StaTestHost.RunOnStaThreadAsync(() =>
                {
                    var failures = new List<string>();
                    Measure(culture, dark, dpi, null, failures);
                    AssertEqual("", string.Join("\n", failures), "Visible numeric and unit bottoms must align");
                })));
        }
        return cases;
    }

    private static IEnumerable<(string Culture, bool Dark, double Dpi)> Contexts()
    {
        foreach (var culture in new[] { "en-US", "de-DE" })
        foreach (var dark in new[] { false, true })
        foreach (var dpi in new[] { 1d, 1.25, 1.5 })
            yield return (culture, dark, dpi);
    }

    internal static void Measure(string culture, bool dark, double dpi, string? directory, List<string> failures)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var settings = new Settings { Speed = .87, Volume = -2 };
            var pluginContext = ContextProxy.CreateContext(settings: settings);
            var storage = (ContextProxy)(object)pluginContext;
            using var vm = new SettingsViewModel(pluginContext, settings);
            var view = SettingsLayoutSuite.CreateThemedView(vm);
            view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
            ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light);
            SettingsLayoutSuite.Layout(view);
            var editors = Descendants(view).OfType<InlineNumericEditor>().Where(e => !string.IsNullOrEmpty(e.Unit)).ToArray();
            foreach (var editor in editors)
            {
                ((Button)editor.FindName("DisplayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                SettingsLayoutSuite.Layout(view);
                editor.CommitEdit();
            }
            typeof(VisualTreeHelper).GetMethod("SetRootDpi", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [view, new DpiScale(dpi, dpi)]);
            foreach (var sample in new[] { (Speed: .87, Volume: -2d), (Speed: 1d, Volume: -10d), (Speed: .5, Volume: -.01), (Speed: .87, Volume: 0d) })
            {
                vm.Speed = sample.Speed; vm.Volume = sample.Volume;
                SettingsLayoutSuite.Layout(view);
                foreach (var editor in editors)
                {
                    var button = (Button)editor.FindName("DisplayButton");
                    var label = Descendants(button).OfType<TextBlock>().Single();
                    var unit = (TextBlock)editor.FindName("UnitText");
                    var context = $"{culture}/{dark}/{dpi}/{unit.Text}/{label.Text}";
                    AssertEqual(dpi, VisualTreeHelper.GetDpi(view).PixelsPerDip, context + ": root actual DPI");
                    AssertEqual(dpi, VisualTreeHelper.GetDpi(label).PixelsPerDip, context + ": number actual DPI");
                    AssertEqual(dpi, VisualTreeHelper.GetDpi(unit).PixelsPerDip, context + ": unit actual DPI");
                    Check(label, unit, editor, context + "/rest", directory, failures);
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    SettingsLayoutSuite.Layout(view);
                    var input = (TextBox)editor.FindName("EditorBox");
                    var viewport = Descendants(input).OfType<Visual>().Single(e => e.GetType().Name == "TextBoxView");
                    AssertEqual(dpi, VisualTreeHelper.GetDpi(viewport).PixelsPerDip, context + ": editor actual DPI");
                    Check(viewport, unit, editor, context + "/edit", directory, failures);
                    editor.CommitEdit();
                }
            }
            foreach (var editor in editors)
            {
                var button = (Button)editor.FindName("DisplayButton");
                var unit = (TextBlock)editor.FindName("UnitText");
                var beforeSaves = storage.SaveCount;
                var committed = editor.Value;
                foreach (var mode in new[] { "unchanged", "family", "style", "formatting", "flow", "compound" })
                {
                    editor.ClearValue(Control.FontFamilyProperty);
                    editor.ClearValue(Control.FontStyleProperty);
                    editor.ClearValue(Control.FontWeightProperty);
                    editor.ClearValue(FrameworkElement.FlowDirectionProperty);
                    editor.ClearValue(TextOptions.TextFormattingModeProperty);
                    button.ClearValue(Control.FontFamilyProperty);
                    ((TextBox)editor.FindName("EditorBox")).ClearValue(Control.FontFamilyProperty);
                    if (mode == "family")
                    {
                        button.FontFamily = new FontFamily("Arial");
                        ((TextBox)editor.FindName("EditorBox")).FontFamily = new FontFamily("Arial");
                    }
                    if (mode is "style" or "compound")
                    {
                        editor.FontStyle = FontStyles.Italic;
                        editor.FontWeight = FontWeights.SemiBold;
                    }
                    if (mode is "formatting" or "compound") TextOptions.SetTextFormattingMode(editor, TextFormattingMode.Display);
                    if (mode is "flow" or "compound") editor.FlowDirection = FlowDirection.RightToLeft;
                    if (mode == "compound") editor.FontFamily = new FontFamily("Segoe UI");
                    SettingsLayoutSuite.Layout(view);
                    var label = Descendants(button).OfType<TextBlock>().Single();
                    var context = $"runtime/{culture}/{dark}/{dpi}/{unit.Text}/{mode}";
                    Check(label, unit, editor, context + "/rest", directory, failures);
                    var anchor = ((TranslateTransform)unit.RenderTransform).Y;
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    SettingsLayoutSuite.Layout(view);
                    var input = (TextBox)editor.FindName("EditorBox");
                    var viewport = Descendants(input).OfType<Visual>().Single(e => e.GetType().Name == "TextBoxView");
                    foreach (var draft in unit.Text == "dB" ? new[] { "-10.00", "-0.01", "0.00" } : new[] { "0.50", "1.00", "0.87" })
                    {
                        input.Text = draft.Replace(".", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
                        SettingsLayoutSuite.Layout(view);
                        Check(viewport, unit, editor, context + "/draft-" + input.Text, directory, failures);
                    }
                    input.Text = button.Content.ToString();
                    SettingsLayoutSuite.Layout(view);
                    foreach (var partial in new[] { "", "-", ".", ",", "-.", "-," })
                    {
                        input.Text = partial;
                        SettingsLayoutSuite.Layout(view);
                        AssertEqual(partial, input.Text, context + ": alignment must preserve incomplete draft");
                        AssertEqual(anchor, ((TranslateTransform)unit.RenderTransform).Y, context + ": digit-free draft retains committed numeric anchor");
                        AssertEqual(committed, editor.Value, context + ": draft must remain isolated from setting");
                    }
                    input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new FakePresentationSource(), 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
                    SettingsLayoutSuite.Layout(view);
                    AssertEqual(committed, editor.Value, context + ": cancelling runtime drafts preserves value");
                    AssertEqual(beforeSaves, storage.SaveCount, context + ": runtime drafts must never save");
                }
            }
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    internal static void AssertAligned(Visual number, TextBlock unit, InlineNumericEditor editor, string context)
    {
        var failures = new List<string>();
        Check(number, unit, editor, context, null, failures);
        AssertEqual("", string.Join("; ", failures), context + ": independently observed visible bottoms");
    }

    private static void Check(Visual number, TextBlock unit, InlineNumericEditor editor, string context, string? directory, List<string> failures)
    {
        var dpi = VisualTreeHelper.GetDpi(editor).PixelsPerDip;
        var bottomNumber = OutlineBottom(number, editor);
        var bottomUnit = OutlineBottom(unit, editor);
        var numberRaster = Raster(number, editor, dpi, 0);
        var unitRaster = Raster(unit, editor, dpi, 0);
        var delta = bottomUnit - bottomNumber;
        var pixelDelta = Edge(unitRaster, 1) - Edge(numberRaster, 1);
        Console.WriteLine($"SUPPORT {context} number={Edge(numberRaster, 1)} unit={Edge(unitRaster, 1)} independentlyAlignedUnit={Edge(Raster(unit, editor, dpi, -delta), 1)}");
        var displayFormatting = TextOptions.GetTextFormattingMode(number) == TextFormattingMode.Display;
        Console.WriteLine($"BOTTOM {context} outlineNumber={bottomNumber:F6} outlineUnit={bottomUnit:F6} delta={delta:F6} alpha128Number={Edge(numberRaster)} alpha128Unit={Edge(unitRaster)} supportDelta={pixelDelta} formatting={TextOptions.GetTextFormattingMode(number)} font={NumericGlyphObservation.Describe(NumericGlyphObservation.Read(number, editor))}");
        // Declared before production: complete alpha support, not the rejected 50%-coverage edge.
        if (Math.Abs(delta) >= .01 || !displayFormatting && Math.Abs(pixelDelta) > 1)
            failures.Add($"{context}: outlineDelta={delta:F6}; physicalRowDelta={pixelDelta}");
        if (displayFormatting)
            Console.WriteLine($"DISPLAY-SUPPORT-LIMIT {context}: observed fixed-crop support difference={pixelDelta}px; exact outline required, no universal one-row native grid-fit claim");
        foreach (var displacement in new[] { -3d, 3d })
        {
            AssertEqual(false, Math.Abs(bottomUnit - delta + displacement / dpi - bottomNumber) < .01,
                context + ": displaced independently aligned outline must fail");
            var alignedRow = Edge(Raster(unit, editor, dpi, -delta), 1);
            var probeRow = Edge(Raster(unit, editor, dpi, -delta + displacement / dpi), 1);
            Console.WriteLine($"PROBE {context} alignedRow={alignedRow} numberRow={Edge(numberRaster, 1)} displacementPx={displacement} probeRow={probeRow}");
            AssertEqual(false, Math.Abs(probeRow - alignedRow) <= 1,
                context + ": fixed aligned-reference raster must detect +/-3px displacement in either formatting mode");
            if (!displayFormatting && Math.Abs(probeRow - Edge(numberRaster, 1)) <= 1)
                failures.Add(context + ": fixed +/-3 physical-pixel displacement did not fail number-versus-unit support bound");
        }
        if (directory is not null)
        {
            var name = context.Replace('/', '-').Replace(',', '_');
            Save(numberRaster, Path.Combine(directory, name + "-number.png"));
            Save(unitRaster, Path.Combine(directory, name + "-unit.png"));
            var composite = new DrawingVisual();
            using (var dc = composite.RenderOpen())
            {
                dc.DrawRectangle((Brush)editor.FindResource("SolidBackgroundFillColorBaseBrush"), null, new Rect(0, 0, 128, 48));
                dc.DrawRectangle(new VisualBrush(editor) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                    null, new Rect(0, 0, editor.ActualWidth, editor.ActualHeight));
            }
            var compositeBitmap = new RenderTargetBitmap((int)(128 * dpi), (int)(48 * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            compositeBitmap.Render(composite);
            Save(compositeBitmap, Path.Combine(directory, name + "-composite.png"));
        }
    }

    internal static double OutlineBottom(Visual owner, Visual root)
    {
        var bounds = Rect.Empty;
        foreach (var run in NumericGlyphObservation.Runs(owner, root))
            bounds.Union(new MatrixTransform(run.Transform).TransformBounds(run.Drawing.GlyphRun.BuildGeometry().Bounds));
        return bounds.Bottom;
    }

    private static RenderTargetBitmap Raster(Visual owner, Visual root, double dpi, double shift)
    {
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(0, shift));
            foreach (var run in NumericGlyphObservation.Runs(owner, root))
            {
                dc.PushTransform(new MatrixTransform(run.Transform));
                dc.DrawGlyphRun(Brushes.White, run.Drawing.GlyphRun);
                dc.Pop();
            }
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap((int)(128 * dpi), (int)(48 * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        return bitmap;
    }

    private static int Edge(BitmapSource bitmap, byte minimumAlpha = 128)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        for (var y = bitmap.PixelHeight - 1; y >= 0; y--)
        for (var x = 0; x < bitmap.PixelWidth; x++)
            if (pixels[(y * bitmap.PixelWidth + x) * 4 + 3] >= minimumAlpha) return y;
        throw new InvalidOperationException("Expected native numeric ink in the fixed crop");
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class FakePresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
