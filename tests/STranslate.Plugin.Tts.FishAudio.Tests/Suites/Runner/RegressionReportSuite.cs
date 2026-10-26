using static TestAssertions;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class RegressionReportSuite
{
    internal static async Task RunAsync()
    {
        HeldCanonicalCannotFailDurableUpdatesOrRepeatActions();
        InitialOwnershipAndAuthoritativeFailuresRemainStrict();
        await ConcurrentReadersCannotBlockImmutableUpdatesAsync();
        await StaleProjectionReplaysOnlyLatestIncompleteCoverageAsync();
        CanonicalAndLatestGenerationTamperingCannotAuthorizeReuse();
        UnreadableOrMissingCanonicalRecoveryIsConservative();
        BareSchemaOneReportsRemainCompatible();
    }

    private static void HeldCanonicalCannotFailDurableUpdatesOrRepeatActions()
    {
        using var fixture = new ReportFixture();
        var report = fixture.Report();
        report.Write(fixture.Path);
        var original = File.ReadAllBytes(fixture.Path);
        var calls = 0;
        using var held = new FileStream(fixture.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        for (var index = 0; index < 16; index++)
        {
            report.Cases[0].Outcome = "running";
            report.Write(fixture.Path);
            calls++;
            report.Cases[0].Outcome = "passed";
            report.Cases[0].ElapsedMilliseconds = index + 1;
            report.Write(fixture.Path);
        }
        report.Complete = true;
        report.FinishedUtc = DateTimeOffset.UtcNow;
        report.Write(fixture.Path);
        var latest = RegressionReport.Read(fixture.Path);
        AssertEqual(16, calls, "A held canonical reader cannot repeat or retry a delegate");
        AssertEqual(true, latest.Complete, "The latest durable final generation remains readable while the projection is held");
        AssertEqual(report.Generation, latest.Generation, "The reader resolves the highest fully committed generation");
        AssertEqual(true, report.ProjectionExceptionType is not null && report.ProjectionExceptionHResult is not null, "Projection faults retain safe type and HResult metadata");
        AssertEqual("passed", latest.Cases[0].Outcome, "The latest generation retains actual action outcomes");
        AssertEqual(true, File.ReadAllBytes(fixture.Path).SequenceEqual(original), "A failed canonical projection leaves the original visible bytes unchanged");
    }

    private static void InitialOwnershipAndAuthoritativeFailuresRemainStrict()
    {
        using var fixture = new ReportFixture();
        var report = fixture.Report();
        report.Create(fixture.Path);
        AssertEqual(0L, RegressionReport.Read(fixture.Path).Generation, "The initial report is valid generation zero before any action");
        AssertEqual(true, File.Exists(RegressionReport.GenerationPath(fixture.Path, report.RunId, 0)), "Generation zero is durable before execution");
        var bytes = File.ReadAllBytes(fixture.Path);
        ExpectRejected(() => fixture.Report().Create(fixture.Path), "An existing canonical owner cannot be overwritten");
        Directory.CreateDirectory(RegressionReport.GenerationPath(fixture.Path, report.RunId, 1));
        report.Cases[0].Outcome = "passed";
        var failed = false;
        try { report.Write(fixture.Path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failed = true; }
        AssertEqual(true, failed, "A failed authoritative immutable write remains a failure");
        AssertEqual(0L, report.Generation, "A failed publication cannot advance the in-memory durable generation");
        AssertEqual(true, File.ReadAllBytes(fixture.Path).SequenceEqual(bytes), "Authoritative failure cannot project an undurable pass");

        using var orphan = new ReportFixture();
        Directory.CreateDirectory(RegressionReport.GenerationsRoot(orphan.Path));
        ExpectRejected(() => orphan.Report().Create(orphan.Path), "An existing sidecar owner cannot be adopted by another invocation");
        AssertEqual(false, File.Exists(orphan.Path), "Sidecar ownership is checked before creating a canonical report");
    }

    private static async Task ConcurrentReadersCannotBlockImmutableUpdatesAsync()
    {
        using var fixture = new ReportFixture();
        var report = fixture.Report();
        report.Create(fixture.Path);
        using var held = new FileStream(fixture.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var stopped = 0;
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = Task.Run(() =>
        {
            var previous = -1L;
            while (Volatile.Read(ref stopped) == 0)
            {
                var current = RegressionReport.Read(fixture.Path);
                AssertEqual(true, current.Generation >= previous, "Immutable generation reads cannot move backwards");
                previous = current.Generation;
                observed.TrySetResult();
            }
        });
        try
        {
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var index = 0; index < 64; index++)
            {
                report.Cases[0].ElapsedMilliseconds = index;
                report.Write(fixture.Path);
            }
            report.Cases[0].Outcome = "passed";
            report.Complete = true;
            report.FinishedUtc = DateTimeOffset.UtcNow;
            report.Write(fixture.Path);
        }
        finally { Volatile.Write(ref stopped, 1); await reader.WaitAsync(TimeSpan.FromSeconds(5)); }
        AssertEqual(true, RegressionReport.Read(fixture.Path).Complete, "Concurrent readers cannot fail or lose a final durable report");
    }

    private static async Task StaleProjectionReplaysOnlyLatestIncompleteCoverageAsync()
    {
        using var fixture = new ReportFixture();
        var calls = new List<string>();
        RegressionCase[] registry = [new("runner.report/pass", "core", () => { calls.Add("pass"); return Task.CompletedTask; }),
            new("runner.report/fail", "core", () => { calls.Add("fail"); return Task.CompletedTask; }),
            new("runner.report/not-run", "core", () => { calls.Add("not-run"); return Task.CompletedTask; })];
        var source = await RegressionSource.CaptureAsync();
        source.RecordDirtyScope(registry);
        var report = new RegressionReport { Source = source.Identity, Cases = registry.Select(item => new RegressionCaseResult { Id = item.Id, Partition = item.Partition }).ToList() };
        report.Create(fixture.Path);
        var canonical = File.ReadAllBytes(fixture.Path);
        using var held = new FileStream(fixture.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        report.Cases[0].Outcome = "passed";
        report.Cases[1].Outcome = "failed";
        report.Cases[1].ExceptionType = typeof(IOException).FullName;
        report.Cases[1].FailureStage = "case";
        report.FinishedUtc = DateTimeOffset.UtcNow;
        report.Write(fixture.Path);
        var generationPath = RegressionReport.GenerationPath(fixture.Path, report.RunId, report.Generation);
        var durable = File.ReadAllBytes(generationPath);
        var replay = fixture.Path + ".replay.json";
        AssertEqual(0, await RegressionRunner.RunAsync(["--rerun-failed", fixture.Path, "--results", replay], registry), "Stale canonical projection replay succeeds from authoritative outcomes");
        AssertEqual(true, calls.SequenceEqual(["fail", "not-run"]), "Replay retains unchanged confirmed passes and executes only latest incomplete coverage");
        AssertEqual(true, File.ReadAllBytes(fixture.Path).SequenceEqual(canonical), "Replay cannot rewrite stale input canonical bytes");
        AssertEqual(true, File.ReadAllBytes(generationPath).SequenceEqual(durable), "Replay cannot rewrite its immutable input evidence");
    }

    private static void CanonicalAndLatestGenerationTamperingCannotAuthorizeReuse()
    {
        foreach (var change in new Action<JsonNode>[]
        {
            item => item["source"]!["fingerprint"] = new string('0', 64),
            item => item["cases"]![0]!["outcome"] = "failed",
            item => item["runId"] = Guid.NewGuid().ToString("N"),
            item => item["generation"] = 999999,
            item => item["generation"] = -1
        })
        {
            using var fixture = new ReportFixture();
            var report = fixture.Report();
            report.Create(fixture.Path);
            report.Cases[0].Outcome = "passed";
            report.Complete = true;
            report.Write(fixture.Path);
            var node = JsonNode.Parse(File.ReadAllBytes(fixture.Path))!;
            change(node);
            File.WriteAllText(fixture.Path, node.ToJsonString());
            ExpectRejected(() => RegressionReport.Read(fixture.Path), "A readable canonical source/outcome/run/generation modification cannot be replaced silently by sidecar coverage");
        }
        foreach (var corrupt in new[] { "malformed", "generation", "source" })
        {
            using var fixture = new ReportFixture();
            var report = fixture.Report();
            report.Create(fixture.Path);
            using var held = new FileStream(fixture.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            report.Write(fixture.Path);
            var path = RegressionReport.GenerationPath(fixture.Path, report.RunId, report.Generation);
            if (corrupt == "malformed") File.WriteAllText(path, "{");
            else
            {
                var node = JsonNode.Parse(File.ReadAllBytes(path))!;
                if (corrupt == "generation") node["generation"] = report.Generation + 1;
                else node["source"]!["fingerprint"] = new string('0', 64);
                File.WriteAllText(path, node.ToJsonString());
            }
            ExpectRejected(() => RegressionReport.Read(fixture.Path), "A malformed or mismatched highest committed generation cannot fall back to an earlier apparent success");
        }
    }

    private static void UnreadableOrMissingCanonicalRecoveryIsConservative()
    {
        using var fixture = new ReportFixture();
        var report = fixture.Report();
        report.Create(fixture.Path);
        report.Cases[0].Outcome = "passed";
        report.Complete = true;
        report.Write(fixture.Path);
        var canonical = File.ReadAllBytes(fixture.Path);
        var durablePath = RegressionReport.GenerationPath(fixture.Path, report.RunId, report.Generation);
        var durable = File.ReadAllBytes(durablePath);
        using (var held = new FileStream(fixture.Path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var recovered = RegressionReport.Read(fixture.Path);
            AssertEqual(false, recovered.Source.Available, "An unreadable canonical cannot authenticate selective pass reuse");
            AssertEqual("passed", recovered.Cases[0].Outcome, "Conservative recovery preserves durable raw outcomes");
        }
        AssertEqual(true, File.ReadAllBytes(fixture.Path).SequenceEqual(canonical), "Sharing recovery cannot alter canonical evidence");
        AssertEqual(true, File.ReadAllBytes(durablePath).SequenceEqual(durable), "Unavailable source marking cannot modify immutable evidence");
        File.Delete(fixture.Path);
        AssertEqual(false, RegressionReport.Read(fixture.Path).Source.Available, "A missing projection recovers only conservative coverage from its unique validated owner");
        Directory.CreateDirectory(RegressionReport.RunDirectory(fixture.Path, Guid.NewGuid().ToString("N")));
        ExpectRejected(() => RegressionReport.Read(fixture.Path), "An unreadable canonical cannot choose between multiple sidecar owners");
    }

    private static void BareSchemaOneReportsRemainCompatible()
    {
        using var fixture = new ReportFixture();
        var report = fixture.Report();
        var node = JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(report, RegressionReport.JsonOptions))!;
        node.AsObject().Remove("generation");
        File.WriteAllText(fixture.Path, node.ToJsonString());
        var bare = RegressionReport.Read(fixture.Path);
        AssertEqual(0L, bare.Generation, "Older schema-one reports default to generation zero");
        AssertEqual(true, bare.Source.Available, "A standalone old/copied report retains its original source-recovery contract");
    }

    private static void ExpectRejected(Action action, string message)
    {
        try { action(); }
        catch (Exception error) when (error is RegressionSelectionException or JsonException) { return; }
        throw new InvalidOperationException(message);
    }

    private sealed class ReportFixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fishaudio-report-" + Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(_directory, "report.json");
        internal ReportFixture() => Directory.CreateDirectory(_directory);
        internal RegressionReport Report() => new()
        {
            Source = new() { Available = true, Fingerprint = new string('a', 64), Commit = new string('b', 40), Tree = new string('c', 40) },
            Cases = [new() { Id = "runner.report/sample", Partition = "core" }]
        };
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
