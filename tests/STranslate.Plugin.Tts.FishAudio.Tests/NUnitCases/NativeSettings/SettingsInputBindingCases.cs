using NUnit.Framework;

namespace NUnitCases.NativeSettings;

// Source candidate only. The complete profile retains its original cold-first-use order.
[TestFixture]
[NonParallelizable]
[Order(3)]
[Property("FishAudioExecutionProfile", "native-settings")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SettingsInputBindingCases
{
    public static IEnumerable<TestCaseData> PasswordBindingRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.binding/api-key-password-input-persists-before-focus-leaves",
            3, "native-settings/003-api-key-password-input-persists-before-focus-leaves");
    }

    [TestCaseSource(nameof(PasswordBindingRows))]
    [Order(3)]
    public Task ApiKeyPasswordInputPersistsBeforeFocusLeaves()
        => SettingsInputBindingSuite.ApiKeyPasswordInputPersistsBeforeFocusLeavesAsync();
}
