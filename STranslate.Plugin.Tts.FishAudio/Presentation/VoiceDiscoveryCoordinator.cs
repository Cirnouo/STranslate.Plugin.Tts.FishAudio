using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Caching;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace STranslate.Plugin.Tts.FishAudio.Presentation;

internal sealed class VoiceDiscoveryCoordinator : IDisposable
{
    private const string SearchApiToken = "dummy";
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
    private readonly Action<bool> _setSubmittingVoiceId;
    private readonly Func<bool> _isFacadeDisposed;
    private long _searchOperationId;
    private long _voiceIdOperationId;
    private bool _isDisposed;
    private CancellationTokenSource? _searchCancellationTokenSource;
    private CancellationTokenSource? _voiceIdCancellationTokenSource;
    private VoiceSearchPagination _pagination = VoiceSearchPagination.Initial;
    private string _committedSearchQuery = "";
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
        VoiceMetadataCache? metadataCache = null)
    {
        _context = context;
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
        _setSubmittingVoiceId = setSubmittingVoiceId;
        _isFacadeDisposed = isFacadeDisposed;
    }

    internal Task SearchVoicesAsync() => ExecuteSearchAsync(1, _getSearchQuery(), resetOnEmptyResponse: true);

    internal async Task NextPageAsync()
    {
        if (_pagination.HasNextPage)
            await ExecuteSearchAsync(_pagination.Page + 1, _committedSearchQuery);
    }

    internal async Task PrevPageAsync()
    {
        if (_pagination.Page > 1)
            await ExecuteSearchAsync(_pagination.Page - 1, _committedSearchQuery);
    }

    internal async Task CommitPageInputAsync()
    {
        if (int.TryParse(_getPageInput(), out var requestedPage)
            && _pagination.CanRequestPage(requestedPage, _highestVisitedPage)
            && requestedPage != _pagination.Page)
        {
            await ExecuteSearchAsync(requestedPage, _committedSearchQuery);
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
            _setVoiceIdError(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_VoiceId_Empty"));
            return;
        }

        if (!SettingsValidation.IsValidVoiceIdFormat(voiceId))
        {
            _setVoiceIdError(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_VoiceId_InvalidFormat"));
            return;
        }

        var operationId = BeginVoiceIdOperation(out var cts);
        _setSubmittingVoiceId(true);
        _setVoiceIdError(null);

        try
        {
            if (_metadataCache.TryGet(voiceId, out var cachedMetadata))
            {
                _setByIdResult(MapMetadata(cachedMetadata));
                return;
            }
            var model = await FishAudioApi.GetModelAsync(_context, SearchApiToken, voiceId, cts.Token);

            if (!IsCurrentVoiceIdOperation(operationId))
                return;

            if (model is null || !IsSelectableVoice(model))
            {
                _setVoiceIdError(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_Voice_NotFound"));
                return;
            }

            var result = MapSearchResult(model, voiceId);
            _setByIdResult(result);
            _setVoiceIdError(null);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentVoiceIdOperation(operationId))
                _setVoiceIdError(FishAudioRequestPolicy.GetUserFacingError(_context, ex));
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
        var cancellation = _voiceIdCancellationTokenSource;
        _voiceIdCancellationTokenSource = null;
        _setSubmittingVoiceId(false);
        _setByIdResult(null);
        cancellation?.Cancel();
    }

    internal static CachedVoiceInfo CreateCachedVoiceInfo(ModelEntity model) => new()
    {
        Title = model.Title,
        Description = model.Description,
        CoverImage = model.CoverImage,
        AuthorName = model.Author?.Nickname ?? "",
        TaskCount = model.TaskCount,
        SampleAudioUrl = model.Samples.FirstOrDefault()?.Audio,
    };

    private async Task ExecuteSearchAsync(int requestedPage, string query, bool resetOnEmptyResponse = false)
    {
        var operationId = BeginSearchOperation(out var cts);
        _setSearching(true);
        try
        {
            var response = await FishAudioApi.SearchModelsAsync(
                _context, SearchApiToken, query, SearchPageSize, requestedPage, cts.Token);

            if (!IsCurrentSearchOperation(operationId))
                return;

            if (response is null)
            {
                if (resetOnEmptyResponse)
                {
                    _pagination = new VoiceSearchPagination(1, 0, true, 1, false, false);
                    _committedSearchQuery = query;
                    _highestVisitedPage = 1;
                    _setSearchPagination(_pagination);
                    _setPageInput("1");
                    _setSearchResults([]);
                }
                return;
            }

            _pagination = VoiceSearchPagination.FromResponse(response, requestedPage, SearchPageSize);
            _committedSearchQuery = query;
            _highestVisitedPage = resetOnEmptyResponse ? requestedPage : Math.Max(_highestVisitedPage, requestedPage);
            _setSearchPagination(_pagination);
            _setPageInput(requestedPage.ToString());
            _setHasSearched(true);
            if (!IsCurrentSearchOperation(operationId))
                return;
            var results = (response.Items ?? []).Where(IsSelectableVoice).Select(model => MapSearchResult(model)).ToList();
            if (!IsCurrentSearchOperation(operationId))
                return;
            _setSearchResults(results);
            _syncPreviewStateToResults();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentSearchOperation(operationId))
                _context.Snackbar.ShowError(FishAudioRequestPolicy.GetUserFacingError(_context, ex));
        }
        finally
        {
            if (IsCurrentSearchOperation(operationId))
                _setSearching(false);
            CompleteSearchOperation(cts);
            cts.Dispose();
        }
    }

    private static bool IsSelectableVoice(ModelEntity model) =>
        string.IsNullOrWhiteSpace(model.Type) || string.Equals(model.Type, "tts", StringComparison.OrdinalIgnoreCase);

    private VoiceSearchItem MapSearchResult(ModelEntity model, string? voiceId = null)
    {
        var metadata = VoiceMetadata.FromModel(model, voiceId);
        _metadataCache.Put(metadata);
        return MapMetadata(metadata);
    }

    private VoiceSearchItem MapMetadata(VoiceMetadata metadata)
    {
        var item = new VoiceSearchItem
        {
            Id = metadata.VoiceId,
            Title = metadata.Title,
            Description = metadata.Description,
            AuthorName = metadata.AuthorName,
            TaskCount = metadata.TaskCount,
            SampleAudioUrl = metadata.SampleAudioUrl,
            CoverImage = metadata.CoverImage,
        };
        item.CoverUrl = _resolveCoverImageUrl(item.Id, item.CoverImage, 64, url => item.CoverUrl = url);
        return item;
    }

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

    internal void InvalidateOperations()
    {
        if (Volatile.Read(ref _isDisposed))
            return;

        Volatile.Write(ref _isDisposed, true);
        Interlocked.Increment(ref _searchOperationId);
        Interlocked.Increment(ref _voiceIdOperationId);
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
