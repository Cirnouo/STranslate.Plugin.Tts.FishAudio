internal static class RegressionSnapshotVerifier
{
    internal static bool IsInvocation(string[] args) => args.Length > 0 && args[0] is "--verify-snapshot-report" or "--verify-snapshot-partition-report";

    // Metadata only: never invoke a scenario while checking completed evidence.
    internal static int Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "--verify-snapshot-partition-report") return RunPartition(args);
        if (args.Length != 3 && (args.Length != 5 || args[3] != "--catalog-output" ||
            !Path.IsPathFullyQualified(args[4]))) return 2;
        var snapshot = SnapshotContext.Read() ?? throw new InvalidDataException("Missing snapshot owner.");
        var report = RegressionReport.Read(args[1]);
        var catalog = RegressionCatalog.Create();
        if (snapshot.Tree != args[2] || !report.Complete || report.FinishedUtc is null || report.Generation <= 0 ||
            !RegressionProcessProtocol.SameSource(report.Source, snapshot.Identity()) ||
            report.Configuration != "Release" || report.Configuration != RegressionReport.BuildMetadata("RegressionBuildConfiguration") ||
            report.Sdk != RegressionReport.BuildMetadata("RegressionBuildSdk") || report.Cases.Count != catalog.Count ||
            !report.Cases.Select(item => (item.Id, item.Partition)).SequenceEqual(catalog.Select(item => (item.Id, item.Partition))) ||
            report.Cases.Any(item => item.Outcome != "passed"))
            throw new InvalidDataException("Incomplete snapshot catalog evidence.");
        if (args.Length == 5)
        {
            // Export the current same-build catalog only after authoritative
            // generation-aware passing-result admission. Later consumers never
            // infer ownership from a mutable report projection.
            using var output = new FileStream(args[4], FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            System.Text.Json.JsonSerializer.Serialize(output, new
            {
                schemaVersion = 1, kind = "verified-legacy-catalog", sourceTree = snapshot.Tree,
                cases = catalog.Select(item => new { id = item.Id, partition = item.Partition }).ToArray(),
            });
        }
        Console.WriteLine($"Verified complete snapshot catalog ({catalog.Count} cases).");
        return 0;
    }

    // Admit one unchanged authoritative report generation for one current
    // nonempty legacy slot. The outer consumer owns the complete hybrid union.
    private static int RunPartition(string[] args)
    {
        if ((args.Length != 5 && args.Length != 7) || args[3] != "--partition" ||
            !RegressionProcessProtocol.Partitions.Contains(args[4], StringComparer.Ordinal) ||
            !Path.IsPathFullyQualified(args[1]) ||
            (args.Length == 7 && (args[5] != "--catalog-output" || !Path.IsPathFullyQualified(args[6])))) return 2;
        var fullCatalog = RegressionCatalog.Create();
        var source = RegressionSource.CaptureAsync().GetAwaiter().GetResult();
        // Match the actual producer: dirty scope describes the full current
        // legacy catalog before any partition selection, not this report slice.
        source.RecordDirtyScope(fullCatalog);
        var identity = source.Identity;
        var report = RegressionReport.Read(args[1]);
        var partition = args[4];
        var catalog = fullCatalog.Where(item => item.Partition == partition).ToArray();
        if (!identity.Available || catalog.Length == 0 || identity.Tree != args[2] || !report.Complete || report.FinishedUtc is null || report.Generation <= 0 ||
            !RegressionProcessProtocol.SameSource(report.Source, identity) ||
            report.Configuration != "Release" || report.Configuration != RegressionReport.BuildMetadata("RegressionBuildConfiguration") ||
            report.Sdk != RegressionReport.BuildMetadata("RegressionBuildSdk") || report.Cases.Count != catalog.Length ||
            !report.Cases.Select(item => (item.Id, item.Partition)).SequenceEqual(catalog.Select(item => (item.Id, item.Partition))) ||
            report.Cases.Any(item => item.Outcome != "passed"))
            throw new InvalidDataException("Incomplete snapshot partition evidence.");
        if (args.Length == 7)
        {
            using var output = new FileStream(args[6], FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            System.Text.Json.JsonSerializer.Serialize(output, new
            {
                schemaVersion = 1, kind = "verified-legacy-partition-catalog", sourceTree = identity.Tree, source = identity, partition,
                cases = catalog.Select(item => new { id = item.Id, partition = item.Partition }).ToArray(),
                reportRunId = report.RunId, reportGeneration = report.Generation,
            }, RegressionReport.JsonOptions);
        }
        Console.WriteLine($"Verified complete snapshot partition ({partition}; {catalog.Length} cases).");
        return 0;
    }
}
