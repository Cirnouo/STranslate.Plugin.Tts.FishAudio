using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(182)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class CursorNativeDarkTemplateCases
{
    public static IEnumerable<TestCaseData> TemplateRetainsOriginalSameSourceCallbackRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.cursor/native/dark/template",
            182, "native-input/182-input.cursor-native-dark-template", true);
    }

    [TestCaseSource(nameof(TemplateRetainsOriginalSameSourceCallbackRows))]
    [Order(182)]
    public Task TemplateRetainsOriginalSameSourceCallback(bool dark)
        => ControlCursorSuite.ObserveNativeTemplateAsync(dark);
}
