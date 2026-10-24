using System.Globalization;
using System.Reflection;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
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

internal static class VoiceLanguageTagsSuite
{
    private static readonly string[] Codes = ["en", "zh", "de", "ja", "fr", "es", "ko", "ar", "ru", "pt", "it", "nl", "pl", "tr", "hi", "vi", "th", "id"];

    internal static async Task RunAllAsync(string? proofDirectory = null)
    {
        if (proofDirectory is not null) SearchAdjustmentControlSuite.WriteProofAssembly(proofDirectory);
        await RunOnStaThreadAsync(() =>
        {
            var settings = new Settings { VoiceId = AppliedKey, CachedVoice = new() { Title = "Voice", Languages = new[] { "en", "zh", "de", "ja" } } };
            using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
            var view = SettingsLayoutSuite.CreateThemedView(vm);
            SettingsLayoutSuite.Layout(view, 480);
            AssertEqual(1, Visuals(view).OfType<VoiceLanguageTags>().Count(),
                "Selected compiled card must render its language metadata in the reusable control");
        }, TimeSpan.FromSeconds(15));
        await RunOnStaThreadAsync(LabelsUseExplicitCultureAndTrustedFlags);
        await RunOnStaThreadAsync(PartitionAndDictionaryReplacement);
        await RunOnStaThreadAsync(OverflowDoesNotRaiseLanguageRow);
        await RunOnStaThreadAsync(ResponsivePrefixFitsOneRowWithoutACap);
        await RunOnStaThreadAsync(FitThresholdsAndLongLabels);
        await RunOnStaThreadAsync(InheritedFontMetricsRefreshTheSameInstance);
        await RunLifetimeAsync();
        foreach (var dark in new[] { false, true })
        foreach (var dpi in new[] { 1d, 1.25, 1.5 })
        foreach (var width in new[] { 320d, 400, 480, 640 })
            await RunOnStaThreadAsync(() => RenderCompiledCards(width, dpi, dark, proofDirectory));
    }

    internal static async Task RunLifetimeAsync()
    {
        await RunOnStaThreadAsync(PopupInitializationFailureRetiresOwnership);
        await RunOnStaThreadAsync(PopupOwnsInputAndLifetime);
        await RunOnStaThreadAsync(WindowOwnerRetiresAndRepositionsPopup);
    }

    private static void PopupInitializationFailureRetiresOwnership()
    {
        foreach (var (failAfterSource, failInChrome) in new[] { (true, false), (true, true), (false, false) })
        {
            var original = new InvalidOperationException("Injected popup material initialization failure");
            var cleanup = new IOException("Injected native material cleanup failure");
            var tags = new VoiceLanguageTags(new FailingPopupBackdropApi(original, cleanup, failAfterSource, failInChrome)) { Languages = Codes };
            var scroll = new ScrollViewer { Content = tags, Height = 220 };
            AddResources(scroll, "en");
            using var source = new HwndSource(new HwndSourceParameters("Fish Audio popup initialization failure fixture")
            { Width = 400, Height = 300, WindowStyle = unchecked((int)0x80000000), PositionX = 120, PositionY = 120 });
            source.RootVisual = scroll;
            SettingsLayoutSuite.Layout(scroll, 400);
            Exception? caught = null;
            try { tags.OpenPopup(); }
            catch (Exception error) { caught = error; }
            try
            {
                if (failAfterSource)
                {
                    AssertEqual<Exception?>(null, caught, "Optional native application failure uses opaque fallback without escaping HWND creation");
                    AssertEqual(true, tags.IsPopupOpen, "A material failure preserves the usable native popup");
                    AssertEqual(true, ReferenceEquals(original, tags.Material.Failure), "The first native failure retains its identity and HRESULT");
                    AssertEqual(true, ReferenceEquals(cleanup, tags.Material.CleanupFailure), "Cleanup failure is retained separately from the primary failure");
                    AssertEqual(false, tags.Material.AttributeAccepted, "A failed application never claims accepted material");
                    AssertEqual(true, ((Border)tags.FindName("PopupSurface")).Background is SolidColorBrush { Color.A: 255 }, "Failed native application has explicit opaque flyout chrome");
                    tags.ClosePopup();
                }
                else AssertEqual(true, ReferenceEquals(original, caught), "The original pre-HWND initialization failure is preserved");
                AssertEqual(false, (bool)typeof(VoiceLanguageTags).GetField("_ownsPopupInput", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tags)!,
                    "A failed material Begin must retire input/overlay subscriptions before returning the error");
                AssertEqual(false, tags.IsPopupOpen, "A failed initialization cannot leave a native popup open");
                AssertEqual(IntPtr.Zero, tags.Material.Handle, "A failed initialization owns no stale popup source");
                AssertEqual<object?>(null, typeof(VoiceLanguageTags).GetField("_owner", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tags), "Retirement releases the per-view overlay owner before native cleanup");
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = scroll };
                InputManager.Current.ProcessInput(key);
                AssertEqual(false, key.Handled, "No retired popup input handler consumes Escape after initialization failure/close");
            }
            finally { try { tags.ClosePopup(); } catch { /* The assertions retain the primary failure. */ } }
        }
    }

    private sealed class FailingPopupBackdropApi(Exception failure, Exception cleanupFailure, bool failAfterSource, bool failInChrome) : IPopupBackdropApi
    {
        public bool IsSupported => failAfterSource ? true : throw failure;
        public PopupBackdropResult Apply(IntPtr handle, bool dark) => failInChrome ? new(0, 0, 3) : throw failure;
        public void UpdateChrome(IntPtr handle, bool dark) { if (failInChrome) throw failure; }
        public void Remove(IntPtr handle) { if (failAfterSource) throw cleanupFailure; }
    }

    internal static async Task RunProofsAsync(string directory)
    {
        SearchAdjustmentControlSuite.WriteProofAssembly(directory);
        foreach (var dark in new[] { false, true })
            await RunOnStaThreadAsync(() => RenderCompiledCards(320, 1.25, dark, directory));
    }

    private static void LabelsUseExplicitCultureAndTrustedFlags()
    {
        var original = CultureInfo.CurrentUICulture;
        var countries = new[] { "us", "cn", "de", "jp", "fr", "es", "kr", "sa", "ru", "pt", "it", "nl", "pl", "tr", "in", "vn", "th", "id" };
        for (var index = 0; index < Codes.Length; index++)
        {
            var label = VoiceLanguagePresentation.Describe(Codes[index], "en");
            AssertEqual(countries[index], label.Country, "Preview languages must use the approved flag mapping");
            AssertEqual(true, label.Flag.IsFrozen, "Embedded flag crops must be detached and frozen");
            AssertEqual(false, label.Name == label.Code, "Recognized codes must have localized names");
        }
        AssertEqual("English", VoiceLanguagePresentation.Describe("en", "en").Name, "English display name");
        AssertEqual("Ukrainian", VoiceLanguagePresentation.Describe("uk", "en").Name, "uk is a language, never a country alias");
        foreach (var (code, country) in new[] { ("uk", "ua"), ("en-gb", "gb"), ("pt-br", "br"), ("zh-hant-tw", "tw"), ("zh-hant-hk", "hk"), ("zh-hans", "cn"), ("sr-latn", "rs") })
            AssertEqual(country, VoiceLanguagePresentation.Describe(code, "en").Country, "Explicit regions and script-specific neutral cultures select their specific-culture country");
        AssertEqual(new RegionInfo(CultureInfo.CreateSpecificCulture("zh-hant").Name).TwoLetterISORegionName.ToLowerInvariant(),
            VoiceLanguagePresentation.Describe("zh-hant", "en").Country, "Script-only neutral cultures delegate to the runtime's specific-culture region");
        foreach (var code in new[] { "x-new", "../../us", "es-419" })
        {
            var label = VoiceLanguagePresentation.Describe(code, "en");
            AssertEqual<string?>(null, label.Country, "Unknown and numeric macroregions use the generic icon");
            if (code != "es-419") AssertEqual(code, label.Name, "Unknown language labels retain their normalized provider value");
        }
        AssertEqual(original, CultureInfo.CurrentUICulture, "Display localization must restore the caller's culture");
        var assembly = typeof(VoiceLanguageTags).Assembly;
        foreach (var file in new[] { "countries.png", "countries.json", "language.png" })
            AssertNotNull(assembly.GetManifestResourceInfo("STranslate.Plugin.Tts.FishAudio.Assets.Flags." + file), "Required flag data must be embedded in the plugin assembly");
    }

    private static void PartitionAndDictionaryReplacement()
    {
        var tags = new VoiceLanguageTags();
        AddResources(tags, "en");
        foreach (var count in new[] { 0, 1, 3, 4, 18 })
        {
            tags.Languages = Codes.Take(count).ToArray();
            SettingsLayoutSuite.Layout(tags, 220);
            var inline = (Panel)tags.FindName("InlinePanel");
            var popup = (LanguageWrapPanel)tags.FindName("PopupPanel");
            var visible = inline.Children.OfType<ContentControl>().Where(item => item.Content is VoiceLanguageLabel && item.Visibility == Visibility.Visible).Select(item => ((VoiceLanguageLabel)item.Content).Code).ToArray();
            AssertEnumerableEqual(Codes.Take(visible.Length), visible, "Inline languages form a provider-order prefix");
            AssertEnumerableEqual(Codes.Take(count).Skip(visible.Length), popup.Children.OfType<ContentControl>().Where(item => item.Content is VoiceLanguageLabel).Select(item => ((VoiceLanguageLabel)item.Content).Code), "The popup contains exactly all remaining languages");
            var more = (Button)tags.FindName("MoreButton");
            AssertEqual(count > visible.Length ? Visibility.Visible : Visibility.Collapsed, more.Visibility, "Remaining trigger visibility follows the exact partition");
            AssertEqual("+" + (count - visible.Length), ((TextBlock)more.Content).Text, "Remaining count excludes exactly the visible prefix");
            AssertEqual(count == 0 ? 0d : 7d, count == 0 ? tags.DesiredSize.Height : inline.Margin.Top, "Empty metadata collapses the whole row including its leading margin");
        }
        tags.Languages = ["en", "ja", "zh", "de", "x-new"];
        var names = new HashSet<string>();
        foreach (var locale in UiLocales.All)
        {
            ReplaceLocale(tags, locale);
            SettingsLayoutSuite.Layout(tags, 190);
            var inline = (Panel)tags.FindName("InlinePanel");
            var label = (VoiceLanguageLabel)inline.Children.OfType<ContentControl>().Where(item => item.Content is VoiceLanguageLabel).First().Content;
            names.Add(label.Name);
            AssertEqual(locale, tags.UiLocale.ToLowerInvariant(), "A same-instance DynamicResource marker follows dictionary replacement");
            var remaining = ((Panel)tags.FindName("PopupPanel")).Children.Count;
            AssertEqual(true, AutomationProperties.GetName((Button)tags.FindName("MoreButton")).EndsWith($"+{remaining}", StringComparison.Ordinal), "Localized automation caption includes the remaining count");
            AssertEqual("x-new", ((VoiceLanguageLabel)((LanguageWrapPanel)tags.FindName("PopupPanel")).Children.OfType<ContentControl>().Where(item => item.Content is VoiceLanguageLabel).Last().Content).Name, "Every remaining label survives locale replacement");
        }
        AssertEqual(true, names.Count >= 6, "Supported UI dictionaries visibly relocalize names without metadata changes");
    }

    private static void PopupOwnsInputAndLifetime()
    {
        var tags = new VoiceLanguageTags { Languages = Codes };
        var second = new VoiceLanguageTags { Languages = Codes };
        var outside = new Button { Content = "Outside", Height = 30 };
        var panel = new StackPanel();
        panel.Children.Add(tags); panel.Children.Add(second); panel.Children.Add(outside);
        var scroll = new ScrollViewer { Content = panel, Height = 80 };
        AddResources(scroll, "en");
        using var source = new HwndSource(new HwndSourceParameters("Fish Audio language popup fixture")
        { Width = 480, Height = 600, WindowStyle = 0, PositionX = 120, PositionY = 120 });
        source.RootVisual = scroll;
        SettingsLayoutSuite.Layout(scroll, 480);
        var more = (Button)tags.FindName("MoreButton");
        more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        AssertEqual(true, tags.IsPopupOpen, "Button activation opens a native Popup");
        var popup = (Popup)tags.FindName("LanguagePopup");
        var surface = (Border)tags.FindName("PopupSurface");
        AssertEqual(false, ReferenceEquals(PresentationSource.FromVisual(surface), source), "Popup content must use its real separate presentation source");
        var peer = UIElementAutomationPeer.CreatePeerForElement(more)!;
        var expand = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse)!;
        AssertEqual(ExpandCollapseState.Expanded, expand.ExpandCollapseState, "Automation exposes expanded state");
        foreach (var locale in UiLocales.All)
        {
            ReplaceLocale(scroll, locale); Drain();
            AssertEqual(true, tags.IsPopupOpen, "Changing all seven language dictionaries keeps open content current on the same instance");
            AssertEqual(locale, tags.UiLocale.ToLowerInvariant(), "Open Popup owner follows a live dictionary replacement");
            foreach (var pill in ((Panel)tags.FindName("PopupPanel")).Children.OfType<ContentControl>())
            {
                var label = (VoiceLanguageLabel)pill.Content;
                AssertEqual(VoiceLanguagePresentation.Describe(label.Code, tags.UiLocale).Name, label.Name, "Open remaining labels follow each explicit UI culture");
            }
        }
        SettingsLayoutSuite.Layout(scroll, 320);
        AssertEqual(true, tags.IsPopupOpen, "A responsive prefix change refreshes the open popup");
        var shown = ((Panel)tags.FindName("InlinePanel")).Children.OfType<ContentControl>().Count(item => item.Content is VoiceLanguageLabel && item.Visibility == Visibility.Visible);
        AssertEqual(Codes.Length - shown, ((Panel)tags.FindName("PopupPanel")).Children.Count, "Open popup remaining partition follows width changes");
        SettingsLayoutSuite.Layout(scroll, 4000);
        AssertEqual(false, tags.IsPopupOpen, "Removing overflow during resize closes the overlay");
        SettingsLayoutSuite.Layout(scroll, 480);
        tags.OpenPopup();
        more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        AssertEqual(false, tags.IsPopupOpen, "Clicking the same trigger toggles closed");
        tags.OpenPopup();
        AssertNotNull(surface.Background, "Native popup resolves an opaque themed background");
        var popupSource = PresentationSource.FromVisual(surface)!;
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, popupSource, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        InputManager.Current.ProcessInput(key);
        AssertEqual(false, tags.IsPopupOpen, "Escape from the Popup's presentation source closes the overlay");
        AssertEqual(true, key.Handled, "Popup Escape is consumed before parent numeric editor handling");
        AssertEqual(true, more.IsKeyboardFocused, "Escape returns focus to the surviving trigger");
        tags.OpenPopup();
        var pointer = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = more };
        InputManager.Current.ProcessInput(pointer);
        AssertEqual(true, tags.IsPopupOpen, "Same-trigger mouse-down does not pre-dismiss the overlay before Click");
        more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        AssertEqual(false, tags.IsPopupOpen, "Same-trigger down/click closes once without reopening");
        tags.OpenPopup();
        InputManager.Current.ProcessInput(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = outside });
        AssertEqual(false, tags.IsPopupOpen, "An outside pointer press closes the native Popup");
        AssertEqual(true, more.IsKeyboardFocused, "Outside dismissal does not force a new focus target");
        tags.OpenPopup();
        tags.DataContext = new object();
        AssertEqual(false, tags.IsPopupOpen, "Rebinding a card closes its old overlay");
        tags.OpenPopup(); tags.Languages = Codes.Take(3).ToArray();
        AssertEqual(false, tags.IsPopupOpen, "Removing the remaining trigger closes its overlay");
        tags.Languages = Codes; tags.OpenPopup(); tags.Visibility = Visibility.Collapsed;
        AssertEqual(false, tags.IsPopupOpen, "Hidden owners close the overlay");
        tags.Visibility = Visibility.Visible; tags.OpenPopup();
        scroll.ScrollToVerticalOffset(10); Drain();
        AssertEqual(false, tags.IsPopupOpen, "Ancestor scroll dismisses the old anchor");
        tags.Languages = Enumerable.Range(0, 105).Select(index => $"x{index}").ToArray();
        // The old 80-DIP owner cannot fit the measured localized header/chrome
        // after screen insets. Give this internal-scroll case a usable viewport;
        // fixed-chrome refusal is covered separately by the popup edge suite.
        scroll.Height = 160;
        SettingsLayoutSuite.Layout(scroll, 480); tags.OpenPopup(); Drain();
        AssertEqual(true, tags.IsPopupOpen, "The internal-scroll case opens within a viewport that fits measured fixed chrome");
        var popupScroller = Visuals(surface).OfType<ScrollViewer>().Single();
        AssertEqual(true, popupScroller.ViewportHeight > 0 && popupScroller.ViewportHeight <= 280,
            "A large remaining list is bounded by 280 DIP and the actual short visible viewport after fixed chrome");
        AssertEqual(true, popupScroller.ScrollableHeight > 0, "All remaining languages stay accessible through vertical scrolling");
        SearchAdjustmentControlSuite.AssertScrollbarExclusion(popupScroller,
            ((Panel)tags.FindName("PopupPanel")).Children.OfType<FrameworkElement>(), "remaining language pills");
        popupScroller.ScrollToVerticalOffset(20); Drain();
        AssertEqual(true, tags.IsPopupOpen, "Scrolling the popup itself does not dismiss its remaining languages");
        tags.OpenPopup();
        panel.Children.Remove(tags); Drain();
        AssertEqual(false, tags.IsPopupOpen, "Unloaded/replaced cards close their overlay");
        AssertEqual(false, popup.IsOpen, "Native popup is closed after lifecycle retirement");
    }

    private static void WindowOwnerRetiresAndRepositionsPopup()
    {
        var tags = new VoiceLanguageTags { Languages = Codes };
        AddResources(tags, "en");
        // Visible-viewport containment intentionally refuses an off-monitor owner.
        var window = new Window { Content = tags, Width = 400, Height = 240, Left = 120, Top = 120, Opacity = 0,
            ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
        try
        {
            window.Show(); Drain(); tags.OpenPopup();
            AssertEqual(true, tags.IsPopupOpen, "A native Window owns the overlay");
            window.Left += 10; window.Width = 480; Drain();
            AssertEqual(true, tags.IsPopupOpen, "Window move/resize repositions the current overlay without losing ownership");
            window.Hide(); Drain();
            AssertEqual(false, tags.IsPopupOpen, "Hiding a native owner retires its overlay");
            window.Show(); Drain(); tags.OpenPopup();
            typeof(Window).GetMethod("OnDeactivated", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [EventArgs.Empty]);
            AssertEqual(false, tags.IsPopupOpen, "Owner deactivation closes the overlay when another application receives focus");
        }
        finally { tags.ClosePopup(); window.Close(); }
    }

    private static void OverflowDoesNotRaiseLanguageRow()
    {
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var card = new VoiceSearchItem { Id = AppliedKey, Title = "Same card", Description = "Same description", AuthorName = "Same author", Languages = Codes.Take(3).ToArray() };
        vm.ByIdResults = [card]; vm.BrowseMode = VoiceBrowseMode.ById;
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        SettingsLayoutSuite.Layout(view, 640);
        var tag = Visuals(view).OfType<VoiceLanguageTags>().Single(item => ReferenceEquals(item.DataContext, card));
        var author = Visuals(view).OfType<TextBlock>().Single(item => item.Text == "Same author");
        var beforeHeight = tag.ActualHeight;
        var beforeAuthor = author.TranslatePoint(new Point(), CardBorder(tag)).Y;
        card.Languages = Codes.Take(5).ToArray();
        SettingsLayoutSuite.Layout(view, 640);
        AssertEqual(beforeHeight, tag.ActualHeight, "A same-line +N trigger must not increase the language row height");
        AssertEqual(beforeAuthor, author.TranslatePoint(new Point(), CardBorder(tag)).Y, "Adding a same-line +N preserves the author baseline");
    }

    private static void ResponsivePrefixFitsOneRowWithoutACap()
    {
        var tags = new VoiceLanguageTags { Languages = Codes.Take(5).ToArray() };
        AddResources(tags, "en");
        foreach (var (width, expected) in new[] { (640d, 5), (300d, 3), (220d, 2), (150d, 1), (80d, 1) })
        {
            SettingsLayoutSuite.Layout(tags, width);
            var inline = (Panel)tags.FindName("InlinePanel");
            var visible = inline.Children.OfType<ContentControl>().Where(item => item.Content is VoiceLanguageLabel && item.Visibility == Visibility.Visible).ToArray();
            AssertEqual(expected, visible.Length, "The largest fitting prefix is responsive and has no three-language cap");
            AssertEqual(32.5, tags.ActualHeight, "Every nonempty language row has the same single-line height including its existing margins");
            AssertEnumerableEqual(Codes.Take(5).Skip(expected), ((Panel)tags.FindName("PopupPanel")).Children.OfType<ContentControl>().Select(item => ((VoiceLanguageLabel)item.Content).Code), "Responsive inline and remaining partitions have neither overlap nor omissions");
            AssertEqual(expected < 5 ? Visibility.Visible : Visibility.Collapsed, ((Button)tags.FindName("MoreButton")).Visibility, "Overflow appears only for actual remaining languages");
        }
    }

    private static void FitThresholdsAndLongLabels()
    {
        var tags = new VoiceLanguageTags { Languages = Enumerable.Range(0, 105).Select(index => $"x{index}").ToArray() };
        AddResources(tags, "en");
        foreach (var dpi in new[] { 1d, 1.25, 1.5 })
        {
            typeof(VisualTreeHelper).GetMethod("SetRootDpi", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [tags, new DpiScale(dpi, dpi)]);
            SettingsLayoutSuite.Layout(tags, 10000);
            var panel = (Panel)tags.FindName("InlinePanel");
            var pills = panel.Children.OfType<ContentControl>().Where(item => item.Content is VoiceLanguageLabel).ToArray();
            var more = (Button)tags.FindName("MoreButton");
            foreach (var count in new[] { 5, 6 })
            {
                more.Visibility = Visibility.Visible; ((TextBlock)more.Content).Text = $"+{105 - count}";
                more.ApplyTemplate();
                ((TextBlock)more.Content).InvalidateMeasure();
                var buttonSurface = (Border)more.Template.FindName("Surface", more);
                buttonSurface.Child.InvalidateMeasure(); buttonSurface.InvalidateMeasure(); more.InvalidateMeasure();
                more.Measure(new Size(double.PositiveInfinity, 20.5));
                var threshold = pills.Take(count).Sum(item => item.Width) + 4 * count + more.DesiredSize.Width;
                foreach (var (delta, expected) in new[] { (-.05, count - 1), (0d, count), (.05, count) })
                {
                    for (var repeat = 0; repeat < 2; repeat++)
                    {
                        SettingsLayoutSuite.Layout(tags, threshold + delta);
                        if (expected != pills.Count(item => item.Visibility == Visibility.Visible))
                        {
                            var actualWidths = (double[])typeof(VoiceLanguageTags).GetField("_naturalWidths", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tags)!;
                            var actualMore = (double)typeof(VoiceLanguageTags).GetMethod("MeasureOverflow", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(tags, [105 - count])!;
                            Console.WriteLine($"Threshold diagnostic W={tags.ActualWidth:R}, desired={tags.DesiredSize.Width:R}, rounding={tags.UseLayoutRounding}, dpi={VisualTreeHelper.GetDpi(tags).DpiScaleX:R}, sum={actualWidths.Take(count).Sum():R}, more={actualMore:R}, oracle={threshold:R}");
                        }
                        AssertEqual(expected, pills.Count(item => item.Visibility == Visibility.Visible), $"Measured fit threshold is maximal, stable and respects the actual +99/+100 digit boundary ({dpi}/{count}/{threshold:R}/{delta})");
                        AssertEqual($"+{105 - expected}", ((TextBlock)more.Content).Text, "Each candidate reserves its own remaining-count button");
                        AssertEqual(32.5, tags.ActualHeight, "Threshold changes never wrap or stretch the row");
                    }
                }
            }
        }
        foreach (var code in new[] { "es-419", "x-very-long-language-name-with-no-country" })
        {
            tags.Languages = [code, "en"];
            SettingsLayoutSuite.Layout(tags, 80);
            var pill = ((Panel)tags.FindName("InlinePanel")).Children.OfType<ContentControl>().Single(item => item.Content is VoiceLanguageLabel && item.Visibility == Visibility.Visible);
            var label = (VoiceLanguageLabel)pill.Content;
            AssertEqual(true, ((string)pill.ToolTip).Contains(label.Name, StringComparison.Ordinal), "Ellipsized names retain full tooltip text");
            AssertEqual($"{label.Name} ({code})", AutomationProperties.GetName(pill), "Ellipsized names retain full automation text and code");
            AssertEqual(true, Visuals(pill).OfType<LanguageFlagImage>().Single().ActualWidth == 14, "The fallback preserves the full flag/icon");
            AssertEqual("+1", ((TextBlock)((Button)tags.FindName("MoreButton")).Content).Text, "Long first names retain exact remaining access");
        }
    }

    private static void InheritedFontMetricsRefreshTheSameInstance()
    {
        const double width = 300;
        var tags = new VoiceLanguageTags { Languages = Codes };
        var host = new ContentControl { Content = tags, FontFamily = new FontFamily("Arial") };
        AddResources(host, "en");
        SettingsLayoutSuite.Layout(host, width);
        foreach (var change in new Action<Control>[]
        {
            control => control.FontFamily = new FontFamily("Consolas"),
            control => control.FontWeight = FontWeights.Bold,
            control => control.FontStyle = FontStyles.Italic,
            control => control.FontStretch = FontStretches.Expanded,
            control => { control.FontFamily = new FontFamily("Arial"); control.FontWeight = FontWeights.Normal; control.FontStyle = FontStyles.Normal; control.FontStretch = FontStretches.Normal; },
        })
        {
            change(host);
            SettingsLayoutSuite.Layout(host, width);
            var fresh = new VoiceLanguageTags { Languages = Codes };
            var reference = new ContentControl { Content = fresh, FontFamily = host.FontFamily, FontWeight = host.FontWeight,
                FontStyle = host.FontStyle, FontStretch = host.FontStretch };
            AddResources(reference, "en");
            SettingsLayoutSuite.Layout(reference, width);
            var visible = InlinePills(tags);
            var expected = InlinePills(fresh);
            AssertEqual(expected.Length, visible.Length, "Inherited font changes recompute the maximal prefix on the same instance");
            for (var index = 0; index < visible.Length; index++)
            {
                AssertEqual(expected[index].Width, visible[index].Width, "Inherited font metrics refresh each natural capsule width");
                if (visible.Length <= 1) continue;
                var text = Visuals(visible[index]).OfType<TextBlock>().Single();
                var full = new TextBlock { Text = text.Text, FontSize = text.FontSize, FontFamily = text.FontFamily,
                    FontWeight = text.FontWeight, FontStyle = text.FontStyle, FontStretch = text.FontStretch };
                typeof(VisualTreeHelper).GetMethod("SetRootDpi", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [full, VisualTreeHelper.GetDpi(text)]);
                full.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                AssertEqual(true, text.ActualWidth + .01 >= full.DesiredSize.Width, "Changed-font multi-pill names retain their full text viewport");
                AssertEqual(false, HasEllipsis(VisualTreeHelper.GetDrawing(text)), "Changed-font fitting prefixes draw every full name");
            }
            var suffix = ((Panel)tags.FindName("PopupPanel")).Children.OfType<ContentControl>().Select(item => ((VoiceLanguageLabel)item.Content).Code);
            AssertEnumerableEqual(Codes, visible.Select(item => ((VoiceLanguageLabel)item.Content).Code).Concat(suffix), "Font changes preserve exact inline/popup union and provider order");
        }
    }

    private static ContentControl[] InlinePills(VoiceLanguageTags tags) => ((Panel)tags.FindName("InlinePanel")).Children
        .OfType<ContentControl>().Where(item => item.Content is VoiceLanguageLabel && item.Visibility == Visibility.Visible).ToArray();

    private static void RenderCompiledCards(double width, double dpi, bool dark, string? proofDirectory)
    {
        var settings = new Settings { VoiceId = AppliedKey, CachedVoice = new() { Title = "Nora · Warm and Gentle Audiobook Narration Voice", Description = "Soft and clear, for audiobooks and everyday conversation.", AuthorName = "Nora Studio", Languages = Codes, SampleAudioUrl = "https://example.invalid/sample.mp3" } };
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var localCover = FindRepoFile("STranslate.Plugin.Tts.FishAudio/Assets/Flags/language.png");
        vm.CachedVoiceCoverUrl = localCover;
        var card = new VoiceSearchItem { Id = AppliedKey, Title = settings.CachedVoice.Title!, Description = settings.CachedVoice.Description!, AuthorName = "Nora Studio", Languages = Codes, CoverUrl = localCover, SampleAudioUrl = "https://example.invalid/sample.mp3" };
        vm.SearchResults = [card]; vm.ByIdResults = [card]; vm.FavoriteResults = [card]; vm.HasSearched = true;
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        view.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        view.SetResourceReference(Control.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light);
        var desktopScale = VisualTreeHelper.GetDpi(new DrawingVisual());
        using var source = new HwndSource(new HwndSourceParameters("Fish Audio rendered language Popup fixture")
        {
            Width = (int)Math.Ceiling(width * desktopScale.DpiScaleX), Height = (int)Math.Ceiling(1200 * desktopScale.DpiScaleY),
            WindowStyle = unchecked((int)0x80000000), PositionX = 120, PositionY = 120,
        });
        // Keep the entire render phase under a finite native client allocation.
        // Inherited-font invalidations can otherwise let an unhosted root receive
        // an automatic infinite-width layout during the dispatcher drain.
        source.RootVisual = view;
        typeof(VisualTreeHelper).GetMethod("SetRootDpi", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [view, new DpiScale(dpi, dpi)]);
        foreach (var mode in new[] { VoiceBrowseMode.Search, VoiceBrowseMode.ById, VoiceBrowseMode.Favorites })
        {
            vm.BrowseMode = mode;
            SettingsLayoutSuite.Layout(view, width);
            var tags = Visuals(view).OfType<VoiceLanguageTags>().Where(IsPresented).ToArray();
            AssertEqual(2, tags.Length, "Selected and every active browsing mode use the same language-row contract");
            var selectedTags = tags.Single(item => ReferenceEquals(item.DataContext, vm));
            var selectedCard = CardBorder(selectedTags);
            AssertEqual(56d, Visuals(selectedCard).OfType<System.Windows.Shapes.Ellipse>().Single(item => item.Fill is ImageBrush).ActualWidth,
                "Selected-card fixture reserves the real cover geometry using a local resource");
            AssertEqual(56d, Visuals(selectedCard).OfType<Button>().Single(item => ReferenceEquals(item.Command, vm.ToggleDisplayPreviewCommand)).ActualWidth,
                "Selected-card fixture reserves the visible preview action without playing audio");
            AssertEqual(true, Math.Abs(selectedTags.ActualWidth - (selectedCard.ActualWidth - 94)) <= 1,
                "The selected metadata spans the full column after 56-DIP cover, gap, border and padding");
            foreach (var tag in tags)
            {
                var inline = (Panel)tag.FindName("InlinePanel");
                AssertEqual(true, double.IsFinite(tag.ActualWidth) && tag.ActualWidth > 0, "Content column has a finite positive width");
                foreach (FrameworkElement child in inline.Children)
                {
                    if (child.Visibility != Visibility.Visible) continue;
                    var point = child.TranslatePoint(new Point(), inline);
                    AssertEqual(true, point.X >= -.1 && point.X + child.ActualWidth <= inline.ActualWidth + .1, "Every flag pill and +N stays within the content column");
                }
                var visiblePills = inline.Children.OfType<ContentControl>().Where(item => item.Content is VoiceLanguageLabel && item.Visibility == Visibility.Visible).ToArray();
                if (visiblePills.Length > 1)
                    foreach (var pill in visiblePills)
                    {
                        var label = Visuals(pill).OfType<TextBlock>().Single();
                        var full = new TextBlock { Text = label.Text, FontFamily = label.FontFamily, FontSize = label.FontSize };
                        typeof(VisualTreeHelper).GetMethod("SetRootDpi", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [full, VisualTreeHelper.GetDpi(label)]);
                        full.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        AssertEqual(true, label.ActualWidth + .01 >= full.DesiredSize.Width,
                            $"Every multi-pill prefix retains full localized names ({width}/{dpi}: {label.Text}, viewport={label.ActualWidth}, full={full.DesiredSize.Width}, pill={pill.Width})");
                        AssertEqual(false, HasEllipsis(VisualTreeHelper.GetDrawing(label)),
                            $"A fitting multi-pill label must not draw an ellipsis ({width}/{dpi}: {label.Text}, viewport={label.ActualWidth}, full={full.DesiredSize.Width}, pill={pill.Width})");
                    }
            }
            var commands = Visuals(view).OfType<Button>().Where(button => IsPresented(button) && ReferenceEquals(button.Command, vm.SelectVoiceCommand)).ToArray();
            AssertEqual(1, commands.Length, "Language pills preserve the existing selection action");
            AssertEqual(32d, commands[0].ActualWidth, "Selection geometry remains unchanged");
            if (proofDirectory is not null && mode == VoiceBrowseMode.ById)
            {
                Save(view, Path.Combine(proofDirectory, $"cards-{width:0}-{dpi * 100:0}-{(dark ? "dark" : "light")}.png"), dpi);
                for (var index = 0; index < tags.Length; index++)
                {
                    var outer = CardBorder(tags[index]);
                    Save(outer, Path.Combine(proofDirectory, $"card-{index}-{width:0}-{dpi * 100:0}-{(dark ? "dark" : "light")}.png"), dpi);
                }
            }
        }
        vm.BrowseMode = VoiceBrowseMode.ById;
        SettingsLayoutSuite.Layout(view, width);
        var nativeTags = Visuals(view).OfType<VoiceLanguageTags>().Where(IsPresented).ToArray();
        nativeTags[0].OpenPopup();
        AssertEqual(true, nativeTags[0].IsPopupOpen, "Selected card trigger opens its native Popup");
        nativeTags[1].OpenPopup();
        AssertEqual(false, nativeTags[0].IsPopupOpen, "Only one overlay is owned by a settings view");
        AssertEqual(true, nativeTags[1].IsPopupOpen, "The second card transfers overlay ownership");
        var nativeSurface = (Border)nativeTags[1].FindName("PopupSurface");
        Drain(); nativeSurface.UpdateLayout();
        AssertEqual(290d, nativeSurface.ActualWidth, "The native popup retains its approved 290 DIP body");
        var nativePopupSource = (HwndSource)PresentationSource.FromVisual(nativeSurface);
        var material = nativeTags[1].Material;
        Console.WriteLine($"[popup-material/languages] hwnd=0x{nativePopupSource.Handle:X}; owner=0x{source.Handle:X}; layered={nativePopupSource.UsesPerPixelOpacity}; frameHR=0x{material.Result.FrameHResult:X8}; readHR=0x{material.Result.ReadbackHResult:X8}; type={material.Result.BackdropType}; accepted={material.AttributeAccepted}; ownerRequestedDpi={dpi:0.##}; popupActualDpi={VisualTreeHelper.GetDpi(nativeSurface).DpiScaleX:0.##}; visibleBlur=UNVERIFIED");
        var nativeViewer = (ScrollViewer)nativeTags[1].FindName("PopupScroll");
        if (nativeViewer.ScrollableHeight > 0)
            SearchAdjustmentControlSuite.AssertScrollbarExclusion(nativeViewer,
                ((Panel)nativeTags[1].FindName("PopupPanel")).Children.OfType<FrameworkElement>(), "compiled remaining suffix");
        if (proofDirectory is not null)
            Save(nativeSurface, Path.Combine(proofDirectory, $"popup-{width:0}-{dpi * 100:0}-{(dark ? "dark" : "light")}.png"), dpi);
        var origin = nativeSurface.PointToScreen(new Point());
        var ownerOrigin = view.PointToScreen(new Point());
        var right = nativeSurface.PointToScreen(new Point(nativeSurface.ActualWidth, 0)).X;
        var ownerRight = view.PointToScreen(new Point(view.ActualWidth, 0)).X;
        AssertEqual(true, origin.X >= ownerOrigin.X + 7 && right <= ownerRight - 7,
            $"Custom native placement contains the Popup horizontally inside the settings viewport ({width}/{dpi}: popup {origin.X}..{right}, view {ownerOrigin.X}..{ownerRight})");
        nativeTags[1].ClosePopup();
        var chosen = vm.CachedVoiceId;
        AssertEqual(AppliedKey, chosen, "Language overlay actions never apply or clear a voice");
    }

    private static void AddResources(FrameworkElement root, string locale)
    {
        root.Resources.MergedDictionaries.Add(new ThemeResources { RequestedTheme = ApplicationTheme.Light });
        root.Resources.MergedDictionaries.Add(new XamlControlsResources());
        ThemeManager.SetRequestedTheme(root, ElementTheme.Light);
        ReplaceLocale(root, locale);
    }
    private static void ReplaceLocale(FrameworkElement root, string locale)
    {
        var previous = root.Resources.MergedDictionaries.SingleOrDefault(item => item.Source?.LocalPath.Replace('\\', '/').Contains("/Languages/", StringComparison.Ordinal) == true);
        if (previous is not null) root.Resources.MergedDictionaries.Remove(previous);
        root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(FindRepoFile($"STranslate.Plugin.Tts.FishAudio/Languages/{locale}.xaml"), UriKind.Absolute) });
    }
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    private static bool IsPresented(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }
    private static void Save(FrameworkElement view, string path, double dpi)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * dpi), (int)Math.Ceiling(view.ActualHeight * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle((Brush?)view.TryFindResource("SolidBackgroundFillColorBaseBrush") ?? Brushes.White, null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
            context.DrawRectangle(new VisualBrush(view) { Stretch = Stretch.Fill }, null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
        }
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }
    private static Border CardBorder(DependencyObject element)
    {
        for (var current = VisualTreeHelper.GetParent(element); current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is Border { Child: DockPanel } border) return border;
        throw new InvalidOperationException("Language control belongs to a real compiled voice card");
    }
    private static bool HasEllipsis(Drawing? drawing) => drawing switch
    {
        DrawingGroup group => group.Children.Any(HasEllipsis),
        GlyphRunDrawing glyph => glyph.GlyphRun.GlyphTypeface.CharacterToGlyphMap.TryGetValue(0x2026, out var ellipsis)
            && glyph.GlyphRun.GlyphIndices.Contains(ellipsis),
        _ => false,
    };

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }
}
