using System.Text.RegularExpressions;

namespace FishAudio.Testing;

internal sealed class ProbeReport
{
    public string Command => "workspace-probe";
    public string Scope => "two-semantic-checks-only";
    public bool CompleteTiaGraph => false;
    public bool Baseline => false;
    public bool Passed { get; set; }
    public bool Coverage { get; init; }
    public string? Project { get; set; }
    public ToolchainEvidence? Toolchain { get; set; }
    public Dictionary<string, string> RequestedProperties { get; } = [];
    public List<ProjectEvidence> Projects { get; } = [];
    public List<DiagnosticEvidence> WorkspaceDiagnostics { get; } = [];
    public int WorkspaceFailureCount => WorkspaceDiagnostics.Count(diagnostic => diagnostic.Kind == "Failure");
    public int CompilationErrorCount => Projects.Sum(project => project.Errors.Count);
    public bool DiagnosticMessagesOmitted => true;
    public List<ProbeFailure> Failures { get; } = [];
    public List<SemanticCheck> Checks { get; } =
    [
        new("nunit-sync-product-source-binding"),
        new("wpf-initialize-component-generated-source-binding"),
    ];
}

internal sealed class ToolchainEvidence
{
    public string? ProbeRuntimeVersion { get; init; }
    public string? LocatorVersion { get; init; }
    public string? SelectedSdkVersion { get; init; }
    public string? SelectedMSBuildFileVersion { get; init; }
    public string? WorkspaceVersion { get; set; }
    public string? CSharpVersion { get; set; }
    public string? EvaluatedTestSdkVersion { get; set; }
    public string? EvaluatedTestConfiguration { get; set; }
    public string WorkspaceHostMSBuildVersion => "not-exposed-by-public-workspace-api";
}

internal sealed class ProjectEvidence(string project)
{
    public string Project { get; } = project;
    public string? LanguageVersion { get; set; }
    public string? Optimization { get; set; }
    public string[] PreprocessorSymbols { get; set; } = [];
    public List<string> Documents { get; } = [];
    public List<string> GeneratedDocuments { get; } = [];
    public List<string> SyntaxTrees { get; } = [];
    public List<string> Analyzers { get; } = [];
    public List<string> ProjectReferences { get; } = [];
    public int MetadataReferenceCount { get; set; }
    public List<DiagnosticEvidence> Errors { get; } = [];
}

internal sealed class SemanticCheck(string id)
{
    public string Id { get; } = id;
    public bool Attempted { get; set; }
    public bool Passed { get; set; }
    public string Reason { get; set; } = "not-attempted";
    public SourceLocation? Invocation { get; set; }
    public SourceLocation? Declaration { get; set; }
}

internal sealed record ProbeFailure(string Stage, string Reason, string? ExceptionType = null);
internal sealed record SourceLocation(string File, int Line, int Column);
internal sealed record DiagnosticEvidence(string Kind, string Code, SourceLocation? Location = null);

internal static partial class ProbePaths
{
    internal const string ProductProject = "STranslate.Plugin.Tts.FishAudio/STranslate.Plugin.Tts.FishAudio.csproj";
    internal const string TestProject = "tests/STranslate.Plugin.Tts.FishAudio.Tests/STranslate.Plugin.Tts.FishAudio.Tests.csproj";
    internal const string ProductDirectory = "STranslate.Plugin.Tts.FishAudio";

    internal static bool Same(string? left, string right)
        => left is not null && Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    internal static string Relative(string root, string? file)
    {
        if (string.IsNullOrEmpty(file)) return "[no-file]";
        if (!Path.IsPathFullyQualified(file)) return "[generated]/" + Token(Path.GetFileName(file));
        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        return relative.Length <= 512 && relative != ".." && !relative.StartsWith("../", StringComparison.Ordinal)
            && !Path.IsPathFullyQualified(relative) ? relative : "[external]/" + Token(Path.GetFileName(file));
    }

    internal static string Token(string? value)
        => value is { Length: > 0 and <= 160 } && SafeToken().IsMatch(value) ? value : "[unavailable]";

    internal static string DiagnosticCode(string message)
    {
        var match = CodeToken().Match(message);
        return match.Success ? match.Value : "workspace-diagnostic";
    }

    [GeneratedRegex(@"\A[A-Za-z0-9_.+`-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeToken();
    [GeneratedRegex(@"\b(?:CS|MSB|NETSDK|NU)\d{4,5}\b", RegexOptions.CultureInvariant)]
    private static partial Regex CodeToken();
}
