using System.Text.Json;
using System.Text.Json.Nodes;
using FishAudio.Testing.Evidence;
using FishAudio.Testing.Execution;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

public sealed partial class IsolatedCoverageSessionTests
{
    // These are hand-declared protocol fixtures backed by actual copied pairs.
    // No profiler, driver, test host or SDK fixture executable is run here.
    [TestCaseSource(nameof(FrameworkToolProductAcceptedCases))]
    public void FrameworkToolMayBeItsIndependentlyBoundCoverageProduct(bool ordered)
    {
        var group = PrepareFrameworkToolProductOwner(ordered);
        var before = CaptureFrameworkFixtureDigests();
        AssertFrameworkToolProductOwner(group);
        AssertFrameworkFixtureDigests(before);
    }

    [TestCaseSource(nameof(FrameworkToolProductFaultCases))]
    public void ToolProductOverlapDoesNotAuthorizeAnotherBinaryRolePairOrIdentity(bool ordered, string fault)
    {
        var group = PrepareFrameworkToolProductOwner(ordered);
        // Every fault starts after admission of this exact raw owner. Mutations
        // below reach the binary/driver boundary before any downstream report.
        AssertFrameworkToolProductOwner(group);
        group = MutateFrameworkToolProductOwner(group, fault);
        var before = CaptureFrameworkFixtureDigests();
        var reason = fault switch
        {
            "non-tool-product" or "foreign-product-pdb" or "tests-driver-alias" or "driver-sha" =>
                ordered ? "ordered-profile-coverage-coverage-session-group-driver-binary" : "coverage-session-framework-driver-identity",
            "product-dll-sha" => ordered ? "ordered-profile-coverage-coverage-session-group-binary-build" : "coverage-session-binary-build-mismatch",
            "product-pdb-bytes" => ordered ? "ordered-profile-coverage-binary-evidence" : "coverage-session-binary-evidence-rejected",
            _ => throw new InvalidOperationException("unknown-tool-product-fixture-fault"),
        };
        if (group is null)
            Assert.That(Assert.Throws<IsolatedCoverageSessionException>(() => Read())!.Code, Is.EqualTo(reason));
        else
            Assert.That(Assert.Throws<OrderedProfileCoverageException>(() =>
                OrderedProfileCoverageSession.ReadValidated(group.Input, group.Expectation))!.Code, Is.EqualTo(reason));
        AssertFrameworkFixtureDigests(before);
    }

    private static IEnumerable<TestCaseData> FrameworkToolProductAcceptedCases()
    {
        foreach (var ordered in new[] { false, true })
        {
            var id = "coverage-driver/" + (ordered ? "ordered-" : "isolated-") + "tool-product-overlap";
            yield return new TestCaseData(ordered).SetName(id).SetProperty("FishAudioCaseId", id);
        }
    }

    private static IEnumerable<TestCaseData> FrameworkToolProductFaultCases()
    {
        foreach (var ordered in new[] { false, true })
        foreach (var fault in new[] { "non-tool-product", "foreign-product-pdb", "tests-driver-alias",
            "driver-sha", "product-dll-sha", "product-pdb-bytes" })
        {
            var id = "coverage-driver/" + (ordered ? "ordered-" : "isolated-") + "reject-" + fault;
            yield return new TestCaseData(ordered, fault).SetName(id).SetProperty("FishAudioCaseId", id);
        }
    }

    private CoverageBaselineOrderedGroup? PrepareFrameworkToolProductOwner(bool ordered)
    {
        var envelope = PrepareFrameworkSession();
        Assert.That(Read().Aggregate.TargetAssembly!.Name, Is.EqualTo("FishAudio.Testing"), "original separate-driver fixture");
        var product = _expected.Binaries.Single(item => item.Role == CoverageBinaryRole.Product);
        var driver = Path.Combine(_expected.BuildDirectory, envelope.DriverAssemblyRelativePath);
        var pdb = Path.ChangeExtension(driver, ".pdb");
        File.Copy(Path.Combine(_expected.BuildDirectory, product.PdbRelativePath), pdb);
        var overlap = product with
        {
            DllRelativePath = envelope.DriverAssemblyRelativePath,
            PdbRelativePath = Path.GetRelativePath(_expected.BuildDirectory, pdb).Replace('\\', '/'),
        };
        _expected = _expected with { Binaries = _expected.Binaries.Select(item => item.Role == CoverageBinaryRole.Product ? overlap : item).ToArray() };
        Assert.That(BinaryIdentity.ReadValidated(driver, pdb), Is.EqualTo(overlap.ExpectedIdentity));
        Assert.That(Read().Supervisor.FrameworkEnvelope!.DriverAssemblySha256, Is.EqualTo(overlap.ExpectedIdentity.DllSha256));
        if (!ordered) return null;
        // The group helper expands one admitted leaf into its exact two-member
        // profile. Reseal discovery and every affected envelope input first.
        ConfigureBaselineDiscovery(this, CanonicalId, resourceOnly: false);
        RefreshBaselineSeals(this);
        Assert.That(Read().TestResult.CanonicalId, Is.EqualTo(CanonicalId));
        return PrepareBaselineGroup();
    }

    private void AssertFrameworkToolProductOwner(CoverageBaselineOrderedGroup? group)
    {
        var product = _expected.Binaries.Single(item => item.Role == CoverageBinaryRole.Product);
        Assert.That(product.ExpectedIdentity.AssemblyName, Is.EqualTo("FishAudio.Testing"));
        if (group is null)
        {
            var admitted = Read();
            Assert.That(admitted.Binaries.Single(item => item.Role == CoverageBinaryRole.Product).Identity, Is.EqualTo(product.ExpectedIdentity));
            Assert.That(admitted.Aggregate.TargetAssembly!.CoveredStatements, Is.EqualTo(3));
            Assert.That(admitted.CompleteBaseline || admitted.SourceMappingsResolved, Is.False);
        }
        else
        {
            var admitted = OrderedProfileCoverageSession.ReadValidated(group.Input, group.Expectation);
            Assert.That(admitted.Binaries.Single(item => item.Role == CoverageBinaryRole.Product).Identity, Is.EqualTo(product.ExpectedIdentity));
            Assert.That(admitted.Members.Select(item => item.Case.CanonicalId), Is.EqualTo(new[] { CanonicalId, "session/other" }));
            Assert.That(admitted.Aggregate.TargetAssembly!.CoveredStatements, Is.EqualTo(3));
            Assert.That(admitted.IndividualCaseCoverage || admitted.CompleteBaseline || admitted.DependencyClosureProven, Is.False);
        }
    }

    private CoverageBaselineOrderedGroup? MutateFrameworkToolProductOwner(CoverageBaselineOrderedGroup? group, string fault)
    {
        var envelope = _expected.Supervisor.FrameworkEnvelope!;
        var driver = Path.Combine(_expected.BuildDirectory, envelope.DriverAssemblyRelativePath);
        var product = _expected.Binaries.Single(item => item.Role == CoverageBinaryRole.Product);
        var tests = _expected.Binaries.Single(item => item.Role == CoverageBinaryRole.Tests);
        var driverSha = envelope.DriverAssemblySha256;
        if (fault == "non-tool-product")
        {
            File.Copy(Path.Combine(_expected.BuildDirectory, tests.DllRelativePath), driver, overwrite: true);
            File.Copy(Path.Combine(_expected.BuildDirectory, tests.PdbRelativePath), Path.ChangeExtension(driver, ".pdb"), overwrite: true);
            product = product with { ExpectedIdentity = BinaryIdentity.ReadValidated(driver, Path.ChangeExtension(driver, ".pdb")) };
            driverSha = product.ExpectedIdentity.DllSha256;
            Assert.That(product.ExpectedIdentity.AssemblyName, Is.Not.EqualTo("FishAudio.Testing"));
        }
        else if (fault == "foreign-product-pdb")
        {
            var foreign = Path.Combine(_expected.BuildDirectory, "foreign", "FishAudio.Testing.pdb");
            Directory.CreateDirectory(Path.GetDirectoryName(foreign)!);
            File.Copy(Path.ChangeExtension(driver, ".pdb"), foreign);
            product = product with { PdbRelativePath = "foreign/FishAudio.Testing.pdb" };
            Assert.That(BinaryIdentity.ReadValidated(driver, foreign), Is.EqualTo(product.ExpectedIdentity), "a valid pair at another path is still not the bound driver pair");
        }
        else if (fault == "tests-driver-alias")
        {
            product = product with { DllRelativePath = "product.dll", PdbRelativePath = "product.pdb" };
            tests = tests with { DllRelativePath = envelope.DriverAssemblyRelativePath,
                PdbRelativePath = Path.GetRelativePath(_expected.BuildDirectory, Path.ChangeExtension(driver, ".pdb")).Replace('\\', '/'),
                ExpectedIdentity = product.ExpectedIdentity };
            Assert.That(BinaryIdentity.ReadValidated(driver, Path.ChangeExtension(driver, ".pdb")), Is.EqualTo(tests.ExpectedIdentity));
        }
        else if (fault == "driver-sha") driverSha = DifferentFrameworkHash(driverSha);
        else if (fault == "product-dll-sha")
            product = product with { ExpectedIdentity = product.ExpectedIdentity with { DllSha256 = DifferentFrameworkHash(product.ExpectedIdentity.DllSha256) } };
        else if (fault == "product-pdb-bytes")
        {
            File.Copy(Path.Combine(_expected.BuildDirectory, tests.PdbRelativePath), Path.ChangeExtension(driver, ".pdb"), overwrite: true);
            Assert.That(Hash(Path.ChangeExtension(driver, ".pdb")), Is.Not.EqualTo(product.ExpectedIdentity.PdbSha256));
        }
        else throw new InvalidOperationException("unknown-tool-product-fixture-fault");
        var binaries = new[] { product, tests };
        _expected = _expected with { Binaries = binaries };
        ChangeFrameworkEnvelope(envelope with { DriverAssemblySha256 = driverSha });
        if (group is not null)
            group = group with { Expectation = group.Expectation with { Binaries = binaries,
                Supervisor = group.Expectation.Supervisor with { FrameworkEnvelope = group.Expectation.Supervisor.FrameworkEnvelope with
                    { DriverAssemblySha256 = driverSha } } } };
        return fault == "tests-driver-alias" ? RebindFrameworkToolTestsOwner(group, tests, driver) : group;
    }

    private CoverageBaselineOrderedGroup? RebindFrameworkToolTestsOwner(CoverageBaselineOrderedGroup? group,
        CoverageBinaryBinding tests, string aliasedDriver)
    {
        var envelope = _expected.Supervisor.FrameworkEnvelope!;
        var testDll = Path.GetFullPath(Path.Combine(_expected.BuildDirectory, tests.DllRelativePath));
        var testHash = tests.ExpectedIdentity.DllSha256;
        var requestPath = Path.Combine(_input.ArtifactDirectory, envelope.ExecutionRequest.RelativePath);
        var request = JsonSerializer.Deserialize<FrameworkRunRequest>(File.ReadAllText(requestPath), FrameworkFixtureJson)!;
        request = request with { TestAssembly = testDll, TestAssemblySha256 = testHash };
        WriteFrameworkJson(requestPath, request, _started.AddMilliseconds(500));

        var contextPath = Path.Combine(_input.ArtifactDirectory, envelope.ControlContext.RelativePath);
        var context = JsonNode.Parse(File.ReadAllText(contextPath))!.AsObject();
        context["testAssembly"] = testDll;
        context["testAssemblySha256"] = testHash;
        WriteFrameworkText(contextPath, context.ToJsonString(FrameworkFixtureJson), _started.AddSeconds(2));

        var inner = JsonNode.Parse(File.ReadAllText(FrameworkArtifactPath(CoverageArtifactKind.CoverInvocation)))!.AsObject();
        var launch = request.ProfiledLaunch ?? request.OrderedProfiledLaunch!.Collection;
        string[] arguments = [launch.ToolAssembly, "cover", "--target-executable", request.DotnetExecutable,
            "--snapshot-output", launch.SnapshotPath, "--log-file", launch.LogPath, "--", "exec",
            launch.VstestConsoleAssembly, testDll, "--Settings:" + request.RunSettings,
            "--logger:trx;LogFileName=" + Path.GetFileName(request.TrxPath),
            "--ResultsDirectory:" + Path.GetDirectoryName(request.TrxPath)];
        inner["arguments"] = JsonSerializer.SerializeToNode(arguments, FrameworkFixtureJson);
        SetFrameworkArtifact(CoverageArtifactKind.CoverInvocation, Artifact(CoverageArtifactKind.CoverInvocation).RelativePath,
            inner.ToJsonString(FrameworkFixtureJson));

        foreach (var pin in new[] { envelope.HostStart, envelope.HostEnd })
        {
            var path = Path.Combine(_input.ArtifactDirectory, pin.RelativePath);
            var host = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            host["assemblyPath"] = testDll;
            host["assemblySha256"] = testHash;
            WriteFrameworkText(path, host.ToJsonString(FrameworkFixtureJson), pin.LastWriteUtc);
        }
        var supervisorPath = Path.Combine(_input.ArtifactDirectory, envelope.Supervisor.RelativePath);
        var supervisor = JsonNode.Parse(File.ReadAllText(supervisorPath))!.AsObject();
        supervisor["testAssemblySha256"] = testHash;
        if (supervisor.ContainsKey("requestSha256")) supervisor["requestSha256"] = Hash(requestPath);
        foreach (var item in supervisor["events"]!.AsArray())
        {
            var record = item!.AsObject();
            if (record["role"]!.GetValue<string>() == "nunit-host") record["assemblySha256"] = testHash;
        }
        WriteFrameworkText(supervisorPath, supervisor.ToJsonString(FrameworkFixtureJson), _started.AddMilliseconds(9900));
        RefreshBaselineSeals(this);
        if (group is not null)
        {
            var sealedEnvelope = _expected.Supervisor.FrameworkEnvelope!;
            group = group with { Input = group.Input with { Artifacts = _input.Artifacts },
                Expectation = group.Expectation with { Binaries = _expected.Binaries, DiscoverySha256 = _expected.DiscoverySha256,
                    Supervisor = group.Expectation.Supervisor with { Artifacts = _input.Artifacts,
                        Diagnostics = _expected.Supervisor.Diagnostics,
                        FrameworkEnvelope = group.Expectation.Supervisor.FrameworkEnvelope with
                        {
                            DriverInvocation = sealedEnvelope.DriverInvocation, ExecutionRequest = sealedEnvelope.ExecutionRequest,
                            ControlContext = sealedEnvelope.ControlContext, Supervisor = sealedEnvelope.Supervisor,
                        } } } };
        }

        // Admit every rewritten raw binding with a separate driver first. Only
        // restoring the driver to the Tests path may cause the final rejection.
        var separateDriver = Path.Combine(_expected.BuildDirectory, "independent-driver", "FishAudio.Testing.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(separateDriver)!);
        File.Copy(aliasedDriver, separateDriver);
        group = RebindFrameworkToolFixtureDriver(group, separateDriver);
        AssertFrameworkToolProductOwner(group);
        return RebindFrameworkToolFixtureDriver(group, aliasedDriver);
    }

    private CoverageBaselineOrderedGroup? RebindFrameworkToolFixtureDriver(CoverageBaselineOrderedGroup? group, string driver)
    {
        driver = Path.GetFullPath(driver);
        var envelope = _expected.Supervisor.FrameworkEnvelope!;
        var requestPath = Path.Combine(_input.ArtifactDirectory, envelope.ExecutionRequest.RelativePath);
        var outerPath = Path.Combine(_input.ArtifactDirectory, envelope.DriverInvocation.RelativePath);
        var outer = JsonNode.Parse(File.ReadAllText(outerPath))!.AsObject();
        outer["arguments"] = JsonSerializer.SerializeToNode(new[] { driver, "framework-run", "--request", requestPath }, FrameworkFixtureJson);
        WriteFrameworkText(outerPath, outer.ToJsonString(FrameworkFixtureJson), _started.AddMilliseconds(500));
        var relative = Path.GetRelativePath(_expected.BuildDirectory, driver).Replace('\\', '/');
        var driverHash = Hash(driver);
        var outerPin = SealFrameworkFixture(outerPath);
        ChangeFrameworkEnvelope(envelope with { DriverInvocation = outerPin,
            DriverAssemblyRelativePath = relative, DriverAssemblySha256 = driverHash });
        if (group is null) return null;
        return group with { Expectation = group.Expectation with
        {
            Supervisor = group.Expectation.Supervisor with
            {
                FrameworkEnvelope = group.Expectation.Supervisor.FrameworkEnvelope with
                { DriverInvocation = outerPin, DriverAssemblyRelativePath = relative, DriverAssemblySha256 = driverHash },
            },
        } };
    }
}
