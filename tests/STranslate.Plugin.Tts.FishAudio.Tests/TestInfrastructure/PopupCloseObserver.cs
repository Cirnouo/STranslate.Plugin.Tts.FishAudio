using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

// One observed open/close lifetime on the original STA. This does not create an
// action, pump the Dispatcher, renew a deadline or support a pending reopen.
internal sealed class PopupCloseObserver : IDisposable
{
    private readonly Popup _popup;
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ExceptionDispatchInfo? _lifetimeFailure;
    private bool _opened;
    private bool _closedObserved;
    private bool _closeStarted;
    private bool _closeFinished;
    private bool _disposed;

    internal PopupCloseObserver(Popup popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        _popup = popup;
        VerifyAccess();
        if (popup.IsOpen || HasLiveSource()) throw Failure("must-observe-before-open");
        popup.Opened += Opened;
        popup.Closed += Closed;
    }

    internal async Task CloseAsync(Action requestClose, Exception? primaryFailure = null)
    {
        VerifyAccess();
        if (_disposed) throw Failure("disposed");
        if (_closeStarted) throw Failure("close-already-requested");
        ArgumentNullException.ThrowIfNull(requestClose);
        _closeStarted = true;
        var failure = primaryFailure is null ? null : ExceptionDispatchInfo.Capture(primaryFailure);
        try
        {
            try { requestClose(); }
            catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
        }
        finally
        {
            try
            {
                if (_opened)
                    await _closed.Task;
                else if (_popup.IsOpen || HasLiveSource())
                    failure ??= ExceptionDispatchInfo.Capture(Failure("unobserved-open"));
                // No Opened and no live source means there was no native
                // lifetime to close. Do not manufacture a Closed event.
            }
            catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
            finally { _closeFinished = true; }
        }
        failure ??= _lifetimeFailure;
        failure?.Throw();
    }

    public void Dispose()
    {
        VerifyAccess();
        if (_disposed) return;
        if ((_opened && !_closedObserved) || (_closeStarted && !_closeFinished)
            || _popup.IsOpen || HasLiveSource())
            throw Failure("close-pending");
        _popup.Opened -= Opened;
        _popup.Closed -= Closed;
        _disposed = true;
    }

    private void Opened(object? sender, EventArgs arguments)
    {
        VerifyAccess();
        if (_opened || _closedObserved)
            _lifetimeFailure ??= ExceptionDispatchInfo.Capture(Failure("reopen-not-supported"));
        _opened = true;
    }

    private void Closed(object? sender, EventArgs arguments)
    {
        VerifyAccess();
        if (!_opened || _closedObserved)
            _lifetimeFailure ??= ExceptionDispatchInfo.Capture(Failure("unexpected-closed"));
        _closedObserved = true;
        _closed.TrySetResult();
    }

    private bool HasLiveSource() => _popup.Child is { } child && (PresentationSource.FromVisual(child) switch
    {
        null => false,
        HwndSource source => !source.IsDisposed,
        _ => true,
    });

    private void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != _threadId || !_popup.Dispatcher.CheckAccess()
            || Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw Failure("wrong-sta");
    }

    private static InvalidOperationException Failure(string reason) => new($"popup-close-observer-{reason}");
}
