using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace FishAudio.Testing.Evidence;

public enum CoverageResponsibility { ProductExpected, RepositoryResourcesOnly, FrameworkLifecycleOnly }

public sealed record CoverageDependencyScopeDeclaration(string RelativeDirectory, string Pattern, bool Recursive,
    bool ExpectedDirectoryExists, IReadOnlyList<string> ExpectedMissing);
public sealed record CoverageDependencyScopeWitness(CoverageDependencyScopeDeclaration Declaration,
    IReadOnlyList<CoverageRepositoryDependency> Files, string EnumerationSha256);
public sealed record CoverageCaseDependency(DiscoveredNUnitCase Case, CoverageResponsibility Responsibility,
    IReadOnlyList<int> TestMethodTokens, bool TestSourceResolved, IReadOnlyList<CoverageRepositoryDependency> CodeSources,
    IReadOnlyList<CoverageRepositoryDependency> Resources, IReadOnlyList<CoverageDependencyScopeWitness> Scopes)
{
    // Method/document bytes authenticate provenance. They are not file-level
    // resource selection edges and do not merge distinct parameter-row identities.
    public bool CodeSourcesAreProvenanceOnly => true;
    public bool UnknownFull => true;
}
public sealed record CoverageDependencyObservationInput(int SchemaVersion, CoverageSourceIdentity Source, string BuildId,
    string SourceDirectory, string TestsDll, string TestsPdb, string Discovery, IReadOnlyList<string> CanonicalIds,
    IReadOnlyList<PortablePdbDocumentMap> DocumentMaps)
{
    // Independent snapshot input, not a filesystem search for candidate code.
    public IReadOnlyList<PortablePdbSourceFile> TrackedSources { get; init; } = [];
}
public sealed record CoverageDependencyObservation(int SchemaVersion, CoverageSourceIdentity Source, string BuildId,
    BinaryIdentityRecord TestsBinary, string DiscoverySha256, IReadOnlyList<PortablePdbDocumentMap> DocumentMaps,
    IReadOnlyList<PortablePdbSourceFile> TrackedSources, IReadOnlyList<CoverageCaseDependency> Cases);
public sealed record CoverageDependencyRecord(CoverageDependencyObservation Observation, string OwnerKind,
    string RunId, string SessionId, string FrameworkSupervisorSha256)
{
    public string Authority => "caller-independent-source-build-discovery-and-validated-framework-envelope";
    public string UnknownAction => "full";
    public bool SourceMappingsResolved => false;
    public bool DependencyClosureProven => false;
    public bool CompleteBaseline => false;
    public bool TiaSelectionAuthorized => false;
}
public sealed class CoverageDependencyException : Exception
{
    public string Code { get; }
    internal CoverageDependencyException(string code) : base(code) => Code = "coverage-dependencies-" + code;
}

// Pure metadata/filesystem observation. Its result is an input expectation for
// the live owner, not self-authenticating authority or a human-review receipt.
public static class CoverageDependencies
{
    private const int MaximumCases = 4096;
    private const int MaximumFiles = 8192;
    private const int MaximumScopes = 256;
    private const long MaximumFileBytes = 16 * 1024 * 1024;
    private const long MaximumSourceBytes = 256 * 1024 * 1024;
    private static readonly Guid Sha1 = new("ff1816ec-aa5e-4d10-87f7-6f4963833460");
    private static readonly Guid Sha256 = new("8829d00f-11b8-4213-878b-770e8597ac16");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static CoverageDependencyObservation Observe(CoverageDependencyObservationInput input)
    {
        var guards = new List<FileStream>();
        var completed = false;
        try { var observed = ObserveCore(input, guards); completed = true; return observed; }
        catch (CoverageDependencyException) { throw; }
        catch (Exception error) when (Known(error)) { throw Rejected("input-or-source-rejected"); }
        finally
        {
            var releaseFailed = false;
            foreach (var guard in guards) { try { guard.Dispose(); } catch { releaseFailed = true; } }
            if (completed && releaseFailed) throw Rejected("file-release-failed");
        }
    }

    internal static CoverageDependencyRecord Validate(CoverageDependencyObservation expected,
        CoverageSourceIdentity source, string buildId, string sourceDirectory, string testsDll, string testsPdb,
        string discovery, IReadOnlyList<DiscoveredNUnitCase> selected, string ownerKind, string runId, string sessionId,
        IsolatedCoverageSession.ValidatedDependencyFramework? framework, List<FileStream> guards)
    {
        try
        {
            Require(expected is not null && expected.Source == source && expected.BuildId == buildId &&
                expected.Cases is { Count: > 0 and <= MaximumCases } && framework is not null &&
                framework.Matches(source, buildId, sourceDirectory, testsDll, testsPdb, discovery, selected,
                    ownerKind, runId, sessionId, guards),
                "independent-owner-binding");
            Require(expected.Cases.All(item => item is not null && item.Case is not null), "case-set");
            Require(expected.Cases.Select(item => item.Case).SequenceEqual(selected), "case-set");
            var actual = ObserveCore(new(1, source, buildId, sourceDirectory, testsDll, testsPdb, discovery,
                selected.Select(item => item.CanonicalId).ToArray(), expected.DocumentMaps)
            { TrackedSources = expected.TrackedSources }, guards);
            Require(JsonSerializer.Serialize(actual, Json) == JsonSerializer.Serialize(expected, Json), "observation-changed");
            Require(actual.TestsBinary == framework.TestsBinary && actual.DiscoverySha256 == framework.DiscoverySha256,
                "independent-owner-binding");
            return new(actual, ownerKind, runId, sessionId, framework.SupervisorSha256);
        }
        catch (CoverageDependencyException) { throw; }
        catch (Exception error) when (Known(error)) { throw Rejected("input-or-source-rejected"); }
    }

    internal static bool AllowsEmpty(CoverageDependencyRecord? record) => record is not null &&
        record.Observation.Cases.All(item => item.Responsibility != CoverageResponsibility.ProductExpected &&
            item.TestSourceResolved && item.CodeSources.Count > 0);

    internal static void ValidateNonempty(CoverageDependencyRecord? record)
    {
        if (record is not null)
            Require(record.Observation.Cases.Any(item => item.Responsibility == CoverageResponsibility.ProductExpected),
                "nonproduct-owner-observed-product");
    }

    internal static void Recheck(CoverageDependencyRecord record, string sourceDirectory, List<FileStream> guards)
    {
        var root = Root(sourceDirectory);
        var state = new ReadState(guards);
        foreach (var item in record.Observation.Cases)
        {
            foreach (var file in item.CodeSources.Concat(item.Resources))
                Require(state.File(root, file.RelativePath).Sha256 == file.Sha256, "dependency-changed");
            foreach (var scope in item.Scopes)
                Require(JsonSerializer.Serialize(ReadScope(root, scope.Declaration, state), Json) ==
                    JsonSerializer.Serialize(scope, Json), "enumeration-changed");
        }
    }

    internal static SortedDictionary<string, string[]> DeclaredMetadata(XElement leaf) => new(StringComparer.Ordinal)
    {
        ["FishAudioCoverageResponsibility"] = Property(leaf, "FishAudioCoverageResponsibility"),
        ["FishAudioEmptyProductCoverage"] = Property(leaf, "FishAudioEmptyProductCoverage"),
        ["FishAudioCodeDependency"] = Property(leaf, "FishAudioCodeDependency", merge: true),
        ["FishAudioRepositoryDependency"] = Property(leaf, "FishAudioRepositoryDependency", merge: true),
        ["FishAudioDependencyScope"] = Property(leaf, "FishAudioDependencyScope", merge: true),
    };

    private static CoverageDependencyObservation ObserveCore(CoverageDependencyObservationInput input, List<FileStream> guards)
    {
        Require(input is not null && input.SchemaVersion == 1 && input.Source is not null && Hash(input.Source.Fingerprint) &&
            Hash(input.BuildId) && input.CanonicalIds is { Count: > 0 and <= MaximumCases } &&
            input.DocumentMaps is { Count: > 0 and <= 16 } && input.TrackedSources is { Count: <= 100_000 }, "input");
        var root = Root(input.SourceDirectory);
        var state = new ReadState(guards);
        state.Open(input.TestsDll, 256 * 1024 * 1024);
        state.Open(input.TestsPdb, 64 * 1024 * 1024);
        var discoveryStream = state.Open(input.Discovery, 64 * 1024 * 1024);
        var discoveryHash = Digest(discoveryStream);
        var binary = BinaryIdentity.ReadValidated(input.TestsDll, input.TestsPdb);
        var inventory = PeMethodInventory.ReadValidated(input.TestsDll, input.TestsPdb, binary);
        Require(inventory.Methods.Count <= 200_000, "method-count");
        var methods = inventory.Methods.GroupBy(item => (TypeKey(item.DeclaringType), item.MetadataName))
            .ToDictionary(item => item.Key, item => item.ToArray());
        var linked = inventory.Methods.Where(item => item.StateMachineKickoffMethodToken is not null)
            .GroupBy(item => item.StateMachineKickoffMethodToken!.Value).ToDictionary(item => item.Key, item => item.ToArray());
        var catalog = NUnitEvidence.ReadCatalog(input.Discovery).ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var selected = new List<DiscoveredNUnitCase>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in input.CanonicalIds)
        {
            Require(Text(id) && unique.Add(id) && catalog.ContainsKey(id), "case-set");
            selected.Add(catalog[id]);
        }
        var document = NUnitEvidence.ReadDiscoveryDocument(input.Discovery);
        var leaves = document.Descendants("test-case").ToDictionary(item => (string)item.Attribute("id")!, StringComparer.Ordinal);
        var tracked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in input.TrackedSources)
        {
            Require(source is not null && Hash(source.Sha256), "tracked-source");
            _ = Child(root, source.RelativePath);
            Require(tracked.TryAdd(source.RelativePath, source.Sha256), "tracked-source");
        }
        foreach (var map in input.DocumentMaps)
        {
            Require(map is not null && Text(map.DocumentPrefix) && map.DocumentPrefix.Length <= 4096 &&
                map.DocumentPrefix.EndsWith('/') && !map.DocumentPrefix.Contains('\\') &&
                map.SnapshotRelativePrefix is { Length: <= 4096 } &&
                (map.SnapshotRelativePrefix.Length == 0 || map.SnapshotRelativePrefix.EndsWith('/')), "document-map");
            if (map.SnapshotRelativePrefix.Length > 0) _ = Child(root, map.SnapshotRelativePrefix.TrimEnd('/'));
        }
        using var pdb = new FileStream(Absolute(input.TestsPdb), FileMode.Open, FileAccess.Read, FileShare.Read);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(pdb, MetadataStreamOptions.LeaveOpen);
        var debug = provider.GetMetadataReader();
        Require(debug.Documents.Count is > 0 and <= 16384, "document-count");
        var documents = new Dictionary<int, CodeDocument>();
        var byPath = new Dictionary<string, List<CodeDocument>>(StringComparer.OrdinalIgnoreCase);
        foreach (var handle in debug.Documents)
        {
            var row = debug.GetDocument(handle);
            var name = debug.GetString(row.Name);
            Require(name.Length <= 4096, "document-text");
            var matches = input.DocumentMaps.Where(item => name.StartsWith(item.DocumentPrefix, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1) continue;
            var relative = matches[0].SnapshotRelativePrefix + name[matches[0].DocumentPrefix.Length..];
            if (!tracked.ContainsKey(relative) || Generated(relative)) continue;
            _ = Child(root, relative);
            var algorithm = debug.GetGuid(row.HashAlgorithm);
            Require(debug.GetBlobReader(row.Hash).Length <= 32, "document-checksum");
            var checksum = debug.GetBlobBytes(row.Hash);
            Require((algorithm == Sha1 && checksum.Length == 20) || (algorithm == Sha256 && checksum.Length == 32), "document-checksum");
            var code = new CodeDocument(relative, algorithm, checksum);
            documents.Add(MetadataTokens.GetRowNumber(handle), code);
            if (!byPath.TryGetValue(relative, out var list)) byPath.Add(relative, list = []);
            list.Add(code);
        }
        var cases = new List<CoverageCaseDependency>();
        var usedSources = new Dictionary<string, PortablePdbSourceFile>(StringComparer.OrdinalIgnoreCase);
        var verifiedCode = new Dictionary<string, CoverageRepositoryDependency>(StringComparer.OrdinalIgnoreCase);
        var totalPoints = 0;
        var recordCount = 0;
        foreach (var selectedCase in selected)
        {
            var leaf = leaves[selectedCase.PlatformId];
            var kind = Property(leaf, "FishAudioCoverageResponsibility");
            var legacyReason = leaf.Elements("properties").Elements("property")
                .Where(item => (string?)item.Attribute("name") == "FishAudioEmptyProductCoverage")
                .Select(item => (string?)item.Attribute("value")).ToArray();
            var responsibility = CoverageResponsibility.ProductExpected;
            if (kind.Length != 0)
                Require(kind.Length == 1 && Enum.TryParse(kind[0], ignoreCase: false, out responsibility) &&
                    Enum.IsDefined(responsibility) && Enum.GetName(responsibility) == kind[0], "responsibility-declaration");
            if (legacyReason.Length > 0)
            {
                Require(legacyReason.Length == 1 && legacyReason[0] == "RepositoryResourcesOnly", "responsibility-declaration");
                Require(kind.Length == 0 || responsibility == CoverageResponsibility.RepositoryResourcesOnly,
                    "conflicting-responsibility-declarations");
                responsibility = CoverageResponsibility.RepositoryResourcesOnly;
            }
            var codeSources = new Dictionary<string, CoverageRepositoryDependency>(StringComparer.OrdinalIgnoreCase);
            var declaredCode = Property(leaf, "FishAudioCodeDependency", merge: true);
            Require(declaredCode.Length <= 128 && declaredCode.Distinct(StringComparer.OrdinalIgnoreCase).Count() == declaredCode.Length,
                "code-declaration");
            foreach (var path in declaredCode)
            {
                Require(tracked.ContainsKey(path) && byPath.ContainsKey(path) && !Generated(path), "code-source-not-in-tests-pdb");
                AddCode(path);
            }
            var tokens = new List<int>();
            var resolved = true;
            if (!methods.TryGetValue((TypeKey(selectedCase.ClassName), selectedCase.MethodName), out var candidates))
                resolved = false;
            else
            {
                Require(candidates.Length <= 128, "method-ambiguity-bound");
                foreach (var method in candidates)
                {
                    tokens.Add(method.MetadataToken);
                    var related = linked.TryGetValue(method.MetadataToken, out var bodies) ? bodies : [];
                    var pointsFound = false;
                    foreach (var body in new[] { method }.Concat(related))
                    {
                        var row = body.MetadataToken & 0x00ffffff;
                        Require(row > 0 && row <= debug.MethodDebugInformation.Count, "method-debug-row");
                        var information = debug.GetMethodDebugInformation(MetadataTokens.MethodDebugInformationHandle(row));
                        foreach (var point in information.GetSequencePoints())
                        {
                            Require(++totalPoints <= 2_000_000, "sequence-point-bound");
                            if (point.IsHidden) continue;
                            var handle = point.Document.IsNil ? information.Document : point.Document;
                            if (handle.IsNil || !documents.TryGetValue(MetadataTokens.GetRowNumber(handle), out var source))
                            { resolved = false; continue; }
                            AddCode(source.RelativePath);
                            pointsFound = true;
                        }
                    }
                    resolved &= pointsFound;
                }
            }
            if (responsibility != CoverageResponsibility.ProductExpected)
                Require(resolved && codeSources.Count > 0 &&
                    (responsibility != CoverageResponsibility.FrameworkLifecycleOnly || declaredCode.Length > 0), "nonproduct-source-required");
            var resources = new List<CoverageRepositoryDependency>();
            var resourceNames = Property(leaf, "FishAudioRepositoryDependency", merge: true);
            Require(resourceNames.Length <= 4096 && resourceNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() == resourceNames.Length,
                "resource-declaration");
            foreach (var path in resourceNames)
            {
                Require(++recordCount <= 32768, "dependency-record-count");
                resources.Add(state.File(root, path));
            }
            if (responsibility == CoverageResponsibility.RepositoryResourcesOnly)
                Require(resources.Count > 0, "resource-responsibility-empty");
            var scopeValues = Property(leaf, "FishAudioDependencyScope", merge: true);
            Require(scopeValues.Length <= MaximumScopes, "scope-count");
            var scopes = new List<CoverageDependencyScopeWitness>();
            var scopeKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in scopeValues)
            {
                Require(value.Length <= 32768, "scope-text");
                RejectDuplicateProperties(value);
                var declaration = JsonSerializer.Deserialize<CoverageDependencyScopeDeclaration>(value, Json);
                Require(declaration is not null && scopeKeys.Add(JsonSerializer.Serialize(declaration, Json)), "scope-declaration");
                var scope = ReadScope(root, declaration, state);
                recordCount = checked(recordCount + scope.Files.Count + 1);
                Require(recordCount <= 32768, "dependency-record-count");
                scopes.Add(scope);
            }
            cases.Add(new(selectedCase, responsibility, tokens.Order().ToArray(), resolved,
                codeSources.Values.OrderBy(item => item.RelativePath, StringComparer.Ordinal).ToArray(),
                resources.OrderBy(item => item.RelativePath, StringComparer.Ordinal).ToArray(), scopes.AsReadOnly()));

            void AddCode(string path)
            {
                if (codeSources.ContainsKey(path)) return;
                Require(++recordCount <= 32768, "dependency-record-count");
                if (!verifiedCode.TryGetValue(path, out var file))
                {
                    file = state.File(root, path);
                    Require(file.Sha256 == tracked[path], "tracked-source-changed");
                    foreach (var code in byPath[path])
                    {
                        var stream = state.Open(Child(root, path), MaximumFileBytes);
                        stream.Position = 0;
                        var actual = code.Algorithm == Sha1 ? SHA1.HashData(stream) : SHA256.HashData(stream);
                        Require(actual.AsSpan().SequenceEqual(code.Checksum), "code-checksum");
                    }
                    verifiedCode.Add(path, file);
                }
                codeSources.TryAdd(path, file);
                usedSources.TryAdd(path, new(path, file.Sha256));
            }
        }
        Require(cases.Sum(item => (long)item.CodeSources.Count + item.Resources.Count + item.Scopes.Sum(scope => (long)scope.Files.Count)) <= 32768,
            "dependency-record-count");
        // Enumeration and absence are checked again while every observed file is
        // still held. This is a current snapshot witness, not a directory lease.
        foreach (var item in cases)
            foreach (var scope in item.Scopes)
                Require(JsonSerializer.Serialize(ReadScope(root, scope.Declaration, state), Json) == JsonSerializer.Serialize(scope, Json),
                    "enumeration-changed");
        return new(1, input.Source, input.BuildId, binary, discoveryHash,
            input.DocumentMaps.ToArray(), usedSources.Values.OrderBy(item => item.RelativePath, StringComparer.Ordinal).ToArray(), cases.AsReadOnly());
    }

    private static CoverageDependencyScopeWitness ReadScope(string root, CoverageDependencyScopeDeclaration declaration, ReadState state)
    {
        Require(declaration is not null && declaration.ExpectedMissing is { Count: <= 4096 } &&
            declaration.RelativeDirectory is { Length: <= 4096 } &&
            Text(declaration.Pattern) && declaration.Pattern.Length <= 128 &&
            !declaration.Pattern.Any(character => char.IsControl(character) || "\\/:<>|\"".Contains(character)) &&
            !declaration.Pattern.Contains("..", StringComparison.Ordinal), "scope-declaration");
        var directory = declaration.RelativeDirectory.Length == 0 ? root : Child(root, declaration.RelativeDirectory);
        NoReparse(directory, allowMissing: true);
        Require(!File.Exists(directory) && Directory.Exists(directory) == declaration.ExpectedDirectoryExists, "scope-directory-presence");
        var pending = new Queue<string>();
        var files = new List<CoverageRepositoryDependency>();
        var directories = 0;
        var entriesRead = 0;
        if (declaration.ExpectedDirectoryExists) pending.Enqueue(directory);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            Require(++directories <= 4096, "scope-directory-bound");
            var entries = Directory.EnumerateFileSystemEntries(current).Take(MaximumFiles + 1).ToArray();
            entriesRead = checked(entriesRead + entries.Length);
            Require(entriesRead <= MaximumFiles, "scope-entry-bound");
            foreach (var entry in entries)
            {
                NoReparse(entry, allowMissing: false);
                if (Directory.Exists(entry))
                {
                    if (declaration.Recursive) { Require(pending.Count < 4096, "scope-directory-bound"); pending.Enqueue(entry); }
                    continue;
                }
                if (!System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(declaration.Pattern, Path.GetFileName(entry), ignoreCase: true)) continue;
                Require(files.Count < MaximumFiles, "scope-file-bound");
                files.Add(state.File(root, Path.GetRelativePath(root, entry).Replace('\\', '/')));
            }
        }
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in declaration.ExpectedMissing)
        {
            var path = Child(root, relative);
            Require(path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                (declaration.Recursive || Path.GetDirectoryName(path)!.Equals(directory, StringComparison.OrdinalIgnoreCase)) &&
                System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(declaration.Pattern, Path.GetFileName(path), ignoreCase: true) &&
                missing.Add(relative) && !File.Exists(path) && !Directory.Exists(path), "expected-absence");
            NoReparse(path, allowMissing: true);
        }
        var sorted = files.OrderBy(item => item.RelativePath, StringComparer.Ordinal).ToArray();
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(sorted, Json)))).ToLowerInvariant();
        return new(declaration with { ExpectedMissing = declaration.ExpectedMissing.Order(StringComparer.Ordinal).ToArray() }, sorted, digest);
    }

    private sealed record CodeDocument(string RelativePath, Guid Algorithm, byte[] Checksum);
    private sealed class ReadState(List<FileStream> guards)
    {
        private readonly Dictionary<string, FileStream> _streams = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _digests = new(StringComparer.OrdinalIgnoreCase);
        private long _bytes;
        public FileStream Open(string path, long maximum)
        {
            path = Absolute(path);
            if (_streams.TryGetValue(path, out var held)) { Require(held.Length <= maximum, "file-size"); return held; }
            NoReparse(path, allowMissing: false);
            Require(System.IO.File.Exists(path), "file-required");
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            guards.Add(stream);
            Require(stream.Length <= maximum, "file-size");
            _streams.Add(path, stream);
            return stream;
        }
        public CoverageRepositoryDependency File(string root, string relative)
        {
            var path = Child(root, relative);
            var already = _streams.ContainsKey(path);
            var stream = Open(path, MaximumFileBytes);
            if (!already) { _bytes = checked(_bytes + stream.Length); Require(_bytes <= MaximumSourceBytes && _streams.Count <= MaximumFiles, "source-total-bound"); }
            if (!_digests.TryGetValue(path, out var digest)) { digest = Digest(stream); _digests.Add(path, digest); }
            return new(relative, digest);
        }
    }

    private static string[] Property(XElement leaf, string name, bool merge = false)
    {
        var result = new List<string>();
        foreach (var owner in leaf.AncestorsAndSelf().Where(item => item == leaf ||
            (item.Name == "test-suite" && (string?)item.Attribute("type") is "TestFixture" or "ParameterizedFixture")))
        {
            Require(owner.Elements("properties").Count() <= 1, "duplicate-properties");
            var values = owner.Elements("properties").Elements("property").Where(item => (string?)item.Attribute("name") == name)
                .Select(item => (string?)item.Attribute("value") ?? "").ToArray();
            Require(values.All(item => Text(item) && item.Length <= 32768), "property-value");
            Require(values.Distinct(StringComparer.Ordinal).Count() == values.Length, "duplicate-property-value");
            if (values.Length > 0 && !merge) return values;
            result.AddRange(values);
            Require(result.Count <= 4096, "property-count");
        }
        return result.Distinct(StringComparer.Ordinal).ToArray();
    }
    private static void RejectDuplicateProperties(string value)
    {
        using var document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 8 });
        Require(document.RootElement.ValueKind == JsonValueKind.Object, "scope-json");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject()) Require(names.Add(property.Name), "scope-json-duplicate");
    }
    private static string Digest(FileStream stream)
    {
        stream.Position = 0;
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    private static string Root(string value)
    {
        var full = Absolute(value);
        Require(Directory.Exists(full), "source-root");
        NoReparse(full, allowMissing: false);
        return Path.TrimEndingDirectorySeparator(full);
    }
    private static string Absolute(string value)
    {
        Require(Text(value) && value.Length <= 4096 && value.Length > 3 && char.IsAsciiLetter(value[0]) &&
            value[1] == ':' && value[2] == '\\' && !value[3..].Contains(':') && !value.Contains('/'), "absolute-path");
        var full = Path.GetFullPath(value);
        Require(full.Equals(value, StringComparison.OrdinalIgnoreCase), "path-alias");
        foreach (var part in value[3..].Split('\\')) Component(part);
        return full;
    }
    private static string Child(string root, string relative)
    {
        Require(Text(relative) && relative.Length <= 4096 && !Path.IsPathRooted(relative) &&
            !relative.Contains('\\') && !relative.Contains(':'), "relative-path");
        foreach (var part in relative.Split('/')) Component(part);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        Require(full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "path-outside-root");
        return full;
    }
    private static void Component(string value) => Require(value.Length > 0 && value is not "." and not ".." &&
        !value.EndsWith('.') && !value.EndsWith(' ') && !value.Any(character => char.IsControl(character) || "<>:\"|?*".Contains(character)) &&
        !System.Text.RegularExpressions.Regex.IsMatch(value, @"\A(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|\z)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant), "path-component");
    private static void NoReparse(string path, bool allowMissing)
    {
        FileSystemInfo? current = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (!allowMissing) Require(current.Exists, "path-required");
        for (; current is not null; current = current switch { DirectoryInfo directory => directory.Parent, FileInfo file => file.Directory, _ => null })
            Require(current.LinkTarget is null && (!current.Exists || (current.Attributes & FileAttributes.ReparsePoint) == 0), "reparse-path");
    }
    private static bool Generated(string relative) => relative.Split('/').Any(item => item is "obj" or "bin" or ".artifacts");
    private static string TypeKey(string name) => name.Replace('+', '.');
    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool Known(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or
        BadImageFormatException or NotSupportedException or OverflowException or KeyNotFoundException or JsonException or
        BinaryIdentityException or PeMethodInventoryException or NUnitEvidenceException;
    private static CoverageDependencyException Rejected(string code) => new(code);
    private static void Require([DoesNotReturnIf(false)] bool condition, string code) { if (!condition) throw Rejected(code); }
}
