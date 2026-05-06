using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.Service;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace STranslate.Plugin.Tts.FishAudio.ViewModel;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IPluginContext _context;
    private readonly Settings _settings;
    private readonly CoverImageCacheService _coverImageCache;
    private readonly Func<Task> _clearCoverImageCacheAsync;
    private readonly TimeSpan _clearCoverImageCacheTimeout;

    private const long LatencyGoodMs = 300;
    private const long LatencyFairMs = 800;
    private const int ClearCoverImageCacheTimeoutSeconds = 10;

    private static readonly SolidColorBrush BrushGood = new(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly SolidColorBrush BrushFair = new(Color.FromRgb(0xFF, 0x98, 0x00));
    private static readonly SolidColorBrush BrushPoor = new(Color.FromRgb(0xF4, 0x43, 0x36));

    // ── API ──

    [ObservableProperty]
    public partial string ApiKey { get; set; }

    [ObservableProperty]
    public partial bool IsApiKeyValid { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmApiKeyCommand))]
    public partial bool IsValidatingApiKey { get; set; }

    [ObservableProperty]
    public partial string? ApiKeyStatusText { get; set; }

    [ObservableProperty]
    public partial ApiKeyStatusKind ApiKeyStatusKind { get; set; }

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
    [NotifyPropertyChangedFor(nameof(ShowNormalizeLoudness))]
    public partial string SelectedModel { get; set; }

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

    public bool ShowNormalizeLoudness => SelectedModel == "s2-pro";

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

    private MediaPlayer? _previewPlayer;
    private DispatcherTimer? _previewTimer;
    private VoiceSearchItem? _previewingSearchItem;
    private long _clearCoverImageCacheOperationId;

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

    public static IReadOnlyList<string> Models { get; } = ["s2-pro", "s1"];
    public static IReadOnlyList<string> Latencies { get; } = ["normal", "balanced", "low"];
    public static IReadOnlyList<int> Mp3Bitrates { get; } = [64, 128, 192];

    private const int SearchPageSize = 6;

    public SettingsViewModel(IPluginContext context, Settings settings, Task<(WalletCreditResponse?, long)>? pendingCreditTask)
        : this(context, settings, pendingCreditTask, null, null)
    {
    }

    internal SettingsViewModel(
        IPluginContext context,
        Settings settings,
        Task<(WalletCreditResponse?, long)>? pendingCreditTask,
        Func<Task>? clearCoverImageCacheAsync,
        TimeSpan? clearCoverImageCacheTimeout)
    {
        _context = context;
        _settings = settings;
        _coverImageCache = new CoverImageCacheService(
            context.MetaData?.PluginCacheDirectoryPath,
            DownloadCoverImageAsync);
        _clearCoverImageCacheAsync = clearCoverImageCacheAsync ?? (() => Task.Run(_coverImageCache.Clear));
        _clearCoverImageCacheTimeout = clearCoverImageCacheTimeout ?? TimeSpan.FromSeconds(ClearCoverImageCacheTimeoutSeconds);

        ApiKey = settings.ApiKey;
        VoiceId = settings.VoiceId;
        SelectedModel = settings.SelectedModel;
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
        CoverImageCacheSizeText = _coverImageCache.GetFormattedCacheSize();

        ApplyCachedVoice(settings.CachedVoice);

        PropertyChanged += OnPropertyChanged;

        if (pendingCreditTask is not null && Settings.IsValidApiKeyFormat(ApiKey))
            _ = ApplyPendingCreditAsync(pendingCreditTask);
    }

    // ── API Key 验证 ──

    private bool CanConfirmApiKey => !IsValidatingApiKey;

    [RelayCommand(CanExecute = nameof(CanConfirmApiKey))]
    private async Task ConfirmApiKeyAsync()
    {
        var key = ApiKey;
        ClearAppliedApiKeyState();

        if (string.IsNullOrEmpty(key))
        {
            ApiKeyStatusText = _context.GetTranslation("STranslate_Plugin_Tts_FishAudio_ApiKey_Empty");
            ApiKeyStatusKind = ApiKeyStatusKind.Error;
            _settings.ApiKey = "";
            _context.SaveSettingStorage<Settings>();
            return;
        }

        if (!Settings.IsValidApiKeyFormat(key))
        {
            ApiKeyStatusText = _context.GetTranslation("STranslate_Plugin_Tts_FishAudio_ApiKey_InvalidFormat");
            ApiKeyStatusKind = ApiKeyStatusKind.Error;
            _settings.ApiKey = "";
            _context.SaveSettingStorage<Settings>();
            return;
        }

        IsValidatingApiKey = true;
        ApiKeyStatusText = null;
        ApiKeyStatusKind = ApiKeyStatusKind.Waiting;

        try
        {
            var (result, _) = await FishAudioApi.GetCreditAsync(_context, key, CancellationToken.None);

            if (ApiKey != key)
            {
                IsApiKeyValid = false;
                ApiKeyStatusKind = ApiKeyStatusKind.None;
                UserCredit = "";
                return;
            }

            if (result is not null)
            {
                IsApiKeyValid = true;
                ApiKeyStatusText = null;
                ApiKeyStatusKind = ApiKeyStatusKind.Success;
                ApplyCreditResult(result);
                _settings.ApiKey = key;
                _context.SaveSettingStorage<Settings>();
            }
            else
            {
                ApiKeyStatusKind = ApiKeyStatusKind.None;
                _settings.ApiKey = "";
                _context.SaveSettingStorage<Settings>();
                _context.Snackbar.ShowError(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_ApiKey_Invalid"));
            }
        }
        catch (Exception ex)
        {
            if (ApiKey != key)
            {
                IsApiKeyValid = false;
                ApiKeyStatusKind = ApiKeyStatusKind.None;
                UserCredit = "";
                return;
            }

            ApiKeyStatusKind = ApiKeyStatusKind.None;
            _settings.ApiKey = "";
            _context.SaveSettingStorage<Settings>();
            _context.Snackbar.ShowError(ex.Message);
        }
        finally
        {
            IsValidatingApiKey = false;
        }
    }

    private string EffectiveAppliedApiKey => IsApiKeyValid ? _settings.ApiKey : "";
    private string EffectiveApiKeyForSearch => IsApiKeyValid ? _settings.ApiKey : "dummy";

    [RelayCommand]
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
        if (!IsApiKeyValid)
        {
            _context.Snackbar.ShowError(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_ApiKey_Empty"));
            return;
        }
        await FetchCreditAsync(showError: true, showLatency: true);
    }

    internal async Task RefreshCreditSilentlyAsync()
    {
        if (!IsApiKeyValid) return;
        await FetchCreditAsync(showError: false, showLatency: false);
    }

    private async Task ApplyPendingCreditAsync(Task<(WalletCreditResponse?, long)> task)
    {
        try
        {
            var (result, _) = await task;
            if (result is not null)
            {
                IsApiKeyValid = true;
                ApiKeyStatusText = null;
                ApiKeyStatusKind = ApiKeyStatusKind.Success;
                ApplyCreditResult(result);
            }
        }
        catch
        {
            // Startup credit check is silent for failures; success still updates visible account state.
        }
    }

    private async Task FetchCreditAsync(bool showError, bool showLatency)
    {
        IsLoadingCredit = true;
        if (showLatency)
        {
            LatencyText = "";
            LatencyBrush = null;
        }

        try
        {
            var (result, ms) = await FishAudioApi.GetCreditAsync(_context, EffectiveAppliedApiKey, CancellationToken.None);
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
            if (showError)
                _context.Snackbar.ShowError(ex.Message);
            if (showLatency)
            {
                LatencyText = "";
                LatencyBrush = null;
            }
        }
        finally
        {
            IsLoadingCredit = false;
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

    private bool CanSearchVoices => !IsSearching;

    [RelayCommand(CanExecute = nameof(CanSearchVoices))]
    private async Task SearchVoicesAsync()
    {
        SearchPage = 1;
        PageInput = "1";
        await ExecuteSearchAsync();
    }

    private async Task ExecuteSearchAsync()
    {
        IsSearching = true;
        try
        {
            var response = await FishAudioApi.SearchModelsAsync(
                _context, EffectiveApiKeyForSearch, SearchQuery, SearchPageSize, SearchPage, CancellationToken.None);

            if (response is null)
            {
                SearchResults = [];
                SearchTotalPages = 1;
                SearchResultCount = 0;
                return;
            }

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
        catch (Exception ex)
        {
            _context.Snackbar.ShowError(ex.Message);
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (SearchPage < SearchTotalPages)
        {
            SearchPage++;
            PageInput = SearchPage.ToString();
            await ExecuteSearchAsync();
        }
    }

    [RelayCommand]
    private async Task PrevPageAsync()
    {
        if (SearchPage > 1)
        {
            SearchPage--;
            PageInput = SearchPage.ToString();
            await ExecuteSearchAsync();
        }
    }

    [RelayCommand]
    private async Task CommitPageInputAsync()
    {
        if (int.TryParse(PageInput, out var n) && n >= 1 && n <= SearchTotalPages && n != SearchPage)
        {
            SearchPage = n;
            await ExecuteSearchAsync();
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
        _context.SaveSettingStorage<Settings>();

        ApplyCachedVoice(cached);
    }

    // ── 通过 ID 选择 ──

    private bool CanSubmitVoiceId => !IsSubmittingVoiceId;

    [RelayCommand(CanExecute = nameof(CanSubmitVoiceId))]
    private async Task SubmitVoiceIdAsync()
    {
        var trimmed = VoiceIdInput.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            VoiceIdError = _context.GetTranslation("STranslate_Plugin_Tts_FishAudio_VoiceId_Empty");
            return;
        }

        if (!Settings.IsValidVoiceIdFormat(trimmed))
        {
            VoiceIdError = _context.GetTranslation("STranslate_Plugin_Tts_FishAudio_VoiceId_InvalidFormat");
            return;
        }

        IsSubmittingVoiceId = true;
        VoiceIdError = null;

        try
        {
            var model = await FishAudioApi.GetModelAsync(_context, EffectiveApiKeyForSearch, trimmed, CancellationToken.None);

            if (model is null)
            {
                VoiceIdError = _context.GetTranslation("STranslate_Plugin_Tts_FishAudio_Voice_NotFound");
                return;
            }

            VoiceId = trimmed;
            _settings.VoiceId = VoiceId;

            var cached = new CachedVoiceInfo
            {
                Title = model.Title,
                Description = model.Description,
                CoverImage = model.CoverImage,
                AuthorName = model.Author?.Nickname ?? "",
                TaskCount = model.TaskCount,
                SampleAudioUrl = model.Samples.FirstOrDefault()?.Audio,
            };
            _settings.CachedVoice = cached;
            _context.SaveSettingStorage<Settings>();

            ApplyCachedVoice(cached);
            VoiceIdError = null;
        }
        catch (Exception ex)
        {
            VoiceIdError = ex.Message;
        }
        finally
        {
            IsSubmittingVoiceId = false;
        }
    }

    // ── 试听系统 ──

    [RelayCommand]
    private void ToggleDisplayPreview()
    {
        if (string.IsNullOrEmpty(CachedVoiceId) || string.IsNullOrEmpty(CachedVoiceSampleUrl))
            return;
        TogglePreview(CachedVoiceId, CachedVoiceSampleUrl);
    }

    [RelayCommand]
    private void ToggleSearchItemPreview(VoiceSearchItem? item)
    {
        if (item is null || string.IsNullOrEmpty(item.SampleAudioUrl))
            return;
        TogglePreview(item.Id, item.SampleAudioUrl);
    }

    private void TogglePreview(string voiceId, string audioUrl)
    {
        if (PreviewingVoiceId == voiceId)
            StopPreview();
        else
            StartPreview(voiceId, audioUrl);
    }

    private void StartPreview(string voiceId, string audioUrl)
    {
        StopPreview();

        PreviewingVoiceId = voiceId;
        PreviewProgress = 0;
        UpdatePreviewState();

        _previewPlayer = new MediaPlayer { Volume = 1.0 };
        _previewPlayer.MediaOpened += OnMediaOpened;
        _previewPlayer.MediaEnded += OnMediaEnded;
        _previewPlayer.MediaFailed += OnMediaFailed;
        _previewPlayer.Open(new Uri(audioUrl));
        _previewPlayer.Play();
    }

    private void StopPreview()
    {
        _previewTimer?.Stop();
        _previewTimer = null;

        if (_previewPlayer is not null)
        {
            _previewPlayer.MediaOpened -= OnMediaOpened;
            _previewPlayer.MediaEnded -= OnMediaEnded;
            _previewPlayer.MediaFailed -= OnMediaFailed;
            _previewPlayer.Stop();
            _previewPlayer.Close();
            _previewPlayer = null;
        }

        PreviewingVoiceId = null;
        PreviewProgress = 0;
        UpdatePreviewState();
    }

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _previewTimer.Tick += OnPreviewTick;
        _previewTimer.Start();
    }

    private void OnMediaEnded(object? sender, EventArgs e) => StopPreview();
    private void OnMediaFailed(object? sender, ExceptionEventArgs e) => StopPreview();

    private void OnPreviewTick(object? sender, EventArgs e)
    {
        if (_previewPlayer?.NaturalDuration.HasTimeSpan != true) return;

        var duration = _previewPlayer.NaturalDuration.TimeSpan.TotalMilliseconds;
        var position = _previewPlayer.Position.TotalMilliseconds;
        PreviewProgress = duration > 0 ? Math.Min(1.0, position / duration) : 0;

        if (_previewingSearchItem is not null)
            _previewingSearchItem.PreviewProgress = PreviewProgress;
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
        _context.SaveSettingStorage<Settings>();
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
        var operationId = Interlocked.Increment(ref _clearCoverImageCacheOperationId);
        IsClearingCoverImageCache = true;

        Task clearTask;
        try
        {
            clearTask = _clearCoverImageCacheAsync();
        }
        catch (Exception ex)
        {
            CompleteCurrentCoverImageCacheClear(operationId);
            _context.Snackbar.ShowError(ex.Message);
            return;
        }

        var completedTask = await Task.WhenAny(clearTask, Task.Delay(_clearCoverImageCacheTimeout));
        if (!ReferenceEquals(completedTask, clearTask))
        {
            if (IsCurrentCoverImageCacheClear(operationId))
            {
                RefreshCoverImageCacheSize();
                IsClearingCoverImageCache = false;
                _context.Snackbar.ShowError(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_ClearCache_Timeout"));
            }

            _ = clearTask.ContinueWith(task =>
            {
                _ = task.Exception;
                RunOnUiThread(RefreshCoverImageCacheSize);
            }, TaskScheduler.Default);
            return;
        }

        try
        {
            await clearTask;
        }
        catch (Exception ex)
        {
            if (IsCurrentCoverImageCacheClear(operationId))
                _context.Snackbar.ShowError(ex.Message);
        }
        finally
        {
            if (IsCurrentCoverImageCacheClear(operationId))
                CompleteCurrentCoverImageCacheClear(operationId);
            else
                RefreshCoverImageCacheSize();
        }
    }

    private bool IsCurrentCoverImageCacheClear(long operationId) =>
        Volatile.Read(ref _clearCoverImageCacheOperationId) == operationId;

    private void CompleteCurrentCoverImageCacheClear(long operationId)
    {
        RefreshCoverImageCacheDisplay();
        if (IsCurrentCoverImageCacheClear(operationId))
            IsClearingCoverImageCache = false;
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
        var result = _coverImageCache.ResolveCoverImageUrl(voiceId, coverImage, displayWidth, localUrl =>
        {
            RunOnUiThread(() =>
            {
                onCacheReady?.Invoke(localUrl);
                RefreshCoverImageCacheSize();
            });
        });
        return result.DisplayUrl;
    }

    private Task<byte[]> DownloadCoverImageAsync(string url, CancellationToken ct) =>
        _context.HttpService.GetAsBytesAsync(url, ct);

    private void RefreshCoverImageCacheSize()
    {
        CoverImageCacheSizeText = _coverImageCache.GetFormattedCacheSize();
    }

    private static void RunOnUiThread(Action action)
    {
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
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(VoiceId):                  _settings.VoiceId = VoiceId; break;
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
            default: return;
        }

        _context.SaveSettingStorage<Settings>();
    }

    public void Dispose()
    {
        PropertyChanged -= OnPropertyChanged;
        StopPreview();
        _latencyHideTimer?.Stop();
        _context.SaveSettingStorage<Settings>();
    }

    private void ClearAppliedApiKeyState()
    {
        IsApiKeyValid = false;
        ApiKeyStatusText = null;
        ApiKeyStatusKind = ApiKeyStatusKind.None;
        UserCredit = "";
    }
}

public enum ApiKeyStatusKind
{
    None,
    Waiting,
    Success,
    Error,
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
