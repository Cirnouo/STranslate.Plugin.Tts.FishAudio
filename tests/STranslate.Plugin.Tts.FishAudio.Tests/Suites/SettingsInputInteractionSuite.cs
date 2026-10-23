using System.Reflection;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
        if (group is not (null or "tabs" or "baseline" or "wheel" or "tooltip" or "precision" or "favorites" or "copy" or "fold-focus" or "zero" or "hover")) throw new ArgumentException("Unknown settings input test group", nameof(group));
        if (group is null or "zero")
            foreach (var culture in new[] { "en-US", "de-DE" })
                await RunOnStaThreadAsync(() => VolumeZeroIsCanonicalAcrossInputRoutes(culture));
        if (group is null or "fold-focus")
            foreach (var dark in new[] { false, true })
                await RunOnStaThreadAsync(() => FoldControlsSeparateFocusFromPointerState(dark));
        if (group is "precision") SettingsStorageSuite.NumericSettingsNormalizeRangesAndGranularity();
        if (group is null or "tabs")
            foreach (var language in new[] { "en", "zh-cn", "zh-tw", "ja", "ko" })
                foreach (var dark in new[] { false, true })
                    await RunOnStaThreadAsync(() => SelectedTabsKeepAccentThroughNativeStates(language, dark));
        if (group is null or "baseline") await RunOnStaThreadAsync(ByIdUsesUnchangedSearchTextGeometry);
        if (group is null or "wheel" or "hover") await RunOnStaThreadAsync(NumericWheelRequiresContinuousHover);
        if (group is null or "wheel" or "hover") await RunOnStaThreadAsync(NumericHoverRearmsWhenPointerRemainsOverTarget);
        if (group is null or "wheel") await RunOnStaThreadAsync(RestingNumericWheelEditsWithoutScrolling);
        if (group is null or "wheel") await RunOnStaThreadAsync(SliderWheelEditsWithoutScrolling);
        if (group is null or "wheel" or "tooltip")
            foreach (var culture in new[] { "en-US", "de-DE" })
                await RunOnStaThreadAsync(() => SliderWheelUsesNativeValueToolTipAsync(culture));
        if (group is null or "wheel" or "tooltip") await RunOnStaThreadAsync(SliderWheelToolTipKeepsDragAndTemplateOwnershipAsync);
        if (group is null or "favorites") await RunOnStaThreadAsync(EmptyFavoritesStayVisibleOutsideCollapsedResults);
        if (group is null or "copy")
            foreach (var language in new[] { "en", "zh-cn", "zh-tw", "ja", "ko" })
                await RunOnStaThreadAsync(() => VoiceTitlesExposeCopyOnlyActions(language));
    }

    private static void VolumeZeroIsCanonicalAcrossInputRoutes(string culture)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var settings = new Settings { SelectedModel = "s2-pro", Volume = -.01, Temperature = .3, TopP = .05 };
            var context = CreateContext(settings: settings);
            var storage = (ContextProxy)(object)context;
            string? saved = null;
            storage.SaveSettings = value => saved = JsonSerializer.Serialize(value);
            using var vm = new SettingsViewModel(context, settings);
            var view = CreateView(vm);
            using var source = new HwndSource(new HwndSourceParameters("Fish Audio canonical volume zero fixture") { Width = 600, Height = 800, WindowStyle = 0, PositionX = -10000, PositionY = -10000 });
            source.RootVisual = view;
            var slider = Logical(view).OfType<Slider>().Single(item => BindingOperations.GetBinding(item, Slider.ValueProperty)?.Path.Path == "Volume");
            var editor = Logical(view).OfType<InlineNumericEditor>().Single(item => BindingOperations.GetBinding(item, InlineNumericEditor.ValueProperty)?.Path.Path == "Volume");
            var display = (Button)editor.FindName("DisplayButton");
            var input = (TextBox)editor.FindName("EditorBox");
            var expected = culture == "de-DE" ? "0,00" : "0.00";
            var failures = new List<string>();
            AssertEqual(.3, Logical(view).OfType<Slider>().Single(item => BindingOperations.GetBinding(item, Slider.ValueProperty)?.Path.Path == "Temperature").Value, "Precision initialization must preserve a valid fractional bound value");
            AssertEqual(.05, Logical(view).OfType<Slider>().Single(item => BindingOperations.GetBinding(item, Slider.ValueProperty)?.Path.Path == "TopP").Value, "Zero canonicalization must preserve the positive TopP floor during construction");
            void CheckZero(string route, ToolTip? toolTip = null)
            {
                var values = new[] { slider.Value, editor.Value, vm.Volume, settings.Volume };
                var bits = string.Join(", ", values.Select(value => $"{value:R}/0x{BitConverter.DoubleToInt64Bits(value):X16}"));
                if (values.Any(value => BitConverter.DoubleToInt64Bits(value) != 0)
                    || !Equals(expected, display.Content)
                    || (toolTip is not null && !Equals(expected, toolTip.Content)))
                    failures.Add($"{route}: slider/editor/VM/settings {bits}; label={display.Content}; bubble={toolTip?.Content}");
            }
            foreach (var width in new[] { 400d, 600d })
            foreach (var initial in new[] { -.01, .01 })
            {
                Layout(view, width);
                var track = (Track)slider.Template.FindName("PART_Track", slider);
                slider.SetCurrentValue(Slider.ValueProperty, initial);
                var saves = storage.SaveCount;
                Wheel(track.Thumb, initial < 0 ? 120 : -120);
                CheckZero($"{width}/slider wheel from {initial}", (ToolTip)track.Thumb.ToolTip);
                AssertEqual(saves + 1, storage.SaveCount, "Crossing to zero must save exactly once");
                track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                track.Thumb.RaiseEvent(new DragDeltaEventArgs(0, 0) { RoutedEvent = Thumb.DragDeltaEvent });
                CheckZero($"{width}/drag over the reached zero from {initial}", (ToolTip)track.Thumb.ToolTip);
                track.Thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
                var loaded = JsonSerializer.Deserialize<Settings>(saved!)!;
                SettingsStore.Load(CreateContext(settings: loaded), new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero));
                if (BitConverter.DoubleToInt64Bits(loaded.Volume) != 0) failures.Add("Wheel zero must survive canonical save/reload");
                slider.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                slider.SetCurrentValue(Slider.ValueProperty, initial);
                Wheel(display, initial < 0 ? 120 : -120);
                CheckZero($"{width}/numeric label wheel from {initial}");
                slider.SetCurrentValue(Slider.ValueProperty, initial);
                Layout(view, width);
                saves = storage.SaveCount;
                var distance = -initial / (slider.Maximum - slider.Minimum) * (track.ActualWidth - track.Thumb.ActualWidth);
                track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                track.Thumb.RaiseEvent(new DragDeltaEventArgs(distance, 0) { RoutedEvent = Thumb.DragDeltaEvent });
                CheckZero($"{width}/native drag from {initial}", (ToolTip)track.Thumb.ToolTip);
                AssertEqual(saves + 1, storage.SaveCount, "A native drag to zero must persist one effective value");
                track.Thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
                slider.SetCurrentValue(Slider.ValueProperty, initial);
                display.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                input.Text = culture == "de-DE" ? "-0,00" : "-0.00";
                editor.CommitEdit();
                CheckZero($"{width}/direct entry from {initial}");
            }
            foreach (var initial in new[] { -.01, .01 })
            foreach (var target in new[] { -.001, .001, -1e-16, 1e-16 })
            {
                slider.SetCurrentValue(Slider.ValueProperty, initial);
                Layout(view, 600);
                var track = (Track)slider.Template.FindName("PART_Track", slider);
                var distance = (target - initial) / (slider.Maximum - slider.Minimum) * (track.ActualWidth - track.Thumb.ActualWidth);
                track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                track.Thumb.RaiseEvent(new DragDeltaEventArgs(distance, 0) { RoutedEvent = Thumb.DragDeltaEvent });
                CheckZero($"native zero-tick bucket from {initial} toward {target}", (ToolTip)track.Thumb.ToolTip);
                track.Thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            }
            foreach (var stored in new[] { BitConverter.Int64BitsToDouble(long.MinValue), -.004, .004 })
            {
                var reloadSettings = new Settings { SelectedModel = "s2-pro", Volume = stored };
                var reloadContext = CreateContext(settings: reloadSettings);
                var reloadStorage = (ContextProxy)(object)reloadContext;
                string? canonical = null;
                reloadStorage.SaveSettings = value => canonical = JsonSerializer.Serialize(value);
                SettingsStore.Load(reloadContext, new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero));
                if (BitConverter.DoubleToInt64Bits(reloadSettings.Volume) != 0 || reloadStorage.SaveCount != 1)
                    failures.Add($"stored {stored:R}/0x{BitConverter.DoubleToInt64Bits(stored):X16}: loaded {reloadSettings.Volume:R}/0x{BitConverter.DoubleToInt64Bits(reloadSettings.Volume):X16}, saves {reloadStorage.SaveCount}; must repair and persist canonical zero once");
                if (canonical is not null && BitConverter.DoubleToInt64Bits(JsonSerializer.Deserialize<Settings>(canonical)!.Volume) != 0)
                    failures.Add("Canonical stored zero must retain its positive sign after JSON round trip");
                SettingsStore.Load(reloadContext, new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero));
                if (reloadStorage.SaveCount != 1) failures.Add("Canonical zero reload must be idempotent");
            }
            slider.SetCurrentValue(Slider.ValueProperty, -.01);
            var notifications = new List<string>();
            vm.PropertyChanging += (_, args) => { if (args.PropertyName == nameof(vm.Volume)) notifications.Add("changing"); };
            vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.Volume)) notifications.Add("changed"); };
            var residueSaves = storage.SaveCount;
            slider.SetCurrentValue(Slider.ValueProperty, -1e-16);
            CheckZero("A raw negative floating-point residue at the zero tick");
            AssertEqual("changing,changed", string.Join(",", notifications), "Canonical source publication must retain the normal changing-before-changed notification order");
            AssertEqual(residueSaves + 1, storage.SaveCount, "A raw zero-tick residue must publish and persist only one canonical value");
            var zeroSaves = storage.SaveCount;
            notifications.Clear();
            vm.Volume = BitConverter.Int64BitsToDouble(long.MinValue);
            CheckZero("An equal signed-zero ViewModel write");
            AssertEqual(0, notifications.Count, "A signed-zero source no-op must not raise duplicate notifications");
            display.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            input.Text = "-0.00";
            editor.CommitEdit();
            CheckZero("Invariant negative-zero entry while already at zero");
            var zeroTrack = (Track)slider.Template.FindName("PART_Track", slider);
            zeroTrack.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            CheckZero("Native drag after a negative-zero input commit", (ToolTip)zeroTrack.Thumb.ToolTip);
            zeroTrack.Thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            AssertEqual(zeroSaves, storage.SaveCount, "Canonicalizing an already-zero value must not introduce a redundant save");
            vm.Volume = -.005;
            AssertEqual(-.005, vm.Volume, "The negative rounding midpoint must not be classified as zero");
            AssertEqual(culture == "de-DE" ? "-0,01" : "-0.01", display.Content, "The existing midpoint-away-from-zero display rule must remain intact");
            vm.Volume = 0d;
            slider.Minimum = .05;
            AssertEqual(.05, slider.Value, "Derived value coercion must retain native dynamic minimum clamping");
            slider.SetCurrentValue(Slider.ValueProperty, -.01);
            AssertEqual(.05, slider.Value, "A raw value below a dynamic minimum must remain clamped");
            slider.Maximum = .3;
            slider.SetCurrentValue(Slider.ValueProperty, 1d);
            AssertEqual(.3, slider.Value, "Derived value coercion must retain native dynamic maximum clamping");
            slider.Minimum = -10;
            slider.Maximum = 10;
            slider.SetCurrentValue(Slider.ValueProperty, -.02);
            var negativeTrack = (Track)slider.Template.FindName("PART_Track", slider);
            Wheel(negativeTrack.Thumb, 120);
            AssertEqual(-.01, slider.Value, "A real negative hundredth must remain negative");
            AssertEqual(culture == "de-DE" ? "-0,01" : "-0.01", display.Content, "Canonical zero must not erase real negative values");
            AssertEqual(display.Content, ((ToolTip)negativeTrack.Thumb.ToolTip).Content, "Native bubble and label must retain matching negative hundredths");
            slider.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            AssertEqual("", string.Join(Environment.NewLine, failures), "All zero-producing routes must publish positive zero with identical text");
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
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

    // Removing dwell gating, swallowing early events, or retaining an old
    // target's partial notch each breaks these real compiled routed controls.
    private static void NumericWheelRequiresContinuousHover()
    {
        var settings = new Settings { SelectedModel = "s2-pro" };
        var context = CreateContext(settings: settings);
        var storage = (ContextProxy)(object)context;
        using var vm = new SettingsViewModel(context, settings);
        var view = CreateView(vm);
        Layout(view, 600);
        var page = new ScrollViewer { Content = view, Height = 180, CanContentScroll = false };
        page.Measure(new Size(600, 180)); page.Arrange(new Rect(0, 0, 600, 180)); page.UpdateLayout();
        foreach (var editor in Logical(view).OfType<InlineNumericEditor>())
        {
            var property = BindingOperations.GetBinding(editor, InlineNumericEditor.ValueProperty)!.Path.Path;
            var slider = Logical(view).OfType<NumericSlider>().Single(item => BindingOperations.GetBinding(item, Slider.ValueProperty)?.Path.Path == property);
            var display = (Button)editor.FindName("DisplayButton");
            var input = (TextBox)editor.FindName("EditorBox");
            var track = (Track)slider.Template.FindName("PART_Track", slider);
            foreach (var target in new UIElement[] { slider, display })
            {
                var clock = HoverClockFor(target);
                LeaveHover(target);
                var initial = slider.Value;
                var saves = storage.SaveCount;
                page.ScrollToTop(); page.UpdateLayout();
                RawWheel(target, -120); page.UpdateLayout();
                AssertEqual(initial, slider.Value, "Before entering, wheel must not change the setting");
                AssertEqual(saves, storage.SaveCount, "Before entering, wheel must not save");
                AssertEqual(true, page.VerticalOffset > 0, "Before activation, the real ScrollViewer must move normally");
                target.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
                clock.Advance(249);
                page.ScrollToTop(); page.UpdateLayout();
                RawWheel(target, -60); page.UpdateLayout();
                AssertEqual(initial, slider.Value, "At 249 ms, wheel must not change the value");
                AssertEqual(true, page.VerticalOffset > 0, "At 249 ms ordinary scrolling remains available");
                AssertEqual(false, track.Thumb.ToolTip is ToolTip { IsOpen: true }, "Unqualified input must not show a bubble");
                clock.Advance(1);
                var qualified = RawWheel(target, 60);
                AssertEqual(true, qualified.Handled, "At exactly 250 ms, the target owns the routed event");
                AssertEqual(initial, slider.Value, "An early half notch must not leak into the first qualified half notch");
                AssertEqual(saves, storage.SaveCount, "A first qualified half notch must not save");
                RawWheel(target, 60);
                AssertEqual(Math.Round(initial + .01, 2), Math.Round(slider.Value, 2), "Qualified half notches apply exactly one hundredth");
                AssertEqual(saves + 1, storage.SaveCount, "A qualified effective change saves exactly once");
                if (target == slider) AssertEqual(true, track.Thumb.ToolTip is ToolTip { IsOpen: true }, "Qualified slider input uses the native value bubble");
                slider.SetCurrentValue(Slider.ValueProperty, slider.Maximum);
                saves = storage.SaveCount;
                AssertEqual(true, RawWheel(target, 120).Handled, "A qualified bound still consumes the gesture");
                AssertEqual(saves, storage.SaveCount, "A bound does not save redundantly");
                slider.SetCurrentValue(Slider.ValueProperty, initial);
                RawWheel(target, 60);
                LeaveHover(target);
                target.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
                clock.Advance(249);
                RawWheel(target, 60);
                AssertEqual(initial, slider.Value, "Reentering requires a fresh continuous dwell");
                clock.Advance(1);
                RawWheel(target, 60);
                AssertEqual(initial, slider.Value, "Leave/reenter discards the old partial notch");
                LeaveHover(target);
                // A template or unload invalidates both elapsed time and remainder.
                foreach (var reset in new Action[] {
                    () => target.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)),
                    () => { if (target == slider) slider.OnApplyTemplate(); else editor.OnApplyTemplate(); },
                })
                {
                    EstablishHover(target);
                    RawWheel(target, 60);
                    reset();
                    RawWheel(target, 60);
                    AssertEqual(initial, slider.Value, "Lifecycle reset must discard dwell and fine-wheel remainder");
                    EstablishHover(target);
                    RawWheel(target, 60);
                    AssertEqual(initial, slider.Value, "The first qualified half notch after lifecycle reset remains incomplete");
                    LeaveHover(target);
                }
            }
            EstablishHover(display);
            RawWheel(display, 60);
            display.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            editor.CommitEdit();
            var editingValue = slider.Value;
            RawWheel(display, 60);
            AssertEqual(editingValue, slider.Value, "Entering editing retires numeric-label dwell and remainder");
            EstablishHover(display);
            RawWheel(display, 60);
            AssertEqual(editingValue, slider.Value, "Editing reset prevents old half notches from leaking");
            LeaveHover(display);
            // Moving between descendants stays inside one slider hover; moving
            // between the slider and its label starts an independent dwell.
            EstablishHover(slider);
            RawWheel(track.Thumb, 60);
            RawWheel(track.IncreaseRepeatButton, 60);
            AssertEqual(Math.Round(editingValue + .01, 2), slider.Value, "Thumb and track descendants share one uninterrupted slider hover");
            LeaveHover(slider);
            display.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            var transferredValue = slider.Value;
            RawWheel(display, 120);
            AssertEqual(transferredValue, slider.Value, "Slider dwell cannot authorize a newly entered numeric label");
            LeaveHover(display);
            EstablishHover(display);
            RawWheel(display, 60);
            LeaveHover(display);
            slider.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            RawWheel(slider, 60);
            AssertEqual(transferredValue, slider.Value, "Label dwell and its half notch cannot authorize a newly entered slider");
            HoverClockFor(slider).Advance(250);
            RawWheel(slider, 60);
            AssertEqual(transferredValue, slider.Value, "The first qualified slider half notch must remain independent of the label");
            LeaveHover(slider);
        }
    }

    private static void NumericHoverRearmsWhenPointerRemainsOverTarget()
    {
        var settings = new Settings { SelectedModel = "s2-pro", Speed = 1 };
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = CreateView(vm);
        using var source = new HwndSource(new HwndSourceParameters("Fish Audio hover lifecycle theme-state fixture") { Width = 600, Height = 800, WindowStyle = 0, PositionX = -10000, PositionY = -10000 });
        source.RootVisual = view;
        Layout(view, 600);
        var slider = Logical(view).OfType<NumericSlider>().First();
        var editor = Logical(view).OfType<InlineNumericEditor>().First();
        var display = (Button)editor.FindName("DisplayButton");
        // This is an explicit WPF state fixture, not physical pointer evidence.
        // Hold the real read-only mouse-over property true across lifecycle
        // callbacks; do not raise MouseEnter to reauthorize after the reset.
        foreach (var target in new UIElement[] { slider, display })
        {
            var clock = HoverClockFor(target);
            SetMouseOverCache(target, true);
            var resets = new List<Action> {
                () => { if (target == slider) slider.OnApplyTemplate(); else { var template = display.Template; display.Template = null; display.Template = template; } },
                () => { target.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); target.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); },
            };
            if (target == display) resets.Add(() => { display.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); editor.CommitEdit(); });
            for (var resetIndex = 0; resetIndex < resets.Count; resetIndex++)
            {
                slider.SetCurrentValue(Slider.ValueProperty, 1d);
                resets[resetIndex]();
                Console.WriteLine($"[numeric-hover] {target.GetType().Name}/reset-{resetIndex}: mouseOver={target.IsMouseOver}, visible={target.IsVisible}.");
                AssertEqual(1d, slider.Value, "Lifecycle rearming itself must not change the setting");
                clock.Advance(249);
                AssertEqual(false, RawWheel(target, 60).Handled, "A retained pointer must still wait a fresh 250ms after lifecycle reset");
                clock.Advance(1);
                AssertEqual(true, RawWheel(target, 60).Handled, "A retained pointer must rearm without a new MouseEnter");
                AssertEqual(1d, slider.Value, "The early half notch cannot enter the fresh remainder");
                RawWheel(target, 60);
                AssertEqual(1.01, slider.Value, "A rearmed target must preserve ordinary fine wheel adjustment");
            }
            SetMouseOverCache(target, false);
            LeaveHover(target);
        }
    }

    private static void SetMouseOverCache(UIElement target, bool value)
    {
        // IsMouseOver reads WPF's cached CoreFlags bit, rather than the public
        // dependency-property value. Model that retained native state without
        // moving the user's pointer or manufacturing a new MouseEnter.
        var writeFlag = typeof(UIElement).GetMethod("WriteFlag", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var mouseOver = Enum.Parse(writeFlag.GetParameters()[0].ParameterType, "IsMouseOverCache");
        writeFlag.Invoke(target, [mouseOver, value]);
        AssertEqual(value, target.IsMouseOver, "The explicit retained-pointer fixture must set the actual WPF mouse-over cache");
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
            var step = .01;
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

    // Missing preview ownership, using drag ticks, losing bindings or sharing
    // partial notches across hover sessions each breaks an observable assertion.
    private static void SliderWheelEditsWithoutScrolling()
    {
        var settings = new Settings { SelectedModel = "s2-pro", Speed = 1, Volume = 0, Temperature = .7, TopP = .7 };
        var context = CreateContext(settings: settings);
        var storage = (ContextProxy)(object)context;
        string? saved = null;
        storage.SaveSettings = value => saved = JsonSerializer.Serialize(value);
        using var vm = new SettingsViewModel(context, settings);
        var view = CreateView(vm);
        var page = new Border { Child = view };
        var pageWheels = 0;
        page.MouseWheel += (_, _) => pageWheels++;
        foreach (var (property, initial, increased, decreased, minimum, maximum, belowMaximum, aboveMinimum, tick) in new[]
        {
            ("Speed", 1d, 1.01, .99, .5, 2d, 1.99, .51, .01),
            ("Volume", 0d, .01, -.01, -10d, 10d, 9.99, -9.99, .01),
            ("Temperature", .7, .71, .69, 0d, 1d, .99, .01, .01),
            ("TopP", .7, .71, .69, .05, 1d, .99, .06, .01),
        })
        {
            var editor = Logical(view).OfType<InlineNumericEditor>().Single(item => BindingOperations.GetBinding(item, InlineNumericEditor.ValueProperty)?.Path.Path == property);
            var slider = Logical(view).OfType<Slider>().Single(item => BindingOperations.GetBinding(item, Slider.ValueProperty)?.Path.Path == property);
            var display = (Button)editor.FindName("DisplayButton");
            var input = (TextBox)editor.FindName("EditorBox");
            void AssertValue(double expected)
            {
                AssertEqual(expected, slider.Value, $"{property}: wheel must preserve precise slider values");
                AssertEqual(expected, editor.Value, $"{property}: wheel must synchronize the numeric editor");
                AssertEqual(expected, (double)typeof(SettingsViewModel).GetProperty(property)!.GetValue(vm)!, $"{property}: wheel must update the bound VM");
                AssertEqual(expected, (double)typeof(Settings).GetProperty(property)!.GetValue(settings)!, $"{property}: wheel must update settings");
            }
            foreach (var width in new[] { 400d, 600d })
            {
                Layout(view, width);
                slider.SetCurrentValue(Slider.ValueProperty, initial);
                Slider.IncreaseSmall.Execute(null, slider);
                AssertEqual(true, Math.Abs(slider.Value - increased) < .000001, "Native small-increment keyboard actions must agree with the fine slider step");
                slider.SetCurrentValue(Slider.ValueProperty, initial);
                var track = (Track)slider.Template.FindName("PART_Track", slider);
                // Route from real template descendants, as a physical track or
                // thumb hit does, rather than invoking a private handler.
                var sources = new UIElement[] { track, Visuals(track.Thumb).OfType<UIElement>().Last(), Visuals(track.DecreaseRepeatButton).OfType<UIElement>().Last(), Visuals(track.IncreaseRepeatButton).OfType<UIElement>().Last() };
                foreach (var source in sources)
                {
                    slider.SetCurrentValue(Slider.ValueProperty, initial);
                    var saves = storage.SaveCount;
                    var bubbles = pageWheels;
                    AssertEqual(true, Wheel(source, 120).Handled, "Slider thumb/track preview must consume the wheel");
                    AssertValue(increased);
                    AssertEqual(saves + 1, storage.SaveCount, "An effective slider notch must persist exactly once");
                    AssertEqual(bubbles, pageWheels, "Slider wheel ownership must prevent page scrolling");
                    var loaded = JsonSerializer.Deserialize<Settings>(saved!)!;
                    SettingsStore.Load(CreateContext(settings: loaded), new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero));
                    using (var reopened = new SettingsViewModel(CreateContext(settings: loaded), loaded))
                        AssertEqual(increased, (double)typeof(SettingsViewModel).GetProperty(property)!.GetValue(reopened)!, "Off-tick slider wheel values must survive save and reload");
                    Wheel(source, -240);
                    AssertValue(decreased);
                    Wheel(source, 120);
                    AssertValue(initial);
                    saves = storage.SaveCount;
                    AssertEqual(true, Wheel(source, 60).Handled, "Incomplete slider notches must also prevent page scrolling");
                    AssertValue(initial);
                    AssertEqual(saves, storage.SaveCount, "A half notch must not persist an unchanged value");
                    Wheel(source, 60);
                    AssertValue(increased);
                    Wheel(source, -120);
                    Wheel(source, 60);
                    slider.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                    Wheel(source, 60);
                    AssertValue(initial);
                    Wheel(source, -60);
                    AssertValue(initial);
                    Wheel(source, -120);
                    AssertValue(decreased);
                    slider.SetCurrentValue(Slider.ValueProperty, maximum);
                    saves = storage.SaveCount;
                    AssertEqual(true, Wheel(source, 240).Handled, "Upper-bound slider notches must remain consumed");
                    Wheel(source, 60);
                    AssertValue(maximum);
                    AssertEqual(saves, storage.SaveCount, "Outward upper-bound input must not save");
                    Wheel(source, -120);
                    AssertValue(belowMaximum);
                    Wheel(source, int.MaxValue);
                    AssertValue(maximum);
                    slider.SetCurrentValue(Slider.ValueProperty, minimum);
                    saves = storage.SaveCount;
                    AssertEqual(true, Wheel(source, -240).Handled, "Lower-bound slider notches must remain consumed");
                    Wheel(source, -60);
                    AssertValue(minimum);
                    AssertEqual(saves, storage.SaveCount, "Outward lower-bound input must not save");
                    Wheel(source, 120);
                    AssertValue(aboveMinimum);
                    Wheel(source, int.MinValue);
                    AssertValue(minimum);
                    AssertEqual(bubbles, pageWheels, "Neither partial notches nor bounds may leak page movement");
                }
                slider.SetCurrentValue(Slider.ValueProperty, initial);
                display.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                input.Text = minimum.ToString(System.Globalization.CultureInfo.CurrentCulture);
                var draftSaves = storage.SaveCount;
                Wheel(track.Thumb, 120);
                AssertValue(increased);
                AssertEqual(Visibility.Collapsed, input.Visibility, "A slider wheel value supersedes an active stale draft");
                editor.CommitEdit();
                AssertValue(increased);
                AssertEqual(draftSaves + 1, storage.SaveCount, "Later editor blur must not overwrite or re-save the slider value");
                foreach (var bound in new[] { minimum, maximum })
                {
                    slider.SetCurrentValue(Slider.ValueProperty, bound);
                    display.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    input.Text = initial.ToString(System.Globalization.CultureInfo.CurrentCulture);
                    var draft = input.Text;
                    draftSaves = storage.SaveCount;
                    var bubbles = pageWheels;
                    AssertEqual(true, Wheel(track.Thumb, bound == minimum ? -120 : 120).Handled, "An unchanged slider bound still owns the wheel during editing");
                    AssertValue(bound);
                    AssertEqual(Visibility.Visible, input.Visibility, "An unchanged bound creates no newer value to supersede an active draft");
                    AssertEqual(draft, input.Text, "An outward bound gesture must retain the draft until its ordinary completion");
                    AssertEqual(draftSaves, storage.SaveCount, "An unchanged bound must not save the draft");
                    AssertEqual(bubbles, pageWheels, "An unchanged bound must not scroll even during editing");
                    editor.CommitEdit();
                    AssertValue(initial);
                    AssertEqual(draftSaves + 1, storage.SaveCount, "The still-current valid draft retains its normal blur commit");
                }
                Wheel(display, 60);
                Wheel(track.Thumb, 60);
                AssertValue(initial);
                Wheel(track.IncreaseRepeatButton, 60);
                AssertValue(increased);
                display.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                slider.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                slider.SetCurrentValue(Slider.ValueProperty, initial);
                Wheel(display, 60);
                AssertValue(initial);
                display.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                AssertEqual(tick, slider.TickFrequency, "Wheel and dragging must share hundredth steps");
                AssertEqual(true, slider.IsSnapToTickEnabled, "Dragging must retain snapping");
            }
        }
    }

    private static async Task SliderWheelUsesNativeValueToolTipAsync(string culture)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var settings = new Settings { SelectedModel = "s2-pro", Speed = 1, Volume = -9.37, Temperature = .7, TopP = .7 };
            var context = CreateContext(settings: settings);
            var storage = (ContextProxy)(object)context;
            using var vm = new SettingsViewModel(context, settings);
            var view = CreateView(vm);
            using var source = new HwndSource(new HwndSourceParameters("Fish Audio native tooltip fixture") { Width = 600, Height = 800, WindowStyle = 0, PositionX = -10000, PositionY = -10000 });
            source.RootVisual = view;
            Layout(view, 600);
            foreach (var slider in Logical(view).OfType<Slider>().ToArray())
            {
                var nativePeer = new Slider();
                var row = (Panel)slider.Parent;
                row.Children.Add(nativePeer);
                Layout(view, 600);
                AssertEqual(nativePeer.Template, slider.Template, "The wheel-capable slider must retain the same resolved native theme template as Slider");
                row.Children.Remove(nativePeer);
                Layout(view, 600);
                var track = (Track)slider.Template.FindName("PART_Track", slider);
                var thumb = track.Thumb;
                var initial = slider.Value;
                var originalToolTip = new object();
                thumb.ToolTip = originalToolTip;
                var saves = storage.SaveCount;
                Wheel(track.DecreaseRepeatButton, 60);
                var toolTip = thumb.ToolTip as ToolTip;
                AssertNotNull(toolTip, "Wheel over the actual slider must show its native current-value tooltip");
                AssertEqual(true, toolTip!.IsOpen, "A partial notch must display the current value immediately");
                AssertEqual(initial.ToString("N2", CultureInfo.CurrentCulture), toolTip.Content, "The tooltip must use two decimal places in the display culture");
                AssertEqual(thumb, toolTip.PlacementTarget, "The value bubble must remain anchored to the actual thumb");
                AssertEqual(PlacementMode.Custom, toolTip.Placement, "Wheel must use the same native placement as dragging");
                AssertEqual(saves, storage.SaveCount, "Showing a value bubble must not save or re-snap a partial notch");
                Wheel(thumb, 60);
                AssertEqual((double)((decimal)initial + .01m), slider.Value, "Updating the bubble must not apply an additional native drag step");
                AssertEqual(saves + 1, storage.SaveCount, "Refreshing the native tooltip must not duplicate a wheel save");
                AssertEqual(slider.Value.ToString("N2", CultureInfo.CurrentCulture), toolTip.Content, "An open bubble must track the current wheel value");
                Layout(view, 600);
                var wheelTemplate = toolTip.Template;
                var wheelPlacement = toolTip.CustomPopupPlacementCallback!(new Size(50, 20), new Size(12, 12), new Point());
                thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                AssertEqual(toolTip, thumb.ToolTip, "Real dragging must reuse the native bubble rather than open a second one");
                AssertEqual(wheelTemplate, toolTip.Template, "Dragging and wheel input must have exactly the same resolved appearance");
                AssertEqual(wheelPlacement[0].Point, toolTip.CustomPopupPlacementCallback!(new Size(50, 20), new Size(12, 12), new Point())[0].Point, "The native wheel and drag popup anchors must agree");
                Wheel(thumb, -120);
                AssertEqual(initial, slider.Value, "Wheel during a real drag must adjust only once");
                AssertEqual(initial.ToString("N2", CultureInfo.CurrentCulture), toolTip.Content, "Wheel during dragging must refresh the current native bubble");
                slider.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                AssertEqual(true, toolTip.IsOpen, "Leaving while dragging must retain native drag ownership");
                thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
                AssertEqual(false, toolTip.IsOpen, "Completing a real drag must close its native bubble");
                AssertEqual(originalToolTip, thumb.ToolTip, "Wheel-to-drag transition must preserve the original thumb tooltip");
                var distance = .01 / (slider.Maximum - slider.Minimum) * (track.ActualWidth - thumb.ActualWidth);
                thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                thumb.RaiseEvent(new DragDeltaEventArgs(distance, 0) { RoutedEvent = Thumb.DragDeltaEvent });
                AssertEqual(true, Math.Abs(slider.Value - (double)((decimal)initial + .01m)) < .000001, "Actual native thumb dragging must allow one-hundredth increments");
                thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
                Wheel(thumb, -120);
                AssertEqual(true, toolTip.IsOpen, "Wheel after dragging must reopen the same native bubble");
                slider.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                AssertEqual(false, toolTip.IsOpen, "Leaving the slider must close a wheel-owned bubble immediately");
                AssertEqual(originalToolTip, thumb.ToolTip, "Leaving must restore the original thumb tooltip");
                Wheel(thumb, 120);
                slider.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                AssertEqual(false, toolTip.IsOpen, "Unloading must close an open wheel bubble");
                AssertEqual(originalToolTip, thumb.ToolTip, "Unloading must restore the original tooltip");
                slider.SetCurrentValue(Slider.ValueProperty, slider.Maximum);
                saves = storage.SaveCount;
                Wheel(track.IncreaseRepeatButton, 120);
                AssertEqual(slider.Maximum.ToString("N2", CultureInfo.CurrentCulture), toolTip.Content, "An outward bound gesture must show the current bounded value");
                AssertEqual(saves, storage.SaveCount, "Showing a bound tooltip must not save");
                slider.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            }
            var idleSlider = Logical(view).OfType<Slider>().First();
            var idleThumb = ((Track)idleSlider.Template.FindName("PART_Track", idleSlider)).Thumb;
            var idleOriginal = idleThumb.ToolTip;
            Wheel(idleThumb, 120);
            var idleToolTip = (ToolTip)idleThumb.ToolTip;
            AssertEqual(true, idleToolTip.IsOpen, "The idle lifecycle starts with a visible value bubble");
            var idle = Stopwatch.StartNew();
            while (idleToolTip.IsOpen && idle.Elapsed < TimeSpan.FromSeconds(2))
                await Task.Delay(20);
            AssertEqual(false, idleToolTip.IsOpen, "A wheel-owned bubble must disappear after a bounded idle interval");
            AssertEqual(true, idle.Elapsed >= TimeSpan.FromMilliseconds(700), "The bubble must remain visible long enough to read the wheel value");
            AssertEqual(idleOriginal, idleThumb.ToolTip, "Idle close must restore the original tooltip");
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    private static async Task SliderWheelToolTipKeepsDragAndTemplateOwnershipAsync()
    {
        var settings = new Settings { SelectedModel = "s2-pro", Speed = 1 };
        var context = CreateContext(settings: settings);
        var storage = (ContextProxy)(object)context;
        using var vm = new SettingsViewModel(context, settings);
        var view = CreateView(vm);
        using var source = new HwndSource(new HwndSourceParameters("Fish Audio tooltip ownership fixture") { Width = 600, Height = 800, WindowStyle = 0, PositionX = -10000, PositionY = -10000 });
        source.RootVisual = view;
        Layout(view, 600);
        var slider = Logical(view).OfType<Slider>().Single(item => BindingOperations.GetBinding(item, Slider.ValueProperty)?.Path.Path == "Speed");
        var track = (Track)slider.Template.FindName("PART_Track", slider);
        var originalToolTip = new object();
        track.Thumb.ToolTip = originalToolTip;
        var dragStarts = 0;
        var dragCompletions = 0;
        slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => dragStarts++));
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => dragCompletions++));
        var saves = storage.SaveCount;
        Wheel(track.Thumb, 120);
        var toolTip = (ToolTip)track.Thumb.ToolTip;
        await Task.Delay(650);
        AssertEqual(true, toolTip.IsOpen, "The native wheel bubble must survive dispatcher processing");
        Wheel(track.IncreaseRepeatButton, 120);
        await Task.Delay(650);
        AssertEqual(true, toolTip.IsOpen, "A repeated wheel gesture must restart the idle deadline without a queued native close");
        AssertEqual(1.02.ToString("N2", CultureInfo.CurrentCulture), toolTip.Content, "Repeated wheel input must keep the current native bubble content");
        AssertEqual(0, dragStarts, "Wheel feedback must not raise synthetic routed drag events");
        AssertEqual(0, dragCompletions, "Wheel refresh must not publish synthetic drag completions");
        AssertEqual(saves + 2, storage.SaveCount, "Repeated bubble refresh must save exactly once per effective notch");
        track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        Wheel(track.Thumb, -120);
        await Task.Delay(1100);
        AssertEqual(true, toolTip.IsOpen, "A wheel idle deadline must not close a bubble owned by an active drag");
        AssertEqual(1, dragStarts, "Only the actual routed drag start must reach drag listeners");
        track.Thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        AssertEqual(false, toolTip.IsOpen, "Actual drag completion retains ownership of closing its bubble");
        AssertEqual(originalToolTip, track.Thumb.ToolTip, "Drag completion after wheel refresh must restore the original tooltip");
        AssertEqual(1, dragCompletions, "Only the actual routed completion must reach drag listeners");
        Wheel(track.Thumb, 60);
        var beforeTemplate = slider.Value;
        var nativeTemplate = slider.Template;
        slider.Template = null;
        slider.ApplyTemplate();
        slider.Template = nativeTemplate;
        Layout(view, 600);
        AssertEqual(false, toolTip.IsOpen, "Reapplying a template must close the old thumb's bubble");
        AssertEqual(originalToolTip, track.Thumb.ToolTip, "The removed thumb must regain its original tooltip");
        var replacement = (Track)slider.Template.FindName("PART_Track", slider);
        AssertEqual(false, ReferenceEquals(track.Thumb, replacement.Thumb), "The template lifecycle fixture must create a replacement thumb");
        Wheel(replacement.Thumb, 60);
        AssertEqual(beforeTemplate, slider.Value, "A replacement template must discard the old hover's half notch");
        var replacementToolTip = (ToolTip)replacement.Thumb.ToolTip;
        AssertEqual(replacement.Thumb, replacementToolTip.PlacementTarget, "The native popup must follow the replacement thumb");
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        AssertEqual(true, replacementToolTip.IsOpen, "The replacement thumb's popup must remain open through native dispatcher work");
        slider.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        AssertEqual(false, replacementToolTip.IsOpen, "The replacement thumb's popup must close on unload");
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

    private sealed class HoverClock : TimeProvider
    {
        private long _milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _milliseconds;
        internal void Advance(long milliseconds) => _milliseconds += milliseconds;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, HoverClock> HoverClocks = new();
    private static UIElement HoverTarget(UIElement target)
    {
        for (DependencyObject? current = target; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is NumericSlider slider) return slider;
            if (current is Button { Name: "DisplayButton" } button) return button;
        }
        return target;
    }
    private static HoverClock HoverClockFor(UIElement target) => HoverClocks.GetValue(target, owner =>
    {
        var clock = new HoverClock();
        if (owner is NumericSlider slider) slider.WheelTimeProvider = clock;
        else if (owner is Button button)
            for (DependencyObject? current = button; current is not null; current = VisualTreeHelper.GetParent(current))
                if (current is InlineNumericEditor editor) { editor.WheelTimeProvider = clock; break; }
        return clock;
    });
    private static void EstablishHover(UIElement target)
    {
        target = HoverTarget(target);
        var clock = HoverClockFor(target);
        target.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        clock.Advance(250);
    }
    private static void LeaveHover(UIElement target) => target.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
    private static MouseWheelEventArgs Wheel(UIElement target, int delta)
    {
        EstablishHover(target);
        return RawWheel(target, delta);
    }
    private static MouseWheelEventArgs RawWheel(UIElement target, int delta)
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
