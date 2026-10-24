using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static STranslate.Plugin.Tts.FishAudio.View.SettingsOverlayOwnership;

namespace STranslate.Plugin.Tts.FishAudio.View;

internal sealed record SearchLanguageOption(string? Code, string Name, ImageSource? Flag);

public partial class SearchAdjustmentControl : UserControl
{
    private readonly HashSet<string> _draftLanguages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ToggleButton> _languageTiles = new(StringComparer.Ordinal);
    private static readonly DependencyProperty UiLocaleProperty = DependencyProperty.Register("UiLocale", typeof(string),
        typeof(SearchAdjustmentControl), new PropertyMetadata("en", LabelsChanged));
    private static readonly DependencyProperty AllCaptionProperty = DependencyProperty.Register("AllCaption", typeof(string),
        typeof(SearchAdjustmentControl), new PropertyMetadata("", LabelsChanged));
    private readonly object _ownershipToken = new();
    private readonly List<ScrollViewer> _scrollOwners = [];
    private FrameworkElement? _owner;
    private Window? _window;
    private bool _ownsInput;
    private bool _updatingDraftTiles;

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
        RefreshLabels();
    }

    private static void LabelsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((SearchAdjustmentControl)sender).RefreshLabels();

    private void RefreshLabels()
    {
        if (LanguageTiles is null) return;
        var locale = (string)GetValue(UiLocaleProperty);
        var options = new List<SearchLanguageOption> { new(null, (string)GetValue(AllCaptionProperty), null) };
        foreach (var code in VoiceSearchCriteria.PresetLanguages)
        {
            var label = VoiceLanguagePresentation.Describe(code, locale);
            options.Add(new(code, label.Name, label.Flag));
        }
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            var code = option.Code ?? "";
            if (!_languageTiles.TryGetValue(code, out var tile))
            {
                tile = new ToggleButton { Tag = code, Style = (Style)FindResource("LanguageTileStyle") };
                tile.Checked += LanguageTile_CheckedChanged;
                tile.Unchecked += LanguageTile_CheckedChanged;
                Grid.SetRow(tile, index == 0 ? 0 : ((index + 1) / 2) * 2);
                Grid.SetColumn(tile, index == 0 ? 0 : ((index - 1) % 2) * 2);
                _languageTiles.Add(code, tile);
                LanguageTiles.Children.Add(tile);
            }
            tile.Content = option;
            tile.ToolTip = option.Name;
            AutomationProperties.SetName(tile, option.Name);
        }
        UpdateDraftTiles();
    }

    private void LanguageTile_CheckedChanged(object sender, RoutedEventArgs args)
    {
        if (_updatingDraftTiles) return;
        var tile = (ToggleButton)sender;
        var code = (string)tile.Tag;
        if (code.Length == 0) _draftLanguages.Clear();
        else if (tile.IsChecked == true) _draftLanguages.Add(code);
        else _draftLanguages.Remove(code);
        UpdateDraftTiles();
        args.Handled = true;
    }

    private void UpdateDraftTiles()
    {
        _updatingDraftTiles = true;
        try
        {
            foreach (var (code, tile) in _languageTiles)
                tile.IsChecked = code.Length == 0 ? _draftLanguages.Count == 0 : _draftLanguages.Contains(code);
        }
        finally { _updatingDraftTiles = false; }
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
        _draftLanguages.Clear();
        _draftLanguages.UnionWith(vm.SearchLanguages);
        UpdateDraftTiles();
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
        _languageTiles[""].Focus();
    }

    private void ClosePopup(bool returnFocus = false)
    {
        if (!AdjustmentPopup.IsOpen && !_ownsInput) return;
        AdjustmentPopup.IsOpen = false;
        ReleaseOwnership();
        if (returnFocus && AdjustmentButton.IsLoaded && AdjustmentButton.IsVisible) AdjustmentButton.Focus();
    }

    private void ReleaseOwnership()
    {
        if (!_ownsInput) return;
        _ownsInput = false;
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
            ClosePopup(true);
            key.Handled = true;
        }
        else if (args.StagingItem.Input is MouseButtonEventArgs mouse
                 && (mouse.RoutedEvent == Mouse.PreviewMouseDownEvent || mouse.RoutedEvent == Mouse.MouseDownEvent)
                 && (mouse.OriginalSource ?? Mouse.DirectlyOver) is DependencyObject source
                 && !IsWithin(source, AdjustmentButton) && !IsWithin(source, PopupSurface)) ClosePopup();
    }

    private void Close_Click(object sender, RoutedEventArgs args) { ClosePopup(true); args.Handled = true; }
    private void Reset_Click(object sender, RoutedEventArgs args)
    {
        _draftLanguages.Clear();
        UpdateDraftTiles();
        RelevanceRadio.IsChecked = true;
        args.Handled = true;
    }
    private async void Apply_Click(object sender, RoutedEventArgs args)
    {
        args.Handled = true;
        if (!AdjustmentPopup.IsOpen || DataContext is not SettingsViewModel vm) return;
        var languages = VoiceSearchCriteria.PresetLanguages.Where(_draftLanguages.Contains).ToArray();
        var sort = PopularityRadio.IsChecked == true ? "task_count" : CreationRadio.IsChecked == true ? "created_at" : "score";
        ClosePopup(true);
        await vm.ApplySearchOptionsAsync(languages, sort);
    }
    private void Popup_PreviewKeyDown(object sender, KeyEventArgs args)
    {
        var source = args.OriginalSource as DependencyObject;
        if (args.Key == Key.Enter && Keyboard.FocusedElement is not ButtonBase
            && source is not ButtonBase && (source is null || !Ancestors(source).OfType<ButtonBase>().Any()))
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
