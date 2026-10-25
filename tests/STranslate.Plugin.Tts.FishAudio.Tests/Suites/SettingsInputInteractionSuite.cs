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
        if (group is not (null or "tabs" or "baseline" or "wheel" or "tooltip" or "precision" or "favorites" or "copy" or "fold-focus" or "zero" or "hover" or "point")) throw new ArgumentException("Unknown settings input test group", nameof(group));
        if (group is null or "point")
            foreach (var field in new[] {
                ("Speed", .5, 2d, 1.67), ("Volume", -10d, 10d, -3.21),
                ("Temperature", 0d, 1d, .83), ("TopP", .05, 1d, .82),
            })
                await RunOnStaThreadAsync(() => SliderTrackClickMovesToPoint(field));
        if (group is null or "zero")
            foreach (var culture in new[] { "en-US", "de-DE", "ru-RU", "tr-TR" })
                await RunOnStaThreadAsync(() => VolumeZeroIsCanonicalAcrossInputRoutes(culture));
        if (group is null or "fold-focus")
            foreach (var dark in new[] { false, true })
                foreach (var name in new[] { "VoiceSearchResultsToggle", "FavoriteVoicesToggle" })
                    await RunOnStaThreadAsync(() => FoldControlsSeparateFocusFromPointerState(dark, name));
        if (group is "precision") SettingsStorageSuite.NumericSettingsNormalizeRangesAndGranularity();
        if (group is null or "tabs")
            foreach (var language in UiLocales.All)
                foreach (var dark in new[] { false, true })
                    await RunOnStaThreadAsync(() => SelectedTabsKeepAccentThroughNativeStates(language, dark));
        if (group is null or "baseline") await RunOnStaThreadAsync(ByIdUsesUnchangedSearchTextGeometry);
        if (group is null or "wheel" or "hover")
            foreach (var property in new[] { "Speed", "Volume", "Temperature", "TopP" })
                await RunOnStaThreadAsync(() => NumericWheelRequiresContinuousHover(property));
        if (group is null or "wheel" or "hover") await RunOnStaThreadAsync(NumericHoverRearmsWhenPointerRemainsOverTarget);
        if (group is null or "wheel")
            foreach (var property in new[] { "Speed", "Volume", "Temperature", "TopP" })
                await RunOnStaThreadAsync(() => RestingNumericWheelEditsWithoutScrolling(property));
        if (group is null or "wheel")
            foreach (var field in new[]
            {
                ("Speed", 1d, 1.01, .99, .5, 2d, 1.99, .51, .01),
                ("Volume", 0d, .01, -.01, -10d, 10d, 9.99, -9.99, .01),
                ("Temperature", .7, .71, .69, 0d, 1d, .99, .01, .01),
                ("TopP", .7, .71, .69, .05, 1d, .99, .06, .01),
            })
                await RunOnStaThreadAsync(() => SliderWheelEditsWithoutScrolling(field));
        if (group is null or "wheel" or "tooltip")
            foreach (var culture in new[] { "en-US", "de-DE", "ru-RU", "tr-TR" })
                foreach (var field in new[] { "Speed", "Volume", "Temperature", "TopP" })
                    await ObserveNativeToolTipActionAsync($"{culture}/{field}", stage => SliderWheelUsesNativeValueToolTipAsync(culture, field, stage));
        if (group is null or "wheel" or "tooltip") await RunOnStaThreadAsync(SliderWheelToolTipKeepsDragAndTemplateOwnershipAsync);
        if (group is null or "favorites") await RunOnStaThreadAsync(EmptyFavoritesStayVisibleOutsideCollapsedResults);
        if (group is null or "copy")
            foreach (var language in UiLocales.All)
                foreach (var width in new[] { 320d, 400d, 480d, 640d })
                    foreach (var mode in new[] { VoiceBrowseMode.Search, VoiceBrowseMode.ById, VoiceBrowseMode.Favorites })
                        await RunOnStaThreadAsync(() => VoiceTitlesExposeCopyOnlyActions(language, width, mode));
    }

    // Disabling native move-to-point on any compiled slider must leave its
    // actual routed track press unable to update the bound setting as expected.
    private static void SliderTrackClickMovesToPoint((string Property, double Minimum, double Maximum, double Requested) field)
    {
        var settings = new Settings { SelectedModel = "s2-pro" };
        var context = CreateContext(settings: settings);
        var storage = (ContextProxy)(object)context;
        using var vm = new SettingsViewModel(context, settings);
        var view = CreateView(vm);
        using var source = new HwndSource(new HwndSourceParameters("Fish Audio native point-click fixture") { Width = 600, Height = 2000, WindowStyle = 0, PositionX = -10000, PositionY = -10000 });
        source.RootVisual = view;
        Layout(view, 600);
        var (property, minimum, maximum, requested) = field;
        var slider = Logical(view).OfType<NumericSlider>().Single(item => BindingOperations.GetBinding(item, Slider.ValueProperty)?.Path.Path == property);
        var track = (Track)slider.Template.FindName("PART_Track", slider);
        var saves = storage.SaveCount;
        var x = track.Thumb.ActualWidth / 2 + (requested - minimum) / (maximum - minimum) * (track.ActualWidth - track.Thumb.ActualWidth);
        var press = TrackPress(source, track, new Point(x, track.ActualHeight / 2), requested > slider.Value ? track.IncreaseRepeatButton : track.DecreaseRepeatButton);
        // WPF converts the cached position through integer screen pixels.
        // Snap the actual native point to the contract's .01 ticks rather
        // than assuming that an arbitrary requested hundredth is reachable.
        var expected = Math.Clamp(Math.Round(minimum + Math.Round((press.PointValue - minimum) / .01, MidpointRounding.AwayFromZero) * .01, 2), minimum, maximum);
        AssertEqual(true, press.Args.Handled, $"{property} must consume a native point-click before repeat-button paging");
        AssertEqual(true, Math.Abs(expected - slider.Value) < 1e-9, $"{property} must move to the clicked hundredth in one press");
        AssertEqual(expected, Math.Round((double)typeof(Settings).GetProperty(property)!.GetValue(settings)!, 2), $"{property} point-click must reach its bound persisted setting");
        AssertEqual(saves + 1, storage.SaveCount, $"{property} point-click must save exactly once");
        Layout(view, 600);
        foreach (var (pointX, bound, target) in new[] {
            (0d, minimum, track.DecreaseRepeatButton),
            (track.ActualWidth, maximum, track.IncreaseRepeatButton),
        })
        {
            saves = storage.SaveCount;
            TrackPress(source, track, new Point(pointX, track.ActualHeight / 2), target);
            AssertEqual(bound, slider.Value, $"{property} endpoint click must respect its existing bounds");
            AssertEqual(saves + 1, storage.SaveCount, $"{property} changed endpoint must save once");
            Layout(view, 600);
            TrackPress(source, track, new Point(pointX, track.ActualHeight / 2), target);
            AssertEqual(saves + 1, storage.SaveCount, $"{property} unchanged endpoint must not save redundantly");
        }
    }

    private static (MouseButtonEventArgs Args, double PointValue) TrackPress(HwndSource source, Track track, Point point, UIElement target)
    {
        // Model WPF's cached native input position on the shared STA, restoring
        // it immediately. No OS cursor movement or installed-host claim.
        var mouse = Mouse.PrimaryDevice;
        var inputSource = typeof(MouseDevice).GetField("_inputSource", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var lastPosition = typeof(MouseDevice).GetField("_lastPosition", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previousSource = inputSource.GetValue(mouse);
        var previousPosition = lastPosition.GetValue(mouse);
        try
        {
            inputSource.SetValue(mouse, source);
            var rootPoint = track.TranslatePoint(point, (UIElement)source.RootVisual);
            lastPosition.SetValue(mouse, source.CompositionTarget.TransformToDevice.Transform(rootPoint));
            var args = new MouseButtonEventArgs(mouse, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent };
            var pointValue = track.ValueFromPoint(mouse.GetPosition(track));
            target.RaiseEvent(args);
            return (args, pointValue);
        }
        finally
        {
            inputSource.SetValue(mouse, previousSource);
            lastPosition.SetValue(mouse, previousPosition);
        }
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
            var expected = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator == "," ? "0,00" : "0.00";
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
                input.Text = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator == "," ? "-0,00" : "-0.00";
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
            AssertEqual(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator == "," ? "-0,01" : "-0.01", display.Content, "The existing midpoint-away-from-zero display rule must remain intact");
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
            AssertEqual(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator == "," ? "-0,01" : "-0.01", display.Content, "Canonical zero must not erase real negative values");
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

    private static void FoldControlsSeparateFocusFromPointerState(bool dark, string name)
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
            AssertEqual(new Thickness(10, 5, 68, 6), search.Padding, "Search retains native text insets and reserves its embedded Clear and Adjust actions");
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
            AssertEqual(68d, byId.Padding.Right, "By ID must reserve its embedded Clear and Paste actions");
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
    private static void NumericWheelRequiresContinuousHover(string property)
    {
        var settings = new Settings { SelectedModel = "s2-pro" };
        var context = CreateContext(settings: settings);
        var storage = (ContextProxy)(object)context;
        using var vm = new SettingsViewModel(context, settings);
        var view = CreateView(vm);
        Layout(view, 600);
        var page = new ScrollViewer { Content = view, Height = 180, CanContentScroll = false };
        page.Measure(new Size(600, 180)); page.Arrange(new Rect(0, 0, 600, 180)); page.UpdateLayout();
        var editor = Logical(view).OfType<InlineNumericEditor>().Single(item => BindingOperations.GetBinding(item, InlineNumericEditor.ValueProperty)?.Path.Path == property);
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
            clock.Advance(349);
            page.ScrollToTop(); page.UpdateLayout();
            RawWheel(target, -60); page.UpdateLayout();
            AssertEqual(initial, slider.Value, "At 349 ms, wheel must not change the value");
            AssertEqual(true, page.VerticalOffset > 0, "At 349 ms ordinary scrolling remains available");
            AssertEqual(false, track.Thumb.ToolTip is ToolTip { IsOpen: true }, "Unqualified input must not show a bubble");
            clock.Advance(1);
            var qualified = RawWheel(target, 60);
            AssertEqual(true, qualified.Handled, "At exactly 350 ms, the target owns the routed event");
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
            clock.Advance(349);
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
        HoverClockFor(slider).Advance(350);
        RawWheel(slider, 60);
        AssertEqual(transferredValue, slider.Value, "The first qualified slider half notch must remain independent of the label");
        LeaveHover(slider);
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
                clock.Advance(349);
                AssertEqual(false, RawWheel(target, 60).Handled, "A retained pointer must still wait a fresh 350ms after lifecycle reset");
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

    private static void RestingNumericWheelEditsWithoutScrolling(string property)
    {
        var settings = new Settings { SelectedModel = "s2-pro", Speed = 1, Volume = 0, Temperature = .7, TopP = .7 };
        var context = CreateContext(settings: settings);
        var storage = (ContextProxy)(object)context;
        using var vm = new SettingsViewModel(context, settings);
        var view = CreateView(vm);
        var page = new Border { Child = view };
        var pageWheels = 0;
        page.MouseWheel += (_, _) => pageWheels++;
        var editor = Logical(view).OfType<InlineNumericEditor>().Single(item => BindingOperations.GetBinding(item, InlineNumericEditor.ValueProperty)?.Path.Path == property);
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

    // Missing preview ownership, using drag ticks, losing bindings or sharing
    // partial notches across hover sessions each breaks an observable assertion.
    private static void SliderWheelEditsWithoutScrolling((string Property, double Initial, double Increased, double Decreased, double Minimum, double Maximum, double BelowMaximum, double AboveMinimum, double Tick) field)
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
        var (property, initial, increased, decreased, minimum, maximum, belowMaximum, aboveMinimum, tick) = field;
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

    internal static async Task NativeToolTipDiagnosticsPreserveFailuresWithoutRetryAsync()
    {
        // These contracts catch replacement/retry, lost failure identity and
        // unbounded observation without dispatching WPF or waiting on real time.
        var outer = new TimeoutException("Synthetic outer action deadline");
        Func<Task>? queued = null;
        var dispatches = 0;
        var runs = 0;
        var observations = 0;
        Exception? caught = null;
        var output = new List<string>();
        TimeSpan? executionBudget = null;
        try
        {
            await ObserveNativeToolTipActionAsync("queued-contract", stage => { runs++; stage("original action body"); return Task.CompletedTask; },
                dispatch: (callback, timeout) => { executionBudget = timeout; dispatches++; queued = callback; throw outer; },
                observe: async completion => { observations++; await queued!(); await completion; }, write: output.Add);
        }
        catch (Exception error) { caught = error; }
        AssertEqual(true, ReferenceEquals(outer, caught), "Queued observation must preserve the original outer timeout identity");
        AssertEqual(1, observations, "A queued outer deadline must observe its same action exactly once");
        AssertEqual(1, dispatches, "Observation must not dispatch a replacement action");
        AssertEqual(1, runs, "The original queued action must run once without retry");
        AssertEqual(true, output.Any(line => line.Contains("before entry", StringComparison.Ordinal)), "Queued failure must identify its pre-entry phase");
        AssertEqual(true, output.Any(line => line.Contains("original action body", StringComparison.Ordinal)), "Failure output must retain the action phases");
        AssertEqual<TimeSpan?>(TimeSpan.FromSeconds(20), executionBudget, "Numeric tooltip injected dispatch receives the shared execution guard");
        AssertEqual(true, output.Any(line => line.Contains("queued with shared 20s async action deadline", StringComparison.Ordinal)), "Numeric tooltip diagnostics describe the actual injected guard");

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? originalAction = null;
        dispatches = 0; runs = 0; observations = 0; caught = null; output.Clear();
        try
        {
            await ObserveNativeToolTipActionAsync("entered-contract", async stage => { runs++; stage("entered action waiting"); await release.Task; stage("same action resumed"); },
                dispatch: (callback, _) => { dispatches++; originalAction = callback(); throw outer; },
                observe: async completion => { observations++; AssertEqual(false, completion.IsCompleted, "Observation must see the incomplete entered action"); release.SetResult(); await completion; }, write: output.Add);
        }
        catch (Exception error) { caught = error; }
        await originalAction!.WaitAsync(RegressionExecution.Timeout);
        AssertEqual(true, ReferenceEquals(outer, caught), "Late entered-action completion must leave the original deadline failed");
        AssertEqual(1, dispatches, "Entered observation must not dispatch again");
        AssertEqual(1, runs, "The entered action must not be replaced or retried");
        AssertEqual(1, observations, "The entered action gets one observation");
        AssertEqual(true, output.Any(line => line.Contains("after entry", StringComparison.Ordinal)), "Entered failure must identify its execution phase");
        AssertEqual(true, output.Any(line => line.Contains("same action resumed", StringComparison.Ordinal)), "Failure phases must include the same action's late completion");

        foreach (var inner in new Exception[] { new TimeoutException("Synthetic action-owned timeout"), new InvalidOperationException("Synthetic action failure") })
        {
            runs = 0; observations = 0; caught = null; output.Clear();
            try
            {
                await ObserveNativeToolTipActionAsync("inner-contract", _ => { runs++; throw inner; },
                    dispatch: (callback, _) => callback(), observe: _ => { observations++; return Task.CompletedTask; }, write: output.Add);
            }
            catch (Exception error) { caught = error; }
            AssertEqual(true, ReferenceEquals(inner, caught), "An action-owned failure must preserve its exact exception identity");
            AssertEqual(0, observations, "An action-owned TimeoutException must not be treated as the outer deadline");
            AssertEqual(1, runs, "An action-owned failure must never retry");
            AssertEqual(true, output.Any(line => line.Contains("action failed", StringComparison.Ordinal)), "An action-owned failure must dump its phase evidence");
        }

        runs = 0; observations = 0; caught = null; output.Clear();
        try
        {
            await ObserveNativeToolTipActionAsync("observation-failure-contract", _ => { runs++; return Task.CompletedTask; },
                dispatch: (callback, _) => { queued = callback; throw outer; },
                observe: _ => { observations++; throw new IOException("Synthetic observation failure"); }, write: output.Add);
        }
        catch (Exception error) { caught = error; }
        AssertEqual(true, ReferenceEquals(outer, caught), "Observation failure must not replace the original outer exception");
        AssertEqual(1, observations, "Observation failure must not trigger a second observer");
        AssertEqual(0, runs, "Observation failure must not execute replacement work");
        AssertEqual(true, output.Any(line => line.Contains("observation ended (IOException)", StringComparison.Ordinal)), "The observation failure must remain visible in safe diagnostics");
        await queued!();
        AssertEqual(1, runs, "The original queued action can still complete once after observation fails");

        var exactClock = new ManualStreamingTimeProvider();
        var incomplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exactBound = BoundNativeToolTipObservation(incomplete.Task, exactClock);
        exactClock.Advance(TimeSpan.FromMilliseconds(19999));
        AssertEqual(false, exactBound.IsCompleted, "The actual observation helper must remain pending at 19999ms");
        exactClock.Advance(TimeSpan.FromMilliseconds(1));
        AssertEqual(true, exactBound.IsCompleted, "The actual helper Task must expire at exactly 20000ms, independently of caller scheduling");
        caught = null;
        try { await exactBound; }
        catch (Exception error) { caught = error; }
        AssertEqual(true, caught is TimeoutException, "The bounded helper must report its twenty-second observation deadline");
        AssertEqual(false, incomplete.Task.IsCompleted, "Observation expiry must not cancel or complete the original action");
        incomplete.SetResult();

        var clock = new ManualStreamingTimeProvider();
        var observing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observationRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? sameCompletion = null;
        dispatches = 0; runs = 0; observations = 0; caught = null; output.Clear();
        var bounded = ObserveNativeToolTipActionAsync("observation-timeout-contract", _ => { runs++; return Task.CompletedTask; },
            dispatch: (callback, _) => { dispatches++; queued = callback; throw outer; },
            observe: completion => { sameCompletion = completion; observations++; observing.SetResult(); return observationRelease.Task; }, write: output.Add, observationTimeProvider: clock);
        await observing.Task.WaitAsync(RegressionExecution.Timeout);
        clock.Advance(TimeSpan.FromMilliseconds(19999));
        AssertEqual(false, bounded.IsCompleted, "An incomplete observer must not retire before the twenty-second boundary");
        AssertEqual(false, sameCompletion!.IsCompleted, "The original action remains pending before observation expiry");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        try { await bounded.WaitAsync(RegressionExecution.Timeout); }
        catch (Exception error) { caught = error; }
        AssertEqual(true, ReferenceEquals(outer, caught), "Observation timeout must preserve the original outer exception");
        AssertEqual(1, observations, "The observation bound must not retry its observer");
        AssertEqual(1, dispatches, "The observation bound must not dispatch another action");
        AssertEqual(0, runs, "Observation expiry must not run replacement work");
        AssertEqual(false, sameCompletion.IsCompleted, "Observation expiry must leave the original completion pending");
        AssertEqual(false, observationRelease.Task.IsCompleted, "Observation expiry must not cancel the observed task");
        AssertEqual(true, output.Any(line => line.Contains("observation ended (TimeoutException)", StringComparison.Ordinal)), "The exact observation timeout must be reported");
        await queued!();
        await sameCompletion.WaitAsync(RegressionExecution.Timeout);
        observationRelease.SetResult();
        AssertEqual(1, runs, "Only the same original queued action may complete after observation expiry");

        output.Clear(); runs = 0;
        await ObserveNativeToolTipActionAsync("success-contract", stage => { runs++; stage("buffered successful phase"); return Task.CompletedTask; },
            dispatch: (callback, _) => callback(), write: output.Add);
        AssertEqual(1, runs, "A successful action executes once");
        AssertEqual(1, output.Count, "Successful diagnostics must emit exactly one compact case summary");
        AssertEqual(false, output[0].Contains("buffered successful phase", StringComparison.Ordinal), "Success must not dump the detailed phase buffer");
    }

    private static async Task ObserveNativeToolTipActionAsync(string name, Func<Action<string>, Task> action,
        Func<Func<Task>, TimeSpan, Task>? dispatch = null, Func<Task, Task>? observe = null, Action<string>? write = null, TimeProvider? observationTimeProvider = null)
    {
        var executionTimeout = RegressionExecution.Timeout;
        var elapsed = Stopwatch.StartNew();
        var phases = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? actionFailure = null;
        long enteredAt = -1;
        void Stage(string phase) => phases.Enqueue($"{elapsed.ElapsedMilliseconds}ms thread={Environment.CurrentManagedThreadId}: {phase}");
        void Dump() { foreach (var phase in phases) (write ?? Console.WriteLine)($"[numeric-tooltip/{name}] {phase}"); }
        Stage(FormattableString.Invariant($"queued with shared {executionTimeout.TotalSeconds}s async action deadline"));
        try
        {
            await (dispatch ?? ((callback, timeout) => RunOnStaThreadAsync(callback, timeout)))(async () =>
            {
                Interlocked.Exchange(ref enteredAt, elapsed.ElapsedMilliseconds);
                Stage("entered");
                try { await action(Stage); }
                catch (Exception error) { Volatile.Write(ref actionFailure, error); Stage($"action failed ({error.GetType().Name})"); throw; }
                finally { Stage("action completed after cleanup"); completion.TrySetResult(); }
            }, executionTimeout);
            (write ?? Console.WriteLine)($"[numeric-tooltip/{name}] completed in {elapsed.ElapsedMilliseconds}ms; entry={Volatile.Read(ref enteredAt)}ms; phases={phases.Count}; native field lifecycle.");
        }
        catch (TimeoutException error) when (!ReferenceEquals(error, Volatile.Read(ref actionFailure)))
        {
            Stage(Volatile.Read(ref enteredAt) < 0 ? "outer deadline before entry; observing same action off STA for at most 20s" : "outer deadline after entry; observing same action off STA for at most 20s");
            try
            {
                // Bound the scheduled observation too; never replace/cancel/retry STA work.
                await BoundNativeToolTipObservation(Task.Run(() => (observe ?? (pending => pending))(completion.Task)), observationTimeProvider).ConfigureAwait(false);
                Stage("same action observation completed; original deadline remains failed");
            }
            catch (Exception observationFailure) { Stage($"same action observation ended ({observationFailure.GetType().Name}); original deadline remains failed"); }
            Dump();
            throw;
        }
        catch
        {
            Dump();
            throw;
        }
    }

    private static Task BoundNativeToolTipObservation(Task observation, TimeProvider? timeProvider = null) =>
        observation.WaitAsync(TimeSpan.FromSeconds(20), timeProvider ?? TimeProvider.System);

    private static async Task SliderWheelUsesNativeValueToolTipAsync(string culture, string field, Action<string> stage)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            stage($"culture {culture} begin");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            stage("culture applied");
            var settings = new Settings { SelectedModel = "s2-pro", Speed = 1, Volume = -9.37, Temperature = .7, TopP = .7 };
            var context = CreateContext(settings: settings);
            var storage = (ContextProxy)(object)context;
            stage("viewmodel construction begin");
            using var vm = new SettingsViewModel(context, settings);
            stage("viewmodel construction end; CreateView construction/Light theme/nested Layout begin");
            var view = CreateView(vm);
            stage("CreateView construction/Light theme/nested Layout end; HwndSource construction begin");
            using var source = new HwndSource(new HwndSourceParameters("Fish Audio native tooltip fixture") { Width = 600, Height = 800, WindowStyle = 0, PositionX = -10000, PositionY = -10000 });
            stage("HwndSource construction end; source attach begin");
            source.RootVisual = view;
            stage("source attach end; attached Layout begin");
            Layout(view, 600);
            stage("attached Layout end");
            try
            {
                var slider = Logical(view).OfType<Slider>().Single(item => BindingOperations.GetBinding(item, Slider.ValueProperty)?.Path.Path == field);
                stage($"{field} native peer construction/add begin");
                var nativePeer = new Slider();
                var row = (Panel)slider.Parent;
                row.Children.Add(nativePeer);
                stage($"{field} native peer added; Layout begin");
                Layout(view, 600);
                stage($"{field} native peer Layout end");
                AssertEqual(nativePeer.Template, slider.Template, "The wheel-capable slider must retain the same resolved native theme template as Slider");
                stage($"{field} native peer remove begin");
                row.Children.Remove(nativePeer);
                stage($"{field} native peer removed; Layout begin");
                Layout(view, 600);
                stage($"{field} peer-removed Layout end; native bubble begin");
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
                stage($"{field} wheel bubble updated; Layout begin");
                Layout(view, 600);
                stage($"{field} bubble Layout end; wheel-to-drag begin");
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
                stage($"{field} wheel-to-drag leave/completion end; native drag begin");
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
                stage($"{field} native drag and wheel leave end; unload begin");
                Wheel(thumb, 120);
                slider.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                AssertEqual(false, toolTip.IsOpen, "Unloading must close an open wheel bubble");
                AssertEqual(originalToolTip, thumb.ToolTip, "Unloading must restore the original tooltip");
                stage($"{field} unload end; bound bubble begin");
                slider.SetCurrentValue(Slider.ValueProperty, slider.Maximum);
                saves = storage.SaveCount;
                Wheel(track.IncreaseRepeatButton, 120);
                AssertEqual(slider.Maximum.ToString("N2", CultureInfo.CurrentCulture), toolTip.Content, "An outward bound gesture must show the current bounded value");
                AssertEqual(saves, storage.SaveCount, "Showing a bound tooltip must not save");
                slider.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                stage($"{field} bound bubble and leave complete");
                if (field == "Speed")
                {
                    stage("idle bubble setup begin");
                    var idleSlider = slider;
                    var idleThumb = ((Track)idleSlider.Template.FindName("PART_Track", idleSlider)).Thumb;
                    var idleOriginal = idleThumb.ToolTip;
                    Wheel(idleThumb, 120);
                    var idleToolTip = (ToolTip)idleThumb.ToolTip;
                    AssertEqual(true, idleToolTip.IsOpen, "The idle lifecycle starts with a visible value bubble");
                    var idle = Stopwatch.StartNew();
                    stage("idle wait begin; original 2s loop and minimum 700ms assertion");
                    while (idleToolTip.IsOpen && idle.Elapsed < TimeSpan.FromSeconds(2))
                    {
                        await Task.Delay(20);
                        stage($"idle resume at {idle.ElapsedMilliseconds}ms; open={idleToolTip.IsOpen}");
                    }
                    stage($"idle wait end after {idle.ElapsedMilliseconds}ms");
                    AssertEqual(false, idleToolTip.IsOpen, "A wheel-owned bubble must disappear after a bounded idle interval");
                    AssertEqual(true, idle.Elapsed >= TimeSpan.FromMilliseconds(700), "The bubble must remain visible long enough to read the wheel value");
                    AssertEqual(idleOriginal, idleThumb.ToolTip, "Idle close must restore the original tooltip");
                }
            }
            finally { stage("source then viewmodel cleanup begin"); }
        }
        finally
        {
            stage("owned resource scope exited; culture restoration begin");
            CultureInfo.CurrentCulture = previousCulture;
            stage("culture restoration end");
        }
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

    private static void VoiceTitlesExposeCopyOnlyActions(string language, double width, VoiceBrowseMode mode)
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
        using var source = new HwndSource(new HwndSourceParameters("Fish Audio title copy tooltip fixture") { Width = 640, Height = 2000, WindowStyle = 0, PositionX = -10000, PositionY = -10000 });
        source.RootVisual = view;
        var cardId = mode switch { VoiceBrowseMode.Search => searchId, VoiceBrowseMode.ById => byId, _ => favoriteId };
        vm.BrowseMode = mode;
        Layout(view, width);
        var titles = Visuals(view).OfType<Button>().Where(button => ReferenceEquals(button.Command, vm.CopyVoiceIdCommand) && Shown(button)).ToArray();
        AssertEqual(2, titles.Length, "Only the selected title and the visible browse title must expose copy actions");
        AssertEnumerableEqual(new[] { selectedId, cardId }.Order(), titles.Select(button => (string)button.CommandParameter).Order(), "All four title locations must pass their canonical voice ID");
        foreach (var title in titles)
        {
            AssertEqual(view.FindResource("STranslate_Plugin_Tts_FishAudio_CopyVoiceId"), AutomationProperties.GetName(title), "Title copy actions retain the localized action name");
            var text = (TextBlock)title.Content;
            var tooltip = (ToolTip)title.ToolTip;
            tooltip.PlacementTarget = title;
            tooltip.IsOpen = true;
            try
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                var tooltipText = Visuals(tooltip).OfType<TextBlock>().ToArray();
                AssertEqual(true, tooltipText.Any(block => block.Text == (string)view.FindResource("STranslate_Plugin_Tts_FishAudio_CopyVoiceId")), "The native tooltip keeps the localized Copy Voice ID caption");
                AssertEqual(true, tooltipText.Any(block => block.Text == text.Text && block.TextTrimming == TextTrimming.None), "The same tooltip includes the full untruncated title");
                AssertEqual<object?>(null, text.ToolTip, "Hovering the title text cannot replace the copy hint with a child tooltip");
                AssertEqual(text.Text, AutomationProperties.GetHelpText(text), "The full title remains available to automation");
                AssertEqual(text.Text, AutomationProperties.GetHelpText(title), "The copy button exposes the full title alongside its localized action name");
            }
            finally { tooltip.IsOpen = false; }
            Near(text.ActualHeight, title.ActualHeight, "A text-only title action must preserve the title's native line height");
            AssertEqual(0, Visuals(title).OfType<Border>().Count(), "Copyable titles must not gain a rectangular button surround");
            // Native input filters hidden/non-hit-testable sibling panels; a raw
            // visual hit can otherwise select their retained layout geometry.
            var titleHit = view.InputHitTest(title.TranslatePoint(new Point(title.ActualWidth / 2, title.ActualHeight / 2), view)) as DependencyObject;
            AssertEqual(true, titleHit is not null && IsInside(titleHit, title), $"The visible title's input-hit belongs to its dedicated copy button ({language}/{width}/{mode})");
            var cursor = new QueryCursorEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.QueryCursorEvent, Cursor = Cursors.Arrow };
            ((UIElement)titleHit!).RaiseEvent(cursor);
            AssertEqual(Cursors.Hand, cursor.Cursor, "The actual title hit resolves the copy-action cursor through its scoped native route");
            var before = writes.Count;
            typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(title, null);
            AssertEqual(before + 1, writes.Count, "A title click must write exactly one clipboard value");
            AssertEqual((string)title.CommandParameter, writes[^1], "The clicked title must copy its own complete voice ID");
            AssertEqual("STranslate_Plugin_Tts_FishAudio_CopyVoiceId_Success", snackbar.LastSuccess, "A successful clipboard write must use the host success snackbar");
        }
        foreach (var text in Visuals(view).OfType<TextBlock>().Where(text => Shown(text) && (text.Text == "·" || text.Text.EndsWith("description", StringComparison.Ordinal) || text.Text.EndsWith("author", StringComparison.Ordinal))))
        {
            AssertEqual(false, HasButtonAncestor(text), "Author, dot and description remain outside the dedicated copy button");
            var hit = view.InputHitTest(text.TranslatePoint(new Point(text.ActualWidth / 2, text.ActualHeight / 2), view)) as DependencyObject;
            AssertEqual(true, hit is not null && !HasButtonAncestor(hit) && hit is not Button, "The actual visible ordinary-text hit is not covered by a copy/action button");
            var before = writes.Count;
            text.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
            text.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
            AssertEqual(before, writes.Count, "Routed author/dot/description clicks cannot copy through a card-wide handler");
        }
        if (width == 640 && mode == VoiceBrowseMode.Favorites)
        {
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
        }
        AssertEqual(selectedId, settings.VoiceId, "Copying titles must never Apply or Clear a voice");
        AssertEqual(0, storage.SaveCount, "Copying titles must not persist unrelated settings");
        AssertEqual(favoriteFile, File.ReadAllText(Path.Combine(directory, FavoriteVoiceStore.FileName)), "Copying titles must preserve favorites exactly");
        AssertEqual(null, vm.PreviewingVoiceId, "Copying titles must not start preview playback");
        AssertEqual(null, vm.PreparingPreviewVoiceId, "Copying titles must not start preview preparation");
        AssertEqual(0, http.GetCallCount + http.GetAsStreamCallCount + http.PostAsBytesCallCount, "Copying any title must not call Fish Audio");
        if (width == 640 && mode == VoiceBrowseMode.Favorites)
        {
            foreach (var suffix in new[] { "", "_Success", "_Failed" })
                AssertNotNull(view.TryFindResource("STranslate_Plugin_Tts_FishAudio_CopyVoiceId" + suffix), "Every copy action and snackbar string must exist in all seven UI locales");
        }
    }

    private static bool IsInside(DependencyObject child, DependencyObject owner)
    {
        for (DependencyObject? current = child; current is not null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, owner)) return true;
        return false;
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
        clock.Advance(350);
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
