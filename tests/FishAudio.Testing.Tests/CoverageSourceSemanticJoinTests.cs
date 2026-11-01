using System.Reflection.Metadata;
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
public sealed class CoverageSourceSemanticJoinTests
{
    private const string OwnedPrefix = "fa-semantic-join-";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private string _root = "";
    private int _emission;

    private const string Source = """
        namespace SemanticFixture;
        public partial class Subject
        {
            private int _value;
            private System.Action _changed;
            public static int Pick(int value) { return value + 1; }
            public static string Pick(string value) { return value + "!"; }
            public int Value { get { return _value; } set { _value = value; } }
            public event System.Action Changed { add { _changed += value; } remove { _changed -= value; } }
            public void get_LooksLikeProperty() { _value++; }
            public static int RefMode(
        #if OUTPUT_PARAMETER
                out int value
        #else
                ref int value
        #endif
            ) { value = 1; return value; }
            public static async System.Threading.Tasks.Task<int> ComputeAsync(int value)
            {
                await System.Threading.Tasks.Task.Yield();
                return value + 1;
            }
            public static T Constrained<T>(T value)
        #if REFERENCE_CONSTRAINT
                where T : class
        #else
                where T : struct
        #endif
            { return value; }
        }
        public class Outer<T> where T : class
        {
            public class Inner<U> where U : struct
            {
                public static V Select<V>(T first, U second, V third) where V : System.IDisposable
                { return third; }
            }
        }
        """;

    private const string Generated = """
        namespace SemanticFixture;
        public partial class Subject
        {
            public static int Generated() { return "生成文档 λ".Length; }
        }
        """;

    [SetUp]
    public void CreateOwnedFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), OwnedPrefix + Guid.NewGuid().ToString("N"));
        Assert.That(File.Exists(_root) || Directory.Exists(_root), Is.False);
        Directory.CreateDirectory(_root);
        RequireOwnedRoot();
    }

    [TearDown]
    public void RetainFailureOrDeleteOwnedFixture()
    {
        if (_root.Length == 0 || !Directory.Exists(_root)) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            RequireOwnedRoot();
            if (failed)
            {
                TestContext.Progress.WriteLine("semantic-join: failed owned fixture retained.");
                return;
            }
            RequireRegularTree(_root);
            Directory.Delete(_root, recursive: true);
        }
        catch when (failed)
        {
            TestContext.Progress.WriteLine("semantic-join: cleanup failed; original failure and evidence retained.");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "source-semantic-join/overloads-and-nested-generics")]
    public void ActualOverloadsAndNestedGenericParametersResolveToDistinctDeclarations()
    {
        var fixture = Emit(Source, Generated);
        var result = Inspect(fixture);
        var picks = result.Methods.Where(item => Metadata(fixture, item).MetadataName == "Pick").ToArray();

        Assert.That(picks, Has.Length.EqualTo(2));
        Assert.That(picks.All(item => item.Status == CoverageSemanticJoinStatus.ExactDeclaration), Is.True);
        Assert.That(picks.Select(item => item.Declaration!.Start).Distinct().Count(), Is.EqualTo(2));
        foreach (var pick in picks)
        {
            var metadata = Metadata(fixture, pick);
            var node = fixture.Compilation.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("Subject.cs", StringComparison.Ordinal))
                .GetRoot().FindNode(new TextSpan(pick.Declaration!.Start, pick.Declaration.Length));
            var symbol = (IMethodSymbol)fixture.Compilation.GetSemanticModel(node.SyntaxTree).GetDeclaredSymbol(node)!;
            Assert.That(symbol.Parameters[0].Type.SpecialType, Is.EqualTo(
                metadata.Signature!.ParameterTypes[0].Primitive == PrimitiveTypeCode.Int32
                    ? SpecialType.System_Int32 : SpecialType.System_String));
        }
        var nested = result.Methods.Single(item => Metadata(fixture, item).MetadataName == "Select");
        Assert.That(nested.Status, Is.EqualTo(CoverageSemanticJoinStatus.ExactDeclaration));
        Assert.That(nested.Declaration!.DeclaringType, Is.EqualTo("SemanticFixture.Outer`1+Inner`1"));
        Assert.That(nested.Declaration.GenericArity, Is.EqualTo(1));
        AssertConservativeClaims(result);
    }

    [Test]
    [Property("FishAudioCaseId", "source-semantic-join/accessor-ownership")]
    public void AccessorsUseTheirSemanticPropertyOwnerAndOrdinaryGetNamesStayOrdinary()
    {
        var fixture = Emit(Source, Generated);
        var result = Inspect(fixture);
        foreach (var name in new[] { "get_Value", "set_Value" })
        {
            var accessor = result.Methods.Single(item => Metadata(fixture, item).MetadataName == name);
            Assert.That(accessor.Status, Is.EqualTo(CoverageSemanticJoinStatus.ExactDeclaration));
            Assert.That(accessor.Declaration!.AccessorOwnerName, Is.EqualTo("Value"));
        }
        foreach (var name in new[] { "add_Changed", "remove_Changed" })
        {
            var accessor = result.Methods.Single(item => Metadata(fixture, item).MetadataName == name);
            Assert.That(accessor.Status, Is.EqualTo(CoverageSemanticJoinStatus.ExactDeclaration));
            Assert.That(accessor.Declaration!.AccessorOwnerName, Is.EqualTo("Changed"));
        }
        var ordinary = result.Methods.Single(item => Metadata(fixture, item).MetadataName == "get_LooksLikeProperty");
        Assert.That(ordinary.Status, Is.EqualTo(CoverageSemanticJoinStatus.ExactDeclaration));
        Assert.That(ordinary.Declaration!.AccessorOwnerName, Is.Null);
    }

    [Test]
    [Property("FishAudioCaseId", "source-semantic-join/refkind-identity")]
    public void RefAndOutShareAByrefTypeButRemainDifferentSemanticContracts()
    {
        var fixture = Emit(Source, Generated);
        var original = Inspect(fixture);
        var trees = fixture.Compilation.SyntaxTrees.Select(tree => CSharpSyntaxTree.ParseText(tree.GetText(),
            ((CSharpParseOptions)tree.Options).WithPreprocessorSymbols("OUTPUT_PARAMETER"), tree.FilePath)).ToArray();
        var changed = fixture.Compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(trees);

        var result = CoverageSourceSemanticJoin.Inspect(fixture.Graph, changed, fixture.SnapshotRoot, ["obj/generated"]);

        var method = result.Methods.Single(item => Metadata(fixture, item).MetadataName == "RefMode");
        Assert.That(original.Methods.Single(item => item.MetadataToken == method.MetadataToken).Status,
            Is.EqualTo(CoverageSemanticJoinStatus.ExactDeclaration));
        Assert.That(method.Status, Is.EqualTo(CoverageSemanticJoinStatus.Unresolved));
        Assert.That(method.UnresolvedReasons, Does.Contain("declaration-symbol-or-signature-mismatch"));
    }

    [Test]
    [Property("FishAudioCaseId", "source-semantic-join/async-kickoff-boundary")]
    public void AsyncBodyAnchorsTheKickoffDeclarationWithoutPretendingMoveNextIsThatDeclaration()
    {
        var fixture = Emit(Source, Generated);
        var result = Inspect(fixture);
        var kickoff = result.Methods.Single(item => Metadata(fixture, item).MetadataName == "ComputeAsync");
        var body = result.Methods.Single(item => Metadata(fixture, item).StateMachineKickoffMethodToken == kickoff.MetadataToken);

        Assert.That(kickoff.Status, Is.EqualTo(CoverageSemanticJoinStatus.ExactDeclaration));
        Assert.That(kickoff.Declaration!.MetadataName, Is.EqualTo("ComputeAsync"));
        Assert.That(body.Status, Is.EqualTo(CoverageSemanticJoinStatus.RelatedKickoffOnly));
        Assert.That(body.Declaration, Is.Null);
        Assert.That(body.RelatedKickoffToken, Is.EqualTo(kickoff.MetadataToken));
        Assert.That(body.RelatedKickoffDeclaration, Is.EqualTo(kickoff.Declaration));
        Assert.That(body.Fallback, Is.EqualTo(CoverageCandidateFallback.Type));
    }

    [Test]
    [Property("FishAudioCaseId", "source-semantic-join/generated-partial-provenance")]
    public void GeneratedPartialDeclarationRetainsItsActualValidatedDocument()
    {
        var fixture = Emit(Source, Generated);
        var result = Inspect(fixture);
        var generated = result.Methods.Single(item => Metadata(fixture, item).MetadataName == "Generated");

        Assert.That(generated.Status, Is.EqualTo(CoverageSemanticJoinStatus.ExactDeclaration));
        Assert.That(generated.Declaration!.RelativePath, Is.EqualTo("obj/generated/Generated.g.cs"));
        var document = result.Documents.Single(item => item.DocumentRowId == generated.Declaration.DocumentRowId);
        Assert.That(document.Provenance, Is.EqualTo(BuildSourceFileProvenance.BuildGenerated));
        Assert.That(document.CompilationTextMatched, Is.True);
    }

    [Test]
    [Property("FishAudioCaseId", "source-semantic-join/compilation-document-mismatch")]
    public void SamePathAndSimilarTextCannotSubstituteForTheCompilerDocument()
    {
        var fixture = Emit(Source, Generated);
        var original = fixture.Compilation.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("Generated.g.cs", StringComparison.Ordinal));
        var changed = CSharpSyntaxTree.ParseText(SourceText.From(Generated.Replace(".Length", ".GetHashCode()", StringComparison.Ordinal),
            Utf8, SourceHashAlgorithm.Sha256), (CSharpParseOptions)original.Options, original.FilePath);
        var differentCompilation = fixture.Compilation.ReplaceSyntaxTree(original, changed);

        var result = CoverageSourceSemanticJoin.Inspect(fixture.Graph, differentCompilation, fixture.SnapshotRoot, ["obj/generated"]);

        var generated = result.Methods.Single(item => Metadata(fixture, item).MetadataName == "Generated");
        Assert.That(generated.Status, Is.EqualTo(CoverageSemanticJoinStatus.Unresolved));
        Assert.That(generated.UnresolvedReasons, Does.Contain("compilation-document-mismatch"));
        Assert.That(result.Methods.Where(item => Metadata(fixture, item).MetadataName == "Pick")
            .All(item => item.Status == CoverageSemanticJoinStatus.ExactDeclaration), Is.True);
    }

    [Test]
    [Property("FishAudioCaseId", "source-semantic-join/defines-and-generic-constraints")]
    public void ActualDefinesThatChangeOnlyGenericConstraintsCannotAcquireTheOtherBuildsJoin()
    {
        var fixture = Emit(Source, Generated);
        var changedTrees = fixture.Compilation.SyntaxTrees.Select(tree => CSharpSyntaxTree.ParseText(tree.GetText(),
            ((CSharpParseOptions)tree.Options).WithPreprocessorSymbols("REFERENCE_CONSTRAINT"), tree.FilePath)).ToArray();
        var changed = fixture.Compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(changedTrees);

        var result = CoverageSourceSemanticJoin.Inspect(fixture.Graph, changed, fixture.SnapshotRoot, ["obj/generated"]);

        var constrained = result.Methods.Single(item => Metadata(fixture, item).MetadataName == "Constrained");
        Assert.That(constrained.Status, Is.EqualTo(CoverageSemanticJoinStatus.Unresolved));
        Assert.That(constrained.UnresolvedReasons, Does.Contain("declaration-symbol-or-signature-mismatch"));
        Assert.That(result.Documents.All(item => item.PreprocessorSymbols.Contains("REFERENCE_CONSTRAINT")), Is.True);
        AssertConservativeClaims(result);
    }

    [Test]
    [Property("FishAudioCaseId", "source-semantic-join/reference-identity-collision")]
    public void IdenticallyNamedExternalTypesInDifferentAssembliesDoNotJoin()
    {
        var first = EmitExternal("ScopeA");
        var second = EmitExternal("ScopeB");
        var fixture = Emit("""
            extern alias Scoped;
            namespace SemanticFixture;
            public static class Subject
            {
                public static int Pick(Scoped::Collision.Payload value) { return 1; }
            }
            """, Generated.Replace("partial class Subject", "partial class Other", StringComparison.Ordinal),
            [MetadataReference.CreateFromFile(first).WithAliases(["Scoped"])]);
        var original = Inspect(fixture);
        var changed = fixture.Compilation.WithReferences(BaseReferences().Append(MetadataReference.CreateFromFile(second).WithAliases(["Scoped"])));

        var result = CoverageSourceSemanticJoin.Inspect(fixture.Graph, changed, fixture.SnapshotRoot, ["obj/generated"]);

        var pick = result.Methods.Single(item => Metadata(fixture, item).MetadataName == "Pick");
        Assert.That(original.Methods.Single(item => item.MetadataToken == pick.MetadataToken).Status,
            Is.EqualTo(CoverageSemanticJoinStatus.ExactDeclaration));
        Assert.That(pick.Status, Is.EqualTo(CoverageSemanticJoinStatus.Unresolved));
        Assert.That(result.Compilation.ReferenceIdentitySha256, Is.Not.EqualTo(original.Compilation.ReferenceIdentitySha256));
    }

    [TestCase(false, TestName = "RawSourceUtf8BomRetainsExactCompilerBytes")]
    [TestCase(true, TestName = "RawSourceUtf16BomRetainsExactCompilerBytes")]
    public void BomAndEncodingAreCheckedAgainstTheOriginalSourceBytes(bool utf16)
    {
        Encoding encoding = utf16 ? new UnicodeEncoding(false, true, true) : new UTF8Encoding(true, true);
        var fixture = Emit(Source, Generated, encoding: encoding);
        var result = Inspect(fixture);
        Assert.That(result.Documents.All(item => item.CompilationTextMatched), Is.True);
        Assert.That(result.Methods.Where(item => Metadata(fixture, item).MetadataName == "Pick")
            .All(item => item.Status == CoverageSemanticJoinStatus.ExactDeclaration), Is.True);
        var path = Path.Combine(fixture.SnapshotRoot, "src/Subject.cs");
        var raw = File.ReadAllBytes(path);
        var withoutBom = raw.AsSpan(encoding.GetPreamble().Length).ToArray();
        var originalTree = fixture.Compilation.SyntaxTrees.Single(tree => tree.FilePath == path);
        using var stream = new MemoryStream(withoutBom, writable: false);
        var differentRawText = SourceText.From(stream, encoding, SourceHashAlgorithm.Sha256);
        Assert.That(originalTree.GetText().ContentEquals(differentRawText), Is.True);
        Assert.That(originalTree.GetText().GetChecksum().SequenceEqual(differentRawText.GetChecksum()), Is.False);
        var changedTree = CSharpSyntaxTree.ParseText(differentRawText, (CSharpParseOptions)originalTree.Options, path);
        var changed = fixture.Compilation.ReplaceSyntaxTree(originalTree, changedTree);

        var alternateEncoding = CoverageSourceSemanticJoin.Inspect(fixture.Graph, changed, fixture.SnapshotRoot, ["obj/generated"]);

        Assert.That(alternateEncoding.Documents.Single(item => item.RelativePath == "src/Subject.cs").UnresolvedReasons,
            Does.Contain("compilation-document-mismatch"));
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(raw));
        File.WriteAllBytes(path, withoutBom);

        var error = Assert.Throws<CoverageSourceSemanticJoinException>(() => Inspect(fixture));
        Assert.That(error!.Code, Is.EqualTo("source-semantic-join-source-bytes-mismatch"));
    }

    [Test]
    [Property("FishAudioCaseId", "source-semantic-join/unsupported-and-bodyless")]
    public void UnsupportedFunctionPointerSignaturesAndBodylessMembersStayExplicitlyUnresolved()
    {
        var fixture = Emit("""
            namespace SemanticFixture;
            public abstract class Subject
            {
                public abstract int Bodyless();
                public static unsafe delegate* managed<int, int> Function(delegate* managed<int, int> value) { return value; }
            }
            """, Generated.Replace("partial class Subject", "partial class Other", StringComparison.Ordinal));
        var result = Inspect(fixture);
        foreach (var name in new[] { "Function", "Bodyless" })
        {
            var method = result.Methods.Single(item => Metadata(fixture, item).MetadataName == name);
            Assert.That(method.Status, Is.EqualTo(CoverageSemanticJoinStatus.Unresolved));
            Assert.That(method.Declaration, Is.Null);
            Assert.That(method.Fallback, Is.EqualTo(CoverageCandidateFallback.Type));
            Assert.That(method.UnresolvedReasons, Is.Not.Empty);
        }
    }

    private CoverageSourceSemanticJoinEvidence Inspect(Fixture fixture) => CoverageSourceSemanticJoin.Inspect(
        fixture.Graph, fixture.Compilation, fixture.SnapshotRoot, ["obj/generated"]);

    private static PeMethodRecord Metadata(Fixture fixture, CoverageSemanticMethodJoin method) =>
        fixture.Graph.Methods.Single(item => item.MetadataToken == method.MetadataToken).Metadata;

    private static void AssertConservativeClaims(CoverageSourceSemanticJoinEvidence result)
    {
        Assert.That(result.Compilation.Authority, Is.EqualTo("caller-supplied-compilation"));
        Assert.That(result.Compilation.ProducedProductBinaryProven || result.Compilation.ReferenceBytesAuthenticated ||
            result.SourceMappingsResolved || result.BuildInputClosureProven || result.GeneratorInputClosureProven ||
            result.DependencyClosureProven || result.CompleteBaseline || result.BaselineClaim, Is.False);
    }

    private Fixture Emit(string tracked, string generated, IReadOnlyList<MetadataReference>? references = null, Encoding? encoding = null)
    {
        var directory = Path.Combine(_root, "emission-" + _emission++);
        var snapshot = Path.Combine(directory, "snapshot");
        Directory.CreateDirectory(Path.Combine(snapshot, "src"));
        Directory.CreateDirectory(Path.Combine(snapshot, "obj/generated"));
        encoding ??= Utf8;
        var inputs = new[] { (Relative: "src/Subject.cs", Text: tracked), (Relative: "obj/generated/Generated.g.cs", Text: generated) };
        var trees = inputs.Select(input =>
        {
            var path = Path.Combine(snapshot, input.Relative);
            var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(input.Text)).ToArray();
            File.WriteAllBytes(path, bytes);
            using var stream = new MemoryStream(bytes, writable: false);
            var text = SourceText.From(stream, encoding: null, checksumAlgorithm: SourceHashAlgorithm.Sha256);
            return CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.CSharp14), path);
        }).ToArray();
        var compilation = CSharpCompilation.Create("SemanticFixture", trees, BaseReferences().Concat(references ?? []),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true, deterministic: true));
        var dll = Path.Combine(directory, "subject.dll");
        var pdb = Path.Combine(directory, "subject.pdb");
        using (var assembly = new FileStream(dll, FileMode.CreateNew))
        using (var symbols = new FileStream(pdb, FileMode.CreateNew))
        {
            var emitted = compilation.Emit(assembly, symbols, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
            Assert.That(emitted.Success, Is.True, string.Join(",", emitted.Diagnostics.Select(item => item.Id)));
        }
        var identity = BinaryIdentity.ReadValidated(dll, pdb);
        var pe = PeMethodInventory.ReadValidated(dll, pdb, identity);
        var source = BuildSourceInventory.ReadValidated(new(1, new string('a', 64), new string('b', 40), new string('c', 64), identity),
            dll, pdb, snapshot, [new(snapshot + Path.DirectorySeparatorChar, "")],
            [new("src/Subject.cs", FileHash(Path.Combine(snapshot, "src/Subject.cs")))], ["obj/generated"]);
        var scopes = pe.Methods.GroupBy(item => item.DeclaringTypeToken).Select((group, index) =>
            new CoverageCandidateScope(index + 1, CoverageCandidateScopeKind.Type, group.Key, group.First().DeclaringType)).ToArray();
        var methods = pe.Methods.Select(method => new CoverageCandidateMethod(method.MetadataToken,
            scopes.Single(scope => scope.DeclaringTypeToken == method.DeclaringTypeToken).Id, method,
            source.SourceEvidence.Methods.Single(item => item.MetadataToken == method.MetadataToken).SequencePoints.Where(item => !item.IsHidden).ToArray(),
            method.UnresolvedReasons)).ToArray();
        var documents = source.Documents.Select(document => new CoverageCandidateDocument(document,
            source.SourceEvidence.Documents.Single(item => item.RowId == document.DocumentRowId))).ToArray();
        // A pure semantic-construction fixture using actual compiler outputs;
        // these zero observations are not a collected coverage/session claim.
        var graph = new CoverageSourceCandidateGraph(1, identity, new string('d', 64), new(0, 0, 0),
            [new(0, CoverageCandidateScopeKind.Assembly, null, null), .. scopes], documents, methods, [], null);
        return new(compilation, graph, snapshot);
    }

    private string EmitExternal(string name)
    {
        var path = Path.Combine(_root, name + ".dll");
        var compilation = CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText("namespace Collision; public sealed class Payload { }")], BaseReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new FileStream(path, FileMode.CreateNew);
        var emitted = compilation.Emit(output);
        Assert.That(emitted.Success, Is.True, string.Join(",", emitted.Diagnostics.Select(item => item.Id)));
        return path;
    }

    private static IEnumerable<MetadataReference> BaseReferences() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("semantic-fixture-references"))
        .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));

    private static string FileHash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private void RequireOwnedRoot()
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
        if (Path.GetDirectoryName(full) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) ||
            !Path.GetFileName(full).StartsWith(OwnedPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("semantic-fixture-root");
        for (var current = new DirectoryInfo(full); current is not null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("semantic-fixture-reparse");
    }

    private static void RequireRegularTree(string directory)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("semantic-fixture-reparse");
            if ((attributes & FileAttributes.Directory) != 0) RequireRegularTree(path);
        }
    }

    private sealed record Fixture(CSharpCompilation Compilation, CoverageSourceCandidateGraph Graph, string SnapshotRoot);
}
