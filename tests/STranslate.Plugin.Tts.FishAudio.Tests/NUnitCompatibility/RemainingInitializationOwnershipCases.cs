using System.Runtime.ExceptionServices;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioExclusiveResource", "global-overrides")]
public sealed class RemainingInitializationOwnershipCases
{
    private const string VoiceId = "11111111111111111111111111111111";

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/reinitialized-settings-view-uses-new-context-and-settings-body-failure-joins-original-work")]
    public Task ReinitializedSettingsViewUsesNewContextAndSettingsBodyFailureJoinsOriginalWork() => CheckHeldFailureAsync(
        probe => InitializationLifecycleSuite.RunReinitializedSettingsViewUsesNewContextAndSettingsAsync(probe), requiresSta: true);

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/startup-settings-save-wins-before-failed-replacement-transition-body-failure-joins-original-work")]
    public Task StartupSettingsSaveWinsBeforeFailedReplacementTransitionBodyFailureJoinsOriginalWork() => CheckHeldFailureAsync(
        probe => InitializationLifecycleSuite.RunStartupSettingsSaveWinsBeforeFailedReplacementTransitionAsync(probe));

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/settings-view-model-creation-waits-for-replacement-commit-body-failure-joins-original-work")]
    public Task SettingsViewModelCreationWaitsForReplacementCommitBodyFailureJoinsOriginalWork() => CheckHeldFailureAsync(
        probe => InitializationLifecycleSuite.RunSettingsViewModelCreationWaitsForReplacementCommitAsync(probe));

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/overtaken-initialization-skips-settings-load-body-failure-joins-original-work")]
    public Task OvertakenInitializationSkipsSettingsLoadBodyFailureJoinsOriginalWork() => CheckHeldFailureAsync(
        probe => InitializationLifecycleSuite.RunOvertakenInitializationSkipsSettingsLoadAsync(probe));

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/reinitialization-waits-for-in-flight-view-model-save-body-failure-joins-original-work")]
    public Task ReinitializationWaitsForInFlightViewModelSaveBodyFailureJoinsOriginalWork() => CheckHeldFailureAsync(
        probe => InitializationLifecycleSuite.RunReinitializationWaitsForInFlightViewModelSaveAsync(probe));

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/startup-voice-ui-apply-rechecks-expected-voice-id-body-failure-joins-original-work")]
    public Task StartupVoiceUiApplyRechecksExpectedVoiceIdBodyFailureJoinsOriginalWork() => CheckHeldFailureAsync(
        probe => InitializationLifecycleSuite.RunStartupVoiceUiApplyRechecksExpectedVoiceIdAsync(probe));

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/delayed-startup-voice-refresh-does-not-restore-cleared-voice-body-failure-joins-original-work")]
    public Task DelayedStartupVoiceRefreshDoesNotRestoreClearedVoiceBodyFailureJoinsOriginalWork() => CheckHeldFailureAsync(
        probe => InitializationLifecycleSuite.RunDelayedStartupVoiceRefreshDoesNotRestoreClearedVoiceAsync(probe));

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/delayed-startup-voice-refresh-does-not-overwrite-new-selection-body-failure-joins-original-work")]
    public Task DelayedStartupVoiceRefreshDoesNotOverwriteNewSelectionBodyFailureJoinsOriginalWork() => CheckHeldFailureAsync(
        probe => InitializationLifecycleSuite.RunDelayedStartupVoiceRefreshDoesNotOverwriteNewSelectionAsync(probe));

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/concurrent-paid-model-selection-still-publishes-post-cutoff-policy-body-failure-joins-original-work")]
    public Task ConcurrentPaidModelSelectionStillPublishesPostCutoffPolicyBodyFailureJoinsOriginalWork() => CheckHeldFailureAsync(
        probe => InitializationLifecycleSuite.RunConcurrentPaidModelSelectionStillPublishesPostCutoffPolicyAsync(probe));

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/startup-model-save-failure-rolls-back-runtime-and-storage-body-failure-joins-original-work")]
    public Task StartupModelSaveFailureRollsBackRuntimeAndStorageBodyFailureJoinsOriginalWork() => CheckHeldFailureAsync(
        probe => InitializationLifecycleSuite.RunStartupModelSaveFailureRollsBackRuntimeAndStorageAsync(probe));

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/startup-voice-save-failure-rolls-back-runtime-and-storage-body-failure-joins-original-work")]
    public Task StartupVoiceSaveFailureRollsBackRuntimeAndStorageBodyFailureJoinsOriginalWork() => CheckHeldFailureAsync(
        probe => InitializationLifecycleSuite.RunStartupVoiceSaveFailureRollsBackRuntimeAndStorageAsync(probe));

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/startup-model-normalization-invalidates-speculative-settings-view-model-body-failure-joins-original-work")]
    public Task StartupModelNormalizationInvalidatesSpeculativeSettingsViewModelBodyFailureJoinsOriginalWork() => CheckHeldFailureAsync(
        probe => InitializationLifecycleSuite.RunStartupModelNormalizationInvalidatesSpeculativeSettingsViewModelAsync(probe));

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/committed-startup-settings-invalidates-post-reservation-view-model-candidate-body-failure-joins-original-work")]
    public Task CommittedStartupSettingsInvalidatesPostReservationViewModelCandidateBodyFailureJoinsOriginalWork() => CheckHeldFailureAsync(
        probe => InitializationLifecycleSuite.RunCommittedStartupSettingsInvalidatesPostReservationViewModelCandidateAsync(probe));

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/startup-voice-refresh-invalidates-speculative-settings-view-model-body-failure-joins-original-work")]
    public Task StartupVoiceRefreshInvalidatesSpeculativeSettingsViewModelBodyFailureJoinsOriginalWork() => CheckHeldFailureAsync(
        probe => InitializationLifecycleSuite.RunStartupVoiceRefreshInvalidatesSpeculativeSettingsViewModelAsync(probe));

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/startup-settings-save-skips-after-new-initialization-request-body-failure-joins-original-work")]
    public Task StartupSettingsSaveSkipsAfterNewInitializationRequestBodyFailureJoinsOriginalWork() => CheckHeldFailureAsync(
        probe => InitializationLifecycleSuite.RunStartupSettingsSaveSkipsAfterNewInitializationRequestAsync(probe));

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/initialization-load-waits-for-authorized-startup-settings-save-body-failure-joins-original-work")]
    public Task InitializationLoadWaitsForAuthorizedStartupSettingsSaveBodyFailureJoinsOriginalWork() => CheckHeldFailureAsync(
        probe => InitializationLifecycleSuite.RunInitializationLoadWaitsForAuthorizedStartupSettingsSaveAsync(probe));

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/failed-reinitialization-restores-startup-settings-save-authorization-body-failure-joins-original-work")]
    public Task FailedReinitializationRestoresStartupSettingsSaveAuthorizationBodyFailureJoinsOriginalWork() => CheckHeldFailureAsync(
        probe => InitializationLifecycleSuite.RunFailedReinitializationRestoresStartupSettingsSaveAuthorizationAsync(probe));


    [Test]
    [Property("FishAudioCaseId", "infra.initialization/failed-unpublished-preparation-body-failure-joins-original-retirement")]
    public async Task FailedUnpublishedPreparationBodyFailureJoinsOriginalRetirement()
    {
        var marker = new InvalidOperationException("remaining-initialization-unpublished-owned-body-failure");
        RemainingInitializationFixtureOwner? owner = null;
        var ready = false;
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            var scenario = InitializationLifecycleSuite.RunFailedSameInstanceNormalizationRestoresOldStateAndBackingStoreAsync(
                onFailedPrepared: acquired =>
                {
                    owner = acquired;
                    var published = acquired.Plugins.Single().CaptureInitializationWork();
                    Assert.That(published, Is.Not.Null);
                    var failed = acquired.Acquisitions.Select(capture => capture.Work)
                        .Where(work => !ReferenceEquals(work, published!.Work)).ToArray();
                    Assert.Multiple(() =>
                    {
                        Assert.That(failed, Has.Length.EqualTo(1),
                            "unpublished-fault-must-retain-the-actual-failed-preparation");
                        Assert.That(published!.Work.RetirementTasks, Is.Empty,
                            "failed-preparation-must-leave-the-old-publication-live");
                        Assert.That(acquired.Views.Any(view => view.CopyVoiceIdCommand.CanExecute(VoiceId)), Is.True);
                    });
                    Assert.That(failed[0].RetirementTasks, Is.Not.Empty,
                        "failed-preparation-must-have-recorded-its-own-real-retirement");
                    Assert.That(acquired.Work.Any(work => ReferenceEquals(work, failed[0])), Is.True,
                        "failed-preparation-original-out-handle-must-belong-to-cleanup");
                    // This offline failed preparation can already be complete.
                    // No pending state, source, gate or delay is fabricated.
                    ready = true;
                    throw marker;
                });
            Exception? failure = null;
            try { await scenario; }
            catch (Exception error) { failure = error; }
            if (!ready && failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            Assert.That(ready, Is.True, "unpublished-fault-did-not-observe-the-real-failed-preparation");
            Assert.That(owner, Is.Not.Null);
            Assert.That(scenario.IsFaulted, Is.True);
            Assert.That(failure, Is.SameAs(marker));
            AssertCompletedBeforeRescue(owner!, marker);
        }, () => RescueAsync(owner));
    }

    private static async Task CheckHeldFailureAsync(
        Func<Action<RemainingInitializationObservation>, Task> run, bool requiresSta = false)
    {
        var marker = new InvalidOperationException("remaining-initialization-held-owned-body-failure");
        RemainingInitializationFixtureOwner? owner = null;
        var ready = false;
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            var scenario = run(observed =>
            {
                owner = observed.Owner;
                var actualTasks = owner.Operations.Concat(owner.Work.Select(work => work.StartupTask));
                Assert.Multiple(() =>
                {
                    Assert.That(owner.Plugins, Is.Not.Empty);
                    Assert.That(actualTasks.Any(task => ReferenceEquals(task, observed.Pending)), Is.True,
                        "held-initialization-fault-must-observe-an-original-owned-task");
                    Assert.That(observed.Pending.IsCompleted, Is.False,
                        "held-initialization-fault-must-observe-real-pending-work");
                    if (observed.Entered is { } entered)
                        Assert.That(entered.IsCompletedSuccessfully, Is.True,
                            "held-initialization-fault-must-follow-the-original-acquisition-signal");
                    else
                    {
                        Assert.That(observed.RequestCalls, Is.GreaterThan(0),
                            "held-initialization-fault-must-enter-the-original-http-handler");
                        Assert.That(owner.Responses.Any(task => !task.IsCompleted), Is.True);
                    }
                });
                ready = true;
                throw marker;
            });
            Exception? failure = null;
            try { await scenario; }
            catch (Exception error) { failure = error; }
            if (!ready && failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            Assert.That(ready, Is.True, "held-initialization-fault-did-not-reach-original-pending-work");
            Assert.That(owner, Is.Not.Null);
            Assert.That(scenario.IsFaulted, Is.True);
            Assert.That(failure, Is.SameAs(marker), "initialization-cleanup-must-preserve-primary-identity");
            AssertCompletedBeforeRescue(owner!, marker);
        }, () => requiresSta
            ? StaTestHost.RunOnStaThreadAsync(() => RescueAsync(owner))
            : RescueAsync(owner));
    }

    private static void AssertCompletedBeforeRescue(RemainingInitializationFixtureOwner owner, Exception marker)
    {
        Assert.Multiple(() =>
        {
            Assert.That(owner.Plugins.All(plugin => plugin.CaptureInitializationWork() is null), Is.True,
                "initialization-owner-returned-before-current-state-retired");
            Assert.That(owner.Operations.All(task => task.IsCompletedSuccessfully), Is.True,
                "initialization-owner-returned-before-original-operations-completed");
            Assert.That(owner.Barriers.All(task => task.IsCompletedSuccessfully), Is.True,
                "initialization-owner-returned-before-original-barriers-released");
            Assert.That(owner.Responses.All(task => task.IsCompletedSuccessfully), Is.True,
                "initialization-owner-returned-before-original-responses-released");
            Assert.That(owner.Work, Is.Not.Empty);
            Assert.That(owner.Acquisitions.All(capture =>
                owner.Work.Any(work => ReferenceEquals(work, capture.Work))), Is.True,
                "initialization-owner-omitted-an-actual-per-call-preparation");
            foreach (var work in owner.Work)
            {
                Assert.That(work.StartupTask.IsCompletedSuccessfully, Is.True,
                    "initialization-owner-returned-before-original-startup-completed");
                Assert.That(work.RetirementTasks, Is.Not.Empty,
                    "initialization-owner-has-no-actual-retirement");
                Assert.That(work.RetirementTasks.All(task => task.IsCompletedSuccessfully), Is.True,
                    "initialization-owner-returned-before-actual-retirement-completed");
            }
            Assert.That(owner.Observers.All(task => task.IsCompletedSuccessfully), Is.True,
                "initialization-owner-returned-before-original-observers-completed");
            Assert.That(owner.Views.All(view => !view.CopyVoiceIdCommand.CanExecute(VoiceId)), Is.True,
                "initialization-owner-returned-before-original-view-retired");
            Assert.That(marker.Data[RegressionFixtureOwner.CleanupTypesKey], Is.Null,
                "initialization-unknown-cleanup-failure-must-remain-visible");
        });
    }

    private static async Task RescueAsync(RemainingInitializationFixtureOwner? owner)
    {
        if (owner is null) return;
        ExceptionDispatchInfo? failure = null;
        foreach (var release in owner.BarrierReleases)
            try { release(); }
            catch (Exception error) { Keep(ref failure, error); }
        foreach (var operation in owner.Operations)
            try { await operation; }
            catch (Exception error) { Keep(ref failure, error); }

        // Read original acquisitions directly after original Init/GetVM tasks
        // finish. This rescue never calls candidate CompletionStages/capture.
        var work = owner.Acquisitions.Select(capture => capture.Work).Distinct().ToList();
        foreach (var initialization in owner.Work)
            if (!work.Contains(initialization)) work.Add(initialization);
        var views = owner.Views.ToList();
        var observers = owner.Observers.ToList();
        foreach (var operation in owner.Operations.OfType<Task<SettingsViewModel>>())
            if (operation.IsCompletedSuccessfully && !views.Contains(operation.Result)) views.Add(operation.Result);
        foreach (var plugin in owner.Plugins)
            try
            {
                var current = plugin.CaptureInitializationWork();
                if (current is null) continue;
                if (!work.Contains(current.Work)) work.Add(current.Work);
                if (current.ViewModel is { } view && !views.Contains(view)) views.Add(view);
            }
            catch (Exception error) { Keep(ref failure, error); }
        foreach (var view in views)
            try
            {
                if (view.PendingStartupCreditObserverTask is { } observer && !observers.Contains(observer))
                    observers.Add(observer);
            }
            catch (Exception error) { Keep(ref failure, error); }
        foreach (var plugin in owner.Plugins)
            try { plugin.Dispose(); }
            catch (Exception error) { Keep(ref failure, error); }
        foreach (var release in owner.ResponseReleases)
            try { release(); }
            catch (Exception error) { Keep(ref failure, error); }
        foreach (var response in owner.Responses)
            try { await response; }
            catch (Exception error) { Keep(ref failure, error); }
        foreach (var initialization in work)
            try { await initialization.StartupTask; }
            catch (Exception error) { Keep(ref failure, error); }
        foreach (var view in views)
            try { view.Dispose(); }
            catch (Exception error) { Keep(ref failure, error); }
        foreach (var observer in observers)
            try { await observer; }
            catch (Exception error) { Keep(ref failure, error); }
        foreach (var initialization in work)
        {
            var retirement = initialization.RetirementTasks;
            if (retirement.Count == 0)
                Keep(ref failure, new InvalidOperationException("initialization-rescue-real-retirement-not-recorded"));
            foreach (var task in retirement)
                try { await task; }
                catch (Exception error) { Keep(ref failure, error); }
        }
        try { owner.NetworkOverride?.Dispose(); }
        catch (Exception error) { Keep(ref failure, error); }
        failure?.Throw();
    }

    private static void Keep(ref ExceptionDispatchInfo? failure, Exception error)
    {
        if (failure is null) { failure = ExceptionDispatchInfo.Capture(error); return; }
        if (ReferenceEquals(failure.SourceException, error)) return;
        try
        {
            if (failure.SourceException.Data[RegressionFixtureOwner.CleanupTypesKey] is not List<string> types)
                failure.SourceException.Data[RegressionFixtureOwner.CleanupTypesKey] = types = [];
            types.Add(error.GetType().FullName ?? error.GetType().Name);
        }
        catch { /* Secondary diagnostics cannot replace the original rescue failure. */ }
    }
}
