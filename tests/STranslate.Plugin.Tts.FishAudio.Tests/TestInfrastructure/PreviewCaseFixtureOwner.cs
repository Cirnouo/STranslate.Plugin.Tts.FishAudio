using System.Diagnostics;

// Test-only ownership for one ordinary preview responsibility. QueueRoot reuses
// an existing root; every cleanup wait uses the same original monotonic epoch.
internal sealed class PreviewCaseFixtureOwner
{
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly List<IDisposable> _cancelAndDispose = [];
    private readonly List<IDisposable> _restore = [];
    private readonly List<Action> _release = [];
    private readonly List<Task> _tasks = [];
    private readonly List<Func<Task?>> _observe = [];

    internal static Task RunAsync(Func<PreviewCaseFixtureOwner, Task> work)
    {
        var owner = new PreviewCaseFixtureOwner();
        return RegressionFixtureOwner.RunAsync(() => work(owner),
            () => RunEachAsync(owner._cancelAndDispose.AsEnumerable().Reverse().Select<IDisposable, Action>(item => () => item.Dispose())),
            () => RunEachAsync(owner._release),
            owner.DrainAsync,
            () => RunEachAsync(owner._restore.AsEnumerable().Reverse().Select<IDisposable, Action>(item => () => item.Dispose())));
    }

    internal T Own<T>(T resource) where T : IDisposable
    {
        _cancelAndDispose.Add(resource);
        return resource;
    }

    internal T Scope<T>(T resource) where T : IDisposable
    {
        _restore.Add(resource);
        return resource;
    }

    internal Task Track(Task task)
    {
        _tasks.Add(task);
        return task;
    }

    internal void ObserveCommand(Func<Task?> originalTask) => _observe.Add(originalTask);

    internal void ReleaseOnCleanup(TaskCompletionSource<string> response) =>
        _release.Add(() => response.TrySetResult("{}"));

    internal void ReleaseOnCleanup(TaskCompletionSource<Stream> response, Func<Stream>? lateStream = null) =>
        _release.Add(() =>
        {
            if (response.Task.IsCompleted) return;
            var stream = lateStream?.Invoke() ?? new MemoryStream([1, 2, 3]);
            if (!response.TrySetResult(stream)) stream.Dispose();
        });

    private async Task DrainAsync()
    {
        // A synchronous Execute can throw after publishing its ExecutionTask.
        // Each original-task capture and the drain is attempted independently.
        var cleanup = _observe.Select<Func<Task?>, Func<Task>>(capture => () =>
        {
            var task = capture();
            if (task is not null) _tasks.Add(task);
            return Task.CompletedTask;
        }).ToList();
        cleanup.Add(async () =>
        {
            var originalTasks = Task.WhenAll(_tasks.Distinct());
            if (originalTasks.IsCompleted) await originalTasks;
            else
            {
                var remaining = RegressionExecution.Timeout - _elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero) throw new RegressionExecutionTimeoutException();
                await originalTasks.WaitAsync(remaining);
            }
        });
        await RegressionFixtureOwner.RunAsync(() => Task.CompletedTask, cleanup.ToArray());
    }

    private static Task RunEachAsync(IEnumerable<Action> actions) =>
        RegressionFixtureOwner.RunAsync(() => Task.CompletedTask, actions.Select<Action, Func<Task>>(action => () =>
        {
            action();
            return Task.CompletedTask;
        }).ToArray());
}
