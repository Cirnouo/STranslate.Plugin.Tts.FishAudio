using NUnit.Framework;

namespace NUnitCases.Core;

[TestFixture]
[NonParallelizable]
public sealed class DiagnosticFixtureCleanupCases
{
    [Test]
    [Property("FishAudioCaseId", "runner.diagnostic-fixture/failed-body-joins-original-callback-and-keeps-failure")]
    public Task FailedBodyJoinsOriginalCallbackAndKeepsFailure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var primary = new InvalidOperationException("diagnostic-fixture-original-body-failure");
        var firstCleanup = new IOException("diagnostic-fixture-first-cleanup-failure");
        var secondCleanup = new ArgumentException("diagnostic-fixture-second-cleanup-failure");
        Task callback = Task.CompletedTask;
        Task pending = Task.CompletedTask;
        return RegressionFixtureOwner.RunAsync(async () =>
        {
            pending = DiagnosticFixtureOwner.RunAsync(async owner =>
            {
                owner.Release(() => release.TrySetResult());
                owner.Release(() => cleanupStarted.TrySetResult());
                _ = owner.Track(Task.FromException(firstCleanup));
                _ = owner.Track(Task.FromException(secondCleanup));
                callback = owner.Track(OriginalCallbackAsync());
                await entered.Task;
                throw primary;
            });
            await cleanupStarted.Task;
            Assert.That(callback.IsCompleted, Is.False, "The original callback is deliberately still held after cleanup releases it");
            Assert.That(pending.IsCompleted, Is.False, "Earlier cleanup faults must not skip joining the actual original callback");
            finish.SetResult();
            Exception? caught = null;
            try { await pending; }
            catch (Exception error) { caught = error; }
            Assert.That(caught, Is.SameAs(primary), "Cleanup must preserve the original body exception object");
            Assert.That(callback.IsCompletedSuccessfully, Is.True, "The owner returns only after the original callback exits");
            Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey],
                Is.EquivalentTo(new[] { typeof(IOException).FullName, typeof(ArgumentException).FullName }),
                "Independent cleanup failures remain secondary metadata on the original failure");
        },
        // Independent rescue follows every ownership/failure assertion above.
        () => { release.TrySetResult(); finish.TrySetResult(); return Task.CompletedTask; },
        () => callback,
        () => ObserveExpectedAsync(pending, primary));

        async Task OriginalCallbackAsync()
        {
            entered.SetResult();
            await release.Task;
            await finish.Task;
        }
    }

    [Test]
    [Property("FishAudioCaseId", "runner.diagnostic-fixture/retired-report-still-joins-actual-worker")]
    public Task RetiredReportStillJoinsActualWorker()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var primary = new InvalidOperationException("diagnostic-fixture-post-retirement-body-failure");
        RegressionDiagnostics.WorkCapture? actual = null;
        Task pending = Task.CompletedTask;
        return RegressionFixtureOwner.RunAsync(async () =>
        {
            pending = DiagnosticFixtureOwner.RunAsync(async owner =>
            {
                owner.Release(() => release.TrySetResult());
                owner.Release(() => cleanupStarted.TrySetResult());
                actual = owner.CaptureWork();
                var clock = new ManualStreamingTimeProvider();
                var report = owner.Track(RegressionDiagnostics.RunAsync(async _ =>
                {
                    entered.SetResult();
                    await release.Task;
                    await finish.Task;
                }, clock, actual));
                await entered.Task;
                clock.Advance(TimeSpan.FromSeconds(10));
                await report;
                Assert.That(actual.Worker, Is.Not.Null, "Capture retains the existing Task.Run worker");
                Assert.That(actual.Worker!.IsCompleted, Is.False, "The bounded report retires while its actual worker remains held");
                throw primary;
            });
            await cleanupStarted.Task;
            Assert.That(actual!.Worker!.IsCompleted, Is.False, "Releasing the observation alone does not finish the held worker");
            Assert.That(pending.IsCompleted, Is.False, "The fixture must join the actual worker after the bounded report returned");
            finish.SetResult();
            Exception? caught = null;
            try { await pending; }
            catch (Exception error) { caught = error; }
            Assert.That(caught, Is.SameAs(primary), "Joining the retired worker preserves the original body failure identity");
            Assert.That(actual.Worker.IsCompletedSuccessfully, Is.True, "The actual captured worker has exited before owner completion");
        },
        // This rescue cannot establish the assertions made before it runs.
        () => { release.TrySetResult(); finish.TrySetResult(); return Task.CompletedTask; },
        () => actual?.Worker ?? Task.CompletedTask,
        () => ObserveExpectedAsync(pending, primary));
    }

    private static async Task ObserveExpectedAsync(Task task, Exception primary)
    {
        try { await task; }
        catch (Exception error) when (ReferenceEquals(error, primary)) { }
    }
}
