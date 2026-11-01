using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using FishAudio.Testing.Evidence;
using FishAudio.Testing.Execution;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

public sealed partial class IsolatedCoverageSessionTests
{
    // These are hand-declared protocol fixtures backed by actual copied DLL/PDBs,
    // not executed profiler sessions or a current complete baseline.
    private readonly List<IsolatedCoverageSessionTests> _baselineAdditionalSessions = [];

    [TearDown]
    public void ReleaseBaselineAdditionalSessions()
    {
        foreach (var fixture in _baselineAdditionalSessions) fixture.ReleaseOwnedSession();
    }

    [Test]
    [Property("FishAudioCaseId", "baseline-candidate/two-catalogs-reconcile-session-local-runtime-ids")]
    public void CompleteRawOwnersReconcileStableMetadataWithoutSharingRuntimeIds()
    {
        var scenario = PrepareBaselineScenario();
        var before = CaptureBaselineBytes(scenario.Admission);
        var result = CoverageBaselineCandidate.ReadValidated(scenario.Input, scenario.Admission);
        Assert.That(result.CoverageUnionComplete && result.AllRequiredOwnersAdmitted, Is.True);
        Assert.That(result.IsolatedOwners.Select(item => item.Coverage.Identity.CanonicalId),
            Is.EquivalentTo(new[] { CanonicalId, "baseline/tool-case" }));
        Assert.That(result.Catalogs.Select(item => item.Cases.Single().PlatformId),
            Is.EqualTo(new[] { "fresh-1", "fresh-2" }));
        Assert.That(result.IsolatedOwners.All(item => item.CatalogBinding.OwnerDiscoverySha256 !=
            item.CatalogBinding.FreshDiscoverySha256), Is.True);
        Assert.That(result.IsolatedOwners.Select(item => item.Coverage.Binaries.Single(binary =>
            binary.Role == CoverageBinaryRole.Tests).Identity.AssemblyName),
            Is.EquivalentTo(new[] { "FishAudio.Testing.Tests", "FishAudio.Testing" }));
        Assert.That(result.BinaryBuildMetadata.All(item => item.SdkVersion is null), Is.True,
            "Current tooling fixture assemblies do not acquire an invented SDK stamp.");
        Assert.That(result.BinaryBuildMetadata.All(item => item.Configuration == "Release"), Is.True);
        Assert.That(result.BuildAuthoritySource, Is.EqualTo("caller-independent-build-admission"));
        Assert.That(result.DependencyUnknownAction, Is.EqualTo("future-full"));
        Assert.That(result.CompleteBaseline || result.MainProvenanceVerified || result.PublicationAuthorized ||
            result.SourceMappingsResolved || result.DependencyClosureProven || result.TiaSelectionAuthorized, Is.False);
        AssertBaselineBytes(before);
    }

    [Test]
    [Property("FishAudioCaseId", "baseline-candidate/explicit-resource-empty-keeps-verified-dependencies")]
    public void CompleteCandidateRetainsTheExistingLegalResourceEmptyOwner()
    {
        var scenario = PrepareBaselineScenario(resourceOnly: true);
        var result = CoverageBaselineCandidate.ReadValidated(scenario.Input, scenario.Admission);
        var resource = result.IsolatedOwners.Single(item => item.Coverage.Identity.CanonicalId == CanonicalId).Coverage;
        Assert.That(resource.Aggregate.TargetAssembly, Is.Null);
        Assert.That(resource.EmptyProduct!.Reason, Is.EqualTo(EmptyProductCoverageReason.RepositoryResourcesOnly));
        Assert.That(resource.EmptyProduct.Dependencies.Single().RelativePath, Is.EqualTo("README.md"));
        Assert.That(resource.EmptyProduct.Dependencies.Single().Sha256,
            Is.EqualTo(Hash(Path.Combine(_expected.SourceDirectory, "README.md"))));
        Assert.That(result.CoverageUnionComplete, Is.True);
    }

    [TestCase("missing-owner", TestName = "baseline-candidate/reject-missing-owner")]
    [TestCase("duplicate-owner", TestName = "baseline-candidate/reject-duplicate-owner")]
    [TestCase("mixed-source", TestName = "baseline-candidate/reject-mixed-source")]
    [TestCase("mixed-build", TestName = "baseline-candidate/reject-mixed-build")]
    [TestCase("wrong-product-binding", TestName = "baseline-candidate/reject-wrong-product-binding")]
    [TestCase("changed-dll", TestName = "baseline-candidate/reject-changed-actual-dll")]
    [TestCase("wrong-configuration", TestName = "baseline-candidate/reject-nonrelease-build-authority")]
    [TestCase("not-run", TestName = "baseline-candidate/reject-not-run-owner")]
    [TestCase("failed-export", TestName = "baseline-candidate/reject-export-failure-even-for-resource-empty")]
    [TestCase("uncontrolled", TestName = "baseline-candidate/reject-owner-without-framework-supervision")]
    public void IncompleteOrMismatchedRawOwnersCannotBecomeACompleteCandidate(string fault)
    {
        var scenario = PrepareBaselineScenario(resourceOnly: fault == "failed-export");
        var input = scenario.Input;
        var admission = scenario.Admission;
        var owner = input.IsolatedOwners[0];
        switch (fault)
        {
            case "missing-owner": input = input with { IsolatedOwners = [owner] }; break;
            case "duplicate-owner": input = input with { IsolatedOwners = [owner, owner] }; break;
            case "mixed-source":
                owner = owner with { Expectation = owner.Expectation with
                { Identity = owner.Expectation.Identity with { Source = owner.Expectation.Identity.Source with { Tree = new string('f', 40) } } } };
                break;
            case "mixed-build":
                owner = owner with { Expectation = owner.Expectation with
                { Identity = owner.Expectation.Identity with { BuildId = new string('f', 64) } } };
                break;
            case "wrong-product-binding":
                owner = owner with { Expectation = owner.Expectation with { Binaries = owner.Expectation.Binaries.Select(binding =>
                    binding.Role == CoverageBinaryRole.Product ? binding with
                    { ExpectedIdentity = binding.ExpectedIdentity with { DllSha256 = new string('f', 64) } } : binding).ToArray() } };
                break;
            case "changed-dll":
                File.AppendAllText(Path.Combine(_expected.BuildDirectory, "product.dll"), "changed bytes");
                break;
            case "wrong-configuration": admission = admission with { BuildAuthority = admission.BuildAuthority with { Configuration = "Debug" } }; break;
            case "not-run":
                var terminal = XDocument.Load(FrameworkArtifactPath(CoverageArtifactKind.NUnitXml));
                terminal.Root!.SetAttributeValue("result", "Skipped");
                terminal.Root.SetAttributeValue("passed", 0);
                terminal.Root.SetAttributeValue("skipped", 1);
                terminal.Descendants("test-case").Single().SetAttributeValue("result", "Skipped");
                SetFrameworkArtifact(CoverageArtifactKind.NUnitXml, Artifact(CoverageArtifactKind.NUnitXml).RelativePath,
                    terminal.ToString(SaveOptions.DisableFormatting));
                owner = owner with { Input = _input, Expectation = _expected };
                break;
            case "failed-export":
                owner = owner with { Expectation = owner.Expectation with { Supervisor = owner.Expectation.Supervisor with
                { Export = owner.Expectation.Supervisor.Export with { ExitCode = 1 } } } };
                break;
            case "uncontrolled": owner = owner with { Expectation = owner.Expectation with { RequireFrameworkSupervision = false,
                Supervisor = owner.Expectation.Supervisor with { FrameworkEnvelope = null } } }; break;
            default: throw new InvalidOperationException("unknown-baseline-fixture-fault");
        }
        if (fault is not "missing-owner" and not "duplicate-owner")
            input = input with { IsolatedOwners = [owner, input.IsolatedOwners[1]] };
        var rejected = Assert.Throws<CoverageBaselineCandidateException>(() => CoverageBaselineCandidate.ReadValidated(input, admission));
        if (fault == "missing-owner") Assert.That(rejected!.UnadmittedCanonicalIds, Is.EqualTo(new[] { "baseline/tool-case" }));
    }

    [TestCase("canonical", TestName = "baseline-candidate/reject-fresh-canonical-drift")]
    [TestCase("fullname", TestName = "baseline-candidate/reject-fresh-parameter-fullname-drift")]
    [TestCase("resource", TestName = "baseline-candidate/reject-fresh-resource-drift")]
    [TestCase("profile", TestName = "baseline-candidate/reject-fresh-profile-drift")]
    [TestCase("empty-dependency", TestName = "baseline-candidate/reject-fresh-empty-dependency-drift")]
    public void ReconciliationRejectsStableMetadataChangesDespiteMatchingDlls(string change)
    {
        var scenario = PrepareBaselineScenario(resourceOnly: change == "empty-dependency");
        var catalog = scenario.Admission.Catalogs[0];
        var document = XDocument.Load(catalog.DiscoveryPath);
        var leaf = document.Descendants("test-case").Single();
        var properties = leaf.Element("properties")!;
        if (change == "canonical") properties.Elements("property").Single(item => (string?)item.Attribute("name") == "FishAudioCaseId").SetAttributeValue("value", "foreign/canonical");
        if (change == "fullname") leaf.SetAttributeValue("fullname", "Example.Cases.Probe(renamed-parameter)");
        if (change == "resource") properties.Add(BaselineProperty("FishAudioExclusiveResource", "new-resource"));
        if (change == "profile")
        {
            properties.Add(BaselineProperty("FishAudioExecutionProfile", "compat.ordered-profile"));
            properties.Add(BaselineProperty("FishAudioProfileOrdinal", "0"));
        }
        if (change == "empty-dependency") properties.Elements("property").Single(item =>
            (string?)item.Attribute("name") == "FishAudioRepositoryDependency").SetAttributeValue("value", "OTHER.md");
        File.WriteAllText(catalog.DiscoveryPath, document.ToString(SaveOptions.DisableFormatting), new UTF8Encoding(false));
        catalog = catalog with { DiscoverySha256 = Hash(catalog.DiscoveryPath) };
        var admission = scenario.Admission with { Catalogs = [catalog, scenario.Admission.Catalogs[1]] };
        Assert.Throws<CoverageBaselineCandidateException>(() => CoverageBaselineCandidate.ReadValidated(scenario.Input, admission));
    }

    [Test]
    [Property("FishAudioCaseId", "baseline-candidate/group-retains-one-original-aggregate-and-ordered-owner")]
    public void CompleteGroupIsRetainedOnceWithoutManufacturingMemberCoverage()
    {
        var scenario = PrepareBaselineScenario();
        var group = PrepareBaselineGroup();
        var first = FreshBaselineCatalog("first", this, "fresh-1");
        var input = scenario.Input with { IsolatedOwners = [scenario.Input.IsolatedOwners[1]], OrderedGroups = [group] };
        var admission = scenario.Admission with { Catalogs = [first, scenario.Admission.Catalogs[1]] };
        var result = CoverageBaselineCandidate.ReadValidated(input, admission);
        Assert.That(result.CoverageUnionComplete, Is.True);
        Assert.That(result.IsolatedOwners, Has.Count.EqualTo(1));
        Assert.That(result.OrderedGroups, Has.Count.EqualTo(1));
        var retained = result.OrderedGroups.Single().Coverage;
        Assert.That(retained.Members.Select(item => item.Case.CanonicalId), Is.EqualTo(new[] { CanonicalId, "session/other" }));
        Assert.That(retained.IndividualCaseCoverage, Is.False);
        Assert.That(retained.Aggregate.ReportSha256, Is.EqualTo(Artifact(CoverageArtifactKind.AggregateJson).Sha256));
        Assert.That(retained.Members.All(item => item.ExecutionDependency == "ordered-native-profile:native-input"), Is.True);
        Assert.That(result.Catalogs[0].Plan.Phases.Single().ExclusiveResources, Does.Contain("desktop"));
    }

    private (CoverageBaselineCandidateInput Input, CoverageBaselineAdmission Admission) PrepareBaselineScenario(bool resourceOnly = false)
    {
        PrepareFrameworkSession();
        ConfigureBaselineDiscovery(this, CanonicalId, resourceOnly);
        if (resourceOnly)
        {
            SetAggregateEmpty(absent: true);
            _input = _input with { EmptyProduct = ResourceExplanation() };
        }
        RefreshBaselineSeals(this);
        var second = new IsolatedCoverageSessionTests();
        second.CreateOwnedSession();
        _baselineAdditionalSessions.Add(second);
        foreach (var pair in new[] { (Name: "product", Assembly: typeof(IsolatedCoverageSessionTests).Assembly.Location),
            (Name: "tests", Assembly: typeof(BinaryIdentity).Assembly.Location) })
        {
            File.Copy(pair.Assembly, Path.Combine(second._root, "build", pair.Name + ".dll"), true);
            File.Copy(Path.ChangeExtension(pair.Assembly, ".pdb"), Path.Combine(second._root, "build", pair.Name + ".pdb"), true);
        }
        second._expected = second._expected with { Binaries = second._expected.Binaries.Select(binding => binding with
        { ExpectedIdentity = BinaryIdentity.ReadValidated(Path.Combine(second._expected.BuildDirectory, binding.DllRelativePath),
            Path.Combine(second._expected.BuildDirectory, binding.PdbRelativePath)) }).ToArray() };
        second.PrepareFrameworkSession();
        RetagBaselineSession(second, "baseline/tool-case", "33333333333333333333333333333333");
        ConfigureBaselineDiscovery(second, "baseline/tool-case", resourceOnly: false);
        second.SetFrameworkArtifact(CoverageArtifactKind.AggregateJson, second.Artifact(CoverageArtifactKind.AggregateJson).RelativePath,
            Aggregate.Replace("\"FishAudio.Testing\"", "\"FishAudio.Testing.Tests\"", StringComparison.Ordinal));
        RefreshBaselineSeals(second);
        var firstCatalog = FreshBaselineCatalog("first", this, "fresh-1");
        var secondCatalog = FreshBaselineCatalog("second", second, "fresh-2");
        var authority = new CoverageBaselineBuildAuthority("fixture-independent-build", new string('e', 64), "10.0.403", "Release", "net10.0-windows",
            _expected.ToolSha256, Hash(Path.Combine(_expected.BuildDirectory, "fixture-sdk", "vstest.console.dll")));
        // Independently admit the raw fixture owners before exercising the new
        // combiner or applying a negative mutation. This prevents invalid test
        // setup from making rejection cases pass for an unrelated reason.
        Assert.DoesNotThrow(() => IsolatedCoverageSession.ReadValidated(_input, _expected), "first raw fixture owner");
        Assert.DoesNotThrow(() => IsolatedCoverageSession.ReadValidated(second._input, second._expected), "second raw fixture owner");
        return (new(1, [new("first", _input, _expected), new("second", second._input, second._expected)], []),
            new(1, _expected.Identity.Source, authority, [firstCatalog, secondCatalog]));
    }

    private static XElement BaselineProperty(string name, string value) => new("property", new XAttribute("name", name), new XAttribute("value", value));

    private static void ConfigureBaselineDiscovery(IsolatedCoverageSessionTests fixture, string caseId, bool resourceOnly)
    {
        var document = XDocument.Parse(Discovery);
        var assembly = document.Descendants("test-suite").Single();
        var leaf = assembly.Elements("test-case").First();
        assembly.Elements("test-case").Skip(1).Remove();
        leaf.Element("properties")!.Elements("property").Single().SetAttributeValue("value", caseId);
        if (resourceOnly) leaf.Element("properties")!.Add(BaselineProperty("FishAudioEmptyProductCoverage", "RepositoryResourcesOnly"),
            BaselineProperty("FishAudioRepositoryDependency", "README.md"));
        leaf.Remove();
        assembly.Add(new XElement("test-suite", new XAttribute("type", "TestFixture"), new XAttribute("id", "fixture-1"),
            new XAttribute("fullname", "Example.Cases"), new XAttribute("runstate", "Runnable"), leaf));
        document.Descendants().Attributes("testcasecount").ToList().ForEach(item => item.Value = "1");
        fixture.SetFrameworkArtifact(CoverageArtifactKind.Discovery, fixture.Artifact(CoverageArtifactKind.Discovery).RelativePath,
            document.ToString(SaveOptions.DisableFormatting));
    }

    private static void RetagBaselineSession(IsolatedCoverageSessionTests fixture, string caseId, string sessionId)
    {
        foreach (var path in Directory.EnumerateFiles(fixture._input.ArtifactDirectory, "*", SearchOption.AllDirectories))
        {
            var written = File.GetLastWriteTimeUtc(path);
            var text = File.ReadAllText(path).Replace(CanonicalId, caseId, StringComparison.Ordinal)
                .Replace("22222222222222222222222222222222", sessionId, StringComparison.Ordinal);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(path, written);
        }
        var identity = fixture._input.Identity with { CanonicalId = caseId, SessionId = sessionId, BuildId = new string('f', 64) };
        fixture._input = fixture._input with { Identity = identity };
        fixture._expected = fixture._expected with { Identity = identity, Supervisor = fixture._expected.Supervisor with { Identity = identity } };
    }

    private static void RefreshBaselineSeals(IsolatedCoverageSessionTests fixture)
    {
        foreach (var artifact in fixture._input.Artifacts.ToArray())
        {
            var path = fixture.FrameworkArtifactPath(artifact.Kind);
            var seconds = artifact.Kind switch
            {
                CoverageArtifactKind.Discovery => 0,
                CoverageArtifactKind.NUnitXml or CoverageArtifactKind.Trx => 8,
                CoverageArtifactKind.CoverInvocation => 2,
                CoverageArtifactKind.CoverProcess => 10,
                CoverageArtifactKind.Snapshot or CoverageArtifactKind.CoverLog or CoverageArtifactKind.CoverStdout or CoverageArtifactKind.CoverStderr => 9,
                _ => 12,
            };
            File.SetLastWriteTimeUtc(path, fixture._started.AddSeconds(seconds).UtcDateTime);
            fixture.UpdateDescriptor(artifact with { Sha256 = Hash(path), Length = new FileInfo(path).Length,
                LastWriteUtc = new(File.GetLastWriteTimeUtc(path)) });
        }
        fixture._expected = fixture._expected with { DiscoverySha256 = fixture.Artifact(CoverageArtifactKind.Discovery).Sha256 };
        fixture.ChangeReview(fixture._expected.Supervisor.Diagnostics with { Evidence = fixture._input.Artifacts
            .Where(item => DiagnosticKinds.Contains(item.Kind)).Select(item => new CoverageDiagnosticBinding(item.Kind, item.Sha256)).ToArray() });
        if (fixture._expected.Supervisor.FrameworkEnvelope is not { } envelope) return;
        // Retagging rewrites terminal and invocation bytes. The hand-declared
        // supervisor must bind those same new bytes before its seal is updated.
        var supervisorPath = Path.Combine(fixture._input.ArtifactDirectory, envelope.Supervisor.RelativePath);
        var supervisor = JsonNode.Parse(File.ReadAllText(supervisorPath))!.AsObject();
        supervisor["rawEvidence"]!["resultXmlSha256"] = fixture.Artifact(CoverageArtifactKind.NUnitXml).Sha256;
        supervisor["rawEvidence"]!["trxSha256"] = fixture.Artifact(CoverageArtifactKind.Trx).Sha256;
        supervisor["launch"]!["invocationSha256"] = fixture.Artifact(CoverageArtifactKind.CoverInvocation).Sha256;
        WriteFrameworkText(supervisorPath, supervisor.ToJsonString(FrameworkFixtureJson), fixture._started.AddMilliseconds(9900));
        CoverageOwnedProcessEvidence Seal(CoverageOwnedProcessEvidence pin) => fixture.SealFrameworkFixture(Path.Combine(fixture._input.ArtifactDirectory, pin.RelativePath));
        fixture.ChangeFrameworkEnvelope(envelope with { DriverInvocation = Seal(envelope.DriverInvocation), DriverStdout = Seal(envelope.DriverStdout),
            DriverStderr = Seal(envelope.DriverStderr), ExecutionRequest = Seal(envelope.ExecutionRequest), RunSettings = Seal(envelope.RunSettings),
            ControlContext = Seal(envelope.ControlContext), Supervisor = Seal(envelope.Supervisor), HostStart = Seal(envelope.HostStart), HostEnd = Seal(envelope.HostEnd) });
    }

    private static CoverageBaselineCatalogAdmission FreshBaselineCatalog(string key, IsolatedCoverageSessionTests fixture, string platformId)
    {
        var document = XDocument.Load(fixture.FrameworkArtifactPath(CoverageArtifactKind.Discovery));
        var ordinal = 0;
        foreach (var leaf in document.Descendants("test-case")) leaf.SetAttributeValue("id", ordinal++ == 0 ? platformId : platformId + "-" + ordinal);
        var path = Path.Combine(fixture._root, "fresh-baseline-discovery.xml");
        File.WriteAllText(path, document.ToString(SaveOptions.DisableFormatting), new UTF8Encoding(false));
        return new(key, path, Hash(path), fixture._expected.SourceDirectory, fixture._expected.BuildDirectory,
            fixture._expected.Identity.BuildId, fixture._expected.Binaries);
    }

    private static Dictionary<string, byte[]> CaptureBaselineBytes(CoverageBaselineAdmission admission) => admission.Catalogs
        .SelectMany(item => Directory.EnumerateFiles(Path.GetDirectoryName(item.SourceDirectory)!, "*", SearchOption.AllDirectories))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

    private static void AssertBaselineBytes(IReadOnlyDictionary<string, byte[]> before)
    {
        foreach (var pair in before) Assert.That(File.ReadAllBytes(pair.Key), Is.EqualTo(pair.Value), "Candidate admission must not rewrite evidence.");
    }
    [TestCase("duplicate", TestName = "baseline-candidate/reject-duplicate-group-owner")]
    [TestCase("isolated-profile", TestName = "baseline-candidate/reject-isolated-profile-substitution")]
    [TestCase("order", TestName = "baseline-candidate/reject-fresh-profile-order-drift")]
    public void ProfileOwnershipCannotBeDuplicatedSplitOrReordered(string fault)
    {
        var scenario = PrepareBaselineScenario();
        var group = PrepareBaselineGroup();
        var catalog = FreshBaselineCatalog("first", this, "fresh-1");
        var owners = new[] { scenario.Input.IsolatedOwners[1] };
        var input = scenario.Input with { IsolatedOwners = owners, OrderedGroups = [group] };
        if (fault == "duplicate") input = input with { OrderedGroups = [group, group] };
        if (fault == "isolated-profile") input = input with { IsolatedOwners = [scenario.Input.IsolatedOwners[0], owners[0]], OrderedGroups = [] };
        if (fault == "order")
        {
            var document = XDocument.Load(catalog.DiscoveryPath);
            var ordinals = document.Descendants("property").Where(item => (string?)item.Attribute("name") == "FishAudioProfileOrdinal").ToArray();
            ordinals[0].SetAttributeValue("value", "1");
            ordinals[1].SetAttributeValue("value", "0");
            File.WriteAllText(catalog.DiscoveryPath, document.ToString(SaveOptions.DisableFormatting), new UTF8Encoding(false));
            catalog = catalog with { DiscoverySha256 = Hash(catalog.DiscoveryPath) };
        }
        var admission = scenario.Admission with { Catalogs = [catalog, scenario.Admission.Catalogs[1]] };
        var rejected = Assert.Throws<CoverageBaselineCandidateException>(() => CoverageBaselineCandidate.ReadValidated(input, admission));
    }

    private CoverageBaselineOrderedGroup PrepareBaselineGroup(Action<XElement, int>? configureLeaf = null,
        EmptyProductCoverage? emptyProduct = null)
    {
        var original = _expected.Supervisor.FrameworkEnvelope!;
        const string profile = "native-input";
        var discovery = XDocument.Load(FrameworkArtifactPath(CoverageArtifactKind.Discovery));
        var first = discovery.Descendants("test-case").Single();
        var second = new XElement(first);
        second.SetAttributeValue("id", "0-2");
        second.SetAttributeValue("name", "Other");
        second.SetAttributeValue("fullname", "Example.Cases.Other");
        second.SetAttributeValue("methodname", "Other");
        second.Element("properties")!.Elements("property").Single().SetAttributeValue("value", "session/other");
        first.Parent!.Add(second);
        var index = 0;
        foreach (var leaf in new[] { first, second })
        {
            leaf.Element("properties")!.Add(BaselineProperty("FishAudioExecutionProfile", profile),
                BaselineProperty("FishAudioProfileOrdinal", index.ToString()), BaselineProperty("FishAudioExclusiveResource", "desktop"));
            configureLeaf?.Invoke(leaf, index);
            index++;
        }
        discovery.Descendants().Attributes("testcasecount").ToList().ForEach(item => item.Value = "2");
        SetFrameworkArtifact(CoverageArtifactKind.Discovery, Artifact(CoverageArtifactKind.Discovery).RelativePath,
            discovery.ToString(SaveOptions.DisableFormatting));
        var terminal = XDocument.Parse(Terminal);
        var result = new XElement(terminal.Descendants("test-case").Single());
        result.SetAttributeValue("id", "7-2");
        result.SetAttributeValue("name", "Other");
        result.SetAttributeValue("fullname", "Example.Cases.Other");
        result.SetAttributeValue("methodname", "Other");
        result.Descendants("property").Single().SetAttributeValue("value", "session/other");
        terminal.Root!.Add(result);
        terminal.Root.SetAttributeValue("total", "2");
        terminal.Root.SetAttributeValue("passed", "2");
        SetFrameworkArtifact(CoverageArtifactKind.NUnitXml, Artifact(CoverageArtifactKind.NUnitXml).RelativePath,
            terminal.ToString(SaveOptions.DisableFormatting));
        var trx = XDocument.Parse(Trx);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var trxResult = new XElement(trx.Descendants(ns + "UnitTestResult").Single());
        trxResult.SetAttributeValue("executionId", "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        trxResult.SetAttributeValue("testId", "22222222-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        trxResult.SetAttributeValue("testName", "Other");
        trx.Root!.Element(ns + "Results")!.Add(trxResult);
        var definition = new XElement(trx.Descendants(ns + "UnitTest").Single());
        definition.SetAttributeValue("id", "22222222-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        definition.SetAttributeValue("name", "Other");
        definition.Element(ns + "Execution")!.SetAttributeValue("id", "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        definition.Element(ns + "TestMethod")!.SetAttributeValue("name", "Other");
        trx.Root.Element(ns + "TestDefinitions")!.Add(definition);
        foreach (var name in new[] { "total", "executed", "passed" }) trx.Descendants(ns + "Counters").Single().SetAttributeValue(name, "2");
        SetFrameworkArtifact(CoverageArtifactKind.Trx, Artifact(CoverageArtifactKind.Trx).RelativePath, trx.ToString(SaveOptions.DisableFormatting));
        var requestPath = Path.Combine(_input.ArtifactDirectory, original.ExecutionRequest.RelativePath);
        var request = JsonSerializer.Deserialize<FrameworkRunRequest>(File.ReadAllText(requestPath), FrameworkFixtureJson)!;
        var cases = new[] { new FrameworkCaseBinding(CanonicalId, FrameworkFullName), new FrameworkCaseBinding("session/other", "Example.Cases.Other") };
        var environment = request.Environment!.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var name in new[] { "FISHAUDIO_COVERAGE_SESSION_ID", "FISHAUDIO_COVERAGE_RUN_ID", "FISHAUDIO_COVERAGE_CASE_ID",
            "FISHAUDIO_COVERAGE_SOURCE_TREE", "FISHAUDIO_COVERAGE_EVIDENCE_ROOT" }) environment[name] = "";
        var launch = request.ProfiledLaunch!;
        request = request with { Cases = cases, Environment = environment, ProfiledLaunch = null,
            OrderedProfiledLaunch = new(profile, Artifact(CoverageArtifactKind.Discovery).Sha256, launch) };
        WriteFrameworkJson(requestPath, request, _started.AddMilliseconds(500));
        var contextPath = Path.GetFullPath(Path.Combine(_input.ArtifactDirectory, original.ControlContext.RelativePath));
        var contextNode = JsonNode.Parse(File.ReadAllText(contextPath))!.AsObject();
        contextNode["cases"] = JsonSerializer.SerializeToNode(cases, FrameworkFixtureJson);
        WriteFrameworkText(contextPath, contextNode.ToJsonString(FrameworkFixtureJson), _started.AddSeconds(2));
        var inner = JsonNode.Parse(File.ReadAllText(FrameworkArtifactPath(CoverageArtifactKind.CoverInvocation)))!.AsObject();
        inner.Remove("canonicalCaseId");
        inner["coverageProfile"] = profile;
        var innerEnvironment = new Dictionary<string, string>(environment) { ["FISHAUDIO_FRAMEWORK_CONTROL_CONTEXT"] = contextPath };
        inner["environment"] = JsonSerializer.SerializeToNode(innerEnvironment, FrameworkFixtureJson);
        SetFrameworkArtifact(CoverageArtifactKind.CoverInvocation, Artifact(CoverageArtifactKind.CoverInvocation).RelativePath,
            inner.ToJsonString(FrameworkFixtureJson));
        var outerPath = Path.Combine(_input.ArtifactDirectory, original.DriverInvocation.RelativePath);
        var outer = JsonNode.Parse(File.ReadAllText(outerPath))!.AsObject();
        outer.Remove("canonicalCaseId");
        outer["coverageProfile"] = profile;
        WriteFrameworkText(outerPath, outer.ToJsonString(FrameworkFixtureJson), _started.AddMilliseconds(500));
        var exportXml = Path.Combine(_input.ArtifactDirectory, "framework-cover", "coverage.xml");
        WriteFrameworkText(exportXml, "<Root />", _started.AddSeconds(12));
        SetFrameworkArtifact(CoverageArtifactKind.ExportInvocation, Artifact(CoverageArtifactKind.ExportInvocation).RelativePath,
            JsonSerializer.Serialize(new { schemaVersion = 1, sessionRunId = _expected.Identity.SessionId, coverageProfile = profile,
                executable = request.DotnetExecutable, workingDirectory = request.WorkingDirectory,
                arguments = new[] { launch.ToolAssembly, "report", "--snapshot-source", launch.SnapshotPath,
                    "--json-report-output", FrameworkArtifactPath(CoverageArtifactKind.AggregateJson), "--xml-report-output", exportXml,
                    "--log-file", FrameworkArtifactPath(CoverageArtifactKind.ExportLog) } }, FrameworkFixtureJson));
        var export = _expected.Supervisor.Export;
        SetFrameworkArtifact(CoverageArtifactKind.ExportProcess, Artifact(CoverageArtifactKind.ExportProcess).RelativePath,
            JsonSerializer.Serialize(new { schemaVersion = 1, startedUtc = export.StartedUtc, finishedUtc = export.FinishedUtc,
                completed = true, exitCode = 0, elapsedMilliseconds = 1000.0, outputDrained = true,
                stdoutDrained = true, stderrDrained = true, ownedTreeCompleted = true, cleanupFailed = false,
                ownership = "windows-job-before-target-go", rootProcessId = 7200, metadataTimeoutMilliseconds = 120000 }, FrameworkFixtureJson));
        RefreshBaselineSeals(this);
        var supervisorPath = Path.Combine(_input.ArtifactDirectory, original.Supervisor.RelativePath);
        var supervisor = JsonNode.Parse(File.ReadAllText(supervisorPath))!.AsObject();
        supervisor["requestSha256"] = Hash(requestPath);
        supervisor["selectedIds"] = JsonSerializer.SerializeToNode(cases.Select(item => item.CanonicalId).ToArray());
        supervisor["policy"] = JsonSerializer.SerializeToNode(new { maximumConcurrency = 1, requireOrder = true });
        supervisor["state"]!["passedIds"] = supervisor["selectedIds"]!.DeepClone();
        supervisor["rawEvidence"]!["resultXmlSha256"] = Artifact(CoverageArtifactKind.NUnitXml).Sha256;
        supervisor["rawEvidence"]!["trxSha256"] = Artifact(CoverageArtifactKind.Trx).Sha256;
        supervisor["launch"] = JsonSerializer.SerializeToNode(new { kind = "dotcover-sdk-vstest-ordered-profile",
            sessionId = _expected.Identity.SessionId, profile, rawDiscoverySha256 = Artifact(CoverageArtifactKind.Discovery).Sha256,
            invocationSha256 = Artifact(CoverageArtifactKind.CoverInvocation).Sha256, toolSha256 = launch.ToolSha256,
            vstestConsoleSha256 = launch.VstestConsoleSha256 });
        supervisor["orderedProfileClock"] = JsonSerializer.SerializeToNode(new { startedTimestamp = 0, completedTimestamp = 800 });
        supervisor["finalization"] = JsonSerializer.SerializeToNode(new { frequency = 1000, cleanupAnchorTimestamp = 700 });
        var nonce = contextNode["nonce"]!.GetValue<string>();
        OrderedProfileCoverageControlEvent Host(string kind, long at, FrameworkCaseBinding? item = null, int ordinal = 0, long queued = 0, string? outcome = null) =>
            new(1, "nunit-host", nonce, request.RunId, request.SourceTree, FrameworkHostPid, kind, at, 1000,
                request.TestAssemblySha256, item?.CanonicalId, item?.FullName, ordinal, queued, outcome);
        OrderedProfileCoverageControlEvent Launcher(string kind, long at, int? exitCode = null) =>
            new(1, "execution-launcher", nonce, request.RunId, request.SourceTree, FrameworkLauncherPid, kind, at, 1000,
                TargetPid: FrameworkTargetPid, ExitCode: exitCode);
        var events = new List<OrderedProfileCoverageControlEvent> { Launcher("target-started", 100), Host("ready", 200) };
        for (var ordinal = 0; ordinal < cases.Length; ordinal++)
        {
            var item = cases[ordinal];
            var at = 300 + 100 * ordinal;
            events.Add(Host("case-start", at, item));
            events.Add(Host("queued", at + 10, item, 1, at + 10));
            events.Add(Host("entered", at + 20, item, 1, at + 10));
            events.Add(Host("completed", at + 30, item, 1, at + 10));
            events.Add(Host("case-finished", at + 40, item, outcome: "passed"));
        }
        events.Add(Host("cleanup-complete", 500));
        events.Add(Launcher("target-exited", 600, 0));
        supervisor["events"] = JsonSerializer.SerializeToNode(events, FrameworkFixtureJson);
        WriteFrameworkText(supervisorPath, supervisor.ToJsonString(FrameworkFixtureJson), _started.AddMilliseconds(9900));
        var identity = new CoverageProfileGroupIdentity(_expected.Identity.RunId, _expected.Identity.SessionId, profile,
            _expected.Identity.Source, _expected.Identity.BuildId);
        var envelope = new OrderedProfileCoverageEnvelope(1, SealFrameworkFixture(outerPath),
            SealFrameworkFixture(Path.Combine(_input.ArtifactDirectory, original.DriverStdout.RelativePath)),
            SealFrameworkFixture(Path.Combine(_input.ArtifactDirectory, original.DriverStderr.RelativePath)),
            SealFrameworkFixture(requestPath), SealFrameworkFixture(Path.Combine(_input.ArtifactDirectory, original.RunSettings.RelativePath)),
            SealFrameworkFixture(contextPath), SealFrameworkFixture(supervisorPath), SealFrameworkFixture(exportXml),
            original.DriverAssemblyRelativePath, original.DriverAssemblySha256);
        var receipt = new OrderedProfileCoverageSupervisorReceipt(1, identity, _expected.Supervisor.StartedUtc, _expected.Supervisor.SealedUtc,
            _expected.Supervisor.Cover, _expected.Supervisor.Export, _expected.Supervisor.Diagnostics, _input.Artifacts, envelope);
        var group = new CoverageBaselineOrderedGroup("first", new(1, identity, _input.ArtifactDirectory, _input.Artifacts),
            new(1, identity, _expected.SourceDirectory, _expected.BuildDirectory, _expected.Binaries,
                Artifact(CoverageArtifactKind.Discovery).Sha256, _expected.DotCoverVersion, _expected.ToolFile, _expected.ToolSha256, receipt)
                { EmptyProduct = emptyProduct });
        Assert.DoesNotThrow(() => OrderedProfileCoverageSession.ReadValidated(group.Input, group.Expectation), "original raw group fixture");
        return group;
    }
}
