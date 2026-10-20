using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using MessagePack;
using MessagePack.Resolvers;
using STranslate.Plugin.Tts.FishAudio.Streaming;

internal sealed class FishAudioSocketDouble : IFishAudioSocket
{
    private readonly Channel<Frame> _incoming = Channel.CreateUnbounded<Frame>();
    private readonly CancellationTokenSource _aborted = new();
    private Frame? _pending;
    private int _offset;
    internal ConcurrentQueue<JsonElement> Sent { get; } = new();
    internal TaskCompletionSource Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Func<CancellationToken, Task>? ConnectBehavior { get; set; }
    internal Func<CancellationToken, Task>? SendBehavior { get; set; }
    internal TaskCompletionSource SendEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Action<JsonElement>? OnSend { get; set; }
    internal Exception? SendFailure { get; set; }
    internal int AbortCount;
    internal int ConnectCount;
    internal int MaximumConcurrentSends;
    internal int MaximumConcurrentReceives;
    private int _sends;
    private int _receives;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref ConnectCount);
        Connected.TrySetResult();
        if (ConnectBehavior is { } behavior) await behavior(cancellationToken);
    }

    public async ValueTask SendBinaryAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sends = Interlocked.Increment(ref _sends);
        MaximumConcurrentSends = Math.Max(MaximumConcurrentSends, sends);
        try
        {
            SendEntered.TrySetResult();
            if (SendBehavior is { } behavior) await behavior(cancellationToken);
            if (SendFailure is { } failure) throw failure;
            using var document = JsonDocument.Parse(MessagePackSerializer.ConvertToJson(message));
            var decoded = document.RootElement.Clone();
            Sent.Enqueue(decoded);
            OnSend?.Invoke(decoded);
        }
        finally { Interlocked.Decrement(ref _sends); }
    }

    public async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _aborted.Token);
        var receives = Interlocked.Increment(ref _receives);
        MaximumConcurrentReceives = Math.Max(MaximumConcurrentReceives, receives);
        try
        {
            _pending ??= await _incoming.Reader.ReadAsync(linked.Token);
            var count = Math.Min(buffer.Length, _pending.Bytes.Length - _offset);
            _pending.Bytes.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            var result = new ValueWebSocketReceiveResult(count, _pending.Type, _pending.End && _offset == _pending.Bytes.Length);
            if (_offset == _pending.Bytes.Length) { _pending = null; _offset = 0; }
            return result;
        }
        finally { Interlocked.Decrement(ref _receives); }
    }

    internal void QueueEvent(Dictionary<string, object?> message, int fragmentBytes = int.MaxValue)
    {
        var bytes = MessagePackSerializer.Serialize(message, MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance));
        for (var offset = 0; offset < bytes.Length; offset += fragmentBytes)
        {
            var count = Math.Min(fragmentBytes, bytes.Length - offset);
            QueueFrame(bytes.AsSpan(offset, count).ToArray(), WebSocketMessageType.Binary, offset + count == bytes.Length);
        }
    }

    internal void QueueFrame(byte[] bytes, WebSocketMessageType type = WebSocketMessageType.Binary, bool end = true) =>
        _incoming.Writer.TryWrite(new(bytes, type, end));
    internal void QueueAudio(byte[] bytes, int fragmentBytes = int.MaxValue) => QueueEvent(new() { ["event"] = "audio", ["audio"] = bytes }, fragmentBytes);
    internal void QueueFinish(string reason = "stop") => QueueEvent(new() { ["event"] = "finish", ["reason"] = reason });
    public void Abort() { Interlocked.Increment(ref AbortCount); _aborted.Cancel(); }
    public void Dispose() { _aborted.Dispose(); }
    private sealed record Frame(byte[] Bytes, WebSocketMessageType Type, bool End);
}
