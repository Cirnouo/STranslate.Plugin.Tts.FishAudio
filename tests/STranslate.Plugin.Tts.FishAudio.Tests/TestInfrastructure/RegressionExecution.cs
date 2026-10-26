internal static class RegressionExecution
{
    internal static readonly TimeSpan WarningTimeout = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);
    internal static readonly TimeSpan DiagnosticTimeout = TimeSpan.FromSeconds(10);

    internal static void ValidateTimeout(TimeSpan executionTimeout)
    {
        if (executionTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(executionTimeout));
    }

    internal static Task WaitAsync(Task work, TimeProvider? timeProvider = null, Action? warning = null) =>
        WaitAsync(work, Timeout, timeProvider, warning);

    internal static Task<T> WaitAsync<T>(Task<T> work, TimeProvider? timeProvider = null, Action? warning = null) =>
        WaitAsync(work, Timeout, timeProvider, warning);

    internal static Task WaitAsync(Task work, TimeSpan executionTimeout, TimeProvider? timeProvider = null, Action? warning = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        ValidateTimeout(executionTimeout);
        if (work.IsCompleted) return work;
        return Guard(work, () => { work.GetAwaiter().GetResult(); return true; }, executionTimeout, timeProvider ?? TimeProvider.System, warning);
    }

    internal static Task<T> WaitAsync<T>(Task<T> work, TimeSpan executionTimeout, TimeProvider? timeProvider = null, Action? warning = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        ValidateTimeout(executionTimeout);
        if (work.IsCompleted) return work;
        return Guard(work, () => work.GetAwaiter().GetResult(), executionTimeout, timeProvider ?? TimeProvider.System, warning);
    }

    private static Task<T> Guard<T>(Task work, Func<T> result, TimeSpan timeout, TimeProvider clock, Action? warning)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = clock.GetTimestamp();
        ITimer? warningTimer = null;
        ITimer? hardTimer = null;
        void DisposeTimers() { warningTimer?.Dispose(); hardTimer?.Dispose(); }
        if (warning is not null && timeout > WarningTimeout)
            warningTimer = clock.CreateTimer(_ =>
            {
                if (completion.Task.IsCompleted) return;
                try { warning(); } catch { /* A slow-run notice cannot change execution. */ }
            }, null, WarningTimeout, System.Threading.Timeout.InfiniteTimeSpan);
        hardTimer = clock.CreateTimer(_ => completion.TrySetException(new RegressionExecutionTimeoutException()), null, timeout, System.Threading.Timeout.InfiniteTimeSpan);
        _ = work.ContinueWith(_ =>
        {
            // A delayed timer callback must not let completion after the hard deadline pass.
            if (clock.GetElapsedTime(started) >= timeout)
                completion.TrySetException(new RegressionExecutionTimeoutException());
            else
            {
                try { completion.TrySetResult(result()); }
                catch (OperationCanceledException error) { completion.TrySetCanceled(error.CancellationToken); }
                catch (Exception error) { completion.TrySetException(error); }
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _ = completion.Task.ContinueWith(_ => DisposeTimers(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        if (completion.Task.IsCompleted) DisposeTimers();
        return completion.Task;
    }
}

// Distinguish the execution guard from a timeout owned by the tested action.
internal sealed class RegressionExecutionTimeoutException : TimeoutException
{
    internal RegressionExecutionTimeoutException() : base("The regression execution hard deadline elapsed.") { }
}
