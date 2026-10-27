using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace NUnitCompatibility;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class ChildCoverageEvidenceTests
{
    // Synthetic protocol-boundary fixtures only. Declared PIDs and EOF flags
    // test admission of metadata; no child, native action or UI is executed.
    private const string Commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Tree = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const int ParentPid = 10101;
    private const int ChildPid = 20202;
    private readonly Stopwatch _clock = new();
    private string _ownerRoot = "";
    private string _parent = "";
    private string _name = "";
    private string _raw = "";
    private string _identityPath = "";
    private string _reportPath = "";
    private string _manifestPath = "";
    private RegressionChildRequest _request = null!;
    private RegressionReport _report = null!;

    [SetUp]
    public void CreateSyntheticProtocolEvidence()
    {
        _clock.Start();
        var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
        _parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(string.IsNullOrEmpty(configured) ? Path.GetTempPath() : configured));
        if ((!string.IsNullOrEmpty(configured) && !Path.IsPathFullyQualified(configured)) || !Directory.Exists(_parent))
            throw new InvalidOperationException("child-evidence-fixture-parent-invalid");
        RequireExistingRegularAncestors(_parent);
        _name = "fa-child-metadata-" + Guid.NewGuid().ToString("N");
        _ownerRoot = Path.Combine(_parent, _name);
        if (Directory.Exists(_ownerRoot) || File.Exists(_ownerRoot))
            throw new InvalidOperationException("child-evidence-fixture-owner-exists");
        Directory.CreateDirectory(_ownerRoot);
        _raw = Path.Combine(_ownerRoot, "raw");
        Directory.CreateDirectory(_raw);
        _identityPath = Path.Combine(_raw, "identity.json");
        _reportPath = Path.Combine(_raw, "regression.json");
        var source = Source(Tree);
        _report = new RegressionReport
        {
            Source = source,
            Invocation = ["nunit-compatibility-child", ChildCompatibilityCases.LegacyCaseId],
            SelectionReasons = ["nunit-child-compatibility"],
            Cases = [new() { Id = ChildCompatibilityCases.LegacyCaseId, Partition = "core" }],
        };
        _report.Create(_reportPath);
        _manifestPath = Path.Combine(_raw, _report.RunId + "." + Guid.NewGuid().ToString("N") + ".child.json");
        _request = new RegressionChildRequest
        {
            RunId = _report.RunId,
            Partition = "core",
            CaseIds = [ChildCompatibilityCases.LegacyCaseId],
            Source = source,
            Configuration = _report.Configuration,
            Sdk = _report.Sdk,
            ManifestPath = _manifestPath,
        };
        RegressionProcessProtocol.ValidateRequest(_request);
        RegressionProcessProtocol.WriteNew(_manifestPath + ".request.json", _request);
        var intermediate = RegressionChildManifest.Create(_request);
        intermediate.Cases[0].Outcome = "passed";
        intermediate.Cases[0].ElapsedMilliseconds = 1;
        RegressionProcessProtocol.ValidateManifest(intermediate, _request);
        RegressionProcessProtocol.WriteNew(Snapshot(1), intermediate);
        var final = RegressionChildManifest.Create(_request);
        final.Final = final.Complete = true;
        final.Cases[0].Outcome = "passed";
        final.Cases[0].ElapsedMilliseconds = 1;
        RegressionProcessProtocol.ValidateManifest(final, _request);
        RegressionProcessProtocol.WriteNew(Snapshot(2), final);
        RegressionProcessProtocol.WriteNew(_manifestPath, final);
        _report.Cases[0].Outcome = "passed";
        _report.Cases[0].ElapsedMilliseconds = 1;
        _report.Cases[0].ChildRequestId = _request.RequestId;
        _report.Complete = true;
        _report.FinishedUtc = DateTimeOffset.UtcNow;
        _report.Write(_reportPath);
        RegressionProcessProtocol.WriteNew(_identityPath, new
        {
            SchemaVersion = 1,
            ProcessId = ParentPid,
            ChildProcessId = ChildPid,
            ChildCompatibilityCases.NUnitCaseId,
            NUnitFullName = typeof(ChildCompatibilityCases).FullName + "." + nameof(ChildCompatibilityCases.StreamingTextRunsInTheSupervisedChild),
            ChildCompatibilityCases.LegacyCaseId,
            _report.RunId,
            ChildRequestId = _request.RequestId,
            ChildExitCode = 0,
            ChildStdoutDrained = true,
            ChildStderrDrained = true,
            Source = source,
        });
    }

    [TearDown]
    public void ReleaseOwnedFixture()
    {
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            if (string.IsNullOrEmpty(_ownerRoot)) return;
            RequireOwnedRoot();
            if (failed)
            {
                TestContext.Error.WriteLine("child-evidence-fixture: failed synthetic evidence retained as " + _name + ".");
                return;
            }
            Assert.That(_clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(120)), "child-evidence-fixture-root-deadline");
            if (Directory.Exists(_ownerRoot)) Directory.Delete(_ownerRoot, recursive: true);
        }
        catch (Exception error) when (failed)
        {
            TestContext.Error.WriteLine("child-evidence-fixture: cleanup failed (" + error.GetType().Name + "); original failure retained.");
        }
    }

    [Test]
    public void OwnedPassedGenerationAndExactSnapshotsProduceOnlyAMetadataReceipt()
    {
        if (_report.Configuration != "Release" || _report.Sdk != "10.0.403")
        {
            Assert.That(() => Read(), Throws.TypeOf<ChildCoverageException>()
                .With.Property("Code").EqualTo("incomplete-authoritative-report"));
            return;
        }
        // A valid older projection must not veto the immutable passed generation.
        File.Copy(RegressionReport.GenerationPath(_reportPath, _report.RunId, 0), _reportPath, overwrite: true);
        using (var projection = JsonDocument.Parse(File.ReadAllBytes(_reportPath)))
            Assert.That(projection.RootElement.GetProperty("complete").GetBoolean(), Is.False);
        var before = RawDigests();
        var receipt = Read();
        Assert.That(receipt.Complete, Is.True);
        Assert.That(receipt.CanonicalId, Is.EqualTo(ChildCompatibilityCases.NUnitCaseId));
        Assert.That(receipt.RunId, Is.EqualTo(_report.RunId));
        Assert.That(receipt.RequestId, Is.EqualTo(_request.RequestId));
        Assert.That(receipt.ParentProcessId, Is.EqualTo(ParentPid));
        Assert.That(receipt.ChildProcessId, Is.EqualTo(ChildPid));
        Assert.That(receipt.ChildExitCode, Is.Zero);
        Assert.That(receipt.ChildStdoutDrained && receipt.ChildStderrDrained, Is.True);
        Assert.That(receipt.JobOwnershipClaim || receipt.BaselineClaim, Is.False);
        Assert.That(receipt.SourceFingerprintKind, Is.EqualTo("sha256-snapshot-v1-commit-tree"));
        Assert.That(receipt.AuthoritativeGenerationRelativePath,
            Is.EqualTo(Path.GetRelativePath(_raw, RegressionReport.GenerationPath(_reportPath, _report.RunId, 1)).Replace('\\', '/')));
        Assert.That(receipt.Artifacts, Has.Count.EqualTo(8));
        foreach (var artifact in receipt.Artifacts)
            Assert.That(artifact.Sha256, Is.EqualTo(before[artifact.RelativePath]));
        var output = Path.Combine(_ownerRoot, "receipt.json");
        Assert.That(ChildCoverageVerifier.Run(Arguments(output)), Is.Zero);
        using var published = JsonDocument.Parse(File.ReadAllBytes(output));
        Assert.That(published.RootElement.GetProperty("complete").GetBoolean(), Is.True);
        Assert.That(published.RootElement.GetProperty("jobOwnershipClaim").GetBoolean(), Is.False);
        Assert.That(published.RootElement.GetProperty("baselineClaim").GetBoolean(), Is.False);
        AssertRawUnchanged(before);
    }

    [TestCase("extra-valid-3", "incomplete-or-extra-child-snapshot-set", TestName = "child-evidence/extra-valid-snapshot-3")]
    [TestCase("extra-failed-3", "incomplete-or-extra-child-snapshot-set", TestName = "child-evidence/extra-failed-snapshot-3")]
    [TestCase("orphan", "orphan-or-malformed-child-snapshot", TestName = "child-evidence/orphan-snapshot")]
    [TestCase("malformed-index", "orphan-or-malformed-child-snapshot", TestName = "child-evidence/noncanonical-snapshot-index")]
    [TestCase("out-of-range", "invalid-child-snapshot-index", TestName = "child-evidence/out-of-range-snapshot-index")]
    [TestCase("missing-1", "incomplete-or-extra-child-snapshot-set", TestName = "child-evidence/missing-intermediate-snapshot")]
    [TestCase("missing-2", "incomplete-or-extra-child-snapshot-set", TestName = "child-evidence/missing-final-snapshot")]
    [TestCase("intermediate-final", "invalid-child-snapshot-lifecycle", TestName = "child-evidence/intermediate-cannot-be-final")]
    [TestCase("failed-intermediate", "invalid-child-snapshot-progress", TestName = "child-evidence/failed-intermediate-cannot-become-passed-final")]
    [TestCase("foreign-source", "raw-child-evidence-rejected", TestName = "child-evidence/intermediate-foreign-source")]
    [TestCase("missing-eof", "raw-child-evidence-rejected", TestName = "child-evidence/old-identity-without-eof")]
    [TestCase("false-eof", "incomplete-child-identity", TestName = "child-evidence/stderr-eof-not-observed")]
    [TestCase("invalid-pid", "incomplete-child-identity", TestName = "child-evidence/child-cannot-alias-parent-pid")]
    [TestCase("request-escape", "declared-path-escapes-or-missing", TestName = "child-evidence/request-cannot-read-outside-raw-root")]
    public void InconsistentOwnedEvidenceCannotAcquireAReceipt(string fault, string expectedCode)
    {
        string? external = null;
        switch (fault)
        {
            case "extra-valid-3":
                RegressionProcessProtocol.WriteNew(Snapshot(3), ReadManifest(2));
                break;
            case "extra-failed-3":
                var failed = ReadManifest(2);
                failed.Complete = false;
                failed.Cases[0].Outcome = "failed";
                failed.Cases[0].ExceptionType = typeof(InvalidOperationException).FullName;
                RegressionProcessProtocol.ValidateManifest(failed, _request);
                RegressionProcessProtocol.WriteNew(Snapshot(3), failed);
                break;
            case "orphan":
                File.Move(Snapshot(1), Path.Combine(_raw, Guid.NewGuid().ToString("N") + ".child.json.snapshot-000001.json"));
                break;
            case "malformed-index":
                File.Move(Snapshot(1), _manifestPath + ".snapshot-00001.json");
                break;
            case "out-of-range":
                File.Move(Snapshot(1), Snapshot(3));
                break;
            case "missing-1": File.Delete(Snapshot(1)); break;
            case "missing-2": File.Delete(Snapshot(2)); break;
            case "intermediate-final":
                RegressionProcessProtocol.Write(Snapshot(1), ReadManifest(2));
                break;
            case "failed-intermediate":
                var intermediateFailure = ReadManifest(1);
                intermediateFailure.Cases[0].Outcome = "failed";
                intermediateFailure.Cases[0].ExceptionType = typeof(InvalidOperationException).FullName;
                RegressionProcessProtocol.ValidateManifest(intermediateFailure, _request);
                RegressionProcessProtocol.Write(Snapshot(1), intermediateFailure);
                break;
            case "foreign-source":
                var foreign = ReadManifest(1);
                foreign.Source = Source("cccccccccccccccccccccccccccccccccccccccc");
                RegressionProcessProtocol.Write(Snapshot(1), foreign);
                break;
            case "missing-eof":
                ChangeIdentity(value => { value.Remove("childStdoutDrained"); value.Remove("childStderrDrained"); });
                break;
            case "false-eof": ChangeIdentity(value => value["childStderrDrained"] = false); break;
            case "invalid-pid": ChangeIdentity(value => value["childProcessId"] = ParentPid); break;
            case "request-escape":
                external = Path.Combine(_ownerRoot, "outside.child.json");
                RegressionProcessProtocol.WriteNew(external, ReadManifest(2));
                var escaped = RegressionProcessProtocol.Read<RegressionChildRequest>(_manifestPath + ".request.json");
                escaped.ManifestPath = external;
                RegressionProcessProtocol.ValidateRequest(escaped);
                RegressionProcessProtocol.Write(_manifestPath + ".request.json", escaped);
                break;
            default: throw new InvalidOperationException("unknown-child-evidence-fixture-fault");
        }
        var before = RawDigests();
        var outsideHash = external is null ? null : Digest(external);
        Assert.That(() => Read(), Throws.TypeOf<ChildCoverageException>().With.Property("Code").EqualTo(expectedCode));
        AssertRawUnchanged(before);
        if (external is not null) Assert.That(Digest(external), Is.EqualTo(outsideHash));
    }

    [Test]
    [NonParallelizable] // This expected CLI rejection temporarily owns Console.Error.
    public void UnknownCanonicalCliCannotPublishAReceipt()
    {
        var before = RawDigests();
        var output = Path.Combine(_ownerRoot, "unexpected-receipt.json");
        var arguments = Arguments(output);
        arguments[4] = "compat.child/not-the-owned-case";
        var previousError = Console.Error;
        using var capturedError = new StringWriter();
        int exitCode;
        try
        {
            Console.SetError(capturedError);
            exitCode = ChildCoverageVerifier.Run(arguments);
        }
        finally { Console.SetError(previousError); }
        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(capturedError.ToString(), Is.EqualTo("child-coverage: unexpected-case-or-tree" + Environment.NewLine));
        Assert.That(File.Exists(output), Is.False);
        AssertRawUnchanged(before);
    }

    private ChildCoverageReceipt Read() => ChildCoverageVerifier.ReadValidated(_raw, ChildCompatibilityCases.NUnitCaseId, Tree);
    private string Snapshot(int number) => RegressionProcessProtocol.SnapshotPath(_request, number);
    private RegressionChildManifest ReadManifest(int number) => RegressionProcessProtocol.Read<RegressionChildManifest>(Snapshot(number));
    private string[] Arguments(string output) => ["--verify-child-coverage", "--fixture-directory", _raw,
        "--canonical-id", ChildCompatibilityCases.NUnitCaseId, "--tree", Tree, "--output", output];

    private static RegressionSourceIdentity Source(string tree) => new()
    {
        Commit = Commit, Tree = tree, Available = true, Snapshot = true,
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("snapshot-v1\n" + Commit + "\n" + tree + "\n"))),
    };

    private void ChangeIdentity(Action<JsonObject> change)
    {
        var value = JsonNode.Parse(File.ReadAllBytes(_identityPath))!.AsObject();
        change(value);
        // This is the existing controlled malformed-evidence writer, not a
        // rewrite of any actual child run or immutable production artifact.
        RegressionProcessProtocol.Write(_identityPath, value);
    }

    private Dictionary<string, string> RawDigests() => Directory.EnumerateFiles(_raw, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(_raw, path).Replace('\\', '/'), Digest, StringComparer.Ordinal);

    private void AssertRawUnchanged(Dictionary<string, string> before)
    {
        var after = RawDigests();
        Assert.That(after.Keys, Is.EquivalentTo(before.Keys));
        foreach (var item in before) Assert.That(after[item.Key], Is.EqualTo(item.Value), "Verifier preserves raw evidence bytes.");
    }

    private static string Digest(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private void RequireOwnedRoot()
    {
        if (Path.GetDirectoryName(Path.GetFullPath(_ownerRoot)) != _parent || Path.GetFileName(_ownerRoot) != _name ||
            !_name.StartsWith("fa-child-metadata-", StringComparison.Ordinal) || !Guid.TryParseExact(_name[18..], "N", out _))
            throw new InvalidOperationException("child-evidence-fixture-owner-changed");
        RequireExistingRegularAncestors(_ownerRoot);
    }

    private static void RequireExistingRegularAncestors(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            if (!current.Exists || (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("child-evidence-fixture-missing-or-reparse");
    }
}
