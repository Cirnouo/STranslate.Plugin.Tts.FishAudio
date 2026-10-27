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
public sealed class NUnitHostEvidenceTests
{
    private const int HostPid = 321;
    private const string Run = "11111111111141118111111111111111";
    private const string Session = "22222222222242228222222222222222";
    private const string Case = "host/example-row";
    private string _parent = "";
    private string _root = "";
    private string _start = "";
    private string _end = "";
    private NUnitHostExpectation _expected = null!;

    [SetUp]
    public void CreateOwnedLifecyclePair()
    {
        var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
        var parent = string.IsNullOrEmpty(configured) ? Path.GetTempPath() : configured;
        Assert.That(Path.IsPathFullyQualified(parent) && Directory.Exists(parent), Is.True);
        _parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        _root = Path.Combine(_parent, "fa-nunit-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _start = Path.Combine(_root, "host-321.start.json");
        _end = Path.Combine(_root, "host-321.end.json");
        var assembly = Path.Combine(_root, "actual-test-assembly.dll");
        File.Copy(typeof(NUnitHostEvidenceTests).Assembly.Location, assembly);
        var coverStarted = DateTimeOffset.UtcNow.AddMinutes(-1);
        _expected = new(1, Run, Session, Case, new string('a', 40), assembly, Hash(assembly), coverStarted, coverStarted.AddSeconds(20));
        Write(_start, Record("start").ToJsonString());
        Write(_end, Record("end").ToJsonString());
    }

    [TearDown]
    public void ReleaseOwnedLifecyclePair()
    {
        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
        {
            TestContext.Error.WriteLine("host-evidence: failed fixture retained as " + Path.GetFileName(_root) + ".");
            return;
        }
        var full = Path.GetFullPath(_root);
        var name = Path.GetFileName(full);
        Assert.That(string.Equals(Path.GetDirectoryName(full), _parent, StringComparison.OrdinalIgnoreCase) &&
            name.StartsWith("fa-nunit-host-", StringComparison.Ordinal) && Guid.TryParseExact(name[14..], "N", out _) &&
            (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0, Is.True);
        if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
    }

    [Test]
    public void ExactCallbackPairProvidesIndependentPidAndPreservesRawBytes()
    {
        var beforeStart = File.ReadAllBytes(_start);
        var beforeEnd = File.ReadAllBytes(_end);
        var evidence = Read();
        Assert.That(evidence.ProcessId, Is.EqualTo(321));
        Assert.That(evidence.RunId, Is.EqualTo(Run));
        Assert.That(evidence.SessionId, Is.EqualTo(Session));
        Assert.That(evidence.CanonicalId, Is.EqualTo(Case));
        Assert.That(evidence.AssemblyPath, Is.EqualTo(_expected.TestAssembly));
        Assert.That(evidence.AssemblySha256, Is.EqualTo(Hash(_expected.TestAssembly)));
        Assert.That(evidence.StartedUtc, Is.EqualTo(_expected.CoverStartedUtc.AddSeconds(2)));
        Assert.That(evidence.FinishedUtc, Is.EqualTo(_expected.CoverStartedUtc.AddSeconds(8)));
        Assert.That(evidence.StartCallbackObserved && evidence.EndCallbackObserved, Is.True);
        Assert.That(evidence.OsExitClaim || evidence.PipeDrainClaim || evidence.JobOwnershipClaim || evidence.TestPassClaim || evidence.BaselineClaim, Is.False);
        Assert.That(evidence.Evidence.Select(item => item.Sha256), Is.EqualTo(new[]
        {
            Convert.ToHexStringLower(SHA256.HashData(beforeStart)), Convert.ToHexStringLower(SHA256.HashData(beforeEnd)),
        }));
        Assert.That(File.ReadAllBytes(_start), Is.EqualTo(beforeStart));
        Assert.That(File.ReadAllBytes(_end), Is.EqualTo(beforeEnd));
    }

    [TestCase("case", "session-identity-mismatch", TestName = "host-evidence/wrong-canonical-case")]
    [TestCase("run", "session-identity-mismatch", TestName = "host-evidence/wrong-run")]
    [TestCase("session", "session-identity-mismatch", TestName = "host-evidence/wrong-session")]
    [TestCase("tree", "session-identity-mismatch", TestName = "host-evidence/wrong-source-tree")]
    [TestCase("pid", "pid-or-file-owner-mismatch", TestName = "host-evidence/pid-and-file-ownership")]
    [TestCase("hash", "assembly-hash-mismatch", TestName = "host-evidence/actual-dll-changed")]
    [TestCase("fields", "unknown-or-duplicate-field", TestName = "host-evidence/unknown-or-duplicate-json-field")]
    [TestCase("truncated", "invalid-record-or-file", TestName = "host-evidence/truncated-json")]
    [TestCase("missing-end", "missing-or-nonregular-file", TestName = "host-evidence/missing-end-callback")]
    [TestCase("phase", "phase-mismatch", TestName = "host-evidence/wrong-lifecycle-phase")]
    [TestCase("time", "lifecycle-time-mismatch", TestName = "host-evidence/end-outside-cover-envelope")]
    public void MismatchedOrIncompleteLifecycleEvidenceIsRejected(string fault, string reason)
    {
        var end = Record("end");
        switch (fault)
        {
            case "case": end["canonicalId"] = "host/other-row"; break;
            case "run": end["runId"] = "33333333333343338333333333333333"; break;
            case "session": end["sessionId"] = "33333333333343338333333333333333"; break;
            case "tree": end["sourceTree"] = new string('b', 40); break;
            case "pid":
                end["pid"] = 456;
                Write(_end, end.ToJsonString());
                Reject(reason);
                Write(_end, Record("end").ToJsonString());
                var renamed = Path.Combine(_root, "host-456.end.json");
                File.Move(_end, renamed);
                _end = renamed;
                Reject(reason);
                return;
            case "hash":
                File.AppendAllText(_expected.TestAssembly, "tampered actual DLL bytes");
                Reject(reason);
                return;
            case "fields":
                var valid = end.ToJsonString();
                Write(_end, valid[..^1] + ",\"pid\":321}");
                Reject(reason);
                end["unexpected"] = "new field is not silently ignored";
                Write(_end, end.ToJsonString());
                Reject(reason);
                return;
            case "truncated": Write(_end, "{\"schemaVersion\":1,"); Reject(reason); return;
            case "missing-end": File.Delete(_end); Reject(reason); return;
            case "phase": end["phase"] = "start"; break;
            case "time": end["finishedUtc"] = _expected.CoverFinishedUtc.AddSeconds(1); break;
            default: throw new InvalidOperationException("unknown-host-fixture-fault");
        }
        Write(_end, end.ToJsonString());
        Reject(reason);
    }

    private NUnitHostLifecycleEvidence Read() => NUnitHostEvidence.ReadValidated(_start, _end, _expected);

    private void Reject(string reason)
    {
        var error = Assert.Throws<NUnitHostEvidenceException>(() => Read())!;
        Assert.That(error.Code, Is.EqualTo("host-evidence-" + reason));
        Assert.That(error.Message, Is.EqualTo(error.Code));
        Assert.That(error.Message, Does.Not.Contain(_root));
        Assert.That(error.InnerException, Is.Null);
    }

    private JsonObject Record(string phase) => new()
    {
        ["schemaVersion"] = 1, ["phase"] = phase, ["runId"] = Run, ["sessionId"] = Session,
        ["canonicalId"] = Case, ["sourceTree"] = new string('a', 40), ["pid"] = HostPid,
        ["assemblyPath"] = _expected.TestAssembly, ["assemblySha256"] = _expected.TestAssemblySha256,
        ["startedUtc"] = _expected.CoverStartedUtc.AddSeconds(2),
        ["finishedUtc"] = phase == "start" ? null : JsonValue.Create(_expected.CoverStartedUtc.AddSeconds(8)),
        ["baselineClaim"] = false,
    };

    private void Write(string path, string text)
    {
        File.WriteAllText(path, text, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(path, _expected.CoverStartedUtc.AddSeconds(9).UtcDateTime);
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }
}
