using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FishAudio.Testing.Evidence;

public sealed record DiagnosticArtifact(string Path, string Sha256);
public sealed record ExportDiagnosticReview(int SchemaVersion, string RuleId,
    IReadOnlyList<DiagnosticArtifact> Evidence, int ReviewedWarningCount, string Scope);

public sealed class DotCoverDiagnosticException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>
/// A narrow export rule for the observed fixed Windows x64 toolchain. This is
/// not a cover/process-completion check and cannot admit a coverage session.
/// Unknown warnings and every error remain invalid. Platform state-machine
/// nodes are retained in the raw report; no product coverage is manufactured.
/// </summary>
public static partial class DotCoverDiagnostics
{
    public const string TimeoutRule = "dotcover-2026.2.3.1-vstest-18.11-timeout-export-v1";
    private const string Engine = "Microsoft.TestPlatform.CrossPlatEngine";
    private const string HostSha = "ff3311bdad71d33a6de17beccc9403fbc4601b005902d331fb102299c25f74c7";
    private const string SdkSha = "6616adb2e638ba4f8235b145b8a4d19038bc11706d67e4e8a6c6a1d76c9e3bb2";
    private static readonly Guid HostMvid = new("b12370bf-542f-4a99-8176-b958678252ff");
    private static readonly Guid SdkMvid = new("f8966587-47d5-463f-880d-0241eac309f2");

    public static ExportDiagnosticReview ReviewExport(string absoluteLog, string absoluteErrorLog,
        string absoluteAggregate, string absoluteSdkEngine, string absoluteHostEngine,
        string sdkVersion, string testHostVersion, string dotCoverVersion)
    {
        Require(sdkVersion == "10.0.403" && testHostVersion == "18.11.0"
            && dotCoverVersion == DotCoverAggregate.SupportedToolVersion, "diagnostic-toolchain-mismatch");
        var log = Read(absoluteLog);
        var errors = Read(absoluteErrorLog);
        var blocks = DiagnosticBlocks(log.Text, requireCompleted: true);
        var errorBlocks = DiagnosticBlocks(errors.Text, requireCompleted: false);
        var evidence = new List<DiagnosticArtifact> { log.Artifact, errors.Artifact };
        var report = DotCoverAggregate.ReadValidated(absoluteAggregate, Engine, dotCoverVersion);
        evidence.Add(new(absoluteAggregate, report.ReportSha256));
        if (blocks.Count == 0)
        {
            Require(errorBlocks.Count == 0 && string.IsNullOrWhiteSpace(errors.Text), "unexpected-export-stderr");
            return new(1, "dotcover-export-clean-v1", evidence.AsReadOnly(), 0, "export-only");
        }

        Require(blocks.Count == 2 && errorBlocks.Count == 2, "unreviewed-export-warning");
        var expected = new[] { "<TimeoutAfterAsync>d__3", "<TimeoutAfterAsync>d__4" };
        for (var index = 0; index < expected.Length; index++)
        {
            var message = "Can't find matching method for compiler generated class " + expected[index]
                + "\nCandidates: TimeoutAfterAsync, TimeoutAfterAsync";
            Require(blocks[index] == ("CompilerGeneratedNestedTypesProcessor", message)
                && errorBlocks[index] == blocks[index], "unreviewed-export-warning");
        }
        // Both modules are observed in the snapshot. The SDK module has no PDB;
        // the TestHost module embeds the linked servermode-client source PDB.
        evidence.Add(ValidateEngine(absoluteSdkEngine, SdkSha, SdkMvid, requireEmbeddedSource: false));
        evidence.Add(ValidateEngine(absoluteHostEngine, HostSha, HostMvid, requireEmbeddedSource: true));
        Require(report.TargetAssembly is not null, "diagnostic-engine-not-in-report");
        foreach (var name in expected)
        {
            var nodes = report.Members.Where(node => node.Kind == "Type" && node.Name == name
                && node.SymbolSegments.Any(segment => segment.Kind == "Type" && segment.Name == "TaskExtensions")).ToArray();
            Require(nodes.Length == 1 && nodes[0].CoveredStatements == 0 && nodes[0].TotalStatements == 10,
                "diagnostic-state-machine-shape-changed");
            var raw = nodes[0].RawPath;
            Require(report.Members.Count(node => node.RawPath.StartsWith(raw + "/", StringComparison.Ordinal)
                && node.Kind == "Method" && node.Name == "MoveNext():void"
                && node.CoveredStatements == 0 && node.TotalStatements == 10) == 1,
                "diagnostic-state-machine-shape-changed");
        }
        Require(report.Members.Count(node => node.Kind == "UnresolvedStateMachineMethod"
            && node.Name is "TimeoutAfterAsync(Task,TimeSpan,bool,string,int):Task"
                or "TimeoutAfterAsync(Task,TimeSpan,CancellationToken,bool,string,int):Task"
            && node.CoveredStatements == 0) == 2, "diagnostic-unresolved-nodes-missing");
        return new(1, TimeoutRule, evidence.AsReadOnly(), 2,
            "export-only; exact platform overload ambiguity; raw unresolved nodes retained; no product exception");
    }

    private static DiagnosticArtifact ValidateEngine(string path, string sha, Guid mvid, bool requireEmbeddedSource)
    {
        var file = Read(path, binary: true);
        Require(file.Artifact.Sha256 == sha, "diagnostic-engine-hash-mismatch");
        try
        {
            using var stream = new MemoryStream(file.Bytes, writable: false);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            Require(metadata.GetString(metadata.GetAssemblyDefinition().Name) == Engine
                && metadata.GetGuid(metadata.GetModuleDefinition().Mvid) == mvid, "diagnostic-engine-identity-mismatch");
            if (requireEmbeddedSource)
            {
                var embedded = pe.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb).ToArray();
                Require(embedded.Length == 1, "diagnostic-engine-pdb-missing");
                using var provider = pe.ReadEmbeddedPortablePdbDebugDirectoryData(embedded[0]);
                var pdb = provider.GetMetadataReader();
                var documents = pdb.Documents.Select(handle => pdb.GetString(pdb.GetDocument(handle).Name)).ToArray();
                Require(documents.Count(name => name == "/_1/microsoft.testing.platform.servermode.client.sources/2.4.0-preview.26410.1/contentFiles/cs/net8.0/Linked/Helpers/TaskExtensions.cs") == 1,
                    "diagnostic-engine-source-mismatch");
            }
        }
        catch (DotCoverDiagnosticException) { throw; }
        catch (Exception error) when (error is BadImageFormatException or InvalidOperationException or IOException)
        { throw new DotCoverDiagnosticException("diagnostic-invalid-engine"); }
        return file.Artifact;
    }

    private static List<(string Component, string Message)> DiagnosticBlocks(string text, bool requireCompleted)
    {
        var blocks = new List<(string, string)>();
        using var reader = new StringReader(text);
        (string Component, string Message)? current = null;
        var completed = false;
        var allowInformationContinuation = false;
        while (reader.ReadLine() is { } line)
        {
            Require(!completed || string.IsNullOrWhiteSpace(line), "content-after-export-completion");
            var match = LogLine().Match(line);
            Require(match.Success || (!line.Contains("|W|", StringComparison.Ordinal)
                && !line.Contains("|E|", StringComparison.Ordinal)
                && !line.Contains("|F|", StringComparison.Ordinal)
                && !Regex.IsMatch(line, @"^\d{2}:\d{2}:\d{2}\.\d{3} \|", RegexOptions.CultureInvariant)),
                "unparsed-export-diagnostic");
            if (match.Success)
            {
                Flush();
                var level = match.Groups["level"].Value;
                Require(level is "I" or "V" or "D" or "W", "export-error-or-unknown-level");
                var message = match.Groups["message"].Value;
                // These are the two observed multiline information sections in
                // the fixed export log. Other orphan text is not a success.
                allowInformationContinuation = level == "I" && match.Groups["component"].Value.Trim() == "Main"
                    && message.TrimEnd() is "Command line:" or "Parsed arguments:";
                if (level == "W") current = (match.Groups["component"].Value.Trim(), message);
                if (message.StartsWith("Console Runner exit code:", StringComparison.Ordinal))
                {
                    Require(!completed && level == "I" && message == "Console Runner exit code: 0", "export-not-completed");
                    completed = true;
                }
            }
            else if (current is { } block)
                current = (block.Component, block.Message + "\n" + line);
            else if (!string.IsNullOrWhiteSpace(line) && (!requireCompleted || !allowInformationContinuation))
                throw new DotCoverDiagnosticException("unparsed-export-content");
        }
        Flush();
        Require(!requireCompleted || completed, "export-not-completed");
        return blocks;
        void Flush() { if (current is { } block) blocks.Add(block); current = null; }
    }

    private static (DiagnosticArtifact Artifact, byte[] Bytes, string Text) Read(string path, bool binary = false)
    {
        Require(Path.IsPathFullyQualified(path), "diagnostic-path-must-be-absolute");
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Require(stream.Length <= 32 * 1024 * 1024, "diagnostic-size-limit");
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            var text = "";
            if (!binary)
            {
                using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
                text = reader.ReadToEnd();
            }
            return (new(path, Convert.ToHexStringLower(SHA256.HashData(bytes))), bytes, text);
        }
        catch (DotCoverDiagnosticException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException)
        { throw new DotCoverDiagnosticException("diagnostic-file-read-failed"); }
    }

    private static void Require(bool condition, string code)
    {
        if (!condition) throw new DotCoverDiagnosticException(code);
    }

    [GeneratedRegex(@"^\d{2}:\d{2}:\d{2}\.\d{3} \|(?<level>[A-Z])\|(?<component>[^|]*)\|[^|]*\| (?<message>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex LogLine();
}
