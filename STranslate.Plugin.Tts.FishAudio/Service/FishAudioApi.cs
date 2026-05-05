using STranslate.Plugin.Tts.FishAudio.Model;
using System.Text.Json;

namespace STranslate.Plugin.Tts.FishAudio.Service;

internal static class FishAudioApi
{
    private const string ApiBase = "https://api.fish.audio";
    private const string CdnBase = "https://public-platform.r2.fish.audio/";

    public static async Task<byte[]> PostTtsAsync(
        IPluginContext context, Settings settings, string text, CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["text"] = text,
            ["format"] = "mp3",
            ["mp3_bitrate"] = settings.Mp3Bitrate,
            ["temperature"] = settings.Temperature,
            ["top_p"] = settings.TopP,
            ["normalize"] = settings.Normalize,
            ["latency"] = settings.Latency,
            ["condition_on_previous_chunks"] = settings.ConditionOnPreviousChunks,
            ["prosody"] = new Dictionary<string, object>
            {
                ["speed"] = settings.Speed,
                ["volume"] = settings.Volume,
                ["normalize_loudness"] = settings.NormalizeLoudness,
            },
        };

        if (!string.IsNullOrWhiteSpace(settings.VoiceId))
            body["reference_id"] = settings.VoiceId;

        var option = new Options
        {
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {settings.ApiKey}",
                ["Content-Type"] = "application/json",
                ["model"] = settings.SelectedModel,
            },
        };

        return await context.HttpService.PostAsBytesAsync($"{ApiBase}/v1/tts", body, option, ct);
    }

    public static async Task<(WalletCreditResponse? Result, long ElapsedMs)> GetCreditAsync(
        IPluginContext context, string apiKey, CancellationToken ct)
    {
        var option = new Options
        {
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {apiKey}",
            },
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var json = await context.HttpService.GetAsync($"{ApiBase}/wallet/self/api-credit", option, ct);
        sw.Stop();

        var result = JsonSerializer.Deserialize<WalletCreditResponse>(json);
        return (result, sw.ElapsedMilliseconds);
    }

    public static async Task<ModelListResponse?> SearchModelsAsync(
        IPluginContext context, string apiKey, string? query, int pageSize, int pageNumber, CancellationToken ct)
    {
        var option = new Options
        {
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {apiKey}",
            },
        };

        var url = $"{ApiBase}/model?page_size={pageSize}&page_number={pageNumber}&sort_by=score";
        if (!string.IsNullOrWhiteSpace(query))
            url += $"&title={Uri.EscapeDataString(query)}";

        var json = await context.HttpService.GetAsync(url, option, ct);
        return JsonSerializer.Deserialize<ModelListResponse>(json);
    }

    public static async Task<ModelEntity?> GetModelAsync(
        IPluginContext context, string apiKey, string modelId, CancellationToken ct)
    {
        var option = new Options
        {
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {apiKey}",
            },
        };

        try
        {
            var json = await context.HttpService.GetAsync($"{ApiBase}/model/{modelId}", option, ct);
            return JsonSerializer.Deserialize<ModelEntity>(json);
        }
        catch
        {
            return null;
        }
    }

    public static string BuildCoverUrl(string coverImage, int width = 64)
    {
        if (string.IsNullOrEmpty(coverImage)) return "";
        return $"{CdnBase}cdn-cgi/image/width={width},format=auto/{coverImage}";
    }

}
