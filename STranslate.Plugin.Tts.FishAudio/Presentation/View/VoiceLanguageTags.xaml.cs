using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using STranslate.Plugin.Tts.FishAudio.Configuration;

namespace STranslate.Plugin.Tts.FishAudio.View;

public partial class VoiceLanguageTags : UserControl
{
    public static readonly DependencyProperty LanguagesProperty = DependencyProperty.Register(nameof(Languages),
        typeof(IReadOnlyList<string>), typeof(VoiceLanguageTags), new PropertyMetadata(null, LanguagesChanged));
    public static readonly DependencyProperty UiLocaleProperty = DependencyProperty.Register(nameof(UiLocale),
        typeof(string), typeof(VoiceLanguageTags), new PropertyMetadata("en", PresentationChanged));
    private static readonly DependencyProperty MoreCaptionProperty = DependencyProperty.Register("MoreCaption",
        typeof(string), typeof(VoiceLanguageTags), new PropertyMetadata("", PresentationChanged));
    private static readonly DependencyProperty PopupOwnerProperty = DependencyProperty.RegisterAttached("PopupOwner",
        typeof(VoiceLanguageTags), typeof(VoiceLanguageTags));

    private FrameworkElement? _owner;
    private Window? _window;
    private bool _ownsPopupInput;
    private readonly List<ScrollViewer> _scrollOwners = [];
    private IReadOnlyList<string> _codes = Array.Empty<string>();
    private VoiceLanguageLabel[] _labels = [];
    private ContentControl[] _inlinePills = [];
    private double[]? _naturalWidths;
    private int _inlineCount = -1;

    public IReadOnlyList<string>? Languages
    {
        get => (IReadOnlyList<string>?)GetValue(LanguagesProperty);
        set => SetValue(LanguagesProperty, value);
    }
    public string UiLocale
    {
        get => (string)GetValue(UiLocaleProperty);
        set => SetValue(UiLocaleProperty, value);
    }
    internal bool IsPopupOpen => LanguagePopup.IsOpen;

    public VoiceLanguageTags()
    {
        InitializeComponent();
        MoreButton.TagsOwner = this;
        SetResourceReference(UiLocaleProperty, "STranslate_Plugin_Tts_FishAudio_Languages_UiLocale");
        SetResourceReference(MoreCaptionProperty, "STranslate_Plugin_Tts_FishAudio_Languages_More");
        LanguagePopup.CustomPopupPlacementCallback = PlacePopup;
        LanguagePopup.Closed += (_, _) => ReleasePopupOwnership();
        DataContextChanged += (_, _) => ClosePopup();
        Unloaded += (_, _) => ClosePopup();
        IsVisibleChanged += (_, _) => { if (!IsVisible) ClosePopup(); };
        RefreshPresentation();
    }

    private static void LanguagesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (VoiceLanguageTags)sender;
        control.ClosePopup();
        control.RefreshPresentation();
    }
    private static void PresentationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((VoiceLanguageTags)sender).RefreshPresentation();

    private void RefreshPresentation()
    {
        if (InlinePanel is null) return;
        _codes = VoiceLanguageMetadata.Normalize(Languages);
        Visibility = _codes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        InlinePanel.Children.Clear();
        PopupPanel.Children.Clear();
        var locale = UiLocale;
        try { _ = CultureInfo.GetCultureInfo(locale); }
        catch (CultureNotFoundException) { locale = "en"; }
        _labels = _codes.Select(code => VoiceLanguagePresentation.Describe(code, locale)).ToArray();
        _inlinePills = _labels.Select(label => CreatePill(label, "LanguagePill")).ToArray();
        _naturalWidths = null;
        _inlineCount = -1;
        foreach (var pill in _inlinePills) InlinePanel.Children.Add(pill);
        InlinePanel.Children.Add(MoreButton);
        if (_codes.Count == 0)
        {
            MoreCountLabel.Text = "+0";
            MoreButton.Visibility = Visibility.Collapsed;
            RemainingCount.Text = "0";
            _inlineCount = 0;
        }
        InvalidateMeasure();
    }

    private ContentControl CreatePill(VoiceLanguageLabel label, string template)
    {
        var pill = new ContentControl
        {
            Content = label,
            ContentTemplate = (DataTemplate)Resources[template],
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Focusable = false,
            ToolTip = $"{label.Name} ({label.Code})",
        };
        System.Windows.Automation.AutomationProperties.SetName(pill, $"{label.Name} ({label.Code})");
        return pill;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (InlinePanel is not null && _codes.Count != 0) FitInlinePrefix(constraint.Width);
        return base.MeasureOverride(constraint);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _naturalWidths = null;
        InvalidateMeasure();
    }

    private void FitInlinePrefix(double width)
    {
        if (_naturalWidths is null)
        {
            _naturalWidths = new double[_inlinePills.Length];
            for (var index = 0; index < _inlinePills.Length; index++)
            {
                var pill = _inlinePills[index];
                pill.Visibility = Visibility.Visible;
                pill.Width = double.NaN;
                pill.Margin = new Thickness();
                pill.Measure(new Size(double.PositiveInfinity, 20.5));
                // Round the complete capsule outward to a device pixel. Subtracting
                // its 32-DIP chrome from an exact fractional desired width can leave
                // the text viewport a floating-point fraction below its full advance,
                // causing WPF to draw an ellipsis despite the measured fit.
                var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                _naturalWidths[index] = Math.BitIncrement(Math.Ceiling(pill.DesiredSize.Width * scale) / scale);
            }
        }
        var prefix = new double[_naturalWidths.Length + 1];
        for (var index = 0; index < _naturalWidths.Length; index++) prefix[index + 1] = prefix[index] + _naturalWidths[index];
        var count = _codes.Count;
        var overflowWidth = 0d;
        var found = false;
        for (; count >= 1; count--)
        {
            overflowWidth = MeasureOverflow(_codes.Count - count);
            var needed = prefix[count] + 4 * (count - 1) + (overflowWidth > 0 ? 4 + overflowWidth : 0);
            if (needed <= width || Math.Abs(needed - width) < .0000001) { found = true; break; }
        }
        if (!found)
        {
            count = 1;
            overflowWidth = MeasureOverflow(_codes.Count - 1);
        }
        for (var index = 0; index < _inlinePills.Length; index++)
        {
            var pill = _inlinePills[index];
            pill.Visibility = index < count ? Visibility.Visible : Visibility.Collapsed;
            pill.Width = index == 0 && !found
                ? Math.Max(0, width - (overflowWidth > 0 ? overflowWidth + 4 : 0)) : _naturalWidths[index];
            pill.Margin = new Thickness(0, 0, index < count - 1 || overflowWidth > 0 ? 4 : 0, 0);
        }
        var remaining = _codes.Count - count;
        MoreCountLabel.Text = $"+{remaining}";
        MoreButton.Visibility = remaining > 0 ? Visibility.Visible : Visibility.Collapsed;
        var caption = (string)GetValue(MoreCaptionProperty);
        System.Windows.Automation.AutomationProperties.SetName(MoreButton, $"{caption} +{remaining}");
        MoreButton.ToolTip = $"{caption} +{remaining}";
        RemainingCount.Text = remaining.ToString(CultureInfo.InvariantCulture);
        if (_inlineCount != count)
        {
            _inlineCount = count;
            PopupPanel.Children.Clear();
            foreach (var label in _labels.Skip(count)) PopupPanel.Children.Add(CreatePill(label, "PopupLanguagePill"));
            if (remaining == 0) ClosePopup();
            else if (IsPopupOpen) Reposition();
        }
    }

    private double MeasureOverflow(int remaining)
    {
        if (remaining == 0) return 0;
        MoreButton.Visibility = Visibility.Visible;
        // Keep one text element in the template: changing Button.Content strings can
        // defer its ContentPresenter update until after this candidate's measure.
        MoreCountLabel.Text = $"+{remaining}";
        MoreButton.ApplyTemplate();
        MoreCountLabel.InvalidateMeasure();
        if (MoreButton.Template.FindName("Surface", MoreButton) is Border surface)
        {
            surface.Child.InvalidateMeasure();
            surface.InvalidateMeasure();
        }
        MoreButton.InvalidateMeasure();
        MoreButton.Measure(new Size(double.PositiveInfinity, 20.5));
        return MoreButton.DesiredSize.Width;
    }

    private void More_Click(object sender, RoutedEventArgs args)
    {
        if (IsPopupOpen) ClosePopup();
        else OpenPopup();
        args.Handled = true;
    }

    internal void OpenPopup()
    {
        if (IsPopupOpen || _inlineCount < 1 || _codes.Count <= _inlineCount || !IsVisible || !IsLoaded) return;
        _window = Window.GetWindow(this);
        _owner = Ancestors(this).OfType<SettingsView>().FirstOrDefault()
            ?? (FrameworkElement?)_window ?? this;
        if (_owner.GetValue(PopupOwnerProperty) is VoiceLanguageTags other) other.ClosePopup();
        _owner.SetValue(PopupOwnerProperty, this);
        _owner.SizeChanged += OwnerSizeChanged;
        _owner.IsVisibleChanged += OwnerVisibilityChanged;
        foreach (var viewer in Ancestors(this).OfType<ScrollViewer>())
        {
            _scrollOwners.Add(viewer);
            viewer.ScrollChanged += OwnerScrollChanged;
        }
        if (_window is not null)
        {
            _window.LocationChanged += WindowLocationChanged;
            _window.SizeChanged += OwnerSizeChanged;
            _window.Deactivated += WindowDeactivated;
        }
        InputManager.Current.PreProcessInput += ObserveInput;
        _ownsPopupInput = true;
        PopupSurface.Width = Math.Max(1, Math.Min(290, _owner.ActualWidth - 16));
        try { LanguagePopup.IsOpen = true; }
        catch { ReleasePopupOwnership(); throw; }
        MoreButton.IsExpanded = true;
    }

    internal void ClosePopup(bool returnFocus = false)
    {
        if (!LanguagePopup.IsOpen && !_ownsPopupInput) return;
        LanguagePopup.IsOpen = false;
        ReleasePopupOwnership();
        if (returnFocus && MoreButton.IsVisible && MoreButton.IsLoaded) MoreButton.Focus();
    }

    private void ReleasePopupOwnership()
    {
        if (!_ownsPopupInput) return;
        _ownsPopupInput = false;
        MoreButton.IsExpanded = false;
        InputManager.Current.PreProcessInput -= ObserveInput;
        foreach (var viewer in _scrollOwners) viewer.ScrollChanged -= OwnerScrollChanged;
        _scrollOwners.Clear();
        if (_window is not null)
        {
            _window.LocationChanged -= WindowLocationChanged;
            _window.SizeChanged -= OwnerSizeChanged;
            _window.Deactivated -= WindowDeactivated;
        }
        if (_owner is not null)
        {
            _owner.SizeChanged -= OwnerSizeChanged;
            _owner.IsVisibleChanged -= OwnerVisibilityChanged;
            if (ReferenceEquals(_owner.GetValue(PopupOwnerProperty), this)) _owner.ClearValue(PopupOwnerProperty);
        }
        _window = null;
        _owner = null;
    }

    private void ObserveInput(object sender, PreProcessInputEventArgs args)
    {
        // StaysOpen=true deliberately avoids Popup's mouse capture dismissal on the
        // trigger's mouse-down followed by an accidental reopen on Button.Click.
        if (args.StagingItem.Input is KeyEventArgs { Key: Key.Escape } key
            && (key.RoutedEvent == Keyboard.PreviewKeyDownEvent || key.RoutedEvent == Keyboard.KeyDownEvent))
        {
            ClosePopup(true);
            key.Handled = true;
        }
        else if (args.StagingItem.Input is MouseButtonEventArgs mouse
                 && (mouse.RoutedEvent == Mouse.PreviewMouseDownEvent || mouse.RoutedEvent == Mouse.MouseDownEvent)
                 && (mouse.OriginalSource ?? Mouse.DirectlyOver) is DependencyObject source
                 && !IsWithin(source, MoreButton) && !IsWithin(source, PopupSurface)) ClosePopup();
    }

    private void OwnerVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) { if (_owner?.IsVisible != true) ClosePopup(); }
    private void WindowDeactivated(object? sender, EventArgs args) => ClosePopup();
    private void WindowLocationChanged(object? sender, EventArgs args) => Reposition();
    private void OwnerSizeChanged(object sender, SizeChangedEventArgs args) => Reposition();
    private void OwnerScrollChanged(object sender, ScrollChangedEventArgs args)
    {
        if (args.OriginalSource is ScrollViewer viewer && _scrollOwners.Contains(viewer)
            && (args.VerticalChange != 0 || args.HorizontalChange != 0)) ClosePopup();
    }
    private void Reposition()
    {
        if (!IsPopupOpen || _owner is null) return;
        PopupSurface.Width = Math.Max(1, Math.Min(290, _owner.ActualWidth - 16));
        // Changing the offset asks WPF to re-run custom placement after move/resize.
        LanguagePopup.HorizontalOffset = LanguagePopup.HorizontalOffset == 0 ? .01 : 0;
    }

    private CustomPopupPlacement[] PlacePopup(Size popupSize, Size targetSize, Point offset)
    {
        var anchor = MoreButton.PointToScreen(new Point());
        var dpi = VisualTreeHelper.GetDpi(MoreButton);
        var left = anchor.X - 12 * dpi.DpiScaleX;
        // WPF invokes custom placement with screen-space (device-pixel) sizes
        // and adds the returned device-pixel offset to the target's screen point.
        var top = anchor.Y + targetSize.Height + 7 * dpi.DpiScaleY;
        var work = MonitorWorkArea(anchor);
        var minX = work.Left;
        var maxX = work.Right;
        if (_owner is { ActualWidth: > 0 })
        {
            var origin = _owner.PointToScreen(new Point());
            minX = Math.Max(minX, origin.X + 8 * dpi.DpiScaleX);
            maxX = Math.Min(maxX, origin.X + (_owner.ActualWidth - 8) * dpi.DpiScaleX);
        }
        left = Math.Clamp(left, minX, Math.Max(minX, maxX - popupSize.Width));
        top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - popupSize.Height));
        return [new CustomPopupPlacement(new Point(left - anchor.X, top - anchor.Y), PopupPrimaryAxis.None)];
    }

    private static bool IsWithin(DependencyObject child, DependencyObject owner) =>
        ReferenceEquals(child, owner) || Ancestors(child).Contains(owner);
    private static IEnumerable<DependencyObject> Ancestors(DependencyObject child)
    {
        for (var parent = ParentOf(child); parent is not null; parent = ParentOf(parent)) yield return parent;
    }
    private static DependencyObject? ParentOf(DependencyObject child) => child is Visual
        ? VisualTreeHelper.GetParent(child) ?? LogicalTreeHelper.GetParent(child)
        : LogicalTreeHelper.GetParent(child);

    private static Rect MonitorWorkArea(Point point)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var monitor = MonitorFromPoint(new NativePoint { X = (int)point.X, Y = (int)point.Y }, 2);
        if (GetMonitorInfo(monitor, ref info))
            return new Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        var dpi = VisualTreeHelper.GetDpi(new DrawingVisual());
        var work = SystemParameters.WorkArea;
        return new Rect(work.X * dpi.DpiScaleX, work.Y * dpi.DpiScaleY, work.Width * dpi.DpiScaleX, work.Height * dpi.DpiScaleY);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
