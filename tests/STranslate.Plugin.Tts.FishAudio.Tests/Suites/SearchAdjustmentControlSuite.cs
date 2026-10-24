using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
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
        await RunOnStaThreadAsync(DraftsApplyAndTileInputAsync);
        await RunOnStaThreadAsync(OverlayOwnershipAndLifetime);
        await RunOnStaThreadAsync(WindowAndScrollLifetime);
        await RunOnStaThreadAsync(ShortViewportKeepsPopupInsideVisibleArea);
        foreach (var locale in UiLocales.All)
        foreach (var dark in new[] { false, true })
        foreach (var dpi in new[] { 1d, 1.25, 1.5 })
            await ObserveMatrixActionAsync($"{locale}/{(dark ? "dark" : "light")}/{dpi * 100:0}%",
                stage => GeometryAndFlags(locale, dark, dpi, stage));
    }

    private static async Task ObserveMatrixActionAsync(string name, Action<Action<string>> action,
        Func<Action, Task>? dispatch = null, Func<Task, Task>? observe = null, Action<string>? write = null, TimeProvider? observationTimeProvider = null)
    {
        var elapsed = Stopwatch.StartNew();
        var phases = new ConcurrentQueue<string>();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? actionFailure = null;
        long enteredAt = -1;
        void Stage(string phase) => phases.Enqueue($"{elapsed.ElapsedMilliseconds}ms thread={Environment.CurrentManagedThreadId}: {phase}");
        void Dump() { foreach (var phase in phases) (write ?? Console.WriteLine)($"[search-adjustment/{name}] {phase}"); }
        Stage("queued with original 5s action deadline");
        try
        {
            await (dispatch ?? (callback => RunOnStaThreadAsync(callback)))(() =>
            {
                Interlocked.Exchange(ref enteredAt, elapsed.ElapsedMilliseconds);
                Stage("entered");
                try { action(Stage); }
                catch (Exception error) { Volatile.Write(ref actionFailure, error); Stage($"action failed ({error.GetType().Name})"); throw; }
                finally { Stage("action completed"); completion.TrySetResult(); }
            });
            (write ?? Console.WriteLine)($"[search-adjustment/{name}] completed in {elapsed.ElapsedMilliseconds}ms; entry={Volatile.Read(ref enteredAt)}ms; phases={phases.Count}; 320/480 DIP.");
        }
        catch (TimeoutException error) when (!ReferenceEquals(error, Volatile.Read(ref actionFailure)))
        {
            Stage(Volatile.Read(ref enteredAt) < 0 ? "outer deadline before entry; observing same action off STA for at most 20s" : "outer deadline after entry; observing same action off STA for at most 20s");
            try
            {
                // Bound observation off the shared STA; never replace/cancel/retry its work.
                await BoundMatrixObservation(Task.Run(() => (observe ?? (pending => pending))(completion.Task)), observationTimeProvider).ConfigureAwait(false);
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

    private static Task BoundMatrixObservation(Task observation, TimeProvider? timeProvider = null) =>
        observation.WaitAsync(TimeSpan.FromSeconds(20), timeProvider ?? TimeProvider.System);

    internal static async Task MatrixDiagnosticsPreserveFailuresWithoutRetryAsync()
    {
        var outer = new TimeoutException("Synthetic outer deadline");
        Action? queued = null;
        var runs = 0;
        var observations = 0;
        Exception? caught = null;
        var output = new List<string>();
        try
        {
            await ObserveMatrixActionAsync("queued-fixture", stage => { runs++; stage("action body"); },
                dispatch: callback => { queued = callback; throw outer; },
                observe: async completion => { observations++; queued!(); await completion; }, write: output.Add);
        }
        catch (Exception error) { caught = error; }
        AssertEqual(true, ReferenceEquals(outer, caught), "Observing a queued action must preserve the original outer deadline exception");
        AssertEqual(1, observations, "An outer deadline must observe only its same queued action once");
        AssertEqual(1, runs, "Observation must execute that queued action once, without a replacement or retry");
        AssertEqual(true, output.Any(line => line.Contains("before entry", StringComparison.Ordinal)), "A pre-entry deadline must report its queue boundary");
        AssertEqual(true, output.Any(line => line.Contains("action body", StringComparison.Ordinal)), "Failure output must retain the recorded phases");

        var inner = new TimeoutException("Synthetic action timeout");
        runs = 0; observations = 0; caught = null; output.Clear();
        try
        {
            await ObserveMatrixActionAsync("inner-fixture", _ => { runs++; throw inner; },
                dispatch: callback => { callback(); return Task.CompletedTask; },
                observe: _ => { observations++; return Task.CompletedTask; }, write: output.Add);
        }
        catch (Exception error) { caught = error; }
        AssertEqual(true, ReferenceEquals(inner, caught), "An action-owned timeout must remain the original failure");
        AssertEqual(0, observations, "An action-owned timeout must not be observed as an outer deadline");
        AssertEqual(1, runs, "An action-owned timeout must not run the action again");

        // Inspect the real helper's Task directly so an early expiry cannot hide
        // behind scheduling of the outer async catch/continuation.
        var exactClock = new ManualStreamingTimeProvider();
        var incomplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exactBound = BoundMatrixObservation(incomplete.Task, exactClock);
        exactClock.Advance(TimeSpan.FromMilliseconds(19999));
        AssertEqual(false, exactBound.IsCompleted, "The real observation bound stays incomplete at 19999ms");
        exactClock.Advance(TimeSpan.FromMilliseconds(1));
        AssertEqual(true, exactBound.IsCompleted, "The real observation bound expires at exactly 20000ms");
        try { await exactBound; throw new InvalidOperationException("The observation did not expire"); }
        catch (TimeoutException) { }
        AssertEqual(false, incomplete.Task.IsCompleted, "The bound never cancels or replaces its incomplete observation");
        incomplete.SetResult();

        caught = null; observations = 0; runs = 0; output.Clear();
        var dispatches = 0;
        var clock = new ManualStreamingTimeProvider();
        var observing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? sameCompletion = null;
        var bounded = ObserveMatrixActionAsync("observation-fixture", _ => runs++,
            dispatch: callback => { dispatches++; queued = callback; throw outer; },
            observe: completion => { sameCompletion = completion; observations++; observing.TrySetResult(); return completion; },
            write: output.Add, observationTimeProvider: clock);
        await observing.Task.WaitAsync(TimeSpan.FromSeconds(2));
        clock.Advance(TimeSpan.FromMilliseconds(19999));
        AssertEqual(false, bounded.IsCompleted, "The real diagnostic observation remains pending at 19999ms");
        AssertEqual(false, sameCompletion!.IsCompleted, "The same queued action is still incomplete before expiration");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        try { await bounded.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (Exception error) { caught = error; }
        AssertEqual(true, ReferenceEquals(outer, caught), "The real 20000ms observation deadline must preserve original outer-error identity");
        AssertEqual(1, observations, "An incomplete action gets exactly one bounded observation");
        AssertEqual(1, dispatches, "Expiration never dispatches a replacement or retry");
        AssertEqual(0, runs, "Expiration does not execute or replace the queued action");
        AssertEqual(false, sameCompletion.IsCompleted, "Expiration does not cancel the original action/completion");
        AssertEqual(true, output.Any(line => line.Contains("observation ended (TimeoutException)", StringComparison.Ordinal)), "The real bound records observation expiration");
        queued!();
        await sameCompletion.WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(1, runs, "The original action can still complete exactly once after diagnostic expiration");

        output.Clear();
        await ObserveMatrixActionAsync("success-fixture", stage => stage("buffered private phase"),
            dispatch: callback => { callback(); return Task.CompletedTask; }, write: output.Add);
        AssertEqual(1, output.Count, "A successful action emits one compact line");
        AssertEqual(false, output[0].Contains("buffered private phase", StringComparison.Ordinal), "Successful output keeps the detailed phase buffer private");
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
        AssertNotNull(control.FindName("LanguageTiles"), "Language options must be eleven equal toggle tiles");
        var tile = Tiles(control).Single(item => (string)item.Tag == "en");
        foreach (var key in new[] { Key.Enter, Key.Space })
        {
            tile.Focus();
            var previous = tile.IsChecked;
            LocalKey(tile, key); Drain();
            AssertEqual(previous != true, tile.IsChecked, "Enter and Space keep native local toggle behavior");
            AssertEqual(true, IsOpen(control), "Tile keyboard activation preserves the popup");
            AssertEqual(0, http.GetCallCount, "Tile keyboard activation cannot invoke Apply");
        }
        Part<RadioButton>(control, "PopularityRadio").Focus();
        LocalKey(Part<RadioButton>(control, "PopularityRadio"), Key.Enter); Drain();
        AssertEqual(true, IsOpen(control), "Native radio Enter stays local without Apply");
        var apply = Part<Button>(control, "ApplyButton");
        apply.Focus(); LocalKey(apply, Key.Enter); Drain();
        AssertEqual(false, IsOpen(control), "Explicit keyboard Apply closes search adjustments");
        AssertEqual(1, http.GetCallCount, "Keyboard Apply invokes exactly one page-one GET");
        await vm.SearchVoicesCommand.ExecuteAsync(null);
        AssertEqual(2, http.GetCallCount, "Explicit Search remains independent of popup keyboard Apply");
    }

    // This hidden HWND cannot acquire OS keyboard focus. Exercise native class
    // handlers with an explicit focus-state fixture; physical Tab is host acceptance.
    private static void LocalKey(FrameworkElement target, Key key)
    {
        var focusKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsKeyboardFocusedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var previous = target.IsKeyboardFocused;
        target.SetValue(focusKey, true);
        try
        {
            foreach (var routed in new[] { Keyboard.PreviewKeyDownEvent, Keyboard.KeyDownEvent, Keyboard.PreviewKeyUpEvent, Keyboard.KeyUpEvent })
            {
                var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, Environment.TickCount, key)
                { RoutedEvent = routed, Source = target };
                target.RaiseEvent(args);
            }
        }
        finally { target.SetValue(focusKey, previous); }
    }

    private static ToggleButton[] Tiles(FrameworkElement control) => Part<Grid>(control, "LanguageTiles").Children.OfType<ToggleButton>().ToArray();
    private static void Toggle(FrameworkElement control, string code)
    {
        var tile = Tiles(control).Single(item => (string)item.Tag == code);
        var peer = UIElementAutomationPeer.CreatePeerForElement(tile)!;
        ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)!).Toggle();
        Drain();
    }
    private static string[] Selected(FrameworkElement control) => Tiles(control).Where(tile => tile.IsChecked == true).Select(tile => (string)tile.Tag).ToArray();

    // Draft controls must have no network side effects. Apply must capture the
    // current TextBox once, including an unchanged refresh and keyboard activation.
    private static async Task DraftsApplyAndTileInputAsync()
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
        AssertEqual(11, Tiles(control).Length, "All and ten approved languages use equal tiles");
        AssertEnumerableEqual(new[] { "" }, Selected(control), "All alone is the default");
        Toggle(control, "ja"); Toggle(control, "en");
        AssertEnumerableEqual(new[] { "en", "ja" }, Selected(control), "Specific languages toggle independently and exclude All");
        Toggle(control, "en"); Toggle(control, "ja");
        AssertEnumerableEqual(new[] { "" }, Selected(control), "Clearing the final specific restores All");
        foreach (var code in new[] { "en", "zh", "de", "ja", "fr", "es", "ko", "ar", "ru", "pt" }) Toggle(control, code);
        AssertEqual(10, Selected(control).Length, "Selecting all ten specifics remains filtered");
        AssertEqual(false, Tiles(control)[0].IsChecked, "All ten does not select All");
        Toggle(control, "");
        AssertEnumerableEqual(new[] { "" }, Selected(control), "All clears every specific");
        Toggle(control, "ja");
        Part<RadioButton>(control, "CreationRadio").IsChecked = true;
        AssertEqual(0, http.GetCallCount, "Draft language/sort edits never call the provider");
        Click(control, "ResetButton");
        AssertEnumerableEqual(new[] { "" }, Selected(control), "Reset drafts All");
        AssertEqual(true, Part<RadioButton>(control, "RelevanceRadio").IsChecked, "Reset drafts relevance");
        AssertEqual(0, http.GetCallCount, "Reset never calls the provider");
        Click(control, "CloseButton");
        AssertEqual(false, IsOpen(control), "Close dismisses without applying");
        AssertEqual(0, http.GetCallCount, "Close discards drafts without a request");
        Open(control);
        AssertEnumerableEqual(new[] { "" }, Selected(control), "Reopening discards the former draft");
        Toggle(control, "ja"); Toggle(control, "en");
        Part<RadioButton>(control, "PopularityRadio").IsChecked = true;
        var input = (TextBox)view.FindName("VoiceSearchInput");
        input.Text = "visible & query";
        Drain();
        Click(control, "ApplyButton");
        Drain();
        AssertEqual(false, IsOpen(control), "Apply closes immediately");
        AssertEqual(1, http.GetCallCount, "Apply invokes exactly one request");
        AssertEqual("https://api.fish.audio/model?page_size=6&page_number=1&sort_by=task_count&title=visible%20%26%20query&language=en&language=ja", http.LastGetUrl, "Apply captures visible text and current drafts");
        Open(control);
        AssertEnumerableEqual(new[] { "en", "ja" }, Selected(control), "Reopening starts from successful selections");
        var surface = Part<Border>(control, "PopupSurface");
        var scroll = Part<ScrollViewer>(control, "PopupScroll");
        scroll.ScrollToVerticalOffset(50); Drain();
        AssertEqual(true, IsOpen(control), "Internal body scrolling preserves adjustment drafts");
        Toggle(control, "ru");
        var key = Escape(surface);
        AssertEqual(true, key.Handled, "Escape is consumed by the adjustment overlay");
        AssertEqual(false, IsOpen(control), "Escape closes adjustments directly");
        AssertEqual(true, Part<Button>(control, "AdjustmentButton").IsKeyboardFocused, "Escape returns focus to the embedded trigger");
        AssertEqual(1, http.GetCallCount, "Escape discards drafts without another GET");
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
            foreach (var name in new[] { "CloseButton", "ResetButton", "ApplyButton" })
            {
                var action = Part<Button>(control, name);
                var actionBounds = action.TransformToAncestor(surface).TransformBounds(new Rect(action.RenderSize));
                AssertEqual(true, actionBounds.Top >= 0 && actionBounds.Bottom <= surface.ActualHeight, "Header/footer actions stay reachable outside the scrolling body");
            }
            Tiles(control).Last().BringIntoView(); Drain();
            AssertEqual(true, IsOpen(control), "Bringing a clipped tile into view scrolls only the internal body");
            AssertEqual(true, contentScroll.VerticalOffset > 0, "Keyboard/focus BringIntoView can reach the final language row");
            SavePopupProof(surface, $"short-viewport-{(nearBottom ? "above" : "below")}-native{dpi.DpiScaleX * 100:0}");
            Click(control, "CloseButton");
        }
    }

    private static void SavePopupProof(FrameworkElement surface, string name)
    {
        var directory = Path.Combine(Path.GetDirectoryName(FindRepoFile("build.ps1"))!, ".scratch", "task-8", "popup-proof");
        Directory.CreateDirectory(directory);
        SaveBitmap(RenderGlyph(surface, surface), Path.Combine(directory, name + ".png"));
    }

    // Incorrect inset/padding/DPI conversion or missing/mismatched tile flags must
    // fail against the actual themed TextBox viewport and popup presentation source.
    private static void GeometryAndFlags(string locale, bool dark, double dpi, Action<string> stage)
    {
        stage("ViewModel construction begin");
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        stage("ViewModel ready; compiled view construction begin");
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        stage("compiled view ready; locale/theme setup begin");
        SetLocale(view, locale);
        view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light);
        // Headless STA has no Application/OS accent initialization. Supply a labeled
        // palette state, retaining the production DynamicResource and native metrics.
        var accentPalette = new SolidColorBrush(dark ? Color.FromRgb(0x4C, 0xC2, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8));
        view.Resources["AccentFillColorDefaultBrush"] = accentPalette;
        view.Resources["RadioButtonOuterEllipseCheckedFill"] = accentPalette;
        view.Resources["RadioButtonOuterEllipseCheckedStroke"] = accentPalette;
        stage("locale/theme ready (concrete accent fixture); source attach/layout begin");
        var source = Attach(view);
        stage("source attach/layout ready");
        FrameworkElement? diagnosticControl = null;
        try
        {
            var control = Control(view);
            diagnosticControl = control;
            var input = (TextBox)view.FindName("VoiceSearchInput");
            input.Text = new string('W', 160); input.CaretIndex = input.Text.Length;
            // Materialize lazy native text views before actual root DPI, preserving
            // the runner's established voice-card first-use ordering.
            stage("native text materialization begin");
            SettingsLayoutSuite.Layout(view, 480);
            _ = input.GetRectFromCharacterIndex(input.Text.Length);
            stage("native text ready; root DPI setup begin");
            VisualTreeHelper.SetRootDpi(view, new DpiScale(dpi, dpi));
            stage("root DPI setup ready");
            foreach (var width in new[] { 320d, 480d })
            {
                stage($"width={width}: layout begin");
                SettingsLayoutSuite.Layout(view, width); Drain();
                stage($"width={width}: layout/drain ready");
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
                stage($"width={width}: main popup begin");
                Open(control);
                stage($"width={width}: main popup open/drain ready");
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
                var tiles = Tiles(control);
                AssertEnumerableEqual(new[] { "", "en", "zh", "de", "ja", "fr", "es", "ko", "ar", "ru", "pt" }, tiles.Select(tile => (string)tile.Tag), "Preset tiles keep their approved order");
                for (var index = 0; index < tiles.Length; index++)
                {
                    var tile = tiles[index];
                    AssertEqual(32d, tile.ActualHeight, "Every tile has the same 32-DIP height");
                    AssertEqual(tiles[0].ActualWidth, tile.ActualWidth, "All and specific tiles have equal width");
                    AssertEqual(index == 0 ? 0 : ((index + 1) / 2) * 2, Grid.GetRow(tile), "All owns its first row and preset pairs follow below");
                    AssertEqual(index == 0 ? 0 : ((index - 1) % 2) * 2, Grid.GetColumn(tile), "All occupies only the left cell");
                    var name = AutomationProperties.GetName(tile);
                    AssertEqual(true, !string.IsNullOrWhiteSpace(name), "Every tile exposes its full localized name");
                    AssertEqual(name, tile.ToolTip as string, "Tooltip retains the full accessible name");
                    var label = Visuals(tile).OfType<TextBlock>().Single();
                    AssertEqual(TextTrimming.CharacterEllipsis, label.TextTrimming, "Constrained names trim without changing tile dimensions");
                    AssertEqual(true, label.ActualWidth <= tile.ActualWidth && label.ActualHeight <= tile.ActualHeight, "Native text stays within the tile");
                }
                var firstBounds = tiles[1].TransformToAncestor(Part<Grid>(control, "LanguageTiles")).TransformBounds(new Rect(tiles[1].RenderSize));
                var secondBounds = tiles[2].TransformToAncestor(Part<Grid>(control, "LanguageTiles")).TransformBounds(new Rect(tiles[2].RenderSize));
                AssertEqual(6d, secondBounds.Left - firstBounds.Right, "Columns have a six-DIP gap");
                AssertEqual(10, Visuals(Part<Grid>(control, "LanguageTiles")).OfType<LanguageFlagImage>().Count(item => item.Visibility == Visibility.Visible), "Ten specifics have embedded flags while All has none");
                foreach (var tile in tiles.Skip(1))
                {
                    var flag = Visuals(tile).OfType<LanguageFlagImage>().Single();
                    AssertEqual(true, ReferenceEquals(VoiceLanguagePresentation.Describe((string)tile.Tag, locale).Flag, flag.Source), "Each tile reuses the matching embedded atlas flag");
                    AssertEqual(true, tile.Focusable && KeyboardNavigation.GetIsTabStop(tile), "Every native toggle remains keyboard reachable");
                }
                if (dpi == 1.25 && (locale == "en" || (locale == "ru" && width == 320)))
                    SavePopupProof(surface, $"{locale}-{(dark ? "dark" : "light")}-{width:0}-all-accentfixture-owner125-popup{VisualTreeHelper.GetDpi(surface).DpiScaleX * 100:0}");
                Toggle(control, "en"); Toggle(control, "zh");
                foreach (var tile in tiles)
                {
                    var selected = tile.IsChecked == true;
                    var border = (Border)tile.Template.FindName("TileBorder", tile);
                    AssertEqual(true, ReferenceEquals(tile.BorderBrush, border.BorderBrush), "Selected border resolves through the template binding");
                    if (selected)
                        AssertEqual(true, ReferenceEquals(view.FindResource("AccentFillColorDefaultBrush"), border.BorderBrush), "Selected tiles resolve the current native accent resource");
                    var check = Visuals(tile).OfType<System.Windows.Shapes.Path>().Single();
                    AssertEqual(selected ? Visibility.Visible : Visibility.Hidden, check.Visibility, "Only selected tiles show the decorative check");
                    AssertEqual(false, check.Focusable, "The check adds no separate focus target");
                }
                if (dpi == 1.25 && (locale == "en" || (locale == "ru" && width == 320)))
                    SavePopupProof(surface, $"{locale}-{(dark ? "dark" : "light")}-{width:0}-multiple-accentfixture-owner125-popup{VisualTreeHelper.GetDpi(surface).DpiScaleX * 100:0}");
                foreach (var next in UiLocales.All)
                {
                    stage($"width={width}: locale={next} begin");
                    SetLocale(view, next); Drain();
                    stage($"width={width}: locale={next} replacement/drain ready");
                    AssertEnumerableEqual(new[] { "en", "zh" }, Selected(control), "Live locale replacement preserves the entire draft set");
                    AssertEqual(true, IsOpen(control), "Live locale replacement updates the same open popup");
                    foreach (var tile in tiles)
                    {
                        var code = (string)tile.Tag;
                        var expected = code.Length == 0 ? (string)view.FindResource("STranslate_Plugin_Tts_FishAudio_Search_AllLanguages") : VoiceLanguagePresentation.Describe(code, next).Name;
                        AssertEqual(expected, AutomationProperties.GetName(tile), "Each live locale supplies the full tile caption");
                        AssertEqual(expected, tile.ToolTip as string, "Locale changes refresh full tooltips");
                    }
                }
                SetLocale(view, locale); Drain();
                var relevance = Part<RadioButton>(control, "RelevanceRadio").Content as string;
                AssertEqual(locale == "zh-cn" ? "相关优先" : locale == "en" ? "Relevance first" : (string)view.FindResource("STranslate_Plugin_Tts_FishAudio_Search_Relevance"), relevance, "Score uses the approved localized relevance-first label");
                stage($"width={width}: popup cleanup begin");
                Click(control, "CloseButton");
                stage($"width={width}: popup cleanup ready open={IsOpen(control)} inputOwned={control.GetType().GetField("_ownsInput", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(control)}");
            }
        }
        finally
        {
            stage("HwndSource cleanup begin");
            source.Dispose();
            stage($"HwndSource cleanup ready inputOwned={diagnosticControl?.GetType().GetField("_ownsInput", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(diagnosticControl)}");
        }
    }


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
