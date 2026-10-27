using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FishAudio.Testing.Evidence;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class IsolatedCoverageSessionTests
{
    // Hand-declared, sanitized NUnit/CLI shapes. The snapshot is opaque fixture
    // bytes: these tests admit evidence contracts, not actual product coverage.
    private const string CanonicalId = "session/example";
    private static readonly CoverageArtifactKind[] DiagnosticKinds =
    [CoverageArtifactKind.CoverStdout, CoverageArtifactKind.CoverStderr, CoverageArtifactKind.CoverLog,
        CoverageArtifactKind.ExportStdout, CoverageArtifactKind.ExportStderr, CoverageArtifactKind.ExportLog];
    private string _root = "";
    private string _parent = "";
    private IsolatedCoverageSessionInput _input = null!;
    private IsolatedCoverageAdmission _expected = null!;
    private DateTimeOffset _started;

    [SetUp]
    public void CreateOwnedSession()
    {
        var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
        var parent = string.IsNullOrEmpty(configured) ? Path.GetTempPath() : configured;
        Assert.That(Path.IsPathFullyQualified(parent) && Directory.Exists(parent), Is.True);
        _parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        for (var directory = new DirectoryInfo(_parent); directory is not null; directory = directory.Parent)
            Assert.That((directory.Attributes & FileAttributes.ReparsePoint) == 0, Is.True);
        _root = Path.Combine(_parent, "fa-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        foreach (var directory in new[] { "raw", "build", "source" }) Directory.CreateDirectory(Path.Combine(_root, directory));
        _started = DateTimeOffset.UtcNow.AddMinutes(-2);
        var identity = new CoverageSessionIdentity(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), CanonicalId,
            new(new string('a', 40), new string('b', 40), new string('c', 64)), new string('d', 64));
        var binaries = new[]
        {
            CopyPair(typeof(BinaryIdentity).Assembly.Location, "product", CoverageBinaryRole.Product),
            CopyPair(typeof(IsolatedCoverageSessionTests).Assembly.Location, "tests", CoverageBinaryRole.Tests),
        };
        var cover = new CoverageProcessCompletion(_started.AddSeconds(1), _started.AddSeconds(10), true, 0, true, true, true, []);
        var export = new CoverageProcessCompletion(_started.AddSeconds(11), _started.AddSeconds(12), true, 0, true, true, true, []);
        var artifacts = new List<CoverageArtifact>();
        foreach (var kind in Enum.GetValues<CoverageArtifactKind>())
        {
            var contents = kind switch
            {
                CoverageArtifactKind.Discovery => Discovery,
                CoverageArtifactKind.NUnitXml => Terminal,
                CoverageArtifactKind.Trx => Trx,
                CoverageArtifactKind.Snapshot => "opaque-native-snapshot-fixture",
                CoverageArtifactKind.AggregateJson => Aggregate,
                CoverageArtifactKind.CoverInvocation => Invocation(identity, cover: true),
                CoverageArtifactKind.ExportInvocation => Invocation(identity, cover: false),
                CoverageArtifactKind.CoverProcess => ProcessRecord(cover),
                CoverageArtifactKind.ExportProcess => ProcessRecord(export),
                _ => "",
            };
            artifacts.Add(WriteArtifact(kind, contents));
        }
        var review = new CoverageDiagnosticReview(CoverageDiagnosticDisposition.Clean, "fixture-clean-v1", new string('e', 64),
            artifacts.Where(item => DiagnosticKinds.Contains(item.Kind)).Select(item => new CoverageDiagnosticBinding(item.Kind, item.Sha256)).ToArray());
        var supervisor = new CoverageSupervisorReceipt(1, identity, _started, _started.AddSeconds(20), cover, export, review, artifacts.ToArray());
        var tool = Path.Combine(_root, "build", "pinned-tool.fixture");
        File.WriteAllText(tool, "independently pinned tool fixture", new UTF8Encoding(false));
        _input = new(1, identity, Path.Combine(_root, "raw"), artifacts, null);
        _expected = new(1, identity, Path.Combine(_root, "source"), Path.Combine(_root, "build"), binaries,
            artifacts.Single(item => item.Kind == CoverageArtifactKind.Discovery).Sha256,
            "2026.2.3.1", tool, Hash(tool), false, supervisor);
        File.WriteAllText(Path.Combine(_root, "source/README.md"), "repository resource fixture", new UTF8Encoding(false));
    }

    [TearDown]
    public void ReleaseOwnedSession()
    {
        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
        {
            TestContext.Error.WriteLine("isolated-session: failed evidence retained as " + Path.GetFileName(_root) + ".");
            return;
        }
        var name = Path.GetFileName(_root);
        Assert.That(string.Equals(Path.GetDirectoryName(Path.GetFullPath(_root)), _parent, StringComparison.OrdinalIgnoreCase) &&
            name.StartsWith("fa-session-", StringComparison.Ordinal) && Guid.TryParseExact(name[11..], "N", out _) &&
            (File.GetAttributes(_root) & FileAttributes.ReparsePoint) == 0, Is.True);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void SinglePassedLeafBindsActualBinariesRawEvidenceAndUnresolvedAggregateSymbols()
    {
        var before = _input.Artifacts.ToDictionary(item => item.RelativePath, item => File.ReadAllBytes(ArtifactPath(item.Kind)));
        var admitted = Read();
        Assert.That(admitted.Identity, Is.EqualTo(_expected.Identity));
        Assert.That(admitted.TestResult.CanonicalId, Is.EqualTo(CanonicalId));
        Assert.That(admitted.TestResult.FullName, Is.EqualTo("Example.Cases.Probe"));
        Assert.That(admitted.TestResult.Duration, Is.EqualTo(TimeSpan.FromMilliseconds(125)));
        Assert.That(admitted.TestResult.PlatformDuration, Is.EqualTo(TimeSpan.FromMilliseconds(125)));
        Assert.That(admitted.Binaries.Select(item => item.Identity.AssemblyName), Is.EqualTo(new[] { "FishAudio.Testing", "FishAudio.Testing.Tests" }));
        Assert.That(admitted.Aggregate.TargetAssembly!.CoveredStatements, Is.EqualTo(3));
        Assert.That(admitted.UnresolvedSymbols, Has.Count.EqualTo(3));
        Assert.That(admitted.UnresolvedSymbols.Last().Observation.UnresolvedReasons, Does.Contain("compiler-generated-member"));
        Assert.That(admitted.CompleteBaseline, Is.False);
        foreach (var artifact in _input.Artifacts) Assert.That(File.ReadAllBytes(ArtifactPath(artifact.Kind)), Is.EqualTo(before[artifact.RelativePath]));
    }

    [TestCase(0.5, false, TestName = "isolated-session/child-proof-before-cover-is-rejected")]
    [TestCase(5.0, true, TestName = "isolated-session/child-proof-inside-cover-remains-admissible")]
    [TestCase(10.5, false, TestName = "isolated-session/child-proof-after-cover-is-rejected")]
    public void OwnedChildProofMustBelongToTheCoverWindow(double seconds, bool accepted)
    {
        // The trusted supervisor validates the child protocol separately. This
        // metadata control changes only its sealed proof's timestamp, keeping
        // both invalid samples inside the broader overall session window.
        var path = Path.Combine(_root, "raw", "child-proof.json");
        File.WriteAllText(path, "supervisor-validated-owned-child-proof", new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(path, _started.AddSeconds(seconds).UtcDateTime);
        var proof = new CoverageOwnedProcessEvidence("child-proof.json", Hash(path),
            new FileInfo(path).Length, new(File.GetLastWriteTimeUtc(path)));
        _expected = _expected with { RequireOwnedAppHost = true };
        ChangeCover(_expected.Supervisor.Cover with
        {
            OwnedProcesses = [new(1234, CoverageProcessRole.OwnedAppHost, true, 0, true, true) { Evidence = [proof] }],
        });
        if (accepted) Assert.That(Read().Supervisor.Cover.OwnedProcesses, Has.Count.EqualTo(1));
        else Assert.That(Assert.Throws<IsolatedCoverageSessionException>(() => Read())!.Code,
            Is.EqualTo("coverage-session-invalid-owned-process-proof"));
    }

    [TestCase(false, TestName = "isolated-session/empty-product-assembly-with-repository-dependency")]
    [TestCase(true, TestName = "isolated-session/absent-product-assembly-with-repository-dependency")]
    public void ExplicitRepositoryResourceCoverageMayHaveNoProductStatements(bool absent)
    {
        SetAggregateEmpty(absent);
        _input = _input with { EmptyProduct = ResourceExplanation() };
        var admitted = Read();
        Assert.That(admitted.EmptyProduct!.Reason, Is.EqualTo(EmptyProductCoverageReason.RepositoryResourcesOnly));
        Assert.That(admitted.EmptyProduct.Dependencies.Single().RelativePath, Is.EqualTo("README.md"));
        Assert.That(admitted.Aggregate.TargetAssembly?.CoveredStatements ?? 0, Is.Zero);
        Assert.That(admitted.CompleteBaseline, Is.False);
    }

    [Test]
    public void ReviewedWarningsBindExactCurrentDiagnosticBytesAndRuleSource()
    {
        ReplaceArtifact(CoverageArtifactKind.ExportStderr, "sanitized warning reviewed by an independent classifier");
        var review = _expected.Supervisor.Diagnostics with
        {
            Disposition = CoverageDiagnosticDisposition.ReviewedWarnings, RuleId = "independent-warning-rule-v1",
            Evidence = _input.Artifacts.Where(item => DiagnosticKinds.Contains(item.Kind)).Select(item => new CoverageDiagnosticBinding(item.Kind, item.Sha256)).ToArray(),
        };
        _expected = _expected with { Supervisor = _expected.Supervisor with { Diagnostics = review } };
        Assert.That(Read().Supervisor.Diagnostics.RuleId, Is.EqualTo("independent-warning-rule-v1"));
    }

    [TestCase("raw-tamper", "artifact-hash-or-length-mismatch", TestName = "isolated-session/corrupt-current-snapshot")]
    [TestCase("stale", "stale-artifact", TestName = "isolated-session/stale-export")]
    [TestCase("unsealed-change", "sealed-artifact-set-mismatch", TestName = "isolated-session/manifest-cannot-reseal-changed-evidence")]
    [TestCase("missing-result", "file-or-directory-missing", TestName = "isolated-session/missing-raw-result")]
    [TestCase("tool-tamper", "tool-hash-mismatch", TestName = "isolated-session/changed-pinned-tool")]
    [TestCase("source", "session-identity-mismatch", TestName = "isolated-session/wrong-source")]
    [TestCase("case", "session-identity-mismatch", TestName = "isolated-session/wrong-case")]
    [TestCase("wrong-pdb", "binary-evidence-rejected", TestName = "isolated-session/wrong-actual-pdb")]
    [TestCase("wrong-build", "binary-build-mismatch", TestName = "isolated-session/wrong-built-dll-hash")]
    [TestCase("extra-result", "test-evidence-rejected", TestName = "isolated-session/extra-terminal-case")]
    [TestCase("not-run", "test-evidence-rejected", TestName = "isolated-session/not-run-terminal-case")]
    [TestCase("malformed-result", "test-evidence-rejected", TestName = "isolated-session/truncated-nunit-xml")]
    [TestCase("wrong-trx", "test-evidence-rejected", TestName = "isolated-session/trx-counter-disagreement")]
    [TestCase("malformed-aggregate", "aggregate-evidence-rejected", TestName = "isolated-session/corrupt-aggregate")]
    [TestCase("raw-process-incomplete", "supervisor-raw-record-mismatch", TestName = "isolated-session/raw-exit-zero-without-completion")]
    [TestCase("drain", "process-not-complete", TestName = "isolated-session/undrained-output")]
    [TestCase("owned-tree", "process-not-complete", TestName = "isolated-session/live-owned-tree")]
    [TestCase("missing-child", "required-owned-process-missing", TestName = "isolated-session/required-apphost-missing")]
    [TestCase("incomplete-child", "owned-process-not-complete", TestName = "isolated-session/required-apphost-incomplete")]
    [TestCase("child-without-proof", "owned-process-proof-required", TestName = "isolated-session/apphost-exit-zero-without-owned-protocol-proof")]
    [TestCase("invocation-owner", "invocation-owner-mismatch", TestName = "isolated-session/copied-invocation-from-other-session")]
    [TestCase("cover-tool", "invocation-target-mismatch", TestName = "isolated-session/cover-launches-foreign-tool")]
    [TestCase("export-tool", "invocation-target-mismatch", TestName = "isolated-session/export-launches-foreign-tool")]
    [TestCase("cover-command-position", "invocation-target-mismatch", TestName = "isolated-session/cover-command-in-wrong-position")]
    [TestCase("export-command-position", "invocation-target-mismatch", TestName = "isolated-session/export-command-in-wrong-position")]
    [TestCase("export-snapshot", "invocation-target-mismatch", TestName = "isolated-session/export-from-other-snapshot")]
    [TestCase("unreviewed", "diagnostics-not-admitted", TestName = "isolated-session/unreviewed-warnings")]
    [TestCase("error", "diagnostics-not-admitted", TestName = "isolated-session/review-does-not-admit-errors")]
    [TestCase("changed-diagnostic", "diagnostic-evidence-mismatch", TestName = "isolated-session/review-of-other-diagnostic-bytes")]
    [TestCase("missing-rule-source", "diagnostic-rule-required", TestName = "isolated-session/review-without-rule-source")]
    [TestCase("empty-unexplained", "empty-product-reason-required", TestName = "isolated-session/unexplained-empty-product")]
    [TestCase("empty-no-dependency", "empty-product-reason-required", TestName = "isolated-session/empty-product-without-dependencies")]
    [TestCase("dependency-mismatch", "repository-dependency-mismatch", TestName = "isolated-session/stale-repository-dependency")]
    public void IncompleteStaleOrInconsistentSessionCannotBeAdmitted(string fault, string reason)
    {
        switch (fault)
        {
            case "raw-tamper": File.AppendAllText(ArtifactPath(CoverageArtifactKind.Snapshot), "changed"); break;
            case "stale":
                var stale = Artifact(CoverageArtifactKind.AggregateJson) with { LastWriteUtc = _started.AddMinutes(-1) };
                File.SetLastWriteTimeUtc(ArtifactPath(stale.Kind), stale.LastWriteUtc.UtcDateTime);
                UpdateDescriptor(stale);
                break;
            case "unsealed-change":
                var changed = WriteArtifact(CoverageArtifactKind.Snapshot, "changed bytes with only a changed manifest hash");
                _input = _input with { Artifacts = _input.Artifacts.Select(item => item.Kind == changed.Kind ? changed : item).ToArray() };
                break;
            case "missing-result": File.Delete(ArtifactPath(CoverageArtifactKind.NUnitXml)); break;
            case "tool-tamper": File.AppendAllText(_expected.ToolFile, "changed pinned tool"); break;
            case "source": _input = _input with { Identity = _input.Identity with { Source = _input.Identity.Source with { Tree = new string('f', 40) } } }; break;
            case "case": _input = _input with { Identity = _input.Identity with { CanonicalId = "session/other" } }; break;
            case "wrong-pdb": File.Copy(Path.Combine(_root, "build/tests.pdb"), Path.Combine(_root, "build/product.pdb"), overwrite: true); break;
            case "wrong-build":
                var bindings = _expected.Binaries.ToArray();
                bindings[0] = bindings[0] with { ExpectedIdentity = bindings[0].ExpectedIdentity with { DllSha256 = new string('0', 64) } };
                _expected = _expected with { Binaries = bindings };
                break;
            case "extra-result": ReplaceArtifact(CoverageArtifactKind.NUnitXml, Terminal.Replace("</test-run>", "<test-case id='8' fullname='Example.Cases.Other' result='Passed' duration='0.1' /></test-run>", StringComparison.Ordinal)); break;
            case "not-run": ReplaceArtifact(CoverageArtifactKind.NUnitXml, Terminal.Replace("result=\"Passed\" duration", "result=\"Skipped\" duration", StringComparison.Ordinal)); break;
            case "malformed-result": ReplaceArtifact(CoverageArtifactKind.NUnitXml, "<test-run result='Passed'>"); break;
            case "wrong-trx": ReplaceArtifact(CoverageArtifactKind.Trx, Trx.Replace("executed=\"1\"", "executed=\"0\"", StringComparison.Ordinal)); break;
            case "malformed-aggregate": ReplaceArtifact(CoverageArtifactKind.AggregateJson, "{\"Kind\":"); break;
            case "raw-process-incomplete": ReplaceArtifact(CoverageArtifactKind.CoverProcess, ProcessRecord(_expected.Supervisor.Cover, completed: false)); break;
            case "drain": ChangeCover(_expected.Supervisor.Cover with { StderrDrained = false }); break;
            case "owned-tree": ChangeCover(_expected.Supervisor.Cover with { OwnedTreeCompleted = false }); break;
            case "missing-child": _expected = _expected with { RequireOwnedAppHost = true }; break;
            case "incomplete-child":
                _expected = _expected with { RequireOwnedAppHost = true };
                ChangeCover(_expected.Supervisor.Cover with { OwnedProcesses = [new(1234, CoverageProcessRole.OwnedAppHost, false, 0, true, true)] });
                break;
            case "child-without-proof":
                _expected = _expected with { RequireOwnedAppHost = true };
                ChangeCover(_expected.Supervisor.Cover with { OwnedProcesses = [new(1234, CoverageProcessRole.OwnedAppHost, true, 0, true, true)] });
                break;
            case "invocation-owner":
                var owner = JsonNode.Parse(File.ReadAllText(ArtifactPath(CoverageArtifactKind.CoverInvocation)))!;
                owner["sessionRunId"] = Guid.NewGuid().ToString("N");
                ReplaceArtifact(CoverageArtifactKind.CoverInvocation, owner.ToJsonString());
                break;
            case "cover-tool":
            case "export-tool":
            case "cover-command-position":
            case "export-command-position":
                var kind = fault.StartsWith("cover-", StringComparison.Ordinal)
                    ? CoverageArtifactKind.CoverInvocation : CoverageArtifactKind.ExportInvocation;
                var changedInvocation = JsonNode.Parse(File.ReadAllText(ArtifactPath(kind)))!;
                var arguments = changedInvocation["arguments"]!.AsArray();
                if (fault.EndsWith("-tool", StringComparison.Ordinal))
                {
                    var foreignTool = Path.Combine(_root, "build", "foreign-tool.fixture");
                    File.WriteAllText(foreignTool, "another actual tool fixture", new UTF8Encoding(false));
                    arguments[0] = foreignTool;
                }
                else arguments.Insert(1, "unrecognized-prelude");
                // Reseal the observed invocation while keeping the independently
                // pinned tool untouched. Old token-presence checks accepted it.
                ReplaceArtifact(kind, changedInvocation.ToJsonString());
                break;
            case "export-snapshot":
                var invocation = JsonNode.Parse(File.ReadAllText(ArtifactPath(CoverageArtifactKind.ExportInvocation)))!;
                invocation["arguments"]![3] = Path.Combine(_root, "other-snapshot.dcvr");
                ReplaceArtifact(CoverageArtifactKind.ExportInvocation, invocation.ToJsonString());
                break;
            case "unreviewed": ChangeReview(_expected.Supervisor.Diagnostics with { Disposition = CoverageDiagnosticDisposition.Unreviewed }); break;
            case "error": ChangeReview(_expected.Supervisor.Diagnostics with { Disposition = CoverageDiagnosticDisposition.Error }); break;
            case "changed-diagnostic": ReplaceArtifact(CoverageArtifactKind.ExportStderr, "different diagnostic bytes"); break;
            case "missing-rule-source": ChangeReview(_expected.Supervisor.Diagnostics with { RuleSourceSha256 = "" }); break;
            case "empty-unexplained": SetAggregateEmpty(absent: false); break;
            case "empty-no-dependency":
                SetAggregateEmpty(absent: false);
                _input = _input with { EmptyProduct = new(EmptyProductCoverageReason.RepositoryResourcesOnly, []) };
                break;
            case "dependency-mismatch":
                SetAggregateEmpty(absent: false);
                _input = _input with { EmptyProduct = ResourceExplanation() };
                File.AppendAllText(Path.Combine(_root, "source/README.md"), "modified after the recorded source snapshot");
                break;
            default: throw new InvalidOperationException("unknown-session-fixture-fault");
        }
        var error = Assert.Throws<IsolatedCoverageSessionException>(() => Read())!;
        Assert.That(error.Code, Is.EqualTo("coverage-session-" + reason));
        Assert.That(error.Message, Is.EqualTo(error.Code));
        Assert.That(error.Message, Does.Not.Contain(_root));
        Assert.That(error.InnerException, Is.Null);
    }

    private CaseCoverage Read() => IsolatedCoverageSession.ReadValidated(_input, _expected);
    private void ChangeCover(CoverageProcessCompletion cover) => _expected = _expected with { Supervisor = _expected.Supervisor with { Cover = cover } };
    private void ChangeReview(CoverageDiagnosticReview review) => _expected = _expected with { Supervisor = _expected.Supervisor with { Diagnostics = review } };
    private CoverageArtifact Artifact(CoverageArtifactKind kind) => _input.Artifacts.Single(item => item.Kind == kind);
    private string ArtifactPath(CoverageArtifactKind kind) => Path.Combine(_root, "raw", kind + ".fixture");
    private void UpdateDescriptor(CoverageArtifact artifact)
    {
        _input = _input with { Artifacts = _input.Artifacts.Select(item => item.Kind == artifact.Kind ? artifact : item).ToArray() };
        _expected = _expected with { Supervisor = _expected.Supervisor with { Artifacts = _input.Artifacts.ToArray() } };
    }
    private void ReplaceArtifact(CoverageArtifactKind kind, string contents) => UpdateDescriptor(WriteArtifact(kind, contents));

    private CoverageArtifact WriteArtifact(CoverageArtifactKind kind, string contents)
    {
        var path = ArtifactPath(kind);
        File.WriteAllText(path, contents, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(path, _started.AddSeconds(kind == CoverageArtifactKind.Snapshot ? 9 : 13).UtcDateTime);
        return new(kind, Path.GetFileName(path), Hash(path), new FileInfo(path).Length, new(File.GetLastWriteTimeUtc(path)));
    }

    private CoverageBinaryBinding CopyPair(string sourceDll, string name, CoverageBinaryRole role)
    {
        var dll = Path.Combine(_root, "build", name + ".dll");
        var pdb = Path.Combine(_root, "build", name + ".pdb");
        File.Copy(sourceDll, dll);
        File.Copy(Path.ChangeExtension(sourceDll, ".pdb"), pdb);
        return new(role, name + ".dll", name + ".pdb", BinaryIdentity.ReadValidated(dll, pdb));
    }

    private void SetAggregateEmpty(bool absent) => ReplaceArtifact(CoverageArtifactKind.AggregateJson, absent
        ? "{\"Kind\":\"Root\",\"CoveredStatements\":0,\"TotalStatements\":0,\"CoveragePercent\":0,\"Children\":[]}"
        : "{\"Kind\":\"Root\",\"CoveredStatements\":0,\"TotalStatements\":9,\"CoveragePercent\":0,\"Children\":[{\"Kind\":\"Assembly\",\"Name\":\"FishAudio.Testing\",\"CoveredStatements\":0,\"TotalStatements\":9,\"CoveragePercent\":0}]}");

    private EmptyProductCoverage ResourceExplanation() => new(EmptyProductCoverageReason.RepositoryResourcesOnly,
        [new("README.md", Hash(Path.Combine(_root, "source/README.md")))]);

    private string Invocation(CoverageSessionIdentity identity, bool cover) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        executable = Path.Combine(_root, "sdk", "dotnet.exe"),
        workingDirectory = Path.Combine(_root, "build"),
        sessionRunId = identity.SessionId, canonicalCaseId = identity.CanonicalId,
        // The current collector launches the explicitly pinned dotCover DLL,
        // then direct vstest.console. Each path uses native segments, including
        // the independently validated test DLL's exact canonical spelling.
        arguments = cover
            ? new[]
            {
                Path.Combine(_root, "build", "pinned-tool.fixture"), "cover", "--target-executable", Path.Combine(_root, "sdk", "dotnet.exe"),
                "--snapshot-output", ArtifactPath(CoverageArtifactKind.Snapshot), "--log-file", ArtifactPath(CoverageArtifactKind.CoverLog),
                "--", "exec", Path.Combine(_root, "sdk", "10.0.403", "vstest.console.dll"), Path.Combine(_root, "build", "tests.dll"),
                "--Settings:" + Path.Combine(_root, "execution.runsettings"), "--logger:trx;LogFileName=results.trx",
                "--ResultsDirectory:" + Path.Combine(_root, "raw", "trx"),
            }
            : new[]
            {
                Path.Combine(_root, "build", "pinned-tool.fixture"), "report", "--snapshot-source", ArtifactPath(CoverageArtifactKind.Snapshot),
                "--json-report-output", ArtifactPath(CoverageArtifactKind.AggregateJson), "--log-file", ArtifactPath(CoverageArtifactKind.ExportLog),
            },
    });

    private static string ProcessRecord(CoverageProcessCompletion phase, bool completed = true) => JsonSerializer.Serialize(new
    {
        startedUtc = phase.StartedUtc, finishedUtc = phase.FinishedUtc, completed, exitCode = 0, outputDrained = true,
        elapsedMilliseconds = (phase.FinishedUtc - phase.StartedUtc).TotalMilliseconds,
    });

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }

    private const string Discovery = """
        <NUnitXml><test-run id="0" name="example.dll" runstate="Runnable" testcasecount="2">
          <test-suite type="Assembly" id="0-10" name="example.dll" runstate="Runnable" testcasecount="2">
            <test-case id="0-1" name="Probe" fullname="Example.Cases.Probe" methodname="Probe" classname="Example.Cases" runstate="Runnable"><properties><property name="FishAudioCaseId" value="session/example" /></properties></test-case>
            <test-case id="0-2" name="Other" fullname="Example.Cases.Other" methodname="Other" classname="Example.Cases" runstate="Runnable"><properties><property name="FishAudioCaseId" value="session/other" /></properties></test-case>
          </test-suite>
        </test-run></NUnitXml>
        """;

    private const string Terminal = """
        <test-run id="7" result="Passed" total="1" passed="1" failed="0" warnings="0" inconclusive="0" skipped="0">
          <test-case id="7-1" name="Probe" fullname="Example.Cases.Probe" methodname="Probe" classname="Example.Cases" result="Passed" duration="0.125"><properties><property name="FishAudioCaseId" value="session/example" /></properties></test-case>
        </test-run>
        """;

    private const string Trx = """
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results><UnitTestResult executionId="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" testId="11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa" testName="Probe" duration="00:00:00.1250000" outcome="Passed" /></Results>
          <TestDefinitions><UnitTest name="Probe" id="11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa"><Execution id="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" /><TestMethod adapterTypeName="executor://nunit3testexecutor/" className="Example.Cases" name="Probe" /></UnitTest></TestDefinitions>
          <ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" /></ResultSummary>
        </TestRun>
        """;

    private const string Aggregate = """
        {"Kind":"Root","CoveredStatements":3,"TotalStatements":9,"CoveragePercent":34,"Children":[
          {"Kind":"Assembly","Name":"FishAudio.Testing","CoveredStatements":3,"TotalStatements":9,"CoveragePercent":34,"Children":[
            {"Kind":"Method","Name":"Await():Task","CoveredStatements":3,"TotalStatements":9,"CoveragePercent":34,"Children":[
              {"Kind":"InternalCompiledMethod","Name":"MoveNext():void","CoveredStatements":3,"TotalStatements":9,"CoveragePercent":34}]}]}]}
        """;
}
