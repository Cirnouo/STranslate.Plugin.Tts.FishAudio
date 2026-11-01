using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

internal sealed class SearchViewportFixtureHooks
{
    internal Action<SearchViewportFixtureProbe>? PopupOpened;
    internal Action<SearchViewportFixtureOwner>? SourceRetiring;
    internal Action<HwndSource>? SourceDisposed;
}

internal sealed record SearchViewportFixtureProbe(HwndSource HostSource, SettingsViewModel ViewModel,
    FrameworkElement Control, NativePopupLifetimeOwner Lifetime, int AttemptOrdinal);

// The viewport row preserves one host/VM across the Search width/refusal chain
// and its same-source Language replacement. Every attempted open is observed.
internal sealed class SearchViewportFixtureOwner(SearchViewportFixtureHooks? hooks = null)
{
    internal SettingsViewModel? ViewModel;
    internal HwndSource? Source;
    internal Task? InitialApplyTask;
    internal Action? RequestSearchClose;
    internal Action? RequestLanguageClose;
    private NativePopupLifetimeOwner? _current;
    private readonly List<NativePopupLifetimeOwner> _lifetimes = [];
    internal IReadOnlyList<NativePopupLifetimeOwner> Lifetimes => _lifetimes;

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(body,
        RequestSearchCloseAsync, RequestLanguageCloseAsync, JoinCurrentAsync, DetachLifetimesAsync,
        ObserveSourceRetiringAsync, DisposeSourceAsync, DisposeViewModelAsync, JoinInitialApplyAsync);

    internal HwndSource AcquireSource(Func<HwndSource> acquire)
    {
        if (Source is not null) throw new InvalidOperationException("search-viewport-host-already-owned");
        Source = acquire();
        return Source;
    }

    internal void Attempt(Popup popup, FrameworkElement control, Action originalOpen, bool requireOpened)
    {
        if (_current is not null) throw new InvalidOperationException("search-viewport-prior-lifetime-pending");
        _current = new NativePopupLifetimeOwner(popup);
        _lifetimes.Add(_current);
        try
        {
            originalOpen();
            if (requireOpened) _current.RequireOpenedSource();
            if (_current.Source is { IsDisposed: false })
                hooks?.PopupOpened?.Invoke(new SearchViewportFixtureProbe(Source!, ViewModel!, control,
                    _current, _lifetimes.Count));
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

    private Task RequestSearchCloseAsync() { RequestSearchClose?.Invoke(); return Task.CompletedTask; }
    private Task RequestLanguageCloseAsync() { RequestLanguageClose?.Invoke(); return Task.CompletedTask; }
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
    private Task DisposeViewModelAsync() { ViewModel?.Dispose(); return Task.CompletedTask; }
    private Task JoinInitialApplyAsync() => InitialApplyTask ?? Task.CompletedTask;
}
