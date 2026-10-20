using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using STranslate.Plugin.Tts.FishAudio.Configuration;

namespace STranslate.Plugin.Tts.FishAudio.Streaming;

internal interface IFishAudioSocket : IDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);
    ValueTask SendBinaryAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken);
    ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);
    void Abort();
}

internal sealed class FishAudioSocket : IFishAudioSocket
{
    private static readonly Uri Endpoint = new("wss://api.fish.audio/v1/tts/live");
    private readonly ClientWebSocket _socket = new();

    internal FishAudioSocket(SynthesisRequest request)
    {
        _socket.Options.SetRequestHeader("Authorization", $"Bearer {request.ApiKey}");
        _socket.Options.SetRequestHeader("model", request.Model);
        _socket.Options.KeepAliveTimeout = Timeout.InfiniteTimeSpan;
        _socket.Options.Proxy = CreateProxy(request.ProxyMode, request.ProxyAddress);
    }

    internal static IWebProxy? CreateProxy(string mode, string address)
    {
        if (!SettingsValidation.IsValidStreamingProxyConfiguration(mode, address))
            throw new InvalidOperationException("Fish Audio streaming proxy configuration is invalid.");
        if (mode == "system") return HttpClient.DefaultProxy;
        if (mode == "direct") return null;
        var uri = new Uri(address, UriKind.Absolute);

        // Never retain credentials in the URI handed to the transport or include the
        // configured address in an exception. The transport applies credentials separately.
        var proxyAddress = new UriBuilder(uri) { UserName = "", Password = "" }.Uri;
        var proxy = new WebProxy(proxyAddress);
        if (uri.UserInfo.Length > 0)
        {
            var separator = uri.UserInfo.IndexOf(':');
            var user = separator < 0 ? uri.UserInfo : uri.UserInfo[..separator];
            var password = separator < 0 ? "" : uri.UserInfo[(separator + 1)..];
            proxy.Credentials = new NetworkCredential(Uri.UnescapeDataString(user), Uri.UnescapeDataString(password));
        }
        return proxy;
    }

    public Task ConnectAsync(CancellationToken cancellationToken) => _socket.ConnectAsync(Endpoint, cancellationToken);
    public ValueTask SendBinaryAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken) =>
        _socket.SendAsync(message, WebSocketMessageType.Binary, true, cancellationToken);
    public ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        _socket.ReceiveAsync(buffer, cancellationToken);
    public void Abort() => _socket.Abort();
    public void Dispose() => _socket.Dispose();
}
