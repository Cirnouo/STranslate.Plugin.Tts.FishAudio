using System.Diagnostics;

internal static class RegressionChildHost
{
    internal static bool IsChildInvocation(string[] args) => args.Length != 0 && args[0] is "--regression-child" or "--regression-grandchild";

    internal static async Task<int> RunAsync(string[] args, TextWriter physicalProtocolOutput)
    {
        if (args.Length != 2 || !Path.IsPathFullyQualified(args[1])) return 2;
        var request = RegressionProcessProtocol.Read<RegressionChildRequest>(args[1]);
        RegressionProcessProtocol.ValidateRequest(request);
        var registry = RegressionCatalog.Create();
        var source = await RegressionSource.CaptureAsync();
        source.RecordDirtyScope(registry);
        if (!RegressionProcessProtocol.SameSource(source.Identity, request.Source)) return 2;
        if (args[0] == "--regression-grandchild")
        {
            if (request.Probe != "hang") return 2;
            File.WriteAllText(request.ManifestPath + ".grandchild", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return 2;
        }
        var selected = request.LegacyArguments is not null || request.Probe is not null ? Array.Empty<RegressionCase>() :
            registry.Where(item => request.CaseIds.Contains(item.Id, StringComparer.Ordinal)).ToArray();
        if (request.LegacyArguments is null && request.Probe is null &&
            (!selected.Select(item => item.Id).SequenceEqual(request.CaseIds, StringComparer.Ordinal) || selected.Any(item => item.Partition != request.Partition))) return 2;
        return await RunCasesAsync(request, physicalProtocolOutput, async index =>
        {
            if (request.Probe is not null) await RunProbeAsync(request, args[1]);
            else if (request.LegacyArguments is not null)
            {
                // The old dispatcher is deliberately raw here: one alias, one controlled
                // process, original argument meanings, no recursive parent launch.
                if (await LegacyRegressionSelectors.TryRunAsync(request.LegacyArguments) != 0)
                    throw new InvalidOperationException("legacy-child-failed");
            }
            else await selected[index].Run();
        });
    }

    internal static async Task<int> RunCasesAsync(RegressionChildRequest request, TextWriter physicalProtocolOutput, Func<int, Task> runCase)
    {
        var manifest = RegressionChildManifest.Create(request);
        var protocol = new RegressionProtocolWriter(physicalProtocolOutput, request);
        Exception? failure = null;
        var failureIndex = -1;
        protocol.Publish("ready");
        for (var index = 0; index < request.CaseIds.Length; index++)
        {
            var result = manifest.Cases[index];
            result.Outcome = "running";
            protocol.Publish("case-start", result.Id);
            var started = Stopwatch.GetTimestamp();
            using var owner = RegressionActionScope.BeginCase(result.Id, action => protocol.Publish(action.Kind, action: action));
            try
            {
                await runCase(index);
                result.Outcome = "passed";
            }
            catch (Exception error)
            {
                result.Outcome = error is RegressionExecutionTimeoutException ? "timed-out" : "failed";
                result.ExceptionType = error.GetType().FullName ?? error.GetType().Name;
                result.FailureStage = error is RegressionExecutionTimeoutException ? "action-guard" : "case";
                failure = error;
                failureIndex = index;
            }
            result.ElapsedMilliseconds = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            // Completed owners are durable before any later protocol/final I/O.
            // Active children never create or replace the canonical manifest.
            RegressionProcessProtocol.WriteNew(RegressionProcessProtocol.SnapshotPath(request, index + 1), manifest);
            protocol.Publish("case-finished", result.Id, snapshot: index + 1);
            if (result.Outcome != "passed") break;
        }
        manifest.Final = true;
        manifest.Complete = manifest.Cases.All(item => item.Outcome == "passed");
        RegressionProcessProtocol.WriteNew(RegressionProcessProtocol.SnapshotPath(request, request.CaseIds.Length + 1), manifest);
        if (request.Probe == "truncated")
        {
            using var truncated = new FileStream(request.ManifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            truncated.Write("{\"version\":"u8);
            truncated.Flush(flushToDisk: true);
        }
        else if (request.Probe != "missing")
        {
            if (request.Probe == "foreign") manifest.RequestId = Guid.NewGuid().ToString("N");
            RegressionProcessProtocol.WriteNew(request.ManifestPath, manifest);
        }
        protocol.Publish("final", snapshot: request.CaseIds.Length + 1);
        // Publish authoritative failed results before optional diagnostic I/O.
        // The supervisor's existing shared exit/drain allowance bounds this work.
        if (failure is not null) RegressionFailureEvidence.TryWrite(request, failureIndex, failure);
        return manifest.Complete ? 0 : 1;
    }

    private static async Task RunProbeAsync(RegressionChildRequest request, string requestPath)
    {
        if (request.Probe == "pipes")
        {
            // Concurrent writes exceed OS pipe capacity in BOTH directions. A parent
            // that drains only one pipe deadlocks this otherwise finite child.
            var payload = new string('x', 8192);
            await Task.WhenAll(Task.Run(async () =>
            {
                for (var index = 0; index < 128; index++) await Console.Out.WriteLineAsync(payload);
            }), Task.Run(async () =>
            {
                for (var index = 0; index < 128; index++) await Console.Error.WriteLineAsync(payload);
            }));
        }
        else if (request.Probe == "hang")
        {
            var root = RegressionActionScope.QueueRoot();
            using var entered = root?.Enter();
            // The descendant remains in the same OS process tree. The parent owns
            // killing it; the blocked fixture dispatcher is never the kill owner.
            using var descendant = Process.Start(RegressionProcessHost.CreateStartInfo("--regression-grandchild", requestPath))
                ?? throw new IOException("probe-descendant-start");
            var deadline = Stopwatch.GetTimestamp();
            while (!File.Exists(request.ManifestPath + ".grandchild"))
            {
                if (Stopwatch.GetElapsedTime(deadline) > TimeSpan.FromSeconds(10)) throw new IOException("probe-descendant-not-ready");
                await Task.Delay(10);
            }
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }
        else if (request.Probe == "fail") throw new IOException("private-probe-input");
        else if (request.Probe == "owned-timeout") throw new TimeoutException("private-probe-deadline");
    }
}
