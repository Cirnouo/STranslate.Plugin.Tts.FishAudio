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
        // One additional ordinary 5s action, after the existing Search first-use
        // boundaries in Program. Fake transactions never initialize WPF/STA.
        await SearchAdjustmentControlSuite.ObserveMatrixActionAsync("native-peers", NativePeers,
            prefix: "native-key-state", details: "one ordinary 5s action; Button and ToggleButton");
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

    private sealed class FakeApi : INativeKeyboardStateApi
    {
        internal byte[] State = Enumerable.Range(0, 256).Select(index => (byte)(index * 37 + 129)).ToArray();
        internal string Failure = "";
        internal int Writes;
        internal int Reads => _reads;
        internal readonly Exception PostconditionFailure = new InvalidOperationException("Synthetic WPF postcondition failure");
        private int _reads;
        private int _contexts;
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
            if (Failure == "mismatch" && _reads == 2 || Failure == "restore-mismatch" && _reads == 4) state[255] ^= 1;
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
        public short KeyState(int key) => Failure == "key" ? (short)(State[key] << 8 ^ 0x8000) : (short)(State[key] << 8);
        public void VerifyWpfState(NativeKeyboardCondition condition)
        {
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
