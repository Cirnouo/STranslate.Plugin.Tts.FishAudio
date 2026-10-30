using System.Runtime.ExceptionServices;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

// Own only actual resources acquired by each original continuous scenario.
// The ordinary regression root remains the sole execution deadline.
internal sealed class DiscoveryRemainderFixtureOwner
{
    private readonly List<SettingsViewModel> _views = [];
    private readonly List<Task> _operations = [];
    private readonly List<(Task Task, CancellationToken Token)> _requests = [];
    private readonly List<Task> _responses = [];
    private readonly List<Action> _releases = [];
    private readonly List<IDisposable> _registrations = [];
    private readonly List<SettingsViewModel> _retiredViews = [];

    internal IReadOnlyList<SettingsViewModel> Views => _views.ToArray();
    internal IReadOnlyList<Task> Operations => _operations.ToArray();
    internal IReadOnlyList<Task> Responses => _responses.ToArray();
    internal IReadOnlyList<(Task Task, CancellationToken Token)> Requests => _requests.ToArray();
    internal IReadOnlyList<SettingsViewModel> RetiredViews => _retiredViews.ToArray();
    internal IReadOnlyList<Action> ResponseReleases => _releases.ToArray();
    internal IReadOnlyList<IDisposable> Registrations => _registrations.ToArray();

    internal void Track(SettingsViewModel view) => _views.Add(view);
    internal void Track(CancellationTokenRegistration registration) => _registrations.Add(registration);
    internal void Track(TaskCompletionSource<string> response, string result)
    {
        _responses.Add(response.Task);
        _releases.Add(() => response.TrySetResult(result));
    }
    internal Task TrackOperation(Task operation)
    {
        _operations.Add(operation);
        return operation;
    }
    internal Task<string> TrackRequest(Task<string> request, CancellationToken token)
    {
        _requests.Add((request, token));
        return request;
    }

    private Func<Task>[] CompletionStages() =>
    [
        // Each acquired facade gets its own cleanup attempt even if cancellation
        // from another facade throws. Repeated original Dispose is idempotent.
        DisposeViewsAsync,
        () => { foreach (var release in _releases) release(); return Task.CompletedTask; },
        () => Task.WhenAll(_operations),
        () => JoinRequestsAsync(_requests),
        () => Task.WhenAll(_responses),
        // The throwing-cancellation scenario itself performs another Dispose
        // after its original tasks finish. Complete that same retirement tail.
        DisposeViewsAsync,
        () => DisposeAllAsync(_registrations),
    ];

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(body, CompletionStages());

    private Task DisposeViewsAsync()
    {
        ExceptionDispatchInfo? failure = null;
        foreach (var view in _views)
        {
            if (_retiredViews.Contains(view)) continue;
            try { view.Dispose(); _retiredViews.Add(view); }
            catch (Exception error) { Retain(ref failure, error); }
        }
        failure?.Throw();
        return Task.CompletedTask;
    }

    private static async Task JoinRequestsAsync(IEnumerable<(Task Task, CancellationToken Token)> requests)
    {
        ExceptionDispatchInfo? failure = null;
        foreach (var request in requests)
            try { await request.Task; }
            catch (OperationCanceledException) when (request.Token.IsCancellationRequested)
            {
                // These are the original explicitly retired 30-second requests.
                // The command already handles the same canceled operation.
            }
            catch (Exception error) { Retain(ref failure, error); }
        failure?.Throw();
    }

    private static Task DisposeAllAsync(IEnumerable<IDisposable> resources)
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

internal sealed class CoverRemainderFixtureOwner
{
    private readonly List<SettingsViewModel> _views = [];
    private readonly List<Task> _operations = [];
    private readonly List<Task> _clearTasks = [];
    private readonly List<Action> _releases = [];
    private readonly List<Task> _timeoutContinuations = [];

    internal string? Root { get; set; }
    internal Task? CacheTask { get; set; }
    internal IReadOnlyList<SettingsViewModel> Views => _views.ToArray();
    internal IReadOnlyList<Task> Operations => _operations.ToArray();
    internal IReadOnlyList<Task> ClearTasks => _clearTasks.ToArray();
    internal IReadOnlyList<Action> ClearReleases => _releases.ToArray();
    internal IReadOnlyList<Task> TimeoutContinuations => _timeoutContinuations.ToArray();

    internal void Track(SettingsViewModel view) => _views.Add(view);
    internal void Track(TaskCompletionSource response)
    {
        _clearTasks.Add(response.Task);
        _releases.Add(() => response.TrySetResult());
    }
    internal Task TrackOperation(Task operation)
    {
        _operations.Add(operation);
        return operation;
    }

    internal void CaptureTimeoutContinuations()
    {
        foreach (var view in _views)
        {
            if (view.TimedOutCoverCacheContinuationTask is not { } task
                || _timeoutContinuations.Any(existing => ReferenceEquals(existing, task)))
                continue;
            // Each acquired clear command can create at most one continuation.
            // These fixtures acquire at most two commands on their single VM.
            if (_timeoutContinuations.Count >= _operations.Count)
                throw new InvalidOperationException("cover-fixture-continuation-count-exceeds-owned-commands");
            _timeoutContinuations.Add(task);
        }
    }

    private Func<Task>[] CompletionStages() =>
    [
        () => { foreach (var release in _releases) release(); return Task.CompletedTask; },
        () => Task.WhenAll(_operations),
        () => { CaptureTimeoutContinuations(); return Task.CompletedTask; },
        () => Task.WhenAll(_clearTasks),
        () => Task.WhenAll(TimeoutContinuations),
        async () => { if (CacheTask is { } task) await task; },
        DisposeViewsAsync,
        () => Root is { } root
            ? CoverImageCacheSuite.CompleteCacheTasksAndDeleteOwnedRootAsync(root, null, CacheTask)
            : Task.CompletedTask,
    ];

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(body, CompletionStages());

    private Task DisposeViewsAsync()
    {
        ExceptionDispatchInfo? failure = null;
        foreach (var view in _views)
            try { view.Dispose(); }
            catch (Exception error)
            {
                if (failure is null) failure = ExceptionDispatchInfo.Capture(error);
                else if (!ReferenceEquals(failure.SourceException, error))
                {
                    try
                    {
                        if (failure.SourceException.Data[RegressionFixtureOwner.CleanupTypesKey] is not List<string> types)
                            failure.SourceException.Data[RegressionFixtureOwner.CleanupTypesKey] = types = [];
                        types.Add(error.GetType().FullName ?? error.GetType().Name);
                    }
                    catch { /* Secondary diagnostics cannot replace the original failure. */ }
                }
            }
        failure?.Throw();
        return Task.CompletedTask;
    }
}
