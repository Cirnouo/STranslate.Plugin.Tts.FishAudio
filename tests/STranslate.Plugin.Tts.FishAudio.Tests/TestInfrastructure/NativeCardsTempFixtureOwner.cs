using static TestAssertions;

// Explicit ordinary-scene ownership. The legacy entry points do not construct this owner.
// All work and cleanup remain inside the existing original shared-STA action.
internal sealed class NativeCardsTempFixtureOwner
{
    private readonly List<IDisposable> _resources = [];
    private readonly List<string> _directories = [];
    private int _state;

    internal string CreateSettingsDirectory()
    {
        RequireRunning();
        var directory = CreateTempDirectory();
        _directories.Add(directory);
        return directory;
    }

    internal T Own<T>(T resource) where T : IDisposable
    {
        RequireRunning();
        ArgumentNullException.ThrowIfNull(resource);
        if (_resources.Any(existing => ReferenceEquals(existing, resource)))
            throw new InvalidOperationException("native-card-fixture-resource-already-owned");
        _resources.Add(resource);
        return resource;
    }

    internal async Task RunAsync(Func<Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            throw new InvalidOperationException("native-card-fixture-already-started");
        try
        {
            await RegressionFixtureOwner.RunAsync(body, DisposeResourcesAsync, DeleteDirectoriesAsync);
        }
        finally { Volatile.Write(ref _state, 2); }
    }

    private Task DisposeResourcesAsync() => RegressionFixtureOwner.RunAsync(
        () => Task.CompletedTask,
        _resources.AsEnumerable().Reverse().Select(resource => (Func<Task>)(() =>
        {
            resource.Dispose();
            return Task.CompletedTask;
        })).ToArray());

    private Task DeleteDirectoriesAsync() => RegressionFixtureOwner.RunAsync(
        () => Task.CompletedTask,
        _directories.AsEnumerable().Reverse().Select(directory => (Func<Task>)(() =>
        {
            DeleteOwnedDirectory(directory);
            return Task.CompletedTask;
        })).ToArray());

    private static void DeleteOwnedDirectory(string directory)
    {
        var full = Path.GetFullPath(directory);
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FishAudioTests"));
        if (!string.Equals(Path.GetDirectoryName(full), parent, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(Path.GetFileName(full), "N", out _))
            throw new InvalidOperationException("native-card-fixture-directory-not-owned");
        if (!Directory.Exists(full)) return;
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("native-card-fixture-directory-is-reparse-point");
        Directory.Delete(full, recursive: true);
    }

    private void RequireRunning()
    {
        if (Volatile.Read(ref _state) != 1)
            throw new InvalidOperationException("native-card-fixture-not-running");
    }
}
