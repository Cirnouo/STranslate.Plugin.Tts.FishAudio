using NUnit.Framework;
using System.Net;
using STranslate.Plugin.Tts.FishAudio.Presentation;

namespace NUnitCases.Preview;

// Process-global network/clock overrides stay exclusive for the complete body and cleanup.
[TestFixture]
[NonParallelizable]
public sealed class PreflightRefreshOwnershipCases
{
    public static IEnumerable<TestCaseData> SearchPreviewRemovedResultIgnoresLateRefreshRows()
    {
        yield return new TestCaseData()
            .SetName("SearchPreviewRemovedResultIgnoresLateRefresh")
            .SetProperty("FishAudioCaseId", "preview.ownership/search-preview-removed-result-ignores-late-refresh");
    }

    [TestCaseSource(nameof(SearchPreviewRemovedResultIgnoresLateRefreshRows))]
    public Task SearchPreviewRemovedResultIgnoresLateRefresh()
        => PreviewSuite.SearchPreviewRemovedResultIgnoresLateRefreshCaseAsync();

    public static IEnumerable<TestCaseData> SearchPreviewSecondClickCancelsPendingRefreshRows()
    {
        yield return new TestCaseData()
            .SetName("SearchPreviewSecondClickCancelsPendingRefresh")
            .SetProperty("FishAudioCaseId", "preview.ownership/search-preview-second-click-cancels-pending-refresh");
    }

    [TestCaseSource(nameof(SearchPreviewSecondClickCancelsPendingRefreshRows))]
    public Task SearchPreviewSecondClickCancelsPendingRefresh()
        => PreviewSuite.SearchPreviewSecondClickCancelsPendingRefreshCaseAsync();

    public static IEnumerable<TestCaseData> DisplayPreviewSecondClickCancelsPendingRefreshWithoutRestartRows()
    {
        yield return new TestCaseData()
            .SetName("DisplayPreviewSecondClickCancelsPendingRefreshWithoutRestart")
            .SetProperty("FishAudioCaseId", "preview.ownership/display-preview-second-click-cancels-pending-refresh-without-restart");
    }

    [TestCaseSource(nameof(DisplayPreviewSecondClickCancelsPendingRefreshWithoutRestartRows))]
    public Task DisplayPreviewSecondClickCancelsPendingRefreshWithoutRestart()
        => PreviewSuite.DisplayPreviewSecondClickCancelsPendingRefreshWithoutRestartCaseAsync();

    public static IEnumerable<TestCaseData> SearchPreviewInvalidatesPendingDisplayRefreshRows()
    {
        yield return new TestCaseData()
            .SetName("SearchPreviewInvalidatesPendingDisplayRefresh")
            .SetProperty("FishAudioCaseId", "preview.ownership/search-preview-invalidates-pending-display-refresh");
    }

    [TestCaseSource(nameof(SearchPreviewInvalidatesPendingDisplayRefreshRows))]
    public Task SearchPreviewInvalidatesPendingDisplayRefresh()
        => PreviewSuite.SearchPreviewInvalidatesPendingDisplayRefreshCaseAsync();

    public static IEnumerable<TestCaseData> DisplayPreviewVoiceSwitchIgnoresLateRefreshRows()
    {
        yield return new TestCaseData()
            .SetName("DisplayPreviewVoiceSwitchIgnoresLateRefresh")
            .SetProperty("FishAudioCaseId", "preview.ownership/display-preview-voice-switch-ignores-late-refresh");
    }

    [TestCaseSource(nameof(DisplayPreviewVoiceSwitchIgnoresLateRefreshRows))]
    public Task DisplayPreviewVoiceSwitchIgnoresLateRefresh()
        => PreviewSuite.DisplayPreviewVoiceSwitchIgnoresLateRefreshCaseAsync();

    public static IEnumerable<TestCaseData> DisplayPreviewDisposeCancelsAndIgnoresLateRefreshRows()
    {
        yield return new TestCaseData()
            .SetName("DisplayPreviewDisposeCancelsAndIgnoresLateRefresh")
            .SetProperty("FishAudioCaseId", "preview.ownership/display-preview-dispose-cancels-and-ignores-late-refresh");
    }

    [TestCaseSource(nameof(DisplayPreviewDisposeCancelsAndIgnoresLateRefreshRows))]
    public Task DisplayPreviewDisposeCancelsAndIgnoresLateRefresh()
        => PreviewSuite.DisplayPreviewDisposeCancelsAndIgnoresLateRefreshCaseAsync();

}
