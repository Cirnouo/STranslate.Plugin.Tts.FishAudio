using System.Diagnostics;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Threading;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static StaTestHost;
using static TestAssertions;

internal static class OperationActivityDispatcherSuite
{
    internal static async Task PlaybackFailureAndCancellationRetireBoundCommandsOnUiThreadAsync()
    {
        // Independent error and cancellation cases each retain the shared STA's
        // five-second budget (aggregate execution allowance: five -> ten seconds).
        foreach (var cancel in new[] { false, true })
            await ObserveDispatcherActionAsync(cancel ? "cancellation" : "timeout", stage => PlaybackRetiresBoundCommandsAsync(cancel, stage));
    }

    private static async Task PlaybackRetiresBoundCommandsAsync(bool cancel, Action<string> stage)
    {
        using var network = OverrideNetworkAvailability(false);
        var dispatcher = Dispatcher.CurrentDispatcher;
        var previousInvoker = SettingsViewModel.UiThreadInvokerOverride;
        var workerPublicationCount = 0;
        SettingsViewModel.UiThreadInvokerOverride = action =>
        {
            if (!dispatcher.CheckAccess()) Interlocked.Increment(ref workerPublicationCount);
            Dispatch(dispatcher, action);
        };
        try
        {
            stage("initialization begin");
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var session = new StreamingSessionDouble
            {
                Handler = async (_, _, token) =>
                {
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                    throw new TimeoutException("Synthetic connection timeout.");
                },
            };
            var snackbar = new TestSnackbar();
            var logger = new TestLogger();
            using var caller = new CancellationTokenSource();
            using var plugin = StreamingEntrySuite.CreatePlugin(() => session);
            plugin.Init(CreateContext(settings: CreateTtsSettings(FishAudioModelPolicy.S2ProModel),
                snackbar: snackbar, logger: logger));
            stage("initialization complete; startup wait begin");
            await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
            stage("startup complete; ViewModel begin");
            var viewModel = plugin.GetOrCreateSettingsViewModel();
            // A real WPF command listener retains its owning dispatcher even
            // after the settings window is no longer visible.
            stage("ViewModel complete; Button begin");
            var pasteButton = new Button { Command = viewModel.PasteApiKeyCommand };
            stage("Button complete");
            var notificationThreads = new List<int>();
            viewModel.PropertyChanged += RecordLockThread;
            network.Set(true);
            stage("playback begin");
            var playback = plugin.PlayAudioAsync("Offline dispatcher regression", caller.Token);
            AssertEqual(false, pasteButton.IsEnabled, "Beginning playback must disable the bound paste command");
            var publicationsBeforeRetirement = Volatile.Read(ref workerPublicationCount);
            stage(cancel ? "worker cancellation begin" : "release begin");
            if (cancel) Task.Run(caller.Cancel).GetAwaiter().GetResult();
            else release.TrySetResult();
            stage("blocking playback wait begin");

            Exception? failure = null;
            try { playback.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
            catch (Exception error) { failure = error; }
            stage("blocking playback wait complete");
            if (cancel)
            {
                AssertEqual(true, failure is TaskCanceledException,
                    "Worker-thread retirement must retain quiet host cancellation instead of a dispatcher exception");
                AssertEqual<string?>(null, snackbar.LastError, "Cancellation must not publish a generic error");
            }
            else
            {
                AssertEqual<Exception?>(null, failure,
                    "Worker-thread retirement must not replace the handled native timeout with a dispatcher exception");
                AssertEqual("STranslate_Plugin_Tts_FishAudio_Request_Timeout", snackbar.LastError,
                    "The original connection timeout must retain its localized error");
                AssertEqual(true, logger.Contains("TimeoutException"), "The original timeout classification must be logged");
            }
            AssertEqual(true, Volatile.Read(ref workerPublicationCount) > publicationsBeforeRetirement,
                "Completed worker retirement must have posted its UI publication before the dispatcher is drained");
            AssertEqual(true, viewModel.IsApiKeyInputLocked,
                "The completed background operation must leave the UI lock unchanged until its queued publication runs");
            AssertEqual(false, pasteButton.IsEnabled,
                "The bound command must remain disabled while final retirement publication is still queued");
            stage("dispatcher drain begin");
            Drain(dispatcher);
            stage("dispatcher drain complete");
            AssertEqual(false, viewModel.IsApiKeyInputLocked, "The final operation must unlock after UI publication");
            AssertEqual(true, pasteButton.IsEnabled, "The bound WPF command must observe final retirement");
            AssertEqual(true, notificationThreads.All(thread => thread == Environment.CurrentManagedThreadId),
                "Activity property notifications must stay on the owning UI dispatcher");
            AssertEqual(1, session.DisposeCount, "Retirement must still dispose its native session");
            stage("cleanup begin");
            viewModel.PropertyChanged -= RecordLockThread;
            pasteButton.Command = null;
            network.Set(false);

            void RecordLockThread(object? sender, PropertyChangedEventArgs args)
            {
                if (args.PropertyName == nameof(SettingsViewModel.IsApiKeyInputLocked))
                    notificationThreads.Add(Environment.CurrentManagedThreadId);
            }
        }
        finally
        {
            SettingsViewModel.UiThreadInvokerOverride = previousInvoker;
            stage("cleanup complete");
        }
    }

    private static async Task ObserveDispatcherActionAsync(string name, Func<Action<string>, Task> action,
        Func<Func<Task>, Task>? dispatch = null, Func<Task, Task>? observe = null, Action<string>? write = null)
    {
        var elapsed = Stopwatch.StartNew();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? actionFailure = null;
        var entered = 0;
        void Stage(string stage) => (write ?? Console.WriteLine)($"[activity-dispatcher/{name}] {stage} after {elapsed.ElapsedMilliseconds}ms on thread {Environment.CurrentManagedThreadId}.");
        Stage("queued");
        try
        {
            await (dispatch ?? (callback => RunOnStaThreadAsync(callback)))(async () =>
            {
                Volatile.Write(ref entered, 1);
                Stage("entered");
                try { await action(Stage); }
                catch (Exception error) { Volatile.Write(ref actionFailure, error); Stage($"action failed ({error.GetType().Name})"); throw; }
                finally { completion.TrySetResult(); Stage("action completed"); }
            });
        }
        catch (TimeoutException error) when (!ReferenceEquals(error, Volatile.Read(ref actionFailure)))
        {
            Stage(Volatile.Read(ref entered) != 0 ? "outer action deadline; observing same action for at most 20s" : "STA dispatch deadline before entry; observing same queued action for at most 20s");
            try
            {
                await (observe ?? (pending => pending.WaitAsync(TimeSpan.FromSeconds(20))))(completion.Task);
                Stage("same action observation complete; original deadline remains failed");
            }
            catch (Exception observationFailure) { Stage($"same action observation ended ({observationFailure.GetType().Name}); original deadline remains failed"); }
            throw;
        }
    }

    internal static async Task DeadlineDiagnosticsPreserveOriginalFailureWithoutRetryAsync()
    {
        // Deterministic schedule seam: no real deadline waits or extra STA.
        var outer = new TimeoutException("Synthetic outer deadline");
        Func<Task>? queued = null;
        var runs = 0;
        var observations = 0;
        var messages = new List<string>();
        Exception? caught = null;
        try
        {
            await ObserveDispatcherActionAsync("fixture-outer", _ => { runs++; return Task.CompletedTask; },
                dispatch: callback => { queued = callback; return Task.FromException(outer); },
                observe: async completion => { observations++; await queued!(); await completion; }, write: messages.Add);
        }
        catch (Exception error) { caught = error; }
        AssertEqual(true, ReferenceEquals(outer, caught), "Late completion must retain the original outer deadline exception");
        AssertEqual(1, runs, "Observation must execute the originally queued action exactly once without retry");
        AssertEqual(1, observations, "The outer deadline must observe the same action once");
        AssertEqual(true, messages.Any(message => message.Contains("before entry", StringComparison.Ordinal)), "A queued action deadline must identify that entry was not yet observed");

        caught = null;
        try
        {
            await ObserveDispatcherActionAsync("fixture-observation-deadline", _ => throw new InvalidOperationException("Must not rerun"),
                dispatch: _ => Task.FromException(outer),
                observe: _ => Task.FromException(new TimeoutException("Synthetic observation deadline")), write: messages.Add);
        }
        catch (Exception error) { caught = error; }
        AssertEqual(true, ReferenceEquals(outer, caught), "An observation deadline must preserve the original action deadline exception");

        // An already-entered action can also finish during observation. A
        // separate deadline in observation must never replace the first error.
        var enteredRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? originalAction = null;
        messages.Clear(); caught = null; runs = 0; observations = 0;
        try
        {
            await ObserveDispatcherActionAsync("fixture-entered", async _ => { runs++; await enteredRelease.Task; },
                dispatch: callback => { originalAction = callback(); return Task.FromException(outer); },
                observe: async completion => { observations++; enteredRelease.SetResult(); await completion; await originalAction!; }, write: messages.Add);
        }
        catch (Exception error) { caught = error; }
        AssertEqual(true, ReferenceEquals(outer, caught), "An entered action's late completion must not turn the outer deadline into success");
        AssertEqual(1, runs, "The entered action must not be executed again during observation");
        AssertEqual(1, observations, "An entered deadline must observe the original completion signal");
        AssertEqual(true, messages.Any(message => message.Contains("outer action deadline", StringComparison.Ordinal)), "An entered deadline must be distinguished from a pre-entry dispatch deadline");

        var inner = new TimeoutException("Synthetic action-owned startup deadline");
        messages.Clear(); caught = null; observations = 0;
        try
        {
            await ObserveDispatcherActionAsync("fixture-inner", _ => Task.FromException(inner),
                dispatch: callback => callback(), observe: _ => { observations++; return Task.CompletedTask; }, write: messages.Add);
        }
        catch (Exception error) { caught = error; }
        AssertEqual(true, ReferenceEquals(inner, caught), "An action's own startup timeout must retain its exact exception");
        AssertEqual(0, observations, "An action-owned timeout must not be mislabeled or observed as an outer action deadline");
        AssertEqual(false, messages.Any(message => message.Contains("outer action deadline", StringComparison.Ordinal)), "An inner timeout must retain its action-stage classification");
    }

    internal static Task QueuedRetirementCannotUnlockNewUiOperationAsync() =>
        RunOnStaThreadAsync(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var previousInvoker = SettingsViewModel.UiThreadInvokerOverride;
            SettingsViewModel.UiThreadInvokerOverride = action => Dispatch(dispatcher, action);
            try
            {
                using var viewModel = new SettingsViewModel(CreateContext(), new Settings());
                var pasteButton = new Button { Command = viewModel.PasteApiKeyCommand };
                viewModel.BeginApiKeyOperation();
                Task.Run(viewModel.EndApiKeyOperation).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                viewModel.BeginApiKeyOperation();
                Drain(dispatcher);
                AssertEqual(true, viewModel.IsApiKeyInputLocked, "A queued old End must publish the newer active count");
                AssertEqual(false, pasteButton.IsEnabled, "A queued old End must not enable a newer operation's command");
                Task.Run(viewModel.EndApiKeyOperation).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                Drain(dispatcher);
                AssertEqual(false, viewModel.IsApiKeyInputLocked, "The newer final End must unlock on the dispatcher");
                AssertEqual(true, pasteButton.IsEnabled, "The command must become enabled after the final UI publication");
                pasteButton.Command = null;
            }
            finally { SettingsViewModel.UiThreadInvokerOverride = previousInvoker; }
        });

    private static void Dispatch(Dispatcher dispatcher, Action action)
    {
        if (dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    private static void Drain(Dispatcher dispatcher) => dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
}
