using System.Security.Cryptography;
using System.Text;
using FishAudio.Testing.Evidence;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class DotCoverProcessDiagnosticTests
{
    // Reduced sanitized native/managed shapes from the eight direct CLI runs.
    // PID 66832 is explicit; managed worker 21 and native prefix 0000CAE0 are
    // distinct thread identifiers. These literals are not product execution.
    private const string Clsid = "14C82A6C-9AB0-4845-8265-268482FE3868";
    private const string Separator = "====================================================================================================";
    private const string Banner = "20:02:43.905 |I| | :2 | JetBrains dotCover Console Runner 2026.2.3.1. Build 20260930.210750 (windows-x64)\n";
    private const string Completed = "20:02:53.777 |I| Main | :2 | Console Runner exit code: 0\n";
    private string _root = "";
    private string _parent = "";
    private string _log = "";
    private string _error = "";
    private string _text = "";

    [SetUp]
    public void PrepareOwnedLogs()
    {
        var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
        Assert.That(string.IsNullOrEmpty(configured) || Path.IsPathFullyQualified(configured), Is.True);
        _parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(string.IsNullOrEmpty(configured) ? Path.GetTempPath() : configured));
        Assert.That(Directory.Exists(_parent), Is.True);
        _root = Path.Combine(_parent, "fa-cover-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _log = Path.Combine(_root, "cover.log");
        _error = Path.Combine(_root, "cover.stderr.log");
        _text = Banner + Managed("Main", ":2", "Command line:") + "dotCover.dll cover --target-executable dotnet.exe\n"
            + Connection("d24edca6-e611-4327-92b0-f6c4028da09b", 66832)
            + Snapshot("d24edca6-e611-4327-92b0-f6c4028da09b", 12, 76)
            + CoreBlock(66832, @"C:\build\testhost.exe", ".NET TP Worker:21", shutdownResult: false)
            + CoreBlock(17416, @"C:\sdk\dotnet.exe", ".NET TP Worker:12", shutdownResult: true)
            + AuxiliaryBlock() + Completed;
        Write(_log, _text);
        Write(_error, "");
    }

    [TearDown]
    public void ReleaseOwnedLogs()
    {
        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
        {
            TestContext.Error.WriteLine("cover-process: failed fixture retained as " + Path.GetFileName(_root) + ".");
            return;
        }
        var name = Path.GetFileName(_root);
        Assert.That(Path.GetDirectoryName(_root), Is.EqualTo(_parent).IgnoreCase);
        Assert.That(name.StartsWith("fa-cover-process-", StringComparison.Ordinal) && Guid.TryParseExact(name[17..], "N", out _), Is.True);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void ExactPidsRetainKnownMarkersAndKeepAbsentShutdownResultUnknown()
    {
        var bytes = File.ReadAllBytes(_log);
        var review = Read(66832, 17416);
        Assert.That(review.Processes.Select(process => process.ProcessId), Is.EqualTo(new[] { 66832, 17416, 59232 }));
        var host = review.Processes[0];
        Assert.That(host.Executable, Is.EqualTo(@"C:\build\testhost.exe"));
        Assert.That(host.Initialized && host.OnEndShutdownObserved && host.CoverageProfilerCreated, Is.True);
        Assert.That(host.CoverageProfilerClsid, Is.EqualTo(new Guid(Clsid)));
        Assert.That(host.ShutdownSucceeded, Is.Null, "The retained host ring does not show BProf::Shutdown res=ok.");
        Assert.That(review.Processes[1].ShutdownSucceeded, Is.True);
        Assert.That(review.Processes[2].AuxiliaryPdbServer, Is.True);
        Assert.That(review.Processes[2].Initialized, Is.False);
        Assert.That(review.Processes[2].OnEndShutdownObserved, Is.False);
        Assert.That(review.SnapshotStatistics.Single(), Is.EqualTo(new DotCoverSnapshotStatistic(14,
            "d24edca6-e611-4327-92b0-f6c4028da09b", 66832, 12, 76)));
        Assert.That(review.Evidence[0].Sha256, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(bytes))));
        Assert.That(File.ReadAllBytes(_log), Is.EqualTo(bytes));
        Assert.That(review.JobOwnershipClaim || review.BaselineClaim, Is.False);
    }

    [Test]
    public void VerboseUnselectedProfilerCandidatesDoNotOverrideSelectedCoverageClsid()
    {
        var review = Read(66832);
        var candidate = review.Processes[0].Observations.Single(item => item.Kind == "ProfilerCandidateNotSelected");
        Assert.That(candidate.Payload, Is.EqualTo("Profiler res=failed pname='ReleaseMemory'"));
        Assert.That(review.Processes[0].CoverageProfilerCreated, Is.True);
    }

    [Test]
    public void Utf8BomIsPreservedInRawDigest()
    {
        Write(_log, "\ufeff" + _text);
        var expected = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(_log)));
        Assert.That(Read(66832).Evidence[0].Sha256, Is.EqualTo(expected));
    }

    [TestCase("warning", "warning-or-error", TestName = "cover-process/structured-warning")]
    [TestCase("error", "warning-or-error", TestName = "cover-process/structured-error")]
    [TestCase("fatal", "warning-or-error", TestName = "cover-process/structured-fatal")]
    [TestCase("core-alert", "core-alert", TestName = "cover-process/core-alert-at-verbose-level")]
    [TestCase("on-alert", "core-alert", TestName = "cover-process/on-alert-at-verbose-level")]
    [TestCase("native-warning", "native-warning-or-error", TestName = "cover-process/native-warning-at-verbose-level")]
    [TestCase("missing-init", "required-profiled-pid-incomplete", TestName = "cover-process/missing-required-initialization")]
    [TestCase("missing-end", "required-profiled-pid-incomplete", TestName = "cover-process/missing-required-shutdown-end")]
    [TestCase("wrong-create", "required-profiled-pid-incomplete", TestName = "cover-process/create-instance-clsid-mismatch")]
    [TestCase("shutdown-failed", "required-profiled-pid-incomplete", TestName = "cover-process/explicit-shutdown-failure")]
    [TestCase("pid-mismatch", "native-pid-mismatch", TestName = "cover-process/hex-decimal-pid-disagreement")]
    [TestCase("unknown-pid", "required-profiled-pid-missing", TestName = "cover-process/unknown-required-pid")]
    [TestCase("managed-thread", "required-profiled-pid-missing", TestName = "cover-process/managed-thread-is-not-pid")]
    [TestCase("native-thread", "required-profiled-pid-missing", TestName = "cover-process/native-prefix-is-not-pid")]
    [TestCase("auxiliary-required", "required-profiled-pid-missing", TestName = "cover-process/pdb-server-is-not-profiled-target")]
    [TestCase("truncated", "cover-not-completed", TestName = "cover-process/truncated-before-console-completion")]
    [TestCase("open-native-block", "cover-not-completed", TestName = "cover-process/truncated-native-block")]
    [TestCase("exit-nonzero", "cover-not-completed", TestName = "cover-process/nonzero-console-result")]
    [TestCase("after-completed", "content-after-completion", TestName = "cover-process/content-after-completion")]
    [TestCase("orphan", "unparsed-content", TestName = "cover-process/unparsed-orphan-content")]
    [TestCase("malformed-structured", "unparsed-diagnostic", TestName = "cover-process/malformed-structured-warning")]
    [TestCase("stderr", "unparsed-stderr", TestName = "cover-process/unknown-stderr")]
    [TestCase("banner", "tool-banner-mismatch", TestName = "cover-process/tool-version-mismatch")]
    public void UnknownOrIncompleteCoverEvidenceIsRejected(string fault, string expectedCode)
    {
        var required = 66832;
        switch (fault)
        {
            case "warning": _text = _text.Replace(Completed, "20:02:53.700 |W| Other | :2 | warning\n" + Completed, StringComparison.Ordinal); break;
            case "error": _text = _text.Replace(Completed, "20:02:53.700 |E| Other | :2 | error\n" + Completed, StringComparison.Ordinal); break;
            case "fatal": _text = _text.Replace(Completed, "20:02:53.700 |F| Other | :2 | fatal\n" + Completed, StringComparison.Ordinal); break;
            case "core-alert": _text = _text.Replace(Completed, Managed("ResultsHandler", "BridgeCoreThread:20", "CoreAlert.") + Completed, StringComparison.Ordinal); break;
            case "on-alert": _text = _text.Replace(Completed, Managed("MeasureCommandProcessor", "BridgeCoreThread:20", "OnAlert severity=Warning hr=E_FAIL") + Completed, StringComparison.Ordinal); break;
            case "native-warning": _text = _text.Replace("BProf::Initialize", "WARNING: Instrumentation was requested, but wasn't made", StringComparison.Ordinal); break;
            case "missing-init": _text = _text.Replace("BProf::Initialize", "BProf::InitFinish res=ok", StringComparison.Ordinal); break;
            case "missing-end": _text = _text.Replace("CProf::OnEndShutdown", "CProf::OnCleanup", StringComparison.Ordinal); break;
            case "wrong-create": _text = _text.Replace("CreateComInstance clsid={" + Clsid, "CreateComInstance clsid={11111111-1111-1111-1111-111111111111", StringComparison.Ordinal); break;
            case "shutdown-failed":
                _text = Banner + CoreBlock(66832, @"C:\build\testhost.exe", ".NET TP Worker:21", shutdownResult: true)
                    .Replace("BProf::Shutdown res=ok", "BProf::Shutdown res=failed", StringComparison.Ordinal) + Completed;
                break;
            case "pid-mismatch": _text = _text.Replace("ProcessId=00010510 (66832)", "ProcessId=00010511 (66832)", StringComparison.Ordinal); break;
            case "unknown-pid": required = 99999; break;
            case "managed-thread": required = 21; break;
            case "native-thread": required = 51936; break;
            case "auxiliary-required": required = 59232; break;
            case "truncated": _text = _text[..^Completed.Length]; break;
            case "open-native-block": _text = Banner + Managed("ResultsHandler", ":21", "Log file content:") + Managed("ResultsHandler", ":21", Separator) + Completed; break;
            case "exit-nonzero": _text = _text.Replace("Console Runner exit code: 0", "Console Runner exit code: 31", StringComparison.Ordinal); break;
            case "after-completed": _text += "unexpected trailing record\n"; break;
            case "orphan": _text = "unexpected orphan record\n" + _text; break;
            case "malformed-structured": _text = _text.Replace(Completed, "20:02:53.700 |W| broken\n" + Completed, StringComparison.Ordinal); break;
            case "stderr": Write(_error, "unexpected stderr\n"); break;
            case "banner": _text = _text.Replace("Console Runner 2026.2.3.1.", "Console Runner 2026.2.3.2.", StringComparison.Ordinal); break;
            default: throw new InvalidOperationException("unknown-cover-fixture-fault");
        }
        Write(_log, _text);
        var error = Assert.Throws<DotCoverProcessDiagnosticException>(() => Read(required))!;
        Assert.That(error.Code, Is.EqualTo("cover-process-" + expectedCode));
        Assert.That(error.Message, Is.EqualTo(error.Code));
        Assert.That(error.InnerException, Is.Null);
        Assert.That(error.Message, Does.Not.Contain(_root));
    }

    private CoverProcessDiagnosticReview Read(params int[] required) => DotCoverProcessDiagnostics.ReadValidated(_log, _error, required, "2026.2.3.1");
    private static void Write(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));
    private static string Managed(string component, string thread, string message) => $"20:02:51.796 |V| {component} | {thread} | {message}\n";

    private static string CoreBlock(int pid, string executable, string thread, bool shutdownResult)
    {
        var header = Managed("ResultsHandler", thread, "Log file content:") + Managed("ResultsHandler", thread, Separator)
            + Managed("ResultsHandler", thread, $"\t ProcessId={pid:X8} ({pid})")
            + Managed("ResultsHandler", thread, "\t Executable='" + executable + "'")
            + Managed("ResultsHandler", thread, "\t CommandLine='example arguments'");
        var events = new[]
        {
            "Profiler res=ok clsid={" + Clsid + "} pname='ReleaseCoverage'",
            "Profiler res=failed pname='ReleaseMemory'", "CreateComInstance clsid={" + Clsid + "} res=ok",
            "BProf::Initialize", "CProf::OnEndShutdown",
        };
        foreach (var message in events) header += Managed("ResultsHandler", thread, "0000CAE0 1 00000241525A64E0 " + message);
        if (shutdownResult) header += Managed("ResultsHandler", thread, "0000CAE0 1 0000000000000000 BProf::Shutdown res=ok");
        return header + Managed("ResultsHandler", thread, Separator);
    }

    private static string AuxiliaryBlock() => Managed("ResultsHandler", ".NET TP Worker:12", "Log file content:")
        + Managed("ResultsHandler", ".NET TP Worker:12", Separator)
        + Managed("ResultsHandler", ".NET TP Worker:12", "00010E28 Initialized")
        + Managed("ResultsHandler", ".NET TP Worker:12", "\t ProcessId=0000E760 (59232)")
        + Managed("ResultsHandler", ".NET TP Worker:12", "\t Executable='C:\\tools\\JetBrains.Profiler.PdbServer.exe'")
        + Managed("ResultsHandler", ".NET TP Worker:12", Separator);

    private static string Connection(string core, int pid) => Managed("ProfilingViewer", "BridgeCoreThread:22", "Incoming connection from profiler core.")
        + "SessionId: 2acace89-0592-4e08-a129-95dbbd99b4ba\nCoreId: " + core + "\nProcessId: " + pid + "\nExecutable: C:\\build\\testhost.exe\n";

    private static string Snapshot(string core, int valid, int total) => Managed("ResultsHandler", ".NET TP Worker:21", "SnapshotDone.")
        + "SessionId: 2acace89-0592-4e08-a129-95dbbd99b4ba\nCoreId: " + core + "\nSnapshot summary:\nGroups: 1\nValid modules: " + valid + " out of " + total + "\n";
}
