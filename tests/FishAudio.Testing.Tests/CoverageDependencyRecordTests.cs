using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

public sealed partial class IsolatedCoverageSessionTests
{
    private const string DependencyTestSource = "tests/FishAudio.Testing.Tests/CoverageDependencyRecordTests.cs";
    private string _dependencyProductAggregate = "";

    [TestCase(false, TestName = "Dependency_FrameworkEmpty_SynchronousSource")]
    [TestCase(true, TestName = "Dependency_FrameworkEmpty_AsyncKickoffSource")]
    public void FrameworkDependencyRequiresRealSourceAndSuccessfulEnvelope(bool asyncSource)
    {
        var observation = PrepareDependencyObservation("FrameworkLifecycleOnly", asyncSource);
        var admitted = ValidateDependencyFixture(observation);

        Assert.That(CoverageDependencies.AllowsEmpty(admitted), Is.True);
        Assert.That(admitted.Observation.Cases.Single().TestSourceResolved, Is.True);
        Assert.That(admitted.Observation.Cases.Single().CodeSources.Select(item => item.RelativePath),
            Does.Contain(DependencyTestSource));
        Assert.That(admitted.UnknownAction, Is.EqualTo("full"));
        Assert.That(admitted.CompleteBaseline, Is.False);
    }

    [Test]
    public void DefaultProductDependencyDoesNotTurnUnknownIntoAnEmptyExemption()
    {
        var observation = PrepareDependencyObservation(null);
        var admitted = ValidateDependencyFixture(observation);
        Assert.That(observation.Cases.Single().Responsibility, Is.EqualTo(CoverageResponsibility.ProductExpected));
        Assert.That(observation.Cases.Single().UnknownFull, Is.True);
        Assert.That(CoverageDependencies.AllowsEmpty(admitted), Is.False);
        SetAggregateEmpty(absent: true);
        var rejected = Assert.Throws<IsolatedCoverageSessionException>(() => ValidateDependencyFixture(observation));
        Assert.That(rejected!.Code, Does.Contain("empty-product-expected-hit-required"));
    }

    [Test]
    public void NonemptyProductOwnerKeepsItsRepositoryAndEnumerationDependencies()
    {
        var observation = PrepareDependencyObservation(null, resourceAndScope: true);
        var admitted = ValidateDependencyFixture(observation);
        Assert.DoesNotThrow(() => CoverageDependencies.ValidateNonempty(admitted));
        var dependencies = admitted.Observation.Cases.Single();
        Assert.That(dependencies.Resources.Select(item => item.RelativePath), Is.EqualTo(new[] { "assets/required.json" }));
        Assert.That(dependencies.Scopes.Single().Files.Select(item => item.RelativePath),
            Is.EqualTo(new[] { "assets/catalog/one.json" }));
        Assert.That(dependencies.Scopes.Single().Declaration.ExpectedMissing, Is.EqualTo(new[] { "assets/catalog/not-created.json" }));
    }

    [Test]
    public void LegacyOnlyResourceResponsibilityRetainsItsStrictEmptyAdmission()
    {
        var observation = PrepareLegacyResourceDependencyObservation();
        var admitted = IsolatedCoverageSession.ReadValidated(_input, _expected with { Dependencies = observation });
        Assert.That(admitted.Aggregate.TargetAssembly, Is.Null);
        Assert.That(admitted.EmptyProduct!.Reason, Is.EqualTo(EmptyProductCoverageReason.RepositoryResourcesOnly));
        Assert.That(admitted.EmptyProduct.Dependencies.Single().RelativePath, Is.EqualTo("assets/required.json"));
        Assert.That(admitted.EmptyProduct.Dependencies.Single().Sha256,
            Is.EqualTo(Hash(Path.Combine(_expected.SourceDirectory, "assets/required.json"))));
        Assert.That(admitted.Dependencies!.Observation.Cases.Single().Responsibility,
            Is.EqualTo(CoverageResponsibility.RepositoryResourcesOnly));
        Assert.That(admitted.Dependencies.Observation.Cases.Single().Scopes.Single().Files.Single().RelativePath,
            Is.EqualTo("assets/catalog/one.json"));
        Assert.That(admitted.CompleteBaseline, Is.False);
    }

    [Test]
    public void ExplicitProductResponsibilityCannotOverrideALegitimateLegacyResourceEmptyOwner()
    {
        var observation = PrepareLegacyResourceDependencyObservation();
        var admitted = ValidateDependencyFixture(observation);
        Assert.That(admitted.Observation.Cases.Single().UnknownFull, Is.True);
        AddDependencyFixtureProperty("FishAudioCoverageResponsibility", "ProductExpected");
        var rejected = Assert.Throws<CoverageDependencyException>(() => ObserveDependencyFixture());
        Assert.That(rejected!.Code, Is.EqualTo("coverage-dependencies-conflicting-responsibility-declarations"));
        var session = Assert.Throws<IsolatedCoverageSessionException>(() => ValidateDependencyFixture(observation));
        Assert.That(session!.Code, Does.Contain("coverage-dependencies-conflicting-responsibility-declarations"));
    }

    [TestCase("source", TestName = "Dependency_ChangedTestSourceRejects")]
    [TestCase("resource", TestName = "Dependency_ChangedResourceRejects")]
    [TestCase("new-member", TestName = "Dependency_NewGlobMemberRejects")]
    [TestCase("missing-now-present", TestName = "Dependency_AbsenceChangedRejects")]
    public void DependencyChangesCannotBeReconciledByTheOldObservation(string mutation)
    {
        var observation = PrepareDependencyObservation(null, resourceAndScope: true);
        _ = ValidateDependencyFixture(observation);
        switch (mutation)
        {
            case "source": File.AppendAllText(Path.Combine(_expected.SourceDirectory, DependencyTestSource), "\n// changed\n"); break;
            case "resource": File.WriteAllText(Path.Combine(_expected.SourceDirectory, "assets/required.json"), "changed"); break;
            case "new-member": File.WriteAllText(Path.Combine(_expected.SourceDirectory, "assets/catalog/two.json"), "new"); break;
            case "missing-now-present": File.WriteAllText(Path.Combine(_expected.SourceDirectory, "assets/catalog/not-created.json"), "now present"); break;
        }
        AssertDependencySessionRejected(observation);
    }

    [TestCase("case", TestName = "Dependency_ForeignCaseRejects")]
    [TestCase("binary", TestName = "Dependency_ForeignTestsBinaryRejects")]
    [TestCase("discovery", TestName = "Dependency_ForeignDiscoveryRejects")]
    [TestCase("missing-case", TestName = "Dependency_MissingCaseRejects")]
    public void DependencyObservationIsBoundToItsActualOwner(string mutation)
    {
        var observation = PrepareDependencyObservation("FrameworkLifecycleOnly");
        _ = ValidateDependencyFixture(observation);
        observation = mutation switch
        {
            "case" => observation with { Cases = [observation.Cases.Single() with { Case = observation.Cases.Single().Case with { CanonicalId = "foreign/case" } }] },
            "binary" => observation with { TestsBinary = observation.TestsBinary with { DllSha256 = new string('0', 64) } },
            "discovery" => observation with { DiscoverySha256 = new string('0', 64) },
            _ => observation with { Cases = [] },
        };
        AssertDependencySessionRejected(observation);
    }

    [Test]
    public void FrameworkDependencyCannotBindWithoutAnActuallyValidatedEnvelopeBoundary()
    {
        var observation = PrepareDependencyObservation("FrameworkLifecycleOnly");
        _ = ValidateDependencyFixture(observation);
        _expected = _expected with { Supervisor = _expected.Supervisor with { FrameworkEnvelope = null } };
        Assert.Throws<IsolatedCoverageSessionException>(() => ValidateDependencyFixture(observation));
    }

    [TestCase("foreign", TestName = "Dependency_ForeignRawFrameworkSupervisorRejects")]
    [TestCase("tampered", TestName = "Dependency_TamperedRawFrameworkSupervisorRejects")]
    public void AValidObservationDoesNotAuthenticateAnUnvalidatedFrameworkDescriptor(string mutation)
    {
        var observation = PrepareDependencyObservation("FrameworkLifecycleOnly");
        _ = ValidateDependencyFixture(observation);
        var envelope = _expected.Supervisor.FrameworkEnvelope!;
        if (mutation == "foreign") ChangeFrameworkEnvelope(envelope with { Supervisor = envelope.HostEnd });
        else File.AppendAllText(Path.Combine(_input.ArtifactDirectory, envelope.Supervisor.RelativePath), " ");
        Assert.Throws<IsolatedCoverageSessionException>(() => ValidateDependencyFixture(observation));
    }

    [TestCase("owner-kind", TestName = "Dependency_SameRawFrameworkCannotMoveToAnotherOwnerKind")]
    [TestCase("run", TestName = "Dependency_SameRawFrameworkCannotMoveToAnotherRun")]
    [TestCase("session", TestName = "Dependency_SameRawFrameworkCannotMoveToAnotherSession")]
    public void TheValidatedFrameworkCapabilityIsBoundToItsActualLiveOwner(string mutation)
    {
        var observation = PrepareDependencyObservation("FrameworkLifecycleOnly");
        _ = ValidateDependencyFixture(observation);
        var tests = _expected.Binaries.Single(item => item.Role == CoverageBinaryRole.Tests);
        var testsDll = Path.Combine(_expected.BuildDirectory, tests.DllRelativePath);
        var testsPdb = Path.Combine(_expected.BuildDirectory, tests.PdbRelativePath);
        var paths = _input.Artifacts.ToDictionary(item => item.Kind, item => FrameworkArtifactPath(item.Kind));
        var guards = new List<FileStream>();
        var completed = false;
        try
        {
            foreach (var path in paths.Values.Concat(new[] { testsDll, testsPdb }))
                guards.Add(new(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            var relative = _input.Artifacts.Select(item => item.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var binaryPaths = _expected.Binaries.SelectMany(item => new[] { item.DllRelativePath, item.PdbRelativePath })
                .Select(item => Path.Combine(_expected.BuildDirectory, item)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var catalog = NUnitEvidence.ReadCatalog(paths[CoverageArtifactKind.Discovery]);
            var framework = IsolatedCoverageSession.ValidatedDependencyFramework.ReadIsolatedValidated(
                _expected.Supervisor.FrameworkEnvelope!, _expected, _input.ArtifactDirectory, _expected.BuildDirectory,
                _expected.SourceDirectory, testsDll, paths, relative, binaryPaths, catalog, guards);
            CoverageDependencyRecord Bind(string owner, string run, string session) => CoverageDependencies.Validate(observation,
                _expected.Identity.Source, _expected.Identity.BuildId, _expected.SourceDirectory, testsDll, testsPdb,
                paths[CoverageArtifactKind.Discovery], catalog.Where(item => item.CanonicalId == CanonicalId).ToArray(),
                owner, run, session, framework, guards);
            _ = Bind("isolated-case", _expected.Identity.RunId, _expected.Identity.SessionId);
            var rejected = Assert.Throws<CoverageDependencyException>(() => Bind(
                mutation == "owner-kind" ? "ordered-profile-group" : "isolated-case",
                mutation == "run" ? new string('a', 32) : _expected.Identity.RunId,
                mutation == "session" ? new string('b', 32) : _expected.Identity.SessionId));
            Assert.That(rejected!.Code, Is.EqualTo("coverage-dependencies-independent-owner-binding"));
            completed = true;
        }
        finally
        {
            var releaseFailed = false;
            foreach (var guard in guards) { try { guard.Dispose(); } catch { releaseFailed = true; } }
            if (releaseFailed && completed) Assert.Fail("dependency-fixture: guard release failed.");
            if (releaseFailed) TestContext.Out.WriteLine("dependency-fixture: secondary guard release failed; original failure retained.");
        }
    }

    [Test]
    public void ADeclaredNonproductOwnerCannotClaimAnObservedProductAggregate()
    {
        var observation = PrepareDependencyObservation("FrameworkLifecycleOnly");
        _ = ValidateDependencyFixture(observation);
        ReplaceArtifact(CoverageArtifactKind.AggregateJson, _dependencyProductAggregate);
        AssertDependencySessionRejected(observation);
    }

    [Test]
    public void FrameworkDeclarationWithoutItsCodeDependencyDoesNotEstablishLegitimateEmptyWork()
    {
        var observation = PrepareDependencyObservation("FrameworkLifecycleOnly");
        _ = ValidateDependencyFixture(observation);
        var discovery = XDocument.Load(FrameworkArtifactPath(CoverageArtifactKind.Discovery));
        discovery.Descendants("property").Where(item => (string?)item.Attribute("name") == "FishAudioCodeDependency").Remove();
        SetFrameworkArtifact(CoverageArtifactKind.Discovery, Artifact(CoverageArtifactKind.Discovery).RelativePath,
            discovery.ToString(SaveOptions.DisableFormatting));
        ResealDependencyFrameworkFixture();
        Assert.Throws<CoverageDependencyException>(() => ObserveDependencyFixture());
    }

    [TestCase("../outside", "*.json", TestName = "Dependency_EscapingDirectoryRejects")]
    [TestCase("assets/catalog", "../*.json", TestName = "Dependency_EscapingPatternRejects")]
    [TestCase("assets/catalog", "", TestName = "Dependency_EmptyPatternRejects")]
    public void DependencyScopesCannotEscapeTheirDeclaredSourceBoundary(string directory, string pattern)
    {
        var observation = PrepareDependencyObservation(null);
        _ = ValidateDependencyFixture(observation);
        AddDependencyFixtureProperty("FishAudioDependencyScope", JsonSerializer.Serialize(
            new CoverageDependencyScopeDeclaration(directory, pattern, false, true, []), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Throws<CoverageDependencyException>(() => ObserveDependencyFixture());
    }

    private CoverageDependencyObservation PrepareDependencyObservation(string? responsibility, bool asyncSource = false,
        bool resourceAndScope = false)
    {
        PrepareFrameworkSession();
        _dependencyProductAggregate = File.ReadAllText(FrameworkArtifactPath(CoverageArtifactKind.AggregateJson));
        var source = FindDependencyTestSource();
        var destination = Path.Combine(_expected.SourceDirectory, DependencyTestSource);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination);
        var document = XDocument.Load(FrameworkArtifactPath(CoverageArtifactKind.Discovery));
        var leaf = DependencyFixtureLeaf(document);
        leaf.SetAttributeValue("classname", typeof(IsolatedCoverageSessionTests).FullName);
        leaf.SetAttributeValue("methodname", asyncSource ? nameof(DependencyAwaitingSourceHelper) : nameof(FrameworkDependencyRequiresRealSourceAndSuccessfulEnvelope));
        SetFrameworkArtifact(CoverageArtifactKind.Discovery, Artifact(CoverageArtifactKind.Discovery).RelativePath,
            document.ToString(SaveOptions.DisableFormatting));
        var terminal = XDocument.Load(FrameworkArtifactPath(CoverageArtifactKind.NUnitXml));
        var result = DependencyFixtureLeaf(terminal);
        result.SetAttributeValue("classname", typeof(IsolatedCoverageSessionTests).FullName);
        result.SetAttributeValue("methodname", (string?)leaf.Attribute("methodname"));
        SetFrameworkArtifact(CoverageArtifactKind.NUnitXml, Artifact(CoverageArtifactKind.NUnitXml).RelativePath,
            terminal.ToString(SaveOptions.DisableFormatting));
        if (responsibility is not null)
        {
            AddDependencyFixtureProperty("FishAudioCoverageResponsibility", responsibility);
            AddDependencyFixtureProperty("FishAudioCodeDependency", DependencyTestSource);
        }
        if (resourceAndScope)
        {
            Directory.CreateDirectory(Path.Combine(_expected.SourceDirectory, "assets/catalog"));
            File.WriteAllText(Path.Combine(_expected.SourceDirectory, "assets/required.json"), "required");
            File.WriteAllText(Path.Combine(_expected.SourceDirectory, "assets/catalog/one.json"), "one");
            AddDependencyFixtureProperty("FishAudioRepositoryDependency", "assets/required.json");
            AddDependencyFixtureProperty("FishAudioDependencyScope", JsonSerializer.Serialize(
                new CoverageDependencyScopeDeclaration("assets/catalog", "*.json", false, true, ["assets/catalog/not-created.json"]),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
        if (responsibility == "FrameworkLifecycleOnly") SetAggregateEmpty(absent: true);
        ResealDependencyFrameworkFixture();
        return ObserveDependencyFixture();
    }

    private CoverageDependencyObservation ObserveDependencyFixture() => CoverageDependencies.Observe(new(1,
        _expected.Identity.Source, _expected.Identity.BuildId, _expected.SourceDirectory,
        Path.Combine(_expected.BuildDirectory, _expected.Binaries.Single(item => item.Role == CoverageBinaryRole.Tests).DllRelativePath),
        Path.Combine(_expected.BuildDirectory, _expected.Binaries.Single(item => item.Role == CoverageBinaryRole.Tests).PdbRelativePath),
        FrameworkArtifactPath(CoverageArtifactKind.Discovery), [CanonicalId], [new("/_/", "")])
    { TrackedSources = [new(DependencyTestSource, Hash(Path.Combine(_expected.SourceDirectory, DependencyTestSource)))] });

    private CoverageDependencyObservation PrepareLegacyResourceDependencyObservation()
    {
        var product = PrepareDependencyObservation(null, resourceAndScope: true);
        _ = ValidateDependencyFixture(product);
        AddDependencyFixtureProperty("FishAudioEmptyProductCoverage", "RepositoryResourcesOnly");
        SetAggregateEmpty(absent: true);
        _input = _input with { EmptyProduct = new(EmptyProductCoverageReason.RepositoryResourcesOnly,
            [new("assets/required.json", Hash(Path.Combine(_expected.SourceDirectory, "assets/required.json")))]) };
        ResealDependencyFrameworkFixture();
        return ObserveDependencyFixture();
    }

    private CoverageDependencyRecord ValidateDependencyFixture(CoverageDependencyObservation observation)
    {
        var admitted = IsolatedCoverageSession.ReadValidated(_input, _expected with { Dependencies = observation });
        Assert.That(admitted.Dependencies, Is.Not.Null);
        return admitted.Dependencies!;
    }

    private void AssertDependencySessionRejected(CoverageDependencyObservation observation)
    {
        var rejected = Assert.Throws<IsolatedCoverageSessionException>(() => ValidateDependencyFixture(observation));
        Assert.That(rejected!.Code, Does.Contain("coverage-dependencies-"));
    }

    private void ResealDependencyFrameworkFixture()
    {
        _expected = _expected with { DiscoverySha256 = Artifact(CoverageArtifactKind.Discovery).Sha256 };
        var envelope = _expected.Supervisor.FrameworkEnvelope!;
        var path = Path.Combine(_input.ArtifactDirectory, envelope.Supervisor.RelativePath);
        var document = JsonNode.Parse(File.ReadAllText(path))!;
        document["rawEvidence"]!["resultXmlSha256"] = Artifact(CoverageArtifactKind.NUnitXml).Sha256;
        document["rawEvidence"]!["trxSha256"] = Artifact(CoverageArtifactKind.Trx).Sha256;
        WriteFrameworkText(path, document.ToJsonString(FrameworkFixtureJson), envelope.Supervisor.LastWriteUtc);
        ChangeFrameworkEnvelope(envelope with { Supervisor = SealFrameworkFixture(path) });
    }

    private void AddDependencyFixtureProperty(string name, string value)
    {
        var document = XDocument.Load(FrameworkArtifactPath(CoverageArtifactKind.Discovery));
        var leaf = DependencyFixtureLeaf(document);
        var properties = leaf.Element("properties");
        if (properties is null) { properties = new("properties"); leaf.Add(properties); }
        properties.Add(new XElement("property", new XAttribute("name", name), new XAttribute("value", value)));
        SetFrameworkArtifact(CoverageArtifactKind.Discovery, Artifact(CoverageArtifactKind.Discovery).RelativePath,
            document.ToString(SaveOptions.DisableFormatting));
        ResealDependencyFrameworkFixture();
    }

    private static XElement DependencyFixtureLeaf(XDocument document) => document.Descendants("test-case").Single(item =>
        item.Elements("properties").Elements("property").Any(property =>
            (string?)property.Attribute("name") == "FishAudioCaseId" && (string?)property.Attribute("value") == CanonicalId));

    private static string FindDependencyTestSource()
    {
        foreach (var start in new[] { Environment.GetEnvironmentVariable("FISHAUDIO_TEST_REPOSITORY_ROOT"), Directory.GetCurrentDirectory(), TestContext.CurrentContext.TestDirectory })
        {
            if (string.IsNullOrEmpty(start)) continue;
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var file = Path.Combine(directory.FullName, DependencyTestSource);
                if (File.Exists(file)) return file;
            }
        }
        throw new InvalidOperationException("dependency-test-source-unavailable");
    }

    private static async Task DependencyAwaitingSourceHelper() => await Task.Yield();
}
