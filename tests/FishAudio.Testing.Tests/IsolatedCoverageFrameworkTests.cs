using System.Text;
using System.Text.Json;
using FishAudio.Testing.Evidence;
using FishAudio.Testing.Execution;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

public sealed partial class IsolatedCoverageSessionTests
{
    // Sanitized, hand-declared admission inputs, not an executed profiler session.
    // Only the Product/Tests DLL+PDB bindings and additional driver DLL are actual
    // copied binaries. Executable, profiler and SDK fixture files are never run.
    private const int FrameworkLauncherPid = 7101;
    private const int FrameworkTargetPid = 7102;
    private const int FrameworkHostPid = 7103;
    private const int ForeignFrameworkHostPid = 7104;
    private const long FrameworkFrequency = 10_000_000;
    private const string FrameworkFullName = "Example.Cases.Probe";
    private static readonly JsonSerializerOptions FrameworkFixtureJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [Test]
    [Property("FishAudioCaseId", "isolated-framework/validated-host-pair-without-exit-claims")]
    public void CallbackPairBindsControlHostWithoutIndividualProcessCompletionClaims()
    {
        var envelope = PrepareFrameworkSession();
        var before = CaptureFrameworkFixtureDigests();
        var host = ReadFixtureHostPair(envelope);
        Assert.That(host.ProcessId, Is.EqualTo(FrameworkHostPid));
        Assert.That(host.StartCallbackObserved && host.EndCallbackObserved, Is.True);
        Assert.That(host.OsExitClaim || host.PipeDrainClaim || host.JobOwnershipClaim || host.TestPassClaim || host.BaselineClaim,
            Is.False);
        Assert.That(_expected.Supervisor.Cover.OwnedProcesses, Is.Empty);

        CaseCoverage? admitted = null;
        Assert.DoesNotThrow(() => admitted = Read(),
            "framework-host-pair-must-admit-without-individual-exit-claims");
        var result = admitted ?? throw new InvalidOperationException("framework-fixture-admission-missing");
        Assert.That(result.Identity, Is.EqualTo(_expected.Identity));
        Assert.That(result.TestResult.CanonicalId, Is.EqualTo(CanonicalId));
        Assert.That(result.TestResult.FullName, Is.EqualTo(FrameworkFullName));
        Assert.That(result.Artifacts, Has.Count.EqualTo(15));
        Assert.That(result.Supervisor.FrameworkEnvelope, Is.EqualTo(envelope));
        Assert.That(result.Supervisor.Cover.OwnedProcesses, Is.Empty);
        Assert.That(result.AttributionMode, Is.EqualTo("isolated-case"));
        Assert.That(result.CompleteBaseline || result.SourceMappingsResolved, Is.False);
        AssertFrameworkFixtureDigests(before);
    }

    [TestCaseSource(nameof(FrameworkSupplementSealCases))]
    public void EachSupplementSealBindsItsOwnActualBytes(string role)
    {
        var envelope = PrepareFrameworkSession();
        var before = CaptureFrameworkFixtureDigests();
        ChangeFrameworkEnvelope(CorruptFrameworkPin(envelope, role));
        AssertFrameworkRejected("framework-envelope-file-mismatch", before);
    }

    [TestCaseSource(nameof(FrameworkRelationshipFaultCases))]
    public void InvalidFrameworkRelationshipsCannotReturnCaseCoverage(string fault, string reason)
    {
        var envelope = PrepareFrameworkSession();
        var before = CaptureFrameworkFixtureDigests();
        switch (fault)
        {
            case "pair-swap":
                ChangeFrameworkEnvelope(envelope with { HostStart = envelope.HostEnd, HostEnd = envelope.HostStart });
                break;
            case "pair-alias":
                ChangeFrameworkEnvelope(envelope with { HostEnd = envelope.HostStart });
                break;
            case "driver-hash":
                ChangeFrameworkEnvelope(envelope with { DriverAssemblySha256 = DifferentFrameworkHash(envelope.DriverAssemblySha256) });
                break;
            case "optional-bad-host-seal":
                _expected = _expected with { RequireFrameworkSupervision = false };
                ChangeFrameworkEnvelope(envelope with { HostEnd = envelope.HostEnd with { Sha256 = DifferentFrameworkHash(envelope.HostEnd.Sha256) } });
                break;
            case "foreign-host-pid":
                // These two additional files are forged negative inputs. The
                // original callback pair and six declared report events stay intact.
                var pair = WriteFrameworkHostPair(ForeignFrameworkHostPid, "foreign-host");
                var forged = envelope with { HostStart = pair.Start, HostEnd = pair.End };
                var independentlyValidPair = ReadFixtureHostPair(forged);
                Assert.That(independentlyValidPair.ProcessId, Is.EqualTo(ForeignFrameworkHostPid));
                Assert.That(independentlyValidPair.OsExitClaim || independentlyValidPair.PipeDrainClaim || independentlyValidPair.JobOwnershipClaim,
                    Is.False);
                ChangeFrameworkEnvelope(forged);
                break;
            default: throw new InvalidOperationException("unknown-framework-fixture-fault");
        }
        AssertFrameworkRejected(reason, before);
    }

    private static IEnumerable<TestCaseData> FrameworkSupplementSealCases()
    {
        foreach (var row in new[]
        {
            (Role: "DriverInvocation", Id: "driver-invocation"),
            (Role: "DriverStdout", Id: "driver-stdout"),
            (Role: "DriverStderr", Id: "driver-stderr"),
            (Role: "ExecutionRequest", Id: "execution-request"),
            (Role: "RunSettings", Id: "run-settings"),
            (Role: "ControlContext", Id: "control-context"),
            (Role: "Supervisor", Id: "supervisor"),
            (Role: "HostStart", Id: "host-start"),
            (Role: "HostEnd", Id: "host-end"),
        })
        {
            var id = "isolated-framework/tampered-" + row.Id;
            yield return new TestCaseData(row.Role).SetName(id).SetProperty("FishAudioCaseId", id);
        }
    }

    private static IEnumerable<TestCaseData> FrameworkRelationshipFaultCases()
    {
        foreach (var row in new[]
        {
            (Fault: "pair-swap", Reason: "framework-host-lifecycle-invalid", Id: "swapped-host-pair"),
            (Fault: "pair-alias", Reason: "framework-envelope-file-identity", Id: "aliased-host-pair"),
            (Fault: "driver-hash", Reason: "framework-driver-mismatch", Id: "driver-sha-mismatch"),
            (Fault: "optional-bad-host-seal", Reason: "framework-envelope-file-mismatch", Id: "optional-flag-cannot-skip-bad-host-seal"),
            (Fault: "foreign-host-pid", Reason: "framework-host-event-mismatch", Id: "callback-pid-must-match-control-host"),
        })
        {
            var id = "isolated-framework/" + row.Id;
            yield return new TestCaseData(row.Fault, row.Reason).SetName(id).SetProperty("FishAudioCaseId", id);
        }
    }

    private CoverageFrameworkEnvelope PrepareFrameworkSession()
    {
        _started = new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);
        var identity = _input.Identity with
        {
            RunId = "11111111111111111111111111111111",
            SessionId = "22222222222222222222222222222222",
        };
        var cover = _expected.Supervisor.Cover with { StartedUtc = _started.AddSeconds(1), FinishedUtc = _started.AddSeconds(10) };
        var export = _expected.Supervisor.Export with { StartedUtc = _started.AddSeconds(11), FinishedUtc = _started.AddSeconds(12) };
        _input = _input with { Identity = identity };
        _expected = _expected with
        {
            Identity = identity,
            RequireFrameworkSupervision = true,
            Supervisor = _expected.Supervisor with
            {
                Identity = identity, StartedUtc = _started, SealedUtc = _started.AddSeconds(20), Cover = cover, Export = export,
            },
        };
        var raw = _input.ArtifactDirectory;
        var output = Path.Combine(raw, "framework-cover");
        var phase = Path.Combine(raw, "phases", "cover");
        var testDll = Path.Combine(_expected.BuildDirectory, "tests.dll");
        var testHash = _expected.Binaries.Single(item => item.Role == CoverageBinaryRole.Tests).ExpectedIdentity.DllSha256;
        var driver = Path.Combine(_expected.BuildDirectory, "driver", "FishAudio.Testing.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(driver)!);
        File.Copy(typeof(BinaryIdentity).Assembly.Location, driver);
        var tool = Path.Combine(_expected.BuildDirectory, "fixture-profiler", "dotCover.dll");
        var vstest = Path.Combine(_expected.BuildDirectory, "fixture-sdk", "vstest.console.dll");
        var dotnet = Path.Combine(_expected.BuildDirectory, "fixture-sdk", "dotnet.exe");
        var shell = Path.Combine(_expected.BuildDirectory, "fixture-sdk", "pwsh.exe");
        var launcher = Path.Combine(_expected.SourceDirectory, "tools", "Invoke-TestProcess.ps1");
        foreach (var path in new[] { tool, vstest, dotnet, shell, launcher })
            WriteFrameworkText(path, "inert hand-declared fixture input; never executed", _started);
        _expected = _expected with { ToolFile = tool, ToolSha256 = Hash(tool) };

        SetFrameworkArtifact(CoverageArtifactKind.NUnitXml, "framework-cover/result-xml/tests.xml", Terminal);
        SetFrameworkArtifact(CoverageArtifactKind.Trx, "framework-cover/trx/results.trx", Trx);
        SetFrameworkArtifact(CoverageArtifactKind.Snapshot, "framework-cover/coverage.dcvr", "opaque-native-snapshot-fixture");
        SetFrameworkArtifact(CoverageArtifactKind.CoverLog, "framework-cover/cover.log", "");
        SetFrameworkArtifact(CoverageArtifactKind.CoverStdout, "framework-cover/stdout.bin", "");
        SetFrameworkArtifact(CoverageArtifactKind.CoverStderr, "framework-cover/stderr.bin", "");
        SetFrameworkArtifact(CoverageArtifactKind.CoverProcess, "phases/cover/process.json", JsonSerializer.Serialize(new
        {
            schemaVersion = 1, startedUtc = cover.StartedUtc, finishedUtc = cover.FinishedUtc,
            completed = true, exitCode = 0, elapsedMilliseconds = 9000.0,
            stdoutDrained = true, stderrDrained = true, outputDrained = true, ownedTreeCompleted = true,
            ownership = "windows-job-before-target-go", rootProcessId = 7100,
            cleanupFailed = false, metadataTimeoutMilliseconds = 0, individualChildExitObservation = "not-collected",
        }, FrameworkFixtureJson));
        SetFrameworkArtifact(CoverageArtifactKind.ExportProcess, Artifact(CoverageArtifactKind.ExportProcess).RelativePath, ProcessRecord(export));
        var runSettings = Path.Combine(raw, "execution.runsettings");
        WriteFrameworkText(runSettings, "<RunSettings><NUnit><NumberOfTestWorkers>0</NumberOfTestWorkers></NUnit></RunSettings>", _started.AddMilliseconds(500));
        var requestPath = Path.Combine(raw, "framework-cover-request.json");
        var runtime = Path.Combine(raw, "runtime-evidence");
        Directory.CreateDirectory(runtime);
        var environment = new Dictionary<string, string>
        {
            ["DOTNET_CLI_UI_LANGUAGE"] = "en", ["VSLANG"] = "1033", ["FISHAUDIO_COMPAT_PARALLEL_PROOF"] = "0",
            ["FISHAUDIO_COMPAT_EVIDENCE_ROOT"] = runtime, ["FISHAUDIO_TEST_REPOSITORY_ROOT"] = _expected.SourceDirectory,
            ["FISHAUDIO_PWSH_EXECUTABLE"] = shell, ["FISHAUDIO_DOTNET_EXECUTABLE"] = dotnet,
            ["FISHAUDIO_COVERAGE_SESSION_ID"] = identity.SessionId, ["FISHAUDIO_COVERAGE_RUN_ID"] = identity.RunId,
            ["FISHAUDIO_COVERAGE_CASE_ID"] = CanonicalId, ["FISHAUDIO_COVERAGE_SOURCE_TREE"] = identity.Source.Tree,
            ["FISHAUDIO_COVERAGE_EVIDENCE_ROOT"] = runtime,
        };
        var launch = new DotCoverSdkVstestLaunch(identity.SessionId, tool, Hash(tool), vstest, Hash(vstest),
            FrameworkArtifactPath(CoverageArtifactKind.Snapshot), FrameworkArtifactPath(CoverageArtifactKind.CoverLog));
        var request = new FrameworkRunRequest(1, identity.RunId, identity.Source.Tree, _expected.SourceDirectory,
            shell, dotnet, launcher, testDll, testHash, runSettings, FrameworkArtifactPath(CoverageArtifactKind.Discovery),
            FrameworkArtifactPath(CoverageArtifactKind.NUnitXml), FrameworkArtifactPath(CoverageArtifactKind.Trx), output,
            [new(CanonicalId, FrameworkFullName)], MaximumConcurrency: 1, RequireOrder: true,
            Environment: environment, SourceFingerprint: identity.Source.Fingerprint, ProfiledLaunch: launch);
        WriteFrameworkJson(requestPath, request, _started.AddMilliseconds(500));
        const string nonce = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string pipe = "fa-fw-33333333333333333333333333333333-host";
        var contextPath = Path.Combine(output, "control-context.json");
        WriteFrameworkJson(contextPath, new
        {
            schemaVersion = 1, runId = identity.RunId, sourceTree = identity.Source.Tree, nonce, pipeName = pipe,
            testAssembly = testDll, testAssemblySha256 = testHash, cases = request.Cases,
        }, _started.AddSeconds(2));
        var innerEnvironment = new Dictionary<string, string>(environment)
        {
            ["FISHAUDIO_FRAMEWORK_CONTROL_CONTEXT"] = contextPath,
        };
        string[] innerArguments = [tool, "cover", "--target-executable", dotnet, "--snapshot-output", launch.SnapshotPath,
            "--log-file", launch.LogPath, "--", "exec", vstest, testDll, "--Settings:" + runSettings,
            "--logger:trx;LogFileName=results.trx", "--ResultsDirectory:" + Path.GetDirectoryName(request.TrxPath)];
        SetFrameworkArtifact(CoverageArtifactKind.CoverInvocation, "framework-cover/invocation.json", JsonSerializer.Serialize(new
        {
            schemaVersion = 1, executable = dotnet, workingDirectory = request.WorkingDirectory,
            arguments = innerArguments, environment = innerEnvironment,
            driverControl = new { pipeName = pipe[..^"-host".Length] + "-launcher", nonce, runId = identity.RunId, sourceTree = identity.Source.Tree },
            sessionRunId = identity.SessionId, canonicalCaseId = CanonicalId,
        }, FrameworkFixtureJson));
        SetFrameworkArtifact(CoverageArtifactKind.ExportInvocation, Artifact(CoverageArtifactKind.ExportInvocation).RelativePath,
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1, executable = dotnet, workingDirectory = request.WorkingDirectory,
                sessionRunId = identity.SessionId, canonicalCaseId = CanonicalId,
                arguments = new[] { tool, "report", "--snapshot-source", launch.SnapshotPath,
                    "--json-report-output", FrameworkArtifactPath(CoverageArtifactKind.AggregateJson),
                    "--log-file", FrameworkArtifactPath(CoverageArtifactKind.ExportLog) },
            }, FrameworkFixtureJson));
        var outerInvocation = Path.Combine(phase, "argv.json");
        WriteFrameworkJson(outerInvocation, new
        {
            schemaVersion = 1, executable = dotnet, workingDirectory = request.WorkingDirectory,
            sessionRunId = identity.SessionId, canonicalCaseId = CanonicalId,
            arguments = new[] { driver, "framework-run", "--request", requestPath },
        }, _started.AddMilliseconds(500));
        var outerStdout = Path.Combine(phase, "stdout.log");
        var outerStderr = Path.Combine(phase, "stderr.log");
        WriteFrameworkText(outerStdout, "", cover.FinishedUtc);
        WriteFrameworkText(outerStderr, "", cover.FinishedUtc);
        var supervisorPath = Path.Combine(output, "supervisor.json");
        object Event(string role, int pid, string kind, long timestamp, string? caseId = null,
            string? fullName = null, string? outcome = null, int? targetPid = null, int? exitCode = null) => new
        {
            schemaVersion = 1, role, nonce, runId = identity.RunId, sourceTree = identity.Source.Tree, pid, kind,
            timestamp, frequency = FrameworkFrequency, assemblySha256 = role == "nunit-host" ? testHash : null,
            caseId, fullName, ordinal = 0, queuedTimestamp = 0L, outcome, targetPid, exitCode,
        };
        WriteFrameworkJson(supervisorPath, new
        {
            schemaVersion = 1, kind = "framework-execution", runId = identity.RunId, sourceTree = identity.Source.Tree,
            sourceFingerprint = identity.Source.Fingerprint, testAssemblySha256 = testHash, baselineClaim = false,
            failureCode = (string?)null, selectedIds = new[] { CanonicalId },
            state = new
            {
                complete = true, stage = "child-exit", failureStage = (string?)null, failureCaseId = (string?)null,
                terminationRequired = false, passedIds = new[] { CanonicalId }, failedIds = Array.Empty<string>(),
                timedOutIds = Array.Empty<string>(), activeIds = Array.Empty<string>(), interruptedIds = Array.Empty<string>(),
                notRunIds = Array.Empty<string>(),
            },
            target = new { started = true, pid = FrameworkTargetPid, exitCode = 0 },
            process = new { launcherPid = FrameworkLauncherPid, launcherExited = true, stdoutDrained = true, stderrDrained = true, ownedActiveProcesses = 0 },
            rawEvidence = new
            {
                verified = true, resultXmlPath = request.ResultXmlPath, trxPath = request.TrxPath,
                resultXmlSha256 = Artifact(CoverageArtifactKind.NUnitXml).Sha256, trxSha256 = Artifact(CoverageArtifactKind.Trx).Sha256,
            },
            launch = new
            {
                kind = "dotcover-sdk-vstest", sessionId = identity.SessionId,
                invocationSha256 = Artifact(CoverageArtifactKind.CoverInvocation).Sha256,
                toolSha256 = launch.ToolSha256, vstestConsoleSha256 = launch.VstestConsoleSha256,
            },
            finalization = new { frequency = FrameworkFrequency, cleanupAnchorTimestamp = 7000L },
            elapsedMilliseconds = 9000.0,
            events = new[]
            {
                Event("execution-launcher", FrameworkLauncherPid, "target-started", 1000, targetPid: FrameworkTargetPid),
                Event("nunit-host", FrameworkHostPid, "ready", 2000),
                Event("nunit-host", FrameworkHostPid, "case-start", 3000, CanonicalId, FrameworkFullName),
                Event("nunit-host", FrameworkHostPid, "case-finished", 4000, CanonicalId, FrameworkFullName, "passed"),
                Event("nunit-host", FrameworkHostPid, "cleanup-complete", 5000),
                Event("execution-launcher", FrameworkLauncherPid, "target-exited", 6000, targetPid: FrameworkTargetPid, exitCode: 0),
            },
        }, _started.AddMilliseconds(9900));
        foreach (var artifact in _input.Artifacts.ToArray())
        {
            var seconds = artifact.Kind switch
            {
                CoverageArtifactKind.Discovery => 0,
                CoverageArtifactKind.NUnitXml or CoverageArtifactKind.Trx => 8,
                CoverageArtifactKind.CoverInvocation => 2,
                CoverageArtifactKind.CoverProcess => 10,
                CoverageArtifactKind.Snapshot or CoverageArtifactKind.CoverLog or CoverageArtifactKind.CoverStdout or CoverageArtifactKind.CoverStderr => 9,
                _ => 12,
            };
            var path = FrameworkArtifactPath(artifact.Kind);
            File.SetLastWriteTimeUtc(path, _started.AddSeconds(seconds).UtcDateTime);
            UpdateDescriptor(artifact with { LastWriteUtc = new(File.GetLastWriteTimeUtc(path)) });
        }
        ChangeReview(_expected.Supervisor.Diagnostics with
        {
            Evidence = _input.Artifacts.Where(item => DiagnosticKinds.Contains(item.Kind))
                .Select(item => new CoverageDiagnosticBinding(item.Kind, item.Sha256)).ToArray(),
        });
        var pair = WriteFrameworkHostPair(FrameworkHostPid, "runtime-evidence");
        var envelope = new CoverageFrameworkEnvelope(1, SealFrameworkFixture(outerInvocation), SealFrameworkFixture(outerStdout),
            SealFrameworkFixture(outerStderr), SealFrameworkFixture(requestPath), SealFrameworkFixture(runSettings),
            SealFrameworkFixture(contextPath), SealFrameworkFixture(supervisorPath), pair.Start, pair.End,
            Path.GetRelativePath(_expected.BuildDirectory, driver).Replace('\\', '/'), Hash(driver));
        ChangeFrameworkEnvelope(envelope);
        return envelope;
    }

    private (CoverageOwnedProcessEvidence Start, CoverageOwnedProcessEvidence End) WriteFrameworkHostPair(int pid, string relativeDirectory)
    {
        var directory = Path.Combine(_input.ArtifactDirectory, relativeDirectory);
        var start = Path.Combine(directory, $"host-{pid}.start.json");
        var end = Path.Combine(directory, $"host-{pid}.end.json");
        object Record(string phase, DateTimeOffset? finishedUtc) => new
        {
            schemaVersion = 1, phase, runId = _expected.Identity.RunId, sessionId = _expected.Identity.SessionId,
            canonicalId = CanonicalId, sourceTree = _expected.Identity.Source.Tree, pid,
            assemblyPath = Path.Combine(_expected.BuildDirectory, "tests.dll"),
            assemblySha256 = _expected.Binaries.Single(item => item.Role == CoverageBinaryRole.Tests).ExpectedIdentity.DllSha256,
            startedUtc = _started.AddSeconds(3), finishedUtc, baselineClaim = false,
        };
        WriteFrameworkJson(start, Record("start", null), _started.AddSeconds(3));
        WriteFrameworkJson(end, Record("end", _started.AddSeconds(7)), _started.AddSeconds(7));
        return (SealFrameworkFixture(start), SealFrameworkFixture(end));
    }

    private NUnitHostLifecycleEvidence ReadFixtureHostPair(CoverageFrameworkEnvelope envelope) => NUnitHostEvidence.ReadValidated(
        Path.Combine(_input.ArtifactDirectory, envelope.HostStart.RelativePath), Path.Combine(_input.ArtifactDirectory, envelope.HostEnd.RelativePath),
        new(1, _expected.Identity.RunId, _expected.Identity.SessionId, CanonicalId, _expected.Identity.Source.Tree,
            Path.Combine(_expected.BuildDirectory, "tests.dll"),
            _expected.Binaries.Single(item => item.Role == CoverageBinaryRole.Tests).ExpectedIdentity.DllSha256,
            _expected.Supervisor.Cover.StartedUtc, _expected.Supervisor.Cover.FinishedUtc));

    private void SetFrameworkArtifact(CoverageArtifactKind kind, string relativePath, string contents)
    {
        var path = Path.Combine(_input.ArtifactDirectory, relativePath);
        WriteFrameworkText(path, contents, _started.AddSeconds(8));
        UpdateDescriptor(new(kind, relativePath, Hash(path), new FileInfo(path).Length, new(File.GetLastWriteTimeUtc(path))));
    }

    private string FrameworkArtifactPath(CoverageArtifactKind kind) => Path.GetFullPath(Path.Combine(_input.ArtifactDirectory, Artifact(kind).RelativePath));

    private static void WriteFrameworkText(string path, string contents, DateTimeOffset written)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(path, written.UtcDateTime);
    }

    private static void WriteFrameworkJson(string path, object value, DateTimeOffset written) =>
        WriteFrameworkText(path, JsonSerializer.Serialize(value, FrameworkFixtureJson), written);

    private CoverageOwnedProcessEvidence SealFrameworkFixture(string path) => new(
        Path.GetRelativePath(_input.ArtifactDirectory, path).Replace('\\', '/'), Hash(path), new FileInfo(path).Length,
        new(File.GetLastWriteTimeUtc(path)));

    private void ChangeFrameworkEnvelope(CoverageFrameworkEnvelope envelope) =>
        _expected = _expected with { Supervisor = _expected.Supervisor with { FrameworkEnvelope = envelope } };

    private static string DifferentFrameworkHash(string hash) => (hash[0] == '0' ? "1" : "0") + hash[1..];

    private static CoverageFrameworkEnvelope CorruptFrameworkPin(CoverageFrameworkEnvelope envelope, string role)
    {
        static CoverageOwnedProcessEvidence Bad(CoverageOwnedProcessEvidence pin) => pin with { Sha256 = DifferentFrameworkHash(pin.Sha256) };
        return role switch
        {
            "DriverInvocation" => envelope with { DriverInvocation = Bad(envelope.DriverInvocation) },
            "DriverStdout" => envelope with { DriverStdout = Bad(envelope.DriverStdout) },
            "DriverStderr" => envelope with { DriverStderr = Bad(envelope.DriverStderr) },
            "ExecutionRequest" => envelope with { ExecutionRequest = Bad(envelope.ExecutionRequest) },
            "RunSettings" => envelope with { RunSettings = Bad(envelope.RunSettings) },
            "ControlContext" => envelope with { ControlContext = Bad(envelope.ControlContext) },
            "Supervisor" => envelope with { Supervisor = Bad(envelope.Supervisor) },
            "HostStart" => envelope with { HostStart = Bad(envelope.HostStart) },
            "HostEnd" => envelope with { HostEnd = Bad(envelope.HostEnd) },
            _ => throw new InvalidOperationException("unknown-framework-supplement-role"),
        };
    }

    private Dictionary<string, string> CaptureFrameworkFixtureDigests() => Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => path, Hash, StringComparer.OrdinalIgnoreCase);

    private static void AssertFrameworkFixtureDigests(IReadOnlyDictionary<string, string> before)
    {
        foreach (var item in before) Assert.That(Hash(item.Key), Is.EqualTo(item.Value));
    }

    private void AssertFrameworkRejected(string reason, IReadOnlyDictionary<string, string> before)
    {
        var failure = Assert.Throws<IsolatedCoverageSessionException>(() => Read(), "framework-fixture-fault-must-not-return-case-coverage");
        Assert.That(failure!.Code, Is.EqualTo("coverage-session-" + reason));
        AssertFrameworkFixtureDigests(before);
    }
}
