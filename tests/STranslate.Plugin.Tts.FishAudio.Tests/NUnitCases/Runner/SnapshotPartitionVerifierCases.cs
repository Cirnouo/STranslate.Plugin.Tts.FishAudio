using System.Text.Json;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace NUnitCases.Runner;

// Synthetic metadata through the real generation owner; no scenarios/native/Git.
[TestFixture]
[NonParallelizable]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Property("FishAudioFreshHost", "true")]
[Property("FishAudioExclusiveResource", "process-environment")]
public sealed class SnapshotPartitionVerifierCases
{
    private const string Prefix = "fa-partition-verifier-";
    private const string Tree = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private string _parent = "", _root = "", _partition = "";
    private string? _previousContext;
    private bool _configured;
    private RegressionCase[] _catalog = [];

    [SetUp]
    public void CreateOwnedSyntheticSnapshotAndCurrentCatalog()
    {
        _parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        RequireDirectoryChain(_parent);
        _root = Path.Combine(_parent, Prefix + Guid.NewGuid().ToString("N"));
        if (File.Exists(_root) || Directory.Exists(_root)) throw new InvalidOperationException("partition-verifier-owner-exists");
        Directory.CreateDirectory(_root);
        RequireOwnedRoot();
        var sourceRoot = RegressionSource.FindSourceRoot()
            ?? throw new InvalidOperationException("partition-verifier-runner-source-owner-missing");
        var context = Path.Combine(_root, "snapshot.json");
        File.WriteAllText(context, JsonSerializer.Serialize(new
        {
            schemaVersion = 1, root = sourceRoot, tree = Tree,
            sourceCommit = new string('b', 40), files = Array.Empty<object>(),
        }));
        _previousContext = Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT");
        Environment.SetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT", context);
        _configured = true;
        _ = SnapshotContext.Read() ?? throw new InvalidOperationException("partition-verifier-context-must-admit");
        _catalog = RegressionCatalog.Create().ToArray();
        _partition = RegressionProcessProtocol.Partitions.First(partition =>
            _catalog.Count(item => item.Partition == partition) > 1);
    }

    [TearDown]
    public void RestoreOwnedEnvironmentAndCleanOnlyOwnedEvidence()
    {
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        var cleanupFailed = false;
        try { if (_configured) Environment.SetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT", _previousContext); }
        catch { cleanupFailed = true; Note("partition-verifier: environment cleanup failed."); }
        try
        {
            if (_root.Length != 0)
            {
                RequireOwnedRoot();
                if (failed) Note("partition-verifier: failed raw generations retained.");
                else
                {
                    var pending = new Stack<string>();
                    pending.Push(_root);
                    while (pending.Count != 0)
                    {
                        var directory = pending.Pop();
                        RequireDirectoryChain(directory);
                        foreach (var path in Directory.GetFileSystemEntries(directory))
                        {
                            var attributes = File.GetAttributes(path);
                            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                                throw new InvalidOperationException("partition-verifier-cleanup-entry-invalid");
                            if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                        }
                    }
                    Directory.Delete(_root, recursive: true);
                }
            }
        }
        catch { cleanupFailed = true; Note("partition-verifier: directory cleanup failed; owned inputs retained."); }
        if (cleanupFailed && !failed) throw new InvalidOperationException("partition-verifier-cleanup-failed");
    }

    [Test]
    [Property("FishAudioCaseId", "infra.snapshot-partition/complete-current-partition-export")]
    public void CompleteCurrentPartitionExportsOnlyItsActualAuthoritativeIdentity()
    {
        var report = Report();
        var path = Save(report, "complete.json");
        var output = Path.Combine(_root, "admitted.json");
        var before = Bytes();
        if (RegressionReport.BuildMetadata("RegressionBuildConfiguration") != "Release")
        {
            Assert.Throws<InvalidDataException>(() => Verify(path, output),
                "partition-verifier-must-retain-release-build-requirement");
            Unchanged(before);
            return;
        }

        Assert.That(Verify(path, output), Is.Zero);
        Assert.That(File.Exists(output), Is.True, "partition-verifier-must-export-authoritative-current-subset");
        using var document = JsonDocument.Parse(File.ReadAllBytes(output));
        var root = document.RootElement;
        Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("verified-legacy-partition-catalog"));
        Assert.That(root.GetProperty("sourceTree").GetString(), Is.EqualTo(Tree));
        Assert.That(root.GetProperty("partition").GetString(), Is.EqualTo(_partition));
        var exportedSource = root.GetProperty("source").Deserialize<RegressionSourceIdentity>(RegressionReport.JsonOptions)!;
        Assert.That(RegressionProcessProtocol.SameSource(exportedSource, report.Source), Is.True);
        Assert.That(root.GetProperty("source").GetProperty("snapshot").GetBoolean(), Is.True,
            "This fixture explicitly owns a SnapshotContext; live callers must retain their actual false value.");
        Assert.That(root.GetProperty("reportRunId").GetString(), Is.EqualTo(report.RunId));
        Assert.That(root.GetProperty("reportGeneration").GetInt64(), Is.EqualTo(RegressionReport.Read(path).Generation));
        Assert.That(root.GetProperty("cases").EnumerateArray()
            .Select(item => (item.GetProperty("id").GetString(), item.GetProperty("partition").GetString())),
            Is.EqualTo(_catalog.Where(item => item.Partition == _partition).Select(item => (item.Id, item.Partition))));
        Unchanged(before);
    }

    public static IEnumerable<TestCaseData> RejectedReports()
    {
        foreach (var fault in new[] { "missing-case", "whole-catalog", "wrong-source", "wrong-partition", "failed-latest-generation" })
            yield return new TestCaseData(fault).SetName("snapshot-partition/reject/" + fault)
                .SetProperty("FishAudioCaseId", "infra.snapshot-partition/reject-" + fault);
    }

    [TestCaseSource(nameof(RejectedReports))]
    public void PartialForeignOrLaterFailedEvidenceCannotAuthorizeThePartition(string fault)
    {
        var report = Report();
        switch (fault)
        {
            case "missing-case": report.Cases.RemoveAt(report.Cases.Count - 1); break;
            case "whole-catalog": report.Cases = _catalog.Select(Result).ToList(); break;
            case "wrong-source":
                report.Source = new SnapshotContext { Tree = new string('c', 40), SourceCommit = new string('b', 40) }.Identity();
                break;
            case "wrong-partition":
                report.Cases[0].Partition = RegressionProcessProtocol.Partitions.First(item => item != _partition);
                break;
        }
        var path = Save(report, "rejected.json");
        if (fault == "failed-latest-generation")
        {
            var passedProjection = File.ReadAllBytes(path);
            report.Complete = false;
            report.Cases[0].Outcome = "failed";
            report.Write(path);
            // A byte-exact valid old projection cannot hide the newer immutable
            // failure generation. Preserve both original generations as inputs.
            File.WriteAllBytes(path, passedProjection);
            Assert.That(RegressionReport.Read(path).Generation, Is.EqualTo(2),
                "partition-verifier-fixture-must-use-authoritative-latest-generation");
            Assert.That(RegressionReport.Read(path).Cases[0].Outcome, Is.EqualTo("failed"));
        }
        else
        {
            Assert.That(RegressionReport.Read(path).Generation, Is.GreaterThan(0),
                "partition-verifier-fixture-must-pass-real-generation-reader");
        }
        var output = Path.Combine(_root, "must-not-admit.json");
        var before = Bytes();

        Assert.Throws<InvalidDataException>(() => Verify(path, output),
            "partition-verifier-invalid-owner-or-generation-must-not-admit");

        Assert.That(File.Exists(output), Is.False);
        Unchanged(before);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.snapshot-partition/reject-unknown-partition-argv")]
    public void UnknownProtocolPartitionIsRejectedBeforeTryingToReadAReport()
    {
        var missing = Path.Combine(_root, "does-not-exist.json");
        Assert.That(RegressionSnapshotVerifier.IsInvocation(["--verify-snapshot-partition-report"]), Is.True);
        Assert.That(RegressionSnapshotVerifier.Run(
            ["--verify-snapshot-partition-report", missing, Tree, "--partition", "unknown-native-slot"]), Is.EqualTo(2),
            "partition-verifier-argv-must-use-real-fixed-protocol-partition");
        Assert.That(File.Exists(missing), Is.False);
    }

    private int Verify(string report, string output) => RegressionSnapshotVerifier.Run(
        ["--verify-snapshot-partition-report", report, Tree, "--partition", _partition, "--catalog-output", output]);

    private RegressionReport Report() => new()
    {
        Source = SnapshotContext.Read()!.Identity(), Complete = true, FinishedUtc = DateTimeOffset.UtcNow,
        Cases = _catalog.Where(item => item.Partition == _partition).Select(Result).ToList(),
    };

    private static RegressionCaseResult Result(RegressionCase item) =>
        new() { Id = item.Id, Partition = item.Partition, Outcome = "passed" };

    private string Save(RegressionReport report, string name)
    {
        var path = Path.Combine(_root, name);
        report.Create(path);
        report.Write(path);
        return path;
    }

    private Dictionary<string, byte[]> Bytes() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(_root, path), File.ReadAllBytes, StringComparer.Ordinal);

    private void Unchanged(Dictionary<string, byte[]> before)
    {
        foreach (var item in before)
            Assert.That(File.ReadAllBytes(Path.Combine(_root, item.Key)), Is.EqualTo(item.Value),
                "partition-verifier-must-not-rewrite-raw-generation-evidence");
    }

    private void RequireOwnedRoot()
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
        var name = Path.GetFileName(full);
        if (!Path.IsPathFullyQualified(_root) || full != _root ||
            !string.Equals(Path.GetDirectoryName(full), _parent, StringComparison.OrdinalIgnoreCase) ||
            !name.StartsWith(Prefix, StringComparison.Ordinal) || name.Length != Prefix.Length + 32 ||
            !Guid.TryParseExact(name[Prefix.Length..], "N", out var id) || id == Guid.Empty)
            throw new InvalidOperationException("partition-verifier-owner-invalid");
        RequireDirectoryChain(full);
    }

    private static void RequireDirectoryChain(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidOperationException("partition-verifier-directory-invalid");
    }

    private static void Note(string message)
    {
        try { TestContext.Progress.WriteLine(message); }
        catch { /* Cleanup diagnostics cannot replace the primary. */ }
    }
}
