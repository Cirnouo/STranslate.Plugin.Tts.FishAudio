using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

internal sealed class RegressionSourceIdentity
{
    public string? Commit { get; set; }
    public string? Tree { get; set; }
    public string Fingerprint { get; set; } = "";
    public bool Dirty { get; set; }
    public bool Available { get; set; }
    public bool Snapshot { get; set; }
    public bool DirtyScopeRecorded { get; set; }
    public string[] DirtyAffectedIds { get; set; } = [];
    public string[] DirtyRuleIds { get; set; } = [];
    public string[] DirtyRecommendedGroups { get; set; } = [];
}

internal sealed class RegressionSource
{
    internal string? Root { get; private init; }
    internal RegressionSourceIdentity Identity { get; private init; } = new();
    private string[]? DirtyPaths { get; init; }

    internal void RecordDirtyScope(IReadOnlyList<RegressionCase> registry)
    {
        if (!Identity.Available || !Identity.Dirty || DirtyPaths is null) return;
        var impact = RegressionImpact.Select(registry, DirtyPaths);
        Identity.DirtyScopeRecorded = true;
        Identity.DirtyAffectedIds = impact.Ids.Order(StringComparer.Ordinal).ToArray();
        Identity.DirtyRuleIds = impact.RuleIds;
        Identity.DirtyRecommendedGroups = impact.RecommendedGroups;
    }

    internal static async Task<RegressionSource> CaptureAsync()
    {
        if (SnapshotContext.Read() is { } snapshot)
            return new RegressionSource { Root = snapshot.Root, Identity = snapshot.Identity() };
        var sourceRoot = FindSourceRoot();
        if (sourceRoot is null)
            return new RegressionSource { Identity = new() { Fingerprint = Convert.ToHexStringLower(SHA256.HashData("unavailable"u8)) } };
        var rootOutput = await CommandAsync(sourceRoot, "git", ["rev-parse", "--show-toplevel"]);
        var root = rootOutput is null ? null : Encoding.UTF8.GetString(rootOutput).Trim();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) || !string.Equals(Path.GetFullPath(root), sourceRoot, StringComparison.OrdinalIgnoreCase))
        {
            // A missing or foreign Git root cannot borrow the caller's checkout identity.
            return new RegressionSource { Root = sourceRoot, Identity = new() { Fingerprint = Convert.ToHexStringLower(SHA256.HashData("unavailable"u8)) } };
        }
        var head = await CommandAsync(root, "git", ["rev-parse", "HEAD"]);
        var tree = await CommandAsync(root, "git", ["rev-parse", "HEAD^{tree}"]);
        var diff = await CommandAsync(root, "git", ["diff", "--binary", "HEAD", "--"]);
        var others = await CommandAsync(root, "git", ["ls-files", "--others", "--exclude-standard", "-z"]);
        if (head is null || tree is null || diff is null || others is null)
            return new RegressionSource { Root = root, Identity = new() { Fingerprint = Convert.ToHexStringLower(SHA256.HashData("unavailable"u8)) } };
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(head);
        hash.AppendData(tree);
        hash.AppendData(diff);
        hash.AppendData(others);
        try
        {
            foreach (var path in Encoding.UTF8.GetString(others).Split('\0', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal))
            {
                using var file = File.OpenRead(Path.Combine(root, path));
                hash.AppendData(await SHA256.HashDataAsync(file));
            }
        }
        catch
        {
            return new RegressionSource { Root = root, Identity = new() { Fingerprint = Convert.ToHexStringLower(SHA256.HashData("unavailable"u8)) } };
        }
        var dirtyNames = diff.Length == 0 ? [] : await CommandAsync(root, "git", ["diff", "--no-renames", "--name-only", "-z", "HEAD", "--"]);
        return new RegressionSource
        {
            Root = root,
            DirtyPaths = dirtyNames is null ? null : Encoding.UTF8.GetString(dirtyNames).Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Concat(Encoding.UTF8.GetString(others).Split('\0', StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.Ordinal).ToArray(),
            Identity = new()
            {
                Commit = SafeHash(head), Tree = SafeHash(tree), Fingerprint = Convert.ToHexStringLower(hash.GetHashAndReset()),
                Dirty = diff.Length != 0 || others.Length != 0, Available = true
            }
        };
    }

    internal static string? FindSourceRoot()
    {
        // The executable's ancestry remains stable when an absolute build/run command is
        // invoked from another checkout. The ignored standalone harness has the same owner.
        var assemblyDirectory = Path.GetDirectoryName(typeof(RegressionSource).Assembly.Location);
        for (var directory = new DirectoryInfo(assemblyDirectory ?? AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var root = directory.FullName;
            if (File.Exists(Path.Combine(root, "build.ps1")) &&
                File.Exists(Path.Combine(root, "STranslate.Plugin.Tts.FishAudio", "STranslate.Plugin.Tts.FishAudio.csproj")) &&
                File.Exists(Path.Combine(root, "tests", "STranslate.Plugin.Tts.FishAudio.Tests", "STranslate.Plugin.Tts.FishAudio.Tests.csproj")))
                return root;
        }
        return null;
    }

    internal async Task<string[]?> ChangedPathsAsync(string baseRef)
    {
        if (Identity.Snapshot) return null;
        if (Root is null || !Identity.Available) return null;
        var mergeBaseBytes = await CommandAsync(Root, "git", ["merge-base", "HEAD", baseRef]);
        var mergeBase = mergeBaseBytes is null ? null : SafeHash(mergeBaseBytes);
        if (mergeBase is null) return null;
        return await ChangedPathsSinceCommitAsync(mergeBase);
    }

    internal async Task<string[]?> ChangedPathsSinceCommitAsync(string? commit)
    {
        if (Identity.Snapshot) return null;
        if (Root is null || !Identity.Available || commit is null || commit.Length is not (40 or 64) || !commit.All(Uri.IsHexDigit)) return null;
        // A direct previous-commit comparison also includes current staged/unstaged work;
        // disabling rename detection preserves both old and new dependency boundaries.
        var diff = await CommandAsync(Root, "git", ["diff", "--no-renames", "--name-only", "-z", commit, "--"]);
        var others = await CommandAsync(Root, "git", ["ls-files", "--others", "--exclude-standard", "-z"]);
        if (diff is null || others is null) return null;
        return Encoding.UTF8.GetString(diff).Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Concat(Encoding.UTF8.GetString(others).Split('\0', StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string? SafeHash(byte[] bytes)
    {
        var value = Encoding.UTF8.GetString(bytes).Trim();
        return value.Length is 40 or 64 && value.All(Uri.IsHexDigit) ? value.ToLowerInvariant() : null;
    }

    internal static bool HasConsistentCleanIdentity(RegressionSourceIdentity identity)
    {
        if (!identity.Available || identity.Dirty || identity.Snapshot || identity.Commit is not { Length: 40 or 64 } commit ||
            identity.Tree is not { Length: 40 or 64 } tree || !commit.All(Uri.IsHexDigit) || !tree.All(Uri.IsHexDigit))
            return false;
        // A clean capture hashes only these two canonical Git output lines: it has no
        // diff, untracked paths or file hashes that could explain another fingerprint.
        var bytes = Encoding.UTF8.GetBytes(commit.ToLowerInvariant() + "\n" + tree.ToLowerInvariant() + "\n");
        var expected = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return string.Equals(expected, identity.Fingerprint, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]?> CommandAsync(string directory, string command, string[] arguments)
    {
        try
        {
            using var process = new Process { StartInfo = new ProcessStartInfo(command) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            if (!process.Start()) return null;
            using var output = new MemoryStream();
            var stdout = process.StandardOutput.BaseStream.CopyToAsync(output);
            var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(deadline.Token);
                await Task.WhenAll(stdout, stderr).WaitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }
            return process.ExitCode == 0 ? output.ToArray() : null;
        }
        catch { return null; }
    }
}
