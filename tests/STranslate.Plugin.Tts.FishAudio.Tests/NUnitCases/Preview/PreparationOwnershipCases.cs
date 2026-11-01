using NUnit.Framework;
using System.Net;
using STranslate.Plugin.Tts.FishAudio.Presentation;

namespace NUnitCases.Preview;

// Process-global network/clock overrides stay exclusive for the complete body and cleanup.
[TestFixture]
[NonParallelizable]
public sealed class PreparationOwnershipCases
{
    public static IEnumerable<TestCaseData> PreparationTransitionsToPlaybackRows()
    {
        yield return new TestCaseData(false)
            .SetName("PreparationTransitionsToPlayback_Display")
            .SetProperty("FishAudioCaseId", "preview.ownership/preview-preparation-transitions-to-real-playback/display");
        yield return new TestCaseData(true)
            .SetName("PreparationTransitionsToPlayback_Search")
            .SetProperty("FishAudioCaseId", "preview.ownership/preview-preparation-transitions-to-real-playback/search");
    }

    [TestCaseSource(nameof(PreparationTransitionsToPlaybackRows))]
    public Task PreparationTransitionsToPlayback(bool search)
        => PreviewSuite.PreparationTransitionsToPlaybackCaseAsync(search);

    public static IEnumerable<TestCaseData> SameVoiceAcrossEntriesCancelsRows()
    {
        yield return new TestCaseData(false)
            .SetName("SameVoiceAcrossEntriesCancels_Display")
            .SetProperty("FishAudioCaseId", "preview.ownership/preview-same-voice-across-entries-cancels-preparation/display");
        yield return new TestCaseData(true)
            .SetName("SameVoiceAcrossEntriesCancels_Search")
            .SetProperty("FishAudioCaseId", "preview.ownership/preview-same-voice-across-entries-cancels-preparation/search");
    }

    [TestCaseSource(nameof(SameVoiceAcrossEntriesCancelsRows))]
    public Task SameVoiceAcrossEntriesCancels(bool search)
        => PreviewSuite.SameVoiceAcrossEntriesCancelsCaseAsync(search);

    public static IEnumerable<TestCaseData> OldCompletionKeepsNewPreparationRows()
    {
        yield return new TestCaseData(false)
            .SetName("OldCompletionKeepsNewPreparation_Display")
            .SetProperty("FishAudioCaseId", "preview.ownership/preview-old-completion-cannot-clear-new-preparation/display");
        yield return new TestCaseData(true)
            .SetName("OldCompletionKeepsNewPreparation_Search")
            .SetProperty("FishAudioCaseId", "preview.ownership/preview-old-completion-cannot-clear-new-preparation/search");
    }

    [TestCaseSource(nameof(OldCompletionKeepsNewPreparationRows))]
    public Task OldCompletionKeepsNewPreparation(bool search)
        => PreviewSuite.OldCompletionKeepsNewPreparationCaseAsync(search);

    public static IEnumerable<TestCaseData> PreparationFailureClearsStateRows()
    {
        yield return new TestCaseData(false, "refresh")
            .SetName("PreparationFailure_Display_refresh")
            .SetProperty("FishAudioCaseId", "preview.ownership/preview-preparation-failure-and-reset-clear-state/failure/display/refresh");
        yield return new TestCaseData(false, "download")
            .SetName("PreparationFailure_Display_download")
            .SetProperty("FishAudioCaseId", "preview.ownership/preview-preparation-failure-and-reset-clear-state/failure/display/download");
        yield return new TestCaseData(false, "open")
            .SetName("PreparationFailure_Display_open")
            .SetProperty("FishAudioCaseId", "preview.ownership/preview-preparation-failure-and-reset-clear-state/failure/display/open");
        yield return new TestCaseData(false, "play")
            .SetName("PreparationFailure_Display_play")
            .SetProperty("FishAudioCaseId", "preview.ownership/preview-preparation-failure-and-reset-clear-state/failure/display/play");
        yield return new TestCaseData(true, "refresh")
            .SetName("PreparationFailure_Search_refresh")
            .SetProperty("FishAudioCaseId", "preview.ownership/preview-preparation-failure-and-reset-clear-state/failure/search/refresh");
        yield return new TestCaseData(true, "download")
            .SetName("PreparationFailure_Search_download")
            .SetProperty("FishAudioCaseId", "preview.ownership/preview-preparation-failure-and-reset-clear-state/failure/search/download");
        yield return new TestCaseData(true, "open")
            .SetName("PreparationFailure_Search_open")
            .SetProperty("FishAudioCaseId", "preview.ownership/preview-preparation-failure-and-reset-clear-state/failure/search/open");
        yield return new TestCaseData(true, "play")
            .SetName("PreparationFailure_Search_play")
            .SetProperty("FishAudioCaseId", "preview.ownership/preview-preparation-failure-and-reset-clear-state/failure/search/play");
    }

    [TestCaseSource(nameof(PreparationFailureClearsStateRows))]
    public Task PreparationFailureClearsState(bool search, string failure)
        => PreviewSuite.PreparationFailureClearsStateCaseAsync(search, failure);

    public static IEnumerable<TestCaseData> ResetClearsPreparationRows()
    {
        yield return new TestCaseData(false)
            .SetName("ResetClearsPreparation_Display")
            .SetProperty("FishAudioCaseId", "preview.ownership/preview-preparation-failure-and-reset-clear-state/reset/display");
        yield return new TestCaseData(true)
            .SetName("ResetClearsPreparation_Search")
            .SetProperty("FishAudioCaseId", "preview.ownership/preview-preparation-failure-and-reset-clear-state/reset/search");
    }

    [TestCaseSource(nameof(ResetClearsPreparationRows))]
    public Task ResetClearsPreparation(bool search)
        => PreviewSuite.ResetClearsPreparationCaseAsync(search);

    public static IEnumerable<TestCaseData> QueuedPublicationReadsCurrentPreparationRows()
    {
        yield return new TestCaseData()
            .SetName("QueuedPublicationReadsCurrentPreparation")
            .SetProperty("FishAudioCaseId", "preview.ownership/preview-queued-publications-read-current-preparation-owner");
    }

    [TestCaseSource(nameof(QueuedPublicationReadsCurrentPreparationRows))]
    public Task QueuedPublicationReadsCurrentPreparation()
        => PreviewSuite.QueuedPublicationReadsCurrentPreparationCaseAsync();

}
