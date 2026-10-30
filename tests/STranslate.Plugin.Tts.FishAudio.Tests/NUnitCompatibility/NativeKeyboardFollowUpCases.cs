using System.Collections;
using System.Text;
using NUnit.Framework;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
public sealed class NativeKeyboardFollowUpCases
{
    [Test]
    [Property("FishAudioCaseId", "native-key-follow-up/latched-mismatch-captures-only-owned-down-bits")]
    public void LaterAgreementCannotReplaceTheLatchedShiftOrAltFailure() => WithoutConsole(() =>
    {
        var samples = new[]
        {
            (Key: 0x10, Planned: false, FirstNative: true, LaterNative: (short)0x0001,
                DownKeys: new[] { 0x01, 0xA0, 0xA2, 0xA4 }, Mask: (ushort)0x0125, LaterTable: false),
            (Key: 0x12, Planned: true, FirstNative: false, LaterNative: unchecked((short)0x8001),
                DownKeys: new[] { 0x10, 0xA1, 0x11, 0xA3, 0x12, 0xA5 }, Mask: (ushort)0x02DA, LaterTable: true),
        };
        foreach (var sample in samples)
        {
            var primary = new MetadataThrowingException();
            var first = new NativeKeyboardFirstMismatch(primary, NativeKeyboardFollowUpPhase.BeforeAction,
                sample.Key, sample.Planned, sample.Planned, sample.FirstNative, 31, 47);
            var api = new FollowUpKeyboardApi(CreateState(sample.DownKeys)) { NativeState = sample.LaterNative };

            var result = NativeKeyboardFollowUp.Capture(api, first);

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(NativeKeyboardFollowUpStatus.Captured));
                Assert.That(ReferenceEquals(result.FirstMismatch, first), Is.True);
                Assert.That(ReferenceEquals(result.FirstMismatch.PrimaryFailure, primary), Is.True);
                Assert.That(result.FirstMismatch.Phase, Is.EqualTo(NativeKeyboardFollowUpPhase.BeforeAction));
                Assert.That(result.FirstMismatch.Key, Is.EqualTo(sample.Key));
                Assert.That(result.FirstMismatch.PlannedDown, Is.EqualTo(sample.Planned));
                Assert.That(result.FirstMismatch.TableDown, Is.EqualTo(sample.Planned));
                Assert.That(result.FirstMismatch.NativeDown, Is.EqualTo(sample.FirstNative));
                Assert.That(result.FirstMismatch.ManagedThreadId, Is.EqualTo(31));
                Assert.That(result.FirstMismatch.NativeThreadId, Is.EqualTo(47u));
                Assert.That(result.ControlledDownMask, Is.EqualTo(sample.Mask));
                Assert.That(result.TableDown, Is.EqualTo(sample.LaterTable));
                Assert.That(result.NativeDown, Is.EqualTo(sample.Planned), "Later agreement remains diagnostic; the first mismatch is unchanged.");
                Assert.That(api.Calls, Is.EqualTo(new[] { "read", "key" }));
                Assert.That(api.RequestedKeys, Is.EqualTo(new[] { sample.Key }));
                Assert.That(api.UnexpectedCalls, Is.Zero);
                Assert.That(primary.MessageReads, Is.Zero);
                Assert.That(primary.DataReads, Is.Zero);
            });
            Array.Fill(api.LastReadBuffer!, (byte)0);
            Assert.That(result.ControlledDownMask, Is.EqualTo(sample.Mask), "The result cannot retain or reread the full native buffer.");
            Assert.That(result.TableDown, Is.EqualTo(sample.LaterTable));
        }
    });

    [Test]
    [Property("FishAudioCaseId", "native-key-follow-up/unavailable-or-rejected-keeps-primary-private")]
    public void FailedOrUnapprovedFollowUpCannotInspectExceptionsOrExpandSampling() => WithoutConsole(() =>
    {
        var primary = new MetadataThrowingException();
        var samplingFailure = new MetadataThrowingException();
        var first = new NativeKeyboardFirstMismatch(primary, NativeKeyboardFollowUpPhase.BeforeAction,
            0x10, false, false, true, 31, 47);
        for (var fault = 0; fault < 3; fault++)
        {
            var api = new FollowUpKeyboardApi(CreateState([0x10]));
            if (fault == 0) api.ReadSucceeded = false;
            if (fault == 1) api.ReadFailure = samplingFailure;
            if (fault == 2) api.KeyFailure = samplingFailure;

            var result = NativeKeyboardFollowUp.Capture(api, first);

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(NativeKeyboardFollowUpStatus.Unavailable));
                Assert.That(ReferenceEquals(result.FirstMismatch, first), Is.True);
                Assert.That(ReferenceEquals(result.FirstMismatch.PrimaryFailure, primary), Is.True);
                Assert.That(result.ControlledDownMask, Is.Null);
                Assert.That(result.TableDown, Is.Null);
                Assert.That(result.NativeDown, Is.Null);
                Assert.That(api.Calls, Is.EqualTo(fault == 2 ? new[] { "read", "key" } : new[] { "read" }));
                Assert.That(api.RequestedKeys, Is.EqualTo(fault == 2 ? new[] { 0x10 } : Array.Empty<int>()));
                Assert.That(api.UnexpectedCalls, Is.Zero);
            });
        }

        var rejectedInputs = new[]
        {
            (Key: -1, Planned: false, Table: false, Native: true, Phase: NativeKeyboardFollowUpPhase.BeforeAction),
            (Key: 0x20, Planned: false, Table: false, Native: true, Phase: NativeKeyboardFollowUpPhase.BeforeAction),
            (Key: 0xFF, Planned: false, Table: false, Native: true, Phase: NativeKeyboardFollowUpPhase.BeforeAction),
            (Key: 0x10, Planned: false, Table: false, Native: false, Phase: NativeKeyboardFollowUpPhase.BeforeAction),
            (Key: 0x12, Planned: true, Table: true, Native: true, Phase: NativeKeyboardFollowUpPhase.BeforeAction),
            (Key: 0x10, Planned: false, Table: true, Native: true, Phase: NativeKeyboardFollowUpPhase.BeforeAction),
            (Key: 0x10, Planned: false, Table: false, Native: true, Phase: (NativeKeyboardFollowUpPhase)42),
        };
        foreach (var input in rejectedInputs)
        {
            var rejected = new NativeKeyboardFirstMismatch(primary, input.Phase,
                input.Key, input.Planned, input.Table, input.Native, 31, 47);
            var api = new FollowUpKeyboardApi(CreateState([0x10]));

            var result = NativeKeyboardFollowUp.Capture(api, rejected);

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(NativeKeyboardFollowUpStatus.Rejected));
                Assert.That(ReferenceEquals(result.FirstMismatch, rejected), Is.True);
                Assert.That(ReferenceEquals(result.FirstMismatch.PrimaryFailure, primary), Is.True);
                Assert.That(result.ControlledDownMask, Is.Null);
                Assert.That(result.TableDown, Is.Null);
                Assert.That(result.NativeDown, Is.Null);
                Assert.That(api.Calls, Is.Empty);
                Assert.That(api.RequestedKeys, Is.Empty);
                Assert.That(api.UnexpectedCalls, Is.Zero);
            });
        }
        Assert.Multiple(() =>
        {
            Assert.That(primary.MessageReads, Is.Zero);
            Assert.That(primary.DataReads, Is.Zero);
            Assert.That(samplingFailure.MessageReads, Is.Zero);
            Assert.That(samplingFailure.DataReads, Is.Zero);
        });
    });

    private static byte[] CreateState(int[] downKeys)
    {
        var state = Enumerable.Repeat((byte)0xFF, 256).ToArray();
        // Every low bit and every unowned slot is hostile to retaining more than Down bits.
        foreach (var key in new[] { 0x01, 0x10, 0xA0, 0xA1, 0x11, 0xA2, 0xA3, 0x12, 0xA4, 0xA5 }) state[key] = 0x7F;
        foreach (var key in downKeys) state[key] = 0xFF;
        return state;
    }

    private static void WithoutConsole(Action body)
    {
        var previousOut = Console.Out;
        var previousError = Console.Error;
        using var writer = new CountingWriter();
        try
        {
            Console.SetOut(writer);
            Console.SetError(writer);
            body();
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
        Assert.That(writer.Calls, Is.Zero, "Follow-up capture must return safe values without publishing diagnostics.");
    }

    private sealed class CountingWriter : TextWriter
    {
        internal int Calls;
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) => Calls++;
        public override void Write(string? value) => Calls++;
        public override void WriteLine(string? value) => Calls++;
    }

    private sealed class MetadataThrowingException : Exception
    {
        internal int MessageReads;
        internal int DataReads;
        public override string Message { get { MessageReads++; throw new InvalidOperationException("External exception text must not be inspected."); } }
        public override IDictionary Data { get { DataReads++; throw new InvalidOperationException("External exception metadata must not be inspected."); } }
    }

    private sealed class FollowUpKeyboardApi(byte[] state) : INativeKeyboardStateApi
    {
        internal readonly List<string> Calls = [];
        internal readonly List<int> RequestedKeys = [];
        internal byte[]? LastReadBuffer;
        internal bool ReadSucceeded = true;
        internal Exception? ReadFailure;
        internal Exception? KeyFailure;
        internal short NativeState;
        internal int UnexpectedCalls;

        public bool Read(byte[] destination)
        {
            Calls.Add("read");
            if (ReadFailure is not null) throw ReadFailure;
            if (!ReadSucceeded) return false;
            state.CopyTo(destination, 0);
            LastReadBuffer = destination;
            return true;
        }
        public short KeyState(int key)
        {
            Calls.Add("key");
            RequestedKeys.Add(key);
            if (KeyFailure is not null) throw KeyFailure;
            return NativeState;
        }
        public void VerifyContext() { UnexpectedCalls++; throw new InvalidOperationException("Follow-up must not revisit WPF or context validation."); }
        public bool Write(byte[] destination) { UnexpectedCalls++; throw new InvalidOperationException("Follow-up cannot modify native state."); }
        public void VerifyWpfState(NativeKeyboardCondition condition) { UnexpectedCalls++; throw new InvalidOperationException("Follow-up cannot query WPF state."); }
    }
}
