using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(85)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SearchRadioPeerCases
{
    public static IEnumerable<TestCaseData> RadioAppearanceMatchesTheIndependentNativePeerRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/radio/light",
            85, "native-popups/085-popup.search-adjustment-native-radio-light", false);
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/radio/dark",
            86, "native-popups/086-popup.search-adjustment-native-radio-dark", true);
    }

    [TestCaseSource(nameof(RadioAppearanceMatchesTheIndependentNativePeerRows))]
    [Order(85)]
    public Task RadioAppearanceMatchesTheIndependentNativePeer(bool dark)
        => StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => new OrdinaryPopupProofDirectory().RunAsync(directory =>
            SearchAdjustmentControlSuite.RadioAppearanceMatchesNativePeer(dark, directory))));
}
