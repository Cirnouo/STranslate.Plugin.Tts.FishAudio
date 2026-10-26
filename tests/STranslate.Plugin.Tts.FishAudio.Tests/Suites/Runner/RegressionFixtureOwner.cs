using System.Runtime.ExceptionServices;

// Only regression fixtures use this lifecycle owner; the runner engine keeps its
// existing parent watchdog and does not acquire per-phase execution timers.
internal static class RegressionFixtureOwner
{
    internal const string CleanupTypesKey = "RegressionFixtureCleanupExceptionTypes";

    internal static async Task RunAsync(Func<Task> work, params Func<Task>[] cleanup)
    {
        var root = RegressionActionScope.QueueRoot();
        using var entered = root?.Enter();
        Exception? failure = null;
        try
        {
            try { await work(); }
            catch (Exception error) { failure = error; }
            foreach (var release in cleanup)
            {
                try { await release(); }
                catch (Exception error)
                {
                    if (failure is null) failure = error;
                    else if (!ReferenceEquals(failure, error))
                    {
                        try
                        {
                            if (failure.Data[CleanupTypesKey] is not List<string> types)
                                failure.Data[CleanupTypesKey] = types = [];
                            types.Add(error.GetType().FullName ?? error.GetType().Name);
                        }
                        catch { /* Secondary diagnostics cannot replace the original failure. */ }
                    }
                }
            }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
        finally { root?.Complete(); }
    }

    internal static Task RunReaderAsync(Func<Action, Task> startReader, Action write, Action stopReader,
        Action verify, params Func<Task>[] cleanup)
    {
        Task reader = Task.CompletedTask;
        var written = false;
        return RunAsync(async () =>
        {
            var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            reader = startReader(() => observed.TrySetResult());
            await Task.WhenAny(observed.Task, reader);
            if (reader.IsCompleted) await reader;
            if (!observed.Task.IsCompleted)
            {
                await reader;
                throw new InvalidOperationException("The fixture reader completed before its first observation.");
            }
            write();
            written = true;
        },
        [
            () => { stopReader(); return Task.CompletedTask; },
            () => reader,
            () => { if (written && reader.IsCompletedSuccessfully) verify(); return Task.CompletedTask; },
            .. cleanup
        ]);
    }
}
