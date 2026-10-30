using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using NUnit.Framework;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
public sealed class PopupCloseObserverCases
{
    [Test]
    [Property("FishAudioCaseId", "popup-close-evidence/none-closed-precedes-owner-dispose")]
    public Task NoAnimationStillRequiresClosedBeforeOwnerDisposal() => WithPopupAsync(async (owner, observer) =>
    {
        await owner.OpenAsync();
        var closeCalls = 0;
        var closing = observer.CloseAsync(() =>
        {
            closeCalls++;
            owner.Events.Add("close-request");
            owner.Popup.IsOpen = false;
            Assert.That(owner.Popup.IsOpen, Is.False);
            Assert.That(owner.Closed.IsCompleted, Is.False, "The request has not delivered real Closed.");
        });

        Assert.That(closeCalls, Is.EqualTo(1));
        Assert.That(closing.IsCompleted, Is.False, "IsOpen=false cannot complete native cleanup.");
        await closing;
        owner.AssertClosedWhileOwnerAlive();
        owner.DisposeSourceAfterClosed();
        Assert.That(owner.Events, Is.EqualTo(new[] { "opened", "close-request", "closed", "owner-dispose" }));
    });

    [Test]
    [Property("FishAudioCaseId", "popup-close-evidence/already-closing-awaits-closed")]
    public Task AnAlreadyClosingPopupCannotCompleteFromIsOpenFalse() => WithPopupAsync(async (owner, observer) =>
    {
        await owner.OpenAsync();
        owner.Events.Add("external-close-request");
        owner.Popup.IsOpen = false;
        Assert.That(owner.Closed.IsCompleted, Is.False);

        var closeCalls = 0;
        var closing = observer.CloseAsync(() =>
        {
            closeCalls++;
            Assert.That(owner.Popup.IsOpen, Is.False);
            owner.Popup.IsOpen = false;
        });

        Assert.That(closeCalls, Is.EqualTo(1));
        Assert.That(closing.IsCompleted, Is.False, "An earlier close request still owns a pending Closed event.");
        await closing;
        owner.AssertClosedWhileOwnerAlive();
        owner.DisposeSourceAfterClosed();
        Assert.That(owner.Events, Is.EqualTo(new[] { "opened", "external-close-request", "closed", "owner-dispose" }));
    });

    [Test]
    [Property("FishAudioCaseId", "popup-close-evidence/close-failure-awaits-closed")]
    public Task ACloseFailureRetainsItsIdentityAfterActualClosed() => WithPopupAsync(async (owner, observer) =>
    {
        await owner.OpenAsync();
        var closeFailure = new InvalidOperationException("synthetic-close-failure");
        var closeCalls = 0;
        var failure = await CaptureExpectedFailureAsync(() => observer.CloseAsync(() =>
        {
            closeCalls++;
            owner.Events.Add("close-request");
            owner.Popup.IsOpen = false;
            ThrowCloseFailure(closeFailure);
        }));

        Assert.That(failure, Is.SameAs(closeFailure));
        Assert.That(failure!.StackTrace, Does.Contain(nameof(ThrowCloseFailure)));
        Assert.That(closeCalls, Is.EqualTo(1));
        owner.AssertClosedWhileOwnerAlive();
        owner.DisposeSourceAfterClosed();
        Assert.That(owner.Events, Is.EqualTo(new[] { "opened", "close-request", "closed", "owner-dispose" }));
    });

    [Test]
    [Property("FishAudioCaseId", "popup-close-evidence/body-failure-remains-primary")]
    public Task ABodyFailureSurvivesCloseFailureWithoutEarlyOwnerDisposal() => WithPopupAsync(async (owner, observer) =>
    {
        await owner.OpenAsync();
        var bodyFailure = new InvalidOperationException("synthetic-body-failure");
        Exception? primary = null;
        try { ThrowBodyFailure(bodyFailure); }
        catch (Exception error) { primary = error; }
        Assert.That(primary, Is.SameAs(bodyFailure));

        var closeFailure = new InvalidOperationException("synthetic-close-failure");
        var closeCalls = 0;
        var failure = await CaptureExpectedFailureAsync(() => observer.CloseAsync(() =>
        {
            closeCalls++;
            owner.Events.Add("close-request");
            owner.Popup.IsOpen = false;
            ThrowCloseFailure(closeFailure);
        }, primary));

        Assert.That(failure, Is.SameAs(bodyFailure));
        Assert.That(failure, Is.Not.SameAs(closeFailure));
        Assert.That(failure!.StackTrace, Does.Contain(nameof(ThrowBodyFailure)));
        Assert.That(closeCalls, Is.EqualTo(1));
        owner.AssertClosedWhileOwnerAlive();
        owner.DisposeSourceAfterClosed();
        Assert.That(owner.Events, Is.EqualTo(new[] { "opened", "close-request", "closed", "owner-dispose" }));
    });

    // One original StaTestHost Func<Task> callback owns construction, real Open,
    // the tested close, independent failure cleanup and source disposal. Awaiting
    // causal WPF events does not start another action or another timeout budget.
    private static Task WithPopupAsync(Func<NativePopupOwner, PopupCloseObserver, Task> scenario) =>
        StaTestHost.RunOnStaThreadAsync(async () =>
        {
            HwndSource? source = null;
            NativePopupOwner? owner = null;
            PopupCloseObserver? observer = null;
            ExceptionDispatchInfo? failure = null;
            try
            {
                Assert.That(Thread.CurrentThread.GetApartmentState(), Is.EqualTo(ApartmentState.STA));
                Assert.That(SynchronizationContext.Current, Is.InstanceOf<DispatcherSynchronizationContext>());
                var target = new Button { Width = 80, Height = 24, Content = "Popup owner" };
                var root = new Border { Width = 160, Height = 100, Child = target };
                source = new HwndSource(new HwndSourceParameters("FishAudioPopupCloseEvidence")
                {
                    Width = 160,
                    Height = 100,
                    WindowStyle = unchecked((int)0x80000000), // Hidden WS_POPUP; no physical input or focus.
                });
                source.RootVisual = root;
                root.Measure(new Size(160, 100));
                root.Arrange(new Rect(0, 0, 160, 100));
                root.UpdateLayout();
                owner = new NativePopupOwner(source, target);
                observer = new PopupCloseObserver(owner.Popup); // Before the first actual Open.
                await scenario(owner, observer);
            }
            catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }

            // The real independent event signal makes the intentional helper
            // stub fail safely: close once if needed, await Closed, then dispose.
            // A cleanup failure cannot replace the original assertion/stub error.
            try { if (owner is not null) await owner.CloseRawAsync(); }
            catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
            try { observer?.Dispose(); }
            catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
            try { if (source is not null && !source.IsDisposed) source.Dispose(); }
            catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
            failure?.Throw();
        });

    private static async Task<InvalidOperationException?> CaptureExpectedFailureAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (InvalidOperationException error) { return error; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowBodyFailure(Exception failure) => throw failure;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowCloseFailure(Exception failure) => throw failure;

    private sealed class NativePopupOwner
    {
        private readonly HwndSource _ownerSource;
        private readonly int _threadId = Environment.CurrentManagedThreadId;
        private readonly TaskCompletionSource<bool> _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private HwndSource? _popupSource;
        private bool _ownerDisposedAtClosed;
        private int _closedThreadId;

        internal NativePopupOwner(HwndSource ownerSource, FrameworkElement target)
        {
            _ownerSource = ownerSource;
            Popup = new Popup
            {
                PlacementTarget = target,
                Placement = PlacementMode.Bottom,
                PopupAnimation = PopupAnimation.None,
                AllowsTransparency = true,
                StaysOpen = true,
                Child = new Border { Width = 100, Height = 50, Background = System.Windows.Media.Brushes.White },
            };
            Popup.Opened += Opened;
            Popup.Closed += ClosedEvent;
        }

        internal Popup Popup { get; }
        internal List<string> Events { get; } = [];
        internal Task Closed => _closed.Task;

        internal async Task OpenAsync()
        {
            Popup.IsOpen = true;
            await _opened.Task;
            Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(_threadId));
            Assert.That(PresentationSource.FromVisual(Popup.Child), Is.TypeOf<HwndSource>());
            _popupSource = (HwndSource)PresentationSource.FromVisual(Popup.Child)!;
            Assert.That(_popupSource.IsDisposed, Is.False);
        }

        internal void AssertClosedWhileOwnerAlive()
        {
            Assert.That(_closed.Task.IsCompletedSuccessfully, Is.True, "Actual Popup.Closed must have occurred.");
            Assert.That(_closedThreadId, Is.EqualTo(_threadId));
            Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(_threadId));
            Assert.That(_ownerDisposedAtClosed, Is.False);
            Assert.That(_ownerSource.IsDisposed, Is.False);
            Assert.That(_popupSource!.IsDisposed, Is.True, "The real popup source retires before Closed completes.");
        }

        internal void DisposeSourceAfterClosed()
        {
            AssertClosedWhileOwnerAlive();
            _ownerSource.Dispose();
            Events.Add("owner-dispose");
            Assert.That(_ownerSource.IsDisposed, Is.True);
        }

        internal async Task CloseRawAsync()
        {
            try
            {
                if (_opened.Task.IsCompletedSuccessfully)
                {
                    if (!_closed.Task.IsCompletedSuccessfully) Popup.IsOpen = false;
                    await _closed.Task;
                }
            }
            finally
            {
                Popup.Opened -= Opened;
                Popup.Closed -= ClosedEvent;
            }
        }

        private void Opened(object? sender, EventArgs arguments)
        {
            Events.Add("opened");
            _opened.TrySetResult(true);
        }

        private void ClosedEvent(object? sender, EventArgs arguments)
        {
            _ownerDisposedAtClosed = _ownerSource.IsDisposed;
            _closedThreadId = Environment.CurrentManagedThreadId;
            Events.Add("closed");
            _closed.TrySetResult(true);
        }
    }
}
