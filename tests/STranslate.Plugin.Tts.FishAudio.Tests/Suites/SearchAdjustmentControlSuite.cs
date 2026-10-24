using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Controls;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static StaTestHost;
using static TestAssertions;

internal static class SearchAdjustmentControlSuite
{
    internal static async Task RunAllAsync(string? group = null)
    {
        if (group is "radio")
        {
            foreach (var dark in new[] { false, true }) await RunOnStaThreadAsync(() => RadioAppearanceMatchesNativePeer(dark));
            return;
        }
        if (group is "edges")
        {
            await RunOnStaThreadAsync(ShortViewportKeepsPopupInsideVisibleArea);
            return;
        }
        await RunOnStaThreadAsync(CaptionsAndKeyboardApplyAsync);
        if (group is "titles") return;
        foreach (var dark in new[] { false, true }) await RunOnStaThreadAsync(() => RadioAppearanceMatchesNativePeer(dark));
        await RunOnStaThreadAsync(DraftsApplyAndNestedInputAsync);
        await RunOnStaThreadAsync(OverlayOwnershipAndLifetime);
        await RunOnStaThreadAsync(WindowAndScrollLifetime);
        await RunOnStaThreadAsync(ShortViewportKeepsPopupInsideVisibleArea);
        foreach (var locale in UiLocales.All)
        foreach (var dark in new[] { false, true })
        foreach (var dpi in new[] { 1d, 1.25, 1.5 })
            await RunOnStaThreadAsync(() =>
            {
                GeometryAndFlags(locale, dark, dpi);
                Console.WriteLine($"[search-adjustment] {locale}/{(dark ? "dark" : "light")}/{dpi * 100:0}%: flags, native viewport/caret and popup containment passed at 320/480 DIP.");
            });
    }

    private static FrameworkElement Control(SettingsView view)
    {
        var control = view.FindName("SearchAdjustments") as FrameworkElement;
        AssertNotNull(control, "The compiled search row must embed the search adjustment action");
        return control!;
    }
    private static T Part<T>(FrameworkElement control, string name) where T : class => (T)control.FindName(name);
    private static void Click(FrameworkElement control, string name) => Part<Button>(control, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Open(FrameworkElement control) { Click(control, "AdjustmentButton"); Drain(); }
    private static bool IsOpen(FrameworkElement control) => Part<Popup>(control, "AdjustmentPopup").IsOpen;
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    private static HwndSource Attach(SettingsView view, int width = 480)
    {
        var screenDpi = VisualTreeHelper.GetDpi(new DrawingVisual());
        var source = new HwndSource(new HwndSourceParameters("Fish Audio search adjustment fixture")
        {
            Width = (int)Math.Ceiling(width * screenDpi.DpiScaleX),
            Height = (int)Math.Ceiling(1600 * screenDpi.DpiScaleY),
            WindowStyle = unchecked((int)0x80000000), PositionX = 120, PositionY = 120,
        });
        source.RootVisual = view;
        SettingsLayoutSuite.Layout(view, width);
        return source;
    }

    private static async Task CaptionsAndKeyboardApplyAsync()
    {
        var settings = new Settings();
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = """{"total":0,"items":[]}""";
        using var vm = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        using var source = Attach(view);
        var control = Control(view);
        Open(control);
        AssertEqual("Relevance first", Part<RadioButton>(control, "RelevanceRadio").Content as string,
            "Each native sort choice presents only its readable title without helper descriptions");
        AssertEqual("Most used first", Part<RadioButton>(control, "PopularityRadio").Content as string, "Popularity retains its rich title");
        AssertEqual("Newest first", Part<RadioButton>(control, "CreationRadio").Content as string, "Creation order retains its rich title");
        Part<RadioButton>(control, "PopularityRadio").Focus();
        var surface = Part<Border>(control, "PopupSurface");
        var enter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(surface)!, Environment.TickCount, Key.Enter)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = Part<RadioButton>(control, "PopularityRadio") };
        InputManager.Current.ProcessInput(enter); Drain();
        AssertEqual(true, enter.Handled, "Enter in popup choices applies once and cannot reach the search TextBox binding");
        AssertEqual(false, IsOpen(control), "Enter closes search adjustments");
        AssertEqual(1, http.GetCallCount, "Keyboard Apply invokes exactly one page-one GET");
        await vm.SearchVoicesCommand.ExecuteAsync(null);
        AssertEqual(2, http.GetCallCount, "Explicit Search remains independent of popup keyboard Apply");
    }

    // Draft controls must have no network side effects. Apply must capture the
    // current TextBox once, including an unchanged refresh and keyboard activation.
    private static async Task DraftsApplyAndNestedInputAsync()
    {
        var settings = new Settings();
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = """{"total":24,"items":[]}""";
        using var vm = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        using var source = Attach(view);
        var control = Control(view);
        Open(control);
        AssertEqual(true, IsOpen(control), "The embedded action opens a native popup");
        var combo = Part<ComboBox>(control, "LanguageCombo");
        AssertEqual(11, combo.Items.Count, "One All option plus the ten approved scalar languages");
        combo.SelectedIndex = 4;
        Part<RadioButton>(control, "CreationRadio").IsChecked = true;
        AssertEqual(0, http.GetCallCount, "Draft language/sort edits never call the provider");
        Click(control, "ResetButton");
        AssertEqual(0, combo.SelectedIndex, "Reset drafts All");
        AssertEqual(true, Part<RadioButton>(control, "RelevanceRadio").IsChecked, "Reset drafts relevance");
        AssertEqual(0, http.GetCallCount, "Reset never calls the provider");
        Click(control, "CloseButton");
        AssertEqual(false, IsOpen(control), "Close dismisses without applying");
        AssertEqual(0, http.GetCallCount, "Close discards drafts without a request");
        Open(control);
        combo.SelectedIndex = 4;
        Part<RadioButton>(control, "PopularityRadio").IsChecked = true;
        var input = (TextBox)view.FindName("VoiceSearchInput");
        input.Text = "visible & query";
        Drain();
        Click(control, "ApplyButton");
        Drain();
        AssertEqual(false, IsOpen(control), "Apply closes immediately");
        AssertEqual(1, http.GetCallCount, "Apply invokes exactly one request");
        AssertEqual("https://api.fish.audio/model?page_size=6&page_number=1&sort_by=task_count&title=visible%20%26%20query&language=ja", http.LastGetUrl, "Apply captures visible text and current drafts");
        Open(control);
        AssertEqual(4, combo.SelectedIndex, "Reopening starts from successfully applied language");
        var surface = Part<Border>(control, "PopupSurface");
        combo.IsDropDownOpen = true; Drain();
        var nested = NativeComboPopup(combo);
        AssertEqual(true, nested.IsOpen, "Language picker uses the native child Popup");
        var nestedRoot = (FrameworkElement)nested.Child;
        InputManager.Current.ProcessInput(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = nestedRoot });
        AssertEqual(true, IsOpen(control), "Blank space inside the exact native dropdown is owned input");
        Visuals(nestedRoot).OfType<ScrollViewer>().First().ScrollToVerticalOffset(50); Drain();
        AssertEqual(true, IsOpen(control), "Nested dropdown scrolling is not ancestor page scrolling");
        // Native ComboBox may dismiss its child after a staged mouse event; reopen
        // that same picker before testing Escape against its active source.
        combo.IsDropDownOpen = true; Drain();
        var key = Escape((FrameworkElement)NativeComboPopup(combo).Child);
        AssertEqual(true, key.Handled, "The first Escape is consumed by the dropdown");
        AssertEqual(false, combo.IsDropDownOpen, "First Escape closes only the native dropdown");
        AssertEqual(true, IsOpen(control), "First Escape preserves the adjustment drafts");
        Escape(surface);
        AssertEqual(false, IsOpen(control), "Second Escape closes adjustments");
        AssertEqual(true, Part<Button>(control, "AdjustmentButton").IsKeyboardFocused, "Escape returns focus to the embedded trigger");
        Open(control);
        Click(control, "ApplyButton"); Drain();
        AssertEqual(2, http.GetCallCount, "Unchanged compiled Apply still refreshes exactly once");
        input.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent)); Drain();
        AssertEqual(2, http.GetCallCount, "Blur after Apply cannot issue a duplicate search");
        await vm.SearchVoicesCommand.ExecuteAsync(null);
        AssertEqual(3, http.GetCallCount, "The public Search command remains usable with applied options");
    }

    // The per-view token prevents competing overlays, stale releases and leaked
    // input subscriptions after tab/rebind/unload/ancestor-scroll retirement.
    private static void OverlayOwnershipAndLifetime()
    {
        var settings = new Settings { VoiceId = AppliedKey, CachedVoice = new() { Title = "Voice", Languages = new[] { "en", "zh", "de", "ja", "fr", "es", "ko", "ar", "ru", "pt" } } };
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        using var source = Attach(view, 400);
        var control = Control(view);
        var tags = Visuals(view).OfType<VoiceLanguageTags>().First();
        tags.OpenPopup();
        AssertEqual(true, tags.IsPopupOpen, "The selected card supplies a real overflow overlay");
        Open(control);
        AssertEqual(false, tags.IsPopupOpen, "Search adjustments retire language overflow");
        tags.OpenPopup();
        AssertEqual(false, IsOpen(control), "Language overflow retires search adjustments");
        tags.ClosePopup();
        Open(control);
        vm.BrowseMode = VoiceBrowseMode.ById; Drain();
        AssertEqual(false, IsOpen(control), "Changing browse tabs closes the search overlay");
        vm.BrowseMode = VoiceBrowseMode.Search; Drain();
        Open(control);
        control.DataContext = new object();
        AssertEqual(false, IsOpen(control), "Rebinding closes the overlay and retires subscriptions");
        control.DataContext = vm;
        Open(control);
        control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        AssertEqual(false, IsOpen(control), "Unloading closes the overlay");
        Open(control);
        var outside = (TextBox)view.FindName("VoiceSearchInput");
        InputManager.Current.ProcessInput(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = outside });
        AssertEqual(false, IsOpen(control), "Outside presses close the overlay");
    }

    private static void WindowAndScrollLifetime()
    {
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        var scroll = new ScrollViewer { Content = view, Height = 320 };
        var window = new Window { Content = scroll, Width = 480, Height = 340, Left = 120, Top = 120, Opacity = 0,
            ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
        try
        {
            window.Show(); Drain();
            var control = Control(view);
            control.BringIntoView(); Drain();
            Open(control);
            AssertEqual(true, IsOpen(control), "A compiled Window owns the adjustment popup");
            window.Left += 10; window.Width = 400; Drain();
            AssertEqual(true, IsOpen(control), "Moving/resizing the native owner repositions without dropping ownership");
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 50); Drain();
            AssertEqual(false, IsOpen(control), "Ancestor page scrolling retires the overlay");
            control.BringIntoView(); Drain(); Open(control);
            window.Hide(); Drain();
            AssertEqual(false, IsOpen(control), "Hiding the owner closes the popup");
            window.Show(); Drain(); Open(control);
            typeof(Window).GetMethod("OnDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [EventArgs.Empty]);
            AssertEqual(false, IsOpen(control), "Window deactivation retires input subscriptions and the native child");
        }
        finally { window.Close(); }
    }

    // Monitor-only Y clamping lets an options popup extend below a short host
    // window. Both anchor edges must fit the actually visible ancestor viewport.
    private static void ShortViewportKeepsPopupInsideVisibleArea()
    {
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        var scroll = new ScrollViewer { Content = view, Height = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var dpi = VisualTreeHelper.GetDpi(new DrawingVisual());
        using var source = new HwndSource(new HwndSourceParameters("Fish Audio short visible popup viewport fixture")
        { Width = (int)(400 * dpi.DpiScaleX), Height = (int)(240 * dpi.DpiScaleY), WindowStyle = unchecked((int)0x80000000), PositionX = 120, PositionY = 120 });
        source.RootVisual = scroll;
        SettingsLayoutSuite.Layout(scroll, 400);
        var control = Control(view);
        var naturalTop = control.TransformToAncestor(view).Transform(new Point()).Y;
        foreach (var nearBottom in new[] { true, false })
        {
            scroll.ScrollToVerticalOffset(Math.Max(0, naturalTop - (nearBottom ? 190 : 12)));
            Drain(); Open(control);
            var surface = Part<Border>(control, "PopupSurface");
            var presenter = Visuals(scroll).OfType<ScrollContentPresenter>().First();
            var viewportTop = presenter.PointToScreen(new Point()).Y;
            var viewportBottom = presenter.PointToScreen(new Point(0, presenter.ActualHeight)).Y;
            var popupTop = surface.PointToScreen(new Point()).Y;
            var popupBottom = surface.PointToScreen(new Point(0, surface.ActualHeight)).Y;
            AssertEqual(true, popupTop >= viewportTop + 7 * dpi.DpiScaleY - 1 && popupBottom <= viewportBottom - 7 * dpi.DpiScaleY + 1,
                $"The popup remains inside the short visible viewport at both anchor edges ({nearBottom}: popup {popupTop}..{popupBottom}; viewport {viewportTop}..{viewportBottom})");
            var trigger = Part<Button>(control, "AdjustmentButton");
            var triggerTop = trigger.PointToScreen(new Point()).Y;
            var triggerBottom = trigger.PointToScreen(new Point(0, trigger.ActualHeight)).Y;
            AssertEqual(true, nearBottom ? popupBottom <= triggerTop - 7 * dpi.DpiScaleY + 1 : popupTop >= triggerBottom + 7 * dpi.DpiScaleY - 1,
                "A short viewport chooses the available side of the trigger with a visible gap");
            var contentScroll = Part<ScrollViewer>(control, "PopupScroll");
            AssertEqual(true, contentScroll.ExtentHeight > contentScroll.ViewportHeight, "A popup taller than available space scrolls internally");
            Click(control, "CloseButton");
        }
    }

    // Incorrect inset/padding/DPI conversion or selected-only flag templates must
    // fail against the actual themed TextBox viewport and popup presentation source.
    private static void GeometryAndFlags(string locale, bool dark, double dpi)
    {
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        SetLocale(view, locale);
        view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light);
        using var source = Attach(view);
        var control = Control(view);
        var input = (TextBox)view.FindName("VoiceSearchInput");
        input.Text = new string('W', 160); input.CaretIndex = input.Text.Length;
        // Materialize lazy native text views before actual root DPI, preserving
        // the runner's established voice-card first-use ordering.
        SettingsLayoutSuite.Layout(view, 480);
        _ = input.GetRectFromCharacterIndex(input.Text.Length);
        VisualTreeHelper.SetRootDpi(view, new DpiScale(dpi, dpi));
        foreach (var width in new[] { 320d, 480d })
        {
            SettingsLayoutSuite.Layout(view, width); Drain();
            input.CaretIndex = input.Text.Length;
            input.ScrollToHorizontalOffset(double.MaxValue); Drain();
            AssertEqual(dpi, VisualTreeHelper.GetDpi(input).DpiScaleX, "The native input receives the requested actual DPI");
            var button = Part<Button>(control, "AdjustmentButton");
            AssertEqual(28d, button.ActualWidth, "Embedded action keeps a 28-DIP hit target");
            AssertEqual(28d, button.ActualHeight, "Embedded action keeps a 28-DIP hit target");
            var buttonBounds = button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize));
            var inputBounds = input.TransformToAncestor(view).TransformBounds(new Rect(input.RenderSize));
            AssertEqual(true, Math.Abs(inputBounds.Right - buttonBounds.Right - 4) <= 1 / dpi, "The action has a four-DIP right inset");
            var textView = Visuals(input).OfType<FrameworkElement>().Single(item => item.GetType().Name == "TextBoxView");
            var viewport = textView.TransformToAncestor(view).TransformBounds(new Rect(textView.RenderSize));
            AssertEqual(true, viewport.Right <= buttonBounds.Left - 3, "The native text viewport reserves room for the embedded action");
            var caret = input.GetRectFromCharacterIndex(input.Text.Length);
            AssertEqual(true, caret.Right <= input.ActualWidth - 34, $"The final caret remains within the reserved text viewport ({locale}/{dpi}/{width}: {caret.Right}/{input.ActualWidth}, offset {input.HorizontalOffset})");
            Open(control);
            var surface = Part<Border>(control, "PopupSurface");
            AssertEqual(true, surface.ActualWidth <= 324 && surface.ActualWidth >= 280, "Popup body is 324 DIP or clamped to the narrow viewport");
            var left = surface.PointToScreen(new Point()).X;
            var right = surface.PointToScreen(new Point(surface.ActualWidth, 0)).X;
            var ownerLeft = view.PointToScreen(new Point()).X;
            var ownerRight = view.PointToScreen(new Point(view.ActualWidth, 0)).X;
            // Explicit visual-root DPI exercises text/layout. The separate native
            // HWND still uses its monitor's screen transform, not that injection.
            var screenScale = (ownerRight - ownerLeft) / view.ActualWidth;
            AssertEqual(true, left >= ownerLeft + 7 * screenScale - 1 && right <= ownerRight - 7 * screenScale + 1, $"Device-pixel placement contains the native Popup in the settings viewport ({locale}/{dpi}/{width}: {left}..{right} owner {ownerLeft}..{ownerRight}, popupDpi={VisualTreeHelper.GetDpi(surface).DpiScaleX}, viewDpi={VisualTreeHelper.GetDpi(view).DpiScaleX})");
            AssertEqual(true, surface.Background is SolidColorBrush { Color.A: 255 }, "Popup surface resolves an opaque native theme brush");
            var combo = Part<ComboBox>(control, "LanguageCombo");
            AssertEnumerableEqual(new string?[] { null, "en", "zh", "de", "ja", "fr", "es", "ko", "ar", "ru", "pt" }, combo.Items.Cast<object>().Select(Code), "Presets retain the exact scalar codes and order");
            combo.SelectedIndex = 1; Drain();
            AssertEqual(true, Visuals(combo).OfType<LanguageFlagImage>().Any(item => item.IsVisible), "The selected native ComboBox item renders a flag");
            combo.IsDropDownOpen = true; Drain();
            var child = NativeComboPopup(combo).Child;
            AssertEqual(10, Visuals(child).OfType<LanguageFlagImage>().Count(item => item.IsVisible), "Every language dropdown item renders its flag while All has none");
            combo.IsDropDownOpen = false;
            foreach (var next in UiLocales.All)
            {
                SetLocale(view, next); Drain();
                AssertEqual("en", Code(combo.SelectedItem), "Live locale replacement preserves the draft selection");
                AssertEqual(true, IsOpen(control), "Live locale replacement updates the same open popup");
            }
            SetLocale(view, locale); Drain();
            var relevance = Part<RadioButton>(control, "RelevanceRadio").Content as string;
            AssertEqual(locale == "zh-cn" ? "相关优先" : locale == "en" ? "Relevance first" : (string)view.FindResource("STranslate_Plugin_Tts_FishAudio_Search_Relevance"), relevance, "Score uses the approved localized relevance-first label");
            Click(control, "CloseButton");
        }
    }

    private static string? Code(object item) => (string?)item.GetType().GetProperty("Code")!.GetValue(item);

    // A popup-only template/color override must diverge from a separate native
    // peer using the exact same SDK theme, including its checked inner glyph.
    private static void RadioAppearanceMatchesNativePeer(bool dark)
    {
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light);
        // This shared STA has no Application/OS accent initialization. Supply a
        // concrete palette only; keep the actual native template and text brushes.
        var accent = new SolidColorBrush(dark ? Color.FromRgb(0x4C, 0xC2, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8));
        view.Resources["RadioButtonOuterEllipseCheckedFill"] = accent;
        view.Resources["RadioButtonOuterEllipseCheckedStroke"] = accent;
        var peer = new RadioButton { Content = "Native peer", GroupName = "IndependentNativePeer" };
        ((Panel)view.Content).Children.Insert(0, peer);
        using var source = Attach(view);
        var control = Control(view);
        Open(control);
        var actual = Part<RadioButton>(control, "RelevanceRadio");
        foreach (var isChecked in new[] { false, true })
        {
            actual.IsChecked = isChecked; peer.IsChecked = isChecked;
            foreach (var radio in new[] { actual, peer })
            {
                radio.ApplyTemplate();
                VisualStateManager.GoToState(radio, "Normal", false);
                VisualStateManager.GoToState(radio, isChecked ? "Checked" : "Unchecked", false);
            }
            Drain();
            var actualGrid = Visuals(actual).OfType<Grid>().Single(grid => grid.Height == 32);
            var peerGrid = Visuals(peer).OfType<Grid>().Single(grid => grid.Height == 32);
            var actualBitmap = RenderGlyph(actualGrid, actual);
            var peerBitmap = RenderGlyph(peerGrid, peer);
            var actualPixels = new byte[actualBitmap.PixelWidth * actualBitmap.PixelHeight * 4];
            var peerPixels = new byte[peerBitmap.PixelWidth * peerBitmap.PixelHeight * 4];
            actualBitmap.CopyPixels(actualPixels, actualBitmap.PixelWidth * 4, 0);
            peerBitmap.CopyPixels(peerPixels, peerBitmap.PixelWidth * 4, 0);
            AssertEnumerableEqual(peerPixels, actualPixels, "Popup radio checked/unchecked raster matches the independent native themed peer");
            foreach (var name in new[] { "OuterEllipse", "CheckOuterEllipse", "CheckGlyph" })
            {
                var glyph = (System.Windows.Shapes.Ellipse)actual.Template.FindName(name, actual);
                var native = (System.Windows.Shapes.Ellipse)peer.Template.FindName(name, peer);
                AssertEqual(native.Fill?.ToString(), glyph.Fill?.ToString(), "Popup and native peer use the same glyph fill");
                AssertEqual(native.Stroke?.ToString(), glyph.Stroke?.ToString(), "Popup and native peer use the same glyph border");
                AssertEqual(native.StrokeThickness, glyph.StrokeThickness, "Popup and native peer use the same border thickness");
            }
            var inner = (System.Windows.Shapes.Ellipse)actual.Template.FindName("CheckGlyph", actual);
            var outer = (System.Windows.Shapes.Ellipse)actual.Template.FindName("CheckOuterEllipse", actual);
            AssertEqual(accent.Color, ((SolidColorBrush)outer.Fill).Color, "The radio fixture has an initialized opaque checked accent palette");
            Console.WriteLine($"[search-radio] {(dark ? "dark" : "light")}/{(isChecked ? "checked" : "unchecked")}: outer={outer.ActualWidth:0.##} fill={outer.Fill}, inner={inner.ActualWidth:0.##} fill={inner.Fill}, nativeDpi={VisualTreeHelper.GetDpi(actual).DpiScaleX:0.##}; peer raster matches.");
            var directory = Path.Combine(Path.GetDirectoryName(FindRepoFile("build.ps1"))!, ".scratch", "task-4", "radio-proof");
            Directory.CreateDirectory(directory);
            SaveBitmap(actualBitmap, Path.Combine(directory, $"{(dark ? "dark" : "light")}-{(isChecked ? "checked" : "unchecked")}-popup.png"));
            SaveBitmap(peerBitmap, Path.Combine(directory, $"{(dark ? "dark" : "light")}-{(isChecked ? "checked" : "unchecked")}-peer.png"));
        }
        Click(control, "CloseButton");
    }

    private static RenderTargetBitmap RenderGlyph(FrameworkElement glyph, FrameworkElement resourceOwner)
    {
        var dpi = VisualTreeHelper.GetDpi(glyph);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(glyph.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(glyph.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle((Brush)resourceOwner.FindResource("FlyoutPresenterBackground"), null, new Rect(glyph.RenderSize));
            dc.DrawRectangle(new VisualBrush(glyph)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(glyph.RenderSize),
                Stretch = Stretch.Fill,
            }, null, new Rect(glyph.RenderSize));
        }
        bitmap.Render(drawing);
        return bitmap;
    }
    private static void SaveBitmap(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static Popup NativeComboPopup(ComboBox combo) => (Popup)(combo.Template.FindName("PART_Popup", combo) ?? combo.Template.FindName("Popup", combo));
    private static KeyEventArgs Escape(FrameworkElement root)
    {
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(root)!, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        InputManager.Current.ProcessInput(key); return key;
    }
    private static void SetLocale(FrameworkElement view, string locale)
    {
        var previous = view.Resources.MergedDictionaries.SingleOrDefault(item => item.Source?.LocalPath.Replace('\\', '/').Contains("/Languages/", StringComparison.Ordinal) == true);
        if (previous is not null) view.Resources.MergedDictionaries.Remove(previous);
        view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(FindRepoFile($"STranslate.Plugin.Tts.FishAudio/Languages/{locale}.xaml"), UriKind.Absolute) });
    }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }
}
