// Keep the physical pipe writer independent of suites that temporarily redirect
// global Console.Out/Error while checking the in-process runner contract.
var physicalProtocolOutput = Console.Out;
using var processHost = new RegressionProcessHost(progressOutput: physicalProtocolOutput);
try
{
    if (RegressionChildHost.IsChildInvocation(args))
        return await RegressionChildHost.RunAsync(args, physicalProtocolOutput);
    return await RegressionProcessHost.TryRunLegacyAsync(args, processHost)
        ?? await RegressionRunner.RunAsync(args, RegressionCatalog.Create(), processHost.ExecuteAsync);
}
catch (Exception error)
{
    // Exception messages can contain private proof paths or fixture input.
    Console.Error.WriteLine($"Regression execution failed ({error.GetType().Name}).");
    return 1;
}
