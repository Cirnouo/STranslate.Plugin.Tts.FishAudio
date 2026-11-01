using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

internal sealed class PopupDirectionFixtureHooks
{
    internal Action<HwndSource, SettingsViewModel>? SourceAcquired;
    internal Action<PopupDirectionFixtureProbe>? PopupOpened;
    internal Action<PopupDirectionFixtureProbe, Exception>? CloseFailureReturned;
    internal Action<HwndSource>? SourceRetiring;
    internal Action<HwndSource>? SourceDisposed;
}

internal sealed record PopupDirectionFixtureProbe(HwndSource HostSource,
    SettingsViewModel ViewModel, NativePopupLifetimeOwner Lifetime, FrameworkElement Control);

// One actual Popup open/close, on its existing STA root. This retains WPF's
// actual source and CLR Closed event; no timer, pump, retry or action is created.
internal sealed class NativePopupLifetimeOwner
{
    private readonly Popup _popup;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? _eventFailure;
    private bool _detached;

    internal NativePopupLifetimeOwner(Popup popup)
    {
        popup.Dispatcher.VerifyAccess();
        if (popup.IsOpen || HasLiveSource(popup))
            throw new InvalidOperationException("native-popup-lifetime-must-observe-before-open");
        _popup = popup;
        popup.Opened += Opened;
        popup.Closed += Closed;
    }

    internal HwndSource? Source { get; private set; }
    internal Task ClosedTask => _closed.Task;
    internal int OpenedEvents { get; private set; }
    internal int ClosedEvents { get; private set; }
    internal bool? PendingAfterCloseSetter { get; private set; }
    internal bool? SourceAliveAfterCloseSetter { get; private set; }
    internal bool InClosedCallback { get; private set; }
    internal bool ClosureVerified { get; private set; }
    internal bool HandlersAttached => !_detached;

    internal void CaptureSource()
    {
        _popup.Dispatcher.VerifyAccess();
        var current = _popup.Child is { } child ? PresentationSource.FromVisual(child) as HwndSource : null;
        if (current is null) return;
        if (Source is not null && !ReferenceEquals(Source, current)) RejectEvent();
        else Source = current;
    }

    private void RejectEvent() => _eventFailure ??=
        new InvalidOperationException("native-popup-lifetime-closed-sequence-invalid");

    private void Opened(object? sender, EventArgs args)
    {
        OpenedEvents++;
        CaptureSource();
        if (!ReferenceEquals(sender, _popup) || OpenedEvents != 1 || ClosedEvents != 0
            || Source is null || Source.IsDisposed)
            RejectEvent();
    }

    private void Closed(object? sender, EventArgs args)
    {
        InClosedCallback = true;
        try
        {
            ClosedEvents++;
            if (!ReferenceEquals(sender, _popup) || OpenedEvents != 1 || ClosedEvents != 1
                || Source is null || !Source.IsDisposed || _popup.IsOpen)
                RejectEvent();
            // WPF disposes this saved HwndSource before raising CLR Closed.
            // RCAA prevents a join continuation from running on this event stack,
            // before Popup's following placement-registration cleanup returns.
            if (ReferenceEquals(sender, _popup) && Source is { IsDisposed: true })
                _closed.TrySetResult();
        }
        finally { InClosedCallback = false; }
    }

    internal void RequireOpenedSource()
    {
        if (_eventFailure is not null) throw _eventFailure;
        if (OpenedEvents != 1 || Source is null || Source.IsDisposed)
            throw new InvalidOperationException("native-popup-lifetime-open-source-not-observed");
    }

    internal void RecordCloseSetterReturn()
    {
        PendingAfterCloseSetter ??= !_closed.Task.IsCompleted;
        SourceAliveAfterCloseSetter ??= Source is { IsDisposed: false };
    }

    internal Task CloseAndJoinAsync() => CloseAndJoinAsync(() =>
    {
        if (_popup.IsOpen) _popup.IsOpen = false;
    });

    internal Task CloseAndJoinAsync(Action requestClose)
    {
        _popup.Dispatcher.VerifyAccess();
        ArgumentNullException.ThrowIfNull(requestClose);
        CaptureSource();
        return RegressionFixtureOwner.RunAsync(() =>
        {
            try { requestClose(); }
            finally { RecordCloseSetterReturn(); }
            return Task.CompletedTask;
        }, JoinActualClosedAsync);
    }

    private async Task JoinActualClosedAsync()
    {
        if (Source is null)
        {
            if (OpenedEvents != 0 || ClosedEvents != 0 || HasLiveSource(_popup))
                throw new InvalidOperationException("native-popup-lifetime-open-source-not-observed");
            // No native lifetime was acquired; no Closed is synthesized.
        }
        else
        {
            await _closed.Task;
            if (_eventFailure is not null) throw _eventFailure;
            if (OpenedEvents != 1 || ClosedEvents != 1 || !Source.IsDisposed || _popup.IsOpen || InClosedCallback)
                throw new InvalidOperationException("native-popup-lifetime-closed-sequence-invalid");
            ClosureVerified = true;
        }
        if (_eventFailure is not null) throw _eventFailure;
    }

    internal Task CloseWithFailureForControlAsync(Exception failure) => CloseAndJoinAsync(() =>
    {
        _popup.IsOpen = false;
        RecordCloseSetterReturn();
        if (_closed.Task.IsCompleted || Source is not { IsDisposed: false })
            throw new InvalidOperationException("native-popup-lifetime-close-failure-control-not-pending");
        throw failure;
    });

    internal void Detach()
    {
        _popup.Dispatcher.VerifyAccess();
        if (_detached) return;
        if (_popup.IsOpen || HasLiveSource(_popup) || (Source is not null && !_closed.Task.IsCompleted))
            throw new InvalidOperationException("native-popup-lifetime-close-pending");
        _popup.Opened -= Opened;
        _popup.Closed -= Closed;
        _detached = true;
    }

    // Controls invoke independent rescue only after their terminal-work oracles.
    internal Task RescueAsync() => RegressionFixtureOwner.RunAsync(() => Task.CompletedTask,
        CloseAndJoinAsync, () => { Detach(); return Task.CompletedTask; });

    private static bool HasLiveSource(Popup popup) => popup.Child is { } child
        && PresentationSource.FromVisual(child) is HwndSource { IsDisposed: false };
}
