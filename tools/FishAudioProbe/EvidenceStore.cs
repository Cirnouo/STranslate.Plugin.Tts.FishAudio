using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FishAudioProbe;

internal sealed class CaseEvidence
{
    internal CaseEvidence(string id, string endpoint, string directory)
    { Id = id; Endpoint = endpoint; Directory = directory; }
    public string Id { get; }
    public string Endpoint { get; }
    public bool IsFixture => false;
    public bool RequestSent { get; set; }
    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedUtc { get; set; }
    public int? HttpStatus { get; set; }
    public string Outcome { get; set; } = "incomplete";
    public string? Failure { get; set; }
    public bool SourceUrlCredentialsRedacted { get; set; }
    public BodyArtifact? Body { get; set; }
    public Dictionary<string, object?> Details { get; } = [];
    [JsonIgnore] internal string Directory { get; }
}

internal sealed record BodyArtifact(string File, int RawBytes, int StoredBytes, string RawSha256,
    string StoredSha256, bool Sanitized, bool Complete, string? ReadFailure, string? ContentType);
internal sealed record RunPointer(string Directory, string Manifest, DateTimeOffset StartedUtc, string Group);
internal sealed class RootManifest { public int SchemaVersion { get; set; } = 1; public List<RunPointer> Runs { get; set; } = []; }

internal sealed class EvidenceStore
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Redactor _redactor;
    private readonly List<CaseEvidence> _cases = [];
    private readonly string _group;
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
    internal string RunDirectory { get; }

    internal EvidenceStore(string output, string group, Redactor redactor)
    {
        _redactor = redactor;
        _group = group;
        Directory.CreateDirectory(output);
        var name = $"run-{_started:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
        RunDirectory = Path.Combine(output, name);
        Directory.CreateDirectory(Path.Combine(RunDirectory, "cases"));
        var rootPath = Path.Combine(output, "manifest.json");
        using (new FileStream(Path.Combine(output, ".manifest.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var root = File.Exists(rootPath)
                ? JsonSerializer.Deserialize<RootManifest>(File.ReadAllText(rootPath), Json) ?? throw new InvalidOperationException("Invalid existing root manifest.")
                : new RootManifest();
            root.Runs.Add(new(name, $"{name}/manifest.json", _started, group));
            WriteJson(rootPath, root);
        }
        Flush();
    }

    internal CaseEvidence Begin(string id, string endpoint)
    {
        if (_cases.Any(c => c.Id == id)) throw new InvalidOperationException("Duplicate case identifier refused.");
        var directory = Path.Combine(RunDirectory, "cases", id);
        Directory.CreateDirectory(directory);
        var evidence = new CaseEvidence(id, endpoint, directory);
        _cases.Add(evidence);
        Save(evidence);
        return evidence;
    }

    internal void Save(CaseEvidence evidence)
    {
        WriteJson(Path.Combine(evidence.Directory, "case.json"), evidence);
        Flush();
    }

    internal void Finish(CaseEvidence evidence)
    {
        evidence.FinishedUtc = DateTimeOffset.UtcNow;
        Save(evidence);
        Console.WriteLine($"{evidence.Id}: {evidence.Outcome} (HTTP {evidence.HttpStatus?.ToString() ?? "n/a"})");
    }

    internal void WriteJson(string path, object value)
    {
        var text = _redactor.CleanJson(JsonSerializer.Serialize(value, Json));
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, text, new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    internal BodyArtifact WriteBody(string directory, string file, byte[] raw, bool complete, string? failure, string? contentType, byte[]? sanitizedOverride = null)
    {
        var bytes = sanitizedOverride ?? raw;
        var textContent = contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true
            || contentType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true;
        if (sanitizedOverride is not null) { }
        else if (textContent)
            bytes = Encoding.UTF8.GetBytes(_redactor.CleanJson(Encoding.UTF8.GetString(raw)));
        else
        {
            // A server can return a JSON error with an inaccurate content type.
            try
            {
                using var document = JsonDocument.Parse(raw);
                bytes = Encoding.UTF8.GetBytes(_redactor.CleanJson(document.RootElement.GetRawText()));
            }
            catch (JsonException) { bytes = _redactor.CleanBinary(raw); }
        }
        File.WriteAllBytes(Path.Combine(directory, file), bytes);
        return new(file, raw.Length, bytes.Length, Hash(raw), Hash(bytes), !raw.AsSpan().SequenceEqual(bytes), complete, failure, contentType);
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal void Flush() => WriteJson(Path.Combine(RunDirectory, "manifest.json"), new
    {
        schemaVersion = 1, kind = "real-network-probes", isFixture = false, group = _group,
        startedUtc = _started, updatedUtc = DateTimeOffset.UtcNow,
        limits = new { secondsPerCase = 120, maximumReceivedBytesPerCase = ProbePolicy.MaximumBytes, concurrency = 1, automaticRetries = 0 },
        synthesis = new { model = ProbePolicy.FreeModel, cutoffUtc = ProbePolicy.CutoffUtc, paidFallback = false },
        cases = _cases,
    });
}
