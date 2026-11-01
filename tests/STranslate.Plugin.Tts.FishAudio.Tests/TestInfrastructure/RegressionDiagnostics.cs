internal static class RegressionDiagnostics
{
    // Registered before fixture launch; assignments retain the actual existing
    // Tasks without invoking a capturer that could replace the action failure.
    internal sealed class WorkCapture
    {
        internal volatile Task? Worker;
        internal volatile Task? Observation;
        internal volatile Task? Report;
    }

    internal static Task BoundAsync(Task work, TimeProvider? clock = null) =>
        work.WaitAsync(RegressionExecution.DiagnosticTimeout, clock ?? TimeProvider.System);

    // Scheduling, observing the original action and writing all share one allowance.
    // A blocked external writer cannot be interrupted; expiry retires later writes.
    internal static async Task RunAsync(Func<Func<bool>, Task> diagnostic, TimeProvider? timeProvider = null, WorkCapture? capture = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var started = clock.GetTimestamp();
        var retired = 0;
        var work = Task.Run(async () =>
        {
            try { await diagnostic(() => Volatile.Read(ref retired) != 0).ConfigureAwait(false); }
            catch { /* Auxiliary reporting cannot replace the action outcome. */ }
        });
        if (capture is not null) capture.Worker = work;
        try
        {
            var remaining = RegressionExecution.DiagnosticTimeout - clock.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero) await work.WaitAsync(remaining, clock).ConfigureAwait(false);
        }
        catch { /* Observation or output expiry leaves the original outcome intact. */ }
        finally { Interlocked.Exchange(ref retired, 1); }
    }
}
