using STranslate.Plugin.Tts.FishAudio.Configuration;
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
    // The host theme caches dispatcher-bound resources, so render these
    // compiled-view cases on one STA, as they are used in the application.
    internal static Task CompiledViewAudioNetworkAndFoldInteractionsAsync() => RunOnStaThreadAsync(() =>
    {
        PcmHidesWholeBitrateCardAndMp3RestoresIt();
        ReadingNetworkShowsAddressOnlyForCustomMode();
        ResultTogglesKeepIndependentViewState();
        SharedVoiceCardReflectsMetadataState();
    });

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

    private static void ReadingNetworkShowsAddressOnlyForCustomMode()
    {
        var settings = new Settings { StreamingProxyMode = "system", StreamingProxyAddress = "http://127.0.0.1:7890" };
        using var viewModel = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = CreateThemedView(viewModel);
        DrainBindings();
        var address = Descendants(view).OfType<TextBox>().Single(control =>
            BindingOperations.GetBinding(control, TextBox.TextProperty)?.Path.Path == nameof(SettingsViewModel.StreamingProxyAddress));
        var card = Ancestor<SettingsCard>(address);
        var modes = BoundComboBox(view, nameof(SettingsViewModel.StreamingProxyMode));
        AssertEqual(Visibility.Collapsed, card.Visibility, "System mode must hide the custom address setting");

        modes.SelectedItem = "custom";
        DrainBindings();
        AssertEqual(Visibility.Visible, card.Visibility, "Custom mode must reveal the complete address setting");
        AssertEqual(true, address.IsEnabled, "A revealed custom address must be editable");
        AssertEqual("http://127.0.0.1:7890", address.Text, "Changing connection mode must retain the saved custom address");
        Layout(view);
        AssertStandardCard(view, modes);
        AssertStandardCard(view, address);

        modes.SelectedItem = "direct";
        DrainBindings();
        AssertEqual(Visibility.Collapsed, card.Visibility, "Direct mode must hide the irrelevant address setting");
        AssertEqual("http://127.0.0.1:7890", settings.StreamingProxyAddress, "Hiding the address must preserve it for a later Custom selection");
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

    private static SettingsView CreateThemedView(SettingsViewModel viewModel)
    {
        var view = new SettingsView { DataContext = viewModel };
        view.Resources.MergedDictionaries.Add(new ThemeResources());
        view.Resources.MergedDictionaries.Add(new XamlControlsResources());
        AddLanguageResources(view);
        return view;
    }

    private static void Layout(FrameworkElement view)
    {
        view.Measure(new Size(600, double.PositiveInfinity));
        view.Arrange(new Rect(0, 0, 600, view.DesiredSize.Height));
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
