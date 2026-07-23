using Microsoft.Extensions.Logging;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.Runtime;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace STranslate.Plugin.Tts.FishAudio.Presentation;

internal sealed class PreviewRefreshCoordinator : IDisposable
{
    internal const string PreviewUnavailableKey = "STranslate_Plugin_Tts_FishAudio_Preview_Unavailable";
    internal const string PreviewRefreshFailedKey = "STranslate_Plugin_Tts_FishAudio_Preview_RefreshFailed";

    private const string PreviewApiToken = "dummy";

    private readonly IPluginContext _context;
    private readonly Func<string?> _getCachedVoiceId;
    private readonly Func<string?> _getCachedVoiceSampleUrl;
    private readonly Func<string> _getVoiceId;
    private readonly Func<string> _getSettingsVoiceId;
    private readonly Func<List<VoiceSearchItem>> _getSearchResults;
    private readonly Func<string?> _getPreviewingVoiceId;
    private readonly Action _stopPreview;
    private readonly Action<string, string> _togglePreview;
    private readonly Func<Func<Settings, bool>, bool> _updateSettingsAndSave;
    private readonly Action<CachedVoiceInfo?> _applyCachedVoice;
    private readonly Func<string, string, int, Action<string>?, string> _resolveCoverImageUrl;
    private readonly Func<bool> _isFacadeDisposed;
    private readonly object _displayPreviewOperationGate = new();

    private long _displayPreviewOperationId;
    private bool _isDisposed;
    private CancellationTokenSource? _displayPreviewCancellationTokenSource;
    private PreviewRefreshTarget? _displayPreviewOperationTarget;

    internal PreviewRefreshCoordinator(
        IPluginContext context,
        Func<string?> getCachedVoiceId,
        Func<string?> getCachedVoiceSampleUrl,
        Func<string> getVoiceId,
        Func<string> getSettingsVoiceId,
        Func<List<VoiceSearchItem>> getSearchResults,
        Func<string?> getPreviewingVoiceId,
        Action stopPreview,
        Action<string, string> togglePreview,
        Func<Func<Settings, bool>, bool> updateSettingsAndSave,
        Action<CachedVoiceInfo?> applyCachedVoice,
        Func<string, string, int, Action<string>?, string> resolveCoverImageUrl,
        Func<bool> isFacadeDisposed)
    {
        _context = context;
        _getCachedVoiceId = getCachedVoiceId;
        _getCachedVoiceSampleUrl = getCachedVoiceSampleUrl;
        _getVoiceId = getVoiceId;
        _getSettingsVoiceId = getSettingsVoiceId;
        _getSearchResults = getSearchResults;
        _getPreviewingVoiceId = getPreviewingVoiceId;
        _stopPreview = stopPreview;
        _togglePreview = togglePreview;
        _updateSettingsAndSave = updateSettingsAndSave;
        _applyCachedVoice = applyCachedVoice;
        _resolveCoverImageUrl = resolveCoverImageUrl;
        _isFacadeDisposed = isFacadeDisposed;
    }

    internal async Task ToggleDisplayPreviewAsync()
    {
        var cachedVoiceId = _getCachedVoiceId();
        var cachedVoiceSampleUrl = _getCachedVoiceSampleUrl();
        if (string.IsNullOrEmpty(cachedVoiceId) || string.IsNullOrEmpty(cachedVoiceSampleUrl))
            return;

        var voiceId = cachedVoiceId;
        var oldAudioUrl = cachedVoiceSampleUrl;
        if (_getPreviewingVoiceId() == voiceId)
        {
            CancelDisplayPreviewOperation();
            _stopPreview();
            return;
        }

        if (!PreviewAudioUrlPolicy.RequiresRefresh(oldAudioUrl, FishAudioClock.LocalUtcNow()))
        {
            CancelDisplayPreviewOperation();
            StartDisplayPreview(voiceId, oldAudioUrl);
            return;
        }

        var target = new PreviewRefreshTarget(voiceId, null);
        if (!TryBeginDisplayPreviewOperation(target, out var operationId, out var cts, out var cancellationToken))
            return;

        try
        {
            if (!CanStartPreview("Selected voice preview"))
                return;

            if (!IsCurrentDisplayPreviewOperation(operationId, target))
                return;

            ModelEntity? model;
            try
            {
                model = await FishAudioApi.GetModelAsync(_context, PreviewApiToken, voiceId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (!IsCurrentDisplayPreviewOperation(operationId, target))
                    return;

                _context.Logger?.LogWarning(ex, "Selected voice preview refresh failed for voice {VoiceId}", voiceId);
                ShowPreviewRefreshFailed();
                return;
            }

            if (!IsCurrentDisplayPreviewOperation(operationId, target))
                return;

            if (model is null)
            {
                _context.Logger?.LogWarning("Selected voice preview refresh returned no voice for {VoiceId}", voiceId);
                ShowPreviewRefreshFailed();
                return;
            }

            var cached = VoiceDiscoveryCoordinator.CreateCachedVoiceInfo(model);
            if (!_updateSettingsAndSave(settings =>
                IsCurrentDisplayPreviewOperation(operationId, target)
                && TryApplyRefreshedCachedVoice(settings, voiceId, cached)))
            {
                return;
            }

            if (!IsCurrentDisplayPreviewOperation(operationId, target))
                return;

            _applyCachedVoice(cached);

            if (!PreviewAudioUrlPolicy.TryCreateAllowedUri(cached.SampleAudioUrl, out _))
            {
                _context.Snackbar.ShowError(_context.GetTranslation(PreviewUnavailableKey));
                return;
            }

            if (PreviewAudioUrlPolicy.RequiresRefresh(cached.SampleAudioUrl, FishAudioClock.LocalUtcNow()))
            {
                ShowPreviewRefreshFailed();
                return;
            }

            if (IsCurrentDisplayPreviewOperation(operationId, target))
                StartDisplayPreview(voiceId, cached.SampleAudioUrl!);
        }
        finally
        {
            CompleteDisplayPreviewOperation(cts);
        }
    }

    internal async Task ToggleSearchItemPreviewAsync(VoiceSearchItem? item)
    {
        if (item is null || string.IsNullOrEmpty(item.SampleAudioUrl))
            return;

        if (_getPreviewingVoiceId() == item.Id)
        {
            CancelDisplayPreviewOperation();
            _stopPreview();
            return;
        }

        var oldAudioUrl = item.SampleAudioUrl;
        if (!PreviewAudioUrlPolicy.RequiresRefresh(oldAudioUrl, FishAudioClock.LocalUtcNow()))
        {
            CancelDisplayPreviewOperation();
            if (CanStartPreview("Search result preview"))
                _togglePreview(item.Id, oldAudioUrl);
            return;
        }

        var target = new PreviewRefreshTarget(item.Id, item);
        if (!TryBeginDisplayPreviewOperation(target, out var operationId, out var cts, out var cancellationToken))
            return;

        try
        {
            if (!CanStartPreview("Search result preview"))
                return;

            if (!IsCurrentDisplayPreviewOperation(operationId, target))
                return;

            ModelEntity? model;
            try
            {
                model = await FishAudioApi.GetModelAsync(_context, PreviewApiToken, item.Id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (!IsCurrentDisplayPreviewOperation(operationId, target))
                    return;

                _context.Logger?.LogWarning(ex, "Search result preview refresh failed for voice {VoiceId}", item.Id);
                ShowPreviewRefreshFailed();
                return;
            }

            if (!IsCurrentDisplayPreviewOperation(operationId, target))
                return;

            if (model is null)
            {
                _context.Logger?.LogWarning("Search result preview refresh returned no voice for {VoiceId}", item.Id);
                ShowPreviewRefreshFailed();
                return;
            }

            ApplyRefreshedSearchItem(item, model);

            if (!PreviewAudioUrlPolicy.TryCreateAllowedUri(item.SampleAudioUrl, out _))
            {
                _context.Snackbar.ShowError(_context.GetTranslation(PreviewUnavailableKey));
                return;
            }

            if (PreviewAudioUrlPolicy.RequiresRefresh(item.SampleAudioUrl, FishAudioClock.LocalUtcNow()))
            {
                ShowPreviewRefreshFailed();
                return;
            }

            if (IsCurrentDisplayPreviewOperation(operationId, target)
                && CanStartPreview("Search result preview playback"))
            {
                _togglePreview(item.Id, item.SampleAudioUrl!);
            }
        }
        finally
        {
            CompleteDisplayPreviewOperation(cts);
        }
    }

    internal static bool TryApplyRefreshedCachedVoice(
        Settings settings,
        string expectedVoiceId,
        CachedVoiceInfo cached)
    {
        if (!string.Equals(settings.VoiceId, expectedVoiceId, StringComparison.Ordinal))
            return false;

        settings.CachedVoice = cached;
        return true;
    }

    internal void CancelDisplayPreviewOperation()
    {
        lock (_displayPreviewOperationGate)
        {
            ++_displayPreviewOperationId;
            CancelCurrentDisplayPreviewOperationLocked();
        }
    }

    private void ApplyRefreshedSearchItem(VoiceSearchItem item, ModelEntity model)
    {
        item.Title = model.Title;
        item.Description = model.Description;
        item.AuthorName = model.Author?.Nickname ?? "";
        item.TaskCount = model.TaskCount;
        item.SampleAudioUrl = model.Samples.FirstOrDefault()?.Audio;
        item.CoverImage = model.CoverImage;
        var coverImage = item.CoverImage;
        item.CoverUrl = _resolveCoverImageUrl(item.Id, coverImage, 64, url =>
        {
            if (!_isFacadeDisposed()
                && _getSearchResults().Contains(item)
                && string.Equals(item.CoverImage, coverImage, StringComparison.Ordinal))
            {
                item.CoverUrl = url;
            }
        });
    }

    private bool CanStartPreview(string operation)
    {
        if (FishAudioRequestPolicy.IsNetworkAvailable())
            return true;

        _context.Logger?.LogWarning("{Operation} preflight failed: Network unavailable", operation);
        _context.Snackbar.ShowError(_context.GetTranslation(FishAudioRequestPolicy.NetworkUnavailableKey));
        return false;
    }

    private void ShowPreviewRefreshFailed()
    {
        var key = FishAudioRequestPolicy.IsNetworkAvailable()
            ? PreviewRefreshFailedKey
            : FishAudioRequestPolicy.NetworkUnavailableKey;
        _context.Snackbar.ShowError(_context.GetTranslation(key));
    }

    private void StartDisplayPreview(string voiceId, string audioUrl)
    {
        if (CanStartPreview("Selected voice preview playback"))
            _togglePreview(voiceId, audioUrl);
    }

    private bool TryBeginDisplayPreviewOperation(
        PreviewRefreshTarget target,
        out long operationId,
        out CancellationTokenSource cts,
        out CancellationToken cancellationToken)
    {
        lock (_displayPreviewOperationGate)
        {
            if (_displayPreviewCancellationTokenSource is not null)
            {
                var cancelOnly = _displayPreviewOperationTarget == target;
                operationId = ++_displayPreviewOperationId;
                cts = null!;
                cancellationToken = default;
                CancelCurrentDisplayPreviewOperationLocked();
                if (cancelOnly)
                    return false;
            }

            operationId = ++_displayPreviewOperationId;
            cts = new CancellationTokenSource();
            cancellationToken = cts.Token;
            _displayPreviewCancellationTokenSource = cts;
            _displayPreviewOperationTarget = target;
            return true;
        }
    }

    private bool IsCurrentDisplayPreviewOperation(long operationId, PreviewRefreshTarget target)
    {
        lock (_displayPreviewOperationGate)
        {
            if (_isDisposed
                || _isFacadeDisposed()
                || _displayPreviewOperationId != operationId
                || _displayPreviewOperationTarget != target)
            {
                return false;
            }
        }

        return target.SearchItem is null
            ? string.Equals(_getVoiceId(), target.VoiceId, StringComparison.Ordinal)
                && string.Equals(_getSettingsVoiceId(), target.VoiceId, StringComparison.Ordinal)
            : string.Equals(target.SearchItem.Id, target.VoiceId, StringComparison.Ordinal)
                && _getSearchResults().Contains(target.SearchItem);
    }

    private void CompleteDisplayPreviewOperation(CancellationTokenSource cts)
    {
        lock (_displayPreviewOperationGate)
        {
            if (!ReferenceEquals(_displayPreviewCancellationTokenSource, cts))
                return;

            _displayPreviewCancellationTokenSource = null;
            _displayPreviewOperationTarget = null;
            cts.Dispose();
        }
    }

    private void CancelCurrentDisplayPreviewOperationLocked()
    {
        var cancellation = _displayPreviewCancellationTokenSource;
        _displayPreviewCancellationTokenSource = null;
        _displayPreviewOperationTarget = null;
        if (cancellation is null)
            return;

        try
        {
            cancellation.Cancel();
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_displayPreviewOperationGate)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            ++_displayPreviewOperationId;
            CancelCurrentDisplayPreviewOperationLocked();
        }
    }

    private readonly record struct PreviewRefreshTarget(string VoiceId, VoiceSearchItem? SearchItem);
}
