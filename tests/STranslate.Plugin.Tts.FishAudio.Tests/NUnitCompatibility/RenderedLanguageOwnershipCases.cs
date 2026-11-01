using System.Reflection;
using System.Windows.Interop;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class RenderedLanguageOwnershipCases
{
    public static IEnumerable<TestCaseData> OwnershipFailureRows()
    {
        yield return new TestCaseData(0).SetName("RenderedLanguage_ReturnedHostBeforeRootVisualAndCleanupFailures")
            .SetProperty("FishAudioCaseId", "infra.native-popups/language-rendered/returned-host-before-rootvisual-and-cleanup-failures");
        yield return new TestCaseData(1).SetName("RenderedLanguage_FirstPopupAndIndependentSourceVmCleanupFailures")
            .SetProperty("FishAudioCaseId", "infra.native-popups/language-rendered/first-popup-and-independent-source-vm-cleanup-failures");
        yield return new TestCaseData(2).SetName("RenderedLanguage_SecondPopupAndIndependentSourceVmCleanupFailures")
            .SetProperty("FishAudioCaseId", "infra.native-popups/language-rendered/second-popup-and-independent-source-vm-cleanup-failures");
    }

    [TestCaseSource(nameof(OwnershipFailureRows))]
    public Task OriginalFailureKeepsActualPopupAndIndependentHostVmRetirement(int boundary) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(async () =>
        {
            var primary = new InvalidOperationException("rendered-language-owned-boundary-failure");
            var sourceFailure = new IOException("rendered-language-host-cleanup-after-dispose");
            var vmFailure = new UnauthorizedAccessException("rendered-language-vm-cleanup-after-dispose");
            HwndSource? host = null;
            SettingsViewModel? vm = null;
            var probes = new List<RenderedLanguagePopupProbe>();
            var lifetimes = new List<NativePopupLifetimeOwner>();
            var retiringEvents = 0;
            var vmDisposedEvents = 0;
            var hooks = new RenderedLanguageFixtureHooks
            {
                SourceAcquired = (source, value) =>
                {
                    host = source; vm = value;
                    Assert.That(source.IsDisposed, Is.False);
                    Assert.That(source.RootVisual, Is.Null);
                    Assert.That(IsDisposed(value), Is.False);
                    Assert.That(value.LastResolvedCoverCacheTask, Is.Null);
                    if (boundary == 0) throw primary;
                },
                PopupOpened = probe =>
                {
                    probes.Add(probe);
                    Assert.That(probe.HostSource, Is.SameAs(host));
                    Assert.That(probe.ViewModel, Is.SameAs(vm));
                    Assert.That(probe.Lifetime.Source, Is.Not.Null);
                    Assert.That(probe.Lifetime.Source!.IsDisposed, Is.False);
                    Assert.That(probe.Lifetime.ClosedTask.IsCompleted, Is.False);
                    Assert.That(probe.OpenOrdinal, Is.EqualTo(probes.Count));
                    if (probe.OpenOrdinal == boundary) throw primary;
                },
                SourceRetiring = owner =>
                {
                    retiringEvents++;
                    Assert.That(owner.Source, Is.SameAs(host));
                    Assert.That(owner.Source!.IsDisposed, Is.False);
                    Assert.That(owner.ViewModel, Is.SameAs(vm));
                    Assert.That(IsDisposed(owner.ViewModel!), Is.False);
                    lifetimes.AddRange(owner.Lifetimes);
                    Assert.That(lifetimes.Count, Is.EqualTo(boundary == 0 ? 0 : 2));
                    for (var index = 0; index < lifetimes.Count; index++)
                        AssertTerminal(lifetimes[index], index < boundary);
                    foreach (var probe in probes)
                    {
                        Assert.That(probe.Tags.IsPopupOpen, Is.False);
                        Assert.That(probe.Tags.Material.Handle, Is.EqualTo(IntPtr.Zero));
                        Assert.That((bool)probe.Tags.GetType().GetField("_ownsPopupInput",
                            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(probe.Tags)!, Is.False);
                        Assert.That(probe.Tags.GetType().GetField("_owner",
                            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(probe.Tags), Is.Null);
                    }
                },
                SourceDisposed = source =>
                {
                    Assert.That(source.IsDisposed, Is.True);
                    Assert.That(IsDisposed(vm!), Is.False);
                    throw sourceFailure;
                },
                ViewModelDisposed = value =>
                {
                    vmDisposedEvents++;
                    Assert.That(IsDisposed(value), Is.True);
                    Assert.That(host!.IsDisposed, Is.True);
                    throw vmFailure;
                }
            };
            await RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try { await VoiceLanguageTagsSuite.RunRenderedLanguageOwnershipControlAsync(hooks); }
                catch (Exception error) { observed = error; }
                // Every terminal oracle precedes independent same-root rescue.
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(retiringEvents, Is.EqualTo(1));
                Assert.That(vmDisposedEvents, Is.EqualTo(1));
                Assert.That(probes.Count, Is.EqualTo(boundary));
                Assert.That(host, Is.Not.Null);
                Assert.That(host!.IsDisposed, Is.True);
                Assert.That(vm, Is.Not.Null);
                Assert.That(IsDisposed(vm!), Is.True);
                for (var index = 0; index < lifetimes.Count; index++)
                    AssertTerminal(lifetimes[index], index < boundary);
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey],
                    Is.EqualTo(new[] { typeof(IOException).FullName, typeof(UnauthorizedAccessException).FullName }));
            },
            async () =>
            {
                foreach (var lifetime in lifetimes) await lifetime.RescueAsync();
            },
            () => { host?.Dispose(); return Task.CompletedTask; },
            () => { vm?.Dispose(); return Task.CompletedTask; });
        }));

    private static void AssertTerminal(NativePopupLifetimeOwner lifetime, bool opened)
    {
        Assert.That(lifetime.HandlersAttached, Is.False);
        Assert.That(lifetime.InClosedCallback, Is.False);
        Assert.That(lifetime.OpenedEvents, Is.EqualTo(opened ? 1 : 0));
        Assert.That(lifetime.ClosedEvents, Is.EqualTo(opened ? 1 : 0));
        Assert.That(lifetime.ClosureVerified, Is.EqualTo(opened));
        Assert.That(lifetime.ClosedTask.IsCompletedSuccessfully, Is.EqualTo(opened));
        if (opened)
        {
            Assert.That(lifetime.Source, Is.Not.Null);
            Assert.That(lifetime.Source!.IsDisposed, Is.True);
        }
        else Assert.That(lifetime.Source, Is.Null);
    }

    private static bool IsDisposed(SettingsViewModel vm) =>
        (bool)typeof(SettingsViewModel).GetField("_isDisposed",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
}
