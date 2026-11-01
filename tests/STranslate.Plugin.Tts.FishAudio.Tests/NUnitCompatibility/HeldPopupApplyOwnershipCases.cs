using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class HeldPopupApplyOwnershipCases
{
    public static IEnumerable<TestCaseData> HeldFailureRows()
    {
        yield return new TestCaseData(false).SetName("HeldPopupApply_FailureImmediatelyAfterApply")
            .SetProperty("FishAudioCaseId", "infra.popup-apply-ownership/failure-immediately-after-apply");
        yield return new TestCaseData(true).SetName("HeldPopupApply_FailureWithReopenedPendingDraft")
            .SetProperty("FishAudioCaseId", "infra.popup-apply-ownership/failure-with-reopened-pending-draft");
    }

    [TestCaseSource(nameof(HeldFailureRows))]
    public Task HeldBodyFailureJoinsTheOriginalTaskAfterOriginalRetirement(bool reopenedDraft) =>
        StaTestHost.RunOnStaThreadAsync(() =>
        {
            var fixture = new HeldPopupApplyFixtureOwner();
            var primary = new InvalidOperationException("held-popup-apply-body-failure");
            Task? originalApply = null;
            Task? originalHttp = null;
            Task? originalPublication = null;
            return RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try
                {
                    await SearchAdjustmentControlSuite.RunHeldApplyReopensAppliedIntentAsync(fixture, (point, owner) =>
                    {
                        var expected = reopenedDraft ? HeldPopupApplyFailurePoint.AfterPendingDraftChanged
                            : HeldPopupApplyFailurePoint.AfterApplyStarted;
                        if (point != expected) return;
                        Assert.That(SettingsViewModel.UiThreadInvokerOverride, Is.Null);
                        Assert.That(Application.Current is null || Application.Current.Dispatcher.CheckAccess(), Is.True,
                            "This fresh-host control observes the original inline UI route without replacing the invoker.");
                        Assert.That(owner.ActualApplyTasks.Count, Is.EqualTo(1));
                        originalApply = owner.ActualApplyTasks[0];
                        originalHttp = owner.HeldResponse!.Task;
                        originalPublication = owner.Publication!;
                        Assert.That(originalApply.IsCompleted, Is.False, "The failure occurs while the actual routed Apply work is held.");
                        Assert.That(originalHttp.IsCompleted, Is.False);
                        Assert.That(originalPublication.IsCompleted, Is.False);
                        Assert.That(originalApply, Is.Not.SameAs(originalHttp));
                        Assert.That(originalApply, Is.Not.SameAs(originalPublication));
                        Assert.That(owner.Http!.GetCallCount, Is.EqualTo(3), "Two original baselines and exactly one routed held Apply ran.");
                        Assert.That(((Popup)owner.Control!.FindName("AdjustmentPopup")).IsOpen, Is.EqualTo(reopenedDraft));
                        throw primary;
                    });
                }
                catch (Exception error) { observed = error; }

                // These are the actual pre-rescue oracles. A completed outer
                // guard, HTTP reply or publication signal cannot satisfy them.
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(originalApply, Is.Not.Null);
                Assert.That(originalHttp, Is.Not.Null);
                Assert.That(originalPublication, Is.Not.Null);
                Assert.That(fixture.ActualApplyTasks.Count, Is.EqualTo(1));
                Assert.That(fixture.ActualApplyTasks[0], Is.SameAs(originalApply));
                Assert.That(fixture.TasksBeforeFirstViewModelDispose, Has.Length.EqualTo(1));
                Assert.That(fixture.TasksBeforeFirstViewModelDispose[0], Is.SameAs(originalApply));
                Assert.That(fixture.PendingBeforeFirstViewModelDispose, Is.EqualTo(new[] { true }),
                    "Release did not turn the original Apply Task into a completed HTTP surrogate before VM retirement.");
                Assert.That(originalApply!.IsCompletedSuccessfully, Is.True,
                    "The original product Task, including its coordinator finally, is terminal after cleanup.");
                Assert.That(originalHttp!.IsCompletedSuccessfully, Is.True);
                Assert.That(originalPublication!.IsCompleted, Is.False,
                    "Original VM retirement rejects publication; joining the real task must still finish.");
                Assert.That(fixture.Http!.GetCallCount, Is.EqualTo(3), "Cleanup issued no replacement Search or Apply.");
                Assert.That(fixture.Source!.IsDisposed, Is.True);
                Assert.That((bool)typeof(SettingsViewModel).GetField("_isDisposed", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(fixture.ViewModel!)!, Is.True);
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey], Is.Null,
                    "Unexpected cleanup failures remain failures of this ownership control.");
            }, fixture.RescueAsync);
        });
}
