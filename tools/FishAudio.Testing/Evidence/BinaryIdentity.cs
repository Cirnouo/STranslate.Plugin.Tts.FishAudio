using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace FishAudio.Testing.Evidence;

public sealed record BinaryIdentityRecord(string AssemblyName, Guid Mvid, string DllSha256,
    string PdbSha256, string PortablePdbId);

public sealed class BinaryIdentityException : Exception
{
    internal BinaryIdentityException(string code) : base(code) => Code = code;
    public string Code { get; }
}

/// <summary>Reads only the two explicit files; no assembly loading or PDB path discovery occurs.</summary>
public static class BinaryIdentity
{
    private const long MaximumDllBytes = 256 * 1024 * 1024;
    private const int MaximumMetadataBytes = 64 * 1024 * 1024;
    private const int MaximumDebugDirectoryBytes = 128 * 28;
    private const int MaximumCodeViewBytes = 64 * 1024;

    public static BinaryIdentityRecord ReadValidated(string absoluteDll, string absolutePortablePdb)
    {
        try
        {
            Require(!string.IsNullOrWhiteSpace(absoluteDll) && Path.IsPathFullyQualified(absoluteDll) &&
                !string.IsNullOrWhiteSpace(absolutePortablePdb) && Path.IsPathFullyQualified(absolutePortablePdb),
                "binary-path-must-be-absolute");
            using var dll = new FileStream(absoluteDll, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var pdb = new FileStream(absolutePortablePdb, FileMode.Open, FileAccess.Read, FileShare.Read);
            Require(dll.Length is > 0 and <= MaximumDllBytes && pdb.Length is > 0 and <= MaximumMetadataBytes,
                "binary-file-size-limit");
            using var pe = new PEReader(dll, PEStreamOptions.LeaveOpen);
            Require(pe.HasMetadata, "binary-not-managed-assembly");
            Require(pe.PEHeaders.MetadataSize is > 0 and <= MaximumMetadataBytes, "binary-metadata-limit");
            var metadata = pe.GetMetadataReader();
            Require(metadata.IsAssembly, "binary-not-managed-assembly");
            var name = metadata.GetString(metadata.GetAssemblyDefinition().Name);
            var module = metadata.GetModuleDefinition();
            Require(!module.Mvid.IsNil, "binary-missing-mvid");
            var mvid = metadata.GetGuid(module.Mvid);
            Require(mvid != Guid.Empty && !string.IsNullOrWhiteSpace(name) && name.Length <= 1024 &&
                !name.Any(char.IsControl), "binary-invalid-assembly-identity");

            var debugSize = pe.PEHeaders.PEHeader?.DebugTableDirectory.Size ?? 0;
            Require(debugSize is > 0 and <= MaximumDebugDirectoryBytes, "binary-debug-directory-limit");
            var codeViews = pe.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.CodeView)
                .Take(2).ToArray();
            Require(codeViews.Length == 1 && codeViews[0].IsPortableCodeView &&
                codeViews[0].DataSize is >= 24 and <= MaximumCodeViewBytes, "binary-missing-portable-codeview");
            var entry = codeViews[0];
            var codeView = pe.ReadCodeViewDebugDirectoryData(entry);

            using var provider = MetadataReaderProvider.FromPortablePdbStream(pdb, MetadataStreamOptions.LeaveOpen);
            var portableMetadata = provider.GetMetadataReader();
            var header = portableMetadata.DebugMetadataHeader;
            Require(header is not null && header.Id.Length == 20, "binary-missing-portable-pdb-id");
            var id = new BlobContentId(header!.Id);
            Require(codeView.Age == 1 && codeView.Guid == id.Guid && entry.Stamp == id.Stamp,
                "binary-dll-pdb-mismatch");

            dll.Position = 0;
            var dllHash = Convert.ToHexStringLower(SHA256.HashData(dll));
            pdb.Position = 0;
            var pdbHash = Convert.ToHexStringLower(SHA256.HashData(pdb));
            return new(name, mvid, dllHash, pdbHash, Convert.ToHexStringLower(header.Id.AsSpan()));
        }
        catch (BinaryIdentityException) { throw; }
        catch (Exception error) when (error is BadImageFormatException or OverflowException)
        {
            throw new BinaryIdentityException("binary-invalid-metadata");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException)
        {
            // Filesystem and metadata exceptions can include caller-owned paths.
            throw new BinaryIdentityException("binary-file-read-failed");
        }
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw new BinaryIdentityException(code);
    }
}
