using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;

internal sealed class RegressionReport
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public int SchemaVersion { get; set; } = 1;
    public long Generation { get; set; }
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedUtc { get; set; }
    public RegressionSourceIdentity Source { get; set; } = new();
    public string Configuration { get; set; } = BuildMetadata("RegressionBuildConfiguration");
    public string Sdk { get; set; } = BuildMetadata("RegressionBuildSdk");
    public string Runtime { get; set; } = Environment.Version.ToString();
    public RegressionCiIdentity? Ci { get; set; }
    public string[] Invocation { get; set; } = [];
    public string? PreviousRunId { get; set; }
    public bool SourceChangedSincePreviousRun { get; set; }
    public bool BuildChangedSincePreviousRun { get; set; }
    public bool ConservativeAffectedFallback { get; set; }
    public string[] SelectionReasons { get; set; } = [];
    public string[] RecommendedGroups { get; set; } = [];
    public bool Complete { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public List<RegressionCaseResult> Cases { get; set; } = [];
    public string? ProjectionExceptionType { get; set; }
    public int? ProjectionExceptionHResult { get; set; }

    private string? _ownedPath;
    private string? _ownedRunId;
    private string? _identity;
    private long _lastGeneration;

    internal static string BuildMetadata(string key)
    {
        var value = typeof(RegressionReport).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == key)?.Value;
        return value is { Length: > 0 and < 100 } && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '+') ? value : "unavailable";
    }

    internal static RegressionReport Read(string path)
    {
        var fullPath = Path.GetFullPath(path);
        byte[] canonicalBytes;
        try { canonicalBytes = ReadBytes(fullPath); }
        catch (IOException error) when ((error is FileNotFoundException || (error.HResult & 0xffff) is 32 or 33) && Directory.Exists(GenerationsRoot(fullPath)))
        {
            var recovered = ReadGenerations(fullPath, expectedRunId: null).Latest;
            // A held projection cannot authenticate its run/source identity. Keep
            // durable outcomes, but require the existing conservative recovery.
            recovered.Source.Available = false;
            return recovered;
        }
        var canonical = Parse(canonicalBytes);
        if (!Directory.Exists(GenerationsRoot(fullPath))) return canonical;
        var generations = ReadGenerations(fullPath, canonical.RunId);
        var projectedPath = GenerationPath(fullPath, canonical.RunId, canonical.Generation);
        if (!File.Exists(projectedPath) || !canonicalBytes.AsSpan().SequenceEqual(ReadBytes(projectedPath)))
            throw new RegressionSelectionException("report-projection-mismatch");
        return generations.Latest;
    }

    private static RegressionReport Parse(byte[] bytes)
    {
        var report = JsonSerializer.Deserialize<RegressionReport>(bytes, JsonOptions);
        if (report is null || report.SchemaVersion != 1 || report.Generation < 0 || report.Source is null || report.Cases is null ||
            !RegressionSelection.IsSafeId(report.RunId) || report.Cases.Count == 0 ||
            report.Cases.Any(item => item is null || !RegressionSelection.IsSafeId(item.Id) ||
                item.Outcome is not ("passed" or "failed" or "timed-out" or "not-run" or "running")) ||
            report.Cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != report.Cases.Count)
            throw new RegressionSelectionException("invalid-report");
        return report;
    }

    internal void Create(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (_ownedPath is not null || File.Exists(fullPath) || Directory.Exists(GenerationsRoot(fullPath)))
            throw new RegressionSelectionException("results-already-exist");
        if (!Guid.TryParseExact(RunId, "N", out _)) throw new RegressionSelectionException("invalid-report-run-id");
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        Generation = 0;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
        _ = Parse(bytes);
        // Publish valid JSON with no overwrite, replacing the old empty reservation.
        PublishNew(fullPath, bytes);
        Directory.CreateDirectory(RunDirectory(fullPath, RunId));
        PublishNew(GenerationPath(fullPath, RunId, 0), bytes);
        _ownedPath = fullPath;
        _ownedRunId = RunId;
        _identity = Identity(this);
    }

    internal void Write(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (_ownedPath is null) { Create(fullPath); return; }
        if (!string.Equals(_ownedPath, fullPath, StringComparison.OrdinalIgnoreCase) || _ownedRunId != RunId || _identity != Identity(this) || Generation != _lastGeneration)
            throw new RegressionSelectionException("report-owner-changed");
        var previous = Generation;
        Generation = checked(previous + 1);
        byte[] bytes;
        try
        {
            bytes = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
            _ = Parse(bytes);
            // This unique full generation is the authority. A failure here must
            // still fail execution; no synthesis or case delegate is repeated.
            PublishNew(GenerationPath(fullPath, RunId, Generation), bytes);
            _lastGeneration = Generation;
        }
        catch { Generation = previous; throw; }
        try { Project(fullPath, bytes); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A held reader can deny Windows replacement even with share-delete.
            // Durability already belongs to the immutable generation above.
            ProjectionExceptionType = error.GetType().FullName;
            ProjectionExceptionHResult = error.HResult;
        }
    }

    internal static string GenerationsRoot(string path) => Path.GetFullPath(path) + ".generations";
    internal static string RunDirectory(string path, string runId) => Path.Combine(GenerationsRoot(path), runId);
    internal static string GenerationPath(string path, string runId, long generation) => Path.Combine(RunDirectory(path, runId), $"{generation:D20}.json");

    private static (RegressionReport Initial, RegressionReport Latest) ReadGenerations(string path, string? expectedRunId)
    {
        var owners = Directory.GetDirectories(GenerationsRoot(path));
        if (owners.Length != 1 || !Guid.TryParseExact(Path.GetFileName(owners[0]), "N", out _))
            throw new RegressionSelectionException("invalid-report-generation-owner");
        var runId = Path.GetFileName(owners[0]);
        if (expectedRunId is not null && expectedRunId != runId)
            throw new RegressionSelectionException("report-generation-run-mismatch");
        var committed = Directory.GetFiles(owners[0], "*.json").Order(StringComparer.Ordinal).ToArray();
        if (committed.Length == 0) throw new RegressionSelectionException("missing-report-generations");
        foreach (var file in committed)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.Length != 20 || !long.TryParse(name, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var generation) ||
                generation < 0 || name != $"{generation:D20}")
                throw new RegressionSelectionException("invalid-report-generation-name");
        }
        var initial = Parse(ReadBytes(GenerationPath(path, runId, 0)));
        var latest = Parse(ReadBytes(committed[^1]));
        var latestNumber = long.Parse(Path.GetFileNameWithoutExtension(committed[^1]), System.Globalization.CultureInfo.InvariantCulture);
        if (initial.RunId != runId || initial.Generation != 0 || latest.RunId != runId || latest.Generation != latestNumber || Identity(initial) != Identity(latest))
            throw new RegressionSelectionException("report-generation-identity-mismatch");
        return (initial, latest);
    }

    internal static string Identity(RegressionReport report) => JsonSerializer.Serialize(new
    {
        report.SchemaVersion, report.RunId, report.StartedUtc, report.Source, report.Configuration, report.Sdk, report.Runtime, report.Ci,
        report.Invocation, report.PreviousRunId, report.SourceChangedSincePreviousRun, report.BuildChangedSincePreviousRun,
        report.ConservativeAffectedFallback, report.SelectionReasons, report.RecommendedGroups,
        Cases = report.Cases.Select(item => new { item.Id, item.Partition }).ToArray()
    }, JsonOptions);

    private static byte[] ReadBytes(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (input.Length is <= 0 or > 16 * 1024 * 1024) throw new RegressionSelectionException("report-too-large");
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        return bytes;
    }

    private static void PublishNew(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            WriteBytes(temporary, bytes);
            File.Move(temporary, path);
        }
        finally { TryDelete(temporary); }
    }

    private static void Project(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            WriteBytes(temporary, bytes);
            if (File.Exists(path)) File.Replace(temporary, path, destinationBackupFileName: null);
            else File.Move(temporary, path);
        }
        finally { TryDelete(temporary); }
    }

    private static void WriteBytes(string path, byte[] bytes)
    {
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        output.Write(bytes);
        output.Flush(flushToDisk: true);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* Cleanup cannot replace the original evidence-write failure. */ }
    }
}

internal sealed class RegressionCaseResult
{
    public string Id { get; set; } = "";
    public string Partition { get; set; } = "";
    public string Outcome { get; set; } = "not-run";
    public long ElapsedMilliseconds { get; set; }
    public string? ExceptionType { get; set; }
    public string? FailureStage { get; set; }
    public string? InfrastructureCode { get; set; }
    public string? ChildRequestId { get; set; }
    public int? ChildExitCode { get; set; }
    public string? ChildInfrastructureExceptionType { get; set; }
    public string? SecondaryExceptionType { get; set; }
    public string? SecondaryFailureStage { get; set; }
    public int SlowActionWarnings { get; set; }
    public int ActionOrdinal { get; set; }
    public long? QueueToEntryMilliseconds { get; set; }
}
