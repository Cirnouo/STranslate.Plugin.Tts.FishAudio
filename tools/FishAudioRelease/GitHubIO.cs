using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace FishAudioRelease;
// Fixed GitHub origins and redirect refusal keep the built-in token on GitHub only.
internal sealed class GitHubIO(string repository, string token, string root, HttpMessageHandler? handler = null) : IReleaseIO, IDisposable
{
    readonly HttpClient client = CreateClient(token, handler);
    static HttpClient CreateClient(string token, HttpMessageHandler? handler)
    {
        Protocol.Require(!string.IsNullOrWhiteSpace(token), "GITHUB_TOKEN is required.");
        var client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FishAudioRelease/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
        return client;
    }

    public string Git(params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git failed to start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            process.Kill(true);
            throw new InvalidOperationException("Git timed out.");
        }

        Task.WaitAll(output, error);
        Protocol.Require(process.ExitCode == 0, "Git identity verification failed.");
        return output.Result.Trim();
    }

    async Task<HttpResponseMessage> Send(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new InvalidOperationException($"GitHub request failed ({status}); response body withheld.");
        }

        return response;
    }

    public async Task<JsonNode?> Request(string method, string endpoint, JsonObject? body = null)
    {
        Protocol.Require(endpoint.StartsWith($"/repos/{repository}/", StringComparison.Ordinal), "API path outside repository.");
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://api.github.com" + endpoint);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        // Only the missing tag lookup is optional. Other 404s must fail closed.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound &&
            method == "GET" &&
            (endpoint.StartsWith($"/repos/{repository}/releases/tags/", StringComparison.Ordinal) || endpoint == $"/repos/{repository}/releases/latest"))
        {
            return null;
        }

        Protocol.Require(response.IsSuccessStatusCode, $"GitHub request failed ({(int)response.StatusCode}); response body withheld.");
        return JsonNode.Parse(await response.Content.ReadAsStringAsync());
    }

    public async Task<byte[]> Download(long assetId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/assets/{assetId}");
        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd("application/octet-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Found)
        {
            var location = response.Headers.Location;
            Protocol.Require(location is { IsAbsoluteUri: true, Scheme: "https", Host: "release-assets.githubusercontent.com", IsDefaultPort: true } &&
            string.IsNullOrEmpty(location.UserInfo), "Unexpected asset redirect origin.");
            // The signed URL is private. Neither credentials nor its value enter diagnostics.
            using var redirected = new HttpRequestMessage(HttpMethod.Get, location);
            using var downloaded = await client.SendAsync(redirected);
            Protocol.Require(downloaded.IsSuccessStatusCode, "Asset download failed; signed URL withheld.");
            return await downloaded.Content.ReadAsByteArrayAsync();
        }

        Protocol.Require(response.IsSuccessStatusCode, $"Asset download failed ({(int)response.StatusCode}).");
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<JsonNode?> Upload(long releaseId, string name, byte[] bytes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://uploads.github.com/repos/{repository}/releases/{releaseId}/assets?name={Uri.EscapeDataString(name)}")
        {
            Content = new ByteArrayContent(bytes)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await Send(request);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync());
    }

    public void Dispose() => client.Dispose();
}
