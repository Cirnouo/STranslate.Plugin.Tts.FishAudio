using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Controls;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static StaTestHost;
using static TestAssertions;

internal static class SearchAdjustmentControlSuite
{
    internal static async Task RunAllAsync(string? group = null, string? proofDirectory = null)
    {
        if (group is "proof")
        {
            foreach (var (locale, dark) in new[] { ("en", false), ("en", true), ("ru", true) })
                await ObserveMatrixActionAsync($"proof/{locale}/{dark}", stage => GeometryAndFlags(locale, dark, 1.25, stage, proofDirectory));
            await RunOnStaThreadAsync(() => ShortViewportKeepsPopupInsideVisibleArea(proofDirectory));
            return;
        }
        if (group is "lifetime")
        {
            await RunOnStaThreadAsync(OverlayOwnershipAndLifetime);
            await RunOnStaThreadAsync(WindowAndScrollLifetime);
            await RunOnStaThreadAsync(PopupMaterialUsesItsOwnNonlayeredSource);
            await RunMaterialFallbackAsync();
            return;
        }
        if (group is "material")
        {
            await RunOnStaThreadAsync(PopupMaterialUsesItsOwnNonlayeredSource);
            await RunMaterialFallbackAsync();
            return;
        }
        if (group is "radio")
        {
            foreach (var dark in new[] { false, true }) await RunOnStaThreadAsync(() => RadioAppearanceMatchesNativePeer(dark));
            return;
        }
        if (group is "edges")
        {
            await RunOnStaThreadAsync(() => ShortViewportKeepsPopupInsideVisibleArea(proofDirectory));
            await ObserveViewportActionAsync(ViewportClampsAcrossTriggerOrRefuses);
            return;
        }
        await RunOnStaThreadAsync(CaptionsAndKeyboardApplyAsync);
        if (group is "titles") return;
        foreach (var dark in new[] { false, true }) await RunOnStaThreadAsync(() => RadioAppearanceMatchesNativePeer(dark));
        await RunOnStaThreadAsync(DraftsApplyAndTileInputAsync);
        await RunOnStaThreadAsync(OverlayOwnershipAndLifetime);
        await RunOnStaThreadAsync(WindowAndScrollLifetime);
        await RunOnStaThreadAsync(() => ShortViewportKeepsPopupInsideVisibleArea(proofDirectory));
        await ObserveViewportActionAsync(ViewportClampsAcrossTriggerOrRefuses);
        await RunOnStaThreadAsync(PopupMaterialUsesItsOwnNonlayeredSource);
        await RunMaterialFallbackAsync();
        foreach (var locale in UiLocales.All)
        foreach (var dark in new[] { false, true })
        foreach (var dpi in new[] { 1d, 1.25, 1.5 })
            await ObserveMatrixActionAsync($"{locale}/{(dark ? "dark" : "light")}/{dpi * 100:0}%",
                stage => GeometryAndFlags(locale, dark, dpi, stage, proofDirectory));
    }

    internal static async Task ObserveMatrixActionAsync(string name, Action<Action<string>> action,
        Func<Action, TimeSpan, Task>? dispatch = null, Func<Task, Task>? observe = null, Action<string>? write = null, TimeProvider? observationTimeProvider = null,
        string prefix = "search-adjustment", string details = "320/480 DIP.", TimeSpan? executionTimeout = null)
    {
        var actionTimeout = executionTimeout ?? RegressionExecution.Timeout;
        var elapsed = Stopwatch.StartNew();
        var phases = new ConcurrentQueue<string>();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? actionFailure = null;
        long enteredAt = -1;
        void Stage(string phase) => phases.Enqueue($"{elapsed.ElapsedMilliseconds}ms thread={Environment.CurrentManagedThreadId}: {phase}");
        void Dump() { foreach (var phase in phases) (write ?? Console.WriteLine)($"[{prefix}/{name}] {phase}"); }
        Stage(FormattableString.Invariant($"queued with {(executionTimeout is null ? "shared" : "explicit")} {actionTimeout.TotalSeconds}s action deadline"));
        try
        {
            await (dispatch ?? ((callback, timeout) => RunOnStaThreadAsync(callback, timeout)))(() =>
            {
                Interlocked.Exchange(ref enteredAt, elapsed.ElapsedMilliseconds);
                Stage("entered");
                try { action(Stage); }
                catch (Exception error) { Volatile.Write(ref actionFailure, error); Stage($"action failed ({error.GetType().Name})"); throw; }
                finally { Stage("action completed"); completion.TrySetResult(); }
            }, actionTimeout);
            (write ?? Console.WriteLine)($"[{prefix}/{name}] completed in {elapsed.ElapsedMilliseconds}ms; entry={Volatile.Read(ref enteredAt)}ms; phases={phases.Count}; deadline={actionTimeout.TotalSeconds}s; {details}");
        }
        catch (TimeoutException error) when (!ReferenceEquals(error, Volatile.Read(ref actionFailure)))
        {
            Stage(Volatile.Read(ref enteredAt) < 0 ? "outer deadline before entry; observing same action off STA for at most 20s" : "outer deadline after entry; observing same action off STA for at most 20s");
            try
            {
                // Bound observation off the shared STA; never replace/cancel/retry its work.
                await BoundMatrixObservation(Task.Run(() => (observe ?? (pending => pending))(completion.Task)), observationTimeProvider).ConfigureAwait(false);
                Stage("same action observation completed; original deadline remains failed");
            }
            catch (Exception observationFailure) { Stage($"same action observation ended ({observationFailure.GetType().Name}); original deadline remains failed"); }
            Dump();
            throw;
        }
        catch
        {
            Dump();
            throw;
        }
    }

    private static Task BoundMatrixObservation(Task observation, TimeProvider? timeProvider = null) =>
        observation.WaitAsync(TimeSpan.FromSeconds(20), timeProvider ?? TimeProvider.System);

    internal static async Task MatrixDiagnosticsPreserveFailuresWithoutRetryAsync()
    {
        var outer = new TimeoutException("Synthetic outer deadline");
        Action? queued = null;
        var runs = 0;
        var observations = 0;
        Exception? caught = null;
        var output = new List<string>();
        TimeSpan? explicitBudget = null;
        try
        {
            await ObserveMatrixActionAsync("queued-fixture", stage => { runs++; stage("action body"); },
                dispatch: (callback, timeout) => { explicitBudget = timeout; queued = callback; throw outer; },
                observe: async completion => { observations++; queued!(); await completion; }, write: output.Add, executionTimeout: TimeSpan.FromSeconds(20));
        }
        catch (Exception error) { caught = error; }
        AssertEqual(true, ReferenceEquals(outer, caught), "Observing a queued action must preserve the original outer deadline exception");
        AssertEqual(1, observations, "An outer deadline must observe only its same queued action once");
        AssertEqual(1, runs, "Observation must execute that queued action once, without a replacement or retry");
        AssertEqual(true, output.Any(line => line.Contains("before entry", StringComparison.Ordinal)), "A pre-entry deadline must report its queue boundary");
        AssertEqual(true, output.Any(line => line.Contains("action body", StringComparison.Ordinal)), "Failure output must retain the recorded phases");
        AssertEqual<TimeSpan?>(TimeSpan.FromSeconds(20), explicitBudget, "The explicit action budget must reach the dispatch boundary");
        AssertEqual(true, output.Any(line => line.Contains("queued with explicit 20s action deadline", StringComparison.Ordinal)), "The queued diagnostic must describe the budget passed to dispatch");

        var inner = new TimeoutException("Synthetic action timeout");
        runs = 0; observations = 0; caught = null; output.Clear();
        try
        {
            await ObserveMatrixActionAsync("inner-fixture", _ => { runs++; throw inner; },
                dispatch: (callback, _) => { callback(); return Task.CompletedTask; },
                observe: _ => { observations++; return Task.CompletedTask; }, write: output.Add);
        }
        catch (Exception error) { caught = error; }
        AssertEqual(true, ReferenceEquals(inner, caught), "An action-owned timeout must remain the original failure");
        AssertEqual(0, observations, "An action-owned timeout must not be observed as an outer deadline");
        AssertEqual(1, runs, "An action-owned timeout must not run the action again");

        // Inspect the real helper's Task directly so an early expiry cannot hide
        // behind scheduling of the outer async catch/continuation.
        var exactClock = new ManualStreamingTimeProvider();
        var incomplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exactBound = BoundMatrixObservation(incomplete.Task, exactClock);
        exactClock.Advance(TimeSpan.FromMilliseconds(19999));
        AssertEqual(false, exactBound.IsCompleted, "The real observation bound stays incomplete at 19999ms");
        exactClock.Advance(TimeSpan.FromMilliseconds(1));
        AssertEqual(true, exactBound.IsCompleted, "The real observation bound expires at exactly 20000ms");
        try { await exactBound; throw new InvalidOperationException("The observation did not expire"); }
        catch (TimeoutException) { }
        AssertEqual(false, incomplete.Task.IsCompleted, "The bound never cancels or replaces its incomplete observation");
        incomplete.SetResult();

        caught = null; observations = 0; runs = 0; output.Clear();
        var dispatches = 0;
        var clock = new ManualStreamingTimeProvider();
        var observing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? sameCompletion = null;
        var bounded = ObserveMatrixActionAsync("observation-fixture", _ => runs++,
            dispatch: (callback, _) => { dispatches++; queued = callback; throw outer; },
            observe: completion => { sameCompletion = completion; observations++; observing.TrySetResult(); return completion; },
            write: output.Add, observationTimeProvider: clock);
        await observing.Task.WaitAsync(RegressionExecution.Timeout);
        clock.Advance(TimeSpan.FromMilliseconds(19999));
        AssertEqual(false, bounded.IsCompleted, "The real diagnostic observation remains pending at 19999ms");
        AssertEqual(false, sameCompletion!.IsCompleted, "The same queued action is still incomplete before expiration");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        try { await bounded.WaitAsync(RegressionExecution.Timeout); }
        catch (Exception error) { caught = error; }
        AssertEqual(true, ReferenceEquals(outer, caught), "The real 20000ms observation deadline must preserve original outer-error identity");
        AssertEqual(1, observations, "An incomplete action gets exactly one bounded observation");
        AssertEqual(1, dispatches, "Expiration never dispatches a replacement or retry");
        AssertEqual(0, runs, "Expiration does not execute or replace the queued action");
        AssertEqual(false, sameCompletion.IsCompleted, "Expiration does not cancel the original action/completion");
        AssertEqual(true, output.Any(line => line.Contains("observation ended (TimeoutException)", StringComparison.Ordinal)), "The real bound records observation expiration");
        queued!();
        await sameCompletion.WaitAsync(RegressionExecution.Timeout);
        AssertEqual(1, runs, "The original action can still complete exactly once after diagnostic expiration");

        output.Clear();
        TimeSpan? defaultBudget = null;
        await ObserveMatrixActionAsync("success-fixture", stage => stage("buffered private phase"),
            dispatch: (callback, timeout) => { defaultBudget = timeout; callback(); return Task.CompletedTask; }, write: output.Add);
        AssertEqual(1, output.Count, "A successful action emits one compact line");
        AssertEqual(false, output[0].Contains("buffered private phase", StringComparison.Ordinal), "Successful output keeps the detailed phase buffer private");
        AssertEqual<TimeSpan?>(TimeSpan.FromSeconds(20), defaultBudget, "An omitted action budget must use the shared execution dispatch boundary");
        AssertEqual(true, output[0].Contains("deadline=20s", StringComparison.Ordinal), "Matrix success diagnostics describe the actual shared dispatch guard");
    }

    private static async Task ObserveViewportActionAsync(Func<Action<string>, Task> action,
        Func<Func<Task>, TimeSpan, Task>? dispatch = null, Func<Task, Task>? observe = null,
        Action<string>? write = null, TimeProvider? observationTimeProvider = null)
    {
        var executionTimeout = RegressionExecution.Timeout;
        var elapsed = Stopwatch.StartNew();
        var phases = new ConcurrentQueue<string>();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? actionFailure = null;
        long enteredAt = -1;
        var phaseAttempts = 0;
        void Stage(string phase)
        {
            try
            {
                if (Interlocked.Increment(ref phaseAttempts) <= 256)
                    phases.Enqueue($"{elapsed.ElapsedMilliseconds}ms thread={Environment.CurrentManagedThreadId}: {phase}");
            }
            catch { /* Phase reporting cannot replace the action outcome. */ }
        }
        void Write(string text) { try { (write ?? Console.WriteLine)(text); } catch { /* Preserve the primary outcome. */ } }
        async Task EmitAsync(bool failed)
        {
            try
            {
                await Task.Run(() =>
                {
                    if (failed)
                        foreach (var phase in phases) Write($"[search-viewport] {phase}");
                    else
                        Write(FormattableString.Invariant($"[search-viewport] completed in {elapsed.ElapsedMilliseconds}ms; entry={Volatile.Read(ref enteredAt)}ms; phases={phases.Count}; execution={executionTimeout.TotalSeconds}s; complete same-source viewport chain."));
                }).ConfigureAwait(false);
            }
            catch { /* Observation and reporting must preserve the primary exception. */ }
        }
        Stage(FormattableString.Invariant($"queued with shared {executionTimeout.TotalSeconds}s async action deadline"));
        Func<Task> callback = async () =>
        {
            Interlocked.Exchange(ref enteredAt, elapsed.ElapsedMilliseconds);
            Stage("entered");
            try { await action(Stage); }
            catch (Exception error) { Volatile.Write(ref actionFailure, error); Stage($"action failed ({error.GetType().Name})"); throw; }
            finally
            {
                Stage("action completed after original source and VM cleanup/unwind");
                completion.TrySetResult();
            }
        };
        try
        {
            await (dispatch ?? ((body, timeout) => RunOnStaThreadAsync(body, timeout)))(callback, executionTimeout).ConfigureAwait(false);
            await EmitAsync(false).ConfigureAwait(false);
        }
        catch (TimeoutException error) when (!ReferenceEquals(error, Volatile.Read(ref actionFailure)))
        {
            Stage(Volatile.Read(ref enteredAt) < 0 ? "outer deadline before entry; observing same action off STA for at most 20s" : "outer deadline after entry; observing same action off STA for at most 20s");
            try
            {
                await BoundMatrixObservation(Task.Run(() => (observe ?? (pending => pending))(completion.Task)), observationTimeProvider).ConfigureAwait(false);
                var eventual = Volatile.Read(ref actionFailure);
                Stage(eventual is null ? "same action completed after cleanup; original deadline remains failed" : $"same action failed after cleanup ({eventual.GetType().Name}); original deadline remains failed");
            }
            catch (Exception observationFailure) { Stage($"same action observation ended ({observationFailure.GetType().Name}); original deadline remains failed"); }
            await EmitAsync(true).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await EmitAsync(true).ConfigureAwait(false);
            throw;
        }
    }

    internal static async Task ViewportDiagnosticsPreserveAsyncCompletionAndFailuresAsync()
    {
        var output = new List<string>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TimeSpan? budget = null;
        var runs = 0;
        var cleanup = false;
        var pending = ObserveViewportActionAsync(async stage =>
        {
            runs++; stage("private held phase"); entered.SetResult();
            try { await release.Task; }
            finally { cleanup = true; }
        }, dispatch: (callback, timeout) => { budget = timeout; return callback(); }, write: output.Add);
        await entered.Task.WaitAsync(RegressionExecution.Timeout);
        AssertEqual(false, pending.IsCompleted, "The async viewport observer cannot accept an action before its await completes");
        AssertEqual(0, output.Count, "An incomplete action cannot publish a success summary");
        release.SetResult();
        await pending.WaitAsync(RegressionExecution.Timeout);
        AssertEqual(true, cleanup, "Successful completion follows the complete action's cleanup");
        AssertEqual(1, runs, "An async action executes exactly once");
        AssertEqual<TimeSpan?>(TimeSpan.FromSeconds(20), budget, "The shared execution budget reaches injected async dispatch");
        AssertEqual(1, output.Count, "Success emits one compact summary");
        AssertEqual(true, output[0].Contains("execution=20s", StringComparison.Ordinal), "Success diagnostics use the same dispatched duration");
        AssertEqual(false, output[0].Contains("private held phase", StringComparison.Ordinal), "Success does not dump buffered private phases");

        var inner = new TimeoutException("Synthetic action-owned timeout");
        var outer = new TimeoutException("Synthetic outer deadline");
        Exception? caught = null;
        var observations = 0;
        output.Clear();
        try
        {
            await ObserveViewportActionAsync(async _ => { await Task.Yield(); throw inner; },
                dispatch: (callback, _) => callback(), observe: _ => { observations++; return Task.CompletedTask; }, write: _ => throw new IOException("Synthetic reporting failure"));
        }
        catch (Exception error) { caught = error; }
        AssertEqual(true, ReferenceEquals(inner, caught), "An async action-owned timeout retains identity even if failure reporting throws");
        AssertEqual(0, observations, "An action-owned timeout must bypass outer-deadline observation");

        Func<Task>? queued = null;
        var dispatches = 0;
        runs = 0; observations = 0; caught = null; cleanup = false; output.Clear();
        var cleanupObserved = false;
        try
        {
            await ObserveViewportActionAsync(async stage => { runs++; try { await Task.Yield(); stage("late original action"); } finally { cleanup = true; } },
                dispatch: (callback, timeout) => { dispatches++; budget = timeout; queued = callback; throw outer; },
                observe: async completion => { observations++; await queued!(); await completion; cleanupObserved = cleanup; }, write: output.Add);
        }
        catch (Exception error) { caught = error; }
        AssertEqual(true, ReferenceEquals(outer, caught), "Late original completion cannot turn an outer deadline into success");
        AssertEqual(true, cleanupObserved, "Same-action completion is published after cleanup");
        AssertEqual(1, dispatches, "Late completion must not dispatch replacement work");
        AssertEqual(1, runs, "Only the same queued callback may run");
        AssertEqual(1, observations, "An outer timeout observes its one original completion");
        AssertEqual<TimeSpan?>(TimeSpan.FromSeconds(20), budget, "Failure dispatch receives the shared execution budget");
        AssertEqual(true, output.Any(line => line.Contains("queued with shared 20s async action deadline", StringComparison.Ordinal)), "Failure diagnostics describe the dispatched duration");

        var secondary = new IOException("Synthetic observation failure");
        caught = null; observations = 0; output.Clear();
        try
        {
            await ObserveViewportActionAsync(_ => Task.CompletedTask,
                dispatch: (callback, _) => { queued = callback; throw outer; },
                observe: _ => { observations++; throw secondary; }, write: output.Add);
        }
        catch (Exception error) { caught = error; }
        AssertEqual(true, ReferenceEquals(outer, caught), "A secondary observation fault cannot replace the primary deadline");
        AssertEqual(1, observations, "A secondary failure does not retry observation");
        await queued!();

        var clock = new ObservationRegistrationClock();
        var observing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? sameCompletion = null;
        dispatches = 0; runs = 0; observations = 0; caught = null; output.Clear();
        var bounded = ObserveViewportActionAsync(_ => { runs++; return Task.CompletedTask; },
            dispatch: (callback, _) => { dispatches++; queued = callback; throw outer; },
            observe: completion => { sameCompletion = completion; observations++; observing.SetResult(); return completion; },
            write: output.Add, observationTimeProvider: clock);
        await observing.Task.WaitAsync(RegressionExecution.Timeout);
        await clock.TimerCreated.Task.WaitAsync(RegressionExecution.Timeout);
        clock.Advance(TimeSpan.FromMilliseconds(19999));
        AssertEqual(false, bounded.IsCompleted, "The same-action observer cannot expire before 20000ms");
        AssertEqual(false, sameCompletion!.IsCompleted, "The original callback is still incomplete at 19999ms");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        try { await bounded.WaitAsync(RegressionExecution.Timeout); }
        catch (Exception error) { caught = error; }
        AssertEqual(true, ReferenceEquals(outer, caught), "Observation expiry preserves the original deadline identity");
        AssertEqual(1, dispatches, "Observation expiry must not dispatch a replacement or retry");
        AssertEqual(1, observations, "Observation expires after its single bounded attempt");
        AssertEqual(0, runs, "Expiry does not run queued replacement work");
        AssertEqual(false, sameCompletion.IsCompleted, "Observation expiry cannot cancel or complete the original action");
        AssertEqual(true, output.Any(line => line.Contains("observation ended (TimeoutException)", StringComparison.Ordinal)), "The actual observation primitive reports exact expiry");
        await queued!();
        await sameCompletion.WaitAsync(RegressionExecution.Timeout);
        AssertEqual(1, runs, "Only the original action completes after expiry");
    }

    private sealed class ObservationRegistrationClock : TimeProvider
    {
        private readonly ManualStreamingTimeProvider _clock = new();
        internal TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override long TimestampFrequency => _clock.TimestampFrequency;
        public override long GetTimestamp() => _clock.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => _clock.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = _clock.CreateTimer(callback, state, dueTime, period);
            TimerCreated.TrySetResult();
            return timer;
        }
        internal void Advance(TimeSpan elapsed) => _clock.Advance(elapsed);
    }

    private static FrameworkElement Control(SettingsView view)
    {
        var control = view.FindName("SearchAdjustments") as FrameworkElement;
        AssertNotNull(control, "The compiled search row must embed the search adjustment action");
        return control!;
    }

    private static void PopupMaterialUsesItsOwnNonlayeredSource()
    {
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        using var ownerSource = Attach(view);
        var control = Control(view);
        Open(control);
        var surface = Part<Border>(control, "PopupSurface");
        var popupSource = (HwndSource)PresentationSource.FromVisual(surface);
        AssertEqual(false, ReferenceEquals(ownerSource, popupSource), "Material must target the exact Popup source, never the host source");
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            AssertEqual(false, popupSource.UsesPerPixelOpacity, "The supported Acrylic compatibility path must create a nonlayered Popup before applying DWM material");
        var material = ((SearchAdjustmentControl)control).Material;
        Console.WriteLine($"[popup-material/search] hwnd=0x{popupSource.Handle:X}; owner=0x{ownerSource.Handle:X}; layered={popupSource.UsesPerPixelOpacity}; frameHR=0x{material.Result.FrameHResult:X8}; readHR=0x{material.Result.ReadbackHResult:X8}; type={material.Result.BackdropType}; accepted={material.AttributeAccepted}; visibleBlur=UNVERIFIED");
        AssertEqual(material.AttributeAccepted, surface.Background is SolidColorBrush { Color.A: 0 }, "Only a successful exact-HWND frame/readback enables a transparent WPF surface");
        Click(control, "CloseButton");
        AssertEqual(IntPtr.Zero, material.Handle, "Close retires the owned popup source");
    }

    private static async Task RunMaterialFallbackAsync()
    {
        foreach (var supported in new[] { false, true })
        foreach (var result in new[] { new PopupBackdropResult(0, unchecked((int)0x80004005), 3), new PopupBackdropResult(0, 0, 2), new PopupBackdropResult(unchecked((int)0x80004005), 0, 3) })
            await RunOnStaThreadAsync(() => PopupMaterialFallbackAndSourceLifetimes(supported, result));
    }

    private static void PopupMaterialFallbackAndSourceLifetimes(bool supported, PopupBackdropResult result)
    {
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        using var ownerSource = Attach(view);
        var surface = new Border { Width = 140, Height = 100, CornerRadius = new CornerRadius(8) };
        var popup = new Popup { PlacementTarget = Control(view), Child = surface, StaysOpen = true };
        var api = new TestPopupBackdropApi { IsSupported = supported, Result = result };
        var material = new PopupMaterial(popup, surface, api);
        material.Begin(); popup.IsOpen = true; Drain();
        var first = (HwndSource)PresentationSource.FromVisual(surface);
        AssertEqual(!supported, first.UsesPerPixelOpacity, "Capability selects the source configuration before creation");
        AssertEqual(false, material.AttributeAccepted, "Failed HRESULT or a different returned material selects explicit opaque fallback");
        AssertEqual(true, surface.Background is SolidColorBrush { Color.A: 255 }, "Fallback resolves the host's opaque native flyout brush");
        AssertEqual(supported ? 1 : 0, api.Applied.Count, "Unsupported platforms never attempt native material");
        AssertEqual(false, api.Applied.Contains(ownerSource.Handle), "No fallback or application may target the owner HWND");
        popup.IsOpen = false; Drain();
        AssertEqual(IntPtr.Zero, material.Handle, "Closed popup retires material ownership");
        var calls = api.Applied.Count;
        surface.Resources["FlyoutPresenterBackground"] = Brushes.Red; Drain();
        AssertEqual(calls, api.Applied.Count, "Retired theme callbacks cannot apply to stale sources");
        material.Begin(); popup.IsOpen = true; Drain();
        var second = (HwndSource)PresentationSource.FromVisual(surface);
        AssertEqual(false, ReferenceEquals(first, second), "Reopen reacquires the newly created Popup source");
        popup.IsOpen = false; material.End(); Drain();
    }

    private sealed class TestPopupBackdropApi : IPopupBackdropApi
    {
        public bool IsSupported { get; init; }
        internal PopupBackdropResult Result { get; init; }
        internal List<IntPtr> Applied { get; } = [];
        public PopupBackdropResult Apply(IntPtr handle, bool dark) { Applied.Add(handle); return Result; }
        public void UpdateChrome(IntPtr handle, bool dark) { }
        public void Remove(IntPtr handle) { }
    }
    private static T Part<T>(FrameworkElement control, string name) where T : class => (T)control.FindName(name);
    private static void Click(FrameworkElement control, string name) => Part<Button>(control, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Open(FrameworkElement control) { Click(control, "AdjustmentButton"); Drain(); }
    private static bool IsOpen(FrameworkElement control) => Part<Popup>(control, "AdjustmentPopup").IsOpen;
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    private static HwndSource Attach(SettingsView view, int width = 480)
    {
        var screenDpi = VisualTreeHelper.GetDpi(new DrawingVisual());
        var source = new HwndSource(new HwndSourceParameters("Fish Audio search adjustment fixture")
        {
            Width = (int)Math.Ceiling(width * screenDpi.DpiScaleX),
            Height = (int)Math.Ceiling(1600 * screenDpi.DpiScaleY),
            WindowStyle = unchecked((int)0x80000000), PositionX = 120, PositionY = 120,
        });
        source.RootVisual = view;
        SettingsLayoutSuite.Layout(view, width);
        return source;
    }

    private static async Task CaptionsAndKeyboardApplyAsync()
    {
        var settings = new Settings();
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = """{"total":0,"items":[]}""";
        using var vm = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        using var source = Attach(view);
        var control = Control(view);
        Open(control);
        AssertEqual("Relevance first", Part<RadioButton>(control, "RelevanceRadio").Content as string,
            "Each native sort choice presents only its readable title without helper descriptions");
        AssertEqual("Most used first", Part<RadioButton>(control, "PopularityRadio").Content as string, "Popularity retains its rich title");
        AssertEqual("Newest first", Part<RadioButton>(control, "CreationRadio").Content as string, "Creation order retains its rich title");
        AssertNotNull(control.FindName("LanguageTiles"), "Language options must be eleven equal toggle tiles");
        var tile = Tiles(control).Single(item => (string)item.Tag == "en");
        foreach (var key in new[] { Key.Enter, Key.Space })
        {
            tile.Focus();
            var previous = tile.IsChecked;
            LocalKey(tile, key); Drain();
            AssertEqual(previous != true, tile.IsChecked, "Enter and Space keep native local toggle behavior");
            AssertEqual(true, IsOpen(control), "Tile keyboard activation preserves the popup");
            AssertEqual(0, http.GetCallCount, "Tile keyboard activation cannot invoke Apply");
        }
        Part<RadioButton>(control, "PopularityRadio").Focus();
        LocalKey(Part<RadioButton>(control, "PopularityRadio"), Key.Enter); Drain();
        AssertEqual(true, IsOpen(control), "Native radio Enter stays local without Apply");
        var apply = Part<Button>(control, "ApplyButton");
        apply.Focus(); LocalKey(apply, Key.Enter); Drain();
        AssertEqual(false, IsOpen(control), "Explicit keyboard Apply closes search adjustments");
        AssertEqual(1, http.GetCallCount, "Keyboard Apply invokes exactly one page-one GET");
        await vm.SearchVoicesCommand.ExecuteAsync(null);
        AssertEqual(2, http.GetCallCount, "Explicit Search remains independent of popup keyboard Apply");
    }

    // This hidden HWND cannot acquire OS keyboard focus. Exercise native class
    // handlers with an explicit focus-state fixture; physical Tab is host acceptance.
    private static void LocalKey(FrameworkElement target, Key key)
    {
        var focusKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsKeyboardFocusedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var previous = target.IsKeyboardFocused;
        target.SetValue(focusKey, true);
        try
        {
            if (key == Key.Space)
                NativeKeyboardStateFixture.Run((HwndSource)PresentationSource.FromVisual(target)!, RaiseEvents);
            else RaiseEvents();

            void RaiseEvents()
            {
                foreach (var routed in new[] { Keyboard.PreviewKeyDownEvent, Keyboard.KeyDownEvent, Keyboard.PreviewKeyUpEvent, Keyboard.KeyUpEvent })
                {
                    var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, Environment.TickCount, key)
                    { RoutedEvent = routed, Source = target };
                    target.RaiseEvent(args);
                }
            }
        }
        finally { target.SetValue(focusKey, previous); }
    }

    private static ToggleButton[] Tiles(FrameworkElement control) => Part<Grid>(control, "LanguageTiles").Children.OfType<ToggleButton>().ToArray();
    private static void Toggle(FrameworkElement control, string code)
    {
        var tile = Tiles(control).Single(item => (string)item.Tag == code);
        var peer = UIElementAutomationPeer.CreatePeerForElement(tile)!;
        ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)!).Toggle();
        Drain();
    }
    private static string[] Selected(FrameworkElement control) => Tiles(control).Where(tile => tile.IsChecked == true).Select(tile => (string)tile.Tag).ToArray();

    // Draft controls must have no network side effects. Apply must capture the
    // current TextBox once, including an unchanged refresh and keyboard activation.
    private static async Task DraftsApplyAndTileInputAsync()
    {
        var settings = new Settings();
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = """{"total":24,"items":[]}""";
        using var vm = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        using var source = Attach(view);
        var control = Control(view);
        Open(control);
        AssertEqual(true, IsOpen(control), "The embedded action opens a native popup");
        AssertEqual(11, Tiles(control).Length, "All and ten approved languages use equal tiles");
        AssertEnumerableEqual(new[] { "" }, Selected(control), "All alone is the default");
        Toggle(control, "ja"); Toggle(control, "en");
        AssertEnumerableEqual(new[] { "en", "ja" }, Selected(control), "Specific languages toggle independently and exclude All");
        Toggle(control, "en"); Toggle(control, "ja");
        AssertEnumerableEqual(new[] { "" }, Selected(control), "Clearing the final specific restores All");
        foreach (var code in new[] { "en", "zh", "de", "ja", "fr", "es", "ko", "ar", "ru", "pt" }) Toggle(control, code);
        AssertEqual(10, Selected(control).Length, "Selecting all ten specifics remains filtered");
        AssertEqual(false, Tiles(control)[0].IsChecked, "All ten does not select All");
        Toggle(control, "");
        AssertEnumerableEqual(new[] { "" }, Selected(control), "All clears every specific");
        Toggle(control, "ja");
        Part<RadioButton>(control, "CreationRadio").IsChecked = true;
        AssertEqual(0, http.GetCallCount, "Draft language/sort edits never call the provider");
        Click(control, "ResetButton");
        AssertEnumerableEqual(new[] { "" }, Selected(control), "Reset drafts All");
        AssertEqual(true, Part<RadioButton>(control, "RelevanceRadio").IsChecked, "Reset drafts relevance");
        AssertEqual(0, http.GetCallCount, "Reset never calls the provider");
        Click(control, "CloseButton");
        AssertEqual(false, IsOpen(control), "Close dismisses without applying");
        AssertEqual(0, http.GetCallCount, "Close discards drafts without a request");
        Open(control);
        AssertEnumerableEqual(new[] { "" }, Selected(control), "Reopening discards the former draft");
        Toggle(control, "ja"); Toggle(control, "en");
        Part<RadioButton>(control, "PopularityRadio").IsChecked = true;
        var input = (TextBox)view.FindName("VoiceSearchInput");
        input.Text = "visible & query";
        Drain();
        Click(control, "ApplyButton");
        Drain();
        AssertEqual(false, IsOpen(control), "Apply closes immediately");
        AssertEqual(1, http.GetCallCount, "Apply invokes exactly one request");
        AssertEqual("https://api.fish.audio/model?page_size=6&page_number=1&sort_by=task_count&title=visible%20%26%20query&language=en&language=ja", http.LastGetUrl, "Apply captures visible text and current drafts");
        Open(control);
        AssertEnumerableEqual(new[] { "en", "ja" }, Selected(control), "Reopening starts from successful selections");
        var surface = Part<Border>(control, "PopupSurface");
        var scroll = Part<ScrollViewer>(control, "PopupScroll");
        scroll.ScrollToVerticalOffset(50); Drain();
        AssertEqual(true, IsOpen(control), "Internal body scrolling preserves adjustment drafts");
        Toggle(control, "ru");
        var key = Escape(surface);
        AssertEqual(true, key.Handled, "Escape is consumed by the adjustment overlay");
        AssertEqual(false, IsOpen(control), "Escape closes adjustments directly");
        AssertEqual(true, Part<Button>(control, "AdjustmentButton").IsKeyboardFocused, "Escape returns focus to the embedded trigger");
        AssertEqual(1, http.GetCallCount, "Escape discards drafts without another GET");
        Open(control);
        Click(control, "ApplyButton"); Drain();
        AssertEqual(2, http.GetCallCount, "Unchanged compiled Apply still refreshes exactly once");
        input.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent)); Drain();
        AssertEqual(2, http.GetCallCount, "Blur after Apply cannot issue a duplicate search");
        await vm.SearchVoicesCommand.ExecuteAsync(null);
        AssertEqual(3, http.GetCallCount, "The public Search command remains usable with applied options");
    }

    // The per-view token prevents competing overlays, stale releases and leaked
    // input subscriptions after tab/rebind/unload/ancestor-scroll retirement.
    private static void OverlayOwnershipAndLifetime()
    {
        var settings = new Settings { VoiceId = AppliedKey, CachedVoice = new() { Title = "Voice", Languages = new[] { "en", "zh", "de", "ja", "fr", "es", "ko", "ar", "ru", "pt" } } };
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        using var source = Attach(view, 400);
        var control = Control(view);
        var tags = Visuals(view).OfType<VoiceLanguageTags>().First();
        tags.OpenPopup();
        AssertEqual(true, tags.IsPopupOpen, "The selected card supplies a real overflow overlay");
        Open(control);
        AssertEqual(false, tags.IsPopupOpen, "Search adjustments retire language overflow");
        tags.OpenPopup();
        AssertEqual(false, IsOpen(control), "Language overflow retires search adjustments");
        tags.ClosePopup();
        Open(control);
        vm.BrowseMode = VoiceBrowseMode.ById; Drain();
        AssertEqual(false, IsOpen(control), "Changing browse tabs closes the search overlay");
        vm.BrowseMode = VoiceBrowseMode.Search; Drain();
        Open(control);
        control.DataContext = new object();
        AssertEqual(false, IsOpen(control), "Rebinding closes the overlay and retires subscriptions");
        control.DataContext = vm;
        Open(control);
        control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        AssertEqual(false, IsOpen(control), "Unloading closes the overlay");
        Open(control);
        var outside = (TextBox)view.FindName("VoiceSearchInput");
        InputManager.Current.ProcessInput(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = outside });
        AssertEqual(false, IsOpen(control), "Outside presses close the overlay");
    }

    private static void WindowAndScrollLifetime()
    {
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        var scroll = new ScrollViewer { Content = view, Height = 320 };
        var window = new Window { Content = scroll, Width = 480, Height = 340, Left = 120, Top = 120, Opacity = 0,
            ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
        try
        {
            window.Show(); Drain();
            var control = Control(view);
            control.BringIntoView(); Drain();
            Open(control);
            AssertEqual(true, IsOpen(control), "A compiled Window owns the adjustment popup");
            window.Left += 10; window.Width = 400; Drain();
            AssertEqual(true, IsOpen(control), "Moving/resizing the native owner repositions without dropping ownership");
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 50); Drain();
            AssertEqual(false, IsOpen(control), "Ancestor page scrolling retires the overlay");
            control.BringIntoView(); Drain(); Open(control);
            window.Hide(); Drain();
            AssertEqual(false, IsOpen(control), "Hiding the owner closes the popup");
            window.Show(); Drain(); Open(control);
            typeof(Window).GetMethod("OnDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [EventArgs.Empty]);
            AssertEqual(false, IsOpen(control), "Window deactivation retires input subscriptions and the native child");
        }
        finally { window.Close(); }
    }

    // Monitor-only Y clamping lets an options popup extend below a short host
    // window. Both anchor edges must fit the actually visible ancestor viewport.
    private static void ShortViewportKeepsPopupInsideVisibleArea(string? proofDirectory = null)
    {
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        // Same labeled headless accent fixture as the locale matrix; the host
        // supplies these native resources in actual installed-window acceptance.
        view.Resources["AccentButtonBackground"] = new SolidColorBrush(Color.FromRgb(0x00, 0x5F, 0xB8));
        view.Resources["AccentButtonForeground"] = Brushes.White;
        var scroll = new ScrollViewer { Content = view, Height = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var dpi = VisualTreeHelper.GetDpi(new DrawingVisual());
        using var source = new HwndSource(new HwndSourceParameters("Fish Audio short visible popup viewport fixture")
        { Width = (int)(400 * dpi.DpiScaleX), Height = (int)(240 * dpi.DpiScaleY), WindowStyle = unchecked((int)0x80000000), PositionX = 120, PositionY = 120 });
        source.RootVisual = scroll;
        SettingsLayoutSuite.Layout(scroll, 400);
        var control = Control(view);
        var naturalTop = control.TransformToAncestor(view).Transform(new Point()).Y;
        foreach (var nearBottom in new[] { true, false })
        {
            scroll.ScrollToVerticalOffset(Math.Max(0, naturalTop - (nearBottom ? 190 : 12)));
            Drain(); Open(control);
            var surface = Part<Border>(control, "PopupSurface");
            var presenter = Visuals(scroll).OfType<ScrollContentPresenter>().First();
            var viewportTop = presenter.PointToScreen(new Point()).Y;
            var viewportBottom = presenter.PointToScreen(new Point(0, presenter.ActualHeight)).Y;
            var popupTop = surface.PointToScreen(new Point()).Y;
            var popupBottom = surface.PointToScreen(new Point(0, surface.ActualHeight)).Y;
            AssertEqual(true, popupTop >= viewportTop + 7 * dpi.DpiScaleY - 1 && popupBottom <= viewportBottom - 7 * dpi.DpiScaleY + 1,
                $"The popup remains inside the short visible viewport at both anchor edges ({nearBottom}: popup {popupTop}..{popupBottom}; viewport {viewportTop}..{viewportBottom})");
            var trigger = Part<Button>(control, "AdjustmentButton");
            var triggerTop = trigger.PointToScreen(new Point()).Y;
            var triggerBottom = trigger.PointToScreen(new Point(0, trigger.ActualHeight)).Y;
            AssertEqual(true, nearBottom ? popupBottom <= triggerTop - 7 * dpi.DpiScaleY + 1 : popupTop >= triggerBottom + 7 * dpi.DpiScaleY - 1,
                "A short viewport chooses the available side of the trigger with a visible gap");
            var contentScroll = Part<ScrollViewer>(control, "PopupScroll");
            AssertEqual(true, contentScroll.ExtentHeight > contentScroll.ViewportHeight, "A popup taller than available space scrolls internally");
            AssertScrollbarExclusion(contentScroll, Tiles(control), "search language tiles");
            foreach (var name in new[] { "CloseButton", "ResetButton", "ApplyButton" })
            {
                var action = Part<Button>(control, name);
                var actionBounds = action.TransformToAncestor(surface).TransformBounds(new Rect(action.RenderSize));
                AssertEqual(true, actionBounds.Top >= 0 && actionBounds.Bottom <= surface.ActualHeight, "Header/footer actions stay reachable outside the scrolling body");
            }
            Tiles(control).Last().BringIntoView(); Drain();
            AssertEqual(true, IsOpen(control), "Bringing a clipped tile into view scrolls only the internal body");
            AssertEqual(true, contentScroll.VerticalOffset > 0, "Keyboard/focus BringIntoView can reach the final language row");
            var oldBar = (ScrollBar)contentScroll.Template.FindName("PART_VerticalScrollBar", contentScroll);
            var oldLane = contentScroll.Padding.Right;
            oldBar.Width = oldBar.ActualWidth + 8; Drain();
            AssertScrollbarExclusion(contentScroll, Tiles(control), "expanded replacement width");
            AssertEqual(true, contentScroll.Padding.Right > oldLane, "A wider actual scrollbar increases the owned lane");
            var template = contentScroll.Template;
            contentScroll.Template = null; contentScroll.UpdateLayout();
            contentScroll.Template = template; contentScroll.UpdateLayout(); Drain();
            AssertEqual(false, ReferenceEquals(oldBar, contentScroll.Template.FindName("PART_VerticalScrollBar", contentScroll)), "Template replacement realizes a new scrollbar part");
            AssertScrollbarExclusion(contentScroll, Tiles(control), "replacement native template");
            SavePopupProof(surface, $"short-viewport-{(nearBottom ? "above" : "below")}-native{dpi.DpiScaleX * 100:0}", proofDirectory);
            Click(control, "CloseButton");
            AssertEqual(0d, contentScroll.Padding.Right, "Close detaches and restores the viewer's original padding");
        }
    }

    internal static void AssertScrollbarExclusion(ScrollViewer viewer, IEnumerable<FrameworkElement> content, string context)
    {
        viewer.ApplyTemplate();
        var bar = (ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar", viewer);
        AssertEqual(true, VisualStateManager.GoToState(viewer, "MouseIndicator", false), "The real native viewer exposes its mouse-indicator state");
        AssertEqual(true, VisualStateManager.GoToState(bar, "Expanded", false), "The real native scrollbar expands through its installed template");
        viewer.UpdateLayout();
        AssertEqual(Visibility.Visible, bar.Visibility, $"{context}: real overflow exposes the native scrollbar part");
        var bounds = bar.TransformToAncestor(viewer).TransformBounds(new Rect(bar.RenderSize));
        AssertEqual(true, bounds.Left >= 0 && bounds.Right <= viewer.ActualWidth + .1, $"{context}: native scrollbar stays in the viewer");
        foreach (var item in content)
        {
            var itemBounds = item.TransformToAncestor(viewer).TransformBounds(new Rect(item.RenderSize));
            AssertEqual(true, itemBounds.Right <= bounds.Left - 3.9,
                $"{context}: selectable/read-only content ends four DIP before the ACTUAL expanded scrollbar ({itemBounds.Right:0.##} <= {bounds.Left - 4:0.##}; part width {bounds.Width:0.##})");
        }
        var laneHit = viewer.InputHitTest(new Point(bounds.Left - 2, viewer.ActualHeight / 2)) as DependencyObject;
        AssertEqual(false, laneHit is not null && AncestorsAndSelf(laneHit).OfType<ToggleButton>().Any(),
            $"{context}: the exclusion lane has no selectable tile behind it");
    }

    private static IEnumerable<DependencyObject> AncestorsAndSelf(DependencyObject item)
    {
        for (DependencyObject? current = item; current is not null; current = VisualTreeHelper.GetParent(current)) yield return current;
    }

    private static async Task ViewportClampsAcrossTriggerOrRefuses(Action<string> stage)
    {
        stage("fake search setup begin");
        var settings = new Settings();
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = """{"total":24,"items":[{"_id":"0123456789abcdef0123456789abcdef","title":"Retained voice","type":"tts","samples":[]}]}""";
        stage("viewmodel construction begin");
        using var vm = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings);
        stage("viewmodel construction end");
        vm.SearchQuery = "retained query";
        stage("fake search apply begin");
        await vm.ApplySearchOptionsAsync(["en"], "task_count");
        stage("fake search apply end");
        var successfulResults = vm.SearchResults;
        var requestCount = http.GetCallCount;
        stage("successful search baselines captured");
        stage("compiled view construction begin");
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        stage("compiled view construction end");
        var scroll = new ScrollViewer { Content = view, Height = 220 };
        stage("native source construction begin");
        using var source = new HwndSource(new HwndSourceParameters("Fish Audio centered short popup fixture")
        { Width = 400, Height = 300, WindowStyle = unchecked((int)0x80000000), PositionX = 120, PositionY = 120 });
        stage("native source construction end");
        stage("source attach begin");
        source.RootVisual = scroll;
        stage("source attach end");
        stage("initial source layout begin");
        SettingsLayoutSuite.Layout(scroll, 400);
        stage("initial source layout end");
        var control = Control(view);
        stage("initial scroll and original drain begin");
        scroll.ScrollToVerticalOffset(control.TransformToAncestor(view).Transform(new Point()).Y - 96); Drain();
        stage("initial scroll and original drain end");
        stage("search/centered vertical open" + " begin");
        Open(control);
        stage("search/centered vertical open" + " end");
        AssertEqual(true, IsOpen(control), "A centered trigger can use the complete visible viewport when neither side fits fixed chrome and an option");
        var surface = Part<Border>(control, "PopupSurface");
        var trigger = Part<Button>(control, "AdjustmentButton");
        AssertEqual(true, surface.PointToScreen(new Point()).Y < trigger.PointToScreen(new Point()).Y
            && surface.PointToScreen(new Point(0, surface.ActualHeight)).Y > trigger.PointToScreen(new Point(0, trigger.ActualHeight)).Y,
            "The useful constrained popup can clamp across the trigger");
        stage("search/setup" + "/fixed actions begin");
        AssertFixedActions(control, surface);
        stage("search/setup" + "/fixed actions end");
        stage("search/setup close" + " begin");
        Click(control, "CloseButton");
        stage("search/setup close" + " end");
        stage("height refusal layout and drain begin");
        scroll.Height = 70; SettingsLayoutSuite.Layout(scroll, 400); Drain();
        stage("height refusal layout and drain end");
        stage("search/height refusal open" + " begin");
        Open(control);
        stage("search/height refusal open" + " end");
        AssertEqual(false, IsOpen(control), "A viewport smaller than measured fixed chrome coherently refuses the popup");
        AssertEqual(false, (bool)control.GetType().GetField("_ownsInput", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!,
            "Refusal retires input and overlay ownership");

        scroll.Height = 280;
        stage("height refusal assertions complete");
        foreach (var (locale, dark) in new[] { ("en", false), ("ru", true) })
        {
            stage("search/" + locale + "/locale-theme begin");
            scroll.Width = 400;
            stage("search/" + locale + "/dictionary replace begin");
            SetLocale(view, locale);
            view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
            ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light);
            stage("search/" + locale + "/locale-theme replacement end");
            stage("search/" + locale + "/locale wide layout-drain" + " begin");
            SettingsLayoutSuite.Layout(scroll, 400); Drain();
            stage("search/" + locale + "/locale wide layout-drain" + " end");
            stage("search/" + locale + "/locale wide open" + " begin");
            Open(control);
            stage("search/" + locale + "/locale wide open" + " end");
            AssertEqual(true, IsOpen(control), "A sufficiently wide viewport opens with native localized actions");
            stage("search/" + locale + "/locale wide" + "/fixed actions begin");
            AssertFixedActions(control, surface);
            stage("search/" + locale + "/locale wide" + "/fixed actions end");
            stage("search/" + locale + "/locale wide" + "/fixed captions begin");
            AssertFixedCaptions(control, surface);
            stage("search/" + locale + "/locale wide" + "/fixed captions end");
            var reset = Part<Button>(control, "ResetButton");
            var apply = Part<Button>(control, "ApplyButton");
            // Independent boundary from the already rendered complete actions,
            // rather than the helper's unconstrained measurement or decision.
            var requiredWidth = reset.ActualWidth + apply.ActualWidth + apply.Margin.Left
                + surface.Padding.Left + surface.Padding.Right + surface.BorderThickness.Left + surface.BorderThickness.Right;
            var presenter = Visuals(scroll).OfType<ScrollContentPresenter>().First();
            var viewportOverhead = scroll.ActualWidth - presenter.ActualWidth + 16;
            stage("search/" + locale + "/draft changes begin");
            Toggle(control, "ja");
            Part<RadioButton>(control, "CreationRadio").IsChecked = true;
            stage("search/" + locale + "/draft changes end");
            stage("search/" + locale + "/draft changes complete; narrow begin");
            scroll.Width = requiredWidth - 2 + viewportOverhead;
            stage("search/" + locale + "/locale narrow layout-drain" + " begin");
            SettingsLayoutSuite.Layout(scroll, 400); Drain();
            stage("search/" + locale + "/locale narrow layout-drain" + " end");
            var rejectedWidth = (presenter.PointToScreen(new Point(presenter.ActualWidth, 0)).X - presenter.PointToScreen(new Point()).X)
                / PopupViewport.ScreenScale(control).DpiScaleX - 16;
            AssertEqual(false, IsOpen(control), "Narrowing an open viewport below its complete native footer width closes the popup");
            stage("search/" + locale + "/locale narrow retirement" + " begin");
            AssertHorizontalRetirement(control, view);
            stage("search/" + locale + "/locale narrow retirement" + " complete");
            stage("search/" + locale + "/locale narrow open" + " begin");
            Open(control);
            stage("search/" + locale + "/locale narrow open" + " end");
            AssertEqual(false, IsOpen(control), "A tall viewport narrower than localized fixed actions refuses reopening");
            stage("search/" + locale + "/locale narrow retirement" + " begin");
            AssertHorizontalRetirement(control, view);
            stage("search/" + locale + "/locale narrow retirement" + " complete");
            if (locale == "ru")
            {
                stage("search/ru/original 150 width begin");
                scroll.Width = 150;
                stage("search/ru refusal layout-drain" + " begin");
                SettingsLayoutSuite.Layout(scroll, 400); Drain();
                stage("search/ru refusal layout-drain" + " end");
                foreach (var attempt in new[] { 1, 2 })
                {
                    stage("search/ru/refusal attempt " + attempt + "/open" + " begin");
                    Open(control);
                    stage("search/ru/refusal attempt " + attempt + "/open" + " end");
                    AssertEqual(false, IsOpen(control), $"The original 150-DIP Russian narrow viewport refuses open {attempt}");
                    stage("search/ru/original refusal retirement" + " begin");
                    AssertHorizontalRetirement(control, view);
                    stage("search/ru/original refusal retirement" + " complete");
                }
            }
            stage("search/" + locale + "/width retirement invariants begin");
            AssertEqual(requestCount, http.GetCallCount, "Width retirement and refused reopening issue no GET");
            AssertEqual(true, ReferenceEquals(successfulResults, vm.SearchResults), "Width refusal retains successful result objects");
            AssertEnumerableEqual(new[] { "en" }, vm.SearchLanguages, "Width retirement cannot apply language drafts");
            AssertEqual("task_count", vm.SearchSort, "Width retirement cannot apply sort drafts");
            AssertEqual(1, vm.SearchPage, "Width retirement preserves successful pagination");
            stage("search/" + locale + "/width retirement invariants complete");

            stage("search/" + locale + "/sufficient width restore begin");
            scroll.Width = requiredWidth + 2 + viewportOverhead;
            stage("search/" + locale + "/locale restored layout-drain-open" + " begin");
            SettingsLayoutSuite.Layout(scroll, 400); Drain(); Open(control);
            stage("search/" + locale + "/locale restored layout-drain-open" + " end");
            AssertEqual(true, IsOpen(control), "Restoring enough width reopens without a forced maximum-width floor");
            stage("search/" + locale + "/locale restored" + "/fixed actions begin");
            AssertFixedActions(control, surface);
            stage("search/" + locale + "/locale restored" + "/fixed actions end");
            stage("search/" + locale + "/locale restored" + "/fixed captions begin");
            AssertFixedCaptions(control, surface);
            stage("search/" + locale + "/locale restored" + "/fixed captions end");
            AssertEnumerableEqual(new[] { "en" }, Selected(control), "Reopen restores successful languages after discarded drafts");
            AssertEqual(true, Part<RadioButton>(control, "PopularityRadio").IsChecked, "Reopen restores successful sort");
            var top = surface.PointToScreen(new Point());
            var bottom = surface.PointToScreen(new Point(surface.ActualWidth, surface.ActualHeight));
            var visibleTop = presenter.PointToScreen(new Point());
            var visibleBottom = presenter.PointToScreen(new Point(presenter.ActualWidth, presenter.ActualHeight));
            AssertEqual(true, top.X >= visibleTop.X && bottom.X <= visibleBottom.X + 1 && top.Y >= visibleTop.Y && bottom.Y <= visibleBottom.Y + 1,
                "The reopened shell remains inside the complete visible viewport on both axes");
            Console.WriteLine($"[popup-horizontal/{locale}/{(dark ? "dark" : "light")}] rendered boundary={requiredWidth:0.###} DIP; rejected requested={requiredWidth - 2:0.###}/actual={rejectedWidth:0.###}; reopened shell={surface.ActualWidth:0.###}; viewport={presenter.ActualWidth:0.###}x{presenter.ActualHeight:0.###}; sourceDpi={PopupViewport.ScreenScale(control).DpiScaleX:0.##}; popupDpi={VisualTreeHelper.GetDpi(surface).DpiScaleX:0.##}; captions complete; draft/noGET/cleanup preserved");
            stage("search/" + locale + "/close" + " begin");
            Click(control, "CloseButton");
            stage("search/" + locale + "/close" + " end");
            stage("search/" + locale + "/locale restored retirement" + " begin");
            AssertHorizontalRetirement(control, view);
            stage("search/" + locale + "/locale restored retirement" + " complete");
        }
        stage("search/original 400 restore begin");
        scroll.Width = 400;
        stage("search/400 restored layout-drain-open" + " begin");
        SettingsLayoutSuite.Layout(scroll, 400); Drain(); Open(control);
        stage("search/400 restored layout-drain-open" + " end");
        AssertEqual(true, IsOpen(control), "Restoring the original 400-DIP viewport reopens after repeated narrow refusal");
        stage("search/400 restored" + "/fixed actions begin");
        AssertFixedActions(control, surface);
        stage("search/400 restored" + "/fixed actions end");
        stage("search/400 restored" + "/fixed captions begin");
        AssertFixedCaptions(control, surface);
        stage("search/400 restored" + "/fixed captions end");
        stage("search/400 restored close" + " begin");
        Click(control, "CloseButton");
        stage("search/400 restored close" + " end");
        stage("search/400 restored retirement" + " begin");
        AssertHorizontalRetirement(control, view);
        stage("search/400 restored retirement" + " complete");
        AssertEqual(requestCount, http.GetCallCount, "All width-only transitions leave the last successful request untouched");
        stage("search original 400 restoration and no-GET complete");

        // The shared helper must use this shell's own fixed count, not impose the
        // search footer's larger width on read-only language overflow.
        stage("language control construction begin");
        var tags = new VoiceLanguageTags { Languages = new[] { "en", "zh", "de", "ja", "fr", "es", "ko", "ar", "ru", "pt", "it", "nl" } };
        tags.Resources.MergedDictionaries.Add(view.Resources);
        stage("language construction and original resource attachment end");
        stage("same-source scroll content replacement begin");
        scroll.Content = tags;
        stage("same-source scroll content replacement end");
        scroll.Width = 150;
        stage("language initial layout-drain-open begin");
        SettingsLayoutSuite.Layout(scroll, 400); Drain(); tags.OpenPopup(); Drain();
        stage("language initial layout-drain-open end");
        AssertEqual(true, tags.IsPopupOpen, "Language overflow can open below the search footer's width requirement");
        var languageSurface = (Border)tags.FindName("PopupSurface");
        var count = (TextBlock)tags.FindName("RemainingCount");
        var languageMinimum = count.ActualWidth + count.Margin.Left + languageSurface.Padding.Left + languageSurface.Padding.Right
            + languageSurface.BorderThickness.Left + languageSurface.BorderThickness.Right;
        var languagePresenter = Visuals(scroll).OfType<ScrollContentPresenter>().First();
        var languageOverhead = scroll.ActualWidth - languagePresenter.ActualWidth + 16;
        stage("language own-count width measurements complete");
        stage("language insufficient width begin");
        scroll.Width = languageMinimum - 2 + languageOverhead;
        stage("language narrow layout-drain begin");
        SettingsLayoutSuite.Layout(scroll, 400); Drain();
        stage("language narrow layout-drain end");
        var rejectedLanguageWidth = (languagePresenter.PointToScreen(new Point(languagePresenter.ActualWidth, 0)).X - languagePresenter.PointToScreen(new Point()).X)
            / PopupViewport.ScreenScale(tags).DpiScaleX - 16;
        AssertEqual(false, tags.IsPopupOpen, "Language overflow closes when its own fixed count cannot fit");
        AssertEqual(IntPtr.Zero, tags.Material.Handle, "Language width retirement releases its exact source");
        AssertEqual(false, (bool)typeof(VoiceLanguageTags).GetField("_ownsPopupInput", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tags)!,
            "Language width retirement detaches input");
        AssertEqual(0d, ((ScrollViewer)tags.FindName("PopupScroll")).Padding.Right, "Language width retirement restores scrollbar padding");
        AssertEqual(false, Escape(scroll).Handled, "Retired language input cannot consume Escape");
        stage("language material input padding and Escape retirement complete");
        stage("language insufficient reopen begin");
        tags.OpenPopup(); Drain();
        stage("language insufficient reopen end");
        AssertEqual(false, tags.IsPopupOpen, "Language overflow refuses a still insufficient width");
        stage("language sufficient width restore begin");
        scroll.Width = languageMinimum + 2 + languageOverhead;
        stage("language restored layout-drain-open begin");
        SettingsLayoutSuite.Layout(scroll, 400); Drain(); tags.OpenPopup(); Drain();
        stage("language restored layout-drain-open end");
        AssertEqual(true, tags.IsPopupOpen, "Language overflow reopens at its own sufficient width");
        var countBounds = count.TransformToAncestor(languageSurface).TransformBounds(new Rect(count.RenderSize));
        AssertEqual(true, countBounds.Left >= 0 && countBounds.Right <= languageSurface.ActualWidth, "The reopened fixed count remains fully visible");
        stage("language restored full count assertion complete");
        Console.WriteLine($"[popup-horizontal/languages/ru] native required={languageMinimum:0.###} DIP; rejected requested={languageMinimum - 2:0.###}/actual={rejectedLanguageWidth:0.###}; reopened={languageSurface.ActualWidth:0.###}; count={count.Text}");
        stage("language close begin");
        tags.ClosePopup();
        stage("language close end");
        AssertEqual(requestCount, http.GetCallCount, "Language width transitions issue no GET");
        stage("all body assertions complete; original source then VM disposals pending");
    }

    private static void AssertHorizontalRetirement(FrameworkElement control, FrameworkElement root)
    {
        object? Field(string name) => control.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control);
        AssertEqual(false, (bool)Field("_ownsInput")!, "Horizontal retirement detaches input");
        AssertEqual<object?>(null, Field("_owner"), "Horizontal retirement releases the overlay owner");
        AssertEqual<object?>(null, Field("_window"), "Horizontal retirement releases window subscriptions");
        AssertEqual(0, ((System.Collections.ICollection)Field("_scrollOwners")!).Count, "Horizontal retirement releases ancestor subscriptions");
        AssertEqual(IntPtr.Zero, ((SearchAdjustmentControl)control).Material.Handle, "Horizontal retirement releases material/source ownership");
        AssertEqual(0d, Part<ScrollViewer>(control, "PopupScroll").Padding.Right, "Horizontal retirement restores original scrollbar padding");
        AssertEqual(false, Escape(root).Handled, "Retired input handlers cannot consume subsequent Escape");
    }

    private static void AssertFixedCaptions(FrameworkElement control, Border surface)
    {
        foreach (var name in new[] { "ResetButton", "ApplyButton" })
        {
            var button = Part<Button>(control, name);
            var label = Visuals(button).OfType<TextBlock>().Single(text => text.Text == (string)button.Content);
            var full = new TextBlock { Text = label.Text, FontFamily = label.FontFamily, FontSize = label.FontSize,
                FontWeight = label.FontWeight, FontStyle = label.FontStyle, FontStretch = label.FontStretch };
            typeof(VisualTreeHelper).GetMethod("SetRootDpi", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [full, VisualTreeHelper.GetDpi(label)]);
            full.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var labelBounds = label.TransformToAncestor(button).TransformBounds(new Rect(label.RenderSize));
            var shellBounds = label.TransformToAncestor(surface).TransformBounds(new Rect(label.RenderSize));
            foreach (var presenter in AncestorsAndSelf(label).TakeWhile(item => !ReferenceEquals(item, button)).OfType<ContentPresenter>())
            {
                var inPresenter = label.TransformToAncestor(presenter).TransformBounds(new Rect(label.RenderSize));
                AssertEqual(true, inPresenter.Left >= -.01 && inPresenter.Right <= presenter.ActualWidth + .01,
                    $"The complete native {name} caption stays inside each real content presenter viewport");
            }
            AssertEqual(true, label.ActualWidth + .01 >= full.DesiredSize.Width && labelBounds.Left >= 0 && labelBounds.Right <= button.ActualWidth + .01
                && shellBounds.Left >= 0 && shellBounds.Right <= surface.ActualWidth + .01,
                $"The complete native {name} caption remains inside its button and visible shell ({label.Text})");
        }
    }

    private static void AssertFixedActions(FrameworkElement control, Border surface)
    {
        foreach (var name in new[] { "CloseButton", "ResetButton", "ApplyButton" })
        {
            var button = Part<Button>(control, name);
            var bounds = button.TransformToAncestor(surface).TransformBounds(new Rect(button.RenderSize));
            AssertEqual(true, bounds.Top >= 0 && bounds.Bottom <= surface.ActualHeight + .1 && bounds.Left >= 0 && bounds.Right <= surface.ActualWidth + .1,
                "Fixed Close/Reset/Apply remain fully inside the constrained shell");
        }
    }

    private static void SavePopupProof(FrameworkElement surface, string name, string? proofDirectory = null)
    {
        var directory = proofDirectory ?? Path.Combine(Path.GetDirectoryName(FindRepoFile("build.ps1"))!, ".superpowers", "sdd", "2026-10-24-voice-search-and-controls", "task-13-candidate-proofs");
        Directory.CreateDirectory(directory);
        WriteProofAssembly(directory);
        SaveBitmap(RenderGlyph(surface, surface), Path.Combine(directory, name + ".png"));
    }

    internal static void WriteProofAssembly(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = typeof(SearchAdjustmentControl).Assembly.Location;
        File.WriteAllText(Path.Combine(directory, "proof-assembly.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            path,
            sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),
            note = "Compiled fixture layout/composition only; native desktop blur is not represented by RenderTargetBitmap."
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    // Incorrect inset/padding/DPI conversion or missing/mismatched tile flags must
    // fail against the actual themed TextBox viewport and popup presentation source.
    private static void GeometryAndFlags(string locale, bool dark, double dpi, Action<string> stage, string? proofDirectory = null)
    {
        stage("ViewModel construction begin");
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        stage("ViewModel ready; compiled view construction begin");
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        stage("compiled view ready; locale/theme setup begin");
        SetLocale(view, locale);
        view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light);
        // Headless STA has no Application/OS accent initialization. Supply a labeled
        // palette state, retaining the production DynamicResource and native metrics.
        var accentPalette = new SolidColorBrush(dark ? Color.FromRgb(0x4C, 0xC2, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8));
        view.Resources["AccentFillColorDefaultBrush"] = accentPalette;
        view.Resources["AccentButtonBackground"] = accentPalette;
        view.Resources["AccentButtonForeground"] = Brushes.White;
        view.Resources["RadioButtonOuterEllipseCheckedFill"] = accentPalette;
        view.Resources["RadioButtonOuterEllipseCheckedStroke"] = accentPalette;
        stage("locale/theme ready (concrete accent fixture); source attach/layout begin");
        var source = Attach(view);
        stage("source attach/layout ready");
        FrameworkElement? diagnosticControl = null;
        try
        {
            var control = Control(view);
            diagnosticControl = control;
            var input = (TextBox)view.FindName("VoiceSearchInput");
            input.Text = new string('W', 160); input.CaretIndex = input.Text.Length;
            // Materialize lazy native text views before actual root DPI, preserving
            // the runner's established voice-card first-use ordering.
            stage("native text materialization begin");
            SettingsLayoutSuite.Layout(view, 480);
            _ = input.GetRectFromCharacterIndex(input.Text.Length);
            stage("native text ready; root DPI setup begin");
            VisualTreeHelper.SetRootDpi(view, new DpiScale(dpi, dpi));
            stage("root DPI setup ready");
            foreach (var width in new[] { 320d, 480d })
            {
                stage($"width={width}: layout begin");
                SettingsLayoutSuite.Layout(view, width); Drain();
                stage($"width={width}: layout/drain ready");
                input.CaretIndex = input.Text.Length;
                input.ScrollToHorizontalOffset(double.MaxValue); Drain();
                AssertEqual(dpi, VisualTreeHelper.GetDpi(input).DpiScaleX, "The native input receives the requested actual DPI");
                var button = Part<Button>(control, "AdjustmentButton");
                AssertEqual(28d, button.ActualWidth, "Embedded action keeps a 28-DIP hit target");
                AssertEqual(28d, button.ActualHeight, "Embedded action keeps a 28-DIP hit target");
                var buttonBounds = button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize));
                var inputBounds = input.TransformToAncestor(view).TransformBounds(new Rect(input.RenderSize));
                AssertEqual(true, Math.Abs(inputBounds.Right - buttonBounds.Right - 4) <= 1 / dpi, "The action has a four-DIP right inset");
                var textView = Visuals(input).OfType<FrameworkElement>().Single(item => item.GetType().Name == "TextBoxView");
                var viewport = textView.TransformToAncestor(view).TransformBounds(new Rect(textView.RenderSize));
                AssertEqual(true, viewport.Right <= buttonBounds.Left - 3, "The native text viewport reserves room for the embedded action");
                var caret = input.GetRectFromCharacterIndex(input.Text.Length);
                AssertEqual(true, caret.Right <= input.ActualWidth - 34, $"The final caret remains within the reserved text viewport ({locale}/{dpi}/{width}: {caret.Right}/{input.ActualWidth}, offset {input.HorizontalOffset})");
                stage($"width={width}: main popup begin");
                Open(control);
                stage($"width={width}: main popup open/drain ready");
                var surface = Part<Border>(control, "PopupSurface");
                AssertEqual(true, surface.ActualWidth <= 324 && surface.ActualWidth >= 280, "Popup body is 324 DIP or clamped to the narrow viewport");
                var left = surface.PointToScreen(new Point()).X;
                var right = surface.PointToScreen(new Point(surface.ActualWidth, 0)).X;
                var ownerLeft = view.PointToScreen(new Point()).X;
                var ownerRight = view.PointToScreen(new Point(view.ActualWidth, 0)).X;
                // Explicit visual-root DPI exercises text/layout. The separate native
                // HWND still uses its monitor's screen transform, not that injection.
                var screenScale = (ownerRight - ownerLeft) / view.ActualWidth;
                AssertEqual(true, left >= ownerLeft + 7 * screenScale - 1 && right <= ownerRight - 7 * screenScale + 1, $"Device-pixel placement contains the native Popup in the settings viewport ({locale}/{dpi}/{width}: {left}..{right} owner {ownerLeft}..{ownerRight}, popupDpi={VisualTreeHelper.GetDpi(surface).DpiScaleX}, viewDpi={VisualTreeHelper.GetDpi(view).DpiScaleX})");
                AssertEqual(true, ((SearchAdjustmentControl)control).Material.AttributeAccepted
                    ? surface.Background is SolidColorBrush { Color.A: 0 }
                    : surface.Background is SolidColorBrush { Color.A: 255 }, "Surface uses accepted native material or explicit native opaque fallback");
                var tiles = Tiles(control);
                var contentViewer = Part<ScrollViewer>(control, "PopupScroll");
                Console.WriteLine($"[popup-dpi] ownerRequested={dpi:0.##}; ownerActual={VisualTreeHelper.GetDpi(view).DpiScaleX:0.##}; popupActual={VisualTreeHelper.GetDpi(surface).DpiScaleX:0.##}; tileActual={VisualTreeHelper.GetDpi(tiles[0]).DpiScaleX:0.##}; OSdisplay=UNCHANGED");
                if (contentViewer.ScrollableHeight > 0)
                {
                    AssertScrollbarExclusion(contentViewer, tiles, $"{locale}/{dark}/{dpi}/{width} tiles");
                    AssertFixedActions(control, surface);
                    Part<RadioButton>(control, "CreationRadio").BringIntoView(); Drain();
                    var finalBounds = Part<RadioButton>(control, "CreationRadio").TransformToAncestor(contentViewer).TransformBounds(new Rect(Part<RadioButton>(control, "CreationRadio").RenderSize));
                    AssertEqual(true, finalBounds.Top >= -.1 && finalBounds.Bottom <= contentViewer.ActualHeight + .1, "BringIntoView reaches the final native sort choice");
                    AssertFixedActions(control, surface);
                    contentViewer.ScrollToTop(); Drain();
                }
                AssertEnumerableEqual(new[] { "", "en", "zh", "de", "ja", "fr", "es", "ko", "ar", "ru", "pt" }, tiles.Select(tile => (string)tile.Tag), "Preset tiles keep their approved order");
                for (var index = 0; index < tiles.Length; index++)
                {
                    var tile = tiles[index];
                    AssertEqual(32d, tile.ActualHeight, "Every tile has the same 32-DIP height");
                    AssertEqual(tiles[0].ActualWidth, tile.ActualWidth, "All and specific tiles have equal width");
                    AssertEqual(index == 0 ? 0 : ((index + 1) / 2) * 2, Grid.GetRow(tile), "All owns its first row and preset pairs follow below");
                    AssertEqual(index == 0 ? 0 : ((index - 1) % 2) * 2, Grid.GetColumn(tile), "All occupies only the left cell");
                    var name = AutomationProperties.GetName(tile);
                    AssertEqual(true, !string.IsNullOrWhiteSpace(name), "Every tile exposes its full localized name");
                    AssertEqual(name, tile.ToolTip as string, "Tooltip retains the full accessible name");
                    var label = Visuals(tile).OfType<TextBlock>().Single();
                    AssertEqual(TextTrimming.CharacterEllipsis, label.TextTrimming, "Constrained names trim without changing tile dimensions");
                    AssertEqual(true, label.ActualWidth <= tile.ActualWidth && label.ActualHeight <= tile.ActualHeight, "Native text stays within the tile");
                }
                var firstBounds = tiles[1].TransformToAncestor(Part<Grid>(control, "LanguageTiles")).TransformBounds(new Rect(tiles[1].RenderSize));
                var secondBounds = tiles[2].TransformToAncestor(Part<Grid>(control, "LanguageTiles")).TransformBounds(new Rect(tiles[2].RenderSize));
                AssertEqual(6d, secondBounds.Left - firstBounds.Right, "Columns have a six-DIP gap");
                AssertEqual(10, Visuals(Part<Grid>(control, "LanguageTiles")).OfType<LanguageFlagImage>().Count(item => item.Visibility == Visibility.Visible), "Ten specifics have embedded flags while All has none");
                foreach (var tile in tiles.Skip(1))
                {
                    var flag = Visuals(tile).OfType<LanguageFlagImage>().Single();
                    AssertEqual(true, ReferenceEquals(VoiceLanguagePresentation.Describe((string)tile.Tag, locale).Flag, flag.Source), "Each tile reuses the matching embedded atlas flag");
                    AssertEqual(true, tile.Focusable && KeyboardNavigation.GetIsTabStop(tile), "Every native toggle remains keyboard reachable");
                }
                if (dpi == 1.25 && (locale == "en" || (locale == "ru" && width == 320)))
                    SavePopupProof(surface, $"{locale}-{(dark ? "dark" : "light")}-{width:0}-all-accentfixture-owner125-popup{VisualTreeHelper.GetDpi(surface).DpiScaleX * 100:0}", proofDirectory);
                Toggle(control, "en"); Toggle(control, "zh");
                foreach (var tile in tiles)
                {
                    var selected = tile.IsChecked == true;
                    var border = (Border)tile.Template.FindName("TileBorder", tile);
                    AssertEqual(true, ReferenceEquals(tile.BorderBrush, border.BorderBrush), "Selected border resolves through the template binding");
                    if (selected)
                        AssertEqual(true, ReferenceEquals(view.FindResource("AccentFillColorDefaultBrush"), border.BorderBrush), "Selected tiles resolve the current native accent resource");
                    var check = Visuals(tile).OfType<System.Windows.Shapes.Path>().Single();
                    AssertEqual(selected ? Visibility.Visible : Visibility.Hidden, check.Visibility, "Only selected tiles show the decorative check");
                    AssertEqual(false, check.Focusable, "The check adds no separate focus target");
                }
                if (dpi == 1.25 && (locale == "en" || (locale == "ru" && width == 320)))
                    SavePopupProof(surface, $"{locale}-{(dark ? "dark" : "light")}-{width:0}-multiple-accentfixture-owner125-popup{VisualTreeHelper.GetDpi(surface).DpiScaleX * 100:0}", proofDirectory);
                foreach (var next in UiLocales.All)
                {
                    stage($"width={width}: locale={next} begin");
                    SetLocale(view, next); Drain();
                    stage($"width={width}: locale={next} replacement/drain ready");
                    AssertEnumerableEqual(new[] { "en", "zh" }, Selected(control), "Live locale replacement preserves the entire draft set");
                    AssertEqual(true, IsOpen(control), "Live locale replacement updates the same open popup");
                    if (contentViewer.ScrollableHeight > 0) AssertScrollbarExclusion(contentViewer, tiles, $"live {next} tiles");
                    foreach (var tile in tiles)
                    {
                        var code = (string)tile.Tag;
                        var expected = code.Length == 0 ? (string)view.FindResource("STranslate_Plugin_Tts_FishAudio_Search_AllLanguages") : VoiceLanguagePresentation.Describe(code, next).Name;
                        AssertEqual(expected, AutomationProperties.GetName(tile), "Each live locale supplies the full tile caption");
                        AssertEqual(expected, tile.ToolTip as string, "Locale changes refresh full tooltips");
                    }
                }
                SetLocale(view, locale); Drain();
                view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Light : ApplicationTheme.Dark;
                ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Light : ElementTheme.Dark); Drain();
                AssertEqual(true, IsOpen(control), "A live theme change preserves drafts and the same popup lifetime");
                if (contentViewer.ScrollableHeight > 0) AssertScrollbarExclusion(contentViewer, tiles, "live theme replacement");
                view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
                ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light); Drain();
                var relevance = Part<RadioButton>(control, "RelevanceRadio").Content as string;
                AssertEqual(locale == "zh-cn" ? "相关优先" : locale == "en" ? "Relevance first" : (string)view.FindResource("STranslate_Plugin_Tts_FishAudio_Search_Relevance"), relevance, "Score uses the approved localized relevance-first label");
                stage($"width={width}: popup cleanup begin");
                Click(control, "CloseButton");
                stage($"width={width}: popup cleanup ready open={IsOpen(control)} inputOwned={control.GetType().GetField("_ownsInput", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(control)}");
            }
        }
        finally
        {
            stage("HwndSource cleanup begin");
            source.Dispose();
            stage($"HwndSource cleanup ready inputOwned={diagnosticControl?.GetType().GetField("_ownsInput", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(diagnosticControl)}");
        }
    }


    // A popup-only template/color override must diverge from a separate native
    // peer using the exact same SDK theme, including its checked inner glyph.
    private static void RadioAppearanceMatchesNativePeer(bool dark)
    {
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light);
        // This shared STA has no Application/OS accent initialization. Supply a
        // concrete palette only; keep the actual native template and text brushes.
        var accent = new SolidColorBrush(dark ? Color.FromRgb(0x4C, 0xC2, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8));
        view.Resources["RadioButtonOuterEllipseCheckedFill"] = accent;
        view.Resources["RadioButtonOuterEllipseCheckedStroke"] = accent;
        var peer = new RadioButton { Content = "Native peer", GroupName = "IndependentNativePeer" };
        ((Panel)view.Content).Children.Insert(0, peer);
        using var source = Attach(view);
        var control = Control(view);
        Open(control);
        var actual = Part<RadioButton>(control, "RelevanceRadio");
        foreach (var isChecked in new[] { false, true })
        {
            actual.IsChecked = isChecked; peer.IsChecked = isChecked;
            foreach (var radio in new[] { actual, peer })
            {
                radio.ApplyTemplate();
                VisualStateManager.GoToState(radio, "Normal", false);
                VisualStateManager.GoToState(radio, isChecked ? "Checked" : "Unchecked", false);
            }
            Drain();
            var actualGrid = Visuals(actual).OfType<Grid>().Single(grid => grid.Height == 32);
            var peerGrid = Visuals(peer).OfType<Grid>().Single(grid => grid.Height == 32);
            var actualBitmap = RenderGlyph(actualGrid, actual);
            var peerBitmap = RenderGlyph(peerGrid, peer);
            var actualPixels = new byte[actualBitmap.PixelWidth * actualBitmap.PixelHeight * 4];
            var peerPixels = new byte[peerBitmap.PixelWidth * peerBitmap.PixelHeight * 4];
            actualBitmap.CopyPixels(actualPixels, actualBitmap.PixelWidth * 4, 0);
            peerBitmap.CopyPixels(peerPixels, peerBitmap.PixelWidth * 4, 0);
            AssertEnumerableEqual(peerPixels, actualPixels, "Popup radio checked/unchecked raster matches the independent native themed peer");
            foreach (var name in new[] { "OuterEllipse", "CheckOuterEllipse", "CheckGlyph" })
            {
                var glyph = (System.Windows.Shapes.Ellipse)actual.Template.FindName(name, actual);
                var native = (System.Windows.Shapes.Ellipse)peer.Template.FindName(name, peer);
                AssertEqual(native.Fill?.ToString(), glyph.Fill?.ToString(), "Popup and native peer use the same glyph fill");
                AssertEqual(native.Stroke?.ToString(), glyph.Stroke?.ToString(), "Popup and native peer use the same glyph border");
                AssertEqual(native.StrokeThickness, glyph.StrokeThickness, "Popup and native peer use the same border thickness");
            }
            var inner = (System.Windows.Shapes.Ellipse)actual.Template.FindName("CheckGlyph", actual);
            var outer = (System.Windows.Shapes.Ellipse)actual.Template.FindName("CheckOuterEllipse", actual);
            AssertEqual(accent.Color, ((SolidColorBrush)outer.Fill).Color, "The radio fixture has an initialized opaque checked accent palette");
            Console.WriteLine($"[search-radio] {(dark ? "dark" : "light")}/{(isChecked ? "checked" : "unchecked")}: outer={outer.ActualWidth:0.##} fill={outer.Fill}, inner={inner.ActualWidth:0.##} fill={inner.Fill}, nativeDpi={VisualTreeHelper.GetDpi(actual).DpiScaleX:0.##}; peer raster matches.");
            var directory = Path.Combine(Path.GetDirectoryName(FindRepoFile("build.ps1"))!, ".scratch", "task-4", "radio-proof");
            Directory.CreateDirectory(directory);
            SaveBitmap(actualBitmap, Path.Combine(directory, $"{(dark ? "dark" : "light")}-{(isChecked ? "checked" : "unchecked")}-popup.png"));
            SaveBitmap(peerBitmap, Path.Combine(directory, $"{(dark ? "dark" : "light")}-{(isChecked ? "checked" : "unchecked")}-peer.png"));
        }
        Click(control, "CloseButton");
    }

    private static RenderTargetBitmap RenderGlyph(FrameworkElement glyph, FrameworkElement resourceOwner)
    {
        var dpi = VisualTreeHelper.GetDpi(glyph);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(glyph.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(glyph.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle((Brush)resourceOwner.FindResource("FlyoutPresenterBackground"), null, new Rect(glyph.RenderSize));
            dc.DrawRectangle(new VisualBrush(glyph)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(glyph.RenderSize),
                Stretch = Stretch.Fill,
            }, null, new Rect(glyph.RenderSize));
        }
        bitmap.Render(drawing);
        return bitmap;
    }
    private static void SaveBitmap(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static KeyEventArgs Escape(FrameworkElement root)
    {
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(root)!, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        InputManager.Current.ProcessInput(key); return key;
    }
    private static void SetLocale(FrameworkElement view, string locale)
    {
        var previous = view.Resources.MergedDictionaries.SingleOrDefault(item => item.Source?.LocalPath.Replace('\\', '/').Contains("/Languages/", StringComparison.Ordinal) == true);
        if (previous is not null) view.Resources.MergedDictionaries.Remove(previous);
        view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(FindRepoFile($"STranslate.Plugin.Tts.FishAudio/Languages/{locale}.xaml"), UriKind.Absolute) });
    }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }
}
