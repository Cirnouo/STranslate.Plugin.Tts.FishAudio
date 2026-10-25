using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
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
        foreach (var mode in new[] { VoiceBrowseMode.Search, VoiceBrowseMode.ById, VoiceBrowseMode.Favorites })
            await RunOnStaThreadAsync(() => InspectCards(width, dpi, dark, locale, mode, proofDirectory));
    }

    private static void InspectCards(double width, double dpi, bool dark, string locale, VoiceBrowseMode mode, string? proofDirectory)
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
            var titleRow = (DockPanel)VisualTreeHelper.GetParent((StackPanel)VisualTreeHelper.GetParent(action));
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
            AssertInlineHeader(title, author, actions, cardRoot);
            AssertEqual(true, date.TranslatePoint(new Point(), metadata).Y > author.TranslatePoint(new Point(), metadata).Y, "Statistics/date have their own row below the inline header");
            AssertEqual(true, Math.Abs(date.TranslatePoint(new Point(date.ActualWidth, 0), metadata).X - metadata.ActualWidth) <= 1, "Date aligns to the full content column's right edge");
            AssertEqual(true, Math.Abs(tag.ActualWidth - metadata.ActualWidth) <= 1, "Languages use the full content width under the title actions");
            AssertEqual(VoiceCreationDateConverter.Format(VoiceCreationDateSuite.Date, TimeZoneInfo.Local), date.Text, "Compiled date binding displays the local day");
            AssertEqual(view.FindResource("STranslate_Plugin_Tts_FishAudio_Voice_CreatedAt"), AutomationProperties.GetName(date), "Creation date has a localized accessible name");
            var idlePlay = Visuals(cardRoot).OfType<System.Windows.Shapes.Path>().Single(path => ReferenceEquals(path.Data, view.FindResource("AvatarPreviewPlayGeometry")));
            AssertEqual(Colors.White, ((SolidColorBrush)idlePlay.Fill).Color, "Filled idle Play retains opaque white contrast in both themes");
            AssertEqual(true, Visuals(cardRoot).OfType<Button>().Where(button => BindingOperations.GetBinding(button, Button.CommandProperty)?.Path.Path.EndsWith("PreviewCommand") != true).All(button => button.Focusable), "Card actions retain keyboard focus eligibility");
            AssertEqual<object?>(selected ? vm.CachedVoiceId : card, selected ? title.CommandParameter : action.CommandParameter, "Actions retain their original selection/identity parameters");
            AssertEqual(selected ? 56d : 48d, Visuals(cardRoot).OfType<System.Windows.Shapes.Ellipse>().Single(ellipse => ellipse.Width == (selected ? 56 : 48)).ActualWidth, "Portraits use the approved slightly larger list/selected allocation");
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
            var expandedTagsY = tag.TranslatePoint(new Point(), metadata).Y;
            double? foldedTagsY = null;
            foreach (var empty in new string?[] { null, "", " ", "\t\r\n " })
            {
                if (selected) vm.CachedVoiceDescription = empty;
                else card.Description = empty!;
                SettingsLayoutSuite.Layout(view, width);
                AssertEqual(Visibility.Collapsed, description.Visibility, "Null/empty/whitespace descriptions collapse in all four card locations");
                var y = author.TranslatePoint(new Point(), metadata).Y;
                AssertEqual(expandedAuthorY, y, "The inline author baseline stays fixed when description disappears");
                var tagsY = tag.TranslatePoint(new Point(), metadata).Y;
                AssertEqual(true, tagsY < expandedTagsY, "Language/footer content moves up when description disappears");
                if (foldedTagsY is { } prior) AssertEqual(prior, tagsY, "Every empty description leaves exactly the same lower-content geometry");
                foldedTagsY = tagsY;
                AssertEqual(empty ?? "", description.Text, "Visibility decisions do not trim meaningful provider text");
                if (proofDirectory is not null && locale == "en" && width is 320 or 640 && dpi == 1 && empty == "")
                    Save(cardRoot, Path.Combine(proofDirectory, $"{(selected ? "selected" : mode.ToString())}-{(dark ? "dark" : "light")}-{width}-empty-description.png"));
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
            if (proofDirectory is not null && locale == "en" && width is 320 or 640 && dpi == 1)
                Save(cardRoot, Path.Combine(proofDirectory, $"{(selected ? "selected" : mode.ToString())}-{(dark ? "dark" : "light")}-{width}-inline-card.png"));
            CheckRuntimeHeaders();
            if (locale == "en" && width == 400)
            {
                var group = (Panel)title.Parent;
                var actionStart = actions.TranslatePoint(new Point(), cardRoot).X;
                group.SetValue(TextElement.FontFamilyProperty, new FontFamily("Georgia"));
                group.Language = XmlLanguage.GetLanguage("ja-JP");
                author.FlowDirection = FlowDirection.RightToLeft;
                SettingsLayoutSuite.Layout(view, width);
                AssertInlineHeader(title, author, actions, cardRoot);
                AssertEqual(actionStart, actions.TranslatePoint(new Point(), cardRoot).X, "Changed inherited font/culture and author flow cannot reorder actions");
                author.ClearValue(FrameworkElement.FlowDirectionProperty);
                group.ClearValue(FrameworkElement.LanguageProperty);
                group.ClearValue(TextElement.FontFamilyProperty);
                SettingsLayoutSuite.Layout(view, width);
                AssertInlineHeader(title, author, actions, cardRoot);
            }

            void CheckRuntimeHeaders()
            {
                var originalTitle = ((TextBlock)title.Content).Text;
                var originalAuthor = author.Text;
                foreach (var (name, writer) in new[] { ("Short", new string('W', 120)), (new string('W', 120), "Li"), ("漢字 e\u0301 " + new string('W', 100), "العربية עברית " + new string('W', 100)), ("Short", (string?)null), ("Short", ""), ("Short", " \t\r\n "), ("", "Only author") })
                {
                    if (selected) { vm.CachedVoiceTitle = name; vm.CachedVoiceAuthor = writer; }
                    else { card.Title = name; card.AuthorName = writer!; }
                    SettingsLayoutSuite.Layout(view, width);
                    AssertInlineHeader(title, author, actions, cardRoot);
                    AssertEqual(writer ?? "", author.Text, "Inline layout preserves the original provider author string");
                }
                if (selected) { vm.CachedVoiceTitle = originalTitle; vm.CachedVoiceAuthor = originalAuthor; }
                else { card.Title = originalTitle; card.AuthorName = originalAuthor; }
                SettingsLayoutSuite.Layout(view, width);
            }
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

    private static void AssertInlineHeader(Button title, TextBlock author, StackPanel actions, Border cardRoot)
    {
        AssertEqual(true, ReferenceEquals(title.Parent, author.Parent), "Author belongs beside the dedicated title button in the same finite header group");
        var group = (Panel)title.Parent;
        var separator = group.Children.OfType<TextBlock>().Single(block => block.Text == "·");
        var name = (TextBlock)title.Content;
        AssertEqual(14d, name.FontSize, "All four card titles use the approved 14-DIP host font");
        AssertEqual(FontWeights.Medium, name.FontWeight, "Title retains medium emphasis");
        AssertEqual(11d, author.FontSize, "The inline author is 11-DIP secondary text");
        AssertEqual(author.FontSize, separator.FontSize, "The middle dot uses the same secondary size as the author");
        AssertEqual(FontWeights.Normal, author.FontWeight, "The inline author retains normal weight");
        AssertEqual(new Thickness(6, 0, 6, 0), separator.Margin, "The middle dot has six-DIP side gaps");
        AssertEqual(true, group.ActualWidth > 0 && double.IsFinite(group.ActualWidth), "The text group receives a finite viewport after reserving actions");
        AssertEqual(true, group.ActualHeight >= 32, "Header retains its 32-DIP minimum row");
        var groupEnd = group.TranslatePoint(new Point(group.ActualWidth, 0), cardRoot).X;
        AssertEqual(true, groupEnd <= actions.TranslatePoint(new Point(), cardRoot).X + .001, "Finite text cannot escape into the reserved action strip");
        AssertEqual(true, name.TextTrimming == TextTrimming.CharacterEllipsis && author.TextTrimming == TextTrimming.CharacterEllipsis, "Both header strings retain single-line character ellipsis");
        AssertEqual(TextWrapping.NoWrap, name.TextWrapping, "The title never wraps");
        AssertEqual(TextWrapping.NoWrap, author.TextWrapping, "The author never wraps");
        var hasAuthor = !string.IsNullOrWhiteSpace(author.Text);
        var hasTitle = name.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(name.Text);
        AssertEqual(hasAuthor ? Visibility.Visible : Visibility.Collapsed, author.Visibility, "Trim-empty authors collapse without modifying metadata");
        AssertEqual(hasAuthor && hasTitle ? Visibility.Visible : Visibility.Collapsed, separator.Visibility, "The dot requires both a presented title and nonempty author");
        var titleNatural = hasTitle ? NaturalWidth(name) : 0;
        var authorNatural = hasAuthor ? NaturalWidth(author) : 0;
        var remaining = Math.Max(0, group.ActualWidth - (hasTitle && hasAuthor ? NaturalWidth(separator) + 12 : 0));
        var expectedTitleWidth = Math.Min(titleNatural, remaining - (hasTitle ? Math.Min(authorNatural, remaining * .35) : Math.Min(authorNatural, remaining)));
        AssertEqual(true, Math.Abs(title.ActualWidth - expectedTitleWidth) < .001, "The title takes the reserved priority share, capped to its natural width");
        if (!hasAuthor) return;
        AssertEqual(author.Text, author.ToolTip, "The full author remains available in its tooltip");
        AssertEqual(author.Text, AutomationProperties.GetName(author), "The full author remains available to automation");
        AssertEqual(true, author.ActualWidth >= 0 && double.IsFinite(author.ActualWidth), "The author has a finite arrange slot");
        var authorSlot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(author);
        AssertEqual(true, Math.Abs(authorSlot.Width - Math.Min(authorNatural, remaining - expectedTitleWidth)) < .001, "Unused short-title width returns to the author, capped to its natural width");
        AssertEqual(true, authorSlot.Right <= group.ActualWidth + .001, "The author's finite arrange slot stays inside the reserved header viewport");
        var visibleAuthor = new Rect(author.RenderSize);
        var clip = VisualTreeHelper.GetClip(author);
        if (clip is not null) visibleAuthor.Intersect(clip.Bounds);
        AssertEqual(true, author.TransformToAncestor(group).TransformBounds(visibleAuthor).Right <= group.ActualWidth + .001,
            $"Actual author ink viewport cannot escape its slot: render={author.ActualWidth:R}, slot={authorSlot.Width:R}, clip={clip?.Bounds}");
        if (author.Text == "Li")
        {
            var dpi = VisualTreeHelper.GetDpi(author).DpiScaleX;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(author.ActualWidth * dpi), (int)Math.Ceiling(author.ActualHeight * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            bitmap.Render(author);
            var typeface = new Typeface(author.FontFamily, author.FontStyle, author.FontWeight, author.FontStretch);
            AssertEqual(true, typeface.TryGetGlyphTypeface(out var font), "The short Latin author fixture uses the resolved host typeface");
            AssertEnumerableEqual(new[] { font!.CharacterToGlyphMap['L'], font.CharacterToGlyphMap['i'] },
                Drawings(VisualTreeHelper.GetDrawing(author)!).OfType<GlyphRunDrawing>().SelectMany(drawing => drawing.GlyphRun.GlyphIndices),
                "A naturally fitting short author draws both actual letters without unexpected native ellipsis");
        }
        if (!hasTitle) return;
        AssertEqual(true, title.TranslatePoint(new Point(title.ActualWidth, 0), group).X <= separator.TranslatePoint(new Point(), group).X + .001, "Title and separator slots do not overlap");
        AssertEqual(true, separator.TranslatePoint(new Point(separator.ActualWidth, 0), group).X <= author.TransformToAncestor(group).TransformBounds(new Rect(author.RenderSize)).Left + .001, "Separator and author slots do not overlap");
        AssertEqual(true, Math.Abs(author.TransformToAncestor(group).TransformBounds(new Rect(author.RenderSize)).Left - title.TranslatePoint(new Point(title.ActualWidth, 0), group).X - separator.ActualWidth - 12) < .001, $"Header strings pack consecutively without a star-column gap: title={title.ActualWidth:R}, dot={separator.ActualWidth:R}, authorX={author.TransformToAncestor(group).TransformBounds(new Rect(author.RenderSize)).Left:R}, group={group.ActualWidth:R}");
        var baseline = name.TranslatePoint(new Point(0, Baseline(name)), group).Y;
        AssertEqual(true, Math.Abs(baseline - author.TranslatePoint(new Point(0, Baseline(author)), group).Y) < .001, "Title and smaller author share the actual measured typographic baseline");
        AssertEqual(true, Math.Abs(baseline - separator.TranslatePoint(new Point(0, Baseline(separator)), group).Y) < .001, "Middle dot shares the title baseline");
        AssertEqual<object?>(null, name.ToolTip, "Title text does not override the button's combined native copy/full-title tooltip");
        AssertEqual(name.Text, AutomationProperties.GetHelpText(name), "Ellipsized title retains complete automation text");
    }

    private static IEnumerable<Drawing> Drawings(Drawing drawing)
    {
        yield return drawing;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var descendant in Drawings(child)) yield return descendant;
    }

    private static double NaturalWidth(TextBlock text) =>
        new FormattedText(text.Text, text.Language.GetEquivalentCulture(), text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize,
            text.Foreground, null, TextOptions.GetTextFormattingMode(text), VisualTreeHelper.GetDpi(text).PixelsPerDip).WidthIncludingTrailingWhitespace;

    private static double Baseline(TextBlock text)
    {
        if (double.IsFinite(text.BaselineOffset)) return text.BaselineOffset;
        return new FormattedText(text.Text, text.Language.GetEquivalentCulture(), text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize,
            text.Foreground, VisualTreeHelper.GetDpi(text).PixelsPerDip).Baseline;
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
