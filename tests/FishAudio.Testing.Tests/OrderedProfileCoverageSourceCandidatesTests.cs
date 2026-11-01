using System.Text.Json;
using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

public sealed partial class CoverageSourceCandidatesTests
{
    [Test]
    [Property("FishAudioCaseId", "coverage-source-candidates/group-bridge-neutral-graph-preserves-isolated-evidence")]
    public void NeutralGraphRetainsActualMetadataDocumentsAndEveryOriginalIsolatedOutputField()
    {
        var isolated = Read();
        var admitted = IsolatedCoverageSession.ReadValidated(_session, _expected);
        var pe = PeMethodInventory.ReadValidated(ProductDll, ProductPdb, _product);
        var sources = BuildSourceInventory.ReadValidated(_source.ProductBuild, ProductDll, ProductPdb, SourceRoot,
            _source.DocumentMaps, _source.TrackedFiles, _source.GeneratedSourceRoots);
        var graph = CoverageSourceCandidates.ConstructGraph(pe, sources, admitted.Aggregate, admitted.EmptyProduct,
            isolated.AggregateSha256, isolated.RootSummary,
            new(CoverageSourceCandidates.MaximumCandidateReferences, CoverageSourceCandidates.MaximumSequencePointAssociations));
        var projected = new CoverageSourceCandidatesEvidence(1, isolated.Identity, graph.Product,
            graph.AggregateSha256, graph.RootSummary, graph.Scopes, graph.Documents, graph.Methods, graph.Observations, graph.EmptyProduct);
        Assert.That(JsonSerializer.Serialize(projected), Is.EqualTo(JsonSerializer.Serialize(isolated)),
            "Extracting a neutral graph must preserve the entire existing isolated wire record, including flags and ordering.");
        Assert.That(graph.Observations, Has.Count.EqualTo(admitted.Aggregate.Members.Count));
        Assert.That(graph.Methods.Select(item => item.MetadataToken), Is.Unique);
        var pick = Observation(projected, "Pick(int):int");
        Assert.That(pick.CandidateMethodTokens, Is.EquivalentTo(MethodTokens("Subject", "Pick")));
        Assert.That(pick.CandidateMethodTokens, Has.Count.EqualTo(2), "A display signature cannot choose one overload.");
        Assert.That(pick.UnresolvedReasons, Does.Contain("signature-not-resolved"));
        Assert.That(pick.UnresolvedReasons, Does.Contain("multiple-metadata-candidates"));
        var compiler = graph.Observations.Single(item => item.Observation.Kind == "InternalCompiledMethod");
        Assert.That(compiler.Role, Is.EqualTo(CoverageObservationRole.UnknownMember));
        Assert.That(compiler.CandidateMethodTokens, Is.Empty);
        Assert.That(compiler.Fallback, Is.EqualTo(CoverageCandidateFallback.Type));
        Assert.That(compiler.UnresolvedReasons, Does.Contain("nested-member-observation"));
        Assert.That(graph.Documents.Any(item => item.Provenance.Provenance == BuildSourceFileProvenance.BuildGenerated), Is.True);
        Assert.That(graph.SourceMappingsResolved || graph.DependencyClosureProven || graph.BuildInputClosureProven ||
            graph.GeneratorInputClosureProven || graph.CompleteBaseline || graph.BaselineClaim, Is.False);
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(graph));
        Assert.That(serialized.RootElement.TryGetProperty("Identity", out _), Is.False,
            "The shared graph cannot borrow the isolated case or invent an ordered group identity.");
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-source-candidates/group-bridge-isolated-raw-is-not-group-authority")]
    public void ValidIsolatedRawEvidenceCannotAuthorizeAGroupGraphWithoutItsIndependentEnvelope()
    {
        var original = IsolatedCoverageSession.ReadValidated(_session, _expected);
        var identity = new CoverageProfileGroupIdentity(_expected.Identity.RunId, _expected.Identity.SessionId,
            "native-input", _expected.Identity.Source, _expected.Identity.BuildId);
        var supervisor = new OrderedProfileCoverageSupervisorReceipt(1, identity, _expected.Supervisor.StartedUtc,
            _expected.Supervisor.SealedUtc, _expected.Supervisor.Cover, _expected.Supervisor.Export,
            _expected.Supervisor.Diagnostics, _session.Artifacts, null!);
        var input = new OrderedProfileCoverageGroupInput(1, identity, _session.ArtifactDirectory, _session.Artifacts);
        var expected = new OrderedProfileCoverageGroupAdmission(1, identity, SourceRoot, BuildRoot,
            _expected.Binaries, _expected.DiscoverySha256, _expected.DotCoverVersion, _expected.ToolFile,
            _expected.ToolSha256, supervisor);
        var rejected = Assert.Throws<OrderedProfileCoverageSourceCandidatesException>(() =>
            OrderedProfileCoverageSourceCandidates.ReadValidated(input, expected, _source));
        Assert.That(rejected!.Code, Is.EqualTo("ordered-profile-source-candidates-group-evidence-rejected"));
        Assert.That(rejected.SourceStageFailure, Is.Null,
            "Without group admission even a conservative admitted-Product source-stage diagnostic is unavailable.");
        Assert.That(rejected.Message, Does.Not.Contain(_root));
        var unchanged = IsolatedCoverageSession.ReadValidated(_session, _expected);
        Assert.That(unchanged.Identity, Is.EqualTo(original.Identity));
        Assert.That(unchanged.Aggregate.ReportSha256, Is.EqualTo(original.Aggregate.ReportSha256));
        Assert.That(unchanged.AttributionMode, Is.EqualTo("isolated-case"));
    }
}
