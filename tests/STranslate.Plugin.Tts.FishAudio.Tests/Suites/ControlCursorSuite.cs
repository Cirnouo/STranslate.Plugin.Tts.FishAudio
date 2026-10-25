using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Shapes;
using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Controls;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static TestAssertions;
using static VoiceDescriptionSuite;

internal static class ControlCursorSuite
{
    internal static Task RunLateComboAsync() => SearchAdjustmentControlSuite.ObserveMatrixActionAsync(
        "pcm-to-mp3", stage => Inspect(320, false, CursorWorkflow.Format, stage), prefix: "cursor", details: "actual late-realized Bitrate combo.");

    internal static Task RunLifecycleAsync() => SearchAdjustmentControlSuite.ObserveMatrixActionAsync("lifecycle", stage =>
    {
        using var vm = new SettingsViewModel(ContextProxy.CreateContext(), new Settings());
        var view = SettingsLayoutSuite.CreateThemedView(vm); SettingsLayoutSuite.Layout(view);
        InspectNativePeers(view, stage, lifecycle: true);
    }, prefix: "cursor", details: "500 DIP native peers.");

    internal static async Task RunAsync()
    {
        await SearchAdjustmentControlSuite.ObserveMatrixActionAsync("construction", stage =>
        {
            using var vm = new SettingsViewModel(ContextProxy.CreateContext(), new Settings());
            SettingsLayoutSuite.Layout(SettingsLayoutSuite.CreateThemedView(vm));
            stage("compiled themed construction and layout completed");
        }, prefix: "cursor", details: "ordinary 5s construction action.");
        foreach (var dark in new[] { false, true })
        foreach (var width in new[] { 320d, 640 })
        foreach (var workflow in new[] { CursorWorkflow.Format, CursorWorkflow.Search, CursorWorkflow.ById, CursorWorkflow.Favorites, CursorWorkflow.General, CursorWorkflow.Overlays })
            await SearchAdjustmentControlSuite.ObserveMatrixActionAsync($"plugin/{workflow}/{width}/{dark}",
                stage => Inspect(width, dark, workflow, stage), prefix: "cursor", details: "independently owned plugin native interaction workflow.");
        foreach (var lifecycle in new[] { false, true })
        foreach (var dark in new[] { false, true })
            await SearchAdjustmentControlSuite.ObserveMatrixActionAsync($"native-{(lifecycle ? "lifecycle" : "parts-template")}/{dark}", stage =>
            {
                using var vm = new SettingsViewModel(ContextProxy.CreateContext(), new Settings());
                var view = SettingsLayoutSuite.CreateThemedView(vm);
                view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
                ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light);
                SettingsLayoutSuite.Layout(view);
                InspectNativePeers(view, stage, lifecycle);
            }, prefix: "cursor", details: "500 DIP native peers.");
    }

    private enum CursorWorkflow { Format, Search, ById, Favorites, General, Overlays }

    private static void Inspect(double width, bool dark, CursorWorkflow workflow, Action<string> stage)
    {
        stage("VM/settings setup begin");
        const string id = "0123456789abcdef0123456789abcdef";
        var settings = new Settings { VoiceId = id, CachedVoice = new()
        { Title = "Selected", AuthorName = "Author", Description = "Passive description", Languages = ["en", "ja", "ko", "fr", "de", "es", "pt", "ru", "tr", "zh"], SampleAudioUrl = "https://example.invalid/sample.mp3" } };
        // Only Format owns the fresh PCM transition; other workflows start in its completed state.
        if (workflow != CursorWorkflow.Format) settings.AudioFormat = "mp3";
        var context = ContextProxy.CreateContext(settings: settings);
        using var vm = new SettingsViewModel(context, settings);
        var item = new VoiceSearchItem { Id = id, Title = "Result", AuthorName = "Author", Description = "Passive description", Languages = settings.CachedVoice.Languages, SampleAudioUrl = settings.CachedVoice.SampleAudioUrl };
        vm.SearchResults = [item]; vm.ByIdResults = [item]; vm.FavoriteResults = [item]; vm.HasSearched = true;
        if (workflow is CursorWorkflow.General or CursorWorkflow.Overlays) vm.BrowseMode = VoiceBrowseMode.Favorites;
        stage("VM/card setup completed; view/theme construction begin");
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        AddOfflineAccent(view);
        view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light);
        stage("view/theme ready; native source construction begin");
        using var source = new HwndSource(new HwndSourceParameters("Offline routed cursor fixture")
        { Width = 900, Height = 2000, WindowStyle = unchecked((int)0x80000000) });
        source.RootVisual = view;
        SettingsLayoutSuite.Layout(view, width);
        stage("plugin source construction/layout completed");
        var saved = ((ContextProxy)(object)context).SaveCount;
        if (workflow == CursorWorkflow.Format)
        {
            var initialCombos = Visuals(view).OfType<ComboBox>().ToArray();
            var initialBitrate = initialCombos.SingleOrDefault(combo => BindingOperations.GetBinding(combo, Selector.SelectedItemProperty)?.Path.Path == nameof(vm.Mp3Bitrate));
            Console.WriteLine($"[cursor/initial/{width}/{dark}] PCM visibleCombos={initialCombos.Count(combo => combo.IsVisible)} visualCombos={initialCombos.Length}; bitrateInVisualTree={initialBitrate is not null}; bitrateLoaded={initialBitrate?.IsLoaded}; bitrateVisible={initialBitrate?.IsVisible}; scopeEnabled={ActionCursor.GetIsScope(view)}");
            AssertEqual(3, initialCombos.Count(control => control.IsVisible), "PCM initially hides the fourth Bitrate combo");
            vm.AudioFormat = "mp3"; SettingsLayoutSuite.Layout(view, width);
            saved++;
            var visibleCombos = Visuals(view).OfType<ComboBox>().Where(control => control.IsVisible).ToArray();
            AssertEqual(4, visibleCombos.Length, "Switching to MP3 realizes all four actual native combo owners");
            foreach (var combo in visibleCombos)
                InspectCombo(combo, view, width);
            AssertEqual(saved, ((ContextProxy)(object)context).SaveCount, "The fresh PCM-to-MP3 transition saves once and its native cursor queries do not save");
            stage("fresh PCM-to-MP3 native dropdowns completed before any scope comparison/reset");
        }
        else if (workflow is CursorWorkflow.Search or CursorWorkflow.ById or CursorWorkflow.Favorites)
        {
            var mode = workflow switch
            {
                CursorWorkflow.Search => VoiceBrowseMode.Search,
                CursorWorkflow.ById => VoiceBrowseMode.ById,
                _ => VoiceBrowseMode.Favorites,
            };
            vm.BrowseMode = mode; SettingsLayoutSuite.Layout(view, width);
            InspectAvatars(view, vm, item, width);
            foreach (var button in Visuals(view).OfType<ButtonBase>().Where(button => button.IsVisible && button.IsEnabled && button is not SettingsCard && button is not HyperlinkButton).ToArray())
            {
                if (Visuals(button).Any(child => child is Ellipse)) continue;
                AssertHand(button, $"Enabled compiled {button.GetType().Name} {button.Name} in {mode}");
                button.SetCurrentValue(UIElement.IsEnabledProperty, false);
                AssertNative(button, "Disabled action retains native cursor");
                button.SetCurrentValue(UIElement.IsEnabledProperty, true);
                AssertHand(button, "Re-enabled action updates without rebuilding the view");
            }
            foreach (var metadata in Visuals(view).OfType<TextBlock>().Where(block => block.IsVisible && block.Text is "Author" or "Passive description"))
                AssertNative(metadata, "Plain card metadata stays passive");
            foreach (var disabled in Visuals(view).OfType<ButtonBase>().Where(button => button.IsVisible && !button.IsEnabled))
                AssertNative(disabled, "Initially command-disabled actions retain their native cursor");
            stage($"{mode} buttons/avatars/metadata completed");
        }
        else if (workflow == CursorWorkflow.General)
        {
            foreach (var (command, setBusy) in new (ICommand, Action<bool>)[]
            {
                (vm.RefreshCreditCommand, busy => vm.IsLoadingCredit = busy),
                (vm.ClearCoverImageCacheCommand, busy => vm.IsClearingCoverImageCache = busy),
                (vm.PasteApiKeyCommand, busy => vm.IsApiKeyInputLocked = busy),
            })
            {
                var button = Visuals(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, command));
                setBusy(true); SettingsLayoutSuite.Layout(view, width);
                AssertEqual(false, button.IsEnabled, "The real busy/key-lock command gate disables its action");
                AssertNative(button, "Busy or locked actions retain native cursor");
                setBusy(false); SettingsLayoutSuite.Layout(view, width); AssertHand(button, "Recovered command action returns Hand");
            }
            foreach (var toggle in Visuals(view).OfType<ToggleSwitch>().Where(control => control.IsVisible))
            {
                AssertHand(toggle, "Native ToggleSwitch template is actionable");
                toggle.SetCurrentValue(UIElement.IsEnabledProperty, false); AssertNative(toggle, "Disabled native toggle remains native"); toggle.SetCurrentValue(UIElement.IsEnabledProperty, true);
            }
            foreach (var slider in Visuals(view).OfType<Slider>().Where(control => control.IsVisible))
            {
                var before = slider.Value;
                var track = (Track)slider.Template.FindName("PART_Track", slider);
                AssertHand(track.Thumb, "Native slider thumb is actionable");
                AssertHand(track.DecreaseRepeatButton, "Native decrease track is actionable");
                AssertHand(track.IncreaseRepeatButton, "Native increase track is actionable");
                AssertEqual(before, slider.Value, "Querying the cursor cannot change a slider value");
            }
            foreach (var card in Visuals(view).OfType<SettingsCard>().Where(control => control.IsVisible))
            {
                AssertEqual(false, card.IsClickEnabled, "Product settings cards remain passive native cards");
                AssertEqual(false, Query(card) == Cursors.Hand, "Passive native SettingsCard does not become Hand");
                var header = Visuals(card).OfType<TextBlock>().FirstOrDefault(text => text.Text == card.Header?.ToString());
                if (header is not null) AssertNative(header, "Passive native settings header does not become Hand");
            }
            stage("busy gates/native toggle/slider/passive cards completed");
            foreach (var editor in Visuals(view).OfType<TextBox>().Where(control => control.IsVisible))
                AssertInputUnchanged(view, editor);
            foreach (var password in Visuals(view).OfType<PasswordBox>().Where(control => control.IsVisible))
            {
                var actual = Query(Hit(password)); ActionCursor.SetIsScope(view, false);
                var native = Query(Hit(password)); ActionCursor.SetIsScope(view, true);
                AssertEqual(native, actual, "Password input retains its native cursor");
            }
            foreach (var action in Visuals(view).OfType<FrameworkElement>().Where(element => element.IsVisible && ActionCursor.GetIsAction(element)))
                AssertHand(action, "Explicit custom promo action is actionable");
            foreach (var link in Visuals(view).OfType<HyperlinkButton>().Where(control => control.IsVisible))
            {
                var actual = Query(Hit(link)); ActionCursor.SetIsScope(view, false);
                var native = Query(Hit(link)); ActionCursor.SetIsScope(view, true);
                AssertEqual(native, actual, "Native hyperlink button cursor remains untouched");
            }
            var hyperlink = (Hyperlink)view.FindName("ProjectRepositoryLink");
            var linkActual = Query(hyperlink); ActionCursor.SetIsScope(view, false);
            var linkNative = Query(hyperlink); ActionCursor.SetIsScope(view, true);
            AssertEqual(linkNative, linkActual, "Native hyperlink text cursor remains untouched");
            var numeric = Visuals(view).OfType<InlineNumericEditor>().First(control => control.IsVisible);
            SetOfflineInputBrushes((TextBox)numeric.FindName("EditorBox"));
            var display = (Button)numeric.FindName("DisplayButton");
            AssertHand(display, "Numeric resting display is actionable");
            display.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); SettingsLayoutSuite.Layout(view, width);
            AssertEqual(true, numeric.IsEditing, "The native numeric input is realized in editing state");
            AssertInputUnchanged(view, (TextBox)numeric.FindName("EditorBox"));
            AssertNative((TextBlock)numeric.FindName("UnitText"), "Separate numeric units remain passive");
            numeric.CommitEdit();
            stage("inputs/links/numeric editing completed");
        }
        else
        {
            InspectPopups(view, vm, width);
            stage("owned adjustment/language popups completed");
        }
        AssertEqual(saved, ((ContextProxy)(object)context).SaveCount, "Cursor queries do not save settings");
        stage("plugin assertions completed; source cleanup next");
    }

    private static void InspectAvatars(SettingsView view, SettingsViewModel vm, VoiceSearchItem item, double width)
    {
        var selected = Visuals(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.ToggleDisplayPreviewCommand));
        var shared = Visuals(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.ToggleSearchItemPreviewCommand) && button.IsVisible);
        foreach (var button in new[] { selected, shared })
        {
            AssertHand(button, "A sample-enabled avatar retains its actual clickable hit target even before hover artwork appears");
            AvatarPreviewSuite.SetHover(AvatarPreviewSuite.Card(button), true); SettingsLayoutSuite.Layout(view, width);
            AssertHand(button, "Hovering the avatar shows the action and Hand");
            AvatarPreviewSuite.SetHover(AvatarPreviewSuite.Card(button), false);
            AssertEqual(false, button.Focusable, "Cursor handling preserves avatar nonfocusability");
            button.SetCurrentValue(UIElement.IsHitTestVisibleProperty, false); AssertNative(button, "An avatar with input gating disabled has no action cursor"); button.ClearValue(UIElement.IsHitTestVisibleProperty);
        }
        var sample = item.SampleAudioUrl; var selectedSample = vm.CachedVoiceSampleUrl;
        item.SampleAudioUrl = null; vm.CachedVoiceSampleUrl = null; SettingsLayoutSuite.Layout(view, width);
        AssertNative(shared, "Missing sample idle avatar remains unavailable"); AssertNative(selected, "Selected missing sample idle avatar remains unavailable");
        foreach (var preparing in new[] { true, false })
        {
            item.IsPreparingPreview = preparing; item.IsBeingPreviewed = !preparing;
            vm.IsDisplayVoicePreparing = preparing; vm.IsDisplayVoicePreviewing = !preparing;
            SettingsLayoutSuite.Layout(view, width);
            AssertHand(shared, "An active missing-sample avatar retains Cancel/Stop");
            AssertHand(selected, "Selected active missing-sample avatar retains Cancel/Stop");
        }
        item.IsBeingPreviewed = false; item.IsPreparingPreview = false; vm.IsDisplayVoicePreparing = false; vm.IsDisplayVoicePreviewing = false;
        SettingsLayoutSuite.Layout(view, width);
        AssertNative(shared, "Completion after pointer leave retires unavailable avatar action");
        item.SampleAudioUrl = sample; vm.CachedVoiceSampleUrl = selectedSample; SettingsLayoutSuite.Layout(view, width);
    }

    private static void InspectCombo(ComboBox combo, SettingsView view, double width)
    {
        AssertHand(combo, "Native combo body/arrow is actionable");
        var style = combo.Style; var template = combo.Template;
        combo.IsDropDownOpen = true; SettingsLayoutSuite.Layout(view, width);
        var container = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(0);
        AssertNotNull(container, "Open native combo generates actual dropdown items");
        AssertHand(container, "Generated native dropdown item text is actionable");
        combo.IsDropDownOpen = false; SettingsLayoutSuite.Layout(view, width);
        AssertEqual(style, combo.Style, "Cursor behavior preserves native ComboBox Style");
        AssertEqual(template, combo.Template, "Cursor behavior preserves native ComboBox Template");
    }

    private static void InspectPopups(SettingsView view, SettingsViewModel vm, double width)
    {
        vm.BrowseMode = VoiceBrowseMode.Search; SettingsLayoutSuite.Layout(view, width);
        var adjustment = Visuals(view).OfType<SearchAdjustmentControl>().Single();
        ((Button)adjustment.FindName("AdjustmentButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        SettingsLayoutSuite.Layout(view, width);
        var popup = (Popup)adjustment.FindName("AdjustmentPopup");
        AssertEqual(true, popup.IsOpen, "The actual adjustment popup opens");
        var surface = (FrameworkElement)popup.Child;
        var buttons = Visuals(surface).OfType<ButtonBase>().Where(button => button.IsVisible).ToArray();
        AssertEqual(11, buttons.OfType<ToggleButton>().Count(button => button is not RadioButton), "All eleven generated language tiles are exercised");
        AssertEqual(3, buttons.OfType<RadioButton>().Count(), "All native sort radios are exercised");
        foreach (var button in buttons.Where(CanPoint)) AssertHand(button, $"Actual adjustment popup action {button.GetType().Name}/{button.Name} is Hand");
        var apply = (Button)adjustment.FindName("ApplyButton");
        AssertEqual(view.FindResource("AccentButtonStyle"), apply.Style, "Apply retains the original native accent Style");
        AssertNative((TextBlock)Visuals(surface).OfType<TextBlock>().First(block => !Ancestors(block).OfType<ButtonBase>().Any()), "Popup heading remains passive");
        ((Button)adjustment.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        SettingsLayoutSuite.Layout(view, width);
        foreach (var tags in Visuals(view).OfType<VoiceLanguageTags>().Where(control => control.IsVisible).ToArray())
        {
            var more = (Button)tags.FindName("MoreButton");
            if (!more.IsVisible) continue;
            AssertHand(more, "Language +N trigger is actionable");
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); SettingsLayoutSuite.Layout(view, width);
            var languagePopup = (Popup)tags.FindName("LanguagePopup");
            AssertEqual(true, languagePopup.IsOpen, "Actual language popup opens");
            foreach (var label in Visuals(languagePopup.Child).OfType<TextBlock>().Where(control => control.IsVisible))
                AssertNative(label, "Language popup pills/metadata remain passive");
            foreach (var bar in Visuals(languagePopup.Child).OfType<ScrollBar>().Where(control => control.IsVisible)) InspectScrollBar(bar);
            languagePopup.IsOpen = false; SettingsLayoutSuite.Layout(view, width);
        }
    }

    private static void InspectScrollBar(ScrollBar bar)
    {
        AvatarPreviewSuite.SetHover(bar, true); bar.UpdateLayout();
        var track = (Track)bar.Template.FindName("PART_Track", bar);
        AssertHand(track.Thumb, "Native scrollbar thumb is actionable");
        AssertHand(track.DecreaseRepeatButton, "Native scrollbar decrease track is actionable");
        AssertHand(track.IncreaseRepeatButton, "Native scrollbar increase track is actionable");
        foreach (var arrow in Visuals(bar).OfType<RepeatButton>().Where(CanPoint)) AssertHand(arrow, "Visible native scrollbar arrows are actionable");
        AvatarPreviewSuite.SetHover(bar, false);
    }

    private static void InspectNativePeers(SettingsView themedView, Action<string> stage, bool lifecycle)
    {
        stage("peer source construction begin");
        var panel = new StackPanel();
        var passive = new SettingsCard { Header = "Passive native card" };
        var active = new SettingsCard { Header = "Clickable native card", IsClickEnabled = true };
        var combo = new ComboBox { IsEditable = true, Width = 180, ItemsSource = Enumerable.Range(0, 100).ToArray(), SelectedIndex = 0 };
        var scroll = new ScrollViewer { Height = 80, Content = new Border { Height = 1000 }, VerticalScrollBarVisibility = ScrollBarVisibility.Visible };
        panel.Children.Add(passive); panel.Children.Add(active); panel.Children.Add(combo); panel.Children.Add(scroll);
        var scope = new Border { Child = panel }; ActionCursor.SetIsScope(scope, true);
        var host = new Button { Content = "Native host peer outside plugin" };
        var root = new StackPanel(); root.Children.Add(scope); root.Children.Add(host);
        foreach (var dictionary in themedView.Resources.MergedDictionaries) root.Resources.MergedDictionaries.Add(dictionary);
        AddOfflineAccent(root);
        var dark = ThemeManager.GetActualTheme(themedView) == ElementTheme.Dark;
        ThemeManager.SetRequestedTheme(root, dark ? ElementTheme.Dark : ElementTheme.Light);
        using var source = new HwndSource(new HwndSourceParameters("Offline native cursor peers")
        { Width = 600, Height = 600, WindowStyle = unchecked((int)0x80000000) });
        source.RootVisual = root; SettingsLayoutSuite.Layout(root, 500);
        AssertEqual(dark ? ElementTheme.Dark : ElementTheme.Light, ThemeManager.GetActualTheme(root), "Native peer root receives the requested theme");
        stage("peer source construction/layout completed");
        var editor = (TextBox)combo.Template.FindName("PART_EditableTextBox", combo);
        SetOfflineInputBrushes(editor);
        if (!lifecycle)
        {
            AssertEqual(false, Query(passive) == Cursors.Hand, "IsClickEnabled=false native SettingsCard remains passive");
            AssertHand(active, "IsClickEnabled=true native SettingsCard is actionable");
            AssertNative(host, "Native host peer outside plugin scope remains unchanged");
            AssertInputUnchanged(scope, editor);
            var arrow = Visuals(combo).OfType<ToggleButton>().First(control => control.IsVisible); AssertHand(arrow, "Editable combo dropdown arrow remains an action");
            combo.IsDropDownOpen = true; SettingsLayoutSuite.Layout(root, 500);
            var item = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(0);
            AssertHand(item, "Editable combo generates actionable native items");
            var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
            foreach (var bar in Visuals(popup.Child).OfType<ScrollBar>().Where(control => control.IsVisible)) InspectScrollBar(bar);
            combo.IsDropDownOpen = false;
            foreach (var bar in Visuals(scroll).OfType<ScrollBar>().Where(control => control.IsVisible)) InspectScrollBar(bar);
            var template = combo.Template;
            stage("native card/input/dropdown/scrollbar completed; template begin");
            combo.Template = null; SettingsLayoutSuite.Layout(root, 500); combo.Template = template; SettingsLayoutSuite.Layout(root, 500);
            combo.IsDropDownOpen = true; SettingsLayoutSuite.Layout(root, 500);
            AssertHand((ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(0), "Template replacement reattaches the current native dropdown");
            combo.IsDropDownOpen = false;
            stage("parts/template assertions completed; peer source cleanup next");
            return;
        }
        stage("localreload begin");
        panel.Children.Remove(combo); SettingsLayoutSuite.Layout(root, 500);
        panel.Children.Insert(2, combo); SettingsLayoutSuite.Layout(root, 500);
        combo.IsDropDownOpen = true; SettingsLayoutSuite.Layout(root, 500);
        AssertHand((ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(0), "A combo reloaded within the still-loaded scope regains its native dropdown handler");
        stage("localreload completed; scope-reload begin");
        combo.IsDropDownOpen = false;
        root.Children.Remove(scope); SettingsLayoutSuite.Layout(root, 500);
        root.Children.Insert(0, scope); SettingsLayoutSuite.Layout(root, 500);
        combo.IsDropDownOpen = true; SettingsLayoutSuite.Layout(root, 500);
        AssertHand((ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(0), "Unload/reload reattaches the native dropdown locally");
        stage("scope-reload completed; foreign reparent begin");
        combo.IsDropDownOpen = false;
        panel.Children.Remove(combo); root.Children.Add(combo); SettingsLayoutSuite.Layout(root, 500);
        combo.IsDropDownOpen = true; SettingsLayoutSuite.Layout(root, 500);
        AssertNative((ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(0), "A genuinely removed combo reparented to the host has no plugin popup cursor behavior");
        AssertNative(combo, "Foreign reparenting also removes plugin cursor scope from the combo body");
        stage("foreign reparent completed; scope removal begin");
        combo.IsDropDownOpen = false;
        root.Children.Remove(combo); panel.Children.Insert(2, combo); SettingsLayoutSuite.Layout(root, 500);
        combo.IsDropDownOpen = true; SettingsLayoutSuite.Layout(root, 500);
        AssertHand((ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(0), "Returning an existing combo from a foreign host to its original still-loaded scope restores popup Hand without resetting the scope");
        combo.IsDropDownOpen = false;
        stage("foreign-to-original return completed without scope reset");
        ActionCursor.SetIsScope(scope, false); ActionCursor.SetIsScope(scope, true);
        combo.IsDropDownOpen = true; SettingsLayoutSuite.Layout(root, 500);
        ActionCursor.SetIsScope(scope, false);
        AssertNative(active, "Removing the local scope removes its cursor handler");
        AssertNative((ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(0), "Removing scope while the dropdown is open cleans its current generated-popup handlers");
        stage("scope removal completed; live-unloaded begin");
        combo.IsDropDownOpen = false;
        ActionCursor.SetIsScope(scope, true);
        panel.Children.Remove(combo); SettingsLayoutSuite.Layout(root, 500);
        AssertEqual(false, combo.IsLoaded, "The removed combo remains alive and unloaded while its scope is still loaded");
        root.Children.Remove(scope); SettingsLayoutSuite.Layout(root, 500);
        root.Children.Add(combo); SettingsLayoutSuite.Layout(root, 500);
        combo.IsDropDownOpen = true; SettingsLayoutSuite.Layout(root, 500);
        AssertNative((ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(0), "Scope retirement clears the live-unloaded combo watch before it later joins a foreign host");
        combo.IsDropDownOpen = false;
        stage("live-unloaded retirement completed; peer source cleanup next");
    }

    private static void AddOfflineAccent(FrameworkElement root)
    {
        // A headless STA has no host Application/OS accent initialization.
        foreach (var suffix in new[] { "", "Dark1", "Dark2", "Dark3", "Light1", "Light2", "Light3" })
            root.Resources["SystemAccentColor" + suffix] = Color.FromRgb(0x00, 0x78, 0xD4);
    }

    private static void SetOfflineInputBrushes(TextBox editor)
    {
        // Native caret selection would otherwise ask the absent host Application for accent colors.
        editor.SelectionBrush = Brushes.DodgerBlue;
        editor.CaretBrush = Brushes.Black;
        // This fixture measures cursor routing, not native focus/accent behavior.
        editor.Focusable = false;
    }

    private static void AssertInputUnchanged(FrameworkElement scope, TextBox editor)
    {
        var target = Hit(editor); var actual = Query(target);
        ActionCursor.SetIsScope(scope, false); var native = Query(target); ActionCursor.SetIsScope(scope, true);
        AssertEqual(native, actual, "Input keeps the native resolved query result");
        AssertEqual(false, actual == Cursors.Hand, "Input never inherits Hand from an actionable owner");
    }

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject child)
    {
        for (var current = VisualTreeHelper.GetParent(child); current is not null; current = VisualTreeHelper.GetParent(current)) yield return current;
    }
    private static UIElement Hit(UIElement element)
    {
        element.UpdateLayout();
        return element.InputHitTest(new Point(element.RenderSize.Width / 2, element.RenderSize.Height / 2)) as UIElement ?? element;
    }
    private static bool CanPoint(UIElement element) => element.IsVisible && element.IsEnabled && element.IsHitTestVisible
        && element.RenderSize.Width > 0 && element.RenderSize.Height > 0
        && new[] { (DependencyObject)element }.Concat(Ancestors(element)).OfType<UIElement>().All(parent => parent.IsVisible && parent.IsHitTestVisible);
    private static Cursor Query(DependencyObject target)
    {
        var args = new QueryCursorEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.QueryCursorEvent, Cursor = Cursors.Arrow };
        if (target is UIElement element) element.RaiseEvent(args);
        else ((ContentElement)target).RaiseEvent(args);
        return args.Cursor;
    }
    private static void AssertHand(UIElement element, string message)
    {
        var hit = Hit(element); var cursor = Query(hit);
        if (cursor != Cursors.Hand)
            Console.WriteLine($"[cursor] target={element.GetType().Name}/{(element as FrameworkElement)?.Name} hit={hit.GetType().Name} route={string.Join(';', new[] { (DependencyObject)hit }.Concat(Ancestors(hit)).OfType<UIElement>().Select(item => $"{item.GetType().Name}:visible={item.IsVisible},enabled={item.IsEnabled},hit={item.IsHitTestVisible},opacity={item.Opacity},scope={ActionCursor.GetIsScope(item)}"))}");
        AssertEqual(Cursors.Hand, cursor, message);
    }
    private static void AssertNative(UIElement element, string message) => AssertEqual(false, Query(Hit(element)) == Cursors.Hand, message);
}
