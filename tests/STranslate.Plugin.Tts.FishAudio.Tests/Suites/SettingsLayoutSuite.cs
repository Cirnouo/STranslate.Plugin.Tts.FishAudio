using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Controls;
using static ContextProxy;
using static StaTestHost;
using static TestAssertions;

internal static class SettingsLayoutSuite
{
    internal static async Task LikePresentationAsync()
    {
        await RunOnStaThreadAsync(FavoriteVoicesTabMatchesPeerTabs);
        await RunOnStaThreadAsync(SelectedVoiceHeartSharesFavoriteState);
    }

    internal static async Task CompactNumericGeometryAsync()
    {
        // Explicit root-DPI propagation warms shared WPF theme metrics. Keep
        // this group after VoiceCardInkSuite's first-use characterization.
        foreach (var (language, culture) in InlineNumericEditorSuite.Cultures)
        foreach (var dark in new[] { false, true })
        foreach (var dpi in new[] { 1d, 1.25, 1.5 })
            await RunOnStaThreadAsync(() => InlineNumericEditorSuite.CompactGeometryForCulture(language, culture, dark, dpi));
    }

    internal static async Task InlineNumericInteractionsAsync()
    {
        // Every locale retains the same shared STA and five-second action
        // deadline. CI must not fit eight complete UI fixtures into one action.
        foreach (var (language, culture) in InlineNumericEditorSuite.Cultures)
            await RunOnStaThreadAsync(() => InlineNumericEditorSuite.RunForCulture(CreateThemedView, Layout, language, culture));
    }

    // The host theme caches dispatcher-bound resources, so render these
    // compiled-view cases on one STA, as they are used in the application.
    internal static async Task CompiledViewAudioNetworkAndFoldInteractionsAsync()
    {
        foreach (var scenario in new Action[]
        {
            PcmHidesWholeBitrateCardAndMp3RestoresIt,
            ReadingNetworkSettingsAreAbsent,
            FavoriteVoicesTabMatchesPeerTabs,
            SelectedVoiceHeartSharesFavoriteState,
            PasteButtonsReserveInputSpace,
            SelectionAndPagingActionsUseBareIcons,
            LoudnessCardFollowsModelCapability,
            TopPMinimumKeepsSliderTracksAligned,
            ResultTogglesKeepIndependentViewState,
            SharedVoiceCardReflectsMetadataState,
        })
        {
            var phase = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"[settings-layout] {scenario.Method.Name} queued.");
            await RunOnStaThreadAsync(() =>
            {
                Console.WriteLine($"[settings-layout] {scenario.Method.Name} entered after {phase.ElapsedMilliseconds}ms.");
                scenario();
                Console.WriteLine($"[settings-layout] {scenario.Method.Name} completed after {phase.ElapsedMilliseconds}ms.");
            });
        }
        // Keep the same dispatcher and bounded per-group deadline as the
        // native template/viewport matrix grows.
        await InlineNumericInteractionsAsync();
    }

    private static void PcmHidesWholeBitrateCardAndMp3RestoresIt()
    {
        var settings = new Settings { AudioFormat = "mp3", Mp3Bitrate = 128 };
        using var viewModel = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = CreateThemedView(viewModel);
        DrainBindings();
        var bitrate = BoundComboBox(view, nameof(SettingsViewModel.Mp3Bitrate));
        var card = Ancestor<SettingsCard>(bitrate);
        AssertEqual(Visibility.Visible, card.Visibility, "MP3 must show the complete bitrate setting");

        BoundComboBox(view, nameof(SettingsViewModel.AudioFormat)).SelectedItem = "pcm";
        DrainBindings();
        AssertEqual("pcm", settings.AudioFormat, "Choosing PCM in the compiled view must update settings");
        AssertEqual(Visibility.Collapsed, card.Visibility, "PCM must collapse the entire bitrate card, including its header and spacing");
        AssertEqual(128, settings.Mp3Bitrate, "Hiding the card must preserve the chosen bitrate");

        BoundComboBox(view, nameof(SettingsViewModel.AudioFormat)).SelectedItem = "mp3";
        DrainBindings();
        AssertEqual(Visibility.Visible, card.Visibility, "Switching back to MP3 must restore the bitrate card");
        AssertEqual(true, bitrate.IsEnabled, "The restored MP3 setting must remain usable");
        AssertEqual(128, bitrate.SelectedItem, "The restored MP3 setting must retain its value");
        Layout(view);
        AssertStandardCard(view, BoundComboBox(view, nameof(SettingsViewModel.AudioFormat)));
    }

    private static void ResultTogglesKeepIndependentViewState()
    {
        var settings = new Settings();
        using var viewModel = new SettingsViewModel(CreateContext(settings: settings), settings) { HasSearched = true };
        var view = CreateThemedView(viewModel);
        DrainBindings();
        var search = view.FindName("VoiceSearchResultsToggle") as ToggleButton;
        var favorites = view.FindName("FavoriteVoicesToggle") as ToggleButton;
        AssertNotNull(search, "Search results must expose an icon toggle on their count row");
        AssertNotNull(favorites, "Favorite results must expose an independent icon toggle on their count row");
        var searchBody = (FrameworkElement)view.FindName("VoiceSearchResultsBody");
        var favoritesBody = (FrameworkElement)view.FindName("FavoriteVoicesBody");
        search!.ApplyTemplate();
        favorites!.ApplyTemplate();
        Layout(view);
        var countRow = (DockPanel)LogicalTreeHelper.GetParent(search);
        var count = countRow.Children.OfType<TextBlock>().Single();
        AssertEqual(true, search.Content is System.Windows.Shapes.Path, "The fold control must contain an icon without a visible label");
        AssertEqual(true, search.TranslatePoint(new Point(), countRow).X > count.TranslatePoint(new Point(), countRow).X,
            "The fold icon must sit to the right of the count in the same row");
        AssertEqual(true, count.ActualHeight > 0, "The count must remain laid out independently of the body");
        AssertEqual(Visibility.Visible, searchBody.Visibility, "Search results start expanded");
        AssertEqual(Visibility.Visible, favoritesBody.Visibility, "Favorite results start expanded");

        // Invoke the actual native click path without opening a window or sending desktop input.
        typeof(ToggleButton).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(search, null);
        DrainBindings();
        Layout(view);
        AssertEqual(false, search.IsChecked, "Clicking the search icon must clear its expanded state");
        AssertEqual(Visibility.Collapsed, searchBody.Visibility, "Clicking the search icon must hide the whole result body");
        AssertEqual(0d, searchBody.DesiredSize.Height, "A collapsed result body must leave no list or paging space");
        AssertEqual(true, count.ActualHeight > 0, "Collapsing the results must keep the count visible in its row");
        AssertEqual(Visibility.Visible, favoritesBody.Visibility, "Collapsing search must leave favorites expanded");
        AssertEqual("Expand search results", AutomationProperties.GetName(search), "The collapsed icon must name its available action");

        viewModel.SearchQuery = "another query";
        viewModel.HasSearched = false;
        viewModel.HasSearched = true;
        viewModel.SwitchToFavoritesCommand.Execute(null);
        viewModel.SwitchToByIdCommand.Execute(null);
        viewModel.SwitchToSearchCommand.Execute(null);
        DrainBindings();
        AssertEqual(false, search.IsChecked, "Query changes and tab switches must preserve the view's search fold state");
        AssertEqual(true, favorites.IsChecked, "Query changes and tab switches must preserve the separate favorites fold state");

        var inputSource = new UnhostedInputSource();
        search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, 0, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
        search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, 0, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
        DrainBindings();
        AssertEqual(true, search.IsChecked, "The icon must expand through the native Space-key activation path");
        AssertEqual(Visibility.Visible, searchBody.Visibility, "Keyboard activation must restore the whole result body");
        AssertEqual("Collapse search results", AutomationProperties.GetName(search), "The expanded icon must name its available action");

        var peer = new ToggleButtonAutomationPeer(favorites);
        var toggle = (IToggleProvider)peer.GetPattern(PatternInterface.Toggle)!;
        AssertEqual(ToggleState.On, toggle.ToggleState, "Assistive technology must observe the expanded state");
        toggle.Toggle();
        DrainBindings();
        AssertEqual(ToggleState.Off, toggle.ToggleState, "Assistive technology must be able to change the fold state");
        AssertEqual(Visibility.Collapsed, favoritesBody.Visibility, "Accessible toggling must hide the favorite cards and paging together");
        AssertEqual(Visibility.Visible, searchBody.Visibility, "Accessible favorites toggling must leave search unchanged");
    }

    private static void ReadingNetworkSettingsAreAbsent()
    {
        var settings = new Settings();
        using var viewModel = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = CreateThemedView(viewModel);
        DrainBindings();
        var proxyModes = Descendants(view).OfType<ComboBox>().Where(control =>
            BindingOperations.GetBinding(control, ComboBox.SelectedItemProperty)?.Path.Path == "StreamingProxyMode");
        var proxyAddresses = Descendants(view).OfType<TextBox>().Where(control =>
            BindingOperations.GetBinding(control, TextBox.TextProperty)?.Path.Path == "StreamingProxyAddress");
        AssertEqual(0, proxyModes.Count(), "The compiled view must not contain reading connection settings");
        AssertEqual(0, proxyAddresses.Count(), "The compiled view must not contain a custom proxy address field");
        AssertEqual(false, Descendants(view).OfType<TextBlock>().Any(block => block.Text == "Reading Network"),
            "Removing the network controls must also remove their group heading");
    }

    private static void FavoriteVoicesTabMatchesPeerTabs()
    {
        var settings = new Settings();
        using var viewModel = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = CreateThemedView(viewModel);
        DrainBindings();
        var favorites = Descendants(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, viewModel.SwitchToFavoritesCommand));
        var search = Descendants(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, viewModel.SwitchToSearchCommand));
        var content = favorites.Content as StackPanel;
        AssertNotNull(content, "The Favorite Voices tab must have an icon and label like its peer tabs");
        var icon = content!.Children.OfType<System.Windows.Shapes.Path>().SingleOrDefault();
        AssertNotNull(icon, "The Likes tab must use a heart vector");
        AssertEqual(true, ReferenceEquals(view.FindResource("VoiceLikeHeartGeometry"), icon!.Data), "The tab shares the card heart contour");
        var referenceIcon = ((StackPanel)search.Content).Children.OfType<TextBlock>().Single(block => block.FontFamily.Source == "Segoe MDL2 Assets");
        AssertEqual(referenceIcon.FontSize, icon.Width, "Likes must retain the peer icon allocation");
        AssertEqual(referenceIcon.FontSize, icon.Height, "Likes must retain the peer icon height");
        AssertEqual(referenceIcon.Margin, icon.Margin, "Likes must retain peer icon spacing");
        AssertEqual(Colors.Transparent, ((SolidColorBrush)icon.Fill).Color, "Inactive tab heart is outlined");
        viewModel.BrowseMode = VoiceBrowseMode.Favorites;
        DrainBindings();
        AssertEqual(((SolidColorBrush)((TextBlock)view.FindName("FavoritesTabLabel")).Foreground).Color,
            ((SolidColorBrush)icon.Fill).Color, "Active tab heart fills with its on-accent label foreground");
        viewModel.BrowseMode = VoiceBrowseMode.Search;
        DrainBindings();
        AssertEqual(Colors.Transparent, ((SolidColorBrush)icon.Fill).Color, "Leaving Likes restores the outline");
        foreach (var (locale, label) in new[] { ("zh-cn", "喜欢"), ("zh-tw", "喜歡"), ("ja", "いいね"), ("ko", "좋아요"), ("ru", "Нравится"), ("tr", "Beğeniler"), ("en", "Likes") })
        {
            view.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(FindRepoFile($"STranslate.Plugin.Tts.FishAudio/Languages/{locale}.xaml"), UriKind.Absolute),
            });
            DrainBindings();
            AssertEqual(true, content.Children.OfType<TextBlock>().Any(block => block.Text == label),
                $"Changing to {locale} must update the compiled tab label");
            var support = (iNKORE.UI.WPF.Modern.Controls.SettingsCard)view.FindName("SupportCard");
            AssertEqual(view.FindResource("STranslate_Plugin_Tts_FishAudio_Support_Description"), support.Description,
                $"Changing to {locale} must update the compiled Support description");
            AssertEqual(view.FindResource("STranslate_Plugin_Tts_FishAudio_Tab_Search"), ((TextBlock)view.FindName("SearchTabLabel")).Text,
                $"Changing to {locale} must update the compiled Search label");
        }
    }

    private static void SelectedVoiceHeartSharesFavoriteState()
    {
        var id = "0123456789abcdef0123456789abcdef";
        var settings = new Settings { VoiceId = id, CachedVoice = new() { Title = "Selected voice" } };
        using var viewModel = new SettingsViewModel(CreateContext(settings: settings, pluginSettingsDirectoryPath: CreateTempDirectory()), settings);
        var matching = new VoiceSearchItem { Id = id, Title = "Matching search result" };
        viewModel.SearchResults = [matching];
        var view = CreateThemedView(viewModel);
        DrainBindings();
        Layout(view);
        var selected = Descendants(view).OfType<Button>().SingleOrDefault(button =>
            BindingOperations.GetBinding(button, Button.CommandProperty)?.Path.Path == "ToggleSelectedVoiceFavoriteCommand");
        AssertNotNull(selected, "The selected voice card must expose its own favorite action");
        AssertEqual(true, selected!.Content is System.Windows.Shapes.Path, "The selected voice favorite must use a vector heart");
        AssertNotNull(selected.Command, "The selected voice favorite command must bind to the live ViewModel");
        AssertEqual(Visibility.Visible, selected.Visibility, "A selected voice must expose its favorite heart");
        AssertEqual("Like", AutomationProperties.GetName(selected), "An unbookmarked selected voice must offer Add");
        var heart = (System.Windows.Shapes.Path)selected.Content;
        AssertEqual(true, ReferenceEquals(view.FindResource("VoiceLikeHeartGeometry"), heart.Data), "Selected and list hearts share one closed contour");
        AssertEqual(Colors.Transparent, ((SolidColorBrush)heart.Fill).Color, "An unbookmarked selected voice must show an outline heart");

        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(selected, null);
        DrainBindings();
        AssertEqual(true, matching.IsFavorite, "Favoriting the selected voice must update a matching browse card");
        AssertEqual("Unlike", AutomationProperties.GetName(selected), "The selected heart must reflect the saved favorite state");
        AssertEqual(((SolidColorBrush)view.FindResource("AccentFillColorDefaultBrush")).Color, ((SolidColorBrush)heart.Fill).Color, "A saved selected voice must show a filled heart");
        AssertEqual(1, viewModel.FavoriteCount, "The selected-card action must save through the shared favorites store");
        AssertEqual(id, settings.VoiceId, "Favoriting a selected voice must preserve the applied voice");

        viewModel.ToggleFavoriteCommand.Execute(matching);
        DrainBindings();
        AssertEqual("Like", AutomationProperties.GetName(selected), "Removing a matching browse favorite must update the selected heart");
        AssertEqual(Colors.Transparent, ((SolidColorBrush)heart.Fill).Color, "Browse removal must restore the selected heart outline");
        AssertEqual(0, viewModel.FavoriteCount, "Browse removal must remove the same persisted favorite");
        viewModel.ClearVoiceCommand.Execute(null);
        DrainBindings();
        AssertEqual(Visibility.Collapsed, selected.Visibility, "Random voice mode must hide the selected-card favorite action");
    }

    private static void PasteButtonsReserveInputSpace()
    {
        var settings = new Settings();
        using var viewModel = new SettingsViewModel(CreateContext(settings: settings), settings);
        viewModel.BrowseMode = VoiceBrowseMode.ById;
        var view = CreateThemedView(viewModel);
        DrainBindings();
        var password = Descendants(view).OfType<PasswordBox>().Single();
        var voiceId = Descendants(view).OfType<TextBox>().Single(box =>
            BindingOperations.GetBinding(box, TextBox.TextProperty)?.Path.Path == nameof(SettingsViewModel.VoiceIdInput));
        var passwordPaste = Descendants(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, viewModel.PasteApiKeyCommand));
        var voiceIdPaste = Descendants(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, viewModel.PasteVoiceIdCommand));
        foreach (var width in new[] { 400d, 600d })
        {
            Layout(view, width);
            foreach (var (input, paste) in new (Control, Button)[] { (password, passwordPaste), (voiceId, voiceIdPaste) })
            {
                var pasteLeft = paste.TranslatePoint(new Point(), input).X;
                AssertEqual(true, pasteLeft >= 0 && pasteLeft + paste.ActualWidth <= input.ActualWidth,
                    $"At {width}px Paste must be inside the input's right edge");
                AssertEqual(true, input.ActualWidth - input.Padding.Right <= pasteLeft,
                    $"At {width}px the input must reserve real text space before Paste");
                var textHost = input.Template.FindName("PART_ContentHost", input) as FrameworkElement;
                AssertNotNull(textHost, "The real input template must expose its text viewport");
                var viewport = VisualDescendants(textHost!).OfType<ScrollContentPresenter>().Single();
                AssertEqual(true, viewport.TranslatePoint(new Point(viewport.ActualWidth, 0), input).X <= pasteLeft,
                    $"At {width}px typed input must not run underneath Paste");
            }
        }
        password.Password = DraftKey;
        DrainBindings();
        AssertEqual(DraftKey, viewModel.ApiKey, "Embedding Paste must preserve immediate password updates");
        var binding = BindingOperations.GetBinding(password, PasswordBoxAssistant.PasswordProperty)!;
        AssertEqual(BindingMode.TwoWay, binding.Mode, "The embedded API Key input must retain explicit TwoWay binding");
        AssertEqual(UpdateSourceTrigger.PropertyChanged, binding.UpdateSourceTrigger, "The embedded API Key input must save before focus leaves");
        var query = Descendants(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, viewModel.SubmitVoiceIdCommand));
        var search = Descendants(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, viewModel.SearchVoicesCommand));
        AssertEqual(true, query.Content is FontIcon, "By ID lookup must use the native search magnifier");
        AssertEqual(((FontIcon)search.Content).Icon, ((FontIcon)query.Content).Icon, "By ID lookup must match the existing Search magnifier");
    }

    private static void SelectionAndPagingActionsUseBareIcons()
    {
        var id = "0123456789abcdef0123456789abcdef";
        var settings = new Settings { VoiceId = id, CachedVoice = new() { Title = "Selected voice" } };
        using var viewModel = new SettingsViewModel(CreateContext(settings: settings), settings);
        var searchItem = new VoiceSearchItem { Id = id, Title = "Search voice" };
        var byIdItem = new VoiceSearchItem { Id = id, Title = "By ID voice" };
        var favoriteItem = new VoiceSearchItem { Id = id, Title = id, HasMetadata = false };
        viewModel.SearchResults = [searchItem];
        viewModel.ByIdResults = [byIdItem];
        viewModel.FavoriteResults = [favoriteItem];
        viewModel.HasSearched = true;
        viewModel.SearchPage = 2;
        viewModel.SearchTotalPages = 3;
        viewModel.SearchHasNextPage = true;
        viewModel.FavoritePage = 2;
        viewModel.FavoriteTotalPages = 3;
        var view = CreateThemedView(viewModel);
        DrainBindings();
        foreach (var mode in new[] { VoiceBrowseMode.Search, VoiceBrowseMode.ById, VoiceBrowseMode.Favorites })
        {
            viewModel.BrowseMode = mode;
            DrainBindings();
            Layout(view);
        }
        var clear = Descendants(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, viewModel.ClearVoiceCommand));
        AssertBareIcon(clear, "\uE711", "Clear");
        var actions = new[] { searchItem, byIdItem, favoriteItem }.Select(item =>
            VisualDescendants(view).OfType<Button>().Single(button =>
                ReferenceEquals(button.Command, viewModel.SelectVoiceCommand) && ReferenceEquals(button.CommandParameter, item))).ToArray();
        foreach (var action in actions)
        {
            AssertBareIcon(action, "\uE711", "Clear");
            AssertEqual(true, action.IsEnabled, "Every matching card, including an ID-only placeholder, must allow clearing selection");
            AssertEqual(clear.Template, action.Template, "Selected and browse selection actions must share their bare icon template");
        }
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(actions[2], null);
        DrainBindings();
        AssertEqual("", settings.VoiceId, "A matching placeholder Cross must clear the current selection");
        AssertEqual(Visibility.Collapsed, clear.Visibility, "Clearing from a browse card must retire the selected-card Cross");
        foreach (var action in actions)
        {
            AssertEqual("\uE73E", action.Content, "Clearing selection must return every browse action to Tick");
            AssertEqual("Apply", AutomationProperties.GetName(action), "An unselected card must expose the Apply action");
            AssertEqual("Apply", action.ToolTip, "An unselected Tick must expose the Apply tooltip");
        }
        AssertEqual(false, actions[2].IsEnabled, "An unselected placeholder must not apply without metadata");
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(actions[0], null);
        DrainBindings();
        AssertEqual(id, settings.VoiceId, "Tick must apply its originating voice");
        AssertEqual(Visibility.Visible, clear.Visibility, "Applying a browse voice must restore the selected-card Cross");
        foreach (var action in actions)
        {
            AssertEqual("\uE711", action.Content, "Applying a voice must synchronize every matching card to Cross");
            AssertEqual("Clear", AutomationProperties.GetName(action), "Every matching card must expose the Clear action");
            AssertEqual(true, action.IsEnabled, "An applied placeholder must regain its enabled Cross");
        }

        foreach (var (command, glyph, name) in new[]
        {
            ((System.Windows.Input.ICommand)viewModel.PrevPageCommand, "\uE76B", "Prev"),
            (viewModel.NextPageCommand, "\uE76C", "Next"),
            (viewModel.FavoritePrevPageCommand, "\uE76B", "Prev"),
            (viewModel.FavoriteNextPageCommand, "\uE76C", "Next"),
        })
        {
            var pager = Descendants(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, command));
            AssertBareIcon(pager, glyph, name);
            AssertEqual(clear.Template, pager.Template, "Both pagers must share the selection action's bare icon template");
        }
        foreach (var (mode, inputName) in new[] { (VoiceBrowseMode.Search, "PageInputBox"), (VoiceBrowseMode.Favorites, "FavoritePageInputBox") })
        {
            viewModel.BrowseMode = mode;
            DrainBindings();
            foreach (var width in new[] { 400d, 600d })
            {
                Layout(view, width);
                var input = (TextBox)view.FindName(inputName);
                var center = (StackPanel)LogicalTreeHelper.GetParent(input);
                var row = (Grid)LogicalTreeHelper.GetParent(center);
                var centerX = center.TranslatePoint(new Point(center.ActualWidth / 2, 0), row).X;
                AssertEqual(true, Math.Abs(centerX - row.ActualWidth / 2) < 0.001,
                    $"At {width}px the {mode} page input must stay centered between its chevrons");
            }
        }
        viewModel.SearchPage = 1;
        viewModel.SearchHasNextPage = false;
        viewModel.FavoritePage = 1;
        viewModel.FavoriteTotalPages = 1;
        DrainBindings();
        foreach (var command in new System.Windows.Input.ICommand[]
        {
            viewModel.PrevPageCommand, viewModel.NextPageCommand,
            viewModel.FavoritePrevPageCommand, viewModel.FavoriteNextPageCommand,
        })
            AssertEqual(Visibility.Collapsed, Descendants(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, command)).Visibility,
                "Pager boundaries must still hide unavailable page actions");
    }

    private static void AssertBareIcon(Button button, string glyph, string action)
    {
        button.ApplyTemplate();
        AssertEqual(glyph, button.Content, "The action must contain only its symbol glyph");
        AssertEqual("Segoe MDL2 Assets", button.FontFamily.Source, "The action must use the native symbol font");
        AssertEqual(action, AutomationProperties.GetName(button), "The icon must expose its localized accessible action");
        AssertEqual(action, button.ToolTip, "The icon must expose its localized tooltip");
        AssertEqual(Colors.Transparent, ((SolidColorBrush)button.Background).Color, "Idle actions must have a transparent background");
        AssertEqual(new Thickness(0), button.BorderThickness, "Idle actions must have no surrounding border");
        var circle = (Border?)button.Template.FindName("BareIconSurface", button);
        AssertNotNull(circle, "The resolved action template must provide a round hover and focus surface");
        AssertEqual(true, circle!.CornerRadius.TopLeft >= button.Height / 2, "The native action surface must remain circular");
        AssertEqual(true, circle.Child is InkCenteredGlyph, "The round action surface must contain the native glyph renderer");
        AssertEqual(glyph, ((InkCenteredGlyph)circle.Child).Glyph, "The resolved template must render the exact action symbol");
        AssertEqual(false, ((InkCenteredGlyph)circle.Child).IsHitTestVisible, "The glyph must preserve the surrounding button hit target");
        AssertEqual(Colors.Transparent, ((SolidColorBrush)circle!.Background).Color, "The resolved idle template must show only the glyph");
        AssertEqual(new Thickness(0), circle.BorderThickness, "The resolved idle template must not draw a box");
        AssertEqual<Style?>(null, button.FocusVisualStyle, "The action must use its circular focus indicator without a rectangular focus adorner");
        try
        {
            SetNativeInputState(button, "IsMouseOver", true);
            AssertEqual(true, ((SolidColorBrush)circle.Background).Color.A > 0, "Hover must resolve to a visible themed fill");
        }
        finally { SetNativeInputState(button, "IsMouseOver", false); }
        try
        {
            SetNativeInputState(button, "IsKeyboardFocused", true);
            AssertEqual(new Thickness(1), circle.BorderThickness, "Keyboard focus must resolve to the circular outline");
            AssertEqual(true, ((SolidColorBrush)circle.BorderBrush).Color.A > 0, "Keyboard focus must use a visible themed outline");
        }
        finally { SetNativeInputState(button, "IsKeyboardFocused", false); }
    }

    private static void SetNativeInputState(UIElement element, string name, bool value)
    {
        // Drive the native template inputs without opening a desktop window.
        var key = (DependencyPropertyKey)typeof(UIElement).GetField(name + "PropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        element.SetValue(key, value);
        DrainBindings();
    }

    private static void LoudnessCardFollowsModelCapability()
    {
        foreach (var stored in new[] { false, true })
        {
            var settings = new Settings { NormalizeLoudness = stored, SelectedModel = FishAudioModelPolicy.S1Model };
            using var viewModel = new SettingsViewModel(CreateContext(settings: settings), settings);
            var view = CreateThemedView(viewModel);
            DrainBindings();
            var toggle = Descendants(view).OfType<ToggleSwitch>().Single(control =>
                BindingOperations.GetBinding(control, ToggleSwitch.IsOnProperty)?.Path.Path == nameof(SettingsViewModel.NormalizeLoudness));
            var card = Ancestor<SettingsCard>(toggle);
            AssertEqual(Visibility.Collapsed, card.Visibility, "S1 must hide the entire unavailable loudness card");
            AssertEqual(stored, settings.NormalizeLoudness, "Hiding loudness must preserve its saved value");
            viewModel.SelectedModel = FishAudioModelPolicy.S21ProModel;
            DrainBindings();
            AssertEqual(Visibility.Visible, card.Visibility, "S2.1 must restore the supported loudness card");
            AssertEqual(true, toggle.IsEnabled, "The restored loudness setting must be usable");
            AssertEqual(stored, toggle.IsOn, "The restored setting must display its saved value");
        }
    }

    private static void TopPMinimumKeepsSliderTracksAligned()
    {
        var settings = new Settings();
        using var viewModel = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = CreateThemedView(viewModel);
        DrainBindings();
        var topP = Descendants(view).OfType<Slider>().Single(control =>
            BindingOperations.GetBinding(control, Slider.ValueProperty)?.Path.Path == nameof(SettingsViewModel.TopP));
        var temperature = Descendants(view).OfType<Slider>().Single(control =>
            BindingOperations.GetBinding(control, Slider.ValueProperty)?.Path.Path == nameof(SettingsViewModel.Temperature));
        Slider.MinimizeValue.Execute(null, topP);
        Slider.MinimizeValue.Execute(null, temperature);
        DrainBindings();
        AssertEqual(0.05, topP.Value, "The real Top P control must stop at the client-supported minimum");
        AssertEqual(0.05, viewModel.TopP, "The minimum UI value must reach the ViewModel");
        AssertEqual(0d, temperature.Value, "The Top P minimum must not change Temperature's zero endpoint");
        foreach (var width in new[] { 400d, 600d })
        {
            Layout(view, width);
            var topTrack = (Track)topP.Template.FindName("PART_Track", topP);
            var temperatureTrack = (Track)temperature.Template.FindName("PART_Track", temperature);
            AssertEqual(180d, topP.Width, "Top P must retain the existing slider width");
            AssertEqual(temperatureTrack.ActualWidth, topTrack.ActualWidth, $"At {width}px both slider tracks must retain identical bounds");
            AssertEqual(true, topTrack.Thumb.ActualWidth > 0, "The compiled slider must lay out its native thumb");
            var topLeft = topTrack.TranslatePoint(new Point(), view).X;
            var temperatureLeft = temperatureTrack.TranslatePoint(new Point(), view).X;
            AssertEqual(true, Math.Abs(topLeft - temperatureLeft) < 0.001, $"At {width}px Top P and Temperature tracks must start in the same column");
            var topThumb = topTrack.Thumb.TranslatePoint(new Point(topTrack.Thumb.ActualWidth / 2, 0), view).X;
            var temperatureThumb = temperatureTrack.Thumb.TranslatePoint(new Point(temperatureTrack.Thumb.ActualWidth / 2, 0), view).X;
            AssertEqual(true, Math.Abs(topThumb - temperatureThumb) < 0.001, $"At {width}px the two minimum thumbs must align at the same left endpoint");
        }
    }

    private static void SharedVoiceCardReflectsMetadataState()
    {
        var settings = new Settings();
        using var viewModel = new SettingsViewModel(CreateContext(settings: settings), settings);
        var item = new VoiceSearchItem
        {
            Id = "0123456789abcdef0123456789abcdef", Title = "0123456789abcdef0123456789abcdef",
            HasMetadata = false, IsLoadingMetadata = true, IsFavorite = true,
        };
        viewModel.ByIdResults = [item];
        viewModel.SwitchToByIdCommand.Execute(null);
        var view = CreateThemedView(viewModel);
        DrainBindings();
        Layout(view);
        var results = Descendants(view).OfType<ItemsControl>().Single(control =>
            BindingOperations.GetBinding(control, ItemsControl.ItemsSourceProperty)?.Path.Path == nameof(SettingsViewModel.ByIdResults));
        var buttons = VisualDescendants(results).OfType<Button>().ToList();
        var apply = buttons.Single(button => ReferenceEquals(button.Command, viewModel.SelectVoiceCommand));
        var favorite = buttons.Single(button => ReferenceEquals(button.Command, viewModel.ToggleFavoriteCommand));
        var preview = buttons.Single(button => ReferenceEquals(button.Command, viewModel.ToggleSearchItemPreviewCommand));
        foreach (var button in new[] { apply, favorite, preview })
            AssertEqual(true, ReferenceEquals(item, button.CommandParameter), "Every shared-card action must receive its originating item");
        AssertEqual(false, apply.IsEnabled, "An ID-only placeholder must not be applied before details load");
        AssertEqual(true, favorite.IsEnabled, "A loading or failed favorite must remain removable");
        AssertEqual(Visibility.Collapsed, Ancestor<Grid>(preview).Visibility, "A placeholder without sample audio must hide preview");
        var loading = VisualDescendants(results).OfType<TextBlock>().Single(block => block.Text == "Loading voice details…");
        AssertEqual(Visibility.Visible, Ancestor<StackPanel>(loading).Visibility, "A pending card must show the localized loading line");

        item.IsLoadingMetadata = false;
        item.MetadataError = "Voice details are temporarily unavailable.";
        DrainBindings();
        AssertEqual(Visibility.Collapsed, Ancestor<StackPanel>(loading).Visibility, "Completion must retire the loading line");
        var error = VisualDescendants(results).OfType<TextBlock>().Single(block => block.Text == "Voice details are temporarily unavailable.");
        AssertEqual(Visibility.Visible, error.Visibility, "A failed detail lookup must remain visible on its originating card");
        AssertEqual(false, apply.IsEnabled, "A failed placeholder must remain unavailable for Apply");
        AssertEqual(true, favorite.IsEnabled, "A failed placeholder must still allow removal");

        item.MetadataError = null;
        item.Title = "Loaded voice";
        item.HasMetadata = true;
        DrainBindings();
        AssertEqual(Visibility.Collapsed, error.Visibility, "A successful retry must clear the previous card error");
        AssertEqual(true, apply.IsEnabled, "Loaded metadata must make Apply available without recreating the card");
    }

    private static void AssertStandardCard(SettingsView view, FrameworkElement input)
    {
        var card = Ancestor<SettingsCard>(input);
        var reference = Ancestor<SettingsCard>(BoundComboBox(view, nameof(SettingsViewModel.SelectedLatency)));
        AssertEqual(true, card.HeaderIcon is FontIcon, "Each new setting must use the existing FontIcon header column");
        var icon = (FontIcon)card.HeaderIcon;
        var referenceIcon = (FontIcon)reference.HeaderIcon;
        AssertEqual(referenceIcon.FontSize, icon.FontSize, "New card icons must inherit the same size as existing settings");
        AssertEqual(reference.Padding, card.Padding, "New cards must retain the existing SettingsCard padding");
        var header = VisualDescendants(card).OfType<TextBlock>().First(block => block.Text == card.Header?.ToString());
        var referenceHeader = VisualDescendants(reference).OfType<TextBlock>().First(block => block.Text == reference.Header?.ToString());
        AssertEqual(referenceHeader.TranslatePoint(new Point(), reference).X, header.TranslatePoint(new Point(), card).X,
            "New card titles must align with the existing title column");
        AssertEqual(referenceIcon.TranslatePoint(new Point(), reference).X, icon.TranslatePoint(new Point(), card).X,
            "New card icons must align with the existing icon column");
        AssertEqual(true, input.TranslatePoint(new Point(), card).X > header.TranslatePoint(new Point(), card).X,
            "At a normal settings width the control must share the horizontal card row");
    }

    private static ComboBox BoundComboBox(DependencyObject root, string property) =>
        Descendants(root).OfType<ComboBox>().Single(control =>
            BindingOperations.GetBinding(control, ComboBox.SelectedItemProperty)?.Path.Path == property);

    private static T Ancestor<T>(DependencyObject child) where T : DependencyObject
    {
        for (var parent = LogicalTreeHelper.GetParent(child); parent is not null; parent = LogicalTreeHelper.GetParent(parent))
            if (parent is T match)
                return match;
        throw new InvalidOperationException($"The compiled input must belong to a {typeof(T).Name}");
    }

    private static void AddLanguageResources(FrameworkElement view) =>
        view.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(FindRepoFile("STranslate.Plugin.Tts.FishAudio/Languages/en.xaml"), UriKind.Absolute),
        });

    internal static SettingsView CreateThemedView(SettingsViewModel viewModel)
    {
        var view = new SettingsView { DataContext = viewModel };
        view.Resources.MergedDictionaries.Add(new ThemeResources { RequestedTheme = ApplicationTheme.Light });
        view.Resources.MergedDictionaries.Add(new XamlControlsResources());
        AddLanguageResources(view);
        ThemeManager.SetRequestedTheme(view, ElementTheme.Light);
        return view;
    }

    internal static void Layout(FrameworkElement view, double width = 600)
    {
        view.Measure(new Size(width, double.PositiveInfinity));
        view.Arrange(new Rect(0, 0, width, view.DesiredSize.Height));
        view.UpdateLayout();
        DrainBindings();
        view.UpdateLayout();
    }

    private static void DrainBindings() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private sealed class UnhostedInputSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in VisualDescendants(child))
                yield return descendant;
        }
    }
}
