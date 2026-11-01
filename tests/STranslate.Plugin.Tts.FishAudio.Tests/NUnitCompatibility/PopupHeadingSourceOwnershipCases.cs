using NUnit.Framework;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class PopupHeadingSourceOwnershipCases
{
    [Test]
    [Property("FishAudioCaseId", "infra.native-popups/heading/source-replacement-body-and-second-cleanup-failure")]
    public Task NativeTextRestorationAndBothSourcesRetireAfterBodyAndSecondSourceCleanupFailure() =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(async () =>
        {
            var primary = new InvalidOperationException("heading-native-source-body-failure");
            var secondary = new IOException("heading-second-source-cleanup-after-dispose");
            PopupHeadingSourceFixtureProbe? probe = null;
            var beforeSecondDisposeEvents = 0;
            var hooks = new PopupHeadingSourceFixtureHooks
            {
                NativeHeadingReady = value =>
                {
                    probe = value;
                    Assert.That(value.FirstSource.IsDisposed, Is.False);
                    Assert.That(value.SecondSource.IsDisposed, Is.False);
                    Assert.That(value.NativeApi.Read(value.FirstSource.Handle), Is.EqualTo("Heading"));
                    throw primary;
                },
                BeforeSecondDispose = source =>
                {
                    beforeSecondDisposeEvents++;
                    Assert.That(probe, Is.Not.Null);
                    Assert.That(source, Is.SameAs(probe!.SecondSource));
                    Assert.That(source.IsDisposed, Is.False);
                    Assert.That(probe.FirstSource.IsDisposed, Is.False);
                    Assert.That(probe.NativeApi.Read(probe.FirstSource.Handle), Is.EqualTo(probe.PriorText),
                        "The actual native heading must restore while both sources are alive, before either outer disposal can compensate.");
                },
                SecondDisposed = source =>
                {
                    Assert.That(source.IsDisposed, Is.True);
                    throw secondary;
                }
            };
            await RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try { await PopupHeadingSuite.RunSourceReplacementOwnershipControlAsync(hooks); }
                catch (Exception error) { observed = error; }
                // Native text and actual source terminal oracles precede rescue.
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(beforeSecondDisposeEvents, Is.EqualTo(1));
                Assert.That(probe, Is.Not.Null);
                Assert.That(probe!.FirstSource.IsDisposed, Is.True,
                    "A preceding second-source disposal failure cannot skip the first source.");
                Assert.That(probe.SecondSource.IsDisposed, Is.True);
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey],
                    Is.EqualTo(new[] { typeof(IOException).FullName }));
            },
            () => { probe?.SecondSource.Dispose(); return Task.CompletedTask; },
            () => { probe?.FirstSource.Dispose(); return Task.CompletedTask; });
        }));
}
