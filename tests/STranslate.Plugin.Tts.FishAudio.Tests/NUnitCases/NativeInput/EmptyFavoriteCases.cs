using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(57)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class EmptyFavoriteCases
{
    public static IEnumerable<TestCaseData> EmptyFavoritesStayVisibleOutsideCollapsedResultsRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.settings/native/empty-favorites",
            57, "native-input/057-input.settings-native-empty-favorites");
    }

    [TestCaseSource(nameof(EmptyFavoritesStayVisibleOutsideCollapsedResultsRows))]
    [Order(57)]
    public Task EmptyFavoritesStayVisibleOutsideCollapsedResults()
        => RunEmptyFavoriteAsync();

    private static Task RunEmptyFavoriteAsync() => StaTestHost.RunOnStaThreadAsync(() =>
    {
        var owner = new HeldFavoritePageFixtureOwner();
        return owner.RunAsync(() =>
        {
            var directory = TestAssertions.CreateTempDirectory();
            owner.DirectoryPath = directory;
            SettingsInputInteractionSuite.EmptyFavoritesStayVisibleOutsideCollapsedResults(directory, owner);
            return Task.CompletedTask;
        });
    });
}
