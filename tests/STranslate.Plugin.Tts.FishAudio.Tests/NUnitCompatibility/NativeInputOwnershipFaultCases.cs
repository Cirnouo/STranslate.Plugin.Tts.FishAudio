using NUnit.Framework;

namespace NUnitCompatibility;

// Inspect the actual terminal owner before independent rescue. Discovery is metadata only.
[TestFixture]
[NonParallelizable]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class NativeInputOwnershipFaultCases
{
    private const string VoiceId = "11111111111111111111111111111111";

    public static IEnumerable<TestCaseData> SearchRows()
    {
        yield return Row("search", false);
        yield return Row("search", true);
    }

    public static IEnumerable<TestCaseData> FavoriteRows()
    {
        yield return Row("favorites", false);
        yield return Row("favorites", true);
    }

    public static IEnumerable<TestCaseData> FieldRows()
    {
        foreach (var cleanupFault in new[] { false, true })
        {
            yield return new TestCaseData("PageInputBox", "PageInput", cleanupFault)
                .SetName("PageEntryOwnership_fields-search-" + (cleanupFault ? "cleanup-fault" : "body-fault"))
                .SetProperty("FishAudioCaseId", "infra.page-entry/fields-search-" + (cleanupFault ? "cleanup-fault" : "body-fault"));
            yield return new TestCaseData("FavoritePageInputBox", "FavoritePageInput", cleanupFault)
                .SetName("PageEntryOwnership_fields-favorites-" + (cleanupFault ? "cleanup-fault" : "body-fault"))
                .SetProperty("FishAudioCaseId", "infra.page-entry/fields-favorites-" + (cleanupFault ? "cleanup-fault" : "body-fault"));
        }
    }

    [TestCaseSource(nameof(SearchRows))]
    public Task SearchPageEntryFailureJoinsTheOriginalCommandBeforeRescue(bool cleanupFault)
        => StaTestHost.RunOnStaThreadAsync(() => CheckAsync("search", cleanupFault, acquired => PageEntrySuite.SearchMaximumAndCommitTransitionsAsync(acquired)));

    [TestCaseSource(nameof(FavoriteRows))]
    public Task FavoritePageEntryFailureJoinsTheOriginalPagesBeforeRescue(bool cleanupFault)
        => StaTestHost.RunOnStaThreadAsync(() => CheckAsync("favorites", cleanupFault, acquired => PageEntrySuite.FavoriteCommitsPreservePendingOwnerAsync(acquired)));

    [TestCaseSource(nameof(FieldRows))]
    public Task CompiledFieldFailureRetiresItsRealViewAndDirectoryBeforeRescue(string name, string source, bool cleanupFault)
        => StaTestHost.RunOnStaThreadAsync(() => CheckAsync("fields", cleanupFault, acquired => PageEntrySuite.CompiledFieldsConstrainWholeDraftsAsync(name, source, acquired)));

    private static async Task CheckAsync(string scene, bool cleanupFault, Func<Action<HeldFavoritePageFixtureOwner>, Task> run)
    {
        var marker = new InvalidOperationException("page-entry-owned-body-failure");
        HeldFavoritePageFixtureOwner? owned = null;
        var injected = false;
        CleanupFault? failingCleanup = null;
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            void OnAcquired(HeldFavoritePageFixtureOwner owner)
            {
                owned = owner;
                Assert.That(owner.Views, Has.Count.EqualTo(1));
                Assert.That(owner.Views[0].CopyVoiceIdCommand.CanExecute(VoiceId), Is.True);
                if (scene == "search")
                {
                    var actual = owner.Views[0].CommitPageInputCommand.ExecutionTask;
                    Assert.That(actual, Is.Not.Null);
                    Assert.That(actual!.IsCompleted, Is.False);
                    Assert.That(owner.Pages, Does.Contain(actual), "Capture the actual pending command ExecutionTask.");
                    Assert.That(owner.Response, Is.Not.Null);
                    Assert.That(owner.Response!.IsCompleted, Is.False);
                }
                else if (scene == "favorites")
                {
                    Assert.That(owner.Pages, Has.Count.EqualTo(4));
                    Assert.That(owner.Pages.Distinct().Count(), Is.EqualTo(4));
                    Assert.That(owner.Pages.All(task => !task.IsCompleted), Is.True);
                    Assert.That(owner.OldPage, Is.SameAs(owner.Pages[0]));
                    Assert.That(owner.NewPage, Is.SameAs(owner.Pages[1]));
                    Assert.That(owner.AfterRemoval, Is.SameAs(owner.Pages[2]));
                    Assert.That(owner.Views[0].PendingFavoriteMetadataTask, Is.SameAs(owner.Pages[3]));
                    Assert.That(owner.Response, Is.Not.Null);
                    Assert.That(owner.Response!.IsCompleted, Is.False);
                }
                if (scene != "search")
                    Assert.That(Directory.Exists(owner.DirectoryPath), Is.True);
                if (cleanupFault) owner.Track(failingCleanup = new CleanupFault());
                injected = true;
                throw marker;
            }

            var scenario = StaTestHost.RunOnStaThreadAsync(() => run(OnAcquired));
            Exception? failure = null;
            try { await scenario; }
            catch (Exception error) { failure = error; }
            Assert.That(injected, Is.True, "The original callback must reach its owned fault observation.");
            Assert.That(owned, Is.Not.Null);
            var completed = owned!;
            Assert.Multiple(() =>
            {
                Assert.That(scenario.IsFaulted, Is.True);
                Assert.That(failure, Is.SameAs(marker), "Cleanup cannot replace the original body failure.");
                if (cleanupFault)
                {
                    Assert.That(failingCleanup!.Attempts, Is.EqualTo(1));
                    Assert.That(marker.Data[RegressionFixtureOwner.CleanupTypesKey],
                        Is.EqualTo(new[] { typeof(IOException).FullName }));
                }
                else Assert.That(marker.Data[RegressionFixtureOwner.CleanupTypesKey], Is.Null);
                Assert.That(completed.Pages.All(task => task.IsCompletedSuccessfully), Is.True,
                    "Every actual started command/page must finish before independent rescue.");
                Assert.That(completed.Views.All(view => !view.CopyVoiceIdCommand.CanExecute(VoiceId)), Is.True,
                    "The original VM must already be retired before independent rescue.");
                if (scene != "fields")
                    Assert.That(completed.Response!.IsCompletedSuccessfully, Is.True,
                        "The original cancellation-ignoring response must already be released.");
                if (scene != "search")
                    Assert.That(Directory.Exists(completed.DirectoryPath), Is.False,
                        "The owned directory must already be removed after actual Tasks complete.");
            });
        }, () => StaTestHost.RunOnStaThreadAsync(() => RescueAsync(owned)));
    }

    private static TestCaseData Row(string scene, bool cleanupFault) =>
        new TestCaseData(cleanupFault)
            .SetName("PageEntryOwnership_" + scene + "-" + (cleanupFault ? "cleanup-fault" : "body-fault"))
            .SetProperty("FishAudioCaseId", "infra.page-entry/" + scene + "-" + (cleanupFault ? "cleanup-fault" : "body-fault"));

    public static IEnumerable<TestCaseData> DirectoryRows()
    {
        foreach (var copy in new[] { false, true })
        foreach (var cleanupFault in new[] { false, true })
            yield return new TestCaseData(copy, cleanupFault)
                .SetName("NativeInputDirectoryOwnership_" + (copy ? "copy" : "empty-favorites") + "-" + (cleanupFault ? "cleanup-fault" : "body-fault"))
                .SetProperty("FishAudioCaseId", "infra.native-input-directory/" + (copy ? "copy" : "empty-favorites") + "-" + (cleanupFault ? "cleanup-fault" : "body-fault"));
    }

    [TestCaseSource(nameof(DirectoryRows))]
    public Task SettingsInputDirectoryFailureCleansTheOriginalResourcesBeforeRescue(bool copy, bool cleanupFault) =>
        StaTestHost.RunOnStaThreadAsync(() => CheckDirectoryAsync(copy, cleanupFault));

    private static async Task CheckDirectoryAsync(bool copy, bool cleanupFault)
    {
        var marker = new InvalidOperationException("native-input-directory-owned-body-failure");
        HeldFavoritePageFixtureOwner? owned = null;
        CleanupFault? failingCleanup = null;
        var injected = false;
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            var scenario = StaTestHost.RunOnStaThreadAsync(() =>
            {
                var owner = new HeldFavoritePageFixtureOwner();
                owned = owner;
                return owner.RunAsync(() =>
                {
                    var directory = TestAssertions.CreateTempDirectory();
                    owner.DirectoryPath = directory;
                    if (copy)
                        SettingsInputInteractionSuite.VoiceTitlesExposeCopyOnlyActions("en", 640,
                            STranslate.Plugin.Tts.FishAudio.ViewModel.VoiceBrowseMode.Favorites, directory, owner);
                    else SettingsInputInteractionSuite.EmptyFavoritesStayVisibleOutsideCollapsedResults(directory, owner);
                    Assert.That(owner.Views, Has.Count.EqualTo(1));
                    Assert.That(owner.Views[0].CopyVoiceIdCommand.CanExecute(VoiceId), Is.True);
                    Assert.That(owner.Resources.OfType<System.Windows.Interop.HwndSource>().Count(), Is.EqualTo(copy ? 1 : 0));
                    Assert.That(Directory.Exists(owner.DirectoryPath), Is.True);
                    if (cleanupFault) owner.Track(failingCleanup = new CleanupFault());
                    injected = true;
                    throw marker;
                });
            });
            Exception? failure = null;
            try { await scenario; }
            catch (Exception error) { failure = error; }
            Assert.That(injected, Is.True);
            Assert.That(owned, Is.Not.Null);
            var completed = owned!;
            Assert.Multiple(() =>
            {
                Assert.That(scenario.IsFaulted, Is.True);
                Assert.That(failure, Is.SameAs(marker));
                if (cleanupFault)
                {
                    Assert.That(failingCleanup!.Attempts, Is.EqualTo(1));
                    Assert.That(marker.Data[RegressionFixtureOwner.CleanupTypesKey],
                        Is.EqualTo(new[] { typeof(IOException).FullName }));
                }
                else Assert.That(marker.Data[RegressionFixtureOwner.CleanupTypesKey], Is.Null);
                Assert.That(completed.Pages.All(task => task.IsCompletedSuccessfully), Is.True);
                Assert.That(completed.Views.All(view => !view.CopyVoiceIdCommand.CanExecute(VoiceId)), Is.True);
                Assert.That(completed.Resources.OfType<System.Windows.Interop.HwndSource>().All(source => source.IsDisposed), Is.True);
                Assert.That(Directory.Exists(completed.DirectoryPath), Is.False,
                    "The original owner must remove its directory before independent rescue.");
            });
        }, () => StaTestHost.RunOnStaThreadAsync(() => RescueAsync(owned)));
    }

    private sealed class CleanupFault : IDisposable
    {
        internal int Attempts { get; private set; }
        public void Dispose()
        {
            Attempts++;
            throw new IOException("page-entry-owned-cleanup-failure");
        }
    }

    private static Task RescueAsync(HeldFavoritePageFixtureOwner? owner)
    {
        if (owner is null) return Task.CompletedTask;
        var pages = owner.Pages.ToList();
        foreach (var view in owner.Views)
        {
            if (!pages.Contains(view.PendingFavoriteMetadataTask)) pages.Add(view.PendingFavoriteMetadataTask);
            if (view.CommitPageInputCommand.ExecutionTask is { } command && !pages.Contains(command)) pages.Add(command);
        }
        if (owner.Response is { } response && !pages.Contains(response)) pages.Add(response);
        var cleanup = owner.Resources.Reverse().Where(resource => resource is not CleanupFault).Select(resource => (Func<Task>)(() =>
        {
            if (resource is STranslate.Plugin.Tts.FishAudio.ViewModel.SettingsViewModel view
                && !view.CopyVoiceIdCommand.CanExecute(VoiceId)) return Task.CompletedTask;
            if (resource is System.Windows.Interop.HwndSource source && source.IsDisposed) return Task.CompletedTask;
            resource.Dispose();
            return Task.CompletedTask;
        })).ToList();
        cleanup.Add(() => { owner.ReleaseHeldResponse?.Invoke(); return Task.CompletedTask; });
        cleanup.Add(() => Task.WhenAll(pages));
        cleanup.Add(() =>
        {
            if (pages.Any(task => !task.IsCompleted)) throw new InvalidOperationException("page-entry-rescue-before-original-tasks-ended");
            if (owner.DirectoryPath is { } directory) DeleteIndependentDirectory(directory);
            return Task.CompletedTask;
        });
        return RegressionFixtureOwner.RunAsync(() => Task.CompletedTask, cleanup.ToArray());
    }

    private static void DeleteIndependentDirectory(string directory)
    {
        var full = Path.GetFullPath(directory);
        var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FishAudioTests"));
        var name = Path.GetFileName(full);
        if (!Path.IsPathFullyQualified(directory)
            || !string.Equals(full, directory, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetDirectoryName(full), expectedParent, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(name, "N", out var ownerId) || name != ownerId.ToString("N"))
            throw new InvalidOperationException("page-entry-rescue-directory-owner-invalid");
        if (!Directory.Exists(full))
        {
            if (File.Exists(full)) throw new InvalidOperationException("page-entry-rescue-owner-not-directory");
            return;
        }
        for (DirectoryInfo? item = new(full); item is not null; item = item.Parent)
            if (!item.Exists || (item.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidOperationException("page-entry-rescue-directory-invalid");
        var pending = new Stack<string>();
        pending.Push(full);
        while (pending.Count != 0)
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                    throw new InvalidOperationException("page-entry-rescue-entry-invalid");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        Directory.Delete(full, recursive: true);
    }
}
