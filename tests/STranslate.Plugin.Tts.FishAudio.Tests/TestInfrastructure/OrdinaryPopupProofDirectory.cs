// Ordinary-only output ownership. Legacy/manual explicit proof destinations
// retain their existing lifetime and are never passed to this cleanup owner.
internal sealed class OrdinaryPopupProofDirectory
{
    private const string Prefix = "FishAudioPopupProof-";
    private string? _parent;
    private string? _name;
    private bool _started;
    internal string? DirectoryPath { get; private set; }
    internal Exception? CleanupFailure { get; private set; }
    internal bool CleanupComplete { get; private set; }

    internal Task RunAsync(Func<string, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (_started) throw new InvalidOperationException("ordinary-popup-proof-owner-reused");
        _started = true;
        return RegressionFixtureOwner.RunAsync(async () =>
        {
            _parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            var directory = Directory.CreateTempSubdirectory(Prefix);
            DirectoryPath = directory.FullName;
            _name = directory.Name;
            await body(directory.FullName);
        }, DeleteOwnedDirectoryAsync);
    }

    internal Task DeleteOwnedDirectoryAsync()
    {
        if (DirectoryPath is null) return Task.CompletedTask;
        try
        {
            var full = Path.GetFullPath(DirectoryPath);
            if (!Path.IsPathFullyQualified(DirectoryPath)
                || !string.Equals(full, DirectoryPath, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetDirectoryName(full), _parent, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(full), _name, StringComparison.Ordinal)
                || _name is null || !_name.StartsWith(Prefix, StringComparison.Ordinal) || _name.Length <= Prefix.Length)
                throw new InvalidOperationException("ordinary-popup-proof-directory-owner-invalid");
            if (!Directory.Exists(full))
            {
                if (File.Exists(full)) throw new InvalidOperationException("ordinary-popup-proof-owner-not-directory");
                CleanupComplete = true;
                return Task.CompletedTask;
            }
            for (DirectoryInfo? directory = new(full); directory is not null; directory = directory.Parent)
                if (!directory.Exists || (directory.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                    throw new InvalidOperationException("ordinary-popup-proof-directory-invalid");
            var pending = new Stack<string>();
            pending.Push(full);
            while (pending.Count != 0)
                foreach (var entry in Directory.GetFileSystemEntries(pending.Pop()))
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                        throw new InvalidOperationException("ordinary-popup-proof-entry-invalid");
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                }
            Directory.Delete(full, recursive: true);
            CleanupComplete = true;
            return Task.CompletedTask;
        }
        catch (Exception error)
        {
            CleanupFailure ??= error;
            throw;
        }
    }
}
