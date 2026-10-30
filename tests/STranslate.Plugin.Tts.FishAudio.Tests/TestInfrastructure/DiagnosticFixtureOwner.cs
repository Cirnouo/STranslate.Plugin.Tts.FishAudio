using System.Runtime.ExceptionServices;

// Synthetic diagnostic fixtures retain real work after a bounded report retires.
// Cleanup shares RegressionFixtureOwner's root; it never starts another timer.
internal sealed class DiagnosticFixtureOwner
{
    private readonly object _sync = new();
    private readonly List<Task> _tasks = [];
    private readonly List<RegressionDiagnostics.WorkCapture> _work = [];
    private readonly List<Action> _release = [];
    private readonly List<Action> _startQueued = [];
    private readonly List<IDisposable> _resources = [];
    private readonly HashSet<Exception> _expected = new(ReferenceEqualityComparer.Instance);
    private Exception? _primary;

    internal static Task RunAsync(Func<DiagnosticFixtureOwner, Task> body)
    {
        var owner = new DiagnosticFixtureOwner();
        return RegressionFixtureOwner.RunAsync(async () =>
        {
            try { await body(owner); }
            catch (Exception error) { owner._primary = error; throw; }
        }, owner.CleanupAsync);
    }

    internal T Track<T>(T task) where T : Task
    {
        lock (_sync) _tasks.Add(task);
        return task;
    }

    internal T Expect<T>(T error) where T : Exception
    {
        lock (_sync) _expected.Add(error);
        return error;
    }

    internal RegressionDiagnostics.WorkCapture CaptureWork()
    {
        var capture = new RegressionDiagnostics.WorkCapture();
        lock (_sync) _work.Add(capture);
        return capture;
    }

    internal void Release(Action release) => _release.Add(release);
    internal T Own<T>(T resource) where T : IDisposable { _resources.Add(resource); return resource; }

    internal Func<Task> Queue(Func<Task> callback)
    {
        var started = 0;
        Task Start()
        {
            try { return Track(callback()); }
            catch (Exception error) { return Track(Task.FromException(error)); }
        }
        _startQueued.Add(() => { if (Interlocked.CompareExchange(ref started, 1, 0) == 0) _ = Start(); });
        return () => Interlocked.CompareExchange(ref started, 1, 0) == 0 ? Start()
            : Track(Task.FromException(new InvalidOperationException("diagnostic-fixture-callback-invoked-more-than-once")));
    }

    internal Action Queue(Action callback)
    {
        var invoke = Queue(() => { callback(); return Task.CompletedTask; });
        return () => invoke().GetAwaiter().GetResult();
    }

    private async Task CleanupAsync()
    {
        Exception? cleanupFailure = null;
        void Record(Exception error)
        {
            var first = _primary ?? cleanupFailure;
            if (first is null) { cleanupFailure = error; return; }
            if (ReferenceEquals(first, error)) return;
            try
            {
                if (first.Data[RegressionFixtureOwner.CleanupTypesKey] is not List<string> types)
                    first.Data[RegressionFixtureOwner.CleanupTypesKey] = types = [];
                types.Add(error.GetType().FullName ?? error.GetType().Name);
            }
            catch { /* Secondary diagnostics cannot replace the original failure. */ }
        }
        foreach (var release in _release)
        {
            try { release(); }
            catch (Exception error) { Record(error); }
        }
        foreach (var start in _startQueued)
        {
            try { start(); }
            catch (Exception error) { Record(error); }
        }
        var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            Task[] pending;
            lock (_sync)
                pending = _tasks.Concat(_work.SelectMany(work => new[] { work.Worker, work.Observation, work.Report }))
                    .OfType<Task>().Where(joined.Add).ToArray();
            if (pending.Length == 0) break;
            foreach (var task in pending)
            {
                try { await task; }
                catch (Exception error)
                {
                    bool expected;
                    lock (_sync) expected = _expected.Contains(error);
                    if (!expected) Record(error);
                }
            }
            // Joining a parent can publish its actual observer/reporter later;
            // rescan the registered slots after all current parents complete.
        }
        foreach (var resource in _resources)
        {
            try { resource.Dispose(); }
            catch (Exception error) { Record(error); }
        }
        if (cleanupFailure is not null) ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }
}
