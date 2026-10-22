using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Controls;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static StaTestHost;
using static TestAssertions;

internal static class VoiceCardInkSuite
{
    internal static async Task RunAsync(string? proofDirectory = null)
    {
        // Run the captured fractional-DPI failure first, before WPF caches any other root DPI.
        await RunOnStaThreadAsync(() => CheckRenderedCards(1.25, TextFormattingMode.Display, VoiceBrowseMode.Search, true, proofDirectory));
        foreach (var dpi in new[] { 1d, 1.25, 1.5, 2d })
        foreach (var format in new[] { TextFormattingMode.Ideal, TextFormattingMode.Display })
        foreach (var mode in new[] { VoiceBrowseMode.Search, VoiceBrowseMode.ById, VoiceBrowseMode.Favorites })
            await RunOnStaThreadAsync(() => CheckRenderedCards(dpi, format, mode, false, proofDirectory));
        Console.WriteLine("Native voice-card outline and physical-raster tests passed at 100/125/150/200% DPI, Ideal/Display.");
    }

    private static void CheckRenderedCards(double dpi, TextFormattingMode format, VoiceBrowseMode mode, bool capturedRegression, string? proofDirectory)
    {
        const string id = "0123456789abcdef0123456789abcdef";
        const string sample = "https://example.invalid/sample.mp3";
        var settings = new Settings { VoiceId = id, CachedVoice = new() { Title = "Selected", SampleAudioUrl = sample } };
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var item = new VoiceSearchItem { Id = id, Title = "Rendered result", SampleAudioUrl = sample };
        vm.SearchResults = [item];
        vm.ByIdResults = [item];
        vm.FavoriteResults = [item];
        vm.HasSearched = true;
        vm.BrowseMode = mode;
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = ApplicationTheme.Dark;
        ThemeManager.SetRequestedTheme(view, ElementTheme.Dark);
        view.UseLayoutRounding = true;
        view.SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(view, format);
        var setDpi = typeof(VisualTreeHelper).GetMethod("SetRootDpi", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        AssertNotNull(setDpi, "The physical-raster fixture must set the actual WPF visual-root DPI");
        setDpi!.Invoke(null, [view, new DpiScale(dpi, dpi)]);
        SettingsLayoutSuite.Layout(view);
        var selectedStar = Logical(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.ToggleSelectedVoiceFavoriteCommand));
        var resultStar = Visuals(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.ToggleFavoriteCommand) && ReferenceEquals(button.CommandParameter, item) && IsShown(button));
        var rows = new[] { (StackPanel)selectedStar.Parent, (StackPanel)resultStar.Parent };
        foreach (var state in new[] { "idle", "filled/Tick", "playing", "preparing" })
        {
            item.IsApplied = state != "filled/Tick";
            item.IsFavorite = state == "filled/Tick";
            vm.IsDisplayVoicePreviewing = item.IsBeingPreviewed = state == "playing";
            vm.IsDisplayVoicePreparing = item.IsPreparingPreview = state == "preparing";
            SettingsLayoutSuite.Layout(view);
            foreach (var row in rows)
            {
                var star = (System.Windows.Shapes.Path)row.Children.OfType<Button>().First().Content;
                star.Stroke = Brushes.White;
                if (star.Fill is SolidColorBrush fill && fill.Color.A != 0) star.Fill = Brushes.White;
                foreach (var glyph in Visuals(row).OfType<InkCenteredGlyph>()) glyph.Foreground = Brushes.White;
            }
            SettingsLayoutSuite.Layout(view);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * dpi), (int)Math.Ceiling(view.ActualHeight * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            bitmap.Render(view);
            if (capturedRegression && state == "idle")
                AssertRasterDetectsDisplacedGlyph(view, rows[1], bitmap, dpi);
            foreach (var row in rows)
            {
                var location = ReferenceEquals(row, rows[0]) ? "Selected" : mode.ToString();
                var star = (System.Windows.Shapes.Path)row.Children.OfType<Button>().First().Content;
                var preview = row.Children.OfType<Grid>().Single();
                var action = Visuals(row.Children.OfType<Button>().Last()).OfType<InkCenteredGlyph>().Single();
                var expected = preview.TranslatePoint(new Point(0, preview.ActualHeight / 2), view).Y * dpi;
                var starCenter = RasterCenter(bitmap, star, view, dpi);
                var actionCenter = RasterCenter(bitmap, action, view, dpi);
                var context = $"{dpi * 100}%/{format}/{location}/{state}";
                // A raster's outer pixel bounds quantize to half-pixel centers. This is a physical
                // sampling limit; the exact outline below must still be centered without tolerance.
                AssertEqual(true, Math.Abs(actionCenter - starCenter) <= .50001, $"{context}: Tick/Cross and star raster centers must differ by at most one half physical pixel");
                if (capturedRegression && ReferenceEquals(row, rows[0]) && state == "idle")
                    AssertEqual(RasterCenter(bitmap, star, view, dpi, false), RasterCenter(bitmap, action, view, dpi, false), "The reproduced 125% Display Cross/star physical raster offset must be eliminated");
                foreach (var glyph in Visuals(row).OfType<InkCenteredGlyph>().Where(IsShown))
                {
                    var drawing = VisualTreeHelper.GetDrawing(glyph)!;
                    var center = glyph.TranslatePoint(new Point(0, drawing.Bounds.Top + drawing.Bounds.Height / 2), view).Y * dpi;
                    AssertEqual(true, Math.Abs(center - expected) < .00001, $"{context}: the native glyph outline must center exactly on the preview");
                    AssertNativeOutlineUnchanged(glyph, drawing);
                    AssertEqual(true, Math.Abs(RasterCenter(bitmap, glyph, view, dpi) - starCenter) <= .50001, $"{context}: glyph and star raster centers must differ by at most one half physical pixel");
                }
                foreach (var stop in Visuals(preview).OfType<Border>().Where(border => IsShown(border) && border.Background is SolidColorBrush brush && brush.Color == Colors.White))
                    AssertEqual(true, Math.Abs(RasterCenter(bitmap, stop, view, dpi) - starCenter) <= .50001, $"{context}: Stop raster must retain the preview center");
                Console.WriteLine($"INK {context} preview={expected:F3} star={starCenter:F3} action={actionCenter:F3}");
            }
            if (proofDirectory is not null && state == "idle" && mode == VoiceBrowseMode.Search && format == TextFormattingMode.Display && (capturedRegression || dpi == 1))
                SaveComparison(view, rows[0], dpi, proofDirectory);
        }
    }

    private static void AssertRasterDetectsDisplacedGlyph(SettingsView view, StackPanel row, RenderTargetBitmap baseline, double dpi)
    {
        var glyph = Visuals(row.Children.OfType<Button>().Last()).OfType<InkCenteredGlyph>().Single();
        var cropOrigin = glyph.TranslatePoint(new Point(-2, -2), view);
        var center = RasterCenter(baseline, glyph, view, dpi);
        var previous = glyph.RenderTransform;
        try
        {
            glyph.RenderTransform = new TranslateTransform(0, 3 / dpi);
            SettingsLayoutSuite.Layout(view);
            var displaced = new RenderTargetBitmap(baseline.PixelWidth, baseline.PixelHeight, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            displaced.Render(view);
            var moved = RasterCenter(displaced, glyph, view, dpi, true, cropOrigin);
            AssertEqual(true, Math.Abs(moved - center - 3) <= .5,
                $"The raster classifier must detect a deliberately displaced glyph inside a FIXED crop, not count the background: before={center}, after={moved}");
            Console.WriteLine($"SENSITIVITY fixed-crop native glyph displaced +3 physical pixels: detected {moved - center:F3}");
        }
        finally
        {
            glyph.RenderTransform = previous;
            SettingsLayoutSuite.Layout(view);
        }
    }

    private static void SaveComparison(SettingsView view, StackPanel row, double dpi, string directory)
    {
        Directory.CreateDirectory(directory);
        Save("after");
        var action = row.Children.OfType<Button>().Last();
        var template = action.Template;
        var play = Visuals(row.Children.OfType<Grid>().Single()).OfType<InkCenteredGlyph>().Single();
        var parent = (Grid)VisualTreeHelper.GetParent(play);
        var index = parent.Children.IndexOf(play);
        var legacy = new TextBlock { Text = play.Glyph, FontFamily = play.FontFamily, FontSize = play.FontSize, Foreground = play.Foreground, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        // Recreate the previous idle template's centered ContentPresenter and native
        // TextBlock; focus/hover surfaces are irrelevant to this resting-row comparison.
        action.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>");
        parent.Children.RemoveAt(index);
        parent.Children.Insert(index, legacy);
        row.UseLayoutRounding = true;
        SettingsLayoutSuite.Layout(view);
        Save("before");
        row.UseLayoutRounding = false;
        action.Template = template;
        parent.Children.RemoveAt(index);
        parent.Children.Insert(index, play);
        SettingsLayoutSuite.Layout(view);

        void Save(string state)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * dpi), (int)Math.Ceiling(view.ActualHeight * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var origin = row.TranslatePoint(new Point(), view);
            var crop = new CroppedBitmap(bitmap, new Int32Rect((int)Math.Floor(origin.X * dpi), (int)Math.Floor(origin.Y * dpi), (int)Math.Ceiling(row.ActualWidth * dpi), (int)Math.Ceiling(row.ActualHeight * dpi)));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(crop));
            using var output = File.Create(System.IO.Path.Combine(directory, $"glyph-{dpi * 100:0}-Display-{state}.png"));
            encoder.Save(output);
        }
    }

    private static void AssertNativeOutlineUnchanged(InkCenteredGlyph glyph, Drawing drawing)
    {
        var typeface = new Typeface(glyph.FontFamily, glyph.FontStyle, glyph.FontWeight, glyph.FontStretch);
        AssertEqual(true, typeface.TryGetGlyphTypeface(out var font), "Native icon typeface must resolve");
        var native = font.GetGlyphOutline(font.CharacterToGlyphMap[glyph.Glyph[0]], glyph.FontSize, glyph.FontSize);
        var painted = Drawings(drawing).OfType<GeometryDrawing>().Single();
        AssertEqual(native.ToString(System.Globalization.CultureInfo.InvariantCulture), painted.Geometry.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Centering must preserve the exact original native glyph contour and em size");
    }

    private static double RasterCenter(RenderTargetBitmap bitmap, FrameworkElement icon, Visual root, double dpi, bool isolate = true, Point? fixedCropOrigin = null)
    {
        var origin = fixedCropOrigin ?? icon.TranslatePoint(new Point(-2, -2), (UIElement)root);
        var rect = new Int32Rect((int)Math.Floor(origin.X * dpi), (int)Math.Floor(origin.Y * dpi), (int)Math.Ceiling((icon.ActualWidth + 4) * dpi), (int)Math.Ceiling((icon.ActualHeight + 4) * dpi));
        var pixels = new byte[rect.Width * rect.Height * 4];
        if (isolate)
        {
            // Render the actual drawing on transparency at its original root position.
            // Copying alpha from the full view would count the opaque card background.
            var ink = new DrawingVisual();
            using (var context = ink.RenderOpen())
            {
                var position = icon.TranslatePoint(new Point(), (UIElement)root);
                context.PushTransform(new TranslateTransform(position.X, position.Y));
                context.DrawDrawing(VisualTreeHelper.GetDrawing(icon));
                context.Pop();
            }
            var isolated = new RenderTargetBitmap(bitmap.PixelWidth, bitmap.PixelHeight, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            isolated.Render(ink);
            isolated.CopyPixels(rect, pixels, rect.Width * 4, 0);
        }
        else bitmap.CopyPixels(rect, pixels, rect.Width * 4, 0);
        var top = rect.Height;
        var bottom = -1;
        for (var y = 0; y < rect.Height; y++)
        for (var x = 0; x < rect.Width; x++)
        {
            var offset = (y * rect.Width + x) * 4;
            if (isolate ? pixels[offset + 3] == 0 : Math.Min(pixels[offset], Math.Min(pixels[offset + 1], pixels[offset + 2])) < 140) continue;
            top = Math.Min(top, y);
            bottom = Math.Max(bottom, y);
        }
        AssertEqual(true, bottom >= top, "The physical-raster assertion must find actual visible pixels");
        return rect.Y + (top + bottom + 1) / 2d;
    }

    private static bool IsShown(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is FrameworkElement; current = VisualTreeHelper.GetParent(current))
            if (((FrameworkElement)current).Visibility != Visibility.Visible) return false;
        return true;
    }

    private static IEnumerable<Drawing> Drawings(Drawing drawing)
    {
        yield return drawing;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
            foreach (var nested in Drawings(child)) yield return nested;
    }

    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in Logical(child)) yield return nested;
        }
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var nested in Visuals(child)) yield return nested;
        }
    }
}
