using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(159)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class DirectionLanguageLifetimeCases
{
    public static IEnumerable<TestCaseData> GeometryLifetimeKeepsReopenReversalRefusalAndRestorationContinuousRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.direction/language/geometry-lifetime",
            159, "native-popups/159-popup.direction-language-geometry-lifetime");
    }

    [TestCaseSource(nameof(GeometryLifetimeKeepsReopenReversalRefusalAndRestorationContinuousRows))]
    [Order(159)]
    public Task GeometryLifetimeKeepsReopenReversalRefusalAndRestorationContinuous()
        => PopupDirectionSuite.ObserveGeometryLifetimeAsync(true);
}
