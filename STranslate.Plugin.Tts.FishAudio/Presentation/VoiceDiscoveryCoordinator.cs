using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Caching;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;

namespace STranslate.Plugin.Tts.FishAudio.Presentation;

internal sealed class VoiceDiscoveryCoordinator : IDisposable
{
    private readonly Func<string> _getApiKey;
    private const int SearchPageSize = 6;

    private readonly IPluginContext _context;
    private readonly VoiceMetadataCache _metadataCache;
    private readonly Func<string> _getSearchQuery;
    private readonly Func<string> _getPageInput;
    private readonly Func<string> _getVoiceIdInput;
    private readonly Action<bool> _setSearching;
    private readonly Action<VoiceSearchPagination> _setSearchPagination;
    private readonly Action<string> _setPageInput;
    private readonly Action<bool> _setHasSearched;
    private readonly Action<List<VoiceSearchItem>> _setSearchResults;
    private readonly Func<string, string, int, Action<string>?, string> _resolveCoverImageUrl;
    private readonly Action _syncPreviewStateToResults;
    private readonly Action<string> _setVoiceIdWithoutSaving;
    private readonly Func<string, CachedVoiceInfo, bool> _persistSelectedVoice;
    private readonly Action<VoiceSearchItem?> _setByIdResult;
    private readonly Action<CachedVoiceInfo?> _applyCachedVoice;
    private readonly Action<string?> _setVoiceIdError;
    private readonly object _voiceIdErrorGate = new();
    private readonly TimeProvider _timeProvider;
    private readonly Action<Action> _runOnUiThread;
    private readonly Action<bool> _setSubmittingVoiceId;
    private readonly Func<bool> _isFacadeDisposed;
    private long _searchOperationId;
    private long _voiceIdOperationId;
    private bool _isDisposed;
    private CancellationTokenSource? _searchCancellationTokenSource;
    private CancellationTokenSource? _voiceIdCancellationTokenSource;
    private ITimer? _voiceIdErrorTimer;
    private long _voiceIdErrorVersion;
    private VoiceSearchPagination _pagination = VoiceSearchPagination.Initial;
    private VoiceSearchCriteria _committedSearchCriteria = new("");
    private VoiceSearchOptions _appliedSearchOptions = new();
    private readonly Action<VoiceSearchOptions> _setAppliedSearchOptions;
    private int _highestVisitedPage = 1;

    internal VoiceDiscoveryCoordinator(
        IPluginContext context,
        Func<string> getSearchQuery,
        Func<string> getPageInput,
        Func<string> getVoiceIdInput,
        Action<bool> setSearching,
        Action<VoiceSearchPagination> setSearchPagination,
        Action<string> setPageInput,
        Action<bool> setHasSearched,
        Action<List<VoiceSearchItem>> setSearchResults,
        Func<string, string, int, Action<string>?, string> resolveCoverImageUrl,
        Action syncPreviewStateToResults,
        Action<string> setVoiceIdWithoutSaving,
        Func<string, CachedVoiceInfo, bool> persistSelectedVoice,
        Action<CachedVoiceInfo?> applyCachedVoice,
        Action<string?> setVoiceIdError,
        Action<bool> setSubmittingVoiceId,
        Func<bool> isFacadeDisposed,
        Action<VoiceSearchItem?>? setByIdResult = null,
        VoiceMetadataCache? metadataCache = null, Func<string>? getApiKey = null,
        TimeProvider? timeProvider = null, Action<Action>? runOnUiThread = null,
        Action<VoiceSearchOptions>? setAppliedSearchOptions = null)
    {
        _context = context;
        _getApiKey = getApiKey ?? (() => "");
        _metadataCache = metadataCache ?? new VoiceMetadataCache();
        _getSearchQuery = getSearchQuery;
        _getPageInput = getPageInput;
        _getVoiceIdInput = getVoiceIdInput;
        _setSearching = setSearching;
        _setSearchPagination = setSearchPagination;
        _setPageInput = setPageInput;
        _setHasSearched = setHasSearched;
        _setSearchResults = setSearchResults;
        _resolveCoverImageUrl = resolveCoverImageUrl;
        _syncPreviewStateToResults = syncPreviewStateToResults;
        _setVoiceIdWithoutSaving = setVoiceIdWithoutSaving;
        _persistSelectedVoice = persistSelectedVoice;
        _setByIdResult = setByIdResult ?? (_ => { });
        _applyCachedVoice = applyCachedVoice;
        _setVoiceIdError = setVoiceIdError;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _runOnUiThread = runOnUiThread ?? (action => action());
        _setAppliedSearchOptions = setAppliedSearchOptions ?? (_ => { });
        _setSubmittingVoiceId = setSubmittingVoiceId;
        _isFacadeDisposed = isFacadeDisposed;
    }

    internal Task SearchVoicesAsync() => ExecuteSearchAsync(1, new VoiceSearchCriteria(_getSearchQuery(), _appliedSearchOptions), resetOnEmptyResponse: true);

    internal Task ApplySearchOptionsAsync(IEnumerable<string>? languages, string sort)
    {
        if (_isDisposed || _isFacadeDisposed()) return Task.CompletedTask;
        // The owning UI action accepts a fully validated, detached menu snapshot
        // before the request starts. Responses never own this session intent.
        var options = new VoiceSearchOptions(languages, VoiceSearchCriteria.ParseSort(sort));
        var criteria = new VoiceSearchCriteria(_getSearchQuery(), options);
        _appliedSearchOptions = options;
        _setAppliedSearchOptions(options);
        return ExecuteSearchAsync(1, criteria, resetOnEmptyResponse: true);
    }

    internal int PageInputMaximum => _pagination.InputMaximum(_highestVisitedPage);

    internal async Task NextPageAsync()
    {
        if (_pagination.HasNextPage)
            await ExecuteSearchAsync(_pagination.Page + 1, _committedSearchCriteria);
    }

    internal async Task PrevPageAsync()
    {
        if (_pagination.Page > 1)
            await ExecuteSearchAsync(_pagination.Page - 1, _committedSearchCriteria);
    }

    internal async Task CommitPageInputAsync()
    {
        if (PageInputDraft.TryGetPage(_getPageInput(), out var requestedPage)
            && _pagination.CanRequestPage(requestedPage, _highestVisitedPage)
            && requestedPage != _pagination.Page)
        {
            _setPageInput(requestedPage.ToString());
            await ExecuteSearchAsync(requestedPage, _committedSearchCriteria);
        }
        else
        {
            _setPageInput(_pagination.Page.ToString());
        }
    }

    internal void SelectVoice(VoiceSearchItem? item)
    {
        if (item is null || !item.HasMetadata || !SettingsValidation.IsValidVoiceIdFormat(item.Id) || _isDisposed || _isFacadeDisposed())
            return;

        var cached = new CachedVoiceInfo
        {
            Title = item.Title,
            Description = item.Description,
            CoverImage = item.CoverImage,
            AuthorName = item.AuthorName,
            TaskCount = item.TaskCount,
            Languages = item.Languages,
            CreatedAt = item.CreatedAt,
            SampleAudioUrl = item.SampleAudioUrl,
        };
        if (!_persistSelectedVoice(item.Id, cached))
            return;

        _setVoiceIdWithoutSaving(item.Id);
        _applyCachedVoice(cached);
    }

    internal async Task SubmitVoiceIdAsync()
    {
        if (_isDisposed || _isFacadeDisposed())
            return;
        InvalidateVoiceIdInput();
        var voiceId = _getVoiceIdInput();
        if (string.IsNullOrEmpty(voiceId))
        {
            SetVoiceIdError(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_VoiceId_Empty"));
            return;
        }

        if (!SettingsValidation.IsValidVoiceIdFormat(voiceId))
        {
            SetVoiceIdError(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_VoiceId_InvalidFormat"));
            return;
        }

        var apiKey = _getApiKey();
        var credentialScope = VoiceMetadataCache.CredentialScope(apiKey);
        var operationId = BeginVoiceIdOperation(out var cts);
        _setSubmittingVoiceId(true);
        SetVoiceIdError(null);

        try
        {
            if (_metadataCache.TryGet(voiceId, out var cachedMetadata, credentialScope))
            {
                _setByIdResult(MapMetadata(cachedMetadata));
                return;
            }
            var model = await FishAudioApi.GetModelAsync(_context, FishAudioApi.MetadataApiKey(apiKey), voiceId, cts.Token);

            if (!IsCurrentVoiceIdOperation(operationId))
                return;

            if (model is null || !IsSelectableVoice(model))
            {
                SetVoiceIdError(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_Voice_NotFound"));
                return;
            }

            var result = MapSearchResult(model, credentialScope, voiceId);
            if (!IsCurrentVoiceIdOperation(operationId))
                return;
            _setByIdResult(result);
            SetVoiceIdError(null);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentVoiceIdOperation(operationId))
                SetVoiceIdError(FishAudioRequestPolicy.GetUserFacingError(_context, ex));
        }
        finally
        {
            if (IsCurrentVoiceIdOperation(operationId))
                _setSubmittingVoiceId(false);
            CompleteVoiceIdOperation(cts);
            cts.Dispose();
        }
    }

    internal void InvalidateVoiceIdInput()
    {
        Interlocked.Increment(ref _voiceIdOperationId);
        SetVoiceIdError(null);
        var cancellation = _voiceIdCancellationTokenSource;
        _voiceIdCancellationTokenSource = null;
        _setSubmittingVoiceId(false);
        _setByIdResult(null);
        cancellation?.Cancel();
    }

    private void SetVoiceIdError(string? error, long? expectedVersion = null)
    {
        if (_isFacadeDisposed())
            return;

        ITimer? previous;
        long version;
        lock (_voiceIdErrorGate)
        {
            if (_isDisposed || (expectedVersion is { } expected && _voiceIdErrorVersion != expected))
                return;

            version = ++_voiceIdErrorVersion;
            previous = _voiceIdErrorTimer;
            _voiceIdErrorTimer = null;
        }
        previous?.Dispose();

        if (!IsCurrentVoiceIdError(version) || _isFacadeDisposed())
            return;
        if (error is not null)
        {
            // Creation/disposal may block or reenter; neither runs under the state gate.
            var candidate = _timeProvider.CreateTimer(_ => _runOnUiThread(() =>
            {
                // Disposing a timer cannot retract a callback already queued to WPF.
                // Conditional retirement rechecks the occurrence inside the UI action.
                SetVoiceIdError(null, version);
            }), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

            var facadeActive = !_isFacadeDisposed();
            bool attached;
            lock (_voiceIdErrorGate)
            {
                attached = facadeActive && !_isDisposed && _voiceIdErrorVersion == version;
                if (attached)
                    _voiceIdErrorTimer = candidate;
            }
            if (!attached)
            {
                candidate.Dispose();
                return;
            }
        }

        if (IsCurrentVoiceIdError(version) && !_isFacadeDisposed())
            _setVoiceIdError(error);
    }

    private bool IsCurrentVoiceIdError(long version)
    {
        lock (_voiceIdErrorGate)
            return !_isDisposed && _voiceIdErrorVersion == version;
    }

    internal static CachedVoiceInfo CreateCachedVoiceInfo(ModelEntity model) => new()
    {
        Title = model.Title,
        Description = model.Description,
        CoverImage = model.CoverImage,
        AuthorName = model.Author?.Nickname ?? "",
        TaskCount = model.TaskCount,
        Languages = model.Languages,
        CreatedAt = model.CreatedAt,
        SampleAudioUrl = model.Samples.FirstOrDefault()?.Audio,
    };

    private async Task ExecuteSearchAsync(int requestedPage, VoiceSearchCriteria criteria, bool resetOnEmptyResponse = false)
    {
        if (_isDisposed || _isFacadeDisposed()) return;
        var apiKey = _getApiKey();
        var credentialScope = VoiceMetadataCache.CredentialScope(apiKey);
        var operationId = BeginSearchOperation(out var cts);
        _setSearching(true);
        try
        {
            var response = await FishAudioApi.SearchModelsAsync(
                _context, FishAudioApi.MetadataApiKey(apiKey), criteria, SearchPageSize, requestedPage, cts.Token);

            if (!IsCurrentSearchOperation(operationId))
                return;

            if (response is null)
                throw new InvalidOperationException("Fish Audio voice search returned an empty response.");

            // Validate and detach provider data before changing the last successful
            // relationship. Queue one publication unit, then recheck its owner there.
            var pagination = VoiceSearchPagination.FromResponse(response, requestedPage, SearchPageSize);
            var metadata = (response.Items ?? []).Where(IsSelectableVoice).Select(model => VoiceMetadata.FromModel(model)).ToArray();
            var results = metadata.Select(CreateSearchItem).ToList();
            _runOnUiThread(() =>
            {
                if (!IsCurrentSearchOperation(operationId)) return;
                _pagination = pagination;
                _committedSearchCriteria = criteria;
                _highestVisitedPage = resetOnEmptyResponse ? requestedPage : Math.Max(_highestVisitedPage, requestedPage);
                _setSearchPagination(pagination);
                _setPageInput(requestedPage.ToString());
                _setHasSearched(true);
                _setSearchResults(results);
                _syncPreviewStateToResults();

                // Covers/cache notifications are enrichment, after the complete
                // successful snapshot (and canonical page bound) is visible.
                for (var index = 0; index < results.Count && IsCurrentSearchOperation(operationId); index++)
                {
                    try
                    {
                        _metadataCache.Put(metadata[index], credentialScope);
                        if (!IsCurrentSearchOperation(operationId)) return;
                        ResolveCover(results[index]);
                    }
                    catch (Exception)
                    {
                        // Enrichment cannot turn a valid metadata response into a
                        // partially failed Apply. Never log remote URLs or text.
                        _context.Logger.LogWarning("Voice search display enrichment failed.");
                    }
                }
            });
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _runOnUiThread(() =>
            {
                if (IsCurrentSearchOperation(operationId))
                    _context.Snackbar.ShowError(FishAudioRequestPolicy.GetUserFacingError(_context, ex));
            });
        }
        finally
        {
            _runOnUiThread(() =>
            {
                if (IsCurrentSearchOperation(operationId)) _setSearching(false);
            });
            CompleteSearchOperation(cts);
            cts.Dispose();
        }
    }

    private static bool IsSelectableVoice(ModelEntity model) =>
        string.IsNullOrWhiteSpace(model.Type) || string.Equals(model.Type, "tts", StringComparison.OrdinalIgnoreCase);

    private VoiceSearchItem MapSearchResult(ModelEntity model, string credentialScope, string? voiceId = null)
    {
        var metadata = VoiceMetadata.FromModel(model, voiceId);
        _metadataCache.Put(metadata, credentialScope);
        return MapMetadata(metadata);
    }

    private VoiceSearchItem MapMetadata(VoiceMetadata metadata)
    {
        var item = CreateSearchItem(metadata);
        ResolveCover(item);
        return item;
    }

    private static VoiceSearchItem CreateSearchItem(VoiceMetadata metadata) => new()
    {
        Id = metadata.VoiceId,
        Title = metadata.Title,
        Description = metadata.Description,
        AuthorName = metadata.AuthorName,
        TaskCount = metadata.TaskCount,
        Languages = metadata.Languages,
        CreatedAt = metadata.CreatedAt,
        SampleAudioUrl = metadata.SampleAudioUrl,
        CoverImage = metadata.CoverImage,
    };

    private void ResolveCover(VoiceSearchItem item) =>
        item.CoverUrl = _resolveCoverImageUrl(item.Id, item.CoverImage, 64, url => item.CoverUrl = url);

    private long BeginSearchOperation(out CancellationTokenSource cts)
    {
        var previous = _searchCancellationTokenSource;
        cts = new CancellationTokenSource();
        var operationId = Interlocked.Increment(ref _searchOperationId);
        _searchCancellationTokenSource = cts;
        previous?.Cancel();
        return operationId;
    }

    private bool IsCurrentSearchOperation(long operationId) =>
        !Volatile.Read(ref _isDisposed)
        && !_isFacadeDisposed()
        && Volatile.Read(ref _searchOperationId) == operationId;

    private void CompleteSearchOperation(CancellationTokenSource cts)
    {
        if (ReferenceEquals(_searchCancellationTokenSource, cts))
            _searchCancellationTokenSource = null;
    }

    private long BeginVoiceIdOperation(out CancellationTokenSource cts)
    {
        var previous = _voiceIdCancellationTokenSource;
        cts = new CancellationTokenSource();
        var operationId = Interlocked.Increment(ref _voiceIdOperationId);
        _voiceIdCancellationTokenSource = cts;
        previous?.Cancel();
        return operationId;
    }

    private bool IsCurrentVoiceIdOperation(long operationId) =>
        !Volatile.Read(ref _isDisposed)
        && !_isFacadeDisposed()
        && Volatile.Read(ref _voiceIdOperationId) == operationId;

    private void CompleteVoiceIdOperation(CancellationTokenSource cts)
    {
        if (ReferenceEquals(_voiceIdCancellationTokenSource, cts))
            _voiceIdCancellationTokenSource = null;
    }

    internal void InvalidateCredentials()
    {
        Interlocked.Increment(ref _searchOperationId);
        Interlocked.Increment(ref _voiceIdOperationId);
        var searchCancellation = _searchCancellationTokenSource;
        var voiceIdCancellation = _voiceIdCancellationTokenSource;
        _searchCancellationTokenSource = null;
        _voiceIdCancellationTokenSource = null;
        // Retain displayed results and the committed navigation state. Later explicit
        // requests capture the accepted key and use its separate metadata-cache scope.
        _setSearching(false);
        _setSubmittingVoiceId(false);
        searchCancellation?.Cancel();
        voiceIdCancellation?.Cancel();
    }

    internal void InvalidateOperations()
    {
        ITimer? timer;
        lock (_voiceIdErrorGate)
        {
            if (_isDisposed)
                return;

            Volatile.Write(ref _isDisposed, true);
            ++_voiceIdErrorVersion;
            timer = _voiceIdErrorTimer;
            _voiceIdErrorTimer = null;
            Interlocked.Increment(ref _searchOperationId);
            Interlocked.Increment(ref _voiceIdOperationId);
        }
        timer?.Dispose();
    }

    public void Dispose()
    {
        InvalidateOperations();

        var searchCancellation = _searchCancellationTokenSource;
        var voiceIdCancellation = _voiceIdCancellationTokenSource;
        _searchCancellationTokenSource = null;
        _voiceIdCancellationTokenSource = null;
        searchCancellation?.Cancel();
        voiceIdCancellation?.Cancel();
    }
}
