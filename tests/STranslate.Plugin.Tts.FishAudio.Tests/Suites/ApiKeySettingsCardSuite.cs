using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Controls.Helpers;
using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static StaTestHost;
using static TestAssertions;

internal static class ApiKeySettingsCardSuite
{
    private static readonly double[] Widths = [400, 475, 476, 500, 600, 800, 1200];
    private static readonly string[] Drafts = ["", new('a', 32), "sk-fish-" + new string('a', 43), new('a', 1000)];

    internal static async Task RunAllAsync()
    {
        foreach (var draft in Drafts)
        {
            Console.WriteLine($"[api-key-layout] width and saturation matrix, draft length {draft.Length}.");
            await RunOnStaThreadAsync(() => WidthMatrix(draft));
        }
        Console.WriteLine("[api-key-layout] controlled smaller password metrics.");
        await RunOnStaThreadAsync(() => WidthMatrix(Drafts[1], 12, requireWrappedSaturation: true));
        Console.WriteLine("[api-key-layout] controlled larger password metrics.");
        await RunOnStaThreadAsync(() => WidthMatrix(Drafts[2], 16));
        foreach (var locale in UiLocales.All)
        {
            Console.WriteLine($"[api-key-layout] localized header {locale}.");
            await RunOnStaThreadAsync(() => LocalizedHeaders(locale));
        }
        foreach (var width in new[] { 400d, 476d, 800d })
        {
            Console.WriteLine($"[api-key-layout] template lifecycle at {width} DIP.");
            await RunOnStaThreadAsync(() => TemplateLifecycle(width));
        }
    }

    private static void WidthMatrix(string draft, double? passwordFontSize = null, bool requireWrappedSaturation = false)
    {
        using var fixture = new Fixture(draft);
        if (passwordFontSize is { } fontSize)
        {
            fixture.Password.FontSize = fontSize;
            fixture.PeerPassword.FontSize = fontSize;
        }
        fixture.Layout(1200);
        var natural = fixture.PeerPassword.ActualWidth;
        foreach (var width in Widths.Concat(Widths.Reverse()))
        {
            fixture.Layout(width);
            fixture.AssertGeometry($"draft length {draft.Length}, width {width}");
            if (draft.Length < 1000 && (width <= 475 || width >= 800))
                fixture.AssertNativeGeometry($"draft length {draft.Length}, width {width}");
        }
        if (draft.Length is 0 or 1000) return;

        // Derive the saturation point from native wide geometry, never from a
        // production width constant. Probe fractional increments on both sides.
        fixture.Layout(1200);
        var saturation = 1200 - (fixture.AvailableInputWidth - natural);
        var samples = Enumerable.Range(-8, 17).Select(i => saturation + i * .25).ToArray();
        var constrainedSamples = 0;
        var naturalSamples = 0;
        var wrappedSamples = 0;
        foreach (var width in samples.Concat(samples.Reverse()))
        {
            fixture.Layout(width);
            var context = $"draft={draft.Length}, font={fixture.Password.FontSize:F2}, natural={natural:F3}, saturation={saturation:F3}, sample={width:F3}, native={fixture.State(fixture.Peer)}, scoped={fixture.State(fixture.Card)}, slot={fixture.AvailableInputWidth:F3}";
            fixture.AssertGeometry(context);
            if (fixture.State(fixture.Peer) == "Right")
            {
                Near(Math.Min(natural, fixture.AvailableInputWidth), fixture.Password.ActualWidth, $"{context}: horizontal field must fill available room then saturate at native natural width");
                if (fixture.AvailableInputWidth < natural - .1) constrainedSamples++;
                if (fixture.AvailableInputWidth >= natural) naturalSamples++;
            }
            else
            {
                AssertEqual("RightWrapped", fixture.State(fixture.Peer), $"{context}: nonhorizontal saturation samples must use the native wrapped state");
                fixture.AssertNativeGeometry(context);
                wrappedSamples++;
            }
        }
        // Font metrics can move a legacy key's entire saturation neighborhood
        // below the native transition. Prefixed keys and the larger-font peer
        // must still exercise both real horizontal regimes in both directions.
        if (draft.Length == 51)
        {
            AssertEqual(true, constrainedSamples > 0, $"font {fixture.Password.FontSize}: saturation samples must include constrained native Right input");
            AssertEqual(true, naturalSamples > 0, $"font {fixture.Password.FontSize}: saturation samples must include natural-width native Right input");
        }
        if (requireWrappedSaturation)
            AssertEqual(true, wrappedSamples > 0, "The smaller-font regression must exercise saturation samples in the native wrapped state");
        fixture.Layout(1200);
        Near(natural, fixture.Password.ActualWidth, "Growth must return to the same native natural width");
        Console.WriteLine($"[api-key-layout] draft length {draft.Length}, font {fixture.Password.FontSize:F2}: native natural width {natural:F2}, saturation at {saturation:F2} DIP, quarter-DIP grow/shrink samples passed (Right constrained={constrainedSamples}, natural={naturalSamples}; wrapped={wrappedSamples}).");
    }

    private static void LocalizedHeaders(string locale)
    {
        using var fixture = new Fixture(Drafts[2], locale);
        foreach (var fontSize in new[] { fixture.Header.FontSize, 20d })
        {
            fixture.Header.FontSize = fontSize;
            fixture.PeerHeader.FontSize = fontSize;
            foreach (var width in new[] { 476d, 500d, 476d })
            {
                fixture.Layout(width);
                fixture.AssertGeometry($"{locale}/font {fontSize}/{width}");
            }
        }
    }

    private static void TemplateLifecycle(double width)
    {
        using var first = new Fixture(Drafts[2], initialWidth: width);
        using var second = new Fixture(Drafts[1], initialWidth: width);
        first.Layout(width);
        second.Layout(width);
        var initialChildren = first.RightStoryboardChildren;
        var secondChildren = second.RightStoryboardChildren;
        for (var iteration = 0; iteration < 3; iteration++)
        {
            first.Card.OnApplyTemplate();
            first.Layout(width);
            first.AssertGeometry($"repeated apply/{width}/{iteration}");
            AssertEqual(initialChildren, first.RightStoryboardChildren, "Repeated application must not accumulate animation resources");
            var oldTemplate = first.Card.Template;
            first.Card.Template = null;
            first.Card.ApplyTemplate();
            first.Card.Template = oldTemplate;
            first.Layout(width, notifyLoaded: false);
            first.AssertGeometry($"template replacement/{width}/{iteration}");
            AssertEqual(initialChildren, first.RightStoryboardChildren, "Fresh native roots must have the same bounded animation resources");
            second.Layout(width);
            second.AssertGeometry($"isolated second instance/{width}/{iteration}");
            AssertEqual(secondChildren, second.RightStoryboardChildren, "Replacing one card must not mutate another card's animations");
            AssertEqual(first.State(first.Peer), first.State(first.Card), "Replacing a template at fixed width must retain native state");
        }
        foreach (var next in Widths.Concat(Widths.Reverse()))
        {
            first.Layout(next);
            second.Layout(next);
            first.AssertGeometry($"replaced resize/{next}");
            second.AssertGeometry($"peer resize/{next}");
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SettingsViewModel _vm;
        private readonly SettingsView _view;
        private readonly Grid _peerHost;
        private readonly string _draft;
        internal SettingsCard Card { get; }
        internal SettingsCard Peer { get; }
        internal PasswordBox Password { get; }
        internal PasswordBox PeerPassword { get; }
        internal TextBlock Header => Visuals(Card).OfType<TextBlock>().First(t => t.Text == Card.Header?.ToString());
        internal TextBlock PeerHeader => Visuals(Peer).OfType<TextBlock>().First(t => t.Text == Peer.Header?.ToString());
        internal double AvailableInputWidth
        {
            get
            {
                var presenter = (FrameworkElement)VisualTreeHelper.GetParent((DependencyObject)Card.Content);
                return LayoutInformation.GetLayoutSlot(presenter).Width - presenter.Margin.Left - presenter.Margin.Right;
            }
        }
        internal int RightStoryboardChildren => Group(Card).States.OfType<VisualState>().Single(state => state.Name == "Right").Storyboard?.Children.Count ?? 0;

        internal Fixture(string draft, string locale = "en", double initialWidth = 1200)
        {
            _draft = draft;
            var settings = new Settings();
            _vm = new SettingsViewModel(CreateContext(settings: settings), settings);
            _view = SettingsLayoutSuite.CreateThemedView(_vm);
            _view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(FindRepoFile($"STranslate.Plugin.Tts.FishAudio/Languages/{locale}.xaml"), UriKind.Absolute) });
            Password = Logical(_view).OfType<PasswordBox>().Single(p => BindingOperations.GetBinding(p, PasswordBoxAssistant.PasswordProperty)?.Path.Path == "ApiKey");
            Card = Ancestor<SettingsCard>(Password);
            SettingsLayoutSuite.Layout(_view, initialWidth);
            Password.Password = draft;
            // Match the compiled card slots, leaving the peer's native
            // template and visual states completely unmodified.
            Peer = new SettingsCard
            {
                Header = Card.Header,
                Description = new HyperlinkButton { Content = "fish.audio", NavigateUri = new Uri("https://fish.audio/app/api-keys") },
                HeaderIcon = new FontIcon { Glyph = ((FontIcon)Card.HeaderIcon).Glyph, FontFamily = ((FontIcon)Card.HeaderIcon).FontFamily, FontSize = ((FontIcon)Card.HeaderIcon).FontSize },
                Content = NativeInput(Password),
                Margin = Card.Margin,
            };
            _peerHost = new Grid { Resources = _view.Resources };
            _peerHost.Children.Add(Peer);
            PeerPassword = Logical(Peer).OfType<PasswordBox>().Single();
            PeerPassword.Password = draft;
            Layout(initialWidth);
        }

        private static Grid NativeInput(PasswordBox source)
        {
            var password = new PasswordBox { MinWidth = 230, Padding = new Thickness(10, 4, 40, 4) };
            password.SetValue(ControlHelper.PlaceholderTextProperty, source.GetValue(ControlHelper.PlaceholderTextProperty));
            password.SetValue(TextBoxHelper.IsDeleteButtonVisibleProperty, false);
            password.SetValue(PasswordBoxHelper.PasswordRevealModeProperty, source.GetValue(PasswordBoxHelper.PasswordRevealModeProperty));
            var paste = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                Width = 28, Height = 28, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(6),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Content = new TextBlock { Text = "\uE77F", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14 },
            };
            var content = new Grid();
            content.Children.Add(password);
            content.Children.Add(paste);
            return content;
        }

        internal void Layout(double width, bool notifyLoaded = true)
        {
            SettingsLayoutSuite.Layout(_view, width);
            SettingsLayoutSuite.Layout(_peerHost, Card.ActualWidth + Peer.Margin.Left + Peer.Margin.Right);
            // Use the native Loaded handler and dispatcher work to select the
            // native responsive state in these offscreen compiled fixtures.
            if (notifyLoaded)
            {
                Card.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Peer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            }
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            _view.UpdateLayout();
            _peerHost.UpdateLayout();
        }

        private static VisualStateGroup Group(SettingsCard card)
        {
            var border = (FrameworkElement)card.Template.FindName("PART_RootBorder", card);
            return VisualStateManager.GetVisualStateGroups(border).OfType<VisualStateGroup>().Single(g => g.Name == "ContentAlignmentStates");
        }

        internal string? State(SettingsCard card) => Group(card).CurrentState?.Name;

        internal void AssertNativeGeometry(string context)
        {
            Near(PeerPassword.ActualWidth, Password.ActualWidth, $"{context}: native field width must survive");
            Near(Right(PeerPassword, Peer), Right(Password, Card), $"{context}: native field right edge must survive");
            foreach (var (element, peer) in new (FrameworkElement, FrameworkElement)[]
            {
                (Header, PeerHeader),
                ((FrameworkElement)Card.Description, (FrameworkElement)Peer.Description),
                ((FrameworkElement)Card.HeaderIcon, (FrameworkElement)Peer.HeaderIcon),
            })
            {
                Near(Left(peer, Peer), Left(element, Card), $"{context}: native header/link/icon left edge must survive");
                Near(peer.TranslatePoint(new Point(), Peer).Y, element.TranslatePoint(new Point(), Card).Y, $"{context}: native header/link/icon top edge must survive");
                Near(peer.ActualWidth, element.ActualWidth, $"{context}: native header/link/icon width must survive");
                Near(peer.ActualHeight, element.ActualHeight, $"{context}: native header/link/icon height must survive");
            }
        }

        internal void AssertGeometry(string context)
        {
            AssertEqual(State(Peer), State(Card), $"{context}: native state choices must be preserved");
            AssertEqual(true, State(Card) is not null, $"{context}: fixture must enter a real native state");
            foreach (var element in new FrameworkElement[] { Header, (FrameworkElement)Card.Description, (FrameworkElement)Card.HeaderIcon })
            {
                element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                AssertEqual(true, element.ActualWidth + .1 >= element.DesiredSize.Width,
                    $"{context}: complete header/link/icon must fit (actual {element.ActualWidth:F2}, desired {element.DesiredSize.Width:F2})");
                AssertEqual(true, Left(element, Card) >= 0 && Right(element, Card) <= Card.ActualWidth + .1,
                    $"{context}: header/link/icon must stay inside card");
                if (State(Card) == "Right")
                    AssertEqual(true, Right(element, Card) <= Left(Password, Card) + .1, $"{context}: complete header/link/icon must end before input");
            }
            AssertEqual(true, Left(Password, Card) >= 0 && Right(Password, Card) <= Card.ActualWidth + .1,
                $"{context}: input must stay inside card (right {Right(Password, Card):F2}, card {Card.ActualWidth:F2})");
            var paste = Logical((DependencyObject)Card.Content).OfType<Button>().Single();
            AssertEqual(true, Left(paste, Password) >= 0 && Right(paste, Password) <= Password.ActualWidth + .1, $"{context}: Paste must stay inside input");
            var host = (FrameworkElement)Password.Template.FindName("PART_ContentHost", Password);
            var viewport = Visuals(host).OfType<ScrollContentPresenter>().Single();
            AssertEqual(true, Right(viewport, Password) <= Left(paste, Password) + .1, $"{context}: real viewport must end before Paste");
            AssertEqual(_draft, Password.Password, $"{context}: layout must preserve the entire password draft");
            AssertEqual(_draft, _vm.ApiKey, $"{context}: immediate binding must retain the entire draft");
        }

        public void Dispose() => _vm.Dispose();
    }

    private static void Near(double expected, double actual, string message) => AssertEqual(true, Math.Abs(expected - actual) <= .1, $"{message} (expected {expected:F3}, actual {actual:F3})");
    private static double Left(FrameworkElement element, Visual target) => element.TranslatePoint(new Point(), (UIElement)target).X;
    private static double Right(FrameworkElement element, Visual target) => element.TranslatePoint(new Point(element.ActualWidth, 0), (UIElement)target).X;
    private static T Ancestor<T>(DependencyObject element) where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent) ?? LogicalTreeHelper.GetParent(parent))
            if (parent is T result) return result;
        throw new InvalidOperationException($"Missing {typeof(T).Name} ancestor");
    }
    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) { yield return child; foreach (var item in Logical(child)) yield return item; }
    }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var item in Visuals(child)) yield return item; }
    }
}
