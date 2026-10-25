using System.Runtime.InteropServices;
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
        Console.WriteLine($"[popup-direction/natural] client={language} actual={natural:0.###} body={fixture.Body.ActualHeight:0.###} extent={fixture.Body.ExtentHeight:0.###} viewport={fixture.Body.ViewportHeight:0.###}");
        SyntheticSides(fixture, natural, chrome, stage);
        fixture.Close();

        var (large, small, tie) = fixture.NativeCapacities(natural, chrome);
        foreach (var (name, aboveCapacity, belowCapacity, expectAbove, full) in new[]
        {
            ("native-above-only-full", natural + 8, fixture.OppositeCapacity(natural + 8), true, true),
            ("native-below-full", fixture.OppositeCapacity(natural + 8), natural + 8, false, true),
            ("native-neither-above-larger", large, small, true, false),
            ("native-neither-below-larger", small, large, false, false),
            ("native-neither-equal-prefers-below", tie, tie, false, false)
        })
        {
            fixture.Configure(aboveCapacity, belowCapacity);
            var (above, below) = fixture.Capacities();
            AssertEqual(true, full ? (expectAbove ? above >= natural && below < natural : below >= natural && above < natural)
                : above < natural && below < natural && above > chrome + 32 && below > chrome + 32,
                $"{name}: measured native capacities establish the declared full/useful-constrained preconditions");
            if (name == "native-neither-equal-prefers-below")
                AssertEqual(true, Math.Abs(above - below) * PopupViewport.ScreenScale(fixture.Anchor).DpiScaleY < .01,
                    "The tie fixture establishes equal capacities in actual native device pixels");
            fixture.Open();
            AssertSide(fixture, expectAbove, name);
            var selected = expectAbove ? above : below;
            AssertNear(selected, fixture.Surface.MaxHeight, $"{name}: the side capacity, rather than across-trigger fallback, sets MaxHeight");
            AssertNear(full ? natural : selected, fixture.Surface.ActualHeight, $"{name}: use complete natural height or the larger available side");
            if (full) fixture.AssertCompleteNatural();
            else AssertEqual(true, fixture.Body.ExtentHeight > fixture.Body.ViewportHeight, $"{name}: constrained body scrolls");
            fixture.AssertFixedChrome();
            if (directory is not null && name == "native-above-only-full")
            {
                Directory.CreateDirectory(directory);
                SearchAdjustmentControlSuite.WriteProofAssembly(directory);
                File.WriteAllText(Path.Combine(directory, "geometry.txt"),
                    $"native feasible above-only: natural={natural:0.###}; chrome={chrome:0.###}; above={above:0.###}; below={below:0.###}; actual={fixture.Surface.ActualHeight:0.###}" + Environment.NewLine
                    + "Opaque accent fixture; WPF layout raster, no desktop Mica/installed-host/physical-input claim. Original five capacity vectors are separate synthetic geometry evidence.");
                Save(fixture.Surface, Path.Combine(directory, "asymmetric.png"));
            }
            fixture.Close();
            stage(name);
        }
        fixture.AssertAppliedDraft();
        stage("source/owner cleanup");
    }

    private static void SyntheticSides(Fixture fixture, double natural, double chrome, Action<string> stage)
    {
        // All original vectors use the compiled live-source zero-body/full-body
        // measure path. Returned geometry is synthetic, never a positioned HWND.
        var source = PresentationSource.FromVisual(fixture.Surface);
        Exception? primary = null;
        try
        {
            foreach (var (name, above, below, expectAbove, full) in new[]
            {
                ("above-only-full-useful-below", natural + 8, chrome + 64, true, true),
                ("below-full-with-larger-above", natural + 32, natural + 8, false, true),
                ("neither-above-larger", natural - 48, chrome + 64, true, false),
                ("neither-below-larger", chrome + 64, natural - 48, false, false),
                ("neither-equal-prefers-below", natural - 48, natural - 48, false, false)
            })
            {
                var scale = PopupViewport.ScreenScale(fixture.Anchor);
                var origin = fixture.Anchor.PointToScreen(new Point());
                var anchorBottom = fixture.Anchor.PointToScreen(new Point(0, fixture.Anchor.ActualHeight)).Y;
                var native = PopupViewport.VisibleBounds(fixture.Anchor, fixture.Root, []);
                var bounds = new Rect(new Point(native.Left, origin.Y - (8 + above) * scale.DpiScaleY),
                    new Point(native.Right, anchorBottom + (8 + below) * scale.DpiScaleY));
                AssertEqual(true, full ? (expectAbove ? above >= natural && below < natural && below > chrome + 32
                    : below >= natural && above > below) : above < natural && below < natural && above > chrome + 32 && below > chrome + 32,
                    $"synthetic/{name}: original useful/full policy preconditions");
                AssertEqual(true, fixture.Prepare(bounds), $"synthetic/{name}: live compiled chrome fits");
                AssertEqual(true, ReferenceEquals(source, PresentationSource.FromVisual(fixture.Surface)),
                    $"synthetic/{name}: preserve the original live compiled source");
                var selected = expectAbove ? above : below;
                AssertNear(selected, fixture.Surface.MaxHeight, $"synthetic/{name}: exact selected side MaxHeight");
                AssertNear(Math.Min(fixture.MaximumBodyHeight, selected - chrome), fixture.Body.MaxHeight,
                    $"synthetic/{name}: preserve fixed chrome outside the body budget");
                fixture.Surface.Measure(new Size(fixture.Surface.Width, double.PositiveInfinity));
                AssertNear(full ? natural : selected, fixture.Surface.DesiredSize.Height,
                    $"synthetic/{name}: full natural or constrained compiled shell demand");
                AssertNear(Math.Min(natural - chrome, selected - chrome), fixture.Body.DesiredSize.Height,
                    $"synthetic/{name}: body demand retains complete fixed chrome");
                var size = new Size(fixture.Surface.DesiredSize.Width * scale.DpiScaleX,
                    fixture.Surface.DesiredSize.Height * scale.DpiScaleY);
                var target = new Size(fixture.Anchor.ActualWidth * scale.DpiScaleX, anchorBottom - origin.Y);
                var placements = PopupViewport.PlaceWithinBounds(fixture.Anchor, size, target, fixture.AlignRight, bounds);
                AssertEqual(1, placements.Length, $"synthetic/{name}: one placement");
                var top = origin + (Vector)placements[0].Point;
                AssertEqual(true, expectAbove ? top.Y + size.Height <= origin.Y - 8 * scale.DpiScaleY + 1
                    : top.Y >= anchorBottom + 8 * scale.DpiScaleY - 1, $"synthetic/{name}: declared side and native-unit gap");
                AssertEqual(true, top.X >= bounds.Left - 1 && top.Y >= bounds.Top - 1
                    && top.X + size.Width <= bounds.Right + 1 && top.Y + size.Height <= bounds.Bottom + 1,
                    $"synthetic/{name}: containment inside the explicit synthetic rectangle");
                stage("synthetic/" + name);
            }
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            try { AssertEqual(true, fixture.Prepare(), "Restore real visible bounds before native transitions"); }
            catch (Exception cleanup) when (primary is not null)
            {
                try { primary.Data["SyntheticBoundsCleanupFailure"] = cleanup.GetType().Name; }
                catch { /* Secondary diagnostics cannot replace the primary failure. */ }
            }
        }
    }

    private static void AssertNear(double expected, double actual, string message) =>
        AssertEqual(true, Math.Abs(expected - actual) < 1, $"{message}; expected={expected:0.###}, actual={actual:0.###}");

    private static void GeometryLifetime(bool language, Action<string> stage)
    {
        stage("compiled source construction");
        using var fixture = new Fixture(language);
        fixture.Position(12); fixture.Open();
        fixture.AssertCompleteNatural();
        var natural = fixture.Surface.ActualHeight;
        var chrome = natural - fixture.Body.ActualHeight;
        var (large, small, _) = fixture.NativeCapacities(natural, chrome);
        fixture.Close();
        fixture.Configure(large, small); fixture.Open();
        AssertSide(fixture, true, "constrained first source");
        var retired = PresentationSource.FromVisual(fixture.Surface);
        fixture.Close();
        AssertEqual(0d, fixture.Body.Padding.Right, "Closing releases the native scrollbar exclusion lane");
        fixture.Configure(fixture.OppositeCapacity(natural + 8), natural + 8); fixture.Open();
        AssertEqual(false, ReferenceEquals(retired, PresentationSource.FromVisual(fixture.Surface)), "Reopen owns the actual new native source");
        AssertSide(fixture, false, "reopen reverses the constrained side");
        AssertEqual(true, Math.Abs(natural - fixture.Surface.ActualHeight) < 1, "A retired constrained DesiredSize cannot shrink the next complete open");
        var live = PresentationSource.FromVisual(fixture.Surface);
        fixture.ChangeDraft();
        var previousHeight = fixture.Root.Height;
        fixture.Configure(natural + 24, fixture.OppositeCapacity(natural + 24));
        AssertEqual(true, fixture.Root.Height != previousHeight, "The live reversal includes the original owner resize stimulus");
        AssertSide(fixture, true, "live owner resize and trigger movement reverse capacities");
        AssertEqual(true, ReferenceEquals(live, PresentationSource.FromVisual(fixture.Surface)), "Live geometry changes retain the current native source");
        fixture.AssertDraft();
        fixture.AssertFixedChrome();
        previousHeight = fixture.Root.Height;
        fixture.Configure(fixture.OppositeCapacity(natural + 40), natural + 40);
        AssertEqual(true, fixture.Root.Height != previousHeight, "The second live reversal resizes the same owner again");
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
        fixture.Configure(natural + 24, fixture.OppositeCapacity(natural + 24)); fixture.Open();
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
        private readonly double _sideBudget;
        internal Canvas Root { get; } = new();
        internal Border Surface { get; }
        internal ScrollViewer Body { get; }
        internal FrameworkElement Anchor { get; }
        internal bool IsOpen => _popup.IsOpen;
        internal bool AlignRight => _control is SearchAdjustmentControl;
        internal double MaximumBodyHeight => _control is VoiceLanguageTags ? 280 : double.PositiveInfinity;
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
            AssertEqual(true, GetClientRect(_source.Handle, out var client), "Read the actual fixture native client capacity");
            Root.Height = Math.Min(680, (client.Bottom - client.Top) / PopupViewport.ScreenScale(Root).DpiScaleY);
            _sourceHeight = Root.Height;
            Layout();
            var visible = PopupViewport.VisibleBounds(Anchor, Root, []);
            var nativeScale = PopupViewport.ScreenScale(Anchor).DpiScaleY;
            var anchorHeight = Anchor.PointToScreen(new Point(0, Anchor.ActualHeight)).Y - Anchor.PointToScreen(new Point()).Y;
            _sideBudget = Math.Floor(visible.Height - anchorHeight - 16 * nativeScale) / nativeScale;
        }

        internal bool Prepare(Rect? bounds = null)
        {
            FrameworkElement[] chrome = _control is VoiceLanguageTags
                ? [(FrameworkElement)_control.FindName("RemainingCount")]
                : [(FrameworkElement)_control.FindName("PopupFooter"), (FrameworkElement)_control.FindName("CloseButton")];
            return bounds is { } explicitBounds
                ? PopupViewport.PrepareWithinBounds(Anchor, Surface, Body, chrome, AlignRight ? 324 : 290, MaximumBodyHeight, explicitBounds)
                : PopupViewport.Prepare(Anchor, Root, [], Surface, Body, chrome, AlignRight ? 324 : 290, MaximumBodyHeight);
        }
        internal (double Above, double Below) Capacities()
        {
            var visible = PopupViewport.VisibleBounds(Anchor, Root, []);
            var scale = PopupViewport.ScreenScale(Anchor).DpiScaleY;
            return ((Anchor.PointToScreen(new Point()).Y - visible.Top) / scale - 8,
                (visible.Bottom - Anchor.PointToScreen(new Point(0, Anchor.ActualHeight)).Y) / scale - 8);
        }
        internal double OppositeCapacity(double full)
        {
            AssertEqual(true, full <= _sideBudget, "The measured native viewport fits the requested complete side");
            // Full-side lifetime states must still resize the owner between the
            // original +8/+24/+40 margins; consuming all remaining space would
            // hold its height constant and remove the real SizeChanged stimulus.
            return 0;
        }
        internal (double Large, double Small, double Tie) NativeCapacities(double natural, double chrome)
        {
            var scale = PopupViewport.ScreenScale(Anchor).DpiScaleY;
            var small = (Math.Ceiling((chrome + 32) * scale) + 1) / scale;
            var large = Math.Floor(Math.Min(natural - 48, _sideBudget - small) * scale) / scale;
            var tie = Math.Floor(Math.Min(natural - 48, _sideBudget / 2) * scale) / scale;
            AssertEqual(true, small > chrome + 32 && large > small && large < natural && tie > chrome + 32 && tie < natural,
                "Measured native capacity supports useful constrained larger-side and tie discriminators");
            return (large, small, tie);
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

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr window, out NativeRect bounds);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
    }
}
