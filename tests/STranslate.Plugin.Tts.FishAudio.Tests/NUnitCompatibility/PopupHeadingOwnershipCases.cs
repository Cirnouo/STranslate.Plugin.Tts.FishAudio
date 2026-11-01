using System.Reflection;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class PopupHeadingOwnershipCases
{
    public static IEnumerable<TestCaseData> RenderedFailureRows()
    {
        yield return new TestCaseData(0).SetName("HeadingPopup_CompiledSearchBodyAndCleanupFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/heading/compiled-search-body-and-cleanup-failure");
        yield return new TestCaseData(1).SetName("HeadingPopup_CompiledLanguageBodyAndCleanupFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/heading/compiled-language-body-and-cleanup-failure");
        yield return new TestCaseData(2).SetName("HeadingPopup_OptionalMaterialBodyAndCleanupFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/heading/optional-material-body-and-cleanup-failure");
    }

    [TestCaseSource(nameof(RenderedFailureRows))]
    public Task ARenderedFailureJoinsTheActualPopupBeforeNativeOwnerAndVmRetirement(int scene) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(async () =>
        {
            var primary = new InvalidOperationException("heading-popup-body-failure");
            var secondary = new IOException("heading-host-cleanup-after-dispose");
            PopupHeadingFixtureProbe? probe = null;
            var beforeDisposeEvents = 0;
            var hooks = new PopupHeadingFixtureHooks
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
                    Assert.That(probe!.Lifetime.ClosedTask.IsCompletedSuccessfully, Is.True);
                    Assert.That(probe.Lifetime.Source!.IsDisposed, Is.True);
                    Assert.That(probe.Lifetime.ClosureVerified, Is.True);
                    Assert.That(probe.Lifetime.InClosedCallback, Is.False);
                    Assert.That(probe.Lifetime.HandlersAttached, Is.False);
                    if (probe.ViewModel is not null) Assert.That(IsDisposed(probe.ViewModel), Is.False);
                    if (probe.Control is SearchAdjustmentControl search)
                    {
                        Assert.That(OwnsInput(search, "_ownsInput"), Is.False);
                        Assert.That(search.Material.Handle, Is.EqualTo(IntPtr.Zero));
                    }
                    else if (probe.Control is VoiceLanguageTags tags)
                    {
                        Assert.That(OwnsInput(tags, "_ownsPopupInput"), Is.False);
                        Assert.That(tags.Material.Handle, Is.EqualTo(IntPtr.Zero));
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
                try { await PopupHeadingSuite.RunOwnershipControlAsync(scene, hooks); }
                catch (Exception error) { observed = error; }
                // These terminal oracles precede every independent rescue.
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
                if (probe.ViewModel is not null) Assert.That(IsDisposed(probe.ViewModel), Is.True);
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey],
                    Is.EqualTo(new[] { typeof(IOException).FullName }));
            },
            () => probe?.Lifetime.RescueAsync() ?? Task.CompletedTask,
            () => { probe?.HostSource.Dispose(); return Task.CompletedTask; },
            () => { probe?.ViewModel?.Dispose(); return Task.CompletedTask; });
        }));

    private static bool IsDisposed(SettingsViewModel vm) =>
        (bool)typeof(SettingsViewModel).GetField("_isDisposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;

    private static bool OwnsInput(object control, string field) =>
        (bool)control.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
}
