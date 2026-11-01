using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCases.NativeCards;

// Source proposal. Retain the complete ordered profile before transferring ownership.
[TestFixture]
[NonParallelizable]
[Order(838)]
[Property("FishAudioExecutionProfile", "native-cards")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class AcceptedPreviewContinuityCases
{
    public static IEnumerable<TestCaseData> SelectionRows()
    {
        yield return NativeTestCaseSource.Row(
            "preview.accepted-continuity/selection/selected",
            838, "native-cards/838-preview.accepted-continuity-selection-selected", NativeAcceptedPreviewOrigin.Selected);
        yield return NativeTestCaseSource.Row(
            "preview.accepted-continuity/selection/search",
            839, "native-cards/839-preview.accepted-continuity-selection-search", NativeAcceptedPreviewOrigin.Search);
        yield return NativeTestCaseSource.Row(
            "preview.accepted-continuity/selection/by-id",
            840, "native-cards/840-preview.accepted-continuity-selection-by-id", NativeAcceptedPreviewOrigin.ById);
        yield return NativeTestCaseSource.Row(
            "preview.accepted-continuity/selection/favorite",
            841, "native-cards/841-preview.accepted-continuity-selection-favorite", NativeAcceptedPreviewOrigin.Favorite);
    }

    [TestCaseSource(nameof(SelectionRows))]
    [Order(838)]
    public Task Selection(NativeAcceptedPreviewOrigin origin) =>
        AcceptedPreviewContinuitySuite.ObserveSelectionAsync(origin);

    public static IEnumerable<TestCaseData> UnlikeRows()
    {
        yield return NativeTestCaseSource.Row(
            "preview.accepted-continuity/unlike/unapplied",
            842, "native-cards/842-preview.accepted-continuity-unlike-unapplied", false);
        yield return NativeTestCaseSource.Row(
            "preview.accepted-continuity/unlike/applied",
            843, "native-cards/843-preview.accepted-continuity-unlike-applied", true);
    }

    [TestCaseSource(nameof(UnlikeRows))]
    [Order(842)]
    public Task Unlike(bool applied) =>
        AcceptedPreviewContinuitySuite.ObserveUnlikeAsync(applied);

    public static IEnumerable<TestCaseData> FavoriteBrowsingRows()
    {
        yield return NativeTestCaseSource.Row(
            "preview.accepted-continuity/browsing/favorites",
            844, "native-cards/844-preview.accepted-continuity-browsing-favorites");
    }

    [TestCaseSource(nameof(FavoriteBrowsingRows))]
    [Order(844)]
    public Task FavoriteBrowsing() =>
        AcceptedPreviewContinuitySuite.ObserveFavoriteBrowsingAsync();

    public static IEnumerable<TestCaseData> ByIdBrowsingRows()
    {
        yield return NativeTestCaseSource.Row(
            "preview.accepted-continuity/browsing/by-id",
            845, "native-cards/845-preview.accepted-continuity-browsing-by-id");
    }

    [TestCaseSource(nameof(ByIdBrowsingRows))]
    [Order(845)]
    public Task ByIdBrowsing() =>
        AcceptedPreviewContinuitySuite.ObserveByIdBrowsingAsync();

    public static IEnumerable<TestCaseData> CollectionHandoffRows()
    {
        yield return NativeTestCaseSource.Row(
            "preview.accepted-continuity/handoff/collection/by-id",
            846, "native-cards/846-preview.accepted-continuity-handoff-collection-by-id", NativeAcceptedPreviewOrigin.ById);
        yield return NativeTestCaseSource.Row(
            "preview.accepted-continuity/handoff/collection/favorite",
            847, "native-cards/847-preview.accepted-continuity-handoff-collection-favorite", NativeAcceptedPreviewOrigin.Favorite);
    }

    [TestCaseSource(nameof(CollectionHandoffRows))]
    [Order(846)]
    public Task CollectionHandoff(NativeAcceptedPreviewOrigin origin) =>
        AcceptedPreviewContinuitySuite.ObserveCollectionHandoffAsync(origin);

    public static IEnumerable<TestCaseData> ManagementHandoffRows()
    {
        yield return NativeTestCaseSource.Row(
            "preview.accepted-continuity/handoff/management/selected",
            848, "native-cards/848-preview.accepted-continuity-handoff-management-selected", NativeAcceptedPreviewOrigin.Selected);
        yield return NativeTestCaseSource.Row(
            "preview.accepted-continuity/handoff/management/search",
            849, "native-cards/849-preview.accepted-continuity-handoff-management-search", NativeAcceptedPreviewOrigin.Search);
        yield return NativeTestCaseSource.Row(
            "preview.accepted-continuity/handoff/management/by-id",
            850, "native-cards/850-preview.accepted-continuity-handoff-management-by-id", NativeAcceptedPreviewOrigin.ById);
        yield return NativeTestCaseSource.Row(
            "preview.accepted-continuity/handoff/management/favorite",
            851, "native-cards/851-preview.accepted-continuity-handoff-management-favorite", NativeAcceptedPreviewOrigin.Favorite);
    }

    [TestCaseSource(nameof(ManagementHandoffRows))]
    [Order(848)]
    public Task ManagementHandoff(NativeAcceptedPreviewOrigin origin) =>
        AcceptedPreviewContinuitySuite.ObserveManagementHandoffAsync(origin);

    public static IEnumerable<TestCaseData> CredentialsRows()
    {
        yield return NativeTestCaseSource.Row(
            "preview.accepted-continuity/credentials",
            852, "native-cards/852-preview.accepted-continuity-credentials");
    }

    [TestCaseSource(nameof(CredentialsRows))]
    [Order(852)]
    public Task Credentials() =>
        AcceptedPreviewContinuitySuite.ObserveCredentialsAsync();

    public static IEnumerable<TestCaseData> LifecycleRows()
    {
        yield return NativeTestCaseSource.Row(
            "preview.accepted-continuity/lifecycle",
            853, "native-cards/853-preview.accepted-continuity-lifecycle");
    }

    [TestCaseSource(nameof(LifecycleRows))]
    [Order(853)]
    public Task Lifecycle() =>
        AcceptedPreviewContinuitySuite.ObserveLifecycleAsync();

    public static IEnumerable<TestCaseData> RejectedSavesRows()
    {
        yield return NativeTestCaseSource.Row(
            "preview.accepted-continuity/rejected-saves",
            854, "native-cards/854-preview.accepted-continuity-rejected-saves");
    }

    [TestCaseSource(nameof(RejectedSavesRows))]
    [Order(854)]
    public Task RejectedSaves() =>
        AcceptedPreviewContinuitySuite.ObserveRejectedSavesAsync();

}
