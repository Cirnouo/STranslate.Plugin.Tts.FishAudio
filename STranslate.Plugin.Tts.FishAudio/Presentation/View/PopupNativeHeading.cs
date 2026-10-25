using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace STranslate.Plugin.Tts.FishAudio.View;

internal interface IPopupWindowTextApi
{
    string Read(IntPtr handle);
    void Write(IntPtr handle, string text);
}

internal sealed class NativePopupWindowTextApi : IPopupWindowTextApi
{
    private const int MaximumLength = 4096;
    public string Read(IntPtr handle)
    {
        var length = GetWindowTextLengthW(handle);
        var error = Marshal.GetLastPInvokeError();
        if (length == 0 && error != 0) throw new Win32Exception(error);
        if (length >= MaximumLength) throw new InvalidOperationException("Popup native text exceeds its read bound.");
        var text = new StringBuilder(MaximumLength + 1);
        var copied = GetWindowTextW(handle, text, text.Capacity);
        error = Marshal.GetLastPInvokeError();
        if (copied == 0 && error != 0) throw new Win32Exception(error);
        if (copied >= MaximumLength) throw new InvalidOperationException("Popup native text reached its read bound.");
        return text.ToString();
    }
    public void Write(IntPtr handle, string text)
    {
        if (!SetWindowTextW(handle, text)) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetWindowTextLengthW(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetWindowTextW(IntPtr handle, StringBuilder text, int maximum);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetWindowTextW(IntPtr handle, string text);
}

// Optional identity on one live source. Another writer's text ends our ownership;
// neither identity failure nor restoration can affect material or popup input.
internal sealed class PopupHeadingLease
{
    private readonly IntPtr _handle;
    private readonly Func<bool> _isLive;
    private readonly IPopupWindowTextApi _api;
    private string? _prior;
    private string? _lastWritten;
    private bool _retired;
    internal Exception? Failure { get; private set; }
    internal Exception? CleanupFailure { get; private set; }

    internal PopupHeadingLease(IntPtr handle, Func<bool> isLive, string heading, IPopupWindowTextApi api)
    {
        _handle = handle;
        _isLive = isLive;
        _api = api;
        try
        {
            if (!_isLive() || heading.Length == 0) return;
            _prior = _api.Read(_handle);
            _api.Write(_handle, heading);
            _lastWritten = heading;
        }
        catch (Exception error) { Failure = error; }
    }

    internal void Update(string heading)
    {
        if (_retired || _lastWritten is null || heading.Length == 0 || heading == _lastWritten) return;
        try
        {
            if (!_isLive()) return;
            if (_api.Read(_handle) != _lastWritten) { _lastWritten = null; return; }
            _api.Write(_handle, heading);
            _lastWritten = heading;
        }
        catch (Exception error) { Failure ??= error; }
    }

    internal void End()
    {
        if (_retired) return;
        _retired = true;
        try
        {
            if (_lastWritten is not null && _isLive() && _api.Read(_handle) == _lastWritten)
                _api.Write(_handle, _prior!);
        }
        catch (Exception error) { CleanupFailure ??= error; }
    }
}

internal sealed class PopupNativeHeading(Border surface, string resourceKey, IPopupWindowTextApi? api = null)
{
    private static readonly DependencyProperty OwnerProperty = DependencyProperty.RegisterAttached(
        "Owner", typeof(PopupNativeHeading), typeof(PopupNativeHeading));
    private static readonly DependencyProperty HeadingProperty = DependencyProperty.RegisterAttached(
        "Heading", typeof(string), typeof(PopupNativeHeading), new PropertyMetadata("", HeadingChanged));
    private readonly IPopupWindowTextApi _api = api ?? new NativePopupWindowTextApi();
    private PopupHeadingLease? _lease;
    private bool _active;

    internal void Begin()
    {
        if (_active) return;
        _active = true;
        surface.SetValue(OwnerProperty, this);
        surface.SetResourceReference(HeadingProperty, resourceKey);
    }
    internal void Attach(HwndSource source)
    {
        RetireSource();
        if (!_active || source.IsDisposed) return;
        var handle = source.Handle;
        _lease = new PopupHeadingLease(handle, () => !source.IsDisposed && source.Handle == handle,
            (string)surface.GetValue(HeadingProperty), _api);
    }
    internal void RetireSource()
    {
        var lease = _lease;
        _lease = null;
        lease?.End();
    }
    internal void End()
    {
        if (!_active) return;
        _active = false;
        RetireSource();
        surface.ClearValue(OwnerProperty);
        surface.ClearValue(HeadingProperty);
    }
    private static void HeadingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender.GetValue(OwnerProperty) is PopupNativeHeading { _active: true } owner)
            owner._lease?.Update((string)args.NewValue);
    }
}
