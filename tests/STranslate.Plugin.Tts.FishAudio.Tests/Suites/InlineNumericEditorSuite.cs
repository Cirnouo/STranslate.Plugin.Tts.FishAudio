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
using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Controls;
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
        [("en", "en-US"), ("zh-cn", "zh-CN"), ("zh-tw", "zh-TW"), ("ja", "ja-JP"), ("ko", "ko-KR"), ("ru", "ru-RU"), ("tr", "tr-TR"), ("en", "de-DE")];

    // Catches a centered resting presenter, focus-only border growth, clipped
    // signed values/carets, or a permanently reserved empty unit column. This
    // is a compiled native-theme state fixture, not installed-host focus proof.
    internal static void CompactGeometryForCulture(string language, string culture, bool dark, double dpi)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var settings = new Settings { SelectedModel = "s2-pro", Speed = 1.25, Volume = -10 };
            var pluginContext = CreateContext(settings: settings);
            var storage = (ContextProxy)(object)pluginContext;
            using var vm = new SettingsViewModel(pluginContext, settings);
            var view = SettingsLayoutSuite.CreateThemedView(vm);
            view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(FindRepoFile($"STranslate.Plugin.Tts.FishAudio/Languages/{language}.xaml"), UriKind.Absolute) });
            view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
            ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light);
            // Materialize the lazy native text views before setting root DPI.
            // Setting DPI before opening an input leaves its later-created
            // TextBoxView at the desktop default in this unhosted fixture.
            SettingsLayoutSuite.Layout(view);
            foreach (var editor in Descendants(view).OfType<InlineNumericEditor>())
            {
                Click(Descendants(editor).OfType<Button>().Single());
                SettingsLayoutSuite.Layout(view);
                PressKey(Descendants(editor).OfType<TextBox>().Single(), Key.Escape);
            }
            SettingsLayoutSuite.Layout(view);
            typeof(VisualTreeHelper).GetMethod("SetRootDpi", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [view, new DpiScale(dpi, dpi)]);
            foreach (var width in new[] { 400d, 600d })
            foreach (var editor in Descendants(view).OfType<InlineNumericEditor>())
            {
                SettingsLayoutSuite.Layout(view, width);
                var property = BindingOperations.GetBinding(editor, InlineNumericEditor.ValueProperty)!.Path.Path;
                var context = $"{language}/{culture}/{(dark ? "dark" : "light")}/{dpi * 100}%/{width}/{property}";
                var display = Descendants(editor).OfType<Button>().Single();
                var input = Descendants(editor).OfType<TextBox>().Single();
                var unit = Descendants(editor).OfType<TextBlock>().Single(block => block.Name == "UnitText");
                var grid = (Grid)unit.Parent;
                var surface = (Border)display.Template.FindName("Surface", display);
                var label = VisualDescendants(display).OfType<TextBlock>().Single();
                // Native keyboard focus can scroll the outer view into sight.
                // Compare layout within the owning card row independently.
                var row = (FrameworkElement)editor.Parent;
                var rowBounds = Bounds(editor, row);
                var displayBounds = Bounds(display, row);
                var labelBounds = Bounds(label, row);
                var slider = Descendants(view).OfType<Slider>().Single(slider => BindingOperations.GetBinding(slider, Slider.ValueProperty)?.Path.Path == property);
                var sliderBounds = Bounds(slider, row);
                NumericTypographySuite.AssertResting(editor, label, unit, context);
                NumericTypographySuite.AssertUnitRoutes(view, editor, unit, () => storage.SaveCount, context);
                Near(dpi, VisualTreeHelper.GetDpi(input).DpiScaleX, $"{context}: the compiled input must inherit the requested actual root DPI");
                Near(180, slider.ActualWidth, $"{context}: compact labels must preserve the native slider length");
                Near(displayBounds.Right - 3, labelBounds.Right, $"{context}: rendered resting text must reach the field's padded right edge");
                Near(52, display.ActualWidth, $"{context}: numeric hit targets must fit the compact field");
                Near(32, display.ActualHeight, $"{context}: numeric hit targets must retain their height");
                AssertEqual(new Thickness(1), surface.BorderThickness, $"{context}: resting border space must be reserved before focus");
                AssertEqual(0, ((SolidColorBrush)surface.BorderBrush).Color.A, $"{context}: resting outline must remain transparent");
                SetKeyboardFocused(display, true);
                SettingsLayoutSuite.Layout(view, width);
                AssertEqual(view.FindResource("TextFillColorPrimaryBrush"), surface.BorderBrush, $"{context}: focus must expose a contrasting theme outline");
                AssertEqual(new Thickness(1), surface.BorderThickness, $"{context}: focus must change the brush without growing the border");
                AssertEqual(labelBounds, Bounds(label, row), $"{context}: focus must not shift the rendered number");
                AssertEqual(displayBounds, Bounds(display, row), $"{context}: focus must not move the numeric field");
                SetKeyboardFocused(display, false);
                Click(display);
                SettingsLayoutSuite.Layout(view, width);
                AssertEqual(displayBounds, Bounds(input, row), $"{context}: editing must occupy the exact resting bounds");
                AssertEqual(rowBounds, Bounds(editor, row), $"{context}: editing must preserve the entire value/unit row");
                AssertEqual(sliderBounds, Bounds(slider, row), $"{context}: editing must not move the neighboring native slider");
                AssertEqual(new Thickness(1), input.BorderThickness, $"{context}: the native input border must be fixed at one DIP");
                AssertEqual(new Thickness(2, 0, 2, 0), input.Padding, $"{context}: native text must retain the compact inner padding");
                AssertEqual(TextAlignment.Center, input.TextAlignment, $"{context}: drafts must center within the unchanged native viewport");
                NumericTypographySuite.AssertEditing(editor, input, unit, context);
                var nativeSetter = input.Template.Triggers.OfType<MultiTrigger>().SelectMany(trigger => trigger.Setters.OfType<Setter>())
                    .Single(setter => setter.Property == iNKORE.UI.WPF.Modern.Controls.Helpers.TextBoxHelper.IsDeleteButtonVisibleProperty);
                var focusedThemeState = new Style(typeof(TextBox), input.Style);
                focusedThemeState.Setters.Add(new Setter(nativeSetter.Property, nativeSetter.Value));
                input.Style = focusedThemeState;
                foreach (var value in property == "Volume" ? new[] { "-10.00", "-10,00" } : new[] { "0.50", "1.00" })
                {
                    input.Text = value;
                    SettingsLayoutSuite.Layout(view, width);
                    AssertNumericViewport(input, value);
                    AssertCaretAndCenter(input, view, context);
                    if (unit.Visibility == Visibility.Visible)
                    {
                        var textView = VisualDescendants(input).OfType<Visual>().Single(element => element.GetType().Name == "TextBoxView");
                        NumericBottomSuite.AssertAligned(textView, unit, editor, context + "/live-draft-" + value);
                    }
                }
                var expectedUnit = property switch { "Speed" => "×", "Volume" => "dB", _ => "" };
                AssertEqual(expectedUnit, unit.Text, $"{context}: units must be separate from the editable numeric draft");
                AssertEqual(false, input.Text.Contains('×') || input.Text.Contains("dB"), $"{context}: native editing must contain only the number");
                if (expectedUnit.Length == 0)
                {
                    AssertEqual(Visibility.Collapsed, unit.Visibility, $"{context}: settings without units must not reserve a unit element");
                    Near(0, grid.ColumnDefinitions[1].ActualWidth, $"{context}: the empty auto unit column must collapse completely");
                    Near(52, editor.ActualWidth, $"{context}: no-unit rows must end at the numeric field");
                }
                else
                {
                    AssertEqual(Visibility.Visible, unit.Visibility, $"{context}: the unit must remain visible during editing");
                    Near(displayBounds.Right, Bounds(unit, row).Left, $"{context}: the unit must start outside the numeric field without an added margin");
                    Near(unit.DesiredSize.Width, grid.ColumnDefinitions[1].ActualWidth, $"{context}: the unit column must size to its real content");
                    Near(52 + unit.DesiredSize.Width, editor.ActualWidth, $"{context}: units must not retain the old fixed-width reservation");
                }
                AssertEqual(true, rowBounds.Right <= row.ActualWidth, $"{context}: compact controls must stay inside the settings width");
                PressKey(input, Key.Escape);
                SettingsLayoutSuite.Layout(view, width);
                AssertEqual(rowBounds, Bounds(editor, row), $"{context}: cancelling must restore the same row bounds");
            }
            NumericTypographySuite.AssertThemeTransition(view, Descendants(view).OfType<InlineNumericEditor>().ToArray(), dark);
            Console.WriteLine($"[inline-numeric] {language}/{culture}/{(dark ? "dark" : "light")}/{dpi * 100}%: compact geometry, native viewport and caret checks passed at 400/600 DIP.");
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    private static void AssertCaretAndCenter(TextBox input, FrameworkElement root, string context)
    {
        var viewport = VisualDescendants(input).OfType<FrameworkElement>().Single(element => element.GetType().Name == "TextBoxView");
        // The native ScrollContentPresenter includes negative padding margins;
        // its child TextBoxView is the actual 46-DIP text viewport.
        var viewportBounds = Bounds(viewport, root);
        var inputBounds = Bounds(input, root);
        Near(VisualTreeHelper.GetDpi(input).DpiScaleX, VisualTreeHelper.GetDpi(viewport).DpiScaleX, $"{context}: the actual text viewport must use the input DPI");
        Near(46, viewport.ActualWidth, $"{context}: the real native viewport must preserve signed-number and caret space");
        Near(inputBounds.Right - 3, viewportBounds.Right, $"{context}: the native viewport must reach the padded right edge");
        var start = input.GetRectFromCharacterIndex(0);
        var end = input.GetRectFromCharacterIndex(input.Text.Length, true);
        AssertEqual(false, start.IsEmpty || end.IsEmpty, $"{context}: both end caret positions must resolve after native layout");
        var startX = input.TranslatePoint(start.TopLeft, root).X;
        var endX = input.TranslatePoint(end.TopLeft, root).X;
        Near(viewportBounds.Left + viewportBounds.Width / 2, (startX + endX) / 2,
            $"{context}: the complete draft between its actual caret positions must center in the text viewport");
        AssertEqual(true, startX >= viewportBounds.Left && endX <= viewportBounds.Right + .01,
            $"{context}: the full signed draft and both caret positions must fit without clipping either end");
        input.Select(0, 0);
        SettingsLayoutSuite.Layout(root, root.ActualWidth);
        Near(0, input.HorizontalOffset, $"{context}: viewing the leading caret must not scroll the signed draft");
        input.Select(input.Text.Length, 0);
        SettingsLayoutSuite.Layout(root, root.ActualWidth);
        Near(0, input.HorizontalOffset, $"{context}: viewing the trailing caret must not scroll away any leading glyph");
    }

    private static Rect Bounds(FrameworkElement element, Visual root) => new(element.TranslatePoint(new Point(), (UIElement)root), element.RenderSize);
    private static void Near(double expected, double actual, string message) => AssertEqual(true, Math.Abs(expected - actual) < .01, $"{message}; expected {expected:F3}, actual {actual:F3}");
    private static void SetKeyboardFocused(Button button, bool value) => button.SetValue((DependencyPropertyKey)typeof(UIElement)
        .GetField("IsKeyboardFocusedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!, value);

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
                ("Speed", "0.83", 0.83, 0.5, 2d, 0.01, "F2"),
                ("Volume", "-9.37", -9.37, -10d, 10d, 0.01, "F2"),
                ("Temperature", "0.82", 0.82, 0d, 1d, 0.01, "F2"),
                ("TopP", "0.82", 0.82, 0.05, 1d, 0.01, "F2"),
            })
            {
                var editor = editors.Single(e => BindingOperations.GetBinding(e, ValueProperty(e))?.Path.Path == property);
                var display = Descendants(editor).OfType<Button>().Single();
                var input = Descendants(editor).OfType<TextBox>().Single();
                var slider = Descendants(view).OfType<Slider>().Single(s => BindingOperations.GetBinding(s, Slider.ValueProperty)?.Path.Path == property);
                AssertEqual(step, slider.TickFrequency, "Dragging and direct entry must both allow hundredths");
                AssertEqual(true, slider.IsSnapToTickEnabled, "Slider dragging must continue to snap to ticks");
                foreach (var width in new[] { 400d, 600d })
                {
                    layout(view, width);
                    var before = display.RenderSize;
                    var rowBefore = editor.RenderSize;
                    var position = editor.TranslatePoint(new Point(), (UIElement)editor.Parent);
                    var text = display.Content?.ToString();
                    Click(display);
                    if (width == 400)
                    {
                        var savesBeforeGrammar = storage.SaveCount;
                        AssertInputGrammar(input, minimum, 2);
                        AssertEqual(savesBeforeGrammar, storage.SaveCount, "Partial typing, rejection, and paste must remain isolated drafts until commit");
                    }
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
                        AssertNumericViewport(input, "1.23", "-10.00");
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
                if (culture is "en-US" or "de-DE" or "ru-RU" or "tr-TR")
                {
                    foreach (var invalid in new[] { "", "-", "NaN", "Infinity", "1e999", "1e0", "+0.8", " 0.8", "0.8 ", "0.800", minimum < 0 ? "000.8" : "00.8", (minimum - 0.01).ToString(CultureInfo.InvariantCulture), (maximum + 0.01).ToString(CultureInfo.InvariantCulture) })
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
                AssertEqual(property switch { "Speed" => "×", "Volume" => "dB", _ => "" }, unit.Text, "Units must remain separate from editable numeric text");
                AssertEqual(false, input.Text.Contains("dB"), "Volume editor must never include its unit");
            }
            AssertEqual(AppliedKey, settings.ApiKey, "Editing numeric settings must preserve the API key");
            AssertNotNull(saved, "Valid inline commits must reach host storage");
            var loaded = JsonSerializer.Deserialize<Settings>(saved!)!;
            SettingsStore.Load(CreateContext(settings: loaded), new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero));
            using var reopened = new SettingsViewModel(CreateContext(settings: loaded), loaded);
            AssertEqual(0.83, reopened.Speed, "Manual speed must survive persisted settings reload");
            AssertEqual(-9.37, reopened.Volume, "Manual hundredth-decibel volume must survive persisted settings reload");
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
    private static void AssertInputGrammar(TextBox input, double minimum, int decimalPlaces)
    {
        var original = input.Text;
        var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        AssertEqual(minimum < 0 ? 6 : 4, input.MaxLength, "Numeric input capacity must match the complete signed fixed-precision format");
        try
        {
            input.Text = "0";
            input.Select(1, 0);
            Compose(input, ".", accepted: true);
            Compose(input, "8", accepted: true);
            if (decimalPlaces == 2) Compose(input, "3", accepted: true);
            var fullDraft = input.Text;
            foreach (var invalid in new[] { "9", ".", ",", "e", "E", "+", "-", " ", "\n", "１", "中", "NaN" })
            {
                input.Select(input.Text.Length, 0);
                Compose(input, invalid, accepted: false);
                AssertEqual(fullDraft, input.Text, "Rejected composition must never appear in the draft");
            }
            input.Select(input.Text.Length - 1, 1);
            Compose(input, "2", accepted: true);
            AssertEqual(decimalPlaces == 2 ? "0.82" : "0.2", input.Text, "Replacing selected fractional digits must remain possible at maximum capacity");
            input.Select(input.Text.Length, 0);
            EditingCommands.Backspace.Execute(null, input);
            AssertEqual(decimalPlaces == 2 ? "0.8" : "0.", input.Text, "Native Backspace must preserve editable partial decimal drafts");

            input.Text = "";
            input.Select(0, 0);
            Compose(input, "-", accepted: minimum < 0);
            AssertEqual(minimum < 0 ? "-" : "", input.Text, "Only the negative-range setting may begin with a minus sign");
            input.Text = minimum < 0 ? "10" : "1";
            input.Select(input.Text.Length, 0);
            Compose(input, "0", accepted: false);
            input.Select(0, input.Text.Length);
            Compose(input, "0", accepted: true);
            Compose(input, separator, accepted: true);
            Compose(input, "8", accepted: true);
            AssertEqual($"0{separator}8", input.Text, "The current culture decimal separator must support normal composition");

            foreach (var invalid in new[] { "1e0", "NaN", "Infinity", " 0.8", "0.8 ", "0..8", "0,8.0", "100", "0.123", "１", "-0.8" })
            {
                if (invalid == "-0.8" && minimum < 0) continue;
                input.Text = "0.8";
                input.SelectAll();
                Paste(input, invalid, accepted: false);
                AssertEqual("0.8", input.Text, "Illegal whole paste must preserve the selected draft rather than sanitize or truncate it");
            }
            input.Text = "0.8";
            input.SelectAll();
            Paste(input, minimum < 0 ? "-10.00" : "0.82", accepted: true);
            AssertEqual(minimum < 0 ? "-10.00" : "0.82", input.Text, "A legal full-capacity paste must replace the selection intact");
            input.SelectAll();
            Paste(input, $"0{separator}8", accepted: true);
            if (separator != ",") { input.SelectAll(); Paste(input, "0,8", accepted: false); }
        }
        finally { input.Text = original; input.SelectAll(); }
    }

    private static void Compose(TextBox input, string text, bool accepted)
    {
        var composition = new TextComposition(InputManager.Current, input, text);
        var args = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
        input.RaiseEvent(args);
        AssertEqual(!accepted, args.Handled, $"Numeric composition '{text}' must be {(accepted ? "accepted" : "rejected")}");
        if (!args.Handled) InsertAcceptedText(input, text);
    }

    private static void InsertAcceptedText(TextBox input, string text)
    {
        var caret = input.SelectionStart + text.Length;
        input.SelectedText = text;
        input.Select(caret, 0);
    }

    private static void Paste(TextBox input, string text, bool accepted)
    {
        var args = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, text), false, DataFormats.UnicodeText) { RoutedEvent = DataObject.PastingEvent };
        input.RaiseEvent(args);
        AssertEqual(!accepted, args.CommandCancelled, $"Whole numeric paste '{text}' must be {(accepted ? "accepted" : "rejected")}");
        if (!args.CommandCancelled) InsertAcceptedText(input, text);
    }
    private static void AssertNumericViewport(TextBox input, params string[] values)
    {
        var contentHost = (ScrollViewer)input.Template.FindName("PART_ContentHost", input);
        var clearButton = VisualDescendants(input).OfType<Button>().Single(button => button.Name == "DeleteButton");

        foreach (var value in values)
        {
            var glyphs = new FormattedText(value, CultureInfo.CurrentCulture, input.FlowDirection,
                new Typeface(input.FontFamily, input.FontStyle, input.FontWeight, input.FontStretch), input.FontSize,
                input.Foreground, VisualTreeHelper.GetDpi(input).PixelsPerDip);
            AssertEqual(true, contentHost.ViewportWidth >= glyphs.WidthIncludingTrailingWhitespace + 1 / VisualTreeHelper.GetDpi(input).DpiScaleX,
                $"The native numeric viewport must fit every glyph of {value} with at least one physical pixel of caret room");
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
