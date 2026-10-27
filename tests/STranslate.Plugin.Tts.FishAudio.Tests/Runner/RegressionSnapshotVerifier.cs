internal static class RegressionSnapshotVerifier
{
    internal static bool IsInvocation(string[] args) => args.Length > 0 && args[0] == "--verify-snapshot-report";

    // Metadata only: never invoke a scenario while checking completed evidence.
    internal static int Run(string[] args)
    {
        if (args.Length != 3) return 2;
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
        Console.WriteLine($"Verified complete snapshot catalog ({catalog.Count} cases).");
        return 0;
    }
}
