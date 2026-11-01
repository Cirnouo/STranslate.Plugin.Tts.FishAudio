using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class PopupDirectionOwnershipCases
{
    public static IEnumerable<TestCaseData> SourceAcquisitionFailureRows()
    {
        yield return new TestCaseData(false).SetName("DirectionSource_SearchAcquisitionFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/direction/search-source-acquisition-failure");
        yield return new TestCaseData(true).SetName("DirectionSource_LanguageAcquisitionFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/direction/language-source-acquisition-failure");
    }

    [TestCaseSource(nameof(SourceAcquisitionFailureRows))]
    public Task AnAcquiredSourceAndVmRetireBeforeInitializationFailureEscapes(bool language) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => DiagnosticFixtureOwner.RunAsync(async diagnostic =>
        {
            var primary = diagnostic.Expect(new InvalidOperationException("direction-source-acquisition-failure"));
            var secondary = new IOException("direction-source-cleanup-after-dispose");
            HwndSource? source = null;
            SettingsViewModel? vm = null;
            var popupEntries = 0;
            var work = diagnostic.CaptureWork();
            var hooks = new PopupDirectionFixtureHooks
            {
                SourceAcquired = (actualSource, actualVm) =>
                {
                    source = actualSource;
                    vm = actualVm;
                    Assert.That(source.IsDisposed, Is.False);
                    Assert.That(source.RootVisual, Is.Null);
                    throw primary;
                },
                PopupOpened = _ => popupEntries++,
                SourceDisposed = actual =>
                {
                    Assert.That(actual.IsDisposed, Is.True);
                    throw secondary;
                }
            };
            await RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try { await diagnostic.Track(PopupDirectionSuite.ObserveOwnershipControlAsync(language, hooks, work)); }
                catch (Exception error) { observed = error; }
                // Terminal ownership is asserted before the independent rescue,
                // under this same already-started STA root and action budget.
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(source, Is.Not.Null);
                Assert.That(source!.IsDisposed, Is.True);
                Assert.That(vm, Is.Not.Null);
                Assert.That(IsDisposed(vm!), Is.True, "A preceding source cleanup failure cannot skip VM retirement.");
                Assert.That(popupEntries, Is.Zero);
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey],
                    Is.EqualTo(new[] { typeof(IOException).FullName }));
                Assert.That(work.Worker, Is.Not.Null);
                Assert.That(work.Worker!.IsCompleted, Is.True);
            },
            () => { source?.Dispose(); return Task.CompletedTask; },
            () => { vm?.Dispose(); return Task.CompletedTask; });
        })));

    public static IEnumerable<TestCaseData> OpenBodyFailureRows()
    {
        yield return new TestCaseData(false, false).SetName("DirectionPopup_SearchFirstOpenFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/direction/search-first-open-failure");
        yield return new TestCaseData(true, false).SetName("DirectionPopup_LanguageFirstOpenFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/direction/language-first-open-failure");
        yield return new TestCaseData(false, true).SetName("DirectionPopup_SearchReopenFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/direction/search-reopen-failure");
        yield return new TestCaseData(true, true).SetName("DirectionPopup_LanguageReopenFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/direction/language-reopen-failure");
    }

    [TestCaseSource(nameof(OpenBodyFailureRows))]
    public Task AFailedFirstOrSecondOpenJoinsEachActualSourceBeforeBothOwnersRetire(bool language, bool reopen) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => DiagnosticFixtureOwner.RunAsync(async diagnostic =>
        {
            var primary = diagnostic.Expect(new InvalidOperationException("direction-open-body-failure"));
            var secondary = new IOException("direction-source-cleanup-after-dispose");
            var probes = new List<PopupDirectionFixtureProbe>();
            var sourceRetiringEvents = 0;
            var work = diagnostic.CaptureWork();
            var hooks = new PopupDirectionFixtureHooks
            {
                PopupOpened = probe =>
                {
                    probes.Add(probe);
                    Assert.That(probe.HostSource.IsDisposed, Is.False);
                    Assert.That(probe.Lifetime.Source, Is.Not.Null);
                    Assert.That(probe.Lifetime.Source!.IsDisposed, Is.False);
                    Assert.That(probe.Lifetime.ClosedTask.IsCompleted, Is.False);
                    if (probes.Count == (reopen ? 2 : 1)) throw primary;
                },
                SourceRetiring = actual =>
                {
                    sourceRetiringEvents++;
                    Assert.That(actual.IsDisposed, Is.False);
                    foreach (var probe in probes)
                    {
                        Assert.That(probe.Lifetime.ClosedTask.IsCompletedSuccessfully, Is.True);
                        Assert.That(probe.Lifetime.Source!.IsDisposed, Is.True);
                        Assert.That(probe.Lifetime.InClosedCallback, Is.False);
                        Assert.That(probe.Lifetime.HandlersAttached, Is.False);
                        Assert.That(IsDisposed(probe.ViewModel), Is.False);
                        Assert.That(OwnsInput(probe), Is.False, "The process-global input listener retires before its host source.");
                        Assert.That(probe.Control is VoiceLanguageTags tags ? tags.Material.Handle
                            : ((SearchAdjustmentControl)probe.Control).Material.Handle, Is.EqualTo(IntPtr.Zero));
                    }
                },
                SourceDisposed = actual =>
                {
                    Assert.That(actual.IsDisposed, Is.True);
                    throw secondary;
                }
            };
            await RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try { await diagnostic.Track(PopupDirectionSuite.ObserveOwnershipControlAsync(language, hooks, work, reopen)); }
                catch (Exception error) { observed = error; }
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(probes.Count, Is.EqualTo(reopen ? 2 : 1));
                Assert.That(sourceRetiringEvents, Is.EqualTo(1));
                foreach (var probe in probes)
                {
                    Assert.That(probe.Lifetime.PendingAfterCloseSetter, Is.True,
                        "IsOpen=false returns before the actual CLR Closed/source retirement.");
                    Assert.That(probe.Lifetime.SourceAliveAfterCloseSetter, Is.True);
                    Assert.That(probe.Lifetime.ClosedTask.IsCompletedSuccessfully, Is.True);
                    Assert.That(probe.Lifetime.Source!.IsDisposed, Is.True);
                    Assert.That(probe.Lifetime.OpenedEvents, Is.EqualTo(1));
                    Assert.That(probe.Lifetime.ClosedEvents, Is.EqualTo(1));
                    Assert.That(probe.Lifetime.ClosureVerified, Is.True);
                    Assert.That(probe.Lifetime.InClosedCallback, Is.False);
                    Assert.That(probe.Lifetime.HandlersAttached, Is.False);
                    Assert.That(probe.HostSource.IsDisposed, Is.True);
                    Assert.That(IsDisposed(probe.ViewModel), Is.True);
                }
                if (reopen)
                {
                    Assert.That(probes[1].HostSource, Is.SameAs(probes[0].HostSource));
                    Assert.That(probes[1].ViewModel, Is.SameAs(probes[0].ViewModel));
                    Assert.That(probes[1].Lifetime.Source, Is.Not.SameAs(probes[0].Lifetime.Source));
                }
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey],
                    Is.EqualTo(new[] { typeof(IOException).FullName }));
                Assert.That(work.Worker, Is.Not.Null);
                Assert.That(work.Worker!.IsCompleted, Is.True);
            },
            () => RegressionFixtureOwner.RunAsync(() => Task.CompletedTask,
                probes.Select(probe => (Func<Task>)probe.Lifetime.RescueAsync).ToArray()),
            () => { foreach (var probe in probes) probe.HostSource.Dispose(); return Task.CompletedTask; },
            () => { foreach (var probe in probes) probe.ViewModel.Dispose(); return Task.CompletedTask; });
        })));

    public static IEnumerable<TestCaseData> CloseRequestFailureRows()
    {
        yield return new TestCaseData(false).SetName("DirectionPopup_SearchCloseRequestFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/direction/search-close-request-failure");
        yield return new TestCaseData(true).SetName("DirectionPopup_LanguageCloseRequestFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/direction/language-close-request-failure");
    }

    [TestCaseSource(nameof(CloseRequestFailureRows))]
    public Task AFailureAfterTheRealCloseRequestStillJoinsTheActualClosedBeforeRetirement(bool language) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => DiagnosticFixtureOwner.RunAsync(async diagnostic =>
        {
            var primary = diagnostic.Expect(new InvalidOperationException("direction-close-request-failure"));
            PopupDirectionFixtureProbe? probe = null;
            var beforeDisposeEvents = 0;
            var closeFailureReturnedEvents = 0;
            var work = diagnostic.CaptureWork();
            var hooks = new PopupDirectionFixtureHooks
            {
                PopupOpened = value => probe = value,
                CloseFailureReturned = (value, error) =>
                {
                    closeFailureReturnedEvents++;
                    Assert.That(error, Is.SameAs(primary));
                    Assert.That(value.HostSource.IsDisposed, Is.False);
                    Assert.That(IsDisposed(value.ViewModel), Is.False);
                    Assert.That(value.Lifetime.ClosedTask.IsCompletedSuccessfully, Is.True,
                        "The helper must join actual Closed before throwing; later fixture cleanup cannot satisfy this oracle.");
                    Assert.That(value.Lifetime.Source!.IsDisposed, Is.True);
                    Assert.That(value.Lifetime.InClosedCallback, Is.False);
                    Assert.That(value.Lifetime.ClosureVerified, Is.True);
                },
                SourceRetiring = source =>
                {
                    beforeDisposeEvents++;
                    Assert.That(source.IsDisposed, Is.False);
                    Assert.That(probe, Is.Not.Null);
                    Assert.That(IsDisposed(probe!.ViewModel), Is.False);
                    Assert.That(probe.Lifetime.ClosedTask.IsCompletedSuccessfully, Is.True);
                    Assert.That(probe.Lifetime.Source!.IsDisposed, Is.True);
                    Assert.That(probe.Lifetime.InClosedCallback, Is.False);
                    Assert.That(probe.Lifetime.HandlersAttached, Is.False);
                }
            };
            await RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try { await diagnostic.Track(PopupDirectionSuite.ObserveCloseFailureControlAsync(language, hooks, work, primary)); }
                catch (Exception error) { observed = error; }
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(beforeDisposeEvents, Is.EqualTo(1));
                Assert.That(closeFailureReturnedEvents, Is.EqualTo(1));
                Assert.That(probe, Is.Not.Null);
                Assert.That(probe!.Lifetime.PendingAfterCloseSetter, Is.True);
                Assert.That(probe.Lifetime.SourceAliveAfterCloseSetter, Is.True);
                Assert.That(probe.Lifetime.ClosedTask.IsCompletedSuccessfully, Is.True);
                Assert.That(probe.Lifetime.Source!.IsDisposed, Is.True);
                Assert.That(probe.Lifetime.OpenedEvents, Is.EqualTo(1));
                Assert.That(probe.Lifetime.ClosedEvents, Is.EqualTo(1));
                Assert.That(probe.Lifetime.ClosureVerified, Is.True);
                Assert.That(probe.Lifetime.HandlersAttached, Is.False);
                Assert.That(probe.HostSource.IsDisposed, Is.True);
                Assert.That(IsDisposed(probe.ViewModel), Is.True);
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey], Is.Null);
                Assert.That(work.Worker, Is.Not.Null);
                Assert.That(work.Worker!.IsCompleted, Is.True);
            },
            () => probe?.Lifetime.RescueAsync() ?? Task.CompletedTask,
            () => { probe?.HostSource.Dispose(); return Task.CompletedTask; },
            () => { probe?.ViewModel.Dispose(); return Task.CompletedTask; });
        })));

    [Test]
    [Property("FishAudioCaseId", "infra.native-popups/direction/close-marker-and-real-reopen-validation-failure")]
    public Task ACloseMarkerRetainsASecondRealClosedValidationFailureBeforeFallbackCleanup() =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => DiagnosticFixtureOwner.RunAsync(async diagnostic =>
        {
            var primary = diagnostic.Expect(new InvalidOperationException("direction-close-marker-with-real-reopen"));
            PopupDirectionFixtureProbe? probe = null;
            HwndSource? reopenedSource = null;
            var returnedEvents = 0;
            var work = diagnostic.CaptureWork();
            var hooks = new PopupDirectionFixtureHooks
            {
                PopupOpened = value => probe = value,
                CloseFailureReturned = (value, error) =>
                {
                    returnedEvents++;
                    Assert.That(error, Is.SameAs(primary));
                    Assert.That(value.HostSource.IsDisposed, Is.False);
                    Assert.That(IsDisposed(value.ViewModel), Is.False);
                    Assert.That(value.Lifetime.ClosedTask.IsCompletedSuccessfully, Is.True);
                    Assert.That(value.Lifetime.Source!.IsDisposed, Is.True);
                    Assert.That(reopenedSource, Is.Not.Null);
                    Assert.That(reopenedSource!.IsDisposed, Is.True);
                    Assert.That(value.Lifetime.OpenedEvents, Is.EqualTo(2));
                    Assert.That(value.Lifetime.ClosedEvents, Is.EqualTo(1));
                    Assert.That(value.Lifetime.ClosureVerified, Is.False,
                        "Actual native retirement cannot admit a repeated-Opened event sequence.");
                    Assert.That(value.Lifetime.InClosedCallback, Is.False);
                    Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey],
                        Is.EqualTo(new[] { typeof(InvalidOperationException).FullName }),
                        "The helper retains its own join failure before fixture fallback cleanup can re-observe it.");
                }
            };
            await RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try
                {
                    await diagnostic.Track(PopupDirectionSuite.ObserveCloseFailureControlAsync(false, hooks, work, primary, value =>
                    {
                        var popup = (Popup)value.Control.FindName("AdjustmentPopup");
                        popup.IsOpen = false;
                        value.Lifetime.RecordCloseSetterReturn();
                        Assert.That(value.Lifetime.ClosedTask.IsCompleted, Is.False);
                        Assert.That(value.Lifetime.Source!.IsDisposed, Is.False);
                        popup.IsOpen = true;
                        value.Lifetime.CaptureSource();
                        reopenedSource = popup.Child is { } child ? PresentationSource.FromVisual(child) as HwndSource : null;
                        Assert.That(reopenedSource, Is.Not.Null);
                        Assert.That(reopenedSource!.IsDisposed, Is.False);
                        Assert.That(value.Lifetime.OpenedEvents, Is.EqualTo(2),
                            "This control requires a real native pending-close reopen; no event is fabricated.");
                        popup.IsOpen = false;
                        throw primary;
                    }));
                }
                catch (Exception error) { observed = error; }
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(returnedEvents, Is.EqualTo(1));
                Assert.That(probe, Is.Not.Null);
                Assert.That(probe!.Lifetime.ClosedTask.IsCompletedSuccessfully, Is.True);
                Assert.That(probe.Lifetime.Source!.IsDisposed, Is.True);
                Assert.That(reopenedSource!.IsDisposed, Is.True);
                Assert.That(probe.Lifetime.HandlersAttached, Is.False);
                Assert.That(probe.HostSource.IsDisposed, Is.True);
                Assert.That(IsDisposed(probe.ViewModel), Is.True);
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey],
                    Does.Contain(typeof(InvalidOperationException).FullName));
                Assert.That(work.Worker, Is.Not.Null);
                Assert.That(work.Worker!.IsCompleted, Is.True);
            },
            () => { probe?.Lifetime.Detach(); return Task.CompletedTask; },
            () => { probe?.HostSource.Dispose(); return Task.CompletedTask; },
            () => { probe?.ViewModel.Dispose(); return Task.CompletedTask; });
        })));

    private static bool IsDisposed(SettingsViewModel vm) =>
        (bool)typeof(SettingsViewModel).GetField("_isDisposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;

    private static bool OwnsInput(PopupDirectionFixtureProbe probe) =>
        (bool)probe.Control.GetType().GetField(probe.Control is VoiceLanguageTags ? "_ownsPopupInput" : "_ownsInput",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(probe.Control)!;
}
