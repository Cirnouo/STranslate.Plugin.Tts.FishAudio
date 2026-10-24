using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static STranslate.Plugin.Tts.FishAudio.View.SettingsOverlayOwnership;

namespace STranslate.Plugin.Tts.FishAudio.View;

internal sealed record SearchLanguageOption(string? Code, string Name, ImageSource? Flag);

public partial class SearchAdjustmentControl : UserControl
{
    private static readonly string[] LanguageCodes = ["en", "zh", "de", "ja", "fr", "es", "ko", "ar", "ru", "pt"];
    private static readonly DependencyProperty UiLocaleProperty = DependencyProperty.Register("UiLocale", typeof(string),
        typeof(SearchAdjustmentControl), new PropertyMetadata("en", LabelsChanged));
    private static readonly DependencyProperty AllCaptionProperty = DependencyProperty.Register("AllCaption", typeof(string),
        typeof(SearchAdjustmentControl), new PropertyMetadata("", LabelsChanged));
    private readonly object _ownershipToken = new();
    private readonly List<ScrollViewer> _scrollOwners = [];
    private FrameworkElement? _owner;
    private Window? _window;
    private Popup? _comboPopup;
    private bool _ownsInput;

    public SearchAdjustmentControl()
    {
        InitializeComponent();
        SetResourceReference(UiLocaleProperty, "STranslate_Plugin_Tts_FishAudio_Languages_UiLocale");
        SetResourceReference(AllCaptionProperty, "STranslate_Plugin_Tts_FishAudio_Search_AllLanguages");
        AdjustmentPopup.CustomPopupPlacementCallback = PlacePopup;
        AdjustmentPopup.Closed += (_, _) => ReleaseOwnership();
        DataContextChanged += (_, _) => ClosePopup();
        Unloaded += (_, _) => ClosePopup();
        IsVisibleChanged += (_, _) => { if (!IsVisible) ClosePopup(); };
        LanguageCombo.DropDownOpened += (_, _) => _comboPopup = LanguageCombo.Template.FindName("PART_Popup", LanguageCombo) as Popup
            ?? LanguageCombo.Template.FindName("Popup", LanguageCombo) as Popup;
        RefreshLabels();
    }

    private static void LabelsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((SearchAdjustmentControl)sender).RefreshLabels();

    private void RefreshLabels()
    {
        if (LanguageCombo is null) return;
        var selected = (LanguageCombo.SelectedItem as SearchLanguageOption)?.Code;
        var locale = (string)GetValue(UiLocaleProperty);
        var options = new List<SearchLanguageOption> { new(null, (string)GetValue(AllCaptionProperty), null) };
        foreach (var code in LanguageCodes)
        {
            var label = VoiceLanguagePresentation.Describe(code, locale);
            options.Add(new(code, label.Name, label.Flag));
        }
        LanguageCombo.ItemsSource = options;
        LanguageCombo.SelectedIndex = options.FindIndex(option => option.Code == selected);
    }

    private void Adjustment_Click(object sender, RoutedEventArgs args)
    {
        if (AdjustmentPopup.IsOpen) ClosePopup();
        else OpenPopup();
        args.Handled = true;
    }

    private void OpenPopup()
    {
        if (AdjustmentPopup.IsOpen || !IsLoaded || !IsVisible || DataContext is not SettingsViewModel vm) return;
        LanguageCombo.SelectedValue = vm.SearchLanguage;
        if (vm.SearchLanguage is null) LanguageCombo.SelectedIndex = 0;
        RelevanceRadio.IsChecked = vm.SearchSort == "score";
        PopularityRadio.IsChecked = vm.SearchSort == "task_count";
        CreationRadio.IsChecked = vm.SearchSort == "created_at";
        _owner = FindOwner(this);
        _window = Window.GetWindow(this);
        Acquire(_owner, _ownershipToken, () => ClosePopup());
        _owner.SizeChanged += OwnerSizeChanged;
        _owner.IsVisibleChanged += OwnerVisibilityChanged;
        foreach (var viewer in Ancestors(this).OfType<ScrollViewer>())
        {
            _scrollOwners.Add(viewer);
            viewer.ScrollChanged += OwnerScrollChanged;
            viewer.SizeChanged += OwnerSizeChanged;
        }
        if (_window is not null)
        {
            _window.LocationChanged += WindowLocationChanged;
            _window.SizeChanged += OwnerSizeChanged;
            _window.Deactivated += WindowDeactivated;
        }
        InputManager.Current.PreProcessInput += ObserveInput;
        _ownsInput = true;
        try
        {
            if (!PreparePopupBounds()) { ReleaseOwnership(); return; }
            AdjustmentPopup.IsOpen = true;
        }
        catch { ReleaseOwnership(); throw; }
        LanguageCombo.Focus();
    }

    private void ClosePopup(bool returnFocus = false)
    {
        if (!AdjustmentPopup.IsOpen && !_ownsInput) return;
        LanguageCombo.IsDropDownOpen = false;
        AdjustmentPopup.IsOpen = false;
        ReleaseOwnership();
        if (returnFocus && AdjustmentButton.IsLoaded && AdjustmentButton.IsVisible) AdjustmentButton.Focus();
    }

    private void ReleaseOwnership()
    {
        if (!_ownsInput) return;
        _ownsInput = false;
        LanguageCombo.IsDropDownOpen = false;
        _comboPopup = null;
        InputManager.Current.PreProcessInput -= ObserveInput;
        foreach (var viewer in _scrollOwners)
        {
            viewer.ScrollChanged -= OwnerScrollChanged;
            viewer.SizeChanged -= OwnerSizeChanged;
        }
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
            Release(_owner, _ownershipToken);
        }
        _owner = null;
        _window = null;
    }

    private void ObserveInput(object sender, PreProcessInputEventArgs args)
    {
        if (args.StagingItem.Input is KeyEventArgs { Key: Key.Escape } key
            && (key.RoutedEvent == Keyboard.PreviewKeyDownEvent || key.RoutedEvent == Keyboard.KeyDownEvent))
        {
            if (LanguageCombo.IsDropDownOpen) LanguageCombo.IsDropDownOpen = false;
            else ClosePopup(true);
            key.Handled = true;
        }
        else if (args.StagingItem.Input is MouseButtonEventArgs mouse
                 && (mouse.RoutedEvent == Mouse.PreviewMouseDownEvent || mouse.RoutedEvent == Mouse.MouseDownEvent)
                 && (mouse.OriginalSource ?? Mouse.DirectlyOver) is DependencyObject source
                 && !IsWithin(source, AdjustmentButton) && !IsWithin(source, PopupSurface)
                 && !(_comboPopup is { IsOpen: true, Child: { } child } && IsWithin(source, child))) ClosePopup();
    }

    private void Close_Click(object sender, RoutedEventArgs args) { ClosePopup(true); args.Handled = true; }
    private void Reset_Click(object sender, RoutedEventArgs args)
    {
        LanguageCombo.SelectedIndex = 0;
        RelevanceRadio.IsChecked = true;
        args.Handled = true;
    }
    private async void Apply_Click(object sender, RoutedEventArgs args)
    {
        args.Handled = true;
        if (!AdjustmentPopup.IsOpen || DataContext is not SettingsViewModel vm) return;
        var language = (LanguageCombo.SelectedItem as SearchLanguageOption)?.Code;
        var sort = PopularityRadio.IsChecked == true ? "task_count" : CreationRadio.IsChecked == true ? "created_at" : "score";
        ClosePopup(true);
        await vm.ApplySearchOptionsAsync(language, sort);
    }
    private void Popup_PreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && !LanguageCombo.IsDropDownOpen && Keyboard.FocusedElement is not Button)
            Apply_Click(sender, args);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Reposition();
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
    private DpiScale ScreenScale()
    {
        var transform = PresentationSource.FromVisual(AdjustmentButton)?.CompositionTarget?.TransformToDevice;
        return transform is { } matrix ? new DpiScale(matrix.M11, matrix.M22) : VisualTreeHelper.GetDpi(AdjustmentButton);
    }
    private static Rect ScreenBounds(FrameworkElement element) => new(element.PointToScreen(new Point()),
        element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight)));

    private Rect VisibleViewport()
    {
        var bounds = VoiceLanguageTags.MonitorWorkArea(AdjustmentButton.PointToScreen(new Point()));
        if (_owner is { ActualWidth: > 0, ActualHeight: > 0 }) bounds.Intersect(ScreenBounds(_owner));
        foreach (var viewer in _scrollOwners)
        {
            // ViewportHeight may be logical item units. Measure the actual rendered
            // presenter instead of the complete, possibly scrolled settings content.
            var presenter = viewer.Template?.FindName("PART_ScrollContentPresenter", viewer) as FrameworkElement
                ?? VisualChildren(viewer).OfType<ScrollContentPresenter>().FirstOrDefault() ?? (FrameworkElement)viewer;
            bounds.Intersect(ScreenBounds(presenter));
        }
        return bounds;
    }
    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in VisualChildren(child)) yield return descendant;
        }
    }
    private bool PreparePopupBounds()
    {
        var dpi = ScreenScale();
        var bounds = VisibleViewport();
        if (bounds.IsEmpty || bounds.Width <= 16 * dpi.DpiScaleX || bounds.Height <= 16 * dpi.DpiScaleY) return false;
        bounds.Inflate(-8 * dpi.DpiScaleX, -8 * dpi.DpiScaleY);
        PopupSurface.Width = Math.Min(324, bounds.Width / dpi.DpiScaleX);
        var anchor = AdjustmentButton.PointToScreen(new Point());
        var anchorBottom = AdjustmentButton.PointToScreen(new Point(0, AdjustmentButton.ActualHeight)).Y;
        var below = Math.Max(0, bounds.Bottom - anchorBottom - 8 * dpi.DpiScaleY);
        var above = Math.Max(0, anchor.Y - 8 * dpi.DpiScaleY - bounds.Top);
        PopupSurface.MaxHeight = double.PositiveInfinity;
        PopupSurface.Measure(new Size(PopupSurface.Width, double.PositiveInfinity));
        var naturalHeight = PopupSurface.DesiredSize.Height * dpi.DpiScaleY;
        var availableHeight = naturalHeight <= below ? below : naturalHeight <= above ? above : Math.Max(above, below);
        PopupSurface.MaxHeight = Math.Max(1, Math.Min(bounds.Height, availableHeight) / dpi.DpiScaleY);
        return true;
    }
    private void Reposition()
    {
        if (AdjustmentPopup is null || !AdjustmentPopup.IsOpen || _owner is null) return;
        LanguageCombo.IsDropDownOpen = false;
        if (!PreparePopupBounds()) { ClosePopup(); return; }
        AdjustmentPopup.HorizontalOffset = AdjustmentPopup.HorizontalOffset == 0 ? .01 : 0;
    }
    private CustomPopupPlacement[] PlacePopup(Size popupSize, Size targetSize, Point offset)
    {
        var anchor = AdjustmentButton.PointToScreen(new Point());
        var dpi = ScreenScale();
        var bounds = VisibleViewport();
        if (bounds.IsEmpty) return [];
        bounds.Inflate(-8 * dpi.DpiScaleX, -8 * dpi.DpiScaleY);
        // Native custom-placement sizes and offsets are already device pixels.
        var left = Math.Clamp(anchor.X + targetSize.Width - popupSize.Width, bounds.Left, Math.Max(bounds.Left, bounds.Right - popupSize.Width));
        var below = bounds.Bottom - anchor.Y - targetSize.Height - 8 * dpi.DpiScaleY;
        var above = anchor.Y - 8 * dpi.DpiScaleY - bounds.Top;
        var useAbove = popupSize.Height > below && (popupSize.Height <= above || above > below);
        var top = useAbove ? anchor.Y - 8 * dpi.DpiScaleY - popupSize.Height : anchor.Y + targetSize.Height + 8 * dpi.DpiScaleY;
        top = Math.Clamp(top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - popupSize.Height));
        return [new CustomPopupPlacement(new Point(left - anchor.X, top - anchor.Y), PopupPrimaryAxis.None)];
    }
}
