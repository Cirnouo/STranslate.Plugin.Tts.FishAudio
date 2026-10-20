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
    internal static Task PlaybackFailureAndCancellationRetireBoundCommandsOnUiThreadAsync() =>
        RunOnStaThreadAsync(() =>
        {
            using var network = OverrideNetworkAvailability(false);
            var dispatcher = Dispatcher.CurrentDispatcher;
            var previousInvoker = SettingsViewModel.UiThreadInvokerOverride;
            SettingsViewModel.UiThreadInvokerOverride = action => Dispatch(dispatcher, action);
            try
            {
                foreach (var cancel in new[] { false, true })
                {
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
                    plugin.PendingStartupTask.GetAwaiter().GetResult();
                    var viewModel = plugin.GetOrCreateSettingsViewModel();
                    // A real WPF command listener retains its owning dispatcher even
                    // after the settings window is no longer visible.
                    var pasteButton = new Button { Command = viewModel.PasteApiKeyCommand };
                    var notificationThreads = new List<int>();
                    viewModel.PropertyChanged += RecordLockThread;
                    network.Set(true);
                    var playback = plugin.PlayAudioAsync("Offline dispatcher regression", caller.Token);
                    AssertEqual(false, pasteButton.IsEnabled, "Beginning playback must disable the bound paste command");
                    if (cancel) Task.Run(caller.Cancel).GetAwaiter().GetResult();
                    else release.TrySetResult();

                    Exception? failure = null;
                    try { playback.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
                    catch (Exception error) { failure = error; }
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
                    Drain(dispatcher);
                    AssertEqual(false, viewModel.IsApiKeyInputLocked, "The final operation must unlock after UI publication");
                    AssertEqual(true, pasteButton.IsEnabled, "The bound WPF command must observe final retirement");
                    AssertEqual(true, notificationThreads.All(thread => thread == Environment.CurrentManagedThreadId),
                        "Activity property notifications must stay on the owning UI dispatcher");
                    AssertEqual(1, session.DisposeCount, "Retirement must still dispose its native session");
                    viewModel.PropertyChanged -= RecordLockThread;
                    pasteButton.Command = null;
                    network.Set(false);

                    void RecordLockThread(object? sender, PropertyChangedEventArgs args)
                    {
                        if (args.PropertyName == nameof(SettingsViewModel.IsApiKeyInputLocked))
                            notificationThreads.Add(Environment.CurrentManagedThreadId);
                    }
                }
            }
            finally { SettingsViewModel.UiThreadInvokerOverride = previousInvoker; }
        });

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
