using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

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
    private static readonly int[] ControlledKeys = [0x01, 0x10, 0xA0, 0xA1, 0x11, 0xA2, 0xA3, 0x12, 0xA4, 0xA5];

    internal static void Run(HwndSource source, Action action, NativeKeyboardCondition condition = NativeKeyboardCondition.Neutral) =>
        Run(new Win32Api(source), action, condition);

    internal static void Run(INativeKeyboardStateApi api, Action action, NativeKeyboardCondition condition = NativeKeyboardCondition.Neutral)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (condition is not (NativeKeyboardCondition.Neutral or NativeKeyboardCondition.AltOnly))
            throw new ArgumentOutOfRangeException(nameof(condition));
        api.VerifyContext();
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
            Verify(api, planned, condition);
            action();
            Verify(api, planned, condition);
        }
        catch (Exception error) { primary = ExceptionDispatchInfo.Capture(error); }
        finally
        {
            try
            {
                Require(api.Write(original), "restore");
                Require(Read(api, "restore readback").SequenceEqual(original), "restore mismatch");
            }
            catch (Exception error) { cleanup = ExceptionDispatchInfo.Capture(error); }
        }
        if (primary is not null)
        {
            if (cleanup is not null)
            {
                primary.SourceException.Data[CleanupFailureKey] = cleanup.SourceException;
                // Diagnostics must never replace the original preparation/action failure.
                try { Console.Error.WriteLine($"[native-key-state] secondary cleanup failure ({cleanup.SourceException.GetType().Name})"); }
                catch { /* The secondary exception remains attached to the primary. */ }
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

    private static void Verify(INativeKeyboardStateApi api, byte[] planned, NativeKeyboardCondition condition)
    {
        api.VerifyContext();
        Require(Read(api, "planned readback").SequenceEqual(planned), "planned mismatch");
        foreach (var key in ControlledKeys)
            Require(((api.KeyState(key) & 0x8000) != 0) == ((planned[key] & 0x80) != 0), "native down-bit mismatch");
        api.VerifyWpfState(condition);
    }

    private static void Require(bool valid, string stage)
    {
        if (!valid) throw new InvalidOperationException($"Native keyboard fixture failed at {stage}.");
    }

    internal sealed class Win32Api(HwndSource source) : INativeKeyboardStateApi
    {
        private readonly int _managedThread = Environment.CurrentManagedThreadId;
        private readonly uint _nativeThread = GetCurrentThreadId();

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
