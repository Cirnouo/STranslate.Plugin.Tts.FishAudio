using System.IO;
using System.Text.Json;

namespace STranslate.Plugin.Tts.FishAudio.Configuration;

internal sealed class FavoriteVoiceStore : IDisposable
{
    internal const string FileName = "favorite-voices.json";
    internal const int MaximumCount = 300;
    internal const int MaximumFileBytes = 32 * 1024;
    private static readonly object RegistryGate = new();
    private static readonly Dictionary<string, PathGate> PathGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _instanceGate = new();
    private readonly string? _filePath;
    private readonly PathGate? _pathGate;
    private readonly Action<string, string> _commitFile;
    private bool _disposed;

    internal FavoriteVoiceStore(string? pluginSettingsDirectoryPath, Action<string, string>? commitFile = null)
    {
        _commitFile = commitFile ?? CommitFile;
        try
        {
            if (string.IsNullOrWhiteSpace(pluginSettingsDirectoryPath) || !Path.IsPathFullyQualified(pluginSettingsDirectoryPath))
                return;
            _filePath = Path.Combine(Path.GetFullPath(pluginSettingsDirectoryPath), FileName);
            lock (RegistryGate)
            {
                if (!PathGates.TryGetValue(_filePath, out _pathGate))
                    PathGates.Add(_filePath, _pathGate = new PathGate());
                _pathGate.Owners++;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            _filePath = null;
        }
    }

    internal IReadOnlyList<string> Read()
    {
        lock (_instanceGate)
        {
            EnsureAvailable();
            lock (_pathGate!.Gate)
                return ReadCore();
        }
    }

    // Lock order: caller's persistence authorization gate -> instance -> path gate.
    // Re-read under the shared path gate so separate profiles never overwrite each other's IDs.
    // No notifications or UI callbacks occur inside these persistence gates.
    internal IReadOnlyList<string> SetFavorite(string voiceId, bool favorite)
    {
        if (!SettingsValidation.IsValidVoiceIdFormat(voiceId))
            throw new ArgumentException("A canonical Voice ID is required.", nameof(voiceId));
        lock (_instanceGate)
        {
            EnsureAvailable();
            lock (_pathGate!.Gate)
            {
                var current = ReadCore().ToList();
                if (current.Contains(voiceId, StringComparer.Ordinal) == favorite)
                    return current;
                if (favorite)
                {
                    if (current.Count >= MaximumCount)
                        throw new FavoriteVoiceLimitException();
                    current.Add(voiceId);
                }
                else
                {
                    current.Remove(voiceId);
                }
                WriteCore(current);
                return current;
            }
        }
    }

    private IReadOnlyList<string> ReadCore()
    {
        if (!File.Exists(_filePath))
        {
            // File.Exists also returns false on some access failures. Probe the directory
            // through an actual open when it exists rather than treating denial as empty.
            try
            {
                using var probe = File.Open(_filePath!, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            }
            catch (FileNotFoundException) { return []; }
            catch (DirectoryNotFoundException) { return []; }
        }
        using var stream = File.Open(_filePath!, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using var buffer = new MemoryStream();
        var bytes = new byte[4096];
        while (buffer.Length <= MaximumFileBytes)
        {
            var read = stream.Read(bytes, 0, (int)Math.Min(bytes.Length, MaximumFileBytes + 1 - buffer.Length));
            if (read == 0)
                break;
            buffer.Write(bytes, 0, read);
        }
        if (buffer.Length > MaximumFileBytes)
            throw new InvalidDataException("The favorite IDs file exceeds its size limit.");
        using var document = JsonDocument.Parse(buffer.ToArray());
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Favorite IDs must be a JSON array.");
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Favorite IDs must contain strings only.");
            var id = element.GetString()!;
            if (!SettingsValidation.IsValidVoiceIdFormat(id) || !seen.Add(id))
                continue;
            if (ids.Count == MaximumCount)
                throw new InvalidDataException("The favorite IDs file exceeds its item limit.");
            ids.Add(id);
        }
        return ids;
    }

    private void WriteCore(List<string> ids)
    {
        var directory = Path.GetDirectoryName(_filePath!)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(JsonSerializer.SerializeToUtf8Bytes(ids));
                stream.Flush(flushToDisk: true);
            }
            _commitFile(temporaryPath, _filePath!);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void CommitFile(string temporaryPath, string destination)
    {
        if (File.Exists(destination))
            File.Replace(temporaryPath, destination, null);
        else
            File.Move(temporaryPath, destination);
    }

    private void EnsureAvailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_filePath is null || _pathGate is null)
            throw new InvalidOperationException("The host plugin data directory is unavailable.");
    }

    public void Dispose()
    {
        lock (_instanceGate)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_pathGate is not null)
            {
                lock (RegistryGate)
                {
                    if (--_pathGate.Owners == 0)
                        PathGates.Remove(_filePath!);
                }
            }
        }
    }

    private sealed class PathGate
    {
        internal object Gate { get; } = new();
        internal int Owners { get; set; }
    }
}

internal sealed class FavoriteVoiceLimitException : InvalidOperationException { }
