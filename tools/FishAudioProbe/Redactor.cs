using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Text;

namespace FishAudioProbe;

internal sealed class Redactor(string secret)
{
    private static readonly Regex Urls = new(@"(?:https?|wss?)://[^\s""'<>\\]+", RegexOptions.IgnoreCase);
    private static readonly Regex Credentials = new(@"(?i)(?:Bearer\s+)[^\s,""';}\]]+", RegexOptions.CultureInvariant);
    private static readonly Regex SensitiveName = new(@"(?i)^(?:authorization|proxy-authorization|set-cookie|cookie|x-api-key|xi-api-key|api[_-]?key|access[_-]?token|token|secret|password|signature|x-amz-.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex PartialWalletIds = new(@"(""(?:_id|user_id)""\s*:\s*"")[^""\r\n]*(?:""|$)", RegexOptions.CultureInvariant);

    internal string Clean(string text)
    {
        var clean = Urls.Replace(text, match => CleanUrl(match.Value));
        clean = Credentials.Replace(clean, "Bearer [REDACTED]");
        if (!string.IsNullOrEmpty(secret))
        {
            clean = clean.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
            clean = clean.Replace(Uri.EscapeDataString(secret), "[REDACTED]", StringComparison.Ordinal);
        }
        return clean;
    }

    internal string CleanUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return "[REDACTED-URI]";
        var builder = new UriBuilder(uri) { UserName = "", Password = "", Fragment = "" };
        if (uri.Query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => Uri.UnescapeDataString(pair.TrimStart('?').Split('=')[0]))
            .Any(name => SensitiveName.IsMatch(name))) builder.Query = "";
        return builder.Uri.AbsoluteUri;
    }

    internal string CleanJson(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            Walk(root);
            return root?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
        }
        catch (JsonException) { return Clean(json); }
    }

    internal string CleanWalletJson(string json)
    {
        var clean = CleanJson(json);
        try
        {
            var root = JsonNode.Parse(clean);
            if (root is JsonObject obj)
                foreach (var name in new[] { "_id", "user_id" })
                    if (obj.ContainsKey(name)) obj[name] = "[REDACTED-ACCOUNT-ID]";
            return root?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
        }
        catch (JsonException) { return PartialWalletIds.Replace(clean, match => match.Groups[1].Value + "[REDACTED-ACCOUNT-ID]\""); }
    }

    internal Dictionary<string, string[]> CleanHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers) =>
        headers.ToDictionary(pair => pair.Key,
            pair => SensitiveName.IsMatch(pair.Key) ? ["[REDACTED]"] : pair.Value.Select(Clean).ToArray(),
            StringComparer.OrdinalIgnoreCase);

    internal bool IsSensitive(string name) => SensitiveName.IsMatch(name);
    internal byte[] CleanBinary(byte[] bytes) => Encoding.Latin1.GetBytes(Clean(Encoding.Latin1.GetString(CleanSecretBytes(bytes))));

    internal byte[] CleanSecretBytes(byte[] bytes)
    {
        if (string.IsNullOrEmpty(secret)) return bytes;
        var patterns = new[] { secret, Uri.EscapeDataString(secret) }.Distinct(StringComparer.Ordinal);
        var clean = bytes;
        foreach (var value in patterns)
        {
            var pattern = Encoding.UTF8.GetBytes(value);
            var offset = clean.AsSpan().IndexOf(pattern);
            if (offset < 0) continue;
            using var result = new MemoryStream();
            var position = 0;
            while (offset >= 0)
            {
                result.Write(clean.AsSpan(position, offset));
                result.Write("[REDACTED]"u8);
                position += offset + pattern.Length;
                offset = clean.AsSpan(position).IndexOf(pattern);
            }
            result.Write(clean.AsSpan(position));
            clean = result.ToArray();
        }
        return clean;
    }

    private void Walk(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var (name, value) in obj.ToArray())
            {
                if (SensitiveName.IsMatch(name)) obj[name] = "[REDACTED]";
                else if (value is JsonValue v && v.TryGetValue<string>(out var text)) obj[name] = Clean(text);
                else Walk(value);
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonValue v && v.TryGetValue<string>(out var text)) array[i] = Clean(text);
                else Walk(array[i]);
            }
        }
    }
}
