internal static class RegressionSnapshotVerifier
{
    internal static bool IsInvocation(string[] args) => args.Length > 0 && args[0] == "--verify-snapshot-report";

    // Metadata only: never invoke a scenario while checking completed evidence.
    internal static int Run(string[] args)
    {
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
}
