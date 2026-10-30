using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(26)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class ByIdGeometryCases
{
    public static IEnumerable<TestCaseData> ByIdUsesUnchangedSearchTextGeometryRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.settings/native/by-id-text-geometry",
            26, "native-input/026-input.settings-native-by-id-text-geometry");
    }

    [TestCaseSource(nameof(ByIdUsesUnchangedSearchTextGeometryRows))]
    [Order(26)]
    public Task ByIdUsesUnchangedSearchTextGeometry()
        => StaTestHost.RunOnStaThreadAsync(SettingsInputInteractionSuite.ByIdUsesUnchangedSearchTextGeometry);
}
