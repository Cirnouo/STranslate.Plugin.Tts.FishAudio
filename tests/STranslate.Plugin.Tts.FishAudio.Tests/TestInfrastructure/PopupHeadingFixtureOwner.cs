using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

internal sealed class PopupHeadingFixtureHooks
{
    internal Action<PopupHeadingFixtureProbe>? PopupOpened;
    internal Action<HwndSource>? SourceRetiring;
    internal Action<HwndSource>? SourceDisposed;
}

internal sealed record PopupHeadingFixtureProbe(HwndSource HostSource, SettingsViewModel? ViewModel,
    NativePopupLifetimeOwner Lifetime, FrameworkElement Control, PopupMaterial? Material);

// Five rendered Heading owners only. The source-replacement/native-text and
// pure lease chains retain their existing owners and remain separate work.
internal sealed class PopupHeadingFixtureOwner(PopupHeadingFixtureHooks? hooks = null)
{
    internal SettingsViewModel? ViewModel;
    internal HwndSource? Source;
    internal bool OriginalFinallyActive;
    internal Action? RequestClose;
    internal Action? EndMaterial;
    internal Action? DrainOriginal;
    internal Action? ReleaseListener;
    private NativePopupLifetimeOwner? _current;
    private readonly List<NativePopupLifetimeOwner> _lifetimes = [];

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(body,
        RequestCloseAsync, EndMaterialAsync, DrainOriginalAsync, JoinCurrentAsync,
        DetachLifetimesAsync, ReleaseListenerAsync, ObserveSourceRetiringAsync,
        DisposeSourceAsync, DisposeViewModelAsync);

    internal void Open(Popup popup, FrameworkElement control, Action originalOpen, PopupMaterial? material = null)
    {
        if (_current is not null) throw new InvalidOperationException("popup-heading-prior-lifetime-pending");
        _current = new NativePopupLifetimeOwner(popup);
        _lifetimes.Add(_current);
        try
        {
            originalOpen();
            _current.RequireOpenedSource();
            hooks?.PopupOpened?.Invoke(new PopupHeadingFixtureProbe(Source!, ViewModel, _current, control, material));
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

    private Task RequestCloseAsync()
    {
        if (OriginalFinallyActive) RequestClose?.Invoke();
        return Task.CompletedTask;
    }

    private Task EndMaterialAsync()
    {
        if (OriginalFinallyActive) EndMaterial?.Invoke();
        return Task.CompletedTask;
    }

    private Task DrainOriginalAsync()
    {
        if (OriginalFinallyActive) DrainOriginal?.Invoke();
        return Task.CompletedTask;
    }

    private Task DetachLifetimesAsync()
    {
        foreach (var lifetime in _lifetimes) lifetime.Detach();
        return Task.CompletedTask;
    }

    private Task ReleaseListenerAsync()
    {
        ReleaseListener?.Invoke();
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
