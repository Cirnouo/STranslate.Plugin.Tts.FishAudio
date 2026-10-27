using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// The commit gate supplies this private context. Snapshot reads never search an
// ancestor checkout, including for paths whose expected state is nonexistence.
internal sealed class SnapshotContext
{
    public int SchemaVersion { get; set; }
    public string Root { get; set; } = "";
    public string Tree { get; set; } = "";
    public string? SourceCommit { get; set; }
    public SnapshotFile[] Files { get; set; } = [];

    internal static SnapshotContext? Read()
    {
        var path = Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT");
        if (string.IsNullOrEmpty(path)) return null;
        using var input = File.OpenRead(path);
        if (input.Length is <= 0 or > 16 * 1024 * 1024) throw new InvalidDataException("Invalid snapshot context size.");
        var context = JsonSerializer.Deserialize<SnapshotContext>(input, RegressionReport.JsonOptions)
            ?? throw new InvalidDataException("Missing snapshot context.");
        if (context.SchemaVersion != 1 || !Path.IsPathFullyQualified(context.Root) || !Directory.Exists(context.Root) ||
            !IsObjectId(context.Tree) || (context.SourceCommit is not null && !IsObjectId(context.SourceCommit)) ||
            context.Files is null || context.Files.Any(file => file is null) ||
            context.Files.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != context.Files.Length)
            throw new InvalidDataException("Invalid snapshot context.");
        context.Root = Path.GetFullPath(context.Root);
        var sourceRoot = RegressionSource.FindSourceRoot();
        if (sourceRoot is null || !string.Equals(Path.TrimEndingDirectorySeparator(context.Root),
            Path.TrimEndingDirectorySeparator(sourceRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Snapshot root does not own the executing runner.");
        context.CheckReparsePoints(context.Root);
        foreach (var file in context.Files)
        {
            _ = context.Resolve(file.Path);
            if (!IsObjectId(file.Blob) || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid snapshot input identity.");
        }
        return context;
    }

    internal string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) ||
            relativePath.Replace('\\', '/').Split('/').Any(part => part is ".." or "." or ""))
            throw new InvalidDataException("Invalid snapshot input path.");
        var fullPath = Path.GetFullPath(Path.Combine(Root, relativePath));
        var prefix = Path.TrimEndingDirectorySeparator(Root) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Snapshot input escaped its root.");
        CheckReparsePoints(fullPath);
        return fullPath;
    }

    private void CheckReparsePoints(string fullPath)
    {
        // Check terminal directories and the root itself as well as file parents.
        for (FileSystemInfo? current = Directory.Exists(fullPath) ? new DirectoryInfo(fullPath) : new FileInfo(fullPath);
            current is not null;)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Snapshot input contains a reparse point.");
            if (string.Equals(Path.TrimEndingDirectorySeparator(current.FullName), Path.TrimEndingDirectorySeparator(Root),
                StringComparison.OrdinalIgnoreCase)) break;
            current = current is DirectoryInfo directory ? directory.Parent : ((FileInfo)current).Directory;
        }
    }

    internal string InputPath(string relativePath)
    {
        var path = Resolve(relativePath);
        if (File.Exists(path)) return ExistingFile(relativePath);
        if (Directory.Exists(path))
        {
            var prefix = relativePath.Replace('\\', '/').TrimEnd('/') + "/";
            foreach (var file in Files.Where(file => file.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                _ = ExistingFile(file.Path);
        }
        return path;
    }

    internal string ExistingFile(string relativePath)
    {
        var path = Resolve(relativePath);
        var key = relativePath.Replace('\\', '/');
        var file = Files.SingleOrDefault(file => string.Equals(file.Path, key, StringComparison.OrdinalIgnoreCase));
        if (file is null || !File.Exists(path)) throw new FileNotFoundException("Missing owned snapshot input.");
        using var input = File.OpenRead(path);
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(input)), file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Snapshot input identity changed.");
        return path;
    }

    internal RegressionSourceIdentity Identity() => new()
    {
        Commit = SourceCommit, Tree = Tree, Available = true, Snapshot = true,
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("snapshot-v1\n" + SourceCommit + "\n" + Tree + "\n")))
    };

    private static bool IsObjectId(string? value) => value is { Length: 40 or 64 } && value.All(Uri.IsHexDigit);
}

internal sealed class SnapshotFile
{
    public string Path { get; set; } = "";
    public string Blob { get; set; } = "";
    public string Sha256 { get; set; } = "";
}
