using NUnit.Framework;

namespace NUnitCompatibility;

// Only the already published double-state owner is observed; no new request,
// completion source, delay or environment setup is introduced.
[TestFixture]
[NonParallelizable]
public sealed class InitializationSharedBackingOwnershipCases
{
    [Test]
    [Property("FishAudioCaseId", "infra.initialization/shared-backing-body-failure-joins-both-published-states")]
    public async Task SharedBackingBodyFailureRetiresBothActualPublishedStatesBeforeRescue()
    {
        var marker = new InvalidOperationException("initialization-shared-backing-owned-body-failure");
        PublishedInitializationObservation? ownership = null;
        var callbacks = 0;
        var ready = false;
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            var scenario = InitializationLifecycleSuite.RunReinitializationRetiresOldViewModelWithoutSavingSharedBackingStoreAsync(observed =>
            {
                ownership = observed;
                callbacks++;
                Assert.Multiple(() =>
                {
                    Assert.That(observed.Work, Has.Count.EqualTo(2));
                    Assert.That(observed.Work[0], Is.Not.SameAs(observed.Work[1]));
                    Assert.That(observed.Work[0].StartupTask.IsCompletedSuccessfully, Is.True);
                    Assert.That(observed.Work[1].StartupTask.IsCompletedSuccessfully, Is.True);
                    Assert.That(observed.Work[0].RetirementTasks, Is.Not.Empty);
                    Assert.That(observed.Work[1].RetirementTasks, Is.Empty);
                    Assert.That(observed.Plugin.CaptureInitializationWork()!.Work, Is.SameAs(observed.Work[1]));
                    Assert.That(observed.Work[1].StartupTask, Is.SameAs(observed.Plugin.PendingStartupTask));
                    Assert.That(observed.ViewModel, Is.Not.Null);
                    Assert.That(observed.ViewModel!.CopyVoiceIdCommand.CanExecute("11111111111111111111111111111111"), Is.True);
                    var actualObserver = observed.ViewModel.PendingStartupCreditObserverTask;
                    if (actualObserver is not null)
                        Assert.That(observed.Observers, Does.Contain(actualObserver));
                    Assert.That(observed.Response, Is.Null);
                    Assert.That(observed.Release, Is.Null);
                });
                ready = true;
                throw marker;
            });
            Exception? failure = null;
            try { await scenario; }
            catch (Exception error) { failure = error; }
            Assert.That(ready, Is.True, "shared-backing-fault-must-observe-two-real-published-states");
            Assert.That(ownership, Is.Not.Null);
            var completed = ownership!;
            Assert.Multiple(() =>
            {
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(scenario.IsFaulted, Is.True);
                Assert.That(failure, Is.SameAs(marker), "shared-backing-cleanup-must-preserve-first-primary-identity");
                Assert.That(marker.Data[RegressionFixtureOwner.CleanupTypesKey], Is.Null,
                    "shared-backing-unknown-cleanup-fault-must-remain-visible");
                Assert.That(completed.Plugin.CaptureInitializationWork(), Is.Null,
                    "shared-backing-current-owner-must-retire-before-independent-rescue");
                Assert.That(completed.ViewModel!.CopyVoiceIdCommand.CanExecute("11111111111111111111111111111111"), Is.False,
                    "shared-backing-new-view-model-must-retire-before-independent-rescue");
                foreach (var work in completed.Work)
                {
                    Assert.That(work.StartupTask.IsCompletedSuccessfully, Is.True);
                    var retirement = work.RetirementTasks;
                    Assert.That(retirement, Is.Not.Empty,
                        "shared-backing-real-retirement-must-be-recorded-before-independent-rescue");
                    Assert.That(retirement.All(task => task.IsCompletedSuccessfully), Is.True,
                        "shared-backing-real-retirement-must-complete-before-independent-rescue");
                }
                Assert.That(completed.Observers.All(task => task.IsCompletedSuccessfully), Is.True,
                    "shared-backing-actual-observers-must-complete-before-independent-rescue");
            });
        },
        () => { ownership?.Plugin.Dispose(); return Task.CompletedTask; },
        async () =>
        {
            if (ownership is not null) await Task.WhenAll(ownership.Work.Select(work => work.StartupTask));
        },
        async () =>
        {
            if (ownership is not null) await Task.WhenAll(ownership.Observers);
        },
        async () =>
        {
            if (ownership is not null) await Task.WhenAll(ownership.Work.SelectMany(work => work.RetirementTasks));
        },
        () =>
        {
            if (ownership is not null && ownership.Work.Any(work => work.RetirementTasks.Count == 0))
                throw new InvalidOperationException("shared-backing-rescue-has-no-actual-retirement-task");
            return Task.CompletedTask;
        });
    }
}

