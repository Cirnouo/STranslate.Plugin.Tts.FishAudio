using NUnit.Framework;

namespace NUnitCases.NativeInput;

[TestFixture]
[NonParallelizable]
[Order(153)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class ExtremePageMaximumCases
{
    public static IEnumerable<TestCaseData> Rows()
    {
        yield return NativeTestCaseSource.Row(
            "input.page-entry/extreme-maximum-overflow-safe",
            153, "native-input/153-page-entry-extreme-maximum-overflow-safe");
    }

    [TestCaseSource(nameof(Rows))]
    [Order(153)]
    public void ExtremePageMaximumRemainsOverflowSafe()
        => PageEntrySuite.ExtremePageMaximumRemainsOverflowSafe();
}
