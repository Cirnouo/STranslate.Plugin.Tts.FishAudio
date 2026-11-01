using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(142)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class RepositoryLinkCases
{
    public static IEnumerable<TestCaseData> CompiledSupportUsesProjectRepositoryRows()
    {
        yield return NativeTestCaseSource.Row(
            "repository.link/compiled/en",
            142, "native-input/142-repository.link-compiled-en", "en");
        yield return NativeTestCaseSource.Row(
            "repository.link/compiled/zh-cn",
            143, "native-input/143-repository.link-compiled-zh-cn", "zh-cn");
        yield return NativeTestCaseSource.Row(
            "repository.link/compiled/zh-tw",
            144, "native-input/144-repository.link-compiled-zh-tw", "zh-tw");
        yield return NativeTestCaseSource.Row(
            "repository.link/compiled/ja",
            145, "native-input/145-repository.link-compiled-ja", "ja");
        yield return NativeTestCaseSource.Row(
            "repository.link/compiled/ko",
            146, "native-input/146-repository.link-compiled-ko", "ko");
        yield return NativeTestCaseSource.Row(
            "repository.link/compiled/ru",
            147, "native-input/147-repository.link-compiled-ru", "ru");
        yield return NativeTestCaseSource.Row(
            "repository.link/compiled/tr",
            148, "native-input/148-repository.link-compiled-tr", "tr");
    }

    [TestCaseSource(nameof(CompiledSupportUsesProjectRepositoryRows))]
    [Order(142)]
    public Task CompiledSupportUsesProjectRepository(string language)
        => StaTestHost.RunOnStaThreadAsync(() => ProjectRepositoryLinkSuite.CompiledSupportUsesProjectRepository(language));
}
