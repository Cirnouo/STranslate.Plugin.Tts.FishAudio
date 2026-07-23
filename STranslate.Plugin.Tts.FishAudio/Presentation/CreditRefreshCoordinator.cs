using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Model;
using System.Windows.Media;
using System.Windows.Threading;

namespace STranslate.Plugin.Tts.FishAudio.Presentation;

internal sealed class CreditRefreshCoordinator : IDisposable
{
    private const long LatencyGoodMs = 300;
    private const long LatencyFairMs = 800;

    private static readonly SolidColorBrush BrushGood = new(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly SolidColorBrush BrushFair = new(Color.FromRgb(0xFF, 0x98, 0x00));
    private static readonly SolidColorBrush BrushPoor = new(Color.FromRgb(0xF4, 0x43, 0x36));

    private readonly IPluginContext _context;
    private readonly Settings _settings;
    private readonly Action<string> _setUserCredit;
    private readonly Action<string, SolidColorBrush?> _setLatency;
    private readonly Action<Action> _runOnUiThread;
    private readonly Func<bool> _isDisposed;
    private readonly object _operationStateGate = new();
    private readonly OperationActivityCounter _apiKeyOperationState;
    private readonly OperationActivityCounter _creditOperationState;
    private StartupCreditRefreshCycle? _startupCreditRefreshCycle;
    private DispatcherTimer? _latencyHideTimer;

    internal CreditRefreshCoordinator(
        IPluginContext context,
        Settings settings,
        Action<bool> setApiKeyInputLocked,
        Action<bool> setCreditLoading,
        Action<string> setUserCredit,
        Action<string, SolidColorBrush?> setLatency,
        Action<Action> runOnUiThread,
        Func<bool> isDisposed)
    {
        _context = context;
        _settings = settings;
        _setUserCredit = setUserCredit;
        _setLatency = setLatency;
        _runOnUiThread = runOnUiThread;
        _isDisposed = isDisposed;
        _apiKeyOperationState = new OperationActivityCounter(
            _operationStateGate,
            setApiKeyInputLocked);
        _creditOperationState = new OperationActivityCounter(
            _operationStateGate,
            setCreditLoading);
    }

    internal async Task RefreshCreditAsync()
    {
        if (!FishAudioRequestPolicy.TryPreflightApiKey(
                _context,
                _settings.ApiKey,
                "Credit refresh",
                showError: true,
                out var apiKey))
        {
            return;
        }

        await FetchCreditAsync(apiKey, showError: true, showLatency: true);
    }

    internal async Task RefreshCreditSilentlyAsync()
    {
        if (_isDisposed())
            return;

        if (!FishAudioRequestPolicy.TryPreflightApiKey(
                _context,
                _settings.ApiKey,
                "Credit refresh",
                showError: false,
                out var apiKey))
        {
            return;
        }

        await FetchCreditAsync(apiKey, showError: false, showLatency: false);
    }

    internal void BeginApiKeyOperation()
    {
        _apiKeyOperationState.Begin();
    }

    internal void EndApiKeyOperation()
    {
        _apiKeyOperationState.End();
    }

    internal void AttachStartupCreditRefresh(StartupCreditRefreshCycle startupCreditRefreshCycle)
    {
        _startupCreditRefreshCycle = startupCreditRefreshCycle;
        if (startupCreditRefreshCycle.Completion.IsCompletedSuccessfully)
        {
            ApplyStartupCreditResult(
                startupCreditRefreshCycle,
                startupCreditRefreshCycle.Completion.GetAwaiter().GetResult());
            return;
        }

        BeginCreditOperation();
        BeginApiKeyOperation();
        _ = ObserveStartupCreditRefreshAsync(startupCreditRefreshCycle);
    }

    internal void InvalidateStartupCreditRefresh()
    {
        _startupCreditRefreshCycle = null;
    }

    private async Task FetchCreditAsync(string apiKey, bool showError, bool showLatency)
    {
        BeginCreditOperation();
        BeginApiKeyOperation();
        if (showLatency)
            _setLatency("", null);

        try
        {
            var (result, ms) = await FishAudioApi.GetCreditAsync(_context, apiKey, CancellationToken.None);
            ApplyCreditResult(result);

            if (showLatency)
            {
                var brush = ms switch
                {
                    <= LatencyGoodMs => BrushGood,
                    <= LatencyFairMs => BrushFair,
                    _ => BrushPoor,
                };
                _setLatency($"{ms} ms", brush);
                StartLatencyHideTimer();
            }
        }
        catch (Exception ex)
        {
            FishAudioRequestPolicy.LogRequestFailure(_context, "Credit refresh", ex);
            if (showError)
                _context.Snackbar.ShowError(FishAudioRequestPolicy.GetUserFacingError(_context, ex));
            if (showLatency)
                _setLatency("", null);
        }
        finally
        {
            EndCreditOperation();
            EndApiKeyOperation();
        }
    }

    private void BeginCreditOperation()
    {
        _creditOperationState.Begin();
    }

    private void EndCreditOperation()
    {
        _creditOperationState.End();
    }

    private void StartLatencyHideTimer()
    {
        _latencyHideTimer?.Stop();
        _latencyHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _latencyHideTimer.Tick += (_, _) =>
        {
            _setLatency("", null);
            _latencyHideTimer?.Stop();
            _latencyHideTimer = null;
        };
        _latencyHideTimer.Start();
    }

    private void ApplyCreditResult(WalletCreditResponse? result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.Credit))
            return;

        _setUserCredit(result.Credit);
    }

    private async Task ObserveStartupCreditRefreshAsync(StartupCreditRefreshCycle startupCreditRefreshCycle)
    {
        try
        {
            var result = await startupCreditRefreshCycle.Completion;
            _runOnUiThread(() => ApplyStartupCreditResult(startupCreditRefreshCycle, result));
        }
        finally
        {
            _runOnUiThread(() =>
            {
                EndCreditOperation();
                EndApiKeyOperation();
            });
        }
    }

    private void ApplyStartupCreditResult(
        StartupCreditRefreshCycle startupCreditRefreshCycle,
        WalletCreditResponse? result)
    {
        if (!startupCreditRefreshCycle.IsActive
            || _isDisposed()
            || !ReferenceEquals(_startupCreditRefreshCycle, startupCreditRefreshCycle)
            || !string.Equals(_settings.ApiKey, startupCreditRefreshCycle.ApiKeySnapshot, StringComparison.Ordinal))
        {
            return;
        }

        ApplyCreditResult(result);
    }

    public void Dispose()
    {
        _startupCreditRefreshCycle = null;
        _latencyHideTimer?.Stop();
    }
}
