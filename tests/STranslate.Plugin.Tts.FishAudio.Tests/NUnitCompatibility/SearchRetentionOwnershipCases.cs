using System.Reflection;
using System.Windows;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Presentation;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SearchRetentionOwnershipCases
{
    [Test]
    [Property("FishAudioCaseId", "infra.native-cards/search-retention/failure-with-original-discovery-held")]
    public Task HeldDiscoveryFailureJoinsTheOriginalSearchBeforeFacadeRetirement() =>
        RunHeldFailureAsync(SearchRetentionFailurePoint.DiscoveryHeldSearch,
            SearchPreviewRetentionSuite.ObserveDiscoveryHeldFailureAsync,
            metadataGets: 8, sampleGets: 1, playerDisposals: 1);

    [Test]
    [Property("FishAudioCaseId", "infra.native-cards/search-retention/failure-with-removed-origin-held")]
    public Task RemovedOriginFailureJoinsTheActualPreviewAndItsRequestedResponse() =>
        RunHeldFailureAsync(SearchRetentionFailurePoint.PendingOriginHeld,
            SearchPreviewRetentionSuite.ObservePendingOriginHeldFailureAsync,
            metadataGets: 0, sampleGets: 1, playerDisposals: 0);

    [Test]
    [Property("FishAudioCaseId", "infra.native-cards/search-retention/failure-with-displayed-preview-and-search-held")]
    public Task DisplayedOriginFailureJoinsBothOriginalOperations() =>
        RunHeldFailureAsync(SearchRetentionFailurePoint.DisplayedPreviewAndSearchHeld,
            SearchPreviewRetentionSuite.ObserveDisplayedHeldFailureAsync,
            metadataGets: 2, sampleGets: 1, playerDisposals: 0, pairedPending: true);

    [Test]
    [Property("FishAudioCaseId", "infra.native-cards/search-retention/failure-with-actual-publication-successor-held")]
    public Task PublicationFailureJoinsTheActualCallbackSuccessorRatherThanTheLastCommandSlot() =>
        RunHeldFailureAsync(SearchRetentionFailurePoint.AcceptedSuccessorStarted,
            SearchPreviewRetentionSuite.ObservePublicationSuccessorHeldFailureAsync,
            metadataGets: 1, sampleGets: 2, playerDisposals: 1,
            configure: probe =>
            {
                var successorResponse = probe.Owner.OwnResponse(new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously));
                probe.Http.GetAsStreamAsyncHandler = (_, _, _) => probe.Http.GetAsStreamCallCount == 1
                    // This is the original first actual request's normal fake resource,
                    // created at that request only. Cleanup never creates a stream.
                    ? Task.FromResult(probe.Http.GetStreamResponse)
                    : probe.Owner.TrackRawResponse(successorResponse.Task);
            });

    private static Task RunHeldFailureAsync(SearchRetentionFailurePoint point,
        Func<SearchRetentionFixtureFactory, SearchRetentionFixtureHooks, Task> run,
        int metadataGets, int sampleGets, int playerDisposals, bool pairedPending = false,
        Action<SearchRetentionFixtureProbe>? configure = null) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(() =>
        {
            // Setup, actual scenario, authoritative oracles and independent rescue
            // all remain inside this one original STA action/root and budget.
            var factory = new SearchRetentionFixtureFactory();
            var primary = new InvalidOperationException("search-retention-held-body-failure");
            var previousNetwork = FishAudioRequestPolicy.NetworkAvailableOverride;
            SearchRetentionFixtureProbe? observedProbe = null;
            Task[] heldRaw = [];
            var hooks = new SearchRetentionFixtureHooks
            {
                ScenarioReady = probe =>
                {
                    Assert.That(SettingsViewModel.UiThreadInvokerOverride, Is.Null);
                    Assert.That(Application.Current is null || Application.Current.Dispatcher.CheckAccess(), Is.True,
                        "The control retains the configured inline UI route without installing an override.");
                    configure?.Invoke(probe);
                },
                Failure = (actual, probe) =>
                {
                    if (actual != point) return;
                    observedProbe = probe;
                    Assert.That(probe.Operation, Is.Not.Null);
                    Assert.That(probe.Operation!.IsCompleted, Is.False, "Failure occurs while the exact original command Task is held.");
                    Assert.That(probe.Owner.Operations.Any(task => ReferenceEquals(task, probe.Operation)), Is.True);
                    heldRaw = probe.Owner.RawResponses.Where(task => !task.IsCompleted).ToArray();
                    Assert.That(heldRaw, Has.Length.EqualTo(pairedPending ? 2 : 1));
                    Assert.That(heldRaw.All(task => !ReferenceEquals(task, probe.Operation)), Is.True,
                        "A raw response Task cannot substitute for the original command's full product tail.");
                    if (pairedPending)
                    {
                        Assert.That(probe.OtherOperation, Is.Not.Null);
                        Assert.That(probe.OtherOperation!.IsCompleted, Is.False);
                        Assert.That(probe.OtherOperation, Is.Not.SameAs(probe.Operation));
                    }
                    else if (probe.OtherOperation is not null)
                        Assert.That(probe.OtherOperation.IsCompletedSuccessfully, Is.True,
                            "The predecessor is terminal while its actual callback-created successor is still held.");
                    Assert.That(probe.Http.GetCallCount, Is.EqualTo(metadataGets));
                    Assert.That(probe.Http.GetAsStreamCallCount, Is.EqualTo(sampleGets));
                    Assert.That(IsDisposed(probe.View), Is.False);
                    Assert.That((object?)FishAudioRequestPolicy.NetworkAvailableOverride, Is.Not.SameAs(previousNetwork));
                    throw primary;
                }
            };
            return RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try { await run(factory, hooks); }
                catch (Exception error) { observed = error; }
                // No independent rescue has run at any of these assertions.
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(observedProbe, Is.Not.Null);
                Assert.That(factory.Owners.Count, Is.EqualTo(1), "Failure creates no replacement scenario or later loop iteration.");
                var probe = observedProbe!;
                var operation = probe.Operation!;
                Assert.That(operation.IsCompletedSuccessfully, Is.True);
                Assert.That(probe.Owner.Operations.All(task => task.IsCompleted), Is.True);
                Assert.That(probe.Owner.RawResponses.All(task => task.IsCompleted), Is.True);
                Assert.That(heldRaw.All(task => task.IsCanceled), Is.True,
                    "Cleanup releases the existing requested responses without a request, replacement body or stream.");
                AssertObservedPendingBeforeRelease(probe.Owner, operation);
                AssertTerminalBeforeFacade(probe.Owner, operation);
                foreach (var raw in heldRaw)
                {
                    AssertObservedPendingBeforeRelease(probe.Owner, raw);
                    AssertTerminalBeforeFacade(probe.Owner, raw);
                }
                if (pairedPending)
                {
                    Assert.That(probe.OtherOperation!.IsCompletedSuccessfully, Is.True);
                    AssertObservedPendingBeforeRelease(probe.Owner, probe.OtherOperation);
                    AssertTerminalBeforeFacade(probe.Owner, probe.OtherOperation);
                }
                else if (probe.OtherOperation is not null)
                    AssertTerminalBeforeFacade(probe.Owner, probe.OtherOperation);
                Assert.That(IsDisposed(probe.View), Is.True);
                Assert.That(probe.View.PreviewingVoiceId, Is.Null);
                Assert.That(typeof(PreviewPlaybackController).GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(Playback(probe.View)), Is.Null, "The actual accepted-output timer is retired.");
                if (probe.Player.LastOpenedStream is not null)
                    Assert.That(probe.Player.LastOpenedStream.CanRead, Is.False, "The actual accepted local buffer is released.");
                Assert.That(probe.Player.DisposeCallCount, Is.EqualTo(playerDisposals));
                Assert.That(probe.Player.StopCallCount, Is.EqualTo(playerDisposals));
                Assert.That(probe.ReplacementPlayer.PlayCallCount, Is.Zero);
                Assert.That(probe.ReplacementPlayer.DisposeCallCount, Is.Zero);
                Assert.That(probe.Http.GetCallCount, Is.EqualTo(metadataGets));
                Assert.That(probe.Http.GetAsStreamCallCount, Is.EqualTo(sampleGets));
                Assert.That((object?)FishAudioRequestPolicy.NetworkAvailableOverride, Is.SameAs(previousNetwork),
                    "The original network override retires after actual operation joins and facade cleanup.");
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey], Is.Null,
                    "Unexpected cleanup errors remain failures of the control.");
            }, factory.RescueAsync);
        }));

    private static void AssertObservedPendingBeforeRelease(SearchRetentionFixtureOwner owner, Task actual)
    {
        var index = Array.FindIndex(owner.TasksBeforeRelease, task => ReferenceEquals(task, actual));
        Assert.That(index, Is.GreaterThanOrEqualTo(0));
        Assert.That(owner.PendingBeforeRelease[index], Is.True);
    }

    private static void AssertTerminalBeforeFacade(SearchRetentionFixtureOwner owner, Task actual)
    {
        var index = Array.FindIndex(owner.TasksBeforeFacadeRetirement, task => ReferenceEquals(task, actual));
        Assert.That(index, Is.GreaterThanOrEqualTo(0));
        Assert.That(owner.PendingBeforeFacadeRetirement[index], Is.False,
            "The saved actual Task is terminal before facade cleanup, independently of any outer guard.");
    }

    private static bool IsDisposed(SettingsViewModel vm) =>
        (bool)typeof(SettingsViewModel).GetField("_isDisposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;

    private static object Playback(SettingsViewModel vm) =>
        typeof(SettingsViewModel).GetField("_previewPlayback", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
}
