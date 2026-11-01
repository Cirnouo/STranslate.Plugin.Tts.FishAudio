using System.Security.Cryptography;
using System.Text;
using FishAudio.Testing.Evidence;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PortablePdbSourceTests
{
    private const string DocumentName = "/_snapshot/src/Probe.cs";
    private const string SourceRelativePath = "src/Probe.cs";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly PortablePdbDocumentMap[] DocumentMaps = [new("/_snapshot/", "")];
    private string _root = "";
    private string SnapshotRoot => Path.Combine(_root, "snapshot");

    private const string Source = """
        namespace PdbFixture;
        public static class Probe
        {
            public static int Add(int value)
            {
                return value + 1; // retained UTF-8: λ
            }
        }
        """;

    [SetUp]
    public void CreateOwnedFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "fa-pdb-source-" + Guid.NewGuid().ToString("N"));
        Assert.That(File.Exists(_root) || Directory.Exists(_root), Is.False);
        Directory.CreateDirectory(SnapshotRoot);
        RequireNoReparseAncestors(_root);
    }

    [TearDown]
    public void RetainFailureOrDeleteOwnedFixture()
    {
        if (_root.Length == 0) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            RequireOwnedRoot();
            if (!Directory.Exists(_root)) return;
            if (failed)
            {
                var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
                if (string.IsNullOrWhiteSpace(configured))
                {
                    TestContext.Progress.WriteLine("portable-pdb-source: failed fixture retained in its original owned temporary directory.");
                    return;
                }
                if (!Path.IsPathFullyQualified(configured) || !Directory.Exists(configured))
                    throw new InvalidOperationException("portable-pdb-source-retention-root-invalid");
                var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
                RequireNoReparseAncestors(parent);
                if (parent.Equals(_root, StringComparison.OrdinalIgnoreCase) ||
                    parent.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("portable-pdb-source-retention-overlap");
                CopyEvidence(_root, Path.Combine(parent, "portable-pdb-source-" + Guid.NewGuid().ToString("N")));
                TestContext.Progress.WriteLine("portable-pdb-source: failed fixture copied to its private evidence root.");
            }
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception) when (failed)
        {
            // The failed assertion remains primary, and the original fixture is retained.
            TestContext.Progress.WriteLine("portable-pdb-source: retention or cleanup failed; original evidence remains owned.");
        }
    }

    [TestCase(SourceHashAlgorithm.Sha1, "ff1816ec-aa5e-4d10-87f7-6f4963833460",
        TestName = "portable-pdb-source/matched-sha1-document")]
    [TestCase(SourceHashAlgorithm.Sha256, "8829d00f-11b8-4213-878b-770e8597ac16",
        TestName = "portable-pdb-source/matched-sha256-document")]
    public void EmittedDocumentBindsExactSnapshotBytesAndMethodLocation(SourceHashAlgorithm algorithm, string algorithmId)
    {
        var fixture = Emit(Source, DocumentName, algorithm);
        var sourceBytes = File.ReadAllBytes(SourceFile(SourceRelativePath));
        var expectedChecksum = Convert.ToHexStringLower(algorithm == SourceHashAlgorithm.Sha1
            ? SHA1.HashData(sourceBytes) : SHA256.HashData(sourceBytes));
        var before = new[] { Hash(fixture.Dll), Hash(fixture.Pdb), Hash(SourceFile(SourceRelativePath)) };

        var evidence = Read(fixture);

        Assert.That(evidence.Assembly.AssemblyName, Is.EqualTo(fixture.AssemblyName));
        Assert.That(evidence.Assembly.DllSha256, Is.EqualTo(before[0]));
        Assert.That(evidence.Assembly.PdbSha256, Is.EqualTo(before[1]));
        Assert.That(evidence.Assembly.Mvid, Is.Not.EqualTo(Guid.Empty));
        var document = evidence.Documents.Single();
        Assert.That(document.DocumentName, Is.EqualTo(DocumentName));
        Assert.That(document.SnapshotRelativePath, Is.EqualTo(SourceRelativePath));
        Assert.That(document.HashAlgorithm, Is.EqualTo(Guid.Parse(algorithmId)));
        Assert.That(document.Checksum, Is.EqualTo(expectedChecksum));
        Assert.That(document.SourceSha256, Is.EqualTo(before[2]));
        var method = evidence.Methods.Single(item => item.DeclaringType == "PdbFixture.Probe" && item.Name == "Add");
        Assert.That(method.MetadataToken & unchecked((int)0xff000000), Is.EqualTo(0x06000000));
        Assert.That(method.SequencePoints.Any(point => !point.IsHidden && point.DocumentRowId == document.RowId &&
            point.StartLine == 6 && point.EndLine == 6), Is.True, "The literal return statement is on source line six.");
        Assert.That(evidence.SourceMappingsResolved, Is.False, "Document validation alone does not resolve coverage symbols.");
        Assert.That(evidence.CompleteBaseline, Is.False);
        Assert.That(new[] { Hash(fixture.Dll), Hash(fixture.Pdb), Hash(SourceFile(SourceRelativePath)) }, Is.EqualTo(before));
    }

    [Test]
    [Property("FishAudioCaseId", "portable-pdb-source/changed-source-despite-current-inventory")]
    public void CurrentInventoryCannotAuthorizeBytesDifferentFromTheCompilerDocument()
    {
        var fixture = Emit(Source, DocumentName, SourceHashAlgorithm.Sha256);
        File.AppendAllText(SourceFile(SourceRelativePath), "\n// changed after compilation\n", Utf8);
        // The trusted inventory describes these current bytes. Only the actual PDB
        // document checksum can detect that they were not the compiler's input.
        Reject(fixture, "portable-pdb-source-checksum-mismatch");
    }

    [Test]
    [Property("FishAudioCaseId", "portable-pdb-source/mismatched-dll-pdb")]
    public void AnotherActuallyEmittedPdbCannotDescribeThisAssembly()
    {
        var fixture = Emit(Source, DocumentName, SourceHashAlgorithm.Sha256);
        var other = Emit(Source + "\n// Different compiler input for incompatible symbols.\n", DocumentName, SourceHashAlgorithm.Sha256);
        // Different assembly names alone can produce identical deterministic
        // PDBs. Establish an actual symbol mismatch before testing admission.
        Assert.That(Hash(fixture.Pdb), Is.Not.EqualTo(Hash(other.Pdb)));
        Assert.That(PortableId(fixture.Pdb), Is.Not.EqualTo(PortableId(other.Pdb)));
        var error = Assert.Throws<BinaryIdentityException>(() => Read(fixture with { Pdb = other.Pdb }))!;
        Assert.That(error.Code, Is.EqualTo("binary-dll-pdb-mismatch"));
        Assert.That(error.InnerException, Is.Null);

        static string PortableId(string path)
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var provider = System.Reflection.Metadata.MetadataReaderProvider.FromPortablePdbStream(
                input, System.Reflection.Metadata.MetadataStreamOptions.LeaveOpen);
            var header = provider.GetMetadataReader().DebugMetadataHeader;
            Assert.That(header, Is.Not.Null);
            return Convert.ToHexStringLower(header!.Id.AsSpan());
        }
    }

    [Test]
    [Property("FishAudioCaseId", "portable-pdb-source/document-cannot-escape-explicit-map")]
    public void DocumentTraversalCannotReachOutsideTheExplicitSnapshotMap()
    {
        var fixture = Emit(Source, "/_snapshot/../outside/Probe.cs", SourceHashAlgorithm.Sha256);
        Reject(fixture, "portable-pdb-source-document-path");
    }

    [Test]
    [Property("FishAudioCaseId", "portable-pdb-source/document-missing-from-inventory")]
    public void AnExistingSourceFileCannotSubstituteForAnInventoryDocument()
    {
        var fixture = Emit(Source, DocumentName, SourceHashAlgorithm.Sha256);
        Assert.That(File.Exists(SourceFile(SourceRelativePath)), Is.True);
        var error = Assert.Throws<PortablePdbSourceException>(() => PortablePdbSource.ReadValidated(
            fixture.Dll, fixture.Pdb, SnapshotRoot, [], DocumentMaps))!;
        Assert.That(error.Code, Is.EqualTo("portable-pdb-source-document-not-in-inventory"));
    }

    [Test]
    [Property("FishAudioCaseId", "portable-pdb-source/unknown-document-checksum")]
    public void UnknownChecksumAlgorithmCannotBeTreatedAsSha256()
    {
        const string externalDocument = "/_snapshot/src/External.cs";
        var externalBytes = Utf8.GetBytes(Source);
        WriteSource("src/External.cs", externalBytes);
        var code = $$"""
            #pragma checksum "{{externalDocument}}" "{11111111-1111-1111-1111-111111111111}" "{{Convert.ToHexStringLower(SHA256.HashData(externalBytes))}}"
            #line 1 "{{externalDocument}}"
            {{Source}}
            #line default
            """;
        // The checksum directive supplies a real portable-PDB Document row; its
        // unknown GUID must not become a default or a source-search fallback.
        var fixture = Emit(code, DocumentName, SourceHashAlgorithm.Sha256);
        Reject(fixture, "portable-pdb-source-unsupported-checksum");
    }

    [Test]
    [Property("FishAudioCaseId", "portable-pdb-source/async-state-machine-provenance")]
    public void CompilerGeneratedStateMachineRetainsItsRecordedSourceAndKickoffMethod()
    {
        const string asyncSource = """
            namespace PdbFixture;
            public static class AsyncProbe
            {
                public static async System.Threading.Tasks.Task<int> ComputeAsync()
                {
                    await System.Threading.Tasks.Task.Yield();
                    return 7;
                }
            }
            """;
        var fixture = Emit(asyncSource, DocumentName, SourceHashAlgorithm.Sha256);

        var evidence = Read(fixture);

        var kickoff = evidence.Methods.Single(item => item.DeclaringType == "PdbFixture.AsyncProbe" && item.Name == "ComputeAsync");
        var stateMachine = evidence.Methods.Single(item => item.StateMachineKickoffMethodToken == kickoff.MetadataToken);
        Assert.That(stateMachine.Name, Is.EqualTo("MoveNext"));
        var document = evidence.Documents.Single();
        Assert.That(document.SnapshotRelativePath, Is.EqualTo(SourceRelativePath));
        Assert.That(document.SourceSha256, Is.EqualTo(Hash(SourceFile(SourceRelativePath))));
        Assert.That(stateMachine.SequencePoints.Any(point => !point.IsHidden && point.DocumentRowId == document.RowId && point.StartLine == 6), Is.True);
        Assert.That(stateMachine.SequencePoints.Any(point => point.IsHidden), Is.True, "Hidden points remain metadata, not invented source locations.");
        Assert.That(evidence.SourceMappingsResolved, Is.False);
        Assert.That(evidence.CompleteBaseline, Is.False);
    }

    private PortablePdbSourceEvidence Read(EmittedFixture fixture) => PortablePdbSource.ReadValidated(
        fixture.Dll, fixture.Pdb, SnapshotRoot, Inventory(), DocumentMaps);

    private void Reject(EmittedFixture fixture, string expectedCode)
    {
        var error = Assert.Throws<PortablePdbSourceException>(() => Read(fixture))!;
        Assert.That(error.Code, Is.EqualTo(expectedCode));
        Assert.That(error.Message, Is.EqualTo(expectedCode));
        Assert.That(error.InnerException, Is.Null);
    }

    private PortablePdbSourceFile[] Inventory() => Directory.GetFiles(SnapshotRoot, "*.cs", SearchOption.AllDirectories)
        .Select(path => new PortablePdbSourceFile(Path.GetRelativePath(SnapshotRoot, path).Replace('\\', '/'), Hash(path))).ToArray();

    private EmittedFixture Emit(string source, string documentName, SourceHashAlgorithm algorithm)
    {
        var bytes = Utf8.GetBytes(source);
        WriteSource(SourceRelativePath, bytes);
        using var input = new MemoryStream(bytes, writable: false);
        var text = SourceText.From(input, Utf8, algorithm);
        var tree = CSharpSyntaxTree.ParseText(text, path: documentName);
        var assemblyName = "PortablePdbFixture_" + Guid.NewGuid().ToString("N");
        var compilation = CSharpCompilation.Create(assemblyName, [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug,
                deterministic: true));
        var directory = Path.Combine(_root, "binaries", assemblyName);
        Directory.CreateDirectory(directory);
        var dll = Path.Combine(directory, "image.bin");
        var pdb = Path.Combine(directory, "symbols.bin");
        using (var image = new FileStream(dll, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var symbols = new FileStream(pdb, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var result = compilation.Emit(image, symbols,
                options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb, pdbFilePath: "fixture.pdb"));
            Assert.That(result.Success, Is.True, "Fixture emit must succeed: " +
                string.Join(",", result.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error).Select(item => item.Id)));
        }
        return new(assemblyName, dll, pdb);
    }

    private string SourceFile(string relative) => Path.Combine(SnapshotRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    private void WriteSource(string relative, byte[] bytes)
    {
        var path = SourceFile(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private void RequireOwnedRoot()
    {
        var full = Path.GetFullPath(_root);
        if (!string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(full).StartsWith("fa-pdb-source-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(Path.GetFileName(full)[14..], "N", out _))
            throw new InvalidOperationException("portable-pdb-source-fixture-owner-changed");
        RequireNoReparseAncestors(full);
    }

    private static void RequireNoReparseAncestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("portable-pdb-source-fixture-reparse");
    }

    private static void CopyEvidence(string source, string destination)
    {
        if (File.Exists(destination) || Directory.Exists(destination) ||
            (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("portable-pdb-source-retention-not-fresh");
        Directory.CreateDirectory(destination);
        RequireNoReparseAncestors(destination);
        foreach (var entry in Directory.GetFileSystemEntries(source))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("portable-pdb-source-retention-reparse");
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0) CopyEvidence(entry, target);
            else File.Copy(entry, target, overwrite: false);
        }
    }

    private sealed record EmittedFixture(string AssemblyName, string Dll, string Pdb);
}
