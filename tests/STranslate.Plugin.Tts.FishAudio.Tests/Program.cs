// Keep the physical pipe writer independent of suites that temporarily redirect
// global Console.Out/Error while checking the in-process runner contract.
var physicalProtocolOutput = Console.Out;
try
{
    if (RegressionChildHost.IsChildInvocation(args))
        return await RegressionChildHost.RunAsync(args, physicalProtocolOutput);
    if (RegressionCiVerifier.IsInvocation(args))
        return await RegressionCiVerifier.RunAsync(args);
    using var processHost = new RegressionProcessHost(progressOutput: physicalProtocolOutput);
    var legacy = await RegressionProcessHost.TryRunLegacyAsync(args, processHost);
    return legacy ?? await RegressionRunner.RunAsync(args, RegressionCatalog.Create(), processHost.ExecuteAsync, RegressionCiIdentity.Capture());
}
catch (Exception error)
{
    // Exception messages can contain private proof paths or fixture input.
    Console.Error.WriteLine($"Regression execution failed ({error.GetType().Name}).");
    return 1;
}
