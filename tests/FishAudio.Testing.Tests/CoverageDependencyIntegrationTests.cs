using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

public sealed partial class IsolatedCoverageSessionTests
{
    private static readonly JsonSerializerOptions DependencyIntegrationJson = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };

    [Test]
    public void OldRawOwnersRemainExplicitlyPartialWithoutDependencyRecords()
    {
        var scenario = PrepareBaselineScenario();
        var candidate = CoverageBaselineCandidate.ReadValidated(scenario.Input, scenario.Admission);
        Assert.That(candidate.CoverageUnionComplete, Is.True);
        Assert.That(candidate.DependencyRecordsComplete, Is.False);
        Assert.That(candidate.IsolatedOwners.All(item => item.Coverage.Dependencies is null), Is.True);
        Assert.That(candidate.CompleteBaseline || candidate.TiaSelectionAuthorized, Is.False);
    }

    [TestCase(false, TestName = "DependencyIntegration_CompleteOwnerRecords")]
    [TestCase(true, TestName = "DependencyIntegration_CompleteOwnerRecordsRetainResourcesAndScopes")]
    public void CompleteRawOwnerRecordsRemainSeparateFromSourceClosure(bool resources)
    {
        var scenario = PrepareDependencyBaseline(resources);
        var candidate = CoverageBaselineCandidate.ReadValidated(scenario.Input, scenario.Admission);
        Assert.That(candidate.DependencyRecordsComplete, Is.True);
        Assert.That(candidate.IsolatedOwners, Has.Count.EqualTo(2));
        Assert.That(candidate.IsolatedOwners.All(item => item.Coverage.Dependencies!.Observation.Cases.Count == 1), Is.True);
        Assert.That(candidate.IsolatedOwners.All(item => item.Coverage.Dependencies!.Observation.Cases.Single().UnknownFull), Is.True);
        // The existing hand-declared Example.* protocol fixtures deliberately
        // have no PE method match. Their positive Product aggregates still have
        // exact isolated raw ownership; no source closure is invented for them.
        Assert.That(candidate.IsolatedOwners.All(item => !item.Coverage.Dependencies!.Observation.Cases.Single().TestSourceResolved), Is.True);
        if (resources)
        {
            var dependencies = candidate.IsolatedOwners[0].Coverage.Dependencies!.Observation.Cases.Single();
            Assert.That(dependencies.Resources.Single().RelativePath, Is.EqualTo("assets/required.json"));
            Assert.That(dependencies.Resources.Single().Sha256, Is.EqualTo(Hash(Path.Combine(_expected.SourceDirectory, "assets/required.json"))));
            Assert.That(dependencies.Scopes.Single().Files.Single().RelativePath, Is.EqualTo("assets/catalog/one.json"));
        }
        Assert.That(candidate.SourceMappingsResolved || candidate.DependencyClosureProven || candidate.CompleteBaseline ||
            candidate.MainProvenanceVerified || candidate.PublicationAuthorized || candidate.TiaSelectionAuthorized, Is.False);
    }

    [TestCase("FishAudioCoverageResponsibility", "ProductExpected", TestName = "DependencyIntegration_FreshFixtureResponsibilityDriftRejects")]
    [TestCase("FishAudioCodeDependency", "tests/other.cs", TestName = "DependencyIntegration_FreshFixtureCodeDependencyDriftRejects")]
    [TestCase("FishAudioRepositoryDependency", "assets/other.json", TestName = "DependencyIntegration_FreshFixtureResourceDriftRejects")]
    [TestCase("FishAudioDependencyScope", "{}", TestName = "DependencyIntegration_FreshFixtureScopeDriftRejects")]
    public void FreshCatalogCannotChangeInheritedDependencyDeclarations(string name, string value)
    {
        var scenario = PrepareDependencyBaseline(resources: false);
        Assert.That(CoverageBaselineCandidate.ReadValidated(scenario.Input, scenario.Admission).DependencyRecordsComplete, Is.True);
        var catalog = scenario.Admission.Catalogs[0];
        var document = XDocument.Load(catalog.DiscoveryPath);
        var fixture = document.Descendants("test-suite").Single(item => (string?)item.Attribute("type") == "TestFixture");
        fixture.Add(new XElement("properties", BaselineProperty(name, value)));
        File.WriteAllText(catalog.DiscoveryPath, document.ToString(SaveOptions.DisableFormatting));
        catalog = catalog with { DiscoverySha256 = Hash(catalog.DiscoveryPath) };
        var expected = scenario.Admission with { Catalogs = [catalog, scenario.Admission.Catalogs[1]] };
        var rejected = Assert.Throws<CoverageBaselineCandidateException>(() => CoverageBaselineCandidate.ReadValidated(scenario.Input, expected));
        Assert.That(rejected!.Code, Does.Contain("fresh-catalog-reconciliation"));
    }

    [Test]
    public void FreshCatalogResourceMultivaluesReconcileAsASet()
    {
        var scenario = PrepareDependencyBaseline(resources: false);
        var directory = Path.Combine(_expected.SourceDirectory, "assets");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "first.json"), "first");
        File.WriteAllText(Path.Combine(directory, "second.json"), "second");
        var document = XDocument.Load(FrameworkArtifactPath(CoverageArtifactKind.Discovery));
        var fixture = document.Descendants("test-suite").Single(item => (string?)item.Attribute("type") == "TestFixture");
        fixture.Add(new XElement("properties", BaselineProperty("FishAudioRepositoryDependency", "assets/first.json"),
            BaselineProperty("FishAudioRepositoryDependency", "assets/second.json")));
        SetFrameworkArtifact(CoverageArtifactKind.Discovery, Artifact(CoverageArtifactKind.Discovery).RelativePath,
            document.ToString(SaveOptions.DisableFormatting));
        RefreshBaselineSeals(this);
        var owner = WithDependencyObservation(this, "first");
        var fresh = FreshBaselineCatalog("first", this, "fresh-set-1");
        var freshDocument = XDocument.Load(fresh.DiscoveryPath);
        var values = freshDocument.Descendants("property").Where(item => (string?)item.Attribute("name") == "FishAudioRepositoryDependency").ToArray();
        values[0].SetAttributeValue("value", "assets/second.json"); values[1].SetAttributeValue("value", "assets/first.json");
        File.WriteAllText(fresh.DiscoveryPath, freshDocument.ToString(SaveOptions.DisableFormatting));
        fresh = fresh with { DiscoverySha256 = Hash(fresh.DiscoveryPath) };
        var input = scenario.Input with { IsolatedOwners = [owner, scenario.Input.IsolatedOwners[1]] };
        var expected = scenario.Admission with { Catalogs = [fresh, scenario.Admission.Catalogs[1]] };
        var candidate = CoverageBaselineCandidate.ReadValidated(input, expected);
        Assert.That(candidate.DependencyRecordsComplete, Is.True);
        Assert.That(candidate.IsolatedOwners[0].Coverage.Dependencies!.Observation.Cases.Single().Resources, Has.Count.EqualTo(2));
    }

    [Test]
    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    public void DependencyObserverCliPublishesMetadataThatTheRawOwnerCanReimport()
    {
        var observation = PrepareDependencyObservation(null, resourceAndScope: true);
        var files = DependencyObserverCommandFiles(observation);
        var result = InvokeDependencyObserverCommand(files.Arguments);
        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(result.Error, Is.Empty);
        using var output = JsonDocument.Parse(File.ReadAllBytes(files.Output));
        Assert.That(output.RootElement.GetProperty("kind").GetString(), Is.EqualTo("coverage-dependencies-observe"));
        Assert.That(output.RootElement.GetProperty("complete").GetBoolean(), Is.True);
        Assert.That(output.RootElement.GetProperty("baselineClaim").GetBoolean(), Is.False);
        var value = output.RootElement.GetProperty("value");
        Assert.That(value.TryGetProperty("authority", out _), Is.False);
        var rebound = value.Deserialize<CoverageDependencyObservation>(DependencyIntegrationJson)!;
        var admitted = IsolatedCoverageSession.ReadValidated(_input, _expected with { Dependencies = rebound });
        Assert.That(admitted.Dependencies!.Observation.Cases.Single().Resources.Single().RelativePath, Is.EqualTo("assets/required.json"));
    }

    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    [TestCase("unknown", TestName = "DependencyObserverCli_UnknownOptionRejects")]
    [TestCase("duplicate", TestName = "DependencyObserverCli_DuplicateOptionRejects")]
    public void DependencyObserverCliRejectsInvalidOptions(string fault)
    {
        var observation = PrepareDependencyObservation(null);
        var files = DependencyObserverCommandFiles(observation);
        var key = fault == "unknown" ? "--unknown" : "--input";
        var result = InvokeDependencyObserverCommand(files.Arguments.Concat(new[] { key, files.Input }).ToArray());
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Error, Is.EqualTo("coverage-evidence: invalid-input-or-output" + Environment.NewLine));
        Assert.That(File.Exists(files.Output), Is.False);
    }

    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    [TestCase("unknown", TestName = "DependencyObserverCli_UnknownJsonRejects")]
    [TestCase("duplicate", TestName = "DependencyObserverCli_DuplicateJsonRejects")]
    public void DependencyObserverCliKeepsTheExistingStrictJsonBoundary(string fault)
    {
        var observation = PrepareDependencyObservation(null);
        var files = DependencyObserverCommandFiles(observation);
        var original = File.ReadAllText(files.Input);
        var extra = fault == "unknown" ? "\"unknown\":true," : "\"schemaVersion\":1,";
        File.WriteAllText(files.Input, "{" + extra + original[1..]);
        var result = InvokeDependencyObserverCommand(files.Arguments);
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(File.Exists(files.Output), Is.False);
    }

    [Test]
    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    public void DependencyObserverCliKeepsTypedRejectionDistinctFromMalformedInput()
    {
        var observation = PrepareDependencyObservation(null);
        var files = DependencyObserverCommandFiles(observation);
        var input = JsonSerializer.Deserialize<CoverageDependencyObservationInput>(File.ReadAllText(files.Input), DependencyIntegrationJson)!;
        File.WriteAllText(files.Input, JsonSerializer.Serialize(input with { SchemaVersion = 2 }, DependencyIntegrationJson));
        var result = InvokeDependencyObserverCommand(files.Arguments);
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Error, Is.EqualTo("coverage-evidence: coverage-dependencies-input" + Environment.NewLine));
        Assert.That(File.Exists(files.Output), Is.False);
    }

    [Test]
    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    public void DependencyObserverCliCannotOverwriteAnExistingOutput()
    {
        var observation = PrepareDependencyObservation(null);
        var files = DependencyObserverCommandFiles(observation);
        byte[] original = [1, 3, 7, 11];
        File.WriteAllBytes(files.Output, original);
        Assert.That(InvokeDependencyObserverCommand(files.Arguments).ExitCode, Is.EqualTo(2));
        Assert.That(File.ReadAllBytes(files.Output), Is.EqualTo(original));
    }

    private (CoverageBaselineCandidateInput Input, CoverageBaselineAdmission Admission) PrepareDependencyBaseline(bool resources)
    {
        var scenario = PrepareBaselineScenario();
        if (resources)
        {
            Directory.CreateDirectory(Path.Combine(_expected.SourceDirectory, "assets/catalog"));
            File.WriteAllText(Path.Combine(_expected.SourceDirectory, "assets/required.json"), "required");
            File.WriteAllText(Path.Combine(_expected.SourceDirectory, "assets/catalog/one.json"), "one");
            var document = XDocument.Load(FrameworkArtifactPath(CoverageArtifactKind.Discovery));
            var leaf = document.Descendants("test-case").Single();
            leaf.Element("properties")!.Add(BaselineProperty("FishAudioRepositoryDependency", "assets/required.json"),
                BaselineProperty("FishAudioDependencyScope", JsonSerializer.Serialize(new CoverageDependencyScopeDeclaration(
                    "assets/catalog", "*.json", false, true, ["assets/catalog/missing.json"]), DependencyIntegrationJson)));
            SetFrameworkArtifact(CoverageArtifactKind.Discovery, Artifact(CoverageArtifactKind.Discovery).RelativePath,
                document.ToString(SaveOptions.DisableFormatting));
            RefreshBaselineSeals(this);
        }
        var second = _baselineAdditionalSessions.Single();
        return (scenario.Input with { IsolatedOwners = [WithDependencyObservation(this, "first"), WithDependencyObservation(second, "second")] },
            scenario.Admission with { Catalogs = [FreshBaselineCatalog("first", this, "fresh-dependency-1"),
                FreshBaselineCatalog("second", second, "fresh-dependency-2")] });
    }

    private static CoverageBaselineIsolatedOwner WithDependencyObservation(IsolatedCoverageSessionTests fixture, string catalogKey)
    {
        var tests = fixture._expected.Binaries.Single(item => item.Role == CoverageBinaryRole.Tests);
        var observation = CoverageDependencies.Observe(new(1, fixture._expected.Identity.Source, fixture._expected.Identity.BuildId,
            fixture._expected.SourceDirectory, Path.Combine(fixture._expected.BuildDirectory, tests.DllRelativePath),
            Path.Combine(fixture._expected.BuildDirectory, tests.PdbRelativePath), fixture.FrameworkArtifactPath(CoverageArtifactKind.Discovery),
            [fixture._expected.Identity.CanonicalId], [new("/_/", "")]) { TrackedSources = [] });
        var expected = fixture._expected with { Dependencies = observation };
        Assert.That(IsolatedCoverageSession.ReadValidated(fixture._input, expected).Dependencies, Is.Not.Null);
        return new(catalogKey, fixture._input, expected);
    }

    private (string[] Arguments, string Input, string Output) DependencyObserverCommandFiles(CoverageDependencyObservation observation)
    {
        var tests = _expected.Binaries.Single(item => item.Role == CoverageBinaryRole.Tests);
        var input = new CoverageDependencyObservationInput(1, _expected.Identity.Source, _expected.Identity.BuildId,
            _expected.SourceDirectory, Path.Combine(_expected.BuildDirectory, tests.DllRelativePath),
            Path.Combine(_expected.BuildDirectory, tests.PdbRelativePath), FrameworkArtifactPath(CoverageArtifactKind.Discovery),
            observation.Cases.Select(item => item.Case.CanonicalId).ToArray(), observation.DocumentMaps)
        { TrackedSources = observation.TrackedSources };
        var path = Path.Combine(_root, "dependency-cli-input.json");
        var output = Path.Combine(_root, "dependency-cli-output.json");
        File.WriteAllText(path, JsonSerializer.Serialize(input, DependencyIntegrationJson));
        return (["coverage-dependencies-observe", "--input", path, "--output", output], path, output);
    }

    private static (int ExitCode, string Error) InvokeDependencyObserverCommand(string[] args)
    {
        var previousOutput = Console.Out; var previousError = Console.Error;
        using var output = new StringWriter(); using var error = new StringWriter();
        try
        {
            Console.SetOut(output); Console.SetError(error);
            return (CoverageCommands.Run(args), error.ToString());
        }
        finally { Console.SetOut(previousOutput); Console.SetError(previousError); }
    }
}
