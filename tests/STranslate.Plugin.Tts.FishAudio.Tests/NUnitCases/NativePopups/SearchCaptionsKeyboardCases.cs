using NUnit.Framework;

namespace NUnitCases.NativePopups;

// Source proposal; unresolved original lifecycle gaps are recorded separately.
[TestFixture]
[NonParallelizable]
[Order(84)]
[Property("FishAudioExecutionProfile", "native-popups")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SearchCaptionsKeyboardCases
{
    public static IEnumerable<TestCaseData> CaptionsAndKeyboardApplyRetainTheirOriginalChainRows()
    {
        yield return NativeTestCaseSource.Row(
            "popup.search-adjustment/native/captions-keyboard-apply",
            84, "native-popups/084-popup.search-adjustment-native-captions-keyboard-apply");
    }

    [TestCaseSource(nameof(CaptionsAndKeyboardApplyRetainTheirOriginalChainRows))]
    [Order(84)]
    public Task CaptionsAndKeyboardApplyRetainTheirOriginalChain()
        => StaTestHost.RunOnStaThreadAsync(SearchAdjustmentControlSuite.CaptionsAndKeyboardApplyAsync);
}
