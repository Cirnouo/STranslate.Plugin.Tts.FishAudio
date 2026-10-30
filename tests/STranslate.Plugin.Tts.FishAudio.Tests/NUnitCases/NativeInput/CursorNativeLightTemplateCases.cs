using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(180)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorNativeLightTemplateCases
{
    public static IEnumerable<TestCaseData> TemplateRetainsOriginalSameSourceCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/native/light/template",
            180, "native-input/180-input.cursor-native-light-template", false);
    }

    [TestCaseSource(nameof(TemplateRetainsOriginalSameSourceCallbackRows))]
    [Order(180)]
    public Task TemplateRetainsOriginalSameSourceCallback(bool dark)
        => ControlCursorSuite.ObserveNativeTemplateAsync(dark);
}
