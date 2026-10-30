using System.Globalization;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
public sealed class NativeKeyboardFollowUpIntegrationCases
{
    [Test]
    [Property("FishAudioCaseId", "native-key-follow-up/default-transaction-does-not-resample")]
    public void DefaultTransactionKeepsTheOriginalSamplingAndRestoration()
    {
        foreach (var useExplicitOptOut in new[] { false, true })
        {
            var api = new TransactionApi();
            var actions = 0;
            var error = ObserveFailure(api, () =>
            {
                if (useExplicitOptOut)
                    NativeKeyboardStateFixture.Run(api, () => actions++, NativeKeyboardCondition.Neutral, null);
                else NativeKeyboardStateFixture.Run(api, () => actions++);
            });
            Assert.Multiple(() =>
            {
                Assert.That(error.Data.Contains(NativeKeyboardFollowUp.EvidenceKey), Is.False);
                Assert.That(api.ReadCount, Is.EqualTo(3));
                Assert.That(api.Keys, Is.EqualTo(new[] { 0x01, 0x10 }));
                Assert.That(api.Writes, Has.Count.EqualTo(2));
                Assert.That(api.Writes[1], Is.EqualTo(api.Original));
                Assert.That(api.State, Is.EqualTo(api.Original));
                Assert.That(api.WpfCalls, Is.Zero);
                Assert.That(actions, Is.Zero);
            });
        }
    }

    [Test]
    [Property("FishAudioCaseId", "native-key-follow-up/opt-in-never-recovers-first-failure")]
    public void OptionalAgreementOrSamplingFailureCannotRecoverTheFirstMismatch()
    {
        foreach (var fault in new[] { "none", "read", "restore" })
        {
            var api = new TransactionApi { FailFollowUpRead = fault == "read", FailRestore = fault == "restore" };
            var actions = 0;
            var error = ObserveFailure(api, () => NativeKeyboardStateFixture.Run(api, () => actions++,
                NativeKeyboardCondition.Neutral, new(31, 47)));
            var observation = error.Data[NativeKeyboardFollowUp.EvidenceKey] as NativeKeyboardFollowUpResult;
            Assert.That(observation, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(ReferenceEquals(observation!.FirstMismatch.PrimaryFailure, error), Is.True);
                Assert.That(observation.FirstMismatch.Key, Is.EqualTo(0x10));
                Assert.That(observation.FirstMismatch.PlannedDown || observation.FirstMismatch.TableDown, Is.False);
                Assert.That(observation.FirstMismatch.NativeDown, Is.True);
                Assert.That(observation.FirstMismatch.ManagedThreadId, Is.EqualTo(31));
                Assert.That(observation.FirstMismatch.NativeThreadId, Is.EqualTo(47u));
                Assert.That(actions, Is.Zero);
                Assert.That(api.WpfCalls, Is.Zero);
                Assert.That(api.Writes, Has.Count.EqualTo(2));
                Assert.That(api.Writes[1], Is.EqualTo(api.Original));
                Assert.That(error.Data[NativeKeyboardStateFixture.FailureContextKey], Is.EqualTo(
                    "phase=before-action; nativeKey=0x10; plannedDown=False; tableDown=False; nativeDown=True"));
            });
            if (fault == "read")
            {
                Assert.That(observation!.Status, Is.EqualTo(NativeKeyboardFollowUpStatus.Unavailable));
                Assert.That(observation.ControlledDownMask, Is.Null);
                Assert.That(api.Keys, Is.EqualTo(new[] { 0x01, 0x10 }));
            }
            else
            {
                Assert.Multiple(() =>
                {
                    Assert.That(observation!.Status, Is.EqualTo(NativeKeyboardFollowUpStatus.Captured));
                    Assert.That(observation.ControlledDownMask, Is.Zero);
                    Assert.That(observation.TableDown, Is.False);
                    Assert.That(observation.NativeDown, Is.False, "Later agreement does not repair the first mismatch.");
                    Assert.That(api.Keys, Is.EqualTo(new[] { 0x01, 0x10, 0x10 }));
                });
            }
            var firstMismatch = api.Events.IndexOf("key-10");
            Assert.That(api.Events.Skip(firstMismatch + 1).Take(fault == "read" ? 1 : 2),
                Is.EqualTo(fault == "read" ? new[] { "read" } : new[] { "read", "key-10" }),
                "The single follow-up precedes Console, formatting, WPF, restoration and other diagnostics.");
            Assert.That(api.ReadCount, Is.EqualTo(fault == "restore" ? 3 : 4));
            if (fault == "restore")
                Assert.That(error.Data[NativeKeyboardStateFixture.CleanupFailureKey], Is.TypeOf<InvalidOperationException>());
            else Assert.That(api.State, Is.EqualTo(api.Original));
        }
    }

    [Test]
    [Property("FishAudioCaseId", "native-key-follow-up/sidecar-keeps-failed-child-and-private-data")]
    public async Task OptionalProjectionIsPublishedOnlyAfterTheFailedChildOutcome()
    {
        var directory = Path.GetFullPath(TestAssertions.CreateTempDirectory());
        var primaryFailure = false;
        const string secret = "follow-up-private-exception-and-payload";
        try
        {
            var request = new RegressionChildRequest
            {
                RunId = Guid.NewGuid().ToString("N"), Partition = "core",
                CaseIds = ["infra.native-follow-up/first", "infra.native-follow-up/not-run"],
                Configuration = RegressionReport.BuildMetadata("RegressionBuildConfiguration"),
                Sdk = RegressionReport.BuildMetadata("RegressionBuildSdk"),
                Source = new() { Available = true, Snapshot = true, Commit = new('a', 40), Tree = new('b', 40), Fingerprint = new('c', 64) },
                ManifestPath = Path.Combine(directory, "optional.child.json")
            };
            RegressionProcessProtocol.ValidateRequest(request);
            var api = new TransactionApi();
            var calls = 0;
            using var protocol = new StringWriter(CultureInfo.InvariantCulture);
            var exit = await RegressionChildHost.RunCasesAsync(request, protocol, _ =>
            {
                calls++;
                var error = ObserveFailure(api, () => NativeKeyboardStateFixture.Run(api,
                    () => throw new InvalidOperationException("The failed preparation must not enter its action."),
                    NativeKeyboardCondition.Neutral, new(31, 47)));
                error.Data[secret] = new PrivatePayload();
                throw error;
            });
            var manifest = RegressionProcessProtocol.Read<RegressionChildManifest>(request.ManifestPath);
            RegressionProcessProtocol.ValidateManifest(manifest, request);
            Assert.Multiple(() =>
            {
                Assert.That(exit, Is.EqualTo(1));
                Assert.That(manifest.Final, Is.True);
                Assert.That(manifest.Complete, Is.False);
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(manifest.Cases[0].Outcome, Is.EqualTo("failed"));
                Assert.That(manifest.Cases[1].Outcome, Is.EqualTo("not-run"));
                Assert.That(protocol.ToString(), Does.Contain("\"kind\":\"final\""));
                Assert.That(api.State, Is.EqualTo(api.Original));
            });
            var bytes = File.ReadAllBytes(RegressionFailureEvidence.SidecarPath(request, 0));
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            Assert.That(root.GetProperty("version").GetInt32(), Is.EqualTo(2), "optional-follow-up-sidecar-must-be-version-two");
            Assert.That(root.GetProperty("requestId").GetString(), Is.EqualTo(request.RequestId));
            Assert.That(root.GetProperty("runId").GetString(), Is.EqualTo(request.RunId));
            Assert.That(root.GetProperty("caseId").GetString(), Is.EqualTo(request.CaseIds[0]));
            var info = root.GetProperty("failure").GetProperty("nativeKeyboardFollowUp");
            Assert.That(info.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(new[]
            {
                "version", "phase", "key", "plannedDown", "firstTableDown", "firstNativeDown",
                "managedThreadId", "nativeThreadId", "status", "controlledDownMask", "tableDown", "nativeDown"
            }));
            Assert.Multiple(() =>
            {
                Assert.That(info.GetProperty("version").GetInt32(), Is.EqualTo(1));
                Assert.That(info.GetProperty("phase").GetString(), Is.EqualTo("before-action"));
                Assert.That(info.GetProperty("key").GetInt32(), Is.EqualTo(0x10));
                Assert.That(info.GetProperty("plannedDown").GetBoolean(), Is.False);
                Assert.That(info.GetProperty("firstTableDown").GetBoolean(), Is.False);
                Assert.That(info.GetProperty("firstNativeDown").GetBoolean(), Is.True);
                Assert.That(info.GetProperty("managedThreadId").GetInt32(), Is.EqualTo(31));
                Assert.That(info.GetProperty("nativeThreadId").GetUInt32(), Is.EqualTo(47u));
                Assert.That(info.GetProperty("status").GetString(), Is.EqualTo("captured"));
                Assert.That(info.GetProperty("controlledDownMask").GetUInt16(), Is.Zero);
                Assert.That(info.GetProperty("tableDown").GetBoolean(), Is.False);
                Assert.That(info.GetProperty("nativeDown").GetBoolean(), Is.False);
                Assert.That(Encoding.UTF8.GetString(bytes), Does.Not.Contain(secret));
            });
        }
        catch { primaryFailure = true; throw; }
        finally
        {
            try
            {
                if (!string.Equals(Path.GetDirectoryName(directory), Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FishAudioTests")), StringComparison.OrdinalIgnoreCase) ||
                    !Guid.TryParseExact(Path.GetFileName(directory), "N", out _) ||
                    (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("native-follow-up-fixture-owner");
                Directory.Delete(directory, recursive: true);
            }
            catch when (primaryFailure) { /* Keep the original assertion when cleanup also fails. */ }
        }
    }

    private static InvalidOperationException ObserveFailure(TransactionApi api, TestDelegate action)
    {
        var previous = Console.Error;
        using var writer = new TraceWriter(api.Events);
        try
        {
            Console.SetError(writer);
            return Assert.Throws<InvalidOperationException>(action)!;
        }
        finally { Console.SetError(previous); }
    }

    private sealed class TraceWriter(List<string> events) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value) => events.Add("console");
    }

    private sealed class PrivatePayload
    {
        public override string ToString() => throw new InvalidOperationException("A private diagnostic object cannot be formatted.");
    }

    private sealed class TransactionApi : INativeKeyboardStateApi
    {
        internal byte[] Original { get; } = MakeOriginal();
        internal byte[] State { get; private set; } = MakeOriginal();
        internal List<string> Events { get; } = [];
        internal List<int> Keys { get; } = [];
        internal List<byte[]> Writes { get; } = [];
        internal int ReadCount;
        internal int WpfCalls;
        internal bool FailFollowUpRead;
        internal bool FailRestore;
        public void VerifyContext() => Events.Add("context");
        public bool Read(byte[] destination)
        {
            Events.Add("read");
            ReadCount++;
            if (ReadCount == 3 && FailFollowUpRead) throw new IOException("optional-read-unavailable");
            State.CopyTo(destination, 0);
            return true;
        }
        public bool Write(byte[] state)
        {
            Events.Add("write");
            Writes.Add((byte[])state.Clone());
            if (Writes.Count == 2 && FailRestore) return false;
            State = (byte[])state.Clone();
            return true;
        }
        public short KeyState(int key)
        {
            Events.Add("key-" + key.ToString("X2", CultureInfo.InvariantCulture));
            Keys.Add(key);
            return key == 0x10 && Keys.Count == 2 ? unchecked((short)0x8000) : (short)0;
        }
        public void VerifyWpfState(NativeKeyboardCondition condition) { WpfCalls++; }
        private static byte[] MakeOriginal()
        {
            var state = new byte[256];
            state[0x10] = 0x80;
            state[0x20] = 0x01;
            return state;
        }
    }
}
