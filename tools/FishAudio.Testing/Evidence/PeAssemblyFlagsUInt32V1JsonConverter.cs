using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FishAudio.Testing.Evidence;

/// <summary>
/// Schema-1 PE evidence profile: AssemblyRef Flags is an unsigned 32-bit
/// metadata value. V1 preserves every bit without interpreting unknown flags.
/// Only the declared Flags property uses this codec; closed enums keep their
/// caller's strict string policy. Presence is an outer schema responsibility.
/// </summary>
public sealed class PeAssemblyFlagsUInt32V1JsonConverter : JsonConverter<AssemblyFlags>
{
    public override AssemblyFlags Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetUInt32(out var raw))
            throw new JsonException("pe-assembly-flags-u32-v1");
        return unchecked((AssemblyFlags)raw);
    }

    public override void Write(Utf8JsonWriter writer, AssemblyFlags value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(unchecked((uint)value));
}
