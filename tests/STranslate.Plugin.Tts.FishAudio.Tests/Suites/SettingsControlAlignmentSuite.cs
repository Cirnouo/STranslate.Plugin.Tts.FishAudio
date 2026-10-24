using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Common;
using iNKORE.UI.WPF.Modern.Controls.Helpers;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static StaTestHost;
using static TestAssertions;

internal static class SettingsControlAlignmentSuite
{
    internal static async Task RunAllAsync()
    {
        foreach (var language in UiLocales.All)
            await RunOnStaThreadAsync(() => BrowseTabPositionsStayFixed(language));
        await RunOnStaThreadAsync(SliderStartsAtNativeContentAnchor);
        await RunOnStaThreadAsync(EmptyBrowseRowsKeepModelPosition);
        foreach (var width in new[] { 400d, 600d })
            await RunOnStaThreadAsync(() => VisibleCardIconsShareCenter(width));
        await RunOnStaThreadAsync(InputViewportsKeepTheirWidth);
        await RunOnStaThreadAsync(VoiceIdCompositionAndPasteRejectInvalidText);
    }

    private static void BrowseTabPositionsStayFixed(string language)
    {
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = CreateView(vm);
        view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(FindRepoFile($"STranslate.Plugin.Tts.FishAudio/Languages/{language}.xaml"), UriKind.Absolute) });
        var commands = new[] { vm.SwitchToSearchCommand, vm.SwitchToByIdCommand, vm.SwitchToFavoritesCommand };
        var tabs = commands.Select(command => Logical(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, command))).ToArray();
        var selectedBrush = view.TryFindResource("AccentFillColorTertiaryBrush") as Brush;
        AssertNotNull(selectedBrush, "Selected tabs must use the native theme accent brush");
        foreach (var width in new[] { 400d, 600d })
        {
            vm.BrowseMode = VoiceBrowseMode.Search;
            Layout(view, width);
            var bounds = tabs.Select(tab => new Rect(tab.TranslatePoint(new Point(), view), tab.RenderSize)).ToArray();
            var labelWeights = tabs.Select(tab => ((StackPanel)tab.Content).Children.OfType<TextBlock>().Last().FontWeight).ToArray();
            foreach (var command in commands)
            {
                command.Execute(null);
                Layout(view, width);
                for (var index = 0; index < tabs.Length; index++)
                {
                    if (ReferenceEquals(command, commands[index]))
                    {
                        AssertEqual(true, ReferenceEquals(selectedBrush, tabs[index].Background), "The selected tab must show the native theme accent surface");
                        AssertEqual(true, Visuals(tabs[index]).OfType<Border>().Any(border => ReferenceEquals(border.Background, selectedBrush)),
                            "The resolved native Button template must paint the selected accent surface");
                        AssertEqual(view.FindResource("TextOnAccentFillColorPrimaryBrush"), ((StackPanel)tabs[index].Content).Children.OfType<TextBlock>().Last().Foreground,
                            "The selected label must use the native text brush paired with the accent fill");
                    }
                    else
                        AssertEqual((byte)0, ((SolidColorBrush)tabs[index].Background).Color.A, "Peer tabs must keep transparent native surfaces");
                    AssertEqual(labelWeights[index], ((StackPanel)tabs[index].Content).Children.OfType<TextBlock>().Last().FontWeight, "Tab selection must preserve native text metrics");
                    Near(bounds[index].X, tabs[index].TranslatePoint(new Point(), view).X, $"{language}/{width}px selecting any tab must preserve all tab left positions");
                    Near(bounds[index].Width, tabs[index].ActualWidth, $"{language}/{width}px active text must not change a tab's measured width");
                }
            }
        }
    }

    private static void SliderStartsAtNativeContentAnchor()
    {
        var settings = new Settings { SelectedModel = "s2-pro" };
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = CreateView(vm);
        var native = Logical(view).OfType<ComboBox>().Single(input => BindingOperations.GetBinding(input, ComboBox.SelectedItemProperty)?.Path.Path == "AudioFormat");
        foreach (var width in new[] { 400d, 600d })
        {
            Layout(view, width);
            // The native wrapped state is selected after Loaded in the host.
            // Enter its real storyboard without opening a desktop window.
            foreach (var card in Logical(view).OfType<SettingsCard>())
                AssertEqual(true, VisualStateManager.GoToState(card, "RightWrapped", false), "The native SettingsCard must expose its real responsive wrapped state");
            Layout(view, width);
            var anchor = native.TranslatePoint(new Point(), view).X;
            foreach (var slider in Logical(view).OfType<Slider>())
            {
                if (width == 400)
                    Near(anchor, slider.TranslatePoint(new Point(), view).X, $"At {width}px the slider must start in the native wrapped input content column");
                var presenter = (FrameworkElement)VisualTreeHelper.GetParent((FrameworkElement)slider.Parent);
                Near(presenter.TranslatePoint(new Point(), view).X, slider.TranslatePoint(new Point(), view).X, "The slider must use the left edge of the native content slot in both responsive states");
                AssertEqual(true, presenter.TranslatePoint(new Point(presenter.ActualWidth, 0), view).X <= width, "The numeric row must remain inside the native card content bounds");
                AssertEqual(180d, slider.ActualWidth, "Adding a numeric editor must preserve the existing slider length");
            }
        }
    }

    private static void EmptyBrowseRowsKeepModelPosition()
    {
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = CreateView(vm);
        var search = Logical(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.SearchVoicesCommand));
        var byId = Logical(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.SubmitVoiceIdCommand));
        Layout(view, 400);
        var model = Logical(view).OfType<TextBlock>().Single(block => block.Text == "Model");
        foreach (var width in new[] { 400d, 600d })
        {
            vm.BrowseMode = VoiceBrowseMode.Search;
            Layout(view, width);
            var searchTop = search.TranslatePoint(new Point(), view).Y;
            var searchHeight = search.ActualHeight;
            var nextHeader = model.TranslatePoint(new Point(), view).Y;
            vm.BrowseMode = VoiceBrowseMode.ById;
            Layout(view, width);
            Near(searchTop, byId.TranslatePoint(new Point(), view).Y, "Empty Search and By ID query rows must share their top edge");
            Near(searchHeight, byId.ActualHeight, "Empty Search and By ID query controls must share their height");
            Near(nextHeader, model.TranslatePoint(new Point(), view).Y, "Switching empty query tabs must preserve the following Model heading position");
        }
    }
    private static void VisibleCardIconsShareCenter(double width)
    {
        const string id = "0123456789abcdef0123456789abcdef";
        const string sample = "https://example.invalid/sample.mp3";
        var settings = new Settings { VoiceId = id, CachedVoice = new() { Title = "Selected", SampleAudioUrl = sample } };
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var items = new[] { "Search", "By ID", "Favorites" }.Select(title => new VoiceSearchItem { Id = id, Title = title, SampleAudioUrl = sample }).ToArray();
        vm.SearchResults = [items[0]];
        vm.ByIdResults = [items[1]];
        vm.FavoriteResults = [items[2]];
        vm.HasSearched = true;
        var view = CreateView(vm);
        foreach (var mode in new[] { VoiceBrowseMode.Search, VoiceBrowseMode.ById, VoiceBrowseMode.Favorites })
        {
            vm.BrowseMode = mode;
            Layout(view, width);
            var selectedHeart = Logical(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.ToggleSelectedVoiceFavoriteCommand));
            CheckIcons((StackPanel)selectedHeart.Parent, $"Selected/{width}px");
            VoiceSearchItem item;
            // Select by the bound collection rather than assuming enum order.
            item = mode == VoiceBrowseMode.Search ? items[0] : mode == VoiceBrowseMode.ById ? items[1] : items[2];
            var heart = Visuals(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.ToggleFavoriteCommand) && ReferenceEquals(button.CommandParameter, item));
            var row = (StackPanel)heart.Parent;
            CheckIcons(row, $"{mode}/{width}px");
            item.IsApplied = false;
            item.IsFavorite = true;
            Layout(view, width);
            CheckIcons(row, $"{mode}/{width}px/filled-heart-and-Tick");
            item.IsApplied = true;
            item.IsFavorite = false;
            foreach (var preparing in new[] { false, true })
            {
                vm.IsDisplayVoicePreviewing = !preparing;
                vm.IsDisplayVoicePreparing = preparing;
                item.IsBeingPreviewed = !preparing;
                item.IsPreparingPreview = preparing;
                Layout(view, width);
                CheckIcons((StackPanel)selectedHeart.Parent, $"Selected/{width}px/{(preparing ? "preparing" : "playing")}");
                CheckIcons(row, $"{mode}/{width}px/{(preparing ? "preparing" : "playing")}");
            }
            vm.IsDisplayVoicePreviewing = false;
            vm.IsDisplayVoicePreparing = false;
            item.IsBeingPreviewed = false;
            item.IsPreparingPreview = false;
        }
    }

    private static void CheckIcons(StackPanel row, string state)
    {
        var heart = (System.Windows.Shapes.Path)row.Children.OfType<Button>().First().Content;
        var card = AvatarPreviewSuite.Card(row);
        AvatarPreviewSuite.SetHover(card, true);
        try
        {
            var preview = AvatarPreviewSuite.Overlay(Visuals(card).OfType<Button>().Single(button =>
                BindingOperations.GetBinding(button, Button.CommandProperty)?.Path.Path.EndsWith("PreviewCommand") == true));
            row.UpdateLayout();
            var action = row.Children.OfType<Button>().Last();
            var center = row.ActualHeight / 2;
            NearInk(center, InkCenter(heart, row), $"{state}: the visible heart drawing must center on its title action row");
            var actionGlyph = Visuals(action).OfType<InkCenteredGlyph>().Single();
            NearInk(center, InkCenter(actionGlyph, row), $"{state}: the visible Tick/Cross glyph must center on its title action row");
            center = preview.TranslatePoint(new Point(18, 18), row).Y;
            var playGlyph = Visuals(preview).OfType<InkCenteredGlyph>().Single();
            if (IsShown(playGlyph))
                NearInk(center, InkCenter(playGlyph, row), $"{state}: the visible Play glyph must center on the preview ring");
            foreach (var stop in Visuals(preview).OfType<Border>().Where(border => IsShown(border) && border.Width is 9 or 10))
                Near(center, stop.TranslatePoint(new Point(stop.ActualWidth / 2, stop.ActualHeight / 2), row).Y, $"{state}: the visible Stop icon must center on the preview ring");
            foreach (var spinner in preview.Children.OfType<Grid>().Where(grid => IsShown(grid)))
                Near(center, spinner.TranslatePoint(new Point(spinner.ActualWidth / 2, spinner.ActualHeight / 2), row).Y, $"{state}: the spinner must retain the preview center");
        }
        finally { AvatarPreviewSuite.SetHover(card, false); }
    }

    private static double InkCenter(FrameworkElement icon, UIElement ancestor)
    {
        var drawing = VisualTreeHelper.GetDrawing(icon);
        AssertNotNull(drawing, "A rendered native icon must expose its actual drawing");
        var bounds = drawing!.Bounds;
        AssertEqual(false, bounds.IsEmpty, "The icon assertion must measure visible ink");
        return icon.TranslatePoint(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2), ancestor).Y;
    }

    private static void InputViewportsKeepTheirWidth()
    {
        var settings = new Settings { ApiKey = AppliedKey };
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings) { BrowseMode = VoiceBrowseMode.ById, VoiceIdInput = AppliedKey };
        var view = CreateView(vm);
        foreach (var width in new[] { 400d, 600d })
        {
            foreach (var mode in new[] { VoiceBrowseMode.ById, VoiceBrowseMode.Search, VoiceBrowseMode.Favorites })
            {
                vm.BrowseMode = mode;
                Layout(view, width);
                foreach (var input in Logical(view).OfType<Control>().Where(control => IsShown(control) && control is TextBox or PasswordBox))
                {
                    var host = (ScrollViewer)input.Template.FindName("PART_ContentHost", input);
                    var viewport = host.ViewportWidth;
                    // Reuse the installed theme's real nonempty/focused delete
                    // setter. Style priority models native state without OS focus.
                    var previousStyle = input.Style;
                    if (input is TextBox)
                    {
                        var setter = input.Template.Triggers.OfType<MultiTrigger>().SelectMany(trigger => trigger.Setters.OfType<Setter>()).Single(s => s.Property == TextBoxHelper.IsDeleteButtonVisibleProperty);
                        var focused = new Style(input.GetType(), previousStyle);
                        focused.Setters.Add(new Setter(setter.Property, setter.Value));
                        input.Style = focused;
                        Layout(view, width);
                        var clear = Visuals(input).OfType<Button>().Single(button => button.Name == "DeleteButton");
                        AssertEqual(Visibility.Collapsed, clear.Visibility, "Every plugin text input must hide the native clear control");
                        AssertEqual(0d, clear.ActualWidth, "Hidden native clear controls must reserve zero text width");
                    }
                    else
                    {
                        AssertEqual(PasswordRevealMode.Hidden, PasswordBoxHelper.GetPasswordRevealMode((PasswordBox)input), "The masked API key must not reserve a native reveal slot underneath Paste");
                        var reveal = Visuals(input).OfType<ToggleButton>().SingleOrDefault(button => button.Name == "RevealButton");
                        AssertEqual(Visibility.Collapsed, reveal?.Visibility ?? Visibility.Collapsed, "The native password reveal adornment must not cover or shrink the API key text");
                    }
                    AssertEqual(viewport, host.ViewportWidth, $"At {width}px focusing {input.GetType().Name} must keep the native text viewport width");
                    var command = input is PasswordBox ? vm.PasteApiKeyCommand : input is TextBox box && BindingOperations.GetBinding(box, TextBox.TextProperty)?.Path.Path == "VoiceIdInput" ? vm.PasteVoiceIdCommand : null;
                    if (command is not null)
                    {
                        var paste = Logical(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, command));
                        var presenter = Visuals(host).OfType<ScrollContentPresenter>().Single();
                        var viewportRight = presenter.TranslatePoint(new Point(presenter.ActualWidth, 0), input).X;
                        AssertEqual(true, viewportRight <= paste.TranslatePoint(new Point(), input).X, "The fixed Paste area must remain outside the native text viewport");
                    }
                    input.Style = previousStyle;
                    Layout(view, width);
                }
            }
        }
    }

    private static void VoiceIdCompositionAndPasteRejectInvalidText()
    {
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings) { BrowseMode = VoiceBrowseMode.ById };
        var view = CreateView(vm);
        Layout(view, 400);
        var input = Logical(view).OfType<TextBox>().Single(box => BindingOperations.GetBinding(box, TextBox.TextProperty)?.Path.Path == "VoiceIdInput");
        AssertEqual(true, SettingsValidation.IsValidVoiceIdPaste(AppliedKey), "The Paste button policy must accept the exact valid ID");
        AssertEqual(32, input.MaxLength, "Voice ID input must stop at 32 ASCII hex characters");
        foreach (var text in new[] { "G", "A", "-", " ", "汉", "１２", "abcZ", "a\nb" })
        {
            var composition = new TextComposition(InputManager.Current, input, text);
            var e = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
            input.RaiseEvent(e);
            AssertEqual(true, e.Handled, $"Invalid Voice ID composition {text} must be rejected before it appears");
        }
        foreach (var text in new[] { "0", "abcdef", "0123456789abcdef0123456789abcdef" })
        {
            input.Text = "";
            var e = new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, input, text)) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
            input.RaiseEvent(e);
            AssertEqual(false, e.Handled, "ASCII lowercase hex composition must remain editable");
        }
        input.Text = AppliedKey;
        input.Select(3, 0);
        var overflow = new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, input, "a")) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
        input.RaiseEvent(overflow);
        AssertEqual(true, overflow.Handled, "Composition must not overflow a full Voice ID");
        foreach (var text in new[] { "https://fish.audio/m/" + AppliedKey, AppliedKey + "0", " " + AppliedKey, AppliedKey + " ", "abcZ", "ab cd", "ＡＢＣ", "abc\ndef", "" })
        {
            AssertEqual(false, SettingsValidation.IsValidVoiceIdPaste(text), "The Paste button must share native paste whole-payload rejection");
            input.Text = "1234";
            input.SelectAll();
            var e = Paste(input, text);
            AssertEqual(true, e.CommandCancelled, "Native paste must reject the entire invalid payload");
            AssertEqual("1234", input.Text, "Rejected native paste must preserve the existing Voice ID");
            AssertEqual("1234", vm.VoiceIdInput, "Rejected native paste must preserve bound lookup input");
        }
        input.SelectAll();
        var valid = Paste(input, AppliedKey);
        AssertEqual(false, valid.CommandCancelled, "A valid clipboard ID must be accepted exactly");
        AssertEqual(AppliedKey, valid.DataObject.GetData(DataFormats.UnicodeText), "Native paste must insert the whole canonical ID without extracting or truncating");
        input.Text = AppliedKey;
        input.Select(0, 0);
        AssertEqual(true, Paste(input, "a").CommandCancelled, "Native paste must reject insertion beyond 32 characters");
        input.Select(0, 1);
        AssertEqual(false, Paste(input, "f").CommandCancelled, "Replacing a selected hex character must remain possible at the limit");
    }

    private static bool IsShown(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement ui && ui.Visibility != Visibility.Visible) return false;
        return true;
    }
    private static DataObjectPastingEventArgs Paste(TextBox input, string text)
    {
        var e = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, text), false, DataFormats.UnicodeText) { RoutedEvent = DataObject.PastingEvent };
        input.RaiseEvent(e);
        return e;
    }
    // A quarter DIP allows subpixel font bearings while detecting real template/padding offsets.
    private static void NearInk(double expected, double actual, string message) => AssertEqual(true, Math.Abs(expected - actual) <= 0.25, $"{message} (expected {expected:F3}, actual {actual:F3})");
    private static void Near(double expected, double actual, string message) => AssertEqual(true, Math.Abs(expected - actual) <= 0.1, $"{message} (expected {expected:F3}, actual {actual:F3})");
    private static SettingsView CreateView(SettingsViewModel vm)
    {
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        Layout(view, 600);
        return view;
    }
    private static void Layout(FrameworkElement view, double width) => SettingsLayoutSuite.Layout(view, width);
    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) { yield return child; foreach (var item in Logical(child)) yield return item; }
    }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) { var child = VisualTreeHelper.GetChild(root, index); yield return child; foreach (var item in Visuals(child)) yield return item; }
    }
}
