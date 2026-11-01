using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static StaTestHost;

// Direct typed entry points retain the original single shared-STA action.
internal static partial class VoiceCardLayoutSuite
{
    internal static Task ObserveColdConstructionAsync() => RunColdConstructionAsync();

    internal static Task ObserveCardsAsync(double width, double dpi, bool dark, string locale, VoiceBrowseMode mode) =>
        RunOnStaThreadAsync((Func<Task>)(() => InspectCards(width, dpi, dark, locale, mode, proofDirectory: null)));

    internal static Task ObserveDescriptionsAsync(double width, double dpi, bool dark, VoiceBrowseMode mode) =>
        RunOnStaThreadAsync((Func<Task>)(() => WithCards(width, dpi, dark, "en", mode, fixture => InspectDescriptions(fixture, proofDirectory: null))));

    internal static Task ObserveHeadersAsync(double width, double dpi, bool dark, VoiceBrowseMode mode) =>
        RunOnStaThreadAsync((Func<Task>)(() => WithCards(width, dpi, dark, "en", mode, InspectRuntimeHeaders)));

    internal static Task ObserveInheritanceAsync(bool dark, double dpi, VoiceBrowseMode mode) =>
        RunOnStaThreadAsync((Func<Task>)(() => WithCards(400, dpi, dark, "en", mode, InspectInheritance)));

    internal static Task ObserveDatesAsync(bool dark, VoiceBrowseMode mode) =>
        RunOnStaThreadAsync((Func<Task>)(() => WithCards(400, 1.25, dark, "en", mode, InspectDates)));

    internal static Task ObserveTooltipsAsync(VoiceBrowseMode mode) =>
        RunOnStaThreadAsync((Func<Task>)(() => WithCards(400, 1, false, "en", mode, (Func<CardFixture, Task>)(fixture => InspectTooltipsAsync(fixture)))));

    internal static Task ObserveTooltipOwnershipControlAsync(CardTooltipFixtureHooks hooks) =>
        RunOnStaThreadAsync((Func<Task>)(() => WithCards(400, 1, false, "en", VoiceBrowseMode.Search,
            fixture => InspectTooltipsAsync(fixture, hooks), hooks)));

    internal static Task ObserveLoadingAsync(bool dark, VoiceBrowseMode mode) =>
        RunOnStaThreadAsync((Func<Task>)(() => WithCards(320, 1.25, dark, "en", mode, InspectLoading)));
}
