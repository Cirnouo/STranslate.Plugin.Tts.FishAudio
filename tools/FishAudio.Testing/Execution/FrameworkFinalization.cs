namespace FishAudio.Testing.Execution;

public enum FrameworkFinalizationOutcome { Completed, Failed, DeadlineExpired }

public sealed record FrameworkFinalizationResult(FrameworkFinalizationOutcome Outcome);

// The caller's existing cleanup anchor owns the fixed ten seconds. Operation
// completion says nothing about test results, process exit or evidence admission.
public static class FrameworkFinalization
{
    private static readonly TimeSpan Allowance = TimeSpan.FromSeconds(10);

    public static async Task<FrameworkFinalizationResult> RunAsync(long cleanupAnchorTimestamp,
        Func<CancellationToken, Task> operation, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var clock = timeProvider ?? TimeProvider.System;
        if (!TryRemaining(clock, cleanupAnchorTimestamp, out var remaining))
            return new(FrameworkFinalizationOutcome.Failed);
        if (remaining <= TimeSpan.Zero)
            return new(FrameworkFinalizationOutcome.DeadlineExpired);

        var cancellation = new CancellationTokenSource();
        Task<FrameworkFinalizationOutcome> worker;
        try
        {
            // Invoke the delegate itself on background work. Calling it first
            // and then applying WaitAsync cannot bound a synchronous file open.
            worker = Task.Run(async () =>
            {
                try
                {
                    if (!TryRemaining(clock, cleanupAnchorTimestamp, out var beforeWork))
                        return FrameworkFinalizationOutcome.Failed;
                    if (beforeWork <= TimeSpan.Zero)
                        return FrameworkFinalizationOutcome.DeadlineExpired;
                    await operation(cancellation.Token).ConfigureAwait(false);
                    return FrameworkFinalizationOutcome.Completed;
                }
                catch { return FrameworkFinalizationOutcome.Failed; }
            });
        }
        catch
        {
            cancellation.Dispose();
            return new(FrameworkFinalizationOutcome.Failed);
        }

        var notifyCancellation = true;
        try
        {
            if (!TryRemaining(clock, cleanupAnchorTimestamp, out remaining))
                return new(FrameworkFinalizationOutcome.Failed);
            if (remaining <= TimeSpan.Zero)
                return new(FrameworkFinalizationOutcome.DeadlineExpired);

            // This is the only deadline timer. Cancellation is merely a later
            // notification, not another timed wait or a cooperation requirement.
            var outcome = await worker.WaitAsync(remaining, clock).ConfigureAwait(false);
            if (!TryRemaining(clock, cleanupAnchorTimestamp, out remaining))
                return new(FrameworkFinalizationOutcome.Failed);
            if (remaining <= TimeSpan.Zero)
                return new(FrameworkFinalizationOutcome.DeadlineExpired);
            notifyCancellation = outcome == FrameworkFinalizationOutcome.DeadlineExpired;
            return new(outcome);
        }
        catch (TimeoutException) { return new(FrameworkFinalizationOutcome.DeadlineExpired); }
        catch { return new(FrameworkFinalizationOutcome.Failed); }
        finally
        {
            // Never wait here for a stalled operation or cancellation callback.
            // Keep the token source alive until both background users retire.
            _ = ReleaseAsync(cancellation, worker, notifyCancellation);
        }
    }

    private static bool TryRemaining(TimeProvider clock, long anchor, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;
        try
        {
            var now = clock.GetTimestamp();
            if (anchor < 0 || now < anchor || clock.TimestampFrequency <= 0) return false;
            var elapsed = clock.GetElapsedTime(anchor, now);
            if (elapsed < TimeSpan.Zero) return false;
            remaining = Allowance - elapsed;
            return true;
        }
        catch { return false; }
    }

    private static async Task ReleaseAsync(CancellationTokenSource cancellation, Task worker, bool notify)
    {
        try
        {
            // CancelAsync executes user registrations asynchronously. Its task
            // is observed here without delaying the already determined owner.
            var notification = notify ? cancellation.CancelAsync() : Task.CompletedTask;
            try { await Task.WhenAll(worker, notification).ConfigureAwait(false); }
            catch { /* A cancellation callback cannot replace the owner outcome. */ }
        }
        catch { /* Preserve the owner's failure if notification itself fails. */ }
        finally { cancellation.Dispose(); }
    }
}
