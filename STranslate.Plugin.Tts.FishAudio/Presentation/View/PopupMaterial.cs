using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using iNKORE.UI.WPF.Modern.Helpers.Styles;

namespace STranslate.Plugin.Tts.FishAudio.View;

internal readonly record struct PopupBackdropResult(int FrameHResult, int ReadbackHResult, int BackdropType)
{
    internal bool AttributeAccepted => FrameHResult >= 0 && ReadbackHResult >= 0 && BackdropType == 3;
}

internal interface IPopupBackdropApi
{
    bool IsSupported { get; }
    PopupBackdropResult Apply(IntPtr handle, bool dark);
    void UpdateChrome(IntPtr handle, bool dark);
    void Remove(IntPtr handle);
}

internal sealed class NativePopupBackdropApi : IPopupBackdropApi
{
    public bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);
    public PopupBackdropResult Apply(IntPtr handle, bool dark)
    {
        var margins = new FrameMargins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        var frameResult = DwmExtendFrameIntoClientArea(handle, ref margins);
        BackdropHelper.Apply(handle, BackdropType.Acrylic11);
        UpdateChrome(handle, dark);
        CornerHelper.SetWindowCorners(handle, WindowCornerStyle.Round);
        // The toolkit's bool ignores the native setter HRESULT. Read the documented
        // attribute on this HWND; this establishes acceptance, never visible blur.
        var readResult = DwmGetWindowAttribute(handle, 38, out var actualType, sizeof(int));
        return new(frameResult, readResult, actualType);
    }

    public void UpdateChrome(IntPtr handle, bool dark)
    {
        if (dark) BackdropHelper.ApplyDarkMode(handle);
        else BackdropHelper.RemoveDarkMode(handle);
        CornerHelper.SetWindowCorners(handle, WindowCornerStyle.Round);
    }

    public void Remove(IntPtr handle)
    {
        BackdropHelper.Remove(handle);
        var margins = new FrameMargins();
        DwmExtendFrameIntoClientArea(handle, ref margins);
        CornerHelper.SetWindowCorners(handle, WindowCornerStyle.Default);
    }

    [StructLayout(LayoutKind.Sequential)] private struct FrameMargins { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr handle, ref FrameMargins margins);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int size);
}

// One owner per popup, active only for its open lifetime. No host HWND, theme
// singleton, private toolkit reflection or second Window participates.
internal sealed class PopupMaterial(Popup popup, Border surface, IPopupBackdropApi? api = null)
{
    private static readonly DependencyProperty OwnerProperty = DependencyProperty.RegisterAttached(
        "Owner", typeof(PopupMaterial), typeof(PopupMaterial));
    private static readonly DependencyProperty NativeBrushProperty = DependencyProperty.RegisterAttached(
        "NativeBrush", typeof(Brush), typeof(PopupMaterial), new PropertyMetadata(null, NativeBrushChanged));
    private readonly IPopupBackdropApi _api = api ?? new NativePopupBackdropApi();
    private HwndSource? _source;
    private bool _active;
    private bool _attempted;
    internal PopupBackdropResult Result { get; private set; }
    internal Exception? Failure { get; private set; }
    internal Exception? CleanupFailure { get; private set; }
    internal IntPtr Handle => _source is { IsDisposed: false } source ? source.Handle : IntPtr.Zero;
    internal bool AttributeAccepted => _source is { IsDisposed: false, UsesPerPixelOpacity: false } && _attempted && Failure is null && Result.AttributeAccepted;

    internal void Begin()
    {
        if (_active) return;
        _active = true;
        _attempted = false;
        Result = default;
        Failure = null;
        CleanupFailure = null;
        // WPF consumes this when creating the native source, before Opened.
        popup.AllowsTransparency = !_api.IsSupported;
        surface.SetValue(OwnerProperty, this);
        surface.SetResourceReference(NativeBrushProperty, "FlyoutPresenterBackground");
        popup.Opened += Opened;
        popup.Closed += Closed;
        surface.Unloaded += Unloaded;
        PresentationSource.AddSourceChangedHandler(surface, SourceChanged);
    }

    internal void End()
    {
        if (!_active) return;
        _active = false;
        popup.Opened -= Opened;
        popup.Closed -= Closed;
        surface.Unloaded -= Unloaded;
        PresentationSource.RemoveSourceChangedHandler(surface, SourceChanged);
        try { RetireSource(); }
        finally
        {
            surface.ClearValue(OwnerProperty);
            surface.ClearValue(NativeBrushProperty);
            surface.SetResourceReference(Border.BackgroundProperty, "FlyoutPresenterBackground");
        }
    }

    private void Opened(object? sender, EventArgs args) => AttachSource();
    private void Closed(object? sender, EventArgs args) => End();
    private void Unloaded(object sender, RoutedEventArgs args) => End();
    private void SourceChanged(object sender, SourceChangedEventArgs args)
    {
        if (!_active) return;
        AttachSource();
    }
    private void SourceDisposed(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, _source)) return;
        RetireSource();
    }

    private void AttachSource()
    {
        if (!_active || !popup.IsOpen) return;
        var source = PresentationSource.FromVisual(surface) as HwndSource;
        if (source is null || source.IsDisposed || ReferenceEquals(source, _source)) return;
        RetireSource();
        _source = source;
        source.Disposed += SourceDisposed;
        if (_api.IsSupported && !source.UsesPerPixelOpacity)
        {
            _attempted = true;
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            // Optional native interop must not throw through WPF source creation.
            // Retain the first failure privately; only this capability falls back.
            var dark = IsDark();
            try { Result = _api.Apply(source.Handle, dark); }
            catch (Exception error) { Failure = error; }
            if (!AttributeAccepted) RemoveNative(source.Handle);
        }
        UpdateSurface();
    }

    private void RetireSource()
    {
        var source = _source;
        _source = null;
        if (source is null) return;
        source.Disposed -= SourceDisposed;
        if (!source.IsDisposed && _attempted) RemoveNative(source.Handle);
        _attempted = false;
    }

    private void RemoveNative(IntPtr handle)
    {
        try { _api.Remove(handle); }
        catch (Exception error) { CleanupFailure ??= error; }
    }

    private static void NativeBrushChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender.GetValue(OwnerProperty) is PopupMaterial owner) owner.UpdateSurface();
    }
    private bool IsDark() => surface.GetValue(NativeBrushProperty) is SolidColorBrush brush
        && brush.Color.R + brush.Color.G + brush.Color.B < 384;
    private void UpdateSurface()
    {
        if (!_active) return;
        var chromeFailed = false;
        var dark = IsDark();
        if (AttributeAccepted)
        {
            try { _api.UpdateChrome(_source!.Handle, dark); }
            catch (Exception error)
            {
                Failure ??= error;
                chromeFailed = true;
                RemoveNative(_source!.Handle);
            }
            if (AttributeAccepted) { surface.Background = Brushes.Transparent; return; }
        }
        surface.SetResourceReference(Border.BackgroundProperty, "FlyoutPresenterBackground");
        if (_source is { IsDisposed: false, UsesPerPixelOpacity: false } source && surface.Background is SolidColorBrush brush)
        {
            source.CompositionTarget.BackgroundColor = brush.Color;
            if (!chromeFailed)
            {
                try { _api.UpdateChrome(source.Handle, dark); }
                catch (Exception error) { Failure ??= error; }
            }
        }
    }
}
