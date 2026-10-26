using System.Diagnostics;

internal static class RegressionRunner
{
    internal static async Task<int> RunAsync(string[] args, IReadOnlyList<RegressionCase> cases,
        Func<IReadOnlyList<RegressionCase>, RegressionReport, string, Task>? execute = null)
    {
        try
        {
            if (cases.Count == 0 || cases.Any(item => item is null || !RegressionSelection.IsSafeId(item.Id) || !RegressionSelection.IsSafeId(item.Partition) || item.Run is null) ||
                cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != cases.Count)
                throw new RegressionSelectionException("invalid-registry");
            var options = RegressionSelection.Parse(args);
            var selectedIds = options.ExplicitIds(cases);
            RegressionReport? previous = null;
            if (options.RerunPath is not null)
            {
                previous = RegressionReport.Read(options.RerunPath);
                foreach (var item in previous.Cases.Where(item => item.Outcome != "passed"))
                {
                    selectedIds.UnionWith(RegressionCaseAliases.Resolve(item.Id, cases, "replay-case-removed"));
                }
            }
            var source = !options.List || options.Affected || previous is not null ? await RegressionSource.CaptureAsync() : null;
            source?.RecordDirtyScope(cases);
            var changedSource = previous is not null && (!previous.Source.Available || source is null || !source.Identity.Available || previous.Source.Fingerprint != source.Identity.Fingerprint);
            var changedBuild = previous is not null && (previous.Configuration != RegressionReport.BuildMetadata("RegressionBuildConfiguration") || previous.Sdk != RegressionReport.BuildMetadata("RegressionBuildSdk"));
            var conservative = false;
            var reasons = new List<string>();
            var recommendedGroups = Array.Empty<string>();
            if (options.Groups.Count != 0 || options.Cases.Count != 0) reasons.Add("explicit-selection");
            if (previous is not null) reasons.Add("replay-incomplete");
            if (options.Partitions.Count != 0) reasons.Add("partition-filter");
            if (options.Affected || changedSource || changedBuild)
            {
                var impacts = new List<RegressionImpactSelection>();
                if (options.Affected) impacts.Add(RegressionImpact.Select(cases, await source!.ChangedPathsAsync(options.BaseRef)));
                if (changedSource) impacts.Add(RegressionImpact.Recover(cases, previous!.Source, await source!.ChangedPathsSinceCommitAsync(previous.Source.Commit)));
                if (changedBuild) impacts.Add(RegressionImpact.Select(cases, null));
                foreach (var impact in impacts)
                {
                    selectedIds.UnionWith(impact.Ids);
                    conservative |= impact.ConservativeFallback;
                    reasons.AddRange(impact.RuleIds);
                }
                recommendedGroups = impacts.SelectMany(impact => impact.RecommendedGroups).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                SafeWriteLine($"Affected rules: {string.Join(", ", reasons)}; recommended groups: {string.Join(", ", recommendedGroups)}.");
                if (changedSource) SafeWriteLine("Source differs from the previous run; mapped recovery coverage was added.");
                if (changedBuild) reasons.Add("build-configuration-or-sdk-changed");
            }
            if (options.Groups.Count == 0 && options.Cases.Count == 0 && !options.Affected && previous is null)
            {
                selectedIds.UnionWith(cases.Select(item => item.Id));
                reasons.Add("default-all");
            }
            var selected = cases.Where(item => selectedIds.Contains(item.Id) && (options.Partitions.Count == 0 || options.Partitions.Contains(item.Partition, StringComparer.Ordinal))).ToArray();
            if (selected.Length == 0) throw new RegressionSelectionException("empty-selection");
            if (options.List)
            {
                foreach (var item in selected) SafeWriteLine($"{item.Id}\t{item.Partition}");
                SafeWriteLine($"Selected {selected.Length} case(s).");
                return 0;
            }
            var path = Path.GetFullPath(options.ResultsPath ?? Path.Combine(source!.Root ?? Environment.CurrentDirectory, ".scratch", "test-runs", $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json"));
            if (File.Exists(path)) throw new RegressionSelectionException("results-already-exist");
            if (options.RerunPath is not null && string.Equals(path, Path.GetFullPath(options.RerunPath), StringComparison.OrdinalIgnoreCase))
                throw new RegressionSelectionException("results-overwrite-replay");
            var report = new RegressionReport
            {
                Source = source!.Identity,
                Invocation = options.SafeInvocation.ToArray(),
                PreviousRunId = previous?.RunId,
                SourceChangedSincePreviousRun = changedSource,
                BuildChangedSincePreviousRun = changedBuild,
                ConservativeAffectedFallback = conservative,
                SelectionReasons = reasons.ToArray(),
                RecommendedGroups = recommendedGroups,
                Cases = selected.Select(item => new RegressionCaseResult { Id = item.Id, Partition = item.Partition }).ToList()
            };
            var runClock = Stopwatch.StartNew();
            report.Create(path);
            SafeWriteLine($"Running {selected.Length} case(s); report {report.RunId}.");
            var passed = 0;
            if (execute is not null)
            {
                await execute(selected, report, path);
                passed = report.Cases.Count(item => item.Outcome == "passed");
            }
            else
            for (var index = 0; index < selected.Length; index++)
            {
                var item = selected[index];
                var result = report.Cases[index];
                result.Outcome = "running";
                report.ElapsedMilliseconds = runClock.ElapsedMilliseconds;
                report.Write(path);
                var caseClock = Stopwatch.StartNew();
                using var progress = new Timer(_ => SafeProgress(item.Id, caseClock), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
                try
                {
                    // The registered owner retains its existing RegressionExecution deadline.
                    // A catalog entry may contain many separately bounded actions; wrapping the
                    // aggregate in a new twenty-second guard would change that contract.
                    await item.Run();
                    result.Outcome = "passed";
                    passed++;
                }
                catch (Exception error)
                {
                    result.Outcome = error is TimeoutException ? "timed-out" : "failed";
                    result.ExceptionType = error.GetType().FullName ?? error.GetType().Name;
                    SafeWriteLine($"FAIL {item.Id}: {result.ExceptionType}", error: true);
                }
                finally
                {
                    result.ElapsedMilliseconds = caseClock.ElapsedMilliseconds;
                    report.ElapsedMilliseconds = runClock.ElapsedMilliseconds;
                }
                try { report.Write(path); }
                catch (Exception reportError) when (result.Outcome != "passed")
                {
                    SafeWriteLine($"Result persistence failed after {item.Id}: {reportError.GetType().FullName}; the original {result.ExceptionType} remains failed.", error: true);
                    return 1;
                }
                SafeWriteLine($"{result.Outcome} {item.Id} ({result.ElapsedMilliseconds} ms); {index + 1}/{selected.Length} observed.");
                if (result.Outcome != "passed") break;
            }
            report.Complete = passed == selected.Length;
            report.FinishedUtc = DateTimeOffset.UtcNow;
            report.ElapsedMilliseconds = runClock.ElapsedMilliseconds;
            report.Write(path);
            foreach (var item in report.Cases.Where(item => item.Outcome is "failed" or "timed-out"))
                SafeWriteLine($"FAIL {item.Id}: {item.ExceptionType}; stage={item.FailureStage ?? "case"}; action={item.ActionOrdinal}.", error: true);
            SafeWriteLine($"{passed}/{selected.Length} passed; {report.Cases.Count(item => item.Outcome == "failed")} failed; {report.Cases.Count(item => item.Outcome == "timed-out")} timed out; {report.Cases.Count(item => item.Outcome == "not-run")} not run; {report.ElapsedMilliseconds} ms.");
            foreach (var item in report.Cases.Where(item => item.Outcome != "not-run").OrderByDescending(item => item.ElapsedMilliseconds).Take(5))
                SafeWriteLine($"Slow: {item.Id} {item.ElapsedMilliseconds} ms ({item.Outcome}).");
            return report.Complete ? 0 : 1;
        }
        catch (RegressionSelectionException error)
        {
            SafeWriteLine($"Runner selection error: {error.Message}.", error: true);
            return 2;
        }
        catch (Exception error)
        {
            // Transport/file/parser messages can contain private text or machine paths.
            SafeWriteLine($"Runner infrastructure failure: {error.GetType().FullName}.", error: true);
            return 2;
        }
    }

    private static void SafeProgress(string id, Stopwatch clock)
    {
        SafeWriteLine($"Running {id}: {clock.ElapsedMilliseconds} ms elapsed.");
    }

    private static void SafeWriteLine(string value, bool error = false)
    {
        try { (error ? Console.Error : Console.Out).WriteLine(value); }
        catch { /* Diagnostics cannot replace the execution or persistence outcome. */ }
    }
}
