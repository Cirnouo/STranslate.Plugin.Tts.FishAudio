using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(152)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class HeadingOptionalFailureCases
{
    public static IEnumerable<TestCaseData> OptionalHeadingFailureRetainsMaterialAndInputRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.heading/optional-failure/read",
            152, "native-popups/152-popup.heading-optional-failure-read", "read");
        yield return NativeTestCaseSource.Row(
            "popup.heading/optional-failure/write",
            153, "native-popups/153-popup.heading-optional-failure-write", "write");
        yield return NativeTestCaseSource.Row(
            "popup.heading/optional-failure/update",
            154, "native-popups/154-popup.heading-optional-failure-update", "update");
    }

    [TestCaseSource(nameof(OptionalHeadingFailureRetainsMaterialAndInputRows))]
    [Order(152)]
    public Task OptionalHeadingFailureRetainsMaterialAndInput(string stage)
        => StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => PopupHeadingSuite.HeadingFailureDoesNotAffectMaterialOrInput(stage)));
}
