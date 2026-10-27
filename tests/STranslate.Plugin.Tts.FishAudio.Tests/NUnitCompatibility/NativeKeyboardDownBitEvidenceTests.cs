using NUnit.Framework;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
public sealed class NativeKeyboardDownBitEvidenceTests
{
    private const string DiagnosticPrefix = "[native-key-state] ";
    private const string SecondaryCleanupLine = DiagnosticPrefix + "secondary cleanup failure (InvalidOperationException)";

    [Test]
    [Property("FishAudioCaseId", "native-key-evidence/existing-transaction-guards")]
    public void ExistingTransactionGuardsRemainRequired() => CaptureExpectedError(
        NativeKeyboardStateSuite.TransactionsPreserveStateAndFailures,
        DiagnosticPrefix + "phase=before-action; controlledChangedCount=0; controlledChanges=[]; spaceChanged=False; otherChangedCount=1",
        DiagnosticPrefix + "phase=before-action; nativeKey=0x01; plannedDown=False; tableDown=False; nativeDown=True",
        DiagnosticPrefix + "phase=restore; controlledChangedCount=0; controlledChanges=[]; spaceChanged=False; otherChangedCount=1",
        SecondaryCleanupLine,
        SecondaryCleanupLine);

    [Test]
    [Property("FishAudioCaseId", "native-key-evidence/existing-private-context-guards")]
    public void ExistingPrivateContextGuardsRemainRequired() => CaptureExpectedError(NativeKeyboardStateSuite.FailureContextIsBoundedAndPrivate);

    [Test]
    [Property("FishAudioCaseId", "native-key-evidence/existing-owned-bit-guards")]
    public void ExistingOwnedBitGuardsRemainRequired() => CaptureExpectedError(NativeKeyboardStateSuite.PostActionChecksOnlyOwnedBitsAndRestoresEverything);

    [Test]
    [Property("FishAudioCaseId", "native-key-evidence/existing-cleanup-primary-guards")]
    public void ExistingCleanupPrimaryGuardsRemainRequired() => CaptureExpectedError(NativeKeyboardStateSuite.CleanupMetadataCannotReplacePrimaryFailure);

    [TestCase(false, 0x01, false, 1,
        "phase=before-action; nativeKey=0x01; plannedDown=False; tableDown=False; nativeDown=True",
        TestName = "native-key-evidence/before-action-rejects-native-down-bit")]
    [TestCase(true, 0xA4, false, 19,
        "phase=after-action; nativeKey=0xA4; plannedDown=False; tableDown=False; nativeDown=True",
        TestName = "native-key-evidence/after-action-rejects-native-down-bit")]
    [TestCase(false, 0x12, true, 8,
        "phase=before-action; nativeKey=0x12; plannedDown=True; tableDown=True; nativeDown=False",
        TestName = "native-key-evidence/alt-planned-down-rejects-native-up-bit")]
    public void NativeMismatchRetainsOnlyTheExistingObservationsAndRestoresTheFullTable(
        bool afterAction, int key, bool altOnly, int expectedKeyCalls, string expectedContext)
    {
        var api = new FakeKeyboardApi(afterAction ? 3 : 2, key);
        var original = (byte[])api.State.Clone();
        var actions = 0;
        InvalidOperationException? failure = null;
        CaptureExpectedError(() => failure = Assert.Throws<InvalidOperationException>(() => NativeKeyboardStateFixture.Run(api, () =>
            {
                actions++;
                api.State[0xFE] ^= 1; // An unowned action update still belongs to full restoration.
            }, altOnly ? NativeKeyboardCondition.AltOnly : NativeKeyboardCondition.Neutral)), DiagnosticPrefix + expectedContext);

        Assert.That(failure!.Message, Is.EqualTo("Native keyboard fixture failed at native down-bit mismatch."));
        Assert.That(failure.Data[NativeKeyboardStateFixture.FailureContextKey], Is.EqualTo(expectedContext));
        Assert.That(RegressionFailureEvidence.Capture(failure).NativeKeyboardContext, Is.EqualTo(expectedContext));
        Assert.That(actions, Is.EqualTo(afterAction ? 1 : 0));
        Assert.That(api.State, Is.EqualTo(original));
        Assert.That(api.LastWritten, Is.EqualTo(original));
        Assert.That(api.Reads, Is.EqualTo(afterAction ? 4 : 3));
        Assert.That(api.Writes, Is.EqualTo(2));
        Assert.That(api.KeyCalls, Is.EqualTo(expectedKeyCalls));
        Assert.That(api.WpfChecks, Is.EqualTo(afterAction ? 1 : 0));
        Assert.That(api.ContextChecks, Is.EqualTo(afterAction ? 4 : 3));
    }

    [Test]
    [Property("FishAudioCaseId", "native-key-evidence/cleanup-failure-keeps-native-primary-context")]
    public void FailedRestorationCannotReplaceTheNativeMismatchOrItsContext()
    {
        var api = new FakeKeyboardApi(2, 0x01) { FailRestoration = true };
        var original = (byte[])api.State.Clone();
        var actions = 0;
        const string context = "phase=before-action; nativeKey=0x01; plannedDown=False; tableDown=False; nativeDown=True";
        InvalidOperationException? failure = null;
        CaptureExpectedError(() => failure = Assert.Throws<InvalidOperationException>(() => NativeKeyboardStateFixture.Run(api, () => actions++)),
            DiagnosticPrefix + context, SecondaryCleanupLine);

        Assert.That(failure!.Message, Is.EqualTo("Native keyboard fixture failed at native down-bit mismatch."));
        Assert.That(failure.Data[NativeKeyboardStateFixture.FailureContextKey], Is.EqualTo(context));
        Assert.That(RegressionFailureEvidence.Capture(failure).NativeKeyboardContext, Is.EqualTo(context));
        var cleanup = failure.Data[NativeKeyboardStateFixture.CleanupFailureKey];
        Assert.That(cleanup, Is.TypeOf<InvalidOperationException>().And.Not.SameAs(failure));
        var secondary = (Exception)cleanup!;
        Assert.That(secondary.Message, Is.EqualTo("Native keyboard fixture failed at restore."));
        Assert.That(secondary.Data[NativeKeyboardStateFixture.FailureContextKey], Is.Null);
        Assert.That(actions, Is.Zero);
        Assert.That(api.LastWritten, Is.EqualTo(original), "The single restoration attempt receives the entire saved table.");
        Assert.That(api.Reads, Is.EqualTo(2), "A failed restore write does not add readback or retry calls.");
        Assert.That(api.Writes, Is.EqualTo(2));
        Assert.That(api.KeyCalls, Is.EqualTo(1));
        Assert.That(api.WpfChecks, Is.Zero);
        Assert.That(api.ContextChecks, Is.EqualTo(3));
    }

    [TestCase("phase=before-action; nativeKey=0xFF; plannedDown=False; tableDown=False; nativeDown=True",
        TestName = "native-key-evidence/reject-unapproved-key-identity")]
    [TestCase("phase=before-action; nativeKey=0x20; plannedDown=False; tableDown=False; nativeDown=True",
        TestName = "native-key-evidence/reject-space-key-identity")]
    [TestCase("phase=before-action; nativeKey=0x01; plannedDown=False; tableDown=False; nativeDown=True\nprivate text",
        TestName = "native-key-evidence/reject-newline-payload")]
    [TestCase(" phase=before-action; nativeKey=0x01; plannedDown=False; tableDown=False; nativeDown=True",
        TestName = "native-key-evidence/reject-noncanonical-whitespace")]
    [TestCase("phase=before-action; nativeKey=0x01; plannedDown=False; tableDown=True; nativeDown=True",
        TestName = "native-key-evidence/reject-contradictory-table-bit")]
    [TestCase("phase=after-action; nativeKey=0xA4; plannedDown=False; tableDown=False; nativeDown=False",
        TestName = "native-key-evidence/reject-absent-native-mismatch")]
    [TestCase("phase=restore; nativeKey=0x01; plannedDown=False; tableDown=False; nativeDown=True",
        TestName = "native-key-evidence/reject-unobserved-native-phase")]
    public void FailureSidecarDoesNotPublishHostileOrContradictoryNativeContext(string context)
    {
        CaptureExpectedError(() =>
        {
            var primary = new InvalidOperationException("synthetic native mismatch");
            primary.Data[NativeKeyboardStateFixture.FailureContextKey] = context;
            var captured = RegressionFailureEvidence.Capture(primary);
            Assert.That(captured.NativeKeyboardContext, Is.Null);
            Assert.That(primary.Data[NativeKeyboardStateFixture.FailureContextKey], Is.SameAs(context));
            Assert.That(primary.Message, Is.EqualTo("synthetic native mismatch"));
        });
    }

    private static void CaptureExpectedError(Action action, params string[] expectedLines)
    {
        var previous = Console.Error;
        using var captured = new StringWriter();
        try
        {
            Console.SetError(captured);
            action();
        }
        finally { Console.SetError(previous); }
        var expected = string.Concat(expectedLines.Select(line => line + Environment.NewLine));
        // Exact per-call transcripts reject extra lines, unknown diagnostic text
        // and unexpected secondary types without disclosing captured payloads.
        Assert.That(string.Equals(captured.ToString(), expected, StringComparison.Ordinal), Is.True,
            "Expected-negative stderr must match only this transaction's documented safe transcript.");
    }

    // No WPF objects, native calls, dispatcher work or global API overrides.
    // Expected stderr is scoped/restored by this nonparallel fixture above.
    private sealed class FakeKeyboardApi(int mismatchRead, int mismatchKey) : INativeKeyboardStateApi
    {
        internal byte[] State = Enumerable.Range(0, 256).Select(index => (byte)((index * 17 + 3) & 0xFF)).ToArray();
        internal byte[]? LastWritten;
        internal bool FailRestoration;
        internal int Reads;
        internal int Writes;
        internal int KeyCalls;
        internal int WpfChecks;
        internal int ContextChecks;

        public void VerifyContext() => ContextChecks++;
        public bool Read(byte[] state)
        {
            Reads++;
            State.CopyTo(state, 0);
            return true;
        }
        public bool Write(byte[] state)
        {
            Writes++;
            LastWritten = (byte[])state.Clone();
            if (FailRestoration && Writes == 2) return false;
            State = (byte[])state.Clone();
            return true;
        }
        public short KeyState(int key)
        {
            KeyCalls++;
            var bits = State[key] << 8;
            if (Reads == mismatchRead && key == mismatchKey) bits ^= 0x8000;
            return unchecked((short)bits);
        }
        public void VerifyWpfState(NativeKeyboardCondition condition) => WpfChecks++;
    }
}
