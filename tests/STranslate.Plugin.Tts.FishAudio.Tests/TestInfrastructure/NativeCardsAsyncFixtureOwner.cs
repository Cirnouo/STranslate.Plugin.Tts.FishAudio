// Each original Scenario acquires an explicit owner; there is no ambient allocator.
internal sealed class NativeCardsAsyncFixtureFactory
{
    internal NativeCardsAsyncFixtureOwner Create() => new();
}

internal sealed class NativeCardsAsyncFixtureOwner
{
    private readonly NativeCardsTempFixtureOwner _storage = new();
    private readonly List<IDisposable> _facades = [];
    private readonly List<IDisposable> _registrations = [];
    private readonly List<Action> _detach = [];
    private readonly List<Action> _release = [];
    private readonly List<Task> _operations = [];

    internal string CreateSettingsDirectory() => _storage.CreateSettingsDirectory();

    internal void OwnFacade(IDisposable facade) => _facades.Add(facade);
    internal void OwnRegistration(IDisposable registration) => _registrations.Add(registration);
    internal void OwnCallback(Action detach) => _detach.Add(detach);

    internal TaskCompletionSource<T> OwnResponse<T>(TaskCompletionSource<T> response)
    {
        // Failure cleanup does not manufacture or transfer a replacement stream.
        _release.Add(() => response.TrySetCanceled());
        return response;
    }

    internal Task TrackOperation(Task operation)
    {
        if (!_operations.Any(existing => ReferenceEquals(existing, operation)))
            _operations.Add(operation);
        return operation;
    }

    internal Task RunAsync(Func<Task> body) => _storage.RunAsync(() => RegressionFixtureOwner.RunAsync(body,
        DetachCallbacksAsync, DisposeRegistrationsAsync, DisposeFacadesAsync, ReleaseResponsesAsync, JoinOperationsAsync));

    private Task DetachCallbacksAsync() => RunCleanupAsync(_detach.AsEnumerable().Reverse());

    private Task DisposeRegistrationsAsync() => RunCleanupAsync(_registrations.AsEnumerable().Reverse()
        .Select(registration => (Action)registration.Dispose));

    private Task DisposeFacadesAsync() => RunCleanupAsync(_facades.AsEnumerable().Reverse()
        .Select(facade => (Action)facade.Dispose));

    private Task ReleaseResponsesAsync() => RunCleanupAsync(_release);

    private async Task JoinOperationsAsync()
    {
        // Disposal can synchronously acquire a cancellation-created successor.
        // Await the actual captured sequence, including descendants appended by it.
        var failures = new List<Exception>();
        for (var index = 0; index < _operations.Count; index++)
        {
            try { await _operations[index]; }
            catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count != 0)
            await RegressionFixtureOwner.RunAsync(() => Task.FromException(failures[0]),
                failures.Skip(1).Select(error => (Func<Task>)(() => Task.FromException(error))).ToArray());
    }

    private static Task RunCleanupAsync(IEnumerable<Action> actions) => RegressionFixtureOwner.RunAsync(
        () => Task.CompletedTask, actions.Select(action => (Func<Task>)(() =>
        {
            action();
            return Task.CompletedTask;
        })).ToArray());
}
