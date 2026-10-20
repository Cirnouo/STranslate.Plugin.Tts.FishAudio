using System.Buffers;
using System.IO;
using System.Text;
using MessagePack;
using MessagePack.Resolvers;
using STranslate.Plugin.Tts.FishAudio.Configuration;

namespace STranslate.Plugin.Tts.FishAudio.Streaming;

internal readonly record struct FishAudioServerEvent(string Event, ReadOnlySequence<byte> Audio, string? Reason);

internal static class FishAudioStreamingProtocol
{
    internal const int MaximumMessageBytes = 8 * 1024 * 1024;
    internal const int AudioBlockBytes = 16 * 1024;
    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard
        .WithResolver(ContractlessStandardResolver.Instance).WithSecurity(MessagePackSecurity.UntrustedData);

    internal static byte[] Start(SynthesisRequest request)
    {
        var prosody = new Dictionary<string, object?> { ["speed"] = request.Speed, ["volume"] = request.Volume };
        if (FishAudioModelPolicy.SupportsNormalizeLoudness(request.Model))
            prosody["normalize_loudness"] = request.NormalizeLoudness;
        var body = new Dictionary<string, object?>
        {
            ["text"] = "",
            ["format"] = request.AudioFormat,
            ["sample_rate"] = 44100,
            ["temperature"] = request.Temperature,
            ["top_p"] = request.TopP,
            ["normalize"] = request.Normalize,
            ["latency"] = request.Latency,
            ["condition_on_previous_chunks"] = request.ConditionOnPreviousChunks,
            ["prosody"] = prosody,
            ["chunk_length"] = 100,
        };
        if (request.AudioFormat == "mp3") body["mp3_bitrate"] = request.Mp3Bitrate;
        if (!string.IsNullOrWhiteSpace(request.VoiceId)) body["reference_id"] = request.VoiceId;
        return Encode(new() { ["event"] = "start", ["request"] = body });
    }

    internal static byte[] Text(string text) => Encode(new() { ["event"] = "text", ["text"] = text });
    internal static byte[] Control(string name) => Encode(new() { ["event"] = name });
    private static byte[] Encode(Dictionary<string, object?> message) => MessagePackSerializer.Serialize(message, Options);

    internal static FishAudioServerEvent Decode(ReadOnlyMemory<byte> message)
    {
        try
        {
            var reader = new MessagePackReader(new ReadOnlySequence<byte>(message));
            var count = reader.ReadMapHeader();
            if (count > 32) throw InvalidMessage();
            string? eventName = null;
            string? reason = null;
            ReadOnlySequence<byte> audio = default;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var key = ReadShortString(ref reader);
                if (!keys.Add(key)) throw InvalidMessage();
                switch (key)
                {
                    case "event": eventName = ReadShortString(ref reader); break;
                    case "reason": reason = ReadShortString(ref reader); break;
                    case "audio": audio = reader.ReadBytes() ?? throw InvalidMessage(); break;
                    default: SkipBounded(ref reader, 0); break;
                }
            }
            if (!reader.End || eventName is null) throw InvalidMessage();
            if (eventName != "audio" && eventName != "finish" && eventName != "error") throw InvalidMessage();
            if (eventName == "audio" && !keys.Contains("audio")) throw InvalidMessage();
            return new(eventName, audio, reason);
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            // Provider error strings and arbitrary metadata never enter diagnostics.
            throw new InvalidDataException("Fish Audio returned an invalid streaming message.");
        }
    }

    private static string ReadShortString(ref MessagePackReader reader)
    {
        var bytes = reader.ReadStringSequence() ?? throw InvalidMessage();
        if (bytes.Length > 64) throw InvalidMessage();
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static void SkipBounded(ref MessagePackReader reader, int depth)
    {
        if (depth >= 16) throw InvalidMessage();
        switch (reader.NextMessagePackType)
        {
            case MessagePackType.Array:
                var length = reader.ReadArrayHeader();
                if (length > 128) throw InvalidMessage();
                for (var i = 0; i < length; i++) SkipBounded(ref reader, depth + 1);
                break;
            case MessagePackType.Map:
                var pairs = reader.ReadMapHeader();
                if (pairs > 128) throw InvalidMessage();
                for (var i = 0; i < pairs; i++) { SkipBounded(ref reader, depth + 1); SkipBounded(ref reader, depth + 1); }
                break;
            default:
                reader.Skip(); // Scalar skip slices bytes; it never expands an object graph.
                break;
        }
    }

    private static InvalidDataException InvalidMessage() => new("Fish Audio returned an invalid streaming message.");
}
