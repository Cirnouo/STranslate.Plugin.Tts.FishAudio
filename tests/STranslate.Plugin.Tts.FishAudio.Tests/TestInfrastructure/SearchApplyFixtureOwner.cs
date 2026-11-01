using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

internal sealed class SearchApplyPopupHooks
{
    internal Action<SearchApplyPopupProbe>? PopupOpened;
    internal Action<SearchApplyPopupSequenceOwner>? LifetimesRetired;
}

internal sealed record SearchApplyPopupProbe(HwndSource HostSource, SettingsViewModel ViewModel,
    FrameworkElement Control, NativePopupLifetimeOwner Lifetime, int OpenOrdinal);

// This owns only Popup lifetimes. Held Apply composes this inside its original
// G1 work, so native joins precede the unchanged G1 release/cancel/Task/source tail.
internal sealed class SearchApplyPopupSequenceOwner(SearchApplyPopupHooks? hooks = null)
{
    internal HwndSource? HostSource;
    internal SettingsViewModel? ViewModel;
    internal FrameworkElement? Control;
    internal Action? RequestClose;
    private NativePopupLifetimeOwner? _current;
    private readonly List<NativePopupLifetimeOwner> _lifetimes = [];
    internal IReadOnlyList<NativePopupLifetimeOwner> Lifetimes => _lifetimes;

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(body,
        RequestCloseAsync, JoinCurrentAsync, DetachLifetimesAsync, ObserveLifetimesRetiredAsync);

    internal void Open(FrameworkElement control, Action originalOpen)
    {
        if (_current is not null) throw new InvalidOperationException("search-apply-prior-popup-pending");
        _current = new NativePopupLifetimeOwner((Popup)control.FindName("AdjustmentPopup"));
        _lifetimes.Add(_current);
        try
        {
            originalOpen();
            _current.RequireOpenedSource();
            hooks?.PopupOpened?.Invoke(new SearchApplyPopupProbe(HostSource!, ViewModel!, control, _current, _lifetimes.Count));
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

    private Task RequestCloseAsync() { RequestClose?.Invoke(); return Task.CompletedTask; }
    private Task DetachLifetimesAsync()
    {
        foreach (var lifetime in _lifetimes) lifetime.Detach();
        return Task.CompletedTask;
    }
    private Task ObserveLifetimesRetiredAsync()
    {
        hooks?.LifetimesRetired?.Invoke(this);
        return Task.CompletedTask;
    }
}

internal sealed class SearchApplyFixtureHooks
{
    internal SearchApplyPopupHooks? PopupHooks;
    internal Action<SearchApplyFixtureOwner>? AfterApplyStarted;
    internal Action<HwndSource>? SourceDisposed;
    internal Action<SearchApplyFixtureOwner>? WorkRetired;
}

// Captions and drafts retain the approved observer's original Apply Tasks.
// Source then VM disposal keeps their original using order; VM cancellation,
// original Task joins and observation retirement remain independent phases.
internal sealed class SearchApplyFixtureOwner(SearchApplyFixtureHooks? hooks = null)
{
    internal readonly SearchApplyPopupSequenceOwner Popups = new(hooks?.PopupHooks);
    internal SettingsViewModel? ViewModel;
    internal HwndSource? Source;
    internal SearchAdjustmentControl? Control;
    internal SearchAdjustmentControl.ApplyTaskObservation? ApplyObservation;
    internal IReadOnlyList<Task> ActualApplyTasks => ApplyObservation?.Tasks ?? Array.Empty<Task>();
    internal Task[] TasksBeforeViewModelDispose { get; private set; } = [];
    internal Task[] JoinedApplyTasks { get; private set; } = [];
    internal bool ObservationEnded { get; private set; }

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(
        () => Popups.RunAsync(body), DisposeSourceAsync, DisposeViewModelAsync,
        JoinActualApplyAsync, EndObservationAsync, ObserveWorkRetiredAsync);

    internal void RecordApplyStarted() => hooks?.AfterApplyStarted?.Invoke(this);

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
        TasksBeforeViewModelDispose = ActualApplyTasks.ToArray();
        ViewModel?.Dispose();
        return Task.CompletedTask;
    }
    private async Task JoinActualApplyAsync()
    {
        var actual = ActualApplyTasks.ToArray();
        if (actual.Length != 0) await Task.WhenAll(actual);
        JoinedApplyTasks = actual;
    }
    private Task EndObservationAsync()
    {
        ApplyObservation?.Dispose();
        ObservationEnded = true;
        return Task.CompletedTask;
    }
    private Task ObserveWorkRetiredAsync()
    {
        hooks?.WorkRetired?.Invoke(this);
        return Task.CompletedTask;
    }
}
