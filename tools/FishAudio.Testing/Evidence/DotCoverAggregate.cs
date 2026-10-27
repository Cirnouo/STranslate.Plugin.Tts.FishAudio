using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FishAudio.Testing.Evidence;

public sealed record DotCoverSymbolSegment(string Kind, string? Name);

public sealed record DotCoverMemberRecord(string AssemblyName, string Kind, string? Name,
    IReadOnlyList<DotCoverSymbolSegment> SymbolSegments, string RawPath, long CoveredStatements,
    long TotalStatements, int CoveragePercent, IReadOnlyList<string> UnresolvedReasons)
{
    // False means the node kinds are recognized, not that a CLR/Roslyn
    // symbol, source location or test relation has been resolved.
    public bool Unresolved => UnresolvedReasons.Count != 0;
}

// ObservedSchemaVersion identifies this reader's shape, not a version claimed
// by the input JSON. ReportPath is private provenance; RawPath is a JSON pointer.
public sealed record DotCoverAggregateReport(int ObservedSchemaVersion, string ToolVersion,
    string ReportPath, string ReportSha256, string TargetAssemblyName,
    DotCoverMemberRecord? TargetAssembly, IReadOnlyList<DotCoverMemberRecord> Members);

public sealed class DotCoverAggregateException : Exception
{
    internal DotCoverAggregateException(string code) : base(code) => Code = code;
    public string Code { get; }
}

/// <summary>
/// Reads the observed aggregate JSON shape from modern dotCover CLI 2026.2.3.1.
/// The caller supplies independently established tool provenance. This reader
/// does not authenticate the tool, bind an isolated session to a test, sum
/// hierarchical counts, or establish source/member coverage relations.
/// </summary>
public static class DotCoverAggregate
{
    public const string SupportedToolVersion = "2026.2.3.1";
    private const int MaximumBytes = 64 * 1024 * 1024;
    private const int MaximumNodes = 100_000;
    private const int MaximumTokens = 4_000_000;
    private const int MaximumDepth = 128;
    private const int MaximumValueLength = 16_384;
    private static readonly HashSet<string> NodeProperties = new(StringComparer.Ordinal)
    {
        "Kind", "Name", "CoveredStatements", "TotalStatements", "CoveragePercent", "Children",
    };
    private static readonly HashSet<string> KnownKinds = new(StringComparer.Ordinal)
    {
        "Root", "Assembly", "Namespace", "Type", "Method", "Constructor", "Property", "AutoProperty",
        "Indexer", "Event", "PropertyGetter", "PropertySetter", "PropertyInitOnlySetter", "EventAdder",
        "EventRemover", "AnonymousMethod", "LocalFunction", "InternalCompiledMethod", "OwnCoverage",
        "UnresolvedStateMachineMethod",
    };
    private static readonly HashSet<string> UnnamedKinds = new(StringComparer.Ordinal)
    {
        "Root", "PropertyGetter", "PropertySetter", "PropertyInitOnlySetter", "EventAdder", "EventRemover",
        "OwnCoverage",
    };
    private static readonly HashSet<string> CompilerKinds = new(StringComparer.Ordinal)
    {
        "AnonymousMethod", "LocalFunction", "InternalCompiledMethod", "OwnCoverage",
    };

    public static DotCoverAggregateReport ReadValidated(string absoluteReportPath, string exactTargetAssemblyName,
        string toolVersion)
    {
        Require(toolVersion == SupportedToolVersion, "unsupported-dotcover-version");
        Require(IsIdentity(exactTargetAssemblyName), "invalid-target-assembly");
        Require(!string.IsNullOrWhiteSpace(absoluteReportPath) && Path.IsPathFullyQualified(absoluteReportPath),
            "aggregate-path-must-be-absolute");
        var bytes = ReadBytes(absoluteReportPath);
        try
        {
            // A UTF-8 BOM, when present, remains part of the raw-file digest.
            var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            var json = bytes.AsMemory(offset);
            _ = new UTF8Encoding(false, true).GetCharCount(json.Span);
            ValidateJson(json.Span);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaximumDepth });
            var root = document.RootElement;
            ValidateNode(root);
            Require(Identity(root, "Kind") == "Root" && !root.TryGetProperty("Name", out _), "invalid-aggregate-root");
            Require(root.TryGetProperty("Children", out var assemblies) && assemblies.ValueKind == JsonValueKind.Array,
                "missing-root-assemblies");
            var names = new HashSet<string>(StringComparer.Ordinal);
            var members = new List<DotCoverMemberRecord>();
            DotCoverMemberRecord? target = null;
            var index = 0;
            foreach (var assembly in assemblies.EnumerateArray())
            {
                ValidateNode(assembly);
                Require(Identity(assembly, "Kind") == "Assembly", "invalid-root-assembly");
                var name = Identity(assembly, "Name");
                Require(names.Add(name), "duplicate-assembly");
                var selected = name == exactTargetAssemblyName;
                Visit(assembly, name, "/Children/" + index, [], [], ambiguous: false, isAssembly: true,
                    selected ? members : null);
                if (selected) target = members[0];
                index++;
            }
            return new(1, toolVersion, absoluteReportPath, Convert.ToHexStringLower(SHA256.HashData(bytes)),
                exactTargetAssemblyName, target, members.AsReadOnly());
        }
        catch (DotCoverAggregateException) { throw; }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or InvalidOperationException)
        {
            // Never publish raw JSON, caller paths or the provider's exception.
            throw Rejected("invalid-aggregate-json");
        }
    }

    private static void Visit(JsonElement node, string assemblyName, string rawPath,
        IReadOnlyList<DotCoverSymbolSegment> ancestors, IReadOnlyList<string> inheritedReasons,
        bool ambiguous, bool isAssembly, List<DotCoverMemberRecord>? output)
    {
        ValidateNode(node);
        var kind = Identity(node, "Kind");
        Require(isAssembly || kind is not ("Root" or "Assembly"), "nested-root-or-assembly");
        var name = node.TryGetProperty("Name", out _) ? Identity(node, "Name") : null;
        Require(name is not null || UnnamedKinds.Contains(kind) || !KnownKinds.Contains(kind), "missing-node-name");
        var reasons = new List<string>(inheritedReasons);
        AddReason(!KnownKinds.Contains(kind), "unknown-kind");
        AddReason(kind == "UnresolvedStateMachineMethod", "unresolved-state-machine");
        AddReason(CompilerKinds.Contains(kind), "compiler-generated-member");
        AddReason(ambiguous, "ambiguous-sibling-symbol");
        var segments = ancestors.Append(new DotCoverSymbolSegment(kind, name)).ToArray();
        var frozenReasons = reasons.AsReadOnly();
        if (output is not null)
            output.Add(new(assemblyName, kind, name, Array.AsReadOnly(segments), rawPath,
                Count(node, "CoveredStatements"), Count(node, "TotalStatements"), Percent(node), frozenReasons));

        if (!node.TryGetProperty("Children", out var children)) return;
        var identities = new Dictionary<(string Kind, string? Name), int>();
        foreach (var child in children.EnumerateArray())
        {
            ValidateNode(child);
            var identity = ChildIdentity(child);
            identities[identity] = identities.GetValueOrDefault(identity) + 1;
        }
        var index = 0;
        foreach (var child in children.EnumerateArray())
        {
            // Real unresolved state-machine reports contain identical sibling
            // names. Their raw positions distinguish them without inventing a
            // unique source/CLR symbol or discarding either subtree.
            Visit(child, assemblyName, rawPath + "/Children/" + index, segments, frozenReasons,
                identities[ChildIdentity(child)] > 1, isAssembly: false, output);
            index++;
        }
        return;

        void AddReason(bool condition, string reason)
        {
            if (condition && !reasons.Contains(reason, StringComparer.Ordinal)) reasons.Add(reason);
        }
    }

    private static (string Kind, string? Name) ChildIdentity(JsonElement node)
        => (Identity(node, "Kind"), node.TryGetProperty("Name", out _) ? Identity(node, "Name") : null);

    private static void ValidateNode(JsonElement node)
    {
        Require(node.ValueKind == JsonValueKind.Object, "invalid-aggregate-node");
        foreach (var property in node.EnumerateObject())
            Require(NodeProperties.Contains(property.Name), "unsupported-node-property");
        _ = Identity(node, "Kind");
        if (node.TryGetProperty("Name", out _)) _ = Identity(node, "Name");
        var covered = Count(node, "CoveredStatements");
        var total = Count(node, "TotalStatements");
        Require(covered <= total, "covered-exceeds-total");
        _ = Percent(node);
        if (node.TryGetProperty("Children", out var children))
            Require(children.ValueKind == JsonValueKind.Array, "invalid-node-children");
    }

    private static long Count(JsonElement node, string property)
    {
        Require(node.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt64(out var count) && count >= 0, "invalid-statement-count");
        return value.GetInt64();
    }

    private static int Percent(JsonElement node)
    {
        Require(node.TryGetProperty("CoveragePercent", out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var percent) && percent is >= 0 and <= 100, "invalid-coverage-percent");
        // The observed CLI rounds this field. Do not reconstruct statement
        // counts from it or impose equality to a recomputed fraction.
        return value.GetInt32();
    }

    private static string Identity(JsonElement node, string property)
    {
        Require(node.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String,
            "invalid-node-identity");
        var identity = value.GetString();
        Require(IsIdentity(identity), "invalid-node-identity");
        return identity!;
    }

    private static bool IsIdentity(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumValueLength && !value.Any(char.IsControl);

    private static byte[] ReadBytes(string path)
    {
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Require(input.Length is > 0 and <= MaximumBytes, "aggregate-size-limit");
            var bytes = new byte[(int)input.Length];
            input.ReadExactly(bytes);
            Require(input.ReadByte() == -1, "aggregate-size-changed");
            return bytes;
        }
        catch (DotCoverAggregateException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw Rejected("aggregate-read-failed");
        }
    }

    private static void ValidateJson(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = MaximumDepth });
        var objects = new Stack<HashSet<string>>();
        var nodes = 0;
        var tokens = 0;
        while (reader.Read())
        {
            Require(++tokens <= MaximumTokens, "aggregate-structure-limit");
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                Require(++nodes <= MaximumNodes, "aggregate-structure-limit");
                objects.Push(new(StringComparer.Ordinal));
            }
            else if (reader.TokenType == JsonTokenType.EndObject) objects.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                Require(reader.ValueSpan.Length <= 128 && objects.Peek().Add(reader.GetString()!),
                    "duplicate-or-oversized-json-property");
            }
            else if (reader.TokenType == JsonTokenType.String)
                Require(reader.ValueSpan.Length <= MaximumValueLength * 6, "aggregate-value-limit");
        }
    }

    private static DotCoverAggregateException Rejected(string code) => new(code);

    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw Rejected(code);
    }
}
