using System.Net;
using System.Text;

namespace FishAudioRelease;

internal static class HttpTests
{
    internal static async Task Run()
    {
        SelfTest.Reject(() =>
        {
            using var io = new GitHubIO("owner/repo", "", ".");
        });
        using var handler = new FakeHandler();
        using var io = new GitHubIO("owner/repo", "test-token", ".", handler);
        SelfTest.Equal("package", Encoding.UTF8.GetString(await io.Download(5)));
        SelfTest.Equal(2, handler.Count);
        SelfTest.Equal(false, handler.LeakedToken);
        using var bad = new FakeHandler
        {
            BadHost = true
        };
        using var badIo = new GitHubIO("owner/repo", "test-token", ".", bad);
        await PublisherTests.Reject(async () => await badIo.Download(5));
        SelfTest.Equal(1, bad.Count);
        Console.WriteLine("Release HTTP token/redirect isolation tests passed.");
    }

    sealed class FakeHandler : HttpMessageHandler
    {
        internal int Count;
        internal bool LeakedToken, BadHost;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            if (request.RequestUri!.Host == "api.github.com")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(BadHost ? "https://attacker.example/package" : "https://release-assets.githubusercontent.com/package?signature=private") } });
            }

            LeakedToken = request.Headers.Authorization is not null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("package")) });
        }
    }
}
