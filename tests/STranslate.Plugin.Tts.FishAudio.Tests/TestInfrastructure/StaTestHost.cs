using System.Windows.Threading;

internal static class StaTestHost
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private static readonly Lazy<Task<Dispatcher>> SharedDispatcher = new(StartDispatcher);

    internal static async Task RunOnStaThreadAsync(Action action)
    {
        var dispatcher = await SharedDispatcher.Value.WaitAsync(TestTimeout);
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }
        await dispatcher.InvokeAsync(action).Task.WaitAsync(TestTimeout);
    }

    internal static async Task RunOnStaThreadAsync(Func<Task> action)
    {
        var dispatcher = await SharedDispatcher.Value.WaitAsync(TestTimeout);
        if (dispatcher.CheckAccess())
        {
            await action();
            return;
        }
        await dispatcher.InvokeAsync(action).Task.Unwrap().WaitAsync(TestTimeout);
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
