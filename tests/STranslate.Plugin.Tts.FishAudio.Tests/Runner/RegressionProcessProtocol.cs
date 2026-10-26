using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class RegressionChildRequest
{
    [JsonRequired] public int Version { get; set; } = 1;
    [JsonRequired] public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    [JsonRequired] public string RunId { get; set; } = "";
    [JsonRequired] public string Partition { get; set; } = "";
    [JsonRequired] public string[] CaseIds { get; set; } = [];
    [JsonRequired] public RegressionSourceIdentity Source { get; set; } = new();
    [JsonRequired] public string Configuration { get; set; } = "";
    [JsonRequired] public string Sdk { get; set; } = "";
    public RegressionCiIdentity? Ci { get; set; }
    [JsonRequired] public string ManifestPath { get; set; } = "";
    public string[]? LegacyArguments { get; set; }
    // Only fixed offline subprocess probes are admitted, never arbitrary code/arguments.
    public string? Probe { get; set; }
}

internal sealed class RegressionChildManifest
{
    [JsonRequired] public int Version { get; set; } = 1;
    [JsonRequired] public string RequestId { get; set; } = "";
    [JsonRequired] public string RunId { get; set; } = "";
    [JsonRequired] public string Partition { get; set; } = "";
    [JsonRequired] public RegressionSourceIdentity Source { get; set; } = new();
    [JsonRequired] public string Configuration { get; set; } = "";
    [JsonRequired] public string Sdk { get; set; } = "";
    public RegressionCiIdentity? Ci { get; set; }
    [JsonRequired] public bool Final { get; set; }
    [JsonRequired] public bool Complete { get; set; }
    [JsonRequired] public List<RegressionCaseResult> Cases { get; set; } = [];

    internal static RegressionChildManifest Create(RegressionChildRequest request) => new()
    {
        RequestId = request.RequestId, RunId = request.RunId, Partition = request.Partition,
        Source = request.Source, Configuration = request.Configuration, Sdk = request.Sdk, Ci = request.Ci,
        Cases = request.CaseIds.Select(id => new RegressionCaseResult { Id = id, Partition = request.Partition }).ToList()
    };
}

internal sealed class RegressionProcessEvent
{
    [JsonRequired] public int Version { get; set; } = 1;
    [JsonRequired] public string RequestId { get; set; } = "";
    [JsonRequired] public string RunId { get; set; } = "";
    [JsonRequired] public string Partition { get; set; } = "";
    [JsonRequired] public string Kind { get; set; } = "";
    [JsonRequired] public string CaseId { get; set; } = "";
    [JsonRequired] public int Ordinal { get; set; }
    [JsonRequired] public long Timestamp { get; set; }
    [JsonRequired] public long Frequency { get; set; } = Stopwatch.Frequency;
    [JsonRequired] public long QueuedTimestamp { get; set; }
    [JsonRequired] public int Snapshot { get; set; }
}

internal static class RegressionProcessProtocol
{
    internal const string Prefix = "FISH-REGRESSION/1 ";
    internal const int MaximumRecordCharacters = 16 * 1024;
    internal static readonly string[] Partitions = ["core", "native-settings", "native-input", "native-popups", "native-cards"];
    internal static readonly JsonSerializerOptions Options = new(RegressionReport.JsonOptions)
    {
        WriteIndented = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static bool SameSource(RegressionSourceIdentity actual, RegressionSourceIdentity expected) =>
        actual is not null && expected is not null && JsonSerializer.Serialize(actual, Options) == JsonSerializer.Serialize(expected, Options);

    internal static T Read<T>(string path)
    {
        // Active child records are immutable; readers never own a writer's path.
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (input.Length is <= 0 or > 16 * 1024 * 1024) throw new InvalidDataException("invalid-child-record-size");
        return JsonSerializer.Deserialize<T>(input, Options) ?? throw new InvalidDataException("missing-child-record");
    }

    internal static void WriteNew<T>(string path, T value) => Write(path, value, replaceExisting: false);

    // Controlled transport fixtures can intentionally replace malformed evidence.
    internal static void Write<T>(string path, T value) => Write(path, value, replaceExisting: true);

    private static void Write<T>(string path, T value, bool replaceExisting)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, value, Options);
                output.Flush(flushToDisk: true);
            }
            if (replaceExisting && File.Exists(path)) File.Replace(temporary, path, destinationBackupFileName: null);
            else File.Move(temporary, path);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch { /* Preserve the write failure. */ }
        }
    }

    internal static void ValidateRequest(RegressionChildRequest request)
    {
        request.Ci?.Validate(request.Partition);
        if (request.Version != 1 || !Guid.TryParseExact(request.RequestId, "N", out _) ||
            !Guid.TryParseExact(request.RunId, "N", out _) || !Partitions.Contains(request.Partition, StringComparer.Ordinal) ||
            request.CaseIds is not { Length: > 0 } || request.CaseIds.Any(id => !RegressionSelection.IsSafeId(id)) ||
            request.CaseIds.Distinct(StringComparer.Ordinal).Count() != request.CaseIds.Length || request.Source is null ||
            request.Configuration != RegressionReport.BuildMetadata("RegressionBuildConfiguration") ||
            request.Sdk != RegressionReport.BuildMetadata("RegressionBuildSdk") || !Path.IsPathFullyQualified(request.ManifestPath))
            throw new InvalidDataException("invalid-child-request");
        if (request.LegacyArguments is not null && (!LegacyRegressionSelectors.AreArgumentsValid(request.LegacyArguments) ||
            request.CaseIds.Length != 1 || request.CaseIds[0] != LegacyId(request.LegacyArguments[0])))
            throw new InvalidDataException("invalid-legacy-child-request");
        if (request.Probe is not null && (request.LegacyArguments is not null || request.Probe is not ("pipes" or "hang" or "missing" or "truncated" or "foreign" or "fail" or "owned-timeout")))
            throw new InvalidDataException("invalid-child-probe");
    }

    internal static string LegacyId(string selector) => "legacy/" + selector.TrimStart('-');

    internal static string SnapshotPath(RegressionChildRequest request, int snapshot) => request.ManifestPath + $".snapshot-{snapshot:D6}.json";

    internal static void ValidateManifest(RegressionChildManifest manifest, RegressionChildRequest request)
    {
        if (manifest.Version != 1 || manifest.RequestId != request.RequestId || manifest.RunId != request.RunId ||
            manifest.Partition != request.Partition || !SameSource(manifest.Source, request.Source) ||
            manifest.Configuration != request.Configuration || manifest.Sdk != request.Sdk || !RegressionCiIdentity.Same(manifest.Ci, request.Ci) || manifest.Cases is null ||
            !manifest.Cases.Select(item => item?.Id).SequenceEqual(request.CaseIds, StringComparer.Ordinal) ||
            manifest.Cases.Any(item => item is null || item.Partition != request.Partition || item.ElapsedMilliseconds < 0 ||
                item.Outcome is not ("passed" or "failed" or "timed-out" or "running" or "not-run") ||
                (item.Outcome is "failed" or "timed-out") != !string.IsNullOrWhiteSpace(item.ExceptionType) ||
                (item.ExceptionType is not null && (item.ExceptionType.Length > 256 || !item.ExceptionType.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '+' or '`'))) ||
                (item.FailureStage is not null && item.FailureStage is not ("case" or "action-guard")) ||
                item.SecondaryExceptionType is not null || item.SecondaryFailureStage is not null) ||
            manifest.Complete != (manifest.Final && manifest.Cases.All(item => item.Outcome == "passed")) ||
            (manifest.Final && manifest.Cases.Any(item => item.Outcome == "running")))
            throw new InvalidDataException("invalid-child-manifest");
        var outstanding = false;
        var failed = false;
        foreach (var item in manifest.Cases)
        {
            if (item.Outcome == "not-run") { outstanding = true; continue; }
            if (outstanding || failed) throw new InvalidDataException("invalid-child-case-order");
            if (item.Outcome is "failed" or "timed-out" or "running") failed = true;
        }
    }
}

internal sealed class RegressionProtocolWriter(TextWriter physicalOutput, RegressionChildRequest request)
{
    private readonly object _gate = new();

    internal void Publish(string kind, string caseId = "", RegressionActionEvent? action = null, int snapshot = 0)
    {
        var record = new RegressionProcessEvent
        {
            RequestId = request.RequestId, RunId = request.RunId, Partition = request.Partition,
            Kind = kind, CaseId = action?.CaseId ?? caseId, Ordinal = action?.Ordinal ?? 0,
            Timestamp = action?.Timestamp ?? Stopwatch.GetTimestamp(), Frequency = action?.Frequency ?? Stopwatch.Frequency,
            QueuedTimestamp = action?.QueuedTimestamp ?? 0, Snapshot = snapshot
        };
        lock (_gate)
        {
            physicalOutput.WriteLine(RegressionProcessProtocol.Prefix + JsonSerializer.Serialize(record, RegressionProcessProtocol.Options));
            physicalOutput.Flush();
        }
    }
}
