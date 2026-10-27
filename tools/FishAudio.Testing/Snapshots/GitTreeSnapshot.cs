#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FishAudio.Testing.Snapshots;

public sealed class GitTreeSnapshotException : IOException
{
    public GitTreeSnapshotException(string code) : base("snapshot-" + code) { Code = "snapshot-" + code; }
    public string Code { get; }
}

public sealed class GitSnapshotFile
{
    internal GitSnapshotFile(string path, string mode, string blob, string sha256, long length)
    { Path = path; Mode = mode; Blob = blob; Sha256 = sha256; Length = length; }
    public string Path { get; }
    public string Mode { get; }
    public string Blob { get; }
    public string Sha256 { get; }
    public long Length { get; }
}

public sealed class GitTreeSnapshotResult
{
    internal GitTreeSnapshotResult(string root, string tree, List<GitSnapshotFile> files)
    { Root = root; Tree = tree; Files = files.AsReadOnly(); }
    public string Root { get; }
    public string Tree { get; }
    public IReadOnlyList<GitSnapshotFile> Files { get; }
}

/// <summary>
/// Materializes only immutable Git tree/blob objects, without checkout filters,
/// archive substitutions, index writes or live-file fallback. Failed partial
/// destinations remain caller-owned. This source also compiles alone in pwsh.
/// Windows extended/device namespaces and components containing a tilde followed
/// by ASCII digits are conservatively unsupported, including raw Git names.
/// </summary>
public static class GitTreeSnapshot
{
    private const int MaximumEntries = 1_000_000;
    private const int MaximumTreeBytes = 64 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

    public static GitTreeSnapshotResult Materialize(string gitExecutable, string repositoryRoot, string tree, string destination)
        => Materialize(gitExecutable, repositoryRoot, tree, destination, TimeSpan.FromSeconds(120));

    public static GitTreeSnapshotResult Materialize(string gitExecutable, string repositoryRoot, string tree, string destination,
        TimeSpan timeout)
    {
        try { return MaterializeAsync(gitExecutable, repositoryRoot, tree, destination, timeout).GetAwaiter().GetResult(); }
        catch (GitTreeSnapshotException) { throw; }
        catch (OperationCanceledException) { throw Rejected("metadata-deadline"); }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
            error is ArgumentException || error is NotSupportedException || error is InvalidOperationException ||
            error is System.ComponentModel.Win32Exception || error is OverflowException)
        { throw Rejected("invalid-input-or-io"); }
    }

    private static async Task<GitTreeSnapshotResult> MaterializeAsync(string gitExecutable, string repositoryRoot,
        string tree, string destination, TimeSpan timeout)
    {
        Require(IsOid(tree), "invalid-tree-oid");
        Require(timeout > TimeSpan.Zero && timeout <= TimeSpan.FromSeconds(120), "invalid-metadata-allowance");
        ValidateInputPath(repositoryRoot);
        ValidateInputPath(destination);
        var root = ExistingDirectory(repositoryRoot);
        var marker = Path.Combine(root, ".git");
        Require(File.Exists(marker) || Directory.Exists(marker), "repository-marker-missing");
        NoReparse(marker);
        Require(Path.IsPathFullyQualified(gitExecutable) && File.Exists(gitExecutable), "git-executable-missing");
        NoReparse(gitExecutable);
        Require(Path.IsPathFullyQualified(destination), "destination-not-absolute");
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        ValidateAbsolutePath(output);
        Require(!output.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "destination-inside-repository");
        Require(!File.Exists(output) && !Directory.Exists(output), "destination-not-fresh");
        _ = ExistingDirectory(Path.GetDirectoryName(output) ?? "");
        using var deadline = new CancellationTokenSource(timeout);
        var token = deadline.Token;
        Directory.CreateDirectory(output);
        NoReparse(output);
        var files = new List<GitSnapshotFile>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<(string Prefix, string Oid)>();
        pending.Enqueue(("", tree));
        var batch = new Batch(gitExecutable, root);
        Exception? primary = null;
        try
        {
            while (pending.Count != 0)
            {
                var current = pending.Dequeue();
                var bytes = await batch.ReadTreeAsync(current.Oid, token).ConfigureAwait(false);
                var position = 0;
                while (position != bytes.Length)
                {
                    var space = Array.IndexOf(bytes, (byte)' ', position);
                    Require(space > position && space - position <= 6, "invalid-tree-record");
                    var mode = Encoding.ASCII.GetString(bytes, position, space - position);
                    var nul = Array.IndexOf(bytes, (byte)0, space + 1);
                    var oidLength = tree.Length / 2;
                    Require(nul > space + 1 && bytes.Length - nul - 1 >= oidLength, "invalid-tree-record");
                    var component = Utf8.GetString(bytes, space + 1, nul - space - 1);
                    ValidateComponent(component);
                    var relative = current.Prefix + component;
                    Require(paths.Count < MaximumEntries && paths.Add(relative), "duplicate-or-case-colliding-path");
                    var oid = Convert.ToHexString(bytes.AsSpan(nul + 1, oidLength)).ToLowerInvariant();
                    position = nul + 1 + oidLength;
                    Require(mode == "40000" || mode == "100644" || mode == "100755", "unsupported-file-mode");
                    if (mode == "40000") pending.Enqueue((relative + "/", oid));
                    else
                    {
                        var target = Path.GetFullPath(Path.Combine(output, relative.Replace('/', Path.DirectorySeparatorChar)));
                        Require(target.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "path-escapes-root");
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        NoReparse(Path.GetDirectoryName(target)!);
                        files.Add(await batch.WriteBlobAsync(oid, relative, mode, target, token).ConfigureAwait(false));
                    }
                }
            }
            Require(files.Count != 0, "empty-tree");
            await batch.CompleteAsync(token).ConfigureAwait(false);
            return new GitTreeSnapshotResult(output, tree, files);
        }
        catch (Exception error) { primary = error; throw; }
        finally { await batch.CloseAsync(primary).ConfigureAwait(false); }
    }

    private static void ValidateComponent(string name, bool rejectGitMetadata = true)
    {
        Require(!HasAliasPattern(name), "unsupported-alias-path");
        Require(name.Length != 0 && name != "." && name != ".." && (!rejectGitMetadata || !name.Equals(".git", StringComparison.OrdinalIgnoreCase)) &&
            !name.EndsWith(".", StringComparison.Ordinal) && !name.EndsWith(" ", StringComparison.Ordinal), "invalid-path");
        foreach (var character in name)
            Require(!char.IsControl(character) && "<>:\"/\\|?*".IndexOf(character) < 0, "invalid-path");
        var stem = name.Split('.')[0].TrimEnd(' ', '.').ToUpperInvariant();
        Require(stem != "CON" && stem != "PRN" && stem != "AUX" && stem != "NUL" &&
            stem != "CONIN$" && stem != "CONOUT$" &&
            !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                "123456789\u00b9\u00b2\u00b3".IndexOf(stem[3]) >= 0), "reserved-path");
    }

    private static string ExistingDirectory(string path)
    {
        ValidateInputPath(path);
        Require(!string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path) && Directory.Exists(path), "directory-missing-or-relative");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        ValidateAbsolutePath(full);
        NoReparse(full);
        return full;
    }

    private static void ValidateAbsolutePath(string full)
    {
        ValidateInputPath(full);
        var prefix = Path.GetPathRoot(full) ?? "";
        foreach (var part in full.Substring(prefix.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries)) ValidateComponent(part, rejectGitMetadata: false);
    }

    private static void ValidateInputPath(string path)
    {
        Require(!string.IsNullOrEmpty(path), "invalid-path");
        var normalized = path.Replace('/', '\\');
        Require(!normalized.StartsWith(@"\\?\", StringComparison.Ordinal) &&
            !normalized.StartsWith(@"\\.\", StringComparison.Ordinal) &&
            !normalized.StartsWith(@"\??\", StringComparison.Ordinal), "unsupported-namespace-path");
        foreach (var part in normalized.Split('\\')) Require(!HasAliasPattern(part), "unsupported-alias-path");
    }

    private static bool HasAliasPattern(string part)
    {
        for (var index = 0; index + 1 < part.Length; index++)
            if (part[index] == '~' && part[index + 1] >= '0' && part[index + 1] <= '9') return true;
        return false;
    }

    private static void NoReparse(string path)
    {
        for (FileSystemInfo? item = File.Exists(path) ? new FileInfo(path) : new DirectoryInfo(path); item != null;
            item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent)
        {
            item.Refresh();
            Require(item.Exists && (item.Attributes & FileAttributes.ReparsePoint) == 0, "missing-or-reparse-path");
        }
    }

    private static bool IsOid(string value)
    {
        if (value == null || (value.Length != 40 && value.Length != 64)) return false;
        foreach (var character in value) if (!(character >= '0' && character <= '9') && !(character >= 'a' && character <= 'f')) return false;
        return true;
    }

    private static GitTreeSnapshotException Rejected(string code) => new GitTreeSnapshotException(code);
    private static void Require(bool condition, string code) { if (!condition) throw Rejected(code); }

    private sealed class Batch
    {
        private readonly Process _process;
        private readonly Stream _input;
        private readonly BufferedStream _output;
        private readonly Task _error;
        private bool _completed;

        internal Batch(string executable, string root)
        {
            _process = new Process();
            _process.StartInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var key in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_PREFIX", "GIT_INDEX_FILE" })
                _process.StartInfo.Environment.Remove(key);
            _process.StartInfo.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
            _process.StartInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            _process.StartInfo.ArgumentList.Add("cat-file");
            _process.StartInfo.ArgumentList.Add("--batch");
            try
            {
                Require(_process.Start(), "git-start-failed");
                _input = _process.StandardInput.BaseStream;
                _output = new BufferedStream(_process.StandardOutput.BaseStream, 65536);
                _error = _process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            }
            catch { _process.Dispose(); throw; }
        }

        internal async Task<byte[]> ReadTreeAsync(string oid, CancellationToken token)
        {
            var size = await RequestAsync(oid, "tree", token).ConfigureAwait(false);
            Require(size <= MaximumTreeBytes, "tree-size-limit");
            var bytes = new byte[(int)size];
            await ReadExactlyAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
            await ReadTerminatorAsync(token).ConfigureAwait(false);
            using var hash = ObjectHash(oid, "tree", size);
            hash.AppendData(bytes);
            Require(Finish(hash) == oid, "object-oid-mismatch");
            return bytes;
        }

        internal async Task<GitSnapshotFile> WriteBlobAsync(string oid, string relative, string mode, string path,
            CancellationToken token)
        {
            var size = await RequestAsync(oid, "blob", token).ConfigureAwait(false);
            using var objectHash = ObjectHash(oid, "blob", size);
            using var contentHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, useAsync: true);
            var buffer = new byte[65536];
            var remaining = size;
            while (remaining != 0)
            {
                var count = (int)Math.Min(buffer.Length, remaining);
                await ReadExactlyAsync(buffer, 0, count, token).ConfigureAwait(false);
                await target.WriteAsync(buffer, 0, count, token).ConfigureAwait(false);
                objectHash.AppendData(buffer, 0, count);
                contentHash.AppendData(buffer, 0, count);
                remaining -= count;
            }
            await ReadTerminatorAsync(token).ConfigureAwait(false);
            Require(Finish(objectHash) == oid, "object-oid-mismatch");
            await target.FlushAsync(token).ConfigureAwait(false);
            target.Flush(flushToDisk: true);
            return new GitSnapshotFile(relative, mode, oid, Finish(contentHash), size);
        }

        private async Task<long> RequestAsync(string oid, string type, CancellationToken token)
        {
            Require(IsOid(oid), "invalid-object-oid");
            var request = Encoding.ASCII.GetBytes(oid + "\n");
            await _input.WriteAsync(request, 0, request.Length, token).ConfigureAwait(false);
            await _input.FlushAsync(token).ConfigureAwait(false);
            var header = new List<byte>();
            var one = new byte[1];
            while (true)
            {
                await ReadExactlyAsync(one, 0, 1, token).ConfigureAwait(false);
                if (one[0] == (byte)'\n') break;
                Require(header.Count < 256 && one[0] >= 32 && one[0] < 127, "invalid-batch-header");
                header.Add(one[0]);
            }
            var parts = Encoding.ASCII.GetString(header.ToArray()).Split(' ');
            var size = 0L;
            Require(parts.Length == 3 && parts[0] == oid && parts[1] == type &&
                long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out size) && size >= 0,
                "missing-or-invalid-object");
            return size;
        }

        private async Task ReadExactlyAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            while (count != 0)
            {
                var read = await _output.ReadAsync(buffer, offset, count, token).ConfigureAwait(false);
                Require(read != 0, "truncated-object");
                offset += read;
                count -= read;
            }
        }

        private async Task ReadTerminatorAsync(CancellationToken token)
        {
            var one = new byte[1];
            await ReadExactlyAsync(one, 0, 1, token).ConfigureAwait(false);
            Require(one[0] == (byte)'\n', "invalid-object-terminator");
        }

        internal async Task CompleteAsync(CancellationToken token)
        {
            _input.Close();
            var one = new byte[1];
            Require(await _output.ReadAsync(one, 0, 1, token).ConfigureAwait(false) == 0, "unexpected-batch-output");
            await Task.WhenAll(_process.WaitForExitAsync(token), _error).WaitAsync(token).ConfigureAwait(false);
            Require(_process.ExitCode == 0, "git-failed");
            _completed = true;
        }

        internal async Task CloseAsync(Exception? primary)
        {
            try
            {
                if (!_completed)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    if (!_process.HasExited) _process.Kill(entireProcessTree: true);
                    await Task.WhenAll(_process.WaitForExitAsync(cleanup.Token), _error).WaitAsync(cleanup.Token).ConfigureAwait(false);
                }
            }
            catch { if (primary == null) throw Rejected("git-cleanup-incomplete"); }
            finally
            {
                try { _output.Dispose(); _input.Dispose(); _process.Dispose(); }
                catch { if (primary == null) throw Rejected("git-release-failed"); }
            }
        }

        private static IncrementalHash ObjectHash(string oid, string type, long size)
        {
            var hash = IncrementalHash.CreateHash(oid.Length == 40 ? HashAlgorithmName.SHA1 : HashAlgorithmName.SHA256);
            hash.AppendData(Encoding.ASCII.GetBytes(type + " " + size.ToString(CultureInfo.InvariantCulture) + "\0"));
            return hash;
        }

        private static string Finish(IncrementalHash hash) => Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
