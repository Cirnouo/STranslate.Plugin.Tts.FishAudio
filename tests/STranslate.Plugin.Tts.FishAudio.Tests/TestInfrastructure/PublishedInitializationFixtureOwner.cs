using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Lifecycle;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

// Test-only owner for an already published state. Capture never constructs a
// ViewModel and never substitutes PendingStartupTask read after disposal.
internal sealed class PublishedInitializationFixtureOwner
{
    private readonly List<PluginInitializationCoordinator.InitializationWorkHandle> _work = [];
    private readonly List<Task> _observers = [];
    private SettingsViewModel? _viewModel;

    internal Main? Plugin { get; set; }
    internal Action? ReleaseHeldResponse { get; set; }

    internal PluginInitializationCoordinator.InitializationWorkHandle CapturePublishedWork()
    {
        var plugin = Plugin ?? throw new InvalidOperationException("published-fixture-owner-not-acquired");
        var capture = plugin.CaptureInitializationWork()
            ?? throw new InvalidOperationException("published-fixture-state-not-published");
        if (!ReferenceEquals(capture.Work.StartupTask, plugin.PendingStartupTask))
            throw new InvalidOperationException("published-fixture-original-startup-identity-changed");
        if (!_work.Contains(capture.Work)) _work.Add(capture.Work);
        if (capture.ViewModel is { } viewModel)
        {
            _viewModel = viewModel;
            var observer = viewModel.PendingStartupCreditObserverTask;
            if (observer is not null && !_observers.Contains(observer)) _observers.Add(observer);
        }
        return capture.Work;
    }

    internal NUnitCompatibility.PublishedInitializationObservation Observe(
        Settings? runtimeSettings = null, Task? requestStarted = null,
        Task? requestCanceled = null, Task? response = null) =>
        new(Plugin ?? throw new InvalidOperationException("published-fixture-owner-not-acquired"),
            _work.ToArray(), _observers.ToArray(), _viewModel, runtimeSettings,
            ReleaseHeldResponse, requestStarted, requestCanceled, response);

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(body,
        () => { Plugin?.Dispose(); return Task.CompletedTask; },
        () => { ReleaseHeldResponse?.Invoke(); return Task.CompletedTask; },
        () => Task.WhenAll(_work.Select(work => work.StartupTask)),
        () => Task.WhenAll(_observers),
        () => Task.WhenAll(_work.SelectMany(work => work.RetirementTasks)),
        () =>
        {
            if (_work.Any(work => work.RetirementTasks.Count == 0))
                throw new InvalidOperationException("published-fixture-has-no-actual-retirement-task");
            return Task.CompletedTask;
        });
}
