using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace FishAudio.Testing.Evidence;

/// <summary>A caller-trusted inventory entry for raw bytes under the explicit snapshot root.</summary>
public sealed record PortablePdbSourceFile(string RelativePath, string Sha256);

/// <summary>Maps a rooted compiler prefix ending in a separator to a canonical relative directory, or the empty snapshot-root prefix.</summary>
public sealed record PortablePdbDocumentMap(string DocumentPrefix, string SnapshotRelativePrefix);

public sealed record PortablePdbDocumentEvidence(int RowId, string DocumentName, string SnapshotRelativePath,
    Guid HashAlgorithm, string Checksum, string SourceSha256);

public sealed record PortablePdbSequencePoint(int Offset, int DocumentRowId, int StartLine, int StartColumn,
    int EndLine, int EndColumn, bool IsHidden);

public sealed record PortablePdbMethodEvidence(int MetadataToken, string DeclaringType, string Name,
    int? StateMachineKickoffMethodToken, IReadOnlyList<PortablePdbSequencePoint> SequencePoints);

public sealed record PortablePdbSourceEvidence(BinaryIdentityRecord Assembly,
    IReadOnlyList<PortablePdbDocumentEvidence> Documents, IReadOnlyList<PortablePdbMethodEvidence> Methods)
{
    // A document/sequence-point read does not yet join any coverage symbol.
    public bool SourceMappingsResolved => false;
    public bool CompleteBaseline => false;
}

public sealed class PortablePdbSourceException : Exception
{
    internal PortablePdbSourceException(string code) : base(code) => Code = code;
    public string Code { get; }
}

/// <summary>Validates explicit portable-PDB source provenance without loading the assembly or searching for files.</summary>
public static class PortablePdbSource
{
    private const int MaximumPathCharacters = 32768;
    private const int MaximumInventoryEntries = 200000;
    private const int MaximumDocuments = 65536;
    private const int MaximumMethods = 100000;
    private const int MaximumSequencePoints = 2000000;
    private const int MaximumMetadataTextCharacters = 16 * 1024 * 1024;
    private const long MaximumSourceBytes = 16 * 1024 * 1024;
    private const long MaximumTotalSourceBytes = 256 * 1024 * 1024;
    private static readonly Guid Sha1Algorithm = new("ff1816ec-aa5e-4d10-87f7-6f4963833460");
    private static readonly Guid Sha256Algorithm = new("8829d00f-11b8-4213-878b-770e8597ac16");

    public static PortablePdbSourceEvidence ReadValidated(string absoluteDll, string absolutePortablePdb,
        string absoluteSnapshotRoot, IReadOnlyList<PortablePdbSourceFile> snapshotFiles,
        IReadOnlyList<PortablePdbDocumentMap> documentMaps)
    {
        var sourceLocks = new List<FileStream>();
        try
        {
            var root = AbsolutePath(absoluteSnapshotRoot);
            Require(Directory.Exists(root), "portable-pdb-source-snapshot-root");
            RequireNoReparseAncestors(root, directory: true);
            var inventory = ReadInventory(snapshotFiles);
            var maps = ReadMaps(documentMaps);

            // These guards stay open while BinaryIdentity independently verifies
            // CodeView/PDB linkage and while metadata and source bytes are read.
            using var dll = OpenRead(AbsolutePath(absoluteDll), 256 * 1024 * 1024);
            using var pdb = OpenRead(AbsolutePath(absolutePortablePdb), 64 * 1024 * 1024);
            var identity = BinaryIdentity.ReadValidated(absoluteDll, absolutePortablePdb);
            using var pe = new PEReader(dll, PEStreamOptions.LeaveOpen);
            var assembly = pe.GetMetadataReader();
            using var provider = MetadataReaderProvider.FromPortablePdbStream(pdb, MetadataStreamOptions.LeaveOpen);
            var debug = provider.GetMetadataReader();
            Require(debug.Documents.Count is > 0 and <= MaximumDocuments, "portable-pdb-source-document-count");
            Require(assembly.MethodDefinitions.Count <= MaximumMethods, "portable-pdb-source-method-count");

            var documents = new List<PortablePdbDocumentEvidence>(debug.Documents.Count);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var sources = new Dictionary<string, (FileStream Stream, string Sha256)>(StringComparer.Ordinal);
            long totalSourceBytes = 0;
            var documentTextCharacters = 0;
            foreach (var handle in debug.Documents)
            {
                var document = debug.GetDocument(handle);
                Require(!document.Name.IsNil && !document.HashAlgorithm.IsNil && !document.Hash.IsNil,
                    "portable-pdb-source-document-metadata");
                var name = debug.GetString(document.Name);
                Require(Text(name, MaximumPathCharacters) && names.Add(name), "portable-pdb-source-document-path");
                documentTextCharacters = checked(documentTextCharacters + name.Length);
                Require(documentTextCharacters <= MaximumMetadataTextCharacters, "portable-pdb-source-metadata-text-limit");
                var relative = MapDocument(name, maps);
                Require(inventory.TryGetValue(relative, out var expectedHash), "portable-pdb-source-document-not-in-inventory");

                var algorithm = debug.GetGuid(document.HashAlgorithm);
                Require(algorithm == Sha1Algorithm || algorithm == Sha256Algorithm, "portable-pdb-source-unsupported-checksum");
                var checksum = debug.GetBlobBytes(document.Hash);
                Require(checksum.Length == (algorithm == Sha1Algorithm ? 20 : 32), "portable-pdb-source-document-checksum");
                if (!sources.TryGetValue(relative, out var source))
                {
                    var path = ResolveSource(root, relative);
                    var input = OpenRead(path, MaximumSourceBytes, allowEmpty: true);
                    sourceLocks.Add(input);
                    totalSourceBytes = checked(totalSourceBytes + input.Length);
                    Require(totalSourceBytes <= MaximumTotalSourceBytes, "portable-pdb-source-total-size-limit");
                    var sourceHash = Convert.ToHexStringLower(SHA256.HashData(input));
                    Require(sourceHash == expectedHash, "portable-pdb-source-inventory-mismatch");
                    source = (input, sourceHash);
                    sources.Add(relative, source);
                }
                source.Stream.Position = 0;
                var actualChecksum = algorithm == Sha1Algorithm
                    ? SHA1.HashData(source.Stream) : SHA256.HashData(source.Stream);
                Require(actualChecksum.AsSpan().SequenceEqual(checksum), "portable-pdb-source-checksum-mismatch");
                documents.Add(new(MetadataTokens.GetRowNumber(handle), name, relative, algorithm,
                    Convert.ToHexStringLower(checksum), source.Sha256));
            }

            var methods = ReadMethods(assembly, debug, documents.Count);
            return new(identity, documents.AsReadOnly(), methods.AsReadOnly());
        }
        catch (PortablePdbSourceException) { throw; }
        catch (BinaryIdentityException) { throw; }
        catch (Exception error) when (error is BadImageFormatException or OverflowException or IndexOutOfRangeException)
        {
            throw new PortablePdbSourceException("portable-pdb-source-invalid-metadata");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or CryptographicException or System.Security.SecurityException)
        {
            // Never publish exception text, inner exceptions, source contents or private paths.
            throw new PortablePdbSourceException("portable-pdb-source-file-read-failed");
        }
        finally
        {
            foreach (var input in sourceLocks) input.Dispose();
        }
    }

    private static Dictionary<string, string> ReadInventory(IReadOnlyList<PortablePdbSourceFile> entries)
    {
        Require(entries is not null && entries.Count <= MaximumInventoryEntries, "portable-pdb-source-inventory");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var physicalPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.ToArray())
        {
            Require(entry is not null && RelativePath(entry.RelativePath) && Hash(entry.Sha256) &&
                physicalPaths.Add(entry.RelativePath) && result.TryAdd(entry.RelativePath, entry.Sha256),
                "portable-pdb-source-inventory");
        }
        return result;
    }

    private static PortablePdbDocumentMap[] ReadMaps(IReadOnlyList<PortablePdbDocumentMap> entries)
    {
        Require(entries is not null && entries.Count is > 0 and <= 128, "portable-pdb-source-document-map");
        var result = entries.ToArray();
        var prefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in result)
        {
            Require(entry is not null && Text(entry.DocumentPrefix, MaximumPathCharacters) &&
                Path.IsPathRooted(entry.DocumentPrefix) && entry.DocumentPrefix[^1] is '/' or '\\' &&
                !entry.DocumentPrefix.Split(['/', '\\']).Any(part => part is "." or "..") &&
                prefixes.Add(entry.DocumentPrefix) && entry.SnapshotRelativePrefix is not null,
                "portable-pdb-source-document-map");
            var target = entry.SnapshotRelativePrefix.EndsWith('/') ? entry.SnapshotRelativePrefix[..^1] : entry.SnapshotRelativePrefix;
            Require(target.Length == 0 ? entry.SnapshotRelativePrefix.Length == 0 : RelativePath(target),
                "portable-pdb-source-document-map");
        }
        return result;
    }

    private static string MapDocument(string name, PortablePdbDocumentMap[] maps)
    {
        var matching = maps.Where(map => name.StartsWith(map.DocumentPrefix, StringComparison.Ordinal)).Take(2).ToArray();
        Require(matching.Length == 1, "portable-pdb-source-document-path");
        var map = matching[0];
        // Preserve the original document name in evidence. Only the explicit
        // mapping's suffix gets translated into canonical inventory separators.
        var suffix = name[map.DocumentPrefix.Length..].Replace('\\', '/');
        Require(RelativePath(suffix), "portable-pdb-source-document-path");
        var target = map.SnapshotRelativePrefix.TrimEnd('/');
        var relative = target.Length == 0 ? suffix : target + "/" + suffix;
        Require(RelativePath(relative), "portable-pdb-source-document-path");
        return relative;
    }

    private static List<PortablePdbMethodEvidence> ReadMethods(MetadataReader assembly, MetadataReader debug, int documentCount)
    {
        var methodCount = assembly.MethodDefinitions.Count;
        var debugRows = debug.GetTableRowCount(TableIndex.MethodDebugInformation);
        Require(debugRows == 0 || debugRows == methodCount, "portable-pdb-source-method-debug-count");
        var methods = new List<PortablePdbMethodEvidence>(methodCount);
        var totalPoints = 0;
        var methodTextCharacters = 0;
        foreach (var handle in assembly.MethodDefinitions)
        {
            var definition = assembly.GetMethodDefinition(handle);
            var name = assembly.GetString(definition.Name);
            Require(Text(name, 4096), "portable-pdb-source-method-metadata");
            var declaringType = DeclaringType(assembly, definition.GetDeclaringType());
            methodTextCharacters = checked(methodTextCharacters + name.Length + declaringType.Length);
            Require(methodTextCharacters <= MaximumMetadataTextCharacters, "portable-pdb-source-metadata-text-limit");
            var points = new List<PortablePdbSequencePoint>();
            int? kickoffToken = null;
            if (debugRows != 0)
            {
                var information = debug.GetMethodDebugInformation(MetadataTokens.MethodDebugInformationHandle(MetadataTokens.GetRowNumber(handle)));
                var kickoff = information.GetStateMachineKickoffMethod();
                if (!kickoff.IsNil)
                {
                    var row = MetadataTokens.GetRowNumber(kickoff);
                    Require(row > 0 && row <= methodCount && kickoff != handle, "portable-pdb-source-state-machine-method");
                    kickoffToken = MetadataTokens.GetToken(kickoff);
                }
                var previousOffset = -1;
                foreach (var point in information.GetSequencePoints())
                {
                    Require(++totalPoints <= MaximumSequencePoints, "portable-pdb-source-sequence-point-count");
                    var document = MetadataTokens.GetRowNumber(point.Document);
                    Require(point.Offset > previousOffset && point.Offset < 0x20000000 &&
                        document >= 0 && document <= documentCount && (point.IsHidden || document > 0),
                        "portable-pdb-source-sequence-point");
                    Require(point.IsHidden
                        ? point.StartLine == 0xfeefee && point.EndLine == 0xfeefee && point.StartColumn == 0 && point.EndColumn == 0
                        : point.StartLine >= 0 && point.EndLine >= point.StartLine && point.EndLine < 0x20000000 &&
                          point.StartColumn >= 0 && point.EndColumn >= 0 &&
                          (point.StartLine != point.EndLine || point.EndColumn > point.StartColumn),
                        "portable-pdb-source-sequence-point");
                    previousOffset = point.Offset;
                    points.Add(new(point.Offset, document, point.StartLine, point.StartColumn, point.EndLine, point.EndColumn, point.IsHidden));
                }
            }
            methods.Add(new(MetadataTokens.GetToken(handle), declaringType, name, kickoffToken, points.AsReadOnly()));
        }
        return methods;
    }

    private static string DeclaringType(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var names = new List<string>();
        var seen = new HashSet<int>();
        var ns = "";
        while (!handle.IsNil)
        {
            Require(names.Count < 128 && seen.Add(MetadataTokens.GetRowNumber(handle)), "portable-pdb-source-type-metadata");
            var definition = metadata.GetTypeDefinition(handle);
            var name = metadata.GetString(definition.Name);
            Require(Text(name, 4096), "portable-pdb-source-type-metadata");
            names.Add(name);
            handle = definition.GetDeclaringType();
            if (handle.IsNil)
            {
                ns = metadata.GetString(definition.Namespace);
                Require(ns.Length <= 4096 && !ns.Any(char.IsControl), "portable-pdb-source-type-metadata");
            }
        }
        Require(names.Count > 0, "portable-pdb-source-type-metadata");
        names.Reverse();
        return (ns.Length == 0 ? "" : ns + ".") + string.Join("+", names);
    }

    private static string AbsolutePath(string path)
    {
        Require(Text(path, MaximumPathCharacters) && Path.IsPathFullyQualified(path), "portable-pdb-source-path-must-be-absolute");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static string ResolveSource(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        Require(full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), "portable-pdb-source-document-path");
        return full;
    }

    private static FileStream OpenRead(string path, long maximumBytes, bool allowEmpty = false)
    {
        RequireNoReparseAncestors(path, directory: false);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            RequireNoReparseAncestors(path, directory: false);
            Require((allowEmpty || stream.Length > 0) && stream.Length <= maximumBytes,
                "portable-pdb-source-file-size-limit");
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static void RequireNoReparseAncestors(string path, bool directory)
    {
        var attributes = File.GetAttributes(path);
        Require(((attributes & FileAttributes.Directory) != 0) == directory &&
            (attributes & FileAttributes.ReparsePoint) == 0, "portable-pdb-source-file-kind-or-reparse");
        var current = directory ? new DirectoryInfo(path) : new FileInfo(path).Directory;
        while (current is not null)
        {
            Require((current.Attributes & FileAttributes.ReparsePoint) == 0, "portable-pdb-source-file-kind-or-reparse");
            current = current.Parent;
        }
    }

    private static bool RelativePath(string path)
    {
        if (!Text(path, MaximumPathCharacters) || Path.IsPathRooted(path) || path.Contains('\\')) return false;
        foreach (var part in path.Split('/'))
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0) return false;
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
                stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                (stem[3] is >= '1' and <= '9' or '¹' or '²' or '³')) return false;
        }
        return true;
    }

    private static bool Text(string value, int maximum) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum && !value.Any(char.IsControl);

    private static bool Hash(string value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw new PortablePdbSourceException(code);
    }
}
