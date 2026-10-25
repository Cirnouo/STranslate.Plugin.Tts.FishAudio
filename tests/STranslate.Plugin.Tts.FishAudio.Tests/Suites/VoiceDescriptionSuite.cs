using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using iNKORE.UI.WPF.Modern;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static TestAssertions;

internal static class VoiceDescriptionSuite
{
    internal static async Task RunAsync(string? directory = null)
    {
        await SearchAdjustmentControlSuite.ObserveMatrixActionAsync("construction", stage =>
        {
            stage("VM construction begin");
            var vm = new SettingsViewModel(ContextProxy.CreateContext(), new Settings());
            try
            {
                stage("compiled view/native layout begin");
                SettingsLayoutSuite.Layout(SettingsLayoutSuite.CreateThemedView(vm));
                stage("compiled view/native layout completed");
            }
            finally
            {
                stage("VM cleanup begin"); vm.Dispose(); stage("VM cleanup completed");
            }
        }, prefix: "description", details: "ordinary 5s construction action.");
        foreach (var dark in new[] { false, true })
        foreach (var width in new[] { 400d, 424, 480, 640 })
        foreach (var mode in new[] { VoiceBrowseMode.Search, VoiceBrowseMode.ById, VoiceBrowseMode.Favorites })
            await SearchAdjustmentControlSuite.ObserveMatrixActionAsync($"{mode}/{width}/{(dark ? "dark" : "light")}",
                stage => Inspect(width, dark, mode, directory, stage), prefix: "description",
                details: "selected/active-mode pair; full runtime sequence at actual 125% root DPI.");
    }

    private static void Inspect(double width, bool dark, VoiceBrowseMode mode, string? directory, Action<string> stage)
    {
        stage("VM/settings setup begin");
        const string id = "0123456789abcdef0123456789abcdef";
        var settings = new Settings { VoiceId = id, CachedVoice = new() { Title = "Selected", Languages = ["ja"], SampleAudioUrl = "https://example.invalid/sample.mp3" } };
        var vm = new SettingsViewModel(ContextProxy.CreateContext(settings: settings), settings);
        try
        {
            stage("VM constructed; card/view setup begin");
            var card = new VoiceSearchItem { Id = id, Title = "Result", Languages = ["ja"], SampleAudioUrl = "https://example.invalid/sample.mp3" };
            vm.SearchResults = [card]; vm.ByIdResults = [card]; vm.FavoriteResults = [card]; vm.HasSearched = true;
            vm.BrowseMode = mode;
            var view = SettingsLayoutSuite.CreateThemedView(vm);
            view.UseLayoutRounding = true;
            view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
            ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light);
            stage("view/theme ready; HwndSource construction begin");
            var source = new HwndSource(new HwndSourceParameters("Offline description fixture")
            { Width = 900, Height = 1800, WindowStyle = unchecked((int)0x80000000) });
            stage("HwndSource constructed");
            try
            {
                stage("source attachment/native layout begin");
                source.RootVisual = view;
                SettingsLayoutSuite.Layout(view, width);
                stage("source native layout completed; actual DPI begin");
                typeof(VisualTreeHelper).GetMethod("SetRootDpi", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, [view, new DpiScale(1.25, 1.25)]);
                stage("actual 125% DPI set");
                var multiline = new string('あ', 12) + "\r\n" + new string('い', 28) + "\r\n" + new string('う', 49);
                foreach (var (name, text) in new[] { ("one", "明るく自然な声です。"), ("three", multiline), ("long", new string('あ', 10000)), ("empty", " \r\n\t ") })
                {
                    stage($"mode={mode}; sample={name}; runtime update/native layout begin");
                    vm.BrowseMode = mode; vm.CachedVoiceDescription = text; card.Description = text;
                    SettingsLayoutSuite.Layout(view, width);
                    stage($"mode={mode}; sample={name}; native layout completed");
                    foreach (var selected in new[] { true, false })
                    {
                        stage($"mode={mode}; sample={name}; selected={selected}; description observation begin");
                        var path = selected ? nameof(vm.CachedVoiceDescription) : nameof(card.Description);
                        var description = Visuals(view).OfType<TextBlock>().Single(block => VisualTreeHelper.GetParent(block) is UIElement { IsVisible: true } && BindingOperations.GetBinding(block, TextBlock.TextProperty)?.Path.Path == path);
                        var metadata = (StackPanel)VisualTreeHelper.GetParent(description);
                        var tags = Visuals(metadata).OfType<VoiceLanguageTags>().Single();
                        var runs = description.Visibility == Visibility.Visible ? Drawings(VisualTreeHelper.GetDrawing(description)).ToArray() : [];
                        var lines = runs.Select(run => run.GlyphRun.BaselineOrigin.Y).Distinct().Order().ToArray();
                        var ink = runs.Select(run => run.GlyphRun.BuildGeometry().Bounds).Where(bounds => !bounds.IsEmpty).ToArray();
                        var bottom = ink.Length == 0 ? 0 : ink.Max(bounds => bounds.Bottom);
                        var tagTop = tags.TranslatePoint(new Point(), description).Y + 7;
                        stage($"mode={mode}; sample={name}; selected={selected}; description observation completed; lines={lines.Length}; height={description.ActualHeight:R}");
                        if (directory is not null) Console.WriteLine($"[description] mode={mode} selected={selected} case={name} dark={dark} width={width} column={description.ActualWidth:R} desired={description.DesiredSize.Height:R} actual={description.ActualHeight:R} lines={string.Join(',', lines.Select(x => x.ToString("F3")))} inkBottom={bottom:R} tagTop={tagTop:R} gap={tagTop-bottom:R}");
                        if (directory is not null && mode == VoiceBrowseMode.Search && name is "one" or "three")
                        {
                            Directory.CreateDirectory(directory);
                            var cardRoot = AvatarPreviewSuite.Card(description);
                            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(cardRoot.ActualWidth * 1.25), (int)Math.Ceiling(cardRoot.ActualHeight * 1.25), 120, 120, PixelFormats.Pbgra32);
                            var drawing = new DrawingVisual();
                            using (var context = drawing.RenderOpen())
                            {
                                context.DrawRectangle((Brush)view.FindResource("SolidBackgroundFillColorBaseBrush"), null, new Rect(cardRoot.RenderSize));
                                context.DrawRectangle(new VisualBrush(cardRoot), null, new Rect(cardRoot.RenderSize));
                            }
                            bitmap.Render(drawing);
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var output = File.Create(Path.Combine(directory, $"{name}-{selected}-{dark}-{width}.png")); encoder.Save(output);
                        }
                        if (name == "three")
                            AssertEqual(true, description.ActualHeight - bottom < 8, "The description allocation must end after its last visible native line, without a spare clipped line");
                        AssertEqual(true, description.ActualHeight <= description.MaxHeight, "Long display descriptions retain their original bounded allocation");
                        AssertEqual(selected ? 36d : 30d, description.MaxHeight, "Selected and shared caps remain unchanged");
                        if (name is "three" or "long") AssertEqual(2, lines.Length, "Native bounded display retains two complete visible lines");
                        if (name == "one") AssertEqual(1, lines.Length, "A single native line does not allocate an unused line");
                        if (name == "empty") AssertEqual(Visibility.Collapsed, description.Visibility, "Trim-empty descriptions fold completely");
                        {
                            AssertEqual(text, description.Text, "Card layout preserves the complete provider description");
                            AssertEqual(!string.IsNullOrWhiteSpace(text), ToolTipService.GetIsEnabled(description), "Blank descriptions do not enable a hover tooltip");
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                AssertEqual(true, description.ToolTip is ToolTip, "Every description uses a native tooltip");
                                var tooltip = (ToolTip)description.ToolTip;
                                AssertEqual(DependencyProperty.UnsetValue, description.ReadLocalValue(ToolTipService.InitialShowDelayProperty), "Descriptions inherit native host tooltip delay");
                                stage($"mode={mode}; sample={name}; selected={selected}; tooltip before open");
                                tooltip.PlacementTarget = description; tooltip.IsOpen = true;
                                stage($"mode={mode}; sample={name}; selected={selected}; tooltip assigned open; original ContextIdle readiness begin");
                                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                                stage($"mode={mode}; sample={name}; selected={selected}; tooltip original ContextIdle readiness completed");
                                AssertEqual(text, tooltip.Content, "Tooltip binds the full original description, including line breaks and clipped tails");
                                var wrapped = Visuals(tooltip).OfType<TextBlock>().Single(block => block.Text == text);
                                AssertEqual(TextWrapping.Wrap, wrapped.TextWrapping, "Native tooltip wraps the full description");
                                AssertEqual(true, double.IsFinite(wrapped.MaxWidth) && wrapped.MaxWidth > 0, "Tooltip has a finite reading width");
                                stage($"mode={mode}; sample={name}; selected={selected}; full content/wrap assertions completed; tooltip close begin");
                                tooltip.IsOpen = false;
                                stage($"mode={mode}; sample={name}; selected={selected}; tooltip close completed");
                            }
                        }
                    }
                }
                stage("all selected/active-mode pair assertions completed");
            }
            finally
            {
                stage("source cleanup begin"); source.Dispose(); stage("source cleanup completed");
            }
        }
        finally
        {
            stage("VM cleanup begin"); vm.Dispose(); stage("VM cleanup completed");
        }
    }

    internal static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); yield return child;
            foreach (var nested in Visuals(child)) yield return nested;
        }
    }

    private static IEnumerable<GlyphRunDrawing> Drawings(Drawing? drawing)
    {
        if (drawing is GlyphRunDrawing glyph) yield return glyph;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var glyphRun in Drawings(child)) yield return glyphRun;
    }
}
