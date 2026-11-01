internal static partial class AcceptedPreviewContinuitySuite
{
    internal static Task ObserveSelectionAsync(NativeAcceptedPreviewOrigin origin) =>
        StaTestHost.RunOnStaThreadAsync(() => SelectionAsync(ToScenarioOrigin(origin), new NativeCardsAsyncFixtureFactory()));

    internal static Task ObserveUnlikeAsync(bool applied) =>
        StaTestHost.RunOnStaThreadAsync(() => UnlikeAsync(applied, new NativeCardsAsyncFixtureFactory()));

    internal static Task ObserveFavoriteBrowsingAsync() =>
        StaTestHost.RunOnStaThreadAsync(() => FavoriteBrowsingAsync(new NativeCardsAsyncFixtureFactory()));

    internal static Task ObserveByIdBrowsingAsync() =>
        StaTestHost.RunOnStaThreadAsync(() => ByIdBrowsingAsync(new NativeCardsAsyncFixtureFactory()));

    internal static Task ObserveCollectionHandoffAsync(NativeAcceptedPreviewOrigin origin) =>
        StaTestHost.RunOnStaThreadAsync(() => CollectionHandoffAsync(ToScenarioOrigin(origin), new NativeCardsAsyncFixtureFactory()));

    internal static Task ObserveManagementHandoffAsync(NativeAcceptedPreviewOrigin origin) =>
        StaTestHost.RunOnStaThreadAsync(() => ManagementHandoffAsync(ToScenarioOrigin(origin), new NativeCardsAsyncFixtureFactory()));

    internal static Task ObserveCredentialsAsync() =>
        StaTestHost.RunOnStaThreadAsync(() => CredentialsAsync(new NativeCardsAsyncFixtureFactory()));

    internal static Task ObserveLifecycleAsync() =>
        StaTestHost.RunOnStaThreadAsync(() => LifecycleAsync(new NativeCardsAsyncFixtureFactory()));

    internal static Task ObserveRejectedSavesAsync() =>
        StaTestHost.RunOnStaThreadAsync(() => RejectedSavesAsync(new NativeCardsAsyncFixtureFactory()));

    private static Origin ToScenarioOrigin(NativeAcceptedPreviewOrigin origin) => origin switch
    {
        NativeAcceptedPreviewOrigin.Selected => Origin.Selected,
        NativeAcceptedPreviewOrigin.Search => Origin.Search,
        NativeAcceptedPreviewOrigin.ById => Origin.ById,
        NativeAcceptedPreviewOrigin.Favorite => Origin.Favorite,
        _ => throw new ArgumentOutOfRangeException(nameof(origin)),
    };
    private static async Task WithScenarioAsync(NativeCardsAsyncFixtureFactory? ordinaryFactory,
        Func<NativeCardsAsyncFixtureOwner?, Scenario> create, Func<Scenario, Task> body)
    {
        if (ordinaryFactory is null)
        {
            using var scenario = create(null);
            await body(scenario);
            return;
        }
        var owner = ordinaryFactory.Create();
        await owner.RunAsync(async () =>
        {
            // The constructor explicitly registers every acquired actual facade.
            // Owner cleanup completes at the same per-Scenario boundary as legacy Dispose.
            var scenario = create(owner);
            await body(scenario);
        });
    }

}
