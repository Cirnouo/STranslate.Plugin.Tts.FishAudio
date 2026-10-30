using System.Runtime.ExceptionServices;
using NUnit.Framework;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
public sealed class CoverCacheOwnershipFaultCases
{
    [Test]
    [Property("FishAudioCaseId", "infra.cover-cache/slow-download-body-failure-joins-original-cache-task")]
    public async Task StartedSlowDownloadBodyFailureJoinsOriginalTaskBeforeRescue()
    {
        var marker = new InvalidOperationException("cover-cache-started-download-owned-body-failure");
        CoverCacheOwnershipObservation? ownership = null;
        Task? scenario = null;
        var ready = false;
        ExceptionDispatchInfo? testFailure = null;
        ExceptionDispatchInfo? rescueFailure = null;
        try
        {
            scenario = CoverImageCacheSuite.RunSlowCoverDownloadForOwnershipAsync(observed =>
            {
                // Retain every real acquisition before any readiness assertion.
                ownership = observed;
                Assert.Multiple(() =>
                {
                    Assert.That(observed.DownloadStarted.IsCompletedSuccessfully, Is.True,
                        "cover-cache-fault-must-reach-the-original-started-download");
                    Assert.That(observed.CacheTask.IsCompleted, Is.False,
                        "cover-cache-fault-must-retain-the-original-pending-cache-task");
                    Assert.That(observed.DownloadCanceled.IsCompleted, Is.False,
                        "cover-cache-fault-must-precede-the-original-natural-timeout");
                    Assert.That(Directory.Exists(observed.Root), Is.True,
                        "cover-cache-fault-must-retain-the-actual-owned-directory");
                });
                ready = true;
                throw marker;
            });
            Exception? observedFailure = null;
            try { await scenario; }
            catch (Exception error) { observedFailure = error; }

            // All ownership observations precede the independent rescue below.
            Assert.That(ready, Is.True, "cover-cache-fault-did-not-reach-real-started-pending-work");
            Assert.That(ownership, Is.Not.Null);
            Assert.That(observedFailure, Is.SameAs(marker),
                "cover-cache-cleanup-must-preserve-the-original-body-failure");
            Assert.That(ownership!.CacheTask.IsCompletedSuccessfully, Is.True,
                "cover-cache-owner-returned-before-original-cache-task-completed");
            Assert.That(ownership.DownloadCanceled.IsCompletedSuccessfully, Is.True,
                "cover-cache-owner-returned-before-original-natural-cancellation");
            Assert.That(Directory.Exists(ownership.Root), Is.False,
                "cover-cache-owner-returned-before-owned-directory-cleanup");
            Assert.That(marker.Data[RegressionFixtureOwner.CleanupTypesKey], Is.Null,
                "cover-cache-unknown-cleanup-failure-must-not-be-accepted");
        }
        catch (Exception error) { testFailure = ExceptionDispatchInfo.Capture(error); }
        finally
        {
            // RED rescue waits for the same product 10-second timer and CacheTask.
            // It creates no reply source, delay, poll, retry or extra deadline.
            if (ownership is not null)
            {
                try { await ownership.CacheTask; }
                catch (Exception error) { rescueFailure ??= ExceptionDispatchInfo.Capture(error); }
                try { DeleteOwnedRootIfPresent(ownership.Root); }
                catch (Exception error) { rescueFailure ??= ExceptionDispatchInfo.Capture(error); }
            }
            try { if (scenario is not null) await scenario; }
            catch (Exception error) when (ReferenceEquals(error, marker)) { }
            catch (Exception error) { rescueFailure ??= ExceptionDispatchInfo.Capture(error); }
        }
        testFailure?.Throw();
        rescueFailure?.Throw();
    }

    private static void DeleteOwnedRootIfPresent(string root)
    {
        // Independent rescue never calls the candidate owner cleanup helper.
        var fullRoot = Path.GetFullPath(root);
        var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FishAudioTests"));
        var name = Path.GetFileName(fullRoot);
        if (!Path.IsPathFullyQualified(root)
            || !string.Equals(Path.GetDirectoryName(fullRoot), expectedParent, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(name, "N", out var ownerId)
            || !string.Equals(name, ownerId.ToString("N"), StringComparison.Ordinal))
            throw new InvalidOperationException("cover-cache-rescue-owner-invalid");
        if (!Directory.Exists(fullRoot)) return;
        for (DirectoryInfo? directory = new(fullRoot); directory is not null; directory = directory.Parent)
        {
            if (!directory.Exists || (directory.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidOperationException("cover-cache-rescue-directory-invalid");
        }
        var pending = new Stack<string>();
        pending.Push(fullRoot);
        while (pending.Count != 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                    throw new InvalidOperationException("cover-cache-rescue-entry-invalid");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }
        Directory.Delete(fullRoot, recursive: true);
    }
}

internal sealed class CoverCacheOwnershipObservation(Task cacheTask, Task downloadStarted, Task downloadCanceled, string root)
{
    internal Task CacheTask { get; } = cacheTask;
    internal Task DownloadStarted { get; } = downloadStarted;
    internal Task DownloadCanceled { get; } = downloadCanceled;
    internal string Root { get; } = root;
}
