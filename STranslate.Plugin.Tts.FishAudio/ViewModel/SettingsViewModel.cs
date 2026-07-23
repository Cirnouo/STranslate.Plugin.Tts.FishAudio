using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.Service;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace STranslate.Plugin.Tts.FishAudio.ViewModel;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IPluginContext _context;
    private readonly Settings _settings;
    private readonly PreviewPlaybackController _previewPlayback;
    private readonly CoverImageCacheDisplayManager _coverImageCacheDisplay;

    private const long LatencyGoodMs = 300;
    private const long LatencyFairMs = 800;
    private const int ClearCoverImageCacheTimeoutSeconds = 10;
    private static readonly TimeSpan CoverImageDownloadTimeout = TimeSpan.FromSeconds(10);

    private static readonly SolidColorBrush BrushGood = new(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly SolidColorBrush BrushFair = new(Color.FromRgb(0xFF, 0x98, 0x00));
    private static readonly SolidColorBrush BrushPoor = new(Color.FromRgb(0xF4, 0x43, 0x36));

    internal static Action<Action>? UiThreadInvokerOverride { get; set; }

    // ── API ──

    [ObservableProperty]
    public partial string ApiKey { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PasteApiKeyCommand))]
    [NotifyPropertyChangedFor(nameof(IsApiKeyInputEnabled))]
    public partial bool IsApiKeyInputLocked { get; set; }

    public bool IsApiKeyInputEnabled => !IsApiKeyInputLocked;

    // ── 账户信息 ──

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCreditCommand))]
    public partial bool IsLoadingCredit { get; set; }

    [ObservableProperty]
    public partial string UserCredit { get; set; }

    [ObservableProperty]
    public partial string LatencyText { get; set; }

    [ObservableProperty]
    public partial SolidColorBrush? LatencyBrush { get; set; }

    // ── 声音选择 ──

    [ObservableProperty]
    public partial string VoiceId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNormalizeLoudnessEnabled))]
    public partial string SelectedModel { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowS21ProFreePromo))]
    public partial bool IsS21ProFreeAvailable { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowS21ProFreePromo))]
    public partial bool IsS21ProFreePromoDismissed { get; set; }

    public bool ShowS21ProFreePromo => IsS21ProFreeAvailable && !IsS21ProFreePromoDismissed;

    // ── 已选声音展示 ──

    [ObservableProperty]
    public partial string? CachedVoiceId { get; set; }

    [ObservableProperty]
    public partial string? CachedVoiceTitle { get; set; }

    [ObservableProperty]
    public partial string? CachedVoiceDescription { get; set; }

    [ObservableProperty]
    public partial string? CachedVoiceCoverUrl { get; set; }

    [ObservableProperty]
    public partial string? CachedVoiceAuthor { get; set; }

    [ObservableProperty]
    public partial string? CachedVoiceSampleUrl { get; set; }

    [ObservableProperty]
    public partial int CachedVoiceTaskCount { get; set; }

    // ── 韵律 ──

    [ObservableProperty]
    public partial double Speed { get; set; }

    [ObservableProperty]
    public partial double Volume { get; set; }

    [ObservableProperty]
    public partial bool NormalizeLoudness { get; set; }

    public bool IsNormalizeLoudnessEnabled => FishAudioRuntime.SupportsNormalizeLoudness(SelectedModel);

    // ── 音频输出 ──

    [ObservableProperty]
    public partial int Mp3Bitrate { get; set; }

    // ── 生成参数 ──

    [ObservableProperty]
    public partial double Temperature { get; set; }

    [ObservableProperty]
    public partial double TopP { get; set; }

    [ObservableProperty]
    public partial string SelectedLatency { get; set; }

    [ObservableProperty]
    public partial bool Normalize { get; set; }

    [ObservableProperty]
    public partial bool ConditionOnPreviousChunks { get; set; }

    // ── 声音搜索 ──

    [ObservableProperty]
    public partial string SearchQuery { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchVoicesCommand))]
    public partial bool IsSearching { get; set; }

    [ObservableProperty]
    public partial bool IsSearchMode { get; set; }

    [ObservableProperty]
    public partial List<VoiceSearchItem> SearchResults { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPrevPage))]
    [NotifyPropertyChangedFor(nameof(ShowNextPage))]
    public partial int SearchPage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNextPage))]
    [NotifyPropertyChangedFor(nameof(ShowPagination))]
    public partial int SearchTotalPages { get; set; }

    [ObservableProperty]
    public partial int SearchResultCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPagination))]
    public partial bool HasSearched { get; set; }

    public bool ShowPrevPage => SearchPage > 1;
    public bool ShowNextPage => SearchPage < SearchTotalPages;
    public bool ShowPagination => HasSearched && SearchTotalPages > 1;

    // ── 通过 ID 选择 ──

    [ObservableProperty]
    public partial string VoiceIdInput { get; set; }

    [ObservableProperty]
    public partial string? VoiceIdError { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitVoiceIdCommand))]
    public partial bool IsSubmittingVoiceId { get; set; }

    // ── 试听 ──

    [ObservableProperty]
    public partial string? PreviewingVoiceId { get; set; }

    [ObservableProperty]
    public partial double PreviewProgress { get; set; }

    [ObservableProperty]
    public partial bool IsDisplayVoicePreviewing { get; set; }

    private VoiceSearchItem? _previewingSearchItem;
    private readonly object _displayPreviewOperationGate = new();
    private long _searchOperationId;
    private long _voiceIdOperationId;
    private long _displayPreviewOperationId;
    private int _apiKeyOperationLockCount;
    private bool _isDisposed;
    private bool _suppressSettingsSave;
    private CancellationTokenSource? _searchCancellationTokenSource;
    private CancellationTokenSource? _voiceIdCancellationTokenSource;
    private CancellationTokenSource? _displayPreviewCancellationTokenSource;

    // ── 分页输入 ──

    [ObservableProperty]
    public partial string PageInput { get; set; }

    // ── 缓存 ──

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCoverImageCacheCommand))]
    public partial bool IsClearingCoverImageCache { get; set; }

    [ObservableProperty]
    public partial string CoverImageCacheSizeText { get; set; }

    // ── 延迟显示 ──

    private DispatcherTimer? _latencyHideTimer;

    // ── 静态选项 ──

    public IReadOnlyList<string> Models { get; private set; }
    public static IReadOnlyList<string> Latencies { get; } = SettingsNormalizer.Latencies;
    public static IReadOnlyList<int> Mp3Bitrates { get; } = SettingsNormalizer.Mp3Bitrates;

    private const int SearchPageSize = 6;

    public SettingsViewModel(
        IPluginContext context,
        Settings settings,
        DateTimeOffset? nowUtc = null)
        : this(context, settings, null, null, nowUtc, null)
    {
    }

    internal SettingsViewModel(
        IPluginContext context,
        Settings settings,
        Func<Task>? clearCoverImageCacheAsync,
        TimeSpan? clearCoverImageCacheTimeout,
        DateTimeOffset? nowUtc = null,
        Func<IPreviewAudioPlayer>? previewAudioPlayerFactory = null)
    {
        _context = context;
        _settings = settings;
        var modelPolicyTime = nowUtc ?? FishAudioRuntime.LocalUtcNow();
        Models = FishAudioRuntime.GetAvailableModels(modelPolicyTime);
        IsS21ProFreeAvailable = FishAudioRuntime.IsS21ProFreeAvailable(modelPolicyTime);
        _previewPlayback = new PreviewPlaybackController(
            context.Logger,
            ApplyPreviewPlaybackState,
            HandlePreviewPlaybackFailed,
            previewAudioPlayerFactory);
        _coverImageCacheDisplay = new CoverImageCacheDisplayManager(
            context,
            DownloadCoverImageAsync,
            clearCoverImageCacheAsync,
            clearCoverImageCacheTimeout ?? TimeSpan.FromSeconds(ClearCoverImageCacheTimeoutSeconds),
            RunOnUiThread);

        ApiKey = settings.ApiKey;
        VoiceId = settings.VoiceId;
        SelectedModel = settings.SelectedModel;
        IsS21ProFreePromoDismissed = settings.IsS21ProFreePromoDismissed;
        Speed = settings.Speed;
        Volume = settings.Volume;
        NormalizeLoudness = settings.NormalizeLoudness;
        Temperature = settings.Temperature;
        TopP = settings.TopP;
        SelectedLatency = settings.Latency;
        Normalize = settings.Normalize;
        Mp3Bitrate = settings.Mp3Bitrate;
        ConditionOnPreviousChunks = settings.ConditionOnPreviousChunks;

        UserCredit = "";
        LatencyText = "";
        SearchQuery = "";
        SearchResults = [];
        SearchPage = 1;
        PageInput = "1";
        VoiceIdInput = "";
        IsSearchMode = true;
        CoverImageCacheSizeText = _coverImageCacheDisplay.GetFormattedCacheSize();

        ApplyCachedVoice(settings.CachedVoice);

        PropertyChanged += OnPropertyChanged;
    }

    // ── API Key ──

    private string EffectiveApiKeyForSearch => "dummy";

    private bool CanPasteApiKey => !IsApiKeyInputLocked;

    [RelayCommand(CanExecute = nameof(CanPasteApiKey))]
    private void PasteApiKey()
    {
        var text = Clipboard.GetText();
        if (!string.IsNullOrEmpty(text))
            ApiKey = text.Trim();
    }

    [RelayCommand]
    private void PasteVoiceId()
    {
        var text = Clipboard.GetText();
        if (!string.IsNullOrEmpty(text))
            VoiceIdInput = text.Trim();
    }

    // ── 账户命令 ──

    private bool CanRefreshCredit => !IsLoadingCredit;

    [RelayCommand(CanExecute = nameof(CanRefreshCredit))]
    private async Task RefreshCreditAsync()
    {
        if (!FishAudioRuntime.TryPreflightApiKey(_context, _settings.ApiKey, "Credit refresh", showError: true, out var apiKey))
            return;

        await FetchCreditAsync(apiKey, showError: true, showLatency: true);
    }

    internal async Task RefreshCreditSilentlyAsync()
    {
        if (!FishAudioRuntime.TryPreflightApiKey(_context, _settings.ApiKey, "Credit refresh", showError: false, out var apiKey))
            return;

        await FetchCreditAsync(apiKey, showError: false, showLatency: false);
    }

    private async Task FetchCreditAsync(string apiKey, bool showError, bool showLatency)
    {
        IsLoadingCredit = true;
        BeginApiKeyOperation();
        if (showLatency)
        {
            LatencyText = "";
            LatencyBrush = null;
        }

        try
        {
            var (result, ms) = await FishAudioApi.GetCreditAsync(_context, apiKey, CancellationToken.None);
            ApplyCreditResult(result);

            if (showLatency)
            {
                LatencyText = $"{ms} ms";
                LatencyBrush = ms switch
                {
                    <= LatencyGoodMs => BrushGood,
                    <= LatencyFairMs => BrushFair,
                    _ => BrushPoor,
                };
                StartLatencyHideTimer();
            }
        }
        catch (Exception ex)
        {
            FishAudioRuntime.LogRequestFailure(_context, "Credit refresh", ex);
            if (showError)
                _context.Snackbar.ShowError(FishAudioRuntime.GetUserFacingError(_context, ex));
            if (showLatency)
            {
                LatencyText = "";
                LatencyBrush = null;
            }
        }
        finally
        {
            IsLoadingCredit = false;
            EndApiKeyOperation();
        }
    }

    internal void BeginApiKeyOperation()
    {
        if (Interlocked.Increment(ref _apiKeyOperationLockCount) == 1)
            IsApiKeyInputLocked = true;
    }

    internal void EndApiKeyOperation()
    {
        var count = Interlocked.Decrement(ref _apiKeyOperationLockCount);
        if (count <= 0)
        {
            Interlocked.Exchange(ref _apiKeyOperationLockCount, 0);
            IsApiKeyInputLocked = false;
        }
    }

    private void StartLatencyHideTimer()
    {
        _latencyHideTimer?.Stop();
        _latencyHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _latencyHideTimer.Tick += (_, _) =>
        {
            LatencyText = "";
            LatencyBrush = null;
            _latencyHideTimer?.Stop();
            _latencyHideTimer = null;
        };
        _latencyHideTimer.Start();
    }

    private void ApplyCreditResult(WalletCreditResponse? result)
    {
        if (result is null) return;
        UserCredit = result.Credit;
    }

    // ── 选择模式切换 ──

    internal void ApplyAvailableModels(DateTimeOffset nowUtc)
    {
        RunOnUiThread(() =>
        {
            if (_isDisposed)
                return;

            Models = FishAudioRuntime.GetAvailableModels(nowUtc);
            OnPropertyChanged(nameof(Models));
            IsS21ProFreeAvailable = FishAudioRuntime.IsS21ProFreeAvailable(nowUtc);

            if (!Models.Contains(SelectedModel, StringComparer.Ordinal))
            {
                _suppressSettingsSave = true;
                try
                {
                    SelectedModel = FishAudioRuntime.GetDefaultModel(nowUtc);
                }
                finally
                {
                    _suppressSettingsSave = false;
                }
            }
        });
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

    internal void ApplyRefreshedCachedVoice(CachedVoiceInfo cached)
    {
        RunOnUiThread(() =>
        {
            if (_isDisposed)
                return;

            _settings.CachedVoice = cached;
            ApplyCachedVoice(cached);
        });
    }

    // ── 限时推广 ──

    [RelayCommand]
    private void DismissS21ProFreePromo()
    {
        IsS21ProFreePromoDismissed = true;
        _settings.IsS21ProFreePromoDismissed = true;
        SettingsStore.Save(_context, _settings);
    }

    [RelayCommand]
    private void UseS21ProFreePromo()
    {
        if (!IsS21ProFreeAvailable)
            return;

        SelectedModel = FishAudioRuntime.S21ProFreeModel;
    }

    [RelayCommand]
    private void SwitchToSearch()
    {
        IsSearchMode = true;
    }

    [RelayCommand]
    private void SwitchToById()
    {
        IsSearchMode = false;
    }

    // ── 声音搜索命令 ──

    private bool CanSearchVoices => true;

    [RelayCommand(CanExecute = nameof(CanSearchVoices), AllowConcurrentExecutions = true)]
    private async Task SearchVoicesAsync()
    {
        await ExecuteSearchAsync(1, resetOnEmptyResponse: true);
    }

    private async Task ExecuteSearchAsync(int requestedPage, bool resetOnEmptyResponse = false)
    {
        var operationId = BeginSearchOperation(out var cts);
        IsSearching = true;
        try
        {
            var response = await FishAudioApi.SearchModelsAsync(
                _context, EffectiveApiKeyForSearch, SearchQuery, SearchPageSize, requestedPage, cts.Token);

            if (!IsCurrentSearchOperation(operationId))
                return;

            if (response is null)
            {
                if (resetOnEmptyResponse)
                {
                    SearchPage = 1;
                    PageInput = "1";
                    SearchResults = [];
                    SearchTotalPages = 1;
                    SearchResultCount = 0;
                }
                return;
            }

            SearchPage = requestedPage;
            PageInput = requestedPage.ToString();
            HasSearched = true;
            SearchResultCount = response.Total;
            SearchTotalPages = Math.Max(1, (int)Math.Ceiling(response.Total / (double)SearchPageSize));
            SearchResults = response.Items.Select(m =>
            {
                var item = new VoiceSearchItem
                {
                    Id = m.Id,
                    Title = m.Title,
                    Description = m.Description,
                    AuthorName = m.Author?.Nickname ?? "",
                    TaskCount = m.TaskCount,
                    SampleAudioUrl = m.Samples.FirstOrDefault()?.Audio,
                    CoverImage = m.CoverImage,
                };
                item.CoverUrl = ResolveCoverImageUrl(item.Id, item.CoverImage, 64, url => item.CoverUrl = url);
                return item;
            }).ToList();

            SyncPreviewStateToResults();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentSearchOperation(operationId))
                _context.Snackbar.ShowError(FishAudioRuntime.GetUserFacingError(_context, ex));
        }
        finally
        {
            if (IsCurrentSearchOperation(operationId))
                IsSearching = false;
            CompleteSearchOperation(cts);
            cts.Dispose();
        }
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (SearchPage < SearchTotalPages)
            await ExecuteSearchAsync(SearchPage + 1);
    }

    [RelayCommand]
    private async Task PrevPageAsync()
    {
        if (SearchPage > 1)
            await ExecuteSearchAsync(SearchPage - 1);
    }

    [RelayCommand]
    private async Task CommitPageInputAsync()
    {
        if (int.TryParse(PageInput, out var n) && n >= 1 && n <= SearchTotalPages && n != SearchPage)
        {
            await ExecuteSearchAsync(n);
        }
        else
        {
            PageInput = SearchPage.ToString();
        }
    }

    [RelayCommand]
    private void SelectVoice(VoiceSearchItem? item)
    {
        if (item is null) return;

        VoiceId = item.Id;
        _settings.VoiceId = VoiceId;

        var cached = new CachedVoiceInfo
        {
            Title = item.Title,
            Description = item.Description,
            CoverImage = item.CoverImage,
            AuthorName = item.AuthorName,
            TaskCount = item.TaskCount,
            SampleAudioUrl = item.SampleAudioUrl,
        };
        _settings.CachedVoice = cached;
        SettingsStore.Save(_context, _settings);

        ApplyCachedVoice(cached);
    }

    // ── 通过 ID 选择 ──

    private bool CanSubmitVoiceId => true;

    [RelayCommand(CanExecute = nameof(CanSubmitVoiceId), AllowConcurrentExecutions = true)]
    private async Task SubmitVoiceIdAsync()
    {
        var trimmed = VoiceIdInput.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            VoiceIdError = _context.GetTranslation("STranslate_Plugin_Tts_FishAudio_VoiceId_Empty");
            return;
        }

        if (!SettingsValidation.IsValidVoiceIdFormat(trimmed))
        {
            VoiceIdError = _context.GetTranslation("STranslate_Plugin_Tts_FishAudio_VoiceId_InvalidFormat");
            return;
        }

        var operationId = BeginVoiceIdOperation(out var cts);
        IsSubmittingVoiceId = true;
        VoiceIdError = null;

        try
        {
            var model = await FishAudioApi.GetModelAsync(_context, EffectiveApiKeyForSearch, trimmed, cts.Token);

            if (!IsCurrentVoiceIdOperation(operationId))
                return;

            if (model is null)
            {
                VoiceIdError = _context.GetTranslation("STranslate_Plugin_Tts_FishAudio_Voice_NotFound");
                return;
            }

            VoiceId = trimmed;
            _settings.VoiceId = VoiceId;

            var cached = CreateCachedVoiceInfo(model);
            _settings.CachedVoice = cached;
            SettingsStore.Save(_context, _settings);

            ApplyCachedVoice(cached);
            VoiceIdError = null;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentVoiceIdOperation(operationId))
                VoiceIdError = FishAudioRuntime.GetUserFacingError(_context, ex);
        }
        finally
        {
            if (IsCurrentVoiceIdOperation(operationId))
                IsSubmittingVoiceId = false;
            CompleteVoiceIdOperation(cts);
            cts.Dispose();
        }
    }

    // ── 试听系统 ──

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleDisplayPreviewAsync()
    {
        if (string.IsNullOrEmpty(CachedVoiceId) || string.IsNullOrEmpty(CachedVoiceSampleUrl))
            return;

        var voiceId = CachedVoiceId;
        var oldAudioUrl = CachedVoiceSampleUrl;
        if (_previewPlayback.PreviewingVoiceId == voiceId)
        {
            CancelDisplayPreviewOperation();
            _previewPlayback.Stop();
            return;
        }

        if (!TryBeginDisplayPreviewOperation(out var operationId, out var cts, out var cancellationToken))
            return;

        try
        {
            if (!CanStartPreview("Selected voice preview"))
                return;

            if (!IsCurrentDisplayPreviewOperation(operationId, voiceId))
                return;

            ModelEntity? model;
            try
            {
                model = await FishAudioApi.GetModelAsync(_context, EffectiveApiKeyForSearch, voiceId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (!IsCurrentDisplayPreviewOperation(operationId, voiceId))
                    return;

                _context.Logger?.LogWarning(ex, "Selected voice preview refresh failed for voice {VoiceId}", voiceId);
                StartDisplayPreview(voiceId, oldAudioUrl);
                return;
            }

            if (!IsCurrentDisplayPreviewOperation(operationId, voiceId))
                return;

            if (model is null)
            {
                _context.Logger?.LogWarning("Selected voice preview refresh returned no voice for {VoiceId}", voiceId);
                StartDisplayPreview(voiceId, oldAudioUrl);
                return;
            }

            var cached = CreateCachedVoiceInfo(model);
            _settings.CachedVoice = cached;
            SettingsStore.Save(_context, _settings);
            ApplyCachedVoice(cached);

            if (!PreviewAudioUrlValidator.TryCreateAllowedUri(cached.SampleAudioUrl, out _))
            {
                _context.Snackbar.ShowError(_context.GetTranslation(FishAudioRuntime.PreviewUnavailableKey));
                return;
            }

            if (IsCurrentDisplayPreviewOperation(operationId, voiceId))
                StartDisplayPreview(voiceId, cached.SampleAudioUrl!);
        }
        finally
        {
            CompleteDisplayPreviewOperation(cts);
        }
    }

    [RelayCommand]
    private void ToggleSearchItemPreview(VoiceSearchItem? item)
    {
        if (item is null || string.IsNullOrEmpty(item.SampleAudioUrl))
            return;

        CancelDisplayPreviewOperation();

        if (_previewPlayback.PreviewingVoiceId == item.Id)
        {
            _previewPlayback.Stop();
            return;
        }

        if (!CanStartPreview("Search result preview"))
            return;

        TogglePreview(item.Id, item.SampleAudioUrl);
    }

    private bool CanStartPreview(string operation)
    {
        if (FishAudioRuntime.IsNetworkAvailable())
            return true;

        _context.Logger?.LogWarning("{Operation} preflight failed: Network unavailable", operation);
        _context.Snackbar.ShowError(_context.GetTranslation(FishAudioRuntime.NetworkUnavailableKey));
        return false;
    }

    private void StartDisplayPreview(string voiceId, string audioUrl)
    {
        if (CanStartPreview("Selected voice preview playback"))
            TogglePreview(voiceId, audioUrl);
    }

    private void TogglePreview(string voiceId, string audioUrl)
    {
        _previewPlayback.Toggle(voiceId, audioUrl);
    }

    private void ApplyPreviewPlaybackState(string? voiceId, double progress)
    {
        PreviewingVoiceId = voiceId;
        PreviewProgress = progress;
        UpdatePreviewState();
    }

    private void HandlePreviewPlaybackFailed(Exception? exception)
    {
        if (_isDisposed)
            return;

        var key = FishAudioRuntime.IsNetworkAvailable()
            ? FishAudioRuntime.PreviewPlaybackFailedKey
            : FishAudioRuntime.NetworkUnavailableKey;
        _context.Snackbar.ShowError(_context.GetTranslation(key));
    }

    private void UpdatePreviewState()
    {
        IsDisplayVoicePreviewing = CachedVoiceId is not null && CachedVoiceId == PreviewingVoiceId;

        if (_previewingSearchItem is not null)
        {
            _previewingSearchItem.IsBeingPreviewed = false;
            _previewingSearchItem.PreviewProgress = 0;
            _previewingSearchItem = null;
        }

        if (PreviewingVoiceId is not null)
        {
            _previewingSearchItem = SearchResults.FirstOrDefault(x => x.Id == PreviewingVoiceId);
            if (_previewingSearchItem is not null)
            {
                _previewingSearchItem.IsBeingPreviewed = true;
                _previewingSearchItem.PreviewProgress = PreviewProgress;
            }
        }
    }

    private void SyncPreviewStateToResults()
    {
        _previewingSearchItem = null;
        if (PreviewingVoiceId is not null)
        {
            _previewingSearchItem = SearchResults.FirstOrDefault(x => x.Id == PreviewingVoiceId);
            if (_previewingSearchItem is not null)
            {
                _previewingSearchItem.IsBeingPreviewed = true;
                _previewingSearchItem.PreviewProgress = PreviewProgress;
            }
        }
    }

    // ── 清除声音 ──

    [RelayCommand]
    private void ClearVoice()
    {
        VoiceId = "";
        _settings.VoiceId = "";
        _settings.CachedVoice = null;
        SettingsStore.Save(_context, _settings);
        ApplyCachedVoice(null);
    }

    private void ApplyCachedVoice(CachedVoiceInfo? cached)
    {
        if (cached is null || string.IsNullOrEmpty(cached.Title))
        {
            CachedVoiceId = null;
            CachedVoiceTitle = null;
            CachedVoiceDescription = null;
            CachedVoiceCoverUrl = null;
            CachedVoiceAuthor = null;
            CachedVoiceSampleUrl = null;
            CachedVoiceTaskCount = 0;
            return;
        }

        CachedVoiceId = VoiceId;
        CachedVoiceTitle = cached.Title;
        CachedVoiceDescription = cached.Description;
        var cachedVoiceId = CachedVoiceId;
        CachedVoiceCoverUrl = ResolveCoverImageUrl(cachedVoiceId, cached.CoverImage, 128, url =>
        {
            if (CachedVoiceId == cachedVoiceId)
                CachedVoiceCoverUrl = url;
        });
        CachedVoiceAuthor = cached.AuthorName;
        CachedVoiceSampleUrl = cached.SampleAudioUrl;
        CachedVoiceTaskCount = cached.TaskCount;

        IsDisplayVoicePreviewing = CachedVoiceId == PreviewingVoiceId;
    }

    // ── 缓存管理 ──

    private bool CanClearCoverImageCache => !IsClearingCoverImageCache;

    [RelayCommand(CanExecute = nameof(CanClearCoverImageCache))]
    private async Task ClearCoverImageCacheAsync()
    {
        await _coverImageCacheDisplay.ClearAsync(
            value => IsClearingCoverImageCache = value,
            RefreshCoverImageCacheDisplay,
            RefreshCoverImageCacheSize);
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
        !_isDisposed && Volatile.Read(ref _searchOperationId) == operationId;

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
        !_isDisposed && Volatile.Read(ref _voiceIdOperationId) == operationId;

    private void CompleteVoiceIdOperation(CancellationTokenSource cts)
    {
        if (ReferenceEquals(_voiceIdCancellationTokenSource, cts))
            _voiceIdCancellationTokenSource = null;
    }

    private bool TryBeginDisplayPreviewOperation(
        out long operationId,
        out CancellationTokenSource cts,
        out CancellationToken cancellationToken)
    {
        lock (_displayPreviewOperationGate)
        {
            if (_displayPreviewCancellationTokenSource is not null)
            {
                operationId = ++_displayPreviewOperationId;
                cts = null!;
                cancellationToken = default;
                CancelCurrentDisplayPreviewOperationLocked();
                return false;
            }

            operationId = ++_displayPreviewOperationId;
            cts = new CancellationTokenSource();
            cancellationToken = cts.Token;
            _displayPreviewCancellationTokenSource = cts;
            return true;
        }
    }

    private bool IsCurrentDisplayPreviewOperation(long operationId, string voiceId) =>
        !_isDisposed
        && Volatile.Read(ref _displayPreviewOperationId) == operationId
        && string.Equals(VoiceId, voiceId, StringComparison.Ordinal)
        && string.Equals(_settings.VoiceId, voiceId, StringComparison.Ordinal);

    private void CompleteDisplayPreviewOperation(CancellationTokenSource cts)
    {
        lock (_displayPreviewOperationGate)
        {
            if (!ReferenceEquals(_displayPreviewCancellationTokenSource, cts))
                return;

            _displayPreviewCancellationTokenSource = null;
            cts.Dispose();
        }
    }

    private void CancelDisplayPreviewOperation()
    {
        lock (_displayPreviewOperationGate)
        {
            ++_displayPreviewOperationId;
            CancelCurrentDisplayPreviewOperationLocked();
        }
    }

    private void CancelCurrentDisplayPreviewOperationLocked()
    {
        var cancellation = _displayPreviewCancellationTokenSource;
        _displayPreviewCancellationTokenSource = null;
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

    private void RefreshCoverImageCacheDisplay()
    {
        RefreshCoverImageCacheSize();

        foreach (var item in SearchResults)
            item.CoverUrl = FishAudioApi.BuildCoverUrl(item.CoverImage);

        if (!string.IsNullOrEmpty(CachedVoiceId) && _settings.CachedVoice is not null)
            CachedVoiceCoverUrl = FishAudioApi.BuildCoverUrl(_settings.CachedVoice.CoverImage, 128);
    }

    private string ResolveCoverImageUrl(string voiceId, string coverImage, int displayWidth, Action<string>? onCacheReady = null)
    {
        return _coverImageCacheDisplay.ResolveCoverImageUrl(
            voiceId,
            coverImage,
            displayWidth,
            onCacheReady,
            RefreshCoverImageCacheSize);
    }

    private Task<Stream> DownloadCoverImageAsync(string url, CancellationToken ct) =>
        _context.HttpService.GetAsStreamAsync(url, new Options { Timeout = CoverImageDownloadTimeout }, ct);

    private void RefreshCoverImageCacheSize()
    {
        CoverImageCacheSizeText = _coverImageCacheDisplay.GetFormattedCacheSize();
    }

    private static void RunOnUiThread(Action action)
    {
        if (UiThreadInvokerOverride is not null)
        {
            UiThreadInvokerOverride(action);
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }

    // ── 属性变更 → 自动保存 ──

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApiKey))
        {
            _settings.ApiKey = ApiKey;
            SettingsStore.Save(_context, _settings);
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(VoiceId):
                _settings.VoiceId = VoiceId;
                CancelDisplayPreviewOperation();
                if (_previewPlayback.PreviewingVoiceId is not null
                    && !string.Equals(_previewPlayback.PreviewingVoiceId, VoiceId, StringComparison.Ordinal))
                {
                    _previewPlayback.Stop();
                }
                break;
            case nameof(SelectedModel):            _settings.SelectedModel = SelectedModel; break;
            case nameof(Speed):                    _settings.Speed = Speed; break;
            case nameof(Volume):                   _settings.Volume = Volume; break;
            case nameof(NormalizeLoudness):         _settings.NormalizeLoudness = NormalizeLoudness; break;
            case nameof(Temperature):              _settings.Temperature = Temperature; break;
            case nameof(TopP):                     _settings.TopP = TopP; break;
            case nameof(SelectedLatency):          _settings.Latency = SelectedLatency; break;
            case nameof(Normalize):                _settings.Normalize = Normalize; break;
            case nameof(Mp3Bitrate):               _settings.Mp3Bitrate = Mp3Bitrate; break;
            case nameof(ConditionOnPreviousChunks): _settings.ConditionOnPreviousChunks = ConditionOnPreviousChunks; break;
            case nameof(IsS21ProFreePromoDismissed): _settings.IsS21ProFreePromoDismissed = IsS21ProFreePromoDismissed; break;
            default: return;
        }

        if (!_suppressSettingsSave)
            SettingsStore.Save(_context, _settings);
    }

    public void Dispose()
    {
        _isDisposed = true;
        PropertyChanged -= OnPropertyChanged;
        Interlocked.Increment(ref _searchOperationId);
        Interlocked.Increment(ref _voiceIdOperationId);
        CancelDisplayPreviewOperation();
        IsSearching = false;
        IsSubmittingVoiceId = false;
        var searchCancellation = _searchCancellationTokenSource;
        var voiceIdCancellation = _voiceIdCancellationTokenSource;
        _searchCancellationTokenSource = null;
        _voiceIdCancellationTokenSource = null;
        searchCancellation?.Cancel();
        voiceIdCancellation?.Cancel();
        _previewPlayback.Dispose();
        _latencyHideTimer?.Stop();
        SettingsStore.Save(_context, _settings);
    }
}

public partial class VoiceSearchItem : ObservableObject
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string AuthorName { get; set; } = "";
    [ObservableProperty]
    public partial string CoverUrl { get; set; }
    public int TaskCount { get; set; }
    public string? SampleAudioUrl { get; set; }
    public string CoverImage { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBeingPreviewed { get; set; }

    [ObservableProperty]
    public partial double PreviewProgress { get; set; }
}
