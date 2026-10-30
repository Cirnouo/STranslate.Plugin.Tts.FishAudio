using System.Runtime.ExceptionServices;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

// Test-only lifecycle over the existing root owner. No guard Tasks or per-stage timers.
internal sealed class OfflineInputRequestFixtureOwner
{
    private readonly List<SettingsViewModel> _views = [];
    private readonly List<IDisposable> _resources = [];
    private readonly List<Task> _commands = [];
    private readonly List<OfflineInputHeldReply> _replies = [];
    internal IReadOnlyList<SettingsViewModel> Views => _views.ToArray();
    internal IReadOnlyList<IDisposable> Resources => _resources.ToArray();
    internal IReadOnlyList<Task> Commands => _commands.ToArray();
    internal IReadOnlyList<OfflineInputHeldReply> Replies => _replies.ToArray();

    internal void Track(SettingsViewModel view)
    {
        if (!_views.Contains(view)) _views.Add(view);
        Track((IDisposable)view);
    }

    internal void Track(IDisposable resource)
    {
        if (!_resources.Contains(resource)) _resources.Add(resource);
    }

    internal Task Track(Task command)
    {
        if (!_commands.Contains(command)) _commands.Add(command);
        return command;
    }

    internal OfflineInputHeldReply Hold(TaskCompletionSource<string> source, string cleanupResponse)
    {
        var reply = new OfflineInputHeldReply(source, cleanupResponse);
        _replies.Add(reply);
        return reply;
    }

    private Task ReleaseReplies()
    {
        ExceptionDispatchInfo? failure = null;
        foreach (var reply in _replies)
            try { reply.Release(); }
            catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
        failure?.Throw();
        return Task.CompletedTask;
    }

    private Task DisposeResources()
    {
        ExceptionDispatchInfo? failure = null;
        for (var index = _resources.Count - 1; index >= 0; index--)
            try { _resources[index].Dispose(); }
            catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
        failure?.Throw();
        return Task.CompletedTask;
    }

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(body,
        ReleaseReplies,
        () => Task.WhenAll(_commands),
        () => Task.WhenAll(_replies.Select(reply => reply.JoinAsync())),
        DisposeResources);
}

internal sealed class OfflineInputHeldReply(TaskCompletionSource<string> source, string cleanupResponse)
{
    private Exception? _expectedFailure;
    internal Task<string> Task => source.Task;
    internal TaskCompletionSource<string> Source => source;
    internal string CleanupResponse => cleanupResponse;
    internal int ObservedCalls { get; private set; }
    internal Exception? ExpectedFailure => _expectedFailure;

    internal Task<string> Observe()
    {
        ObservedCalls++;
        return source.Task;
    }

    internal void SetException(Exception error)
    {
        _expectedFailure = error;
        source.SetException(error);
    }

    internal void Release() => source.TrySetResult(cleanupResponse);

    internal async Task JoinAsync()
    {
        try { await source.Task; }
        catch (Exception error) when (ReferenceEquals(error, _expectedFailure)) { }
    }
}
