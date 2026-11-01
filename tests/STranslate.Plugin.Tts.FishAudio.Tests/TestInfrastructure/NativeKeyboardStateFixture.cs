using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

internal enum NativeKeyboardCondition { Neutral, AltOnly }

internal interface INativeKeyboardStateApi
{
    void VerifyContext();
    bool Read(byte[] state);
    bool Write(byte[] state);
    short KeyState(int key);
    void VerifyWpfState(NativeKeyboardCondition condition);
}

// A synchronous calling-thread table fixture, never physical input. Callers own
// their source and must keep dispatcher pumping outside this transaction.
internal static class NativeKeyboardStateFixture
{
    internal const string CleanupFailureKey = "NativeKeyboardStateFixture.CleanupFailure";
    internal const string FailureContextKey = "NativeKeyboardStateFixture.FailureContext";
    internal const int MaximumFailureContextLength = 1600;
    private static readonly int[] ControlledKeys = [0x01, 0x10, 0xA0, 0xA1, 0x11, 0xA2, 0xA3, 0x12, 0xA4, 0xA5];

    internal static void Run(HwndSource source, Action action, NativeKeyboardCondition condition = NativeKeyboardCondition.Neutral)
    {
        var api = new Win32Api(source);
        // Read the explicit diagnostic switch before touching the keyboard table.
        // Thread identities were captured when this calling-thread API was created.
        var configured = Environment.GetEnvironmentVariable("FISHAUDIO_NATIVE_KEYBOARD_FOLLOW_UP");
        var followUp = configured switch
        {
            null or "" => null,
            "1" => new NativeKeyboardFollowUpIdentity(api.CapturedManagedThreadId, api.CapturedNativeThreadId),
            _ => throw new InvalidOperationException("native-key-follow-up-option-invalid")
        };
        RunOnSource(source, api, action, condition, followUp);
    }

    internal static void RunOnSource(HwndSource ownedSource, INativeKeyboardStateApi api, Action action,
        NativeKeyboardCondition condition = NativeKeyboardCondition.Neutral, NativeKeyboardFollowUpIdentity? followUp = null)
    {
        ownedSource.Dispatcher.VerifyAccess();
        RunCore(api, action, condition, followUp, ownedSource.Dispatcher);
    }

    internal static void Run(INativeKeyboardStateApi api, Action action, NativeKeyboardCondition condition = NativeKeyboardCondition.Neutral) =>
        Run(api, action, condition, null);

    internal static void Run(INativeKeyboardStateApi api, Action action, NativeKeyboardCondition condition,
        NativeKeyboardFollowUpIdentity? followUp) => RunCore(api, action, condition, followUp, null);

    private static void RunCore(INativeKeyboardStateApi api, Action action, NativeKeyboardCondition condition,
        NativeKeyboardFollowUpIdentity? followUp, Dispatcher? dispatcher)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (condition is not (NativeKeyboardCondition.Neutral or NativeKeyboardCondition.AltOnly))
            throw new ArgumentOutOfRangeException(nameof(condition));
        api.VerifyContext();
        using var processing = dispatcher?.DisableProcessing();
        var original = Read(api, "capture");
        var planned = (byte[])original.Clone();
        foreach (var key in ControlledKeys) planned[key] &= 0x7F;
        if (condition == NativeKeyboardCondition.AltOnly) { planned[0x12] |= 0x80; planned[0xA4] |= 0x80; }
        ExceptionDispatchInfo? primary = null;
        ExceptionDispatchInfo? cleanup = null;
        try
        {
            api.VerifyContext();
            Require(api.Write(planned), "apply");
            Verify(api, planned, condition, "before-action", ownedOnly: false, followUp);
            action();
            Verify(api, planned, condition, "after-action", ownedOnly: true, followUp);
        }
        catch (Exception error) { primary = ExceptionDispatchInfo.Capture(error); }
        finally
        {
            try
            {
                Require(api.Write(original), "restore");
                RequireSnapshot(Read(api, "restore readback"), original, "restore mismatch", "restore");
            }
            catch (Exception error) { cleanup = ExceptionDispatchInfo.Capture(error); }
        }
        if (primary is not null)
        {
            if (cleanup is not null)
            {
                try { primary.SourceException.Data[CleanupFailureKey] = cleanup.SourceException; }
                catch { /* Extensible exception metadata cannot replace the primary. */ }
                // Diagnostics must never replace the original preparation/action failure.
                try { Console.Error.WriteLine($"[native-key-state] secondary cleanup failure ({cleanup.SourceException.GetType().Name})"); }
                catch { /* Secondary diagnostics are best effort. */ }
            }
            primary.Throw();
        }
        cleanup?.Throw();
    }

    private static byte[] Read(INativeKeyboardStateApi api, string stage)
    {
        var state = new byte[256];
        Require(api.Read(state), stage);
        return state;
    }

    private static void Verify(INativeKeyboardStateApi api, byte[] planned, NativeKeyboardCondition condition,
        string phase, bool ownedOnly, NativeKeyboardFollowUpIdentity? followUp)
    {
        api.VerifyContext();
        var table = Read(api, "planned readback");
        RequireSnapshot(table, planned, "planned mismatch", phase, ownedOnly);
        foreach (var key in ControlledKeys)
        {
            var nativeDown = (api.KeyState(key) & 0x8000) != 0;
            var plannedDown = (planned[key] & 0x80) != 0;
            if (nativeDown == plannedDown) continue;
            var error = new InvalidOperationException("Native keyboard fixture failed at native down-bit mismatch.");
            if (followUp is not null)
            {
                // The first mismatch is already latched. Exactly one optional
                // read/query precedes formatting, Console, WPF and restoration.
                // It cannot repair state, retry the action or recover this error.
                var first = new NativeKeyboardFirstMismatch(error,
                    phase == "before-action" ? NativeKeyboardFollowUpPhase.BeforeAction : NativeKeyboardFollowUpPhase.AfterAction,
                    key, plannedDown, (table[key] & 0x80) != 0, nativeDown,
                    followUp.ManagedThreadId, followUp.NativeThreadId);
                var observed = NativeKeyboardFollowUp.Capture(api, first);
                try { error.Data[NativeKeyboardFollowUp.EvidenceKey] = observed; }
                catch { /* Optional metadata cannot replace the first mismatch. */ }
            }
            try
            {
                var context = $"phase={phase}; nativeKey=0x{key:X2}; plannedDown={plannedDown}; tableDown={(table[key] & 0x80) != 0}; nativeDown={nativeDown}";
                error.Data[FailureContextKey] = context;
                try { Console.Error.WriteLine($"[native-key-state] {context}"); }
                catch { /* Failure diagnostics cannot replace the strict mismatch. */ }
            }
            catch { /* Preserve mismatch type/message even if formatting or Data fails. */ }
            throw error;
        }
        api.VerifyWpfState(condition);
    }

    private static void RequireSnapshot(byte[] actual, byte[] expected, string stage, string phase, bool ownedOnly = false)
    {
        var matches = true;
        if (ownedOnly)
        {
            // The fixture configures only Down bits on these keys. Low/toggle
            // metadata and unrelated keys still belong to the full restoration.
            foreach (var key in ControlledKeys) matches &= (actual[key] & 0x80) == (expected[key] & 0x80);
        }
        else matches = actual.SequenceEqual(expected);
        if (matches) return;
        var error = new InvalidOperationException($"Native keyboard fixture failed at {stage}.");
        try
        {
            var changes = new List<string>(ControlledKeys.Length);
            foreach (var key in ControlledKeys)
            {
                if (actual[key] == expected[key]) continue;
                changes.Add($"0x{key:X2}:0x{expected[key]:X2}>0x{actual[key]:X2}" +
                    $"(down={(expected[key] & 0x80) != 0}>{(actual[key] & 0x80) != 0},low=0x{expected[key] & 0x7F:X2}>0x{actual[key] & 0x7F:X2})");
            }
            var otherChanged = 0;
            for (var key = 0; key < 256; key++)
                if (key != 0x20 && !ControlledKeys.Contains(key) && actual[key] != expected[key]) otherChanged++;
            var context = $"phase={phase}; controlledChangedCount={changes.Count}; controlledChanges=[{string.Join(",", changes)}]; " +
                $"spaceChanged={actual[0x20] != expected[0x20]}; otherChangedCount={otherChanged}";
            // Only fixed phases and ten declared key identities/bytes are exposed.
            // No snapshot or arbitrary key identity is retained in exception data.
            if (context.Length > MaximumFailureContextLength) context = context[..MaximumFailureContextLength];
            error.Data[FailureContextKey] = context;
            try { Console.Error.WriteLine($"[native-key-state] {context}"); }
            catch { /* Failure diagnostics cannot replace the strict mismatch. */ }
        }
        catch { /* Preserve mismatch type/message even if formatting or Data fails. */ }
        throw error;
    }

    private static void Require(bool valid, string stage)
    {
        if (!valid) throw new InvalidOperationException($"Native keyboard fixture failed at {stage}.");
    }

    internal sealed class Win32Api(HwndSource source) : INativeKeyboardStateApi
    {
        private readonly int _managedThread = Environment.CurrentManagedThreadId;
        private readonly uint _nativeThread = GetCurrentThreadId();
        internal int CapturedManagedThreadId => _managedThread;
        internal uint CapturedNativeThreadId => _nativeThread;

        public void VerifyContext()
        {
            Require(Environment.CurrentManagedThreadId == _managedThread && GetCurrentThreadId() == _nativeThread &&
                Thread.CurrentThread.GetApartmentState() == ApartmentState.STA && source.Dispatcher.CheckAccess(), "calling STA");
            Require(!source.IsDisposed && source.Handle != IntPtr.Zero, "live source");
            Require(GetWindowThreadProcessId(source.Handle, out var process) == _nativeThread && process == (uint)Environment.ProcessId, "source ownership");
            var focus = GetFocus();
            if (focus != IntPtr.Zero)
                Require(GetWindowThreadProcessId(focus, out var focusProcess) == _nativeThread && focusProcess == (uint)Environment.ProcessId, "focus ownership");
            Require(Keyboard.PrimaryDevice.GetType().FullName == "System.Windows.Input.Win32KeyboardDevice" &&
                Mouse.PrimaryDevice.GetType().FullName == "System.Windows.Input.Win32MouseDevice", "native devices");
        }

        public bool Read(byte[] state) => GetKeyboardState(state);
        public bool Write(byte[] state) => SetKeyboardState(state);
        public short KeyState(int key) => GetKeyState(key);
        public void VerifyWpfState(NativeKeyboardCondition condition)
        {
            Require(Keyboard.Modifiers == (condition == NativeKeyboardCondition.AltOnly ? ModifierKeys.Alt : ModifierKeys.None), "WPF modifiers");
            Require(Mouse.PrimaryDevice.LeftButton == MouseButtonState.Released, "WPF left release");
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetKeyboardState([Out] byte[] state);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetKeyboardState(byte[] state);
        [DllImport("user32.dll")]
        private static extern short GetKeyState(int key);
        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint process);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
