using NUnit.Framework;

namespace NUnitCases.NativeSettings;

// Source candidate only. The complete profile retains its original cold-first-use order.
[TestFixture]
[NonParallelizable]
[Order(126)]
[Property("FishAudioExecutionProfile", "native-settings")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class NumericBottomCases
{
    public static IEnumerable<TestCaseData> NumericBottomRows()
    {
        yield return NativeTestCaseSource.Row(
            "numeric.bottom/en-us/light/dpi-1",
            126, "native-settings/126-en-us-light-dpi-1", "en-US", false, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.bottom/en-us/light/dpi-1-25",
            127, "native-settings/127-en-us-light-dpi-1-25", "en-US", false, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.bottom/en-us/light/dpi-1-5",
            128, "native-settings/128-en-us-light-dpi-1-5", "en-US", false, 1.5);
        yield return NativeTestCaseSource.Row(
            "numeric.bottom/en-us/dark/dpi-1",
            129, "native-settings/129-en-us-dark-dpi-1", "en-US", true, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.bottom/en-us/dark/dpi-1-25",
            130, "native-settings/130-en-us-dark-dpi-1-25", "en-US", true, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.bottom/en-us/dark/dpi-1-5",
            131, "native-settings/131-en-us-dark-dpi-1-5", "en-US", true, 1.5);
        yield return NativeTestCaseSource.Row(
            "numeric.bottom/de-de/light/dpi-1",
            132, "native-settings/132-de-de-light-dpi-1", "de-DE", false, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.bottom/de-de/light/dpi-1-25",
            133, "native-settings/133-de-de-light-dpi-1-25", "de-DE", false, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.bottom/de-de/light/dpi-1-5",
            134, "native-settings/134-de-de-light-dpi-1-5", "de-DE", false, 1.5);
        yield return NativeTestCaseSource.Row(
            "numeric.bottom/de-de/dark/dpi-1",
            135, "native-settings/135-de-de-dark-dpi-1", "de-DE", true, 1d);
        yield return NativeTestCaseSource.Row(
            "numeric.bottom/de-de/dark/dpi-1-25",
            136, "native-settings/136-de-de-dark-dpi-1-25", "de-DE", true, 1.25);
        yield return NativeTestCaseSource.Row(
            "numeric.bottom/de-de/dark/dpi-1-5",
            137, "native-settings/137-de-de-dark-dpi-1-5", "de-DE", true, 1.5);
    }

    [TestCaseSource(nameof(NumericBottomRows))]
    [Order(126)]
    public Task VisibleNumericAndUnitBottomsAlign(string culture, bool dark, double dpi)
        => StaTestHost.RunOnStaThreadAsync(() =>
        {
            var failures = new List<string>();
            NumericBottomSuite.Measure(culture, dark, dpi, null, failures);
            TestAssertions.AssertEqual("", string.Join("\n", failures), "Visible numeric and unit bottoms must align");
        });
}
