using System.Security.Cryptography;
using System.Text.Json;
using FishAudio.Testing.Execution;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

// The global namespace makes this lifecycle apply to every fixture in the linked
// assembly. Discovery does not invoke either callback or perform evidence I/O.
[SetUpFixture]
public sealed class CoverageHostEvidence
{
    private Owner? _owner;

    [OneTimeSetUp]
    public void CaptureStart()
    {
        FrameworkLifecycleClient.StartHost();
        try
        {
            var context = ReadContext();
            if (context is null) return;

            var startedUtc = DateTimeOffset.UtcNow;
            var assemblyPath = typeof(CoverageHostEvidence).Assembly.Location;
            Require(Path.IsPathFullyQualified(assemblyPath), "coverage-host-assembly-path-invalid");
            var owner = new Owner(context, Environment.ProcessId, assemblyPath,
                ReadAssemblySha256(assemblyPath), startedUtc);
            WriteNew(owner, "start", finishedUtc: null);
            _owner = owner;
        }
        catch (Exception error) when (IsEvidenceFailure(error))
        {
            FrameworkLifecycleClient.AbortHost();
            // Filesystem exception messages can contain the private evidence path.
            throw new InvalidDataException("coverage-host-start-evidence-failed");
        }
        catch
        {
            FrameworkLifecycleClient.AbortHost();
            throw;
        }
    }

    [OneTimeTearDown]
    public void CaptureEnd()
    {
        var preserveFailure = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            var owner = _owner;
            if (owner is null) return;

            try
            {
                var finishedUtc = DateTimeOffset.UtcNow;
                Require(finishedUtc >= owner.StartedUtc, "coverage-host-clock-regressed");
                Require(ReadAssemblySha256(owner.AssemblyPath) == owner.AssemblySha256,
                    "coverage-host-assembly-changed");
                WriteNew(owner, "end", finishedUtc);
            }
            catch (Exception error) when (IsEvidenceFailure(error))
            {
                // Missing or partial end evidence remains inadmissible. Preserve
                // an existing NUnit failure if secondary evidence publication fails.
                if (preserveFailure)
                {
                    try { TestContext.Error.WriteLine("coverage-host-end-evidence-failed"); }
                    catch { /* Preserve the existing NUnit failure. */ }
                }
                else
                    throw new InvalidDataException("coverage-host-end-evidence-failed");
            }
        }
        catch
        {
            preserveFailure = true;
            throw;
        }
        finally
        {
            FrameworkLifecycleClient.CleanupComplete(preserveFailure);
        }
    }

    private static Context? ReadContext()
    {
        var sessionId = Environment.GetEnvironmentVariable("FISHAUDIO_COVERAGE_SESSION_ID");
        var runId = Environment.GetEnvironmentVariable("FISHAUDIO_COVERAGE_RUN_ID");
        var canonicalId = Environment.GetEnvironmentVariable("FISHAUDIO_COVERAGE_CASE_ID");
        var sourceTree = Environment.GetEnvironmentVariable("FISHAUDIO_COVERAGE_SOURCE_TREE");
        var evidenceRoot = Environment.GetEnvironmentVariable("FISHAUDIO_COVERAGE_EVIDENCE_ROOT");
        if (string.IsNullOrEmpty(sessionId) && string.IsNullOrEmpty(runId) &&
            string.IsNullOrEmpty(canonicalId) && string.IsNullOrEmpty(sourceTree) &&
            string.IsNullOrEmpty(evidenceRoot)) return null;

        Require(IsOwnerId(sessionId) && IsOwnerId(runId), "coverage-host-owner-invalid");
        Require(canonicalId is { Length: > 0 and <= 4096 } && !canonicalId.Any(char.IsControl),
            "coverage-host-case-invalid");
        Require(sourceTree is { Length: 40 or 64 } && sourceTree.All(character =>
            char.IsAsciiDigit(character) || character is >= 'a' and <= 'f'), "coverage-host-tree-invalid");
        Require(!string.IsNullOrEmpty(evidenceRoot) && Path.IsPathFullyQualified(evidenceRoot),
            "coverage-host-root-invalid");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(evidenceRoot!));
        RequireExistingDirectory(root);
        return new Context(sessionId!, runId!, canonicalId!, sourceTree!, root);
    }

    private static bool IsOwnerId(string? value)
        => Guid.TryParseExact(value, "N", out var id) && id != Guid.Empty && value == id.ToString("N");

    private static void RequireExistingDirectory(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
        {
            Require(directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) == 0,
                "coverage-host-root-missing-or-reparse");
        }
    }

    private static string ReadAssemblySha256(string path)
    {
        var file = new FileInfo(path);
        Require(file.Exists && (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0,
            "coverage-host-assembly-missing-or-reparse");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void WriteNew(Owner owner, string phase, DateTimeOffset? finishedUtc)
    {
        RequireExistingDirectory(owner.Context.EvidenceRoot);
        var evidence = new
        {
            SchemaVersion = 1,
            Phase = phase,
            owner.Context.RunId,
            owner.Context.SessionId,
            owner.Context.CanonicalId,
            owner.Context.SourceTree,
            Pid = owner.ProcessId,
            owner.AssemblyPath,
            owner.AssemblySha256,
            owner.StartedUtc,
            FinishedUtc = finishedUtc,
            BaselineClaim = false
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(evidence, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        var path = Path.Combine(owner.Context.EvidenceRoot, $"host-{owner.ProcessId}.{phase}.json");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
        // End means NUnit reached this callback; it does not establish OS exit,
        // profiler completion, pipe drainage or ownership of the process tree.
    }

    private static bool IsEvidenceFailure(Exception error)
        => error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static void Require(bool condition, string code)
    {
        if (!condition) throw new InvalidDataException(code);
    }

    private sealed record Context(string SessionId, string RunId, string CanonicalId,
        string SourceTree, string EvidenceRoot);

    private sealed record Owner(Context Context, int ProcessId, string AssemblyPath,
        string AssemblySha256, DateTimeOffset StartedUtc);
}
