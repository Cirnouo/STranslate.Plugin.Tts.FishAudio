using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
using static TestAssertions;

internal static class PopupDirectionSuite
{
    internal static async Task RunAsync(string? directory = null)
    {
        // Four semantic owners, each retaining the shared execution action
        // deadline: 80 seconds cumulative, with no new startup/observation budget.
        foreach (var language in new[] { false, true })
        {
            var client = language ? "language" : "search";
            await SearchAdjustmentControlSuite.ObserveMatrixActionAsync($"{client}-natural-sides",
                stage => NaturalSides(language, language ? null : directory, stage), prefix: "popup-direction", details: "five measured side policies.");
            await SearchAdjustmentControlSuite.ObserveMatrixActionAsync($"{client}-lifetime",
                stage => GeometryLifetime(language, stage), prefix: "popup-direction", details: "reopen/resize/move/fallback/refusal.");
        }
    }

    private static void NaturalSides(bool language, string? directory, Action<string> stage)
    {
        stage("compiled source construction");
        using var fixture = new Fixture(language);
        fixture.Position(12);
        fixture.Open();
        fixture.AssertCompleteNatural();
        var natural = fixture.Surface.ActualHeight;
        var chrome = natural - fixture.Body.ActualHeight;
        var firstSource = PresentationSource.FromVisual(fixture.Surface);
        Console.WriteLine($"[popup-direction/natural] client={language} actual={natural:0.###} body={fixture.Body.ActualHeight:0.###} extent={fixture.Body.ExtentHeight:0.###} viewport={fixture.Body.ViewportHeight:0.###}");
        fixture.Close();
        fixture.Root.Height = natural + 24 + fixture.Anchor.ActualHeight + 16 + chrome + 64;
        fixture.Position(natural + 24);
        fixture.Open();
        var bounds = PopupViewport.VisibleBounds(fixture.Anchor, fixture.Root, []);
        var scale = PopupViewport.ScreenScale(fixture.Anchor);
        var top = fixture.Anchor.PointToScreen(new Point()).Y;
        var bottom = fixture.Anchor.PointToScreen(new Point(0, fixture.Anchor.ActualHeight)).Y;
        var above = (top - bounds.Top) / scale.DpiScaleY - 8;
        var below = (bounds.Bottom - bottom) / scale.DpiScaleY - 8;
        var popupTop = fixture.Surface.PointToScreen(new Point()).Y;
        var popupBottom = fixture.Surface.PointToScreen(new Point(0, fixture.Surface.ActualHeight)).Y;
        var metadata = $"natural={natural:0.###}; above={above:0.###}; below={below:0.###}; actual={fixture.Surface.ActualHeight:0.###}; popupY={popupTop:0.###}..{popupBottom:0.###}; anchorY={top:0.###}..{bottom:0.###}; visible={bounds}; anchorScale={scale.DpiScaleX},{scale.DpiScaleY}; popupDpi={VisualTreeHelper.GetDpi(fixture.Surface).DpiScaleX}; liveSource={PresentationSource.FromVisual(fixture.Surface) is HwndSource { IsDisposed: false }}; sameSource={ReferenceEquals(firstSource, PresentationSource.FromVisual(fixture.Surface))}";
        Console.WriteLine("[popup-direction/asymmetric] " + metadata);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            SearchAdjustmentControlSuite.WriteProofAssembly(directory);
            File.WriteAllText(Path.Combine(directory, "geometry.txt"), metadata + Environment.NewLine + "Opaque accent fixture; WPF layout raster, no desktop Mica/installed-host/physical-input claim.");
            Save(fixture.Surface, Path.Combine(directory, "asymmetric.png"));
        }
        AssertEqual(true, above >= natural && below < natural && below > chrome + 32, "The observed intermediate geometry must fit natural content only above while preserving useful below space");
        AssertEqual(true, popupBottom <= top - 7 * scale.DpiScaleY + 1,
            "Complete natural content fits above, so a chrome-only cached measurement must not choose the cramped below side");
        AssertEqual(true, Math.Abs(natural - fixture.Surface.ActualHeight) < 1, "Above must retain the complete independently observed natural shell");
        stage("above-only complete natural content verified");
        fixture.Close();

        foreach (var (name, aboveCapacity, belowCapacity, expectAbove, full) in new[]
        {
            ("below-full-with-larger-above", natural + 32, natural + 8, false, true),
            ("neither-above-larger", natural - 48, chrome + 64, true, false),
            ("neither-below-larger", chrome + 64, natural - 48, false, false),
            ("neither-equal-prefers-below", natural - 48, natural - 48, false, false)
        })
        {
            fixture.Configure(aboveCapacity, belowCapacity);
            fixture.Open();
            if (name == "neither-equal-prefers-below")
            {
                var visible = PopupViewport.VisibleBounds(fixture.Anchor, fixture.Root, []);
                var nativeGap = 8 * PopupViewport.ScreenScale(fixture.Anchor).DpiScaleY;
                var actualAbove = fixture.Anchor.PointToScreen(new Point()).Y - visible.Top - nativeGap;
                var actualBelow = visible.Bottom - fixture.Anchor.PointToScreen(new Point(0, fixture.Anchor.ActualHeight)).Y - nativeGap;
                AssertEqual(true, Math.Abs(actualAbove - actualBelow) < .01, "The tie fixture establishes equal capacities in actual native device pixels");
            }
            AssertSide(fixture, expectAbove, name);
            AssertEqual(true, Math.Abs(fixture.Surface.ActualHeight - (full ? natural : Math.Max(aboveCapacity, belowCapacity))) < 1,
                $"{name}: use complete natural height or the larger available side");
            if (!full) AssertEqual(true, fixture.Body.ExtentHeight > fixture.Body.ViewportHeight, $"{name}: constrained body scrolls");
            fixture.AssertFixedChrome();
            fixture.Close();
            stage(name);
        }
        fixture.AssertAppliedDraft();
        stage("source/owner cleanup");
    }

    private static void GeometryLifetime(bool language, Action<string> stage)
    {
        stage("compiled source construction");
        using var fixture = new Fixture(language);
        fixture.Position(12); fixture.Open();
        fixture.AssertCompleteNatural();
        var natural = fixture.Surface.ActualHeight;
        var chrome = natural - fixture.Body.ActualHeight;
        fixture.Close();
        fixture.Configure(natural - 48, chrome + 64); fixture.Open();
        AssertSide(fixture, true, "constrained first source");
        var retired = PresentationSource.FromVisual(fixture.Surface);
        fixture.Close();
        AssertEqual(0d, fixture.Body.Padding.Right, "Closing releases the native scrollbar exclusion lane");
        fixture.Configure(chrome + 64, natural + 8); fixture.Open();
        AssertEqual(false, ReferenceEquals(retired, PresentationSource.FromVisual(fixture.Surface)), "Reopen owns the actual new native source");
        AssertSide(fixture, false, "reopen reverses the constrained side");
        AssertEqual(true, Math.Abs(natural - fixture.Surface.ActualHeight) < 1, "A retired constrained DesiredSize cannot shrink the next complete open");
        var live = PresentationSource.FromVisual(fixture.Surface);
        fixture.ChangeDraft();
        fixture.Configure(natural + 24, chrome + 64);
        AssertSide(fixture, true, "live owner resize and trigger movement reverse capacities");
        AssertEqual(true, ReferenceEquals(live, PresentationSource.FromVisual(fixture.Surface)), "Live geometry changes retain the current native source");
        fixture.AssertDraft();
        fixture.AssertFixedChrome();
        fixture.Configure(chrome + 64, natural + 40);
        AssertSide(fixture, false, "second live owner resize reverses capacities again");
        fixture.AssertDraft();
        stage("constrained reopen and two live geometry reversals");
        fixture.Close();

        fixture.Configure(chrome * .4, chrome * .4); fixture.Open();
        var surfaceTop = fixture.Surface.PointToScreen(new Point()).Y;
        var surfaceBottom = fixture.Surface.PointToScreen(new Point(0, fixture.Surface.ActualHeight)).Y;
        AssertEqual(true, surfaceTop < fixture.Anchor.PointToScreen(new Point()).Y
            && surfaceBottom > fixture.Anchor.PointToScreen(new Point(0, fixture.Anchor.ActualHeight)).Y,
            "When neither side fits chrome plus one option, the useful shell clamps across the trigger");
        fixture.AssertFixedChrome();
        fixture.Root.Height = chrome - 4; fixture.Layout();
        AssertEqual(false, fixture.IsOpen, "A viewport shorter than complete chrome closes and retires the overlay");
        AssertEqual(0d, fixture.Body.Padding.Right, "Height refusal releases the owned scrollbar padding");
        AssertEqual(IntPtr.Zero, fixture.MaterialHandle, "Height refusal releases the exact native material source");
        fixture.Configure(natural + 24, chrome + 64); fixture.Open();
        AssertSide(fixture, true, "restored sufficient viewport reopens");
        fixture.AssertFixedChrome();
        fixture.Close();
        fixture.AssertAppliedDraft();
        stage("across-trigger fallback, height refusal, restored reopen and cleanup");
    }

    private static void AssertSide(Fixture fixture, bool above, string name)
    {
        var bounds = PopupViewport.VisibleBounds(fixture.Anchor, fixture.Root, []);
        var top = fixture.Surface.PointToScreen(new Point());
        var bottom = fixture.Surface.PointToScreen(new Point(fixture.Surface.ActualWidth, fixture.Surface.ActualHeight));
        var scale = PopupViewport.ScreenScale(fixture.Anchor);
        var anchorTop = fixture.Anchor.PointToScreen(new Point()).Y;
        var anchorBottom = fixture.Anchor.PointToScreen(new Point(0, fixture.Anchor.ActualHeight)).Y;
        AssertEqual(true, above ? bottom.Y <= anchorTop - 8 * scale.DpiScaleY + 1 : top.Y >= anchorBottom + 8 * scale.DpiScaleY - 1,
            $"{name}: choose the declared side with the native screen-transform gap");
        AssertEqual(true, top.X >= bounds.Left - 1 && top.Y >= bounds.Top - 1 && bottom.X <= bounds.Right + 1 && bottom.Y <= bounds.Bottom + 1,
            $"{name}: retain both-axis owner/work-area containment");
    }

    private static void Save(FrameworkElement surface, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(surface);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(surface.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle((Brush)surface.FindResource("FlyoutPresenterBackground"), null, new Rect(surface.RenderSize));
            context.DrawRectangle(new VisualBrush(surface), null, new Rect(surface.RenderSize));
        }
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SettingsViewModel _viewModel;
        private readonly TestHttpServiceProxy _http;
        private readonly FrameworkElement _control;
        private readonly Popup _popup;
        private readonly HwndSource _source;
        private readonly double _sourceHeight;
        internal Canvas Root { get; } = new();
        internal Border Surface { get; }
        internal ScrollViewer Body { get; }
        internal FrameworkElement Anchor { get; }
        internal bool IsOpen => _popup.IsOpen;
        internal IntPtr MaterialHandle => _control is VoiceLanguageTags tags ? tags.Material.Handle : ((SearchAdjustmentControl)_control).Material.Handle;

        internal Fixture(bool language)
        {
            var settings = new Settings();
            var (service, http) = TestHttpServiceProxy.Create();
            _http = http;
            _viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings);
            Root.Resources.MergedDictionaries.Add(new ThemeResources { RequestedTheme = ApplicationTheme.Light });
            Root.Resources.MergedDictionaries.Add(new XamlControlsResources());
            Root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(FindRepoFile("STranslate.Plugin.Tts.FishAudio/Languages/en.xaml"), UriKind.Absolute) });
            ThemeManager.SetRequestedTheme(Root, ElementTheme.Light);
            // Explicit headless accent fixture, retaining native templates/metrics.
            Root.Resources["AccentButtonBackground"] = Brushes.DodgerBlue;
            Root.Resources["AccentButtonForeground"] = Brushes.White;
            Root.Resources["AccentFillColorDefaultBrush"] = Brushes.DodgerBlue;
            Root.Resources["RadioButtonOuterEllipseCheckedFill"] = Brushes.DodgerBlue;
            Root.Resources["RadioButtonOuterEllipseCheckedStroke"] = Brushes.DodgerBlue;
            _control = language
                ? new VoiceLanguageTags { Width = 190, Languages = ["en", "zh", "de", "ja", "fr", "es", "ko", "ar", "ru", "pt", "it", "nl", "pl", "tr", "hi", "vi", "th", "id"] }
                : new SearchAdjustmentControl { DataContext = _viewModel };
            Canvas.SetLeft(_control, language ? 180 : 350);
            Root.Children.Add(_control);
            Surface = (Border)_control.FindName("PopupSurface");
            Body = (ScrollViewer)_control.FindName("PopupScroll");
            Anchor = (FrameworkElement)_control.FindName(language ? "MoreButton" : "AdjustmentButton");
            _popup = (Popup)_control.FindName(language ? "LanguagePopup" : "AdjustmentPopup");
            var work = VoiceLanguageTags.MonitorWorkArea(new Point(120, 120));
            var dpi = VisualTreeHelper.GetDpi(new DrawingVisual());
            _source = new HwndSource(new HwndSourceParameters("Fish Audio popup direction fixture")
            {
                Width = (int)(400 * dpi.DpiScaleX), Height = (int)(work.Height - 48 * dpi.DpiScaleY),
                PositionX = (int)(work.Left + 24 * dpi.DpiScaleX), PositionY = (int)(work.Top + 24 * dpi.DpiScaleY), WindowStyle = unchecked((int)0x80000000)
            });
            _source.RootVisual = Root;
            Root.Width = 400;
            Root.Height = (work.Height - 48 * dpi.DpiScaleY) / PopupViewport.ScreenScale(Root).DpiScaleY;
            _sourceHeight = Root.Height;
            Layout();
        }

        internal void Position(double top) { Canvas.SetTop(_control, top); Layout(); }
        internal void Configure(double above, double below)
        {
            var scale = PopupViewport.ScreenScale(Anchor).DpiScaleY;
            // Round capacities in the actual native pixel space; exact ties then
            // remain ties instead of becoming visual-root-DPI rounding artifacts.
            above = Math.Round(above * scale) / scale;
            below = Math.Round(below * scale) / scale;
            var anchorHeight = (Anchor.PointToScreen(new Point(0, Anchor.ActualHeight)).Y - Anchor.PointToScreen(new Point()).Y) / scale;
            var anchorOffset = Anchor.TranslatePoint(new Point(), _control).Y;
            Canvas.SetTop(_control, above + 16 - anchorOffset);
            Root.Height = above + below + anchorHeight + 32;
            AssertEqual(true, Root.Height <= _sourceHeight, "The fixture owner stays inside its actual native client viewport");
            Layout();
        }
        internal void AssertFixedChrome()
        {
            foreach (var name in _control is VoiceLanguageTags ? new[] { "RemainingCount" } : new[] { "CloseButton", "ResetButton", "ApplyButton" })
            {
                var part = (FrameworkElement)_control.FindName(name);
                var bounds = part.TransformToAncestor(Surface).TransformBounds(new Rect(part.RenderSize));
                AssertEqual(true, bounds.Left >= 0 && bounds.Right <= Surface.ActualWidth + .1 && bounds.Top >= 0 && bounds.Bottom <= Surface.ActualHeight + .1,
                    "Localized fixed chrome stays complete and reachable outside scrolling");
            }
        }
        internal void ChangeDraft()
        {
            if (_control is SearchAdjustmentControl search)
            {
                ((Grid)search.FindName("LanguageTiles")).Children.OfType<ToggleButton>().Single(tile => (string)tile.Tag == "en").IsChecked = true;
                ((RadioButton)search.FindName("CreationRadio")).IsChecked = true;
            }
        }
        internal void AssertDraft()
        {
            if (_control is SearchAdjustmentControl search)
            {
                AssertEqual(true, ((Grid)search.FindName("LanguageTiles")).Children.OfType<ToggleButton>().Single(tile => (string)tile.Tag == "en").IsChecked, "Live geometry preserves language drafts");
                AssertEqual(true, ((RadioButton)search.FindName("CreationRadio")).IsChecked, "Live geometry preserves sort drafts");
            }
        }
        internal void AssertAppliedDraft()
        {
            AssertEqual(0, _viewModel.SearchLanguages.Count, "Geometry/dismissal never applies language drafts");
            AssertEqual("score", _viewModel.SearchSort, "Geometry/dismissal never applies sort drafts");
            AssertEqual(0, _http.GetCallCount, "Opening, geometry changes, refusal and dismissal issue no metadata GET");
        }
        internal void AssertCompleteNatural() => AssertEqual(true, Body.ExtentHeight <= Body.ViewportHeight + .1,
            "The independent initial natural-height oracle must expose its complete body without viewport scrolling");
        internal void Layout() { Root.Measure(new Size(Root.Width, Root.Height)); Root.Arrange(new Rect(0, 0, Root.Width, Root.Height)); Root.UpdateLayout(); Drain(); }
        internal void Open()
        {
            if (_control is VoiceLanguageTags tags) tags.OpenPopup();
            else ((Button)Anchor).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain();
            AssertEqual(true, _popup.IsOpen, "The compiled popup owns a live native source before measuring direction");
            AssertEqual(true, PresentationSource.FromVisual(Surface) is HwndSource { IsDisposed: false } popupSource && !ReferenceEquals(popupSource, _source),
                "Geometry observes the popup's actual live source separately from its owner");
        }
        internal void Close() { _popup.IsOpen = false; Drain(); }
        public void Dispose() { Close(); _source.Dispose(); _viewModel.Dispose(); }
        private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }
}
