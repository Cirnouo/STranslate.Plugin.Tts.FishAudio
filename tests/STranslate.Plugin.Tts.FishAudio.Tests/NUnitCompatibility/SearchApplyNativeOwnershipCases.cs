using System.Reflection;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SearchApplyNativeOwnershipCases
{
    public static IEnumerable<TestCaseData> DraftApplyFailureRows()
    {
        yield return new TestCaseData(1).SetName("SearchDraft_FirstActualApplyAndSourceCleanupFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/search-apply/first-draft-apply-and-source-cleanup-failure");
        yield return new TestCaseData(2).SetName("SearchDraft_SecondActualApplyAndSourceCleanupFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/search-apply/second-draft-apply-and-source-cleanup-failure");
    }

    [TestCaseSource(nameof(DraftApplyFailureRows))]
    public Task OriginalDraftApplyTasksAndNativeSourcesRetireAfterFailure(int applyCount) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(async () =>
        {
            var primary = new InvalidOperationException("search-draft-after-actual-apply-failure");
            var secondary = new IOException("search-draft-after-actual-host-dispose");
            SearchApplyFixtureOwner? fixture = null;
            Task[] actual = [];
            var nativeRetiredEvents = 0;
            var workRetiredEvents = 0;
            var hooks = new SearchApplyFixtureHooks
            {
                PopupHooks = new SearchApplyPopupHooks
                {
                    LifetimesRetired = popups =>
                    {
                        nativeRetiredEvents++;
                        Assert.That(popups.HostSource!.IsDisposed, Is.False);
                        Assert.That(IsDisposed(popups.ViewModel!), Is.False);
                        Assert.That(popups.Lifetimes.Count, Is.EqualTo(applyCount == 1 ? 2 : 4));
                        foreach (var lifetime in popups.Lifetimes) AssertPopupTerminal(lifetime);
                    }
                },
                AfterApplyStarted = owner =>
                {
                    if (owner.ActualApplyTasks.Count != applyCount) return;
                    fixture = owner;
                    actual = owner.ActualApplyTasks.ToArray();
                    Assert.That(actual.Length, Is.EqualTo(applyCount));
                    Assert.That(owner.Source!.IsDisposed, Is.False);
                    Assert.That(IsDisposed(owner.ViewModel!), Is.False);
                    throw primary;
                },
                SourceDisposed = source =>
                {
                    Assert.That(source.IsDisposed, Is.True);
                    throw secondary;
                },
                WorkRetired = owner =>
                {
                    workRetiredEvents++;
                    Assert.That(owner, Is.SameAs(fixture));
                    Assert.That(owner.Source!.IsDisposed, Is.True);
                    Assert.That(IsDisposed(owner.ViewModel!), Is.True);
                    AssertOriginalTaskReferences(owner.TasksBeforeViewModelDispose, actual);
                    AssertOriginalTaskReferences(owner.JoinedApplyTasks, actual);
                    foreach (var task in actual) Assert.That(task.IsCompleted, Is.True);
                    Assert.That(owner.ObservationEnded, Is.True);
                    Assert.That(typeof(SearchAdjustmentControl).GetField("_applyTaskObservation",
                        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner.Control), Is.Null);
                }
            };
            await RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try { await SearchAdjustmentControlSuite.RunSearchDraftOwnershipControlAsync(hooks); }
                catch (Exception error) { observed = error; }
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(nativeRetiredEvents, Is.EqualTo(1));
                Assert.That(workRetiredEvents, Is.EqualTo(1));
                Assert.That(fixture, Is.Not.Null);
                foreach (var lifetime in fixture!.Popups.Lifetimes) AssertPopupTerminal(lifetime);
                Assert.That(fixture.Source!.IsDisposed, Is.True);
                Assert.That(IsDisposed(fixture.ViewModel!), Is.True);
                AssertOriginalTaskReferences(fixture.JoinedApplyTasks, actual);
                foreach (var task in actual) Assert.That(task.IsCompleted, Is.True);
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey],
                    Is.EqualTo(new[] { typeof(IOException).FullName }));
            },
            async () =>
            {
                if (fixture is null) return;
                foreach (var lifetime in fixture.Popups.Lifetimes) await lifetime.RescueAsync();
            },
            () => { fixture?.Source?.Dispose(); return Task.CompletedTask; },
            () => { fixture?.ViewModel?.Dispose(); return Task.CompletedTask; },
            async () =>
            {
                if (actual.Length != 0) await Task.WhenAll(actual);
            },
            () => { fixture?.ApplyObservation?.Dispose(); return Task.CompletedTask; });
        }));

    [Test]
    [Property("FishAudioCaseId", "infra.native-popups/search-apply/held-second-popup-before-original-g1-tail")]
    public Task HeldPopupActualClosedPrecedesUnchangedG1ActualTaskTail() =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(async () =>
        {
            var primary = new InvalidOperationException("held-second-native-popup-failure");
            var secondary = new IOException("held-after-real-popup-retirement");
            var owner = new HeldPopupApplyFixtureOwner();
            SearchApplyPopupSequenceOwner? sequence = null;
            SearchApplyPopupProbe? probe = null;
            Task[] actual = [];
            var nativeRetiredEvents = 0;
            var hooks = new SearchApplyPopupHooks
            {
                PopupOpened = value =>
                {
                    if (value.OpenOrdinal != 2) return;
                    probe = value;
                    Assert.That(value.HostSource.IsDisposed, Is.False);
                    Assert.That(IsDisposed(value.ViewModel), Is.False);
                    Assert.That(owner.ActualApplyTasks.Count, Is.EqualTo(1));
                    actual = owner.ActualApplyTasks.ToArray();
                    Assert.That(actual[0].IsCompleted, Is.False);
                    Assert.That(owner.HeldResponse!.Task.IsCompleted, Is.False);
                    throw primary;
                },
                LifetimesRetired = value =>
                {
                    sequence = value;
                    nativeRetiredEvents++;
                    Assert.That(value.Lifetimes.Count, Is.EqualTo(2));
                    foreach (var lifetime in value.Lifetimes) AssertPopupTerminal(lifetime);
                    Assert.That(value.HostSource!.IsDisposed, Is.False);
                    Assert.That(IsDisposed(value.ViewModel!), Is.False);
                    Assert.That(actual.Length, Is.EqualTo(1));
                    Assert.That(actual[0].IsCompleted, Is.False);
                    Assert.That(owner.HeldResponse!.Task.IsCompleted, Is.False);
                    Assert.That(owner.TasksBeforeFirstViewModelDispose, Is.Empty);
                    throw secondary;
                }
            };
            await RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try { await SearchAdjustmentControlSuite.RunSearchHeldNativeOwnershipControlAsync(owner, hooks); }
                catch (Exception error) { observed = error; }
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(nativeRetiredEvents, Is.EqualTo(1));
                Assert.That(probe, Is.Not.Null);
                Assert.That(sequence, Is.Not.Null);
                foreach (var lifetime in sequence!.Lifetimes) AssertPopupTerminal(lifetime);
                Assert.That(probe!.HostSource.IsDisposed, Is.True);
                Assert.That(IsDisposed(probe.ViewModel), Is.True);
                Assert.That(owner.HeldResponse!.Task.IsCompletedSuccessfully, Is.True);
                Assert.That(owner.PublicationHandler, Is.Null);
                Assert.That(typeof(SearchAdjustmentControl).GetField("_applyTaskObservation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner.Control), Is.Null);
                AssertOriginalTaskReferences(owner.TasksBeforeFirstViewModelDispose, actual);
                AssertOriginalTaskReferences(owner.ActualApplyTasks.ToArray(), actual);
                foreach (var task in actual) Assert.That(task.IsCompleted, Is.True);
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey],
                    Is.EqualTo(new[] { typeof(IOException).FullName }));
            },
            async () =>
            {
                if (sequence is null) return;
                foreach (var lifetime in sequence.Lifetimes) await lifetime.RescueAsync();
            },
            owner.RescueAsync);
        }));

    private static void AssertOriginalTaskReferences(IReadOnlyList<Task> observed, IReadOnlyList<Task> actual)
    {
        Assert.That(observed.Count, Is.EqualTo(actual.Count));
        for (var index = 0; index < actual.Count; index++)
            Assert.That((object)observed[index], Is.SameAs((object)actual[index]));
    }

    private static void AssertPopupTerminal(NativePopupLifetimeOwner lifetime)
    {
        Assert.That(lifetime.Source, Is.Not.Null);
        Assert.That(lifetime.Source!.IsDisposed, Is.True);
        Assert.That(lifetime.OpenedEvents, Is.EqualTo(1));
        Assert.That(lifetime.ClosedEvents, Is.EqualTo(1));
        Assert.That(lifetime.ClosedTask.IsCompletedSuccessfully, Is.True);
        Assert.That(lifetime.ClosureVerified, Is.True);
        Assert.That(lifetime.HandlersAttached, Is.False);
        Assert.That(lifetime.InClosedCallback, Is.False);
    }

    private static bool IsDisposed(SettingsViewModel vm) =>
        (bool)typeof(SettingsViewModel).GetField("_isDisposed",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
}
