using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Caching;
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
    private readonly Action<string> _copyVoiceIdToClipboard;

    private const int ClearCoverImageCacheTimeoutSeconds = 10;
    private static readonly TimeSpan CoverImageDownloadTimeout = TimeSpan.FromSeconds(10);

    internal static Action<Action>? UiThreadInvokerOverride { get; set; }

    // ── API ──

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearApiKeyCommand))]
    public partial string ApiKey { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PasteApiKeyCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearApiKeyCommand))]
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

    [ObservableProperty]
    public partial IReadOnlyList<string> CachedVoiceLanguages { get; set; } = Array.Empty<string>();

    [ObservableProperty]
    public partial DateTimeOffset? CachedVoiceCreatedAt { get; set; }

    // ── 韵律 ──

    [ObservableProperty]
    public partial double Speed { get; set; }

    private double _volume;
    public double Volume
    {
        get => _volume;
        set
        {
            // Canonicalize before SetProperty's equality check and auto-save;
            // WPF may publish its uncoerced value through a two-way binding.
            var canonical = value is > -.005 and < .005
                && SettingsNormalizer.RoundToPrecision((decimal)value, 2) == 0d ? 0d : value;
            SetProperty(ref _volume, canonical);
        }
    }

    [ObservableProperty]
    public partial bool NormalizeLoudness { get; set; }

    public bool IsNormalizeLoudnessEnabled => FishAudioModelPolicy.SupportsNormalizeLoudness(SelectedModel);

    // ── 音频输出 ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMp3BitrateEnabled))]
    public partial string AudioFormat { get; set; }

    public bool IsMp3BitrateEnabled => string.Equals(AudioFormat, "mp3", StringComparison.Ordinal);

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
    [NotifyCanExecuteChangedFor(nameof(ClearSearchQueryCommand))]
    public partial string SearchQuery { get; set; }

    public IReadOnlyList<string> SearchLanguages { get; private set; } = Array.AsReadOnly(Array.Empty<string>());
    public string SearchSort { get; private set; } = "score";
    public bool HasSearchAdjustments => SearchLanguages.Count != 0 || SearchSort != "score";

    internal Task ApplySearchOptionsAsync(IEnumerable<string>? languages, string sort) =>
        _voiceDiscoveryCoordinator.ApplySearchOptionsAsync(languages, sort);

    private void ApplySearchOptions(VoiceSearchOptions options)
    {
        SearchLanguages = options.Languages;
        SearchSort = options.SortParameter;
        OnPropertyChanged(nameof(SearchLanguages));
        OnPropertyChanged(nameof(SearchSort));
        OnPropertyChanged(nameof(HasSearchAdjustments));
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchVoicesCommand))]
    public partial bool IsSearching { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearchMode))]
    [NotifyPropertyChangedFor(nameof(IsByIdMode))]
    [NotifyPropertyChangedFor(nameof(IsFavoritesMode))]
    public partial VoiceBrowseMode BrowseMode { get; set; }

    public bool IsSearchMode => BrowseMode == VoiceBrowseMode.Search;
    public bool IsByIdMode => BrowseMode == VoiceBrowseMode.ById;
    public bool IsFavoritesMode => BrowseMode == VoiceBrowseMode.Favorites;

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
    [NotifyPropertyChangedFor(nameof(ShowNextPage))]
    [NotifyPropertyChangedFor(nameof(ShowPagination))]
    public partial bool SearchHasNextPage { get; set; }

    [ObservableProperty]
    public partial string SearchResultCountText { get; set; } = "?";

    [ObservableProperty]
    public partial string SearchPageLimitText { get; set; } = "?";

    [ObservableProperty]
    public partial bool SearchHasUncertainExtent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPagination))]
    public partial bool HasSearched { get; set; }

    public bool ShowPrevPage => SearchPage > 1;
    public bool ShowNextPage => SearchHasNextPage;
    public bool ShowPagination => HasSearched && (SearchPage > 1 || SearchHasNextPage || SearchTotalPages > 1);

    // ── 通过 ID 选择 ──

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearVoiceIdInputCommand))]
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
    [NotifyPropertyChangedFor(nameof(IsDisplayVoicePreviewActive))]
    public partial bool IsDisplayVoicePreviewing { get; set; }

    [ObservableProperty]
    public partial string? PreparingPreviewVoiceId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisplayVoicePreviewActive))]
    public partial bool IsDisplayVoicePreparing { get; set; }

    public bool IsDisplayVoicePreviewActive => IsDisplayVoicePreparing || IsDisplayVoicePreviewing;

    private readonly List<VoiceSearchItem> _previewStateItems = [];
    private bool _isDisposed;
    private bool _suppressSettingsSave;

    // ── 分页输入 ──

    [ObservableProperty]
    public partial string PageInput { get; set; }

    public int SearchPageInputMaximum => _voiceDiscoveryCoordinator.PageInputMaximum;

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
    public static IReadOnlyList<string> AudioFormats { get; } = SettingsNormalizer.AudioFormats;

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
        SettingsWriteLease? settingsWriteLease = null,
        VoiceMetadataCache? voiceMetadataCache = null,
        FavoriteVoiceStore? favoriteVoiceStore = null,
        Action<string>? copyVoiceIdToClipboard = null,
        TimeProvider? voiceIdErrorTimeProvider = null)
    {
        _context = context;
        _settings = settings;
        _settingsWriteLease = settingsWriteLease;
        _copyVoiceIdToClipboard = copyVoiceIdToClipboard ?? Clipboard.SetText;
        _voiceMetadataCache = voiceMetadataCache ?? new VoiceMetadataCache();
        _favoriteStore = favoriteVoiceStore ?? new FavoriteVoiceStore(context.MetaData?.PluginSettingsDirectoryPath);
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
            previewAudioPlayerFactory,
            runOnUiThread: RunOnUiThread);
        _coverImageCacheDisplay = new CoverImageCacheDisplayManager(
            context,
            DownloadCoverImageAsync,
            clearCoverImageCacheAsync,
            clearCoverImageCacheTimeout ?? TimeSpan.FromSeconds(ClearCoverImageCacheTimeoutSeconds),
            RunOnUiThread);
        _voiceDiscoveryCoordinator = new VoiceDiscoveryCoordinator(
            context: context,
            getSearchQuery: () => SearchQuery,
            getPageInput: () => PageInput,
            getVoiceIdInput: () => VoiceIdInput,
            setSearching: value => IsSearching = value,
            setSearchPagination: ApplySearchPagination,
            setPageInput: value => PageInput = value,
            setHasSearched: value => HasSearched = value,
            setSearchResults: value => SearchResults = value,
            resolveCoverImageUrl: ResolveCoverImageUrl,
            syncPreviewStateToResults: SyncPreviewStateToResults,
            setVoiceIdWithoutSaving: SetVoiceIdWithoutSaving,
            persistSelectedVoice: (voiceId, cached) => UpdateSettingsAndSave(settings =>
            {
                settings.VoiceId = voiceId;
                settings.CachedVoice = cached;
            }),
            applyCachedVoice: ApplyCachedVoice,
            setVoiceIdError: value => VoiceIdError = value,
            setSubmittingVoiceId: value => IsSubmittingVoiceId = value,
            isFacadeDisposed: () => _isDisposed,
            setByIdResult: item => ByIdResults = item is null ? [] : [item],
            metadataCache: _voiceMetadataCache, getApiKey: () => _settings.ApiKey,
            timeProvider: voiceIdErrorTimeProvider, runOnUiThread: RunOnUiThread,
            setAppliedSearchOptions: ApplySearchOptions);
        _previewRefreshCoordinator = new PreviewRefreshCoordinator(
            context: context,
            getCachedVoiceId: () => CachedVoiceId,
            getCachedVoiceSampleUrl: () => CachedVoiceSampleUrl,
            getVoiceId: () => VoiceId,
            getSettingsVoiceId: () => _settings.VoiceId,
            getSearchResults: GetVoiceCards,
            getPreviewingVoiceId: () => _previewPlayback.PreviewingVoiceId,
            stopPreview: _previewPlayback.Stop,
            togglePreview: _previewPlayback.Toggle,
            updateSettingsAndSave: update => UpdateSettingsAndSave(update),
            applyCachedVoice: ApplyCachedVoice,
            resolveCoverImageUrl: ResolveCoverImageUrl,
            isFacadeDisposed: () => _isDisposed,
            applyPreparingState: ApplyPreviewPreparingState,
            runOnUiThread: RunOnUiThread,
            metadataCache: _voiceMetadataCache, getApiKey: () => _settings.ApiKey);

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
        AudioFormat = settings.AudioFormat;
        Mp3Bitrate = settings.Mp3Bitrate;
        ConditionOnPreviousChunks = settings.ConditionOnPreviousChunks;

        UserCredit = "";
        LatencyText = "";
        SearchQuery = "";
        SearchResults = [];
        SearchPage = 1;
        PageInput = "1";
        VoiceIdInput = "";
        BrowseMode = VoiceBrowseMode.Search;
        ReloadFavorites();
        CoverImageCacheSizeText = _coverImageCacheDisplay.GetFormattedCacheSize();

        ApplyCachedVoice(settings.CachedVoice);

        PropertyChanged += OnPropertyChanged;
        SubscribeToVoiceMetadataUpdates();
        if (startupCreditRefreshCycle is not null)
            AttachStartupCreditRefresh(startupCreditRefreshCycle);
    }

    // ── API Key ──

    private bool CanClearApiKey => !_isDisposed && !IsApiKeyInputLocked && !string.IsNullOrEmpty(ApiKey);
    private bool CanClearSearchQuery => !_isDisposed && !string.IsNullOrEmpty(SearchQuery);
    private bool CanClearVoiceIdInput => !_isDisposed && !string.IsNullOrEmpty(VoiceIdInput);

    [RelayCommand(CanExecute = nameof(CanClearApiKey))]
    private void ClearApiKey()
    {
        if (CanClearApiKey) ApiKey = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanClearSearchQuery))]
    private void ClearSearchQuery()
    {
        if (CanClearSearchQuery) SearchQuery = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanClearVoiceIdInput))]
    private void ClearVoiceIdInput()
    {
        if (CanClearVoiceIdInput) VoiceIdInput = string.Empty;
    }

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
        if (SettingsValidation.IsValidVoiceIdPaste(text))
            VoiceIdInput = text;
    }

    private bool CanCopyVoiceId(string? voiceId) => !_isDisposed && SettingsValidation.IsValidVoiceIdFormat(voiceId ?? "");

    [RelayCommand(CanExecute = nameof(CanCopyVoiceId))]
    private void CopyVoiceId(string? voiceId)
    {
        if (!CanCopyVoiceId(voiceId)) return;
        try
        {
            _copyVoiceIdToClipboard(voiceId!);
            _context.Snackbar.ShowSuccess(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_CopyVoiceId_Success"));
        }
        catch (Exception exception)
        {
            _context.Logger.LogError(exception, "Failed to copy voice ID to the clipboard.");
            _context.Snackbar.ShowError(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_CopyVoiceId_Failed"));
        }
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

    internal Task? PendingStartupCreditObserverTask => _creditRefreshCoordinator.PendingStartupCreditObserverTask;

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

    internal void ApplyRefreshedCachedVoice(string expectedVoiceId, CachedVoiceInfo cached, string? expectedApiKey = null,
        object? expectedCredentialIdentity = null)
    {
        string apiKey;
        object credentialIdentity;
        lock (_settings)
        {
            apiKey = expectedApiKey ?? _settings.ApiKey;
            credentialIdentity = expectedCredentialIdentity ?? _settings.CredentialIdentity;
        }
        RunOnUiThread(() =>
        {
            bool isCurrent;
            lock (_settings)
                isCurrent = ReferenceEquals(_settings.CredentialIdentity, credentialIdentity)
                    && string.Equals(_settings.ApiKey, apiKey, StringComparison.Ordinal)
                    && string.Equals(_settings.VoiceId, expectedVoiceId, StringComparison.Ordinal);
            if (_isDisposed || !isCurrent || !string.Equals(VoiceId, expectedVoiceId, StringComparison.Ordinal))
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
        BrowseMode = VoiceBrowseMode.Search;
    }

    [RelayCommand]
    private void SwitchToById()
    {
        BrowseMode = VoiceBrowseMode.ById;
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
    private void SelectVoice(VoiceSearchItem? item)
    {
        if (_isDisposed || item is null || !GetVoiceCards().Contains(item)
            || !SettingsValidation.IsValidVoiceIdFormat(item.Id))
            return;
        if (string.Equals(item.Id, VoiceId, StringComparison.Ordinal))
            ClearVoice();
        else
            _voiceDiscoveryCoordinator.SelectVoice(item);
    }

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

    private void ApplyPreviewPreparingState(string? voiceId)
    {
        PreparingPreviewVoiceId = voiceId;
        UpdatePreviewState();
    }

    internal void ReportRepositoryLinkFailure()
    {
        if (!_isDisposed)
            _context.Snackbar.ShowError(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_RepositoryLink_Failed"));
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
        IsDisplayVoicePreparing = CachedVoiceId is not null && CachedVoiceId == PreparingPreviewVoiceId;

        foreach (var item in _previewStateItems)
        {
            item.IsBeingPreviewed = false;
            item.IsPreparingPreview = false;
            item.PreviewProgress = 0;
        }
        _previewStateItems.Clear();

        foreach (var item in GetVoiceCards())
        {
            item.IsBeingPreviewed = PreviewingVoiceId is not null && item.Id == PreviewingVoiceId;
            item.IsPreparingPreview = PreparingPreviewVoiceId is not null && item.Id == PreparingPreviewVoiceId;
            item.PreviewProgress = item.IsBeingPreviewed ? PreviewProgress : 0;
            if (item.IsBeingPreviewed || item.IsPreparingPreview) _previewStateItems.Add(item);
        }
    }

    private void SyncPreviewStateToResults() => UpdatePreviewState();

    // ── 清除声音 ──

    [RelayCommand]
    private void ClearVoice()
    {
        if (!UpdateSettingsAndSave(settings =>
            {
                settings.VoiceId = "";
                settings.CachedVoice = null;
            }))
        {
            return;
        }

        SetVoiceIdWithoutSaving("");
        ApplyCachedVoice(null);
    }

    private void ApplyCachedVoice(CachedVoiceInfo? cached)
    {
        if (cached is null || string.IsNullOrEmpty(cached.Title))
        {
            CachedVoiceId = SettingsValidation.IsValidVoiceIdFormat(VoiceId) ? VoiceId : null;
            CachedVoiceTitle = CachedVoiceId;
            CachedVoiceDescription = null;
            CachedVoiceCoverUrl = null;
            CachedVoiceAuthor = null;
            CachedVoiceSampleUrl = null;
            CachedVoiceTaskCount = 0;
            CachedVoiceLanguages = [];
            CachedVoiceCreatedAt = null;
            UpdatePreviewState();
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
        CachedVoiceLanguages = VoiceLanguageMetadata.Normalize(cached.Languages);
        CachedVoiceCreatedAt = cached.CreatedAt;

        UpdatePreviewState();
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

        foreach (var item in GetVoiceCards())
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

    private void ApplySearchPagination(VoiceSearchPagination pagination)
    {
        // Publish the canonical bound before any new page draft reaches the view.
        OnPropertyChanged(nameof(SearchPageInputMaximum));
        SearchPage = pagination.Page;
        SearchTotalPages = pagination.LastAccessiblePage
            ?? (pagination.HasNextPage ? checked(pagination.Page + 1) : pagination.Page);
        SearchResultCount = pagination.ReportedTotal ?? 0;
        SearchResultCountText = pagination.ReportedTotal is { } total
            ? (pagination.IsTotalExact ? total.ToString() : $"≈{total}")
            : "?";
        var exactPageCount = pagination.IsTotalExact && !pagination.IsWindowLimited
            && pagination.ReportedTotal is { } exactTotal
            && pagination.LastAccessiblePage == Math.Max(1, ((long)exactTotal + 5) / 6);
        SearchPageLimitText = pagination.LastAccessiblePage is { } lastPage
            ? (exactPageCount ? lastPage.ToString() : $"≤{lastPage}")
            : "?";
        SearchHasUncertainExtent = !exactPageCount;
        SearchHasNextPage = pagination.HasNextPage;
    }

    // ── 属性变更 → 自动保存 ──

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Action<Settings> updateSettings;
        switch (e.PropertyName)
        {
            case nameof(SearchResults):
            case nameof(ByIdResults):
            case nameof(FavoriteResults):
                _previewRefreshCoordinator.OnVoiceCardsChanged();
                SyncFavoriteState();
                SyncPreviewStateToResults();
                return;
            case nameof(VoiceIdInput):
                _voiceDiscoveryCoordinator.InvalidateVoiceIdInput();
                return;
            case nameof(BrowseMode):
                if (!IsFavoritesMode)
                    CancelFavoritePageLoad();
                return;
            case nameof(ApiKey):
                if (_suppressSettingsSave)
                    return;
                var previousKey = _settings.ApiKey;
                try
                {
                    if (!UpdateSettingsAndSave(settings => settings.ApiKey = ApiKey))
                    {
                        RestoreApiKeyDraft(_settings.ApiKey);
                        return;
                    }
                }
                catch
                {
                    if (_settingsWriteLease is null)
                    {
                        _settings.ApiKey = previousKey;
                    }
                    RestoreApiKeyDraft(_settings.ApiKey);
                    throw;
                }
                if (_settingsWriteLease is null && !string.Equals(previousKey, _settings.ApiKey, StringComparison.Ordinal))
                {
                    // The legacy facade saves the same settings object directly.
                    // Its publication identity changes only after that save succeeds.
                    lock (_settings)
                        _settings.CredentialIdentity = new object();
                }
                SubscribeToVoiceMetadataUpdates();
                _voiceDiscoveryCoordinator.InvalidateCredentials();
                _previewRefreshCoordinator.CancelDisplayPreviewOperation();
                CancelFavoritePageLoad();
                return;
            case nameof(VoiceId):
                SyncFavoriteState();
                _previewRefreshCoordinator.CancelDisplayPreviewOperation();
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
            case nameof(AudioFormat): updateSettings = settings => settings.AudioFormat = AudioFormat; break;
            case nameof(Mp3Bitrate): updateSettings = settings => settings.Mp3Bitrate = Mp3Bitrate; break;
            case nameof(ConditionOnPreviousChunks): updateSettings = settings => settings.ConditionOnPreviousChunks = ConditionOnPreviousChunks; break;
            case nameof(IsS21ProFreePromoDismissed): updateSettings = settings => settings.IsS21ProFreePromoDismissed = IsS21ProFreePromoDismissed; break;
            default: return;
        }

        if (!_suppressSettingsSave)
            UpdateSettingsAndSave(updateSettings);
    }

    private void RestoreApiKeyDraft(string apiKey)
    {
        _suppressSettingsSave = true;
        try { ApiKey = apiKey; }
        finally { _suppressSettingsSave = false; }
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
        lock (_favoritesPersistenceGate)
            _isDisposed = true;
        CancelFavoritePageLoad();
        _voiceMetadataCache.MetadataUpdated -= _metadataUpdatedHandler;
        _favoriteStore.Dispose();
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
