using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Lifecycle;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

// Owns only the two held TTS scenarios, never scenario registration/execution.
internal sealed class HeldTtsFixtureOwner
{
    private readonly List<PluginInitializationCoordinator.InitializationWorkHandle> _work = [];
    private readonly List<Task> _observers = [];
    private readonly List<SettingsViewModel> _views = [];
    internal Main? Plugin { get; set; }
    internal Task? Replacement { get; set; }
    internal Task? Playback { get; set; }
    internal Task? NewStartup { get; set; }
    internal Task? Entered { get; set; }
    internal Task? Barrier { get; set; }
    internal Action? ReleaseHeldBarrier { get; set; }
    internal bool PlaybackCancellationObserved { get; set; }
    internal bool MainDisposed { get; private set; }
    internal bool PlaybackCancellationJoined { get; private set; }
    internal IReadOnlyList<PluginInitializationCoordinator.InitializationWorkHandle> Work => _work.ToArray();
    internal IReadOnlyList<Task> Observers => _observers.ToArray();
    internal IReadOnlyList<SettingsViewModel> Views => _views.ToArray();

    internal void CaptureCurrent()
    {
        var capture = Plugin?.CaptureInitializationWork();
        if (capture is null) return;
        if (!ReferenceEquals(capture.Work.StartupTask, Plugin!.PendingStartupTask))
            throw new InvalidOperationException("held-tts-original-startup-identity-changed");
        if (!_work.Contains(capture.Work)) _work.Add(capture.Work);
        if (capture.ViewModel is not { } view) return;
        if (!_views.Contains(view)) _views.Add(view);
        var observer = view.PendingStartupCreditObserverTask;
        if (observer is not null && !_observers.Contains(observer)) _observers.Add(observer);
    }

    private Task DisposeMain()
    {
        if (Plugin is null) return Task.CompletedTask;
        Plugin.Dispose();
        MainDisposed = true;
        return Task.CompletedTask;
    }

    private async Task JoinPlayback()
    {
        var playback = Playback;
        if (playback is null) return;
        try { await playback; }
        catch (TaskCanceledException) when (playback.IsCanceled &&
            (PlaybackCancellationObserved || MainDisposed))
        {
            // Only this original Task's observed or explicitly retired
            // cancellation is expected. A faulted Task remains a failure.
            PlaybackCancellationJoined = true;
        }
    }

    private Func<Task>[] CompletionStages() =>
    [
        () => { ReleaseHeldBarrier?.Invoke(); return Task.CompletedTask; },
        () => Replacement ?? Task.CompletedTask,
        () => { CaptureCurrent(); return Task.CompletedTask; },
        DisposeMain,
        JoinPlayback,
        () => Task.WhenAll(_work.Select(work => work.StartupTask)),
        () => Task.WhenAll(_observers),
        () => Task.WhenAll(_work.SelectMany(work => work.RetirementTasks)),
        () =>
        {
            if (_work.Any(work => work.RetirementTasks.Count == 0))
                throw new InvalidOperationException("held-tts-retirement-not-recorded");
            return Task.CompletedTask;
        },
    ];

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(body, CompletionStages());
}

