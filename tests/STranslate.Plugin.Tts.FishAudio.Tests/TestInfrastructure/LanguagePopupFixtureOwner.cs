using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using STranslate.Plugin.Tts.FishAudio.View;

internal sealed class LanguagePopupFixtureHooks
{
    internal Action<HwndSource>? SourceAcquired;
    internal Action<LanguagePopupFixtureProbe>? PopupOpened;
    internal Action<LanguagePopupFixtureOwner>? SourceRetiring;
    internal Action<HwndSource>? SourceDisposed;
}

internal sealed record LanguagePopupFixtureProbe(HwndSource HostSource,
    VoiceLanguageTags Tags, NativePopupLifetimeOwner Lifetime, int OpenOrdinal);

// The three initialization rows and one continuous input row start no async
// command work. One returned host and every actual Popup lifetime belong to
// their existing STA root, including failure before the first RootVisual.
internal sealed class LanguagePopupFixtureOwner(LanguagePopupFixtureHooks? hooks = null)
{
    internal HwndSource? Source;
    internal VoiceLanguageTags? Tags;
    internal Action? RequestClose;
    private NativePopupLifetimeOwner? _current;
    private readonly List<NativePopupLifetimeOwner> _lifetimes = [];
    internal IReadOnlyList<NativePopupLifetimeOwner> Lifetimes => _lifetimes;

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(body,
        RequestCloseAsync, JoinCurrentAsync, DetachLifetimesAsync,
        ObserveSourceRetiringAsync, DisposeSourceAsync);

    internal HwndSource AcquireSource(Func<HwndSource> acquire)
    {
        if (Source is not null) throw new InvalidOperationException("language-popup-host-already-owned");
        Source = acquire();
        hooks?.SourceAcquired?.Invoke(Source);
        return Source;
    }

    internal void BeginOpen(VoiceLanguageTags tags)
    {
        if (_current is not null) throw new InvalidOperationException("language-popup-prior-lifetime-pending");
        _current = new NativePopupLifetimeOwner((Popup)tags.FindName("LanguagePopup"));
        _lifetimes.Add(_current);
    }

    internal void CaptureSource() => _current?.CaptureSource();

    internal void Open(VoiceLanguageTags tags, Action originalOpen)
    {
        BeginOpen(tags);
        try
        {
            originalOpen();
            // The original scenario includes guarded attempts before measure.
            // Observe acquired lifetimes without requiring a refused attempt to open.
            if (tags.IsPopupOpen)
            {
                _current!.RequireOpenedSource();
                hooks?.PopupOpened?.Invoke(new LanguagePopupFixtureProbe(Source!, tags, _current, _lifetimes.Count));
            }
        }
        finally { CaptureSource(); }
    }

    internal async Task JoinCurrentAsync()
    {
        if (_current is null) return;
        await _current.CloseAndJoinAsync();
        _current.Detach();
        _current = null;
    }

    private Task RequestCloseAsync() { RequestClose?.Invoke(); return Task.CompletedTask; }

    private Task DetachLifetimesAsync()
    {
        foreach (var lifetime in _lifetimes) lifetime.Detach();
        return Task.CompletedTask;
    }

    private Task ObserveSourceRetiringAsync()
    {
        if (Source is not null) hooks?.SourceRetiring?.Invoke(this);
        return Task.CompletedTask;
    }

    private Task DisposeSourceAsync()
    {
        if (Source is not null)
        {
            Source.Dispose();
            hooks?.SourceDisposed?.Invoke(Source);
        }
        return Task.CompletedTask;
    }
}
