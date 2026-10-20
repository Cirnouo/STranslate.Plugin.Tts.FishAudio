using System.Text.Json;

namespace FishAudioProbe;

internal sealed record VoiceInfo(string Id, string? Sample, string? Cover, bool FromDetail);

internal sealed class ProbeRunner(CliOptions options, EvidenceStore store, Redactor redactor, string? apiKey)
{
    private readonly List<VoiceInfo> _voices = [];
    private bool Selected(string id) => options.Cases.Count == 0 || options.Cases.Contains(id);

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = Timeout.InfiniteTimeSpan };
        var publicHttp = new HttpProber(store, redactor, "dummy", client);
        var authenticatedHttp = new HttpProber(store, redactor, apiKey, client);
        var invalidHttp = new HttpProber(store, redactor, "invalid-probe-key", client);
        if (options.Group is "public" or "all")
        {
            if (Selected("catalog-all")) await publicHttp.GetAsync("catalog-all", "engine-catalog", new("https://api.fish.audio/compat/v1/models"), false, cancellationToken: cancellationToken);
            if (Selected("catalog-speech")) await publicHttp.GetAsync("catalog-speech", "engine-catalog", new("https://api.fish.audio/compat/v1/models?output_modalities=speech"), false, cancellationToken: cancellationToken);
            if (Selected("voices-anonymous")) await publicHttp.GetAsync("voices-anonymous", "voices", new("https://api.fish.audio/model?page_size=3&page_number=1&sort_by=score"), false, cancellationToken: cancellationToken);
            await DiscoverAsync(publicHttp, "public", cancellationToken);
            await AssetsAsync(publicHttp, "public", cancellationToken);
        }
        if (options.Group is "authenticated" or "all")
        {
            await DiscoverAsync(authenticatedHttp, "authenticated", cancellationToken);
            await AssetsAsync(authenticatedHttp, "authenticated", cancellationToken);
            if (Selected("balance-valid")) await authenticatedHttp.GetAsync("balance-valid", "balance", new("https://api.fish.audio/wallet/self/api-credit"), true, cancellationToken: cancellationToken);
            if (Selected("balance-repeat")) await authenticatedHttp.GetAsync("balance-repeat", "balance", new("https://api.fish.audio/wallet/self/api-credit"), true, cancellationToken: cancellationToken);
            if (Selected("balance-missing-auth")) await authenticatedHttp.GetAsync("balance-missing-auth", "balance", new("https://api.fish.audio/wallet/self/api-credit"), false, expectedFailure: true, cancellationToken: cancellationToken);
            if (Selected("balance-invalid-auth")) await invalidHttp.GetAsync("balance-invalid-auth", "balance", new("https://api.fish.audio/wallet/self/api-credit"), true, expectedFailure: true, cancellationToken: cancellationToken);
        }
        if (options.Group is "http-tts" or "ws-tts" or "all")
        {
            ProbePolicy.EnsureSynthesisAllowed(ProbePolicy.FreeModel, DateTimeOffset.UtcNow);
            if (_voices.Count == 0) await DiscoverAsync(authenticatedHttp, "synthesis-discovery", cancellationToken);
            var voice = _voices.FirstOrDefault();
            if (options.Group is "http-tts" or "all")
            {
                foreach (var (id, format, text) in new[]
                {
                    ("http-pcm-en", "pcm", "Hello."), ("http-pcm-en-repeat", "pcm", "Hello."),
                    ("http-mp3-zh", "mp3", "你好。"), ("http-mp3-zh-repeat", "mp3", "你好。"),
                    ("http-invalid-format", "unsupported-probe-format", "Hi."),
                })
                {
                    if (!Selected(id)) continue;
                    if (voice is null) { Skip(id, "http-tts", "No voice ID was obtained from a real voice response."); continue; }
                    var body = new Dictionary<string, object?>
                    {
                        ["text"] = text, ["reference_id"] = voice.Id, ["format"] = format,
                        ["sample_rate"] = 44100, ["latency"] = "balanced", ["chunk_length"] = 100,
                    };
                    if (format == "mp3") body["mp3_bitrate"] = 128;
                    await authenticatedHttp.TtsAsync(id, body, cancellationToken, id == "http-invalid-format");
                }
            }
            if (options.Group is "ws-tts" or "all")
            {
                var ws = new WsProber(store, redactor, apiKey!);
                foreach (var (id, format, cancel, missingAuth, segmented) in new[]
                {
                    ("ws-pcm", "pcm", false, false, false), ("ws-mp3", "mp3", false, false, false),
                    ("ws-pcm-segmented", "pcm", false, false, true),
                    ("ws-cancel", "pcm", true, false, false), ("ws-missing-auth", "pcm", false, true, false),
                })
                {
                    if (!Selected(id)) continue;
                    if (voice is null) { Skip(id, "ws-tts", "No voice ID was obtained from a real voice response."); continue; }
                    await ws.RunAsync(id, voice.Id, format, cancel, missingAuth, cancellationToken, segmented);
                }
            }
        }
        store.Flush();
    }

    private async Task DiscoverAsync(HttpProber http, string prefix, CancellationToken cancellationToken)
    {
        // These read-only dependency requests still run when --case selects a synthesis/resource case.
        var list = await http.GetAsync($"{prefix}-voices-default", "voices",
            new("https://api.fish.audio/model?page_size=5&page_number=1&sort_by=score"), true, cancellationToken: cancellationToken);
        using (var json = list.ParseJson())
            if (json?.RootElement.TryGetProperty("items", out var items) == true && items.ValueKind == JsonValueKind.Array)
                foreach (var item in items.EnumerateArray()) AddVoice(item);
        if (Selected($"{prefix}-voices-search")) await http.GetAsync($"{prefix}-voices-search", "voices",
            new("https://api.fish.audio/model?page_size=3&page_number=1&sort_by=score&title=English"), true, cancellationToken: cancellationToken);
        if (Selected($"{prefix}-voices-page-2")) await http.GetAsync($"{prefix}-voices-page-2", "voices",
            new("https://api.fish.audio/model?page_size=3&page_number=2&sort_by=score"), true, cancellationToken: cancellationToken);
        if (Selected($"{prefix}-voices-no-result")) await http.GetAsync($"{prefix}-voices-no-result", "voices",
            new("https://api.fish.audio/model?page_size=3&page_number=1&title=FishAudioProbeNoResult8ce81d98c4734cf79c38174a7798df44"), true, cancellationToken: cancellationToken);
        foreach (var (voice, index) in _voices.DistinctBy(v => v.Id).Take(2).ToArray().Select((v, i) => (v, i)))
        {
            var detail = await http.GetAsync($"{prefix}-voice-detail-{index + 1}", "voice-detail",
                new($"https://api.fish.audio/model/{Uri.EscapeDataString(voice.Id)}"), true, cancellationToken: cancellationToken);
            using var json = detail.ParseJson();
            if (json is not null) AddVoice(json.RootElement, fromDetail: true);
        }
        if (Selected($"{prefix}-voice-detail-missing")) await http.GetAsync($"{prefix}-voice-detail-missing", "voice-detail",
            new("https://api.fish.audio/model/00000000000000000000000000000000"), true, expectedFailure: true, cancellationToken: cancellationToken);
    }

    private void AddVoice(JsonElement value, bool fromDetail = false)
    {
        if (!value.TryGetProperty("_id", out var id) || id.ValueKind != JsonValueKind.String) return;
        if (value.TryGetProperty("type", out var type) && type.GetString() != "tts") return;
        string? sample = null;
        if (value.TryGetProperty("samples", out var samples) && samples.ValueKind == JsonValueKind.Array)
            foreach (var item in samples.EnumerateArray())
                if (item.TryGetProperty("audio", out var audio) && audio.ValueKind == JsonValueKind.String)
                { sample = audio.GetString(); break; }
        var cover = value.TryGetProperty("cover_image", out var image) && image.ValueKind == JsonValueKind.String ? image.GetString() : null;
        _voices.Add(new(id.GetString()!, sample, cover, fromDetail));
    }

    private async Task AssetsAsync(HttpProber http, string prefix, CancellationToken cancellationToken)
    {
        var sample = _voices.Select(v => v.Sample).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        foreach (var (suffix, range) in new[] { ("sample-full", false), ("sample-range", true) })
        {
            var id = $"{prefix}-{suffix}";
            if (!Selected(id)) continue;
            if (sample is null || !Uri.TryCreate(sample, UriKind.Absolute, out var uri)) Skip(id, "preview-assets", "The real voice responses contained no usable sample URL.");
            else await http.GetAsync(id, "preview-assets", uri, false, asset: true, range: range, cancellationToken: cancellationToken);
        }
        // Detail responses can carry expiring signed URLs even when list samples are public URLs.
        // Keep those source paths separate rather than silently testing the first list sample again.
        var detailSample = _voices.Where(v => v.FromDetail).Select(v => v.Sample).LastOrDefault(v => !string.IsNullOrWhiteSpace(v));
        foreach (var (suffix, range) in new[] { ("sample-detail-full", false), ("sample-detail-range", true) })
        {
            var id = $"{prefix}-{suffix}";
            if (!Selected(id)) continue;
            if (detailSample is null || !Uri.TryCreate(detailSample, UriKind.Absolute, out var uri))
                Skip(id, "preview-assets", "The real voice-detail responses contained no usable sample URL.");
            else
            {
                var capture = await http.GetAsync(id, "preview-assets", uri, false, asset: true, range: range, cancellationToken: cancellationToken);
                capture.Evidence.Details["assetSource"] = "latest voice-detail sample";
                store.Save(capture.Evidence);
            }
        }
        var coverId = $"{prefix}-cover-full";
        if (!Selected(coverId)) return;
        var cover = _voices.Select(v => v.Cover).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (cover is null) { Skip(coverId, "preview-assets", "The real voice responses contained no cover path."); return; }
        var coverUri = Uri.TryCreate(cover, UriKind.Absolute, out var absolute) ? absolute : new Uri(new Uri("https://public-platform.r2.fish.audio/"), cover.TrimStart('/'));
        await http.GetAsync(coverId, "preview-assets", coverUri, false, asset: true, cancellationToken: cancellationToken);
    }

    private void Skip(string id, string endpoint, string reason)
    {
        var evidence = store.Begin(id, endpoint);
        evidence.Outcome = "skipped-dependency";
        evidence.Details["reason"] = reason;
        store.Finish(evidence);
    }
}
