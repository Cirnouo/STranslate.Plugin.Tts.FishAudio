using MessagePack;
using MessagePack.Resolvers;
using System.Collections;

namespace FishAudioProbe;

internal static class WsCodec
{
    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard
        .WithResolver(ContractlessStandardResolver.Instance).WithSecurity(MessagePackSecurity.UntrustedData);
    internal static byte[] Encode(Dictionary<string, object?> value) => MessagePackSerializer.Serialize(value, Options);
    internal static Dictionary<string, object?> Decode(byte[] bytes) => MessagePackSerializer.Deserialize<Dictionary<string, object?>>(bytes, Options);

    internal static byte[] Sanitize(Dictionary<string, object?> message, byte[] original, Redactor redactor)
    {
        var changed = false;
        object? Scrub(object? value)
        {
            if (value is string text)
            {
                var clean = redactor.Clean(text);
                changed |= clean != text;
                return clean;
            }
            if (value is byte[] bytes)
            {
                var clean = redactor.CleanSecretBytes(bytes);
                changed |= !clean.AsSpan().SequenceEqual(bytes);
                return clean;
            }
            if (value is IDictionary dictionary)
            {
                var result = new Dictionary<string, object?>();
                foreach (DictionaryEntry pair in dictionary)
                {
                    var name = pair.Key.ToString() ?? "";
                    if (redactor.IsSensitive(name)) { result[name] = "[REDACTED]"; changed = true; }
                    else result[name] = Scrub(pair.Value);
                }
                return result;
            }
            if (value is object?[] array) return array.Select(Scrub).ToArray();
            return value;
        }
        var sanitized = (Dictionary<string, object?>)Scrub(message)!;
        return changed ? Encode(sanitized) : original;
    }
}
