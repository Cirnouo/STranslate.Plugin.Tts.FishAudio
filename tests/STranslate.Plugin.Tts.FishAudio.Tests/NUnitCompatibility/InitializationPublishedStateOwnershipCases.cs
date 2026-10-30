using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Lifecycle;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCompatibility;

// The original scenario owns its cleanup. These assertions precede this
// independent rescue and inspect only the original acquired objects and tasks.
[TestFixture]
[NonParallelizable]
public sealed class InitializationPublishedStateOwnershipCases
{
    [Test]
    [Property("FishAudioCaseId", "infra.initialization/dispose-lease-body-failure-joins-published-work")]
    public Task DisposeLeaseBodyFailureJoinsActualPublishedWorkBeforeRescue() =>
        AssertBodyFailureAsync(128);

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/startup-dispose-body-failure-joins-original-request")]
    public Task StartupDisposeBodyFailureJoinsActualRequestBeforeRescue() =>
        AssertBodyFailureAsync(150);

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/reinitialized-body-failure-joins-both-published-states")]
    public Task ReinitializedBodyFailureJoinsBothActualPublishedStatesBeforeRescue() =>
        AssertBodyFailureAsync(152);

    private static async Task AssertBodyFailureAsync(int originalOrdinal)
    {
        var marker = new InvalidOperationException("initialization-published-owned-body-failure-" + originalOrdinal);
        PublishedInitializationObservation? ownership = null;
        Task? scenario = null;
        var callbacks = 0;
        var ready = false;
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            void OnAcquired(PublishedInitializationObservation observed)
            {
                ownership = observed;
                callbacks++;
                Assert.That(observed.Work, Is.Not.Empty);
                if (originalOrdinal == 152 && observed.Work.Count == 1)
                {
                    Assert.That(observed.Work[0].StartupTask, Is.SameAs(observed.Plugin.PendingStartupTask));
                    Assert.That(observed.Response, Is.Not.Null);
                    Assert.That(observed.Response!.IsCompleted, Is.False);
                    Assert.That(observed.Work[0].StartupTask.IsCompleted, Is.False);
                    Assert.That(observed.Work[0].RetirementTasks, Is.Empty);
                    return;
                }

                Assert.Multiple(() =>
                {
                    Assert.That(observed.Work.Count, Is.EqualTo(originalOrdinal == 152 ? 2 : 1));
                    Assert.That(observed.Work[^1].StartupTask, Is.SameAs(observed.Plugin.PendingStartupTask),
                        "published-fault-must-retain-the-current-original-startup-task");
                    Assert.That(observed.Work[^1].RetirementTasks, Is.Empty,
                        "published-fault-must-precede-current-state-retirement");
                    Assert.That(observed.Plugin.CaptureInitializationWork()!.Work, Is.SameAs(observed.Work[^1]));
                    if (originalOrdinal == 128)
                    {
                        Assert.That(observed.ViewModel, Is.Not.Null);
                        Assert.That(observed.RuntimeSettings, Is.Not.Null);
                        Assert.That(observed.RuntimeSettings!.ActiveWriteLease, Is.Not.Zero);
                        Assert.That(observed.ViewModel!.CopyVoiceIdCommand.CanExecute("11111111111111111111111111111111"), Is.True);
                        var actualObserver = observed.ViewModel.PendingStartupCreditObserverTask;
                        if (actualObserver is null) Assert.That(observed.Observers, Is.Empty);
                        else Assert.That(observed.Observers, Is.EquivalentTo(new[] { actualObserver }));
                    }
                    else
                    {
                        Assert.That(observed.ViewModel, Is.Null,
                            "published-fault-must-not-create-a-view-model-to-invent-an-observer");
                        Assert.That(observed.Observers, Is.Empty);
                        Assert.That(observed.Work[0].StartupTask.IsCompleted, Is.False);
                    }
                    if (originalOrdinal == 150)
                    {
                        Assert.That(observed.RequestStarted?.IsCompletedSuccessfully, Is.True);
                        Assert.That(observed.RequestCanceled?.IsCompleted, Is.False);
                        Assert.That(observed.Response, Is.Null,
                            "the-original-cancellable-request-has-no-artificial-release-task");
                    }
                    if (originalOrdinal == 152)
                    {
                        Assert.That(observed.Work[0], Is.Not.SameAs(observed.Work[1]));
                        Assert.That(observed.Work[0].RetirementTasks, Is.Not.Empty);
                        Assert.That(observed.Response?.IsCompleted, Is.False);
                    }
                });
                ready = true;
                throw marker;
            }

            scenario = originalOrdinal switch
            {
                128 => InitializationLifecycleSuite.RunDisposeRetiresSettingsWriteLeaseAsync(OnAcquired),
                150 => InitializationLifecycleSuite.RunStartupRefreshDisposeCancelsPendingWorkWithoutLoggingAsync(OnAcquired),
                152 => InitializationLifecycleSuite.RunReinitializedStartupRefreshCannotMutateNewSettingsAsync(OnAcquired),
                _ => throw new ArgumentOutOfRangeException(nameof(originalOrdinal)),
            };
            Exception? failure = null;
            try { await scenario; }
            catch (Exception error) { failure = error; }

            Assert.That(ready, Is.True, "published-fault-must-observe-the-real-acquired-owner-before-injection");
            Assert.That(ownership, Is.Not.Null);
            var completed = ownership!;
            Assert.Multiple(() =>
            {
                Assert.That(callbacks, Is.EqualTo(originalOrdinal == 152 ? 2 : 1));
                Assert.That(scenario.IsFaulted, Is.True);
                Assert.That(failure, Is.SameAs(marker), "published-cleanup-must-preserve-first-primary-identity");
                Assert.That(marker.Data[RegressionFixtureOwner.CleanupTypesKey], Is.Null,
                    "published-cleanup-unknown-secondary-fault-must-remain-visible");
                Assert.That(completed.Plugin.CaptureInitializationWork(), Is.Null,
                    "published-owner-must-retire-before-independent-rescue");
                foreach (var work in completed.Work)
                {
                    Assert.That(work.StartupTask.IsCompletedSuccessfully, Is.True,
                        "published-original-startup-must-complete-before-independent-rescue");
                    var retirement = work.RetirementTasks;
                    Assert.That(retirement, Is.Not.Empty,
                        "published-real-retirement-must-be-recorded-before-independent-rescue");
                    Assert.That(retirement.All(task => task.IsCompletedSuccessfully), Is.True,
                        "published-real-retirement-must-complete-before-independent-rescue");
                }
                Assert.That(completed.Observers.All(task => task.IsCompletedSuccessfully), Is.True,
                    "published-original-observer-must-complete-before-independent-rescue");
                if (originalOrdinal == 128)
                {
                    Assert.That(completed.RuntimeSettings!.ActiveWriteLease, Is.Zero,
                        "published-original-runtime-write-lease-must-retire-before-independent-rescue");
                    Assert.That(completed.ViewModel!.CopyVoiceIdCommand.CanExecute("11111111111111111111111111111111"), Is.False,
                        "published-original-view-model-must-retire-before-independent-rescue");
                }
                if (originalOrdinal == 150)
                    Assert.That(completed.RequestCanceled?.IsCompletedSuccessfully, Is.True,
                        "published-original-request-must-cancel-before-independent-rescue");
                if (originalOrdinal == 152)
                    Assert.That(completed.Response?.IsCompletedSuccessfully, Is.True,
                        "published-original-response-must-release-before-independent-rescue");
            });
        },
        () => { ownership?.Plugin.Dispose(); return Task.CompletedTask; },
        () => { ownership?.Release?.Invoke(); return Task.CompletedTask; },
        async () =>
        {
            if (ownership is null) return;
            await Task.WhenAll(ownership.Work.Select(work => work.StartupTask));
        },
        async () =>
        {
            if (ownership is null) return;
            await Task.WhenAll(ownership.Observers);
        },
        async () =>
        {
            if (ownership is null) return;
            await Task.WhenAll(ownership.Work.SelectMany(work => work.RetirementTasks));
        },
        () =>
        {
            if (ownership is not null && ownership.Work.Any(work => work.RetirementTasks.Count == 0))
                throw new InvalidOperationException("published-rescue-has-no-actual-retirement-task");
            return Task.CompletedTask;
        });
    }
}

internal sealed class PublishedInitializationObservation(
    Main plugin,
    IReadOnlyList<PluginInitializationCoordinator.InitializationWorkHandle> work,
    IReadOnlyList<Task> observers,
    SettingsViewModel? viewModel,
    STranslate.Plugin.Tts.FishAudio.Configuration.Settings? runtimeSettings,
    Action? release,
    Task? requestStarted,
    Task? requestCanceled,
    Task? response)
{
    internal Main Plugin { get; } = plugin;
    internal IReadOnlyList<PluginInitializationCoordinator.InitializationWorkHandle> Work { get; } = work;
    internal IReadOnlyList<Task> Observers { get; } = observers;
    internal SettingsViewModel? ViewModel { get; } = viewModel;
    internal STranslate.Plugin.Tts.FishAudio.Configuration.Settings? RuntimeSettings { get; } = runtimeSettings;
    internal Action? Release { get; } = release;
    internal Task? RequestStarted { get; } = requestStarted;
    internal Task? RequestCanceled { get; } = requestCanceled;
    internal Task? Response { get; } = response;
}
