using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Windows.Automation.Provider;
using System.Windows.Automation.Peers;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

// Actual acquired references are retained for independent failure oracles/rescue.
internal sealed class NativeAvatarFixtureTrace
{
    internal string Directory { get; set; } = "";
    internal SettingsViewModel? ViewModel { get; set; }
    internal List<IDisposable> Resources { get; } = [];
    internal List<Task> Commands { get; } = [];
    internal List<TaskCompletionSource<Stream>> Responses { get; } = [];
    internal List<Task> ObservedReplies { get; } = [];
    internal List<DispatcherOperation> Automation { get; } = [];
    internal List<Task> AutomationTasks { get; } = [];
    internal TaskCompletionSource<Stream>? AutomationReply { get; set; }
}

// Only typed ordinary Avatar observations construct this explicit owner.
internal sealed class NativeAvatarFixtureOwner(Action<string, NativeAvatarFixtureTrace>? observe = null,
    bool holdAutomationReply = false)
{
    private readonly NativeCardsTempFixtureOwner _storage = new();
    private readonly NativeAvatarFixtureTrace _trace = new();
    private readonly List<Action> _detach = [];
    private readonly HashSet<Task> _releasedReplies = [];
    private readonly object _commandsGate = new();

    internal string CreateSettingsDirectory() => _trace.Directory = _storage.CreateSettingsDirectory();

    internal void OwnResource(IDisposable resource)
    {
        _storage.Own(resource);
        _trace.Resources.Add(resource);
    }

    internal void OwnFacade(SettingsViewModel viewModel) => _trace.ViewModel = viewModel;
    internal void OwnCallback(Action detach) => _detach.Add(detach);
    internal void Observe(string stage) => observe?.Invoke(stage, _trace);

    internal void PrepareAutomationReply(TestHttpServiceProxy http)
    {
        if (!holdAutomationReply) return;
        var response = OwnResponse(new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously));
        _trace.AutomationReply = response;
        http.GetAsStreamAsyncHandler = (_, _, _) => ObserveReply(response);
    }

    internal TaskCompletionSource<Stream> OwnResponse(TaskCompletionSource<Stream> response)
    {
        _trace.Responses.Add(response);
        return response;
    }

    internal Task<Stream> ObserveReply(TaskCompletionSource<Stream> response)
    {
        _trace.ObservedReplies.Add(response.Task);
        return response.Task;
    }

    internal Task TrackOperation(Task operation)
    {
        lock (_commandsGate)
            if (!_trace.Commands.Any(existing => ReferenceEquals(existing, operation))) _trace.Commands.Add(operation);
        return operation;
    }

    internal void ObserveCommand(IAsyncRelayCommand command)
    {
        PropertyChangedEventHandler changed = (_, args) =>
        {
            if (args.PropertyName == nameof(IAsyncRelayCommand.ExecutionTask) && command.ExecutionTask is { } actual)
                TrackOperation(actual);
        };
        command.PropertyChanged += changed;
        OwnCallback(() => command.PropertyChanged -= changed);
    }

    internal void InvokeAutomation(IInvokeProvider provider)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        dispatcher.VerifyAccess();
        if (provider is not ButtonAutomationPeer peer || !ReferenceEquals(peer.Dispatcher, dispatcher))
            throw new InvalidOperationException("avatar-uia-owner-dispatcher-mismatch");
        var postingThread = Environment.CurrentManagedThreadId;
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("avatar-uia-owner-not-sta");
        var acquired = new List<DispatcherOperation>();
        DispatcherHookEventHandler posted = (_, args) =>
        {
            // Hooks also observe other producer threads. Only this synchronous
            // provider invocation on this exact dispatcher/posting thread binds.
            if (Environment.CurrentManagedThreadId != postingThread || !dispatcher.CheckAccess()) return;
            acquired.Add(args.Operation);
            _trace.Automation.Add(args.Operation);
            _trace.AutomationTasks.Add(args.Operation.Task);
        };
        Exception? failure = null;
        dispatcher.Hooks.OperationPosted += posted;
        try { provider.Invoke(); } // No await, queue fence or pump in this window.
        catch (Exception error) { failure = error; throw; }
        finally { Restore(failure, () => dispatcher.Hooks.OperationPosted -= posted); }
        // Versioned WPF source posts one Input callback for Button's provider.
        // Retain every acquired operation even when rejecting ambiguity.
        if (acquired.Count != 1 || acquired[0].Priority != DispatcherPriority.Input)
            throw new InvalidOperationException("avatar-uia-post-not-unambiguous");
    }

    internal Task RunAsync(Func<Task> body) => _storage.RunAsync(() => RegressionFixtureOwner.RunAsync(body,
        JoinAutomationAsync, DisposeFacadeAsync, ReleaseRepliesAsync, JoinCommandsAsync, JoinRepliesAsync, DetachCallbacksAsync));

    private async Task JoinAutomationAsync()
    {
        // Complete the actual UIA callback before facade retirement. Its real
        // command ExecutionTask observer remains attached across this await.
        var failures = new List<Exception>();
        for (var index = 0; index < _trace.AutomationTasks.Count; index++)
            try { await _trace.AutomationTasks[index]; } catch (Exception error) { failures.Add(error); }
        ThrowFailures(failures);
    }

    private Task DisposeFacadeAsync()
    {
        _trace.ViewModel?.Dispose();
        return Task.CompletedTask;
    }

    private Task ReleaseRepliesAsync()
    {
        foreach (var response in _trace.Responses)
            if (response.TrySetCanceled()) _releasedReplies.Add(response.Task);
        return Task.CompletedTask;
    }

    private async Task JoinCommandsAsync()
    {
        var failures = new List<Exception>();
        for (var index = 0; ; index++)
        {
            Task actual;
            lock (_commandsGate)
            {
                if (index == _trace.Commands.Count) break;
                actual = _trace.Commands[index];
            }
            try { await actual; } catch (Exception error) { failures.Add(error); }
        }
        ThrowFailures(failures);
    }

    private async Task JoinRepliesAsync()
    {
        var failures = new List<Exception>();
        foreach (var response in _trace.Responses)
        {
            try { await response.Task; }
            catch (OperationCanceledException) when (_releasedReplies.Contains(response.Task) && response.Task.IsCanceled) { }
            catch (Exception error) { failures.Add(error); }
        }
        ThrowFailures(failures);
    }

    private Task DetachCallbacksAsync()
    {
        Restore(null, _detach.AsEnumerable().Reverse().ToArray());
        return Task.CompletedTask;
    }

    internal static void Restore(Exception? primary, params Action[] restoration)
    {
        var failure = primary;
        foreach (var restore in restoration)
        {
            try { restore(); }
            catch (Exception error)
            {
                if (failure is null) failure = error;
                else if (!ReferenceEquals(failure, error))
                {
                    try
                    {
                        if (failure.Data[RegressionFixtureOwner.CleanupTypesKey] is not List<string> types)
                            failure.Data[RegressionFixtureOwner.CleanupTypesKey] = types = [];
                        types.Add(error.GetType().FullName ?? error.GetType().Name);
                    }
                    catch { /* Secondary diagnostics cannot replace the first failure. */ }
                }
            }
        }
        if (primary is null && failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void ThrowFailures(IReadOnlyList<Exception> failures)
    {
        if (failures.Count == 0) return;
        Restore(null, failures.Select(error => (Action)(() => ExceptionDispatchInfo.Capture(error).Throw())).ToArray());
    }
}
