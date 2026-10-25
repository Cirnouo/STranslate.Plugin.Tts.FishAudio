internal static class RegressionExecution
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    internal static void ValidateTimeout(TimeSpan executionTimeout)
    {
        if (executionTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(executionTimeout));
    }

    internal static Task WaitAsync(Task work, TimeProvider? timeProvider = null) =>
        WaitAsync(work, Timeout, timeProvider);

    internal static Task<T> WaitAsync<T>(Task<T> work, TimeProvider? timeProvider = null) =>
        WaitAsync(work, Timeout, timeProvider);

    internal static Task WaitAsync(Task work, TimeSpan executionTimeout, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        ValidateTimeout(executionTimeout);
        return work.WaitAsync(executionTimeout, timeProvider ?? TimeProvider.System);
    }

    internal static Task<T> WaitAsync<T>(Task<T> work, TimeSpan executionTimeout, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        ValidateTimeout(executionTimeout);
        return work.WaitAsync(executionTimeout, timeProvider ?? TimeProvider.System);
    }
}
