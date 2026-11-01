// Keep the physical pipe writer independent of suites that temporarily redirect
// global Console.Out/Error while checking the in-process runner contract.
var physicalProtocolOutput = Console.Out;
try
{
    if (RegressionSnapshotVerifier.IsInvocation(args))
        return RegressionSnapshotVerifier.Run(args);
    if (RegressionChildHost.IsChildInvocation(args))
        return await RegressionChildHost.RunAsync(args, physicalProtocolOutput);
    if (ChildCoverageVerifier.IsInvocation(args))
        return ChildCoverageVerifier.Run(args);
    if (RegressionCiVerifier.IsInvocation(args))
        return await RegressionCiVerifier.RunAsync(args);
    // VSTest loads the assembly without executing Main. The owned child and
    // verifier protocols above remain separate from retired parent execution.
    var legacyArguments = args.Length != 0 && args[0] == "--legacy" ? args[1..] : args;
    return RegressionLegacyEntryPoint.Run(legacyArguments);
}
catch (Exception error)
{
    // Exception messages can contain private proof paths or fixture input.
    Console.Error.WriteLine($"Regression execution failed ({error.GetType().Name}).");
    return 1;
}
