using NUnit.Framework;

namespace NUnitCases.NativeInput;

[TestFixture]
[NonParallelizable]
[Order(152)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class FavoritePageCommitCases
{
    public static IEnumerable<TestCaseData> Rows()
    {
        yield return NativeTestCaseSource.Row(
            "input.page-entry/favorite-commits-preserve-pending-owner",
            152, "native-input/152-page-entry-favorite-commits-preserve-pending-owner");
    }

    [TestCaseSource(nameof(Rows))]
    [Order(152)]
    public Task FavoriteCommitsPreservePendingOwnerAsync()
        => StaTestHost.RunOnStaThreadAsync(() => PageEntrySuite.FavoriteCommitsPreservePendingOwnerAsync());
}
