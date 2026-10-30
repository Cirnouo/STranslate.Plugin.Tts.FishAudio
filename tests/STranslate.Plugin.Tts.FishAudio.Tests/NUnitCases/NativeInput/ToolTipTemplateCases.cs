using NUnit.Framework;

namespace NUnitCases.NativeInput;

// Source proposal only; execution retains the original whole native callback.
[TestFixture]
[NonParallelizable]
[Order(56)]
[Property("FishAudioExecutionProfile", "native-input")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class ToolTipTemplateCases
{
    public static IEnumerable<TestCaseData> SliderWheelToolTipKeepsDragAndTemplateOwnershipAsyncRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.settings/native/tooltip-drag-template-ownership",
            56, "native-input/056-input.settings-native-tooltip-drag-template-ownership");
    }

    [TestCaseSource(nameof(SliderWheelToolTipKeepsDragAndTemplateOwnershipAsyncRows))]
    [Order(56)]
    public Task SliderWheelToolTipKeepsDragAndTemplateOwnershipAsync()
        => StaTestHost.RunOnStaThreadAsync(SettingsInputInteractionSuite.SliderWheelToolTipKeepsDragAndTemplateOwnershipAsync);
}
