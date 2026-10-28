using System.Runtime.ExceptionServices;
using NUnit.Framework;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
public sealed class VoiceLanguageOwnershipCases
{
    private const string CleanupFailureKey = "VoiceLanguageSuite.CleanupFailure";

    private static IEnumerable<TestCaseData> FavoriteFailureCases()
    {
        yield return new TestCaseData(false)
            .SetName("FavoriteBodyFailureJoinsOriginalTaskAndDeletesDirectory")
            .SetProperty("FishAudioCaseId", "infra.voice-language/favorite-body-failure-cleans-owned-resources");
        yield return new TestCaseData(true)
            .SetName("FavoriteDirectoryCleanupFailurePreservesOriginalBodyFailure")
            .SetProperty("FishAudioCaseId", "infra.voice-language/favorite-cleanup-failure-preserves-primary");
    }

    [TestCaseSource(nameof(FavoriteFailureCases))]
    public async Task FavoriteFailureRetiresTheActualOwnedWorkBeforeReturning(bool holdDirectoryFile)
    {
        var marker = new InvalidOperationException("voice-language-owned-favorite-body-failure");
        TaskCompletionSource<string>? reply = null;
        Task? ownedTask = null;
        string? directory = null;
        FileStream? heldFile = null;
        ExceptionDispatchInfo? testFailure = null;
        ExceptionDispatchInfo? rescueFailure = null;
        try
        {
            Exception? observed = null;
            try
            {
                await VoiceLanguageSuite.ByIdAndFavoriteRefreshPreserveAndClearLanguagesAsync((pending, task, path) =>
                {
                    reply = pending;
                    ownedTask = task;
                    directory = path;
                    Assert.That(pending.Task.IsCompleted, Is.False, "favorite-fault-seam-reply-must-still-be-held");
                    Assert.That(task.IsCompleted, Is.False, "favorite-fault-seam-must-capture-the-pending-page-task");
                    if (holdDirectoryFile)
                    {
                        var heldPath = Path.Combine(GetOwnedDirectoryPath(path), "cleanup-held.lock");
                        heldFile = new FileStream(heldPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                    }
                    throw marker;
                });
            }
            catch (Exception error) { observed = error; }

            Assert.That(observed, Is.SameAs(marker), "favorite-failure-must-preserve-the-original-body-exception");
            Assert.That(reply, Is.Not.Null, "favorite-fault-seam-did-not-expose-the-original-reply");
            Assert.That(ownedTask, Is.Not.Null, "favorite-fault-seam-did-not-expose-the-original-task");
            Assert.That(directory, Is.Not.Null, "favorite-fault-seam-did-not-expose-the-owned-directory");
            // Observe the scenario's own cleanup before this test releases any held resource.
            Assert.Multiple(() =>
            {
                Assert.That(reply!.Task.IsCompletedSuccessfully, Is.True, "favorite-failure-original-reply-not-released");
                Assert.That(ownedTask!.IsCompletedSuccessfully, Is.True, "favorite-failure-original-page-task-still-pending");
                if (holdDirectoryFile)
                {
                    var secondary = marker.Data[CleanupFailureKey];
                    Assert.That(secondary is IOException or UnauthorizedAccessException, Is.True,
                        "favorite-failure-real-directory-cleanup-error-not-retained");
                    Assert.That(Directory.Exists(directory!), Is.True,
                        "favorite-failure-exclusive-file-must-remain-owned-until-test-rescue");
                }
                else
                {
                    Assert.That(Directory.Exists(directory!), Is.False, "favorite-failure-owned-directory-not-deleted");
                }
            });
        }
        catch (Exception error) { testFailure = ExceptionDispatchInfo.Capture(error); }
        finally
        {
            // Self-rescue also owns the pre-fix RED path; it cannot satisfy the assertions above.
            try { reply?.TrySetResult("""{"title":"Fresh favorite","created_at":"2026-09-18T01:02:03.1234567+08:00","languages":["KO","en"]}"""); }
            catch (Exception error) { rescueFailure ??= ExceptionDispatchInfo.Capture(error); }
            try
            {
                if (ownedTask is not null) await ownedTask;
            }
            catch (Exception error) { rescueFailure ??= ExceptionDispatchInfo.Capture(error); }
            try { heldFile?.Dispose(); }
            catch (Exception error) { rescueFailure ??= ExceptionDispatchInfo.Capture(error); }
            try
            {
                if (directory is not null)
                {
                    var ownedDirectory = GetOwnedDirectoryPath(directory);
                    if (Directory.Exists(ownedDirectory)) Directory.Delete(ownedDirectory, recursive: true);
                }
            }
            catch (Exception error) { rescueFailure ??= ExceptionDispatchInfo.Capture(error); }
        }
        testFailure?.Throw();
        rescueFailure?.Throw();
    }

    private static string GetOwnedDirectoryPath(string directory)
    {
        var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FishAudioTests"));
        var owned = Path.GetFullPath(directory);
        if (!string.Equals(Path.GetDirectoryName(owned), expectedParent, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(Path.GetFileName(owned), "N", out _))
            throw new InvalidOperationException("favorite-rescue-directory-is-outside-the-owned-test-root");
        return owned;
    }
}
