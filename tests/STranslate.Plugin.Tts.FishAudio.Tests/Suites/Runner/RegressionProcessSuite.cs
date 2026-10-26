using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using static TestAssertions;

internal static class RegressionProcessSuite
{
    internal static async Task RunAsync()
    {
        WarningAndHardDeadlineUseTheOriginalQueuedClock();
        SeparateRootsAndStartupDoNotBecomeAnAggregateDeadline();
        await ActionContextSurvivesAwaitAndNestedOffStaDispatchAsync();
        StrictEvidenceRejectsForeignIncompleteAndReorderedManifests();
        AtomicEvidenceReplacementPreservesAnOpenReaderSnapshot();
        await ConcurrentImmutableSnapshotsNeverCompeteWithTheWriterAsync();
        await ChildCompletionNeverReplacesAnActiveManifestAsync();
        await ChildPublicationFailurePreservesTheOwnedFailureAsync();
        await FakeTransportStopsOnlyTheFailedPartitionAsync();
        await FakeTransportCannotRehabilitateTheSameLateActionAsync();
        await FakeTransportRejectsUnconfirmedPassesAsync();
        await ExitCannotSkipCompletionFramesQueuedByTheFinalDrainAsync();
        await PipeCleanupCannotReplaceADurableCaseFailureAsync();
        await BlockedProgressCannotBlockSupervisionAndRetiresQueuedWritesAsync();
        await ListingNeverStartsAProcessAsync();
        await RealChildrenDrainBothPipesAndRequireFinalEvidenceAsync();
        await RealChildHardExpiryKillsItsDescendantAsync();
        await LegacyRequestsRetainPrivateArgumentsAndUseTheSameHostAsync();
    }

    private static void WarningAndHardDeadlineUseTheOriginalQueuedClock()
    {
        var request = Request(["runner.probe/action"]);
        var clock = new ManualStreamingTimeProvider();
        var watchdog = new RegressionProcessWatchdog(request, clock.GetTimestamp(), clock.TimestampFrequency);
        Observe(watchdog, request, clock, "ready");
        Observe(watchdog, request, clock, "case-start", request.CaseIds[0]);
        var queued = clock.GetTimestamp();
        Observe(watchdog, request, clock, "queued", request.CaseIds[0], 1, queued);
        clock.Advance(TimeSpan.FromSeconds(19.999));
        watchdog.Tick(clock.GetTimestamp());
        AssertEqual(0, watchdog.WarningCount, "The queued action has no premature warning");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        watchdog.Tick(clock.GetTimestamp());
        watchdog.Tick(clock.GetTimestamp());
        AssertEqual(1, watchdog.WarningCount, "The twenty-second warning is emitted once");
        clock.Advance(TimeSpan.FromSeconds(60));
        Observe(watchdog, request, clock, "entered", request.CaseIds[0], 1, queued);
        AssertEqual<long?>(80_000, watchdog.EntryElapsedMilliseconds, "Queue delay remains distinguishable from callback execution");
        clock.Advance(TimeSpan.FromSeconds(39.999));
        watchdog.Tick(clock.GetTimestamp());
        AssertEqual<string?>(null, watchdog.FailureStage, "Entry never shortens the original action allowance");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        watchdog.Tick(clock.GetTimestamp());
        AssertEqual("action", watchdog.FailureStage, "Entry never renews the original 120-second queued deadline");
        clock.Advance(TimeSpan.FromSeconds(1));
        Observe(watchdog, request, clock, "completed", request.CaseIds[0], 1, queued);
        AssertEqual("action", watchdog.FailureStage, "A finite late callback and its cleanup remain failed");
    }

    private static void SeparateRootsAndStartupDoNotBecomeAnAggregateDeadline()
    {
        var request = Request(["runner.probe/aggregate"]);
        var clock = new ManualStreamingTimeProvider();
        var watchdog = new RegressionProcessWatchdog(request, 0, clock.TimestampFrequency);
        clock.Advance(TimeSpan.FromSeconds(119));
        Observe(watchdog, request, clock, "ready");
        Observe(watchdog, request, clock, "case-start", request.CaseIds[0]);
        Observe(watchdog, request, clock, "dispatcher-starting", request.CaseIds[0]);
        clock.Advance(TimeSpan.FromSeconds(119));
        Observe(watchdog, request, clock, "dispatcher-ready", request.CaseIds[0]);
        for (var ordinal = 1; ordinal <= 2; ordinal++)
        {
            var queued = clock.GetTimestamp();
            Observe(watchdog, request, clock, "queued", request.CaseIds[0], ordinal, queued);
            Observe(watchdog, request, clock, "entered", request.CaseIds[0], ordinal, queued);
            clock.Advance(TimeSpan.FromSeconds(70));
            Observe(watchdog, request, clock, "completed", request.CaseIds[0], ordinal, queued);
        }
        Observe(watchdog, request, clock, "case-finished", request.CaseIds[0]);
        Observe(watchdog, request, clock, "final");
        AssertEqual<string?>(null, watchdog.FailureStage, "Two separately owned seventy-second roots pass inside one long owner");
        AssertEqual(2, watchdog.WarningCount, "Each independent slow action has its own warning");
        AssertEqual(true, watchdog.Final, "Separate child and dispatcher startup are explicit bounded phases");

        var idle = new RegressionProcessWatchdog(request, clock.GetTimestamp(), clock.TimestampFrequency);
        Observe(idle, request, clock, "ready");
        Observe(idle, request, clock, "case-start", request.CaseIds[0]);
        clock.Advance(TimeSpan.FromSeconds(120));
        idle.Tick(clock.GetTimestamp());
        AssertEqual("gap", idle.FailureStage, "Non-STA/core work and idle gaps retain a finite bound");

        var core = new RegressionProcessWatchdog(request, clock.GetTimestamp(), clock.TimestampFrequency);
        Observe(core, request, clock, "ready");
        Observe(core, request, clock, "case-start", request.CaseIds[0]);
        clock.Advance(TimeSpan.FromSeconds(21));
        core.Tick(clock.GetTimestamp());
        AssertEqual(1, core.WarningCount, "A finite slow non-STA/core owner also gets the twenty-second warning");
        Observe(core, request, clock, "case-finished", request.CaseIds[0]);
        Observe(core, request, clock, "final");
        AssertEqual<string?>(null, core.FailureStage, "The warning does not fail a finite 21-second core case");
    }

    private static async Task ActionContextSurvivesAwaitAndNestedOffStaDispatchAsync()
    {
        var events = new List<RegressionActionEvent>();
        using var owner = RegressionActionScope.BeginCase("runner.probe/context", events.Add);
        var root = RegressionActionScope.QueueRoot()!;
        using (root.Enter())
        {
            await Task.Run(async () =>
            {
                await Task.Yield();
                AssertEqual(true, RegressionActionScope.QueueRoot() is null, "Off-STA async descendants reuse the live root");
            }).ConfigureAwait(false);
            AssertEqual(false, events.Any(item => item.Kind == "completed"), "An outer waiter has no completion authority");
            root.Complete();
        }
        var next = RegressionActionScope.QueueRoot()!;
        AssertEqual(2, next.Ordinal, "Only a separately owned root gets the next ordinal");
        using (next.Enter()) { next.Complete(); }
        AssertEqual(true, events.Select(item => item.Kind).SequenceEqual(["queued", "entered", "completed", "queued", "entered", "completed"]), "Only actual callback completion retires each root");
        AssertEqual(true, events.Take(3).All(item => item.QueuedTimestamp == root.QueuedTimestamp), "The queued timestamp is immutable across async execution");

        using var physical = new StringWriter();
        using var replacement = new StringWriter();
        var oldOutput = Console.Out;
        try
        {
            var protocol = new RegressionProtocolWriter(physical, Request(["runner.probe/context"]));
            Console.SetOut(replacement);
            protocol.Publish("ready");
            AssertEqual(true, physical.ToString().StartsWith(RegressionProcessProtocol.Prefix, StringComparison.Ordinal), "Protocol records use the injected physical writer");
            AssertEqual("", replacement.ToString(), "Ambient Console redirection cannot swallow protocol evidence");
        }
        finally { Console.SetOut(oldOutput); }
    }

    private static void StrictEvidenceRejectsForeignIncompleteAndReorderedManifests()
    {
        var request = Request(["runner.probe/first", "runner.probe/second"]);
        foreach (var mutate in new Action<RegressionChildManifest>[]
        {
            item => item.RequestId = Guid.NewGuid().ToString("N"),
            item => item.RunId = Guid.NewGuid().ToString("N"),
            item => item.Source = new RegressionSourceIdentity { Fingerprint = "foreign" },
            item => item.Configuration = "foreign",
            item => item.Sdk = "foreign",
            item => item.Partition = "native-cards",
            item => item.Cases.Reverse(),
            item => item.Cases[0].Partition = "native-cards",
            item => item.Cases[0].Outcome = "running",
            item => item.Cases[0].ExceptionType = "private/path",
            item => item.Complete = false,
        })
        {
            var manifest = PassedManifest(request);
            mutate(manifest);
            AssertThrows<InvalidDataException>(() => RegressionProcessProtocol.ValidateManifest(manifest, request), "Foreign or inconsistent child evidence never confirms success");
        }
        var valid = PassedManifest(request);
        RegressionProcessProtocol.ValidateManifest(valid, request);
        AssertEqual(true, valid.Complete, "Exact ordered complete evidence remains valid");
    }

    private static async Task ChildCompletionNeverReplacesAnActiveManifestAsync()
    {
        using var fixture = new ProcessFixture();
        var request = Request(["runner.probe/first", "runner.probe/second"]);
        request.ManifestPath = fixture.Path + ".child.json";
        var calls = new List<int>();
        var records = new List<RegressionProcessEvent>();
        FileStream? canonicalReader = null;
        FileStream? completedReader = null;
        byte[]? completedBytes = null;
        using var output = new ProtocolObserver(line =>
        {
            var record = JsonSerializer.Deserialize<RegressionProcessEvent>(line[RegressionProcessProtocol.Prefix.Length..], RegressionProcessProtocol.Options)!;
            records.Add(record);
            if (record.Kind == "ready" && File.Exists(request.ManifestPath))
                canonicalReader = new FileStream(request.ManifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (record.Kind == "case-finished" && record.Snapshot == 1)
            {
                var path = RegressionProcessProtocol.SnapshotPath(request, 1);
                completedBytes = File.ReadAllBytes(path);
                completedReader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
        });
        try
        {
            var exit = await RegressionChildHost.RunCasesAsync(request, output, index =>
            {
                calls.Add(index);
                AssertEqual(false, File.Exists(request.ManifestPath), "An active child exposes completion snapshots without a mutable canonical manifest");
                return Task.CompletedTask;
            });
            AssertEqual(0, exit, "Holding a completed snapshot cannot fail a finite child");
            AssertEqual(true, calls.SequenceEqual([0, 1]), "Each selected delegate executes exactly once");
            AssertEqual(true, records.Select(item => item.Kind).SequenceEqual(["ready", "case-start", "case-finished", "case-start", "case-finished", "final"]), "Completion publication remains ordered and complete");
            AssertEqual(true, completedBytes!.SequenceEqual(File.ReadAllBytes(RegressionProcessProtocol.SnapshotPath(request, 1))), "Later completion and final publication never replace earlier evidence");
            var canonical = RegressionProcessProtocol.Read<RegressionChildManifest>(request.ManifestPath);
            RegressionProcessProtocol.ValidateManifest(canonical, request);
            AssertEqual(true, canonical.Final && canonical.Complete, "The canonical manifest is published once with the complete final state");
            AssertEqual(true, File.ReadAllBytes(request.ManifestPath).SequenceEqual(File.ReadAllBytes(RegressionProcessProtocol.SnapshotPath(request, 3))), "Canonical final bytes match the immutable final snapshot");
            AssertThrows<IOException>(() => RegressionProcessProtocol.WriteNew(request.ManifestPath, RegressionChildManifest.Create(request)), "A second owner cannot replace the published final manifest");
            AssertEqual(true, canonical.Complete && File.ReadAllBytes(request.ManifestPath).SequenceEqual(File.ReadAllBytes(RegressionProcessProtocol.SnapshotPath(request, 3))), "Rejected duplicate publication preserves original final bytes");
        }
        finally { canonicalReader?.Dispose(); completedReader?.Dispose(); }
    }

    private static async Task FakeTransportStopsOnlyTheFailedPartitionAsync()
    {
        using var fixture = new ProcessFixture();
        var cases = new[]
        {
            new RegressionCase("runner.probe/core-fail", "core", () => throw new Exception("Parent must never invoke selected delegates")),
            new RegressionCase("runner.probe/core-not-run", "core", () => Task.CompletedTask),
            new RegressionCase("runner.probe/settings", "native-settings", () => Task.CompletedTask),
            new RegressionCase("runner.probe/input", "native-input", () => Task.CompletedTask),
            new RegressionCase("runner.probe/popups", "native-popups", () => Task.CompletedTask),
            new RegressionCase("runner.probe/cards", "native-cards", () => Task.CompletedTask)
        };
        var report = fixture.Report(cases);
        var launches = new List<string>();
        var children = new List<FakeChild>();
        var host = new RegressionProcessHost(info =>
        {
            var request = RegressionProcessProtocol.Read<RegressionChildRequest>(info.ArgumentList[^1]);
            launches.Add(request.Partition);
            var manifest = PassedManifest(request);
            if (request.Partition == "core")
            {
                manifest.Complete = false;
                manifest.Cases[0].Outcome = "failed";
                manifest.Cases[0].ExceptionType = typeof(TimeoutException).FullName;
                manifest.Cases[0].FailureStage = "case";
                manifest.Cases[1].Outcome = "not-run";
            }
            RegressionProcessProtocol.Write(request.ManifestPath, manifest);
            var child = FakeChild.ForManifest(request, manifest);
            children.Add(child);
            return child;
        });
        await host.ExecuteAsync(cases, report, fixture.Path);
        AssertEqual(true, launches.SequenceEqual(RegressionProcessProtocol.Partitions), "All five partitions, including core, launch serially in fixed order");
        AssertEqual(true, report.Cases.Select(item => item.Outcome).SequenceEqual(["failed", "not-run", "passed", "passed", "passed", "passed"]), "One failed partition stops locally while independent partitions continue");
        AssertEqual(typeof(TimeoutException).FullName, report.Cases[0].ExceptionType, "An action-owned timeout retains its original type");
        AssertEqual("case", report.Cases[0].FailureStage, "Action-owned timeouts remain ordinary case failures");
        AssertEqual(false, children.Any(item => item.Killed), "Ordinary fixture failures never trigger the hard kill policy");
    }

    private static async Task ChildPublicationFailurePreservesTheOwnedFailureAsync()
    {
        foreach (var scenario in new[] { "completion-event", "final-publication", "untyped-pass" })
        {
            using var fixture = new ProcessFixture();
            RegressionCase[] cases = [new("runner.probe/owned-failure", "core", () => Task.CompletedTask),
                new("runner.probe/not-run", "core", () => Task.CompletedTask)];
            var report = fixture.Report(cases);
            var calls = 0;
            using var host = new RegressionProcessHost(info =>
            {
                var request = RegressionProcessProtocol.Read<RegressionChildRequest>(info.ArgumentList[^1]);
                using var output = new ProtocolObserver(line =>
                {
                    var record = JsonSerializer.Deserialize<RegressionProcessEvent>(line[RegressionProcessProtocol.Prefix.Length..], RegressionProcessProtocol.Options)!;
                    if (record.Kind != "case-finished") return;
                    if (scenario != "final-publication") throw new IOException("controlled-completion-pipe-failure");
                    Directory.CreateDirectory(request.ManifestPath);
                });
                try
                {
                    RegressionChildHost.RunCasesAsync(request, output, _ =>
                    {
                        calls++;
                        return scenario == "untyped-pass" ? Task.CompletedTask : Task.FromException(new InvalidOperationException("controlled-original-assertion"));
                    }).GetAwaiter().GetResult();
                    throw new InvalidOperationException("The controlled publication boundary must fail");
                }
                catch (IOException)
                {
                    return new FakeChild(output.ToString(), 1, new StringReader("Regression execution failed (IOException).\n"));
                }
            });
            await host.ExecuteAsync(cases, report, fixture.Path);
            AssertEqual(1, calls, "A publication failure never retries the failed delegate or starts a later one");
            AssertEqual(true, report.Cases.Select(item => item.Outcome).SequenceEqual(["failed", "not-run"]), "Only the durable failed owner is recovered without new pass coverage");
            AssertEqual(nameof(IOException), report.Cases[0].ChildInfrastructureExceptionType, "The child publication I/O failure remains safe secondary evidence");
            if (scenario == "untyped-pass")
                AssertEqual("child-evidence", report.Cases[0].FailureStage, "An untyped passed snapshot cannot turn missing completion evidence green");
            else
            {
                AssertEqual(typeof(InvalidOperationException).FullName, report.Cases[0].ExceptionType, "The immutable completed failure remains primary when publication fails");
                AssertEqual("case", report.Cases[0].FailureStage, "The original owned failure keeps its case stage");
                AssertEqual(true, report.Cases[0].SecondaryExceptionType is not null, "Parent evidence failure cannot replace the original owned failure");
            }
        }
    }

    private static void AtomicEvidenceReplacementPreservesAnOpenReaderSnapshot()
    {
        using var fixture = new ProcessFixture();
        var request = Request(["runner.probe/replacement"]);
        request.ManifestPath = fixture.Path + ".child.json";
        var original = RegressionChildManifest.Create(request);
        var oldPath = RegressionProcessProtocol.SnapshotPath(request, 1);
        var nextPath = RegressionProcessProtocol.SnapshotPath(request, 2);
        RegressionProcessProtocol.Write(oldPath, original);
        using var reader = new FileStream(oldPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        RegressionProcessProtocol.Write(nextPath, PassedManifest(request));
        var oldSnapshot = JsonSerializer.Deserialize<RegressionChildManifest>(reader, RegressionProcessProtocol.Options)!;
        var current = RegressionProcessProtocol.Read<RegressionChildManifest>(nextPath);
        AssertEqual(false, oldSnapshot.Final, "A live reader retains its original complete snapshot during replacement");
        AssertEqual(true, current.Complete, "Even a reader without share-delete cannot prevent a separately owned next snapshot");
    }

    private static async Task ConcurrentImmutableSnapshotsNeverCompeteWithTheWriterAsync()
    {
        using var fixture = new ProcessFixture();
        var request = Request(["runner.probe/concurrent-snapshots"]);
        request.ManifestPath = fixture.Path + ".child.json";
        var published = 1;
        var stopped = 0;
        RegressionProcessProtocol.Write(RegressionProcessProtocol.SnapshotPath(request, 1), PassedManifest(request));
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = Task.Run(() =>
        {
            while (Volatile.Read(ref stopped) == 0)
            {
                var snapshot = RegressionProcessProtocol.Read<RegressionChildManifest>(RegressionProcessProtocol.SnapshotPath(request, Volatile.Read(ref published)));
                RegressionProcessProtocol.ValidateManifest(snapshot, request);
                read.TrySetResult();
            }
        });
        try
        {
            await read.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var index = 2; index <= 64; index++)
            {
                RegressionProcessProtocol.Write(RegressionProcessProtocol.SnapshotPath(request, index), PassedManifest(request));
                Volatile.Write(ref published, index);
            }
        }
        finally { Volatile.Write(ref stopped, 1); await reader.WaitAsync(TimeSpan.FromSeconds(5)); }
        AssertEqual(64, published, "Concurrent completion snapshots remain readable without sharing races or write retries");
    }

    private static async Task ListingNeverStartsAProcessAsync()
    {
        var executed = 0;
        using var output = new StringWriter();
        var old = Console.Out;
        try
        {
            Console.SetOut(output);
            var exit = await RegressionRunner.RunAsync(["--list"], [new("runner.probe/list", "core", () => throw new InvalidOperationException())],
                (_, _, _) => { executed++; return Task.CompletedTask; });
            AssertEqual(0, exit, "Metadata-only selection succeeds");
            AssertEqual(0, executed, "Metadata listing spawns no child and invokes no fixture");
        }
        finally { Console.SetOut(old); }
    }

    private static async Task FakeTransportCannotRehabilitateTheSameLateActionAsync()
    {
        foreach (var scenario in new[] { "late-pass", "malformed-protocol", "stderr-fault", "late-owned-fault", "late-owned-snapshot" })
            await VerifyLateActionAsync(scenario);
    }

    private static async Task VerifyLateActionAsync(string scenario)
    {
        using var fixture = new ProcessFixture();
        RegressionCase[] cases = [new("runner.probe/confirmed", "core", () => Task.CompletedTask),
            new("runner.probe/late", "core", () => Task.CompletedTask), new("runner.probe/not-run", "core", () => Task.CompletedTask)];
        var report = fixture.Report(cases);
        var host = new RegressionProcessHost(info =>
        {
            var request = RegressionProcessProtocol.Read<RegressionChildRequest>(info.ArgumentList[^1]);
            var manifest = PassedManifest(request);
            if (scenario is "late-owned-fault" or "late-owned-snapshot")
            {
                manifest.Complete = false;
                manifest.Cases[1].Outcome = "failed";
                manifest.Cases[1].ExceptionType = typeof(IOException).FullName;
                manifest.Cases[1].FailureStage = "case";
                manifest.Cases[2].Outcome = "not-run";
            }
            if (scenario != "late-owned-snapshot") RegressionProcessProtocol.Write(request.ManifestPath, manifest);
            else
            {
                manifest.Final = false;
                RegressionProcessProtocol.WriteNew(RegressionProcessProtocol.SnapshotPath(request, 2), manifest);
            }
            var now = Stopwatch.GetTimestamp();
            var records = new List<RegressionProcessEvent>();
            void Add(string kind, string id = "", int ordinal = 0, long queued = 0, long? timestamp = null) => records.Add(new()
            {
                RequestId = request.RequestId, RunId = request.RunId, Partition = request.Partition, Kind = kind,
                CaseId = id, Ordinal = ordinal, QueuedTimestamp = queued, Timestamp = timestamp ?? now
            });
            Add("ready");
            Add("case-start", cases[0].Id);
            var first = RegressionChildManifest.Create(request);
            first.Cases[0].Outcome = "passed";
            RegressionProcessProtocol.Write(RegressionProcessProtocol.SnapshotPath(request, 1), first);
            Add("case-finished", cases[0].Id);
            records[^1].Snapshot = 1;
            Add("case-start", cases[1].Id);
            Add("queued", cases[1].Id, 1, now);
            Add("entered", cases[1].Id, 1, now);
            Add("completed", cases[1].Id, 1, now, now + 121 * Stopwatch.Frequency);
            Add("case-finished", cases[1].Id, timestamp: now + 121 * Stopwatch.Frequency);
            Add("final", timestamp: now + 121 * Stopwatch.Frequency);
            var output = string.Join('\n', records.Select(item => RegressionProcessProtocol.Prefix + JsonSerializer.Serialize(item, RegressionProcessProtocol.Options))) + "\n";
            if (scenario == "malformed-protocol") output += RegressionProcessProtocol.Prefix + "{invalid-json}\n";
            return new FakeChild(output, 1, scenario == "stderr-fault" ? new FailingReader() : null);
        });
        await host.ExecuteAsync(cases, report, fixture.Path);
        AssertEqual(true, report.Cases.Select(item => item.Outcome).SequenceEqual(["passed", "timed-out", "not-run"]), "Hard expiry fails the same late action and rejects later passes claimed by an advanced manifest");
        AssertEqual("action", report.Cases[1].FailureStage, "Hard expiry retains the same root's phase");
        AssertEqual(typeof(RegressionExecutionTimeoutException).FullName, report.Cases[1].ExceptionType, "A latched hard failure remains the primary cross-process exception");
        if (scenario is "malformed-protocol" or "stderr-fault")
        {
            AssertEqual(typeof(InvalidDataException).FullName, report.Cases[1].SecondaryExceptionType, "A concurrent protocol or pipe fault remains secondary to the original hard latch");
            AssertEqual("child-protocol", report.Cases[1].SecondaryFailureStage, "The secondary protocol failure keeps its safe stage");
            AssertEqual("invalid-child-protocol", report.Cases[1].InfrastructureCode, "The concurrent protocol failure remains diagnosable without changing hard ownership");
        }
        if (scenario is "late-owned-fault" or "late-owned-snapshot")
        {
            AssertEqual(typeof(IOException).FullName, report.Cases[1].SecondaryExceptionType, "An owned error published after hard expiry cannot replace the earlier hard failure");
            AssertEqual("case", report.Cases[1].SecondaryFailureStage, "The late owned error retains its case stage as secondary evidence");
        }
    }

    private static async Task PipeCleanupCannotReplaceADurableCaseFailureAsync()
    {
        using var fixture = new ProcessFixture();
        RegressionCase[] cases = [new("runner.probe/original-failure", "core", () => Task.CompletedTask)];
        var report = fixture.Report(cases);
        var host = new RegressionProcessHost(info =>
        {
            var request = RegressionProcessProtocol.Read<RegressionChildRequest>(info.ArgumentList[^1]);
            var manifest = PassedManifest(request);
            manifest.Complete = false;
            manifest.Cases[0].Outcome = "failed";
            manifest.Cases[0].ExceptionType = typeof(TimeoutException).FullName;
            manifest.Cases[0].FailureStage = "case";
            RegressionProcessProtocol.Write(request.ManifestPath, manifest);
            return FakeChild.ForManifest(request, manifest, new FailingReader());
        });
        await host.ExecuteAsync(cases, report, fixture.Path);
        AssertEqual(typeof(TimeoutException).FullName, report.Cases[0].ExceptionType, "Pipe cleanup failure preserves the durable original exception type");
        AssertEqual("case", report.Cases[0].FailureStage, "Pipe cleanup preserves the original action-owned failure stage");
        AssertEqual(typeof(InvalidDataException).FullName, report.Cases[0].SecondaryExceptionType, "Secondary protocol failure stays distinguishable without replacing the original");
    }

    private static async Task ExitCannotSkipCompletionFramesQueuedByTheFinalDrainAsync()
    {
        using var fixture = new ProcessFixture();
        RegressionCase[] cases = [new("runner.probe/exit-drain-tail", "core", () => Task.CompletedTask)];
        var report = fixture.Report(cases);
        HeldTailReader? reader = null;
        var host = new RegressionProcessHost(info =>
        {
            var request = RegressionProcessProtocol.Read<RegressionChildRequest>(info.ArgumentList[^1]);
            var manifest = PassedManifest(request);
            RegressionProcessProtocol.Write(request.ManifestPath, manifest);
            using var completed = FakeChild.ForManifest(request, manifest);
            var frames = completed.Output.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            reader = new HeldTailReader(string.Join('\n', frames.Take(2)) + "\n", string.Join('\n', frames.Skip(2)) + "\n");
            // The first Exit getter runs only after the parent has consumed ready
            // and case-start. Releasing this synchronous continuation queues the
            // completion frames and finishes the drain before IsCompleted checks.
            return new FakeChild("", 0, outputReader: reader, exitRead: reader.Release);
        });
        await host.ExecuteAsync(cases, report, fixture.Path);
        AssertEqual(1, reader!.ReleaseCount, "The fake exit boundary releases exactly one held stdout tail");
        AssertEqual(true, reader.ReachedEnd, "The final drain completes synchronously at the exit-query boundary");
        AssertEqual("passed", report.Cases[0].Outcome, "Exit and EOF cannot skip newly queued completion frames and their immutable evidence");
    }

    private static async Task FakeTransportRejectsUnconfirmedPassesAsync()
    {
        foreach (var scenario in new[] { "missing-root-completion", "missing-case-events", "malformed-active", "failed-incomplete-root" })
        {
            using var fixture = new ProcessFixture();
            RegressionCase[] cases = [new("runner.probe/active", "core", () => Task.CompletedTask),
                new("runner.probe/future-one", "core", () => Task.CompletedTask), new("runner.probe/future-two", "core", () => Task.CompletedTask)];
            var report = fixture.Report(cases);
            var host = new RegressionProcessHost(info =>
            {
                var request = RegressionProcessProtocol.Read<RegressionChildRequest>(info.ArgumentList[^1]);
                var manifest = PassedManifest(request);
                if (scenario == "failed-incomplete-root")
                {
                    manifest.Complete = false;
                    manifest.Cases[0].Outcome = "failed";
                    manifest.Cases[0].ExceptionType = typeof(TimeoutException).FullName;
                    manifest.Cases[0].FailureStage = "case";
                    foreach (var later in manifest.Cases.Skip(1)) later.Outcome = "not-run";
                }
                RegressionProcessProtocol.Write(request.ManifestPath, manifest);
                using var output = new StringWriter();
                var protocol = new RegressionProtocolWriter(output, request);
                protocol.Publish("ready");
                if (scenario != "missing-case-events")
                {
                    protocol.Publish("case-start", cases[0].Id);
                    var queued = Stopwatch.GetTimestamp();
                    protocol.Publish("queued", action: new("queued", cases[0].Id, 1, queued, Stopwatch.Frequency, queued));
                    protocol.Publish("entered", action: new("entered", cases[0].Id, 1, Stopwatch.GetTimestamp(), Stopwatch.Frequency, queued));
                    if (scenario != "malformed-active")
                    {
                        var snapshot = RegressionChildManifest.Create(request);
                        snapshot.Cases[0].Outcome = manifest.Cases[0].Outcome;
                        snapshot.Cases[0].ExceptionType = manifest.Cases[0].ExceptionType;
                        snapshot.Cases[0].FailureStage = manifest.Cases[0].FailureStage;
                        RegressionProcessProtocol.Write(RegressionProcessProtocol.SnapshotPath(request, 1), snapshot);
                        protocol.Publish("case-finished", cases[0].Id, snapshot: 1);
                    }
                }
                if (scenario == "malformed-active") output.WriteLine(RegressionProcessProtocol.Prefix + "{invalid-json}");
                else
                {
                    RegressionProcessProtocol.Write(RegressionProcessProtocol.SnapshotPath(request, cases.Length + 1), manifest);
                    protocol.Publish("final", snapshot: cases.Length + 1);
                }
                return new FakeChild(output.ToString(), manifest.Complete ? 0 : 1);
            });
            await host.ExecuteAsync(cases, report, fixture.Path);
            AssertEqual(true, report.Cases.Select(item => item.Outcome).SequenceEqual(["failed", "not-run", "not-run"]), "A live root, absent case events or malformed active protocol cannot confirm current or future passes");
            if (scenario == "failed-incomplete-root")
            {
                AssertEqual(typeof(TimeoutException).FullName, report.Cases[0].ExceptionType, "A failed owner may terminate with an incomplete root without replacing its original action-owned error");
                AssertEqual("case", report.Cases[0].FailureStage, "An ordinary failed incomplete root remains an ordinary case failure");
            }
            else
            {
                AssertEqual(typeof(InvalidDataException).FullName, report.Cases[0].ExceptionType, "Missing completion provenance remains an infrastructure failure");
                AssertEqual(scenario switch
                {
                    "missing-root-completion" => "incomplete-child-root",
                    "missing-case-events" => "incomplete-child-case-events",
                    _ => "invalid-child-protocol"
                }, report.Cases[0].InfrastructureCode, "Fixed safe diagnostics identify the missing completion contract");
            }
        }
    }

    private static async Task BlockedProgressCannotBlockSupervisionAndRetiresQueuedWritesAsync()
    {
        using var fixture = new ProcessFixture();
        using var writer = new BlockingWriter();
        RegressionCase[] cases = [new("runner.probe/progress-first", "core", () => Task.CompletedTask), new("runner.probe/progress-second", "core", () => Task.CompletedTask)];
        var report = fixture.Report(cases);
        using var host = new RegressionProcessHost(info =>
        {
            var request = RegressionProcessProtocol.Read<RegressionChildRequest>(info.ArgumentList[^1]);
            var manifest = PassedManifest(request);
            RegressionProcessProtocol.Write(request.ManifestPath, manifest);
            return FakeChild.ForManifest(request, manifest);
        }, progressOutput: writer);
        try
        {
            await host.ExecuteAsync(cases, report, fixture.Path).WaitAsync(TimeSpan.FromSeconds(5));
            await writer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            AssertEqual(true, report.Cases.All(item => item.Outcome == "passed"), "A blocked parent output writer cannot block pipe drains, exit validation or supervision");
            host.Dispose();
            writer.Release.TrySetResult();
            await writer.Exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
            AssertEqual(1, writer.Calls, "Retirement discards queued progress after a previously blocked write returns");
        }
        finally { host.Dispose(); writer.Release.TrySetResult(); }
    }

    private static async Task RealChildrenDrainBothPipesAndRequireFinalEvidenceAsync()
    {
        var source = await CaptureSourceAsync();
        foreach (var probe in new[] { "pipes", "missing", "truncated", "foreign", "owned-timeout" })
        {
            using var fixture = new ProcessFixture();
            var report = fixture.Report([new("runner.probe/" + probe, "core", () => Task.CompletedTask)], source);
            var request = RegressionProcessHost.CreateRequest(report, "core", [report.Cases[0].Id], fixture.Path);
            request.Probe = probe;
            await new RegressionProcessHost().ExecutePartitionAsync(request, report, fixture.Path);
            AssertEqual(probe == "pipes" ? "passed" : "failed", report.Cases[0].Outcome, "A real child requires complete evidence plus its expected exit; both large pipes remain drainable");
            if (probe == "owned-timeout")
            {
                AssertEqual(typeof(TimeoutException).FullName, report.Cases[0].ExceptionType, "Cross-process action-owned timeout type is preserved");
                AssertEqual("case", report.Cases[0].FailureStage, "Cross-process action-owned timeout is distinct from a hard guard");
            }
        }
    }

    private static async Task RealChildHardExpiryKillsItsDescendantAsync()
    {
        using var fixture = new ProcessFixture();
        var report = fixture.Report([new("runner.probe/hang", "core", () => Task.CompletedTask)], await CaptureSourceAsync());
        var request = RegressionProcessHost.CreateRequest(report, "core", [report.Cases[0].Id], fixture.Path);
        request.Probe = "hang";
        var profile = new RegressionProcessProfile(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(2));
        var started = Stopwatch.GetTimestamp();
        await new RegressionProcessHost(profile: profile).ExecutePartitionAsync(request, report, fixture.Path);
        AssertEqual("timed-out", report.Cases[0].Outcome, "The outside-process guard stops a noncompleting action");
        AssertEqual("action", report.Cases[0].FailureStage, "Hard expiry reports its original action phase");
        AssertEqual(true, Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(15), "Kill and pipe cleanup stay bounded");
        var pidPath = request.ManifestPath + ".grandchild";
        AssertEqual(true, File.Exists(pidPath), "The process-tree probe created an actual descendant before expiry");
        var pid = int.Parse(File.ReadAllText(pidPath), CultureInfo.InvariantCulture);
        var alive = false;
        try { using var process = Process.GetProcessById(pid); alive = !process.HasExited; }
        catch (ArgumentException) { }
        AssertEqual(false, alive, "Hard expiry kills the descendant and leaves no orphan");
    }

    private static async Task LegacyRequestsRetainPrivateArgumentsAndUseTheSameHostAsync()
    {
        using var fixture = new ProcessFixture();
        var id = RegressionProcessProtocol.LegacyId("--favorite-order-tests");
        var report = fixture.Report([new(id, "core", () => Task.CompletedTask)], await CaptureSourceAsync());
        var request = RegressionProcessHost.CreateRequest(report, "core", [id], fixture.Path);
        request.LegacyArguments = ["--favorite-order-tests"];
        await new RegressionProcessHost().ExecutePartitionAsync(request, report, fixture.Path);
        AssertEqual("passed", report.Cases[0].Outcome, "A whitelisted real legacy alias uses the controlled child without recursive launch");
        var privateRequest = Request([RegressionProcessProtocol.LegacyId("--popup-proof-tests")]);
        privateRequest.LegacyArguments = ["--popup-proof-tests", "private-original-proof-output"];
        RegressionProcessProtocol.ValidateRequest(privateRequest);
        AssertEqual("private-original-proof-output", privateRequest.LegacyArguments[1], "Legacy proof values retain their original private meaning");
        var publicJson = JsonSerializer.Serialize(report, RegressionReport.JsonOptions);
        AssertEqual(false, publicJson.Contains("private-original-proof-output", StringComparison.Ordinal), "Proof parameters never enter public result metadata");
    }

    private static RegressionChildRequest Request(string[] ids) => new()
    {
        RunId = Guid.NewGuid().ToString("N"), Partition = "core", CaseIds = ids,
        Configuration = RegressionReport.BuildMetadata("RegressionBuildConfiguration"), Sdk = RegressionReport.BuildMetadata("RegressionBuildSdk"),
        ManifestPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")
    };

    private static RegressionChildManifest PassedManifest(RegressionChildRequest request)
    {
        var result = RegressionChildManifest.Create(request);
        result.Final = result.Complete = true;
        foreach (var item in result.Cases) item.Outcome = "passed";
        return result;
    }

    private static void AssertThrows<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException(message);
    }

    private static async Task<RegressionSourceIdentity> CaptureSourceAsync()
    {
        var source = await RegressionSource.CaptureAsync();
        source.RecordDirtyScope(RegressionCatalog.Create());
        return source.Identity;
    }

    private static void Observe(RegressionProcessWatchdog watchdog, RegressionChildRequest request, ManualStreamingTimeProvider clock,
        string kind, string caseId = "", int ordinal = 0, long queued = 0) => watchdog.Observe(new()
        {
            RequestId = request.RequestId, RunId = request.RunId, Partition = request.Partition, Kind = kind,
            CaseId = caseId, Ordinal = ordinal, Timestamp = clock.GetTimestamp(), Frequency = clock.TimestampFrequency, QueuedTimestamp = queued
        });

    private sealed class FakeChild(string output, int exitCode, TextReader? error = null, TextReader? outputReader = null, Action? exitRead = null) : IRegressionChildProcess
    {
        public TextReader Output { get; } = outputReader ?? new StringReader(output);
        public TextReader Error { get; } = error ?? new StringReader(new string('e', 200_000));
        public Task Exit { get { exitRead?.Invoke(); return Task.CompletedTask; } }
        public int ExitCode => exitCode;
        internal bool Killed { get; private set; }
        public void KillTree() => Killed = true;
        public void Dispose() { Output.Dispose(); Error.Dispose(); }
        internal static FakeChild ForManifest(RegressionChildRequest request, RegressionChildManifest manifest, TextReader? error = null)
        {
            using var output = new StringWriter();
            var protocol = new RegressionProtocolWriter(output, request);
            protocol.Publish("ready");
            for (var index = 0; index < manifest.Cases.Count && manifest.Cases[index].Outcome != "not-run"; index++)
            {
                var item = manifest.Cases[index];
                protocol.Publish("case-start", item.Id);
                var snapshot = JsonSerializer.Deserialize<RegressionChildManifest>(JsonSerializer.Serialize(manifest, RegressionProcessProtocol.Options), RegressionProcessProtocol.Options)!;
                snapshot.Final = snapshot.Complete = false;
                foreach (var later in snapshot.Cases.Skip(index + 1)) { later.Outcome = "not-run"; later.ExceptionType = later.FailureStage = null; }
                RegressionProcessProtocol.Write(RegressionProcessProtocol.SnapshotPath(request, index + 1), snapshot);
                protocol.Publish("case-finished", item.Id, snapshot: index + 1);
            }
            RegressionProcessProtocol.Write(RegressionProcessProtocol.SnapshotPath(request, request.CaseIds.Length + 1), manifest);
            protocol.Publish("final", snapshot: request.CaseIds.Length + 1);
            return new(output.ToString(), manifest.Complete ? 0 : 1, error);
        }
    }

    private sealed class HeldTailReader(string prefix, string tail) : TextReader
    {
        // Inline continuations make the empty-channel/exit-check interleaving
        // deterministic; no sleep, scheduler luck or extra process is involved.
        private readonly TaskCompletionSource<int> _pending = new();
        private Memory<char> _pendingBuffer;
        private bool _prefixRead;
        private bool _held;
        private bool _released;
        internal int ReleaseCount { get; private set; }
        internal bool ReachedEnd { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (!_prefixRead)
            {
                _prefixRead = true;
                prefix.AsMemory().CopyTo(buffer);
                return ValueTask.FromResult(prefix.Length);
            }
            if (_released)
            {
                ReachedEnd = true;
                return ValueTask.FromResult(0);
            }
            _held = true;
            _pendingBuffer = buffer;
            return new ValueTask<int>(_pending.Task);
        }
        internal void Release()
        {
            if (_released) return;
            AssertEqual(true, _held, "The stdout tail is held before the parent first queries exit");
            _released = true;
            ReleaseCount++;
            tail.AsMemory().CopyTo(_pendingBuffer);
            _pending.SetResult(tail.Length);
        }
    }

    private sealed class ProtocolObserver(Action<string> observe) : StringWriter
    {
        public override void WriteLine(string? value)
        {
            observe(value!);
            base.WriteLine(value);
        }
    }

    private sealed class FailingReader : TextReader
    {
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("private-pipe-cleanup"));
    }

    private sealed class BlockingWriter : StringWriter
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        internal int Calls => Volatile.Read(ref _calls);
        public override void WriteLine(string? value)
        {
            Interlocked.Increment(ref _calls);
            Entered.TrySetResult();
            Release.Task.GetAwaiter().GetResult();
            Exited.TrySetResult();
        }
        protected override void Dispose(bool disposing)
        {
            Release.TrySetResult();
            base.Dispose(disposing);
        }
    }

    private sealed class ProcessFixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fishaudio-process-" + Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(_directory, "parent.json");
        internal ProcessFixture() => Directory.CreateDirectory(_directory);
        internal RegressionReport Report(IReadOnlyList<RegressionCase> cases, RegressionSourceIdentity? source = null)
        {
            var report = new RegressionReport { Source = source ?? new(), Cases = cases.Select(item => new RegressionCaseResult { Id = item.Id, Partition = item.Partition }).ToList() };
            report.Write(Path);
            return report;
        }
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
