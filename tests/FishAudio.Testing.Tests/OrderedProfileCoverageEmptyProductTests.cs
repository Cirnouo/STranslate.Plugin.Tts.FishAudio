using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

public sealed partial class IsolatedCoverageSessionTests
{
    [TestCase(true, TestName = "ordered-group/resource-empty-absent-product-retains-distinct-dependencies-once")]
    [TestCase(false, TestName = "ordered-group/resource-empty-zero-hit-product-retains-distinct-dependencies-once")]
    public void RepositoryGroupNeedsEveryMembersDeclaredResponsibilityAndActualDependencyUnion(bool absent)
    {
        var scenario = PrepareBaselineScenario();
        var explanation = PrepareGroupResourceDependencies();
        SetAggregateEmpty(absent);
        var group = PrepareBaselineGroup(DeclareGroupResourceMember, explanation);
        var first = FreshBaselineCatalog("first", this, "fresh-1");
        var input = scenario.Input with { IsolatedOwners = [scenario.Input.IsolatedOwners[1]], OrderedGroups = [group] };
        var admission = scenario.Admission with { Catalogs = [first, scenario.Admission.Catalogs[1]] };
        var before = CaptureBaselineBytes(admission);

        var result = CoverageBaselineCandidate.ReadValidated(input, admission);

        Assert.That(result.CoverageUnionComplete && result.AllRequiredOwnersAdmitted, Is.True);
        Assert.That(result.OrderedGroups, Has.Count.EqualTo(1));
        Assert.That(result.IsolatedOwners.Select(item => item.Coverage.Identity.CanonicalId), Is.EqualTo(new[] { "baseline/tool-case" }));
        var retained = result.OrderedGroups.Single().Coverage;
        Assert.That(retained.Members.Select(item => item.Case.CanonicalId), Is.EqualTo(new[] { CanonicalId, "session/other" }));
        Assert.That(retained.EmptyProduct!.Reason, Is.EqualTo(EmptyProductCoverageReason.RepositoryResourcesOnly));
        Assert.That(retained.EmptyProduct.Dependencies, Is.EqualTo(explanation.Dependencies));
        Assert.That(retained.EmptyProduct.Dependencies.Select(item => item.RelativePath), Is.EquivalentTo(new[] { "README.md", "config.json" }));
        foreach (var dependency in retained.EmptyProduct.Dependencies)
            Assert.That(dependency.Sha256, Is.EqualTo(Hash(Path.Combine(_expected.SourceDirectory, dependency.RelativePath))));
        Assert.That(retained.Aggregate.TargetAssembly?.CoveredStatements ?? 0, Is.Zero);
        Assert.That(retained.Aggregate.ReportSha256, Is.EqualTo(Artifact(CoverageArtifactKind.AggregateJson).Sha256));
        Assert.That(retained.IndividualCaseCoverage || retained.CompleteBaseline || retained.DependencyClosureProven, Is.False);
        Assert.That(result.CompleteBaseline || result.MainProvenanceVerified || result.TiaSelectionAuthorized, Is.False);
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(retained));
        Assert.That(serialized.RootElement.GetProperty("Members").EnumerateArray().All(member =>
            !member.TryGetProperty("Aggregate", out _) && !member.TryGetProperty("EmptyProduct", out _)), Is.True,
            "One group observation and one responsibility record cannot be copied into per-member coverage.");
        AssertBaselineBytes(before);
    }

    [TestCase("missing-admission", TestName = "ordered-group/reject-empty-without-independent-responsibility")]
    [TestCase("wrong-admission", TestName = "ordered-group/reject-empty-with-wrong-independent-responsibility")]
    [TestCase("missing-declarations", TestName = "ordered-group/reject-empty-without-member-responsibilities")]
    [TestCase("wrong-declaration", TestName = "ordered-group/reject-empty-with-wrong-member-responsibility")]
    [TestCase("mixed-product-member", TestName = "ordered-group/reject-empty-with-product-bearing-member")]
    [TestCase("missing-dependency", TestName = "ordered-group/reject-empty-member-without-file-dependency")]
    [TestCase("duplicate-dependency", TestName = "ordered-group/reject-duplicate-member-dependency")]
    [TestCase("wrong-union", TestName = "ordered-group/reject-incomplete-independent-dependency-union")]
    [TestCase("changed-bytes", TestName = "ordered-group/reject-changed-resource-dependency-bytes")]
    [TestCase("unsafe-path", TestName = "ordered-group/reject-resource-dependency-outside-snapshot")]
    public void EmptyProductCannotExemptAnUndeclaredMixedOrUnboundGroup(string fault)
    {
        var scenario = PrepareBaselineScenario();
        var explanation = PrepareGroupResourceDependencies();
        SetAggregateEmpty(absent: true);
        var group = PrepareBaselineGroup(DeclareGroupResourceMember, explanation);
        // PrepareBaselineGroup independently admits the complete valid raw owner
        // before these faults. Discovery mutations reseal all dependent records,
        // so rejection cannot be borrowed from a stale envelope or digest.
        switch (fault)
        {
            case "missing-admission": group = group with { Expectation = group.Expectation with { EmptyProduct = null } }; break;
            case "wrong-admission": group = group with { Expectation = group.Expectation with
                { EmptyProduct = explanation with { Reason = EmptyProductCoverageReason.Unspecified } } }; break;
            case "wrong-union": group = group with { Expectation = group.Expectation with
                { EmptyProduct = explanation with { Dependencies = [explanation.Dependencies[0]] } } }; break;
            case "changed-bytes": File.AppendAllText(Path.Combine(_expected.SourceDirectory, "config.json"), "changed bytes"); break;
            default:
                group = RewriteGroupResourceDeclarations(group, leaves =>
                {
                    IEnumerable<XElement> Properties(XElement leaf, string name) => leaf.Element("properties")!.Elements("property")
                        .Where(item => (string?)item.Attribute("name") == name);
                    if (fault == "missing-declarations")
                        foreach (var leaf in leaves) Properties(leaf, "FishAudioEmptyProductCoverage").Remove();
                    else if (fault == "wrong-declaration")
                        Properties(leaves[1], "FishAudioEmptyProductCoverage").Single().SetAttributeValue("value", "ProductCode");
                    else if (fault == "mixed-product-member")
                    {
                        Properties(leaves[1], "FishAudioEmptyProductCoverage").Remove();
                        Properties(leaves[1], "FishAudioRepositoryDependency").Remove();
                    }
                    else if (fault == "missing-dependency") Properties(leaves[1], "FishAudioRepositoryDependency").Remove();
                    else if (fault == "duplicate-dependency") leaves[1].Element("properties")!.Add(BaselineProperty("FishAudioRepositoryDependency", "config.json"));
                    else if (fault == "unsafe-path") Properties(leaves[1], "FishAudioRepositoryDependency").Single().SetAttributeValue("value", "../outside.json");
                    else throw new InvalidOperationException("unknown-group-resource-fixture-fault");
                });
                break;
        }
        var before = CaptureBaselineBytes(scenario.Admission);
        var error = Assert.Throws<OrderedProfileCoverageException>(() => OrderedProfileCoverageSession.ReadValidated(group.Input, group.Expectation));
        var reason = fault switch
        {
            "missing-admission" or "wrong-admission" => "empty-product-reason-required",
            "missing-declarations" or "mixed-product-member" => "group-empty-product-declaration-required",
            "wrong-declaration" => "group-invalid-empty-product-declaration",
            "missing-dependency" or "duplicate-dependency" => "group-invalid-repository-dependency-declaration",
            "wrong-union" => "group-empty-product-dependency-declaration-mismatch",
            "changed-bytes" => "repository-dependency-mismatch",
            "unsafe-path" => "invalid-relative-path",
            _ => throw new InvalidOperationException("unknown-group-resource-fixture-fault"),
        };
        Assert.That(error!.Code, Is.EqualTo("ordered-profile-coverage-coverage-session-" + reason));
        Assert.That(error.Message, Does.Not.Contain(_root));
        AssertBaselineBytes(before);
    }

    [Test]
    [Property("FishAudioCaseId", "ordered-group/reject-repository-exemption-for-observed-product")]
    public void ObservedProductWorkCannotAcquireARepositoryOnlyExemption()
    {
        PrepareBaselineScenario();
        var explanation = PrepareGroupResourceDependencies();
        var group = PrepareBaselineGroup();
        Assert.That(OrderedProfileCoverageSession.ReadValidated(group.Input, group.Expectation).Aggregate.TargetAssembly!.CoveredStatements,
            Is.GreaterThan(0));
        var expectation = group.Expectation with { EmptyProduct = explanation };
        var error = Assert.Throws<OrderedProfileCoverageException>(() => OrderedProfileCoverageSession.ReadValidated(group.Input, expectation));
        Assert.That(error!.Code, Is.EqualTo("ordered-profile-coverage-coverage-session-group-unexpected-empty-product-explanation"));
    }

    private EmptyProductCoverage PrepareGroupResourceDependencies()
    {
        File.WriteAllText(Path.Combine(_expected.SourceDirectory, "config.json"), "{\"resourceMode\":true}", new UTF8Encoding(false));
        return new(EmptyProductCoverageReason.RepositoryResourcesOnly,
            [new("config.json", Hash(Path.Combine(_expected.SourceDirectory, "config.json"))),
                new("README.md", Hash(Path.Combine(_expected.SourceDirectory, "README.md")))]);
    }

    private static void DeclareGroupResourceMember(XElement leaf, int ordinal)
    {
        leaf.Element("properties")!.Add(BaselineProperty("FishAudioEmptyProductCoverage", "RepositoryResourcesOnly"),
            BaselineProperty("FishAudioRepositoryDependency", ordinal == 0 ? "README.md" : "config.json"));
    }

    private CoverageBaselineOrderedGroup RewriteGroupResourceDeclarations(CoverageBaselineOrderedGroup group, Action<XElement[]> change)
    {
        var discovery = XDocument.Load(FrameworkArtifactPath(CoverageArtifactKind.Discovery));
        change(discovery.Descendants("test-case").ToArray());
        SetFrameworkArtifact(CoverageArtifactKind.Discovery, Artifact(CoverageArtifactKind.Discovery).RelativePath,
            discovery.ToString(SaveOptions.DisableFormatting));
        var digest = Artifact(CoverageArtifactKind.Discovery).Sha256;
        var envelope = group.Expectation.Supervisor.FrameworkEnvelope;
        var requestPath = Path.Combine(_input.ArtifactDirectory, envelope.ExecutionRequest.RelativePath);
        var request = JsonNode.Parse(File.ReadAllText(requestPath))!.AsObject();
        request["orderedProfiledLaunch"]!["rawDiscoverySha256"] = digest;
        WriteFrameworkText(requestPath, request.ToJsonString(FrameworkFixtureJson), _started.AddMilliseconds(500));
        var supervisorPath = Path.Combine(_input.ArtifactDirectory, envelope.Supervisor.RelativePath);
        var supervisor = JsonNode.Parse(File.ReadAllText(supervisorPath))!.AsObject();
        supervisor["requestSha256"] = Hash(requestPath);
        supervisor["launch"]!["rawDiscoverySha256"] = digest;
        WriteFrameworkText(supervisorPath, supervisor.ToJsonString(FrameworkFixtureJson), _started.AddMilliseconds(9900));
        return group with
        {
            Input = group.Input with { Artifacts = _input.Artifacts },
            Expectation = group.Expectation with
            {
                DiscoverySha256 = digest,
                Supervisor = group.Expectation.Supervisor with
                {
                    Artifacts = _input.Artifacts,
                    FrameworkEnvelope = envelope with { ExecutionRequest = SealFrameworkFixture(requestPath),
                        Supervisor = SealFrameworkFixture(supervisorPath) },
                },
            },
        };
    }
}
