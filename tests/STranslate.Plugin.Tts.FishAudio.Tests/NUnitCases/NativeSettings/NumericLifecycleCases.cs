using NUnit.Framework;

namespace NUnitCases.NativeSettings;

// Source candidate only. The complete profile retains its original cold-first-use order.
[TestFixture]
[NonParallelizable]
[Order(138)]
[Property("FishAudioExecutionProfile", "native-settings")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class NumericLifecycleCases
{
    public static IEnumerable<TestCaseData> NumericLifecycleRows()
    {
        yield return NativeTestCaseSource.Row(
            "numeric.lifecycle/run",
            138, "native-settings/138-run");
    }

    [TestCaseSource(nameof(NumericLifecycleRows))]
    [Order(138)]
    public Task NumericAlignmentLifecycle()
        => NumericAlignmentLifecycleSuite.RunAsync();
}
