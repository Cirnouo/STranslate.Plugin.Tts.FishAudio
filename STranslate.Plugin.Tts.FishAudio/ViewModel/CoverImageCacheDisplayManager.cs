using STranslate.Plugin.Tts.FishAudio.Service;
using System.IO;
using System.Threading;

namespace STranslate.Plugin.Tts.FishAudio.ViewModel;

internal sealed class CoverImageCacheDisplayManager
{
    private readonly IPluginContext _context;
    private readonly CoverImageCacheService _cache;
    private readonly Func<Task> _clearAsync;
    private readonly TimeSpan _clearTimeout;
    private readonly Action<Action> _runOnUiThread;

    private long _clearOperationId;

    public CoverImageCacheDisplayManager(
        IPluginContext context,
        Func<string, CancellationToken, Task<Stream>> openStreamAsync,
        Func<Task>? clearAsync,
        TimeSpan? clearTimeout,
        Action<Action> runOnUiThread)
    {
        _context = context;
        _cache = new CoverImageCacheService(context.MetaData?.PluginCacheDirectoryPath, openStreamAsync);
        _clearAsync = clearAsync ?? (() => Task.Run(_cache.Clear));
        _clearTimeout = clearTimeout ?? TimeSpan.FromSeconds(10);
        _runOnUiThread = runOnUiThread;
    }

    public string GetFormattedCacheSize() => _cache.GetFormattedCacheSize();

    public string ResolveCoverImageUrl(
        string voiceId,
        string coverImage,
        int displayWidth,
        Action<string>? onCacheReady,
        Action refreshCacheSize)
    {
        var result = _cache.ResolveCoverImageUrl(voiceId, coverImage, displayWidth, localUrl =>
        {
            _runOnUiThread(() =>
            {
                onCacheReady?.Invoke(localUrl);
                refreshCacheSize();
            });
        });
        return result.DisplayUrl;
    }

    public async Task ClearAsync(Action<bool> setClearing, Action refreshDisplay, Action refreshCacheSize)
    {
        var operationId = Interlocked.Increment(ref _clearOperationId);
        setClearing(true);

        Task clearTask;
        try
        {
            clearTask = _clearAsync();
        }
        catch (Exception ex)
        {
            CompleteCurrentClear(operationId, setClearing, refreshDisplay);
            _context.Snackbar.ShowError(ex.Message);
            return;
        }

        var completedTask = await Task.WhenAny(clearTask, Task.Delay(_clearTimeout));
        if (!ReferenceEquals(completedTask, clearTask))
        {
            if (IsCurrentClear(operationId))
            {
                refreshCacheSize();
                setClearing(false);
                _context.Snackbar.ShowError(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_ClearCache_Timeout"));
            }

            _ = clearTask.ContinueWith(task =>
            {
                _ = task.Exception;
                _runOnUiThread(refreshCacheSize);
            }, TaskScheduler.Default);
            return;
        }

        try
        {
            await clearTask;
        }
        catch (Exception ex)
        {
            if (IsCurrentClear(operationId))
                _context.Snackbar.ShowError(ex.Message);
        }
        finally
        {
            if (IsCurrentClear(operationId))
                CompleteCurrentClear(operationId, setClearing, refreshDisplay);
            else
                refreshCacheSize();
        }
    }

    private bool IsCurrentClear(long operationId) =>
        Volatile.Read(ref _clearOperationId) == operationId;

    private void CompleteCurrentClear(long operationId, Action<bool> setClearing, Action refreshDisplay)
    {
        refreshDisplay();
        if (IsCurrentClear(operationId))
            setClearing(false);
    }
}
