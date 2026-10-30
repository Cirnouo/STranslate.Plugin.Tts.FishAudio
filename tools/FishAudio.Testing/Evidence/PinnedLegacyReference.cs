using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;

namespace FishAudio.Testing.Evidence;

public sealed class PinnedLegacyReferenceException : Exception
{
    internal PinnedLegacyReferenceException(string code) : base(code) => Code = code;
    public string Code { get; }
}

/// <summary>
/// Authenticates the immutable original migration reference against source-owned
/// byte pins. This is identity evidence, not test registration, execution evidence
/// or a reusable coverage baseline. Callers cannot supply an expected digest.
/// Only the exact historical Git blob and its CRLF checkout representation are
/// accepted. No file normalization, neighboring digest lookup or source discovery
/// occurs. The returned digest identifies the raw representation actually read.
/// </summary>
public static class PinnedLegacyReference
{
    private const int MaximumReferenceBytes = 1024 * 1024;
    private const int OriginalCaseCount = 1580;
    private const string SourceCommit = "ec28a59e1a9f4d5debaf2998f668bb3f7988aeca";
    private const string SourceTree = "ebbae71fc69aaf5b47d74e4dcbdcc6b6cf4f058c";
    private const string GitBlobSha256 = "aa382df4bc3786cfdba9a8c4e1764efd4f99135a982162c388a0f1b5e54cdbef";
    private const string CheckoutSha256 = "c2b27285534fcd7fc7572b379f9856b42285bfcea7e2d982204c95a470fbb627";

    public static MigrationReference ReadValidated(string absoluteReferencePath)
    {
        FileStream? stream = null;
        var admitted = false;
        try
        {
            Require(!string.IsNullOrWhiteSpace(absoluteReferencePath) &&
                Path.IsPathFullyQualified(absoluteReferencePath) && !DevicePath(absoluteReferencePath),
                "pinned-legacy-reference-invalid-input-or-file");
            var path = Path.GetFullPath(absoluteReferencePath);
            Require(!DevicePath(path), "pinned-legacy-reference-invalid-input-or-file");
            RequireRegularFile(path);
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            // Keep the explicit input open against writes/replacement while its
            // raw bytes, pinned identity and ordered records are admitted.
            RequireRegularFile(path);
            Require(stream.Length is > 0 and <= MaximumReferenceBytes,
                "pinned-legacy-reference-file-size-limit");
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            Require(stream.ReadByte() == -1 && stream.Length == bytes.Length,
                "pinned-legacy-reference-file-changed-during-read");
            var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Require(digest is GitBlobSha256 or CheckoutSha256, "pinned-legacy-reference-hash-mismatch");

            // Parse only authenticated bytes. The pins bind the complete
            // historical document, including its original array order and
            // provenance; the schema/source checks also make that contract
            // explicit at this typed API boundary.
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            Require(ExactFields(root, "schemaVersion", "referenceKind", "sourceReferenceCommit", "sourceTree",
                "provenance", "cases"), "pinned-legacy-reference-schema-mismatch");
            var version = 0;
            var schema = root.GetProperty("schemaVersion");
            Require(schema.ValueKind == JsonValueKind.Number && schema.TryGetInt32(out version) && version == 1 &&
                String(root.GetProperty("referenceKind")) == "git-tree" &&
                root.GetProperty("provenance").ValueKind == JsonValueKind.Object,
                "pinned-legacy-reference-schema-mismatch");
            Require(String(root.GetProperty("sourceReferenceCommit")) == SourceCommit &&
                String(root.GetProperty("sourceTree")) == SourceTree,
                "pinned-legacy-reference-source-mismatch");
            var cases = root.GetProperty("cases");
            Require(cases.ValueKind == JsonValueKind.Array && cases.GetArrayLength() == OriginalCaseCount,
                "pinned-legacy-reference-cases-mismatch");
            var records = new MigrationLegacyCase[OriginalCaseCount];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;
            foreach (var item in cases.EnumerateArray())
            {
                Require(ExactFields(item, "id", "partition"), "pinned-legacy-reference-cases-mismatch");
                var id = String(item.GetProperty("id"));
                var partition = String(item.GetProperty("partition"));
                Require(!string.IsNullOrWhiteSpace(id) && id.Length <= 16_384 && !id.Any(char.IsControl) &&
                    ids.Add(id) && partition is "core" or "native-settings" or "native-input" or "native-popups" or "native-cards",
                    "pinned-legacy-reference-cases-mismatch");
                records[index++] = new(id, partition);
            }
            var reference = new MigrationReference(version, new(SourceCommit, SourceTree, digest), Array.AsReadOnly(records));
            admitted = true;
            return reference;
        }
        catch (PinnedLegacyReferenceException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or JsonException or KeyNotFoundException or
            OverflowException or CryptographicException or System.Security.SecurityException)
        {
            // Public exception data never includes caller-owned paths or raw
            // filesystem/JSON exceptions. The supplied files remain untouched.
            throw Rejected("pinned-legacy-reference-invalid-input-or-file");
        }
        finally
        {
            try { stream?.Dispose(); }
            catch
            {
                // Cleanup cannot replace the original admission failure.
                if (admitted) throw Rejected("pinned-legacy-reference-file-release-failed");
            }
        }
    }

    private static void RequireRegularFile(string path)
    {
        var file = new FileInfo(path);
        Require(file.Exists && (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0,
            "pinned-legacy-reference-invalid-input-or-file");
        for (var parent = file.Directory; parent is not null; parent = parent.Parent)
            Require(parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) == 0,
                "pinned-legacy-reference-invalid-input-or-file");
    }

    private static bool ExactFields(JsonElement item, params string[] expected)
    {
        if (item.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in item.EnumerateObject())
            if (!expected.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) return false;
        return seen.Count == expected.Length;
    }

    private static string? String(JsonElement item) => item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    private static bool DevicePath(string path) => path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
        path.StartsWith(@"\\.\", StringComparison.Ordinal) || path.StartsWith(@"\??\", StringComparison.Ordinal);
    private static PinnedLegacyReferenceException Rejected(string code) => new(code);
    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw Rejected(code);
    }
}
