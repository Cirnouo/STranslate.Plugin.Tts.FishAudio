using System.Reflection;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SearchPopupOwnershipCases
{
    public static IEnumerable<TestCaseData> OpenFailureRows()
    {
        yield return new TestCaseData(0).SetName("SearchPopup_RadioOpenAndSourceCleanupFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/search/radio-open-and-source-cleanup-failure");
        yield return new TestCaseData(1).SetName("SearchPopup_MaterialOwnSourceOpenAndCleanupFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/search/material-own-source-open-and-cleanup-failure");
        yield return new TestCaseData(2).SetName("SearchPopup_MaterialFallbackOpenAndCleanupFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/search/material-fallback-open-and-cleanup-failure");
    }

    [TestCaseSource(nameof(OpenFailureRows))]
    public Task FailedRadioAndMaterialOpenJoinsActualClosedBeforeHostAndVmRetire(int scene) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(async () =>
        {
            var primary = new InvalidOperationException("search-popup-open-failure");
            var secondary = new IOException("search-popup-host-cleanup-after-dispose");
            SearchPopupFixtureProbe? probe = null;
            var beforeDisposeEvents = 0;
            var hooks = new SearchPopupFixtureHooks
            {
                PopupOpened = value =>
                {
                    probe = value;
                    Assert.That(value.HostSource.IsDisposed, Is.False);
                    Assert.That(value.Lifetime.Source, Is.Not.Null);
                    Assert.That(value.Lifetime.Source!.IsDisposed, Is.False);
                    Assert.That(value.Lifetime.ClosedTask.IsCompleted, Is.False);
                    throw primary;
                },
                SourceRetiring = source =>
                {
                    beforeDisposeEvents++;
                    Assert.That(source.IsDisposed, Is.False);
                    Assert.That(probe, Is.Not.Null);
                    Assert.That(IsDisposed(probe!.ViewModel), Is.False);
                    Assert.That(probe.Lifetime.ClosedTask.IsCompletedSuccessfully, Is.True);
                    Assert.That(probe.Lifetime.Source!.IsDisposed, Is.True);
                    Assert.That(probe.Lifetime.ClosureVerified, Is.True);
                    Assert.That(probe.Lifetime.InClosedCallback, Is.False);
                    Assert.That(probe.Lifetime.HandlersAttached, Is.False);
                    if (probe.Control is SearchAdjustmentControl search)
                    {
                        Assert.That((bool)typeof(SearchAdjustmentControl).GetField("_ownsInput",
                            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(search)!, Is.False);
                        Assert.That(search.Material.Handle, Is.EqualTo(IntPtr.Zero));
                    }
                    else
                    {
                        Assert.That(probe.Material, Is.Not.Null);
                        Assert.That(probe.Material!.Handle, Is.EqualTo(IntPtr.Zero));
                    }
                },
                SourceDisposed = source =>
                {
                    Assert.That(source.IsDisposed, Is.True);
                    throw secondary;
                }
            };
            await RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try { await SearchAdjustmentControlSuite.RunPopupOwnershipControlAsync(scene, hooks); }
                catch (Exception error) { observed = error; }
                // Every actual source/work oracle precedes independent rescue.
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(beforeDisposeEvents, Is.EqualTo(1));
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
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey],
                    Is.EqualTo(new[] { typeof(IOException).FullName }));
            },
            () => probe?.Lifetime.RescueAsync() ?? Task.CompletedTask,
            () => { probe?.HostSource.Dispose(); return Task.CompletedTask; },
            () => { probe?.ViewModel.Dispose(); return Task.CompletedTask; });
        }));

    private static bool IsDisposed(SettingsViewModel vm) =>
        (bool)typeof(SettingsViewModel).GetField("_isDisposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
}
