using System.Diagnostics;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
public sealed class ChildCompatibilityCases
{
    private const string NUnitCaseId = "compat.child/streaming-text-first-window";
    private const string LegacyCaseId = "streaming.text/first-window-is-lazy-and-cuts-ordinary-long-sentences";

    [Test]
    [Property("FishAudioCaseId", NUnitCaseId)]
    public async Task StreamingTextRunsInTheSupervisedChild()
    {
        try
        {
            var configuredRoot = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
            var retainEvidence = !string.IsNullOrEmpty(configuredRoot);
            if (retainEvidence && !Path.IsPathFullyQualified(configuredRoot!))
                throw new InvalidDataException("compat-evidence-root-must-be-absolute");
            var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(retainEvidence ? configuredRoot! : Path.GetTempPath()));
            var name = "fa-c-" + Guid.NewGuid().ToString("N");
            var directory = Path.GetFullPath(Path.Combine(parent, name));
            var created = false;
            await RegressionFixtureOwner.RunAsync(async () =>
            {
                if (Directory.Exists(directory)) throw new IOException("compat-evidence-owner-already-exists");
                Directory.CreateDirectory(directory);
                created = true;
                await RunOwnedChildAsync(directory);
            }, () =>
            {
                if (created && !retainEvidence) DeleteOwnedDirectory(parent, name, directory);
                return Task.CompletedTask;
            });
        }
        catch (Exception error)
        {
            // Raw process, assertion and filesystem diagnostics remain private.
            NUnitAssert.Fail($"Child compatibility failed ({error.GetType().Name}).");
        }
    }

    private static async Task RunOwnedChildAsync(string directory)
    {
        var registry = RegressionCatalog.Create();
        var selected = registry.Where(item => item.Id == LegacyCaseId).ToArray();
        NUnitAssert.That(selected.Length, Is.EqualTo(1));
        NUnitAssert.That(selected[0].Partition, Is.EqualTo("core"));
        var source = await RegressionSource.CaptureAsync();
        source.RecordDirtyScope(registry);
        NUnitAssert.That(source.Identity.Available, Is.True);
        var report = new RegressionReport
        {
            Source = source.Identity,
            Invocation = ["nunit-compatibility-child", LegacyCaseId],
            SelectionReasons = ["nunit-child-compatibility"],
            Cases = [new() { Id = LegacyCaseId, Partition = "core" }]
        };
        var reportPath = Path.Combine(directory, "regression.json");
        report.Create(reportPath);
        ObservedChildProcess? child = null;
        using var host = new RegressionProcessHost(start: info =>
        {
            if (child is not null) throw new InvalidOperationException("compat-child-started-more-than-once");
            return child = new ObservedChildProcess(info);
        });
        await host.ExecuteAsync(selected, report, reportPath);
        report.Complete = report.Cases.All(item => item.Outcome == "passed");
        report.FinishedUtc = DateTimeOffset.UtcNow;
        report.Write(reportPath);
        var authoritative = RegressionReport.Read(reportPath);
        RegressionProcessProtocol.WriteNew(Path.Combine(directory, "identity.json"), new
        {
            SchemaVersion = 1,
            ProcessId = Environment.ProcessId,
            ChildProcessId = child?.ProcessId,
            NUnitCaseId,
            NUnitFullName = typeof(ChildCompatibilityCases).FullName + "." + nameof(StreamingTextRunsInTheSupervisedChild),
            LegacyCaseId,
            authoritative.RunId,
            ChildRequestId = authoritative.Cases.Single().ChildRequestId,
            ChildExitCode = child?.ObservedExitCode,
            authoritative.Source
        });

        NUnitAssert.That(authoritative.RunId, Is.EqualTo(report.RunId));
        NUnitAssert.That(RegressionReport.Identity(authoritative), Is.EqualTo(RegressionReport.Identity(report)));
        NUnitAssert.That(RegressionProcessProtocol.SameSource(authoritative.Source, source.Identity), Is.True);
        NUnitAssert.That(authoritative.Cases.Count, Is.EqualTo(1));
        var result = authoritative.Cases[0];
        NUnitAssert.That(result.Id, Is.EqualTo(LegacyCaseId));
        NUnitAssert.That(result.Partition, Is.EqualTo("core"));
        NUnitAssert.That(authoritative.Complete && authoritative.FinishedUtc is not null && result.Outcome == "passed", Is.True);
        NUnitAssert.That(child is not null && child.ObservedExitCode == 0, Is.True);
        NUnitAssert.That(Guid.TryParseExact(result.ChildRequestId, "N", out _), Is.True);

        var requestPaths = Directory.GetFiles(directory, "*.child.json.request.json");
        NUnitAssert.That(requestPaths.Length, Is.EqualTo(1));
        var request = RegressionProcessProtocol.Read<RegressionChildRequest>(requestPaths[0]);
        RegressionProcessProtocol.ValidateRequest(request);
        NUnitAssert.That(request.CaseIds, Is.EqualTo(new[] { LegacyCaseId }));
        NUnitAssert.That(request.LegacyArguments is null && request.Probe is null, Is.True);
        NUnitAssert.That(request.RequestId, Is.EqualTo(result.ChildRequestId));
        NUnitAssert.That(request.RunId, Is.EqualTo(authoritative.RunId));
        NUnitAssert.That(RegressionProcessProtocol.SameSource(request.Source, authoritative.Source), Is.True);
        var manifest = RegressionProcessProtocol.Read<RegressionChildManifest>(request.ManifestPath);
        RegressionProcessProtocol.ValidateManifest(manifest, request);
        NUnitAssert.That(manifest.Final && manifest.Complete && manifest.Cases.Single().Outcome == "passed", Is.True);
    }

    private static void DeleteOwnedDirectory(string parent, string name, string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        if (!Path.IsPathFullyQualified(parent) || !Path.IsPathFullyQualified(directory) ||
            !string.Equals(Path.GetDirectoryName(fullPath), parent, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(fullPath) != name || !name.StartsWith("fa-c-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[5..], "N", out _))
            throw new InvalidOperationException("compat-evidence-owner-changed");
        Directory.Delete(fullPath, recursive: true);
    }

    // Observe the real process supplied to the unchanged host; never execute the
    // selected delegate in the NUnit process or substitute transport evidence.
    private sealed class ObservedChildProcess : IRegressionChildProcess
    {
        private readonly Process _process;

        internal ObservedChildProcess(ProcessStartInfo info)
        {
            _process = Process.Start(info) ?? throw new IOException("compat-child-start-failed");
            ProcessId = _process.Id;
            Exit = ObserveExitAsync();
        }

        internal int ProcessId { get; }
        internal int? ObservedExitCode { get; private set; }
        public TextReader Output => _process.StandardOutput;
        public TextReader Error => _process.StandardError;
        public Task Exit { get; }
        public int ExitCode => _process.ExitCode;

        private async Task ObserveExitAsync()
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
            ObservedExitCode = _process.ExitCode;
        }

        public void KillTree()
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }

        public void Dispose() => _process.Dispose();
    }
}
