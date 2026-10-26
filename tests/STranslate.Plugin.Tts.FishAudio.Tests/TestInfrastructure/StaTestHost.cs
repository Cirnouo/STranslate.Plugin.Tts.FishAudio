using System.Windows.Threading;

internal static class StaTestHost
{
    private static readonly Lazy<Task<Dispatcher>> SharedDispatcher = new(StartDispatcher);

    internal static Task RunOnStaThreadAsync(Action action) =>
        RunOnStaThreadAsync(action, RegressionExecution.Timeout);

    internal static async Task RunOnStaThreadAsync(Action action, TimeSpan executionTimeout)
    {
        RegressionExecution.ValidateTimeout(executionTimeout);

        var dispatcher = await GetDispatcherAsync();
        var root = RegressionActionScope.QueueRoot();
        void Run()
        {
            using var entered = root?.Enter();
            try { action(); }
            finally { root?.Complete(); }
        }
        if (dispatcher.CheckAccess())
        {
            Run();
            return;
        }
        await RegressionExecution.WaitAsync(dispatcher.InvokeAsync(Run).Task, executionTimeout);
    }

    internal static Task RunOnStaThreadAsync(Func<Task> action) =>
        RunOnStaThreadAsync(action, RegressionExecution.Timeout);

    internal static async Task RunOnStaThreadAsync(Func<Task> action, TimeSpan executionTimeout)
    {
        RegressionExecution.ValidateTimeout(executionTimeout);

        var dispatcher = await GetDispatcherAsync();
        var root = RegressionActionScope.QueueRoot();
        async Task RunAsync()
        {
            using var entered = root?.Enter();
            try { await action(); }
            finally { root?.Complete(); }
        }
        if (dispatcher.CheckAccess())
        {
            await RunAsync();
            return;
        }
        await RegressionExecution.WaitAsync(dispatcher.InvokeAsync(RunAsync).Task.Unwrap(), executionTimeout);
    }

    private static async Task<Dispatcher> GetDispatcherAsync()
    {
        var starting = !SharedDispatcher.IsValueCreated;
        if (starting) RegressionActionScope.DispatcherStarting();
        var dispatcher = await RegressionExecution.WaitAsync(SharedDispatcher.Value);
        if (starting) RegressionActionScope.DispatcherReady();
        return dispatcher;
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
