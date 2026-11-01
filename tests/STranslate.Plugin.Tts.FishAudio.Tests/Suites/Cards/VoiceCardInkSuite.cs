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
using static TestAssertions;

internal static class VoiceCardInkSuite
{
    internal static async Task RunAsync(string? proofDirectory = null)
    {
        foreach (var testCase in CreateCases(proofDirectory))
            await testCase.Run();
        Console.WriteLine("Native voice-card outline and physical-raster tests passed at 100/125/150/200% DPI, Ideal/Display.");
    }

    internal static IReadOnlyList<(string Id, Func<Task> Run)> CreateCases(string? proofDirectory = null)
    {
        // Keep the captured fractional-DPI owner before every other root-DPI context.
        var cases = new List<(string Id, Func<Task> Run)>
        {
            ("cards.ink/captured-display125-search", () => RunOnStaThreadAsync(() => CheckRenderedCards(1.25, TextFormattingMode.Display, VoiceBrowseMode.Search, true, proofDirectory))),
        };
        foreach (var dpi in new[] { 1d, 1.25, 1.5, 2d })
        foreach (var format in new[] { TextFormattingMode.Ideal, TextFormattingMode.Display })
        foreach (var mode in new[] { VoiceBrowseMode.Search, VoiceBrowseMode.ById, VoiceBrowseMode.Favorites })
        {
            var scale = dpi switch { 1 => "1", 1.25 => "1-25", 1.5 => "1-5", _ => "2" };
            var browse = mode switch { VoiceBrowseMode.Search => "search", VoiceBrowseMode.ById => "by-id", _ => "favorites" };
            cases.Add(($"cards.ink/dpi-{scale}/{format.ToString().ToLowerInvariant()}/{browse}",
                () => RunOnStaThreadAsync(() => CheckRenderedCards(dpi, format, mode, false, proofDirectory))));
        }
        return cases;
    }

    internal static void CheckRenderedCards(double dpi, TextFormattingMode format, VoiceBrowseMode mode, bool capturedRegression, string? proofDirectory)
    {
        const string id = "0123456789abcdef0123456789abcdef";
        const string sample = "https://example.invalid/sample.mp3";
        var settings = new Settings { VoiceId = id, CachedVoice = new() { Title = "Selected", SampleAudioUrl = sample } };
        using var vm = new SettingsViewModel(CreateContext(settings: settings, pluginSettingsDirectoryPath: CreateTempDirectory()), settings);
        var item = new VoiceSearchItem { Id = id, Title = "Rendered result", SampleAudioUrl = sample };
        vm.SearchResults = [item];
        vm.ByIdResults = [item];
        vm.FavoriteResults = [item];
        vm.HasSearched = true;
        vm.BrowseMode = mode;
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = ApplicationTheme.Dark;
        ThemeManager.SetRequestedTheme(view, ElementTheme.Dark);
        // This headless STA has no Application/OS accent initialization. Supply an
        // opaque fixture accent for native controls; liked hearts use their fixed red.
        view.Resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF));
        view.UseLayoutRounding = true;
        view.SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(view, format);
        var setDpi = typeof(VisualTreeHelper).GetMethod("SetRootDpi", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        AssertNotNull(setDpi, "The physical-raster fixture must set the actual WPF visual-root DPI");
        setDpi!.Invoke(null, [view, new DpiScale(dpi, dpi)]);
        SettingsLayoutSuite.Layout(view);
        var selectedHeart = Logical(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.ToggleSelectedVoiceFavoriteCommand));
        foreach (var state in new[] { "idle", "filled/Tick", "playing", "preparing" })
        {
            // Exercise the selected facade's real persisted-ID state as well as list state.
            // Keep mutation refresh offline; restore the deliberately supplied active card.
            vm.BrowseMode = VoiceBrowseMode.Search;
            if (vm.IsSelectedVoiceFavorite != (state == "filled/Tick"))
                vm.ToggleSelectedVoiceFavoriteCommand.Execute(null);
            vm.FavoriteResults = [item];
            vm.BrowseMode = mode;
            item.SampleAudioUrl = sample;
            item.HasMetadata = true;
            item.IsApplied = state != "filled/Tick";
            item.IsFavorite = state == "filled/Tick";
            SettingsLayoutSuite.Layout(view);
            var resultHeart = Visuals(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.ToggleFavoriteCommand) && ReferenceEquals(button.CommandParameter, item) && IsShown(button));
            var rows = new[] { (StackPanel)selectedHeart.Parent, (StackPanel)resultHeart.Parent };
            vm.IsDisplayVoicePreviewing = item.IsBeingPreviewed = state == "playing";
            vm.IsDisplayVoicePreparing = item.IsPreparingPreview = state == "preparing";
            SettingsLayoutSuite.Layout(view);
            try
            {
                foreach (var row in rows)
                {
                    AvatarPreviewSuite.SetHover(AvatarPreviewSuite.Card(row), true);
                    var heart = (System.Windows.Shapes.Path)row.Children.OfType<Button>().First().Content;
                    heart.ClearValue(System.Windows.Shapes.Shape.FillProperty);
                    heart.ClearValue(System.Windows.Shapes.Shape.StrokeProperty);
                    AssertEqual(state == "filled/Tick", ((SolidColorBrush)heart.Fill).Color.A != 0,
                        "Both selected and browse heart fill must follow their own Like state before raster isolation");
                    heart.Stroke = Brushes.White;
                    if (heart.Fill is SolidColorBrush fill && fill.Color.A != 0) heart.Fill = Brushes.White;
                    foreach (var glyph in Visuals(AvatarPreviewSuite.Card(row)).OfType<InkCenteredGlyph>()) glyph.Foreground = Brushes.White;
                    foreach (var play in Visuals(Preview(row)).OfType<System.Windows.Shapes.Path>()) play.Fill = Brushes.White;
                    foreach (var stop in Visuals(Preview(row)).OfType<Border>().Where(border => border.Width is 9 or 10)) stop.Background = Brushes.White;
                }
                SettingsLayoutSuite.Layout(view);
                foreach (var row in rows)
                    AssertEqual(1d, Preview(row).Opacity, "The injected hover fixture must expose actual compiled preview artwork before ink measurement");
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * dpi), (int)Math.Ceiling(view.ActualHeight * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
                bitmap.Render(view);
                if (capturedRegression && state == "idle")
                    AssertRasterDetectsDisplacedGlyph(view, rows[1], bitmap, dpi);
                foreach (var row in rows)
                {
                    var location = ReferenceEquals(row, rows[0]) ? "Selected" : mode.ToString();
                    var heart = (System.Windows.Shapes.Path)row.Children.OfType<Button>().First().Content;
                    var preview = Preview(row);
                    var action = Visuals(row.Children.OfType<Button>().Last()).OfType<InkCenteredGlyph>().Single();
                    var expected = row.TranslatePoint(new Point(0, row.ActualHeight / 2), view).Y * dpi;
                    var avatarCenter = preview.TranslatePoint(new Point(preview.ActualWidth / 2, preview.ActualHeight / 2), view).Y * dpi;
                    var heartCenter = RasterCenter(bitmap, heart, view, dpi);
                    var actionCenter = RasterCenter(bitmap, action, view, dpi);
                    var context = $"{dpi * 100}%/{format}/{location}/{state}";
                    var heartFrameCenter = heart.TranslatePoint(new Point(0, heart.ActualHeight / 2), view).Y * dpi;
                    AssertEqual(true, Math.Abs(heartFrameCenter - expected) < .00001,
                        $"{context}: the aspect-preserved heart frame centers exactly on its title row");
                    // A raster's outer pixel bounds quantize to half-pixel centers. This is a physical
                    // sampling limit; the exact outline below must still be centered without tolerance.
                    AssertEqual(true, Math.Abs(actionCenter - heartCenter) <= .50001, $"{context}: Tick/Cross and heart raster centers must differ by at most one half physical pixel");
                    foreach (var glyph in Visuals(row).OfType<InkCenteredGlyph>().Where(IsShown))
                    {
                        var drawing = VisualTreeHelper.GetDrawing(glyph)!;
                        var center = glyph.TranslatePoint(new Point(0, drawing.Bounds.Top + drawing.Bounds.Height / 2), view).Y * dpi;
                        AssertEqual(true, Math.Abs(center - expected) < .00001, $"{context}: the native action outline must center exactly on its title row");
                        AssertNativeOutlineUnchanged(glyph, drawing);
                        AssertEqual(true, Math.Abs(RasterCenter(bitmap, glyph, view, dpi) - heartCenter) <= .50001, $"{context}: glyph and heart raster centers must differ by at most one half physical pixel");
                    }
                    foreach (var glyph in Visuals(preview).OfType<InkCenteredGlyph>().Where(IsShown))
                    {
                        var drawing = VisualTreeHelper.GetDrawing(glyph)!;
                        var center = glyph.TranslatePoint(new Point(0, drawing.Bounds.Top + drawing.Bounds.Height / 2), view).Y * dpi;
                        AssertEqual(true, Math.Abs(center - avatarCenter) < .00001, $"{context}: Play outline centers exactly on its avatar overlay");
                        AssertNativeOutlineUnchanged(glyph, drawing);
                        AssertEqual(true, Math.Abs(RasterCenter(bitmap, glyph, view, dpi) - avatarCenter) <= .50001, $"{context}: Play keeps the half-physical-pixel avatar bound");
                    }
                    var play = Visuals(preview).OfType<System.Windows.Shapes.Path>().Single();
                    AssertEqual(state is "idle" or "filled/Tick", IsShown(play), $"{context}: only idle state uses the current filled Play contour");
                    if (IsShown(play))
                    {
                        var bounds = VisualTreeHelper.GetDrawing(play)!.Bounds;
                        var center = play.TranslatePoint(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2), view);
                        var owner = preview.TranslatePoint(new Point(preview.ActualWidth / 2, preview.ActualHeight / 2), view);
                        var frameCenter = play.TranslatePoint(new Point(play.ActualWidth / 2, play.ActualHeight / 2), view);
                        AssertEqual(true, Math.Abs(frameCenter.X - owner.X) * dpi < .00001 && Math.Abs(frameCenter.Y - owner.Y) * dpi < .00001,
                            $"{context}: filled Play frame centers exactly on its avatar owner");
                        AssertEqual(true, Math.Abs(center.X - owner.X) * dpi < .00001 && Math.Abs(center.Y - owner.Y) * dpi < .00001,
                            $"{context}: filled Play ink centers exactly on its avatar owner");
                        AssertEqual(true, Math.Abs(RasterCenter(bitmap, play, view, dpi) - avatarCenter) <= .50001,
                            $"{context}: filled Play keeps the unchanged half-physical-pixel avatar bound");
                    }
                    foreach (var stop in Visuals(preview).OfType<Border>().Where(border => IsShown(border) && border.Width is 9 or 10))
                        AssertEqual(true, Math.Abs(RasterCenter(bitmap, stop, view, dpi) - avatarCenter) <= .50001, $"{context}: Stop raster must retain the preview center");
                    if (capturedRegression && ReferenceEquals(row, rows[0]) && state == "idle")
                        CheckLegacyReference(view, dpi, proofDirectory);
                    Console.WriteLine($"INK {context} avatar={avatarCenter:F3} title={expected:F3} heart={heartCenter:F3} action={actionCenter:F3}");
                }
                if (proofDirectory is not null && state == "idle" && mode == VoiceBrowseMode.Search && format == TextFormattingMode.Display && (capturedRegression || dpi == 1))
                    CheckLegacyReference(view, dpi, proofDirectory);
            }
            finally
            {
                foreach (var row in rows) AvatarPreviewSuite.SetHover(AvatarPreviewSuite.Card(row), false);
            }
        }
    }

    private static Grid Preview(StackPanel row) => AvatarPreviewSuite.Overlay(Visuals(AvatarPreviewSuite.Card(row)).OfType<Button>().Single(button =>
        BindingOperations.GetBinding(button, Button.CommandProperty)?.Path.Path.EndsWith("PreviewCommand") == true));

    private static void CheckLegacyReference(SettingsView view, double dpi, string? proofDirectory)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, UseLayoutRounding = false, HorizontalAlignment = HorizontalAlignment.Right };
        var heart = new System.Windows.Shapes.Path { Width = 18, Stretch = Stretch.Uniform, StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round, Stroke = Brushes.White, Fill = Brushes.Transparent, Data = (Geometry)view.FindResource("VoiceLikeHeartGeometry") };
        row.Children.Add(new Button { Width = 32, Height = 32, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(6), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Content = heart });
        var preview = new Grid { Width = 36, Height = 36, Margin = new Thickness(0, 0, 4, 0) };
        preview.Children.Add(new System.Windows.Shapes.Ellipse { StrokeThickness = 1, Stroke = Brushes.Gray });
        preview.Children.Add(new InkCenteredGlyph { Glyph = "\uE768", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13,
            Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(preview);
        row.Children.Add(new Button { Style = (Style)view.FindResource("BareIconButtonStyle"), Content = "\uE711", Foreground = Brushes.White });
        var backdrop = new Border { Background = Brushes.Black, Child = row, HorizontalAlignment = HorizontalAlignment.Right, UseLayoutRounding = false };
        var panel = (Panel)view.Content;
        panel.Children.Add(backdrop);
        try
        {
            SettingsLayoutSuite.Layout(view);
            foreach (var glyph in Visuals(row).OfType<InkCenteredGlyph>())
                AssertNativeOutlineUnchanged(glyph, VisualTreeHelper.GetDrawing(glyph)!);
            var referenceBitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * dpi), (int)Math.Ceiling(view.ActualHeight * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            referenceBitmap.Render(view);
            AssertRecordedCompositePhase(view, row, referenceBitmap, dpi, proofDirectory);
            if (proofDirectory is not null) SaveComparison(view, row, dpi, proofDirectory);
        }
        finally { panel.Children.Remove(backdrop); SettingsLayoutSuite.Layout(view); }
    }

    private static void AssertRecordedCompositePhase(SettingsView view, StackPanel row, RenderTargetBitmap actualBitmap,
        double dpi, string? proofDirectory)
    {
        // The independent legacy fixture retains the original common row and sampling phase.
        // The historical exact full-composite classifier was captured at an integer
        // common-center phase. The title-row reflow changes the row's absolute phase;
        // retain that capture in a separate original reference strip without moving product layout.
        const double recordedCommonCenterPixels = 847;
        var heart = (System.Windows.Shapes.Path)row.Children.OfType<Button>().First().Content;
        var preview = row.Children.OfType<Grid>().Single();
        var action = Visuals(row.Children.OfType<Button>().Last()).OfType<InkCenteredGlyph>().Single();
        var actualCenter = preview.TranslatePoint(new Point(0, preview.ActualHeight / 2), view).Y * dpi;
        Console.WriteLine($"INK-LEGACY-REFERENCE-PHASE center={actualCenter:R}, heart-alpha={RasterCenter(actualBitmap, heart, view, dpi):R}, action-alpha={RasterCenter(actualBitmap, action, view, dpi):R}, heart-RGB={RasterCenter(actualBitmap, heart, view, dpi, false):R}, action-RGB={RasterCenter(actualBitmap, action, view, dpi, false):R}");
        var referenceHost = (Border)row.Parent;
        var previous = referenceHost.RenderTransform;
        try
        {
            referenceHost.RenderTransform = new TranslateTransform(0, (recordedCommonCenterPixels - actualCenter) / dpi);
            SettingsLayoutSuite.Layout(view);
            var recordedCenter = preview.TranslatePoint(new Point(0, preview.ActualHeight / 2), view).Y * dpi;
            AssertEqual(true, Math.Abs(recordedCenter - recordedCommonCenterPixels) < .00001,
                "The historical 125% Display composite fixture must use its recorded integer common-center phase");
            var recorded = new RenderTargetBitmap(actualBitmap.PixelWidth, actualBitmap.PixelHeight, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            recorded.Render(view);
            // Preserve the fixed-crop sensitivity oracle at the recorded phase too.
            AssertRasterDetectsDisplacedGlyph(view, row, recorded, dpi);
            var heartCenter = RasterCenter(recorded, heart, view, dpi, false);
            var crossCenter = RasterCenter(recorded, action, view, dpi, false);
            AssertEqual(true, Math.Abs(heartCenter - crossCenter) <= .50001,
                "The captured-phase reference heart/Cross contours retain the half-physical-pixel bound");
            Console.WriteLine($"INK-CAPTURED-HEART center={recordedCenter:R}, heart-RGB={heartCenter:R}, action-RGB={crossCenter:R}");
            // Retain the original exact Cross/star oracle with its original contour.
            // The product heart has a different RGB-threshold edge classification.
            var productGeometry = heart.Data;
            var productHeight = heart.Height;
            var productStretch = heart.Stretch;
            try
            {
                heart.Data = Geometry.Parse("M 12,2 L 14.94,8.04 L 21.61,9.02 L 16.8,13.72 L 17.93,20.36 L 12,17.22 L 6.07,20.36 L 7.2,13.72 L 2.39,9.02 L 9.06,8.04 Z");
                heart.Height = 18;
                heart.Stretch = Stretch.Fill;
                SettingsLayoutSuite.Layout(view);
                var historical = new RenderTargetBitmap(actualBitmap.PixelWidth, actualBitmap.PixelHeight, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
                historical.Render(view);
                AssertRasterDetectsDisplacedGlyph(view, row, historical, dpi);
                AssertEqual(RasterCenter(historical, heart, view, dpi, false), RasterCenter(historical, action, view, dpi, false),
                    "The original captured 125% Display Cross/star physical raster offset must be eliminated");
                Console.WriteLine($"INK-CAPTURED-REFERENCE-STAR center={recordedCenter:R}, star-RGB={RasterCenter(historical, heart, view, dpi, false):R}, action-RGB={RasterCenter(historical, action, view, dpi, false):R}");
            }
            finally
            {
                heart.Data = productGeometry;
                heart.Height = productHeight;
                heart.Stretch = productStretch;
                SettingsLayoutSuite.Layout(view);
            }
            if (proofDirectory is not null)
            {
                Directory.CreateDirectory(proofDirectory);
                var origin = row.TranslatePoint(new Point(), view);
                var crop = new CroppedBitmap(recorded, new Int32Rect((int)Math.Floor(origin.X * dpi), (int)Math.Floor(origin.Y * dpi),
                    (int)Math.Ceiling(row.ActualWidth * dpi), (int)Math.Ceiling(row.ActualHeight * dpi)));
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(crop));
                using var output = File.Create(Path.Combine(proofDirectory, "historical-captured-phase-125-Display.png"));
                encoder.Save(output);
            }
        }
        finally
        {
            referenceHost.RenderTransform = previous;
            SettingsLayoutSuite.Layout(view);
            var restoredCenter = preview.TranslatePoint(new Point(0, preview.ActualHeight / 2), view).Y * dpi;
            AssertEqual(true, Math.Abs(restoredCenter - actualCenter) < .00001, "Captured fixture restores its independent legacy reference phase");
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
