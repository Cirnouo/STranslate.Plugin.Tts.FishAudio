using System.Runtime.ExceptionServices;
using System.Text.Json;
using NUnit.Framework;

namespace NUnitCompatibility;

// No replacement tasks or timing stimuli: each callback receives the actual
// acquired scenario owner. Rescue follows the post-scenario ownership checks.
[TestFixture]
[NonParallelizable]
public sealed class ThreeHeldWorkOwnershipCases
{
    private const string VoiceId = "11111111111111111111111111111111";

    [Test]
    [Property("FishAudioCaseId", "infra.tts/blocked-load-body-failure-releases-before-dispose")]
    public Task BlockedLoadBodyFailureReleasesBeforeDisposeAndJoinsActualOwners() => AssertTtsAsync(true);

    [Test]
    [Property("FishAudioCaseId", "infra.tts/reinit-credit-body-failure-joins-original-work")]
    public Task ReinitCreditBodyFailureJoinsActualOwners() => AssertTtsAsync(false);

    private static async Task AssertTtsAsync(bool blockedLoad)
    {
        var ordinal = blockedLoad ? 154 : 155;
        var marker = new InvalidOperationException("held-tts-owned-body-failure-" + ordinal);
        HeldTtsFixtureOwner? owned = null;
        var injected = false;
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            void OnHeld(HeldTtsOwnershipObservation observation)
            {
                var owner = observation.Owner;
                owned = owner;
                Assert.Multiple(() =>
                {
                    Assert.That(owner.Plugin, Is.Not.Null);
                    Assert.That(observation.Session.Entered.Task.IsCompletedSuccessfully, Is.True);
                    Assert.That(owner.Entered?.IsCompletedSuccessfully, Is.True);
                    Assert.That(owner.Barrier?.IsCompleted, Is.False,
                        "held-tts-fixture-must-own-the-original-unreleased-barrier");
                    Assert.That(owner.Plugin!.CaptureInitializationWork()!.Work, Is.SameAs(owner.Work[^1]));
                    Assert.That(owner.Work[^1].StartupTask, Is.SameAs(owner.Plugin.PendingStartupTask));
                    if (blockedLoad)
                    {
                        Assert.That(owner.Work, Has.Count.EqualTo(1));
                        Assert.That(owner.Replacement?.IsCompleted, Is.False);
                        Assert.That(owner.Playback?.IsCompleted, Is.False);
                        Assert.That(owner.Work[0].StartupTask.IsCompletedSuccessfully, Is.True);
                    }
                    else
                    {
                        Assert.That(owner.Work, Has.Count.EqualTo(2));
                        Assert.That(owner.Playback?.IsCanceled, Is.True);
                        Assert.That(owner.PlaybackCancellationObserved, Is.True);
                        Assert.That(owner.NewStartup, Is.SameAs(owner.Work[^1].StartupTask));
                        Assert.That(owner.NewStartup!.IsCompleted, Is.False);
                        var observer = owner.Views[^1].PendingStartupCreditObserverTask;
                        Assert.That(observer, Is.Not.Null);
                        Assert.That(observer!.IsCompleted, Is.False);
                        Assert.That(owner.Observers, Does.Contain(observer));
                    }
                });
                if (blockedLoad) WriteBlockedLoadReady(owner);
                injected = true;
                throw marker;
            }

            var scenario = blockedLoad
                ? TtsSuite.RunPlayAudioUsesOldInitializationWhileReinitLoadIsBlockedAsync(OnHeld)
                : TtsSuite.RunPlayAudioUsesInitializationSnapshotAcrossReinitAsync(OnHeld);
            Exception? failure = null;
            try { await scenario; }
            catch (Exception error) { failure = error; }

            // In authentic RED154, old Dispose blocks behind held OnLoad and
            // this point is not reached before the unchanged external deadline.
            Assert.That(injected, Is.True, "held-tts-fault-must-observe-original-started-work");
            Assert.That(owned, Is.Not.Null);
            var completed = owned!;
            Assert.Multiple(() =>
            {
                Assert.That(scenario.IsFaulted, Is.True);
                Assert.That(failure, Is.SameAs(marker), "held-tts-cleanup-must-preserve-primary-identity");
                Assert.That(marker.Data[RegressionFixtureOwner.CleanupTypesKey], Is.Null,
                    "held-tts-unknown-cleanup-fault-must-remain-visible");
                Assert.That(completed.Barrier!.IsCompletedSuccessfully, Is.True,
                    "held-tts-original-barrier-must-release-before-rescue");
                Assert.That(completed.Playback!.IsCanceled, Is.True);
                Assert.That(completed.PlaybackCancellationJoined, Is.True,
                    "held-tts-original-playback-must-be-joined-before-rescue");
                Assert.That(completed.Plugin!.CaptureInitializationWork(), Is.Null,
                    "held-tts-main-must-retire-before-rescue");
                Assert.That(completed.Work, Has.Count.EqualTo(2));
                Assert.That(completed.Views, Has.Count.EqualTo(2));
                foreach (var work in completed.Work)
                {
                    Assert.That(work.StartupTask.IsCompletedSuccessfully, Is.True,
                        "held-tts-original-startup-must-complete-before-rescue");
                    var retirement = work.RetirementTasks;
                    Assert.That(retirement, Is.Not.Empty);
                    Assert.That(retirement.All(task => task.IsCompletedSuccessfully), Is.True,
                        "held-tts-actual-retirement-must-complete-before-rescue");
                }
                Assert.That(completed.Observers.All(task => task.IsCompletedSuccessfully), Is.True);
                Assert.That(completed.Views.All(view => !view.CopyVoiceIdCommand.CanExecute(VoiceId)), Is.True);
                if (blockedLoad) Assert.That(completed.Replacement!.IsCompletedSuccessfully, Is.True);
                else Assert.That(completed.NewStartup!.IsCompletedSuccessfully, Is.True);
            });
        }, () => RescueTtsAsync(owned));
    }

    [Test]
    [Property("FishAudioCaseId", "infra.favorite-cache/held-page-body-failure-joins-original-pages")]
    public async Task HeldPageBodyFailureJoinsOriginalPagesAndCleansOwnedDirectory()
    {
        var marker = new InvalidOperationException("held-favorite-page-owned-body-failure");
        HeldFavoritePageFixtureOwner? owned = null;
        var snapshots = 0;
        var injected = false;
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            var scenario = FavoriteStorageCacheSuite.RunFavoriteMissesLoadOnlyVisiblePageAndRejectRemovedCardResultsAsync(observation =>
            {
                var owner = observation.Owner;
                owned = owner;
                snapshots++;
                if (owner.AfterRemoval is null) return;
                Assert.Multiple(() =>
                {
                    Assert.That(snapshots, Is.EqualTo(3));
                    Assert.That(owner.OldPage, Is.Not.Null);
                    Assert.That(owner.NewPage, Is.Not.Null);
                    Assert.That(owner.OldPage, Is.Not.SameAs(owner.NewPage));
                    Assert.That(owner.NewPage, Is.Not.SameAs(owner.AfterRemoval));
                    Assert.That(owner.OldPage!.IsCompleted, Is.False);
                    Assert.That(owner.NewPage!.IsCompleted, Is.False);
                    Assert.That(owner.AfterRemoval!.IsCompleted, Is.False);
                    Assert.That(owner.AfterRemoval, Is.SameAs(owner.Views[0].PendingFavoriteMetadataTask));
                    Assert.That(owner.Response!.IsCompleted, Is.False);
                    Assert.That(owner.Views[0].CopyVoiceIdCommand.CanExecute(VoiceId), Is.True);
                    Assert.That(Directory.Exists(owner.DirectoryPath), Is.True);
                });
                injected = true;
                throw marker;
            });
            Exception? failure = null;
            try { await scenario; }
            catch (Exception error) { failure = error; }
            Assert.That(injected, Is.True, "held-page-fault-must-observe-three-original-held-pages");
            Assert.That(owned, Is.Not.Null);
            var completed = owned!;
            Assert.Multiple(() =>
            {
                Assert.That(scenario.IsFaulted, Is.True);
                Assert.That(failure, Is.SameAs(marker), "held-page-cleanup-must-preserve-primary-identity");
                Assert.That(marker.Data[RegressionFixtureOwner.CleanupTypesKey], Is.Null,
                    "held-page-unknown-cleanup-fault-must-remain-visible");
                Assert.That(completed.Response!.IsCompletedSuccessfully, Is.True,
                    "held-page-original-response-must-release-before-rescue");
                Assert.That(completed.Pages.All(task => task.IsCompletedSuccessfully), Is.True,
                    "held-page-original-page-tasks-must-complete-before-rescue");
                Assert.That(completed.Views.All(view => !view.CopyVoiceIdCommand.CanExecute(VoiceId)), Is.True);
                Assert.That(Directory.Exists(completed.DirectoryPath), Is.False,
                    "held-page-owned-directory-must-clean-after-task-joins-before-rescue");
            });
        }, () => RescueFavoritePagesAsync(owned));
    }
    private static void WriteBlockedLoadReady(HeldTtsFixtureOwner owner)
    {
        var evidenceRoot = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
        var contextPath = Environment.GetEnvironmentVariable("FISHAUDIO_FRAMEWORK_CONTROL_CONTEXT");
        // A direct non-framework invocation has no authenticated framework run to bind.
        if (string.IsNullOrEmpty(evidenceRoot) || string.IsNullOrEmpty(contextPath)) return;
        try
        {
            if (!Path.IsPathFullyQualified(evidenceRoot) || !Path.IsPathFullyQualified(contextPath))
                throw new InvalidDataException("held-tts-ready-context-invalid");
            var root = Path.GetFullPath(evidenceRoot);
            if (!Directory.Exists(root)
                || (File.GetAttributes(root) & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidDataException("held-tts-ready-root-invalid");
            var contextFile = new FileInfo(contextPath);
            if (!contextFile.Exists
                || (contextFile.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidDataException("held-tts-ready-context-invalid");
            using var contextStream = new FileStream(contextPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (contextStream.Length is <= 0 or > 16 * 1024 * 1024)
                throw new InvalidDataException("held-tts-ready-context-invalid");
            using var document = JsonDocument.Parse(contextStream);
            var context = document.RootElement;
            if (context.GetProperty("schemaVersion").GetInt32() != 1)
                throw new InvalidDataException("held-tts-ready-context-invalid");
            var runId = context.GetProperty("runId").GetString();
            if (!Guid.TryParseExact(runId, "N", out var run) || run == Guid.Empty || run.ToString("N") != runId)
                throw new InvalidDataException("held-tts-ready-run-invalid");
            var treeValue = context.GetProperty("sourceTree");
            string? sourceTree = treeValue.ValueKind == JsonValueKind.Null ? null : treeValue.GetString();
            if (treeValue.ValueKind != JsonValueKind.Null &&
                (sourceTree is null || sourceTree.Length is not (40 or 64)
                    || !sourceTree.All(value => value is >= '0' and <= '9' or >= 'a' and <= 'f')))
                throw new InvalidDataException("held-tts-ready-tree-invalid");
            const string caseId = "infra.tts/blocked-load-body-failure-releases-before-dispose";
            var cases = context.GetProperty("cases");
            if (cases.ValueKind != JsonValueKind.Array || cases.EnumerateArray().Count(item =>
                item.GetProperty("canonicalId").GetString() == caseId
                    && item.GetProperty("fullName").GetString() == TestContext.CurrentContext.Test.FullName) != 1)
                throw new InvalidDataException("held-tts-ready-case-invalid");
            var loadPending = owner.Entered?.IsCompletedSuccessfully == true && owner.Barrier?.IsCompleted == false;
            var replacementPending = owner.Replacement?.IsCompleted == false;
            var playbackPending = owner.Playback?.IsCompleted == false;
            if (!loadPending || !replacementPending || !playbackPending)
                throw new InvalidDataException("held-tts-ready-work-no-longer-pending");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                kind = "held-tts-blocked-load-ready",
                caseId,
                runId,
                sourceTree,
                pid = Environment.ProcessId,
                loadPending,
                replacementPending,
                playbackPending,
            });
            using var stream = new FileStream(Path.Combine(root, "held-tts-154-ready.json"),
                FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            // Context/filesystem messages can contain private paths; emit only a fixed failure.
            throw new InvalidDataException("held-tts-ready-evidence-failed");
        }
    }

    private static async Task RescueTtsAsync(HeldTtsFixtureOwner? owner)
    {
        if (owner is null) return;
        ExceptionDispatchInfo? failure = null;
        var work = owner.Work.ToList();
        var observers = owner.Observers.ToList();
        var views = owner.Views.ToList();
        var startup = work.Select(item => item.StartupTask).ToList();
        if (owner.NewStartup is { } newStartup && !startup.Contains(newStartup)) startup.Add(newStartup);
        // Independent rescue releases first: authentic RED154 cannot reach this
        // callback while its awaited scenario is blocked in old Dispose.
        try { owner.ReleaseHeldBarrier?.Invoke(); }
        catch (Exception error) { KeepRescueFailure(ref failure, error); }
        if (owner.Replacement is { } replacement)
            try { await replacement; }
            catch (Exception error) { KeepRescueFailure(ref failure, error); }
        try
        {
            // Observe any actual replacement published after the original probe.
            // Do not call the candidate CaptureCurrent/CompletionStages helpers.
            var capture = owner.Plugin?.CaptureInitializationWork();
            if (capture is not null)
            {
                if (!work.Contains(capture.Work)) work.Add(capture.Work);
                if (!startup.Contains(capture.Work.StartupTask)) startup.Add(capture.Work.StartupTask);
                if (capture.ViewModel is { } view)
                {
                    if (!views.Contains(view)) views.Add(view);
                    var observer = view.PendingStartupCreditObserverTask;
                    if (observer is not null && !observers.Contains(observer)) observers.Add(observer);
                }
            }
        }
        catch (Exception error) { KeepRescueFailure(ref failure, error); }
        var mainRetired = owner.MainDisposed;
        try
        {
            if (owner.Plugin is { } plugin)
            {
                plugin.Dispose();
                mainRetired = true;
            }
        }
        catch (Exception error) { KeepRescueFailure(ref failure, error); }
        foreach (var view in views)
            try { if (view.CopyVoiceIdCommand.CanExecute(VoiceId)) view.Dispose(); }
            catch (Exception error) { KeepRescueFailure(ref failure, error); }
        if (owner.Playback is { } playback)
            try { await playback; }
            catch (TaskCanceledException) when (playback.IsCanceled &&
                (owner.PlaybackCancellationObserved || mainRetired)) { }
            catch (Exception error) { KeepRescueFailure(ref failure, error); }
        foreach (var task in startup)
            try { await task; }
            catch (Exception error) { KeepRescueFailure(ref failure, error); }
        foreach (var task in observers)
            try { await task; }
            catch (Exception error) { KeepRescueFailure(ref failure, error); }
        foreach (var initialization in work)
        {
            var retirement = initialization.RetirementTasks;
            if (retirement.Count == 0)
                KeepRescueFailure(ref failure, new InvalidOperationException("held-tts-rescue-retirement-not-recorded"));
            foreach (var task in retirement)
                try { await task; }
                catch (Exception error) { KeepRescueFailure(ref failure, error); }
        }
        failure?.Throw();
    }

    private static async Task RescueFavoritePagesAsync(HeldFavoritePageFixtureOwner? owner)
    {
        if (owner is null) return;
        ExceptionDispatchInfo? failure = null;
        var pages = owner.Pages.ToList();
        foreach (var view in owner.Views)
            try
            {
                var actualPage = view.PendingFavoriteMetadataTask;
                if (!pages.Contains(actualPage)) pages.Add(actualPage);
            }
            catch (Exception error) { KeepRescueFailure(ref failure, error); }
        var resources = owner.Resources;
        for (var index = resources.Count - 1; index >= 0; index--)
            try
            {
                // Avoid disposing an already-retired VM a second time. The store's
                // own Dispose is idempotent; it may be a separate acquired resource.
                if (resources[index] is STranslate.Plugin.Tts.FishAudio.ViewModel.SettingsViewModel view
                    && !view.CopyVoiceIdCommand.CanExecute(VoiceId)) continue;
                resources[index].Dispose();
            }
            catch (Exception error) { KeepRescueFailure(ref failure, error); }
        try { owner.ReleaseHeldResponse?.Invoke(); }
        catch (Exception error) { KeepRescueFailure(ref failure, error); }
        foreach (var page in pages)
            try { await page; }
            catch (Exception error) { KeepRescueFailure(ref failure, error); }
        try
        {
            if (owner.DirectoryPath is { } directory)
            {
                if (pages.Any(page => !page.IsCompleted))
                    throw new InvalidOperationException("held-page-rescue-before-original-pages-ended");
                DeleteOwnedFavoriteRoot(directory);
            }
        }
        catch (Exception error) { KeepRescueFailure(ref failure, error); }
        failure?.Throw();
    }

    private static void DeleteOwnedFavoriteRoot(string directory)
    {
        // This path check and deletion are independent of the candidate owner.
        var full = Path.GetFullPath(directory);
        var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FishAudioTests"));
        var name = Path.GetFileName(full);
        if (!Path.IsPathFullyQualified(directory)
            || !string.Equals(full, directory, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetDirectoryName(full), expectedParent, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(name, "N", out var ownerId) || name != ownerId.ToString("N"))
            throw new InvalidOperationException("held-page-rescue-directory-owner-invalid");
        if (!Directory.Exists(full))
        {
            if (File.Exists(full)) throw new InvalidOperationException("held-page-rescue-owner-not-directory");
            return;
        }
        for (DirectoryInfo? item = new(full); item is not null; item = item.Parent)
            if (!item.Exists || (item.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidOperationException("held-page-rescue-directory-invalid");
        var pending = new Stack<string>();
        pending.Push(full);
        while (pending.Count != 0)
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                    throw new InvalidOperationException("held-page-rescue-entry-invalid");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        Directory.Delete(full, recursive: true);
    }

    private static void KeepRescueFailure(ref ExceptionDispatchInfo? failure, Exception error)
    {
        if (failure is null)
        {
            failure = ExceptionDispatchInfo.Capture(error);
            return;
        }
        if (ReferenceEquals(failure.SourceException, error)) return;
        try
        {
            if (failure.SourceException.Data[RegressionFixtureOwner.CleanupTypesKey] is not List<string> types)
                failure.SourceException.Data[RegressionFixtureOwner.CleanupTypesKey] = types = [];
            types.Add(error.GetType().FullName ?? error.GetType().Name);
        }
        catch { /* Secondary diagnostics cannot replace the original rescue failure. */ }
    }

}

internal sealed class HeldTtsOwnershipObservation(HeldTtsFixtureOwner owner, StreamingSessionDouble session)
{
    internal HeldTtsFixtureOwner Owner { get; } = owner;
    internal StreamingSessionDouble Session { get; } = session;
}

internal sealed class HeldFavoritePageObservation(HeldFavoritePageFixtureOwner owner)
{
    internal HeldFavoritePageFixtureOwner Owner { get; } = owner;
}

