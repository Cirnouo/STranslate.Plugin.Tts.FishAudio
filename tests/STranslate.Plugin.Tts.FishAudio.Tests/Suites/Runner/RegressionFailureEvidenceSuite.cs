using System.Collections;
using System.Diagnostics;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;

internal static class RegressionFailureEvidenceSuite
{
    private const int MaximumFrames = 12;
    private const string Secret = "failure-evidence-private-canary";
    private static readonly int[] ApprovedControlledKeys = [0x01, 0x10, 0xA0, 0xA1, 0x11, 0xA2, 0xA3, 0x12, 0xA4, 0xA5];
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal static void CapturesTheOwnedThrowAndCallerFrames()
    {
        var error = CaptureOwnedFailure(new InvalidOperationException(Secret));
        var info = RegressionFailureEvidence.Capture(error);

        Require(info.Frames.Any(frame => frame.Type == typeof(RegressionFailureEvidenceSuite).FullName && frame.Method == nameof(ThrowOwnedFailure)),
            "A real failure retains its owned throw helper.");
        Require(info.Frames.Any(frame => frame.Type == typeof(RegressionFailureEvidenceSuite).FullName && frame.Method == nameof(CallOwnedFailure)),
            "The same real exception retains its owned caller.");
        Require(info.Frames.All(frame => frame.Type != typeof(RegressionFailureEvidence).FullName),
            "Capturing evidence does not substitute the capture call stack for the failure stack.");
        Require(info.NativeKeyboardContext is null, "Ordinary failures do not invent native keyboard context.");
        AssertSafeFrames(info);
    }

    internal static void OmitsFrameworkFramesAndBoundsOwnedRecursion()
    {
        Exception frameworkFailure;
        try { ThrowThroughFramework(); throw new InvalidOperationException("Unreachable fixture branch."); }
        catch (FormatException error) { frameworkFailure = error; }
        var frameworkInfo = RegressionFailureEvidence.Capture(frameworkFailure);
        Require(frameworkInfo.Frames.Any(frame => frame.Method == nameof(ThrowThroughFramework)),
            "A framework failure still retains its owned calling boundary.");
        Require(frameworkInfo.Frames.All(frame => frame.Type == typeof(RegressionFailureEvidenceSuite).FullName),
            "Only methods from the current test assembly enter failure evidence.");
        AssertSafeFrames(frameworkInfo);

        Exception recursiveFailure;
        try { ThrowThroughOwnedRecursion(MaximumFrames + 8); throw new InvalidOperationException("Unreachable fixture branch."); }
        catch (InvalidOperationException error) { recursiveFailure = error; }
        var recursiveInfo = RegressionFailureEvidence.Capture(recursiveFailure);
        Require(recursiveInfo.Frames.Count == MaximumFrames,
            "A deeper real owned stack retains a useful prefix within the fixed frame bound.");
        AssertSafeFrames(recursiveInfo);
    }

    internal static void SerializesOnlyApprovedFieldsWithoutPrivateExceptionContent()
    {
        var payload = new HostileDiagnosticPayload();
        var error = CaptureOwnedFailure(new InvalidOperationException(Secret + "-message", new IOException(Secret + "-inner")));
        error.Data[Secret + "-data-key"] = Secret + "-data-value";
        error.Data["unrelated-payload"] = payload;
        error.Data[NativeKeyboardStateFixture.CleanupFailureKey] = new IOException(Secret + "-cleanup");

        var info = RegressionFailureEvidence.Capture(error);
        var json = JsonSerializer.SerializeToElement(info, JsonOptions);
        var fields = json.EnumerateObject().Select(property => property.Name).ToArray();
        Require(fields.Contains("frames", StringComparer.Ordinal) && fields.All(name => name is "frames" or "nativeKeyboardContext"),
            "Failure information exposes only the approved frame and native-context fields.");
        foreach (var frame in json.GetProperty("frames").EnumerateArray())
        {
            var frameFields = frame.EnumerateObject().Select(property => property.Name).ToArray();
            Require(new[] { "line", "type", "method" }.All(name => frameFields.Contains(name, StringComparer.Ordinal)) &&
                frameFields.All(name => name is "sourceFile" or "line" or "type" or "method"),
                "A serialized frame exposes only filename, line and owned method metadata.");
        }
        Require(!json.GetRawText().Contains(Secret, StringComparison.Ordinal),
            "Messages, inner messages, arbitrary Data keys/values and cleanup exception content are absent from serialized evidence.");
        Require(payload.ToStringReads == 0, "Unrelated exception Data is never stringified.");
        AssertSafeFrames(info);
    }

    internal static void RetainsOnlyTheBoundedNativeFixtureGrammar()
    {
        const string emptyChanges = "phase=before-action; controlledChangedCount=0; controlledChanges=[]; spaceChanged=False; otherChangedCount=0";
        var allChanges = "phase=restore; controlledChangedCount=10; controlledChanges=[" +
            string.Join(",", ApprovedControlledKeys.Select(key => $"0x{key:X2}:0x00>0x80(down=False>True,low=0x00>0x00)")) +
            "]; spaceChanged=True; otherChangedCount=245";
        var afterAction = allChanges.Replace("phase=restore", "phase=after-action", StringComparison.Ordinal);
        foreach (var context in new[] { emptyChanges, allChanges, afterAction })
        {
            var error = new InvalidOperationException(Secret);
            error.Data[NativeKeyboardStateFixture.FailureContextKey] = context;
            var info = RegressionFailureEvidence.Capture(error);
            Require(info.NativeKeyboardContext == context && info.NativeKeyboardContext.Length <= NativeKeyboardStateFixture.MaximumFailureContextLength,
                "Each fixed fixture phase retains only the complete bounded approved context.");
        }

        var hostile = new HostileDiagnosticPayload();
        object[] malformed =
        [
            Secret,
            emptyChanges + "\n" + Secret,
            emptyChanges.Replace("before-action", "capture", StringComparison.Ordinal),
            allChanges.Replace("0xA5:", "0x20:", StringComparison.Ordinal),
            allChanges.Replace("controlledChangedCount=10", "controlledChangedCount=11", StringComparison.Ordinal),
            allChanges.Replace("controlledChangedCount=10", "controlledChangedCount=9", StringComparison.Ordinal),
            allChanges.Replace("otherChangedCount=245", "otherChangedCount=246", StringComparison.Ordinal),
            emptyChanges.Replace("otherChangedCount=0", "otherChangedCount=\u0661", StringComparison.Ordinal),
            emptyChanges + "; rawTable=" + Secret,
            new string('x', NativeKeyboardStateFixture.MaximumFailureContextLength + 1),
            hostile
        ];
        foreach (var context in malformed)
        {
            var error = new InvalidOperationException(Secret);
            error.Data[NativeKeyboardStateFixture.FailureContextKey] = context;
            var info = RegressionFailureEvidence.Capture(error);
            Require(info.NativeKeyboardContext is null,
                "Malformed, oversized, unknown-key, inconsistent-count and non-string native metadata is discarded without truncation or coercion.");
            Require(!JsonSerializer.Serialize(info, JsonOptions).Contains(Secret, StringComparison.Ordinal),
                "Rejected native metadata cannot leak through another serialized field.");
            Require(ReferenceEquals(error.Data[NativeKeyboardStateFixture.FailureContextKey], context),
                "Rejecting metadata does not edit the original exception Data.");
        }
        Require(hostile.ToStringReads == 0, "A non-string native context is not coerced with ToString.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void HandlesMissingPhysicalStackWithoutUsingRemoteStackText()
    {
        var unthrown = new InvalidOperationException(Secret);
        var empty = RegressionFailureEvidence.Capture(unthrown);
        Require(empty.Frames.Count == 0, "An exception that has never been thrown has no invented physical failure frames.");
        AssertSafeFrames(empty);

        var remoteOnly = new InvalidOperationException(Secret);
        ExceptionDispatchInfo.SetRemoteStackTrace(remoteOnly, "at " + Secret + " in Z:\\private-fixture\\source.cs:line 123");
        var remote = RegressionFailureEvidence.Capture(remoteOnly);
        Require(remote.Frames.Count == 0, "Remote stack text without physical owned frames is not parsed into evidence.");
        Require(!JsonSerializer.Serialize(remote, JsonOptions).Contains(Secret, StringComparison.Ordinal),
            "Raw remote stack text and its private path are absent from serialized evidence.");
        AssertSafeFrames(remote);

        var withoutPdb = new DynamicMethod("FailureEvidenceWithoutPdb", typeof(void), Type.EmptyTypes, typeof(RegressionFailureEvidenceSuite).Module);
        var generator = withoutPdb.GetILGenerator();
        generator.Emit(OpCodes.Ldstr, Secret);
        generator.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
        generator.Emit(OpCodes.Throw);
        var throwWithoutPdb = (Action)withoutPdb.CreateDelegate(typeof(Action));
        Exception missingPdb;
        try { throwWithoutPdb(); throw new InvalidOperationException("Unreachable fixture branch."); }
        catch (InvalidOperationException error) { missingPdb = error; }
        Require((new StackTrace(missingPdb, true).GetFrames() ?? []).Any(frame =>
            frame.GetMethod()?.Name == "FailureEvidenceWithoutPdb" && frame.GetFileName() is null && frame.GetFileLineNumber() == 0),
            "The fixture supplies a real thrown frame without PDB filename or line metadata.");
        var partial = RegressionFailureEvidence.Capture(missingPdb);
        Require(partial.Frames.Any(frame => frame.Method == nameof(HandlesMissingPhysicalStackWithoutUsingRemoteStackText)),
            "Missing PDB or declaring-type metadata on one frame does not discard the owned calling boundary.");
        Require(!JsonSerializer.Serialize(partial, JsonOptions).Contains(Secret, StringComparison.Ordinal),
            "A thrown frame without PDB metadata does not fall back to private message or raw stack text.");
        AssertSafeFrames(partial);
    }

    internal static void LeavesThePrimaryExceptionAndItsCleanupEvidenceUnchanged()
    {
        var cleanup = new IOException(Secret + "-cleanup");
        var payload = new HostileDiagnosticPayload();
        var primary = new HostileDiagnosticException(new IOException(Secret + "-inner"));
        primary.Data[NativeKeyboardStateFixture.CleanupFailureKey] = cleanup;
        primary.Data["unrelated-payload"] = payload;
        primary.Data[NativeKeyboardStateFixture.FailureContextKey] = "unapproved=" + Secret;
        var initialData = primary.Data.Cast<DictionaryEntry>().ToArray();
        var caught = CaptureOwnedFailure(primary);

        var first = RegressionFailureEvidence.Capture(caught);
        var second = RegressionFailureEvidence.Capture(caught);
        Require(first.Frames.Any(frame => frame.Method == nameof(ThrowOwnedFailure)) && second.Frames.Any(frame => frame.Method == nameof(CallOwnedFailure)),
            "Failure capture retains real owned frames even when arbitrary diagnostic accessors throw.");
        Require(primary.MessageReads == 0 && primary.ToStringReads == 0 && primary.RawStackReads == 0 && payload.ToStringReads == 0,
            "Capture never invokes exception Message, ToString, raw StackTrace or arbitrary Data value formatting.");
        Require(ReferenceEquals(caught, primary) && ReferenceEquals(primary.Data[NativeKeyboardStateFixture.CleanupFailureKey], cleanup),
            "The primary exception and its secondary cleanup exception retain their original identities.");
        Require(primary.Data.Count == initialData.Length && initialData.All(entry => ReferenceEquals(primary.Data[entry.Key], entry.Value)),
            "Capture does not add, remove or replace exception metadata.");

        try { ExceptionDispatchInfo.Capture(primary).Throw(); }
        catch (HostileDiagnosticException rethrown)
        {
            Require(ReferenceEquals(rethrown, primary) && ReferenceEquals(rethrown.Data[NativeKeyboardStateFixture.CleanupFailureKey], cleanup),
                "Evidence capture leaves subsequent propagation of the same primary and cleanup evidence intact.");
            return;
        }
        throw new InvalidOperationException("The preserved primary exception must still propagate.");
    }

    internal static async Task SidecarsRemainSecondaryToTheOriginalOutcomeAsync()
    {
        var directory = Path.GetFullPath(TestAssertions.CreateTempDirectory());
        var primaryFailure = false;
        try
        {
            foreach (var mode in new[] { "success", "failure", "collision" })
            {
                var request = new RegressionChildRequest
                {
                    RunId = Guid.NewGuid().ToString("N"), Partition = "core",
                    CaseIds = mode == "success" ? ["runner.failure-evidence/success"] :
                        ["runner.failure-evidence/" + mode, "runner.failure-evidence/not-run"],
                    Configuration = RegressionReport.BuildMetadata("RegressionBuildConfiguration"),
                    Sdk = RegressionReport.BuildMetadata("RegressionBuildSdk"),
                    Source = new() { Available = true, Snapshot = true, Commit = new('a', 40), Tree = new('b', 40), Fingerprint = new('c', 64) },
                    ManifestPath = Path.Combine(directory, mode + ".child.json")
                };
                RegressionProcessProtocol.ValidateRequest(request);
                var sidecarPath = RegressionFailureEvidence.SidecarPath(request, 0);
                byte[] collision = "existing-owned-sidecar"u8.ToArray();
                if (mode == "collision") File.WriteAllBytes(sidecarPath, collision);
                var error = new InvalidOperationException(Secret);
                var cleanup = new IOException(Secret + "-cleanup");
                error.Data[NativeKeyboardStateFixture.CleanupFailureKey] = cleanup;
                error.Data["private-payload"] = Secret;
                var calls = 0;
                using var protocol = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
                var exit = await RegressionChildHost.RunCasesAsync(request, protocol, _ =>
                {
                    calls++;
                    if (mode != "success") CallOwnedFailure(error);
                    return Task.CompletedTask;
                });
                var manifest = RegressionProcessProtocol.Read<RegressionChildManifest>(request.ManifestPath);
                RegressionProcessProtocol.ValidateManifest(manifest, request);
                if (mode == "success")
                {
                    Require(exit == 0 && manifest.Complete && manifest.Cases[0].Outcome == "passed" && !File.Exists(sidecarPath),
                        "A genuinely successful child callback creates no failure sidecar.");
                    continue;
                }
                Require(exit == 1 && manifest.Final && !manifest.Complete && calls == 1 && manifest.Cases[0].Outcome == "failed" &&
                    manifest.Cases[0].ExceptionType == typeof(InvalidOperationException).FullName && manifest.Cases[0].FailureStage == "case" &&
                    manifest.Cases.Skip(1).All(item => item.Outcome == "not-run"),
                    "Failure evidence never recovers, retries or replaces the original failed child outcome and classification.");
                Require(ReferenceEquals(error.Data[NativeKeyboardStateFixture.CleanupFailureKey], cleanup),
                    "Persisting diagnostics leaves the original cleanup evidence attached to the same failure.");
                if (mode == "collision")
                {
                    Require(File.ReadAllBytes(sidecarPath).SequenceEqual(collision),
                        "A failed WriteNew preserves the existing sidecar while the original case failure remains authoritative.");
                    continue;
                }
                var sidecar = RegressionProcessProtocol.Read<RegressionFailureSidecar>(sidecarPath);
                Require(sidecar.Version == 1 && sidecar.RequestId == request.RequestId && sidecar.RunId == request.RunId &&
                    sidecar.CaseId == request.CaseIds[0] && RegressionProcessProtocol.SameSource(sidecar.Source, request.Source),
                    "The actual child-host failure sidecar belongs to the exact request, run, case and source identity.");
                Require(sidecar.Failure.Frames.Any(frame => frame.Method == nameof(ThrowOwnedFailure)) &&
                    !File.ReadAllText(sidecarPath).Contains(Secret, StringComparison.Ordinal),
                    "Persisted evidence retains the real owned failure frame without private message, Data or cleanup content.");
                AssertSafeFrames(sidecar.Failure);
            }
        }
        catch { primaryFailure = true; throw; }
        finally
        {
            try
            {
                Require(string.Equals(Path.GetDirectoryName(directory), Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FishAudioTests")), StringComparison.OrdinalIgnoreCase) &&
                    Guid.TryParseExact(Path.GetFileName(directory), "N", out _), "Only the owned private fixture directory is removed.");
                Directory.Delete(directory, recursive: true);
            }
            catch when (primaryFailure) { /* Preserve the primary regression failure when cleanup also fails. */ }
        }
    }

    private static void AssertSafeFrames(RegressionFailureInfo info)
    {
        Require(info.Frames.Count <= MaximumFrames, "Failure frame count is bounded.");
        foreach (var frame in info.Frames)
        {
            Require(!string.IsNullOrWhiteSpace(frame.Type) && !string.IsNullOrWhiteSpace(frame.Method),
                "Retained frames identify an owned type and method.");
            Require(frame.Line >= 0 && (frame.SourceFile is not null || frame.Line == 0),
                "Missing source/PDB information uses a null filename and zero line without failing capture.");
            Require(frame.SourceFile is null || frame.SourceFile == Path.GetFileName(frame.SourceFile) &&
                frame.SourceFile.IndexOfAny(['/', '\\', ':']) < 0 && !Path.IsPathFullyQualified(frame.SourceFile),
                "Source evidence contains only a filename, with no absolute or relative directory path.");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception CaptureOwnedFailure(Exception error)
    {
        try { CallOwnedFailure(error); }
        catch (Exception caught) { return caught; }
        throw new InvalidOperationException("The fixture throw helper must throw.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CallOwnedFailure(Exception error)
    {
        ThrowOwnedFailure(error);
        GC.KeepAlive(error);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOwnedFailure(Exception error) => throw error;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowThroughFramework() => _ = int.Parse("fixture-is-not-an-integer", System.Globalization.CultureInfo.InvariantCulture);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowThroughOwnedRecursion(int depth)
    {
        if (depth == 0) throw new InvalidOperationException(Secret);
        ThrowThroughOwnedRecursion(depth - 1);
        GC.KeepAlive(depth);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class HostileDiagnosticPayload
    {
        internal int ToStringReads { get; private set; }
        public override string ToString()
        {
            ToStringReads++;
            throw new InvalidOperationException(Secret + "-payload-format");
        }
    }

    private sealed class HostileDiagnosticException(Exception inner) : Exception(Secret + "-message", inner)
    {
        internal int MessageReads { get; private set; }
        internal int ToStringReads { get; private set; }
        internal int RawStackReads { get; private set; }
        public override string Message
        {
            get { MessageReads++; throw new InvalidOperationException(Secret + "-message-access"); }
        }
        public override string? StackTrace
        {
            get { RawStackReads++; throw new InvalidOperationException(Secret + "-raw-stack-access"); }
        }
        public override string ToString()
        {
            ToStringReads++;
            throw new InvalidOperationException(Secret + "-exception-format");
        }
    }
}
