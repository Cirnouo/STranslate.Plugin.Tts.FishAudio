internal static partial class SearchPreviewRetentionSuite
{
    internal static Task ObserveDiscoveryKeepsAcceptedAudioAsync() =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => DiscoveryKeepsAcceptedAudioAsync(new SearchRetentionFixtureFactory(), null)));

    internal static Task ObservePendingOriginStillRetiresAsync() =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => PendingOriginStillRetiresAsync(new SearchRetentionFixtureFactory(), null)));

    internal static Task ObserveDisplayedPendingOriginMayStillAcceptAsync() =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => DisplayedPendingOriginMayStillAcceptAsync(new SearchRetentionFixtureFactory(), null)));

    internal static Task ObserveAcceptanceReentrancyKeepsTheCorrectOwnerAsync() =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => AcceptanceReentrancyKeepsTheCorrectOwnerAsync(new SearchRetentionFixtureFactory(), null)));

    internal static Task ObserveAcceptedPublicationCannotRetireItsSuccessorAsync() =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => AcceptedPublicationCannotRetireItsSuccessorAsync(new SearchRetentionFixtureFactory(), null)));

    internal static Task ObserveExplicitReplacementAndRetirementAsync() =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => ExplicitReplacementAndRetirementAsync(new SearchRetentionFixtureFactory(), null)));

    internal static Task ObserveDiscoveryHeldFailureAsync(SearchRetentionFixtureFactory factory, SearchRetentionFixtureHooks hooks) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => DiscoveryKeepsAcceptedAudioAsync(factory, hooks)));

    internal static Task ObservePendingOriginHeldFailureAsync(SearchRetentionFixtureFactory factory, SearchRetentionFixtureHooks hooks) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => PendingOriginStillRetiresAsync(factory, hooks)));

    internal static Task ObserveDisplayedHeldFailureAsync(SearchRetentionFixtureFactory factory, SearchRetentionFixtureHooks hooks) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => DisplayedPendingOriginMayStillAcceptAsync(factory, hooks)));

    internal static Task ObservePublicationSuccessorHeldFailureAsync(SearchRetentionFixtureFactory factory, SearchRetentionFixtureHooks hooks) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => AcceptedPublicationCannotRetireItsSuccessorAsync(factory, hooks)));

    internal static Task ObserveOtherOriginRemovalRetainsAcceptedAudioAsync() =>
        StaTestHost.RunOnStaThreadAsync(() => OtherOriginRemovalRetainsAcceptedAudioAsync(new NativeCardsAsyncFixtureFactory()));
    private static Task WithNonsearchOriginAsync(NativeCardsAsyncFixtureFactory? ordinaryFactory,
        Func<NativeCardsAsyncFixtureOwner?, Task> body)
    {
        if (ordinaryFactory is null) return body(null);
        var owner = ordinaryFactory.Create();
        return owner.RunAsync(() => body(owner));
    }

}
