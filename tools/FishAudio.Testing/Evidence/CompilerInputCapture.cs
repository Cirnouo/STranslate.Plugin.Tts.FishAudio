using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FishAudio.Testing.Evidence;

public sealed record CompilerInputCaptureInput(string CaptureDirectory, string BuildEvidenceDirectory);
public sealed record CompilerInputCaptureOwnedBuild(CoverageOwnedProcessEvidence Invocation,
    CoverageOwnedProcessEvidence Process, CoverageOwnedProcessEvidence Stdout, CoverageOwnedProcessEvidence Stderr,
    CoverageProcessCompletion Completion);
public sealed record CompilerInputCaptureToolchain(string SdkDirectory, string CaptureTargetsPath, string CaptureTargetsSha256,
    string CSharpTargetsSha256, string ManagedTargetsSha256, string CompilerTaskSha256, string CompilerSha256);
public sealed record CompilerInputCaptureToolchainPins(string SdkVersion, string CaptureTargetsSha256,
    string CSharpTargetsSha256, string ManagedTargetsSha256, string CompilerTaskSha256, string CompilerSha256);

// Independent successful build/source/seal authority, never deserialized from
// the capture's own context. The caller admits the existing owned process and
// supplies its exact raw files. These strings do not authenticate their owner.
public sealed record CompilerInputCaptureAdmission(int SchemaVersion, string InvocationId,
    CoverageSourceIdentity Source, BuildSourceInventoryIdentity Build, OrderedProfileCoverageWorkspaceRequest Snapshot,
    IReadOnlyList<PortablePdbSourceFile> TrackedFiles, IReadOnlyList<CoverageOwnedProcessEvidence> CaptureFiles,
    string DllRelativePath, string PdbRelativePath, CompilerInputCaptureOwnedBuild Owner, CompilerInputCaptureToolchain Toolchain);

public sealed record CompilerInputCaptureEvidence(int SchemaVersion, CoverageSourceIdentity Source, string InvocationId, string BuildId,
    string ProjectRelativePath, string SnapshotContextSha256, string DllRelativePath, string PdbRelativePath, BinaryIdentityRecord Binary,
    string InvocationSha256, string ProcessSha256, IReadOnlyList<CoverageOwnedProcessEvidence> RawCaptureFiles,
    IReadOnlyList<CoverageOwnedProcessEvidence> RawBuildFiles, CompilerInputCaptureToolchainPins ToolchainPins,
    [property: JsonIgnore] IReadOnlyList<string> TaskArgumentItems)
{
    public int ArgumentItemCount => TaskArgumentItems.Count;
    public string ArgumentRepresentation => "roslyn-csc-commandlineargs-item-identity-lines-v1";
    public string BuildAuthority => "caller-independent-owned-build-admission";
    public bool SuccessfulOwnedBuildAdmitted => true;
    public bool ArgumentTransportObserved => false;
    public bool CompilerExecutionIndependentlyObserved => false;
    public bool SdkTaskLoadingObserved => false;
    public bool ProducedProductBinaryProven => false;
    public bool WholeProjectCompilationReconstructed => false;
    public bool ProjectReferenceContextAdmitted => false;
    public bool CompilerOptionsInterpreted => false;
    public bool ReferenceBytesAuthenticated => false;
    public bool GeneratorResultsAuthenticated => false;
    public bool BuildInputClosureProven => false;
    public bool GeneratorInputClosureProven => false;
    public bool SourceMappingsResolved => false;
    public bool CompleteBaseline => false;
    public bool MainProvenanceVerified => false;
    public bool PublicationAuthorized => false;
    public bool TiaSelectionAuthorized => false;
}

public sealed class CompilerInputCaptureException : Exception
{
    internal CompilerInputCaptureException(string reason) : base("compiler-input-capture-" + reason) => Code = Message;
    public string Code { get; }
}

// This admits raw task-item transport and existing build bindings only. It does
// not interpret compiler options/response files, run generators, construct a
// compilation, load MSBuild, or reconstruct historical compiler authority.
public static class CompilerInputCapture
{
    private static readonly string[] CaptureNames = ["begin.txt", "context.txt", "arguments.txt", "corecompile-returned.txt"];
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static CompilerInputCaptureEvidence ReadValidated(CompilerInputCaptureInput input,
        CompilerInputCaptureAdmission expected, CancellationToken token = default)
    {
        var guards = new List<FileStream>();
        var pinned = new Dictionary<string, (string Hash, FileStream Stream)>(StringComparer.OrdinalIgnoreCase);
        OrderedProfileCoverageWorkspaceJoin.SnapshotLease? snapshot = null;
        var completed = false;
        try
        {
            Require(input is not null && expected is not null && expected.SchemaVersion == 1 &&
                Hex(expected.InvocationId, 32) && expected.Source is not null && expected.Build is not null &&
                expected.Build.SchemaVersion == 1 && Hex(expected.Build.BuildId, 64) && expected.Build.ExpectedAssembly is not null &&
                expected.Source.Tree == expected.Build.SourceTree && expected.Source.Fingerprint == expected.Build.SourceFingerprint &&
                expected.Owner is not null && expected.Toolchain is not null && expected.CaptureFiles is { Count: 4 }, "admission");
            var capture = Root(input.CaptureDirectory);
            var ownerRoot = Root(input.BuildEvidenceDirectory);
            snapshot = OrderedProfileCoverageWorkspaceJoin.OpenSnapshot(expected.Source, expected.TrackedFiles, expected.Snapshot, token);
            Require(!Inside(snapshot.Root, capture) && !Inside(snapshot.Root, ownerRoot), "evidence-outside-snapshot-required");
            var owner = expected.Owner;
            var phase = owner.Completion;
            Require(phase is not null && phase.Completed && phase.ExitCode == 0 && phase.StdoutDrained && phase.StderrDrained &&
                phase.OwnedTreeCompleted && phase.OwnedProcesses is { Count: 0 } && phase.StartedUtc.Offset == TimeSpan.Zero &&
                phase.FinishedUtc.Offset == TimeSpan.Zero && phase.FinishedUtc >= phase.StartedUtc, "successful-owned-build-required");
            Require(new[] { owner.Invocation, owner.Process, owner.Stdout, owner.Stderr }.All(item => item is not null) &&
                new[] { owner.Invocation.RelativePath, owner.Process.RelativePath, owner.Stdout.RelativePath, owner.Stderr.RelativePath }
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() == 4, "owner-file-set");
            var raw = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var file in expected.CaptureFiles)
            {
                Require(file is not null && CaptureNames.Contains(file.RelativePath, StringComparer.Ordinal) && !raw.ContainsKey(file.RelativePath) &&
                    file.LastWriteUtc >= phase.StartedUtc && file.LastWriteUtc <= phase.FinishedUtc, "capture-file-set");
                raw.Add(file.RelativePath, ReadSeal(capture, file, file.RelativePath == "arguments.txt" ? 16 * 1024 * 1024 : 64 * 1024, allowEmpty: false));
            }
            var begin = Lines(raw["begin.txt"], 2);
            var end = Lines(raw["corecompile-returned.txt"], 2);
            Require(begin.SequenceEqual(new[] { "fresh-intermediate-output-slot-v1", "invocationId=" + expected.InvocationId }) &&
                end.SequenceEqual(new[] { "corecompile-returned-with-arguments-v1", "invocationId=" + expected.InvocationId }), "capture-markers");
            var arguments = Lines(raw["arguments.txt"], 200_000);
            Require(arguments.Length > 0 && arguments.All(item => item.Length is > 0 and <= 32768 && !item.Any(char.IsControl)), "argument-items");
            var context = Lines(raw["context.txt"], 22);
            var dll = Child(snapshot.Root, expected.DllRelativePath);
            var pdb = Child(snapshot.Root, expected.PdbRelativePath);
            Require(!Same(dll, pdb) && Inside(Path.GetDirectoryName(Child(snapshot.Root, expected.Snapshot.ProductProject))!, dll) &&
                Inside(Path.GetDirectoryName(Child(snapshot.Root, expected.Snapshot.ProductProject))!, pdb), "intermediate-pair-scope");
            ValidateContext(context, arguments.Length, snapshot.Root, dll, pdb, expected);
            Pin(dll, expected.Build.ExpectedAssembly.DllSha256, 256 * 1024 * 1024);
            Pin(pdb, expected.Build.ExpectedAssembly.PdbSha256, 64 * 1024 * 1024);
            var binary = BinaryIdentity.ReadValidated(dll, pdb);
            Require(binary == expected.Build.ExpectedAssembly, "actual-binary-mismatch");
            var toolchain = expected.Toolchain;
            var sdk = Root(toolchain.SdkDirectory);
            Pin(Absolute(toolchain.CaptureTargetsPath), toolchain.CaptureTargetsSha256, 1024 * 1024);
            Pin(Child(sdk, "Roslyn/Microsoft.CSharp.Core.targets"), toolchain.CSharpTargetsSha256, 1024 * 1024);
            Pin(Child(sdk, "Roslyn/Microsoft.Managed.Core.targets"), toolchain.ManagedTargetsSha256, 1024 * 1024);
            Pin(Child(sdk, "Roslyn/Microsoft.Build.Tasks.CodeAnalysis.dll"), toolchain.CompilerTaskSha256, 64 * 1024 * 1024);
            Pin(Child(sdk, "Roslyn/bincore/csc.dll"), toolchain.CompilerSha256, 64 * 1024 * 1024);
            var invocation = ReadSeal(ownerRoot, owner.Invocation, 1024 * 1024, allowEmpty: false);
            var process = ReadSeal(ownerRoot, owner.Process, 1024 * 1024, allowEmpty: false);
            _ = ReadSeal(ownerRoot, owner.Stdout, 16 * 1024 * 1024, allowEmpty: true);
            _ = ReadSeal(ownerRoot, owner.Stderr, 16 * 1024 * 1024, allowEmpty: true);
            ValidateInvocation(invocation, snapshot.Root, capture, expected);
            ValidateProcess(process, phase);
            token.ThrowIfCancellationRequested();
            completed = true;
            return new(1, expected.Source, expected.InvocationId, expected.Build.BuildId, expected.Snapshot.ProductProject, snapshot.ContextSha256,
                expected.DllRelativePath, expected.PdbRelativePath, binary, owner.Invocation.Sha256, owner.Process.Sha256,
                Array.AsReadOnly(expected.CaptureFiles.ToArray()), Array.AsReadOnly(new[] { owner.Invocation, owner.Process, owner.Stdout, owner.Stderr }),
                new(expected.Snapshot.SdkVersion, toolchain.CaptureTargetsSha256, toolchain.CSharpTargetsSha256,
                    toolchain.ManagedTargetsSha256, toolchain.CompilerTaskSha256, toolchain.CompilerSha256), Array.AsReadOnly(arguments));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (CompilerInputCaptureException) { throw; }
        catch (Exception error) when (error is OrderedProfileCoverageWorkspaceJoinException or PortablePdbSourceException or BinaryIdentityException or
            IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or JsonException or
            OverflowException or KeyNotFoundException or FormatException or CryptographicException or System.Security.SecurityException)
        { throw Rejected("input-or-build-rejected"); }
        finally
        {
            var failed = false;
            foreach (var guard in guards) { try { guard.Dispose(); } catch { failed = true; } }
            try { snapshot?.Dispose(); } catch { failed = true; }
            if (completed && failed) throw Rejected("file-release-failed");
        }

        FileStream Pin(string path, string hash, long limit, bool allowEmpty = false)
        {
            token.ThrowIfCancellationRequested();
            Require(Hex(hash, 64), "file-identity");
            path = Absolute(path);
            if (pinned.TryGetValue(path, out var prior))
            {
                Require(prior.Hash == hash && prior.Stream.Length <= limit && (allowEmpty || prior.Stream.Length > 0), "conflicting-pin");
                return prior.Stream;
            }
            var stream = PortablePdbSource.OpenRead(path, limit, allowEmpty);
            guards.Add(stream);
            Require(Convert.ToHexStringLower(SHA256.HashData(stream)) == hash, "file-digest");
            stream.Position = 0;
            pinned.Add(path, (hash, stream));
            return stream;
        }
        byte[] ReadSeal(string root, CoverageOwnedProcessEvidence seal, int limit, bool allowEmpty)
        {
            Require(seal is not null && seal.Length >= 0 && seal.Length <= limit && seal.LastWriteUtc.Offset == TimeSpan.Zero, "raw-seal");
            var path = Child(root, seal.RelativePath);
            var stream = Pin(path, seal.Sha256, limit, allowEmpty);
            Require(stream.Length == seal.Length && new DateTimeOffset(File.GetLastWriteTimeUtc(path)) == seal.LastWriteUtc, "raw-seal-changed");
            stream.Position = 0;
            var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
        }
    }

    private static void ValidateContext(string[] lines, int count, string root, string dll, string pdb, CompilerInputCaptureAdmission expected)
    {
        var product = Child(root, expected.Snapshot.ProductProject);
        var values = new[] { "format=fish-audio-csc-task-items-v1", "invocationId=" + expected.InvocationId,
            "sourceTree=" + expected.Source.Tree, "sourceFingerprint=" + expected.Source.Fingerprint,
            "project=" + product, "projectDirectory=" + Path.GetDirectoryName(product), "configuration=Release", "targetFramework=net10.0-windows",
            "platform=AnyCPU", "sdkVersion=10.0.403", "optimize=true", "debugType=portable", "designTimeBuild=false", "skipCompilerExecution=false",
            "useSharedCompilation=false", "compilerToolPath=", "compilerToolExe=", "roslynTargetsPath=" + Path.Combine(Root(expected.Toolchain.SdkDirectory), "Roslyn"),
            "intermediateDll=" + dll, "intermediatePdb=" + pdb, "argumentItemCount=" + count.ToString(CultureInfo.InvariantCulture), "buildingProject=true" };
        Require(lines.Length == values.Length, "capture-context");
        for (var i = 0; i < values.Length; i++)
        {
            if (i is 4 or 5 or 17 or 18 or 19)
            {
                var prefix = values[i][..(values[i].IndexOf('=') + 1)];
                Require(lines[i].StartsWith(prefix, StringComparison.Ordinal) &&
                    Same(Absolute(lines[i][prefix.Length..]), values[i][prefix.Length..]), "capture-context");
            }
            else Require(lines[i] == values[i], "capture-context");
        }
    }

    private static void ValidateInvocation(byte[] bytes, string root, string capture, CompilerInputCaptureAdmission expected)
    {
        using var document = Json(bytes);
        var value = document.RootElement;
        Fields(value, ["schemaVersion", "executable", "arguments", "workingDirectory", "environment"]);
        Require(value.GetProperty("schemaVersion").GetInt32() == 1 && Same(value.GetProperty("workingDirectory").GetString(), root) &&
            value.GetProperty("executable").ValueKind == JsonValueKind.String && value.GetProperty("executable").GetString() is { } executable &&
            Path.IsPathFullyQualified(executable) && Path.GetFileName(executable) == "dotnet.exe" &&
            value.GetProperty("environment").ValueKind == JsonValueKind.Object && !value.GetProperty("environment").EnumerateObject().Any(), "build-invocation");
        var arguments = value.GetProperty("arguments");
        Require(arguments.ValueKind == JsonValueKind.Array && arguments.GetArrayLength() == 16, "build-invocation");
        var required = new[] { "build", Child(root, expected.Snapshot.EntryProject), "--configuration", "Release", "--disable-build-servers",
            "-p:RestoreLockedMode=true", "-p:FishAudioCoverage=true", "-p:UseSharedCompilation=false", "-nodeReuse:false",
            "-p:CustomAfterMicrosoftCommonTargets=" + Absolute(expected.Toolchain.CaptureTargetsPath), "-p:FishAudioCompilerReceipt=true",
            "-p:FishAudioCompilerReceiptProjectFullPath=" + Child(root, expected.Snapshot.ProductProject),
            "-p:FishAudioCompilerReceiptOutputDirectory=" + capture, "-p:FishAudioCompilerReceiptInvocationId=" + expected.InvocationId,
            "-p:FishAudioCompilerReceiptSourceTree=" + expected.Source.Tree, "-p:FishAudioCompilerReceiptSourceFingerprint=" + expected.Source.Fingerprint };
        var actual = arguments.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null).ToArray();
        Require(actual.All(item => item is not null) && actual.Select(item => item!).SequenceEqual(required, StringComparer.Ordinal), "build-invocation");
    }

    private static void ValidateProcess(byte[] bytes, CoverageProcessCompletion expected)
    {
        using var document = Json(bytes);
        var value = document.RootElement;
        Fields(value, ["schemaVersion", "kind", "complete", "startedUtc", "finishedUtc", "elapsedMilliseconds", "exitCode", "outputDrained",
            "allChildrenExited", "ownership", "cleanupFailed", "phaseTimeoutMilliseconds", "rootProcessId"]);
        Require(value.GetProperty("schemaVersion").GetInt32() == 1 && value.GetProperty("kind").GetString() == "owned-process" &&
            value.GetProperty("complete").GetBoolean() && value.GetProperty("exitCode").GetInt32() == 0 && value.GetProperty("outputDrained").GetBoolean() &&
            value.GetProperty("allChildrenExited").GetBoolean() && !value.GetProperty("cleanupFailed").GetBoolean() &&
            value.GetProperty("ownership").GetString() == "windows-job-before-target-go" && value.GetProperty("phaseTimeoutMilliseconds").GetInt32() == 0 &&
            value.GetProperty("rootProcessId").GetInt32() > 0 && value.GetProperty("startedUtc").GetDateTimeOffset() == expected.StartedUtc &&
            value.GetProperty("finishedUtc").GetDateTimeOffset() == expected.FinishedUtc && value.GetProperty("elapsedMilliseconds").TryGetDouble(out var elapsed) &&
            double.IsFinite(elapsed) && elapsed >= 0, "owned-process");
    }

    private static JsonDocument Json(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 32 });
        var scopes = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) scopes.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) scopes.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName) Require(scopes.Peek().Add(reader.GetString()!), "duplicate-json-field");
        }
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
    }
    private static void Fields(JsonElement value, string[] names) => Require(value.ValueKind == JsonValueKind.Object &&
        value.EnumerateObject().Select(item => item.Name).ToHashSet(StringComparer.Ordinal).SetEquals(names), "raw-json-shape");
    private static string[] Lines(byte[] bytes, int maximum)
    {
        var start = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
        var text = Utf8.GetString(bytes, start, bytes.Length - start);
        Require(text.EndsWith('\n'), "line-transport");
        Require(text.Count(character => character == '\n') <= maximum, "line-count");
        var lines = text[..^1].Split('\n');
        return lines.Select(line => line.EndsWith('\r') ? line[..^1] : line).ToArray();
    }
    private static string Absolute(string path)
    {
        Require(path is { Length: > 0 and <= 32768 } && Path.IsPathFullyQualified(path), "path");
        var root = Path.GetPathRoot(path);
        Require(root is { Length: 3 } && char.IsAsciiLetter(root[0]) && root[1] == ':' && root[2] is '/' or '\\', "path");
        var relative = path[root.Length..].Replace('\\', '/').TrimEnd('/');
        Require(relative.Length == 0 || PortablePdbSource.RelativePath(relative), "path");
        return PortablePdbSource.AbsolutePath(path);
    }
    private static string Root(string path) { path = Absolute(path); PortablePdbSource.RequireNoReparseAncestors(path, true); return Path.TrimEndingDirectorySeparator(path); }
    private static string Child(string root, string relative) { Require(PortablePdbSource.RelativePath(relative), "relative-path"); return PortablePdbSource.ResolveSource(root, relative); }
    private static bool Inside(string root, string path) => Same(root, path) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool Same(string? left, string right) => left is not null && Path.TrimEndingDirectorySeparator(Path.GetFullPath(left))
        .Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
    private static bool Hex(string? value, int length) => value is not null && value.Length == length && value.All(item => item is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static CompilerInputCaptureException Rejected(string reason) => new(reason);
    private static void Require([DoesNotReturnIf(false)] bool condition, string reason) { if (!condition) throw Rejected(reason); }
}
