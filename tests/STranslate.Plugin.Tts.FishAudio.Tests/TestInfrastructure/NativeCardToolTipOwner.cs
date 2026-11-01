using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

internal sealed class CardTooltipFixtureHooks
{
    internal Action<ToolTip>? BeforeTooltipOwnership;
    internal Action<CardTooltipFixtureProbe>? TooltipReady;
    internal Action<HwndSource, SettingsViewModel>? SourceAcquired;
    internal Action<HwndSource>? SourceDisposed;
}

internal sealed record CardTooltipFixtureProbe(NativeCardToolTipOwner Tooltip,
    HwndSource HostSource, SettingsViewModel ViewModel);

// Each instance observes exactly one original open/close. It owns no timer,
// readiness pump or animation setting. The enclosing fixture owns the STA root.
internal sealed class NativeCardToolTipOwner
{
    private readonly ToolTip _tooltip;
    private readonly object _localPlacement;
    private readonly BindingBase? _placementBinding;
    private readonly UIElement? _placement;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Popup? _popup;
    private Exception? _eventFailure;
    private bool _openedAttached;
    private bool _closedAttached;
    private bool _popupClosedAttached;
    private bool _closeExpected;
    private bool _started;

    internal NativeCardToolTipOwner(ToolTip tooltip)
    {
        tooltip.Dispatcher.VerifyAccess();
        if (tooltip.IsOpen) throw new InvalidOperationException("native-card-tooltip-already-open");
        _tooltip = tooltip;
        _placementBinding = BindingOperations.GetBindingBase(tooltip, ToolTip.PlacementTargetProperty);
        _localPlacement = tooltip.ReadLocalValue(ToolTip.PlacementTargetProperty);
        _placement = tooltip.PlacementTarget;
        if (_placementBinding is null && _localPlacement != DependencyProperty.UnsetValue
            && _localPlacement is not null && _localPlacement is not UIElement)
            throw new InvalidOperationException("native-card-tooltip-placement-not-restorable");
    }

    internal ToolTip ToolTip => _tooltip;
    internal HwndSource? Source { get; private set; }
    internal Task ClosedTask => _closed.Task;
    internal int OpenedEvents { get; private set; }
    internal int ToolTipClosedEvents { get; private set; }
    internal int PopupClosedEvents { get; private set; }
    internal bool? PendingAfterCloseSetter { get; private set; }
    internal bool? SourceAliveAfterCloseSetter { get; private set; }
    internal bool InClosedCallback { get; private set; }
    internal bool HandlersAttached => _openedAttached || _closedAttached || _popupClosedAttached;
    internal bool PlacementRestored { get; private set; }
    internal bool ClosureVerified { get; private set; }

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(async () =>
    {
        if (_started) throw new InvalidOperationException("native-card-tooltip-owner-reused");
        _started = true;
        _tooltip.AddHandler(ToolTip.OpenedEvent, new RoutedEventHandler(Opened), true);
        _openedAttached = true;
        _tooltip.AddHandler(ToolTip.ClosedEvent, new RoutedEventHandler(ToolTipClosed), true);
        _closedAttached = true;
        await body();
    }, () => CloseAndJoinAsync(verify: true), DetachOpenedAsync, DetachToolTipClosedAsync, DetachPopupClosedAsync, RestorePlacementAsync);

    private void RejectEvent() => _eventFailure ??= new InvalidOperationException("native-card-tooltip-closed-sequence-invalid");

    private void CaptureSource()
    {
        var current = PresentationSource.FromVisual(_tooltip) as HwndSource;
        if (current is not null)
        {
            if (Source is not null && !ReferenceEquals(Source, current)) RejectEvent();
            else Source = current;
        }
        var popup = LogicalTreeHelper.GetParent(_tooltip) as Popup;
        if (popup is null || !ReferenceEquals(popup.Child, _tooltip)) return;
        if (_popup is not null && !ReferenceEquals(_popup, popup)) { RejectEvent(); return; }
        _popup = popup;
        if (!_popupClosedAttached)
        {
            popup.Closed += PopupClosed;
            _popupClosedAttached = true;
        }
    }

    private void Opened(object sender, RoutedEventArgs args)
    {
        OpenedEvents++;
        CaptureSource();
        if (!ReferenceEquals(sender, _tooltip) || !ReferenceEquals(args.OriginalSource, _tooltip)
            || OpenedEvents != 1 || Source is null || Source.IsDisposed || _popup is null)
            RejectEvent();
    }

    private void ToolTipClosed(object sender, RoutedEventArgs args)
    {
        InClosedCallback = true;
        try
        {
            ToolTipClosedEvents++;
            if (!ReferenceEquals(sender, _tooltip) || !ReferenceEquals(args.OriginalSource, _tooltip)
                || !_closeExpected || ToolTipClosedEvents != 1 || Source is null || !Source.IsDisposed || _tooltip.IsOpen)
                RejectEvent();
            // A routed ToolTip event alone never completes native cleanup.
        }
        finally { InClosedCallback = false; }
    }

    private void PopupClosed(object? sender, EventArgs args)
    {
        InClosedCallback = true;
        try
        {
            PopupClosedEvents++;
            if (!ReferenceEquals(sender, _popup) || !_closeExpected || PopupClosedEvents != 1
                || ToolTipClosedEvents != 1 || Source is null || !Source.IsDisposed || _tooltip.IsOpen)
                RejectEvent();
            // This is the actual parent Popup CLR event, after its saved HWND
            // source is disposed. RCAA resumes only after this event stack and
            // Popup's following placement-registration cleanup have returned.
            if (ReferenceEquals(sender, _popup) && Source is { IsDisposed: true }) _closed.TrySetResult();
        }
        finally { InClosedCallback = false; }
    }

    internal void RequireOpenedSource()
    {
        if (OpenedEvents != 1 || Source is null || Source.IsDisposed || _popup is null || !_popupClosedAttached)
            throw new InvalidOperationException("native-card-tooltip-open-source-not-observed");
        if (_eventFailure is not null) throw _eventFailure;
    }

    internal void ExpectClose() => _closeExpected = true;

    internal void RecordCloseSetterReturn()
    {
        PendingAfterCloseSetter ??= !_closed.Task.IsCompleted;
        SourceAliveAfterCloseSetter ??= Source is { IsDisposed: false };
    }

    private async Task CloseAndJoinAsync(bool verify)
    {
        _tooltip.Dispatcher.VerifyAccess();
        CaptureSource(); // Covers a body failure after native creation but before its observation.
        if (!_closeExpected || _tooltip.IsOpen)
        {
            _closeExpected = true;
            _tooltip.IsOpen = false;
        }
        RecordCloseSetterReturn();
        if (Source is null)
        {
            if (verify) throw new InvalidOperationException("native-card-tooltip-open-source-not-observed");
            return;
        }
        await _closed.Task;
        if (!verify) return;
        if (_eventFailure is not null) throw _eventFailure;
        if (OpenedEvents != 1 || ToolTipClosedEvents != 1 || PopupClosedEvents != 1 || !Source.IsDisposed
            || _tooltip.IsOpen || InClosedCallback)
            throw new InvalidOperationException("native-card-tooltip-closed-sequence-invalid");
        ClosureVerified = true;
    }

    private Task DetachOpenedAsync()
    {
        if (_openedAttached) { _tooltip.RemoveHandler(ToolTip.OpenedEvent, new RoutedEventHandler(Opened)); _openedAttached = false; }
        return Task.CompletedTask;
    }

    private Task DetachToolTipClosedAsync()
    {
        if (_closedAttached) { _tooltip.RemoveHandler(ToolTip.ClosedEvent, new RoutedEventHandler(ToolTipClosed)); _closedAttached = false; }
        return Task.CompletedTask;
    }

    private Task DetachPopupClosedAsync()
    {
        if (_popupClosedAttached) { _popup!.Closed -= PopupClosed; _popupClosedAttached = false; }
        return Task.CompletedTask;
    }

    private Task RestorePlacementAsync()
    {
        if (_placementBinding is not null) BindingOperations.SetBinding(_tooltip, ToolTip.PlacementTargetProperty, _placementBinding);
        else if (_localPlacement == DependencyProperty.UnsetValue) _tooltip.ClearValue(ToolTip.PlacementTargetProperty);
        else _tooltip.SetValue(ToolTip.PlacementTargetProperty, _localPlacement);
        var binding = BindingOperations.GetBindingBase(_tooltip, ToolTip.PlacementTargetProperty);
        var local = _tooltip.ReadLocalValue(ToolTip.PlacementTargetProperty);
        if (!ReferenceEquals(binding, _placementBinding) || !ReferenceEquals(_tooltip.PlacementTarget, _placement)
            || (_placementBinding is null && !ReferenceEquals(local, _localPlacement)))
            throw new InvalidOperationException("native-card-tooltip-placement-not-restored");
        PlacementRestored = true;
        return Task.CompletedTask;
    }

    // Independent rescue is used only after a control's authoritative assertions.
    // It cannot manufacture a Closed signal or satisfy any earlier oracle.
    internal Task RescueAsync() => RegressionFixtureOwner.RunAsync(() => Task.CompletedTask,
        () => CloseAndJoinAsync(verify: false), DetachOpenedAsync, DetachToolTipClosedAsync, DetachPopupClosedAsync, RestorePlacementAsync);
}
