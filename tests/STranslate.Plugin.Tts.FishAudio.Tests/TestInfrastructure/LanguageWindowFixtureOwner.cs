using System.Windows;
using System.Windows.Controls.Primitives;
using STranslate.Plugin.Tts.FishAudio.View;

internal sealed class LanguageWindowFixtureHooks
{
    internal Action<LanguageWindowFixtureProbe>? PopupOpened;
    internal Action<LanguageWindowFixtureOwner>? WindowRetiring;
    internal Action<LanguageWindowFixtureOwner>? WindowRetired;
}

internal sealed record LanguageWindowFixtureProbe(NativeWindowLifetimeOwner WindowLifetime,
    VoiceLanguageTags Tags, NativePopupLifetimeOwner PopupLifetime, int OpenOrdinal);

// One original Window, two original Popups, no ViewModel or command work.
// The normal tail uses original Close calls and actual native event joins.
internal sealed class LanguageWindowFixtureOwner(LanguageWindowFixtureHooks? hooks = null)
{
    internal VoiceLanguageTags? Tags;
    internal Action? RequestPopupClose;
    internal NativeWindowLifetimeOwner? WindowLifetime;
    private NativePopupLifetimeOwner? _current;
    private readonly List<NativePopupLifetimeOwner> _lifetimes = [];
    internal IReadOnlyList<NativePopupLifetimeOwner> Lifetimes => _lifetimes;

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(body,
        RequestPopupCloseAsync, JoinCurrentAsync, DetachLifetimesAsync,
        ObserveWindowRetiringAsync, CloseWindowAsync, DetachWindowAsync, ObserveWindowRetiredAsync);

    internal Window AcquireWindow(Func<Window> acquire)
    {
        if (WindowLifetime is not null) throw new InvalidOperationException("language-window-already-owned");
        var window = acquire();
        WindowLifetime = new NativeWindowLifetimeOwner(window);
        return window;
    }

    internal void RequireShownSource() => WindowLifetime!.RequireShownSource();

    internal void Open(VoiceLanguageTags tags, Action originalOpen)
    {
        if (_current is not null) throw new InvalidOperationException("language-window-prior-popup-pending");
        _current = new NativePopupLifetimeOwner((Popup)tags.FindName("LanguagePopup"));
        _lifetimes.Add(_current);
        try
        {
            originalOpen();
            _current.RequireOpenedSource();
            hooks?.PopupOpened?.Invoke(new LanguageWindowFixtureProbe(WindowLifetime!, tags, _current, _lifetimes.Count));
        }
        finally { _current.CaptureSource(); }
    }

    internal async Task JoinCurrentAsync()
    {
        if (_current is null) return;
        await _current.CloseAndJoinAsync();
        _current.Detach();
        _current = null;
    }

    private Task RequestPopupCloseAsync() { RequestPopupClose?.Invoke(); return Task.CompletedTask; }
    private Task DetachLifetimesAsync()
    {
        foreach (var lifetime in _lifetimes) lifetime.Detach();
        return Task.CompletedTask;
    }
    private Task ObserveWindowRetiringAsync()
    {
        if (WindowLifetime is not null) hooks?.WindowRetiring?.Invoke(this);
        return Task.CompletedTask;
    }
    private Task CloseWindowAsync() => WindowLifetime?.CloseAndJoinAsync() ?? Task.CompletedTask;
    private Task DetachWindowAsync() { WindowLifetime?.Detach(); return Task.CompletedTask; }
    private Task ObserveWindowRetiredAsync()
    {
        if (WindowLifetime is not null) hooks?.WindowRetired?.Invoke(this);
        return Task.CompletedTask;
    }
}
