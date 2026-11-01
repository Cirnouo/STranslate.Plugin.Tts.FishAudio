using System.Runtime.ExceptionServices;
using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Lifecycle;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

// One instance owns one original continuous initialization scenario. Capture
// retains actual per-call work, including a prepared state that never published.
internal sealed class RemainingInitializationFixtureOwner
{
    private readonly object _gate = new();
    private readonly List<Main> _plugins = [];
    private readonly List<PluginInitializationCoordinator.InitializationWorkCapture> _acquisitions = [];
    private readonly List<PluginInitializationCoordinator.InitializationWorkHandle> _work = [];
    private readonly List<SettingsViewModel> _views = [];
    private readonly List<Task> _observers = [];
    private readonly List<Task> _operations = [];
    private readonly List<Task> _barriers = [];
    private readonly List<Action> _barrierReleases = [];
    private readonly List<Task> _responses = [];
    private readonly List<Action> _responseReleases = [];
    internal IDisposable? NetworkOverride { get; set; }

    internal IReadOnlyList<Main> Plugins { get { lock (_gate) return _plugins.ToArray(); } }
    internal IReadOnlyList<PluginInitializationCoordinator.InitializationWorkCapture> Acquisitions { get { lock (_gate) return _acquisitions.ToArray(); } }
    internal IReadOnlyList<PluginInitializationCoordinator.InitializationWorkHandle> Work { get { lock (_gate) return _work.ToArray(); } }
    internal IReadOnlyList<SettingsViewModel> Views { get { lock (_gate) return _views.ToArray(); } }
    internal IReadOnlyList<Task> Observers { get { lock (_gate) return _observers.ToArray(); } }
    internal IReadOnlyList<Task> Operations { get { lock (_gate) return _operations.ToArray(); } }
    internal IReadOnlyList<Task> Barriers { get { lock (_gate) return _barriers.ToArray(); } }
    internal IReadOnlyList<Action> BarrierReleases { get { lock (_gate) return _barrierReleases.ToArray(); } }
    internal IReadOnlyList<Task> Responses { get { lock (_gate) return _responses.ToArray(); } }
    internal IReadOnlyList<Action> ResponseReleases { get { lock (_gate) return _responseReleases.ToArray(); } }

    internal Main Track(Main plugin)
    {
        lock (_gate) if (!_plugins.Contains(plugin)) _plugins.Add(plugin);
        return plugin;
    }

    internal SettingsViewModel TrackView(SettingsViewModel view)
    {
        var observer = view.PendingStartupCreditObserverTask;
        lock (_gate)
        {
            if (!_views.Contains(view)) _views.Add(view);
            if (observer is not null && !_observers.Contains(observer)) _observers.Add(observer);
        }
        return view;
    }

    internal Task TrackTask(Task operation)
    {
        lock (_gate) if (!_operations.Contains(operation)) _operations.Add(operation);
        return operation;
    }

    internal Task<T> TrackTask<T>(Task<T> operation)
    {
        TrackTask((Task)operation);
        return operation;
    }

    internal void TrackBarrier(TaskCompletionSource barrier)
    {
        lock (_gate)
        {
            _barriers.Add(barrier.Task);
            _barrierReleases.Add(() => barrier.TrySetResult());
        }
    }

    internal void TrackResponse(TaskCompletionSource<string> response, Action release)
    {
        lock (_gate)
        {
            _responses.Add(response.Task);
            _responseReleases.Add(release);
        }
    }

    internal void Initialize(Main plugin, IPluginContext context, DateTimeOffset nowUtc)
    {
        PluginInitializationCoordinator.InitializationWorkCapture? acquired = null;
        try { plugin.Init(context, nowUtc, out acquired); }
        finally
        {
            // The out value refers to this Init call's local prepared state,
            // including a failed unpublished factory. A current-state getter
            // alone can return an older publication or null after disposal.
            if (acquired is not null)
            {
                // One raw acquisition per actual Init call also lets the
                // independent RED rescue retain an omitted prepared handle.
                lock (_gate) _acquisitions.Add(acquired);
                Capture(acquired);
            }
            CaptureCurrent(plugin);
        }
    }

    private void Capture(PluginInitializationCoordinator.InitializationWorkCapture capture)
    {
        lock (_gate) if (!_work.Contains(capture.Work)) _work.Add(capture.Work);
        if (capture.ViewModel is { } view) TrackView(view);
    }

    private void CaptureCurrent(Main plugin)
    {
        var current = plugin.CaptureInitializationWork();
        if (current is not null) Capture(current);
    }

    private Task CaptureAfterOperations()
    {
        foreach (var task in Operations.OfType<Task<SettingsViewModel>>())
            if (task.IsCompletedSuccessfully) TrackView(task.Result);
        foreach (var plugin in Plugins) CaptureCurrent(plugin);
        return Task.CompletedTask;
    }

    internal RemainingInitializationObservation Observe(
        Task pending, Task? entered = null, int requestCalls = 0) =>
        new(this, pending, entered, requestCalls);

    private Func<Task>[] CompletionStages() =>
    [
        // Host save/load, revision and constructor callbacks can hold the
        // transition gate. Release their original barriers before disposal.
        () => RunActionsAsync(BarrierReleases),
        () => Task.WhenAll(Operations),
        CaptureAfterOperations,
        () => DisposeAllAsync(Plugins),
        () => RunActionsAsync(ResponseReleases),
        () => Task.WhenAll(Responses),
        () => Task.WhenAll(Work.Select(work => work.StartupTask)),
        () => DisposeAllAsync(Views),
        () => Task.WhenAll(Observers),
        () => Task.WhenAll(Work.SelectMany(work => work.RetirementTasks)),
        () =>
        {
            if (Work.Any(work => work.RetirementTasks.Count == 0))
                throw new InvalidOperationException("remaining-initialization-real-retirement-not-recorded");
            return Task.CompletedTask;
        },
        () => { NetworkOverride?.Dispose(); return Task.CompletedTask; },
    ];

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(body, CompletionStages());

    private static Task RunActionsAsync(IEnumerable<Action> actions)
    {
        ExceptionDispatchInfo? failure = null;
        foreach (var action in actions)
            try { action(); }
            catch (Exception error) { Retain(ref failure, error); }
        failure?.Throw();
        return Task.CompletedTask;
    }

    private static Task DisposeAllAsync<T>(IEnumerable<T> resources) where T : IDisposable
    {
        ExceptionDispatchInfo? failure = null;
        foreach (var resource in resources)
            try { resource.Dispose(); }
            catch (Exception error) { Retain(ref failure, error); }
        failure?.Throw();
        return Task.CompletedTask;
    }

    private static void Retain(ref ExceptionDispatchInfo? failure, Exception error)
    {
        if (failure is null) { failure = ExceptionDispatchInfo.Capture(error); return; }
        if (ReferenceEquals(failure.SourceException, error)) return;
        try
        {
            if (failure.SourceException.Data[RegressionFixtureOwner.CleanupTypesKey] is not List<string> types)
                failure.SourceException.Data[RegressionFixtureOwner.CleanupTypesKey] = types = [];
            types.Add(error.GetType().FullName ?? error.GetType().Name);
        }
        catch { /* Secondary diagnostics cannot replace the original failure. */ }
    }
}

internal sealed class RemainingInitializationObservation(
    RemainingInitializationFixtureOwner owner, Task pending, Task? entered, int requestCalls)
{
    internal RemainingInitializationFixtureOwner Owner { get; } = owner;
    internal Task Pending { get; } = pending;
    internal Task? Entered { get; } = entered;
    internal int RequestCalls { get; } = requestCalls;
}
