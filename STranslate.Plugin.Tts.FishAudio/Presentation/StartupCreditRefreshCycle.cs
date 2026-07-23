using Microsoft.Extensions.Logging;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Model;

namespace STranslate.Plugin.Tts.FishAudio.Presentation;

internal sealed class StartupCreditRefreshCycle
{
    private int _isActive = 1;

    private StartupCreditRefreshCycle(string apiKeySnapshot, Task<WalletCreditResponse?> completion)
    {
        ApiKeySnapshot = apiKeySnapshot;
        Completion = completion;
    }

    internal string ApiKeySnapshot { get; }
    internal Task<WalletCreditResponse?> Completion { get; }
    internal bool IsActive => Volatile.Read(ref _isActive) != 0;

    internal void Invalidate() => Interlocked.Exchange(ref _isActive, 0);

    internal static StartupCreditRefreshCycle Start(
        IPluginContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var apiKeySnapshot = settings.ApiKey;
        return new StartupCreditRefreshCycle(
            apiKeySnapshot,
            FetchAsync(context, apiKeySnapshot, cancellationToken));
    }

    private static async Task<WalletCreditResponse?> FetchAsync(
        IPluginContext context,
        string apiKeySnapshot,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return null;

        if (!FishAudioRequestPolicy.TryPreflightApiKey(
                context,
                apiKeySnapshot,
                "Startup credit refresh",
                showError: false,
                out var apiKey))
        {
            return null;
        }

        try
        {
            var (result, _) = await FishAudioApi.GetCreditAsync(context, apiKey, cancellationToken);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Logger?.LogError(
                "Startup credit refresh failed: {ExceptionType}",
                ex.GetType().Name);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
