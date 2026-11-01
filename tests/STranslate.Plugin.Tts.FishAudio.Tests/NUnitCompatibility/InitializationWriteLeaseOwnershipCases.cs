using System.Runtime.ExceptionServices;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static TestAssertions;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
public sealed class InitializationWriteLeaseOwnershipCases
{
    private const string ValidVoiceId = "0123456789abcdef0123456789abcdef";

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/write-lease-body-failure-retires-both-view-models")]
    public void BodyFailureRetiresBothActualWriteLeaseViewModels()
    {
        var settings = new Settings { ApiKey = AppliedKey };
        var context = CreateContext(settings: settings);
        var contextProxy = (ContextProxy)(object)context;
        var marker = new InvalidOperationException("initialization-write-lease-owned-body-failure");
        Settings? firstSettings = null;
        Settings? secondSettings = null;
        SettingsWriteLease firstWriteLease = default;
        SettingsWriteLease secondWriteLease = default;
        SettingsViewModel? oldViewModel = null;
        SettingsViewModel? newViewModel = null;
        ExceptionDispatchInfo? testFailure = null;
        ExceptionDispatchInfo? rescueFailure = null;
        try
        {
            firstSettings = SettingsStore.Load(context,
                FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1), out firstWriteLease);
            oldViewModel = new SettingsViewModel(context, firstSettings,
                clearCoverImageCacheAsync: null, clearCoverImageCacheTimeout: null,
                settingsWriteLease: firstWriteLease);
            SettingsStore.Retire(firstSettings, firstWriteLease);
            secondSettings = SettingsStore.Load(context,
                FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1), out secondWriteLease);
            newViewModel = new SettingsViewModel(context, secondSettings,
                clearCoverImageCacheAsync: null, clearCoverImageCacheTimeout: null,
                settingsWriteLease: secondWriteLease);

            Assert.Multiple(() =>
            {
                Assert.That(oldViewModel.CopyVoiceIdCommand.CanExecute(ValidVoiceId), Is.True);
                Assert.That(newViewModel.CopyVoiceIdCommand.CanExecute(ValidVoiceId), Is.True);
                Assert.That(contextProxy.LoadCount, Is.EqualTo(2));
                Assert.That(contextProxy.SaveCount, Is.Zero);
            });

            ExceptionDispatchInfo? primaryFailure = null;
            try { throw marker; }
            catch (InvalidOperationException error) { primaryFailure = ExceptionDispatchInfo.Capture(error); }
            Exception? observed = null;
            try
            {
                InitializationLifecycleSuite.FinishWriteLeaseOwners(
                    firstSettings, firstWriteLease, oldViewModel,
                    secondSettings, secondWriteLease, newViewModel, primaryFailure);
            }
            catch (InvalidOperationException error) { observed = error; }

            // NotImplementedException remains the direct RED failure. All real
            // ownership observations happen before the independent rescue.
            Assert.That(observed, Is.SameAs(marker), "The same primary body failure must survive owner cleanup.");
            Assert.Multiple(() =>
            {
                Assert.That(oldViewModel.CopyVoiceIdCommand.CanExecute(ValidVoiceId), Is.False,
                    "Cleanup must retire the actual old VM, not only its already-retired write lease.");
                Assert.That(newViewModel.CopyVoiceIdCommand.CanExecute(ValidVoiceId), Is.False,
                    "Cleanup must retire the actual replacement VM.");
                Assert.That(contextProxy.SaveCount, Is.Zero, "Retirement must not save either owner.");
            });
            AssertLeaseRetired(firstSettings, firstWriteLease);
            AssertLeaseRetired(secondSettings, secondWriteLease);
        }
        catch (Exception error) { testFailure = ExceptionDispatchInfo.Capture(error); }
        finally
        {
            RescueOwners(firstSettings, firstWriteLease, oldViewModel,
                secondSettings, secondWriteLease, newViewModel, ref rescueFailure);
        }
        testFailure?.Throw();
        rescueFailure?.Throw();
    }

    [Test]
    [Property("FishAudioCaseId", "infra.initialization/replacement-load-failure-retires-created-view-model")]
    public void RealReplacementLoadFailureRetiresOnlyTheCreatedViewModel()
    {
        var settings = new Settings { ApiKey = AppliedKey };
        var context = CreateContext(settings: settings);
        var contextProxy = (ContextProxy)(object)context;
        var marker = new InvalidOperationException("initialization-write-lease-replacement-load-failure");
        Settings? firstSettings = null;
        Settings? secondSettings = null;
        SettingsWriteLease firstWriteLease = default;
        SettingsWriteLease secondWriteLease = default;
        SettingsViewModel? oldViewModel = null;
        SettingsViewModel? newViewModel = null;
        ExceptionDispatchInfo? testFailure = null;
        ExceptionDispatchInfo? rescueFailure = null;
        try
        {
            firstSettings = SettingsStore.Load(context,
                FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1), out firstWriteLease);
            oldViewModel = new SettingsViewModel(context, firstSettings,
                clearCoverImageCacheAsync: null, clearCoverImageCacheTimeout: null,
                settingsWriteLease: firstWriteLease);
            SettingsStore.Retire(firstSettings, firstWriteLease);
            contextProxy.OnLoad = () => throw marker;
            ExceptionDispatchInfo? primaryFailure = null;
            try
            {
                secondSettings = SettingsStore.Load(context,
                    FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1), out secondWriteLease);
                newViewModel = new SettingsViewModel(context, secondSettings,
                    clearCoverImageCacheAsync: null, clearCoverImageCacheTimeout: null,
                    settingsWriteLease: secondWriteLease);
            }
            catch (InvalidOperationException error) { primaryFailure = ExceptionDispatchInfo.Capture(error); }

            Assert.Multiple(() =>
            {
                Assert.That(primaryFailure?.SourceException, Is.SameAs(marker),
                    "The real second host load must produce the exact marker.");
                Assert.That(contextProxy.LoadCount, Is.EqualTo(2));
                Assert.That(secondSettings is null, Is.True, "A failed load does not invent replacement settings.");
                Assert.That(newViewModel is null, Is.True, "A failed load never constructs a replacement VM.");
                Assert.That(oldViewModel.CopyVoiceIdCommand.CanExecute(ValidVoiceId), Is.True,
                    "The already-created VM still requires cleanup after replacement load fails.");
            });

            Exception? observed = null;
            try
            {
                InitializationLifecycleSuite.FinishWriteLeaseOwners(
                    firstSettings, firstWriteLease, oldViewModel,
                    secondSettings, secondWriteLease, newViewModel, primaryFailure);
            }
            catch (InvalidOperationException error) { observed = error; }

            Assert.That(observed, Is.SameAs(marker), "Owner cleanup must preserve the real host-load failure.");
            Assert.That(oldViewModel.CopyVoiceIdCommand.CanExecute(ValidVoiceId), Is.False);
            Assert.That(contextProxy.SaveCount, Is.Zero, "Failure cleanup must not save the retired host settings.");
            AssertLeaseRetired(firstSettings, firstWriteLease);
        }
        catch (Exception error) { testFailure = ExceptionDispatchInfo.Capture(error); }
        finally
        {
            RescueOwners(firstSettings, firstWriteLease, oldViewModel,
                secondSettings, secondWriteLease, newViewModel, ref rescueFailure);
        }
        testFailure?.Throw();
        rescueFailure?.Throw();
    }

    private static void AssertLeaseRetired(Settings runtimeSettings, SettingsWriteLease writeLease)
    {
        var entered = false;
        var accepted = SettingsStore.TryUpdate(runtimeSettings, writeLease, () => entered = true);
        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.False, "The actual runtime settings/lease pair must reject later updates.");
            Assert.That(entered, Is.False, "A retired lease cannot enter its update callback.");
        });
    }

    private static void RescueOwners(
        Settings? firstSettings, SettingsWriteLease firstWriteLease, SettingsViewModel? oldViewModel,
        Settings? secondSettings, SettingsWriteLease secondWriteLease, SettingsViewModel? newViewModel,
        ref ExceptionDispatchInfo? failure)
    {
        // On stub RED, retire each acquired runtime lease and each live VM.
        // The valid-ID command predicate observes actual VM retirement, so a
        // helper that already disposed an owner never receives a second Dispose.
        try { if (secondSettings is not null) SettingsStore.Retire(secondSettings, secondWriteLease); }
        catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
        try
        {
            if (newViewModel is not null && newViewModel.CopyVoiceIdCommand.CanExecute(ValidVoiceId))
                newViewModel.Dispose();
        }
        catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
        try { if (firstSettings is not null) SettingsStore.Retire(firstSettings, firstWriteLease); }
        catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
        try
        {
            if (oldViewModel is not null && oldViewModel.CopyVoiceIdCommand.CanExecute(ValidVoiceId))
                oldViewModel.Dispose();
        }
        catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
    }
}
