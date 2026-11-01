using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCases.NativeCards;

// Source proposal. Retain the complete ordered profile before transferring ownership.
[TestFixture]
[NonParallelizable]
[Order(831)]
[Property("FishAudioExecutionProfile", "native-cards")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SearchPreviewRetentionCases
{
    public static IEnumerable<TestCaseData> DiscoveryKeepsAcceptedAudioRows()
    {
        yield return NativeTestCaseSource.Row(
            "preview.search-retention/discovery-keeps-accepted-audio",
            831, "native-cards/831-preview.search-retention-discovery-keeps-accepted-audio");
    }

    [TestCaseSource(nameof(DiscoveryKeepsAcceptedAudioRows))]
    [Order(831)]
    public Task DiscoveryKeepsAcceptedAudio() =>
        SearchPreviewRetentionSuite.ObserveDiscoveryKeepsAcceptedAudioAsync();

    public static IEnumerable<TestCaseData> PendingOriginStillRetiresRows()
    {
        yield return NativeTestCaseSource.Row(
            "preview.search-retention/pending-origin-retires",
            832, "native-cards/832-preview.search-retention-pending-origin-retires");
    }

    [TestCaseSource(nameof(PendingOriginStillRetiresRows))]
    [Order(832)]
    public Task PendingOriginStillRetires() =>
        SearchPreviewRetentionSuite.ObservePendingOriginStillRetiresAsync();

    public static IEnumerable<TestCaseData> DisplayedPendingOriginMayStillAcceptRows()
    {
        yield return NativeTestCaseSource.Row(
            "preview.search-retention/displayed-pending-origin-may-accept",
            833, "native-cards/833-preview.search-retention-displayed-pending-origin-may-accept");
    }

    [TestCaseSource(nameof(DisplayedPendingOriginMayStillAcceptRows))]
    [Order(833)]
    public Task DisplayedPendingOriginMayStillAccept() =>
        SearchPreviewRetentionSuite.ObserveDisplayedPendingOriginMayStillAcceptAsync();

    public static IEnumerable<TestCaseData> AcceptanceReentrancyKeepsTheCorrectOwnerRows()
    {
        yield return NativeTestCaseSource.Row(
            "preview.search-retention/acceptance-reentrancy-correct-owner",
            834, "native-cards/834-preview.search-retention-acceptance-reentrancy-correct-owner");
    }

    [TestCaseSource(nameof(AcceptanceReentrancyKeepsTheCorrectOwnerRows))]
    [Order(834)]
    public Task AcceptanceReentrancyKeepsTheCorrectOwner() =>
        SearchPreviewRetentionSuite.ObserveAcceptanceReentrancyKeepsTheCorrectOwnerAsync();

    public static IEnumerable<TestCaseData> AcceptedPublicationCannotRetireItsSuccessorRows()
    {
        yield return NativeTestCaseSource.Row(
            "preview.search-retention/accepted-publication-cannot-retire-successor",
            835, "native-cards/835-preview.search-retention-accepted-publication-cannot-retire-successor");
    }

    [TestCaseSource(nameof(AcceptedPublicationCannotRetireItsSuccessorRows))]
    [Order(835)]
    public Task AcceptedPublicationCannotRetireItsSuccessor() =>
        SearchPreviewRetentionSuite.ObserveAcceptedPublicationCannotRetireItsSuccessorAsync();

    public static IEnumerable<TestCaseData> ExplicitReplacementAndRetirementRows()
    {
        yield return NativeTestCaseSource.Row(
            "preview.search-retention/explicit-replacement-retirement",
            836, "native-cards/836-preview.search-retention-explicit-replacement-retirement");
    }

    [TestCaseSource(nameof(ExplicitReplacementAndRetirementRows))]
    [Order(836)]
    public Task ExplicitReplacementAndRetirement() =>
        SearchPreviewRetentionSuite.ObserveExplicitReplacementAndRetirementAsync();

    public static IEnumerable<TestCaseData> OtherOriginRemovalRetainsAcceptedAudioRows()
    {
        yield return NativeTestCaseSource.Row(
            "preview.search-retention/other-origin-removal-retains-audio",
            837, "native-cards/837-preview.search-retention-other-origin-removal-retains-audio");
    }

    [TestCaseSource(nameof(OtherOriginRemovalRetainsAcceptedAudioRows))]
    [Order(837)]
    public Task OtherOriginRemovalRetainsAcceptedAudio() =>
        SearchPreviewRetentionSuite.ObserveOtherOriginRemovalRetainsAcceptedAudioAsync();

}
