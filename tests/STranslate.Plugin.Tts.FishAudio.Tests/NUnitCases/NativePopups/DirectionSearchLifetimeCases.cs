using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(157)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class DirectionSearchLifetimeCases
{
    public static IEnumerable<TestCaseData> GeometryLifetimeKeepsReopenReversalRefusalAndRestorationContinuousRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.direction/search/geometry-lifetime",
            157, "native-popups/157-popup.direction-search-geometry-lifetime");
    }

    [TestCaseSource(nameof(GeometryLifetimeKeepsReopenReversalRefusalAndRestorationContinuousRows))]
    [Order(157)]
    public Task GeometryLifetimeKeepsReopenReversalRefusalAndRestorationContinuous()
        => PopupDirectionSuite.ObserveGeometryLifetimeAsync(false);
}
