using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using iNKORE.UI.WPF.Modern;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Converter;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static StaTestHost;
using static TestAssertions;

internal static class VoiceCardLayoutSuite
{
    internal static async Task RunAllAsync(string? proofDirectory = null)
    {
        // Cold construction remains a separately bounded phase; matrix actions keep five seconds.
        await RunOnStaThreadAsync(() =>
        {
            using var vm = new SettingsViewModel(CreateContext(), new Settings());
            SettingsLayoutSuite.Layout(SettingsLayoutSuite.CreateThemedView(vm), 400);
        }, TimeSpan.FromSeconds(15));
        foreach (var locale in UiLocales.All)
        foreach (var dark in new[] { false, true })
        foreach (var dpi in new[] { 1d, 1.25, 1.5 })
        foreach (var width in new[] { 320d, 400, 480, 640 })
            await RunOnStaThreadAsync(() => InspectCards(width, dpi, dark, locale, proofDirectory));
    }

    private static void InspectCards(double width, double dpi, bool dark, string locale, string? proofDirectory)
    {
        var settings = new Settings { VoiceId = AppliedKey, CachedVoice = new()
        {
            Title = "Morning monologue · a long example voice title for ellipsis geometry",
            Description = "A clear and natural narrative voice. " + new string('x', 200),
            AuthorName = "Example author with a very long name that should remain on one line",
            Languages = new[] { "en", "zh", "ja", "ko", "de", "fr", "es", "pt", "ru", "tr" },
            CreatedAt = VoiceCreationDateSuite.Date, TaskCount = 128600,
            SampleAudioUrl = "https://example.invalid/sample.mp3"
        } };
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var cover = FindRepoFile("STranslate.Plugin.Tts.FishAudio/Assets/Flags/language.png");
        vm.CachedVoiceCoverUrl = cover;
        var card = new VoiceSearchItem
        {
            Id = AppliedKey, Title = settings.CachedVoice.Title, Description = settings.CachedVoice.Description,
            AuthorName = settings.CachedVoice.AuthorName, Languages = settings.CachedVoice.Languages,
            CreatedAt = VoiceCreationDateSuite.Date, TaskCount = 128600, CoverUrl = cover,
            SampleAudioUrl = settings.CachedVoice.SampleAudioUrl,
        };
        vm.SearchResults = [card]; vm.ByIdResults = [card]; vm.FavoriteResults = [card]; vm.HasSearched = true;
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        view.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        view.SetResourceReference(Control.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        var language = view.Resources.MergedDictionaries.Single(item => item.Source?.LocalPath.Replace('\\', '/').Contains("/Languages/", StringComparison.Ordinal) == true);
        view.Resources.MergedDictionaries.Remove(language);
        view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(FindRepoFile($"STranslate.Plugin.Tts.FishAudio/Languages/{locale}.xaml"), UriKind.Absolute) });
        view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light);
        var desktopScale = VisualTreeHelper.GetDpi(new DrawingVisual());
        using var source = new HwndSource(new HwndSourceParameters("Fish Audio dated card fixture")
        {
            Width = (int)Math.Ceiling(width * desktopScale.DpiScaleX), Height = (int)Math.Ceiling(1600 * desktopScale.DpiScaleY),
            WindowStyle = unchecked((int)0x80000000), PositionX = 120, PositionY = 120,
        });
        source.RootVisual = view;
        SettingsLayoutSuite.Layout(view, width);
        typeof(VisualTreeHelper).GetMethod("SetRootDpi", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [view, new DpiScale(dpi, dpi)]);
        foreach (var mode in new[] { VoiceBrowseMode.Search, VoiceBrowseMode.ById, VoiceBrowseMode.Favorites })
        {
            vm.BrowseMode = mode;
            SettingsLayoutSuite.Layout(view, width);
            typeof(VisualTreeHelper).GetMethod("SetRootDpi", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [view, new DpiScale(dpi, dpi)]);
            SettingsLayoutSuite.Layout(view, width);
            var tags = Visuals(view).OfType<VoiceLanguageTags>().Where(IsPresented).ToArray();
            AssertEqual(2, tags.Length, "Selected and active browse card must both be present");
            foreach (var tag in tags)
            {
                var selected = ReferenceEquals(tag.DataContext, vm);
                var cardRoot = CardBorder(tag);
                var title = Visuals(cardRoot).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.CopyVoiceIdCommand));
                var action = Visuals(cardRoot).OfType<Button>().Single(button => ReferenceEquals(button.Command, selected ? vm.ClearVoiceCommand : vm.SelectVoiceCommand));
                var description = BoundText(cardRoot, selected ? nameof(vm.CachedVoiceDescription) : nameof(card.Description));
                var author = BoundText(cardRoot, selected ? nameof(vm.CachedVoiceAuthor) : nameof(card.AuthorName));
                var date = BoundText(cardRoot, selected ? nameof(vm.CachedVoiceCreatedAt) : nameof(card.CreatedAt));
                var titleRow = (DockPanel)VisualTreeHelper.GetParent(title);
                var metadata = (StackPanel)VisualTreeHelper.GetParent(tag);
                var actions = (StackPanel)VisualTreeHelper.GetParent(action);
                AssertEqual(dpi, VisualTreeHelper.GetDpi(date).DpiScaleX, "Date text inherits the requested visual-root DPI");
                AssertEqual(true, Math.Abs(title.TranslatePoint(new Point(0, title.ActualHeight / 2), cardRoot).Y - action.TranslatePoint(new Point(0, action.ActualHeight / 2), cardRoot).Y) <= 1,
                    "Voice title and actions share the same row center while the copy target retains native text height");
                AssertEqual(((TextBlock)title.Content).ActualHeight, title.ActualHeight, "Title copy target remains exactly the native text height");
                AssertEqual(true, actions.TranslatePoint(new Point(actions.ActualWidth, 0), metadata).X <= metadata.ActualWidth + 1, "Actions fit inside the content column");
                AssertEqual(true, title.ActualWidth > 0 && title.ActualWidth + actions.ActualWidth <= titleRow.ActualWidth + 1, "Long title has a finite viewport beside actions");
                AssertEqual(true, description.ActualHeight <= description.MaxHeight && description.ActualWidth <= metadata.ActualWidth, "Descriptions retain bounded lines at every width");
                AssertEqual(true, author.TextTrimming == TextTrimming.CharacterEllipsis && author.ActualWidth <= metadata.ActualWidth, "Long author remains in a finite ellipsizing row");
                AssertEqual(true, author.TranslatePoint(new Point(), metadata).Y >= tag.TranslatePoint(new Point(0, tag.ActualHeight), metadata).Y - 1, "Author follows the language row");
                AssertEqual(true, date.TranslatePoint(new Point(), metadata).Y > author.TranslatePoint(new Point(), metadata).Y, "Statistics/date have their own row below author");
                AssertEqual(true, Math.Abs(date.TranslatePoint(new Point(date.ActualWidth, 0), metadata).X - metadata.ActualWidth) <= 1, "Date aligns to the full content column's right edge");
                AssertEqual(true, Math.Abs(tag.ActualWidth - metadata.ActualWidth) <= 1, "Languages use the full content width under the title actions");
                AssertEqual(VoiceCreationDateConverter.Format(VoiceCreationDateSuite.Date, TimeZoneInfo.Local), date.Text, "Compiled date binding displays the local day");
                AssertEqual(view.FindResource("STranslate_Plugin_Tts_FishAudio_Voice_CreatedAt"), AutomationProperties.GetName(date), "Creation date has a localized accessible name");
                var idlePlay = Visuals(cardRoot).OfType<InkCenteredGlyph>().Single(glyph => glyph.Glyph == "\uE768");
                AssertEqual(((SolidColorBrush)view.FindResource("TextFillColorPrimaryBrush")).Color, ((SolidColorBrush)idlePlay.Foreground).Color, "Idle Play retains native theme foreground contrast");
                AssertEqual(true, Visuals(cardRoot).OfType<Button>().All(button => button.Focusable), "Card actions retain keyboard focus eligibility");
                AssertEqual<object?>(selected ? vm.CachedVoiceId : card, selected ? title.CommandParameter : action.CommandParameter, "Actions retain their original selection/identity parameters");
                AssertEqual(selected ? 48d : 40d, Visuals(cardRoot).OfType<System.Windows.Shapes.Ellipse>().Single(ellipse => ellipse.Width == (selected ? 48 : 40)).ActualWidth, "Avatar geometry is unchanged");
                if (locale == "en" && width == 400 && dpi == 1 && !dark)
                {
                    var tooltip = (ToolTip)date.ToolTip;
                    tooltip.PlacementTarget = date; tooltip.IsOpen = true;
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    var tooltipText = ((StackPanel)tooltip.Content).Children.OfType<TextBlock>().ToArray();
                    AssertEqual(view.FindResource("STranslate_Plugin_Tts_FishAudio_Voice_CreatedAt"), tooltipText[0].Text, "Native date tooltip resolves its localized caption");
                    AssertEqual(VoiceCreationDateConverter.Format(VoiceCreationDateSuite.Date, TimeZoneInfo.Local, true), tooltipText[1].Text, "Native date tooltip binds the complete local timestamp and offset");
                    tooltip.IsOpen = false;
                }
                // Runtime whitespace transitions must fold the whole description row without changing provider text.
                var original = description.Text;
                var expandedAuthorY = author.TranslatePoint(new Point(), metadata).Y;
                double? foldedAuthorY = null;
                foreach (var empty in new string?[] { null, "", " ", "\t\r\n " })
                {
                    if (selected) vm.CachedVoiceDescription = empty;
                    else card.Description = empty!;
                    SettingsLayoutSuite.Layout(view, width);
                    AssertEqual(Visibility.Collapsed, description.Visibility, "Null/empty/whitespace descriptions collapse in all four card locations");
                    var y = author.TranslatePoint(new Point(), metadata).Y;
                    AssertEqual(true, y < expandedAuthorY, "Following metadata moves up when the description disappears");
                    if (foldedAuthorY is { } prior) AssertEqual(prior, y, "Every empty description leaves exactly the same geometry");
                    foldedAuthorY = y;
                    AssertEqual(empty ?? "", description.Text, "Visibility decisions do not trim meaningful provider text");
                    if (proofDirectory is not null && locale == "en" && width == 400 && dpi == 1 && empty == "")
                        Save(cardRoot, Path.Combine(proofDirectory, $"{(selected ? "selected" : mode.ToString())}-{(dark ? "dark" : "light")}-empty-description-v2.png"));
                }
                if (selected) vm.CachedVoiceDescription = original;
                else card.Description = original;
                SettingsLayoutSuite.Layout(view, width);
                AssertEqual(Visibility.Visible, description.Visibility, "Nonempty description row reappears at runtime");
                AssertEqual(expandedAuthorY, author.TranslatePoint(new Point(), metadata).Y, "Restoring description restores the metadata position");
                if (selected) vm.CachedVoiceCreatedAt = null;
                else card.CreatedAt = null;
                SettingsLayoutSuite.Layout(view, width);
                AssertEqual("—", date.Text, "Absent creation date updates to an em dash at runtime");
                if (selected) vm.CachedVoiceCreatedAt = VoiceCreationDateSuite.Date;
                else card.CreatedAt = VoiceCreationDateSuite.Date;
                SettingsLayoutSuite.Layout(view, width);
                if (proofDirectory is not null && locale == "en" && width == 400 && dpi == 1)
                    Save(cardRoot, Path.Combine(proofDirectory, $"{(selected ? "selected" : mode.ToString())}-{(dark ? "dark" : "light")}-dated-card-v2.png"));
            }
            card.IsLoadingMetadata = true; card.HasMetadata = false;
            SettingsLayoutSuite.Layout(view, width);
            var apply = Visuals(view).OfType<Button>().Where(IsPresented).Single(button => ReferenceEquals(button.Command, vm.SelectVoiceCommand));
            AssertEqual(card.IsApplied, apply.IsEnabled, "A loading placeholder permits clearing only an already applied voice");
            var wasApplied = card.IsApplied; card.IsApplied = false;
            SettingsLayoutSuite.Layout(view, width);
            AssertEqual(false, apply.IsEnabled, "Unapplied loading placeholders retain disabled Apply");
            card.IsApplied = wasApplied;
            card.HasMetadata = true; card.IsLoadingMetadata = false;
            AssertEqual(AppliedKey, vm.VoiceId, "Presentation changes do not apply/clear selection");
        }
    }

    private static TextBlock BoundText(DependencyObject root, string path) => Visuals(root).OfType<TextBlock>().Single(block => BindingOperations.GetBinding(block, TextBlock.TextProperty)?.Path.Path == path);
    private static Border CardBorder(DependencyObject child)
    {
        for (var current = VisualTreeHelper.GetParent(child); current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is Border { Child: DockPanel } border) return border;
        throw new InvalidOperationException("Language tags must belong to a compiled card");
    }
    private static void Save(FrameworkElement element, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle((Brush)element.FindResource("SolidBackgroundFillColorBaseBrush"), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            context.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.Fill }, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }
    private static bool IsPresented(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
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
}
