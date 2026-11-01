using System.Globalization;
using System.IO.Compression;
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
public sealed class CoverageCiObservationTests
{
    private string _root = "";
    private string _jobs = "";
    private string _request = "";
    private string _expectation = "";
    private FullCoverageCiContext _context = null!;
    private readonly List<FullCoverageCiArtifactRequest> _artifacts = [];
    private static readonly byte[] Raw = [0, 255, 13, 10, 10, 128, 65, 0];

    [SetUp]
    public void CreatePrivateOperationArchivesAndCompleteRawJobSnapshot()
    {
        _root = Path.Combine(Path.GetTempPath(), "fa-coverage-observation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var operation = Path.Combine(_root, "run-901");
        var prepareRoot = Path.Combine(operation, "prepare", "attempt-1");
        var source = Path.Combine(prepareRoot, "source");
        Directory.CreateDirectory(Path.Combine(source, ".config"));
        Directory.CreateDirectory(Path.Combine(source, "resources", "日本語"));
        Directory.CreateDirectory(Path.Combine(source, "empty"));
        Directory.CreateDirectory(Path.Combine(prepareRoot, "preparation"));
        File.WriteAllBytes(Path.Combine(source, ".config", "support.bin"), Raw);
        File.SetAttributes(Path.Combine(source, ".config", "support.bin"), FileAttributes.Hidden);
        File.WriteAllBytes(Path.Combine(source, "resources", "日本語", "decoder.bin"), Raw.Reverse().ToArray());
        var preparedPath = Path.Combine(prepareRoot, "preparation", "prepared.json");
        File.WriteAllBytes(preparedPath, Encoding.UTF8.GetBytes("{\"fixture\":\"prepared bytes only\"}\r\n"));
        var commit = new string('a', 40);
        var tree = new string('b', 40);
        var prepared = new FullCoveragePreparedExpectation(1, preparedPath, FileSha(preparedPath), source,
            Path.Combine(prepareRoot, "snapshot-context.json"), Path.Combine(source, "tools", "FishAudio.Testing.dll"),
            Path.Combine(_root, "runtime", "dotnet.exe"), Path.Combine(_root, "runtime", "pwsh.exe"),
            Path.Combine(_root, "runtime", "dotCover.dll"));
        var preparation = new FullCoverageCiPreparationIdentity(new(commit, tree, Sha("source")), prepared, new('1', 32),
            Sha("context"), Sha("metadata-tool"), Sha("build-authority"), Sha("dotcover"), Sha("vstest"),
            "10.0.403", "Release", "net10.0-windows", CoverageInstrumentationPolicy.CurrentVersion);
        FullCoverageCiRoleExpectation[] roles = [
            new(FullCoverageCiRole.Prepare, null, "Fixture / Prepare coverage", ["Prepare immutable inputs", "Record owner"], "preparation/prepared.json"),
            new(FullCoverageCiRole.Shard, 0, "Fixture / Shard 000000", ["Collect selected owners", "Admit raw owners", "Record owner"], "shard.json")];
        _context = new(1, "owner/repository", "push", "refs/heads/main", commit, tree, "Validation", 901, 1,
            operation, Sha("pending-jobs"), preparation, 1, 4, roles);
        var selectedJobs = roles.Select((role, index) => Job(role, index == 0 ? 101 : 200)).ToArray();
        var unrelated = selectedJobs[0] with { Id = 999, Name = "build-test-package", Status = "queued", Conclusion = null,
            StartedAt = null, CompletedAt = null, RunnerId = 0, RunnerName = null, Steps = null };
        var jobs = new FullCoverageCiJobsSnapshot(_context.Repository, 901, "all", 3,
            [.. selectedJobs, unrelated], [new(1, Time(0))]);
        var jobJson = JsonSerializer.SerializeToNode(jobs, CoverageCiTransport.JsonOptions)!;
        // These are real API fields retained by Get-RegressionJobSnapshot. They
        // are metadata only; the controlled typed execution fields still bind.
        foreach (var item in jobJson["jobs"]!.AsArray())
        {
            item!["url"] = "https://api.github.com/repos/owner/repository/actions/jobs/101";
            item["html_url"] = "https://github.com/owner/repository/actions/runs/901/job/101";
            item["check_run_url"] = "https://api.github.com/repos/owner/repository/check-runs/101";
            item["workflow_name"] = "Validation";
            item["head_branch"] = "main";
            item["created_at"] = Time(0);
            item["runner_group_id"] = 1;
            item["runner_group_name"] = "Default";
            item["labels"] = new JsonArray("windows-latest");
        }
        _jobs = Path.Combine(_root, "jobs.json");
        WriteJson(_jobs, jobJson);
        _context = _context with { JobsSha256 = FileSha(_jobs) };
        for (var index = 0; index < roles.Length; index++)
        {
            var role = roles[index];
            var ownerRoot = FullCoverageCiProvenance.OriginalRootFor(_context, role.Role, role.ShardIndex, 1);
            if (role.Role == FullCoverageCiRole.Shard)
            {
                Directory.CreateDirectory(Path.Combine(ownerRoot, "raw"));
                File.WriteAllBytes(Path.Combine(ownerRoot, "shard.json"), Encoding.UTF8.GetBytes("{\"fixture\":\"partial shard bytes\"}\r\n"));
                File.WriteAllBytes(Path.Combine(ownerRoot, "raw", "coverage.dcvr"), Raw);
            }
            var key = role.Role == FullCoverageCiRole.Prepare ? "prepare" : "shard-000000";
            var archive = Path.Combine(_root, key + ".zip");
            var packed = CoverageCiTransport.Pack(new(1, ownerRoot, archive), TransportExpectation(ownerRoot));
            var inventory = Inventory(ownerRoot);
            var evidence = inventory.Files.Single(file => file.Path == role.EvidenceRelativePath);
            var owner = new FullCoverageCiOwner(1, "coverage-ci-operation-owner", role.Role, role.ShardIndex, 1,
                _context.Repository, commit, tree, _context.WorkflowName, 901, 1, selectedJobs[index].Id,
                Time(role.Role == FullCoverageCiRole.Prepare ? 2 : 4), 101, 1, preparation, ownerRoot,
                "coverage-" + key + "-" + commit + "-901-1", packed.ArchiveSha256, evidence.Sha256, inventory);
            var ownerRecord = Path.Combine(_root, key + "-owner.json");
            WriteJson(ownerRecord, owner);
            _artifacts.Add(new(ownerRecord, archive));
        }
        _request = Path.Combine(_root, "select-request.json");
        _expectation = Path.Combine(_root, "select-expected.json");
        WriteJson(_request, new FullCoverageCiSelectionRequest(1, _jobs, _artifacts));
        WriteJson(_expectation, _context);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-ci-observation/inspect-complete-bytes-with-occupied-or-absent-owner")]
    public void InspectionReturnsIndependentInventoryWithoutTouchingTheOriginalOwner()
    {
        var owner = ReadOwner(0);
        var original = Inventory(owner.OriginalRoot);
        var directories = Directory.GetDirectories(_root, "*", SearchOption.AllDirectories);
        var input = new CoverageCiTransportRestoreInput(1, _artifacts[0].ArchivePath, owner.ArchiveSha256);
        var inspected = CoverageCiTransport.Inspect(input, TransportExpectation(owner.OriginalRoot));
        AssertInventory(inspected.Manifest, original);
        Assert.That(Inventory(owner.OriginalRoot).Directories, Is.EqualTo(original.Directories));
        Assert.That(Inventory(owner.OriginalRoot).Files, Is.EqualTo(original.Files));
        Assert.That(Directory.GetDirectories(_root, "*", SearchOption.AllDirectories), Is.EquivalentTo(directories));
        Assert.That(inspected.Manifest.Files.Single(file => file.Path == "source/.config/support.bin").Sha256, Is.EqualTo(Sha(Raw)));
        Assert.That(inspected.Manifest.Directories, Does.Contain("source/empty"));
        Assert.That(inspected.Receipt.Operation, Is.EqualTo("Inspect"));
        Assert.That(inspected.Receipt.ByteTransportComplete, Is.True);
        Assert.That(inspected.Receipt.SourceAuthenticated || inspected.Receipt.BuildAuthenticated || inspected.Receipt.TestExecutionVerified ||
            inspected.Receipt.MainProvenanceVerified || inspected.Receipt.CoverageUnionComplete || inspected.Receipt.CompleteBaseline ||
            inspected.Receipt.PublicationAuthorized || inspected.Receipt.TiaSelectionAuthorized, Is.False);
        AssertOwned(owner.OriginalRoot);
        Directory.Move(owner.OriginalRoot, Path.Combine(_root, "retained-prepare"));
        directories = Directory.GetDirectories(_root, "*", SearchOption.AllDirectories);
        AssertInventory(CoverageCiTransport.Inspect(input, TransportExpectation(owner.OriginalRoot)).Manifest, original);
        Assert.That(Directory.Exists(owner.OriginalRoot), Is.False);
        Assert.That(Directory.GetDirectories(_root, "*", SearchOption.AllDirectories), Is.EquivalentTo(directories));
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-ci-observation/inspect-rejects-rehashed-bad-payload-without-stage")]
    public void MatchingContainerDigestCannotHideMalformedPayloadBytes()
    {
        var owner = ReadOwner(1);
        AssertOwned(owner.OriginalRoot);
        Directory.Move(owner.OriginalRoot, Path.Combine(_root, "retained-shard"));
        RewritePayload(_artifacts[1].ArchivePath);
        var directories = Directory.GetDirectories(_root, "*", SearchOption.AllDirectories);
        var error = Assert.Throws<CoverageCiTransportException>(() => CoverageCiTransport.Inspect(
            new(1, _artifacts[1].ArchivePath, FileSha(_artifacts[1].ArchivePath)), TransportExpectation(owner.OriginalRoot)))!;
        Assert.That(error.Code, Is.EqualTo("coverage-ci-transport-payload-bytes"));
        Assert.That(Directory.GetDirectories(_root, "*", SearchOption.AllDirectories), Is.EquivalentTo(directories));
        Assert.That(Directory.Exists(owner.OriginalRoot), Is.False);
    }

    [Test]
    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    [Property("FishAudioCaseId", "coverage-ci-observation/inspect-cli-publishes-manifest-and-byte-only-receipt")]
    public void ExactInspectCommandPublishesAnInventoryWithoutRestoration()
    {
        var owner = ReadOwner(0);
        var input = Path.Combine(_root, "inspect-request.json");
        var expected = Path.Combine(_root, "inspect-expected.json");
        var output = Path.Combine(_root, "inspected.json");
        WriteJson(input, new CoverageCiTransportRestoreInput(1, _artifacts[0].ArchivePath, owner.ArchiveSha256));
        WriteJson(expected, TransportExpectation(owner.OriginalRoot));
        var before = Inventory(owner.OriginalRoot);
        Assert.That(CoverageCiTransportCommands.IsInvocation(["coverage-ci-transport-inspect"]), Is.True);
        Assert.That(Run(() => CoverageCiTransportCommands.Run(["coverage-ci-transport-inspect", "--input", input,
            "--expectation", expected, "--output", output])), Is.Zero);
        using var receipt = JsonDocument.Parse(File.ReadAllBytes(output));
        Assert.That(receipt.RootElement.GetProperty("receipt").GetProperty("operation").GetString(), Is.EqualTo("Inspect"));
        Assert.That(receipt.RootElement.GetProperty("manifest").GetProperty("files").GetArrayLength(), Is.EqualTo(before.Files.Count));
        Assert.That(Inventory(owner.OriginalRoot).Files, Is.EqualTo(before.Files));
        using var closed = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.That(closed.Length, Is.GreaterThan(0));
    }

    [Test]
    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    [Property("FishAudioCaseId", "coverage-ci-observation/select-cli-observes-raw-files-and-real-job-metadata")]
    public void SelectionConstructsObservationsFromRealFilesRatherThanCallerDtoClaims()
    {
        var output = Path.Combine(_root, "selected.json");
        Assert.That(FullCoverageCiCommands.IsInvocation(["coverage-ci-select"]), Is.True);
        Assert.That(Select(output), Is.Zero);
        using var result = JsonDocument.Parse(File.ReadAllBytes(output));
        var root = result.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("jobsSha256").GetString(), Is.EqualTo(FileSha(_jobs)));
            Assert.That(root.GetProperty("preparation").GetProperty("ownerRecordSha256").GetString(), Is.EqualTo(FileSha(_artifacts[0].OwnerRecordPath)));
            Assert.That(root.GetProperty("shards")[0].GetProperty("archiveSha256").GetString(), Is.EqualTo(FileSha(_artifacts[1].ArchivePath)));
            Assert.That(root.GetProperty("ownerSelectionComplete").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("requiresIndependentRawReimport").GetBoolean(), Is.True);
            foreach (var name in new[] { "apiEventAuthenticated", "sourceBuildBytesVerified", "machineConcurrencyVerified",
                "mainProvenanceVerified", "rawCoverageUnionAdmitted", "completeBaseline", "publicationAuthorized", "tiaSelectionAuthorized" })
                Assert.That(root.GetProperty(name).GetBoolean(), Is.False, name);
        });
        // Inputs and final output must all be released before success returns.
        foreach (var path in new[] { _request, _expectation, _jobs, output }.Concat(_artifacts.SelectMany(item => new[] { item.OwnerRecordPath, item.ArchivePath })))
            using (var closed = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) Assert.That(closed.Length, Is.GreaterThan(0));
    }

    [Test]
    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    [Property("FishAudioCaseId", "coverage-ci-observation/reject-actual-jobs-digest-drift")]
    public void ActualJobBytesMustMatchTheIndependentCallerDigest()
    {
        var bytes = File.ReadAllBytes(_jobs);
        File.WriteAllBytes(_jobs, [.. bytes, 32]);
        var output = Path.Combine(_root, "bad-jobs.json");
        Assert.That(Select(output, "full-coverage-ci-jobs-digest"), Is.Not.Zero);
        Assert.That(File.Exists(output), Is.False);
        Assert.That(_artifacts.All(item => File.Exists(item.OwnerRecordPath) && File.Exists(item.ArchivePath)), Is.True);
    }

    [Test]
    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    [Property("FishAudioCaseId", "coverage-ci-observation/reject-actual-archive-digest-drift")]
    public void OwnerArchiveSealMustMatchTheActualDownloadedContainer()
    {
        RewritePayload(_artifacts[1].ArchivePath);
        var output = Path.Combine(_root, "bad-archive.json");
        Assert.That(Select(output, "coverage-ci-transport-archive-digest"), Is.Not.Zero);
        Assert.That(File.Exists(output), Is.False);
        Assert.That(File.Exists(_artifacts[1].ArchivePath), Is.True);
    }

    [Test]
    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    [Property("FishAudioCaseId", "coverage-ci-observation/reject-owner-seal-and-strict-json-substitution")]
    public void OwnerRecordsAndOuterRequestsCannotSupplyUnobservedEvidence()
    {
        var ownerPath = _artifacts[1].OwnerRecordPath;
        var originalOwner = File.ReadAllBytes(ownerPath);
        var originalRequest = File.ReadAllBytes(_request);
        var originalJobs = File.ReadAllBytes(_jobs);
        var originalExpected = File.ReadAllBytes(_expectation);
        var before = Inventory(ReadOwner(1).OriginalRoot);
        for (var mutation = 0; mutation < 6; mutation++)
        {
            File.WriteAllBytes(ownerPath, originalOwner);
            File.WriteAllBytes(_request, originalRequest);
            File.WriteAllBytes(_jobs, originalJobs);
            File.WriteAllBytes(_expectation, originalExpected);
            if (mutation == 0) WriteJson(ownerPath, ReadOwner(1) with { EvidenceSha256 = Sha("wrong-evidence") });
            if (mutation == 1)
            {
                var record = JsonNode.Parse(originalOwner)!;
                record["observedInventory"] = JsonSerializer.SerializeToNode(before, CoverageCiTransport.JsonOptions);
                WriteJson(ownerPath, record);
            }
            if (mutation == 2) File.WriteAllText(ownerPath, Encoding.UTF8.GetString(originalOwner).Insert(1, "\"schemaVersion\":1,"), new UTF8Encoding(false));
            if (mutation == 3)
            {
                var request = JsonNode.Parse(originalRequest)!;
                request["artifacts"]![0]!["observedArchiveSha256"] = FileSha(_artifacts[0].ArchivePath);
                WriteJson(_request, request);
            }
            if (mutation == 4)
            {
                var duplicate = Encoding.UTF8.GetString(originalJobs).Replace("\"runner_group_id\": 1",
                    "\"runner_group_id\": 1, \"runner_group_id\": 1", StringComparison.Ordinal);
                Assert.That(duplicate, Is.Not.EqualTo(Encoding.UTF8.GetString(originalJobs)));
                File.WriteAllText(_jobs, duplicate, new UTF8Encoding(false));
                WriteJson(_expectation, _context with { JobsSha256 = FileSha(_jobs) });
            }
            if (mutation == 5) WriteJson(ownerPath, ReadOwner(1) with { OriginalRoot = Path.Combine(_root, "payload-chosen-owner") });
            var output = Path.Combine(_root, "bad-owner-" + mutation.ToString(CultureInfo.InvariantCulture) + ".json");
            var reason = mutation switch {
                0 => "full-coverage-ci-operation-evidence", 2 or 4 => "coverage-ci-transport-duplicate-json-property",
                5 => "full-coverage-ci-artifact-owner", _ => "coverage-ci-transport-io-or-invalid-data" };
            Assert.That(Select(output, reason), Is.Not.Zero, "mutation " + mutation);
            Assert.That(File.Exists(output), Is.False);
        }
        Assert.That(Inventory(FullCoverageCiProvenance.OriginalRootFor(_context, FullCoverageCiRole.Shard, 0, 1)).Files, Is.EqualTo(before.Files));
        Assert.That(Directory.Exists(Path.Combine(_root, "payload-chosen-owner")), Is.False);
    }

    [Test]
    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    [Property("FishAudioCaseId", "coverage-ci-observation/selection-output-is-fresh-external-closed-and-create-once")]
    public void SelectionCannotOverwriteOrAddFilesToAnyOperationOwner()
    {
        var output = Path.Combine(_root, "once.json");
        Assert.That(Select(output), Is.Zero);
        var published = File.ReadAllBytes(output);
        Assert.That(Select(output, "coverage-ci-transport-destination-occupied"), Is.Not.Zero);
        Assert.That(File.ReadAllBytes(output), Is.EqualTo(published));
        var inside = Path.Combine(ReadOwner(1).OriginalRoot, "selected.json");
        Assert.That(Select(inside, "coverage-ci-transport-output-inside-owner-or-source"), Is.Not.Zero);
        Assert.That(File.Exists(inside), Is.False);
        Assert.That(Directory.GetFiles(_root, "*.pending-*", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-ci-observation/input-release-failure-preserves-primary-and-blocks-success")]
    public void GuardReleaseCannotReplaceThePrimaryFaultOrReturnASuccessValue()
    {
        var primary = new CoverageCiTransportException("fixture-primary");
        var thrown = Assert.Throws<CoverageCiTransportException>(() => CoverageCiTransport.Held<int>(guards =>
        {
            guards.Add(new FailingRelease());
            throw primary;
        }));
        Assert.That(thrown, Is.SameAs(primary));
        Assert.That(Assert.Throws<CoverageCiTransportException>(() => CoverageCiTransport.Held(guards =>
        {
            guards.Add(new FailingRelease());
            return 42;
        }))!.Code, Is.EqualTo("coverage-ci-transport-input-release-failed"));
    }

    private sealed class FailingRelease : IDisposable { public void Dispose() => throw new IOException("fixture-release"); }
    private CoverageCiTransportExpectation TransportExpectation(string root) => new(1, root, _context.Preparation.Prepared.SourceDirectory, _context.Tree);
    private FullCoverageCiOwner ReadOwner(int index) => JsonSerializer.Deserialize<FullCoverageCiOwner>(
        File.ReadAllBytes(_artifacts[index].OwnerRecordPath), CoverageCiTransport.JsonOptions)!;
    private int Select(string output, string? expectedFailure = null) => Run(() => FullCoverageCiCommands.Run(["coverage-ci-select", "--input", _request,
        "--expectation", _expectation, "--output", output]), expectedFailure);
    private static int Run(Func<int> operation, string? expectedFailure = null)
    {
        var priorOut = Console.Out;
        var priorError = Console.Error;
        using var messages = new StringWriter();
        int exitCode;
        try { Console.SetOut(messages); Console.SetError(messages); exitCode = operation(); }
        finally { Console.SetOut(priorOut); Console.SetError(priorError); }
        if (expectedFailure is not null) Assert.That(messages.ToString(), Does.Contain(expectedFailure));
        return exitCode;
    }
    private FullCoverageCiJob Job(FullCoverageCiRoleExpectation role, long id)
    {
        var start = Time(role.Role == FullCoverageCiRole.Prepare ? 1 : 3);
        var end = Time(role.Role == FullCoverageCiRole.Prepare ? 2 : 4);
        return new(id, 901, 1, role.JobName, _context.Commit, "completed", "success", start, end, id,
            "fixture-runner-" + id.ToString(CultureInfo.InvariantCulture), role.RequiredSteps.Select((name, index) =>
                new FullCoverageCiStep(index + 1, name, "completed", "success", start, end)).ToArray());
    }
    private static FullCoverageCiInventory Inventory(string root) => new(
        Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray(),
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(path => new FullCoverageCiFile(
            Path.GetRelativePath(root, path).Replace('\\', '/'), new FileInfo(path).Length, FileSha(path))).OrderBy(file => file.Path, StringComparer.Ordinal).ToArray());
    private static void AssertInventory(CoverageCiTransportManifest manifest, FullCoverageCiInventory expected)
    {
        Assert.That(manifest.Directories, Is.EqualTo(expected.Directories));
        Assert.That(manifest.Files.Select(file => new FullCoverageCiFile(file.Path, file.Length, file.Sha256)), Is.EqualTo(expected.Files));
    }
    private void RewritePayload(string archive)
    {
        AssertOwned(archive);
        List<(string Name, int Attributes, byte[] Bytes)> entries;
        using (var zip = ZipFile.OpenRead(archive))
            entries = zip.Entries.Select(entry => { using var bytes = new MemoryStream(); using var stream = entry.Open();
                stream.CopyTo(bytes); return (entry.FullName, entry.ExternalAttributes, bytes.ToArray()); }).ToList();
        var changed = false;
        var rewritten = archive + ".changed";
        using (var zip = ZipFile.Open(rewritten, ZipArchiveMode.Create))
            foreach (var item in entries)
            {
                var bytes = item.Bytes;
                if (!changed && item.Name.StartsWith("payload/", StringComparison.Ordinal) && bytes.Length > 0)
                { bytes = bytes.Select(value => (byte)(value ^ 1)).ToArray(); changed = true; }
                var entry = zip.CreateEntry(item.Name, CompressionLevel.NoCompression);
                entry.ExternalAttributes = item.Attributes;
                using var stream = entry.Open(); stream.Write(bytes);
            }
        Assert.That(changed, Is.True);
        AssertOwned(rewritten);
        File.Move(rewritten, archive, true);
    }
    private static void WriteJson<T>(string path, T value) => File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(value, CoverageCiTransport.JsonOptions));
    private static string FileSha(string path) => Sha(File.ReadAllBytes(path));
    private static string Sha(string text) => Sha(Encoding.UTF8.GetBytes(text));
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Time(int minute) => new DateTimeOffset(2026, 10, 31, 0, minute, 0, TimeSpan.Zero).ToString("O", CultureInfo.InvariantCulture);
    private void AssertOwned(string target)
    {
        var root = Path.GetFullPath(_root);
        const string prefix = "fa-coverage-observation-";
        if (Path.GetDirectoryName(root) != Path.TrimEndingDirectorySeparator(Path.GetTempPath()) ||
            !Path.GetFileName(root).StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(Path.GetFileName(root)[prefix.Length..], "N", out _) ||
            (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0 ||
            !Path.GetFullPath(target).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("coverage-observation-fixture-ownership");
        var current = Path.GetFullPath(target);
        while (current != root)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("coverage-observation-fixture-reparse");
            current = Path.GetDirectoryName(current)!;
        }
    }
    [TearDown]
    public void ReleasePrivateFixture()
    {
        if (_root.Length == 0 || !Directory.Exists(_root)) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            AssertOwned(Path.Combine(_root, "ownership-check"));
            if (failed) { TestContext.Error.WriteLine("coverage-observation: failed fixture retained as " + Path.GetFileName(_root)); return; }
            Directory.Delete(_root, true);
        }
        catch (Exception error) when (failed) { TestContext.Error.WriteLine("coverage-observation: cleanup failed (" + error.GetType().Name + ")."); }
    }
}
