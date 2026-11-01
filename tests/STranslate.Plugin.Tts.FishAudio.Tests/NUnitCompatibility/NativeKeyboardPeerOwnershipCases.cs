using System.Windows.Interop;
using NUnit.Framework;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class NativeKeyboardPeerOwnershipCases
{
    public static IEnumerable<TestCaseData> ReturnedSourceFailureRows()
    {
        yield return new TestCaseData(true).SetName("NativeKeyboardPeer_RootVisualCleanupFailureStillDisposesSource")
            .SetProperty("FishAudioCaseId", "infra.native-popups/keyboard-peer/rootvisual-cleanup-failure-still-disposes-source");
        yield return new TestCaseData(false).SetName("NativeKeyboardPeer_SourceDisposeFailurePreservesOriginal")
            .SetProperty("FishAudioCaseId", "infra.native-popups/keyboard-peer/source-dispose-failure-preserves-original");
    }

    [TestCaseSource(nameof(ReturnedSourceFailureRows))]
    public Task ReturnedActualSourceKeepsPrimaryAcrossIndependentRootVisualAndDisposeCleanup(bool failClear) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(async () =>
        {
            var primary = new InvalidOperationException("native-key-peer-after-source-and-rootvisual-return");
            var secondary = new IOException("native-key-peer-after-actual-cleanup");
            HwndSource? source = null;
            var events = new List<string>();
            var cleared = false;
            var hooks = new NativeKeyboardPeerFixtureHooks
            {
                SourceReturned = value =>
                {
                    source = value;
                    events.Add("returned");
                    Assert.That(value.IsDisposed, Is.False);
                    Assert.That(value.RootVisual, Is.Not.Null);
                    // The original native keyboard block has not started.
                    throw primary;
                },
                RootVisualCleared = value =>
                {
                    events.Add("rootvisual-cleared");
                    Assert.That(value, Is.SameAs(source));
                    Assert.That(value.IsDisposed, Is.False);
                    Assert.That(value.RootVisual, Is.Null);
                    cleared = true;
                    if (failClear) throw secondary;
                },
                SourceDisposed = value =>
                {
                    events.Add("source-disposed");
                    Assert.That(value, Is.SameAs(source));
                    Assert.That(value.IsDisposed, Is.True);
                    Assert.That(cleared, Is.True);
                    if (!failClear) throw secondary;
                }
            };
            await RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try { await NativeKeyboardStateSuite.RunNativePeerOwnershipControlAsync(hooks); }
                catch (Exception error) { observed = error; }
                // All actual-resource oracles precede independent rescue.
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(source, Is.Not.Null);
                Assert.That(source!.IsDisposed, Is.True);
                Assert.That(cleared, Is.True);
                Assert.That(events, Is.EqualTo(new[] { "returned", "rootvisual-cleared", "source-disposed" }));
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey],
                    Is.EqualTo(new[] { typeof(IOException).FullName }));
            },
            () =>
            {
                if (source is { IsDisposed: false }) source.RootVisual = null;
                return Task.CompletedTask;
            },
            () => { source?.Dispose(); return Task.CompletedTask; });
        }));
}
