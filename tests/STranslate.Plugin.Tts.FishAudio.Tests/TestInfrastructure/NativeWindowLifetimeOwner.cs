using System.Windows;
using System.Windows.Interop;

// Window.Closed and HwndSource.Disposed have separate meanings. Disposed is
// raised before source cleanup completes; both actual events and callback-exit
// IsDisposed are required. This observer creates no HWND, timer, pump or retry.
internal sealed class NativeWindowLifetimeOwner
{
    private readonly Window _window;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _sourceDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? _eventFailure;
    private bool _detached;
    internal HwndSource? Source { get; private set; }
    internal Task ClosedTask => _closed.Task;
    internal Task SourceDisposedTask => _sourceDisposed.Task;
    internal int SourceInitializedEvents { get; private set; }
    internal int ClosedEvents { get; private set; }
    internal int SourceDisposedEvents { get; private set; }
    internal bool InClosedCallback { get; private set; }
    internal bool InSourceDisposedCallback { get; private set; }
    internal bool ClosureVerified { get; private set; }
    internal bool HandlersAttached => !_detached;

    internal NativeWindowLifetimeOwner(Window window)
    {
        window.Dispatcher.VerifyAccess();
        _window = window;
        window.SourceInitialized += SourceInitialized;
        window.Closed += Closed;
    }

    private void Reject() => _eventFailure ??= new InvalidOperationException("native-window-lifetime-event-sequence-invalid");

    private void SourceInitialized(object? sender, EventArgs args)
    {
        SourceInitializedEvents++;
        // Handle reads the source created by the original Show; EnsureHandle is
        // deliberately not used, so observation cannot create a native source.
        var source = HwndSource.FromHwnd(new WindowInteropHelper(_window).Handle);
        if (!ReferenceEquals(sender, _window) || SourceInitializedEvents != 1 ||
            Source is not null || source is null || source.IsDisposed)
        {
            Reject();
            return;
        }
        Source = source;
        source.Disposed += SourceDisposed;
    }

    private void Closed(object? sender, EventArgs args)
    {
        InClosedCallback = true;
        try
        {
            ClosedEvents++;
            if (!ReferenceEquals(sender, _window) || ClosedEvents != 1) Reject();
            if (ReferenceEquals(sender, _window)) _closed.TrySetResult();
        }
        finally { InClosedCallback = false; }
    }

    private void SourceDisposed(object? sender, EventArgs args)
    {
        InSourceDisposedCallback = true;
        try
        {
            SourceDisposedEvents++;
            if (!ReferenceEquals(sender, Source) || SourceDisposedEvents != 1) Reject();
            if (ReferenceEquals(sender, Source)) _sourceDisposed.TrySetResult();
        }
        finally { InSourceDisposedCallback = false; }
    }

    internal void RequireShownSource()
    {
        if (_eventFailure is not null) throw _eventFailure;
        if (SourceInitializedEvents != 1 || Source is null || Source.IsDisposed || _closed.Task.IsCompleted)
            throw new InvalidOperationException("native-window-lifetime-source-not-observed");
    }

    internal Task CloseAndJoinAsync() => RegressionFixtureOwner.RunAsync(() =>
    {
        if (!_closed.Task.IsCompleted) _window.Close();
        return Task.CompletedTask;
    }, async () =>
    {
        await _closed.Task;
        if (Source is not null) await _sourceDisposed.Task;
        if (_eventFailure is not null) throw _eventFailure;
        if (ClosedEvents != 1 || InClosedCallback || InSourceDisposedCallback ||
            (Source is not null && (SourceInitializedEvents != 1 || SourceDisposedEvents != 1 || !Source.IsDisposed)))
            throw new InvalidOperationException("native-window-lifetime-close-pending");
        ClosureVerified = true;
    });

    internal void Detach()
    {
        _window.Dispatcher.VerifyAccess();
        if (_detached) return;
        if (!ClosureVerified) throw new InvalidOperationException("native-window-lifetime-close-pending");
        _window.SourceInitialized -= SourceInitialized;
        _window.Closed -= Closed;
        if (Source is not null) Source.Disposed -= SourceDisposed;
        _detached = true;
    }
}
