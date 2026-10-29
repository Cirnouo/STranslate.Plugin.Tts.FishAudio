using System.Runtime.ExceptionServices;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

internal sealed class HeldFavoritePageFixtureOwner
{
    private readonly List<SettingsViewModel> _views = [];
    private readonly List<IDisposable> _resources = [];
    private readonly List<Task> _pages = [];
    internal string? DirectoryPath { get; set; }
    internal Task? Response { get; set; }
    internal Task? OldPage { get; set; }
    internal Task? NewPage { get; set; }
    internal Task? AfterRemoval { get; set; }
    internal Action? ReleaseHeldResponse { get; set; }
    internal IReadOnlyList<Task> Pages => _pages.ToArray();
    internal IReadOnlyList<SettingsViewModel> Views => _views.ToArray();
    internal IReadOnlyList<IDisposable> Resources => _resources.ToArray();

    internal void Track(SettingsViewModel view)
    {
        if (!_views.Contains(view)) _views.Add(view);
        Track((IDisposable)view);
    }

    internal void Track(IDisposable resource)
    {
        if (!_resources.Contains(resource)) _resources.Add(resource);
    }

    internal void Track(Task task)
    {
        if (!_pages.Contains(task)) _pages.Add(task);
    }

    private Task CaptureCurrentPages()
    {
        foreach (var view in _views) Track(view.PendingFavoriteMetadataTask);
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

    private Task DeleteOwnedDirectory()
    {
        if (DirectoryPath is null) return Task.CompletedTask;
        if (_pages.Any(task => !task.IsCompleted))
            throw new InvalidOperationException("held-page-cleanup-before-original-tasks-ended");
        var full = Path.GetFullPath(DirectoryPath);
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FishAudioTests"));
        var name = Path.GetFileName(full);
        if (!Path.IsPathFullyQualified(DirectoryPath) || !string.Equals(full, DirectoryPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(full), parent, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(name, "N", out var owner) || name != owner.ToString("N"))
            throw new InvalidOperationException("held-page-directory-owner-invalid");
        if (!Directory.Exists(full))
        {
            if (File.Exists(full)) throw new InvalidOperationException("held-page-owner-not-directory");
            return Task.CompletedTask;
        }
        for (DirectoryInfo? directory = new(full); directory is not null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidOperationException("held-page-directory-invalid");
        var pending = new Stack<string>();
        pending.Push(full);
        while (pending.Count != 0)
            foreach (var entry in Directory.GetFileSystemEntries(pending.Pop()))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                    throw new InvalidOperationException("held-page-entry-invalid");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        Directory.Delete(full, recursive: true);
        return Task.CompletedTask;
    }

    private Func<Task>[] CompletionStages() =>
    [
        CaptureCurrentPages,
        DisposeResources,
        () => { ReleaseHeldResponse?.Invoke(); return Task.CompletedTask; },
        () => Task.WhenAll(_pages),
        DeleteOwnedDirectory,
    ];

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(body, CompletionStages());
}

