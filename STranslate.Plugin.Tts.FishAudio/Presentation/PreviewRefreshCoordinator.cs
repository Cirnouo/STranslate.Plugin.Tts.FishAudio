using Microsoft.Extensions.Logging;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Caching;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.Runtime;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using System.IO;
using System.Net;
using System.Net.Http;

namespace STranslate.Plugin.Tts.FishAudio.Presentation;

internal sealed class PreviewRefreshCoordinator : IDisposable
{
    internal const string PreviewUnavailableKey = "STranslate_Plugin_Tts_FishAudio_Preview_Unavailable";
    internal const string PreviewRefreshFailedKey = "STranslate_Plugin_Tts_FishAudio_Preview_RefreshFailed";
    private const string PreviewPlaybackFailedKey = "STranslate_Plugin_Tts_FishAudio_Preview_PlaybackFailed";
    private readonly Func<string> _getApiKey;
    private readonly IPluginContext _context;
    private readonly Func<string?> _getCachedVoiceId;
    private readonly Func<string?> _getCachedVoiceSampleUrl;
    private readonly Func<string> _getVoiceId;
    private readonly Func<string> _getSettingsVoiceId;
    private readonly Func<List<VoiceSearchItem>> _getSearchResults;
    private readonly Func<string?> _getPreviewingVoiceId;
    private readonly Action _stopPreview;
    private readonly Action<string, MemoryStream> _togglePreview;
    private readonly Func<Func<Settings, bool>, bool> _updateSettingsAndSave;
    private readonly Action<CachedVoiceInfo?> _applyCachedVoice;
    private readonly Func<string, string, int, Action<string>?, string> _resolveCoverImageUrl;
    private readonly Func<bool> _isFacadeDisposed;
    private readonly Action<string?> _applyPreparingState;
    private readonly Action<Action> _runOnUiThread;
    private readonly PreviewAudioDownloader _downloader;
    private readonly TimeProvider _timeProvider;
    private readonly Action<VoiceSearchItem> _cardRefreshed;
    private readonly VoiceMetadataCache _metadataCache;
    private readonly object _displayPreviewOperationGate = new();
    private long _displayPreviewOperationId;
    private bool _isDisposed;
    private CancellationTokenSource? _displayPreviewCancellationTokenSource;
    private PreviewRefreshTarget? _displayPreviewOperationTarget;
    private PreviewRefreshTarget? _playbackTarget;

    internal PreviewRefreshCoordinator(IPluginContext context, Func<string?> getCachedVoiceId,
        Func<string?> getCachedVoiceSampleUrl, Func<string> getVoiceId, Func<string> getSettingsVoiceId,
        Func<List<VoiceSearchItem>> getSearchResults, Func<string?> getPreviewingVoiceId,
        Action stopPreview, Action<string, MemoryStream> togglePreview,
        Func<Func<Settings, bool>, bool> updateSettingsAndSave, Action<CachedVoiceInfo?> applyCachedVoice,
        Func<string, string, int, Action<string>?, string> resolveCoverImageUrl,
        Func<bool> isFacadeDisposed, TimeProvider? timeProvider = null,
        Action<string?>? applyPreparingState = null, Action<Action>? runOnUiThread = null,
        Action<VoiceSearchItem>? cardRefreshed = null, VoiceMetadataCache? metadataCache = null, Func<string>? getApiKey = null)
    {
        _context = context;
        _getApiKey = getApiKey ?? (() => "");
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
        _applyPreparingState = applyPreparingState ?? (_ => { });
        _runOnUiThread = runOnUiThread ?? (action => action());
        _downloader = new PreviewAudioDownloader(context.HttpService);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _cardRefreshed = cardRefreshed ?? (_ => { });
        _metadataCache = metadataCache ?? new VoiceMetadataCache();
    }

    internal Task ToggleDisplayPreviewAsync()
    {
        var id = _getCachedVoiceId();
        var url = _getCachedVoiceSampleUrl();
        return string.IsNullOrEmpty(id) || string.IsNullOrEmpty(url)
            ? Task.CompletedTask : ToggleAsync(new(id, null), url);
    }

    internal Task ToggleSearchItemPreviewAsync(VoiceSearchItem? item) =>
        item is null || string.IsNullOrEmpty(item.SampleAudioUrl)
            ? Task.CompletedTask : ToggleAsync(new(item.Id, item), item.SampleAudioUrl);

    private async Task ToggleAsync(PreviewRefreshTarget target, string? audioUrl)
    {
        if (_getPreviewingVoiceId() == target.VoiceId)
        {
            CancelDisplayPreviewOperation();
            _playbackTarget = null;
            _stopPreview();
            return;
        }
        if (!TryBeginDisplayPreviewOperation(target, out var operationId, out var cts, out var token))
        {
            PublishPreparingState();
            _stopPreview();
            return;
        }
        _playbackTarget = null;
        _stopPreview();
        PublishPreparingState();
        MemoryStream? audio = null;
        try
        {
            if (!CanStartPreview(TargetName(target)) || !IsCurrentDisplayPreviewOperation(operationId, target)) return;
            if (!PreviewAudioUrlPolicy.TryCreateAllowedUri(audioUrl, out _))
            {
                _context.Logger?.LogWarning("Rejected preview audio URL for voice {VoiceId}: host or scheme is not allowed", target.VoiceId);
                return;
            }
            var refreshed = false;
            if (PreviewAudioUrlPolicy.RequiresRefresh(audioUrl, FishAudioClock.LocalUtcNow()))
            {
                refreshed = true;
                audioUrl = await RefreshAsync(operationId, target, token);
                if (audioUrl is null) return;
            }
            while (true)
            {
                if (!CanStartPreview(TargetName(target)) || !IsCurrentDisplayPreviewOperation(operationId, target)) return;
                if (!PreviewAudioUrlPolicy.TryCreateAllowedUri(audioUrl, out var uri))
                {
                    ShowError(PreviewUnavailableKey);
                    return;
                }
                try { audio = await _downloader.DownloadAsync(uri!, token); break; }
                catch (HttpRequestException exception) when (!refreshed && exception.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                {
                    if (!IsCurrentDisplayPreviewOperation(operationId, target)) return;
                    refreshed = true;
                    audioUrl = await RefreshAsync(operationId, target, token);
                    if (audioUrl is null) return;
                }
            }
            if (!IsCurrentDisplayPreviewOperation(operationId, target) || !CanStartPreview(TargetName(target))) return;
            _togglePreview(target.VoiceId, audio);
            audio = null; // The controller owns the buffer even if local playback fails.
            if (!IsCurrentDisplayPreviewOperation(operationId, target)) { _stopPreview(); return; }
            if (_getPreviewingVoiceId() == target.VoiceId) _playbackTarget = target;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Explicit cancellation invalidates the identity; a still-current timeout is a failure.
            if (IsCurrentIdentity(operationId, target)) ShowError(PreviewPlaybackFailedKey);
        }
        catch (Exception exception)
        {
            if (!IsCurrentDisplayPreviewOperation(operationId, target)) return;
            _context.Logger?.LogWarning("Preview download failed for voice {VoiceId} ({ErrorType})", target.VoiceId, exception.GetType().Name);
            ShowError(PreviewPlaybackFailedKey);
        }
        finally { audio?.Dispose(); CompleteDisplayPreviewOperation(cts); }
    }

    private async Task<string?> RefreshAsync(long operationId, PreviewRefreshTarget target, CancellationToken token)
    {
        try
        {
            var apiKey = _getApiKey();
            var credentialScope = VoiceMetadataCache.CredentialScope(apiKey);
            var model = await FishAudioApi.GetModelAsync(_context, FishAudioApi.MetadataApiKey(apiKey), target.VoiceId, token);
            if (!IsCurrentDisplayPreviewOperation(operationId, target)) return null;
            if (model is null)
            {
                _context.Logger?.LogWarning("{Operation} refresh returned no voice for {VoiceId}", TargetName(target), target.VoiceId);
                ShowError(PreviewRefreshFailedKey);
                return null;
            }
            // URL expiry/403/404 recovery deliberately bypasses the metadata TTL cache.
            _metadataCache.Put(VoiceMetadata.FromModel(model, target.VoiceId), credentialScope);
            if (!IsCurrentDisplayPreviewOperation(operationId, target)
                || !string.Equals(_getApiKey(), apiKey, StringComparison.Ordinal)) return null;
            string? url;
            if (target.SearchItem is { } item)
            {
                ApplyRefreshedSearchItem(item, model);
                _cardRefreshed(item);
                url = item.SampleAudioUrl;
            }
            else
            {
                var cached = VoiceDiscoveryCoordinator.CreateCachedVoiceInfo(model);
                if (!_updateSettingsAndSave(settings => IsCurrentDisplayPreviewOperation(operationId, target)
                    && string.Equals(settings.ApiKey, apiKey, StringComparison.Ordinal)
                    && TryApplyRefreshedCachedVoice(settings, target.VoiceId, cached))) return null;
                if (!IsCurrentDisplayPreviewOperation(operationId, target)) return null;
                _applyCachedVoice(cached);
                url = cached.SampleAudioUrl;
            }
            if (!PreviewAudioUrlPolicy.TryCreateAllowedUri(url, out _)) { ShowError(PreviewUnavailableKey); return null; }
            if (PreviewAudioUrlPolicy.RequiresRefresh(url, FishAudioClock.LocalUtcNow())) { ShowError(PreviewRefreshFailedKey); return null; }
            return url;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (IsCurrentIdentity(operationId, target)) ShowError(PreviewRefreshFailedKey);
            return null;
        }
        catch (Exception exception)
        {
            if (!IsCurrentDisplayPreviewOperation(operationId, target)) return null;
            _context.Logger?.LogWarning("{Operation} refresh failed for voice {VoiceId} ({ErrorType})", TargetName(target), target.VoiceId, exception.GetType().Name);
            ShowError(PreviewRefreshFailedKey);
            return null;
        }
    }

    private static string TargetName(PreviewRefreshTarget target) => target.SearchItem is null ? "Selected voice preview" : "Search result preview";
    private bool CanStartPreview(string operation)
    {
        if (FishAudioRequestPolicy.IsNetworkAvailable()) return true;
        _context.Logger?.LogWarning("{Operation} preflight failed: Network unavailable", operation);
        ShowError(FishAudioRequestPolicy.NetworkUnavailableKey);
        return false;
    }
    private void ShowError(string key) => _context.Snackbar.ShowError(_context.GetTranslation(
        FishAudioRequestPolicy.IsNetworkAvailable() ? key : FishAudioRequestPolicy.NetworkUnavailableKey));

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
        PublishPreparingState();
    }

    private void ApplyRefreshedSearchItem(VoiceSearchItem item, ModelEntity model)
    {
        item.Title = model.Title;
        item.Description = model.Description;
        item.AuthorName = model.Author?.Nickname ?? "";
        item.TaskCount = model.TaskCount;
        item.Languages = model.Languages;
        item.CreatedAt = model.CreatedAt;
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

    internal void OnSearchResultsChanged()
    {
        var stop = false;
        lock (_displayPreviewOperationGate)
        {
            if (_displayPreviewOperationTarget is { SearchItem: { } pending } && !_getSearchResults().Contains(pending))
            {
                ++_displayPreviewOperationId;
                CancelCurrentDisplayPreviewOperationLocked();
                stop = true;
            }
            if (_playbackTarget is { SearchItem: { } playing } && !_getSearchResults().Contains(playing))
            {
                _playbackTarget = null;
                stop = true;
            }
        }
        if (stop)
        {
            PublishPreparingState();
            _stopPreview();
        }
    }

    private bool TryBeginDisplayPreviewOperation(PreviewRefreshTarget target, out long operationId,
        out CancellationTokenSource cts, out CancellationToken cancellationToken)
    {
        lock (_displayPreviewOperationGate)
        {
            operationId = ++_displayPreviewOperationId;
            var cancelOnly = _displayPreviewCancellationTokenSource is not null
                && _displayPreviewOperationTarget?.VoiceId == target.VoiceId;
            CancelCurrentDisplayPreviewOperationLocked();
            if (_isDisposed || _isFacadeDisposed() || cancelOnly)
            {
                cts = null!;
                cancellationToken = default;
                return false;
            }
            cts = new CancellationTokenSource(PreviewAudioDownloader.Timeout, _timeProvider);
            cancellationToken = cts.Token;
            _displayPreviewCancellationTokenSource = cts;
            _displayPreviewOperationTarget = target;
            return true;
        }
    }

    private bool IsCurrentDisplayPreviewOperation(long operationId, PreviewRefreshTarget target)
    {
        lock (_displayPreviewOperationGate)
            return IsCurrentIdentity(operationId, target) && _displayPreviewCancellationTokenSource?.IsCancellationRequested == false;
    }

    private bool IsCurrentIdentity(long operationId, PreviewRefreshTarget target)
    {
        lock (_displayPreviewOperationGate)
        {
            if (_isDisposed || _isFacadeDisposed() || _displayPreviewOperationId != operationId || _displayPreviewOperationTarget != target) return false;
        }
        return target.SearchItem is null
            ? string.Equals(_getVoiceId(), target.VoiceId, StringComparison.Ordinal) && string.Equals(_getSettingsVoiceId(), target.VoiceId, StringComparison.Ordinal)
            : string.Equals(target.SearchItem.Id, target.VoiceId, StringComparison.Ordinal) && _getSearchResults().Contains(target.SearchItem);
    }

    private void CompleteDisplayPreviewOperation(CancellationTokenSource cts)
    {
        lock (_displayPreviewOperationGate)
        {
            if (!ReferenceEquals(_displayPreviewCancellationTokenSource, cts)) return;
            _displayPreviewCancellationTokenSource = null;
            _displayPreviewOperationTarget = null;
            cts.Dispose();
        }
        PublishPreparingState();
    }

    private void PublishPreparingState() => _runOnUiThread(() =>
    {
        string? voiceId;
        lock (_displayPreviewOperationGate)
            voiceId = _displayPreviewOperationTarget?.VoiceId;
        // Read the current owner when the UI callback runs, so a queued old completion cannot clear a new request.
        _applyPreparingState(voiceId);
    });
    private void CancelCurrentDisplayPreviewOperationLocked()
    {
        var cancellation = _displayPreviewCancellationTokenSource;
        _displayPreviewCancellationTokenSource = null;
        _displayPreviewOperationTarget = null;
        if (cancellation is null) return;
        try { cancellation.Cancel(); }
        finally { cancellation.Dispose(); }
    }
    public void Dispose()
    {
        lock (_displayPreviewOperationGate)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            ++_displayPreviewOperationId;
            CancelCurrentDisplayPreviewOperationLocked();
        }
        PublishPreparingState();
        _playbackTarget = null;
        _stopPreview();
    }
    private readonly record struct PreviewRefreshTarget(string VoiceId, VoiceSearchItem? SearchItem);
}
