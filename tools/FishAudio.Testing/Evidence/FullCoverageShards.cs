using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FishAudio.Testing.Evidence;

public sealed record FullCoverageFilePin(string Path, string Sha256, long Length);
public sealed record FullCoveragePreparedDocument(int SchemaVersion, string RunId, string SourceDirectory,
    string PreparationDirectory, string SnapshotContextPath, CoverageSourceIdentity Source,
    CoverageBaselineAdmission Admission, string MetadataToolFile, string RuntimeEnvironmentPath,
    string RunnerMetadataTemplatePath, IReadOnlyList<string> BuildDirectories, IReadOnlyList<FullCoverageFilePin> Files);
// These paths and this digest come from the caller's independently observed
// preparation, not from the supplied bundle's own success declaration.
public sealed record FullCoveragePreparedExpectation(int SchemaVersion, string PreparedPath, string PreparedSha256,
    string SourceDirectory, string SnapshotContextPath, string MetadataToolFile, string DotnetFile,
    string ShellFile, string DotCoverToolFile);
public sealed record FullCoverageOwnerFiles(string CatalogKey, string InputPath, string ExpectationPath);
public sealed record FullCoverageShardInput(int SchemaVersion, int ShardIndex, int ShardCount,
    string ShardDirectory, string RuntimeEnvironmentPath, IReadOnlyList<FullCoverageOwnerFiles> IsolatedOwners,
    IReadOnlyList<FullCoverageOwnerFiles> OrderedGroups);
public sealed record FullCoverageShardManifest(int SchemaVersion, string PreparedPath, string PreparedSha256,
    int ShardIndex, int ShardCount, string ShardDirectory, FullCoverageFilePin Input,
    FullCoverageFilePin Expectation, FullCoverageFilePin Receipt, FullCoverageFilePin ValidationInvocation,
    FullCoverageFilePin ValidationProcess, FullCoverageFilePin Coordinator);
public sealed record FullCoverageShardManifestPin(string Path, string Sha256);
public sealed record FullCoverageShardCompositionInput(int SchemaVersion, int ShardCount,
    IReadOnlyList<FullCoverageShardManifestPin> Shards);
public sealed record FullCoveragePreparedEvidence(int SchemaVersion, string PreparedSha256, string RunId,
    CoverageSourceIdentity Source, FullCoveragePlanResult WholePlan)
{
    public bool BuildAndCatalogsValidated => true;
    public bool CoverageUnionComplete => false;
    public bool CompleteBaseline => false;
    public bool PublicationAuthorized => false;
    public bool PortableOrCrossRoot => false;
}
public sealed record FullCoverageShardEvidence(int SchemaVersion, string PreparedSha256, string RunId,
    CoverageSourceIdentity Source, int ShardIndex, int ShardCount, IReadOnlyList<string> SelectedOwnerIds,
    IReadOnlyList<string> UnselectedOwnerIds, IReadOnlyList<string> SelectedCanonicalIds)
{
    public bool SelectedOwnersAdmitted => true;
    public bool DependencyRecordsComplete => true;
    public string? EmptySelectionReason => SelectedOwnerIds.Count == 0 ? "deterministic-current-owner-plan-empty" : null;
    public bool CoverageUnionComplete => false;
    public bool CompleteBaseline => false;
    public bool PublicationAuthorized => false;
    public bool TiaSelectionAuthorized => false;
    public bool PortableOrCrossRoot => false;
}
public sealed class FullCoverageShardException : Exception
{
    internal FullCoverageShardException(string code) : base("full-coverage-shards-" + code) => Code = Message;
    public string Code { get; }
}

// This is the existing current-catalog admission composed over independent
// raw owners. It does not execute cases, restore roots or trust report flags.
public static class FullCoverageShards
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 64, Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };
    private sealed record Prepared(FullCoveragePreparedDocument Document, string Sha256, FullCoveragePlanResult Plan);

    internal static FullCoveragePreparedEvidence ReadPrepared(FullCoveragePreparedExpectation expected, List<FileStream> guards)
    {
        var prepared = LoadPrepared(expected, guards);
        return new(1, prepared.Sha256, prepared.Document.RunId, prepared.Document.Source, prepared.Plan);
    }

    internal static FullCoverageShardEvidence ReadShard(FullCoverageShardInput input,
        FullCoveragePreparedExpectation expected, List<FileStream> guards) =>
        ReadShard(input, expected, LoadPrepared(expected, guards), guards).Evidence;

    private static (FullCoverageShardEvidence Evidence, CoverageBaselineCandidateInput Owners) ReadShard(
        FullCoverageShardInput input, FullCoveragePreparedExpectation expected, Prepared prepared, List<FileStream> guards)
    {
        Require(input is not null && input.SchemaVersion == 1 && input.ShardCount is >= 1 and <= 100_000 &&
            input.ShardIndex >= 0 && input.ShardIndex < input.ShardCount && input.IsolatedOwners is not null && input.OrderedGroups is not null,
            "shard-schema-or-range");
        var directory = Root(input.ShardDirectory);
        Require(!Same(directory, prepared.Document.PreparationDirectory) && !Same(directory, expected.SourceDirectory) &&
            !Within(directory, expected.SourceDirectory), "shard-directory");
        Require(Same(input.RuntimeEnvironmentPath, Path.Combine(directory, "runtime-environment.json")), "environment-path");
        using (var original = ReadDocument(prepared.Document.RuntimeEnvironmentPath, guards))
        using (var current = ReadDocument(input.RuntimeEnvironmentPath, guards))
        {
            Require(EnvironmentValue(original.RootElement) == EnvironmentValue(current.RootElement), "environment-drift");
        }
        var plan = Plan(prepared.Document.Admission, input.ShardIndex, input.ShardCount);
        Require((long)input.IsolatedOwners.Count + input.OrderedGroups.Count == plan.SelectedOwnerIds.Count, "selected-owner-count");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        T OwnerFile<T>(string path)
        {
            Require(Within(path, directory) && paths.Add(Absolute(path)), "owner-file-scope-or-alias");
            return Read<T>(path, guards);
        }
        var isolated = input.IsolatedOwners.Select(item =>
        {
            Require(item is not null, "owner-file");
            var raw = OwnerFile<IsolatedCoverageSessionInput>(item.InputPath);
            var admission = OwnerFile<IsolatedCoverageAdmission>(item.ExpectationPath);
            Require(Within(raw.ArtifactDirectory, directory) && admission.Identity.RunId == prepared.Document.RunId, "owner-session-scope");
            return new CoverageBaselineIsolatedOwner(item.CatalogKey, raw, admission);
        }).ToArray();
        var groups = input.OrderedGroups.Select(item =>
        {
            Require(item is not null, "owner-file");
            var raw = OwnerFile<OrderedProfileCoverageGroupInput>(item.InputPath);
            var admission = OwnerFile<OrderedProfileCoverageGroupAdmission>(item.ExpectationPath);
            Require(Within(raw.ArtifactDirectory, directory) && admission.Identity.RunId == prepared.Document.RunId, "owner-session-scope");
            return new CoverageBaselineOrderedGroup(item.CatalogKey, raw, admission);
        }).ToArray();
        var owners = new CoverageBaselineCandidateInput(1, isolated, groups);
        var result = CoverageBaselineCandidate.ReadValidatedSelection(owners, prepared.Document.Admission, plan.SelectedCanonicalIds);
        Require(!result.CoverageUnionComplete && result.DependencyRecordsComplete, "selected-dependencies-incomplete");
        return (new(1, prepared.Sha256, prepared.Document.RunId, prepared.Document.Source, input.ShardIndex, input.ShardCount,
            plan.SelectedOwnerIds, plan.UnselectedOwnerIds, plan.SelectedCanonicalIds), owners);
    }

    internal static CoverageBaselineCandidateEvidence Compose(FullCoverageShardCompositionInput input,
        FullCoveragePreparedExpectation expected, List<FileStream> guards)
    {
        Require(input is not null && input.SchemaVersion == 1 && input.ShardCount is >= 1 and <= 100_000 &&
            input.Shards is not null && input.Shards.Count == input.ShardCount, "complete-shard-set-required");
        var prepared = LoadPrepared(expected, guards);
        var indexes = new HashSet<int>();
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isolated = new List<CoverageBaselineIsolatedOwner>();
        var groups = new List<CoverageBaselineOrderedGroup>();
        foreach (var pin in input.Shards)
        {
            Require(pin is not null && Hash(pin.Sha256), "shard-manifest-pin");
            Require(Path.GetFileName(Absolute(pin.Path)) == "shard.json", "final-shard-manifest-required");
            var stream = Open(pin.Path, guards);
            Require(Digest(stream) == pin.Sha256, "shard-manifest-mismatch");
            var manifest = Read<FullCoverageShardManifest>(pin.Path, guards);
            Require(manifest.SchemaVersion == 1 && manifest.PreparedSha256 == expected.PreparedSha256 &&
                Same(manifest.PreparedPath, expected.PreparedPath) && manifest.ShardCount == input.ShardCount &&
                manifest.ShardIndex >= 0 && manifest.ShardIndex < input.ShardCount && indexes.Add(manifest.ShardIndex) &&
                Same(Path.GetDirectoryName(Absolute(pin.Path))!, manifest.ShardDirectory) &&
                directories.Add(Root(manifest.ShardDirectory)), "shard-identity-or-duplicate");
            foreach (var file in new[] { manifest.Input, manifest.Expectation, manifest.Receipt, manifest.ValidationInvocation,
                manifest.ValidationProcess, manifest.Coordinator })
            {
                Require(file is not null && Within(file.Path, manifest.ShardDirectory), "shard-evidence-scope");
                Pin(file, guards);
            }
            var shardExpected = Read<FullCoveragePreparedExpectation>(manifest.Expectation.Path, guards);
            Require(shardExpected == expected, "mixed-preparation");
            using (var process = ReadDocument(manifest.ValidationProcess.Path, guards)) ValidateProcess(process.RootElement);
            using (var invocation = ReadDocument(manifest.ValidationInvocation.Path, guards))
            {
                var argv = invocation.RootElement;
                Require(Same(argv.GetProperty("executable").GetString()!, expected.DotnetFile) &&
                    Same(argv.GetProperty("workingDirectory").GetString()!, expected.SourceDirectory) &&
                    argv.GetProperty("arguments").EnumerateArray().Select(item => item.GetString()).SequenceEqual(new[]
                    { prepared.Document.MetadataToolFile, "coverage-full-shard-check", "--input", manifest.Input.Path,
                        "--expectation", manifest.Expectation.Path, "--output", manifest.Receipt.Path }, StringComparer.Ordinal), "shard-validation-invocation");
            }
            using (var coordinator = ReadDocument(manifest.Coordinator.Path, guards))
            {
                var outcome = coordinator.RootElement;
                Require(outcome.GetProperty("operation").GetString() == "Shard" &&
                    outcome.GetProperty("operationComplete").GetBoolean() &&
                    outcome.GetProperty("failureType").ValueKind == JsonValueKind.Null &&
                    outcome.GetProperty("reason").ValueKind == JsonValueKind.Null, "failed-shard");
            }
            var shard = Read<FullCoverageShardInput>(manifest.Input.Path, guards);
            Require(shard.ShardIndex == manifest.ShardIndex && shard.ShardCount == manifest.ShardCount &&
                Same(shard.ShardDirectory, manifest.ShardDirectory), "shard-selection-identity");
            // The receipt's complete flags are deliberately not read as proof.
            var admitted = ReadShard(shard, expected, prepared, guards);
            isolated.AddRange(admitted.Owners.IsolatedOwners);
            groups.AddRange(admitted.Owners.OrderedGroups);
        }
        Require(indexes.Count == input.ShardCount, "missing-shard");
        var complete = CoverageBaselineCandidate.ReadValidated(new(1, isolated, groups), prepared.Document.Admission);
        Require(complete.CoverageUnionComplete && complete.DependencyRecordsComplete, "complete-union-incomplete");
        return complete;
    }

    private static Prepared LoadPrepared(FullCoveragePreparedExpectation expected, List<FileStream> guards)
    {
        Require(expected is not null && expected.SchemaVersion == 1 && Hash(expected.PreparedSha256), "preparation-expectation");
        var file = Open(expected.PreparedPath, guards);
        Require(Digest(file) == expected.PreparedSha256, "preparation-digest");
        var document = Read<FullCoveragePreparedDocument>(expected.PreparedPath, guards);
        Require(document.SchemaVersion == 1 && Guid.TryParseExact(document.RunId, "N", out var run) && run != Guid.Empty &&
            Same(document.SourceDirectory, expected.SourceDirectory) && Same(document.SnapshotContextPath, expected.SnapshotContextPath) &&
            Same(document.MetadataToolFile, expected.MetadataToolFile) &&
            Same(document.PreparationDirectory, Path.GetDirectoryName(Absolute(expected.PreparedPath))!) &&
            Path.GetFileName(expected.PreparedPath) == "prepared.json" && document.Source == document.Admission.Source &&
            document.Admission.BuildAuthority.InstrumentationPolicyVersion == CoverageInstrumentationPolicy.CurrentVersion,
            "preparation-identity");
        var source = Root(expected.SourceDirectory);
        var preparation = Root(document.PreparationDirectory);
        Require(!Same(preparation, source) && !Within(preparation, source), "preparation-directory");
        Require(document.Files is { Count: > 0 and <= 200_000 } && document.BuildDirectories is { Count: > 0 and <= 16 }, "preparation-inventory");
        var allowedExternal = new[] { expected.SnapshotContextPath, expected.DotnetFile, expected.ShellFile, expected.DotCoverToolFile };
        var sdk = Path.Combine(Path.GetDirectoryName(Absolute(expected.DotnetFile))!, "sdk", "10.0.403");
        var tool = Path.GetDirectoryName(Absolute(expected.DotCoverToolFile))!;
        var pins = new Dictionary<string, FullCoverageFilePin>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var pin in document.Files)
        {
            Require(pin is not null && (Within(pin.Path, source) || Within(pin.Path, preparation) || Within(pin.Path, sdk) ||
                Within(pin.Path, tool) || allowedExternal.Any(path => Same(pin.Path, path))) && pins.TryAdd(Absolute(pin.Path), pin),
                "preparation-pin-scope-or-alias");
            total = checked(total + pin.Length);
            Require(total <= 8L * 1024 * 1024 * 1024, "preparation-byte-limit");
            Pin(pin, guards);
        }
        FullCoverageFilePin Required(string path)
        {
            Require(pins.TryGetValue(Absolute(path), out var pin), "preparation-required-pin");
            return pin!;
        }
        _ = Required(expected.SnapshotContextPath); _ = Required(expected.DotnetFile); _ = Required(expected.ShellFile);
        Require(Required(expected.DotCoverToolFile).Sha256 == document.Admission.BuildAuthority.DotCoverToolSha256 &&
            Required(Path.Combine(sdk, "vstest.console.dll")).Sha256 == document.Admission.BuildAuthority.VstestConsoleSha256,
            "preparation-toolchain");
        _ = Required(expected.MetadataToolFile); _ = Required(document.RuntimeEnvironmentPath); _ = Required(document.RunnerMetadataTemplatePath);
        Require(Within(document.RuntimeEnvironmentPath, preparation) && Within(document.RunnerMetadataTemplatePath, preparation), "preparation-metadata-scope");
        var sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outputDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(source, ".artifacts") };
        using (var context = ReadDocument(expected.SnapshotContextPath, guards))
        {
            var root = context.RootElement;
            Require(root.GetProperty("schemaVersion").GetInt32() == 1 && Same(root.GetProperty("root").GetString()!, source) &&
                root.GetProperty("tree").GetString() == document.Source.Tree &&
                root.GetProperty("sourceCommit").GetString() == document.Source.Commit, "preparation-source-context");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = root.GetProperty("files").EnumerateArray().ToArray();
            Require(entries.Length is > 0 and <= 100_000, "preparation-source-count");
            foreach (var entry in entries)
            {
                var relative = entry.GetProperty("path").GetString()!;
                Require(!Path.IsPathRooted(relative) && !relative.Split('/', '\\').Any(part => part is "." or ".."), "preparation-source-path");
                var path = Absolute(Path.Combine(source, relative));
                Require(Within(path, source) && names.Add(path) && sourceFiles.Add(path) &&
                    Required(path).Sha256 == entry.GetProperty("sha256").GetString(), "preparation-source-pin");
                if (Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    outputDirectories.Add(Path.Combine(Path.GetDirectoryName(path)!, "bin"));
                    outputDirectories.Add(Path.Combine(Path.GetDirectoryName(path)!, "obj"));
                }
            }
        }
        var pending = new Stack<string>();
        pending.Push(source);
        var physicalFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var physicalCount = 0;
        while (pending.Count > 0)
        {
            foreach (var entry in new DirectoryInfo(pending.Pop()).EnumerateFileSystemInfos())
            {
                Require(++physicalCount <= 200_000 &&
                    (entry.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) == 0, "physical-source-scope-or-limit");
                if (entry is DirectoryInfo)
                {
                    if (outputDirectories.Contains(entry.FullName)) continue;
                    pending.Push(entry.FullName);
                }
                else Require(physicalFiles.Add(Absolute(entry.FullName)) && sourceFiles.Contains(entry.FullName), "unlisted-source-input");
            }
        }
        Require(physicalFiles.SetEquals(sourceFiles), "physical-source-inventory");
        var buildFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in document.BuildDirectories)
        {
            Require(Within(directory, source), "build-directory-scope");
            _ = Root(directory);
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                if (!BuildInput(path)) continue;
                Require(buildFiles.Count < 20_000 && buildFiles.Add(Absolute(path)), "build-inventory-alias-or-limit");
                _ = Required(path);
            }
        }
        var recordedBuildFiles = document.Files.Where(pin => document.BuildDirectories.Any(directory =>
            Same(Path.GetDirectoryName(Absolute(pin.Path))!, directory)) && BuildInput(pin.Path)).Select(pin => Absolute(pin.Path));
        Require(buildFiles.SetEquals(recordedBuildFiles), "build-inventory-drift");
        Require(document.Admission.Catalogs is { Count: 2 } && document.Admission.Catalogs.All(catalog =>
            Within(catalog.DiscoveryPath, preparation) &&
            (Same(catalog.SourceDirectory, source) || Within(catalog.SourceDirectory, source)) &&
            (Same(catalog.BuildDirectory, source) || Within(catalog.BuildDirectory, source))), "preparation-catalog-scope");
        foreach (var catalog in document.Admission.Catalogs) Require(Required(catalog.DiscoveryPath).Sha256 == catalog.DiscoverySha256, "preparation-catalog-pin");
        _ = CoverageBaselineCandidate.ReadValidatedSelection(new(1, [], []), document.Admission, []);
        return new(document, expected.PreparedSha256, Plan(document.Admission, 0, 1));
    }

    private static FullCoveragePlanResult Plan(CoverageBaselineAdmission admission, int index, int count) =>
        FullCoveragePlan.ReadValidated(new(1, admission.Catalogs.Select(item => new FullCoverageCatalogInput(
            item.Key, item.DiscoveryPath, item.DiscoverySha256)).ToArray(), index, count));
    private static string EnvironmentValue(JsonElement root)
    {
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("kind").GetString() == "coverage-full-environment" &&
            root.GetProperty("complete").GetBoolean() && root.GetProperty("value").GetProperty("isWindows").GetBoolean(), "environment-record");
        return root.GetProperty("value").GetRawText();
    }
    private static bool BuildInput(string path) => Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".pdb", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase);
    private static void ValidateProcess(JsonElement value) => Require(value.GetProperty("schemaVersion").GetInt32() == 1 &&
        value.GetProperty("kind").GetString() == "owned-process" && value.GetProperty("complete").GetBoolean() &&
        value.GetProperty("exitCode").GetInt32() == 0 && value.GetProperty("stdoutDrained").GetBoolean() &&
        value.GetProperty("stderrDrained").GetBoolean() && value.GetProperty("outputDrained").GetBoolean() &&
        value.GetProperty("allChildrenExited").GetBoolean() && !value.GetProperty("cleanupFailed").GetBoolean() &&
        value.GetProperty("ownership").GetString() == "windows-job-before-target-go" &&
        value.GetProperty("rootProcessId").GetInt32() > 0 && value.GetProperty("phaseTimeoutMilliseconds").GetInt32() == 0,
        "shard-validation-process");
    private static void Pin(FullCoverageFilePin pin, List<FileStream> guards)
    {
        Require(Hash(pin.Sha256) && pin.Length is >= 0 and <= 256 * 1024 * 1024, "file-pin");
        var stream = Open(pin.Path, guards);
        Require(stream.Length == pin.Length && Digest(stream) == pin.Sha256, "file-pin-mismatch");
    }
    private static string Digest(FileStream stream) { stream.Position = 0; var value = Convert.ToHexStringLower(SHA256.HashData(stream)); stream.Position = 0; return value; }
    private static FileStream Open(string path, List<FileStream> guards)
    {
        path = Absolute(path); _ = Root(Path.GetDirectoryName(path)!);
        Require(File.Exists(path) && (File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Device | FileAttributes.Directory)) == 0, "regular-file");
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); guards.Add(stream);
        Require(stream.Length <= 256 * 1024 * 1024, "file-size"); return stream;
    }
    private static T Read<T>(string path, List<FileStream> guards)
    {
        using var document = ReadDocument(path, guards);
        return document.RootElement.Deserialize<T>(Json) ?? throw new FullCoverageShardException("empty-json");
    }
    private static JsonDocument ReadDocument(string path, List<FileStream> guards)
    {
        var stream = Open(path, guards); Require(stream.Length is > 0 and <= 64 * 1024 * 1024, "json-size");
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 64 });
        var names = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) names.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) names.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName) Require(names.Peek().Add(reader.GetString()!), "duplicate-json-property");
        }
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
    }
    private static string Absolute(string path)
    {
        Require(!string.IsNullOrWhiteSpace(path) && path.Length is >= 3 and <= 32767 && char.IsAsciiLetter(path[0]) && path[1] == ':' &&
            path[2] is '/' or '\\' && !path[2..].Contains(':') && !path.Split('/', '\\').Any(part => part is "." or "..") &&
            Path.IsPathFullyQualified(path), "absolute-regular-path"); return Path.GetFullPath(path);
    }
    private static string Root(string path)
    {
        var value = Path.TrimEndingDirectorySeparator(Absolute(path)); var directory = new DirectoryInfo(value);
        Require(directory.Exists, "directory");
        for (DirectoryInfo? current = directory; current is not null; current = current.Parent)
            Require((current.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) == 0, "reparse-directory");
        return value;
    }
    private static bool Same(string left, string right) => Path.TrimEndingDirectorySeparator(Absolute(left)).Equals(
        Path.TrimEndingDirectorySeparator(Absolute(right)), StringComparison.OrdinalIgnoreCase);
    private static bool Within(string path, string root) => Absolute(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Absolute(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool Hash(string value) => value is { Length: 64 } && value.All(item => item is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void Require([DoesNotReturnIf(false)] bool condition, string code) { if (!condition) throw new FullCoverageShardException(code); }
}
