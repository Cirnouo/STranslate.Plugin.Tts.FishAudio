using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using static TestAssertions;

internal static class NativeKeyboardStateSuite
{
    internal static async Task RunAsync()
    {
        TransactionsPreserveStateAndFailures();
        FailureContextIsBoundedAndPrivate();
        PostActionChecksOnlyOwnedBitsAndRestoresEverything();
        CleanupMetadataCannotReplacePrimaryFailure();
        // One additional shared-guard action, after the existing Search first-use
        // boundaries in Program. Fake transactions never initialize WPF/STA.
        await SearchAdjustmentControlSuite.ObserveMatrixActionAsync("native-peers", NativePeers,
            prefix: "native-key-state", details: "one shared-guard action; Button and ToggleButton");
    }

    internal static void TransactionsPreserveStateAndFailures()
    {
        foreach (var condition in new[] { NativeKeyboardCondition.Neutral, NativeKeyboardCondition.AltOnly })
        foreach (var throws in new[] { false, true })
        {
            var api = new FakeApi();
            var original = (byte[])api.State.Clone();
            var failure = new ApplicationException("synthetic action failure");
            var calls = 0;
            var caught = Catch(() => NativeKeyboardStateFixture.Run(api, () =>
            {
                calls++;
                var controlled = new[] { 1, 0x10, 0xA0, 0xA1, 0x11, 0xA2, 0xA3, 0x12, 0xA4, 0xA5 };
                for (var key = 0; key < 256; key++)
                {
                    var expected = controlled.Contains(key) ? (byte)(original[key] & 0x7F) : original[key];
                    if (condition == NativeKeyboardCondition.AltOnly && key is 0x12 or 0xA4) expected |= 0x80;
                    AssertEqual(expected, api.State[key], "Only declared down bits change; all toggles, Space and unrelated bytes survive");
                }
                if (throws) throw failure;
            }, condition));
            AssertEqual(throws ? failure : null, caught, "Action exception identity is preserved");
            AssertEqual(1, calls, "The action executes exactly once");
            AssertEqual(true, original.SequenceEqual(api.State), "All 256 original bytes restore on success and action failure");
            AssertEqual(2, api.Writes, "One apply and one restoration, never a retry");
        }

        foreach (var stage in new[] { "context", "capture", "apply", "readback", "mismatch", "key", "wpf", "later-context", "after-action", "restore", "restore-read", "restore-mismatch" })
        {
            var api = new FakeApi { Failure = stage };
            var original = (byte[])api.State.Clone();
            var calls = 0;
            var caught = Catch(() => NativeKeyboardStateFixture.Run(api, () => calls++));
            AssertNotNull(caught, $"{stage} failure cannot silently pass");
            var entered = stage is "after-action" or "restore" or "restore-read" or "restore-mismatch";
            AssertEqual(entered ? 1 : 0, calls, "Preparation failures prevent the action; nothing retries it");
            AssertEqual(stage is "context" or "capture" ? 0 : stage == "later-context" ? 1 : 2, api.Writes, "Only one possible apply and one required restoration");
            if (stage == "context") AssertEqual(0, api.Reads, "Wrong-thread/source rejection precedes keyboard capture");
            if (stage is not "restore")
                AssertEqual(true, original.SequenceEqual(api.State), "Even partial apply/readback failures restore the entire snapshot");
        }

        foreach (var preparation in new[] { false, true })
        {
            var api = new FakeApi { Failure = preparation ? "wpf-and-restore" : "restore" };
            var primary = new ApplicationException("synthetic primary");
            var caught = Catch(() => NativeKeyboardStateFixture.Run(api, () => throw primary));
            if (!preparation) AssertEqual(true, ReferenceEquals(primary, caught), "Cleanup cannot replace the original action exception");
            else AssertEqual(true, ReferenceEquals(api.PostconditionFailure, caught), "Cleanup cannot replace the original preparation exception");
            AssertNotNull(caught!.Data[NativeKeyboardStateFixture.CleanupFailureKey], "Secondary cleanup failure stays attached to the primary");
            AssertEqual(2, api.Writes, "Cleanup failure never triggers automatic restore retry");
        }
        Console.WriteLine("Native keyboard pure-fake transaction regressions passed.");
    }

    private static Exception? Catch(Action action)
    {
        try { action(); return null; }
        catch (Exception error) { return error; }
    }

    internal static void FailureContextIsBoundedAndPrivate()
    {
        var success = new FakeApi();
        var successOutput = CaptureError(() => NativeKeyboardStateFixture.Run(success, () => { }));
        AssertEqual("", successOutput, "Successful comparisons emit no failure context");
        AssertEqual((4, 2, 20, 2, 4), success.Counts, "Success retains exactly the original native boundary call counts");

        foreach (var phase in new[] { "before-action", "after-action", "restore" })
        {
            var api = new FakeApi { Failure = phase == "before-action" ? "mismatch" : phase == "restore" ? "restore-mismatch" : "" };
            var original = (byte[])api.State.Clone();
            var actions = 0;
            Exception? caught = null;
            var output = CaptureError(() => caught = Catch(() => NativeKeyboardStateFixture.Run(api, () =>
            {
                actions++;
                if (phase == "after-action") { api.State[1] ^= 0x80; api.State[0x41] ^= 0x17; }
            })));
            AssertNotNull(caught, "Every full-table mismatch remains a failure");
            AssertEqual(typeof(InvalidOperationException), caught!.GetType(), "Telemetry preserves the mismatch exception type");
            AssertEqual($"Native keyboard fixture failed at {(phase == "restore" ? "restore mismatch" : "planned mismatch")}.", caught.Message,
                "Telemetry preserves the original mismatch message");
            var context = (string)caught.Data[NativeKeyboardStateFixture.FailureContextKey]!;
            var changes = phase == "after-action" ? "1; controlledChanges=[0x01:0x26>0xA6(down=False>True,low=0x26>0x26)]" : "0; controlledChanges=[]";
            AssertEqual($"phase={phase}; controlledChangedCount={changes}; spaceChanged=False; otherChangedCount=1", context,
                "Owned failures disclose controlled differences but other bytes only their count");
            AssertEqual($"[native-key-state] {context}{Environment.NewLine}", output, "Mismatch emits exactly one compact safe line");
            AssertEqual(true, original.SequenceEqual(api.State), "Strict post-action rejection still restores every original byte");
            AssertEqual(phase == "before-action" ? 0 : 1, actions, "Preparation rejects before action and never retries");
            AssertEqual(phase == "before-action" ? (3, 2, 0, 0, 3) : phase == "after-action" ? (4, 2, 10, 1, 4) : (4, 2, 20, 2, 4), api.Counts,
                "Failure context adds no native reads, writes or hidden retries");
        }

        var allChanged = new FakeApi();
        var allOriginal = (byte[])allChanged.State.Clone();
        Exception? allFailure = null;
        var allOutput = CaptureError(() => allFailure = Catch(() => NativeKeyboardStateFixture.Run(allChanged, () =>
        {
            for (var key = 0; key < 256; key++) allChanged.State[key] ^= 0xFF;
        })));
        var allContext = (string)allFailure!.Data[NativeKeyboardStateFixture.FailureContextKey]!;
        AssertEqual(true, allContext.Length <= NativeKeyboardStateFixture.MaximumFailureContextLength, "Worst-case context has a fixed size bound");
        AssertEqual(true, allContext.StartsWith("phase=after-action; controlledChangedCount=10;", StringComparison.Ordinal), "All ten allowed controlled changes are counted");
        AssertEqual(true, allContext.Contains("0x01:0x26>0xD9(down=False>True,low=0x26>0x59)", StringComparison.Ordinal), "Controlled keys expose only declared hex/down/low details");
        AssertEqual(true, allContext.EndsWith("spaceChanged=True; otherChangedCount=245", StringComparison.Ordinal), "Space is a boolean and all other changes are one count");
        AssertEqual(false, allContext.Contains("0x20:", StringComparison.Ordinal) || allContext.Contains("0x41:", StringComparison.Ordinal), "Space and arbitrary typed-key identities are never listed");
        AssertEqual(false, allContext.Contains(Convert.ToHexString(allOriginal), StringComparison.Ordinal), "The raw table is not serialized");
        AssertEqual($"[native-key-state] {allContext}{Environment.NewLine}", allOutput, "Stderr contains only the bounded safe context");
        AssertEqual(true, allOriginal.SequenceEqual(allChanged.State), "Even all-byte post-action changes fail and restore exactly");
        AssertEqual((4, 2, 10, 1, 4), allChanged.Counts, "Worst-case metadata reuses the existing snapshot only");

        var secondaryApi = new FakeApi { MismatchRead = 3 };
        var primary = new ApplicationException("synthetic primary remains private");
        Exception? preserved = null;
        var secondaryOutput = CaptureError(() => preserved = Catch(() => NativeKeyboardStateFixture.Run(secondaryApi, () => throw primary)));
        AssertEqual(true, ReferenceEquals(primary, preserved), "Restoration metadata cannot replace original action exception identity");
        AssertEqual(null, primary.Data[NativeKeyboardStateFixture.FailureContextKey], "Secondary restoration context is not mislabeled as the primary failure");
        var secondary = (Exception)primary.Data[NativeKeyboardStateFixture.CleanupFailureKey]!;
        AssertEqual(true, ((string)secondary.Data[NativeKeyboardStateFixture.FailureContextKey]!).StartsWith("phase=restore;", StringComparison.Ordinal), "Cleanup keeps its own restoration context");
        AssertEqual(false, secondaryOutput.Contains(primary.Message, StringComparison.Ordinal), "Stderr never prints the original action's text");
        AssertEqual((3, 2, 10, 1, 3), secondaryApi.Counts, "Double failure preserves native call counts without retry");

        var previousError = Console.Error;
        using var failingWriter = new FailingErrorWriter();
        try
        {
            Console.SetError(failingWriter);
            var caught = Catch(() => NativeKeyboardStateFixture.Run(new FakeApi { Failure = "mismatch" }, () => throw new InvalidOperationException("Action must not execute")));
            AssertEqual("Native keyboard fixture failed at planned mismatch.", caught!.Message, "Broken stderr cannot replace the strict mismatch");
            AssertNotNull(caught.Data[NativeKeyboardStateFixture.FailureContextKey], "Safe context survives a stderr failure");
        }
        finally { Console.SetError(previousError); }
        Console.WriteLine("Native keyboard failure-context privacy and strict-boundary regressions passed.");
    }

    internal static void PostActionChecksOnlyOwnedBitsAndRestoresEverything()
    {
        // The same unrelated key remains strict during preparation/restoration,
        // but does not belong to the native Space postcondition.
        var unowned = new FakeApi();
        var original = (byte[])unowned.State.Clone();
        var actions = 0;
        var output = CaptureError(() => NativeKeyboardStateFixture.Run(unowned, () => { actions++; unowned.State[255] ^= 1; }));
        AssertEqual(1, actions, "Unowned post-action changes do not retry or bypass the action");
        AssertEqual("", output, "Unowned post-action updates do not emit drift diagnostics on success");
        AssertEqual(true, original.SequenceEqual(unowned.State), "Every unowned byte still restores from the full snapshot");
        AssertEqual((4, 2, 20, 2, 4), unowned.Counts, "Allowed unowned updates still execute every original native/WPF/context check exactly once");
        foreach (var phase in new[] { "before-action", "restore" })
        {
            var api = new FakeApi { MismatchRead = phase == "before-action" ? 2 : 4 };
            var saved = (byte[])api.State.Clone();
            var calls = 0;
            Exception? failure = null;
            CaptureError(() => failure = Catch(() => NativeKeyboardStateFixture.Run(api, () => calls++)));
            AssertNotNull(failure, "The identical unowned-byte difference still fails full preparation/restoration equality");
            AssertEqual(true, ((string)failure!.Data[NativeKeyboardStateFixture.FailureContextKey]!).StartsWith($"phase={phase};", StringComparison.Ordinal), "Full checks retain their exact failure phase");
            AssertEqual(phase == "before-action" ? 0 : 1, calls, "Preparation rejects before invoking the action");
            AssertEqual(true, saved.SequenceEqual(api.State), "Unowned readback failure still restores every original byte");
            AssertEqual(phase == "before-action" ? (3, 2, 0, 0, 3) : (4, 2, 20, 2, 4), api.Counts, "Full-boundary rejection adds no API calls or retries");
        }

        foreach (var key in new[] { 0x01, 0x10, 0xA0, 0xA1, 0x11, 0xA2, 0xA3, 0x12, 0xA4, 0xA5, 0x20 })
        foreach (var changedBit in new byte[] { 0x80, 0x01 })
        {
            var api = new FakeApi();
            var saved = (byte[])api.State.Clone();
            Exception? failure = null;
            var diagnostic = CaptureError(() => failure = Catch(() => NativeKeyboardStateFixture.Run(api, () => api.State[key] ^= changedBit)));
            if (key == 0x20 || changedBit == 0x01)
            {
                AssertEqual(null, failure, "Unconfigured Space metadata and controlled low bits do not change the configured Down postcondition");
                AssertEqual("", diagnostic, "Allowed metadata changes emit no failure logging");
                AssertEqual(true, saved.SequenceEqual(api.State), "Space and controlled low bits still restore with all 256 original bytes");
                AssertEqual((4, 2, 20, 2, 4), api.Counts, "Metadata updates never bypass native/WPF/context checks or add calls");
                continue;
            }
            AssertNotNull(failure, "Every configured Down change rejects the post-action snapshot");
            AssertEqual("Native keyboard fixture failed at planned mismatch.", failure!.Message, "Owned-byte rejection retains the original message");
            var context = (string)failure.Data[NativeKeyboardStateFixture.FailureContextKey]!;
            AssertEqual(true, context.StartsWith("phase=after-action;", StringComparison.Ordinal), "Owned-byte rejection retains its phase");
            AssertEqual(true, context.Contains($"spaceChanged={key == 0x20}; otherChangedCount=0", StringComparison.Ordinal), "Space is separate from controlled and unowned differences");
            AssertEqual(true, saved.SequenceEqual(api.State), "Each controlled and Space byte restores in full after rejection");
            AssertEqual((4, 2, 10, 1, 4), api.Counts, "Owned-bit checking uses the existing post-action read without retry");
        }
        foreach (var key in new[] { 0x01, 0x10, 0xA0, 0xA1, 0x11, 0xA2, 0xA3, 0x12, 0xA4, 0xA5 })
        foreach (var phase in new[] { "before-action", "restore" })
        {
            var api = new FakeApi { MismatchRead = phase == "before-action" ? 2 : 4, MismatchKey = key };
            var saved = (byte[])api.State.Clone();
            var calls = 0;
            Exception? failure = null;
            CaptureError(() => failure = Catch(() => NativeKeyboardStateFixture.Run(api, () => calls++)));
            AssertNotNull(failure, "The identical controlled low-bit mutation still fails full preparation/restoration equality");
            AssertEqual(true, ((string)failure!.Data[NativeKeyboardStateFixture.FailureContextKey]!).StartsWith($"phase={phase};", StringComparison.Ordinal), "Full low-bit guards keep their exact phase");
            AssertEqual(phase == "before-action" ? 0 : 1, calls, "A preparation low-bit mismatch prevents the action");
            AssertEqual(true, saved.SequenceEqual(api.State), "Full restoration includes every controlled low-bit value");
            AssertEqual(phase == "before-action" ? (3, 2, 0, 0, 3) : (4, 2, 20, 2, 4), api.Counts, "Full low-bit rejection adds no API call or retry");
        }
        foreach (var changedBit in new byte[] { 0x80, 0x01 })
        foreach (var phase in new[] { "before-action", "restore" })
        {
            var api = new FakeApi { MismatchRead = phase == "before-action" ? 2 : 4, MismatchKey = 0x20, MismatchMask = changedBit };
            var saved = (byte[])api.State.Clone();
            var calls = 0;
            Exception? failure = null;
            CaptureError(() => failure = Catch(() => NativeKeyboardStateFixture.Run(api, () => calls++)));
            AssertNotNull(failure, "Space high/low differences still fail strict full preparation/restoration");
            var context = (string)failure!.Data[NativeKeyboardStateFixture.FailureContextKey]!;
            AssertEqual(true, context.StartsWith($"phase={phase};", StringComparison.Ordinal) && context.Contains("spaceChanged=True;", StringComparison.Ordinal), "Full Space guards retain their safe phase/boolean context");
            AssertEqual(phase == "before-action" ? 0 : 1, calls, "Preparation Space mismatches prevent the action");
            AssertEqual(true, saved.SequenceEqual(api.State), "Space high/low failure paths still restore the full original table");
            AssertEqual(phase == "before-action" ? (3, 2, 0, 0, 3) : (4, 2, 20, 2, 4), api.Counts, "Space guards add no API calls or retries");
        }
        Console.WriteLine("Native keyboard owned-Down-bit postcondition and full-restoration regressions passed.");
    }

    internal static void CleanupMetadataCannotReplacePrimaryFailure()
    {
        foreach (var throwingGetter in new[] { false, true })
        {
            var api = new FakeApi { Failure = "restore" };
            var primary = new DiagnosticDataException(throwingGetter);
            var actions = 0;
            Exception? caught = null;
            var output = CaptureError(() => caught = Catch(() => NativeKeyboardStateFixture.Run(api, () =>
            {
                actions++;
                ThrowPrimary(primary);
            })));
            AssertEqual(true, ReferenceEquals(primary, caught), "Read-only or throwing Data cannot replace the original primary object");
            AssertEqual(true, caught!.StackTrace!.Contains(nameof(ThrowPrimary), StringComparison.Ordinal), "EDI preserves the original primary throw frame even when attachment fails");
            AssertEqual(1, actions, "Primary metadata failure never retries the action");
            AssertEqual((2, 2, 10, 1, 3), api.Counts, "Metadata failure retains one apply and one failed restore attempt with no retry");
            AssertEqual($"[native-key-state] secondary cleanup failure (InvalidOperationException){Environment.NewLine}", output, "Only safe secondary exception type is logged, never primary/Data messages");
        }
        Console.WriteLine("Native keyboard primary-error preservation with read-only/throwing Data passed.");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ThrowPrimary(Exception error) => throw error;

    private sealed class DiagnosticDataException(bool throwingGetter) : Exception("Sensitive synthetic primary text")
    {
        private readonly System.Collections.IDictionary _readOnly =
            new System.Collections.ObjectModel.ReadOnlyDictionary<object, object>(new Dictionary<object, object>());
        public override System.Collections.IDictionary Data => throwingGetter
            ? throw new InvalidOperationException("Sensitive synthetic Data text") : _readOnly;
    }

    private static string CaptureError(Action action)
    {
        var previous = Console.Error;
        using var writer = new StringWriter();
        try { Console.SetError(writer); action(); return writer.ToString(); }
        finally { Console.SetError(previous); }
    }

    private sealed class FailingErrorWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("Synthetic diagnostic sink failure");
    }

    private sealed class FakeApi : INativeKeyboardStateApi
    {
        internal byte[] State = Enumerable.Range(0, 256).Select(index => (byte)(index * 37 + 129)).ToArray();
        internal string Failure = "";
        internal int Writes;
        internal int Reads => _reads;
        internal int MismatchRead;
        internal int MismatchKey = 255;
        internal byte MismatchMask = 1;
        internal (int Reads, int Writes, int Keys, int Wpf, int Contexts) Counts => (_reads, Writes, _keys, _wpf, _contexts);
        internal readonly Exception PostconditionFailure = new InvalidOperationException("Synthetic WPF postcondition failure");
        private int _reads;
        private int _contexts;
        private int _keys;
        private int _wpf;
        public void VerifyContext()
        {
            _contexts++;
            if (Failure == "context" || Failure == "later-context" && _contexts == 2)
                throw new InvalidOperationException("Synthetic wrong-thread/source context");
        }
        public bool Read(byte[] state)
        {
            _reads++;
            if (Failure == "capture" && _reads == 1 || Failure == "readback" && _reads == 2 ||
                Failure == "after-action" && _reads == 3 || Failure == "restore-read" && _reads == 4) return false;
            State.CopyTo(state, 0);
            if (Failure == "mismatch" && _reads == 2 || Failure == "restore-mismatch" && _reads == 4 || _reads == MismatchRead) state[MismatchKey] ^= MismatchMask;
            return true;
        }
        public bool Write(byte[] state)
        {
            Writes++;
            if ((Failure is "restore" or "wpf-and-restore") && Writes == 2) return false;
            // An API may mutate before reporting failure; restoration is still required.
            State = (byte[])state.Clone();
            return Failure != "apply" || Writes != 1;
        }
        public short KeyState(int key)
        {
            _keys++;
            return Failure == "key" ? (short)(State[key] << 8 ^ 0x8000) : (short)(State[key] << 8);
        }
        public void VerifyWpfState(NativeKeyboardCondition condition)
        {
            _wpf++;
            if (Failure is "wpf" or "wpf-and-restore") throw PostconditionFailure;
        }
    }

    private static void NativePeers(Action<string> stage)
    {
        var panel = new StackPanel();
        var button = new Button { Content = "Button", Width = 100, Height = 30 };
        var toggle = new ToggleButton { Content = "Toggle", Width = 100, Height = 30 };
        panel.Children.Add(button); panel.Children.Add(toggle);
        using var source = new HwndSource(new HwndSourceParameters("Fish Audio native key state peers")
        { Width = 120, Height = 80, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = panel };
        try
        {
            panel.Measure(new Size(120, 80)); panel.Arrange(new Rect(0, 0, 120, 80)); panel.UpdateLayout();
            foreach (var target in new ButtonBase[] { button, toggle })
            {
                var clicks = 0;
                var commands = 0;
                target.Command = new CountingCommand(() => commands++);
                RoutedEventHandler listener = (_, _) => clicks++;
                target.AddHandler(ButtonBase.ClickEvent, listener, true);
                try
                {
                    foreach (var condition in new[] { NativeKeyboardCondition.AltOnly, NativeKeyboardCondition.Neutral })
                    {
                        target.Focus();
                        AssertEqual(true, target.IsKeyboardFocused, "Native peer receives focus naturally");
                        AssertEqual(false, target.IsPressed || target.IsMouseCaptured, "Peer starts unpressed and uncaptured");
                        var api = new NativeKeyboardStateFixture.Win32Api(source);
                        var original = new byte[256];
                        AssertEqual(true, api.Read(original), "Independent pre-transaction capture succeeds");
                        var positive = condition == NativeKeyboardCondition.Neutral;
                        NativeKeyboardStateFixture.Run(source, () =>
                        {
                            var down = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space)
                            { RoutedEvent = Keyboard.KeyDownEvent };
                            target.RaiseEvent(down);
                            AssertEqual(positive, down.Handled, "Alt-only rejects native SpaceDown; neutral arms it");
                            AssertEqual(positive, target.IsPressed, "Native SpaceDown controls pressed state");
                            AssertEqual(positive, target.IsMouseCaptured, "Native SpaceDown controls capture");
                            AssertEqual(true, ReferenceEquals(target, down.OriginalSource), "Native event originates at its target");
                            var up = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space)
                            { RoutedEvent = Keyboard.KeyUpEvent };
                            target.RaiseEvent(up);
                            AssertEqual(positive, up.Handled, "Native SpaceUp preserves modifier gates");
                            AssertEqual(false, target.IsPressed || target.IsMouseCaptured, "SpaceUp retires pressed/capture state");
                            AssertEqual(positive ? 1 : 0, clicks, "Native class handler raises exactly the expected Click");
                            AssertEqual(positive ? 1 : 0, commands, "Native Click executes exactly the expected command");
                            if (target == toggle) AssertEqual(positive, toggle.IsChecked, "Native ToggleButton toggles only on neutral Space");
                        }, condition);
                        var restored = new byte[256];
                        AssertEqual(true, api.Read(restored) && original.SequenceEqual(restored), "All 256 bytes restore before any dispatcher drain");
                        stage($"{target.GetType().Name}/{condition}: native gates and exact restoration passed before drain");
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    }
                }
                finally { target.RemoveHandler(ButtonBase.ClickEvent, listener); target.Command = null; }
            }
        }
        finally { source.RootVisual = null; }
    }

    private sealed class CountingCommand(Action execute) : ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
