using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Controls;
using STranslate.Plugin.Tts.FishAudio.Caching;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static StaTestHost;
using static TestAssertions;

internal static class SettingsInputInteractionSuite
{
    internal static async Task RunAllAsync(string? group = null)
    {
        if (group is not (null or "tabs" or "baseline" or "wheel" or "favorites" or "copy" or "fold-focus")) throw new ArgumentException("Unknown settings input test group", nameof(group));
        if (group is null or "fold-focus")
            foreach (var dark in new[] { false, true })
                await RunOnStaThreadAsync(() => FoldControlsSeparateFocusFromPointerState(dark));
        if (group is null or "tabs")
            foreach (var language in new[] { "en", "zh-cn", "zh-tw", "ja", "ko" })
                foreach (var dark in new[] { false, true })
                    await RunOnStaThreadAsync(() => SelectedTabsKeepAccentThroughNativeStates(language, dark));
        if (group is null or "baseline") await RunOnStaThreadAsync(ByIdUsesUnchangedSearchTextGeometry);
        if (group is null or "wheel") await RunOnStaThreadAsync(RestingNumericWheelEditsWithoutScrolling);
        if (group is null or "favorites") await RunOnStaThreadAsync(EmptyFavoritesStayVisibleOutsideCollapsedResults);
        if (group is null or "copy")
            foreach (var language in new[] { "en", "zh-cn", "zh-tw", "ja", "ko" })
                await RunOnStaThreadAsync(() => VoiceTitlesExposeCopyOnlyActions(language));
    }

    private static SettingsView CreateView(SettingsViewModel vm)
    {
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        Layout(view, 600);
        return view;
    }

    private static void FoldControlsSeparateFocusFromPointerState(bool dark)
    {
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        vm.HasSearched = true;
        vm.FavoriteCount = 1;
        var view = CreateView(vm);
        view.Resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xC0));
        view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light);
        foreach (var width in new[] { 400d, 600d })
        foreach (var name in new[] { "VoiceSearchResultsToggle", "FavoriteVoicesToggle" })
        {
            vm.BrowseMode = name == "VoiceSearchResultsToggle" ? VoiceBrowseMode.Search : VoiceBrowseMode.Favorites;
            var toggle = (ToggleButton)view.FindName(name);
            Layout(view, width);
            var border = (Border)toggle.Template.FindName("ToggleBorder", toggle);
            var size = toggle.RenderSize;
            var padding = border.Padding;
            AssertEqual(true, toggle.Focusable && KeyboardNavigation.GetIsTabStop(toggle), "Fold controls must remain reachable by keyboard");
            AssertNotNull(toggle.FocusVisualStyle, "Fold controls must retain a keyboard navigation focus adorner");
            var focus = new Control { Style = toggle.FocusVisualStyle, Width = 32, Height = 28, Resources = view.Resources };
            Layout(focus, 32);
            var bitmap = new RenderTargetBitmap(32, 28, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(focus);
            var pixels = new byte[32 * 28 * 4];
            bitmap.CopyPixels(pixels, 32 * 4, 0);
            var visibleFocusInk = Enumerable.Range(0, 32 * 28).Any(index =>
                pixels[index * 4 + 3] > 128 && (dark
                    ? pixels[index * 4] + pixels[index * 4 + 1] + pixels[index * 4 + 2] > 384
                    : pixels[index * 4] + pixels[index * 4 + 1] + pixels[index * 4 + 2] < 384));
            AssertEqual(true, visibleFocusInk, "The keyboard focus adorner must render a contrasting outline in both themes");
            // A mouse click also grants IsKeyboardFocused. Reproduce that native
            // state without stealing OS focus or driving a user's desktop.
            SetNativeState(toggle, "IsKeyboardFocusedPropertyKey", true, typeof(UIElement));
            foreach (var expanded in new[] { false, true })
            {
                toggle.IsChecked = expanded;
                foreach (var (over, pressed, resource) in new[]
                {
                    (true, false, "SubtleFillColorSecondaryBrush"),
                    (true, true, "SubtleFillColorTertiaryBrush"),
                    (false, false, ""),
                })
                {
                    SetNativeState(toggle, "IsMouseOverPropertyKey", over, typeof(UIElement));
                    SetNativeState(toggle, "IsPressedPropertyKey", pressed, typeof(ButtonBase));
                    Layout(view, width);
                    AssertEqual(0, RenderFill(border.BorderBrush).A, "Mouse-acquired focus must not leave an accent border after the pointer moves away");
                    if (resource.Length > 0)
                        AssertEqual(view.FindResource(resource), border.Background, "Fold hover and pressed feedback must retain their theme brush");
                    else
                        AssertEqual(0, RenderFill(border.Background).A, "A focused fold control must return to its resting surface");
                    AssertEqual(size, toggle.RenderSize, "Focus and pointer states must not resize fold controls");
                    AssertEqual(padding, border.Padding, "Focus and pointer states must not move fold content");
                    AssertEqual(toggle.ToolTip, AutomationProperties.GetName(toggle), "Expanded and collapsed names must match their localized tooltip");
                }
            }
            SetNativeState(toggle, "IsKeyboardFocusedPropertyKey", false, typeof(UIElement));
        }
    }

    private static void SelectedTabsKeepAccentThroughNativeStates(string language, bool dark)
    {
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = CreateView(vm);
        view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light);
        view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(FindRepoFile($"STranslate.Plugin.Tts.FishAudio/Languages/{language}.xaml"), UriKind.Absolute) });
        // A concrete accent palette avoids relying on OS accent initialization
        // in an unhosted fixture, while all text/overlay brushes remain native.
        var accent = Color.FromRgb(0x38, 0x6C, 0x9C);
        view.Resources["AccentFillColorTertiaryBrush"] = new SolidColorBrush(accent);
        var commands = new[] { vm.SwitchToSearchCommand, vm.SwitchToByIdCommand, vm.SwitchToFavoritesCommand };
        var tabs = commands.Select(command => Logical(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, command))).ToArray();
        foreach (var tab in tabs)
        {
            // The host installs its native resources before constructing the
            // plugin view. Rebase the compiled inline style onto that installed
            // style here, since this shared STA intentionally has no Application.
            var installed = new Style(typeof(Button), (Style)view.FindResource(typeof(Button)));
            foreach (var setter in tab.Style.Setters) installed.Setters.Add(setter);
            foreach (var trigger in tab.Style.Triggers) installed.Triggers.Add(trigger);
            tab.Style = installed;
        }
        foreach (var width in new[] { 400d, 600d })
        {
            Layout(view, width);
            var positions = tabs.Select(tab => tab.TranslatePoint(new Point(), view).X).ToArray();
            foreach (var command in commands)
            {
                command.Execute(null);
                Layout(view, width);
                var tab = tabs[Array.IndexOf(commands, command)];
                var nativeTemplate = tab.Template;
                var foreground = (Brush)view.FindResource("TextOnAccentFillColorPrimaryBrush");
                var overlay = (SolidColorBrush)view.FindResource("TextFillColorPrimaryBrush");
                foreach (var (over, pressed, amount) in new[] { (false, false, 0d), (true, false, .08), (true, true, .16) })
                {
                    SetNativeState(tab, "IsMouseOverPropertyKey", over, typeof(UIElement));
                    SetNativeState(tab, "IsPressedPropertyKey", pressed, typeof(ButtonBase));
                    Layout(view, width);
                    var surface = Visuals(tab).OfType<FrameworkElement>().Single(element => element.Name == "Background");
                    var actual = RenderFill((Brush)surface.GetValue(Panel.BackgroundProperty));
                    var expected = Blend(accent, overlay.Color, amount);
                    NearColor(expected, actual, $"{language}/{dark}/{width}px native selected hover/press must retain the accent with the requested overlay");
                    foreach (var text in ((StackPanel)tab.Content).Children.OfType<TextBlock>())
                        AssertEqual(foreground, text.Foreground, "Both selected icon and title must retain the paired on-accent foreground through hover/press");
                    AssertEqual(nativeTemplate, tab.Template, "Native interaction and focus templates must remain installed");
                    for (var index = 0; index < tabs.Length; index++)
                        Near(positions[index], tabs[index].TranslatePoint(new Point(), view).X, "Hover/press/selection must not move peer tabs");
                }
                SetNativeState(tab, "IsPressedPropertyKey", false, typeof(ButtonBase));
                SetNativeState(tab, "IsMouseOverPropertyKey", false, typeof(UIElement));
                SetNativeState(tab, "IsKeyboardFocusedPropertyKey", true, typeof(UIElement));
                Layout(view, width);
                AssertEqual(nativeTemplate, tab.Template, "Keyboard focus must preserve the native template and focus visual");
                AssertNotNull(tab.FocusVisualStyle, "Navigation buttons must preserve their native keyboard focus indicator");
                SetNativeState(tab, "IsKeyboardFocusedPropertyKey", false, typeof(UIElement));
            }
        }
    }

    private static void ByIdUsesUnchangedSearchTextGeometry()
    {
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = CreateView(vm);
        var search = BoundInput(view, "SearchQuery");
        var byId = BoundInput(view, "VoiceIdInput");
        foreach (var width in new[] { 400d, 600d })
        {
            vm.SearchQuery = "0123456789abcdef";
            vm.VoiceIdInput = "0123456789abcdef";
            vm.BrowseMode = VoiceBrowseMode.Search;
            Layout(view, width);
            AssertEqual(new Thickness(10, 5, 6, 6), search.Padding, "Search must retain the installed native padding");
            AssertEqual(VerticalAlignment.Top, search.VerticalContentAlignment, "Search vertical placement must remain unchanged");
            AssertEqual(14d, search.FontSize, "Search must keep native text metrics");
            var searchCaret = search.GetRectFromCharacterIndex(0);
            var searchTop = search.TranslatePoint(new Point(0, searchCaret.Top), view).Y;
            var searchViewport = Visuals(search).OfType<ScrollContentPresenter>().Single();
            var viewportHeight = searchViewport.ActualHeight;
            vm.BrowseMode = VoiceBrowseMode.ById;
            Layout(view, width);
            AssertEqual(search.Padding.Top, byId.Padding.Top, "Only By ID must adopt the existing Search top padding");
            AssertEqual(search.Padding.Bottom, byId.Padding.Bottom, "Only By ID must adopt the existing Search bottom padding");
            AssertEqual(40d, byId.Padding.Right, "By ID must retain its reserved Paste region");
            AssertEqual(search.FontFamily, byId.FontFamily, "Both query inputs must use the existing native font");
            AssertEqual(search.FontSize, byId.FontSize, "Both query inputs must use the existing native font size");
            var idCaret = byId.GetRectFromCharacterIndex(0);
            Near(searchTop, byId.TranslatePoint(new Point(0, idCaret.Top), view).Y, "The actual typed text must start at the existing Search baseline");
            Near(searchCaret.Height, idCaret.Height, "The actual typed text must retain the same native line height");
            Near(viewportHeight, Visuals(byId).OfType<ScrollContentPresenter>().Single().ActualHeight, "The native text viewport heights must match");
            vm.SearchQuery = "";
            vm.VoiceIdInput = "";
            foreach (var mode in new[] { VoiceBrowseMode.Search, VoiceBrowseMode.ById }) { vm.BrowseMode = mode; Layout(view, width); }
            var searchPlaceholder = Visuals(search).OfType<TextBlock>().Single(block => block.Name == "PlaceholderTextContentPresenter");
            var idPlaceholder = Visuals(byId).OfType<TextBlock>().Single(block => block.Name == "PlaceholderTextContentPresenter");
            Near(searchPlaceholder.Padding.Top, idPlaceholder.Padding.Top, "Placeholder top placement must match the unchanged Search field");
            Near(searchPlaceholder.Padding.Bottom, idPlaceholder.Padding.Bottom, "Placeholder bottom placement must match the unchanged Search field");
        }
    }

    private static void RestingNumericWheelEditsWithoutScrolling()
    {
        var settings = new Settings { SelectedModel = "s2-pro", Speed = 1, Volume = 0, Temperature = .7, TopP = .7 };
        var context = CreateContext(settings: settings);
        var storage = (ContextProxy)(object)context;
        using var vm = new SettingsViewModel(context, settings);
        var view = CreateView(vm);
        var page = new Border { Child = view };
        var pageWheels = 0;
        page.MouseWheel += (_, _) => pageWheels++;
        foreach (var editor in Logical(view).OfType<InlineNumericEditor>())
        {
            var property = BindingOperations.GetBinding(editor, InlineNumericEditor.ValueProperty)!.Path.Path;
            var display = (Button)editor.FindName("DisplayButton");
            var input = (TextBox)editor.FindName("EditorBox");
            var unit = (TextBlock)editor.FindName("UnitText");
            var slider = Logical(view).OfType<Slider>().Single(slider => BindingOperations.GetBinding(slider, Slider.ValueProperty)?.Path.Path == property);
            var step = property == "Volume" ? .1 : .01;
            var initial = editor.Value;
            foreach (var width in new[] { 400d, 600d })
            {
                Layout(view, width);
                var position = editor.TranslatePoint(new Point(), view);
                var saves = storage.SaveCount;
                var bubbles = pageWheels;
                var label = Visuals(display).OfType<TextBlock>().Single();
                var wheel = Wheel(label, 120);
                AssertEqual(true, wheel.Handled, "A wheel over resting numeric text must be consumed before the settings page scrolls");
                Near(initial + step, editor.Value, "A resting wheel notch must apply the display precision step rather than slider ticks");
                Near(editor.Value, slider.Value, "Wheel edits must synchronize the real slider without tick re-snapping");
                AssertEqual(saves + 1, storage.SaveCount, "An effective wheel notch must persist exactly once");
                AssertEqual(bubbles, pageWheels, "The resting numeric wheel must not bubble to the settings page");
                AssertEqual(position, editor.TranslatePoint(new Point(), view), "Wheel editing must preserve row geometry");
                Wheel(display, -120);
                Near(initial, editor.Value, "Reverse wheel direction must restore the previous value exactly");
                saves = storage.SaveCount;
                Wheel(display, 60);
                Near(initial, editor.Value, "A partial high-resolution notch must accumulate without an early save");
                AssertEqual(saves, storage.SaveCount, "A partial notch must not save an unchanged value");
                Wheel(display, 60);
                Near(initial + step, editor.Value, "Two high-resolution half notches must produce one precision step");
                Wheel(display, -120);
                editor.SetCurrentValue(InlineNumericEditor.ValueProperty, editor.Maximum);
                saves = storage.SaveCount;
                bubbles = pageWheels;
                AssertEqual(true, Wheel(display, 120).Handled, "A wheel at the upper bound must still prevent page jumping");
                Near(editor.Maximum, editor.Value, "Wheel edits must clamp at the upper bound");
                AssertEqual(saves, storage.SaveCount, "A wheel at the bound must not persist a redundant value");
                AssertEqual(bubbles, pageWheels, "A bound must not release the wheel into page scrolling");
                Wheel(display, 60);
                Wheel(display, -120);
                Near(editor.Maximum - step, editor.Value, "An outward partial notch at the upper bound must not delay an inward notch");
                editor.SetCurrentValue(InlineNumericEditor.ValueProperty, editor.Minimum);
                AssertEqual(true, Wheel(display, -240).Handled, "Lower-bound notches must remain consumed");
                Near(editor.Minimum, editor.Value, "Multiple notches must clamp at the lower bound");
                editor.SetCurrentValue(InlineNumericEditor.ValueProperty, initial);
                display.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Layout(view, width);
                AssertEqual(Visibility.Visible, input.Visibility, "The numeric text must still open its existing native editor");
                var draft = input.Text;
                saves = storage.SaveCount;
                bubbles = pageWheels;
                var editingWheel = Wheel(input, 120);
                AssertEqual(false, editingWheel.Handled, "A wheel over the editing TextBox must not be consumed by numeric editing");
                Near(initial, editor.Value, "An editing TextBox wheel must not change the committed value");
                AssertEqual(draft, input.Text, "An editing TextBox wheel must preserve the active draft");
                AssertEqual(saves, storage.SaveCount, "An editing wheel must not save");
                AssertEqual(bubbles + 1, pageWheels, "An editing wheel must retain the ordinary page bubble path");
                var contentHost = (UIElement)input.Template.FindName("PART_ContentHost", input);
                var textView = Visuals(input).OfType<UIElement>().Single(element => element.GetType().Name == "TextBoxView");
                foreach (var nativeTarget in new[] { contentHost, textView })
                {
                    bubbles = pageWheels;
                    AssertEqual(false, Wheel(nativeTarget, 120).Handled, "A wheel from the actual native editing content must retain normal page scrolling");
                    AssertEqual(bubbles + 1, pageWheels, "The native editing content host must not swallow the page's wheel route");
                    Near(initial, editor.Value, "Wheel over actual native editing content must not adjust the numeric value");
                    AssertEqual(draft, input.Text, "Wheel over actual native editing content must preserve the draft");
                }
                editor.CommitEdit();
                bubbles = pageWheels;
                AssertEqual(false, Wheel(unit, 120).Handled, "Wheel over a separate unit must retain ordinary page scrolling");
                AssertEqual(bubbles + 1, pageWheels, "The unit must not steal wheel scrolling from the page");
                bubbles = pageWheels;
                AssertEqual(false, Wheel(view, 120).Handled, "Wheel over the rest of a card must retain ordinary page scrolling");
                AssertEqual(bubbles + 1, pageWheels, "Ordinary card space must retain the page bubble path");
            }
        }
    }

    private static void EmptyFavoritesStayVisibleOutsideCollapsedResults()
    {
        var settings = new Settings();
        var cache = new VoiceMetadataCache();
        using var vm = new SettingsViewModel(CreateContext(settings: settings, pluginSettingsDirectoryPath: CreateTempDirectory()), settings, null, null, voiceMetadataCache: cache);
        vm.BrowseMode = VoiceBrowseMode.Favorites;
        var view = CreateView(vm);
        var toggle = (ToggleButton)view.FindName("FavoriteVoicesToggle");
        var header = (FrameworkElement)LogicalTreeHelper.GetParent(toggle);
        var body = (FrameworkElement)view.FindName("FavoriteVoicesBody");
        var empty = Logical(view).OfType<TextBlock>().Single(block => block.Text == view.FindResource("STranslate_Plugin_Tts_FishAudio_Favorites_Empty").ToString());
        AssertEqual(Visibility.Collapsed, header.Visibility, "An empty Favorites tab must hide the entire count/fold row");
        AssertEqual(true, Shown(empty), "An empty Favorites tab must show only its existing localized empty hint");
        var items = Enumerable.Range(1, 7).Select(number => new VoiceSearchItem { Id = number.ToString("x32"), Title = $"Voice {number}" }).ToArray();
        foreach (var item in items)
        {
            cache.Put(VoiceMetadata.FromCachedVoice(item.Id, new() { Title = item.Title }));
            vm.ToggleFavoriteCommand.Execute(item);
        }
        Layout(view, 400);
        AssertEqual(Visibility.Visible, header.Visibility, "Nonempty Favorites must restore the count/fold row");
        AssertEqual(6, vm.FavoriteResults.Count, "Nonempty Favorites must retain its existing six-card pages");
        AssertEqual(true, vm.ShowFavoritePagination, "Seven favorites must retain the existing pager");
        vm.FavoriteNextPageCommand.Execute(null);
        AssertEqual(1, vm.FavoriteResults.Count, "The final favorite page must remain intact");
        toggle.IsChecked = false;
        Layout(view, 400);
        AssertEqual(Visibility.Collapsed, body.Visibility, "Nonempty Favorites must retain its remembered fold behavior");
        foreach (var item in items) vm.ToggleFavoriteCommand.Execute(item);
        Layout(view, 400);
        AssertEqual(0, vm.FavoriteCount, "The test must remove the final favorite through the real store command");
        AssertEqual(false, toggle.IsChecked, "Removing the final favorite must preserve the remembered fold state");
        AssertEqual(Visibility.Collapsed, header.Visibility, "Removing the final favorite must hide count and fold controls");
        AssertEqual(false, vm.ShowFavoritePagination, "Removing the final favorite must hide the pager");
        AssertEqual(true, Shown(empty), "Removing the last favorite while folded must still reveal the empty hint");
        vm.FavoritesError = "Synthetic storage failure";
        Layout(view, 400);
        AssertEqual(false, Shown(empty), "A storage failure must not be reported as an empty collection");
        var error = Logical(view).OfType<TextBlock>().Single(block => BindingOperations.GetBinding(block, TextBlock.TextProperty)?.Path.Path == "FavoritesError");
        AssertEqual(true, Shown(error), "Storage errors must remain visible even if the prior results were folded");
    }

    private static void VoiceTitlesExposeCopyOnlyActions(string language)
    {
        var directory = CreateTempDirectory();
        var selectedId = new string('a', 32);
        var searchId = new string('b', 32);
        var byId = new string('c', 32);
        var favoriteId = new string('d', 32);
        using (var favorites = new FavoriteVoiceStore(directory)) favorites.SetFavorite(favoriteId, true);
        var favoriteFile = File.ReadAllText(Path.Combine(directory, FavoriteVoiceStore.FileName));
        var settings = new Settings { VoiceId = selectedId, CachedVoice = new() { Title = "Selected title", Description = "Selected description", AuthorName = "Selected author" } };
        var cache = new VoiceMetadataCache();
        cache.Put(VoiceMetadata.FromCachedVoice(favoriteId, new() { Title = "Favorite title", Description = "Favorite description", AuthorName = "Favorite author" }));
        var snackbar = new TestSnackbar();
        var (service, http) = TestHttpServiceProxy.Create();
        var context = CreateContext(settings: settings, snackbar: snackbar, httpService: service, pluginSettingsDirectoryPath: directory);
        var storage = (ContextProxy)(object)context;
        var writes = new List<string>();
        var failClipboard = false;
        using var vm = new SettingsViewModel(context, settings, null, null, voiceMetadataCache: cache,
            copyVoiceIdToClipboard: text => { if (failClipboard) throw new InvalidOperationException("Synthetic clipboard failure"); writes.Add(text); });
        vm.SearchResults = [new VoiceSearchItem { Id = searchId, Title = "Search title", Description = "Search description", AuthorName = "Search author" }];
        vm.ByIdResults = [new VoiceSearchItem { Id = byId, Title = "By ID title", Description = "By ID description", AuthorName = "By ID author" }];
        vm.HasSearched = true;
        var view = CreateView(vm);
        view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(FindRepoFile($"STranslate.Plugin.Tts.FishAudio/Languages/{language}.xaml"), UriKind.Absolute) });
        foreach (var width in new[] { 400d, 600d })
        {
            foreach (var (mode, cardId) in new[] { (VoiceBrowseMode.Search, searchId), (VoiceBrowseMode.ById, byId), (VoiceBrowseMode.Favorites, favoriteId) })
            {
                vm.BrowseMode = mode;
                Layout(view, width);
                var titles = Visuals(view).OfType<Button>().Where(button => ReferenceEquals(button.Command, vm.CopyVoiceIdCommand) && Shown(button)).ToArray();
                AssertEqual(2, titles.Length, "Only the selected title and the visible browse title must expose copy actions");
                AssertEnumerableEqual(new[] { selectedId, cardId }.Order(), titles.Select(button => (string)button.CommandParameter).Order(), "All four title locations must pass their canonical voice ID");
                foreach (var title in titles)
                {
                    AssertEqual(Cursors.Hand, title.Cursor, "A copyable voice title must advertise its click action");
                    AssertEqual(view.FindResource("STranslate_Plugin_Tts_FishAudio_CopyVoiceId"), title.ToolTip, "Title copy tooltips must be localized");
                    AssertEqual(title.ToolTip, AutomationProperties.GetName(title), "Title copy actions must have a localized accessible name");
                    var text = (TextBlock)title.Content;
                    Near(text.ActualHeight, title.ActualHeight, "A text-only title action must preserve the title's native line height");
                    AssertEqual(0, Visuals(title).OfType<Border>().Count(), "Copyable titles must not gain a rectangular button surround");
                    var before = writes.Count;
                    typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(title, null);
                    AssertEqual(before + 1, writes.Count, "A title click must write exactly one clipboard value");
                    AssertEqual((string)title.CommandParameter, writes[^1], "The clicked title must copy its own complete voice ID");
                    AssertEqual("STranslate_Plugin_Tts_FishAudio_CopyVoiceId_Success", snackbar.LastSuccess, "A successful clipboard write must use the host success snackbar");
                }
                foreach (var text in Visuals(view).OfType<TextBlock>().Where(text => Shown(text) && (text.Text.EndsWith("description", StringComparison.Ordinal) || text.Text.EndsWith("author", StringComparison.Ordinal))))
                    AssertEqual(false, HasButtonAncestor(text), "Author and description text must remain ordinary noncopyable text");
            }
        }
        var successes = snackbar.SuccessCount;
        var copied = writes.Count;
        failClipboard = true;
        vm.CopyVoiceIdCommand.Execute(selectedId);
        AssertEqual(copied, writes.Count, "A failed clipboard operation must not report a completed write");
        AssertEqual(successes, snackbar.SuccessCount, "A failed clipboard operation must never produce a false success toast");
        AssertEqual("STranslate_Plugin_Tts_FishAudio_CopyVoiceId_Failed", snackbar.LastError, "Clipboard failure must use the localized host error snackbar");
        foreach (var invalid in new[] { "", "partial", selectedId + "\n", selectedId.ToUpperInvariant() })
        {
            AssertEqual(false, vm.CopyVoiceIdCommand.CanExecute(invalid), "Only canonical voice IDs may be copied");
            vm.CopyVoiceIdCommand.Execute(invalid);
        }
        AssertEqual(copied, writes.Count, "Invalid copy parameters must never reach the clipboard boundary");
        AssertEqual(selectedId, settings.VoiceId, "Copying titles must never Apply or Clear a voice");
        AssertEqual(0, storage.SaveCount, "Copying titles must not persist unrelated settings");
        AssertEqual(favoriteFile, File.ReadAllText(Path.Combine(directory, FavoriteVoiceStore.FileName)), "Copying titles must preserve favorites exactly");
        AssertEqual(null, vm.PreviewingVoiceId, "Copying titles must not start preview playback");
        AssertEqual(null, vm.PreparingPreviewVoiceId, "Copying titles must not start preview preparation");
        AssertEqual(0, http.GetCallCount + http.GetAsStreamCallCount + http.PostAsBytesCallCount, "Copying any title must not call Fish Audio");
        foreach (var suffix in new[] { "", "_Success", "_Failed" })
            AssertNotNull(view.TryFindResource("STranslate_Plugin_Tts_FishAudio_CopyVoiceId" + suffix), "Every copy action and snackbar string must exist in all five locales");
    }

    private static bool HasButtonAncestor(DependencyObject child)
    {
        for (var parent = VisualTreeHelper.GetParent(child); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is Button) return true;
        return false;
    }

    private static MouseWheelEventArgs Wheel(UIElement target, int delta)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        target.RaiseEvent(args);
        if (!args.Handled) { args.RoutedEvent = Mouse.MouseWheelEvent; target.RaiseEvent(args); }
        return args;
    }
    private static void SetNativeState(DependencyObject target, string key, object value, Type owner) =>
        target.SetValue((DependencyPropertyKey)owner.GetField(key, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!, value);
    private static Color Blend(Color background, Color overlay, double opacity)
    {
        var alpha = opacity * overlay.A / 255d;
        return Color.FromRgb((byte)Math.Round(background.R * (1 - alpha) + overlay.R * alpha), (byte)Math.Round(background.G * (1 - alpha) + overlay.G * alpha), (byte)Math.Round(background.B * (1 - alpha) + overlay.B * alpha));
    }
    private static Color RenderFill(Brush fill)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) context.DrawRectangle(fill, null, new Rect(0, 0, 1, 1));
        var bitmap = new RenderTargetBitmap(1, 1, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixel = new byte[4];
        bitmap.CopyPixels(pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
    private static void NearColor(Color expected, Color actual, string message) => AssertEqual(true,
        Math.Abs(expected.R - actual.R) <= 1 && Math.Abs(expected.G - actual.G) <= 1 && Math.Abs(expected.B - actual.B) <= 1 && actual.A == 255,
        $"{message}: expected {expected}, actual {actual}");
    private static TextBox BoundInput(DependencyObject root, string property) => Logical(root).OfType<TextBox>().Single(input => BindingOperations.GetBinding(input, TextBox.TextProperty)?.Path.Path == property);
    private static void Near(double expected, double actual, string message) => AssertEqual(true, Math.Abs(expected - actual) < .01, $"{message}: expected {expected}, actual {actual}");
    private static void Layout(FrameworkElement view, double width) => SettingsLayoutSuite.Layout(view, width);
    private static bool Shown(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current)) if (current is UIElement ui && ui.Visibility != Visibility.Visible) return false;
        return true;
    }
    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) { yield return child; foreach (var item in Logical(child)) yield return item; }
    }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) { var child = VisualTreeHelper.GetChild(root, index); yield return child; foreach (var item in Visuals(child)) yield return item; }
    }
}
