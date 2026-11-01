using System.Reflection;
using NUnit.Framework;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class LanguageWindowOwnershipCases
{
    public static IEnumerable<TestCaseData> OwnershipFailureRows()
    {
        yield return new TestCaseData(1).SetName("LanguageWindow_FirstPopupAndActualWindowRetirementFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/language-window/first-popup-and-actual-window-retirement-failure");
        yield return new TestCaseData(2).SetName("LanguageWindow_SecondPopupAndActualWindowRetirementFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/language-window/second-popup-and-actual-window-retirement-failure");
    }

    [TestCaseSource(nameof(OwnershipFailureRows))]
    public Task OriginalFailureJoinsPopupBeforeOriginalWindowCloseAndActualSourceRetirement(int openOrdinal) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(async () =>
        {
            var primary = new InvalidOperationException("language-window-owned-open-failure");
            var secondary = new IOException("language-window-cleanup-after-actual-retirement");
            LanguageWindowFixtureProbe? probe = null;
            var lifetimes = new List<NativePopupLifetimeOwner>();
            var retiringEvents = 0;
            var retiredEvents = 0;
            var hooks = new LanguageWindowFixtureHooks
            {
                PopupOpened = value =>
                {
                    if (value.OpenOrdinal != openOrdinal) return;
                    probe = value;
                    Assert.That(value.WindowLifetime.Source, Is.Not.Null);
                    Assert.That(value.WindowLifetime.Source!.IsDisposed, Is.False);
                    Assert.That(value.WindowLifetime.SourceInitializedEvents, Is.EqualTo(1));
                    Assert.That(value.PopupLifetime.Source, Is.Not.Null);
                    Assert.That(value.PopupLifetime.Source!.IsDisposed, Is.False);
                    throw primary;
                },
                WindowRetiring = owner =>
                {
                    retiringEvents++;
                    Assert.That(probe, Is.Not.Null);
                    Assert.That(owner.WindowLifetime, Is.SameAs(probe!.WindowLifetime));
                    Assert.That(owner.WindowLifetime!.Source!.IsDisposed, Is.False);
                    Assert.That(owner.WindowLifetime.ClosedEvents, Is.EqualTo(0));
                    Assert.That(owner.WindowLifetime.SourceDisposedEvents, Is.EqualTo(0));
                    lifetimes.AddRange(owner.Lifetimes);
                    Assert.That(lifetimes.Count, Is.EqualTo(openOrdinal));
                    foreach (var lifetime in lifetimes) AssertPopupTerminal(lifetime);
                    Assert.That(owner.Tags!.IsPopupOpen, Is.False);
                    Assert.That(owner.Tags.Material.Handle, Is.EqualTo(IntPtr.Zero));
                    Assert.That((bool)owner.Tags.GetType().GetField("_ownsPopupInput",
                        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner.Tags)!, Is.False);
                },
                WindowRetired = owner =>
                {
                    retiredEvents++;
                    AssertWindowTerminal(owner.WindowLifetime!);
                    throw secondary;
                }
            };
            await RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try { await VoiceLanguageTagsSuite.RunLanguageWindowOwnershipControlAsync(hooks); }
                catch (Exception error) { observed = error; }
                // Real native termination is required before independent rescue.
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(retiringEvents, Is.EqualTo(1));
                Assert.That(retiredEvents, Is.EqualTo(1));
                Assert.That(probe, Is.Not.Null);
                AssertWindowTerminal(probe!.WindowLifetime);
                foreach (var lifetime in lifetimes) AssertPopupTerminal(lifetime);
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey],
                    Is.EqualTo(new[] { typeof(IOException).FullName }));
            },
            async () =>
            {
                foreach (var lifetime in lifetimes) await lifetime.RescueAsync();
            },
            async () =>
            {
                if (probe is null) return;
                await probe.WindowLifetime.CloseAndJoinAsync();
                probe.WindowLifetime.Detach();
            });
        }));

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

    private static void AssertWindowTerminal(NativeWindowLifetimeOwner lifetime)
    {
        Assert.That(lifetime.Source, Is.Not.Null);
        Assert.That(lifetime.Source!.IsDisposed, Is.True);
        Assert.That(lifetime.SourceInitializedEvents, Is.EqualTo(1));
        Assert.That(lifetime.ClosedEvents, Is.EqualTo(1));
        Assert.That(lifetime.SourceDisposedEvents, Is.EqualTo(1));
        Assert.That(lifetime.ClosedTask.IsCompletedSuccessfully, Is.True);
        Assert.That(lifetime.SourceDisposedTask.IsCompletedSuccessfully, Is.True);
        Assert.That(lifetime.ClosureVerified, Is.True);
        Assert.That(lifetime.HandlersAttached, Is.False);
        Assert.That(lifetime.InClosedCallback, Is.False);
        Assert.That(lifetime.InSourceDisposedCallback, Is.False);
    }
}
