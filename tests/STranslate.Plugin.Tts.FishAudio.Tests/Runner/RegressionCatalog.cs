internal static class RegressionCatalog
{
    // Every historical responsibility has a mapped framework owner. This empty
    // current identity catalog is metadata, never a successful execution result.
    // Explicit historical descriptors belong only to internal contract fixtures.
    internal static IReadOnlyList<RegressionCase> Create() => Array.Empty<RegressionCase>();
}
