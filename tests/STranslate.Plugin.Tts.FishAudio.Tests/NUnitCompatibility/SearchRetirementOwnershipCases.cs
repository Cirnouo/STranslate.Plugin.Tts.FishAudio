using System.Reflection;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class SearchRetirementOwnershipCases
{
    public static IEnumerable<TestCaseData> FailureRows()
    {
        yield return new TestCaseData(0).SetName("SearchRetirement_OverlayHandoffBodyAndHostCleanupFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/search/overlay-handoff-body-and-host-cleanup-failure");
        yield return new TestCaseData(1).SetName("SearchRetirement_WindowReopenBodyAndHostCleanupFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/search/window-reopen-body-and-host-cleanup-failure");
        yield return new TestCaseData(2).SetName("SearchRetirement_ShortProofBodyAndHostCleanupFailure")
            .SetProperty("FishAudioCaseId", "infra.native-popups/search/short-proof-body-and-host-cleanup-failure");
    }

    [TestCaseSource(nameof(FailureRows))]
    public Task ActualLifetimesAndProofTerminateBeforeIndependentRescue(int scene) =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(async () =>
        {
            var primary = new InvalidOperationException("search-retirement-original-body-failure");
            var secondary = new IOException("search-retirement-after-actual-host-retirement");
            SearchRetirementPopupProbe? probe = null;
            var beforeHost = 0;
            var afterHost = 0;
            var proof = new OrdinaryPopupProofDirectory();
            var hooks = new SearchRetirementFixtureHooks
            {
                Opened = value =>
                {
                    if ((scene == 0 && value.Name == "overlay/search-1") ||
                        (scene == 1 && value.Name == "window/search-2")) FailOriginalBody(value);
                },
                Checkpoint = (name, value) =>
                {
                    if (scene != 2 || name != "short/proof-above") return;
                    Assert.That(value.Owner.ProofDirectory, Is.EqualTo(proof.DirectoryPath));
                    Assert.That(Directory.Exists(proof.DirectoryPath), Is.True);
                    Assert.That(File.Exists(Path.Combine(proof.DirectoryPath!, "proof-assembly.json")), Is.True);
                    Assert.That(Directory.GetFiles(proof.DirectoryPath!, "short-viewport-above-native*.png"), Has.Length.EqualTo(1),
                        "The original unconditional proof write completed in the actual owned directory before failure.");
                    FailOriginalBody(value);
                },
                HostRetiring = owner =>
                {
                    beforeHost++;
                    Assert.That(owner.HostSource, Is.Not.Null);
                    Assert.That(owner.HostSource!.IsDisposed, Is.False);
                    Assert.That(IsDisposed(owner.ViewModel!), Is.False);
                    AssertPopupTermination(owner);
                    if (owner.WindowLifetime is { } window)
                    {
                        Assert.That(window.SourceInitializedEvents, Is.EqualTo(1));
                        Assert.That(window.ClosedTask.IsCompleted, Is.False);
                        Assert.That(window.SourceDisposedTask.IsCompleted, Is.False);
                    }
                },
                HostRetired = owner =>
                {
                    afterHost++;
                    Assert.That(owner.HostSource!.IsDisposed, Is.True);
                    if (owner.WindowLifetime is { } window) AssertWindowTermination(window);
                    throw secondary;
                },
            };

            void FailOriginalBody(SearchRetirementPopupProbe value)
            {
                probe = value;
                Assert.That(value.Owner.HostSource, Is.Not.Null);
                Assert.That(value.Owner.HostSource!.IsDisposed, Is.False);
                Assert.That(value.Lifetime.Source, Is.Not.Null);
                Assert.That(value.Lifetime.Source!.IsDisposed, Is.False);
                Assert.That(value.Lifetime.ClosedTask.IsCompleted, Is.False);
                Assert.That(IsDisposed(value.Owner.ViewModel!), Is.False);
                throw primary;
            }

            await RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try
                {
                    if (scene == 2)
                        await proof.RunAsync(directory => SearchAdjustmentControlSuite.RunRetirementOwnershipControlAsync(scene, hooks, directory));
                    else await SearchAdjustmentControlSuite.RunRetirementOwnershipControlAsync(scene, hooks);
                }
                catch (Exception error) { observed = error; }
                // These are the original captured native owners. Rescue has not run.
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(probe, Is.Not.Null);
                var owner = probe!.Owner;
                Assert.That(owner.Popups, Has.Count.EqualTo(scene == 2 ? 1 : 2));
                Assert.That(beforeHost, Is.EqualTo(1));
                Assert.That(afterHost, Is.EqualTo(1));
                AssertPopupTermination(owner);
                Assert.That(owner.HostSource!.IsDisposed, Is.True);
                Assert.That(IsDisposed(owner.ViewModel!), Is.True);
                if (owner.WindowLifetime is { } window) AssertWindowTermination(window);
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey],
                    Is.EqualTo(new[] { typeof(IOException).FullName }));
                if (scene == 2)
                {
                    Assert.That(proof.CleanupComplete, Is.True);
                    Assert.That(proof.CleanupFailure, Is.Null);
                    Assert.That(Directory.Exists(proof.DirectoryPath), Is.False);
                }
            },
            () => probe?.Owner.RescueAsync() ?? Task.CompletedTask,
            () => scene == 2 ? proof.DeleteOwnedDirectoryAsync() : Task.CompletedTask);
        }));

    private static void AssertPopupTermination(SearchRetirementFixtureOwner owner)
    {
        foreach (var popup in owner.Popups)
        {
            Assert.That(popup.Lifetime.ClosedTask.IsCompletedSuccessfully, Is.True);
            Assert.That(popup.Lifetime.Source, Is.Not.Null);
            Assert.That(popup.Lifetime.Source!.IsDisposed, Is.True);
            Assert.That(popup.Lifetime.OpenedEvents, Is.EqualTo(1));
            Assert.That(popup.Lifetime.ClosedEvents, Is.EqualTo(1));
            Assert.That(popup.Lifetime.ClosureVerified, Is.True);
            Assert.That(popup.Lifetime.InClosedCallback, Is.False);
            Assert.That(popup.Lifetime.HandlersAttached, Is.False);
            if (popup.Control is SearchAdjustmentControl search)
            {
                Assert.That((bool)typeof(SearchAdjustmentControl).GetField("_ownsInput", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(search)!, Is.False);
                Assert.That(search.Material.Handle, Is.EqualTo(IntPtr.Zero));
            }
            else if (popup.Control is VoiceLanguageTags tags)
            {
                Assert.That((bool)typeof(VoiceLanguageTags).GetField("_ownsPopupInput", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tags)!, Is.False);
                Assert.That(tags.Material.Handle, Is.EqualTo(IntPtr.Zero));
            }
        }
    }

    private static void AssertWindowTermination(NativeWindowLifetimeOwner window)
    {
        Assert.That(window.SourceInitializedEvents, Is.EqualTo(1));
        Assert.That(window.ClosedTask.IsCompletedSuccessfully, Is.True);
        Assert.That(window.SourceDisposedTask.IsCompletedSuccessfully, Is.True);
        Assert.That(window.ClosedEvents, Is.EqualTo(1));
        Assert.That(window.SourceDisposedEvents, Is.EqualTo(1));
        Assert.That(window.Source!.IsDisposed, Is.True);
        Assert.That(window.InClosedCallback, Is.False);
        Assert.That(window.InSourceDisposedCallback, Is.False);
        Assert.That(window.ClosureVerified, Is.True);
        Assert.That(window.HandlersAttached, Is.False);
    }

    private static bool IsDisposed(SettingsViewModel vm) =>
        (bool)typeof(SettingsViewModel).GetField("_isDisposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
}
