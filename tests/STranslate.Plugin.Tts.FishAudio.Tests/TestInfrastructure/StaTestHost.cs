using System.Windows.Threading;

internal static class StaTestHost
{
    private static readonly Lazy<Task<Dispatcher>> SharedDispatcher = new(StartDispatcher);

    internal static Task RunOnStaThreadAsync(Action action) =>
        RunOnStaThreadAsync(action, RegressionExecution.Timeout);

    internal static async Task RunOnStaThreadAsync(Action action, TimeSpan executionTimeout)
    {
        RegressionExecution.ValidateTimeout(executionTimeout);

        var dispatcher = await RegressionExecution.WaitAsync(SharedDispatcher.Value);
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }
        await RegressionExecution.WaitAsync(dispatcher.InvokeAsync(action).Task, executionTimeout);
    }

    internal static Task RunOnStaThreadAsync(Func<Task> action) =>
        RunOnStaThreadAsync(action, RegressionExecution.Timeout);

    internal static async Task RunOnStaThreadAsync(Func<Task> action, TimeSpan executionTimeout)
    {
        RegressionExecution.ValidateTimeout(executionTimeout);

        var dispatcher = await RegressionExecution.WaitAsync(SharedDispatcher.Value);
        if (dispatcher.CheckAccess())
        {
            await action();
            return;
        }
        await RegressionExecution.WaitAsync(dispatcher.InvokeAsync(action).Task.Unwrap(), executionTimeout);
    }

    private static Task<Dispatcher> StartDispatcher()
    {
        var completion = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                completion.SetResult(dispatcher);
                Dispatcher.Run();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Fish Audio regression UI",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
