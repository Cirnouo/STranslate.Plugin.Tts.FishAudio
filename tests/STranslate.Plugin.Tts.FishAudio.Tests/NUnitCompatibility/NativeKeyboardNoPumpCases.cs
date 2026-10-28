using System.Runtime.ExceptionServices;
using System.Windows.Interop;
using System.Windows.Threading;
using NUnit.Framework;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
public sealed class NativeKeyboardNoPumpCases
{
    [Test]
    [Property("FishAudioCaseId", "native-key-no-pump/rejects-nested-frame-and-restores")]
    public Task NestedFrameCannotRunAnOwnedCallbackInsideTheKeyboardTransaction() => OnOwnedSourceAsync(async source =>
    {
        var api = new TransactionApi(source);
        var frame = new DispatcherFrame();
        DispatcherOperation? queued = null;
        InvalidOperationException? firstFrameException = null;
        Exception? observed = null;
        ExceptionDispatchInfo? primary = null;
        ExceptionDispatchInfo? cleanup = null;
        var callbacks = 0;
        var mutations = 0;
        var afterScope = 0;
        var frameReturned = false;
        api.PlannedRead = () =>
        {
            queued = source.Dispatcher.InvokeAsync(() =>
            {
                callbacks++;
                if (api.TransactionActive)
                {
                    api.State[0x01] |= 0x80;
                    mutations++;
                }
                frame.Continue = false;
            });
            try
            {
                Dispatcher.PushFrame(frame);
                frameReturned = true;
            }
            catch (InvalidOperationException error)
            {
                firstFrameException = error;
                throw;
            }
        };
        try
        {
            try { NativeKeyboardStateFixture.RunOnSource(source, api, () => api.Actions++); }
            catch (Exception error) { observed = error; }

            Assert.That(firstFrameException, Is.Not.Null, "native-key-no-pump-must-reject-nested-frame");
            Assert.Multiple(() =>
            {
                Assert.That(observed, Is.SameAs(firstFrameException), "Restoration must preserve the actual WPF frame-rejection object.");
                Assert.That(frameReturned, Is.False);
                Assert.That(queued, Is.Not.Null);
                Assert.That(mutations, Is.Zero);
                Assert.That(api.Actions, Is.Zero);
                Assert.That(api.ReadCount, Is.EqualTo(3));
                Assert.That(api.ContextCalls, Is.EqualTo(3));
                Assert.That(api.Keys, Is.Empty);
                Assert.That(api.WpfConditions, Is.Empty);
            });
            AssertRestored(api);
        }
        catch (Exception error) { primary = ExceptionDispatchInfo.Capture(error); }
        finally
        {
            // Join this exact operation before disposing the source. After the
            // guard has released, it may already have run outside the transaction;
            // no assumption about a later callback's Pending state is needed.
            api.TransactionActive = false;
            try { if (queued is not null) await queued.Task; }
            catch (Exception error) { cleanup = ExceptionDispatchInfo.Capture(error); }
            try { await source.Dispatcher.InvokeAsync(() => afterScope++).Task; }
            catch (Exception error) { cleanup ??= ExceptionDispatchInfo.Capture(error); }
        }
        primary?.Throw();
        cleanup?.Throw();
        Assert.Multiple(() =>
        {
            Assert.That(callbacks, Is.EqualTo(1), "Cleanup joins the original queued operation rather than abandoning or replacing it.");
            Assert.That(mutations, Is.Zero, "The late callback cannot modify the restored fake table.");
            Assert.That(afterScope, Is.EqualTo(1), "Dispatcher processing must be available after the transaction scope.");
        });
        AssertRestored(api);
    });

    [Test]
    [Property("FishAudioCaseId", "native-key-no-pump/preserves-strict-mismatch-and-success")]
    public Task StrictMismatchAndSuccessfulTransactionKeepTheirOriginalContracts() => OnOwnedSourceAsync(async source =>
    {
        var failed = new TransactionApi(source) { FirstQueryDown = true };
        Exception? observed = null;
        var previousError = Console.Error;
        using var expectedError = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            Console.SetError(expectedError);
            try
            {
                NativeKeyboardStateFixture.RunOnSource(source, failed, () => failed.Actions++,
                    NativeKeyboardCondition.Neutral, new NativeKeyboardFollowUpIdentity(31, 47));
            }
            catch (Exception error) { observed = error; }
        }
        finally { Console.SetError(previousError); }

        Assert.That(observed, Is.TypeOf<InvalidOperationException>());
        Assert.That(expectedError.ToString(), Is.EqualTo(
            "[native-key-state] phase=before-action; nativeKey=0x01; plannedDown=False; tableDown=False; nativeDown=True" + Environment.NewLine),
            "Capture only this deliberate fake mismatch; unexpected diagnostic output must still fail the case.");
        var mismatch = (InvalidOperationException)observed!;
        var followUp = mismatch.Data[NativeKeyboardFollowUp.EvidenceKey] as NativeKeyboardFollowUpResult;
        Assert.That(followUp, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(followUp!.FirstMismatch.PrimaryFailure, Is.SameAs(mismatch));
            Assert.That(followUp.FirstMismatch.Phase, Is.EqualTo(NativeKeyboardFollowUpPhase.BeforeAction));
            Assert.That(followUp.FirstMismatch.Key, Is.EqualTo(0x01));
            Assert.That(followUp.FirstMismatch.PlannedDown || followUp.FirstMismatch.TableDown, Is.False);
            Assert.That(followUp.FirstMismatch.NativeDown, Is.True);
            Assert.That(followUp.Status, Is.EqualTo(NativeKeyboardFollowUpStatus.Captured));
            Assert.That(followUp.ControlledDownMask, Is.Zero);
            Assert.That(followUp.TableDown, Is.False);
            Assert.That(followUp.NativeDown, Is.False, "Later agreement cannot recover the original strict mismatch.");
            Assert.That(mismatch.Data[NativeKeyboardStateFixture.FailureContextKey], Is.EqualTo(
                "phase=before-action; nativeKey=0x01; plannedDown=False; tableDown=False; nativeDown=True"));
            Assert.That(failed.Actions, Is.Zero);
            Assert.That(failed.ReadCount, Is.EqualTo(4));
            Assert.That(failed.ContextCalls, Is.EqualTo(3));
            Assert.That(failed.Keys, Is.EqualTo(new[] { 0x01, 0x01 }));
            Assert.That(failed.WpfConditions, Is.Empty);
        });
        AssertRestored(failed);
        var afterFailure = 0;
        await source.Dispatcher.InvokeAsync(() => afterFailure++).Task;
        Assert.That(afterFailure, Is.EqualTo(1));

        var succeeded = new TransactionApi(source);
        NativeKeyboardStateFixture.RunOnSource(source, succeeded, () => succeeded.Actions++);
        Assert.Multiple(() =>
        {
            Assert.That(succeeded.Actions, Is.EqualTo(1), "The action executes once without a retry.");
            Assert.That(succeeded.ReadCount, Is.EqualTo(4));
            Assert.That(succeeded.ContextCalls, Is.EqualTo(4));
            Assert.That(succeeded.Keys, Is.EqualTo(new[]
            {
                0x01, 0x10, 0xA0, 0xA1, 0x11, 0xA2, 0xA3, 0x12, 0xA4, 0xA5,
                0x01, 0x10, 0xA0, 0xA1, 0x11, 0xA2, 0xA3, 0x12, 0xA4, 0xA5
            }));
            Assert.That(succeeded.WpfConditions, Is.EqualTo(new[]
            {
                NativeKeyboardCondition.Neutral, NativeKeyboardCondition.Neutral
            }));
        });
        AssertRestored(succeeded);
        var afterSuccess = 0;
        await source.Dispatcher.InvokeAsync(() => afterSuccess++).Task;
        Assert.That(afterSuccess, Is.EqualTo(1));
    });

    private static Task OnOwnedSourceAsync(Func<HwndSource, Task> action) => StaTestHost.RunOnStaThreadAsync(async () =>
    {
        HwndSource? source = null;
        ExceptionDispatchInfo? primary = null;
        ExceptionDispatchInfo? cleanup = null;
        try
        {
            source = new HwndSource(new HwndSourceParameters("FishAudioNativeKeyboardNoPump")
            {
                Width = 32, Height = 24, WindowStyle = unchecked((int)0x80000000)
            });
            await action(source);
        }
        catch (Exception error) { primary = ExceptionDispatchInfo.Capture(error); }
        finally
        {
            try { source?.Dispose(); }
            catch (Exception error) { cleanup = ExceptionDispatchInfo.Capture(error); }
        }
        primary?.Throw();
        cleanup?.Throw();
    });

    private static void AssertRestored(TransactionApi api)
    {
        var planned = new byte[256];
        planned[0x01] = 0x01;
        planned[0xA0] = 0x01;
        planned[0xA2] = 0x01;
        planned[0x12] = 0x01;
        planned[0xA5] = 0x01;
        planned[0x20] = 0x01;
        planned[0x7B] = 0x12;
        planned[0xFE] = 0x7F;
        Assert.Multiple(() =>
        {
            Assert.That(api.Writes, Is.EqualTo(new[] { planned, api.Original }), "The same transaction applies once and restores all 256 original bytes once.");
            Assert.That(api.State, Is.EqualTo(api.Original));
            Assert.That(api.TransactionActive, Is.False);
        });
    }

    private sealed class TransactionApi(HwndSource source) : INativeKeyboardStateApi
    {
        internal byte[] Original { get; } = MakeOriginal();
        internal byte[] State { get; private set; } = MakeOriginal();
        internal List<byte[]> Writes { get; } = [];
        internal List<int> Keys { get; } = [];
        internal List<NativeKeyboardCondition> WpfConditions { get; } = [];
        internal Action? PlannedRead;
        internal bool TransactionActive;
        internal bool FirstQueryDown;
        internal int ReadCount;
        internal int ContextCalls;
        internal int Actions;

        public void VerifyContext()
        {
            source.Dispatcher.VerifyAccess();
            ContextCalls++;
        }

        public bool Read(byte[] destination)
        {
            ReadCount++;
            State.CopyTo(destination, 0);
            if (ReadCount == 2) PlannedRead?.Invoke();
            return true;
        }

        public bool Write(byte[] state)
        {
            Writes.Add((byte[])state.Clone());
            TransactionActive = Writes.Count == 1;
            State = (byte[])state.Clone();
            return true;
        }

        public short KeyState(int key)
        {
            Keys.Add(key);
            if (FirstQueryDown && Keys.Count == 1) return unchecked((short)0x8000);
            return (State[key] & 0x80) != 0 ? unchecked((short)0x8000) : (short)0;
        }

        public void VerifyWpfState(NativeKeyboardCondition condition) => WpfConditions.Add(condition);

        private static byte[] MakeOriginal()
        {
            var state = new byte[256];
            state[0x01] = 0x81;
            state[0x10] = 0x80;
            state[0xA0] = 0x81;
            state[0xA1] = 0x80;
            state[0x11] = 0x80;
            state[0xA2] = 0x81;
            state[0xA3] = 0x80;
            state[0x12] = 0x81;
            state[0xA4] = 0x80;
            state[0xA5] = 0x81;
            state[0x20] = 0x01;
            state[0x7B] = 0x12;
            state[0xFE] = 0x7F;
            return state;
        }
    }
}
