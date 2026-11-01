using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

[assembly: FishAudio.Testing.Execution.FrameworkLifecycleAction]

namespace FishAudio.Testing.Execution;

// Construction and discovery have no environment, file or pipe effects.
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class FrameworkLifecycleActionAttribute : TestActionAttribute
{
    public override ActionTargets Targets => ActionTargets.Test;
    public override void BeforeTest(ITest test) => FrameworkLifecycleClient.BeforeTest(test);
    public override void AfterTest(ITest test) => FrameworkLifecycleClient.AfterTest(test);
}

internal static partial class FrameworkLifecycleClient
{
    private const string ContextVariable = "FISHAUDIO_FRAMEWORK_CONTROL_CONTEXT";
    private const int MaximumContextBytes = 16 * 1024 * 1024;
    private const int MaximumLineBytes = 64 * 1024;
    private static readonly object HostSync = new();
    private static readonly AsyncLocal<CaseOwner?> CurrentCase = new();
    private static HostOwner? _host;

    internal static void StartHost()
    {
        var path = Environment.GetEnvironmentVariable(ContextVariable);
        if (path is null) return;

        lock (HostSync)
        {
            Require(_host is null, "framework-control-host-already-started");
            HostOwner? host = null;
            try
            {
                host = OpenHost(path);
                host.Pipe.ConnectAsync(120_000).GetAwaiter().GetResult();
                using (var acknowledgement = new StreamReader(host.Pipe, new System.Text.UTF8Encoding(false, true),
                    detectEncodingFromByteOrderMarks: false, bufferSize: 256, leaveOpen: true))
                    Require(acknowledgement.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(120)).GetAwaiter().GetResult()
                        == "framework-owner-ready:" + host.Nonce, "framework-control-owner-not-ready");
                host.Publish("ready");
                _host = host;
            }
            catch
            {
                try { host?.Dispose(); }
                catch { /* Preserve the controlled startup failure. */ }
                throw new InvalidDataException("framework-control-start-failed");
            }
        }
    }

    internal static void BeforeTest(ITest test)
    {
        var host = Volatile.Read(ref _host);
        if (host is null) return;

        try
        {
            var canonicalId = ActualCanonicalId(test);
            Require(IsIdentity(test.FullName) && host.Selected.TryGetValue(canonicalId, out var selected) &&
                selected.FullName == test.FullName, "framework-control-case-not-selected");
            var owner = new CaseOwner(test, canonicalId, test.FullName, CurrentCase.Value);
            lock (host.CaseSync)
            {
                Require(host.Active.Count < 4 && host.Active.TryAdd(canonicalId, owner),
                    "framework-control-case-owner-conflict");
            }
            CurrentCase.Value = owner;
            // Keep this owner even if setup publication fails: NUnit calls
            // AfterTest on that path, and it must dispose any installed scope.
            InstallActionOwnership(canonicalId, ref owner.ActionScope);
            host.Publish("case-start", owner);
        }
        catch
        {
            host.MarkFailure();
            throw new InvalidDataException("framework-control-case-start-failed");
        }
    }

    internal static void AfterTest(ITest test)
    {
        var host = Volatile.Read(ref _host);
        if (host is null) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        CaseOwner? owner = null;
        var failedToFinish = false;
        try
        {
            owner = host.Active.Values.SingleOrDefault(candidate => ReferenceEquals(candidate.Test, test));
            Require(owner is not null, "framework-control-case-owner-missing");
            var canonicalId = ActualCanonicalId(test);
            Require(owner.CanonicalId == canonicalId && owner.FullName == test.FullName,
                "framework-control-case-identity-changed");
            try { owner.ActionScope?.Dispose(); }
            finally { owner.ActionScope = null; }
            var outcome = TestContext.CurrentContext.Result.Outcome.Status.ToString().ToLowerInvariant();
            host.Publish("case-finished", owner, outcome: outcome);
        }
        catch
        {
            host.MarkFailure();
            failedToFinish = true;
        }
        finally
        {
            if (owner is not null)
            {
                try { owner.ActionScope?.Dispose(); }
                catch
                {
                    host.MarkFailure();
                    failedToFinish = true;
                }
                owner.ActionScope = null;
                host.Active.TryRemove(owner.CanonicalId, out _);
                CurrentCase.Value = owner.Previous;
            }
        }

        // Missing control evidence remains a parent failure. It must not replace
        // an existing product/setup/teardown failure with a reporting exception.
        if (!failed && (failedToFinish || host.HasFailure))
            throw new InvalidDataException("framework-control-case-finish-failed");
    }

    internal static void PublishAction(string caseId, string kind, int ordinal,
        long timestamp, long frequency, long queuedTimestamp)
    {
        var host = Volatile.Read(ref _host);
        if (host is null) return;
        try
        {
            Require(host.Active.TryGetValue(caseId, out var owner), "framework-control-action-owner-missing");
            Require(kind is "dispatcher-starting" or "dispatcher-ready" or "queued" or "entered" or "completed",
                "framework-control-action-kind-invalid");
            host.Publish(kind, owner, ordinal, timestamp, frequency, queuedTimestamp);
        }
        catch
        {
            // The original action error owns its failure. A successful case
            // observes this latch in AfterTest, and the parent requires events.
            host.MarkFailure();
        }
    }

    internal static void CleanupComplete(bool preserveFailure)
    {
        HostOwner? host;
        lock (HostSync)
        {
            host = _host;
            _host = null;
        }
        if (host is null) return;
        var failed = host.HasFailure;
        try
        {
            Require(host.Active.IsEmpty, "framework-control-active-cases-at-cleanup");
            host.Publish("cleanup-complete");
        }
        catch { failed = true; }
        finally
        {
            try { host.Dispose(); }
            catch { failed = true; }
        }
        if (failed && !preserveFailure)
            throw new InvalidDataException("framework-control-cleanup-failed");
    }

    internal static void AbortHost()
    {
        HostOwner? host;
        lock (HostSync)
        {
            host = _host;
            _host = null;
        }
        try { host?.Dispose(); }
        catch { /* Setup failure remains primary; no cleanup barrier is invented. */ }
    }

    // The main test assembly installs its existing action ownership here. Tool
    // tests deliberately have no WPF action implementation or native warmup.
    static partial void InstallActionOwnership(string caseId, ref IDisposable? scope);

    private static string ActualCanonicalId(ITest test)
    {
        if (!test.Properties.TryGet("FishAudioCaseId", out var values))
        {
            Require(IsIdentity(test.FullName), "framework-control-case-identity-invalid");
            return test.FullName;
        }
        Require(values.Count == 1 && values[0] is string id && IsIdentity(id),
            "framework-control-case-identity-invalid");
        return (string)values[0]!;
    }

    private static HostOwner OpenHost(string path)
    {
        FileStream? contextStream = null;
        FileStream? assemblyStream = null;
        NamedPipeClientStream? pipe = null;
        try
        {
            contextStream = OpenRegularFile(path);
            Require(contextStream.Length is > 0 and <= MaximumContextBytes, "framework-control-context-size-invalid");
            var bytes = new byte[checked((int)contextStream.Length)];
            contextStream.ReadExactly(bytes);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            RequireFields(root, "schemaVersion", "runId", "sourceTree", "nonce", "pipeName",
                "testAssembly", "testAssemblySha256", "cases");
            Require(root.GetProperty("schemaVersion").GetInt32() == 1, "framework-control-schema-invalid");
            var runId = ReadString(root, "runId");
            // The required property remains explicit null for a manually
            // fingerprinted workspace. The driver authenticates that boundary;
            // a source fingerprint must never masquerade as a Git tree here.
            var treeValue = root.GetProperty("sourceTree");
            var tree = treeValue.ValueKind == JsonValueKind.Null ? null : ReadString(root, "sourceTree");
            var nonce = ReadString(root, "nonce");
            var pipeName = ReadString(root, "pipeName");
            var expectedAssembly = ReadString(root, "testAssembly");
            var expectedHash = ReadString(root, "testAssemblySha256");
            Require(Guid.TryParseExact(runId, "N", out var id) && id != Guid.Empty && id.ToString("N") == runId,
                "framework-control-run-invalid");
            Require(tree is null || IsHash(tree, 40) || IsHash(tree, 64), "framework-control-tree-invalid");
            Require(IsHash(nonce, 64) && nonce.Any(character => character != '0'), "framework-control-nonce-invalid");
            Require(pipeName.Length is >= 17 and <= 128 && pipeName.StartsWith("fa-fw-", StringComparison.Ordinal) &&
                pipeName.EndsWith("-host", StringComparison.Ordinal) && pipeName.All(character =>
                    char.IsAsciiLetterOrDigit(character) || character == '-'), "framework-control-pipe-invalid");
            Require(Path.IsPathFullyQualified(expectedAssembly) && IsHash(expectedHash, 64),
                "framework-control-assembly-invalid");
            var actualAssembly = Path.GetFullPath(typeof(CoverageHostEvidence).Assembly.Location);
            Require(string.Equals(Path.GetFullPath(expectedAssembly), actualAssembly, StringComparison.OrdinalIgnoreCase),
                "framework-control-assembly-path-mismatch");
            assemblyStream = OpenRegularFile(actualAssembly);
            var actualHash = Convert.ToHexStringLower(SHA256.HashData(assemblyStream));
            Require(expectedHash == actualHash, "framework-control-assembly-hash-mismatch");

            var casesElement = root.GetProperty("cases");
            Require(casesElement.ValueKind == JsonValueKind.Array && casesElement.GetArrayLength() is > 0 and <= 100_000,
                "framework-control-cases-invalid");
            var cases = new Dictionary<string, FrameworkCaseBinding>(StringComparer.Ordinal);
            var fullNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in casesElement.EnumerateArray())
            {
                RequireFields(item, "canonicalId", "fullName");
                var canonicalId = ReadString(item, "canonicalId");
                var fullName = ReadString(item, "fullName");
                Require(IsIdentity(canonicalId) && IsIdentity(fullName) && fullNames.Add(fullName) &&
                    cases.TryAdd(canonicalId, new FrameworkCaseBinding(canonicalId, fullName)),
                    "framework-control-case-binding-invalid");
            }
            var context = new FrameworkClientContext(1, runId, tree, nonce, pipeName, actualAssembly,
                actualHash, cases.Values.ToArray());
            pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            return new HostOwner(context, cases, contextStream, assemblyStream, pipe);
        }
        catch
        {
            try { pipe?.Dispose(); } catch { }
            try { assemblyStream?.Dispose(); } catch { }
            try { contextStream?.Dispose(); } catch { }
            throw;
        }
    }

    private static FileStream OpenRegularFile(string path)
    {
        Require(Path.IsPathFullyQualified(path), "framework-control-file-path-invalid");
        var file = new FileInfo(Path.GetFullPath(path));
        Require(file.Exists && (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0,
            "framework-control-file-missing-or-reparse");
        for (var directory = file.Directory; directory is not null; directory = directory.Parent)
            Require(directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) == 0,
                "framework-control-directory-missing-or-reparse");
        return new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static void RequireFields(JsonElement element, params string[] names)
    {
        Require(element.ValueKind == JsonValueKind.Object, "framework-control-context-shape-invalid");
        var remaining = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            Require(remaining.Remove(property.Name), "framework-control-context-fields-invalid");
        Require(remaining.Count == 0, "framework-control-context-fields-invalid");
    }

    private static string ReadString(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        Require(value.ValueKind == JsonValueKind.String, "framework-control-context-value-invalid");
        return value.GetString()!;
    }

    private static bool IsIdentity(string? value)
        => value is { Length: > 0 and <= 4096 } && !value.Any(char.IsControl);

    private static bool IsHash(string value, int length)
        => value.Length == length && value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');

    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw new InvalidDataException(code);
    }

    private sealed class CaseOwner(ITest test, string canonicalId, string fullName, CaseOwner? previous)
    {
        internal ITest Test { get; } = test;
        internal string CanonicalId { get; } = canonicalId;
        internal string FullName { get; } = fullName;
        internal CaseOwner? Previous { get; } = previous;
        internal IDisposable? ActionScope = null;
    }

    private sealed class HostOwner(FrameworkClientContext context,
        IReadOnlyDictionary<string, FrameworkCaseBinding> selected, FileStream contextStream,
        FileStream assemblyStream, NamedPipeClientStream pipe) : IDisposable
    {
        private readonly object _writeSync = new();
        private int _failure;
        internal object CaseSync { get; } = new();
        internal ConcurrentDictionary<string, CaseOwner> Active { get; } = new(StringComparer.Ordinal);
        internal IReadOnlyDictionary<string, FrameworkCaseBinding> Selected { get; } = selected;
        internal NamedPipeClientStream Pipe { get; } = pipe;
        internal string Nonce => context.Nonce;
        internal bool HasFailure => Volatile.Read(ref _failure) != 0;
        internal void MarkFailure() => Interlocked.Exchange(ref _failure, 1);

        internal void Publish(string kind, CaseOwner? owner = null, int ordinal = 0,
            long? timestamp = null, long? frequency = null, long queuedTimestamp = 0, string? outcome = null)
        {
            var message = new FrameworkHostMessage(1, "nunit-host", context.Nonce, context.RunId,
                context.SourceTree, Environment.ProcessId, context.TestAssemblySha256, kind,
                owner?.CanonicalId, owner?.FullName, ordinal, timestamp ?? Stopwatch.GetTimestamp(),
                frequency ?? Stopwatch.Frequency, queuedTimestamp, outcome);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(message, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never
            });
            Require(bytes.Length + 1 <= MaximumLineBytes, "framework-control-message-too-large");
            lock (_writeSync)
            {
                Pipe.Write(bytes);
                Pipe.WriteByte((byte)'\n');
                Pipe.Flush();
            }
        }

        public void Dispose()
        {
            try { Pipe.Dispose(); }
            finally
            {
                try { assemblyStream.Dispose(); }
                finally { contextStream.Dispose(); }
            }
        }
    }
}
