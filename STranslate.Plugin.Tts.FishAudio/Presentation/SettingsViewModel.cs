using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.Presentation;
using STranslate.Plugin.Tts.FishAudio.Runtime;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace STranslate.Plugin.Tts.FishAudio.ViewModel;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    internal const string PreviewUnavailableKey = PreviewRefreshCoordinator.PreviewUnavailableKey;
    internal const string PreviewPlaybackFailedKey = "STranslate_Plugin_Tts_FishAudio_Preview_PlaybackFailed";
    internal const string PreviewRefreshFailedKey = PreviewRefreshCoordinator.PreviewRefreshFailedKey;

    private readonly IPluginContext _context;
    private readonly Settings _settings;
    private readonly SettingsWriteLease? _settingsWriteLease;
    private readonly PreviewPlaybackController _previewPlayback;
    private readonly PreviewRefreshCoordinator _previewRefreshCoordinator;
    private readonly CoverImageCacheDisplayManager _coverImageCacheDisplay;
    private readonly CreditRefreshCoordinator _creditRefreshCoordinator;
    private readonly VoiceDiscoveryCoordinator _voiceDiscoveryCoordinator;

    private const int ClearCoverImageCacheTimeoutSeconds = 10;
    private static readonly TimeSpan CoverImageDownloadTimeout = TimeSpan.FromSeconds(10);

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

    public bool IsNormalizeLoudnessEnabled => FishAudioModelPolicy.SupportsNormalizeLoudness(SelectedModel);

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
    private bool _isDisposed;
    private bool _suppressSettingsSave;

    // ── 分页输入 ──

    [ObservableProperty]
    public partial string PageInput { get; set; }

    // ── 缓存 ──

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCoverImageCacheCommand))]
    public partial bool IsClearingCoverImageCache { get; set; }

    [ObservableProperty]
    public partial string CoverImageCacheSizeText { get; set; }

    // ── 静态选项 ──

    public IReadOnlyList<string> Models { get; private set; }
    public static IReadOnlyList<string> Latencies { get; } = SettingsNormalizer.Latencies;
    public static IReadOnlyList<int> Mp3Bitrates { get; } = SettingsNormalizer.Mp3Bitrates;

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
        Func<IPreviewAudioPlayer>? previewAudioPlayerFactory = null,
        StartupCreditRefreshCycle? startupCreditRefreshCycle = null,
        SettingsWriteLease? settingsWriteLease = null)
    {
        _context = context;
        _settings = settings;
        _settingsWriteLease = settingsWriteLease;
        _creditRefreshCoordinator = new CreditRefreshCoordinator(
            context,
            settings,
            value => IsApiKeyInputLocked = value,
            value => IsLoadingCredit = value,
            value => UserCredit = value,
            (text, brush) =>
            {
                LatencyText = text;
                LatencyBrush = brush;
            },
            RunOnUiThread,
            () => _isDisposed);
        var modelPolicyTime = nowUtc ?? FishAudioClock.LocalUtcNow();
        Models = FishAudioModelPolicy.GetAvailableModels(modelPolicyTime);
        IsS21ProFreeAvailable = FishAudioModelPolicy.IsS21ProFreeAvailable(modelPolicyTime);
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
        _voiceDiscoveryCoordinator = new VoiceDiscoveryCoordinator(
            context: context,
            getSearchQuery: () => SearchQuery,
            getSearchPage: () => SearchPage,
            getSearchTotalPages: () => SearchTotalPages,
            getPageInput: () => PageInput,
            getVoiceIdInput: () => VoiceIdInput,
            setSearching: value => IsSearching = value,
            setSearchPage: value => SearchPage = value,
            setPageInput: value => PageInput = value,
            setHasSearched: value => HasSearched = value,
            setSearchResultCount: value => SearchResultCount = value,
            setSearchTotalPages: value => SearchTotalPages = value,
            setSearchResults: value => SearchResults = value,
            resolveCoverImageUrl: ResolveCoverImageUrl,
            syncPreviewStateToResults: SyncPreviewStateToResults,
            setVoiceIdWithoutSaving: SetVoiceIdWithoutSaving,
            persistSelectedVoice: cached => UpdateSettingsAndSave(settings =>
            {
                settings.VoiceId = VoiceId;
                settings.CachedVoice = cached;
            }),
            applyCachedVoice: ApplyCachedVoice,
            setVoiceIdError: value => VoiceIdError = value,
            setSubmittingVoiceId: value => IsSubmittingVoiceId = value,
            isFacadeDisposed: () => _isDisposed);
        _previewRefreshCoordinator = new PreviewRefreshCoordinator(
            context: context,
            getCachedVoiceId: () => CachedVoiceId,
            getCachedVoiceSampleUrl: () => CachedVoiceSampleUrl,
            getVoiceId: () => VoiceId,
            getSettingsVoiceId: () => _settings.VoiceId,
            getSearchResults: () => SearchResults,
            getPreviewingVoiceId: () => _previewPlayback.PreviewingVoiceId,
            stopPreview: _previewPlayback.Stop,
            togglePreview: _previewPlayback.Toggle,
            updateSettingsAndSave: update => UpdateSettingsAndSave(update),
            applyCachedVoice: ApplyCachedVoice,
            resolveCoverImageUrl: ResolveCoverImageUrl,
            isFacadeDisposed: () => _isDisposed);

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
        if (startupCreditRefreshCycle is not null)
            AttachStartupCreditRefresh(startupCreditRefreshCycle);
    }

    // ── API Key ──

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
    private Task RefreshCreditAsync() => _creditRefreshCoordinator.RefreshCreditAsync();

    internal Task RefreshCreditSilentlyAsync() => _creditRefreshCoordinator.RefreshCreditSilentlyAsync();

    internal void BeginApiKeyOperation()
    {
        _creditRefreshCoordinator.BeginApiKeyOperation();
    }

    internal void EndApiKeyOperation()
    {
        _creditRefreshCoordinator.EndApiKeyOperation();
    }

    internal void AttachStartupCreditRefresh(StartupCreditRefreshCycle startupCreditRefreshCycle)
    {
        _creditRefreshCoordinator.AttachStartupCreditRefresh(startupCreditRefreshCycle);
    }

    // ── 选择模式切换 ──

    internal void ApplyAvailableModels(DateTimeOffset nowUtc)
    {
        RunOnUiThread(() =>
        {
            if (_isDisposed)
                return;

            Models = FishAudioModelPolicy.GetAvailableModels(nowUtc);
            OnPropertyChanged(nameof(Models));
            IsS21ProFreeAvailable = FishAudioModelPolicy.IsS21ProFreeAvailable(nowUtc);

            if (!Models.Contains(SelectedModel, StringComparer.Ordinal))
            {
                _suppressSettingsSave = true;
                try
                {
                    SelectedModel = FishAudioModelPolicy.GetDefaultModel(nowUtc);
                }
                finally
                {
                    _suppressSettingsSave = false;
                }
            }
        });
    }

    internal static CachedVoiceInfo CreateCachedVoiceInfo(ModelEntity model) =>
        VoiceDiscoveryCoordinator.CreateCachedVoiceInfo(model);

    internal static bool TryApplyRefreshedCachedVoice(
        Settings settings,
        string expectedVoiceId,
        CachedVoiceInfo cached) =>
        PreviewRefreshCoordinator.TryApplyRefreshedCachedVoice(settings, expectedVoiceId, cached);

    internal void ApplyRefreshedCachedVoice(string expectedVoiceId, CachedVoiceInfo cached)
    {
        RunOnUiThread(() =>
        {
            if (_isDisposed
                || !string.Equals(VoiceId, expectedVoiceId, StringComparison.Ordinal)
                || !string.Equals(_settings.VoiceId, expectedVoiceId, StringComparison.Ordinal))
            {
                return;
            }

            ApplyCachedVoice(cached);
        });
    }

    // ── 限时推广 ──

    [RelayCommand]
    private void DismissS21ProFreePromo()
    {
        IsS21ProFreePromoDismissed = true;
    }

    [RelayCommand]
    private void UseS21ProFreePromo()
    {
        if (!IsS21ProFreeAvailable)
            return;

        SelectedModel = FishAudioModelPolicy.S21ProFreeModel;
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
    private Task SearchVoicesAsync() => _voiceDiscoveryCoordinator.SearchVoicesAsync();

    [RelayCommand]
    private Task NextPageAsync() => _voiceDiscoveryCoordinator.NextPageAsync();

    [RelayCommand]
    private Task PrevPageAsync() => _voiceDiscoveryCoordinator.PrevPageAsync();

    [RelayCommand]
    private Task CommitPageInputAsync() => _voiceDiscoveryCoordinator.CommitPageInputAsync();

    [RelayCommand]
    private void SelectVoice(VoiceSearchItem? item) => _voiceDiscoveryCoordinator.SelectVoice(item);

    // ── 通过 ID 选择 ──

    private bool CanSubmitVoiceId => true;

    [RelayCommand(CanExecute = nameof(CanSubmitVoiceId), AllowConcurrentExecutions = true)]
    private Task SubmitVoiceIdAsync() => _voiceDiscoveryCoordinator.SubmitVoiceIdAsync();

    // ── 试听系统 ──

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ToggleDisplayPreviewAsync() => _previewRefreshCoordinator.ToggleDisplayPreviewAsync();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ToggleSearchItemPreviewAsync(VoiceSearchItem? item) =>
        _previewRefreshCoordinator.ToggleSearchItemPreviewAsync(item);

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

        var key = FishAudioRequestPolicy.IsNetworkAvailable()
            ? PreviewPlaybackFailedKey
            : FishAudioRequestPolicy.NetworkUnavailableKey;
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
        SetVoiceIdWithoutSaving("");
        if (!UpdateSettingsAndSave(settings =>
            {
                settings.VoiceId = "";
                settings.CachedVoice = null;
            }))
        {
            return;
        }

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
        Action<Settings> updateSettings;
        switch (e.PropertyName)
        {
            case nameof(ApiKey):
                updateSettings = settings => settings.ApiKey = ApiKey;
                break;
            case nameof(VoiceId):
                _previewRefreshCoordinator.CancelDisplayPreviewOperation();
                if (_previewPlayback.PreviewingVoiceId is not null
                    && !string.Equals(_previewPlayback.PreviewingVoiceId, VoiceId, StringComparison.Ordinal))
                {
                    _previewPlayback.Stop();
                }
                updateSettings = settings => settings.VoiceId = VoiceId;
                break;
            case nameof(SelectedModel): updateSettings = settings => settings.SelectedModel = SelectedModel; break;
            case nameof(Speed): updateSettings = settings => settings.Speed = Speed; break;
            case nameof(Volume): updateSettings = settings => settings.Volume = Volume; break;
            case nameof(NormalizeLoudness): updateSettings = settings => settings.NormalizeLoudness = NormalizeLoudness; break;
            case nameof(Temperature): updateSettings = settings => settings.Temperature = Temperature; break;
            case nameof(TopP): updateSettings = settings => settings.TopP = TopP; break;
            case nameof(SelectedLatency): updateSettings = settings => settings.Latency = SelectedLatency; break;
            case nameof(Normalize): updateSettings = settings => settings.Normalize = Normalize; break;
            case nameof(Mp3Bitrate): updateSettings = settings => settings.Mp3Bitrate = Mp3Bitrate; break;
            case nameof(ConditionOnPreviousChunks): updateSettings = settings => settings.ConditionOnPreviousChunks = ConditionOnPreviousChunks; break;
            case nameof(IsS21ProFreePromoDismissed): updateSettings = settings => settings.IsS21ProFreePromoDismissed = IsS21ProFreePromoDismissed; break;
            default: return;
        }

        if (!_suppressSettingsSave)
            UpdateSettingsAndSave(updateSettings);
    }

    private bool UpdateSettingsAndSave(Action<Settings> update) =>
        UpdateSettingsAndSave(candidate =>
        {
            update(candidate);
            return true;
        });

    private bool UpdateSettingsAndSave(Func<Settings, bool> update)
    {
        if (_settingsWriteLease is { } writeLease)
        {
            return SettingsStore.TryUpdateAndSave(
                _context,
                _settings,
                writeLease,
                update);
        }

        if (!update(_settings))
            return false;

        SettingsStore.Save(_context, _settings);
        return true;
    }

    private void SetVoiceIdWithoutSaving(string voiceId)
    {
        _suppressSettingsSave = true;
        try
        {
            VoiceId = voiceId;
        }
        finally
        {
            _suppressSettingsSave = false;
        }
    }

    public void Dispose()
    {
        _isDisposed = true;
        _creditRefreshCoordinator.InvalidateStartupCreditRefresh();
        PropertyChanged -= OnPropertyChanged;
        _voiceDiscoveryCoordinator.InvalidateOperations();
        _previewRefreshCoordinator.Dispose();
        IsSearching = false;
        IsSubmittingVoiceId = false;
        _voiceDiscoveryCoordinator.Dispose();
        _previewPlayback.Dispose();
        _creditRefreshCoordinator.Dispose();
    }
}
