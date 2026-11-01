internal static class RegressionLegacyEntryPoint
{
    internal const string RetiredDiagnostic = "Legacy regression execution is retired; use NUnit case selection.";

    // This production boundary exports current identity metadata only. An empty
    // catalog never supplies a passing execution report or a completed baseline.
    // Program dispatches owned child and verifier protocols before reaching here.
    internal static int Run(string[] args)
    {
        if (args.Length == 1 && args[0] == "--list")
        {
            if (RegressionCatalog.Create().Count != 0)
                throw new InvalidOperationException("retired-legacy-catalog-must-be-empty");
            Console.WriteLine("Selected 0 case(s).");
            return 0;
        }
        Console.Error.WriteLine(RetiredDiagnostic);
        return 2;
    }
}
