internal enum NativeKeyboardFollowUpPhase { BeforeAction, AfterAction }
internal enum NativeKeyboardFollowUpStatus { Captured, Unavailable, Rejected }

internal sealed record NativeKeyboardFollowUpIdentity(int ManagedThreadId, uint NativeThreadId);

// The caller supplies facts already latched at its first strict mismatch,
// including only its own previously captured thread IDs. No native sampling
// or exception inspection occurs while constructing this immutable input.
internal sealed class NativeKeyboardFirstMismatch(Exception primaryFailure, NativeKeyboardFollowUpPhase phase,
    int key, bool plannedDown, bool tableDown, bool nativeDown, int managedThreadId, uint nativeThreadId)
{
    internal Exception PrimaryFailure { get; } = primaryFailure;
    internal NativeKeyboardFollowUpPhase Phase { get; } = phase;
    internal int Key { get; } = key;
    internal bool PlannedDown { get; } = plannedDown;
    internal bool TableDown { get; } = tableDown;
    internal bool NativeDown { get; } = nativeDown;
    internal int ManagedThreadId { get; } = managedThreadId;
    internal uint NativeThreadId { get; } = nativeThreadId;
}

// Captured means only that the optional follow-up was available. The original
// failure is retained by reference; no result represents a passing transaction.
internal sealed class NativeKeyboardFollowUpResult(NativeKeyboardFirstMismatch firstMismatch,
    NativeKeyboardFollowUpStatus status, ushort? controlledDownMask, bool? tableDown, bool? nativeDown)
{
    internal NativeKeyboardFirstMismatch FirstMismatch { get; } = firstMismatch;
    internal NativeKeyboardFollowUpStatus Status { get; } = status;
    // Bits 0..9 map to 01,10,A0,A1,11,A2,A3,12,A4,A5 respectively.
    // No native buffer, low/toggle bit, arbitrary key or exception text is retained.
    internal ushort? ControlledDownMask { get; } = controlledDownMask;
    internal bool? TableDown { get; } = tableDown;
    internal bool? NativeDown { get; } = nativeDown;
}

// Explicit opt-in, failure-only observation. Normal fixture transactions do not
// call this helper. It never publishes I/O or owns a new watchdog allowance.
internal static class NativeKeyboardFollowUp
{
    internal const string EvidenceKey = "NativeKeyboardStateFixture.OptionalFollowUp";
    private static readonly int[] ControlledKeys = [0x01, 0x10, 0xA0, 0xA1, 0x11, 0xA2, 0xA3, 0x12, 0xA4, 0xA5];

    internal static NativeKeyboardFollowUpResult Capture(INativeKeyboardStateApi api, NativeKeyboardFirstMismatch firstMismatch)
    {
        ArgumentNullException.ThrowIfNull(firstMismatch);
        if (api is null || firstMismatch.PrimaryFailure is null ||
            firstMismatch.Phase is not (NativeKeyboardFollowUpPhase.BeforeAction or NativeKeyboardFollowUpPhase.AfterAction) ||
            Array.IndexOf(ControlledKeys, firstMismatch.Key) < 0 ||
            firstMismatch.TableDown != firstMismatch.PlannedDown || firstMismatch.NativeDown == firstMismatch.PlannedDown ||
            firstMismatch.ManagedThreadId <= 0 || firstMismatch.NativeThreadId == 0)
            return new(firstMismatch, NativeKeyboardFollowUpStatus.Rejected, null, null, null);

        try
        {
            var state = new byte[256];
            if (!api.Read(state))
                return new(firstMismatch, NativeKeyboardFollowUpStatus.Unavailable, null, null, null);

            ushort mask = 0;
            for (var index = 0; index < ControlledKeys.Length; index++)
                if ((state[ControlledKeys[index]] & 0x80) != 0) mask |= (ushort)(1 << index);
            var tableDown = (state[firstMismatch.Key] & 0x80) != 0;
            var nativeDown = (api.KeyState(firstMismatch.Key) & 0x8000) != 0;
            return new(firstMismatch, NativeKeyboardFollowUpStatus.Captured, mask, tableDown, nativeDown);
        }
        catch
        {
            // Optional observation failure cannot inspect or replace the latched primary.
            return new(firstMismatch, NativeKeyboardFollowUpStatus.Unavailable, null, null, null);
        }
    }
}
