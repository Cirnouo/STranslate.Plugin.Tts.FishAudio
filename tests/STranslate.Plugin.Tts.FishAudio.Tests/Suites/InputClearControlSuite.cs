using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Common;
using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Controls.Helpers;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static TestAssertions;

internal static class InputClearControlSuite
{
    // 1 construction + 28 locale/theme/width contexts + 3 field behavior +
    // 2 explicitly forced password compatibility states = 34 ordinary 5s actions.
    internal static async Task RunAsync()
    {
        await Action("construction", stage => { _ = new SettingsView(); stage("compiled construction complete"); });
        foreach (var locale in UiLocales.All)
        foreach (var dark in new[] { false, true })
        foreach (var narrow in new[] { false, true })
            await Action($"{locale}/{dark}/{narrow}", stage => Geometry(locale, dark, narrow, stage));
        foreach (var field in new[] { "ApiKey", "SearchQuery", "VoiceIdInput" })
            await Action($"activation/{field}", stage => Activation(field, stage));
        foreach (var mode in new[] { PasswordRevealMode.Visible, PasswordRevealMode.Peek })
            await Action($"forced-password-compatibility/{mode}", stage => PasswordCompatibility(mode, stage));
    }

    private static Task Action(string name, Action<Action<string>> body) =>
        SearchAdjustmentControlSuite.ObserveMatrixActionAsync(name, body, prefix: "input-clear", details: "ordinary 5s action; owned native source");

    private static void Geometry(string locale, bool dark, bool narrow, Action<string> stage)
    {
        using var fixture = new Fixture(locale, dark);
        stage("compiled view/source ready; production Hidden password");
        fixture.Vm.ApiKey = new string('W', 1000);
        fixture.Layout(narrow ? 400 : 600);
        AssertEqual(PasswordRevealMode.Hidden, PasswordBoxHelper.GetPasswordRevealMode(fixture.Password), "Production remains obscured without a reveal target");
        Check(fixture.Password, fixture.Button("ApiKeyClearButton"), fixture.Button("ApiKeyPasteButton"), fixture.View, stage);
        var passwordTemplate = fixture.Password.Template;
        fixture.Password.Template = null; fixture.Password.ApplyTemplate(); fixture.Password.Template = passwordTemplate;
        fixture.Layout(narrow ? 400 : 600);
        Check(fixture.Password, fixture.Button("ApiKeyClearButton"), fixture.Button("ApiKeyPasteButton"), fixture.View, stage);
        foreach (var (mode, name, clearName, tailName) in new[]
        {
            (VoiceBrowseMode.Search, "VoiceSearchInput", "SearchQueryClearButton", "SearchAdjustments"),
            (VoiceBrowseMode.ById, "VoiceIdInputBox", "VoiceIdClearButton", "VoiceIdPasteButton"),
        })
        {
            fixture.Vm.BrowseMode = mode;
            var input = (TextBox)fixture.View.FindName(name);
            input.Text = mode == VoiceBrowseMode.Search ? new string('W', 180) : new string('f', 32);
            fixture.Layout(narrow ? 320 : 480);
            var clear = fixture.Button(clearName);
            var tail = (FrameworkElement)fixture.View.FindName(tailName);
            Check(input, clear, tail, fixture.View, stage);
            foreach (var atEnd in new[] { false, true })
            {
                input.Focus(); input.CaretIndex = atEnd ? input.Text.Length : 0;
                input.ScrollToHorizontalOffset(atEnd ? double.MaxValue : 0); Drain();
                var caret = input.GetRectFromCharacterIndex(input.CaretIndex);
                var textView = Visuals(input).OfType<FrameworkElement>().Single(element => element.GetType().Name == "TextBoxView");
                AssertEqual(VisualTreeHelper.GetDpi(fixture.View).DpiScaleX, VisualTreeHelper.GetDpi(textView).DpiScaleX, "Materialized native text view retains actual root DPI");
                AssertEqual(true, !caret.IsEmpty && caret.Right <= Left(clear, input) + .1, "The actual native terminal/start caret stays before clear at both scroll ends");
            }
            var template = input.Template;
            input.Template = null; input.ApplyTemplate(); input.Template = template;
            fixture.Layout(narrow ? 320 : 480);
            Check(input, clear, tail, fixture.View, stage);
            var expected = fixture.View.FindResource("STranslate_Plugin_Tts_FishAudio_ClearVoice").ToString();
            AssertEqual(expected, AutomationProperties.GetName(clear), "Clear accessibility text follows each live locale");
            AssertEqual(expected, clear.ToolTip?.ToString(), "Clear tooltip follows each live locale");
        }
        stage("all three real viewports/caret/actions/template and locale passed");
    }

    private static void Activation(string field, Action<string> stage)
    {
        using var fixture = new Fixture("en", false);
        fixture.Vm.BrowseMode = field == "VoiceIdInput" ? VoiceBrowseMode.ById : VoiceBrowseMode.Search;
        var input = field == "ApiKey" ? (Control)fixture.Password : (TextBox)fixture.View.FindName(field == "SearchQuery" ? "VoiceSearchInput" : "VoiceIdInputBox");
        var button = fixture.Button(field == "ApiKey" ? "ApiKeyClearButton" : field == "SearchQuery" ? "SearchQueryClearButton" : "VoiceIdClearButton");
        var property = typeof(SettingsViewModel).GetProperty(field)!;
        foreach (var draft in new[] { "x", "   ", field == "VoiceIdInput" ? new string('f', 32) : new string('W', 150) })
        {
            property.SetValue(fixture.Vm, draft);
            fixture.Layout(480);
            AssertEqual(Visibility.Visible, button.Visibility, "Any nonempty draft, including spaces, shows clear");
            AssertEqual(true, button.Focusable && button.IsTabStop, "Native clear retains keyboard reachability");
            var cursorSaves = fixture.Storage.SaveCount;
            AssertEqual(Cursors.Hand, Query(button), "Enabled clear participates in native action cursor routing");
            AssertEqual(false, Query(input) == Cursors.Hand, "Adjacent editable input retains its native cursor");
            AssertEqual(cursorSaves, fixture.Storage.SaveCount, "Cursor queries cannot save or activate clear");
            if (field == "ApiKey")
            {
                fixture.Vm.IsApiKeyInputLocked = true; Drain();
                AssertEqual(false, button.IsEnabled, "API clear mirrors the credential lock");
                AssertEqual(false, Query(button) == Cursors.Hand, "Locked clear retains native disabled cursor behavior");
                fixture.Vm.IsApiKeyInputLocked = false; Drain();
            }
            var lostFocus = 0;
            RoutedEventHandler listener = (_, _) => lostFocus++;
            var keyboardActivation = draft == "   ";
            if (keyboardActivation) button.Focus(); else input.Focus();
            input.LostFocus += listener;
            var saves = fixture.Storage.SaveCount;
            if (keyboardActivation)
            {
                AssertEqual(true, button.IsKeyboardFocused, "The owned-source keyboard fixture focuses the native clear button");
                NativeKeyboardStateFixture.Run((HwndSource)PresentationSource.FromVisual(button), () =>
                {
                    button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(button), Environment.TickCount, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
                    button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(button), Environment.TickCount, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
                });
            }
            else
            {
                var peer = new ButtonAutomationPeer(button);
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
            }
            Drain();
            input.LostFocus -= listener;
            AssertEqual("", property.GetValue(fixture.Vm), "Actual native Button invocation clears its bound source");
            AssertEqual("", input is PasswordBox password ? password.Password : ((TextBox)input).Text, "Actual native Button invocation clears its control before blur");
            AssertEqual(0, lostFocus, "Clear does not use a blur to commit the field");
            AssertEqual(Visibility.Collapsed, button.Visibility, "An empty field hides clear");
            AssertEqual(true, input.IsKeyboardFocusWithin, "Focus returns to the surviving enabled field");
            AssertEqual(saves + (field == "ApiKey" ? 1 : 0), fixture.Storage.SaveCount, "Only API clear saves once");
            AssertEqual(0, fixture.Http.GetCallCount, "Clear activation never invokes query or Apply");
            fixture.Layout(480);
        }
        stage("actual invoke/source/control/visibility/lock/focus/save assertions passed");
    }

    private static void PasswordCompatibility(PasswordRevealMode mode, Action<string> stage)
    {
        using var fixture = new Fixture("en", mode == PasswordRevealMode.Peek);
        fixture.Vm.ApiKey = new string('W', 1000);
        PasswordBoxHelper.SetPasswordRevealMode(fixture.Password, mode);
        foreach (var width in new[] { 400d, 600d })
        {
            fixture.Layout(width);
            Check(fixture.Password, fixture.Button("ApiKeyClearButton"), fixture.Button("ApiKeyPasteButton"), fixture.View, stage);
            foreach (var editor in Visuals(fixture.Password).OfType<TextBox>().Where(editor => editor.IsVisible))
            {
                editor.SelectionBrush = Brushes.DodgerBlue;
                editor.CaretBrush = Brushes.Black;
                editor.CaretIndex = editor.Text.Length; editor.ScrollToHorizontalOffset(double.MaxValue); Drain();
                var visibleHost = (FrameworkElement)editor.Template.FindName("PART_ContentHost", editor);
                var visibleViewport = Visuals(visibleHost).OfType<ScrollContentPresenter>().Single();
                AssertEqual(true, Right(visibleViewport, fixture.Password) <= Left(fixture.Button("ApiKeyClearButton"), fixture.Password), "The actually visible revealed editor viewport also ends before clear");
            }
            var template = fixture.Password.Template;
            fixture.Password.Template = null; fixture.Password.ApplyTemplate(); fixture.Password.Template = template;
            fixture.Layout(width);
            Check(fixture.Password, fixture.Button("ApiKeyClearButton"), fixture.Button("ApiKeyPasteButton"), fixture.View, stage);
        }
        stage($"forced {mode} native theme state only; production remains Hidden");
    }

    private static void Check(Control input, Button clear, FrameworkElement tail, SettingsView view, Action<string> stage)
    {
        AssertEqual(28d, clear.ActualWidth, "Clear keeps the native embedded 28-DIP target width");
        AssertEqual(28d, clear.ActualHeight, "Clear keeps the native embedded 28-DIP target height");
        AssertEqual(true, Left(clear, input) >= 0 && Right(clear, input) <= Left(tail, input) + .1 && Right(tail, input) <= input.ActualWidth + .1, "Clear precedes Paste/Adjust and both stay inside the input");
        AssertEqual(true, Math.Abs(input.ActualWidth - Right(tail, input) - 4) < .1, "Paste/Adjust retains its native four-DIP inset");
        AssertEqual(false, (bool)input.GetValue(TextBoxHelper.IsDeleteButtonVisibleProperty), "Native duplicate clear remains disabled");
        var host = (FrameworkElement)input.Template.FindName("PART_ContentHost", input);
        var viewport = Visuals(host).OfType<ScrollContentPresenter>().Single();
        AssertEqual(true, Right(viewport, input) <= Left(clear, input) + .1, "The actual native viewport ends before clear");
        var activeViewports = Visuals(input).OfType<ScrollContentPresenter>().Where(item => item.IsVisible && item.ActualWidth > 0).ToArray();
        AssertEqual(true, activeViewports.Length > 0, "The check measures an actually visible native editor viewport");
        foreach (var active in activeViewports)
            AssertEqual(true, Right(active, input) <= Left(clear, input) + .1, "Every actually visible native editor viewport ends before clear");
        foreach (var nativeDelete in Visuals(input).OfType<ButtonBase>().Where(button => button.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase)))
            AssertEqual(false, nativeDelete.IsVisible, "Native template replacement/focus cannot introduce a duplicate delete action");
        AssertEqual(VisualTreeHelper.GetDpi(view).DpiScaleX, VisualTreeHelper.GetDpi(input).DpiScaleX, "Input uses the actual visual-root DPI");
        if (input is TextBox) AssertEqual(36d, input.ActualHeight, "Search and By-ID keep their 36-DIP rows");
        stage("native target and viewport checked");
    }

    private sealed class Fixture : IDisposable
    {
        internal SettingsViewModel Vm { get; }
        internal SettingsView View { get; }
        internal PasswordBox Password => (PasswordBox)View.FindName("ApiKeyInput");
        internal ContextProxy Storage { get; }
        internal TestHttpServiceProxy Http { get; }
        private readonly HwndSource _source;
        internal Fixture(string locale, bool dark)
        {
            var settings = new Settings();
            var (service, http) = TestHttpServiceProxy.Create(); Http = http;
            var context = CreateContext(settings: settings, httpService: service); Storage = (ContextProxy)(object)context;
            Vm = new SettingsViewModel(context, settings);
            View = SettingsLayoutSuite.CreateThemedView(Vm);
            // Explicit headless accent state, as in the native cursor fixture;
            // no Application/OS accent initialization is available on this STA.
            foreach (var suffix in new[] { "", "Dark1", "Dark2", "Dark3", "Light1", "Light2", "Light3" })
                View.Resources["SystemAccentColor" + suffix] = Color.FromRgb(0x00, 0x78, 0xD4);
            View.Resources["TextControlBorderBrushFocused"] = Brushes.DodgerBlue;
            View.Resources["TextControlSelectionHighlightColor"] = Colors.DodgerBlue;
            View.Resources["AccentFillColorDefaultBrush"] = Brushes.DodgerBlue;
            View.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(FindRepoFile($"STranslate.Plugin.Tts.FishAudio/Languages/{locale}.xaml"), UriKind.Absolute) });
            View.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
            ThemeManager.SetRequestedTheme(View, dark ? ElementTheme.Dark : ElementTheme.Light);
            foreach (var editor in new[] { (TextBox)View.FindName("VoiceSearchInput"), (TextBox)View.FindName("VoiceIdInputBox") })
            {
                editor.SelectionBrush = Brushes.DodgerBlue;
                editor.CaretBrush = dark ? Brushes.White : Brushes.Black;
            }
            Password.SelectionBrush = Brushes.DodgerBlue;
            Password.CaretBrush = dark ? Brushes.White : Brushes.Black;
            _source = new HwndSource(new HwndSourceParameters("Fish Audio internal clear fixture") { Width = 600, Height = 1200, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = View };
            Layout(480);
        }
        internal Button Button(string name) => (Button)(View.FindName(name) ?? throw new InvalidOperationException($"Missing internal clear control: {name}"));
        internal void Layout(double width) { SettingsLayoutSuite.Layout(View, width); Drain(); View.UpdateLayout(); }
        public void Dispose() { _source.RootVisual = null; _source.Dispose(); Vm.Dispose(); }
    }

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    private static Cursor Query(UIElement element)
    {
        var args = new QueryCursorEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.QueryCursorEvent, Cursor = Cursors.Arrow };
        element.RaiseEvent(args);
        return args.Cursor;
    }
    private static double Left(FrameworkElement element, UIElement target) => element.TranslatePoint(new Point(), target).X;
    private static double Right(FrameworkElement element, UIElement target) => element.TranslatePoint(new Point(element.ActualWidth, 0), target).X;
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var item in Visuals(child)) yield return item; }
    }
}
