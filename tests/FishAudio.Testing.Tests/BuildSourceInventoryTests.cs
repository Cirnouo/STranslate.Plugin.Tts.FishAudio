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
public sealed class BuildSourceInventoryTests
{
    private const string CompilerPrefix = "/_build/";
    private const string TrackedPath = "src/Subject.cs";
    private const string GeneratedRoot = "obj/coverage/generated";
    private const string GeneratedPath = GeneratedRoot + "/生成代码.g.cs";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly PortablePdbDocumentMap[] Maps = [new(CompilerPrefix, "")];
    private string _root = "";
    private string SnapshotRoot => Path.Combine(_root, "snapshot");

    private const string TrackedSource = """
        namespace BuildInventoryFixture;
        public static partial class Subject
        {
            public static int Tracked(int value)
            {
                return value + 1;
            }
        }
        """;

    private const string GeneratedSource = """
        namespace BuildInventoryFixture;
        public static partial class Subject
        {
            public static int Generated(int value)
            {
                return value + "编译文档 λ".Length;
            }
        }
        """;

    [SetUp]
    public void CreateOwnedFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "fa-build-source-" + Guid.NewGuid().ToString("N"));
        Assert.That(File.Exists(_root) || Directory.Exists(_root), Is.False);
        Directory.CreateDirectory(SnapshotRoot);
        Directory.CreateDirectory(SourcePath(GeneratedRoot));
        RequireOwnedRoot();
    }

    [TearDown]
    public void RetainFailedEvidenceOrDeleteOwnedFixture()
    {
        if (_root.Length == 0 || !Directory.Exists(_root)) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            RequireOwnedRoot();
            if (failed)
            {
                var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
                if (string.IsNullOrWhiteSpace(configured))
                {
                    SafeNote("build-source-inventory: failed fixture retained in its original owned temporary directory.");
                    return;
                }
                if (!Path.IsPathFullyQualified(configured) || !Directory.Exists(configured))
                    throw new InvalidOperationException("build-source-inventory-retention-root-invalid");
                var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
                RequireDirectoryChain(parent);
                if (parent.Equals(_root, StringComparison.OrdinalIgnoreCase) ||
                    parent.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("build-source-inventory-retention-overlap");
                CopyEvidence(_root, Path.Combine(parent, "build-source-inventory-" + Guid.NewGuid().ToString("N")));
                SafeNote("build-source-inventory: failed fixture copied to its private evidence root.");
            }
            RequireRegularTree(_root);
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception) when (failed)
        {
            SafeNote("build-source-inventory: retention or cleanup failed; original evidence remains retained.");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "build-source-inventory/tracked-and-generated-documents")]
    public void ActualCompilerDocumentsRetainSeparateTrackedAndGeneratedProvenance()
    {
        var fixture = Emit();
        var before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, FileHash, StringComparer.Ordinal);

        var evidence = Read(fixture);

        Assert.That(evidence.ExpectedBuild, Is.EqualTo(fixture.ExpectedBuild));
        Assert.That(evidence.SourceIdentityAuthority, Is.EqualTo("caller-supplied"));
        Assert.That(evidence.Documents.Count, Is.EqualTo(2));
        var tracked = evidence.Documents.Single(item => item.SnapshotRelativePath == TrackedPath);
        Assert.That(tracked.DocumentName, Is.EqualTo(CompilerPrefix + TrackedPath));
        Assert.That(tracked.Provenance, Is.EqualTo(BuildSourceFileProvenance.Tracked));
        Assert.That(tracked.Sha256, Is.EqualTo(BytesHash(Utf8.GetBytes(TrackedSource))));
        Assert.That(tracked.GeneratedRoot, Is.Null);
        var generated = evidence.Documents.Single(item => item.SnapshotRelativePath == GeneratedPath);
        Assert.That(generated.DocumentName, Is.EqualTo(CompilerPrefix + GeneratedPath));
        Assert.That(generated.Provenance, Is.EqualTo(BuildSourceFileProvenance.BuildGenerated));
        Assert.That(generated.GeneratedRoot, Is.EqualTo(GeneratedRoot));
        Assert.That(generated.Sha256, Is.EqualTo(BytesHash(Utf8.GetBytes(GeneratedSource))));
        Assert.That(evidence.SourceEvidence.Assembly.DllSha256, Is.EqualTo(before[fixture.Dll]));
        Assert.That(evidence.SourceEvidence.Assembly.PdbSha256, Is.EqualTo(before[fixture.Pdb]));
        Assert.That(evidence.SourceEvidence.Documents.Select(item => item.SnapshotRelativePath),
            Is.EquivalentTo(new[] { TrackedPath, GeneratedPath }));
        Assert.That(evidence.SourceEvidence.Methods.Single(item => item.Name == "Generated").SequencePoints
            .Any(point => !point.IsHidden && point.DocumentRowId == generated.DocumentRowId), Is.True);
        Assert.That(evidence.BuildInputClosureProven, Is.False);
        Assert.That(evidence.GeneratorInputClosureProven, Is.False);
        Assert.That(evidence.SourceMappingsResolved, Is.False);
        Assert.That(evidence.CompleteBaseline, Is.False);
        Assert.That(Directory.GetFiles(_root, "*", SearchOption.AllDirectories), Is.EquivalentTo(before.Keys));
        foreach (var entry in before) Assert.That(FileHash(entry.Key), Is.EqualTo(entry.Value));
    }

    [Test]
    [Property("FishAudioCaseId", "build-source-inventory/missing-tracked-rejects-generated-shadow")]
    public void AGeneratedShadowCannotReplaceAMissingTrackedCompilerInput()
    {
        var fixture = Emit();
        File.Delete(SourcePath(TrackedPath));
        WriteSource(GeneratedRoot + "/Subject.cs", Utf8.GetBytes(TrackedSource));

        Reject(fixture, "build-source-inventory-tracked-source-missing");

        Assert.That(File.Exists(SourcePath(TrackedPath)), Is.False);
        Assert.That(File.ReadAllBytes(SourcePath(GeneratedRoot + "/Subject.cs")), Is.EqualTo(Utf8.GetBytes(TrackedSource)));
    }

    [Test]
    [Property("FishAudioCaseId", "build-source-inventory/ambiguous-document-map")]
    public void TwoExplicitMapsCannotAuthorizeTheSameCompilerDocument()
    {
        var fixture = Emit();
        PortablePdbDocumentMap[] overlapping = [new(CompilerPrefix, ""), new(CompilerPrefix + "obj/", "obj/")];

        Reject(fixture, "build-source-inventory-document-map-ambiguous", maps: overlapping);
    }

    [Test]
    [Property("FishAudioCaseId", "build-source-inventory/generated-outside-intermediate-root")]
    public void AnExistingGeneratedDocumentOutsideTheDeclaredRootRemainsUnadmitted()
    {
        const string outside = "obj/other-build/生成代码.g.cs";
        var fixture = Emit(outside);
        Assert.That(File.Exists(SourcePath(outside)), Is.True);

        Reject(fixture, "build-source-inventory-generated-source-outside-root");
        Reject(fixture, "build-source-inventory-generated-root-invalid", generatedRoots: ["."]);
        Reject(fixture, "build-source-inventory-generated-root-overlaps-tracked", generatedRoots: [GeneratedRoot, "src"]);
    }

    [Test]
    [Property("FishAudioCaseId", "build-source-inventory/generated-checksum-mismatch")]
    public void CurrentGeneratedBytesMustStillMatchTheActualCompilerDocument()
    {
        var fixture = Emit();
        File.AppendAllText(SourcePath(GeneratedPath), "\n// generated file changed after emit\n", Utf8);
        var changed = FileHash(SourcePath(GeneratedPath));

        var error = Assert.Throws<PortablePdbSourceException>(() => Read(fixture))!;

        Assert.That(error.Code, Is.EqualTo("portable-pdb-source-checksum-mismatch"));
        Assert.That(error.InnerException, Is.Null);
        Assert.That(FileHash(SourcePath(GeneratedPath)), Is.EqualTo(changed));
    }

    [Test]
    [Property("FishAudioCaseId", "build-source-inventory/expected-binary-identity")]
    public void ValidSymbolsCannotAuthorizeADifferentExpectedBuildBinary()
    {
        var fixture = Emit();
        var different = fixture.ExpectedBuild with
        {
            ExpectedAssembly = fixture.ExpectedBuild.ExpectedAssembly with { DllSha256 = new string('d', 64) },
        };

        Reject(fixture with { ExpectedBuild = different }, "build-source-inventory-binary-build-mismatch");
    }

    [Test]
    [Property("FishAudioCaseId", "build-source-inventory/canonical-alias-boundaries")]
    public void RawAliasesCannotCrossTheTrackedGeneratedOrBinaryPathBoundaries()
    {
        // An ordinary tilde remains legal; the conservative alias rule concerns
        // a tilde followed by an ASCII digit, not every name containing '~'.
        var fixture = Emit(GeneratedRoot + "/生成~代码.g.cs");
        Assert.DoesNotThrow(() => Read(fixture));
        var binaryDirectory = Path.GetDirectoryName(fixture.Dll)!;
        var binaryName = Path.GetFileName(fixture.Dll);
        string[] rawDllPaths =
        [
            Path.Combine(binaryDirectory, "SHORT~1", binaryName),
            Path.Combine(binaryDirectory, "SHORT~1", "..", binaryName),
            Path.Combine(binaryDirectory, ".", binaryName),
            Path.Combine(binaryDirectory, "not-created", "..", binaryName),
            Path.Combine(binaryDirectory, "not-created.", "..", binaryName),
            Path.Combine(binaryDirectory, "not-created ", "..", binaryName),
            fixture.Dll + ".",
            fixture.Dll + " "
        ];

        Assert.Multiple(() =>
        {
            Assert.That(() => Read(fixture, generatedRoots: [GeneratedRoot + "/SHORT~1"]),
                Throws.TypeOf<BuildSourceInventoryException>().With.Property(nameof(BuildSourceInventoryException.Code))
                    .EqualTo("build-source-inventory-generated-root-invalid"));
            Assert.That(() => Read(fixture, maps: [new(CompilerPrefix, "obj/SHORT~1")]),
                Throws.TypeOf<PortablePdbSourceException>().With.Property(nameof(PortablePdbSourceException.Code))
                    .EqualTo("portable-pdb-source-document-map"));
            // These are raw strings, including cancellable components whose
            // lexical normalization reaches the real DLL. No short-name support
            // or alias directory needs to exist, and file-not-found is not a pass.
            foreach (var rawDllPath in rawDllPaths)
                Assert.That(() => Read(fixture with { Dll = rawDllPath }),
                    Throws.TypeOf<BuildSourceInventoryException>().With.Property(nameof(BuildSourceInventoryException.Code))
                        .EqualTo("build-source-inventory-path-invalid"),
                    "Raw DLL components must reject before filesystem lookup.");
        });
    }

    private BuildSourceInventoryEvidence Read(EmittedFixture fixture, IReadOnlyList<PortablePdbDocumentMap>? maps = null,
        IReadOnlyList<string>? generatedRoots = null)
        => BuildSourceInventory.ReadValidated(fixture.ExpectedBuild, fixture.Dll, fixture.Pdb, SnapshotRoot,
            maps ?? Maps, [new(TrackedPath, BytesHash(Utf8.GetBytes(TrackedSource)))], generatedRoots ?? [GeneratedRoot]);

    private void Reject(EmittedFixture fixture, string code, IReadOnlyList<PortablePdbDocumentMap>? maps = null,
        IReadOnlyList<string>? generatedRoots = null)
    {
        var error = Assert.Throws<BuildSourceInventoryException>(() => Read(fixture, maps, generatedRoots))!;
        Assert.That(error.Code, Is.EqualTo(code));
        Assert.That(error.Message, Is.EqualTo(code));
        Assert.That(error.InnerException, Is.Null);
    }

    private EmittedFixture Emit(string generatedRelativePath = GeneratedPath)
    {
        var trackedBytes = Utf8.GetBytes(TrackedSource);
        var generatedBytes = Utf8.GetBytes(GeneratedSource);
        WriteSource(TrackedPath, trackedBytes);
        WriteSource(generatedRelativePath, generatedBytes);
        var assemblyName = "BuildSourceFixture_" + Guid.NewGuid().ToString("N");
        var compilation = CSharpCompilation.Create(assemblyName,
            [Tree(trackedBytes, TrackedPath), Tree(generatedBytes, generatedRelativePath)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release,
                deterministic: true));
        var directory = Path.Combine(_root, "binaries");
        Directory.CreateDirectory(directory);
        var dll = Path.Combine(directory, "fixture.dll");
        var pdb = Path.Combine(directory, "fixture.pdb");
        using (var image = new FileStream(dll, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var symbols = new FileStream(pdb, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var result = compilation.Emit(image, symbols, options: new EmitOptions(
                debugInformationFormat: DebugInformationFormat.PortablePdb, pdbFilePath: "fixture.pdb"));
            Assert.That(result.Success, Is.True, "Real fixture emit must succeed: " +
                string.Join(",", result.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error).Select(item => item.Id)));
        }
        // These are explicit fixture-owner expectations, not evidence of a Git
        // tree or a complete compiler-input closure. BinaryIdentity supplies the
        // already-tested DLL/PDB binding used as this reader's independent input.
        var expected = new BuildSourceInventoryIdentity(1, new string('a', 64), new string('b', 40),
            new string('c', 64), BinaryIdentity.ReadValidated(dll, pdb));
        return new(dll, pdb, expected);
    }

    private static SyntaxTree Tree(byte[] bytes, string relative)
    {
        using var input = new MemoryStream(bytes, writable: false);
        return CSharpSyntaxTree.ParseText(SourceText.From(input, Utf8, SourceHashAlgorithm.Sha256),
            path: CompilerPrefix + relative);
    }

    private string SourcePath(string relative) => Path.Combine(SnapshotRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    private void WriteSource(string relative, byte[] bytes)
    {
        var path = SourcePath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static string BytesHash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string FileHash(string path) => BytesHash(File.ReadAllBytes(path));

    private void RequireOwnedRoot()
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
        var name = Path.GetFileName(full);
        const string prefix = "fa-build-source-";
        if (!Path.IsPathFullyQualified(_root) || full != _root ||
            !string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())),
                StringComparison.OrdinalIgnoreCase) || !name.StartsWith(prefix, StringComparison.Ordinal) ||
            name.Length != prefix.Length + 32 || !Guid.TryParseExact(name[prefix.Length..], "N", out _))
            throw new InvalidOperationException("build-source-inventory-fixture-owner-invalid");
        RequireDirectoryChain(full);
    }

    private static void RequireDirectoryChain(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("build-source-inventory-fixture-directory-invalid");
    }

    private static void RequireRegularTree(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            var directory = pending.Pop();
            RequireDirectoryChain(directory);
            foreach (var path in Directory.GetFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                    throw new InvalidOperationException("build-source-inventory-fixture-entry-invalid");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
            }
        }
    }

    private static void CopyEvidence(string source, string destination)
    {
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new InvalidOperationException("build-source-inventory-retention-not-fresh");
        RequireRegularTree(source);
        Directory.CreateDirectory(destination);
        RequireDirectoryChain(destination);
        foreach (var path in Directory.GetFileSystemEntries(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(path));
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) CopyEvidence(path, target);
            else File.Copy(path, target, overwrite: false);
        }
    }

    private static void SafeNote(string message)
    {
        try { TestContext.Progress.WriteLine(message); }
        catch { /* Diagnostic output cannot replace the original failure. */ }
    }

    private sealed record EmittedFixture(string Dll, string Pdb, BuildSourceInventoryIdentity ExpectedBuild);
}
