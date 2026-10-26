using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

internal sealed record RegressionProcessProfile(TimeSpan Warning, TimeSpan Hard, TimeSpan Startup, TimeSpan Diagnostics)
{
    internal static RegressionProcessProfile Default { get; } = new(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(10));
}

internal interface IRegressionChildProcess : IDisposable
{
    TextReader Output { get; }
    TextReader Error { get; }
    Task Exit { get; }
    int ExitCode { get; }
    void KillTree();
}

internal sealed class RegressionProcessHost : IDisposable
{
    private readonly Func<ProcessStartInfo, IRegressionChildProcess> _start;
    private readonly RegressionProcessProfile _profile;
    private readonly ProgressReporter? _progress;

    internal RegressionProcessHost(Func<ProcessStartInfo, IRegressionChildProcess>? start = null, RegressionProcessProfile? profile = null, TextWriter? progressOutput = null)
    {
        _start = start ?? (info => new SystemChildProcess(info));
        _profile = profile ?? RegressionProcessProfile.Default;
        _progress = progressOutput is null ? null : new ProgressReporter(progressOutput);
        if (_profile.Warning <= TimeSpan.Zero || _profile.Hard <= _profile.Warning || _profile.Startup <= TimeSpan.Zero ||
            _profile.Diagnostics <= TimeSpan.Zero || _profile.Diagnostics > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(profile));
    }

    internal async Task ExecuteAsync(IReadOnlyList<RegressionCase> selected, RegressionReport report, string reportPath)
    {
        if (selected.Any(item => !RegressionProcessProtocol.Partitions.Contains(item.Partition, StringComparer.Ordinal)))
            throw new RegressionSelectionException("unsupported-process-partition");
        foreach (var partition in RegressionProcessProtocol.Partitions)
        {
            var cases = selected.Where(item => item.Partition == partition).ToArray();
            if (cases.Length == 0) continue;
            var request = CreateRequest(report, partition, cases.Select(item => item.Id).ToArray(), reportPath);
            await ExecutePartitionAsync(request, report, reportPath);
        }
    }

    internal static async Task<int?> TryRunLegacyAsync(string[] args, RegressionProcessHost? host = null)
    {
        if (!LegacyRegressionSelectors.ContainsSelector(args)) return null;
        if (!LegacyRegressionSelectors.AreArgumentsValid(args)) return 2;
        var source = await RegressionSource.CaptureAsync();
        source.RecordDirtyScope(RegressionCatalog.Create());
        var id = RegressionProcessProtocol.LegacyId(args[0]);
        var report = new RegressionReport
        {
            Source = source.Identity, Invocation = [args[0], .. args.Skip(1).Select(_ => "<private-value>")],
            SelectionReasons = ["legacy-compatible-selector"],
            Cases = [new() { Id = id, Partition = "core" }]
        };
        var path = Path.Combine(source.Root ?? Environment.CurrentDirectory, ".scratch", "test-runs", report.RunId + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        report.Write(path);
        var request = CreateRequest(report, "core", [id], path);
        request.LegacyArguments = args;
        await (host ?? new RegressionProcessHost()).ExecutePartitionAsync(request, report, path);
        report.Complete = report.Cases.All(item => item.Outcome == "passed");
        report.FinishedUtc = DateTimeOffset.UtcNow;
        report.Write(path);
        return report.Complete ? 0 : 1;
    }

    internal static RegressionChildRequest CreateRequest(RegressionReport report, string partition, string[] ids, string reportPath) => new()
    {
        RunId = report.RunId, Partition = partition, CaseIds = ids, Source = report.Source,
        Configuration = report.Configuration, Sdk = report.Sdk,
        ManifestPath = Path.Combine(Path.GetDirectoryName(reportPath)!, report.RunId + "." + Guid.NewGuid().ToString("N") + ".child.json")
    };

    internal async Task ExecutePartitionAsync(RegressionChildRequest request, RegressionReport report, string reportPath)
    {
        RegressionProcessProtocol.ValidateRequest(request);
        var requestPath = request.ManifestPath + ".request.json";
        RegressionProcessProtocol.Write(requestPath, request);
        var targets = request.CaseIds.Select(id => report.Cases.Single(item => item.Id == id && item.Partition == request.Partition)).ToArray();
        foreach (var target in targets) target.ChildRequestId = request.RequestId;
        var started = Stopwatch.GetTimestamp();
        var watchdog = new RegressionProcessWatchdog(request, started, warning: _profile.Warning, hard: _profile.Hard, startup: _profile.Startup);
        var channel = Channel.CreateBounded<RegressionProcessEvent>(new BoundedChannelOptions(4096) { SingleReader = true, SingleWriter = true });
        var stdout = new BoundedChildOutput();
        var stderr = new BoundedChildOutput();
        IRegressionChildProcess? child = null;
        Task? outputDrain = null;
        Task? errorDrain = null;
        var infrastructureStage = "child-startup";
        var hardExpired = false;
        long? diagnosticStarted = null;
        var warningsObserved = 0;
        var confirmedCaseCount = 0;
        var progressTimestamp = started;
        void LatchHardFailure()
        {
            if (watchdog.FailureStage is null) return;
            hardExpired = true;
            diagnosticStarted ??= Stopwatch.GetTimestamp();
        }
        void PublishWarnings()
        {
            if (watchdog.WarningCount == warningsObserved) return;
            var target = targets.FirstOrDefault(item => item.Id == watchdog.CurrentCaseId);
            if (target is not null)
            {
                target.SlowActionWarnings += watchdog.WarningCount - warningsObserved;
                target.ActionOrdinal = watchdog.CurrentOrdinal;
                target.QueueToEntryMilliseconds = watchdog.EntryElapsedMilliseconds;
                report.Write(reportPath);
                _progress?.Publish($"WARN {target.Id}: {watchdog.Stage} guard {target.ActionOrdinal} passed {_profile.Warning.TotalSeconds:0.###}s.");
            }
            warningsObserved = watchdog.WarningCount;
        }
        try
        {
            child = _start(CreateStartInfo("--regression-child", requestPath));
            // Start both independent drains before observing exit or waiting on any
            // event. Neither drain invokes Console, a writer supplied by a suite,
            // report persistence, or any other potentially blocking diagnostic sink.
            outputDrain = DrainAsync(child.Output, stdout, record => channel.Writer.TryWrite(record));
            errorDrain = DrainAsync(child.Error, stderr, null);
            while (true)
            {
                while (channel.Reader.TryRead(out var record))
                {
                    infrastructureStage = "child-event";
                    watchdog.Observe(record);
                    LatchHardFailure();
                    PublishWarnings();
                    if (watchdog.FailureStage is not null) break;
                    var target = targets.FirstOrDefault(item => item.Id == record.CaseId);
                    if (target is not null)
                    {
                        if (record.Kind == "case-start" && target.Outcome == "not-run")
                        {
                            target.Outcome = "running";
                            report.Write(reportPath);
                        }
                        if (record.Kind == "queued") target.ActionOrdinal = record.Ordinal;
                        if (record.Kind == "entered") target.QueueToEntryMilliseconds = watchdog.EntryElapsedMilliseconds;
                    }
                    if (record.Kind is "case-finished" or "final")
                    {
                        infrastructureStage = "child-evidence";
                        var expectedSnapshot = record.Kind == "final" ? request.CaseIds.Length + 1 : Array.IndexOf(request.CaseIds, record.CaseId) + 1;
                        if (record.Snapshot != expectedSnapshot || expectedSnapshot <= 0) throw new InvalidDataException("invalid-child-snapshot");
                        if (record.Kind == "final" && watchdog.CompletedCaseCount != confirmedCaseCount)
                            throw new InvalidDataException("incomplete-child-case-events");
                        MergeManifest(request, targets, requireFinal: record.Kind == "final", snapshot: expectedSnapshot,
                            maximumPassedCases: record.Kind == "case-finished" ? expectedSnapshot : confirmedCaseCount,
                            incompleteRoot: record.Kind == "case-finished" && watchdog.CaseFinishedWithIncompleteRoot);
                        if (record.Kind == "case-finished") confirmedCaseCount = expectedSnapshot;
                        report.Write(reportPath);
                        if (record.Kind == "case-finished" && target is not null)
                            _progress?.Publish($"{target.Outcome} {target.Id} ({target.ElapsedMilliseconds} ms).");
                    }
                }
                if (stdout.ProtocolFault || stderr.ProtocolFault)
                {
                    infrastructureStage = "child-protocol";
                    throw new InvalidDataException("invalid-child-protocol");
                }
                watchdog.Tick(Stopwatch.GetTimestamp());
                LatchHardFailure();
                PublishWarnings();
                if (watchdog.FailureStage is not null)
                {
                    infrastructureStage = watchdog.FailureStage;
                    // Completed immutable snapshots already establish the prefix.
                    // Never open the child's replaceable manifest while it is alive.
                    break;
                }
                if (watchdog.Final) break;
                if (child.Exit.IsCompleted && outputDrain.IsCompleted && !channel.Reader.TryPeek(out _)) break;
                if (Stopwatch.GetElapsedTime(progressTimestamp) >= TimeSpan.FromSeconds(15))
                {
                    _progress?.Publish($"Running {watchdog.CurrentCaseId ?? request.Partition}: {watchdog.Stage}; {(long)Stopwatch.GetElapsedTime(started).TotalMilliseconds} ms in child.");
                    progressTimestamp = Stopwatch.GetTimestamp();
                }
                await Task.WhenAny(child.Exit.IsCompleted ? Task.Delay(25) : child.Exit, Task.Delay(25));
            }
            if (hardExpired)
            {
                infrastructureStage = "child-kill";
                child.KillTree();
            }
            // A final event is still followed by bounded exit and stream closure.
            // Descendants inheriting pipes cannot keep the parent alive forever.
            diagnosticStarted ??= Stopwatch.GetTimestamp();
            TimeSpan RemainingDiagnostics()
            {
                var remaining = _profile.Diagnostics - Stopwatch.GetElapsedTime(diagnosticStarted.Value);
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.FromTicks(1);
            }
            infrastructureStage = "child-exit";
            await child.Exit.WaitAsync(RemainingDiagnostics());
            infrastructureStage = "child-drain";
            await Task.WhenAll(outputDrain, errorDrain).WaitAsync(RemainingDiagnostics());
            while (channel.Reader.TryRead(out var record))
            {
                watchdog.Observe(record);
                LatchHardFailure();
            }
            if (stdout.ProtocolFault || stderr.ProtocolFault)
            {
                infrastructureStage = "child-protocol";
                throw new InvalidDataException("invalid-child-protocol");
            }
            if (hardExpired)
            {
                TryMergePartial(request, targets, confirmedCaseCount);
                MarkFailure(targets, watchdog.FailureCaseId, typeof(RegressionExecutionTimeoutException).FullName!, watchdog.FailureStage!, timedOut: true);
            }
            else
            {
                infrastructureStage = "child-evidence";
                var manifest = MergeManifest(request, targets, requireFinal: true, maximumPassedCases: confirmedCaseCount);
                var finalSnapshot = RegressionProcessProtocol.Read<RegressionChildManifest>(RegressionProcessProtocol.SnapshotPath(request, request.CaseIds.Length + 1));
                RegressionProcessProtocol.ValidateManifest(finalSnapshot, request);
                if (!finalSnapshot.Final || JsonSerializer.Serialize(manifest, RegressionProcessProtocol.Options) != JsonSerializer.Serialize(finalSnapshot, RegressionProcessProtocol.Options))
                    throw new InvalidDataException("invalid-final-child-snapshot");
                if (!watchdog.Ready || !watchdog.Final || child.ExitCode != (manifest.Complete ? 0 : 1))
                    throw new InvalidDataException("incomplete-child-exit");
            }
        }
        catch (Exception error)
        {
            // Observe can latch hard expiry before another protocol/report/cleanup
            // fault throws. Promote that ownership before any late evidence merge.
            LatchHardFailure();
            var secondaryStage = infrastructureStage;
            int? exitCode = null;
            try { exitCode = child?.ExitCode; } catch { }
            try { child?.KillTree(); }
            catch { /* The evidence/guard failure owns the outcome. */ }
            if (child is not null)
            {
                diagnosticStarted ??= Stopwatch.GetTimestamp();
                var remaining = _profile.Diagnostics - Stopwatch.GetElapsedTime(diagnosticStarted.Value);
                try { await child.Exit.WaitAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.FromTicks(1)); }
                catch { /* Bounded cleanup cannot replace the original failure. */ }
                if (child.Exit.IsCompletedSuccessfully) TryMergePartial(request, targets, confirmedCaseCount);
            }
            var failure = MarkFailure(targets, hardExpired ? watchdog.FailureCaseId : watchdog.CurrentCaseId, hardExpired ? typeof(RegressionExecutionTimeoutException).FullName! : error.GetType().FullName!,
                hardExpired ? watchdog.FailureStage ?? infrastructureStage : infrastructureStage, hardExpired);
            if (hardExpired) RecordSecondaryFailure(failure, error.GetType().FullName!, secondaryStage);
            foreach (var target in targets.Where(item => item.Outcome is "failed" or "timed-out")) target.ChildExitCode = exitCode;
            foreach (var target in targets.Where(item => item.Outcome is "failed" or "timed-out")) target.ChildInfrastructureExceptionType = stderr.SafeProgramFailureType();
            if (error is InvalidDataException && error.Message is "invalid-child-event" or "invalid-child-event-order" or "unknown-child-event" or
                "invalid-child-manifest" or "invalid-child-case-order" or "missing-final-child-manifest" or "incomplete-child-exit" or "invalid-child-protocol" or "child-pass-reverted" or "invalid-child-snapshot" or "invalid-final-child-snapshot" or "incomplete-child-root" or "incomplete-child-case-events")
                failure.InfrastructureCode = error.Message;
        }
        finally
        {
            // Child raw output remains a bounded private memory buffer; only fixed
            // stage/type/timing metadata enters durable parent reports.
            report.ElapsedMilliseconds += (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            report.Write(reportPath);
            child?.Dispose();
        }
    }

    public void Dispose() => _progress?.Dispose();

    private static RegressionChildManifest MergeManifest(RegressionChildRequest request, RegressionCaseResult[] targets, bool requireFinal,
        int snapshot = 0, int maximumPassedCases = 0, bool incompleteRoot = false)
    {
        var manifest = RegressionProcessProtocol.Read<RegressionChildManifest>(snapshot > 0 ? RegressionProcessProtocol.SnapshotPath(request, snapshot) : request.ManifestPath);
        RegressionProcessProtocol.ValidateManifest(manifest, request);
        if (requireFinal && !manifest.Final) throw new InvalidDataException("missing-final-child-manifest");
        if (requireFinal && manifest.Complete && maximumPassedCases != targets.Length)
            throw new InvalidDataException("incomplete-child-case-events");
        if (snapshot > 0 && snapshot <= targets.Length && (manifest.Final ||
            manifest.Cases[snapshot - 1].Outcome is not ("passed" or "failed" or "timed-out") ||
            manifest.Cases.Take(snapshot - 1).Any(item => item.Outcome != "passed") || manifest.Cases.Skip(snapshot).Any(item => item.Outcome != "not-run")))
            throw new InvalidDataException("invalid-child-snapshot");
        if (incompleteRoot && manifest.Cases[snapshot - 1].Outcome == "passed")
            throw new InvalidDataException("incomplete-child-root");
        for (var index = 0; index < targets.Length; index++)
        {
            var child = manifest.Cases[index];
            var target = targets[index];
            if (target.Outcome == "passed" && child.Outcome != "passed") throw new InvalidDataException("child-pass-reverted");
            if (child.Outcome == "running") continue;
            // A canonical file can advance ahead of typed completion evidence.
            // It can recover a failed owner after exit, but cannot prove new passes.
            if (child.Outcome == "passed" && index >= maximumPassedCases) continue;
            target.Outcome = child.Outcome;
            target.ExceptionType = child.ExceptionType;
            target.FailureStage = child.FailureStage;
            target.ElapsedMilliseconds = child.ElapsedMilliseconds;
        }
        return manifest;
    }

    private static void TryMergePartial(RegressionChildRequest request, RegressionCaseResult[] targets, int confirmedCaseCount)
    {
        try { MergeManifest(request, targets, requireFinal: false, maximumPassedCases: confirmedCaseCount); }
        catch { /* Invalid evidence cannot confirm a new pass or hide the primary failure. */ }
    }

    private static RegressionCaseResult MarkFailure(RegressionCaseResult[] targets, string? currentId, string exceptionType, string stage, bool timedOut)
    {
        var target = targets.FirstOrDefault(item => item.Id == currentId && (timedOut || item.Outcome != "passed"))
            ?? targets.FirstOrDefault(item => item.Outcome != "passed") ?? targets[^1];
        // A failed partition cannot supply coverage after its first unconfirmed
        // owner, even when an ahead-of-events canonical file claims later passes.
        var failedIndex = Array.IndexOf(targets, target);
        foreach (var later in targets.Skip(failedIndex + 1))
        {
            later.Outcome = "not-run";
            later.ExceptionType = later.FailureStage = later.InfrastructureCode = null;
            later.SecondaryExceptionType = later.SecondaryFailureStage = null;
            later.ElapsedMilliseconds = later.SlowActionWarnings = later.ActionOrdinal = 0;
            later.QueueToEntryMilliseconds = null;
        }
        if (target.Outcome is "failed" or "timed-out" && target.ExceptionType is not null)
        {
            if (!timedOut)
            {
                RecordSecondaryFailure(target, exceptionType, stage);
                return target;
            }
            if (target.ExceptionType != exceptionType || target.FailureStage != stage)
                RecordSecondaryFailure(target, target.ExceptionType, target.FailureStage ?? "case");
        }
        // A missing final manifest/exit can invalidate the terminal apparent pass,
        // while earlier durably confirmed successes remain reusable.
        target.Outcome = timedOut ? "timed-out" : "failed";
        target.ExceptionType = exceptionType;
        target.FailureStage = stage;
        return target;
    }

    private static void RecordSecondaryFailure(RegressionCaseResult target, string exceptionType, string stage)
    {
        target.SecondaryExceptionType ??= exceptionType;
        target.SecondaryFailureStage ??= stage;
    }

    internal static ProcessStartInfo CreateStartInfo(string mode, string requestPath)
    {
        var assembly = typeof(RegressionProcessHost).Assembly.Location;
        var apphost = Path.ChangeExtension(assembly, OperatingSystem.IsWindows() ? ".exe" : null);
        var info = new ProcessStartInfo
        {
            FileName = File.Exists(apphost) ? apphost : (Environment.ProcessPath is { } process && Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? process : "dotnet"),
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        if (!File.Exists(apphost)) info.ArgumentList.Add(assembly);
        info.ArgumentList.Add(mode);
        info.ArgumentList.Add(requestPath);
        return info;
    }

    private static async Task DrainAsync(TextReader reader, BoundedChildOutput output, Func<RegressionProcessEvent, bool>? publish)
    {
        var buffer = new char[4096];
        var line = new StringBuilder();
        var discardedLine = false;
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory())) != 0)
            {
                output.Append(buffer.AsSpan(0, read));
                if (publish is null) continue;
                for (var index = 0; index < read; index++)
                {
                    var character = buffer[index];
                    if (character == '\n')
                    {
                        if (!discardedLine && line.ToString().StartsWith(RegressionProcessProtocol.Prefix, StringComparison.Ordinal))
                        {
                            try
                            {
                                var record = JsonSerializer.Deserialize<RegressionProcessEvent>(line.ToString()[RegressionProcessProtocol.Prefix.Length..], RegressionProcessProtocol.Options);
                                if (record is null || !publish(record)) output.ProtocolFault = true;
                            }
                            catch { output.ProtocolFault = true; }
                        }
                        line.Clear();
                        discardedLine = false;
                    }
                    else if (!discardedLine)
                    {
                        if (line.Length == RegressionProcessProtocol.MaximumRecordCharacters)
                        {
                            if (line.ToString().StartsWith(RegressionProcessProtocol.Prefix, StringComparison.Ordinal)) output.ProtocolFault = true;
                            line.Clear();
                            discardedLine = true;
                        }
                        else if (character != '\r') line.Append(character);
                    }
                }
            }
            if (publish is not null && line.ToString().StartsWith(RegressionProcessProtocol.Prefix, StringComparison.Ordinal)) output.ProtocolFault = true;
        }
        catch { output.ProtocolFault = true; }
    }

    private sealed class BoundedChildOutput
    {
        private readonly char[] _tail = new char[64 * 1024];
        private int _position;
        internal volatile bool ProtocolFault;
        internal void Append(ReadOnlySpan<char> value)
        {
            foreach (var character in value) { _tail[_position] = character; _position = (_position + 1) % _tail.Length; }
        }
        internal string? SafeProgramFailureType()
        {
            var value = new string(_tail[_position..]) + new string(_tail[.._position]);
            const string prefix = "Regression execution failed (";
            var start = value.LastIndexOf(prefix, StringComparison.Ordinal);
            if (start < 0) return null;
            start += prefix.Length;
            var end = value.IndexOf(").", start, StringComparison.Ordinal);
            if (end < 0 || end - start > 200) return null;
            var type = value[start..end];
            return type.Length > 0 && type.All(character => char.IsAsciiLetterOrDigit(character) || character == '.') ? type : null;
        }
    }

    private sealed class SystemChildProcess : IRegressionChildProcess
    {
        private readonly Process _process;
        internal SystemChildProcess(ProcessStartInfo info)
        {
            _process = Process.Start(info) ?? throw new IOException("child-start-failed");
            Exit = _process.WaitForExitAsync();
        }
        public TextReader Output => _process.StandardOutput;
        public TextReader Error => _process.StandardError;
        public Task Exit { get; }
        public int ExitCode => _process.ExitCode;
        public void KillTree()
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        public void Dispose() => _process.Dispose();
    }

    private sealed class ProgressReporter : IDisposable
    {
        private readonly Channel<string> _messages = Channel.CreateBounded<string>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        private int _retired;
        internal ProgressReporter(TextWriter writer)
        {
            _ = Task.Run(async () =>
            {
                await foreach (var message in _messages.Reader.ReadAllAsync())
                {
                    if (Volatile.Read(ref _retired) != 0) break;
                    try { writer.WriteLine(message); } catch { }
                }
            });
        }
        internal void Publish(string message)
        {
            if (Volatile.Read(ref _retired) == 0) _messages.Writer.TryWrite(message);
        }
        public void Dispose()
        {
            Interlocked.Exchange(ref _retired, 1);
            _messages.Writer.TryComplete();
        }
    }
}
