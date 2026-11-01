using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class NativeCardTooltipOwnershipCases
{
    public static IEnumerable<TestCaseData> HeldBodyFailureRows()
    {
        yield return new TestCaseData(false).SetName("CardTooltip_LayoutBodyFailure")
            .SetProperty("FishAudioCaseId", "infra.native-cards/tooltip/layout-body-failure");
        yield return new TestCaseData(true).SetName("CardTooltip_DescriptionBodyAndSourceCleanupFailure")
            .SetProperty("FishAudioCaseId", "infra.native-cards/tooltip/description-body-and-source-cleanup-failure");
    }

    [TestCaseSource(nameof(HeldBodyFailureRows))]
    public Task BodyFailureWaitsForTheActualTooltipBeforeBothOwnersRetire(bool description) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => DiagnosticFixtureOwner.RunAsync(async diagnostic =>
        {
            var primary = diagnostic.Expect(new InvalidOperationException("native-card-tooltip-body-failure"));
            var secondary = new IOException("native-card-source-cleanup-after-dispose");
            CardTooltipFixtureProbe? probe = null;
            ToolTip? tooltip = null;
            var existingOpenedEvents = 0;
            var existingClosedEvents = 0;
            RoutedEventHandler existingOpened = (_, _) => existingOpenedEvents++;
            RoutedEventHandler existingClosed = (_, _) => existingClosedEvents++;
            var priorTarget = new Border();
            BindingBase? priorBinding = null;
            object? priorLocal = null;
            UIElement? priorEffective = null;
            var work = diagnostic.CaptureWork();
            var hooks = new CardTooltipFixtureHooks
            {
                BeforeTooltipOwnership = value =>
                {
                    tooltip = value;
                    if (description)
                    {
                        priorBinding = new Binding { Source = priorTarget };
                        BindingOperations.SetBinding(value, ToolTip.PlacementTargetProperty, priorBinding);
                    }
                    priorLocal = value.ReadLocalValue(ToolTip.PlacementTargetProperty);
                    priorEffective = value.PlacementTarget;
                    value.Opened += existingOpened;
                    value.Closed += existingClosed;
                },
                TooltipReady = value =>
                {
                    probe = value;
                    Assert.That(value.HostSource.IsDisposed, Is.False);
                    Assert.That(value.Tooltip.Source, Is.Not.Null);
                    Assert.That(value.Tooltip.Source!.IsDisposed, Is.False);
                    Assert.That(value.Tooltip.ClosedTask.IsCompleted, Is.False);
                    Assert.That(value.Tooltip.ToolTip.IsOpen, Is.True);
                    throw primary;
                },
                SourceDisposed = source =>
                {
                    Assert.That(source.IsDisposed, Is.True);
                    if (description) throw secondary;
                }
            };
            await RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try
                {
                    var scenario = description
                        ? VoiceDescriptionSuite.ObserveTooltipOwnershipControlAsync(hooks, work)
                        : VoiceCardLayoutSuite.ObserveTooltipOwnershipControlAsync(hooks);
                    await diagnostic.Track(scenario);
                }
                catch (Exception error) { observed = error; }
                // Authoritative oracles precede independent rescue, on this same STA root.
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(probe, Is.Not.Null);
                Assert.That(probe!.Tooltip.PendingAfterCloseSetter, Is.True,
                    "IsOpen=false returned before the actual native Closed/source completion.");
                Assert.That(probe.Tooltip.SourceAliveAfterCloseSetter, Is.True);
                Assert.That(probe.Tooltip.ClosedTask.IsCompletedSuccessfully, Is.True);
                Assert.That(probe.Tooltip.Source!.IsDisposed, Is.True);
                Assert.That(probe.Tooltip.OpenedEvents, Is.EqualTo(1));
                Assert.That(probe.Tooltip.ToolTipClosedEvents, Is.EqualTo(1));
                Assert.That(probe.Tooltip.PopupClosedEvents, Is.EqualTo(1));
                Assert.That(probe.Tooltip.ClosureVerified, Is.True);
                Assert.That(probe.Tooltip.InClosedCallback, Is.False);
                Assert.That(probe.Tooltip.HandlersAttached, Is.False);
                Assert.That(probe.Tooltip.PlacementRestored, Is.True);
                Assert.That(probe.HostSource.IsDisposed, Is.True);
                Assert.That(IsDisposed(probe.ViewModel), Is.True);
                Assert.That(existingOpenedEvents, Is.EqualTo(1), "The pre-existing Opened handler remains installed.");
                Assert.That(existingClosedEvents, Is.EqualTo(1), "The pre-existing handler survives observer detachment.");
                Assert.That(tooltip!.PlacementTarget, Is.SameAs(priorEffective));
                Assert.That(BindingOperations.GetBindingBase(tooltip, ToolTip.PlacementTargetProperty), Is.SameAs(priorBinding));
                if (priorBinding is null)
                    Assert.That(tooltip.ReadLocalValue(ToolTip.PlacementTargetProperty), Is.SameAs(priorLocal));
                if (description)
                {
                    Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey], Is.EqualTo(new[] { typeof(IOException).FullName }));
                    Assert.That(work.Worker, Is.Not.Null);
                    Assert.That(work.Worker!.IsCompleted, Is.True, "The bounded diagnostic wrapper cannot stand in for its actual worker.");
                }
                else Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey], Is.Null);
            },
            () => probe?.Tooltip.RescueAsync() ?? Task.CompletedTask,
            () => { if (tooltip is not null) tooltip.Opened -= existingOpened; return Task.CompletedTask; },
            () => { if (tooltip is not null) tooltip.Closed -= existingClosed; return Task.CompletedTask; },
            () => { probe?.HostSource.Dispose(); return Task.CompletedTask; },
            () => { probe?.ViewModel.Dispose(); return Task.CompletedTask; });
        })));

    public static IEnumerable<TestCaseData> SourceAcquisitionFailureRows()
    {
        yield return new TestCaseData(false).SetName("CardSource_LayoutAcquisitionFailure")
            .SetProperty("FishAudioCaseId", "infra.native-cards/source/layout-acquisition-failure");
        yield return new TestCaseData(true).SetName("CardSource_DescriptionAcquisitionFailure")
            .SetProperty("FishAudioCaseId", "infra.native-cards/source/description-acquisition-failure");
    }

    [TestCaseSource(nameof(SourceAcquisitionFailureRows))]
    public Task NewlyAcquiredSourceAndVmRetireBeforeAttachmentFailureEscapes(bool description) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => DiagnosticFixtureOwner.RunAsync(async diagnostic =>
        {
            var primary = diagnostic.Expect(new InvalidOperationException("native-card-source-acquisition-failure"));
            var secondary = new IOException("native-card-source-cleanup-after-dispose");
            HwndSource? source = null;
            SettingsViewModel? vm = null;
            var tooltipEntries = 0;
            var work = diagnostic.CaptureWork();
            var hooks = new CardTooltipFixtureHooks
            {
                SourceAcquired = (actualSource, actualVm) =>
                {
                    source = actualSource; vm = actualVm;
                    Assert.That(source.IsDisposed, Is.False);
                    Assert.That(source.RootVisual, Is.Null);
                    throw primary;
                },
                BeforeTooltipOwnership = _ => tooltipEntries++,
                SourceDisposed = actual => { Assert.That(actual.IsDisposed, Is.True); throw secondary; }
            };
            await RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try
                {
                    await diagnostic.Track(description
                        ? VoiceDescriptionSuite.ObserveTooltipOwnershipControlAsync(hooks, work)
                        : VoiceCardLayoutSuite.ObserveTooltipOwnershipControlAsync(hooks));
                }
                catch (Exception error) { observed = error; }
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(source, Is.Not.Null);
                Assert.That(source!.IsDisposed, Is.True);
                Assert.That(vm, Is.Not.Null);
                Assert.That(IsDisposed(vm!), Is.True);
                Assert.That(tooltipEntries, Is.Zero, "Failure precedes attachment, tooltip opening and any replacement fixture.");
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey], Is.EqualTo(new[] { typeof(IOException).FullName }));
                if (description)
                {
                    Assert.That(work.Worker, Is.Not.Null);
                    Assert.That(work.Worker!.IsCompleted, Is.True);
                }
            },
            () => { source?.Dispose(); return Task.CompletedTask; },
            () => { vm?.Dispose(); return Task.CompletedTask; });
        })));

    public static IEnumerable<TestCaseData> RejectedClosedRows()
    {
        yield return new TestCaseData(false).SetName("CardTooltip_ClosedWithoutOpenedSourceRejected")
            .SetProperty("FishAudioCaseId", "infra.native-cards/tooltip/closed-without-opened-source-rejected");
        yield return new TestCaseData(true).SetName("CardTooltip_PrematureDuplicateRoutedClosedRejected")
            .SetProperty("FishAudioCaseId", "infra.native-cards/tooltip/premature-duplicate-routed-closed-rejected");
    }

    [TestCaseSource(nameof(RejectedClosedRows))]
    public Task RoutedClosedAloneCannotAdmitNativeCleanup(bool realOpen) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(() =>
        {
            NativeCardToolTipOwner? owner = null;
            CardTooltipFixtureProbe? probe = null;
            return RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try
                {
                    if (realOpen)
                    {
                        await VoiceCardLayoutSuite.ObserveTooltipOwnershipControlAsync(new CardTooltipFixtureHooks
                        {
                            TooltipReady = value =>
                            {
                                probe = value; owner = value.Tooltip;
                                Assert.That(owner.Source!.IsDisposed, Is.False);
                                owner.ToolTip.RaiseEvent(new RoutedEventArgs(ToolTip.ClosedEvent, owner.ToolTip));
                                owner.ToolTip.RaiseEvent(new RoutedEventArgs(ToolTip.ClosedEvent, owner.ToolTip));
                                Assert.That(owner.ClosedTask.IsCompleted, Is.False,
                                    "Premature duplicate routed events do not complete the actual Popup/source join.");
                                Assert.That(owner.Source.IsDisposed, Is.False);
                            }
                        });
                    }
                    else
                    {
                        var tooltip = new ToolTip { Content = "No native source fixture" };
                        owner = new NativeCardToolTipOwner(tooltip);
                        await owner.RunAsync(() =>
                        {
                            tooltip.RaiseEvent(new RoutedEventArgs(ToolTip.ClosedEvent, tooltip));
                            Assert.That(owner.ClosedTask.IsCompleted, Is.False);
                            return Task.CompletedTask;
                        });
                    }
                }
                catch (Exception error) { observed = error; }
                Assert.That(observed, Is.TypeOf<InvalidOperationException>());
                Assert.That(observed!.Message, Is.EqualTo(realOpen
                    ? "native-card-tooltip-closed-sequence-invalid" : "native-card-tooltip-open-source-not-observed"));
                Assert.That(owner, Is.Not.Null);
                Assert.That(owner!.ClosureVerified, Is.False);
                Assert.That(owner.HandlersAttached, Is.False);
                Assert.That(owner.PlacementRestored, Is.True);
                if (realOpen)
                {
                    Assert.That(owner.PendingAfterCloseSetter, Is.True);
                    Assert.That(owner.SourceAliveAfterCloseSetter, Is.True);
                    Assert.That(owner.ToolTipClosedEvents, Is.EqualTo(3));
                    Assert.That(owner.PopupClosedEvents, Is.EqualTo(1));
                    Assert.That(owner.ClosedTask.IsCompletedSuccessfully, Is.True,
                        "Actual disposal may be terminal while event correspondence still rejects admission.");
                    Assert.That(owner.Source!.IsDisposed, Is.True);
                    Assert.That(probe!.HostSource.IsDisposed, Is.True);
                    Assert.That(IsDisposed(probe.ViewModel), Is.True);
                }
                else
                {
                    Assert.That(owner.Source, Is.Null);
                    Assert.That(owner.PopupClosedEvents, Is.Zero);
                    Assert.That(owner.ClosedTask.IsCompleted, Is.False);
                }
            },
            () => owner?.RescueAsync() ?? Task.CompletedTask,
            () => { probe?.HostSource.Dispose(); return Task.CompletedTask; },
            () => { probe?.ViewModel.Dispose(); return Task.CompletedTask; });
        }));

    private static bool IsDisposed(SettingsViewModel vm) =>
        (bool)typeof(SettingsViewModel).GetField("_isDisposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
}
