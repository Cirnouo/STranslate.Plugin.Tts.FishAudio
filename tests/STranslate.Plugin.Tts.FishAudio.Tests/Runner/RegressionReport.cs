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
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedUtc { get; set; }
    public RegressionSourceIdentity Source { get; set; } = new();
    public string Configuration { get; set; } = BuildMetadata("RegressionBuildConfiguration");
    public string Sdk { get; set; } = BuildMetadata("RegressionBuildSdk");
    public string Runtime { get; set; } = Environment.Version.ToString();
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

    internal static string BuildMetadata(string key)
    {
        var value = typeof(RegressionReport).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == key)?.Value;
        return value is { Length: > 0 and < 100 } && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '+') ? value : "unavailable";
    }

    internal static RegressionReport Read(string path)
    {
        using var input = File.OpenRead(path);
        if (input.Length > 16 * 1024 * 1024) throw new RegressionSelectionException("report-too-large");
        var report = JsonSerializer.Deserialize<RegressionReport>(input, JsonOptions);
        if (report is null || report.SchemaVersion != 1 || report.Source is null || report.Cases is null ||
            !RegressionSelection.IsSafeId(report.RunId) || report.Cases.Count == 0 ||
            report.Cases.Any(item => item is null || !RegressionSelection.IsSafeId(item.Id) ||
                item.Outcome is not ("passed" or "failed" or "timed-out" or "not-run" or "running")) ||
            report.Cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != report.Cases.Count)
            throw new RegressionSelectionException("invalid-report");
        return report;
    }

    internal void Write(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, this, JsonOptions);
                output.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, destinationBackupFileName: null);
            else File.Move(temporary, path);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch { /* A cleanup fault cannot replace the original evidence-write failure. */ }
        }
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
