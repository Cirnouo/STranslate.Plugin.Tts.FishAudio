using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FishAudio.Testing.Evidence;

// Caller identity assertions only. These values do not authenticate a build,
// Git tree, workflow event, execution attempt or complete coverage result.
public sealed record CoverageCiTransportExpectation(int SchemaVersion, string OriginalRoot,
    string SourceDirectory, string SourceTree);
public sealed record CoverageCiTransportPackInput(int SchemaVersion, string OwnerDirectory, string ArchivePath);
public sealed record CoverageCiTransportRestoreInput(int SchemaVersion, string ArchivePath, string ExpectedArchiveSha256);
public sealed record CoverageCiTransportFile(string Path, long Length, string Sha256);
public sealed record CoverageCiTransportManifest(int SchemaVersion, string Kind, string OriginalRoot,
    string SourceDirectory, string SourceTree, IReadOnlyList<string> Directories, IReadOnlyList<CoverageCiTransportFile> Files);
public sealed record CoverageCiTransportInspection(int SchemaVersion, string Kind,
    CoverageCiTransportManifest Manifest, CoverageCiTransportReceipt Receipt);
// Version two adds a separate complete file-time table. Files keep their
// original three-field byte inventory; schema-one JSON is unchanged.
public sealed record CoverageCiTransportFileTime(string Path, long LastWriteTimeUtcTicks);
public sealed record CoverageCiTransportManifestV2(int SchemaVersion, string Kind, string OriginalRoot,
    string SourceDirectory, string SourceTree, IReadOnlyList<string> Directories, IReadOnlyList<CoverageCiTransportFile> Files,
    IReadOnlyList<CoverageCiTransportFileTime> FileTimes);
public sealed record CoverageCiTransportInspectionV2(int SchemaVersion, string Kind,
    CoverageCiTransportManifestV2 Manifest, CoverageCiTransportReceipt Receipt);
public sealed record CoverageCiTransportReceipt(int SchemaVersion, string Kind, string Operation, string ArchivePath,
    string ArchiveSha256, string ManifestSha256, string OriginalRoot, string SourceDirectory, string SourceTree,
    int FileCount, int DirectoryCount, long TotalBytes)
{
    public bool ByteTransportComplete => true;
    public bool SourceAuthenticated => false;
    public bool BuildAuthenticated => false;
    public bool TestExecutionVerified => false;
    public bool MainProvenanceVerified => false;
    public bool CoverageUnionComplete => false;
    public bool CompleteBaseline => false;
    public bool PublicationAuthorized => false;
    public bool TiaSelectionAuthorized => false;
}
public sealed class CoverageCiTransportException : Exception
{
    internal CoverageCiTransportException(string code, Exception? cause = null) : base("coverage-ci-transport-" + code, cause) => Code = Message;
    public string Code { get; }
}

// Complete-directory transport at the original absolute root. Original
// evidence bytes and timestamps remain unchanged; v2 restores file times only
// on fresh extracted copies. Failed pending files/staging
// directories remain owned evidence; this component never retries or deletes.
public static class CoverageCiTransport
{
    private const string ManifestName = "transport-manifest.json";
    private const string ManifestKind = "coverage-operation-byte-archive";
    private const string ManifestKindV2 = "coverage-operation-file-time-archive";
    private const int MaximumEntries = 200_000;
    private const long MaximumFileBytes = 256L * 1024 * 1024;
    private const long MaximumTotalBytes = 8L * 1024 * 1024 * 1024;
    private const int MaximumJsonBytes = 64 * 1024 * 1024;
    private const long MaximumArchiveBytes = MaximumTotalBytes + 4L * MaximumJsonBytes;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true, MaxDepth = 64,
    };
    private sealed record Tree(IReadOnlyList<string> Directories, IReadOnlyList<string> Files);
    private sealed record AdmittedArchive(string Path, FileStream Stream, byte[] ManifestBytes,
        CoverageCiTransportManifest Manifest, IReadOnlyList<CoverageCiTransportFileTime>? FileTimes,
        IReadOnlyDictionary<string, ZipArchiveEntry> Entries);

    public static CoverageCiTransportReceipt Pack(CoverageCiTransportPackInput input,
        CoverageCiTransportExpectation expected) => PackCore(input, expected, schemaVersion: 1);

    public static CoverageCiTransportReceipt PackV2(CoverageCiTransportPackInput input,
        CoverageCiTransportExpectation expected) => PackCore(input, expected, schemaVersion: 2);

    private static CoverageCiTransportReceipt PackCore(CoverageCiTransportPackInput input,
        CoverageCiTransportExpectation expected, int schemaVersion) => Held(guards =>
    {
        ValidateExpectation(expected);
        Require(input is not null && input.SchemaVersion == schemaVersion && expected.SchemaVersion == schemaVersion && Absolute(input.OwnerDirectory) == expected.OriginalRoot, "pack-identity");
        var root = ExistingDirectory(expected.OriginalRoot);
        var archivePath = ExternalOutput(input.ArchivePath, expected);
        guards.Add(SourceLease(expected.SourceDirectory));
        var tree = Enumerate(root);
        var pins = new List<CoverageCiTransportFile>();
        var fileTimes = new List<CoverageCiTransportFileTime>();
        var streams = new List<FileStream>();
        long total = 0;
        foreach (var relative in tree.Files)
        {
            var stream = OpenFile(Child(root, relative), guards);
            Require(stream.Length <= MaximumFileBytes, "file-size");
            total = checked(total + stream.Length);
            Require(total <= MaximumTotalBytes, "total-size");
            pins.Add(new(relative, stream.Length, Digest(stream)));
            if (schemaVersion == 2) fileTimes.Add(new(relative, SourceFileTimeTicks(stream)));
            streams.Add(stream);
        }
        var manifest = new CoverageCiTransportManifest(schemaVersion, schemaVersion == 2 ? ManifestKindV2 : ManifestKind, expected.OriginalRoot,
            expected.SourceDirectory, expected.SourceTree, tree.Directories, pins.AsReadOnly());
        var manifestBytes = schemaVersion == 2
            ? JsonSerializer.SerializeToUtf8Bytes(TimeManifest(manifest, fileTimes.AsReadOnly()), JsonOptions)
            : JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        Require(manifestBytes.Length <= MaximumJsonBytes, "manifest-size");
        var pending = archivePath + ".pending-" + Guid.NewGuid().ToString("N");
        // ZipArchive must finish its central directory before the underlying
        // file is flushed, disposed and atomically published under its final name.
        Held<object?>(writes =>
        {
            var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            writes.Add(output);
            Held<object?>(archives =>
            {
                var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: Utf8);
                archives.Add(zip);
                WriteEntry(zip, ManifestName, manifestBytes);
                foreach (var directory in tree.Directories)
                {
                    var entry = zip.CreateEntry("payload/" + directory + "/", CompressionLevel.NoCompression);
                    entry.ExternalAttributes = (int)FileAttributes.Directory;
                }
                for (var index = 0; index < pins.Count; index++)
                {
                    var pin = pins[index];
                    var stream = streams[index];
                    stream.Position = 0;
                    var entry = zip.CreateEntry("payload/" + pin.Path, CompressionLevel.NoCompression);
                    entry.ExternalAttributes = (int)FileAttributes.Normal;
                    Held<object?>(entries =>
                    {
                        var target = entry.Open(); entries.Add(target);
                        CopyVerified(stream, target, pin);
                        return null;
                    });
                }
                return null;
            });
            output.Flush(flushToDisk: true);
            return null;
        });
        Require(SameTree(tree, Enumerate(root)), "source-inventory-drift");
        for (var index = 0; index < pins.Count; index++)
        {
            Require(streams[index].Length == pins[index].Length && Digest(streams[index]) == pins[index].Sha256, "source-byte-drift");
            if (schemaVersion == 2)
                Require(SourceFileTimeTicks(streams[index]) == fileTimes[index].LastWriteTimeUtcTicks, "source-file-time-drift");
        }
        var archiveSha256 = Held(checks =>
        {
            var file = OpenFile(pending, checks);
            Require(file.Length is > 0 and <= MaximumArchiveBytes, "archive-size");
            return Digest(file);
        });
        _ = ExternalOutput(archivePath, expected);
        File.Move(pending, archivePath, overwrite: false);
        Require(Held(checks => Digest(OpenFile(archivePath, checks))) == archiveSha256, "published-archive-drift");
        return Receipt("Pack", archivePath, archiveSha256, manifestBytes, manifest);
    });

    public static CoverageCiTransportReceipt Restore(CoverageCiTransportRestoreInput input,
        CoverageCiTransportExpectation expected) => RestoreCore(input, expected, schemaVersion: 1);

    public static CoverageCiTransportReceipt RestoreV2(CoverageCiTransportRestoreInput input,
        CoverageCiTransportExpectation expected) => RestoreCore(input, expected, schemaVersion: 2);

    private static CoverageCiTransportReceipt RestoreCore(CoverageCiTransportRestoreInput input,
        CoverageCiTransportExpectation expected, int schemaVersion) => Held(guards =>
    {
        ValidateExpectation(expected);
        var root = expected.OriginalRoot;
        Fresh(root);
        var parent = ExistingDirectory(Path.GetDirectoryName(root)!);
        var admitted = AdmitArchive(input, expected, guards, schemaVersion);
        var manifest = admitted.Manifest;
        // Only this caller-derived fresh sibling can receive bytes. All names,
        // identities and the independent archive digest passed before it exists.
        var stage = Path.Combine(parent, "." + Path.GetFileName(root) + ".restore-" + Guid.NewGuid().ToString("N"));
        Fresh(stage);
        Directory.CreateDirectory(stage);
        _ = ExistingDirectory(stage);
        foreach (var relative in manifest.Directories)
        {
            var directory = Child(stage, relative);
            Directory.CreateDirectory(directory);
            _ = ExistingDirectory(directory);
        }
        foreach (var pin in manifest.Files)
        {
            var targetPath = Child(stage, pin.Path);
            _ = ExistingDirectory(Path.GetDirectoryName(targetPath)!);
            Held<object?>(copies =>
            {
                var stream = admitted.Entries["payload/" + pin.Path].Open(); copies.Add(stream);
                var output = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None); copies.Add(output);
                CopyVerified(stream, output, pin);
                output.Flush(flushToDisk: true);
                return null;
            });
        }
        if (admitted.FileTimes is not null) RestoreFileTimes(stage, admitted.FileTimes);
        ValidateRestoredTree(stage, manifest, admitted.FileTimes);
        Require(Digest(admitted.Stream) == input.ExpectedArchiveSha256, "archive-drift");
        Fresh(root);
        _ = ExistingDirectory(parent);
        _ = ExistingDirectory(stage);
        Directory.Move(stage, root);
        ValidateRestoredTree(root, manifest, admitted.FileTimes);
        return Receipt("Restore", admitted.Path, input.ExpectedArchiveSha256, admitted.ManifestBytes, manifest);
    });

    // Admission of a downloaded archive without materializing its owner. The
    // same strict parser, member plan and per-file verifier serve Restore.
    // An occupied or missing original root is only an independent identity;
    // no owner parent, stage, output file or restored directory is created here.
    public static CoverageCiTransportInspection Inspect(CoverageCiTransportRestoreInput input,
        CoverageCiTransportExpectation expected)
    {
        var admitted = InspectCore(input, expected, schemaVersion: 1);
        return new(1, "coverage-operation-byte-inspection", admitted.Manifest, admitted.Receipt);
    }

    public static CoverageCiTransportInspectionV2 InspectV2(CoverageCiTransportRestoreInput input,
        CoverageCiTransportExpectation expected)
    {
        var admitted = InspectCore(input, expected, schemaVersion: 2);
        // Inspection admits archive bytes and time metadata, not the successful
        // setting of filesystem times; only Restore performs that operation.
        return new(2, "coverage-operation-file-time-inspection", TimeManifest(admitted.Manifest, admitted.FileTimes!), admitted.Receipt);
    }

    private static (CoverageCiTransportManifest Manifest, IReadOnlyList<CoverageCiTransportFileTime>? FileTimes,
        CoverageCiTransportReceipt Receipt) InspectCore(CoverageCiTransportRestoreInput input,
        CoverageCiTransportExpectation expected, int schemaVersion) => Held(guards =>
    {
        var admitted = AdmitArchive(input, expected, guards, schemaVersion);
        foreach (var pin in admitted.Manifest.Files)
            Held<object?>(checks =>
            {
                var stream = admitted.Entries["payload/" + pin.Path].Open(); checks.Add(stream);
                CopyVerified(stream, Stream.Null, pin);
                return null;
            });
        Require(Digest(admitted.Stream) == input.ExpectedArchiveSha256, "archive-drift");
        return (admitted.Manifest, admitted.FileTimes,
            Receipt("Inspect", admitted.Path, input.ExpectedArchiveSha256, admitted.ManifestBytes, admitted.Manifest));
    });

    private static AdmittedArchive AdmitArchive(CoverageCiTransportRestoreInput input,
        CoverageCiTransportExpectation expected, List<IDisposable> guards, int schemaVersion)
    {
        ValidateExpectation(expected);
        Require(input is not null && input.SchemaVersion == schemaVersion && expected.SchemaVersion == schemaVersion && Hash(input.ExpectedArchiveSha256), "restore-input");
        var archivePath = Absolute(input.ArchivePath);
        Require(!archivePath.Equals(expected.OriginalRoot, StringComparison.OrdinalIgnoreCase) &&
            !Within(archivePath, expected.OriginalRoot), "archive-inside-owner");
        guards.Add(SourceLease(expected.SourceDirectory));
        var archive = OpenFile(archivePath, guards);
        Require(archive.Length is > 0 and <= MaximumArchiveBytes, "archive-size");
        Require(Digest(archive) == input.ExpectedArchiveSha256, "archive-digest");
        var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: Utf8);
        guards.Add(zip);
        Require(zip.Entries.Count is > 0 and <= MaximumEntries + 1, "entry-count");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
            Require(entry.FullName.Length <= 32767 && entries.TryAdd(entry.FullName, entry) && aliases.Add(entry.FullName), "duplicate-entry");
        Require(entries.TryGetValue(ManifestName, out var manifestEntry), "missing-manifest");
        var manifestFile = manifestEntry!;
        ValidateAttributes(manifestFile, directory: false);
        Require(manifestFile.Length is > 0 and <= MaximumJsonBytes, "manifest-size");
        var manifestBytes = Held(reads =>
        {
            var stream = manifestFile.Open(); reads.Add(stream);
            var bytes = new byte[(int)manifestFile.Length];
            stream.ReadExactly(bytes);
            Require(stream.ReadByte() == -1, "manifest-length");
            return bytes;
        });
        CoverageCiTransportManifest manifest;
        IReadOnlyList<CoverageCiTransportFileTime>? fileTimes = null;
        if (schemaVersion == 2)
        {
            var versioned = ReadJson<CoverageCiTransportManifestV2>(manifestBytes);
            manifest = new(versioned.SchemaVersion, versioned.Kind, versioned.OriginalRoot, versioned.SourceDirectory,
                versioned.SourceTree, versioned.Directories, versioned.Files);
            fileTimes = versioned.FileTimes;
        }
        else manifest = ReadJson<CoverageCiTransportManifest>(manifestBytes);
        ValidateManifest(manifest, expected, entries);
        if (schemaVersion == 2) ValidateFileTimes(fileTimes, manifest.Files);
        return new(archivePath, archive, manifestBytes, manifest, fileTimes, entries);
    }

    private static void ValidateRestoredTree(string root, CoverageCiTransportManifest manifest,
        IReadOnlyList<CoverageCiTransportFileTime>? fileTimes)
    {
        var restored = Enumerate(root);
        Require(restored.Directories.SequenceEqual(manifest.Directories, StringComparer.Ordinal) &&
            restored.Files.SequenceEqual(manifest.Files.Select(pin => pin.Path), StringComparer.Ordinal), "restored-inventory");
        Held<object?>(checks =>
        {
            for (var index = 0; index < manifest.Files.Count; index++)
            {
                var pin = manifest.Files[index];
                var file = OpenFile(Child(root, pin.Path), checks);
                Require(file.Length == pin.Length && Digest(file) == pin.Sha256, "restored-bytes");
                if (fileTimes is not null)
                    Require(File.GetLastWriteTimeUtc(file.SafeFileHandle).Ticks == fileTimes[index].LastWriteTimeUtcTicks, "restored-file-time");
            }
            return null;
        });
    }

    private static CoverageCiTransportReceipt Receipt(string operation, string archivePath, string digest,
        byte[] manifestBytes, CoverageCiTransportManifest manifest) => new(manifest.SchemaVersion,
        manifest.SchemaVersion == 2 ? "coverage-operation-file-time-transport" : "coverage-operation-byte-transport", operation,
        archivePath, digest, Convert.ToHexStringLower(SHA256.HashData(manifestBytes)), manifest.OriginalRoot,
        manifest.SourceDirectory, manifest.SourceTree, manifest.Files.Count, manifest.Directories.Count,
        manifest.Files.Sum(pin => pin.Length));

    private static void ValidateExpectation(CoverageCiTransportExpectation expected)
    {
        Require(OperatingSystem.IsWindows(), "windows-required");
        Require(expected is not null && expected.SchemaVersion is 1 or 2 &&
            expected.OriginalRoot == Absolute(expected.OriginalRoot) && expected.SourceDirectory == Absolute(expected.SourceDirectory) &&
            expected.SourceTree is { Length: 40 or 64 } && expected.SourceTree.All(IsLowerHex), "expectation");
        Require(Path.GetDirectoryName(expected.OriginalRoot) is not null &&
            expected.OriginalRoot.Length > Path.GetPathRoot(expected.OriginalRoot)!.Length, "owner-root");
        ExistingAncestors(expected.OriginalRoot);
        ExistingAncestors(expected.SourceDirectory);
    }

    private static void ValidateManifest(CoverageCiTransportManifest manifest, CoverageCiTransportExpectation expected,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        Require(manifest is not null && manifest.SchemaVersion == expected.SchemaVersion &&
            manifest.Kind == (expected.SchemaVersion == 2 ? ManifestKindV2 : ManifestKind) &&
            manifest.OriginalRoot == expected.OriginalRoot && manifest.SourceDirectory == expected.SourceDirectory &&
            manifest.SourceTree == expected.SourceTree, "manifest-identity");
        Require(manifest.Directories is not null && manifest.Files is not null &&
            (long)manifest.Directories.Count + manifest.Files.Count is > 0 and <= MaximumEntries &&
            manifest.Directories.SequenceEqual(manifest.Directories.Order(StringComparer.Ordinal), StringComparer.Ordinal) &&
            manifest.Files.All(pin => pin is not null) &&
            manifest.Files.Select(pin => pin.Path).SequenceEqual(manifest.Files.Select(pin => pin.Path).Order(StringComparer.Ordinal), StringComparer.Ordinal), "manifest-inventory");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        var expectedEntries = new HashSet<string>(StringComparer.Ordinal) { ManifestName };
        foreach (var directory in manifest.Directories)
        {
            _ = Relative(directory);
            Require(names.Add(directory) && directories.Add(directory), "manifest-alias");
            expectedEntries.Add("payload/" + directory + "/");
            Require(entries.TryGetValue("payload/" + directory + "/", out var entry) && entry.Length == 0, "directory-entry");
            ValidateAttributes(entry!, directory: true);
        }
        long total = 0;
        foreach (var pin in manifest.Files)
        {
            _ = Relative(pin.Path);
            Require(names.Add(pin.Path) && Hash(pin.Sha256) && pin.Length is >= 0 and <= MaximumFileBytes, "manifest-file");
            total = checked(total + pin.Length);
            Require(total <= MaximumTotalBytes, "total-size");
            expectedEntries.Add("payload/" + pin.Path);
            Require(entries.TryGetValue("payload/" + pin.Path, out var entry) && entry.Length == pin.Length, "file-entry");
            ValidateAttributes(entry!, directory: false);
        }
        foreach (var name in names)
        {
            var parent = name.LastIndexOf('/');
            Require(parent < 0 || directories.Contains(name[..parent]), "missing-parent-directory");
        }
        Require(expectedEntries.SetEquals(entries.Keys), "entry-set");
    }

    private static CoverageCiTransportManifestV2 TimeManifest(CoverageCiTransportManifest manifest,
        IReadOnlyList<CoverageCiTransportFileTime> fileTimes) => new(2, ManifestKindV2, manifest.OriginalRoot,
        manifest.SourceDirectory, manifest.SourceTree, manifest.Directories, manifest.Files, fileTimes);

    private static bool FileTimeTicks(long ticks) => ticks >= 504911232000000000L && ticks <= DateTime.MaxValue.Ticks;

    private static long SourceFileTimeTicks(FileStream stream)
    {
        var ticks = File.GetLastWriteTimeUtc(stream.SafeFileHandle).Ticks;
        // The lower bound is the Windows FILETIME epoch, 1601-01-01 UTC.
        Require(FileTimeTicks(ticks), "source-file-time");
        return ticks;
    }

    private static void ValidateFileTimes(IReadOnlyList<CoverageCiTransportFileTime>? fileTimes,
        IReadOnlyList<CoverageCiTransportFile> files)
    {
        Require(fileTimes is not null && fileTimes.Count == files.Count && fileTimes.All(item => item is not null) &&
            fileTimes.Select(item => item.Path).SequenceEqual(files.Select(item => item.Path), StringComparer.Ordinal), "manifest-file-times");
        foreach (var item in fileTimes)
            Require(FileTimeTicks(item.LastWriteTimeUtcTicks), "manifest-file-time");
    }

    private static void RestoreFileTimes(string stage, IReadOnlyList<CoverageCiTransportFileTime> fileTimes)
    {
        // Called only for this Restore's freshly extracted sibling stage, after
        // every output stream was flushed and closed. No original file is set.
        foreach (var item in fileTimes)
        {
            var path = Child(stage, item.Path);
            Regular(path, directory: false);
            File.SetLastWriteTimeUtc(path, new DateTime(item.LastWriteTimeUtcTicks, DateTimeKind.Utc));
            Regular(path, directory: false);
            Require(File.GetLastWriteTimeUtc(path).Ticks == item.LastWriteTimeUtcTicks, "restored-file-time");
        }
    }

    private static void ValidateAttributes(ZipArchiveEntry entry, bool directory)
    {
        var unixType = (entry.ExternalAttributes >> 16) & 0xf000;
        Require((entry.ExternalAttributes & (int)(FileAttributes.ReparsePoint | FileAttributes.Device)) == 0 &&
            (unixType == 0 || unixType == (directory ? 0x4000 : 0x8000)) &&
            (((entry.ExternalAttributes & (int)FileAttributes.Directory) != 0) == directory || unixType != 0) &&
            entry.FullName.EndsWith('/') == directory, "entry-attributes");
    }

    private static Tree Enumerate(string root)
    {
        _ = ExistingDirectory(root);
        var directories = new List<string>();
        var files = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count != 0)
            foreach (var path in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                var attributes = File.GetAttributes(path);
                Require((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) == 0, "physical-reparse-or-device");
                var relative = Relative(Path.GetRelativePath(root, path).Replace('\\', '/'));
                Require(names.Add(relative) && names.Count <= MaximumEntries, "physical-alias-or-count");
                if ((attributes & FileAttributes.Directory) != 0) { directories.Add(relative); pending.Push(path); }
                else files.Add(relative);
            }
        Require(names.Count > 0, "empty-owner");
        directories.Sort(StringComparer.Ordinal); files.Sort(StringComparer.Ordinal);
        return new(directories.AsReadOnly(), files.AsReadOnly());
    }
    private static bool SameTree(Tree left, Tree right) => left.Directories.SequenceEqual(right.Directories, StringComparer.Ordinal) &&
        left.Files.SequenceEqual(right.Files, StringComparer.Ordinal);

    private static void WriteEntry(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
        entry.ExternalAttributes = (int)FileAttributes.Normal;
        Held<object?>(guards => { var stream = entry.Open(); guards.Add(stream); stream.Write(bytes); return null; });
    }
    private static void CopyVerified(Stream source, Stream target, CoverageCiTransportFile pin)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        long total = 0;
        int count;
        while ((count = source.Read(buffer)) != 0)
        {
            total = checked(total + count);
            Require(total <= pin.Length, "payload-length");
            hash.AppendData(buffer, 0, count);
            target.Write(buffer, 0, count);
        }
        Require(total == pin.Length && Convert.ToHexStringLower(hash.GetHashAndReset()) == pin.Sha256, "payload-bytes");
    }
    private static string Digest(FileStream stream)
    {
        stream.Position = 0;
        var digest = Convert.ToHexStringLower(SHA256.HashData(stream));
        stream.Position = 0;
        return digest;
    }

    private static FileStream SourceLease(string source)
    {
        var directory = Path.Combine(Path.GetTempPath(), "fa-full-coverage-source-leases");
        ExistingAncestors(directory);
        Directory.CreateDirectory(directory);
        _ = ExistingDirectory(directory);
        var key = Convert.ToHexStringLower(SHA256.HashData(Utf8.GetBytes(source.ToUpperInvariant())));
        var path = Path.Combine(directory, key + ".lock");
        if (TryAttributes(path, out _)) Regular(path, directory: false);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) { throw new CoverageCiTransportException("source-lease-unavailable", error); }
    }

    internal static string ExternalOutput(string path, CoverageCiTransportExpectation expected)
    {
        ValidateExpectation(expected);
        path = Absolute(path);
        Require(!path.Equals(expected.OriginalRoot, StringComparison.OrdinalIgnoreCase) && !Within(path, expected.OriginalRoot) &&
            !path.Equals(expected.SourceDirectory, StringComparison.OrdinalIgnoreCase) && !Within(path, expected.SourceDirectory), "output-inside-owner-or-source");
        _ = ExistingDirectory(Path.GetDirectoryName(path)!);
        Fresh(path);
        return path;
    }
    internal static FileStream OpenFile(string path, List<IDisposable> guards)
    {
        path = Absolute(path);
        Regular(path, directory: false);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        guards.Add(stream);
        Regular(path, directory: false);
        return stream;
    }
    internal static T ReadJsonFile<T>(string path, List<IDisposable> guards) => ReadJson<T>(ReadJsonBytes(path, guards));
    internal static byte[] ReadJsonBytes(string path, List<IDisposable> guards)
    {
        var stream = OpenFile(path, guards);
        Require(stream.Length is > 0 and <= MaximumJsonBytes, "json-size");
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes);
        return bytes;
    }
    internal static T ReadJson<T>(byte[] bytes, JsonSerializerOptions? options = null)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 64 });
        var names = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) names.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) names.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName)
                Require(names.Peek().Add(reader.GetString()!), "duplicate-json-property");
        }
        return JsonSerializer.Deserialize<T>(bytes, options ?? JsonOptions) ?? throw new CoverageCiTransportException("empty-json");
    }
    internal static void PublishReceipt(string path, CoverageCiTransportReceipt receipt)
    {
        var expected = new CoverageCiTransportExpectation(receipt.SchemaVersion, receipt.OriginalRoot, receipt.SourceDirectory, receipt.SourceTree);
        PublishJson(path, receipt, expected);
    }
    internal static void PublishJson<T>(string path, T value, CoverageCiTransportExpectation expected)
    {
        path = ExternalOutput(path, expected);
        var pending = path + ".pending-" + Guid.NewGuid().ToString("N");
        Held<object?>(guards =>
        {
            var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None); guards.Add(stream);
            JsonSerializer.Serialize(stream, value, JsonOptions);
            stream.Flush(flushToDisk: true);
            return null;
        });
        _ = ExternalOutput(path, expected);
        File.Move(pending, path, overwrite: false);
    }
    internal static T Held<T>(Func<List<IDisposable>, T> operation)
    {
        var guards = new List<IDisposable>();
        Exception? failure = null;
        T result = default!;
        try { result = operation(guards); }
        catch (Exception error) { failure = error; }
        finally
        {
            for (var index = guards.Count - 1; index >= 0; index--)
                try { guards[index].Dispose(); }
                catch (Exception error) { failure ??= new CoverageCiTransportException("input-release-failed", error); }
        }
        if (failure is CoverageCiTransportException) ExceptionDispatchInfo.Capture(failure).Throw();
        if (failure is not null) throw new CoverageCiTransportException("io-or-invalid-data", failure);
        return result;
    }

    private static string Absolute(string path)
    {
        Require(!string.IsNullOrWhiteSpace(path) && path.Length is >= 3 and <= 32767 &&
            char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '/' or '\\' &&
            !path.Any(char.IsControl) && !path[2..].Contains(':') && !path.StartsWith("\\\\", StringComparison.Ordinal), "absolute-path");
        if (path.Length > 3) foreach (var part in path[3..].Split('/', '\\')) ValidatePart(part);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    private static string Relative(string path)
    {
        Require(!string.IsNullOrWhiteSpace(path) && path.Length <= 32767 && !path.Contains('\\') &&
            !Path.IsPathRooted(path) && !path.Any(char.IsControl), "relative-path");
        foreach (var part in path.Split('/')) ValidatePart(part);
        return path;
    }
    private static void ValidatePart(string part)
    {
        Require(part.Length > 0 && part is not "." and not ".." && !part.EndsWith('.') && !part.EndsWith(' ') &&
            part.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !part.Contains(':') &&
            !System.Text.RegularExpressions.Regex.IsMatch(part, "~[0-9]"), "path-component");
        _ = Utf8.GetByteCount(part);
        var stem = part.Split('.')[0].ToUpperInvariant();
        Require(stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$") &&
            !System.Text.RegularExpressions.Regex.IsMatch(stem, "^(COM|LPT)[0-9¹²³]$"), "device-path");
    }
    private static string Child(string root, string relative)
    {
        var path = Absolute(Path.Combine(root, Relative(relative)));
        Require(Within(path, root), "path-escape");
        return path;
    }
    private static bool Within(string path, string root) => path.StartsWith(
        root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string ExistingDirectory(string path) { path = Absolute(path); Regular(path, directory: true); return path; }
    private static void ExistingAncestors(string path)
    {
        var current = Absolute(path);
        while (current.Length > Path.GetPathRoot(current)!.Length)
        {
            if (TryAttributes(current, out var attributes)) Regular(current, (attributes & FileAttributes.Directory) != 0);
            current = Path.GetDirectoryName(current)!;
        }
        Require(Directory.Exists(current), "drive-root-missing");
        Require((File.GetAttributes(current) & (FileAttributes.ReparsePoint | FileAttributes.Device)) == 0, "reparse-ancestor");
    }
    private static void Regular(string path, bool directory)
    {
        Require(TryAttributes(path, out _), "path-missing");
        string? current = path;
        var first = true;
        while (current is not null)
        {
            var attributes = File.GetAttributes(current);
            Require((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) == 0 &&
                (!first || ((attributes & FileAttributes.Directory) != 0) == directory), "reparse-or-kind");
            first = false; current = Path.GetDirectoryName(current);
        }
    }
    private static bool TryAttributes(string path, out FileAttributes attributes)
    {
        try { attributes = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { attributes = default; return false; }
        catch (DirectoryNotFoundException) { attributes = default; return false; }
    }
    private static void Fresh(string path) => Require(!TryAttributes(path, out _), "destination-occupied");
    private static bool IsLowerHex(char value) => value is >= '0' and <= '9' or >= 'a' and <= 'f';
    private static bool Hash(string value) => value is { Length: 64 } && value.All(IsLowerHex);
    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    { if (!condition) throw new CoverageCiTransportException(code); }
}
