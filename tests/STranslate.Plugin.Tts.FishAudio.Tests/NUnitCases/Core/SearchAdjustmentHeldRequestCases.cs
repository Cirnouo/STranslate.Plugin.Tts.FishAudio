using NUnit.Framework;

namespace NUnitCases.Core;

// Metadata sources construct rows only; scene ownership starts in the test body.
[TestFixture]
[NonParallelizable]
public sealed class SearchAdjustmentHeldRequestCases
{
    public static IEnumerable<TestCaseData> PendingIntentRows()
    {
        yield return new TestCaseData()
            .SetName("SearchAdjustmentHeld_PendingIntent")
            .SetProperty("FishAudioCaseId", "voice.search-adjustment/pending-intent-is-independent-of-successful-criteria");
    }

    [TestCaseSource(nameof(PendingIntentRows))]
    [Order(0)]
    public Task PendingIntentRemainsIndependentOfSuccessfulCriteria()
        => SearchAdjustmentSuite.RunPendingIntentIsIndependentOfSuccessfulCriteriaOwnedAsync();

    public static IEnumerable<TestCaseData> PendingReplacementRows()
    {
        yield return new TestCaseData("success")
            .SetName("SearchAdjustmentHeld_PendingReplacement_success")
            .SetProperty("FishAudioCaseId", "voice.search-adjustment/pending-replacements-cannot-roll-back-intent/success");
        yield return new TestCaseData("failure")
            .SetName("SearchAdjustmentHeld_PendingReplacement_failure")
            .SetProperty("FishAudioCaseId", "voice.search-adjustment/pending-replacements-cannot-roll-back-intent/failure");
        yield return new TestCaseData("credentials")
            .SetName("SearchAdjustmentHeld_PendingReplacement_credentials")
            .SetProperty("FishAudioCaseId", "voice.search-adjustment/pending-replacements-cannot-roll-back-intent/credentials");
    }

    [TestCaseSource(nameof(PendingReplacementRows))]
    [Order(1)]
    public Task PendingReplacementCannotRollBackAppliedIntent(string outcome)
        => SearchAdjustmentSuite.RunPendingReplacementsCannotRollBackIntentOwnedAsync(outcome);

    public static IEnumerable<TestCaseData> RetiredCriteriaRows()
    {
        yield return new TestCaseData("replacement")
            .SetName("SearchAdjustmentHeld_RetiredCriteria_replacement")
            .SetProperty("FishAudioCaseId", "voice.search-adjustment/retired-criteria-cannot-publish/replacement");
        yield return new TestCaseData("credentials")
            .SetName("SearchAdjustmentHeld_RetiredCriteria_credentials")
            .SetProperty("FishAudioCaseId", "voice.search-adjustment/retired-criteria-cannot-publish/credentials");
        yield return new TestCaseData("dispose")
            .SetName("SearchAdjustmentHeld_RetiredCriteria_dispose")
            .SetProperty("FishAudioCaseId", "voice.search-adjustment/retired-criteria-cannot-publish/dispose");
    }

    [TestCaseSource(nameof(RetiredCriteriaRows))]
    [Order(2)]
    public Task RetiredCriteriaCannotPublish(string retirement)
        => SearchAdjustmentSuite.RunRetiredCriteriaCannotPublishOwnedAsync(retirement);
}
