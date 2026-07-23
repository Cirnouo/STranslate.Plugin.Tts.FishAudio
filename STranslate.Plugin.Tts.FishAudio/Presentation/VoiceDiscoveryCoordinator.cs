using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace STranslate.Plugin.Tts.FishAudio.Presentation;

internal sealed class VoiceDiscoveryCoordinator : IDisposable
{
    private const string SearchApiToken = "dummy";
    private const int SearchPageSize = 6;

    private readonly IPluginContext _context;
    private readonly Func<string> _getSearchQuery;
    private readonly Func<int> _getSearchPage;
    private readonly Func<int> _getSearchTotalPages;
    private readonly Func<string> _getPageInput;
    private readonly Func<string> _getVoiceIdInput;
    private readonly Action<bool> _setSearching;
    private readonly Action<int> _setSearchPage;
    private readonly Action<string> _setPageInput;
    private readonly Action<bool> _setHasSearched;
    private readonly Action<int> _setSearchResultCount;
    private readonly Action<int> _setSearchTotalPages;
    private readonly Action<List<VoiceSearchItem>> _setSearchResults;
    private readonly Func<string, string, int, Action<string>?, string> _resolveCoverImageUrl;
    private readonly Action _syncPreviewStateToResults;
    private readonly Action<string> _setVoiceIdWithoutSaving;
    private readonly Func<CachedVoiceInfo, bool> _persistSelectedVoice;
    private readonly Action<CachedVoiceInfo?> _applyCachedVoice;
    private readonly Action<string?> _setVoiceIdError;
    private readonly Action<bool> _setSubmittingVoiceId;
    private readonly Func<bool> _isFacadeDisposed;
    private long _searchOperationId;
    private long _voiceIdOperationId;
    private bool _isDisposed;
    private CancellationTokenSource? _searchCancellationTokenSource;
    private CancellationTokenSource? _voiceIdCancellationTokenSource;

    internal VoiceDiscoveryCoordinator(
        IPluginContext context,
        Func<string> getSearchQuery,
        Func<int> getSearchPage,
        Func<int> getSearchTotalPages,
        Func<string> getPageInput,
        Func<string> getVoiceIdInput,
        Action<bool> setSearching,
        Action<int> setSearchPage,
        Action<string> setPageInput,
        Action<bool> setHasSearched,
        Action<int> setSearchResultCount,
        Action<int> setSearchTotalPages,
        Action<List<VoiceSearchItem>> setSearchResults,
        Func<string, string, int, Action<string>?, string> resolveCoverImageUrl,
        Action syncPreviewStateToResults,
        Action<string> setVoiceIdWithoutSaving,
        Func<CachedVoiceInfo, bool> persistSelectedVoice,
        Action<CachedVoiceInfo?> applyCachedVoice,
        Action<string?> setVoiceIdError,
        Action<bool> setSubmittingVoiceId,
        Func<bool> isFacadeDisposed)
    {
        _context = context;
        _getSearchQuery = getSearchQuery;
        _getSearchPage = getSearchPage;
        _getSearchTotalPages = getSearchTotalPages;
        _getPageInput = getPageInput;
        _getVoiceIdInput = getVoiceIdInput;
        _setSearching = setSearching;
        _setSearchPage = setSearchPage;
        _setPageInput = setPageInput;
        _setHasSearched = setHasSearched;
        _setSearchResultCount = setSearchResultCount;
        _setSearchTotalPages = setSearchTotalPages;
        _setSearchResults = setSearchResults;
        _resolveCoverImageUrl = resolveCoverImageUrl;
        _syncPreviewStateToResults = syncPreviewStateToResults;
        _setVoiceIdWithoutSaving = setVoiceIdWithoutSaving;
        _persistSelectedVoice = persistSelectedVoice;
        _applyCachedVoice = applyCachedVoice;
        _setVoiceIdError = setVoiceIdError;
        _setSubmittingVoiceId = setSubmittingVoiceId;
        _isFacadeDisposed = isFacadeDisposed;
    }

    internal Task SearchVoicesAsync() => ExecuteSearchAsync(1, resetOnEmptyResponse: true);

    internal async Task NextPageAsync()
    {
        var searchPage = _getSearchPage();
        if (searchPage < _getSearchTotalPages())
            await ExecuteSearchAsync(searchPage + 1);
    }

    internal async Task PrevPageAsync()
    {
        var searchPage = _getSearchPage();
        if (searchPage > 1)
            await ExecuteSearchAsync(searchPage - 1);
    }

    internal async Task CommitPageInputAsync()
    {
        var searchPage = _getSearchPage();
        if (int.TryParse(_getPageInput(), out var requestedPage)
            && requestedPage >= 1
            && requestedPage <= _getSearchTotalPages()
            && requestedPage != searchPage)
        {
            await ExecuteSearchAsync(requestedPage);
        }
        else
        {
            _setPageInput(searchPage.ToString());
        }
    }

    internal void SelectVoice(VoiceSearchItem? item)
    {
        if (item is null)
            return;

        _setVoiceIdWithoutSaving(item.Id);

        var cached = new CachedVoiceInfo
        {
            Title = item.Title,
            Description = item.Description,
            CoverImage = item.CoverImage,
            AuthorName = item.AuthorName,
            TaskCount = item.TaskCount,
            SampleAudioUrl = item.SampleAudioUrl,
        };
        if (!_persistSelectedVoice(cached))
            return;

        _applyCachedVoice(cached);
    }

    internal async Task SubmitVoiceIdAsync()
    {
        var trimmed = _getVoiceIdInput().Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            _setVoiceIdError(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_VoiceId_Empty"));
            return;
        }

        if (!SettingsValidation.IsValidVoiceIdFormat(trimmed))
        {
            _setVoiceIdError(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_VoiceId_InvalidFormat"));
            return;
        }

        var operationId = BeginVoiceIdOperation(out var cts);
        _setSubmittingVoiceId(true);
        _setVoiceIdError(null);

        try
        {
            var model = await FishAudioApi.GetModelAsync(_context, SearchApiToken, trimmed, cts.Token);

            if (!IsCurrentVoiceIdOperation(operationId))
                return;

            if (model is null)
            {
                _setVoiceIdError(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_Voice_NotFound"));
                return;
            }

            _setVoiceIdWithoutSaving(trimmed);

            var cached = CreateCachedVoiceInfo(model);
            if (!_persistSelectedVoice(cached))
                return;

            _applyCachedVoice(cached);
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

    internal static CachedVoiceInfo CreateCachedVoiceInfo(ModelEntity model) => new()
    {
        Title = model.Title,
        Description = model.Description,
        CoverImage = model.CoverImage,
        AuthorName = model.Author?.Nickname ?? "",
        TaskCount = model.TaskCount,
        SampleAudioUrl = model.Samples.FirstOrDefault()?.Audio,
    };

    private async Task ExecuteSearchAsync(int requestedPage, bool resetOnEmptyResponse = false)
    {
        var operationId = BeginSearchOperation(out var cts);
        _setSearching(true);
        try
        {
            var response = await FishAudioApi.SearchModelsAsync(
                _context, SearchApiToken, _getSearchQuery(), SearchPageSize, requestedPage, cts.Token);

            if (!IsCurrentSearchOperation(operationId))
                return;

            if (response is null)
            {
                if (resetOnEmptyResponse)
                {
                    _setSearchPage(1);
                    _setPageInput("1");
                    _setSearchResults([]);
                    _setSearchTotalPages(1);
                    _setSearchResultCount(0);
                }
                return;
            }

            _setSearchPage(requestedPage);
            _setPageInput(requestedPage.ToString());
            _setHasSearched(true);
            _setSearchResultCount(response.Total);
            _setSearchTotalPages(Math.Max(1, (int)Math.Ceiling(response.Total / (double)SearchPageSize)));
            var results = response.Items.Select(MapSearchResult).ToList();
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

    private VoiceSearchItem MapSearchResult(ModelEntity model)
    {
        var item = new VoiceSearchItem
        {
            Id = model.Id,
            Title = model.Title,
            Description = model.Description,
            AuthorName = model.Author?.Nickname ?? "",
            TaskCount = model.TaskCount,
            SampleAudioUrl = model.Samples.FirstOrDefault()?.Audio,
            CoverImage = model.CoverImage,
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
