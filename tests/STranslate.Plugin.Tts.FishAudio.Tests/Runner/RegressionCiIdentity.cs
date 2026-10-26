using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class RegressionCiIdentity
{
    internal const string ProducerJob = "regression";

    [JsonRequired] public string Repository { get; set; } = "";
    [JsonRequired] public long WorkflowRunId { get; set; }
    [JsonRequired] public int Attempt { get; set; }
    [JsonRequired] public string Job { get; set; } = "";
    [JsonRequired] public string Partition { get; set; } = "";

    // Only the actual producer entry captures CI context. Internal runner fixtures
    // and old local reports retain null rather than borrowing ambient CI state.
    internal static RegressionCiIdentity? Capture(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        var partition = read("REGRESSION_PARTITION");
        if (string.IsNullOrEmpty(partition)) return null;
        if (!long.TryParse(read("GITHUB_RUN_ID"), NumberStyles.None, CultureInfo.InvariantCulture, out var runId) ||
            !int.TryParse(read("GITHUB_RUN_ATTEMPT"), NumberStyles.None, CultureInfo.InvariantCulture, out var attempt))
            throw new InvalidDataException("invalid-ci-identity");
        var identity = new RegressionCiIdentity
        {
            Repository = read("GITHUB_REPOSITORY") ?? "", WorkflowRunId = runId,
            Attempt = attempt, Job = read("GITHUB_JOB") ?? "", Partition = partition
        };
        identity.Validate(partition);
        return identity;
    }

    internal void Validate(string partition)
    {
        var names = Repository?.Split('/');
        if (names is not { Length: 2 } || names.Any(name => name.Length == 0 || name is "." or ".." ||
                !name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')) ||
            WorkflowRunId <= 0 || Attempt <= 0 || Job != ProducerJob || Partition != partition ||
            !RegressionProcessProtocol.Partitions.Contains(Partition, StringComparer.Ordinal))
            throw new InvalidDataException("invalid-ci-identity");
    }

    internal static bool Same(RegressionCiIdentity? actual, RegressionCiIdentity? expected) =>
        JsonSerializer.Serialize(actual, RegressionReport.JsonOptions) == JsonSerializer.Serialize(expected, RegressionReport.JsonOptions);
}
