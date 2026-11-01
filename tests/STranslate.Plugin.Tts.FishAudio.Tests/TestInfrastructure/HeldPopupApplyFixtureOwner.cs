using System.ComponentModel;
using System.Windows.Interop;
using System.Windows.Threading;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

internal enum HeldPopupApplyFailurePoint { AfterApplyStarted, AfterPendingDraftChanged }

// Only the one compiled held-Apply fixture uses this owner. The actual returned
// Apply Tasks and raw HTTP Task remain distinct from the publication signal.
internal sealed class HeldPopupApplyFixtureOwner
{
    internal SettingsViewModel? ViewModel;
    internal HwndSource? Source;
    internal SearchAdjustmentControl? Control;
    internal TestHttpServiceProxy? Http;
    internal SearchAdjustmentControl.ApplyTaskObservation? ApplyObservation;
    internal TaskCompletionSource<string>? HeldResponse;
    internal Task? Publication;
    internal PropertyChangedEventHandler? PublicationHandler;
    internal string ResponseJson = "";
    internal bool OriginalFinallyActive;
    internal Task[] TasksBeforeFirstViewModelDispose { get; private set; } = [];
    internal bool[] PendingBeforeFirstViewModelDispose { get; private set; } = [];
    internal IReadOnlyList<Task> ActualApplyTasks => ApplyObservation?.Tasks ?? Array.Empty<Task>();

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(body,
        ReleaseResponseAsync,
        DisposeFirstViewModelAsync,
        DrainOriginalAsync,
        JoinActualApplyAsync,
        JoinRawHttpAsync,
        DetachPublicationAsync,
        EndObservationAsync,
        DisposeSourceAsync,
        DisposeOuterViewModelAsync);

    private Task ReleaseResponseAsync()
    {
        if (OriginalFinallyActive) HeldResponse?.TrySetResult(ResponseJson);
        return Task.CompletedTask;
    }

    private Task DisposeFirstViewModelAsync()
    {
        if (OriginalFinallyActive)
        {
            TasksBeforeFirstViewModelDispose = ActualApplyTasks.ToArray();
            PendingBeforeFirstViewModelDispose = TasksBeforeFirstViewModelDispose.Select(task => !task.IsCompleted).ToArray();
            ViewModel?.Dispose();
        }
        return Task.CompletedTask;
    }

    private Task DrainOriginalAsync()
    {
        if (OriginalFinallyActive)
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        return Task.CompletedTask;
    }

    private async Task JoinActualApplyAsync()
    {
        // WhenAll joins the original references; it is never exposed as a
        // replacement for an observed Apply Task or the original HTTP Task.
        var actual = ActualApplyTasks.ToArray();
        if (actual.Length != 0) await Task.WhenAll(actual);
    }

    private async Task JoinRawHttpAsync()
    {
        if (OriginalFinallyActive && HeldResponse is not null) await HeldResponse.Task;
    }

    private Task DetachPublicationAsync()
    {
        if (ViewModel is not null && PublicationHandler is not null)
            ViewModel.PropertyChanged -= PublicationHandler;
        PublicationHandler = null;
        return Task.CompletedTask;
    }

    private Task EndObservationAsync()
    {
        ApplyObservation?.Dispose();
        return Task.CompletedTask;
    }

    private Task DisposeSourceAsync()
    {
        Source?.Dispose();
        return Task.CompletedTask;
    }

    private Task DisposeOuterViewModelAsync()
    {
        ViewModel?.Dispose();
        return Task.CompletedTask;
    }

    // Test-only independent rescue follows the controls' terminal-work oracles.
    // It adds no pump or timeout and cannot satisfy those earlier assertions.
    internal Task RescueAsync() => RegressionFixtureOwner.RunAsync(() => Task.CompletedTask,
        ReleaseResponseAsync, JoinActualApplyAsync, JoinRawHttpAsync,
        DetachPublicationAsync, EndObservationAsync, DisposeSourceAsync, DisposeOuterViewModelAsync);
}
