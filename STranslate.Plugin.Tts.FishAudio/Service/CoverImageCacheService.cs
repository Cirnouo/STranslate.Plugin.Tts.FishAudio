using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace STranslate.Plugin.Tts.FishAudio.Service;

internal sealed class CoverImageCacheService
{
    internal const string DirectoryName = "cover_images";

    private const int CacheDownloadWidth = 128;

    private static readonly string[] SizeUnits = ["B", "KB", "MB", "GB", "TB", "PB"];

    private readonly string? _cacheDirectory;
    private readonly Func<string, CancellationToken, Task<byte[]>> _downloadBytesAsync;
    private readonly object _gate = new();
    private readonly HashSet<string> _cachedVoiceIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PendingDownload> _pendingDownloads = new(StringComparer.OrdinalIgnoreCase);

    private long _cachedSizeBytes;
    private long _generation;

    public CoverImageCacheService(string? pluginCacheDirectoryPath, Func<string, CancellationToken, Task<byte[]>> downloadBytesAsync)
    {
        _downloadBytesAsync = downloadBytesAsync ?? throw new ArgumentNullException(nameof(downloadBytesAsync));

        if (string.IsNullOrWhiteSpace(pluginCacheDirectoryPath))
            return;

        _cacheDirectory = Path.Combine(pluginCacheDirectoryPath, DirectoryName);
        if (Directory.Exists(_cacheDirectory))
            RebuildIndexLocked();
    }

    public CoverImageCacheResult ResolveCoverImageUrl(string voiceId, string coverImage, int displayWidth, Action<string>? onCacheReady = null)
    {
        var remoteUrl = FishAudioApi.BuildCoverUrl(coverImage, displayWidth);
        if (!IsEnabled || string.IsNullOrWhiteSpace(voiceId) || !Settings.IsValidVoiceIdFormat(voiceId) || string.IsNullOrWhiteSpace(coverImage))
            return new CoverImageCacheResult(remoteUrl, null);

        var localPath = GetCacheFilePath(voiceId);
        if (TryGetExistingLocalUrl(voiceId, localPath, out var localUrl))
            return new CoverImageCacheResult(localUrl, null);

        var downloadTask = QueueDownloadAsync(voiceId, coverImage, onCacheReady);
        return new CoverImageCacheResult(remoteUrl, downloadTask);
    }

    public long GetCacheSizeBytes()
    {
        lock (_gate)
            return _cachedSizeBytes;
    }

    public string GetFormattedCacheSize() => FormatBytes(GetCacheSizeBytes());

    public void Clear()
    {
        if (!IsEnabled)
            return;

        lock (_gate)
        {
            _generation++;
            _cachedVoiceIds.Clear();
            _pendingDownloads.Clear();
            _cachedSizeBytes = 0;
        }

        if (!Directory.Exists(_cacheDirectory))
            return;

        foreach (var file in Directory.GetFiles(_cacheDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            TryDeleteFile(file);
        }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";

        double value = bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < SizeUnits.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {SizeUnits[unitIndex]}";
    }

    private bool IsEnabled => _cacheDirectory is not null;

    private string GetCacheFilePath(string voiceId) => Path.Combine(_cacheDirectory!, $"{voiceId}.jpg");

    private static string ToLocalFileUrl(string path) => new Uri(path).AbsoluteUri;

    private bool TryGetExistingLocalUrl(string voiceId, string localPath, out string localUrl)
    {
        lock (_gate)
        {
            if (_cachedVoiceIds.Contains(voiceId))
            {
                if (File.Exists(localPath))
                {
                    localUrl = ToLocalFileUrl(localPath);
                    return true;
                }

                RebuildIndexLocked();
                if (_cachedVoiceIds.Contains(voiceId) && File.Exists(localPath))
                {
                    localUrl = ToLocalFileUrl(localPath);
                    return true;
                }
            }
        }

        if (File.Exists(localPath))
        {
            lock (_gate)
            {
                if (_cachedVoiceIds.Add(voiceId))
                    _cachedSizeBytes += new FileInfo(localPath).Length;
            }

            localUrl = ToLocalFileUrl(localPath);
            return true;
        }

        localUrl = "";
        return false;
    }

    private Task QueueDownloadAsync(string voiceId, string coverImage, Action<string>? onCacheReady)
    {
        lock (_gate)
        {
            if (_pendingDownloads.TryGetValue(voiceId, out var pending))
            {
                if (onCacheReady is not null)
                    pending.Callbacks.Add(onCacheReady);
                return pending.Task!;
            }

            pending = new PendingDownload(_generation);
            if (onCacheReady is not null)
                pending.Callbacks.Add(onCacheReady);

            pending.Task = Task.Run(() => DownloadAndStoreAsync(voiceId, coverImage, pending));
            _pendingDownloads[voiceId] = pending;
            return pending.Task;
        }
    }

    private async Task DownloadAndStoreAsync(string voiceId, string coverImage, PendingDownload pending)
    {
        if (!IsEnabled || _cacheDirectory is null)
            return;

        var downloadUrl = FishAudioApi.BuildCoverUrl(coverImage, CacheDownloadWidth);
        var localPath = GetCacheFilePath(voiceId);
        var tempPath = $"{localPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            var bytes = await _downloadBytesAsync(downloadUrl, CancellationToken.None).ConfigureAwait(false);
            if (bytes.Length == 0)
                return;

            lock (_gate)
            {
                if (pending.Generation != _generation)
                    return;
            }

            Directory.CreateDirectory(_cacheDirectory);
            await File.WriteAllBytesAsync(tempPath, bytes, CancellationToken.None).ConfigureAwait(false);

            lock (_gate)
            {
                if (pending.Generation != _generation)
                {
                    TryDeleteFile(tempPath);
                    return;
                }
            }

            File.Move(tempPath, localPath, true);

            var fileLength = new FileInfo(localPath).Length;
            lock (_gate)
            {
                if (pending.Generation != _generation)
                {
                    TryDeleteFile(localPath);
                    return;
                }

                if (_cachedVoiceIds.Add(voiceId))
                    _cachedSizeBytes += fileLength;
                else
                    _cachedSizeBytes = RecalculateCacheSizeLocked();
            }

            lock (_gate)
            {
                if (pending.Generation != _generation)
                    return;
            }

            InvokeCallbacks(pending, ToLocalFileUrl(localPath));
        }
        catch
        {
            TryDeleteFile(tempPath);
        }
        finally
        {
            lock (_gate)
            {
                if (_pendingDownloads.TryGetValue(voiceId, out var current) && ReferenceEquals(current, pending))
                    _pendingDownloads.Remove(voiceId);
            }
        }
    }

    private void InvokeCallbacks(PendingDownload pending, string localUrl)
    {
        foreach (var callback in pending.Callbacks)
        {
            try
            {
                callback(localUrl);
            }
            catch
            {
                // Cache callbacks update transient UI only; never fail the cache write.
            }
        }
    }

    private void RebuildIndexLocked()
    {
        if (_cacheDirectory is null || !Directory.Exists(_cacheDirectory))
        {
            _cachedVoiceIds.Clear();
            _cachedSizeBytes = 0;
            return;
        }

        _cachedVoiceIds.Clear();
        _cachedSizeBytes = 0;

        foreach (var file in Directory.EnumerateFiles(_cacheDirectory, "*.jpg", SearchOption.TopDirectoryOnly))
        {
            var voiceId = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(voiceId))
                continue;

            if (_cachedVoiceIds.Add(voiceId))
                _cachedSizeBytes += new FileInfo(file).Length;
        }
    }

    private long RecalculateCacheSizeLocked()
    {
        if (_cacheDirectory is null || !Directory.Exists(_cacheDirectory))
            return 0;

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(_cacheDirectory, "*.jpg", SearchOption.TopDirectoryOnly))
        {
            total += new FileInfo(file).Length;
        }

        return total;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private sealed class PendingDownload
    {
        public PendingDownload(long generation) => Generation = generation;

        public long Generation { get; }
        public Task? Task { get; set; }
        public List<Action<string>> Callbacks { get; } = new();
    }
}

internal sealed record CoverImageCacheResult(string DisplayUrl, Task? CacheTask);
