using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
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
public sealed class CoverageSourceCandidatesTests
{
    private const string OwnedPrefix = "fa-source-candidates-";
    private const string CanonicalId = "source-candidates/fixture";
    private const string CompilerPrefix = "/_coverage-candidates/";
    private const string TrackedPath = "src/Subject.cs";
    private const string GeneratedRoot = "obj/coverage/generated";
    private const string GeneratedPath = GeneratedRoot + "/Generated.g.cs";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly CoverageArtifactKind[] DiagnosticKinds =
    [
        CoverageArtifactKind.CoverStdout, CoverageArtifactKind.CoverStderr, CoverageArtifactKind.CoverLog,
        CoverageArtifactKind.ExportStdout, CoverageArtifactKind.ExportStderr, CoverageArtifactKind.ExportLog,
    ];
    private string _root = "";
    private string _parent = "";
    private DateTimeOffset _started;
    private BinaryIdentityRecord _product = null!;
    private IsolatedCoverageSessionInput _session = null!;
    private IsolatedCoverageAdmission _expected = null!;
    private CoverageSourceCandidatesInput _source = null!;
    private string SourceRoot => Path.Combine(_root, "source");
    private string BuildRoot => Path.Combine(_root, "build");
    private string ProductDll => Path.Combine(BuildRoot, "product.dll");
    private string ProductPdb => Path.Combine(BuildRoot, "product.pdb");

    private const string TrackedSource = """
        namespace CandidateFixture;
        public partial class Subject
        {
            private int _value;
            private static readonly int Seed;
            static Subject() { Seed = 3; }
            public Subject() { _value = 1; }
            public Subject(int value) { _value = value; }
            public static int Pick(int value) { return value + Seed; }
            public static string Pick(string value) { return value + "!"; }
            public int Value { get => _value; set => _value = value; }
            public event System.Action? Changed;
            public void Fire() { Changed?.Invoke(); }
            public void get_LooksLikeProperty() { _value++; }
            public static async System.Threading.Tasks.Task<int> ComputeAsync(int value)
            {
                await System.Threading.Tasks.Task.Yield();
                return value + 1;
            }
        }
        public abstract class Bodyless { public abstract int NoBody(); }
        """;

    private const string GeneratedSource = """
        namespace CandidateFixture;
        public partial class Subject
        {
            public static int Generated() { return "生成文档 λ".Length; }
        }
        """;

    [SetUp]
    public void CreateActualMetadataAndHandwrittenSessionFixture()
    {
        var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
        var parent = string.IsNullOrEmpty(configured) ? Path.GetTempPath() : configured;
        if (!Path.IsPathFullyQualified(parent)) throw new InvalidOperationException("source-candidates-fixture-parent");
        _parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        RequireDirectoryChain(_parent);
        _root = Path.Combine(_parent, OwnedPrefix + Guid.NewGuid().ToString("N"));
        if (File.Exists(_root) || Directory.Exists(_root)) throw new InvalidOperationException("source-candidates-fixture-exists");
        Directory.CreateDirectory(_root);
        foreach (var child in new[] { "source/src", "source/" + GeneratedRoot, "build", "raw", "originals/raw" })
            Directory.CreateDirectory(Path.Combine(_root, child));
        RequireOwnedRoot();
        var fixtureNamespace = TestContext.CurrentContext.Test.Properties.Get("CandidateFixtureNamespace") as string ?? "CandidateFixture";
        var trackedSource = InFixtureNamespace(TrackedSource, fixtureNamespace);
        var generatedSource = InFixtureNamespace(GeneratedSource, fixtureNamespace);
        WriteNew(Path.Combine(SourceRoot, TrackedPath), Utf8.GetBytes(trackedSource));
        WriteNew(Path.Combine(SourceRoot, GeneratedPath), Utf8.GetBytes(generatedSource));
        WriteNew(Path.Combine(SourceRoot, "README.md"), Utf8.GetBytes("repository-resource dependency fixture"));
        EmitProduct(fixtureNamespace);
        var testsDll = typeof(CoverageSourceCandidatesTests).Assembly.Location;
        File.Copy(testsDll, Path.Combine(BuildRoot, "tests.dll"));
        File.Copy(Path.ChangeExtension(testsDll, ".pdb"), Path.Combine(BuildRoot, "tests.pdb"));
        var tests = BinaryIdentity.ReadValidated(Path.Combine(BuildRoot, "tests.dll"), Path.Combine(BuildRoot, "tests.pdb"));
        _product = BinaryIdentity.ReadValidated(ProductDll, ProductPdb);
        File.Copy(ProductDll, Path.Combine(_root, "originals/product.dll"));
        File.Copy(ProductPdb, Path.Combine(_root, "originals/product.pdb"));
        WriteNew(Path.Combine(_root, "originals/Subject.cs"), Utf8.GetBytes(trackedSource));
        WriteNew(Path.Combine(_root, "originals/Generated.g.cs"), Utf8.GetBytes(generatedSource));
        WriteNewJson(Path.Combine(_root, "originals/product-identity.json"), _product);

        _started = DateTimeOffset.UtcNow.AddMinutes(-2);
        var identity = new CoverageSessionIdentity(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"),
            CanonicalId, new(new string('a', 40), new string('b', 40), new string('c', 64)), new string('d', 64));
        var tool = Path.Combine(BuildRoot, "pinned-tool.fixture");
        WriteNew(tool, Utf8.GetBytes("pinned tool bytes: handwritten admission-contract fixture, not a profiler"));
        var cover = new CoverageProcessCompletion(_started.AddSeconds(1), _started.AddSeconds(10), true, 0, true, true, true, []);
        var export = new CoverageProcessCompletion(_started.AddSeconds(11), _started.AddSeconds(12), true, 0, true, true, true, []);
        var artifacts = new List<CoverageArtifact>();
        foreach (var kind in Enum.GetValues<CoverageArtifactKind>())
        {
            var contents = kind switch
            {
                CoverageArtifactKind.Discovery => Discovery,
                CoverageArtifactKind.NUnitXml => Terminal,
                CoverageArtifactKind.Trx => Trx,
                CoverageArtifactKind.Snapshot => "opaque handwritten native-snapshot fixture",
                CoverageArtifactKind.AggregateJson => Aggregate(),
                CoverageArtifactKind.CoverInvocation => Invocation(identity, cover: true),
                CoverageArtifactKind.ExportInvocation => Invocation(identity, cover: false),
                CoverageArtifactKind.CoverProcess => ProcessRecord(cover),
                CoverageArtifactKind.ExportProcess => ProcessRecord(export),
                _ => "",
            };
            artifacts.Add(WriteArtifact(kind, contents));
            File.Copy(ArtifactPath(kind), Path.Combine(_root, "originals/raw", kind + ".fixture"));
        }
        var review = new CoverageDiagnosticReview(CoverageDiagnosticDisposition.Clean, "handwritten-fixture-v1", new string('e', 64),
            artifacts.Where(item => DiagnosticKinds.Contains(item.Kind))
                .Select(item => new CoverageDiagnosticBinding(item.Kind, item.Sha256)).ToArray());
        var receipt = new CoverageSupervisorReceipt(1, identity, _started, _started.AddSeconds(20),
            cover, export, review, artifacts.ToArray());
        _session = new(1, identity, Path.Combine(_root, "raw"), artifacts, null);
        _expected = new(1, identity, SourceRoot, BuildRoot,
            [new(CoverageBinaryRole.Product, "product.dll", "product.pdb", _product),
             new(CoverageBinaryRole.Tests, "tests.dll", "tests.pdb", tests)],
            artifacts.Single(item => item.Kind == CoverageArtifactKind.Discovery).Sha256,
            DotCoverAggregate.SupportedToolVersion, tool, Hash(tool), false, receipt);
        _source = new(1, new(1, identity.BuildId, identity.Source.Tree, identity.Source.Fingerprint, _product),
            [new(CompilerPrefix, "")],
            [new(TrackedPath, Hash(Path.Combine(SourceRoot, TrackedPath))), new("README.md", Hash(Path.Combine(SourceRoot, "README.md")))],
            [GeneratedRoot]);
        WriteNewJson(Path.Combine(_root, "fixture-contract.json"), new
        {
            kind = "handwritten-report-and-supervisor-fixture",
            productCoverageClaim = false, buildOwnerAuthenticated = false,
            subjectAssembly = _product, sessionInput = _session, admission = _expected, sourceInput = _source,
        });
        // Existing readers must accept the real binaries/source and literal raw
        // protocol before any new reader stub is reached. This is not coverage
        // collected by dotCover and makes no real-process/supervision claim.
        var admitted = IsolatedCoverageSession.ReadValidated(_session, _expected);
        Assert.That(admitted.Identity == identity, Is.True);
        var source = BuildSourceInventory.ReadValidated(_source.ProductBuild, ProductDll, ProductPdb, SourceRoot,
            _source.DocumentMaps, _source.TrackedFiles, _source.GeneratedSourceRoots);
        Assert.That(source.Documents.Count, Is.EqualTo(2));
    }

    [TearDown]
    public void RetainFailureOrDeleteOnlyTheOwnedFixture()
    {
        if (_root.Length == 0 || !Directory.Exists(_root)) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            RequireOwnedRoot();
            if (failed)
            {
                SafeNote("coverage-source-candidates: failed owned fixture retained.");
                return;
            }
            RequireRegularTree(_root);
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception) when (failed)
        {
            SafeNote("coverage-source-candidates: cleanup validation failed; original fixture retained.");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-source-candidates/session-build-source-identity")]
    public void SessionBuildTreeAndFingerprintAreIndependentRequiredBindings()
    {
        var identity = _source.ProductBuild;
        Assert.Multiple(() =>
        {
            ExpectRejected(() => Read(_source with { ProductBuild = identity with { BuildId = new string('1', 64) } }),
                "coverage-source-candidates-session-build-mismatch", "build-id");
            ExpectRejected(() => Read(_source with { ProductBuild = identity with { SourceTree = new string('2', 40) } }),
                "coverage-source-candidates-session-build-mismatch", "source-tree");
            ExpectRejected(() => Read(_source with { ProductBuild = identity with { SourceFingerprint = new string('3', 64) } }),
                "coverage-source-candidates-session-build-mismatch", "source-fingerprint");
        });
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-source-candidates/product-five-field-identity")]
    public void EveryExpectedProductIdentityFieldMustMatchTheAdmittedBinary()
    {
        var variants = new[]
        {
            _product with { AssemblyName = _product.AssemblyName + "_other" },
            _product with { Mvid = Guid.NewGuid() },
            _product with { DllSha256 = new string('0', 64) },
            _product with { PdbSha256 = new string('1', 64) },
            _product with { PortablePdbId = new string('2', 40) },
        };
        Assert.Multiple(() =>
        {
            foreach (var variant in variants)
                ExpectRejected(() => Read(_source with { ProductBuild = _source.ProductBuild with { ExpectedAssembly = variant } }),
                    "coverage-source-candidates-product-identity-mismatch", "product-identity-field");
        });
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-source-candidates/rejects-wrong-pdb")]
    public void AnotherActualPortablePdbCannotReplaceTheProductPair()
    {
        File.Copy(Path.Combine(BuildRoot, "tests.pdb"), ProductPdb, overwrite: true);
        ExpectRejected(() => Read(), "coverage-source-candidates-session-evidence-rejected", "wrong-current-pdb");
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-source-candidates/rejects-changed-aggregate")]
    public void CurrentAggregateBytesMustStillMatchTheSealedSession()
    {
        File.AppendAllText(ArtifactPath(CoverageArtifactKind.AggregateJson), " ", Utf8);
        ExpectRejected(() => Read(), "coverage-source-candidates-session-evidence-rejected", "changed-current-aggregate");
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-source-candidates/rejects-changed-source")]
    public void SourceBytesMustMatchBothTrustedInventoryAndCompilerChecksum()
    {
        File.AppendAllText(Path.Combine(SourceRoot, TrackedPath), "\n// changed source bytes", Utf8);
        ExpectRejected(() => Read(), "coverage-source-candidates-source-evidence-rejected", "changed-current-source");
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-source-candidates/overloads-and-constructors")]
    public void CountsAndDisplayParametersCannotSelectOneOverloadOrConstructor()
    {
        var evidence = Read();
        AssertIdentityAndLimits(evidence);
        var picks = MethodTokens("Subject", "Pick");
        Assert.That(picks, Has.Length.EqualTo(2));
        var first = Observation(evidence, "Pick(int):int");
        var second = Observation(evidence, "Pick(string):string");
        Assert.Multiple(() =>
        {
            Assert.That(first.Role, Is.EqualTo(CoverageObservationRole.SupportedMemberHint));
            Assert.That(second.Role, Is.EqualTo(CoverageObservationRole.SupportedMemberHint));
            Assert.That(first.CandidateMethodTokens, Is.EquivalentTo(picks));
            Assert.That(second.CandidateMethodTokens, Is.EquivalentTo(picks));
            Assert.That(first.ScopeId, Is.EqualTo(second.ScopeId));
            Assert.That(first.HasObservedMemberCoverage, Is.True);
            Assert.That(second.HasObservedMemberCoverage, Is.False);
        });
        var constructors = MethodTokens("Subject", ".ctor").Concat(MethodTokens("Subject", ".cctor")).ToArray();
        Assert.That(constructors, Has.Length.EqualTo(3));
        Assert.That(Observation(evidence, "Subject()").CandidateMethodTokens, Is.EquivalentTo(constructors));
        Assert.That(Observation(evidence, "Subject(int)").CandidateMethodTokens, Is.EquivalentTo(constructors));
        var definitions = evidence.Methods.Where(method => constructors.Contains(method.MetadataToken)).ToArray();
        Assert.That(definitions.Count(method => (method.Metadata.Attributes & MethodAttributes.Static) != 0), Is.EqualTo(1));
        Assert.That(definitions.Count(method => (method.Metadata.Attributes & MethodAttributes.Static) == 0), Is.EqualTo(2));
        Assert.That(evidence.Scopes.Count(scope => scope.Kind == CoverageCandidateScopeKind.Type &&
            scope.DeclaringType == "CandidateFixture.Subject"), Is.EqualTo(1));
        Assert.That(evidence.Methods.Select(method => method.MetadataToken).Distinct().Count(), Is.EqualTo(evidence.Methods.Count));
        // Summary counts remain raw summaries, never extra method coverage edges
        // or Type/Assembly execution dependencies for every ordinary case.
        Assert.That(evidence.RootSummary.Role, Is.EqualTo(CoverageObservationRole.ContainerSummary));
        Assert.That(evidence.RootSummary.Kind, Is.EqualTo("Root"));
        Assert.That(evidence.RootSummary.RawPath, Is.Empty);
        Assert.That(evidence.RootSummary.Reason, Is.EqualTo("container-summary"));
        Assert.That(evidence.RootSummary.HasObservedMemberCoverage, Is.False);
        Assert.That(evidence.RootSummary.CoveredStatements, Is.EqualTo(1));
        var summaries = evidence.Observations.Where(item => item.Role == CoverageObservationRole.ContainerSummary).ToArray();
        Assert.That(summaries.Select(item => item.Observation.Kind), Does.Contain("Assembly"));
        Assert.That(summaries.Select(item => item.Observation.Kind), Does.Contain("Namespace"));
        Assert.That(summaries.Select(item => item.Observation.Kind), Does.Contain("Type"));
        foreach (var summary in summaries)
        {
            Assert.That(summary.CandidateMethodTokens, Is.Empty);
            Assert.That(summary.Fallback, Is.EqualTo(CoverageCandidateFallback.None));
            Assert.That(summary.HasObservedMemberCoverage, Is.False);
            Assert.That(summary.UnresolvedReasons, Does.Contain("container-summary"));
        }
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-source-candidates/accessors-kickoff-and-document-provenance")]
    public void ActualAccessorAndKickoffMetadataLeadToValidatedCompilerDocuments()
    {
        var evidence = Read();
        AssertIdentityAndLimits(evidence);
        foreach (var expected in new[]
        {
            (Name: "get_Value", Display: "get_Value():int", Owner: "Value", OwnerKind: PeAccessorOwnerKind.Property, Kind: PeAccessorKind.Getter),
            (Name: "set_Value", Display: "set_Value(int):void", Owner: "Value", OwnerKind: PeAccessorOwnerKind.Property, Kind: PeAccessorKind.Setter),
            (Name: "add_Changed", Display: "add_Changed(Action):void", Owner: "Changed", OwnerKind: PeAccessorOwnerKind.Event, Kind: PeAccessorKind.Add),
            (Name: "remove_Changed", Display: "remove_Changed(Action):void", Owner: "Changed", OwnerKind: PeAccessorOwnerKind.Event, Kind: PeAccessorKind.Remove),
        })
        {
            var token = MethodTokens("Subject", expected.Name).Single();
            Assert.That(Observation(evidence, expected.Display).CandidateMethodTokens, Is.EquivalentTo(new[] { token }));
            var method = evidence.Methods.Single(item => item.MetadataToken == token);
            Assert.That(method.Metadata.Accessors.Any(binding => binding.OwnerName == expected.Owner &&
                binding.OwnerKind == expected.OwnerKind && binding.Kind == expected.Kind), Is.True);
        }
        var lookalike = MethodTokens("Subject", "get_LooksLikeProperty").Single();
        Assert.That(Observation(evidence, "get_LooksLikeProperty():void").CandidateMethodTokens, Is.EquivalentTo(new[] { lookalike }));
        Assert.That(evidence.Methods.Single(item => item.MetadataToken == lookalike).Metadata.Accessors, Is.Empty,
            "An accessor-like name cannot manufacture MethodSemantics.");

        var kickoff = MethodTokens("Subject", "ComputeAsync").Single();
        var moveNext = ActualStateMachineMethod(kickoff);
        Assert.That(Observation(evidence, "ComputeAsync(int):Task<int>").CandidateMethodTokens, Is.EquivalentTo(new[] { kickoff }));
        var machine = evidence.Methods.Single(method => method.MetadataToken == moveNext);
        Assert.That(machine.Metadata.MetadataName, Is.EqualTo("MoveNext"));
        Assert.That(machine.Metadata.StateMachineKickoffMethodToken, Is.EqualTo(kickoff));
        Assert.That(machine.NonHiddenSequencePoints, Is.Not.Empty);
        var tracked = evidence.Documents.Single(item => item.Provenance.SnapshotRelativePath == TrackedPath);
        var generated = evidence.Documents.Single(item => item.Provenance.SnapshotRelativePath == GeneratedPath);
        Assert.Multiple(() =>
        {
            Assert.That(tracked.Provenance.Provenance, Is.EqualTo(BuildSourceFileProvenance.Tracked));
            Assert.That(tracked.CompilerDocument.Checksum, Is.EqualTo(BytesHash(Utf8.GetBytes(TrackedSource))));
            Assert.That(generated.Provenance.Provenance, Is.EqualTo(BuildSourceFileProvenance.BuildGenerated));
            Assert.That(generated.Provenance.GeneratedRoot, Is.EqualTo(GeneratedRoot));
            Assert.That(generated.CompilerDocument.Checksum, Is.EqualTo(BytesHash(Utf8.GetBytes(GeneratedSource))));
            Assert.That(generated.CompilerDocument.HashAlgorithm, Is.EqualTo(new Guid("8829d00f-11b8-4213-878b-770e8597ac16")));
        });
        Assert.That(machine.NonHiddenSequencePoints.All(point => point.DocumentRowId == tracked.CompilerDocument.RowId), Is.True);
        var generatedToken = MethodTokens("Subject", "Generated").Single();
        Assert.That(Observation(evidence, "Generated():int").CandidateMethodTokens, Is.EquivalentTo(new[] { generatedToken }));
        var generatedMethod = evidence.Methods.Single(item => item.MetadataToken == generatedToken);
        Assert.That(generatedMethod.NonHiddenSequencePoints.Any(point => point.DocumentRowId == generated.CompilerDocument.RowId), Is.True);
        foreach (var method in evidence.Methods)
            foreach (var point in method.NonHiddenSequencePoints)
            {
                Assert.That(point.IsHidden, Is.False);
                Assert.That(evidence.Documents.Any(document => document.CompilerDocument.RowId == point.DocumentRowId &&
                    document.Provenance.DocumentRowId == point.DocumentRowId), Is.True);
            }
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-source-candidates/unknowns-and-bodyless-are-not-empty")]
    public void UnknownAndCompilerObservationsRetainScopeInsteadOfBecomingEmptyCoverage()
    {
        var evidence = Read();
        AssertIdentityAndLimits(evidence);
        var unknown = Observation(evidence, "Missing():int");
        var compiler = Observation(evidence, "MoveNext():void");
        foreach (var item in new[] { unknown, compiler })
        {
            Assert.That(item.Role, Is.EqualTo(CoverageObservationRole.UnknownMember));
            Assert.That(item.HasObservedMemberCoverage, Is.True);
            Assert.That(item.CandidateMethodTokens, Is.Empty);
            Assert.That(item.UnresolvedReasons, Is.Not.Empty);
            Assert.That(item.Fallback, Is.EqualTo(CoverageCandidateFallback.Type));
            Assert.That(evidence.Scopes.Single(scope => scope.Id == item.ScopeId).DeclaringType, Is.EqualTo("CandidateFixture.Subject"));
            Assert.That(item.Observation.RawPath, Does.StartWith("/Children/"));
            Assert.That(item.Observation.CoveredStatements, Is.EqualTo(1));
        }
        var foreign = evidence.Observations.Single(item => item.Observation.Name == "Pick(int):int" &&
            item.Observation.SymbolSegments.Any(segment => segment.Kind == "Type" && segment.Name == "NoSuchType"));
        Assert.That(foreign.CandidateMethodTokens, Is.Empty, "A matching short method name in another type cannot be borrowed.");
        Assert.That(foreign.Fallback, Is.EqualTo(CoverageCandidateFallback.Assembly));
        Assert.That(evidence.Scopes.Single(scope => scope.Id == foreign.ScopeId).Kind, Is.EqualTo(CoverageCandidateScopeKind.Assembly));
        var bodylessToken = MethodTokens("Bodyless", "NoBody").Single();
        var bodyless = evidence.Methods.Single(method => method.MetadataToken == bodylessToken);
        Assert.That(bodyless.Metadata.Attributes.HasFlag(MethodAttributes.Abstract), Is.True);
        Assert.That(bodyless.NonHiddenSequencePoints, Is.Empty);
        Assert.That(bodyless.UnresolvedReasons, Does.Contain("no-nonhidden-sequence-points"));
        var bodylessObservation = evidence.Observations.Single(item => item.Observation.Name == "NoBody():int");
        Assert.That(bodylessObservation.CandidateMethodTokens, Is.EquivalentTo(new[] { bodylessToken }));
        Assert.That(bodylessObservation.UnresolvedReasons, Does.Contain("no-nonhidden-sequence-points"));
        Assert.That(evidence.EmptyProduct, Is.Null, "Unknown source correspondence cannot manufacture legal empty product coverage.");
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-source-candidates/cumulative-work-budget")]
    public void SharedScopesDoNotExemptRepeatedCandidateAndPointAssociationWork()
    {
        ReplaceAggregate(Aggregate(onlyPicks: true));
        var admitted = IsolatedCoverageSession.ReadValidated(_session, _expected);
        Assert.That(admitted.Aggregate.Members.Count(item => item.Kind == "Method"), Is.EqualTo(2));
        var uniquePoints = ActualNonHiddenPointCount(MethodTokens("Subject", "Pick"));
        Assert.That(uniquePoints, Is.GreaterThan(0));
        Assert.Multiple(() =>
        {
            ExpectRejected(() => CoverageSourceCandidates.ReadValidated(_session, _expected, _source,
                new(3, CoverageSourceCandidates.MaximumSequencePointAssociations)),
                "coverage-source-candidates-candidate-limit", "two observations times two overloads exceed three references");
            ExpectRejected(() => CoverageSourceCandidates.ReadValidated(_session, _expected, _source,
                new(CoverageSourceCandidates.MaximumCandidateReferences, uniquePoints)),
                "coverage-source-candidates-sequence-point-limit", "repeated associations exceed unique point storage");
            foreach (var budget in new[]
            {
                new CoverageSourceCandidateBudget(0, 1),
                new CoverageSourceCandidateBudget(1, 0),
                new CoverageSourceCandidateBudget(CoverageSourceCandidates.MaximumCandidateReferences + 1, 1),
                new CoverageSourceCandidateBudget(1, CoverageSourceCandidates.MaximumSequencePointAssociations + 1),
            })
                ExpectRejected(() => CoverageSourceCandidates.ReadValidated(_session, _expected, _source, budget),
                    "coverage-source-candidates-invalid-budget", "the internal seam can only lower positive caps");
        });
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-source-candidates/resource-empty-retains-dependencies")]
    public void LegalResourceOnlyCoverageRetainsItsRepositoryDependency()
    {
        DeclareRepositoryResourceDependency("README.md");
        ReplaceAggregate(Node("Root", null, 0, 0).ToJsonString());
        var dependency = new CoverageRepositoryDependency("README.md", Hash(Path.Combine(SourceRoot, "README.md")));
        _session = _session with { EmptyProduct = new(EmptyProductCoverageReason.RepositoryResourcesOnly, [dependency]) };
        var admitted = IsolatedCoverageSession.ReadValidated(_session, _expected);
        Assert.That(admitted.Aggregate.TargetAssembly, Is.Null);
        var evidence = Read();
        AssertIdentityAndLimits(evidence);
        Assert.That(evidence.EmptyProduct, Is.Not.Null);
        Assert.That(evidence.EmptyProduct!.Reason, Is.EqualTo(EmptyProductCoverageReason.RepositoryResourcesOnly));
        Assert.That(evidence.EmptyProduct.Dependencies, Is.EqualTo(new[] { dependency }));
        Assert.That(evidence.Observations, Is.Empty);
        Assert.That(evidence.RootSummary.Role, Is.EqualTo(CoverageObservationRole.ContainerSummary));
        Assert.That(evidence.RootSummary.CoveredStatements, Is.Zero);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-source-candidates/ordered-namespace-fragments")]
    [Property("CandidateFixtureNamespace", "CandidateFixture.Nested")]
    public void OrderedNamespaceFragmentsReachActualMetadataAndChecksummedSource()
    {
        var (typeToken, methodToken) = ActualNestedNamespaceGeneratedTokens();
        using var pdb = File.OpenRead(ProductPdb);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(pdb, MetadataStreamOptions.LeaveOpen);
        var metadata = provider.GetMetadataReader();
        var debug = metadata.GetMethodDebugInformation(MetadataTokens.MethodDebugInformationHandle(methodToken & 0x00ffffff));
        var points = debug.GetSequencePoints().Where(point => !point.IsHidden).Select(point =>
            new PortablePdbSequencePoint(point.Offset, MetadataTokens.GetRowNumber(point.Document.IsNil ? debug.Document : point.Document),
                point.StartLine, point.StartColumn, point.EndLine, point.EndColumn, point.IsHidden)).ToArray();
        Assert.That(points, Is.Not.Empty, "The emitted fixture must provide actual nonhidden PDB points.");
        var row = points.Select(point => point.DocumentRowId).Distinct().Single();
        var document = metadata.GetDocument(MetadataTokens.DocumentHandle(row));
        var checksum = Convert.ToHexStringLower(metadata.GetBlobBytes(document.Hash));
        var sourceHash = Hash(Path.Combine(SourceRoot, GeneratedPath));
        Assert.That(metadata.GetString(document.Name), Is.EqualTo(CompilerPrefix + GeneratedPath));
        Assert.That(metadata.GetGuid(document.HashAlgorithm), Is.EqualTo(new Guid("8829d00f-11b8-4213-878b-770e8597ac16")));
        Assert.That(checksum, Is.EqualTo(sourceHash), "The oracle reads the actual compiler checksum and current emitted source bytes.");
        ReplaceAggregate(Node("Root", null, 1, 1,
            Node("Assembly", _product.AssemblyName, 1, 1,
                Node("Namespace", "CandidateFixture", 1, 1,
                    Node("Namespace", "Nested", 1, 1,
                        Node("Type", "Subject", 1, 1, Node("Method", "Generated():int", 1, 1)))))).ToJsonString());

        var evidence = Read();
        AssertIdentityAndLimits(evidence);
        var observation = Observation(evidence, "Generated():int");
        Assert.That(observation.Observation.SymbolSegments, Is.EqualTo(new[]
        {
            new DotCoverSymbolSegment("Assembly", _product.AssemblyName),
            new DotCoverSymbolSegment("Namespace", "CandidateFixture"),
            new DotCoverSymbolSegment("Namespace", "Nested"),
            new DotCoverSymbolSegment("Type", "Subject"),
            new DotCoverSymbolSegment("Method", "Generated():int"),
        }));
        Assert.That(observation.Role, Is.EqualTo(CoverageObservationRole.SupportedMemberHint),
            "coverage-source-candidates-ordered-namespace-must-resolve");
        Assert.That(observation.CandidateMethodTokens, Is.EqualTo(new[] { methodToken }));
        Assert.That(observation.Fallback, Is.EqualTo(CoverageCandidateFallback.None));
        Assert.That(observation.HasObservedMemberCoverage, Is.True);
        var scope = evidence.Scopes.Single(item => item.Id == observation.ScopeId);
        Assert.That(scope.Kind, Is.EqualTo(CoverageCandidateScopeKind.Type));
        Assert.That(scope.DeclaringTypeToken, Is.EqualTo(typeToken));
        Assert.That(scope.DeclaringType, Is.EqualTo("CandidateFixture.Nested.Subject"));
        var method = evidence.Methods.Single(item => item.MetadataToken == methodToken);
        Assert.That(method.Metadata.DeclaringTypeToken, Is.EqualTo(typeToken));
        Assert.That(method.Metadata.MetadataName, Is.EqualTo("Generated"));
        Assert.That(method.NonHiddenSequencePoints, Is.EqualTo(points));
        var mapped = evidence.Documents.Single(item => item.CompilerDocument.RowId == row);
        Assert.That(mapped.CompilerDocument.DocumentName, Is.EqualTo(CompilerPrefix + GeneratedPath));
        Assert.That(mapped.CompilerDocument.Checksum, Is.EqualTo(checksum));
        Assert.That(mapped.CompilerDocument.SourceSha256, Is.EqualTo(sourceHash));
        Assert.That(mapped.Provenance.SnapshotRelativePath, Is.EqualTo(GeneratedPath));
        Assert.That(mapped.Provenance.Sha256, Is.EqualTo(sourceHash));
        Assert.That(mapped.Provenance.Provenance, Is.EqualTo(BuildSourceFileProvenance.BuildGenerated));
        Assert.That(mapped.Provenance.GeneratedRoot, Is.EqualTo(GeneratedRoot));
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-source-candidates/namespace-boundaries-retain-fallback")]
    [Property("CandidateFixtureNamespace", "CandidateFixture.Nested")]
    public void ReversedForeignAndPostTypeNamespacesCannotBorrowTheActualType()
    {
        _ = ActualNestedNamespaceGeneratedTokens();
        ReplaceAggregate(Node("Root", null, 1, 1,
            Node("Assembly", _product.AssemblyName, 1, 1,
                Node("Namespace", "Nested", 1, 1,
                    Node("Namespace", "CandidateFixture", 1, 1,
                        Node("Type", "Subject", 1, 1, Node("Method", "Generated():int", 1, 1)))),
                Node("Namespace", "CandidateFixture", 1, 1,
                    Node("Namespace", "Foreign", 1, 1,
                        Node("Type", "Subject", 1, 1, Node("Method", "Generated():int", 1, 1)))),
                Node("Namespace", "CandidateFixture", 1, 1,
                    Node("Namespace", "Nested", 1, 1,
                        Node("Type", "Subject", 1, 1,
                            Node("Namespace", "Foreign", 1, 1, Node("Method", "Generated():int", 1, 1))))))).ToJsonString());
        var evidence = Read();
        AssertIdentityAndLimits(evidence);
        var observations = evidence.Observations.Where(item => item.Observation.Name == "Generated():int").ToArray();
        Assert.That(observations, Has.Length.EqualTo(3));
        var paths = new[]
        {
            new[] { "Assembly", "Namespace", "Namespace", "Type", "Method" },
            new[] { "Assembly", "Namespace", "Namespace", "Type", "Method" },
            new[] { "Assembly", "Namespace", "Namespace", "Type", "Namespace", "Method" },
        };
        var names = new[]
        {
            new[] { _product.AssemblyName, "Nested", "CandidateFixture", "Subject", "Generated():int" },
            new[] { _product.AssemblyName, "CandidateFixture", "Foreign", "Subject", "Generated():int" },
            new[] { _product.AssemblyName, "CandidateFixture", "Nested", "Subject", "Foreign", "Generated():int" },
        };
        for (var index = 0; index < observations.Length; index++)
        {
            var observation = observations[index];
            Assert.That(observation.Observation.SymbolSegments.Select(item => item.Kind), Is.EqualTo(paths[index]));
            Assert.That(observation.Observation.SymbolSegments.Select(item => item.Name), Is.EqualTo(names[index]));
            Assert.That(observation.Role, Is.EqualTo(CoverageObservationRole.UnknownMember));
            Assert.That(observation.CandidateMethodTokens, Is.Empty);
            Assert.That(observation.HasObservedMemberCoverage, Is.True);
            Assert.That(observation.Fallback, Is.EqualTo(CoverageCandidateFallback.Assembly));
            Assert.That(observation.UnresolvedReasons, Does.Contain("type-scope-unresolved"));
            var scope = evidence.Scopes.Single(item => item.Id == observation.ScopeId);
            Assert.That(scope.Kind, Is.EqualTo(CoverageCandidateScopeKind.Assembly));
            Assert.That(scope.DeclaringTypeToken, Is.Null);
            Assert.That(scope.DeclaringType, Is.Null);
        }
        Assert.That(evidence.EmptyProduct, Is.Null, "Unresolved namespaces remain observed Product work, never legal empty coverage.");
    }

    private CoverageSourceCandidatesEvidence Read(CoverageSourceCandidatesInput? source = null) =>
        CoverageSourceCandidates.ReadValidated(_session, _expected, source ?? _source);

    private void AssertIdentityAndLimits(CoverageSourceCandidatesEvidence evidence)
    {
        Assert.Multiple(() =>
        {
            Assert.That(evidence.SchemaVersion, Is.EqualTo(1));
            Assert.That(evidence.Identity == _expected.Identity, Is.True);
            Assert.That(evidence.Product == _product, Is.True);
            Assert.That(evidence.Methods.All(method => method.MetadataToken == method.Metadata.MetadataToken), Is.True);
            Assert.That(evidence.Scopes.Select(scope => scope.Id).Distinct().Count(), Is.EqualTo(evidence.Scopes.Count));
            Assert.That(evidence.AggregateSha256, Is.EqualTo(_session.Artifacts.Single(item => item.Kind == CoverageArtifactKind.AggregateJson).Sha256));
            Assert.That(evidence.SourceIdentityAuthority, Is.EqualTo("caller-supplied"));
            Assert.That(evidence.BuildInputClosureProven || evidence.GeneratorInputClosureProven ||
                evidence.SourceMappingsResolved || evidence.CompleteBaseline || evidence.BaselineClaim, Is.False);
        });
    }

    private static CoverageObservationCandidates Observation(CoverageSourceCandidatesEvidence evidence, string name) =>
        evidence.Observations.Single(item => item.Observation.Name == name &&
            item.Observation.SymbolSegments.Any(segment => segment.Kind == "Type" && segment.Name == "Subject"));

    private static void ExpectRejected(TestDelegate operation, string code, string clue)
    {
        var error = Assert.Throws<CoverageSourceCandidatesException>(operation, clue);
        if (error is not null) Assert.That(error.Code, Is.EqualTo(code), clue);
    }

    private void EmitProduct(string fixtureNamespace = "CandidateFixture")
    {
        var trees = new[] { (TrackedPath, InFixtureNamespace(TrackedSource, fixtureNamespace)),
            (GeneratedPath, InFixtureNamespace(GeneratedSource, fixtureNamespace)) }.Select(input =>
        {
            using var bytes = new MemoryStream(Utf8.GetBytes(input.Item2), writable: false);
            var text = SourceText.From(bytes, Utf8, SourceHashAlgorithm.Sha256);
            return CSharpSyntaxTree.ParseText(text, path: CompilerPrefix + input.Item1);
        }).ToArray();
        var compilation = CSharpCompilation.Create("CoverageCandidates_" + Guid.NewGuid().ToString("N"), trees,
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug,
                deterministic: true).WithNullableContextOptions(NullableContextOptions.Enable));
        using var dll = new FileStream(ProductDll, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var pdb = new FileStream(ProductPdb, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var emitted = compilation.Emit(dll, pdb,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb, pdbFilePath: "fixture.pdb"));
        Assert.That(emitted.Success, Is.True, "Source candidate fixture emit must succeed: " +
            string.Join(",", emitted.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error).Select(item => item.Id)));
    }

    private static string InFixtureNamespace(string source, string fixtureNamespace) =>
        fixtureNamespace == "CandidateFixture" ? source :
            source.Replace("namespace CandidateFixture;", "namespace " + fixtureNamespace + ";", StringComparison.Ordinal);

    private (int TypeToken, int MethodToken) ActualNestedNamespaceGeneratedTokens()
    {
        using var dll = File.OpenRead(ProductDll);
        using var pe = new PEReader(dll, PEStreamOptions.LeaveOpen);
        var metadata = pe.GetMetadataReader();
        var typeHandle = metadata.TypeDefinitions.Single(handle =>
        {
            var type = metadata.GetTypeDefinition(handle);
            return metadata.GetString(type.Namespace) == "CandidateFixture.Nested" && metadata.GetString(type.Name) == "Subject";
        });
        var methodHandle = metadata.GetTypeDefinition(typeHandle).GetMethods()
            .Single(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name) == "Generated");
        return (MetadataTokens.GetToken(typeHandle), MetadataTokens.GetToken(methodHandle));
    }

    private int[] MethodTokens(string typeName, string methodName)
    {
        using var dll = File.OpenRead(ProductDll);
        using var pe = new PEReader(dll, PEStreamOptions.LeaveOpen);
        var metadata = pe.GetMetadataReader();
        return metadata.MethodDefinitions.Where(handle =>
        {
            var method = metadata.GetMethodDefinition(handle);
            var type = metadata.GetTypeDefinition(method.GetDeclaringType());
            return metadata.GetString(type.Namespace) == "CandidateFixture" && metadata.GetString(type.Name) == typeName &&
                metadata.GetString(method.Name) == methodName;
        }).Select(handle => MetadataTokens.GetToken(handle)).ToArray();
    }

    private int ActualStateMachineMethod(int kickoff)
    {
        using var pdb = File.OpenRead(ProductPdb);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(pdb, MetadataStreamOptions.LeaveOpen);
        var metadata = provider.GetMetadataReader();
        return metadata.MethodDebugInformation.Where(handle =>
        {
            var target = metadata.GetMethodDebugInformation(handle).GetStateMachineKickoffMethod();
            return !target.IsNil && MetadataTokens.GetToken(target) == kickoff;
        }).Select(handle => 0x06000000 | MetadataTokens.GetRowNumber(handle)).Single();
    }

    private int ActualNonHiddenPointCount(IEnumerable<int> tokens)
    {
        using var pdb = File.OpenRead(ProductPdb);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(pdb, MetadataStreamOptions.LeaveOpen);
        var metadata = provider.GetMetadataReader();
        return tokens.Sum(token => metadata.GetMethodDebugInformation(
            MetadataTokens.MethodDebugInformationHandle(token & 0x00ffffff)).GetSequencePoints().Count(point => !point.IsHidden));
    }

    private string Aggregate(bool onlyPicks = false)
    {
        var members = new List<JsonObject>
        {
            Node("Method", "Pick(int):int", 1, 1),
            Node("Method", "Pick(string):string", 0, 11),
        };
        if (!onlyPicks)
            members.AddRange(
            [
                Node("Constructor", "Subject()", 0, 6),
                Node("Constructor", "Subject(int)", 0, 11),
                Node("Method", "get_Value():int", 0, 1),
                Node("Method", "set_Value(int):void", 0, 1),
                Node("Method", "add_Changed(Action):void", 0, 1),
                Node("Method", "remove_Changed(Action):void", 0, 1),
                Node("Method", "get_LooksLikeProperty():void", 0, 1),
                Node("Method", "Generated():int", 1, 1),
                Node("Method", "ComputeAsync(int):Task<int>", 1, 1,
                    Node("InternalCompiledMethod", "MoveNext():void", 1, 1)),
                Node("Method", "Missing():int", 1, 1),
            ]);
        var types = new List<JsonObject> { Node("Type", "Subject", 1, 1, members.ToArray()) };
        if (!onlyPicks)
            types.AddRange(
            [
                Node("Type", "Bodyless", 0, 1, Node("Method", "NoBody():int", 0, 1)),
                Node("Type", "NoSuchType", 1, 1, Node("Method", "Pick(int):int", 1, 1)),
            ]);
        return Node("Root", null, 1, 1,
            Node("Assembly", _product.AssemblyName, 1, 1,
                Node("Namespace", "CandidateFixture", 1, 1, types.ToArray()))).ToJsonString();
    }

    private static JsonObject Node(string kind, string? name, long covered, long total, params JsonObject[] children)
    {
        var node = new JsonObject
        {
            ["Kind"] = kind, ["CoveredStatements"] = covered, ["TotalStatements"] = total,
            ["CoveragePercent"] = total == 0 ? 0 : (int)Math.Ceiling(covered * 100d / total),
        };
        if (name is not null) node["Name"] = name;
        if (children.Length != 0 || kind == "Root") node["Children"] = new JsonArray(children.Cast<JsonNode>().ToArray());
        return node;
    }

    private CoverageArtifact WriteArtifact(CoverageArtifactKind kind, string text)
    {
        var path = ArtifactPath(kind);
        File.WriteAllText(path, text, Utf8);
        File.SetLastWriteTimeUtc(path, _started.AddSeconds(kind == CoverageArtifactKind.Snapshot ? 9 : 13).UtcDateTime);
        return new(kind, Path.GetFileName(path), Hash(path), new FileInfo(path).Length, new(File.GetLastWriteTimeUtc(path)));
    }

    private void ReplaceAggregate(string text)
    {
        var changed = WriteArtifact(CoverageArtifactKind.AggregateJson, text);
        _session = _session with { Artifacts = _session.Artifacts.Select(item => item.Kind == changed.Kind ? changed : item).ToArray() };
        _expected = _expected with { Supervisor = _expected.Supervisor with { Artifacts = _session.Artifacts.ToArray() } };
    }

    private void DeclareRepositoryResourceDependency(string relativePath)
    {
        var document = XDocument.Parse(Discovery, LoadOptions.PreserveWhitespace);
        var leaf = document.Descendants("test-case").Single(element => element.Elements("properties").Elements("property")
            .Any(property => (string?)property.Attribute("name") == "FishAudioCaseId" &&
                (string?)property.Attribute("value") == CanonicalId));
        var properties = leaf.Element("properties")!;
        properties.Add(new XElement("property", new XAttribute("name", "FishAudioEmptyProductCoverage"),
            new XAttribute("value", "RepositoryResourcesOnly")));
        properties.Add(new XElement("property", new XAttribute("name", "FishAudioRepositoryDependency"),
            new XAttribute("value", relativePath)));
        // A fresh raw generation retains both the original discovery fixture and
        // its new case declaration. Only current sealed bindings advance.
        var path = Path.Combine(_root, "raw", "declared-discovery-" + Guid.NewGuid().ToString("N") + ".fixture");
        WriteNew(path, Utf8.GetBytes(document.ToString(SaveOptions.DisableFormatting)));
        File.SetLastWriteTimeUtc(path, _started.AddSeconds(13).UtcDateTime);
        var changed = new CoverageArtifact(CoverageArtifactKind.Discovery, Path.GetFileName(path), Hash(path),
            new FileInfo(path).Length, new(File.GetLastWriteTimeUtc(path)));
        _session = _session with { Artifacts = _session.Artifacts.Select(item => item.Kind == changed.Kind ? changed : item).ToArray() };
        _expected = _expected with
        {
            DiscoverySha256 = changed.Sha256,
            Supervisor = _expected.Supervisor with { Artifacts = _session.Artifacts.ToArray() },
        };
    }

    private string ArtifactPath(CoverageArtifactKind kind) => Path.Combine(_root, "raw", kind + ".fixture");

    private string Invocation(CoverageSessionIdentity identity, bool cover) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1, executable = Path.Combine(_root, "sdk", "dotnet.exe"), workingDirectory = BuildRoot,
        sessionRunId = identity.SessionId, canonicalCaseId = identity.CanonicalId,
        arguments = cover
            ? new[]
            {
                Path.Combine(BuildRoot, "pinned-tool.fixture"), "cover", "--target-executable", Path.Combine(_root, "sdk", "dotnet.exe"),
                "--snapshot-output", ArtifactPath(CoverageArtifactKind.Snapshot), "--log-file", ArtifactPath(CoverageArtifactKind.CoverLog),
                "--", "exec", Path.Combine(_root, "sdk", "10.0.403", "vstest.console.dll"), Path.Combine(BuildRoot, "tests.dll"),
                "--Settings:" + Path.Combine(_root, "execution.runsettings"), "--logger:trx;LogFileName=results.trx",
                "--ResultsDirectory:" + Path.Combine(_root, "raw", "trx"),
            }
            : new[]
            {
                Path.Combine(BuildRoot, "pinned-tool.fixture"), "report", "--snapshot-source", ArtifactPath(CoverageArtifactKind.Snapshot),
                "--json-report-output", ArtifactPath(CoverageArtifactKind.AggregateJson), "--log-file", ArtifactPath(CoverageArtifactKind.ExportLog),
            },
    });

    private static string ProcessRecord(CoverageProcessCompletion phase) => JsonSerializer.Serialize(new
    {
        startedUtc = phase.StartedUtc, finishedUtc = phase.FinishedUtc, completed = true, exitCode = 0, outputDrained = true,
        elapsedMilliseconds = (phase.FinishedUtc - phase.StartedUtc).TotalMilliseconds,
    });

    private void RequireOwnedRoot()
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
        var name = Path.GetFileName(full);
        if (!Path.IsPathFullyQualified(_root) || !Path.GetDirectoryName(full)!.Equals(_parent, StringComparison.OrdinalIgnoreCase) ||
            !name.StartsWith(OwnedPrefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[OwnedPrefix.Length..], "N", out _))
            throw new InvalidOperationException("source-candidates-fixture-owner");
        RequireDirectoryChain(full);
    }

    private static void RequireDirectoryChain(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path))
            throw new InvalidOperationException("source-candidates-fixture-parent");
        for (var item = new DirectoryInfo(path); item is not null; item = item.Parent)
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("source-candidates-fixture-reparse");
    }

    private static void RequireRegularTree(string path)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidOperationException("source-candidates-fixture-not-regular");
            if ((attributes & FileAttributes.Directory) != 0) RequireRegularTree(entry);
        }
    }

    private static void SafeNote(string text)
    {
        try { TestContext.Out.WriteLine(text); }
        catch { }
    }

    private static void WriteNew(string path, byte[] bytes)
    {
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        output.Write(bytes);
    }

    private static void WriteNewJson(string path, object value) => WriteNew(path, JsonSerializer.SerializeToUtf8Bytes(value));
    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }
    private static string BytesHash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    // Literal sanitized Adapter/NUnit/TRX shapes, not generated test results.
    private const string Discovery = """
        <NUnitXml><test-run id="0" name="fixture.dll" runstate="Runnable" testcasecount="1">
          <test-suite type="Assembly" id="0-10" name="fixture.dll" runstate="Runnable" testcasecount="1">
            <test-case id="0-1" name="Probe" fullname="CandidateCases.Probe" methodname="Probe" classname="CandidateCases" runstate="Runnable"><properties><property name="FishAudioCaseId" value="source-candidates/fixture" /></properties></test-case>
          </test-suite>
        </test-run></NUnitXml>
        """;
    private const string Terminal = """
        <test-run id="7" result="Passed" total="1" passed="1" failed="0" warnings="0" inconclusive="0" skipped="0">
          <test-case id="7-1" name="Probe" fullname="CandidateCases.Probe" methodname="Probe" classname="CandidateCases" result="Passed" duration="0.125"><properties><property name="FishAudioCaseId" value="source-candidates/fixture" /></properties></test-case>
        </test-run>
        """;
    private const string Trx = """
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results><UnitTestResult executionId="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" testId="11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa" testName="Probe" duration="00:00:00.1250000" outcome="Passed" /></Results>
          <TestDefinitions><UnitTest name="Probe" id="11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa"><Execution id="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" /><TestMethod adapterTypeName="executor://nunit3testexecutor/" className="CandidateCases" name="Probe" /></UnitTest></TestDefinitions>
          <ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" /></ResultSummary>
        </TestRun>
        """;
}
