using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FishAudio.Testing.Evidence;

public sealed record NUnitHostExpectation(int SchemaVersion, string RunId, string SessionId,
    string CanonicalId, string SourceTree, string TestAssembly, string TestAssemblySha256,
    DateTimeOffset CoverStartedUtc, DateTimeOffset CoverFinishedUtc);

public sealed record NUnitHostLifecycleEvidence(int SchemaVersion, string RunId, string SessionId,
    string CanonicalId, string SourceTree, int ProcessId, string AssemblyPath, string AssemblySha256,
    DateTimeOffset StartedUtc, DateTimeOffset FinishedUtc, IReadOnlyList<DiagnosticArtifact> Evidence)
{
    public bool StartCallbackObserved => true;
    public bool EndCallbackObserved => true;
    public bool OsExitClaim => false;
    public bool PipeDrainClaim => false;
    public bool JobOwnershipClaim => false;
    public bool TestPassClaim => false;
    public bool BaselineClaim => false;
}

public sealed class NUnitHostEvidenceException : Exception
{
    internal NUnitHostEvidenceException(string code) : base(code) => Code = code;
    public string Code { get; }
}

/// <summary>
/// Validates the global NUnit setup fixture's observed schema-1 callback pair.
/// The caller supplies independently established session/build and cover times.
/// Files remain caller-owned; callback completion does not imply test success,
/// process exit, pipe drainage, profiler completion or process-tree ownership.
/// </summary>
public static partial class NUnitHostEvidence
{
    private const int MaximumRecordBytes = 64 * 1024;
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    {
        "schemaVersion", "phase", "runId", "sessionId", "canonicalId", "sourceTree", "pid",
        "assemblyPath", "assemblySha256", "startedUtc", "finishedUtc", "baselineClaim",
    };

    public static NUnitHostLifecycleEvidence ReadValidated(string absoluteStart, string absoluteEnd,
        NUnitHostExpectation expected)
    {
        var locks = new List<FileStream>();
        var succeeded = false;
        try
        {
            Require(expected is not null && expected.SchemaVersion == 1 && Owner(expected.RunId) && Owner(expected.SessionId) &&
                Text(expected.CanonicalId, 4096) && GitHash(expected.SourceTree) && Hash(expected.TestAssemblySha256) &&
                Path.IsPathFullyQualified(expected.TestAssembly) && Utc(expected.CoverStartedUtc) && Utc(expected.CoverFinishedUtc) &&
                expected.CoverFinishedUtc >= expected.CoverStartedUtc, "invalid-expectation");
            Require(Path.IsPathFullyQualified(absoluteStart) && Path.IsPathFullyQualified(absoluteEnd), "paths-must-be-absolute");
            var startPath = Path.GetFullPath(absoluteStart);
            var endPath = Path.GetFullPath(absoluteEnd);
            Require(!startPath.Equals(endPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetDirectoryName(startPath), Path.GetDirectoryName(endPath), StringComparison.OrdinalIgnoreCase), "pair-directory-mismatch");
            var startFile = OpenRegular(startPath, MaximumRecordBytes, locks);
            var endFile = OpenRegular(endPath, MaximumRecordBytes, locks);
            var assemblyPath = Path.GetFullPath(expected.TestAssembly);
            var assembly = OpenRegular(assemblyPath, 256 * 1024 * 1024, locks);
            Require(Digest(assembly) == expected.TestAssemblySha256, "assembly-hash-mismatch");
            var start = Read(startFile);
            var end = Read(endFile);
            Require(start.Record.Phase == "start" && end.Record.Phase == "end", "phase-mismatch");
            foreach (var record in new[] { start.Record, end.Record })
            {
                Require(record.RunId == expected.RunId && record.SessionId == expected.SessionId &&
                    record.CanonicalId == expected.CanonicalId && record.SourceTree == expected.SourceTree,
                    "session-identity-mismatch");
                Require(Path.IsPathFullyQualified(record.AssemblyPath) && record.AssemblyPath.Equals(assemblyPath, StringComparison.OrdinalIgnoreCase) &&
                    record.AssemblySha256 == expected.TestAssemblySha256, "assembly-identity-mismatch");
            }
            Require(start.Record.Pid == end.Record.Pid &&
                Path.GetFileName(startPath) == $"host-{start.Record.Pid}.start.json" &&
                Path.GetFileName(endPath) == $"host-{start.Record.Pid}.end.json", "pid-or-file-owner-mismatch");
            Require(start.Record.AssemblyPath == end.Record.AssemblyPath && start.Record.AssemblySha256 == end.Record.AssemblySha256 &&
                start.Record.StartedUtc == end.Record.StartedUtc, "pair-lifecycle-mismatch");
            Require(start.Record.FinishedUtc is null && end.Record.FinishedUtc is { } finished &&
                start.Record.StartedUtc >= expected.CoverStartedUtc && finished >= start.Record.StartedUtc &&
                finished <= expected.CoverFinishedUtc, "lifecycle-time-mismatch");
            foreach (var path in new[] { startPath, endPath })
            {
                var written = new DateTimeOffset(File.GetLastWriteTimeUtc(path));
                Require(written >= expected.CoverStartedUtc && written <= expected.CoverFinishedUtc, "record-outside-cover-envelope");
            }
            succeeded = true;
            return new(1, expected.RunId, expected.SessionId, expected.CanonicalId, expected.SourceTree,
                start.Record.Pid, assemblyPath, expected.TestAssemblySha256, start.Record.StartedUtc,
                end.Record.FinishedUtc!.Value, Array.AsReadOnly(new[]
                {
                    new DiagnosticArtifact(startPath, start.Digest), new DiagnosticArtifact(endPath, end.Digest),
                }));
        }
        catch (NUnitHostEvidenceException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or JsonException or DecoderFallbackException or FormatException or OverflowException or KeyNotFoundException)
        {
            throw Rejected("invalid-record-or-file");
        }
        finally
        {
            var releaseFailed = false;
            foreach (var stream in locks)
            {
                try { stream.Dispose(); }
                catch { releaseFailed = true; }
            }
            if (succeeded && releaseFailed) throw Rejected("file-release-failed");
        }
    }

    private static (HostRecord Record, string Digest) Read(FileStream stream)
    {
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        Require(stream.ReadByte() == -1, "record-size-changed");
        var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
        var json = bytes.AsMemory(offset);
        _ = new UTF8Encoding(false, true).GetCharCount(json.Span);
        var reader = new Utf8JsonReader(json.Span, new JsonReaderOptions { MaxDepth = 4 });
        var properties = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) properties.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) properties.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName)
                Require(properties.Count == 1 && Fields.Contains(reader.GetString()!) && properties.Peek().Add(reader.GetString()!),
                    "unknown-or-duplicate-field");
        }
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
        var root = document.RootElement;
        Require(root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count() == Fields.Count, "required-field-set-mismatch");
        var schema = 0;
        var pid = 0;
        Require(root.GetProperty("schemaVersion").TryGetInt32(out schema) && schema == 1 &&
            root.GetProperty("pid").TryGetInt32(out pid) && pid > 0 && root.GetProperty("baselineClaim").ValueKind == JsonValueKind.False,
            "invalid-schema-pid-or-claim");
        var run = String(root, "runId");
        var session = String(root, "sessionId");
        var canonical = String(root, "canonicalId");
        var tree = String(root, "sourceTree");
        var sha = String(root, "assemblySha256");
        Require(Owner(run) && Owner(session) && Text(canonical, 4096) && GitHash(tree) && Hash(sha), "invalid-record-identity");
        var finished = root.GetProperty("finishedUtc");
        return (new(String(root, "phase"), run, session, canonical, tree, pid, String(root, "assemblyPath"), sha,
            Time(root.GetProperty("startedUtc")), finished.ValueKind == JsonValueKind.Null ? null : Time(finished)),
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private static string String(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        Require(value.ValueKind == JsonValueKind.String, "invalid-field-type");
        var text = value.GetString()!;
        Require(Text(text, 32768), "invalid-field-text");
        return text;
    }

    private static DateTimeOffset Time(JsonElement element)
    {
        Require(element.ValueKind == JsonValueKind.String, "invalid-utc-time");
        var text = element.GetString()!;
        var value = DateTimeOffset.MinValue;
        Require(UtcText().IsMatch(text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out value) && Utc(value), "invalid-utc-time");
        return value;
    }

    private static FileStream OpenRegular(string path, int maximum, List<FileStream> locks)
    {
        var file = new FileInfo(path);
        file.Refresh();
        Require(file.Exists && (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0, "missing-or-nonregular-file");
        for (var directory = file.Directory; directory is not null; directory = directory.Parent)
        {
            directory.Refresh();
            Require(directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) == 0, "missing-or-reparse-directory");
        }
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        locks.Add(stream);
        Require(stream.Length > 0 && stream.Length <= maximum, "file-size-limit");
        return stream;
    }

    private static string Digest(FileStream stream)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        stream.Position = 0;
        return hash;
    }

    private static bool Owner(string? value) => Guid.TryParseExact(value, "N", out var id) && id != Guid.Empty && value == id.ToString("N");
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool GitHash(string? value) => value is { Length: 40 or 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);
    private static bool Utc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;
    private static NUnitHostEvidenceException Rejected(string code) => new("host-evidence-" + code);
    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw Rejected(code);
    }

    private sealed record HostRecord(string Phase, string RunId, string SessionId, string CanonicalId, string SourceTree,
        int Pid, string AssemblyPath, string AssemblySha256, DateTimeOffset StartedUtc, DateTimeOffset? FinishedUtc);

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|\+00:00)$", RegexOptions.CultureInvariant)]
    private static partial Regex UtcText();
}
