using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static TestAssertions;

internal static class InlineNumericEditorSuite
{
    // Breaks if the real view loses inline editing, commits drafts prematurely,
    // re-snaps typed precision, or lets the native input change row geometry.
    internal static IReadOnlyList<(string Language, string Culture)> Cultures { get; } =
        [("en", "en-US"), ("zh-cn", "zh-CN"), ("zh-tw", "zh-TW"), ("ja", "ja-JP"), ("ko", "ko-KR"), ("en", "de-DE")];

    internal static void RunForCulture(Func<SettingsViewModel, SettingsView> createView, Action<FrameworkElement, double> layout, string language, string culture)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var settings = new Settings { SelectedModel = "s2-pro", ApiKey = AppliedKey, Speed = 1.25, Volume = -10, Temperature = 0.7, TopP = 0.7 };
            var context = CreateContext(settings: settings);
            var storage = (ContextProxy)(object)context;
            string? saved = null;
            storage.SaveSettings = value => saved = JsonSerializer.Serialize(value);
            using var vm = new SettingsViewModel(context, settings);
            var view = createView(vm);
            view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(FindRepoFile($"STranslate.Plugin.Tts.FishAudio/Languages/{language}.xaml"), UriKind.Absolute) });
            Drain();
            var editors = Descendants(view).OfType<FrameworkElement>().Where(e => e.GetType().Name == "InlineNumericEditor").ToList();
            AssertEqual(4, editors.Count, "All four actual settings sliders must have a reusable clickable numeric editor");
            foreach (var (property, draft, expected, minimum, maximum, step, format) in new[]
            {
                ("Speed", "0.83", 0.83, 0.5, 2d, 0.05, "F2"),
                ("Volume", "-9.3", -9.3, -10d, 10d, 0.1, "F1"),
                ("Temperature", "0.82", 0.82, 0d, 1d, 0.05, "F2"),
                ("TopP", "0.82", 0.82, 0.05, 1d, 0.05, "F2"),
            })
            {
                var editor = editors.Single(e => BindingOperations.GetBinding(e, ValueProperty(e))?.Path.Path == property);
                var display = Descendants(editor).OfType<Button>().Single();
                var input = Descendants(editor).OfType<TextBox>().Single();
                var slider = Descendants(view).OfType<Slider>().Single(s => BindingOperations.GetBinding(s, Slider.ValueProperty)?.Path.Path == property);
                AssertEqual(step, slider.TickFrequency, "Manual precision must preserve the existing drag tick interval");
                AssertEqual(true, slider.IsSnapToTickEnabled, "Slider dragging must continue to snap to ticks");
                foreach (var width in new[] { 400d, 600d })
                {
                    layout(view, width);
                    var before = display.RenderSize;
                    var rowBefore = editor.RenderSize;
                    var position = editor.TranslatePoint(new Point(), (UIElement)editor.Parent);
                    var text = display.Content?.ToString();
                    Click(display);
                    layout(view, width);
                    if (culture == "en-US" && ((property == "Speed" && width == 400) || (property == "Volume" && width == 600)))
                    {
                        // Model the installed theme's focused/nonempty
                        // setter without an OS focus source. Both style
                        // and template setters have lower priority than
                        // the editor's local clear-button override.
                        var nativeSetter = input.Template.Triggers.OfType<MultiTrigger>()
                            .SelectMany(trigger => trigger.Setters.OfType<Setter>())
                            .Single(setter => setter.Property == iNKORE.UI.WPF.Modern.Controls.Helpers.TextBoxHelper.IsDeleteButtonVisibleProperty);
                        var previousStyle = input.Style;
                        var focusedThemeState = new Style(typeof(TextBox), previousStyle);
                        focusedThemeState.Setters.Add(new Setter(nativeSetter.Property, nativeSetter.Value));
                        input.Style = focusedThemeState;
                        layout(view, width);
                        AssertNumericViewport(input, "1.23", "-10.0");
                        input.Style = previousStyle;
                        layout(view, width);
                    }
                    AssertEqual(text, input.Text, "Opening must copy the exact displayed numeric text without the unit");
                    AssertEqual(before, input.RenderSize, $"At {width}px the native editor must retain both display dimensions");
                    AssertEqual(rowBefore, editor.RenderSize, "Entering edit mode must keep the complete numeric/unit row size");
                    AssertEqual(position, editor.TranslatePoint(new Point(), (UIElement)editor.Parent), "Entering edit mode must not shift the value or slider");
                    AssertEqual(true, editor.TranslatePoint(new Point(), view).X + editor.ActualWidth <= width + 0.01, "The value and separate unit must remain inside the settings width");
                    var saves = storage.SaveCount;
                    input.Text = draft;
                    AssertEqual(saves, storage.SaveCount, "Typing a draft must not save an intermediate value");
                    Blur(input);
                    layout(view, width);
                    AssertEqual(expected, slider.Value, "Blur must synchronize the bound slider without snapping manually entered decimals");
                    AssertEqual(expected.ToString(format, CultureInfo.CurrentCulture), display.Content?.ToString(), "Committed display must use the configured precision and culture");
                    AssertEqual(Visibility.Visible, display.Visibility, "Blur must restore clickable numeric text");
                }
                // Parsing edge cases and interaction paths are culture-independent;
                // run the full matrix for dot/comma cultures while every
                // localized view above still commits all four values.
                if (culture is "en-US" or "de-DE")
                {
                    foreach (var invalid in new[] { "", "-", "NaN", "Infinity", "1e999", (minimum - 0.01).ToString(CultureInfo.InvariantCulture), (maximum + 0.01).ToString(CultureInfo.InvariantCulture) })
                    {
                        var saves = storage.SaveCount;
                        Click(display); input.Text = invalid; Blur(input); Drain();
                        AssertEqual(expected, slider.Value, "An invalid draft must restore the prior value rather than default or clamp it");
                        AssertEqual(saves, storage.SaveCount, "Rejecting a draft must not save unrelated settings");
                    }
                    foreach (var bound in new[] { minimum, maximum })
                    {
                        Click(display); input.Text = bound.ToString(CultureInfo.CurrentCulture); Blur(input); Drain();
                        AssertEqual(bound, slider.Value, "Both exact bounds must be accepted, including Temperature zero and negative Volume");
                    }
                    Click(display); input.Text = draft.Replace(".", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator); Blur(input); Drain();
                    AssertEqual(expected, slider.Value, "Both invariant dot and current culture decimal input must work");
                    var savedCount = storage.SaveCount;
                    Click(display); input.Text = minimum.ToString(CultureInfo.InvariantCulture); PressKey(input, Key.Escape); Blur(input); Drain();
                    AssertEqual(expected, slider.Value, "Escape followed by blur must discard the draft");
                    AssertEqual(savedCount, storage.SaveCount, "Cancel must never save");
                    Click(display); input.Text = minimum.ToString(CultureInfo.InvariantCulture); PressKey(input, Key.Enter); Blur(input); Drain();
                    AssertEqual(minimum, slider.Value, "Enter must commit without waiting for blur");
                    AssertEqual(savedCount + 1, storage.SaveCount, "Enter followed by blur must save exactly once");
                    Click(display); input.Text = draft; Blur(input); Drain();
                    // Test the actual parent preview route: a blank card area,
                    // another button, and the slider all commit before acting.
                    foreach (var outside in new UIElement[] { view, slider, editors.Where(e => !ReferenceEquals(e, editor)).SelectMany(Descendants).OfType<Button>().First() })
                    {
                        Click(display); input.Text = minimum.ToString(CultureInfo.InvariantCulture);
                        outside.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent });
                        Drain();
                        AssertEqual(minimum, slider.Value, "Clicking outside on blank space, a slider, or another value must commit the draft");
                        AssertEqual(Visibility.Collapsed, input.Visibility, "Outside click must close the prior editor");
                        Click(display); input.Text = draft; Blur(input); Drain();
                    }
                    Click(display); input.Text = minimum.ToString(CultureInfo.InvariantCulture);
                    var inline = Descendants(view).OfType<Run>().First();
                    inline.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent });
                    Drain();
                    AssertEqual(minimum, slider.Value, "Clicking an actual rich-text Run outside the editor must commit without an ancestry exception");
                    Click(display); input.Text = draft; Blur(input); Drain();
                    Click(display); input.Text = minimum.ToString(CultureInfo.InvariantCulture);
                    vm.GetType().GetProperty(property)!.SetValue(vm, maximum); Drain();
                    Blur(input); Drain();
                    AssertEqual(maximum, slider.Value, "A newer external value must cancel the stale draft before blur");
                    display.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new UnhostedInputSource(), 0, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
                    display.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new UnhostedInputSource(), 0, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
                    Drain();
                    AssertEqual(Visibility.Visible, input.Visibility, "The native Space-key button path must open numeric editing");
                    PressKey(input, Key.Escape);
                    AssertEqual(true, AutomationProperties.GetName(display).Length > 0, "Each numeric button must have a localized accessible name");
                    var invoke = (IInvokeProvider)new ButtonAutomationPeer(display).GetPattern(PatternInterface.Invoke)!;
                    invoke.Invoke(); Drain();
                    AssertEqual(Visibility.Visible, input.Visibility, "Assistive technology must be able to open the native numeric input");
                    input.Text = draft; Blur(input); Drain();
                }
                var unit = Descendants(editor).OfType<TextBlock>().Single(b => b.Name == "UnitText");
                AssertEqual(property == "Volume" ? "dB" : "", unit.Text, "Units must remain separate from editable numeric text");
                AssertEqual(false, input.Text.Contains("dB"), "Volume editor must never include its unit");
            }
            AssertEqual(AppliedKey, settings.ApiKey, "Editing numeric settings must preserve the API key");
            AssertNotNull(saved, "Valid inline commits must reach host storage");
            var loaded = JsonSerializer.Deserialize<Settings>(saved!)!;
            SettingsStore.Load(CreateContext(settings: loaded), new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero));
            using var reopened = new SettingsViewModel(CreateContext(settings: loaded), loaded);
            AssertEqual(0.83, reopened.Speed, "Manual speed must survive persisted settings reload");
            AssertEqual(-9.3, reopened.Volume, "Manual volume must survive persisted settings reload");
            AssertEqual(0.82, reopened.Temperature, "Manual temperature must survive persisted settings reload");
            AssertEqual(0.82, reopened.TopP, "Manual TopP must reopen as 0.82 rather than 0.80");
            var reopenedView = createView(reopened);
            Drain();
            AssertEqual(0.82, Descendants(reopenedView).OfType<Slider>().Single(s => BindingOperations.GetBinding(s, Slider.ValueProperty)?.Path.Path == "TopP").Value,
                "A newly constructed actual settings view must retain the persisted off-tick slider position");
            AssertEqual(false, SettingsNormalizer.Normalize(loaded, new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero)), "Display precision normalization must be idempotent");
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    private static DependencyProperty ValueProperty(FrameworkElement editor) => (DependencyProperty)editor.GetType().GetField("ValueProperty")!.GetValue(null)!;
    private static void AssertNumericViewport(TextBox input, params string[] values)
    {
        var contentHost = (ScrollViewer)input.Template.FindName("PART_ContentHost", input);
        var clearButton = VisualDescendants(input).OfType<Button>().Single(button => button.Name == "DeleteButton");

        foreach (var value in values)
        {
            var glyphs = new FormattedText(value, CultureInfo.CurrentCulture, input.FlowDirection,
                new Typeface(input.FontFamily, input.FontStyle, input.FontWeight, input.FontStretch), input.FontSize,
                input.Foreground, VisualTreeHelper.GetDpi(input).PixelsPerDip);
            AssertEqual(true, contentHost.ViewportWidth >= glyphs.WidthIncludingTrailingWhitespace,
                $"The native numeric viewport must fit every glyph of {value} without horizontal clipping");
        }
        AssertEqual(Visibility.Collapsed, clearButton.Visibility, "Inline numeric editing must not reserve space for the native clear button");
        AssertEqual(0d, clearButton.ActualWidth, "The collapsed native clear button must consume zero horizontal space");
    }
    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in VisualDescendants(child)) yield return descendant;
        }
    }
    private static void Click(Button button) { typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null); Drain(); }
    private static void Blur(TextBox input) => input.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, input, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
    private static void PressKey(TextBox input, Key key) => input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new UnhostedInputSource(), 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    private sealed class UnhostedInputSource : PresentationSource { public override Visual RootVisual { get; set; } = null!; public override bool IsDisposed => false; protected override CompositionTarget GetCompositionTargetCore() => null!; }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
}
