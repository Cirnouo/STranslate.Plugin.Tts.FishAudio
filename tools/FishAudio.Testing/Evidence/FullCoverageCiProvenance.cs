using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FishAudio.Testing.Evidence;

public enum FullCoverageCiRole { Prepare, Shard }
public sealed record FullCoverageCiRoleExpectation(FullCoverageCiRole Role, int? ShardIndex, string JobName,
    IReadOnlyList<string> RequiredSteps, string EvidenceRelativePath);
public sealed record FullCoverageCiPreparationIdentity(CoverageSourceIdentity Source, FullCoveragePreparedExpectation Prepared,
    string RunId, string SnapshotContextSha256, string MetadataToolSha256, string BuildAuthoritySha256,
    string DotCoverToolSha256, string VstestConsoleSha256, string SdkVersion, string Configuration,
    string TargetFramework, string InstrumentationPolicyVersion);
// This context must be acquired independently from the immutable runner event,
// workflow and successful preparation. This core does not fetch or authenticate
// any of them. JobName is the exact complete API name, including any wrapper.
public sealed record FullCoverageCiContext(int SchemaVersion, string Repository, string EventName, string Ref,
    string Commit, string Tree, string WorkflowName, long RunId, int CurrentAttempt, string OperationRoot,
    string JobsSha256, FullCoverageCiPreparationIdentity Preparation, int ShardCount, int MaximumConcurrentMachines,
    IReadOnlyList<FullCoverageCiRoleExpectation> Roles);

// These are the existing Get-RegressionJobSnapshot fields, retaining the raw
// Actions job/step spellings. Parsing and authenticated acquisition stay external.
public sealed record FullCoverageCiAttemptFence(
    [property: JsonPropertyName("runAttempt")] int RunAttempt,
    [property: JsonPropertyName("runStartedAt")] string RunStartedAt);
public sealed record FullCoverageCiStep(
    [property: JsonPropertyName("number")] long Number,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("conclusion")] string? Conclusion,
    [property: JsonPropertyName("started_at")] string? StartedAt,
    [property: JsonPropertyName("completed_at")] string? CompletedAt);
public sealed record FullCoverageCiJob(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("run_id")] long RunId,
    [property: JsonPropertyName("run_attempt")] int RunAttempt,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("head_sha")] string HeadSha,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("conclusion")] string? Conclusion,
    [property: JsonPropertyName("started_at")] string? StartedAt,
    [property: JsonPropertyName("completed_at")] string? CompletedAt,
    [property: JsonPropertyName("runner_id")] long RunnerId,
    [property: JsonPropertyName("runner_name")] string? RunnerName,
    [property: JsonPropertyName("steps")] IReadOnlyList<FullCoverageCiStep>? Steps);
public sealed record FullCoverageCiJobsSnapshot(
    [property: JsonPropertyName("repository")] string Repository,
    [property: JsonPropertyName("runId")] long RunId,
    [property: JsonPropertyName("filter")] string Filter,
    [property: JsonPropertyName("totalCount")] long TotalCount,
    [property: JsonPropertyName("jobs")] IReadOnlyList<FullCoverageCiJob> Jobs,
    [property: JsonPropertyName("attempts")] IReadOnlyList<FullCoverageCiAttemptFence> Attempts);

public sealed record FullCoverageCiFile(string Path, long Length, string Sha256);
public sealed record FullCoverageCiInventory(IReadOnlyList<string> Directories, IReadOnlyList<FullCoverageCiFile> Files);
// This separate owner envelope is not a GitHub API record or a success receipt.
// Its inventory describes the complete operation archive payload. Its own raw
// bytes and archive origin must be observed/authenticated by the external caller.
public sealed record FullCoverageCiOwner(int SchemaVersion, string Kind, FullCoverageCiRole Role, int? ShardIndex,
    int ShardCount, string Repository, string Commit, string Tree, string WorkflowName, long WorkflowRunId, int Attempt,
    long JobId, string FinishedUtc, long PreparationJobId, int PreparationAttempt,
    FullCoverageCiPreparationIdentity Preparation, string OriginalRoot, string ArtifactName,
    string ArchiveSha256, string EvidenceSha256, FullCoverageCiInventory Inventory);
public sealed record FullCoverageCiArtifact(FullCoverageCiOwner Owner, string OwnerRecordSha256,
    string ObservedArchiveSha256, FullCoverageCiInventory ObservedInventory);
public sealed record FullCoverageCiInput(int SchemaVersion, FullCoverageCiJobsSnapshot Jobs, string JobsSha256,
    IReadOnlyList<FullCoverageCiArtifact> Artifacts);
public sealed record FullCoverageCiSelectedOwner(FullCoverageCiRole Role, int? ShardIndex, int ActualAttempt,
    long ActualJobId, long PreparationJobId, int PreparationAttempt, string JobName, long StartedUtcTicks,
    long CompletedUtcTicks, string ArtifactName, string OriginalRoot, string OwnerRecordSha256,
    string ArchiveSha256, string InventorySha256, string EvidencePath, string EvidenceSha256);
public sealed record FullCoverageCiSelection(int SchemaVersion, string Repository, string Commit, string Tree,
    string WorkflowName, long RunId, int CurrentAttempt, string JobsSha256, string PreparedSha256,
    int ShardCount, int MaximumConcurrentMachines, FullCoverageCiSelectedOwner Preparation,
    IReadOnlyList<FullCoverageCiSelectedOwner> Shards)
{
    public bool OwnerSelectionComplete => true;
    public bool RequiresIndependentRawReimport => true;
    public bool ApiEventAuthenticated => false;
    public bool SourceBuildBytesVerified => false;
    public bool MachineConcurrencyVerified => false;
    public bool MainProvenanceVerified => false;
    public bool RawCoverageUnionAdmitted => false;
    public bool CompleteBaseline => false;
    public bool PublicationAuthorized => false;
    public bool TiaSelectionAuthorized => false;
}
public sealed class FullCoverageCiProvenanceException : Exception
{
    internal FullCoverageCiProvenanceException(string code) : base("full-coverage-ci-" + code) => Code = Message;
    public string Code { get; }
}

// Pure whole-operation selection over independently supplied evidence. It never
// runs tests, reads files, restores archives, queries Actions, publishes a main
// baseline or mutates CoverageBaselineCandidateEvidence's conservative flags.
// A selected role set does not establish smoke gating/order or a complete main
// pipeline. MaximumConcurrentMachines is caller policy, not measured enforcement.
public static class FullCoverageCiProvenance
{
    private const int MaximumMembers = 200_000;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private sealed record Execution(long Started, long Completed, string Stamp);
    private sealed class History
    {
        internal Dictionary<int, FullCoverageCiJob> Advertised { get; } = [];
        internal Dictionary<int, FullCoverageCiJob> Actual { get; } = [];
        internal Dictionary<int, Execution> Executions { get; } = [];
        internal Dictionary<int, FullCoverageCiArtifact> Artifacts { get; } = [];
    }

    public static FullCoverageCiSelection Select(FullCoverageCiInput input, FullCoverageCiContext expected)
    {
        ValidateContext(expected);
        Require(input is not null && input.SchemaVersion == 1 && Hash(input.JobsSha256) &&
            input.JobsSha256 == expected.JobsSha256, "jobs-digest");
        var jobs = input.Jobs;
        Require(jobs is not null && jobs.Repository == expected.Repository && jobs.RunId == expected.RunId && jobs.Filter == "all" &&
            jobs.Jobs is { Count: <= MaximumMembers } && jobs.TotalCount == jobs.Jobs.Count, "jobs-snapshot");
        Require(jobs.Attempts is not null && jobs.Attempts.Count == expected.CurrentAttempt, "attempt-fences");
        var fences = new List<long>();
        foreach (var fence in jobs.Attempts)
        {
            Require(fence is not null && fence.RunAttempt == fences.Count + 1, "attempt-fences");
            var started = Utc(fence.RunStartedAt);
            Require(fences.Count == 0 || started > fences[^1], "attempt-fences");
            fences.Add(started);
        }
        var roles = expected.Roles.ToDictionary(role => (role.Role, role.ShardIndex));
        var byName = expected.Roles.ToDictionary(role => role.JobName, StringComparer.Ordinal);
        var histories = roles.Keys.ToDictionary(key => key, _ => new History());
        var ids = new HashSet<long>();
        foreach (var job in jobs.Jobs)
        {
            Require(job is not null && job.Id > 0 && ids.Add(job.Id) && job.RunId == expected.RunId &&
                job.RunAttempt >= 1 && job.RunAttempt <= expected.CurrentAttempt && Text(job.Name) && Text(job.Status) &&
                (job.Conclusion is null || Text(job.Conclusion)), "job-owner");
            // Unrelated jobs remain in the complete pagination witness; only
            // independently controlled exact API names identify these roles.
            if (!byName.TryGetValue(job.Name, out var role)) continue;
            var history = histories[(role.Role, role.ShardIndex)];
            Require(history.Advertised.TryAdd(job.RunAttempt, job), "duplicate-role-attempt-job");
        }
        foreach (var history in histories.Values)
        {
            foreach (var pair in history.Advertised.OrderBy(item => item.Key))
            {
                var attempt = pair.Key;
                var job = pair.Value;
                // Failure/unfinished owners block fallback even if no runner,
                // timestamps, steps or artifact were ever emitted.
                if (!Success(job)) { history.Actual.Add(attempt, job); continue; }
                var execution = ReadExecution(job, expected.Commit);
                if (execution.Started >= fences[attempt - 1])
                {
                    history.Actual.Add(attempt, job);
                    history.Executions.Add(attempt, execution);
                    continue;
                }
                Require(execution.Completed < fences[attempt - 1], "overlapping-cached-job");
                Require(history.Executions.Count(item => item.Key < attempt && item.Value.Stamp == execution.Stamp) == 1,
                    "unmatched-or-ambiguous-cached-job");
            }
        }
        Require(input.Artifacts is { Count: > 0 and <= MaximumMembers }, "artifact-set");
        foreach (var artifact in input.Artifacts)
        {
            Require(artifact is not null && artifact.Owner is not null, "artifact-role");
            var owner = artifact.Owner;
            Require(owner.SchemaVersion == 1 && owner.Kind == "coverage-ci-operation-owner" &&
                roles.ContainsKey((owner.Role, owner.ShardIndex)) && owner.Attempt >= 1 && owner.Attempt <= expected.CurrentAttempt,
                "artifact-role");
            Require(histories[(owner.Role, owner.ShardIndex)].Artifacts.TryAdd(owner.Attempt, artifact), "duplicate-role-attempt-artifact");
        }
        // Preparation is selected before any shard payload is filtered. Even
        // equal copied seals cannot rehabilitate an older preparation job.
        var prepareRole = roles[(FullCoverageCiRole.Prepare, null)];
        var preparation = SelectOwner(histories[(FullCoverageCiRole.Prepare, null)], prepareRole, expected, null);
        var shards = new List<FullCoverageCiSelectedOwner>();
        for (var index = 0; index < expected.ShardCount; index++)
        {
            var role = roles[(FullCoverageCiRole.Shard, index)];
            shards.Add(SelectOwner(histories[(FullCoverageCiRole.Shard, index)], role, expected, preparation));
        }
        return new(1, expected.Repository, expected.Commit, expected.Tree, expected.WorkflowName, expected.RunId,
            expected.CurrentAttempt, input.JobsSha256, expected.Preparation.Prepared.PreparedSha256, expected.ShardCount,
            expected.MaximumConcurrentMachines, preparation, shards.AsReadOnly());
    }

    private static FullCoverageCiSelectedOwner SelectOwner(History history, FullCoverageCiRoleExpectation role,
        FullCoverageCiContext expected, FullCoverageCiSelectedOwner? preparation)
    {
        var attempts = history.Actual.Keys.Concat(history.Artifacts.Keys).ToArray();
        Require(attempts.Length > 0, "missing-latest-job");
        var latest = attempts.Max();
        Require(history.Actual.TryGetValue(latest, out var job), "missing-latest-job");
        Require(Success(job!), "unsuccessful-latest-job");
        Require(history.Artifacts.TryGetValue(latest, out var artifact), "missing-latest-artifact");
        var selectedJob = job!;
        var selectedArtifact = artifact!;
        var execution = history.Executions[latest];
        foreach (var name in role.RequiredSteps)
        {
            var steps = selectedJob.Steps!.Where(step => step.Name == name).ToArray();
            Require(steps.Length == 1 && steps[0].Status == "completed" && steps[0].Conclusion == "success", "required-step");
        }
        var owner = selectedArtifact.Owner;
        var key = RoleKey(role.Role, role.ShardIndex);
        var originalRoot = OriginalRootFor(expected, role.Role, role.ShardIndex, latest);
        var evidencePath = Path.Combine(originalRoot, role.EvidenceRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var artifactName = "coverage-" + key + "-" + expected.Commit + "-" + expected.RunId.ToString(CultureInfo.InvariantCulture) +
            "-" + latest.ToString(CultureInfo.InvariantCulture);
        Require(owner.Repository == expected.Repository && owner.Commit == expected.Commit && owner.Tree == expected.Tree &&
            owner.WorkflowName == expected.WorkflowName && owner.WorkflowRunId == expected.RunId && owner.JobId == selectedJob.Id &&
            owner.ShardCount == expected.ShardCount && owner.OriginalRoot == originalRoot && owner.ArtifactName == artifactName &&
            Hash(selectedArtifact.OwnerRecordSha256), "artifact-owner");
        var finished = Utc(owner.FinishedUtc);
        Require(finished >= execution.Started && finished <= execution.Completed, "owner-time");
        if (preparation is null)
        {
            Require(evidencePath == expected.Preparation.Prepared.PreparedPath, "prepared-path");
            Require(owner.PreparationJobId == selectedJob.Id && owner.PreparationAttempt == latest, "mixed-preparation");
        }
        else
        {
            Require(owner.PreparationJobId == preparation.ActualJobId && owner.PreparationAttempt == preparation.ActualAttempt,
                "mixed-preparation");
            Require(execution.Started >= preparation.CompletedUtcTicks, "shard-before-preparation");
        }
        Require(owner.Preparation == expected.Preparation, "preparation-binding");
        Require(Hash(owner.ArchiveSha256) && owner.ArchiveSha256 == selectedArtifact.ObservedArchiveSha256, "archive-binding");
        ValidateInventory(owner.Inventory);
        ValidateInventory(selectedArtifact.ObservedInventory);
        Require(owner.Inventory.Directories.SequenceEqual(selectedArtifact.ObservedInventory.Directories, StringComparer.Ordinal) &&
            owner.Inventory.Files.SequenceEqual(selectedArtifact.ObservedInventory.Files), "inventory-mismatch");
        var evidence = owner.Inventory.Files.SingleOrDefault(file => file.Path == role.EvidenceRelativePath);
        Require(evidence is not null && Hash(owner.EvidenceSha256) && evidence.Sha256 == owner.EvidenceSha256 && evidence.Length > 0,
            "operation-evidence");
        if (role.Role == FullCoverageCiRole.Prepare)
            Require(owner.EvidenceSha256 == expected.Preparation.Prepared.PreparedSha256, "prepared-seal");
        var inventorySha256 = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(owner.Inventory, Json)));
        return new(role.Role, role.ShardIndex, latest, selectedJob.Id, owner.PreparationJobId, owner.PreparationAttempt,
            selectedJob.Name, execution.Started, execution.Completed, artifactName, originalRoot, selectedArtifact.OwnerRecordSha256,
            owner.ArchiveSha256, inventorySha256, evidencePath, owner.EvidenceSha256);
    }

    private static Execution ReadExecution(FullCoverageCiJob job, string commit)
    {
        Require(job.HeadSha is not null && GitHash(job.HeadSha.ToLowerInvariant()) && job.HeadSha.ToLowerInvariant() == commit, "execution-sha");
        var started = Utc(job.StartedAt);
        var completed = Utc(job.CompletedAt);
        Require(completed >= started && job.RunnerId > 0 && Text(job.RunnerName) &&
            job.Steps is { Count: > 0 and <= MaximumMembers }, "execution-identity");
        var steps = new List<object>();
        long previous = 0;
        foreach (var step in job.Steps)
        {
            Require(step is not null && step.Number > previous && Text(step.Name) && Text(step.Status) &&
                (step.Conclusion is null || Text(step.Conclusion)), "execution-step");
            long? start = null, end = null;
            if (step.StartedAt is null && step.CompletedAt is null) Require(step.Conclusion == "skipped", "execution-step-time");
            else
            {
                start = Utc(step.StartedAt); end = Utc(step.CompletedAt);
                Require(start >= started && end >= start && end <= completed, "execution-step-time");
            }
            steps.Add(new { step.Number, step.Name, step.Status, step.Conclusion, started = start, completed = end });
            previous = step.Number;
        }
        // API IDs and advertised attempts are omitted exactly as in the hybrid
        // witness. Every controlled execution/runner/ordered-step fact remains.
        var stamp = JsonSerializer.Serialize(new { job.Name, headSha = commit, job.Status, job.Conclusion,
            started, completed, job.RunnerId, job.RunnerName, steps }, Json);
        return new(started, completed, stamp);
    }

    internal static void ValidateContext(FullCoverageCiContext context)
    {
        Require(context is not null && context.SchemaVersion == 1 && context.EventName == "push" && context.Ref == "refs/heads/main" &&
            Text(context.Repository) && Regex.IsMatch(context.Repository, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant) &&
            GitHash(context.Commit) && GitHash(context.Tree) && Text(context.WorkflowName) && context.RunId > 0 &&
            context.CurrentAttempt is >= 1 and <= MaximumMembers && context.ShardCount is >= 1 and <= 100_000 &&
            context.MaximumConcurrentMachines is >= 1 and <= 4 && Hash(context.JobsSha256) && Absolute(context.OperationRoot), "context");
        var binding = context.Preparation;
        Require(binding is not null && binding.Source is not null && binding.Prepared is not null &&
            binding.Source.Commit == context.Commit && binding.Source.Tree == context.Tree && Hash(binding.Source.Fingerprint) &&
            binding.Prepared.SchemaVersion == 1 && Hash(binding.Prepared.PreparedSha256) &&
            binding.RunId is { Length: 32 } && binding.RunId.All(LowerHex) && Guid.TryParseExact(binding.RunId, "N", out var run) && run != Guid.Empty &&
            Hash(binding.SnapshotContextSha256) && Hash(binding.MetadataToolSha256) && Hash(binding.BuildAuthoritySha256) &&
            Hash(binding.DotCoverToolSha256) && Hash(binding.VstestConsoleSha256) && binding.SdkVersion == "10.0.403" &&
            binding.Configuration == "Release" && binding.TargetFramework == "net10.0-windows" &&
            binding.InstrumentationPolicyVersion == CoverageInstrumentationPolicy.CurrentVersion &&
            new[] { binding.Prepared.PreparedPath, binding.Prepared.SourceDirectory, binding.Prepared.SnapshotContextPath,
                binding.Prepared.MetadataToolFile, binding.Prepared.DotnetFile, binding.Prepared.ShellFile, binding.Prepared.DotCoverToolFile }.All(Absolute),
            "preparation-context");
        Require(context.Roles is not null && context.Roles.Count == context.ShardCount + 1, "role-plan");
        var keys = new HashSet<(FullCoverageCiRole Role, int? ShardIndex)>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in context.Roles)
        {
            Require(role is not null && ((role.Role == FullCoverageCiRole.Prepare && role.ShardIndex is null) ||
                (role.Role == FullCoverageCiRole.Shard && role.ShardIndex >= 0 && role.ShardIndex < context.ShardCount)) &&
                keys.Add((role.Role, role.ShardIndex)) && Text(role.JobName) && names.Add(role.JobName) &&
                Relative(role.EvidenceRelativePath) && role.RequiredSteps is { Count: > 0 and <= 100 } &&
                role.RequiredSteps.All(Text) && role.RequiredSteps.Distinct(StringComparer.Ordinal).Count() == role.RequiredSteps.Count, "role-plan");
        }
        Require(keys.Contains((FullCoverageCiRole.Prepare, null)) &&
            Enumerable.Range(0, context.ShardCount).All(index => keys.Contains((FullCoverageCiRole.Shard, index))), "role-plan");
    }

    // Only the independently supplied context controls the absolute owner root.
    // This validates bounded role/index/attempt inputs before any archive read;
    // the caller validates the complete context once before processing members.
    public static string OriginalRootFor(FullCoverageCiContext context, FullCoverageCiRole role, int? index, int attempt)
    {
        Require(context is not null && Absolute(context.OperationRoot) && context.ShardCount is >= 1 and <= 100_000 &&
            context.CurrentAttempt is >= 1 and <= MaximumMembers && attempt >= 1 && attempt <= context.CurrentAttempt &&
            ((role == FullCoverageCiRole.Prepare && index is null) ||
             (role == FullCoverageCiRole.Shard && index >= 0 && index < context.ShardCount)), "artifact-role");
        return Path.Combine(context.OperationRoot, RoleKey(role, index), "attempt-" + attempt.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidateInventory(FullCoverageCiInventory inventory)
    {
        Require(inventory is not null && inventory.Directories is not null && inventory.Files is { Count: > 0 } &&
            (long)inventory.Directories.Count + inventory.Files.Count <= MaximumMembers &&
            inventory.Directories.SequenceEqual(inventory.Directories.Order(StringComparer.Ordinal), StringComparer.Ordinal) &&
            inventory.Files.All(file => file is not null) &&
            inventory.Files.Select(file => file.Path).SequenceEqual(inventory.Files.Select(file => file.Path).Order(StringComparer.Ordinal), StringComparer.Ordinal),
            "inventory-shape");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in inventory.Directories)
            Require(Relative(directory) && names.Add(directory) && directories.Add(directory), "inventory-shape");
        long bytes = 0;
        foreach (var file in inventory.Files)
        {
            Require(Relative(file.Path) && names.Add(file.Path) && file.Length is >= 0 and <= 256L * 1024 * 1024 && Hash(file.Sha256), "inventory-shape");
            bytes = checked(bytes + file.Length);
            Require(bytes <= 8L * 1024 * 1024 * 1024, "inventory-shape");
        }
        foreach (var name in names)
        {
            var parent = name.LastIndexOf('/');
            Require(parent < 0 || directories.Contains(name[..parent]), "inventory-shape");
        }
    }
    private static bool Success(FullCoverageCiJob job) => job.Status == "completed" && job.Conclusion == "success";
    private static string RoleKey(FullCoverageCiRole role, int? index) => role == FullCoverageCiRole.Prepare ? "prepare" :
        "shard-" + index!.Value.ToString("D6", CultureInfo.InvariantCulture);
    private static long Utc(string? text)
    {
        Require(Text(text, 64) && Regex.IsMatch(text!,
            "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\\.[0-9]{1,7})?(?:Z|\\+00:00)\\z", RegexOptions.CultureInvariant) &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time) && time.Offset == TimeSpan.Zero,
            "invalid-time");
        return DateTimeOffset.Parse(text!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcTicks;
    }
    private static bool Absolute(string value) => Text(value, 32767) && value.Length >= 3 && char.IsAsciiLetter(value[0]) &&
        value[1] == ':' && value[2] == '\\' && !value[2..].Contains(':') &&
        value[3..].Split('\\').All(Part) && !value.EndsWith('\\');
    private static bool Relative(string value) => Text(value, 32767) && !value.Contains('\\') && !Path.IsPathRooted(value) && value.Split('/').All(Part);
    private static bool Part(string value)
    {
        if (value.Length == 0 || value is "." or ".." || value.EndsWith('.') || value.EndsWith(' ') ||
            value.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']) >= 0 || value.Any(char.IsControl) ||
            Regex.IsMatch(value, "~[0-9]")) return false;
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (++index >= value.Length || !char.IsLowSurrogate(value[index])) return false;
            }
            else if (char.IsLowSurrogate(value[index])) return false;
        }
        var stem = value.Split('.')[0].ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$") && !Regex.IsMatch(stem, "^(COM|LPT)[0-9¹²³]$");
    }
    private static bool Text(string? value) => Text(value, 1024);
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(LowerHex);
    private static bool GitHash(string? value) => value is { Length: 40 or 64 } && value.All(LowerHex);
    private static bool LowerHex(char value) => value is >= '0' and <= '9' or >= 'a' and <= 'f';
    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    { if (!condition) throw new FullCoverageCiProvenanceException(code); }
}
