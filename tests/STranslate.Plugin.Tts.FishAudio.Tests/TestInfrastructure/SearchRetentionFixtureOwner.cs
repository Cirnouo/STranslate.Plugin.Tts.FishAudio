using STranslate.Plugin.Tts.FishAudio.ViewModel;

internal enum SearchRetentionFailurePoint
{
    DiscoveryHeldSearch,
    PendingOriginHeld,
    DisplayedPreviewAndSearchHeld,
    AcceptedSuccessorStarted,
}

internal sealed record SearchRetentionFixtureProbe(SearchRetentionFixtureOwner Owner,
    SettingsViewModel View, TestPreviewAudioPlayer Player, TestPreviewAudioPlayer ReplacementPlayer,
    TestHttpServiceProxy Http, Task? Operation = null, Task? RawResponse = null,
    Task? OtherOperation = null, Task? OtherRawResponse = null);

internal sealed class SearchRetentionFixtureHooks
{
    internal Action<SearchRetentionFixtureProbe>? ScenarioReady;
    internal Action<SearchRetentionFailurePoint, SearchRetentionFixtureProbe>? Failure;
}

// Explicitly created by each ordinary leaf; no ambient allocator or discovery work.
internal sealed class SearchRetentionFixtureFactory
{
    private readonly List<SearchRetentionFixtureOwner> _owners = [];
    internal IReadOnlyList<SearchRetentionFixtureOwner> Owners => _owners;
    internal SearchRetentionFixtureOwner Create()
    {
        var owner = new SearchRetentionFixtureOwner();
        _owners.Add(owner);
        return owner;
    }

    internal Task RescueAsync() => RegressionFixtureOwner.RunAsync(() => Task.CompletedTask,
        _owners.AsEnumerable().Reverse().Select(owner => (Func<Task>)(() => owner.RescueAsync())).ToArray());
}

internal sealed class SearchRetentionFixtureOwner
{
    private sealed record HeldResponse(Task Task, Func<bool> Cancel);
    private readonly object _gate = new();
    private readonly List<Task> _operations = [];
    private readonly List<Task> _raw = [];
    private readonly List<HeldResponse> _responses = [];
    private readonly List<IDisposable> _facades = [];
    private readonly List<IDisposable> _resources = [];
    private readonly List<Action> _detach = [];
    private readonly HashSet<Task> _joined = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Task> _cleanupCanceled = new(ReferenceEqualityComparer.Instance);
    private bool _capturedBeforeRelease;
    private bool _capturedBeforeFacade;

    internal IReadOnlyList<Task> Operations { get { lock (_gate) return _operations.ToArray(); } }
    internal IReadOnlyList<Task> RawResponses { get { lock (_gate) return _raw.ToArray(); } }
    internal Task[] TasksBeforeRelease { get; private set; } = [];
    internal bool[] PendingBeforeRelease { get; private set; } = [];
    internal Task[] TasksBeforeFacadeRetirement { get; private set; } = [];
    internal bool[] PendingBeforeFacadeRetirement { get; private set; } = [];

    internal void OwnFacade(IDisposable facade) => _facades.Add(facade);
    internal void OwnCallback(Action detach) => _detach.Add(detach);
    internal T OwnResource<T>(T resource) where T : IDisposable { _resources.Add(resource); return resource; }

    internal TaskCompletionSource<T> OwnResponse<T>(TaskCompletionSource<T> response)
    {
        lock (_gate) _responses.Add(new(response.Task, () => response.TrySetCanceled()));
        return response;
    }

    internal Task TrackOperation(Task operation)
    {
        lock (_gate)
            if (!_operations.Any(existing => ReferenceEquals(existing, operation))) _operations.Add(operation);
        return operation;
    }

    internal Task<T> TrackRawResponse<T>(Task<T> response)
    {
        lock (_gate)
            if (!_raw.Any(existing => ReferenceEquals(existing, response))) _raw.Add(response);
        return response;
    }

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(body,
        CaptureBeforeReleaseAsync, ReleaseResponsesAsync, JoinActualTasksAsync,
        RetireFacadesAsync, ReleaseResponsesAsync, JoinActualTasksAsync,
        DetachCallbacksAsync, DisposeResourcesAsync);

    private Task[] AllTasks()
    {
        lock (_gate)
        {
            var seen = new HashSet<Task>(ReferenceEqualityComparer.Instance);
            return _operations.Concat(_raw).Where(seen.Add).ToArray();
        }
    }

    private Task CaptureBeforeReleaseAsync()
    {
        if (!_capturedBeforeRelease)
        {
            _capturedBeforeRelease = true;
            TasksBeforeRelease = AllTasks();
            PendingBeforeRelease = TasksBeforeRelease.Select(task => !task.IsCompleted).ToArray();
        }
        return Task.CompletedTask;
    }

    private Task ReleaseResponsesAsync()
    {
        HeldResponse[] responses;
        lock (_gate) responses = _responses.ToArray();
        return RegressionFixtureOwner.RunAsync(() => Task.CompletedTask,
            responses.Select(response => (Func<Task>)(() =>
            {
                // No request is invoked and no replacement stream is allocated.
                // Unrequested TCS values stay fixture-only; only handler-returned
                // raw Tasks appear in RawResponses and require HTTP-work joining.
                if (response.Cancel()) lock (_gate) _cleanupCanceled.Add(response.Task);
                return Task.CompletedTask;
            })).ToArray());
    }

    private async Task JoinActualTasksAsync()
    {
        var failures = new List<Exception>();
        while (true)
        {
            // A synchronous callback, or an awaited predecessor's retirement,
            // can append its actual successor and a newly registered response.
            await ReleaseResponsesAsync();
            Task[] next;
            lock (_gate) next = _operations.Concat(_raw).Where(_joined.Add).ToArray();
            if (next.Length == 0) break;
            foreach (var task in next)
            {
                try { await task; }
                catch (OperationCanceledException) when (IsExpectedCleanupCancellation(task)) { }
                catch (Exception error) { failures.Add(error); }
            }
        }
        if (failures.Count != 0)
            await RegressionFixtureOwner.RunAsync(() => Task.FromException(failures[0]),
                failures.Skip(1).Select(error => (Func<Task>)(() => Task.FromException(error))).ToArray());
    }

    private bool IsExpectedCleanupCancellation(Task task)
    {
        lock (_gate) return task.IsCanceled && _cleanupCanceled.Contains(task);
    }

    private Task RetireFacadesAsync()
    {
        if (!_capturedBeforeFacade)
        {
            _capturedBeforeFacade = true;
            TasksBeforeFacadeRetirement = AllTasks();
            PendingBeforeFacadeRetirement = TasksBeforeFacadeRetirement.Select(task => !task.IsCompleted).ToArray();
        }
        // Named callbacks stay observable through original Dispose reentrancy.
        // The following release/join pass includes any real captured descendant.
        return RunCleanupAsync(_facades.AsEnumerable().Reverse().Select(facade => (Action)facade.Dispose));
    }

    private Task DetachCallbacksAsync() => RunCleanupAsync(_detach.AsEnumerable().Reverse());
    private Task DisposeResourcesAsync() => RunCleanupAsync(_resources.AsEnumerable().Reverse().Select(resource => (Action)resource.Dispose));
    private static Task RunCleanupAsync(IEnumerable<Action> actions) => RegressionFixtureOwner.RunAsync(
        () => Task.CompletedTask, actions.Select(action => (Func<Task>)(() => { action(); return Task.CompletedTask; })).ToArray());

    // Controls invoke this only after the actual-task/resource/primary oracles.
    internal Task RescueAsync() => RegressionFixtureOwner.RunAsync(() => Task.CompletedTask,
        ReleaseResponsesAsync, JoinActualTasksAsync, RetireFacadesAsync,
        ReleaseResponsesAsync, JoinActualTasksAsync, DetachCallbacksAsync, DisposeResourcesAsync);
}
