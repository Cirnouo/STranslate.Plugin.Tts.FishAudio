using System.Reflection;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SearchViewportOwnershipCases
{
    public static IEnumerable<TestCaseData> OwnershipFailureRows()
    {
        yield return new TestCaseData(false).SetName("SearchViewport_FirstSearchPopupAndHostCleanupFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/search-viewport/first-search-popup-and-host-cleanup-failure");
        yield return new TestCaseData(true).SetName("SearchViewport_FirstLanguagePopupAndHostCleanupFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/search-viewport/first-language-popup-and-host-cleanup-failure");
    }

    [TestCaseSource(nameof(OwnershipFailureRows))]
    public Task OriginalFailureRetainsSameSourceAcrossActualPopupAndRefusalLifetimes(bool language) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(async () =>
        {
            var primary = new InvalidOperationException("search-viewport-owned-open-failure");
            var secondary = new IOException("search-viewport-host-cleanup-after-dispose");
            SearchViewportFixtureProbe? probe = null;
            var lifetimes = new List<NativePopupLifetimeOwner>();
            var retiringEvents = 0;
            var hooks = new SearchViewportFixtureHooks
            {
                PopupOpened = value =>
                {
                    if ((value.Control is VoiceLanguageTags) != language) return;
                    probe = value;
                    Assert.That(value.HostSource.IsDisposed, Is.False);
                    Assert.That(IsDisposed(value.ViewModel), Is.False);
                    Assert.That(value.Lifetime.Source, Is.Not.Null);
                    Assert.That(value.Lifetime.Source!.IsDisposed, Is.False);
                    Assert.That(value.Lifetime.ClosedTask.IsCompleted, Is.False);
                    Assert.That(value.AttemptOrdinal, Is.EqualTo(language ? 12 : 1));
                    throw primary;
                },
                SourceRetiring = owner =>
                {
                    retiringEvents++;
                    Assert.That(probe, Is.Not.Null);
                    Assert.That(owner.Source, Is.SameAs(probe!.HostSource));
                    Assert.That(owner.Source!.IsDisposed, Is.False);
                    Assert.That(owner.ViewModel, Is.SameAs(probe.ViewModel));
                    Assert.That(IsDisposed(owner.ViewModel!), Is.False);
                    Assert.That(owner.InitialApplyTask, Is.Not.Null);
                    Assert.That(owner.InitialApplyTask!.IsCompletedSuccessfully, Is.True);
                    lifetimes.AddRange(owner.Lifetimes);
                    Assert.That(lifetimes.Count, Is.EqualTo(language ? 12 : 1));
                    foreach (var lifetime in lifetimes) AssertTerminal(lifetime);
                    if (probe.Control is VoiceLanguageTags tags)
                    {
                        Assert.That(tags.IsPopupOpen, Is.False);
                        Assert.That(tags.Material.Handle, Is.EqualTo(IntPtr.Zero));
                        Assert.That((bool)tags.GetType().GetField("_ownsPopupInput",
                            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tags)!, Is.False);
                    }
                    else
                    {
                        var search = (SearchAdjustmentControl)probe.Control;
                        Assert.That(search.Material.Handle, Is.EqualTo(IntPtr.Zero));
                        Assert.That((bool)search.GetType().GetField("_ownsInput",
                            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(search)!, Is.False);
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
                try { await SearchAdjustmentControlSuite.RunSearchViewportOwnershipControlAsync(hooks); }
                catch (Exception error) { observed = error; }
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(retiringEvents, Is.EqualTo(1));
                Assert.That(probe, Is.Not.Null);
                Assert.That(probe!.HostSource.IsDisposed, Is.True);
                Assert.That(IsDisposed(probe.ViewModel), Is.True);
                foreach (var lifetime in lifetimes) AssertTerminal(lifetime);
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey],
                    Is.EqualTo(new[] { typeof(IOException).FullName }));
            },
            async () =>
            {
                foreach (var lifetime in lifetimes) await lifetime.RescueAsync();
            },
            () => { probe?.HostSource.Dispose(); return Task.CompletedTask; },
            () => { probe?.ViewModel.Dispose(); return Task.CompletedTask; });
        }));

    private static void AssertTerminal(NativePopupLifetimeOwner lifetime)
    {
        Assert.That(lifetime.HandlersAttached, Is.False);
        Assert.That(lifetime.InClosedCallback, Is.False);
        if (lifetime.Source is null)
        {
            Assert.That(lifetime.OpenedEvents, Is.EqualTo(0));
            Assert.That(lifetime.ClosedEvents, Is.EqualTo(0));
            Assert.That(lifetime.ClosedTask.IsCompleted, Is.False);
            Assert.That(lifetime.ClosureVerified, Is.False);
        }
        else
        {
            Assert.That(lifetime.Source.IsDisposed, Is.True);
            Assert.That(lifetime.OpenedEvents, Is.EqualTo(1));
            Assert.That(lifetime.ClosedEvents, Is.EqualTo(1));
            Assert.That(lifetime.ClosedTask.IsCompletedSuccessfully, Is.True);
            Assert.That(lifetime.ClosureVerified, Is.True);
        }
    }

    private static bool IsDisposed(SettingsViewModel vm) =>
        (bool)typeof(SettingsViewModel).GetField("_isDisposed",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
}
