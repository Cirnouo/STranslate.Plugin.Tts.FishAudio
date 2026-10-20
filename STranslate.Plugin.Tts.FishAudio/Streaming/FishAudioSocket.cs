using System.Net.Http;
using System.Net.WebSockets;

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
        _socket.Options.Proxy = HttpClient.DefaultProxy;
    }

    public Task ConnectAsync(CancellationToken cancellationToken) => _socket.ConnectAsync(Endpoint, cancellationToken);
    public ValueTask SendBinaryAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken) =>
        _socket.SendAsync(message, WebSocketMessageType.Binary, true, cancellationToken);
    public ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        _socket.ReceiveAsync(buffer, cancellationToken);
    public void Abort() => _socket.Abort();
    public void Dispose() => _socket.Dispose();
}
