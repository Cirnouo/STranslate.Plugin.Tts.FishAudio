internal static class RegressionDiagnostics
{
    internal static Task BoundAsync(Task work, TimeProvider? clock = null) =>
        work.WaitAsync(RegressionExecution.DiagnosticTimeout, clock ?? TimeProvider.System);

    // Scheduling, observing the original action and writing all share one allowance.
    // A blocked external writer cannot be interrupted; expiry retires later writes.
    internal static async Task RunAsync(Func<Func<bool>, Task> diagnostic, TimeProvider? timeProvider = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var started = clock.GetTimestamp();
        var retired = 0;
        var work = Task.Run(async () =>
        {
            try { await diagnostic(() => Volatile.Read(ref retired) != 0).ConfigureAwait(false); }
            catch { /* Auxiliary reporting cannot replace the action outcome. */ }
        });
        try
        {
            var remaining = RegressionExecution.DiagnosticTimeout - clock.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero) await work.WaitAsync(remaining, clock).ConfigureAwait(false);
        }
        catch { /* Observation or output expiry leaves the original outcome intact. */ }
        finally { Interlocked.Exchange(ref retired, 1); }
    }
}
