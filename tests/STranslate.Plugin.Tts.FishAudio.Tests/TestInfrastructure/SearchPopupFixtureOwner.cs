using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

internal sealed class SearchPopupFixtureHooks
{
    internal Action<SearchPopupFixtureProbe>? PopupOpened;
    internal Action<HwndSource>? SourceRetiring;
    internal Action<HwndSource>? SourceDisposed;
}

internal sealed record SearchPopupFixtureProbe(HwndSource HostSource, SettingsViewModel ViewModel,
    NativePopupLifetimeOwner Lifetime, FrameworkElement Control, PopupMaterial? Material);

// The eleven Radio/material scenes start no Apply work and use no synthetic
// keyboard transaction. Existing held-Apply ownership remains a separate owner.
internal sealed class SearchPopupFixtureOwner(SearchPopupFixtureHooks? hooks = null)
{
    internal SettingsViewModel? ViewModel;
    internal HwndSource? Source;
    internal Action? RequestClose;
    internal Action? EndMaterial;
    internal Action? DrainOriginal;
    private NativePopupLifetimeOwner? _current;
    private readonly List<NativePopupLifetimeOwner> _lifetimes = [];

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(body,
        RequestCloseAsync, EndMaterialAsync, DrainOriginalAsync, JoinCurrentAsync,
        DetachLifetimesAsync, ObserveSourceRetiringAsync, DisposeSourceAsync, DisposeViewModelAsync);

    internal void Open(Popup popup, FrameworkElement control, Action originalOpen, PopupMaterial? material = null)
    {
        if (_current is not null) throw new InvalidOperationException("search-popup-prior-lifetime-pending");
        _current = new NativePopupLifetimeOwner(popup);
        _lifetimes.Add(_current);
        try
        {
            originalOpen();
            _current.RequireOpenedSource();
            hooks?.PopupOpened?.Invoke(new SearchPopupFixtureProbe(Source!, ViewModel!, _current, control, material));
        }
        finally { _current.CaptureSource(); }
    }

    internal void RecordCloseSetterReturn() => _current?.RecordCloseSetterReturn();

    internal async Task JoinCurrentAsync()
    {
        if (_current is null) return;
        await _current.CloseAndJoinAsync();
        _current.Detach();
        _current = null;
    }

    private Task RequestCloseAsync() { RequestClose?.Invoke(); return Task.CompletedTask; }
    private Task EndMaterialAsync() { EndMaterial?.Invoke(); return Task.CompletedTask; }
    private Task DrainOriginalAsync() { DrainOriginal?.Invoke(); return Task.CompletedTask; }

    private Task DetachLifetimesAsync()
    {
        foreach (var lifetime in _lifetimes) lifetime.Detach();
        return Task.CompletedTask;
    }

    private Task ObserveSourceRetiringAsync()
    {
        if (Source is not null) hooks?.SourceRetiring?.Invoke(Source);
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

    private Task DisposeViewModelAsync()
    {
        ViewModel?.Dispose();
        return Task.CompletedTask;
    }
}
