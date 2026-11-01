using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCases.NativeCards;

// Source proposal. Retain the complete ordered profile before transferring ownership.
[TestFixture]
[NonParallelizable]
[Order(0)]
[Property("FishAudioExecutionProfile", "native-cards")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CompiledTemplateAnchorCases
{
    public static IEnumerable<TestCaseData> SharedCardTemplateIsCompiledRows()
    {
        yield return NativeTestCaseSource.Row(
            "cards.like/shared-card-template-is-compiled",
            0, "native-cards/000-cards.like-shared-card-template-is-compiled");
    }

    [TestCaseSource(nameof(SharedCardTemplateIsCompiledRows))]
    [Order(0)]
    public Task SharedCardTemplateIsCompiled() =>
        VoiceLikeSuite.SharedCardTemplateIsCompiledAsync();

}
