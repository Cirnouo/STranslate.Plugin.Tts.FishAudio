using NUnit.Framework;

namespace NUnitCases.Core;

// Metadata sources construct rows only; scene ownership starts in the test body.
[TestFixture]
[NonParallelizable]
public sealed class InputClearRequestCases
{
    public static IEnumerable<TestCaseData> ApiKeyRows()
    {
        yield return new TestCaseData(false)
            .SetName("InputClearRequests_ApiKeyAcceptedClear")
            .SetProperty("FishAudioCaseId", "input.clear/request/api-key-accepted-clear");
        yield return new TestCaseData(true)
            .SetName("InputClearRequests_ApiKeySaveFailure")
            .SetProperty("FishAudioCaseId", "input.clear/request/api-key-save-failure");
    }

    [TestCaseSource(nameof(ApiKeyRows))]
    [Order(0)]
    public Task ApiKeyClearUsesTheAcceptedSettingsOwner(bool failure)
        => InputClearSuite.RunApiClearUsesAcceptedSettingsOwnerOwnedAsync(failure);

    public static IEnumerable<TestCaseData> RejectedLeaseRows()
    {
        yield return new TestCaseData(false)
            .SetName("InputClearRequests_RejectedLeaseSaveFailure")
            .SetProperty("FishAudioCaseId", "input.clear/request/rejected-lease-save-failure");
        yield return new TestCaseData(true)
            .SetName("InputClearRequests_RejectedLeaseRetired")
            .SetProperty("FishAudioCaseId", "input.clear/request/rejected-lease-retired");
    }

    [TestCaseSource(nameof(RejectedLeaseRows))]
    [Order(1)]
    public Task RejectedLeaseClearRetainsCredentialOwnership(bool retired)
        => InputClearSuite.RunRejectedLeaseClearRetainsCredentialOwnerOwnedAsync(retired);

    public static IEnumerable<TestCaseData> SearchDraftRows()
    {
        yield return new TestCaseData()
            .SetName("InputClearRequests_SearchDraftOnly")
            .SetProperty("FishAudioCaseId", "input.clear/request/search-draft-only");
    }

    [TestCaseSource(nameof(SearchDraftRows))]
    [Order(2)]
    public Task SearchClearOnlyEditsTheVisibleDraft()
        => InputClearSuite.RunSearchClearOnlyEditsDraftOwnedAsync();

    public static IEnumerable<TestCaseData> ByIdRows()
    {
        yield return new TestCaseData(false)
            .SetName("InputClearRequests_ByIdLateSuccess")
            .SetProperty("FishAudioCaseId", "input.clear/request/by-id-late-success");
        yield return new TestCaseData(true)
            .SetName("InputClearRequests_ByIdLateFailure")
            .SetProperty("FishAudioCaseId", "input.clear/request/by-id-late-failure");
    }

    [TestCaseSource(nameof(ByIdRows))]
    [Order(3)]
    public Task ByIdClearRetiresTheOriginalInputOwner(bool lateFailure)
        => InputClearSuite.RunByIdClearRetiresItsInputOwnerOwnedAsync(lateFailure);
}
