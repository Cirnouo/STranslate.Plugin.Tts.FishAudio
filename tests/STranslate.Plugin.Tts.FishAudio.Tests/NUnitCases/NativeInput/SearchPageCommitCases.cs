using NUnit.Framework;

namespace NUnitCases.NativeInput;

[TestFixture]
[NonParallelizable]
[Order(151)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SearchPageCommitCases
{
    public static IEnumerable<TestCaseData> Rows()
    {
        yield return NativeTestCaseSource.Row(
            "input.page-entry/search-maximum-and-commit-transitions",
            151, "native-input/151-page-entry-search-maximum-and-commit-transitions");
    }

    [TestCaseSource(nameof(Rows))]
    [Order(151)]
    public Task SearchMaximumAndCommitTransitionsAsync()
        => StaTestHost.RunOnStaThreadAsync(() => PageEntrySuite.SearchMaximumAndCommitTransitionsAsync());
}
