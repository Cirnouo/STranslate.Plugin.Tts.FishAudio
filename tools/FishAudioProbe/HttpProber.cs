using System.Net;
using System.Text;
using System.Text.Json;

namespace FishAudioProbe;

internal sealed record HttpCapture(CaseEvidence Evidence, byte[] Bytes)
{
    internal bool Success => Evidence.Outcome == "success";
    internal JsonDocument? ParseJson()
    {
        if (!Success) return null;
        try { return JsonDocument.Parse(Bytes); }
        catch (JsonException) { return null; }
    }
}

internal sealed class HttpProber(EvidenceStore store, Redactor redactor, string? apiKey, HttpClient client)
{
    internal async Task<HttpCapture> GetAsync(string id, string endpoint, Uri uri, bool authenticated,
        bool asset = false, bool range = false, bool expectedFailure = false, CancellationToken cancellationToken = default) =>
        await SendAsync(id, endpoint, uri, null, authenticated, asset, range, expectedFailure, cancellationToken);

    internal async Task<HttpCapture> TtsAsync(string id, object body, CancellationToken cancellationToken, bool expectedFailure = false) =>
        await SendAsync(id, "http-tts", new("https://api.fish.audio/v1/tts"), body, true, false, false, expectedFailure, cancellationToken);

    private async Task<HttpCapture> SendAsync(string id, string endpoint, Uri uri, object? body, bool authenticated,
        bool asset, bool range, bool expectedFailure, CancellationToken cancellationToken)
    {
        var evidence = store.Begin(id, endpoint);
        byte[] bytes = [];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbePolicy.CaseTimeout);
        try
        {
            if (asset)
            {
                if (authenticated || body is not null || !ProbePolicy.IsAllowedAsset(uri))
                    throw new InvalidOperationException("Asset destination or authentication refused.");
            }
            else
            {
                ProbePolicy.EnsureApiUri(uri);
                if (body is not null)
                {
                    if (uri.AbsolutePath != "/v1/tts") throw new InvalidOperationException("Unexpected POST refused.");
                    ProbePolicy.EnsureSynthesisAllowed(ProbePolicy.FreeModel, DateTimeOffset.UtcNow);
                }
                else if (uri.AbsolutePath is not ("/model" or "/wallet/self/api-credit" or "/compat/v1/models")
                    && !uri.AbsolutePath.StartsWith("/model/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Undocumented GET route refused.");
            }
            if (authenticated && string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("FISH_API_KEY is required for this case.");
            using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, uri)
            { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
            if (authenticated) request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
            if (range) request.Headers.Range = new(0, 1023);
            if (body is not null)
            {
                request.Headers.Add("model", ProbePolicy.FreeModel);
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            }
            evidence.SourceUrlCredentialsRedacted = redactor.CleanUrl(uri.AbsoluteUri) != uri.AbsoluteUri;
            evidence.Details["expectedHttpFailure"] = expectedFailure;
            evidence.Details["authenticationMode"] = authenticated ? apiKey switch
            { "dummy" => "public-dummy", "invalid-probe-key" => "synthetic-invalid", _ => "environment-key" } : "none";
            store.WriteJson(Path.Combine(evidence.Directory, "request.json"), new
            {
                method = request.Method.Method, url = uri.AbsoluteUri,
                headers = redactor.CleanHeaders(request.Headers), body,
                source = asset ? "URL returned by the voice API (relative covers use the documented CDN base)" : "documented API route",
            });
            evidence.RequestSent = true;
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            evidence.HttpStatus = (int)response.StatusCode;
            var contentType = response.Content.Headers.ContentType?.ToString();
            store.WriteJson(Path.Combine(evidence.Directory, "response-headers.json"), new
            {
                status = (int)response.StatusCode, version = response.Version.ToString(),
                headers = redactor.CleanHeaders(response.Headers.Concat(response.Content.Headers)),
            });
            Capture capture;
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                capture = await BoundedCapture.ReadAsync(stream, ProbePolicy.MaximumBytes, timeout.Token);
            }
            catch (Exception ex) { capture = new([], false, ex.GetType().Name); }
            bytes = capture.Bytes;
            var text = contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true
                || contentType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true;
            evidence.Body = store.WriteBody(evidence.Directory, text ? "body.json" : "body.bin", bytes,
                capture.Complete, capture.Failure, contentType, endpoint == "balance"
                    ? Encoding.UTF8.GetBytes(redactor.CleanWalletJson(Encoding.UTF8.GetString(bytes))) : null);
            if (endpoint == "balance") evidence.Details["privateWalletIdentifiersRedacted"] = true;
            evidence.Details["bodyContainsSignedUrlFields"] = text && Encoding.UTF8.GetString(bytes).Contains("X-Amz-", StringComparison.OrdinalIgnoreCase);
            evidence.Outcome = !capture.Complete ? "response-read-failed" : response.IsSuccessStatusCode ? "success"
                : expectedFailure && (int)response.StatusCode is >= 400 and < 500 ? "expected-http-error" : "http-error";
            evidence.Failure = capture.Failure;
        }
        catch (Exception ex)
        {
            evidence.Outcome = evidence.RequestSent ? "transport-error" : "refused-before-request";
            evidence.Failure = ex.GetType().Name;
            evidence.Details["error"] = redactor.Clean(ex.Message);
        }
        finally { store.Finish(evidence); }
        return new(evidence, bytes);
    }
}
