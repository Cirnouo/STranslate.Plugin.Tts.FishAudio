using NUnit.Framework;

namespace NUnitCases.Core;

// Complete original scenario responsibilities use framework discovery and execution.
[TestFixture]
[NonParallelizable]
public sealed class SearchAdjustmentRequestCases
{
    [Test]
    [Property("FishAudioCaseId", "voice.search-adjustment/multiple-languages-are-one-detached-request")]
    public Task MultipleLanguagesAreOneDetachedRequest()
        => SearchAdjustmentSuite.MultipleLanguagesAreOneDetachedRequestAsync();

    [Test]
    [Property("FishAudioCaseId", "voice.search-adjustment/requests-capture-language-and-explicit-sort")]
    public Task RequestsCaptureLanguageAndExplicitSort()
        => SearchAdjustmentSuite.RequestsCaptureLanguageAndExplicitSortAsync();

    [Test]
    [Property("FishAudioCaseId", "voice.search-adjustment/applied-criteria-survive-failure-and-draft-edits")]
    public Task AppliedCriteriaSurviveFailureAndDraftEdits()
        => SearchAdjustmentSuite.AppliedCriteriaSurviveFailureAndDraftEditsAsync();

    [Test]
    [Property("FishAudioCaseId", "voice.search-adjustment/queued-publication-rechecks-its-owner")]
    public Task QueuedPublicationRechecksItsOwner()
        => SearchAdjustmentSuite.QueuedPublicationRechecksItsOwnerAsync();

    [Test]
    [Property("FishAudioCaseId", "voice.search-adjustment/malformed-apply-retains-successful-snapshot")]
    public Task MalformedApplyRetainsSuccessfulSnapshot()
        => SearchAdjustmentSuite.MalformedApplyRetainsSuccessfulSnapshotAsync();

    [Test]
    [Property("FishAudioCaseId", "voice.search-adjustment/cover-failure-cannot-split-successful-snapshot")]
    public Task CoverFailureCannotSplitSuccessfulSnapshot()
        => SearchAdjustmentSuite.CoverFailureCannotSplitSuccessfulSnapshotAsync();
}
