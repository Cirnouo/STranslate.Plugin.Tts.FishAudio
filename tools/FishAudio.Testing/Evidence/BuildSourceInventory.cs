using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;

namespace FishAudio.Testing.Evidence;

/// <summary>
/// Caller-supplied immutable-build expectations. Valid hashes alone do not
/// authenticate the source tree, build owner or complete compiler inputs.
/// </summary>
public sealed record BuildSourceInventoryIdentity(int SchemaVersion, string BuildId, string SourceTree,
    string SourceFingerprint, BinaryIdentityRecord ExpectedAssembly);

public enum BuildSourceFileProvenance { Tracked, BuildGenerated }

public sealed record BuildSourceDocumentEvidence(int DocumentRowId, string DocumentName,
    string SnapshotRelativePath, string Sha256, BuildSourceFileProvenance Provenance, string? GeneratedRoot);

public sealed record BuildSourceInventoryEvidence(int SchemaVersion, BuildSourceInventoryIdentity ExpectedBuild,
    IReadOnlyList<BuildSourceDocumentEvidence> Documents, PortablePdbSourceEvidence SourceEvidence)
{
    public string SourceIdentityAuthority => "caller-supplied";
    public bool BuildInputClosureProven => false;
    public bool GeneratorInputClosureProven => false;
    public bool SourceMappingsResolved => false;
    public bool CompleteBaseline => false;
}

public sealed class BuildSourceInventoryException : Exception
{
    internal BuildSourceInventoryException(string code) : base(code) => Code = code;
    public string Code { get; }
}

/// <summary>
/// Establishes each portable-PDB document's exact byte provenance before the
/// existing PortablePdbSource reader checks checksums and sequence points.
/// Generated roots are explicit snapshot-relative intermediate directories;
/// they cannot cover the snapshot root or any trusted tracked path. Missing
/// tracked input cannot be replaced or reclassified as generated input.
/// </summary>
public static class BuildSourceInventory
{
    private const int MaximumDocuments = 65536;
    private const int MaximumDocumentCharacters = 32768;
    private const int MaximumMetadataTextCharacters = 16 * 1024 * 1024;
    private const long MaximumSourceBytes = 16 * 1024 * 1024;
    private const long MaximumTotalSourceBytes = 256 * 1024 * 1024;

    public static BuildSourceInventoryEvidence ReadValidated(BuildSourceInventoryIdentity expectedBuild,
        string absoluteDll, string absolutePortablePdb, string absoluteSnapshotRoot,
        IReadOnlyList<PortablePdbDocumentMap> documentMaps, IReadOnlyList<PortablePdbSourceFile> trackedFiles,
        IReadOnlyList<string> generatedSourceRoots)
    {
        var sourceLocks = new List<FileStream>();
        try
        {
            Require(expectedBuild is not null && expectedBuild.SchemaVersion == 1 && Hash(expectedBuild.BuildId) &&
                ObjectId(expectedBuild.SourceTree) && Hash(expectedBuild.SourceFingerprint) && expectedBuild.ExpectedAssembly is not null,
                "build-source-inventory-build-identity");
            var expected = expectedBuild with { ExpectedAssembly = expectedBuild.ExpectedAssembly with { } };
            var root = InputPath(absoluteSnapshotRoot);
            Require(Directory.Exists(root), "build-source-inventory-snapshot-root");
            PortablePdbSource.RequireNoReparseAncestors(root, directory: true);
            var tracked = PortablePdbSource.ReadInventory(trackedFiles);
            var maps = PortablePdbSource.ReadMaps(documentMaps);
            var generatedRoots = ReadGeneratedRoots(generatedSourceRoots, tracked.Keys);

            // Hold the two explicit binaries while independently validating their
            // CodeView/PDB linkage and while all compiler documents are admitted.
            var dllPath = InputPath(absoluteDll);
            var pdbPath = InputPath(absolutePortablePdb);
            using var dll = PortablePdbSource.OpenRead(dllPath, 256 * 1024 * 1024);
            using var pdb = PortablePdbSource.OpenRead(pdbPath, 64 * 1024 * 1024);
            var identity = BinaryIdentity.ReadValidated(dllPath, pdbPath);
            Require(identity == expected.ExpectedAssembly, "build-source-inventory-binary-build-mismatch");
            using var provider = MetadataReaderProvider.FromPortablePdbStream(pdb, MetadataStreamOptions.LeaveOpen);
            var metadata = provider.GetMetadataReader();
            Require(metadata.Documents.Count is > 0 and <= MaximumDocuments, "build-source-inventory-document-count");

            var combined = new Dictionary<string, string>(tracked, StringComparer.Ordinal);
            var plans = new Dictionary<int, DocumentPlan>();
            var physicalPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalSourceBytes = 0;
            var metadataTextCharacters = 0;
            foreach (var handle in metadata.Documents)
            {
                var document = metadata.GetDocument(handle);
                Require(!document.Name.IsNil, "build-source-inventory-document-metadata");
                var name = metadata.GetString(document.Name);
                Require(!string.IsNullOrWhiteSpace(name) && name.Length <= MaximumDocumentCharacters && !name.Any(char.IsControl),
                    "build-source-inventory-document-metadata");
                metadataTextCharacters = checked(metadataTextCharacters + name.Length);
                Require(metadataTextCharacters <= MaximumMetadataTextCharacters, "build-source-inventory-metadata-text-limit");
                var matchingMaps = maps.Count(map => name.StartsWith(map.DocumentPrefix, StringComparison.Ordinal));
                Require(matchingMaps <= 1, "build-source-inventory-document-map-ambiguous");
                var relative = PortablePdbSource.MapDocument(name, maps);
                Require(physicalPaths.Add(relative), "build-source-inventory-document-path-alias");

                BuildSourceFileProvenance provenance;
                string? generatedRoot = null;
                var path = PortablePdbSource.ResolveSource(root, relative);
                if (tracked.TryGetValue(relative, out var expectedHash))
                {
                    // A trusted tracked path remains tracked even when it is
                    // missing. Never search a generated directory for a shadow.
                    Require(File.Exists(path), "build-source-inventory-tracked-source-missing");
                    provenance = BuildSourceFileProvenance.Tracked;
                }
                else
                {
                    var matchingRoots = generatedRoots.Where(item => Below(relative, item, StringComparison.Ordinal)).Take(2).ToArray();
                    Require(matchingRoots.Length == 1, "build-source-inventory-generated-source-outside-root");
                    generatedRoot = matchingRoots[0];
                    provenance = BuildSourceFileProvenance.BuildGenerated;
                }

                var input = PortablePdbSource.OpenRead(path, MaximumSourceBytes, allowEmpty: true);
                sourceLocks.Add(input);
                totalSourceBytes = checked(totalSourceBytes + input.Length);
                Require(totalSourceBytes <= MaximumTotalSourceBytes, "build-source-inventory-total-size-limit");
                var hash = Convert.ToHexStringLower(SHA256.HashData(input));
                if (provenance == BuildSourceFileProvenance.Tracked)
                    Require(hash == expectedHash, "build-source-inventory-tracked-source-mismatch");
                else
                    Require(combined.TryAdd(relative, hash), "build-source-inventory-document-path-alias");
                plans.Add(MetadataTokens.GetRowNumber(handle), new(name, relative, hash, provenance, generatedRoot));
            }

            // The generated SHA above binds current bytes, not their legitimacy.
            // The original reader independently checks those bytes against each
            // actual compiler checksum, then validates methods and sequence points.
            var inventory = combined.Select(item => new PortablePdbSourceFile(item.Key, item.Value)).ToArray();
            var sourceEvidence = PortablePdbSource.ReadValidated(dllPath, pdbPath, root, inventory, maps);
            Require(sourceEvidence.Assembly == identity && sourceEvidence.Documents.Count == plans.Count,
                "build-source-inventory-source-evidence-mismatch");
            var documents = new List<BuildSourceDocumentEvidence>(plans.Count);
            foreach (var document in sourceEvidence.Documents)
            {
                Require(plans.TryGetValue(document.RowId, out var plan) && plan.Name == document.DocumentName &&
                    plan.RelativePath == document.SnapshotRelativePath && plan.Sha256 == document.SourceSha256,
                    "build-source-inventory-source-evidence-mismatch");
                documents.Add(new(document.RowId, document.DocumentName, document.SnapshotRelativePath,
                    document.SourceSha256, plan.Provenance, plan.GeneratedRoot));
            }
            return new(1, expected, documents.AsReadOnly(), sourceEvidence);
        }
        catch (BuildSourceInventoryException) { throw; }
        catch (PortablePdbSourceException) { throw; }
        catch (BinaryIdentityException) { throw; }
        catch (Exception error) when (error is BadImageFormatException or OverflowException or IndexOutOfRangeException)
        {
            throw new BuildSourceInventoryException("build-source-inventory-invalid-metadata");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or CryptographicException or System.Security.SecurityException)
        {
            // Never expose filesystem messages, private paths or inner exceptions.
            throw new BuildSourceInventoryException("build-source-inventory-file-read-failed");
        }
        finally
        {
            foreach (var input in sourceLocks) input.Dispose();
        }
    }

    private static string[] ReadGeneratedRoots(IReadOnlyList<string> entries, IEnumerable<string> trackedPaths)
    {
        Require(entries is not null && entries.Count <= 128, "build-source-inventory-generated-root-invalid");
        var roots = entries.ToArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tracked = trackedPaths.ToArray();
        var trackedSet = new HashSet<string>(tracked, StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            Require(PortablePdbSource.RelativePath(root) && seen.Add(root), "build-source-inventory-generated-root-invalid");
            var prefix = root + "/";
            Require(!tracked.Any(path => string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)), "build-source-inventory-generated-root-overlaps-tracked");
            for (var separator = root.IndexOf('/'); separator >= 0; separator = root.IndexOf('/', separator + 1))
                Require(!trackedSet.Contains(root[..separator]), "build-source-inventory-generated-root-overlaps-tracked");
        }
        for (var index = 0; index < roots.Length; index++)
            for (var other = index + 1; other < roots.Length; other++)
                Require(!Overlap(roots[index], roots[other]), "build-source-inventory-generated-root-ambiguous");
        return roots;
    }

    private static bool Below(string path, string root, StringComparison comparison) => path.StartsWith(root + "/", comparison);

    private static bool Overlap(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase) ||
        Below(first, second, StringComparison.OrdinalIgnoreCase) || Below(second, first, StringComparison.OrdinalIgnoreCase);

    private static string InputPath(string path)
    {
        Require(path is not null && path.Length <= MaximumDocumentCharacters && Path.IsPathFullyQualified(path) &&
            NotDevicePath(path.Replace('/', '\\')), "build-source-inventory-path-invalid");
        var rawRoot = Path.GetPathRoot(path);
        // This local build owner admits ordinary absolute drive paths with either
        // separator. UNC/device roots have no canonical owner proof here.
        Require(rawRoot is { Length: 3 } && char.IsAsciiLetter(rawRoot[0]) && rawRoot[1] == ':' && rawRoot[2] is '/' or '\\',
            "build-source-inventory-path-invalid");
        var rawRelative = path[rawRoot.Length..].Replace('\\', '/').TrimEnd('/');
        // Validate before GetFullPath can erase dot/parent/trailing components.
        Require(rawRelative.Length == 0 || PortablePdbSource.RelativePath(rawRelative),
            "build-source-inventory-path-invalid");
        var full = PortablePdbSource.AbsolutePath(path);
        Require(NotDevicePath(full), "build-source-inventory-path-invalid");
        var root = Path.GetPathRoot(full);
        Require(!string.IsNullOrEmpty(root), "build-source-inventory-path-invalid");
        var relative = full[root.Length..].Replace('\\', '/');
        Require(relative.Length == 0 || PortablePdbSource.RelativePath(relative),
            "build-source-inventory-path-invalid");
        return full;
    }

    private static bool NotDevicePath(string path) => !path.StartsWith(@"\\?\", StringComparison.Ordinal) &&
        !path.StartsWith(@"\\.\", StringComparison.Ordinal) && !path.StartsWith(@"\??\", StringComparison.Ordinal);

    private static bool Hash(string value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool ObjectId(string value) => value is { Length: 40 or 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private sealed record DocumentPlan(string Name, string RelativePath, string Sha256,
        BuildSourceFileProvenance Provenance, string? GeneratedRoot);

    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw new BuildSourceInventoryException(code);
    }
}
