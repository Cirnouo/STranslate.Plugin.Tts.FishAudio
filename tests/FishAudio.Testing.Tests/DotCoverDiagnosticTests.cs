using System.Text;
using System.Text.Json.Nodes;
using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class DotCoverDiagnosticTests
{
    private const string Completed = "20:03:09.501 |I| Main | :2 | Console Runner exit code: 0\n";
    private const string Warnings = """
        20:03:09.014 |W| CompilerGeneratedNestedTypesProcessor| JetPool(S) #6:23 | Can't find matching method for compiler generated class <TimeoutAfterAsync>d__3
        Candidates: TimeoutAfterAsync, TimeoutAfterAsync
        20:03:09.015 |W| CompilerGeneratedNestedTypesProcessor| JetPool(S) #6:23 | Can't find matching method for compiler generated class <TimeoutAfterAsync>d__4
        Candidates: TimeoutAfterAsync, TimeoutAfterAsync
        """ + "\n";
    private string _root = "";
    private string _log = "";
    private string _error = "";
    private string _report = "";

    [SetUp]
    public void Prepare()
    {
        _root = Path.Combine(Path.GetTempPath(), "fa-export-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _log = Write("export.log", Warnings + Completed);
        _error = Write("export.err.log", Warnings);
        var engine = Node("Assembly", "Microsoft.TestPlatform.CrossPlatEngine", 0, 30);
        var helper = Node("Type", "TaskExtensions", 0, 30);
        engine["Children"] = new JsonArray(helper);
        var children = new JsonArray();
        foreach (var state in new[] { "<TimeoutAfterAsync>d__3", "<TimeoutAfterAsync>d__4" })
        {
            var type = Node("Type", state, 0, 10);
            type["Children"] = new JsonArray(Node("Method", "MoveNext():void", 0, 10));
            children.Add(type);
        }
        children.Add(Node("UnresolvedStateMachineMethod", "TimeoutAfterAsync(Task,TimeSpan,bool,string,int):Task", 0, 0));
        children.Add(Node("UnresolvedStateMachineMethod", "TimeoutAfterAsync(Task,TimeSpan,CancellationToken,bool,string,int):Task", 0, 10));
        helper["Children"] = children;
        var root = Node("Root", null, 0, 30);
        root["Children"] = new JsonArray(engine);
        _report = Write("coverage.json", root.ToJsonString());
    }

    [TearDown]
    public void Cleanup()
    {
        Assert.That(Path.GetDirectoryName(_root), Is.EqualTo(Path.TrimEndingDirectorySeparator(Path.GetTempPath())).IgnoreCase);
        Assert.That(Path.GetFileName(_root), Does.StartWith("fa-export-diagnostics-"));
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void ExactWarningsRequireBothPinnedEnginesAndRetainedUnresolvedNodes()
    {
        var review = Review();
        Assert.That(review.RuleId, Is.EqualTo(DotCoverDiagnostics.TimeoutRule));
        Assert.That(review.ReviewedWarningCount, Is.EqualTo(2));
        Assert.That(review.Evidence, Has.Count.EqualTo(5));
        Assert.That(review.Evidence.All(item => item.Sha256.Length == 64), Is.True);
        Assert.That(review.Scope, Does.Contain("export-only").And.Contain("no product exception"));
    }

    [Test]
    public void CleanExportHasNoWarningExceptionAndDoesNotNeedEngineException()
    {
        File.WriteAllText(_log, Completed);
        File.WriteAllText(_error, "");
        var review = Review(sdkEngine: Path.Combine(_root, "absent.dll"));
        Assert.That(review.RuleId, Is.EqualTo("dotcover-export-clean-v1"));
        Assert.That(review.ReviewedWarningCount, Is.Zero);
        Assert.That(review.Evidence, Has.Count.EqualTo(3));
    }

    [TestCase(false, TestName = "Unparsed_failure_after_clean_completion_is_rejected")]
    [TestCase(true, TestName = "Unparsed_failure_after_reviewed_warning_completion_is_rejected")]
    public void CompletionDoesNotHideLaterUnstructuredFailure(bool warnings)
    {
        File.WriteAllText(_log, (warnings ? Warnings : "") + Completed + "Abnormal application termination\n");
        File.WriteAllText(_error, warnings ? Warnings : "");
        Assert.That(() => Review(), Throws.TypeOf<DotCoverDiagnosticException>());
    }

    [Test]
    public void SelectedCustomSdkInstallationUsesItsOwnPinnedEngine()
    {
        var customHost = Path.Combine(_root, "selected-dotnet", "dotnet.exe");
        var engine = SdkEngineForHost(customHost);
        Directory.CreateDirectory(Path.GetDirectoryName(engine)!);
        File.Copy(SdkEngineForHost(SelectedDotnet()), engine);
        Assert.That(Review(dotnetHost: customHost).RuleId, Is.EqualTo(DotCoverDiagnostics.TimeoutRule));
    }

    [TestCase("error", TestName = "Export_error_is_not_a_warning_exception")]
    [TestCase("writer", TestName = "Writer_warning_is_not_a_platform_exception")]
    [TestCase("extra", TestName = "Additional_warning_invalidates_review")]
    [TestCase("identity", TestName = "Different_state_machine_is_unreviewed")]
    [TestCase("candidate", TestName = "Missing_overload_evidence_is_unreviewed")]
    [TestCase("component", TestName = "Different_warning_component_is_unreviewed")]
    [TestCase("malformed", TestName = "Malformed_warning_prefix_does_not_disappear")]
    [TestCase("level", TestName = "Unknown_log_level_is_rejected")]
    [TestCase("stderr", TestName = "Unstructured_export_stderr_is_rejected")]
    [TestCase("unfinished", TestName = "Missing_console_completion_invalidates_export")]
    [TestCase("exit", TestName = "Nonzero_console_completion_invalidates_export")]
    [TestCase("duplicate", TestName = "Duplicated_completion_invalidates_export")]
    [TestCase("orphan-before", TestName = "Unstructured_failure_before_log_is_rejected")]
    [TestCase("orphan-between", TestName = "Unstructured_failure_after_unrelated_information_is_rejected")]
    public void ChangedDiagnosticNeverUsesTheNarrowRule(string fault)
    {
        var text = File.ReadAllText(_log);
        text = fault switch
        {
            "error" => text.Replace("|W|", "|E|", StringComparison.Ordinal),
            "writer" => text.Replace("Can't find matching method", "Writer exception", StringComparison.Ordinal),
            "extra" => Warnings + text,
            "identity" => text.Replace("d__3", "d__5", StringComparison.Ordinal),
            "candidate" => text.Replace("Candidates: TimeoutAfterAsync, TimeoutAfterAsync\n", "", StringComparison.Ordinal),
            "component" => text.Replace("CompilerGeneratedNestedTypesProcessor", "Other", StringComparison.Ordinal),
            "malformed" => text.Replace("20:03:09.014", "broken-time", StringComparison.Ordinal),
            "level" => text.Replace("|W|", "|X|", StringComparison.Ordinal),
            "unfinished" => text.Replace(Completed, "", StringComparison.Ordinal),
            "exit" => text.Replace("exit code: 0", "exit code: 1", StringComparison.Ordinal),
            "duplicate" => text + Completed,
            "orphan-before" => "Abnormal application termination\n" + text,
            "orphan-between" => text.Replace(Completed,
                "20:03:09.500 |I| Main | :2 | Snapshot container built\nAbnormal application termination\n" + Completed, StringComparison.Ordinal),
            _ => text,
        };
        File.WriteAllText(_log, text);
        if (fault == "stderr") File.AppendAllText(_error, "writer failed\n");
        Assert.That(() => Review(), Throws.TypeOf<DotCoverDiagnosticException>());
    }

    [Test]
    public void EngineBinaryMismatchIsRejectedBeforeGrantingException()
    {
        var wrong = Write("wrong.dll", "not the pinned engine");
        Assert.That(() => Review(sdkEngine: wrong), Throws.TypeOf<DotCoverDiagnosticException>()
            .With.Property("Code").EqualTo("diagnostic-engine-hash-mismatch"));
    }

    [Test]
    public void DifferentToolchainDoesNotInheritThisVersionedRule()
        => Assert.That(() => Review(sdk: "10.0.404"), Throws.TypeOf<DotCoverDiagnosticException>()
            .With.Property("Code").EqualTo("diagnostic-toolchain-mismatch"));

    [TestCase("missing", TestName = "Missing_platform_nodes_prevent_warning_review")]
    [TestCase("covered", TestName = "Executed_ambiguous_platform_path_prevents_warning_review")]
    [TestCase("unresolved", TestName = "Dropped_unresolved_platform_node_prevents_warning_review")]
    public void ChangedReportInvalidatesTheWarningEvidence(string fault)
    {
        var root = JsonNode.Parse(File.ReadAllText(_report))!;
        var children = root["Children"]![0]!["Children"]![0]!["Children"]!.AsArray();
        if (fault == "missing") children.RemoveAt(0);
        else if (fault == "unresolved") children.RemoveAt(children.Count - 1);
        else children[0]!["CoveredStatements"] = 1;
        File.WriteAllText(_report, root.ToJsonString());
        Assert.That(() => Review(), Throws.TypeOf<DotCoverDiagnosticException>());
    }

    private ExportDiagnosticReview Review(string? sdkEngine = null, string sdk = "10.0.403", string? dotnetHost = null)
        => DotCoverDiagnostics.ReviewExport(_log, _error, _report,
            sdkEngine ?? SdkEngineForHost(dotnetHost ?? SelectedDotnet()),
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Microsoft.TestPlatform.CrossPlatEngine.dll"),
            sdk, "18.11.0", "2026.2.3.1");

    private static string SelectedDotnet()
    {
        // The driver supplies the exact host it resolved from the active PATH.
        // A direct vstest diagnostic falls back to its actual shared runtime's
        // installation, never an unrelated ProgramFiles SDK.
        var selected = Environment.GetEnvironmentVariable("FISHAUDIO_DOTNET_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(selected))
        {
            Assert.That(Path.IsPathFullyQualified(selected) && File.Exists(selected), Is.True);
            return selected;
        }
        var runtime = new DirectoryInfo(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
        var installation = runtime.Parent?.Parent?.Parent?.FullName ?? throw new InvalidOperationException("selected-dotnet-runtime-root-missing");
        var host = Path.Combine(installation, "dotnet.exe");
        Assert.That(File.Exists(host), Is.True);
        return host;
    }

    private static string SdkEngineForHost(string host)
        => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(host))!, "sdk", "10.0.403", "Microsoft.TestPlatform.CrossPlatEngine.dll");

    private string Write(string name, string text)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    private static JsonObject Node(string kind, string? name, int covered, int total)
    {
        var node = new JsonObject { ["Kind"] = kind, ["CoveredStatements"] = covered,
            ["TotalStatements"] = total, ["CoveragePercent"] = 0 };
        if (name is not null) node["Name"] = name;
        return node;
    }
}
