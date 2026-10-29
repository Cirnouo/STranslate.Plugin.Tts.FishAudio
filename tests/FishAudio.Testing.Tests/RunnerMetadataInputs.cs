using System.IO.Compression;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FishAudio.Testing.Evidence;
using FishAudio.Testing.Execution;

namespace FishAudio.Testing.Tests;

// Execution-only reader for an existing outer driver's captured artifacts.
// It never starts discovery, creates a fixture, writes output, searches ancestors,
// or grants identity to a copied build. The raw request already retains its JSON.
internal static class RunnerMetadataInputs
{
    internal const string ContextVariable = "FISHAUDIO_RUNNER_METADATA_JSON";
    internal const string RequestVariable = "FISHAUDIO_RUNNER_REQUEST_PATH";
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal sealed record FilePair(string Assembly, string AssemblySha256, string Pdb, string? PdbSha256);
    internal sealed record CatalogPair(string Key, string Assembly, string AssemblySha256, string Pdb,
        string? PdbSha256, string Discovery, string DiscoverySha256, string Catalog, string CatalogSha256);
    internal sealed record Context(int SchemaVersion, string Kind, string RunId, string RepositoryRoot,
        string SessionDirectory, string? SourceTree, string SourceFingerprint, string SourcePath,
        string SourceSha256, string? SnapshotContextPath, string? SnapshotContextSha256,
        IReadOnlyList<CatalogPair> Assemblies, FilePair Tool);
    internal sealed record Binding(Context Context, FrameworkRunRequest Request);
    internal sealed record SdkDependencyEvidence(string PackageId, string Version, string ZipEntry,
        string LockSha256, string AssetsSha256, string MetadataSha256, string DeclaredContentHash,
        string RawArchiveSha512, string CacheArchiveSha512, string SourceSha256,
        string PdbHashAlgorithm, string PdbChecksum, string AssemblySha256, string PdbSha256)
    {
        public int SchemaVersion => 1;
        public string Kind => "runner-sdk-compiler-dependency";
        // Declared contentHash and signed archive raw SHA512 are separate facts.
        public bool RawArchiveHashEquivalentToLockContentHash => false;
        public bool PackageSignatureVerified => false;
        public bool CompleteBuildInputClosure => false;
        public bool CompleteBaseline => false;
    }
    private const string SdkId = "Microsoft.NET.Test.Sdk";
    private const string SdkVersion = "18.11.0";
    private const string SdkEntry = "build/net8.0/Microsoft.NET.Test.Sdk.Program.cs";
    private const string SdkContentHash = "xf/bR0LNkhK0SnadDRlZIQmC4sac5Wk0e1WAdiQjRN3MSo6gQ9M2c4c1XRNnVGjfOvckh704W5CJ19AL0/Xsdg==";
    private static readonly Guid Sha1Algorithm = new("ff1816ec-aa5e-4d10-87f7-6f4963833460");
    private static readonly Guid Sha256Algorithm = new("8829d00f-11b8-4213-878b-770e8597ac16");
    internal sealed class Rejected(string code) : Exception("runner-metadata-" + code);

    internal static Context ReadActual(string root, Func<string, string?>? readEnvironment = null,
        Action<SdkDependencyEvidence>? verifiedDependency = null)
    {
        var binding = ReadBinding(root, readEnvironment);
        ValidateInputs(binding.Context, binding.Request, root, verifiedDependency);
        return binding.Context;
    }

    // Fault controls retain the actual outer binding, then submit only an invalid
    // input value to the same admission boundary. They never admit fake contexts.
    internal static Binding ReadBinding(string root, Func<string, string?>? readEnvironment = null)
    {
        readEnvironment ??= Environment.GetEnvironmentVariable;
        var guards = new List<FileStream>();
        var failed = false;
        try
        {
            var text = readEnvironment(ContextVariable);
            var requestPath = readEnvironment(RequestVariable);
            var controlPath = readEnvironment("FISHAUDIO_FRAMEWORK_CONTROL_CONTEXT");
            Require(!string.IsNullOrWhiteSpace(text) && text.Length <= 32767 &&
                !string.IsNullOrWhiteSpace(requestPath) && !string.IsNullOrWhiteSpace(controlPath), "missing-context");
            var context = Parse<Context>(text!);
            var request = Read<FrameworkRunRequest>(requestPath!, guards);
            var control = Read<FrameworkClientContext>(controlPath!, guards);
            Require(request.Environment is not null &&
                request.Environment.TryGetValue(ContextVariable, out var pinned) && pinned == text &&
                request.Environment.TryGetValue(RequestVariable, out var pinnedRequest) && Same(pinnedRequest, requestPath!) &&
                Same(controlPath!, Path.Combine(request.OutputDirectory, "control-context.json")) &&
                Same(requestPath!, Path.Combine(Path.GetDirectoryName(request.OutputDirectory)!, "execution-request.json")),
                "request-context-binding");
            Require(control.SchemaVersion == 1 && control.RunId == request.RunId && control.SourceTree == request.SourceTree &&
                Same(control.TestAssembly, request.TestAssembly) && control.TestAssemblySha256 == request.TestAssemblySha256 &&
                control.Cases.SequenceEqual(request.Cases) && Same(request.WorkingDirectory, root) &&
                Same(request.TestAssembly, typeof(RunnerMetadataInputs).Assembly.Location) &&
                Hash(request.TestAssembly) == request.TestAssemblySha256, "executing-owner-binding");
            Require(context.SchemaVersion == 1 && context.Kind == "runner-readonly-metadata" &&
                context.RunId == request.RunId && Same(context.RepositoryRoot, root), "context-binding");
            return new(context, request);
        }
        catch (Rejected) { failed = true; throw; }
        catch (Exception error) when (IsInputFailure(error)) { failed = true; throw new Rejected("invalid-context"); }
        finally { Release(guards, failed); }
    }

    internal static void ValidateInputs(Context context, FrameworkRunRequest request, string root,
        Action<SdkDependencyEvidence>? verifiedDependency = null)
    {
        var guards = new List<FileStream>();
        var failed = false;
        var dependencies = new List<SdkDependencyEvidence>();
        try
        {
            // Four owned directory levels: session/key/execution/phase/supervised.
            var session = Path.GetFullPath(Path.Combine(request.OutputDirectory, "../../../.."));
            Require(context.SchemaVersion == 1 && context.Kind == "runner-readonly-metadata" &&
                context.RunId == request.RunId && Same(context.RepositoryRoot, root) &&
                Same(context.SessionDirectory, session) && Same(context.SourcePath, Path.Combine(session, "source.json")) &&
                context.SourceTree == request.SourceTree && context.SourceFingerprint == request.SourceFingerprint,
                "source-binding");
            using var source = ReadDocument(context.SourcePath, context.SourceSha256, guards);
            var identity = source.RootElement;
            Require(identity.GetProperty("fingerprint").GetString() == context.SourceFingerprint &&
                identity.GetProperty("tree").GetString() == context.SourceTree &&
                identity.GetProperty("fingerprintKind").GetString() == "sha256-source-path-and-bytes-ordinal-v1", "source-binding");
            var snapshot = Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT");
            if (identity.GetProperty("snapshot").GetBoolean())
            {
                Require(snapshot is not null && context.SourceTree is not null &&
                    Same(snapshot, context.SnapshotContextPath!) &&
                    context.SnapshotContextSha256 == identity.GetProperty("contextSha256").GetString() &&
                    identity.GetProperty("snapshotRootValidated").GetBoolean(), "snapshot-binding");
                using var original = ReadDocument(snapshot!, context.SnapshotContextSha256!, guards);
                Require(original.RootElement.GetProperty("schemaVersion").GetInt32() == 1 &&
                    original.RootElement.GetProperty("tree").GetString() == context.SourceTree &&
                    Same(original.RootElement.GetProperty("root").GetString()!, root), "snapshot-binding");
            }
            else
                Require(snapshot is null && context.SourceTree is null && context.SnapshotContextPath is null &&
                    context.SnapshotContextSha256 is null, "workspace-binding");

            Require(context.Assemblies is { Count: 2 } && context.Assemblies.All(item => item is not null) &&
                context.Tool is not null && context.Assemblies.Select(item => item.Key)
                .SequenceEqual(new[] { "plugin", "testing-tool" }, StringComparer.Ordinal), "assembly-binding");
            var projects = new[] { "tests/STranslate.Plugin.Tts.FishAudio.Tests", "tests/FishAudio.Testing.Tests" };
            var names = new[] { "STranslate.Plugin.Tts.FishAudio.Tests", "FishAudio.Testing.Tests" };
            for (var index = 0; index < 2; index++)
            {
                var item = context.Assemblies[index];
                var expected = Path.Combine(root, projects[index], "bin/Release/net10.0-windows", names[index] + ".dll");
                Require(Same(item.Assembly, expected) && Same(item.Pdb, Path.ChangeExtension(expected, ".pdb")) &&
                    Same(item.Catalog, Path.Combine(session, item.Key, "catalog.json")) &&
                    Within(item.Discovery, Path.Combine(session, "phases")) &&
                    Path.GetFileName(item.Discovery) == "discovery.dump" &&
                    Path.GetFileName(Path.GetDirectoryName(item.Discovery)!).EndsWith("-discover-" + item.Key, StringComparison.Ordinal),
                    "assembly-binding");
                var dependency = VerifyBinaryAndSources(new(item.Assembly, item.AssemblySha256, item.Pdb, item.PdbSha256), root, guards);
                if (dependency is not null) dependencies.Add(dependency);
                _ = Open(item.Discovery, item.DiscoverySha256 ?? "", guards);
                using var catalog = ReadDocument(item.Catalog, item.CatalogSha256, guards);
                var actual = NUnitEvidence.ReadCatalog(item.Discovery);
                var raw = NUnitEvidence.ReadDiscoveryDocument(item.Discovery);
                var owner = raw.Descendants("test-suite").Where(node => (string?)node.Attribute("type") == "Assembly").ToArray();
                Require(owner.Length == 1 && Same((string?)owner[0].Attribute("fullname") ?? "", item.Assembly) &&
                    (string?)owner[0].Attribute("name") == names[index] + ".dll", "discovery-assembly");
                Require(catalog.RootElement.GetProperty("schemaVersion").GetInt32() == 1 &&
                    catalog.RootElement.GetProperty("kind").GetString() == "nunit-catalog" &&
                    catalog.RootElement.GetProperty("complete").GetBoolean() &&
                    catalog.RootElement.GetProperty("cases").Deserialize<DiscoveredNUnitCase[]>(Json)!.SequenceEqual(actual), "catalog-binding");
            }
            var executing = context.Assemblies[1];
            Require(Same(executing.Assembly, request.TestAssembly) && executing.AssemblySha256 == request.TestAssemblySha256 &&
                Same(executing.Discovery, request.DiscoveryPath), "executing-owner-binding");
            var tool = context.Tool;
            var expectedTool = Path.Combine(root, "tools/FishAudio.Testing/bin/Release/net10.0-windows/FishAudio.Testing.dll");
            Require(Same(tool.Assembly, expectedTool) && Same(tool.Pdb, Path.ChangeExtension(expectedTool, ".pdb")), "tool-binding");
            _ = VerifyBinary(tool, guards);
            // Normal tool builds do not emit GeneratedRegex source files. Tool
            // linkage/loaded image identity is required; generator closure is not claimed.
            Require(Hash(typeof(BinaryIdentity).Assembly.Location) == tool.AssemblySha256,
                "loaded-tool-binding");
            Require(dependencies.Count == 1, "sdk-document-count");
            foreach (var dependency in dependencies) verifiedDependency?.Invoke(dependency);
        }
        catch (Rejected) { failed = true; throw; }
        catch (Exception error) when (IsInputFailure(error)) { failed = true; throw new Rejected("invalid-input"); }
        finally { Release(guards, failed); }
    }

    private static BinaryIdentityRecord VerifyBinary(FilePair pair, List<FileStream> guards)
    {
        Require(pair.PdbSha256 is { Length: 64 } && pair.AssemblySha256 is { Length: 64 }, "binary-binding");
        _ = Open(pair.Assembly, pair.AssemblySha256, guards);
        _ = Open(pair.Pdb, pair.PdbSha256!, guards);
        var binary = BinaryIdentity.ReadValidated(pair.Assembly, pair.Pdb);
        Require(binary.DllSha256 == pair.AssemblySha256 && binary.PdbSha256 == pair.PdbSha256, "binary-binding");
        return binary;
    }

    private static SdkDependencyEvidence? VerifyBinaryAndSources(FilePair pair, string root, List<FileStream> guards)
    {
        _ = VerifyBinary(pair, guards);
        var pdb = guards[^1];
        pdb.Position = 0;
        using var provider = MetadataReaderProvider.FromPortablePdbStream(pdb, MetadataStreamOptions.LeaveOpen);
        var metadata = provider.GetMetadataReader();
        Require(metadata.Documents.Count is > 0 and <= 65536, "compiler-documents");
        var names = new HashSet<string>(StringComparer.Ordinal);
        SdkDependencyEvidence? dependency = null;
        long sourceBytes = 0;
        foreach (var handle in metadata.Documents)
        {
            var document = metadata.GetDocument(handle);
            Require(!document.Name.IsNil && !document.HashAlgorithm.IsNil && !document.Hash.IsNil, "compiler-document-metadata");
            var name = metadata.GetString(document.Name);
            Require(name.Length is > 0 and <= 32768 && !name.Any(char.IsControl) && names.Add(name), "compiler-document-name");
            if (!name.StartsWith("/_/", StringComparison.Ordinal))
            {
                // This one SDK Compile item is outside Directory.Build.props' map.
                // Package identity, fixed entry and compiled bytes all have to agree.
                Require(dependency is null, "sdk-document-count");
                dependency = VerifySdkSource(pair, root, name, metadata, document, guards);
                continue;
            }
            var relative = name[3..];
            Require(relative.Length > 0 && !relative.Contains('\\') && !Path.IsPathRooted(relative) &&
                relative.Split('/').All(part => part.Length > 0 && part is not "." and not ".."), "compiler-path-map");
            var full = Path.GetFullPath(Path.Combine(root, relative));
            Require(Within(full, root), "compiler-source-root");
            var input = Open(full, expected: null, guards, allowEmpty: true, maximumBytes: 16 * 1024 * 1024);
            sourceBytes = checked(sourceBytes + input.Length);
            Require(sourceBytes <= 256 * 1024 * 1024, "compiler-source-size");
            _ = VerifyDocumentChecksum(metadata, document, input);
        }
        // The metadata contract proves document bytes and binary/PDB linkage.
        // Complete coverage/sequence-point semantics remain the existing tool's owner.
        return dependency;
    }

    private static SdkDependencyEvidence VerifySdkSource(FilePair pair, string root, string name,
        MetadataReader metadata, Document document, List<FileStream> guards)
    {
        var project = Path.Combine(root, "tests/FishAudio.Testing.Tests");
        Require(Same(pair.Assembly, Path.Combine(project, "bin/Release/net10.0-windows/FishAudio.Testing.Tests.dll")),
            "compiler-path-map");
        var lockPath = Path.Combine(project, "packages.lock.json");
        var assetsPath = Path.Combine(project, "obj/project.assets.json");
        using var locked = ReadCurrentDocument(lockPath, guards);
        using var assets = ReadCurrentDocument(assetsPath, guards);
        var lockedRoot = locked.RootElement;
        var assetsRoot = assets.RootElement;
        var package = lockedRoot.GetProperty("dependencies").GetProperty("net10.0-windows7.0").GetProperty(SdkId);
        var library = assetsRoot.GetProperty("libraries").GetProperty(SdkId + "/" + SdkVersion);
        var restore = assetsRoot.GetProperty("project").GetProperty("restore");
        var packageRelative = "microsoft.net.test.sdk/" + SdkVersion;
        Require(lockedRoot.GetProperty("version").GetInt32() == 1 && package.GetProperty("type").GetString() == "Direct" &&
            package.GetProperty("resolved").GetString() == SdkVersion && package.GetProperty("contentHash").GetString() == SdkContentHash &&
            assetsRoot.GetProperty("version").GetInt32() == 4 && library.GetProperty("type").GetString() == "package" &&
            library.GetProperty("path").GetString() == packageRelative && library.GetProperty("sha512").GetString() == SdkContentHash &&
            Same(restore.GetProperty("projectPath").GetString()!, Path.Combine(project, "FishAudio.Testing.Tests.csproj")) &&
            Same(Path.TrimEndingDirectorySeparator(restore.GetProperty("outputPath").GetString()!), Path.Combine(project, "obj")), "sdk-package-binding");
        var files = library.GetProperty("files").EnumerateArray().Select(item => item.GetString()).ToArray();
        Require(files.Count(item => item == SdkEntry) == 1 &&
            assetsRoot.GetProperty("targets").GetProperty("net10.0-windows").GetProperty(SdkId + "/" + SdkVersion)
                .GetProperty("build").TryGetProperty("build/net8.0/Microsoft.NET.Test.Sdk.targets", out _), "sdk-package-binding");
        var packages = restore.GetProperty("packagesPath").GetString()!;
        Require(!string.IsNullOrWhiteSpace(packages) && Path.IsPathFullyQualified(packages) &&
            assetsRoot.GetProperty("packageFolders").EnumerateObject().Any(item => Same(item.Name, packages)), "sdk-package-root");
        var directory = Path.Combine(packages, packageRelative);
        var sourcePath = Path.GetFullPath(Path.Combine(directory, SdkEntry));
        Require(name == sourcePath && Within(sourcePath, directory), "compiler-path-map");
        var metadataPath = Path.Combine(directory, ".nupkg.metadata");
        using var packageMetadata = ReadCurrentDocument(metadataPath, guards);
        Require(packageMetadata.RootElement.GetProperty("version").GetInt32() == 2 &&
            packageMetadata.RootElement.GetProperty("contentHash").GetString() == SdkContentHash, "sdk-package-binding");
        var archive = Open(Path.Combine(directory, "microsoft.net.test.sdk." + SdkVersion + ".nupkg"),
            expected: null, guards, maximumBytes: 16 * 1024 * 1024);
        var rawHash = Convert.ToBase64String(SHA512.HashData(archive));
        archive.Position = 0;
        var cached = Open(Path.Combine(directory, "microsoft.net.test.sdk." + SdkVersion + ".nupkg.sha512"),
            expected: null, guards, maximumBytes: 256);
        using var cachedReader = new StreamReader(cached, leaveOpen: true);
        var cacheHash = cachedReader.ReadToEnd().Trim();
        Require(cacheHash == rawHash, "sdk-archive-cache-digest");
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        Require(zip.Entries.Count is > 0 and <= 256, "sdk-archive-entry");
        var entries = zip.Entries.Where(entry => entry.FullName.Equals(SdkEntry, StringComparison.OrdinalIgnoreCase)).ToArray();
        Require(entries.Length == 1 && entries[0].FullName == SdkEntry && entries[0].Length == 198, "sdk-archive-entry");
        using var entry = entries[0].Open();
        var rawSource = new byte[198];
        entry.ReadExactly(rawSource);
        Require(entry.ReadByte() == -1, "sdk-archive-entry");
        var input = Open(sourcePath, expected: null, guards, maximumBytes: 198);
        Require(input.Length == rawSource.Length, "sdk-source-entry");
        var actualSource = new byte[rawSource.Length];
        input.ReadExactly(actualSource);
        Require(actualSource.AsSpan().SequenceEqual(rawSource), "sdk-source-entry");
        Require(metadata.GetGuid(document.HashAlgorithm) == Sha256Algorithm, "sdk-compiler-checksum");
        var checksum = VerifyDocumentChecksum(metadata, document, input);
        var sourceHash = Convert.ToHexStringLower(SHA256.HashData(rawSource));
        Require(checksum == sourceHash, "sdk-compiler-checksum");
        return new(SdkId, SdkVersion, SdkEntry, Hash(lockPath), Hash(assetsPath), Hash(metadataPath), SdkContentHash,
            rawHash, cacheHash, sourceHash, Sha256Algorithm.ToString("D"), checksum, pair.AssemblySha256, pair.PdbSha256!);
    }

    private static string VerifyDocumentChecksum(MetadataReader metadata, Document document, Stream source)
    {
        var algorithm = metadata.GetGuid(document.HashAlgorithm);
        var checksum = metadata.GetBlobBytes(document.Hash);
        Require((algorithm == Sha1Algorithm && checksum.Length == 20) ||
            (algorithm == Sha256Algorithm && checksum.Length == 32), "compiler-checksum-algorithm");
        source.Position = 0;
        var actual = algorithm == Sha1Algorithm ? SHA1.HashData(source) : SHA256.HashData(source);
        Require(actual.AsSpan().SequenceEqual(checksum), "compiled-source-binding");
        source.Position = 0;
        return Convert.ToHexStringLower(checksum);
    }

    private static JsonDocument ReadCurrentDocument(string path, List<FileStream> guards)
    {
        var stream = Open(path, expected: null, guards, maximumBytes: 16 * 1024 * 1024);
        var document = JsonDocument.Parse(stream);
        try { RejectDuplicateProperties(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }

    private static JsonDocument ReadDocument(string path, string expected, List<FileStream> guards)
    {
        Require(expected is { Length: 64 }, "input-digest");
        var stream = Open(path, expected, guards);
        stream.Position = 0;
        var document = JsonDocument.Parse(stream);
        try { RejectDuplicateProperties(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }

    private static T Read<T>(string path, List<FileStream> guards)
    {
        var stream = Open(path, expected: null, guards);
        stream.Position = 0;
        using var document = JsonDocument.Parse(stream);
        RejectDuplicateProperties(document.RootElement);
        return document.RootElement.Deserialize<T>(Json) ?? throw new Rejected("invalid-context");
    }

    private static T Parse<T>(string text)
    {
        using var document = JsonDocument.Parse(text);
        RejectDuplicateProperties(document.RootElement);
        return document.RootElement.Deserialize<T>(Json) ?? throw new Rejected("invalid-context");
    }

    private static FileStream Open(string path, string? expected, List<FileStream> guards, bool allowEmpty = false, long maximumBytes = 256 * 1024 * 1024)
    {
        Require(!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) &&
            !path.StartsWith(@"\\?\", StringComparison.Ordinal) && !path.StartsWith(@"\\.\", StringComparison.Ordinal), "input-path");
        var file = new FileInfo(Path.GetFullPath(path));
        Require(file.Exists && (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) == 0, "input-kind");
        for (var parent = file.Directory; parent is not null; parent = parent.Parent)
            Require(parent.Exists && (parent.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) == 0, "input-kind");
        var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        guards.Add(stream);
        Require((allowEmpty || stream.Length > 0) && stream.Length <= maximumBytes, "input-size");
        if (expected is not null)
        {
            Require(expected.Length == 64 && expected.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') &&
                Convert.ToHexStringLower(SHA256.HashData(stream)) == expected, "input-digest");
            stream.Position = 0;
        }
        return stream;
    }

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in value.EnumerateObject())
            {
                Require(names.Add(item.Name), "duplicate-property");
                RejectDuplicateProperties(item.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicateProperties(item);
    }

    private static bool IsInputFailure(Exception error) => error is IOException or UnauthorizedAccessException or
        ArgumentException or InvalidOperationException or JsonException or NotSupportedException or BadImageFormatException or OverflowException or
        NUnitEvidenceException or BinaryIdentityException or CryptographicException or KeyNotFoundException;
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }
    private static bool Same(string left, string right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
        Path.IsPathFullyQualified(left) && Path.IsPathFullyQualified(right) &&
        Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    private static bool Within(string path, string parent) => Path.IsPathFullyQualified(path) &&
        Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    private static void Require(bool condition, string code) { if (!condition) throw new Rejected(code); }
    private static void Release(List<FileStream> guards, bool primary)
    {
        var failed = false;
        foreach (var guard in guards) try { guard.Dispose(); } catch { failed = true; }
        if (failed && !primary) throw new Rejected("input-release");
    }
}
