using System.Reflection;
using System.Windows.Interop;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.View;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class LanguagePopupOwnershipCases
{
    public static IEnumerable<TestCaseData> OwnershipFailureRows()
    {
        yield return new TestCaseData(0).SetName("LanguagePopup_ReturnedHostBeforeRootVisualAndCleanupFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/language/returned-host-before-rootvisual-and-cleanup-failure");
        yield return new TestCaseData(1).SetName("LanguagePopup_FirstInputOpenAndHostCleanupFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/language/first-input-open-and-host-cleanup-failure");
        yield return new TestCaseData(2).SetName("LanguagePopup_SecondInputOpenAndHostCleanupFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/language/second-input-open-and-host-cleanup-failure");
    }

    [TestCaseSource(nameof(OwnershipFailureRows))]
    public Task OriginalFailureRetainsHostAndActualClosedOwnership(int boundary) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(async () =>
        {
            var primary = new InvalidOperationException("language-popup-owned-boundary-failure");
            var secondary = new IOException("language-popup-host-cleanup-after-dispose");
            HwndSource? host = null;
            var probes = new List<LanguagePopupFixtureProbe>();
            var retiringEvents = 0;
            var hooks = new LanguagePopupFixtureHooks
            {
                SourceAcquired = source =>
                {
                    host = source;
                    Assert.That(source.IsDisposed, Is.False);
                    Assert.That(source.RootVisual, Is.Null);
                    if (boundary == 0) throw primary;
                },
                PopupOpened = probe =>
                {
                    probes.Add(probe);
                    Assert.That(probe.HostSource, Is.SameAs(host));
                    Assert.That(probe.HostSource.IsDisposed, Is.False);
                    Assert.That(probe.Lifetime.Source, Is.Not.Null);
                    Assert.That(probe.Lifetime.Source!.IsDisposed, Is.False);
                    Assert.That(probe.Lifetime.ClosedTask.IsCompleted, Is.False);
                    Assert.That(probe.OpenOrdinal, Is.EqualTo(probes.Count));
                    if (probe.OpenOrdinal > 1) AssertTerminal(probes[^2].Lifetime);
                    if (probe.OpenOrdinal == boundary) throw primary;
                },
                SourceRetiring = owner =>
                {
                    retiringEvents++;
                    Assert.That(owner.Source, Is.SameAs(host));
                    Assert.That(owner.Source!.IsDisposed, Is.False);
                    Assert.That(owner.Lifetimes.Count, Is.EqualTo(boundary));
                    foreach (var lifetime in owner.Lifetimes) AssertTerminal(lifetime);
                    if (boundary > 0)
                    {
                        Assert.That(owner.Tags, Is.Not.Null);
                        Assert.That(owner.Tags!.IsPopupOpen, Is.False);
                        Assert.That(owner.Tags.Material.Handle, Is.EqualTo(IntPtr.Zero));
                        Assert.That((bool)typeof(VoiceLanguageTags).GetField("_ownsPopupInput",
                            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner.Tags)!, Is.False);
                        Assert.That(typeof(VoiceLanguageTags).GetField("_owner",
                            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner.Tags), Is.Null);
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
                try { await VoiceLanguageTagsSuite.RunLanguageOwnershipControlAsync(boundary, hooks); }
                catch (Exception error) { observed = error; }
                // All terminal oracles precede independent rescue on this root.
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(retiringEvents, Is.EqualTo(1));
                Assert.That(host, Is.Not.Null);
                Assert.That(host!.IsDisposed, Is.True);
                Assert.That(probes.Count, Is.EqualTo(boundary));
                foreach (var probe in probes) AssertTerminal(probe.Lifetime);
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey],
                    Is.EqualTo(new[] { typeof(IOException).FullName }));
            },
            async () =>
            {
                foreach (var probe in probes) await probe.Lifetime.RescueAsync();
            },
            () => { host?.Dispose(); return Task.CompletedTask; });
        }));

    private static void AssertTerminal(NativePopupLifetimeOwner lifetime)
    {
        Assert.That(lifetime.ClosedTask.IsCompletedSuccessfully, Is.True);
        Assert.That(lifetime.Source, Is.Not.Null);
        Assert.That(lifetime.Source!.IsDisposed, Is.True);
        Assert.That(lifetime.OpenedEvents, Is.EqualTo(1));
        Assert.That(lifetime.ClosedEvents, Is.EqualTo(1));
        Assert.That(lifetime.ClosureVerified, Is.True);
        Assert.That(lifetime.InClosedCallback, Is.False);
        Assert.That(lifetime.HandlersAttached, Is.False);
    }
}
