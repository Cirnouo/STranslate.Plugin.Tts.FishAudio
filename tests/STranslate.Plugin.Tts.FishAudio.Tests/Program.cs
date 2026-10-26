try
{
    return await LegacyRegressionSelectors.TryRunAsync(args)
        ?? await RegressionRunner.RunAsync(args, RegressionCatalog.Create());
}
catch (Exception error)
{
    // Exception messages can contain private proof paths or fixture input.
    Console.Error.WriteLine($"Regression execution failed ({error.GetType().Name}).");
    return 1;
}
