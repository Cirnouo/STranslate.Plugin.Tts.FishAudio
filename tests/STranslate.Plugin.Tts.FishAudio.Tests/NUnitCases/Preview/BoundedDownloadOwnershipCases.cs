using NUnit.Framework;
using System.Net;
using STranslate.Plugin.Tts.FishAudio.Presentation;

namespace NUnitCases.Preview;

// Process-global network/clock overrides stay exclusive for the complete body and cleanup.
[TestFixture]
[NonParallelizable]
public sealed class BoundedDownloadOwnershipCases
{
    public static IEnumerable<TestCaseData> RejectsInvalidByteSizeRows()
    {
        yield return new TestCaseData(false, 0)
            .SetName("RejectsByteSize_Display_Empty")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/reject-size/display/empty");
        yield return new TestCaseData(false, PreviewAudioDownloader.MaximumBytes + 128)
            .SetName("RejectsByteSize_Display_OverCap")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/reject-size/display/over-cap");
        yield return new TestCaseData(true, 0)
            .SetName("RejectsByteSize_Search_Empty")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/reject-size/search/empty");
        yield return new TestCaseData(true, PreviewAudioDownloader.MaximumBytes + 128)
            .SetName("RejectsByteSize_Search_OverCap")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/reject-size/search/over-cap");
    }

    [TestCaseSource(nameof(RejectsInvalidByteSizeRows))]
    public Task RejectsInvalidByteSize(bool search, int size)
        => PreviewSuite.RejectedPreviewSizeCaseAsync(search, size);

    public static IEnumerable<TestCaseData> AcceptsExactCapRows()
    {
        yield return new TestCaseData(false)
            .SetName("AcceptsExactCap_Display")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/exact-cap/display");
        yield return new TestCaseData(true)
            .SetName("AcceptsExactCap_Search")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/exact-cap/search");
    }

    [TestCaseSource(nameof(AcceptsExactCapRows))]
    public Task AcceptsExactCap(bool search)
        => PreviewSuite.ExactCapPreviewCaseAsync(search);

    public static IEnumerable<TestCaseData> RefreshesForbiddenOrMissingResourceRows()
    {
        yield return new TestCaseData(false, HttpStatusCode.Forbidden)
            .SetName("RefreshesResource_Display_Forbidden")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/resource-refresh/display/forbidden");
        yield return new TestCaseData(false, HttpStatusCode.NotFound)
            .SetName("RefreshesResource_Display_NotFound")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/resource-refresh/display/notfound");
        yield return new TestCaseData(true, HttpStatusCode.Forbidden)
            .SetName("RefreshesResource_Search_Forbidden")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/resource-refresh/search/forbidden");
        yield return new TestCaseData(true, HttpStatusCode.NotFound)
            .SetName("RefreshesResource_Search_NotFound")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/resource-refresh/search/notfound");
    }

    [TestCaseSource(nameof(RefreshesForbiddenOrMissingResourceRows))]
    public Task RefreshesForbiddenOrMissingResource(bool search, HttpStatusCode status)
        => PreviewSuite.RefreshedResourcePreviewCaseAsync(search, status);

    public static IEnumerable<TestCaseData> TerminalResourceFailureIsBoundedRows()
    {
        yield return new TestCaseData(false, HttpStatusCode.Unauthorized)
            .SetName("TerminalResourceFailure_Display_Unauthorized")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/terminal-resource-failure/display/unauthorized");
        yield return new TestCaseData(false, HttpStatusCode.InternalServerError)
            .SetName("TerminalResourceFailure_Display_InternalServerError")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/terminal-resource-failure/display/internalservererror");
        yield return new TestCaseData(false, HttpStatusCode.Forbidden)
            .SetName("TerminalResourceFailure_Display_Forbidden")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/terminal-resource-failure/display/forbidden");
        yield return new TestCaseData(true, HttpStatusCode.Unauthorized)
            .SetName("TerminalResourceFailure_Search_Unauthorized")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/terminal-resource-failure/search/unauthorized");
        yield return new TestCaseData(true, HttpStatusCode.InternalServerError)
            .SetName("TerminalResourceFailure_Search_InternalServerError")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/terminal-resource-failure/search/internalservererror");
        yield return new TestCaseData(true, HttpStatusCode.Forbidden)
            .SetName("TerminalResourceFailure_Search_Forbidden")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/terminal-resource-failure/search/forbidden");
    }

    [TestCaseSource(nameof(TerminalResourceFailureIsBoundedRows))]
    public Task TerminalResourceFailureIsBounded(bool search, HttpStatusCode status)
        => PreviewSuite.TerminalResourceFailurePreviewCaseAsync(search, status);

    public static IEnumerable<TestCaseData> PendingDownloadCancellationOwnsLateResponseRows()
    {
        yield return new TestCaseData(false, "second-click")
            .SetName("PendingCancellation_Display_second_click")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/pending-cancellation/display/second-click");
        yield return new TestCaseData(false, "voice-change")
            .SetName("PendingCancellation_Display_voice_change")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/pending-cancellation/display/voice-change");
        yield return new TestCaseData(false, "dispose")
            .SetName("PendingCancellation_Display_dispose")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/pending-cancellation/display/dispose");
        yield return new TestCaseData(true, "second-click")
            .SetName("PendingCancellation_Search_second_click")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/pending-cancellation/search/second-click");
        yield return new TestCaseData(true, "voice-change")
            .SetName("PendingCancellation_Search_voice_change")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/pending-cancellation/search/voice-change");
        yield return new TestCaseData(true, "dispose")
            .SetName("PendingCancellation_Search_dispose")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/pending-cancellation/search/dispose");
        yield return new TestCaseData(true, "results-replaced")
            .SetName("PendingCancellation_Search_results_replaced")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/pending-cancellation/search/results-replaced");
    }

    [TestCaseSource(nameof(PendingDownloadCancellationOwnsLateResponseRows))]
    public Task PendingDownloadCancellationOwnsLateResponse(bool search, string trigger)
        => PreviewSuite.PendingDownloadCancellationCaseAsync(search, trigger);

    public static IEnumerable<TestCaseData> SwitchEntryRejectsLateDownloadRows()
    {
        yield return new TestCaseData()
            .SetName("SwitchEntryRejectsLateDownload")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/cross-entry-late-download");
    }

    [TestCaseSource(nameof(SwitchEntryRejectsLateDownloadRows))]
    public Task SwitchEntryRejectsLateDownload()
        => PreviewSuite.SwitchEntryRejectsLateDownloadCaseAsync();

    public static IEnumerable<TestCaseData> PreparationSharesOneOriginalDeadlineRows()
    {
        yield return new TestCaseData()
            .SetName("PreparationSharesOneOriginalDeadline")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/original-shared-deadline");
    }

    [TestCaseSource(nameof(PreparationSharesOneOriginalDeadlineRows))]
    public Task PreparationSharesOneOriginalDeadline()
        => PreviewSuite.PreparationUsesOneDeadlineCaseAsync();

    public static IEnumerable<TestCaseData> LocalPlayerStateAndFailureRows()
    {
        yield return new TestCaseData("none")
            .SetName("LocalPlayerState_none")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/local-player/none");
        yield return new TestCaseData("factory")
            .SetName("LocalPlayerState_factory")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/local-player/factory");
        yield return new TestCaseData("open")
            .SetName("LocalPlayerState_open")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/local-player/open");
        yield return new TestCaseData("play")
            .SetName("LocalPlayerState_play")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/local-player/play");
        yield return new TestCaseData("synchronous-end")
            .SetName("LocalPlayerState_synchronous_end")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/local-player/synchronous-end");
    }

    [TestCaseSource(nameof(LocalPlayerStateAndFailureRows))]
    public Task LocalPlayerStateAndFailure(string failure)
        => PreviewSuite.LocalPlayerStateAndFailureCaseAsync(failure);

    public static IEnumerable<TestCaseData> QueuedStalePlayerEventCannotResetSuccessorRows()
    {
        yield return new TestCaseData()
            .SetName("QueuedStalePlayerEventCannotResetSuccessor")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/queued-stale-player-event");
    }

    [TestCaseSource(nameof(QueuedStalePlayerEventCannotResetSuccessorRows))]
    public Task QueuedStalePlayerEventCannotResetSuccessor()
        => PreviewSuite.QueuedStalePlayerEventCaseAsync();

    public static IEnumerable<TestCaseData> AcceptedPlaybackSurvivesResultsReplacementRows()
    {
        yield return new TestCaseData()
            .SetName("AcceptedPlaybackSurvivesResultsReplacement")
            .SetProperty("FishAudioCaseId", "preview.ownership/bounded/accepted-playback-results-replaced");
    }

    [TestCaseSource(nameof(AcceptedPlaybackSurvivesResultsReplacementRows))]
    public Task AcceptedPlaybackSurvivesResultsReplacement()
        => PreviewSuite.AcceptedPreviewSurvivesResultReplacementCaseAsync();

}
