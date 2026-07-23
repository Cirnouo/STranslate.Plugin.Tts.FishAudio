using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.Presentation;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using static AsyncTestWait;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static StaTestHost;
using static TestAssertions;

internal static class OperationActivitySuite
{
    internal static void OperationActivityCounterLinearizesStateTransitions()
    {
        var sharedGate = new object();
        var counter = new OperationActivityCounter(sharedGate);

        AssertEqual(false, counter.IsActive, "A new operation counter should be inactive");
        AssertEqual(true, counter.Begin(), "The first Begin should report an inactive-to-active transition");
        AssertEqual(false, counter.Begin(), "Overlapping Begin should not report another active transition");
        AssertEqual(false, counter.End(), "A non-final End should not report an active-to-inactive transition");
        AssertEqual(true, counter.IsActive, "The counter should stay active until the final operation ends");
        AssertEqual(true, counter.End(), "The final End should report an active-to-inactive transition");
        AssertEqual(false, counter.IsActive, "The counter should become inactive after the final End");
        AssertEqual(false, counter.End(), "Extra End calls should be ignored without making the count negative");

        counter.Begin();
        var delayedEndTransition = counter.End();
        var newerBeginTransition = counter.Begin();

        AssertEqual(true, delayedEndTransition, "The old End should observe its own transition to inactive");
        AssertEqual(true, newerBeginTransition, "A newer Begin should transition the counter back to active");
        AssertEqual(true, counter.IsActive, "A delayed old notification must observe the newer active state");
        counter.End();
    }

    internal static void OperationActivityCounterPublishesOutsideGateAcrossThreads()
    {
        var sharedGate = new object();
        var observableState = false;
        OperationActivityCounter? counter = null;
        Thread[]? workers = null;
        using var workersReady = new CountdownEvent(2);
        using var workersCompleted = new CountdownEvent(2);
        var workersStartedInsideCallback = false;
        var workersCompletedInsideCallback = false;
        counter = new OperationActivityCounter(sharedGate, value =>
        {
            observableState = value;
            if (value || workers is not null)
                return;

            workers = Enumerable.Range(0, 2)
                .Select(_ => new Thread(() =>
                {
                    workersReady.Signal();
                    try
                    {
                        counter!.Begin();
                    }
                    finally
                    {
                        workersCompleted.Signal();
                    }
                })
                {
                    IsBackground = true,
                })
                .ToArray();

            foreach (var worker in workers)
                worker.Start();

            workersStartedInsideCallback = workersReady.Wait(TimeSpan.FromSeconds(2));
            workersCompletedInsideCallback = workersStartedInsideCallback
                && workersCompleted.Wait(TimeSpan.FromSeconds(1));
        });

        counter.Begin();
        counter.End();

        AssertEqual(true, workersCompleted.Wait(TimeSpan.FromSeconds(2)), "Cross-thread operations should finish after the setter callback returns");
        foreach (var worker in workers!)
            worker.Join();

        AssertEqual(true, workersStartedInsideCallback, "Both worker operations should start while the setter callback is active");
        AssertEqual(true, workersCompletedInsideCallback, "The setter callback should not hold the operation gate while waiting for worker operations");
        AssertEqual(true, counter.IsActive, "Concurrent Begin operations should leave the counter active");
        AssertEqual(true, observableState, "Published state should converge to the active count after cross-thread reentrancy");

        counter.End();
        AssertEqual(true, counter.IsActive, "The first worker End should leave the other operation active");
        AssertEqual(true, observableState, "Observable state should remain active until the final worker End");
        counter.End();
        AssertEqual(false, counter.IsActive, "The final worker End should make the counter inactive");
        AssertEqual(false, observableState, "Observable state should converge to inactive after the final worker End");
        AssertEqual(false, counter.End(), "An extra End should remain safe after cross-thread publication");
    }

    internal static void OperationActivityCounterRecoveryFailureLeavesPublisherReusable()
    {
        var sharedGate = new object();
        var observableState = false;
        var activePublicationCount = 0;
        var initialException = new InvalidOperationException("Expected initial observable publication failure.");
        var recoveryException = new InvalidOperationException("Expected recovery observable publication failure.");
        var throwOnInactivePublication = true;
        var throwOnRecoveryPublication = false;
        Thread? reentrantWorker = null;
        using var reentrantWorkerCompleted = new ManualResetEventSlim();
        OperationActivityCounter? counter = null;
        counter = new OperationActivityCounter(sharedGate, value =>
        {
            observableState = value;
            if (value)
            {
                activePublicationCount++;
                if (throwOnRecoveryPublication)
                {
                    throwOnRecoveryPublication = false;
                    throw recoveryException;
                }

                return;
            }

            if (!throwOnInactivePublication)
                return;

            throwOnInactivePublication = false;
            reentrantWorker = new Thread(() =>
            {
                try
                {
                    counter!.Begin();
                }
                finally
                {
                    reentrantWorkerCompleted.Set();
                }
            })
            {
                IsBackground = true,
            };
            reentrantWorker.Start();
            if (!reentrantWorkerCompleted.Wait(TimeSpan.FromSeconds(2)))
                throw new TimeoutException("The reentrant Begin did not complete inside the setter callback.");

            throwOnRecoveryPublication = true;
            throw initialException;
        });

        counter.Begin();
        Exception? caughtException = null;
        try
        {
            counter.End();
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        reentrantWorker?.Join();
        AssertEqual(typeof(AggregateException), caughtException?.GetType(), "A failed recovery publication should report both publication exceptions");
        var aggregateException = (AggregateException)caughtException!;
        AssertEqual(2, aggregateException.InnerExceptions.Count, "Recovery failure should retain both observable publication exceptions");
        AssertEqual(initialException, aggregateException.InnerExceptions[0], "Recovery failure should retain the original publication exception first");
        AssertEqual(recoveryException, aggregateException.InnerExceptions[1], "Recovery failure should retain the recovery publication exception second");
        AssertEqual(true, counter.IsActive, "The reentrant Begin should remain counted after both publication attempts fail");
        AssertEqual(true, observableState, "The failed recovery setter should still expose the state it assigned before throwing");

        AssertEqual(false, counter.Begin(), "A later overlapping Begin should reclaim publisher ownership after recovery failure");
        AssertEqual(3, activePublicationCount, "The next operation should republish the still-pending active version exactly once");
        AssertEqual(false, counter.End(), "The recovery Begin should end without clearing the remaining active operation");
        AssertEqual(true, counter.End(), "The original reentrant operation should end normally after publisher recovery");
        AssertEqual(false, observableState, "Observable state should converge to inactive after publisher recovery");
    }

    internal static void OperationActivityCounterPublishesPendingStateBeforeRethrowingPublicationFailure()
    {
        var sharedGate = new object();
        var observableState = false;
        var activePublicationCount = 0;
        var expectedException = new InvalidOperationException("Expected one-shot observable publication failure.");
        var throwOnInactivePublication = true;
        var reentrantWorkerCompletedInsideCallback = false;
        Thread? reentrantWorker = null;
        using var reentrantWorkerCompleted = new ManualResetEventSlim();
        OperationActivityCounter? counter = null;
        counter = new OperationActivityCounter(sharedGate, value =>
        {
            observableState = value;
            if (value)
            {
                activePublicationCount++;
                return;
            }

            if (!throwOnInactivePublication)
                return;

            throwOnInactivePublication = false;
            reentrantWorker = new Thread(() =>
            {
                try
                {
                    counter!.Begin();
                }
                finally
                {
                    reentrantWorkerCompleted.Set();
                }
            })
            {
                IsBackground = true,
            };
            reentrantWorker.Start();
            reentrantWorkerCompletedInsideCallback = reentrantWorkerCompleted.Wait(TimeSpan.FromSeconds(2));
            throw expectedException;
        });

        counter.Begin();
        Exception? caughtException = null;
        try
        {
            counter.End();
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        reentrantWorker?.Join();
        AssertEqual(true, reentrantWorkerCompletedInsideCallback, "The reentrant Begin should complete before the failing setter callback returns");
        AssertEqual(expectedException, caughtException, "End should propagate the original observable publication exception");
        AssertEqual(2, activePublicationCount, "The pending active state should be published before the original exception is rethrown");
        AssertEqual(true, counter.IsActive, "The reentrant Begin should leave the counter active after exception recovery");
        AssertEqual(true, observableState, "Observable state should converge to the pending active count before End rethrows");

        AssertEqual(false, counter.Begin(), "A later overlapping Begin should remain usable after exception recovery");
        AssertEqual(false, counter.End(), "The overlapping operation should end without clearing the remaining active operation");
        AssertEqual(true, counter.End(), "The remaining operation should end normally after exception recovery");
        AssertEqual(false, observableState, "A later final End should publish inactive normally after exception recovery");
        AssertEqual(true, counter.Begin(), "A new operation should still publish active after exception recovery");
        AssertEqual(true, observableState, "A new operation should update observable state after exception recovery");
        AssertEqual(true, counter.End(), "The new operation should end normally after exception recovery");
        AssertEqual(false, observableState, "Observable state should finish inactive after the follow-up operation");
    }

    internal static async Task OverlappingStartupManualAndTtsOperationsUnlockAfterFinalCompletionAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings { ApiKey = AppliedKey };
        var startupCreditStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStartupCredit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var manualCreditStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseManualCredit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var creditRequestCount = 0;
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetAsyncHandler = (url, _, _) =>
        {
            if (!url.Contains("/wallet/self/api-credit", StringComparison.Ordinal))
                return Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");

            return Interlocked.Increment(ref creditRequestCount) switch
            {
                1 => StartRequest(startupCreditStarted, releaseStartupCredit),
                2 => StartRequest(manualCreditStarted, releaseManualCredit),
                _ => Task.FromException<string>(new InvalidOperationException("Unexpected credit request.")),
            };
        };
        var plugin = new Main();

        plugin.Init(CreateContext(settings: settings, httpService: httpService), FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        await startupCreditStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var viewModel = plugin.GetOrCreateSettingsViewModel();
        viewModel.BeginApiKeyOperation();

        releaseStartupCredit.SetResult("{\"credit\":\"45.67\"}");
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !viewModel.IsLoadingCredit, "startup credit overlap to complete");

        AssertEqual(true, viewModel.IsApiKeyInputLocked, "Startup completion should not unlock the overlapping TTS operation");

        var manualRefreshTask = viewModel.RefreshCreditCommand.ExecuteAsync(null);
        await manualCreditStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(true, viewModel.IsLoadingCredit, "Manual refresh should become the active credit operation");
        AssertEqual(true, viewModel.IsApiKeyInputLocked, "Manual refresh should keep the API Key locked while TTS is active");

        viewModel.EndApiKeyOperation();
        AssertEqual(true, viewModel.IsApiKeyInputLocked, "TTS completion should not unlock the overlapping manual refresh");

        var reentrantTtsStarted = false;
        viewModel.PropertyChanging += (_, args) =>
        {
            if (!reentrantTtsStarted && args.PropertyName == nameof(SettingsViewModel.IsApiKeyInputLocked))
            {
                reentrantTtsStarted = true;
                viewModel.BeginApiKeyOperation();
            }
        };

        releaseManualCredit.SetResult("{\"credit\":\"56.78\"}");
        await manualRefreshTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(true, reentrantTtsStarted, "Manual completion should deterministically start the final reentrant TTS operation");
        AssertEqual(false, viewModel.IsLoadingCredit, "Manual completion should clear credit loading after startup has completed");
        AssertEqual(true, viewModel.IsApiKeyInputLocked, "Manual completion should not overwrite the final TTS lock");

        viewModel.EndApiKeyOperation();
        AssertEqual(false, viewModel.IsApiKeyInputLocked, "The API Key should unlock only after the final TTS operation completes");

        static Task<string> StartRequest(TaskCompletionSource started, TaskCompletionSource<string> release)
        {
            started.TrySetResult();
            return release.Task;
        }
    }

    internal static void ApiKeyOperationCounterHandlesPropertyChangingReentrancy()
    {
        var viewModel = new SettingsViewModel(CreateContext(), new Settings());
        viewModel.BeginApiKeyOperation();
        var injectedNewOperation = false;
        viewModel.PropertyChanging += (_, args) =>
        {
            if (!injectedNewOperation && args.PropertyName == nameof(SettingsViewModel.IsApiKeyInputLocked))
            {
                injectedNewOperation = true;
                viewModel.BeginApiKeyOperation();
            }
        };

        viewModel.EndApiKeyOperation();

        AssertEqual(true, injectedNewOperation, "The test should inject Begin while the old End is changing the observable lock state");
        AssertEqual(true, viewModel.IsApiKeyInputLocked, "A reentrant newer Begin should not be overwritten by the old End state assignment");

        viewModel.EndApiKeyOperation();
        AssertEqual(false, viewModel.IsApiKeyInputLocked, "The reentrant operation should unlock when its own End completes");
    }

    internal static async Task ApiKeyOperationCounterHandlesPropertyChangedReentrancyAsync()
    {
        var viewModel = new SettingsViewModel(CreateContext(), new Settings());
        viewModel.BeginApiKeyOperation();
        var injectedNewOperation = false;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (!injectedNewOperation && args.PropertyName == nameof(SettingsViewModel.IsApiKeyInputLocked))
            {
                injectedNewOperation = true;
                viewModel.BeginApiKeyOperation();
            }
        };

        await Task.Run(viewModel.EndApiKeyOperation).WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(true, injectedNewOperation, "The test should inject Begin after the old End changes the observable lock state");
        AssertEqual(true, viewModel.IsApiKeyInputLocked, "PropertyChanged reentrancy should restore the active API Key lock without deadlocking");

        viewModel.EndApiKeyOperation();
        AssertEqual(false, viewModel.IsApiKeyInputLocked, "The PropertyChanged reentrant operation should unlock after its own End completes");
    }
}
