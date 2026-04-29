using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.Service;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Media;

namespace STranslate.Plugin.Tts.FishAudio.ViewModel;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IPluginContext _context;
    private readonly Settings _settings;

    private const long LatencyGoodMs = 300;
    private const long LatencyFairMs = 800;

    private bool _suppressModelLookup;
    private CancellationTokenSource? _modelLookupCts;

    private static readonly SolidColorBrush BrushGood = new(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly SolidColorBrush BrushFair = new(Color.FromRgb(0xFF, 0x98, 0x00));
    private static readonly SolidColorBrush BrushPoor = new(Color.FromRgb(0xF4, 0x43, 0x36));

    // ── API 连接 ──

    [ObservableProperty]
    public partial string ApiKey { get; set; }

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

    // ── 模型选择 ──

    [ObservableProperty]
    public partial string ReferenceId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNormalizeLoudness))]
    public partial string SelectedModel { get; set; }

    // ── 当前选中模型缓存显示 ──

    [ObservableProperty]
    public partial string? CachedModelId { get; set; }

    [ObservableProperty]
    public partial string? CachedModelTitle { get; set; }

    [ObservableProperty]
    public partial string? CachedModelCoverUrl { get; set; }

    [ObservableProperty]
    public partial string? CachedModelAuthor { get; set; }

    [ObservableProperty]
    public partial string? CachedModelSampleUrl { get; set; }

    [ObservableProperty]
    public partial int CachedModelTaskCount { get; set; }

    // ── 韵律 ──

    [ObservableProperty]
    public partial double Speed { get; set; }

    [ObservableProperty]
    public partial double Volume { get; set; }

    [ObservableProperty]
    public partial bool NormalizeLoudness { get; set; }

    public bool ShowNormalizeLoudness => SelectedModel == "s2-pro";

    // ── 生成参数 ──

    [ObservableProperty]
    public partial double Temperature { get; set; }

    [ObservableProperty]
    public partial double TopP { get; set; }

    [ObservableProperty]
    public partial string SelectedLatency { get; set; }

    [ObservableProperty]
    public partial bool Normalize { get; set; }

    // ── 模型搜索 ──

    [ObservableProperty]
    public partial string SearchQuery { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchModelsCommand))]
    public partial bool IsSearching { get; set; }

    [ObservableProperty]
    public partial bool IsSearchPanelVisible { get; set; }

    [ObservableProperty]
    public partial List<ModelSearchItem> SearchResults { get; set; }

    [ObservableProperty]
    public partial int SearchPage { get; set; }

    [ObservableProperty]
    public partial int SearchTotalPages { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlaySampleCommand))]
    public partial bool IsPlayingSample { get; set; }

    // ── 静态选项 ──

    public static IReadOnlyList<string> Models { get; } = ["s2-pro", "s1"];
    public static IReadOnlyList<string> Latencies { get; } = ["normal", "balanced", "low"];

    private const int SearchPageSize = 6;

    public SettingsViewModel(IPluginContext context, Settings settings, Task<(WalletCreditResponse?, long)>? pendingCreditTask)
    {
        _context = context;
        _settings = settings;

        _suppressModelLookup = true;
        ApiKey = settings.ApiKey;
        ReferenceId = settings.ReferenceId;
        SelectedModel = settings.SelectedModel;
        Speed = settings.Speed;
        Volume = settings.Volume;
        NormalizeLoudness = settings.NormalizeLoudness;
        Temperature = settings.Temperature;
        TopP = settings.TopP;
        SelectedLatency = settings.Latency;
        Normalize = settings.Normalize;

        UserCredit = "";
        LatencyText = "";
        SearchQuery = "";
        SearchResults = [];
        SearchPage = 1;

        ApplyCachedModel(settings.CachedModel);

        PropertyChanged += OnPropertyChanged;
        _suppressModelLookup = false;

        if (pendingCreditTask is not null)
            _ = ApplyPendingCreditAsync(pendingCreditTask);
    }

    // ── 账户命令 ──

    private bool CanRefreshCredit => !IsLoadingCredit;

    [RelayCommand(CanExecute = nameof(CanRefreshCredit))]
    private async Task RefreshCreditAsync()
    {
        await FetchCreditAsync(showError: true, showLatency: true);
    }

    internal async Task RefreshCreditSilentlyAsync()
    {
        await FetchCreditAsync(showError: false, showLatency: false);
    }

    private async Task ApplyPendingCreditAsync(Task<(WalletCreditResponse?, long)> task)
    {
        try
        {
            var (result, ms) = await task;
            ApplyCreditResult(result);
        }
        catch { }
    }

    private async Task FetchCreditAsync(bool showError, bool showLatency)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            if (showError)
                _context.Snackbar.ShowError(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_ApiKey_Empty"));
            return;
        }

        IsLoadingCredit = true;
        if (showLatency)
        {
            LatencyText = "";
            LatencyBrush = null;
        }

        try
        {
            var (result, ms) = await FishAudioApi.GetCreditAsync(_context, ApiKey, CancellationToken.None);
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

    private void ApplyCreditResult(WalletCreditResponse? result)
    {
        if (result is null) return;
        UserCredit = result.Credit;
    }

    // ── 模型搜索命令 ──

    [RelayCommand]
    private void ToggleSearchPanel()
    {
        IsSearchPanelVisible = !IsSearchPanelVisible;
    }

    private bool CanSearchModels => !IsSearching;

    [RelayCommand(CanExecute = nameof(CanSearchModels))]
    private async Task SearchModelsAsync()
    {
        SearchPage = 1;
        await ExecuteSearchAsync();
    }

    private async Task ExecuteSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            _context.Snackbar.ShowError(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_ApiKey_Empty"));
            return;
        }

        IsSearching = true;
        try
        {
            var response = await FishAudioApi.SearchModelsAsync(
                _context, ApiKey, SearchQuery, SearchPageSize, SearchPage, CancellationToken.None);

            if (response is null)
            {
                SearchResults = [];
                SearchTotalPages = 1;
                return;
            }

            SearchTotalPages = Math.Max(1, (int)Math.Ceiling(response.Total / (double)SearchPageSize));
            SearchResults = response.Items.Select(m => new ModelSearchItem
            {
                Id = m.Id,
                Title = m.Title,
                AuthorName = m.Author?.Nickname ?? "",
                CoverUrl = FishAudioApi.BuildCoverUrl(m.CoverImage),
                TaskCount = m.TaskCount,
                SampleAudioUrl = m.Samples.FirstOrDefault()?.Audio,
                CoverImage = m.CoverImage,
            }).ToList();
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
            await ExecuteSearchAsync();
        }
    }

    [RelayCommand]
    private async Task PrevPageAsync()
    {
        if (SearchPage > 1)
        {
            SearchPage--;
            await ExecuteSearchAsync();
        }
    }

    [RelayCommand]
    private void SelectModel(ModelSearchItem? item)
    {
        if (item is null) return;

        _suppressModelLookup = true;
        ReferenceId = item.Id;
        _suppressModelLookup = false;

        var cached = new CachedModelInfo
        {
            Title = item.Title,
            CoverImage = item.CoverImage,
            AuthorName = item.AuthorName,
            TaskCount = item.TaskCount,
            SampleAudioUrl = item.SampleAudioUrl,
        };
        _settings.CachedModel = cached;
        _context.SaveSettingStorage<Settings>();

        ApplyCachedModel(cached);
        IsSearchPanelVisible = false;
    }

    private bool CanPlaySample => !IsPlayingSample;

    [RelayCommand(CanExecute = nameof(CanPlaySample))]
    private async Task PlaySampleAsync(string? audioUrl)
    {
        if (string.IsNullOrEmpty(audioUrl)) return;

        IsPlayingSample = true;
        try
        {
            await _context.AudioPlayer.PlayAsync(audioUrl, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _context.Snackbar.ShowError(ex.Message);
        }
        finally
        {
            IsPlayingSample = false;
        }
    }

    [RelayCommand]
    private void ClearModel()
    {
        _suppressModelLookup = true;
        ReferenceId = "";
        _suppressModelLookup = false;
        _settings.CachedModel = null;
        _context.SaveSettingStorage<Settings>();
        ApplyCachedModel(null);
    }

    private void ApplyCachedModel(CachedModelInfo? cached)
    {
        if (cached is null || string.IsNullOrEmpty(cached.Title))
        {
            CachedModelId = null;
            CachedModelTitle = _context.GetTranslation("STranslate_Plugin_Tts_FishAudio_RandomModel");
            CachedModelCoverUrl = null;
            CachedModelAuthor = null;
            CachedModelSampleUrl = null;
            CachedModelTaskCount = 0;
            return;
        }

        CachedModelId = ReferenceId;
        CachedModelTitle = cached.Title;
        CachedModelCoverUrl = FishAudioApi.BuildCoverUrl(cached.CoverImage, 128);
        CachedModelAuthor = cached.AuthorName;
        CachedModelSampleUrl = cached.SampleAudioUrl;
        CachedModelTaskCount = cached.TaskCount;
    }

    // ── 模型 ID 变更 → 自动查询 ──

    private async Task LookupModelAsync(string modelId)
    {
        _modelLookupCts?.Cancel();
        var cts = new CancellationTokenSource();
        _modelLookupCts = cts;

        try
        {
            await Task.Delay(500, cts.Token);

            if (string.IsNullOrWhiteSpace(modelId))
            {
                _settings.CachedModel = null;
                _context.SaveSettingStorage<Settings>();
                ApplyCachedModel(null);
                return;
            }

            if (string.IsNullOrWhiteSpace(ApiKey)) return;

            var model = await FishAudioApi.GetModelAsync(_context, ApiKey, modelId, cts.Token);

            if (model is null)
            {
                _context.Snackbar.ShowError(_context.GetTranslation("STranslate_Plugin_Tts_FishAudio_Model_NotFound"));
                _settings.CachedModel = null;
                _context.SaveSettingStorage<Settings>();
                ApplyCachedModel(null);
                return;
            }

            var cached = new CachedModelInfo
            {
                Title = model.Title,
                CoverImage = model.CoverImage,
                AuthorName = model.Author?.Nickname ?? "",
                TaskCount = model.TaskCount,
                SampleAudioUrl = model.Samples.FirstOrDefault()?.Audio,
            };
            _settings.CachedModel = cached;
            _context.SaveSettingStorage<Settings>();
            ApplyCachedModel(cached);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _context.Snackbar.ShowError(ex.Message);
            _settings.CachedModel = null;
            _context.SaveSettingStorage<Settings>();
            ApplyCachedModel(null);
        }
    }

    // ── 属性变更 → 自动保存 ──

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ApiKey):             _settings.ApiKey = ApiKey; break;
            case nameof(ReferenceId):
                _settings.ReferenceId = ReferenceId;
                if (!_suppressModelLookup)
                    _ = LookupModelAsync(ReferenceId);
                break;
            case nameof(SelectedModel):      _settings.SelectedModel = SelectedModel; break;
            case nameof(Speed):              _settings.Speed = Speed; break;
            case nameof(Volume):             _settings.Volume = Volume; break;
            case nameof(NormalizeLoudness):  _settings.NormalizeLoudness = NormalizeLoudness; break;
            case nameof(Temperature):        _settings.Temperature = Temperature; break;
            case nameof(TopP):               _settings.TopP = TopP; break;
            case nameof(SelectedLatency):    _settings.Latency = SelectedLatency; break;
            case nameof(Normalize):          _settings.Normalize = Normalize; break;
            default: return;
        }

        _context.SaveSettingStorage<Settings>();
    }

    public void Dispose()
    {
        PropertyChanged -= OnPropertyChanged;
        _modelLookupCts?.Cancel();
        _modelLookupCts?.Dispose();
    }
}

public class ModelSearchItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string CoverUrl { get; set; } = "";
    public int TaskCount { get; set; }
    public string? SampleAudioUrl { get; set; }
    public string CoverImage { get; set; } = "";
}
