using System.Reflection;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
public sealed class LanguageViewModelOwnershipCases
{
    public static IEnumerable<TestCaseData> OwnershipFailureRows()
    {
        yield return new TestCaseData(0).SetName("LanguageViewModel_CompiledCardPrimaryAndDisposeFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/language-vm/compiled-card-primary-and-dispose-failure");
        yield return new TestCaseData(1).SetName("LanguageViewModel_OverflowRowPrimaryAndDisposeFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/language-vm/overflow-row-primary-and-dispose-failure");
    }

    [TestCaseSource(nameof(OwnershipFailureRows))]
    public Task ReturnedViewModelRetainsOriginalFailureWhenDisposeAlsoFails(int scene) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(async () =>
        {
            var primary = new InvalidOperationException("language-vm-before-view-failure");
            var secondary = new IOException("language-vm-cleanup-after-dispose");
            SettingsViewModel? vm = null;
            var disposedEvents = 0;
            var hooks = new LanguageViewModelFixtureHooks
            {
                ViewModelAcquired = value =>
                {
                    vm = value;
                    Assert.That(IsDisposed(value), Is.False);
                    Assert.That(value.LastResolvedCoverCacheTask, Is.Null);
                    throw primary;
                },
                ViewModelDisposed = value =>
                {
                    disposedEvents++;
                    Assert.That(value, Is.SameAs(vm));
                    Assert.That(IsDisposed(value), Is.True);
                    throw secondary;
                }
            };
            await RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try { await VoiceLanguageTagsSuite.RunLanguageViewModelOwnershipControlAsync(scene, hooks); }
                catch (Exception error) { observed = error; }
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(vm, Is.Not.Null);
                Assert.That(IsDisposed(vm!), Is.True);
                Assert.That(disposedEvents, Is.EqualTo(1));
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey],
                    Is.EqualTo(new[] { typeof(IOException).FullName }));
            },
            () => { vm?.Dispose(); return Task.CompletedTask; });
        }));

    private static bool IsDisposed(SettingsViewModel vm) =>
        (bool)typeof(SettingsViewModel).GetField("_isDisposed",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
}
