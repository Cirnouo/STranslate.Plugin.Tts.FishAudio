using NUnit.Framework;

namespace NUnitCases.NativeInput;

[TestFixture]
[NonParallelizable]
[Order(149)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CompiledPageFieldCases
{
    public static IEnumerable<TestCaseData> Rows()
    {
        yield return NativeTestCaseSource.Row(
            "input.page-entry/fields/search",
            149, "native-input/149-page-entry-fields-search", "PageInputBox", "PageInput");
        yield return NativeTestCaseSource.Row(
            "input.page-entry/fields/favorites",
            150, "native-input/150-page-entry-fields-favorites", "FavoritePageInputBox", "FavoritePageInput");
    }

    [TestCaseSource(nameof(Rows))]
    [Order(149)]
    public Task CompiledFieldsConstrainWholeDraftsAsync(string name, string source)
        => StaTestHost.RunOnStaThreadAsync(() => PageEntrySuite.CompiledFieldsConstrainWholeDraftsAsync(name, source));
}
