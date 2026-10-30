using System.Runtime.ExceptionServices;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static TestAssertions;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
public sealed class StartupCreditOwnershipCases
{
    private static IEnumerable<TestCaseData> BodyFailureCases()
    {
        yield return new TestCaseData(false)
            .SetName("BodyFailureJoinsOriginalStartupObserverAndRetirement")
            .SetProperty("FishAudioCaseId", "infra.startup-credit/body-failure-joins-original-work");
        yield return new TestCaseData(true)
            .SetName("BodyFailureSurvivesOriginalObserverCleanupFailure")
            .SetProperty("FishAudioCaseId", "infra.startup-credit/observer-cleanup-failure-preserves-body");
    }

    [TestCaseSource(nameof(BodyFailureCases))]
    public async Task BodyFailureOwnsTheActualHeldCreditWork(bool failObserver)
    {
        var bodyFailure = new InvalidOperationException("startup-credit-owned-body-failure");
        var observerFailure = new IOException("startup-credit-owned-observer-failure");
        using var observerRelease = new ManualResetEventSlim();
        var observerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousInvoker = SettingsViewModel.UiThreadInvokerOverride;
        StartupCreditSuite.StartupCreditOwnershipProbe? probe = null;
        Task? scenario = null;
        ExceptionDispatchInfo? testFailure = null;
        ExceptionDispatchInfo? rescueFailure = null;
        var entered = 0;
        try
        {
            scenario = Task.Run(() => StartupCreditSuite.PendingStartupCreditRefreshForOwnershipAsync(value =>
            {
                probe = value;
                Assert.That(value.Work.Count, Is.EqualTo(1), "startup-credit-capture-must-keep-one-init-holder-across-view-model-publication");
                Assert.That(value.Work[0].StartupTask, Is.SameAs(value.Plugin.PendingStartupTask),
                    "startup-credit-capture-must-retain-the-original-startup-task");
                Assert.That(value.ObserverTasks.Count, Is.EqualTo(1), "startup-credit-pending-attachment-must-expose-its-original-observer");
                Assert.That(value.ObserverTasks[0].IsCompleted, Is.False, "startup-credit-fault-seam-observer-must-still-be-pending");
                Assert.That(value.Work[0].RetirementTasks, Is.Empty, "startup-credit-fault-seam-must-not-retire-before-the-body-oracle");
                SettingsViewModel.UiThreadInvokerOverride = action =>
                {
                    if (Interlocked.CompareExchange(ref entered, 1, 0) == 0)
                    {
                        observerEntered.TrySetResult();
                        observerRelease.Wait();
                    }
                    if (failObserver) throw observerFailure;
                    action();
                };
                throw bodyFailure;
            }));

            // A broken cleanup can return before it releases the held reply. Observe that
            // failure before rescue; otherwise wait causally for the real observer.
            await Task.WhenAny(scenario, observerEntered.Task);
            Assert.That(probe, Is.Not.Null, "startup-credit-real-body-did-not-reach-the-owned-fault-seam");
            Assert.That(probe!.CreditReply.Task.IsCompletedSuccessfully, Is.True,
                "startup-credit-owner-returned-with-the-original-credit-reply-held");
            Assert.That(observerEntered.Task.IsCompletedSuccessfully, Is.True,
                "startup-credit-cleanup-did-not-observe-the-real-observer");
            Assert.That(probe.ObserverTasks[0].IsCompleted, Is.False,
                "startup-credit-original-observer-must-remain-held-at-the-publication-boundary");
            Assert.That(scenario.IsCompleted, Is.False,
                "startup-credit-cleanup-returned-before-the-original-observer-completed");

            observerRelease.Set();
            var observed = await CaptureFailureAsync(scenario);
            Assert.That(observed, Is.SameAs(bodyFailure), "startup-credit-cleanup-replaced-the-original-body-failure");
            AssertOwnedWorkFinished(probe);
            if (failObserver)
                Assert.That(probe.ObserverTasks[0].Exception?.InnerException, Is.SameAs(observerFailure),
                    "startup-credit-actual-observer-did-not-retain-its-cleanup-failure");
            else
                Assert.That(probe.ObserverTasks[0].IsCompletedSuccessfully, Is.True,
                    "startup-credit-original-observer-did-not-complete-successfully");
        }
        catch (Exception error) { testFailure = ExceptionDispatchInfo.Capture(error); }
        finally
        {
            // Rescue occurs after every ownership assertion above and owns the RED path.
            observerRelease.Set();
            try { await RescueAsync(probe, failObserver ? observerFailure : null); }
            catch (Exception error) { rescueFailure ??= ExceptionDispatchInfo.Capture(error); }
            try
            {
                if (scenario is not null) await scenario;
            }
            catch (Exception error) when (ReferenceEquals(error, bodyFailure)) { }
            catch (Exception error) { rescueFailure ??= ExceptionDispatchInfo.Capture(error); }
            SettingsViewModel.UiThreadInvokerOverride = previousInvoker;
        }
        testFailure?.Throw();
        rescueFailure?.Throw();
    }

    [Test]
    [Property("FishAudioCaseId", "infra.startup-credit/publication-failure-joins-both-init-owners")]
    public async Task PublicationWindowFailureReleasesAndJoinsBothActualInitializations()
    {
        var bodyFailure = new InvalidOperationException("startup-credit-publication-owned-body-failure");
        using var observerRelease = new ManualResetEventSlim();
        var observerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousInvoker = SettingsViewModel.UiThreadInvokerOverride;
        StartupCreditSuite.StartupCreditOwnershipProbe? probe = null;
        Task? scenario = null;
        ExceptionDispatchInfo? testFailure = null;
        ExceptionDispatchInfo? rescueFailure = null;
        var entered = 0;
        try
        {
            scenario = Task.Run(() => StartupCreditSuite.PublishedReplacementForOwnershipAsync(value =>
            {
                probe = value;
                Assert.That(value.Work.Count, Is.EqualTo(2), "startup-credit-publication-must-capture-the-original-and-replacement-holders");
                Assert.That(value.ObserverTasks.Count, Is.EqualTo(2), "startup-credit-publication-must-capture-both-original-observers");
                Assert.That(value.ReinitializationTask, Is.Not.Null, "startup-credit-publication-must-expose-the-original-task-run");
                Assert.That(value.ReinitializationTask!.IsCompleted, Is.False,
                    "startup-credit-publication-fault-seam-must-retain-the-original-window");
                Assert.That(value.Work.All(work => work.RetirementTasks.Count == 0), Is.True,
                    "startup-credit-publication-must-not-retire-an-owner-before-the-original-window-oracle");
                SettingsViewModel.UiThreadInvokerOverride = action =>
                {
                    if (Interlocked.CompareExchange(ref entered, 1, 0) == 0)
                    {
                        observerEntered.TrySetResult();
                        observerRelease.Wait();
                    }
                    action();
                };
                throw bodyFailure;
            }));

            await Task.WhenAny(scenario, observerEntered.Task);
            Assert.That(probe, Is.Not.Null, "startup-credit-publication-body-did-not-reach-its-owned-fault-seam");
            Assert.That(probe!.CreditReply.Task.IsCompletedSuccessfully, Is.True,
                "startup-credit-publication-owner-left-the-original-credit-reply-held");
            Assert.That(probe.ReplacementCreditReply?.Task.IsCompletedSuccessfully, Is.True,
                "startup-credit-publication-owner-left-the-replacement-credit-reply-held");
            Assert.That(probe.PublicationWindow?.Task.IsCompletedSuccessfully, Is.True,
                "startup-credit-publication-owner-left-the-original-window-held");
            Assert.That(observerEntered.Task.IsCompletedSuccessfully, Is.True,
                "startup-credit-publication-cleanup-did-not-observe-a-real-observer");
            Assert.That(scenario.IsCompleted, Is.False,
                "startup-credit-publication-cleanup-returned-before-its-original-observer");

            observerRelease.Set();
            var observed = await CaptureFailureAsync(scenario);
            Assert.That(observed, Is.SameAs(bodyFailure), "startup-credit-publication-cleanup-replaced-the-original-body-failure");
            Assert.That(probe.ReinitializationTask!.IsCompletedSuccessfully, Is.True,
                "startup-credit-publication-cleanup-did-not-join-the-original-task-run");
            AssertOwnedWorkFinished(probe);
            Assert.That(probe.ObserverTasks.All(task => task.IsCompletedSuccessfully), Is.True,
                "startup-credit-publication-cleanup-did-not-join-both-original-observers");
        }
        catch (Exception error) { testFailure = ExceptionDispatchInfo.Capture(error); }
        finally
        {
            observerRelease.Set();
            try { await RescueAsync(probe, null); }
            catch (Exception error) { rescueFailure ??= ExceptionDispatchInfo.Capture(error); }
            try
            {
                if (scenario is not null) await scenario;
            }
            catch (Exception error) when (ReferenceEquals(error, bodyFailure)) { }
            catch (Exception error) { rescueFailure ??= ExceptionDispatchInfo.Capture(error); }
            SettingsViewModel.UiThreadInvokerOverride = previousInvoker;
        }
        testFailure?.Throw();
        rescueFailure?.Throw();
    }

    [Test]
    [Property("FishAudioCaseId", "infra.startup-credit/completed-cycle-keeps-null-observer-and-original-retirement")]
    public async Task CompletedCycleDoesNotInventAnObserverOrLoseItsRetirementHandle()
    {
        using var network = OverrideNetworkAvailability(false);
        var plugin = new Main();
        STranslate.Plugin.Tts.FishAudio.Lifecycle.PluginInitializationCoordinator.InitializationWorkHandle? work = null;
        ExceptionDispatchInfo? primaryFailure = null;
        ExceptionDispatchInfo? cleanupFailure = null;
        try
        {
            plugin.Init(CreateContext(settings: new Settings { ApiKey = AppliedKey }));
            var beforeView = plugin.CaptureInitializationWork();
            Assert.That(beforeView, Is.Not.Null, "startup-credit-completed-cycle-did-not-capture-the-current-owner");
            work = beforeView!.Work;
            Assert.That(work.StartupTask, Is.SameAs(plugin.PendingStartupTask),
                "startup-credit-completed-cycle-capture-replaced-the-original-startup-task");
            await work.StartupTask;
            var view = plugin.GetOrCreateSettingsViewModel();
            var afterView = plugin.CaptureInitializationWork();
            Assert.That(afterView!.Work, Is.SameAs(work),
                "startup-credit-view-model-record-copy-replaced-the-original-retirement-holder");
            Assert.That(view.PendingStartupCreditObserverTask, Is.Null,
                "startup-credit-synchronous-attachment-invented-an-observer-task");
            Assert.That(work.RetirementTasks, Is.Empty, "startup-credit-current-cycle-was-retired-before-disposal");
            plugin.Dispose();
            Assert.That(plugin.CaptureInitializationWork(), Is.Null,
                "startup-credit-disposed-main-must-not-return-a-fabricated-current-owner");
            Assert.That(work.RetirementTasks.Count, Is.EqualTo(1),
                "startup-credit-disposal-did-not-retain-the-original-continuation-task");
            await Task.WhenAll(work.RetirementTasks);
            Assert.That(view.PendingStartupCreditObserverTask, Is.Null,
                "startup-credit-disposal-invented-a-completed-observer");
        }
        catch (Exception error) { primaryFailure = ExceptionDispatchInfo.Capture(error); }
        finally
        {
            try { plugin.Dispose(); }
            catch (Exception error) { cleanupFailure ??= ExceptionDispatchInfo.Capture(error); }
            if (work is not null)
            {
                try { await work.StartupTask; }
                catch (Exception error) { cleanupFailure ??= ExceptionDispatchInfo.Capture(error); }
                try { await Task.WhenAll(work.RetirementTasks); }
                catch (Exception error) { cleanupFailure ??= ExceptionDispatchInfo.Capture(error); }
            }
        }
        primaryFailure?.Throw();
        cleanupFailure?.Throw();
    }

    private static void AssertOwnedWorkFinished(StartupCreditSuite.StartupCreditOwnershipProbe probe)
    {
        Assert.That(probe.Plugin.CaptureInitializationWork(), Is.Null,
            "startup-credit-owner-returned-with-a-current-initialization");
        foreach (var work in probe.Work)
        {
            Assert.That(work.StartupTask.IsCompleted, Is.True, "startup-credit-owner-did-not-join-an-original-startup-task");
            Assert.That(work.RetirementTasks.Count, Is.GreaterThan(0),
                "startup-credit-owner-did-not-retire-a-captured-initialization");
            Assert.That(work.RetirementTasks.All(task => task.IsCompletedSuccessfully), Is.True,
                "startup-credit-owner-returned-before-original-retirement-cleanup");
        }
        Assert.That(probe.ObserverTasks.All(task => task.IsCompleted), Is.True,
            "startup-credit-owner-returned-before-an-original-observer-completed");
        Assert.That(probe.ViewModels.Select(view => view.PendingStartupCreditObserverTask).Where(task => task is not null),
            Is.EquivalentTo(probe.ObserverTasks), "startup-credit-retirement-erased-an-original-observer-handle");
    }

    private static async Task<Exception?> CaptureFailureAsync(Task operation)
    {
        try { await operation; return null; }
        catch (Exception error) { return error; }
    }

    private static async Task RescueAsync(StartupCreditSuite.StartupCreditOwnershipProbe? probe, Exception? expectedObserverFailure)
    {
        if (probe is null) return;
        ExceptionDispatchInfo? failure = null;
        foreach (var release in probe.BarrierReleases)
            try { release(); }
            catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
        if (probe.ReinitializationTask is not null)
            try { await probe.ReinitializationTask; }
            catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
        foreach (var work in probe.Work)
            try { await work.StartupTask; }
            catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
        foreach (var task in probe.ObserverTasks)
            try { await task; }
            catch (Exception error) when (ReferenceEquals(error, expectedObserverFailure)) { }
            catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
        try { probe.Plugin.Dispose(); }
        catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
        foreach (var work in probe.Work)
            try { await Task.WhenAll(work.RetirementTasks); }
            catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
        failure?.Throw();
    }
}
