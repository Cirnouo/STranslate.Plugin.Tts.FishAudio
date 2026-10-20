using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Caching;
using STranslate.Plugin.Tts.FishAudio.FishAudio;

namespace STranslate.Plugin.Tts.FishAudio.ViewModel;

public enum VoiceBrowseMode { Search, ById, Favorites }

public partial class SettingsViewModel
{
    private const int FavoritePageSize = 6;
    private readonly VoiceMetadataCache _voiceMetadataCache;
    private readonly FavoriteVoiceStore _favoriteStore;
    private readonly object _favoritesPersistenceGate = new();
    private IReadOnlyList<string> _favoriteIds = [];
    private CancellationTokenSource? _favoritePageCancellation;
    private long _favoritePageGeneration;

    internal Task PendingFavoriteMetadataTask { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial List<VoiceSearchItem> ByIdResults { get; set; } = [];

    [ObservableProperty]
    public partial List<VoiceSearchItem> FavoriteResults { get; set; } = [];

    [ObservableProperty]
    public partial string? FavoritesError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFavoritePrevPage))]
    [NotifyPropertyChangedFor(nameof(ShowFavoriteNextPage))]
    public partial int FavoritePage { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFavoriteNextPage))]
    [NotifyPropertyChangedFor(nameof(ShowFavoritePagination))]
    public partial int FavoriteTotalPages { get; set; } = 1;

    [ObservableProperty]
    public partial int FavoriteCount { get; set; }

    [ObservableProperty]
    public partial string FavoritePageInput { get; set; } = "1";

    public bool ShowFavoritePrevPage => FavoritePage > 1;
    public bool ShowFavoriteNextPage => FavoritePage < FavoriteTotalPages;
    public bool ShowFavoritePagination => FavoriteTotalPages > 1;

    [RelayCommand]
    private void SwitchToFavorites()
    {
        BrowseMode = VoiceBrowseMode.Favorites;
        ReloadFavorites();
    }

    [RelayCommand]
    private void FavoriteNextPage()
    {
        if (ShowFavoriteNextPage)
            ShowFavoritePage(FavoritePage + 1);
    }

    [RelayCommand]
    private void FavoritePrevPage()
    {
        if (ShowFavoritePrevPage)
            ShowFavoritePage(FavoritePage - 1);
    }

    [RelayCommand]
    private void CommitFavoritePageInput()
    {
        if (int.TryParse(FavoritePageInput, out var page) && page >= 1 && page <= FavoriteTotalPages)
            ShowFavoritePage(page);
        else
            FavoritePageInput = FavoritePage.ToString();
    }

    [RelayCommand]
    private void ToggleFavorite(VoiceSearchItem? item)
    {
        if (_isDisposed || item is null || !SettingsValidation.IsValidVoiceIdFormat(item.Id))
            return;
        var add = !_favoriteIds.Contains(item.Id, StringComparer.Ordinal);
        IReadOnlyList<string>? savedIds = null;
        try
        {
            // Lock order: VM persistence -> SettingsStore persistence authorization -> store/path.
            // This serializes disposal/retirement with the file commit; publish UI only after release.
            lock (_favoritesPersistenceGate)
            {
                if (_isDisposed)
                    return;
                if (_settingsWriteLease is { } lease)
                {
                    if (!SettingsStore.TryExecuteWithWriteLease(_settings, lease,
                        () => savedIds = _favoriteStore.SetFavorite(item.Id, add)))
                        return;
                }
                else
                {
                    savedIds = _favoriteStore.SetFavorite(item.Id, add);
                }
            }
            if (_isDisposed)
                return;
            FavoritesError = null;
            _favoriteIds = savedIds!;
            RefreshFavoriteCards();
            SyncFavoriteState();
        }
        catch (FavoriteVoiceLimitException)
        {
            ShowFavoriteError("Favorites_Limit");
        }
        catch
        {
            FavoritesError = _context.GetTranslation("STranslate_Plugin_Tts_FishAudio_Favorites_SaveFailed");
            ShowFavoriteError("Favorites_SaveFailed");
        }
    }

    private void ReloadFavorites()
    {
        if (_isDisposed)
            return;
        try
        {
            _favoriteIds = _favoriteStore.Read();
            FavoritesError = null;
        }
        catch
        {
            // Keep any already-displayed IDs on a failed reload. Never replace a broken file.
            FavoritesError = _context.GetTranslation("STranslate_Plugin_Tts_FishAudio_Favorites_LoadFailed");
        }
        RefreshFavoriteCards();
        SyncFavoriteState();
    }

    private void ShowFavoriteError(string suffix) =>
        _context.Snackbar.ShowError(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_" + suffix));

    private List<VoiceSearchItem> GetVoiceCards() => [.. SearchResults, .. ByIdResults, .. FavoriteResults];

    private void SyncFavoriteState()
    {
        var ids = _favoriteIds.ToHashSet(StringComparer.Ordinal);
        foreach (var card in GetVoiceCards())
            card.IsFavorite = ids.Contains(card.Id);
    }

    private void RefreshFavoriteCards()
    {
        FavoriteCount = _favoriteIds.Count;
        FavoriteTotalPages = Math.Max(1, (FavoriteCount + FavoritePageSize - 1) / FavoritePageSize);
        ShowFavoritePage(Math.Min(FavoritePage, FavoriteTotalPages));
    }

    private void ShowFavoritePage(int page)
    {
        CancelFavoritePageLoad();
        FavoritePage = page;
        FavoritePageInput = page.ToString();
        var previous = FavoriteResults.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var cards = _favoriteIds.Skip((page - 1) * FavoritePageSize).Take(FavoritePageSize).Select(id =>
        {
            var card = previous.GetValueOrDefault(id) ?? new VoiceSearchItem { Id = id };
            card.IsFavorite = true;
            if (_voiceMetadataCache.TryGet(id, out var metadata))
                ApplyMetadata(card, metadata);
            else
                ResetMetadataPlaceholder(card);
            return card;
        }).ToList();
        FavoriteResults = cards;
        if (!IsFavoritesMode || FavoritesError is not null)
            return;
        var missing = cards.Where(card => !card.HasMetadata).ToArray();
        if (missing.Length == 0)
            return;
        var cancellation = new CancellationTokenSource();
        _favoritePageCancellation = cancellation;
        var generation = _favoritePageGeneration;
        foreach (var card in missing)
            card.IsLoadingMetadata = true;
        PendingFavoriteMetadataTask = LoadFavoritePageAsync(missing, generation, cancellation);
    }

    private async Task LoadFavoritePageAsync(VoiceSearchItem[] cards, long generation, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.WhenAll(cards.Select(card => LoadFavoriteMetadataAsync(card, generation, cancellation.Token)));
        }
        finally
        {
            if (ReferenceEquals(_favoritePageCancellation, cancellation))
                _favoritePageCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task LoadFavoriteMetadataAsync(VoiceSearchItem card, long generation, CancellationToken token)
    {
        try
        {
            var model = await FishAudioApi.GetModelAsync(_context, "dummy", card.Id, token);
            if (!IsCurrentFavoriteCard(card, generation))
                return;
            if (model is null || (!string.IsNullOrWhiteSpace(model.Type) && !string.Equals(model.Type, "tts", StringComparison.OrdinalIgnoreCase)))
            {
                if (!card.HasMetadata)
                    card.MetadataError = _context.GetTranslation("STranslate_Plugin_Tts_FishAudio_Voice_NotFound");
                return;
            }
            var metadata = VoiceMetadata.FromModel(model, card.Id);
            _voiceMetadataCache.Put(metadata);
            if (IsCurrentFavoriteCard(card, generation))
                ApplyMetadata(card, metadata);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (IsCurrentFavoriteCard(card, generation) && !card.HasMetadata)
                card.MetadataError = FishAudioRequestPolicy.GetUserFacingError(_context, exception);
        }
        finally
        {
            if (IsCurrentFavoriteCard(card, generation))
                card.IsLoadingMetadata = false;
        }
    }

    private bool IsCurrentFavoriteCard(VoiceSearchItem card, long generation) =>
        !_isDisposed && generation == _favoritePageGeneration && FavoriteResults.Contains(card)
        && _favoriteIds.Contains(card.Id, StringComparer.Ordinal);

    private void CancelFavoritePageLoad()
    {
        ++_favoritePageGeneration;
        var cancellation = _favoritePageCancellation;
        _favoritePageCancellation = null;
        foreach (var card in FavoriteResults)
            card.IsLoadingMetadata = false;
        if (cancellation is null)
            return;
        try { cancellation.Cancel(); }
        finally { cancellation.Dispose(); }
    }

    private static void ResetMetadataPlaceholder(VoiceSearchItem card)
    {
        card.HasMetadata = false;
        card.IsLoadingMetadata = false;
        card.MetadataError = null;
        card.Title = card.Id;
        card.Description = "";
        card.AuthorName = "";
        card.TaskCount = 0;
        card.SampleAudioUrl = null;
        card.CoverImage = "";
        card.CoverUrl = "";
    }

    private void OnVoiceMetadataUpdated(VoiceMetadata metadata) => RunOnUiThread(() =>
    {
        if (_isDisposed)
            return;
        foreach (var card in GetVoiceCards().Where(card => card.Id == metadata.VoiceId))
            ApplyMetadata(card, metadata);
    });

    private void ApplyMetadata(VoiceSearchItem card, VoiceMetadata metadata)
    {
        card.Title = string.IsNullOrEmpty(metadata.Title) ? card.Id : metadata.Title;
        card.Description = metadata.Description;
        card.AuthorName = metadata.AuthorName;
        card.TaskCount = metadata.TaskCount;
        card.SampleAudioUrl = metadata.SampleAudioUrl;
        card.CoverImage = metadata.CoverImage;
        card.HasMetadata = true;
        card.IsLoadingMetadata = false;
        card.MetadataError = null;
        ResolveCardCover(card);
    }

    private void ResolveCardCover(VoiceSearchItem card)
    {
        var coverImage = card.CoverImage;
        card.CoverUrl = ResolveCoverImageUrl(card.Id, coverImage, 64, url =>
        {
            if (!_isDisposed && card.CoverImage == coverImage && GetVoiceCards().Contains(card))
                card.CoverUrl = url;
        });
    }

}
