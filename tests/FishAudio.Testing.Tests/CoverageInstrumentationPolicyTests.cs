using System.Text.Json;
using System.Text.Json.Nodes;
using FishAudio.Testing.Evidence;
using FishAudio.Testing.Execution;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

public sealed partial class IsolatedCoverageSessionTests
{
    // Hand-declared raw envelopes and actual copied PE pairs. No profiler,
    // launcher, test host or SDK fixture executable is run by these controls.
    [TestCaseSource(nameof(InstrumentationAcceptedCases))]
    public void FixedInstrumentationPolicyAgreesWithProducerAndEachRawOwner(bool ordered)
    {
        var group = PrepareInstrumentationOwner(ordered);
        var request = ReadInstrumentationRequest();
        var arguments = FrameworkExecutionDriver.TargetArguments(request);
        var at = Array.IndexOf(arguments, "--exclude-assemblies");
        Assert.That(arguments.Count(item => item == "--exclude-assemblies"), Is.EqualTo(1));
        Assert.That(arguments[at + 1], Is.EqualTo("Microsoft.TestPlatform.CommunicationUtilities,Microsoft.TestPlatform.CoreUtilities,Microsoft.TestPlatform.CrossPlatEngine,Microsoft.TestPlatform.PlatformAbstractions,Microsoft.VisualStudio.TestPlatform.*,NUnit3.TestAdapter,testhost"));
        Assert.That(at, Is.LessThan(Array.IndexOf(arguments, "--")));
        PreflightInstrumentation(request);
        var before = CaptureFrameworkFixtureDigests();
        AssertInstrumentationOwner(group);
        AssertFrameworkFixtureDigests(before);
        var unprofiled = request with { ProfiledLaunch = null, OrderedProfiledLaunch = null };
        Assert.That(FrameworkExecutionDriver.TargetArguments(unprofiled)[0], Is.EqualTo("vstest"));
        Assert.That(FrameworkExecutionDriver.TargetArguments(unprofiled), Does.Not.Contain("--exclude-assemblies"));
    }

    [TestCaseSource(nameof(InstrumentationRawFaultCases))]
    public void FixedPolicyCannotBeChangedOrDetachedFromItsIndependentAdmission(bool ordered, string fault, string reason)
    {
        var group = PrepareInstrumentationOwner(ordered);
        AssertInstrumentationOwner(group);
        var envelope = _expected.Supervisor.FrameworkEnvelope!;
        var requestPath = Path.Combine(_input.ArtifactDirectory, envelope.ExecutionRequest.RelativePath);
        var request = JsonNode.Parse(File.ReadAllText(requestPath))!.AsObject();
        var collection = ordered ? request["orderedProfiledLaunch"]!["collection"]! : request["profiledLaunch"]!;
        var inner = JsonNode.Parse(File.ReadAllText(FrameworkArtifactPath(CoverageArtifactKind.CoverInvocation)))!.AsObject();
        var arguments = inner["arguments"]!.Deserialize<List<string>>(FrameworkFixtureJson)!;
        var at = arguments.IndexOf("--exclude-assemblies");
        Assert.That(at, Is.GreaterThanOrEqualTo(0));
        if (fault == "csv") arguments[at + 1] += ",*.Tests";
        else if (fault == "missing-option") arguments.RemoveRange(at, 2);
        else if (fault == "duplicate-option") arguments.InsertRange(at, ["--exclude-assemblies", CoverageInstrumentationPolicy.ExcludedAssemblies]);
        else if (fault == "extra-option") arguments.InsertRange(arguments.IndexOf("--"), ["--exclude-processes", "testhost"]);
        else if (fault == "unknown-policy") collection["instrumentationPolicyVersion"] = "future-policy";
        else if (fault == "missing-policy") collection.AsObject().Remove("instrumentationPolicyVersion");
        else if (fault == "foreign-product")
        {
            collection["productAssembly"] = Path.GetFullPath(Path.Combine(_expected.BuildDirectory, "tests.dll"));
            collection["productAssemblySha256"] = _expected.Binaries.Single(item => item.Role == CoverageBinaryRole.Tests).ExpectedIdentity.DllSha256;
        }
        else if (fault != "recorded-policy") throw new InvalidOperationException("unknown-instrumentation-fault");
        WriteFrameworkText(requestPath, request.ToJsonString(FrameworkFixtureJson), _started.AddMilliseconds(500));
        inner["arguments"] = JsonSerializer.SerializeToNode(arguments, FrameworkFixtureJson);
        SetFrameworkArtifact(CoverageArtifactKind.CoverInvocation, Artifact(CoverageArtifactKind.CoverInvocation).RelativePath,
            inner.ToJsonString(FrameworkFixtureJson));
        if (fault == "recorded-policy")
        {
            var path = Path.Combine(_input.ArtifactDirectory, envelope.Supervisor.RelativePath);
            var supervisor = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            supervisor["launch"]!["instrumentationPolicyVersion"] = "future-policy";
            WriteFrameworkText(path, supervisor.ToJsonString(FrameworkFixtureJson), _started.AddMilliseconds(9900));
        }
        group = SealInstrumentationOwner(group);
        var before = CaptureFrameworkFixtureDigests();
        if (group is null)
            Assert.That(Assert.Throws<IsolatedCoverageSessionException>(() => Read())!.Code, Is.EqualTo("coverage-session-" + reason));
        else
            Assert.That(Assert.Throws<OrderedProfileCoverageException>(() =>
                OrderedProfileCoverageSession.ReadValidated(group.Input, group.Expectation))!.Code, Is.EqualTo(reason));
        AssertFrameworkFixtureDigests(before);
    }

    [TestCaseSource(nameof(InstrumentationPreflightCases))]
    public void InstrumentationPreflightUsesActualProtectedPeNamesAndPinnedBytes(string fault, string reason)
    {
        PrepareInstrumentationOwner(ordered: false);
        var request = ReadInstrumentationRequest();
        PreflightInstrumentation(request);
        var launch = request.ProfiledLaunch!;
        if (fault is "product-collision" or "tests-collision")
        {
            var source = Path.Combine(Path.GetDirectoryName(typeof(IsolatedCoverageSessionTests).Assembly.Location)!,
                "Microsoft.VisualStudio.TestPlatform.ObjectModel.dll");
            Assert.That(File.Exists(source), Is.True, "actual Test SDK dependency must be present; no fabricated PE identity");
            var copied = Path.Combine(_expected.BuildDirectory, "unrelated-input-name.dll");
            File.Copy(source, copied);
            if (fault == "product-collision") launch = launch with { ProductAssembly = copied, ProductAssemblySha256 = Hash(copied) };
            else request = request with { TestAssembly = copied, TestAssemblySha256 = Hash(copied) };
        }
        else if (fault == "product-sha") launch = launch with { ProductAssemblySha256 = DifferentFrameworkHash(launch.ProductAssemblySha256!) };
        else if (fault == "unknown-policy") launch = launch with { InstrumentationPolicyVersion = "framework-infrastructure-exclusions-v1" };
        else if (fault == "v0-fields") launch = launch with { InstrumentationPolicyVersion = null };
        else throw new InvalidOperationException("unknown-instrumentation-preflight-fault");
        request = request with { ProfiledLaunch = launch };
        var before = CaptureFrameworkFixtureDigests();
        Assert.That(Assert.Throws<InvalidDataException>(() => PreflightInstrumentation(request))!.Message, Is.EqualTo(reason));
        AssertFrameworkFixtureDigests(before);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-policy/legacy-no-envelope-cannot-hide-filters-as-v0")]
    public void HistoricalReceiptCannotAcquireFiltersWithoutAnIndependentPolicy()
    {
        Assert.That(Read().InstrumentationPolicyVersion, Is.Null);
        Assert.That(_expected.Supervisor.FrameworkEnvelope, Is.Null);
        var inner = JsonNode.Parse(File.ReadAllText(FrameworkArtifactPath(CoverageArtifactKind.CoverInvocation)))!.AsObject();
        var arguments = inner["arguments"]!.Deserialize<List<string>>(FrameworkFixtureJson)!;
        arguments.InsertRange(arguments.IndexOf("--"), ["--exclude-assemblies", CoverageInstrumentationPolicy.ExcludedAssemblies]);
        inner["arguments"] = JsonSerializer.SerializeToNode(arguments, FrameworkFixtureJson);
        SetFrameworkArtifact(CoverageArtifactKind.CoverInvocation, Artifact(CoverageArtifactKind.CoverInvocation).RelativePath,
            inner.ToJsonString(FrameworkFixtureJson));
        RefreshBaselineSeals(this);
        var before = CaptureFrameworkFixtureDigests();
        Assert.That(Assert.Throws<IsolatedCoverageSessionException>(() => Read())!.Code,
            Is.EqualTo("coverage-session-instrumentation-policy-v0-argv"));
        AssertFrameworkFixtureDigests(before);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-policy/independent-authority-admits-two-v2-owners")]
    public void CompleteCompositionRetainsTheIndependentlyExpectedCurrentPolicy()
    {
        var scenario = PrepareBaselineScenario();
        Assert.That(CoverageBaselineCandidate.ReadValidated(scenario.Input, scenario.Admission).CoverageUnionComplete, Is.True);
        var second = _baselineAdditionalSessions.Single();
        EnableInstrumentationOwner(group: null);
        second.EnableInstrumentationOwner(group: null);
        AssertInstrumentationOwner(group: null);
        second.AssertInstrumentationOwner(group: null);
        var owners = new[] { scenario.Input.IsolatedOwners[0] with { Input = _input, Expectation = _expected },
            scenario.Input.IsolatedOwners[1] with { Input = second._input, Expectation = second._expected } };
        var authority = scenario.Admission.BuildAuthority with { InstrumentationPolicyVersion = CoverageInstrumentationPolicy.CurrentVersion };
        var result = CoverageBaselineCandidate.ReadValidated(scenario.Input with { IsolatedOwners = owners },
            scenario.Admission with { BuildAuthority = authority });
        Assert.That(result.IsolatedOwners.Select(item => item.Coverage.InstrumentationPolicyVersion),
            Is.All.EqualTo(CoverageInstrumentationPolicy.CurrentVersion));
        Assert.That(result.CompleteBaseline || result.TiaSelectionAuthorized || result.DependencyClosureProven, Is.False);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-policy/independent-authority-rejects-mixed-v0-v2")]
    public void CompleteCompositionCannotMixHistoricalAndFilteredOwners()
    {
        var scenario = PrepareBaselineScenario();
        Assert.That(CoverageBaselineCandidate.ReadValidated(scenario.Input, scenario.Admission).CoverageUnionComplete, Is.True);
        EnableInstrumentationOwner(group: null);
        AssertInstrumentationOwner(group: null);
        var first = scenario.Input.IsolatedOwners[0] with { Input = _input, Expectation = _expected };
        var input = scenario.Input with { IsolatedOwners = [first, scenario.Input.IsolatedOwners[1]] };
        var authority = scenario.Admission.BuildAuthority with { InstrumentationPolicyVersion = CoverageInstrumentationPolicy.CurrentVersion };
        var error = Assert.Throws<CoverageBaselineCandidateException>(() =>
            CoverageBaselineCandidate.ReadValidated(input, scenario.Admission with { BuildAuthority = authority }));
        Assert.That(error!.Code, Is.EqualTo("coverage-baseline-candidate-owner-instrumentation-policy"));
    }

    private static IEnumerable<TestCaseData> InstrumentationAcceptedCases()
    {
        foreach (var ordered in new[] { false, true })
        {
            var id = "coverage-policy/" + (ordered ? "ordered" : "isolated") + "-producer-raw-owner-agreement";
            yield return new TestCaseData(ordered).SetName(id).SetProperty("FishAudioCaseId", id);
        }
    }

    private static IEnumerable<TestCaseData> InstrumentationRawFaultCases()
    {
        foreach (var row in new[]
        {
            (Ordered: false, Fault: "csv", Reason: "framework-profiler-invocation-mismatch"),
            (Ordered: false, Fault: "missing-option", Reason: "framework-profiler-invocation-mismatch"),
            (Ordered: false, Fault: "duplicate-option", Reason: "framework-profiler-invocation-mismatch"),
            (Ordered: false, Fault: "unknown-policy", Reason: "framework-instrumentation-policy"),
            (Ordered: true, Fault: "extra-option", Reason: "ordered-profile-coverage-coverage-session-group-cover-invocation"),
            (Ordered: true, Fault: "missing-policy", Reason: "ordered-profile-coverage-coverage-session-framework-instrumentation-policy"),
            (Ordered: true, Fault: "recorded-policy", Reason: "ordered-profile-coverage-launch"),
            (Ordered: true, Fault: "foreign-product", Reason: "ordered-profile-coverage-coverage-session-framework-instrumentation-policy-product"),
        })
        {
            var id = "coverage-policy/" + (row.Ordered ? "ordered" : "isolated") + "-reject-" + row.Fault;
            yield return new TestCaseData(row.Ordered, row.Fault, row.Reason).SetName(id).SetProperty("FishAudioCaseId", id);
        }
    }

    private static IEnumerable<TestCaseData> InstrumentationPreflightCases()
    {
        foreach (var row in new[]
        {
            (Fault: "product-collision", Reason: "framework-instrumentation-policy-collision"),
            (Fault: "tests-collision", Reason: "framework-instrumentation-policy-collision"),
            (Fault: "product-sha", Reason: "framework-instrumentation-policy-input-mismatch"),
            (Fault: "unknown-policy", Reason: "framework-instrumentation-policy-unknown"),
            (Fault: "v0-fields", Reason: "framework-instrumentation-policy-v0-fields"),
        })
        {
            var id = "coverage-policy/preflight-reject-" + row.Fault;
            yield return new TestCaseData(row.Fault, row.Reason).SetName(id).SetProperty("FishAudioCaseId", id);
        }
    }

    private CoverageBaselineOrderedGroup? PrepareInstrumentationOwner(bool ordered)
    {
        PrepareFrameworkSession();
        Assert.That(Read().InstrumentationPolicyVersion, Is.Null);
        Assert.That(FrameworkExecutionDriver.TargetArguments(ReadInstrumentationRequest()), Does.Not.Contain("--exclude-assemblies"));
        CoverageBaselineOrderedGroup? group = null;
        if (ordered)
        {
            ConfigureBaselineDiscovery(this, CanonicalId, resourceOnly: false);
            RefreshBaselineSeals(this);
            group = PrepareBaselineGroup();
        }
        return EnableInstrumentationOwner(group);
    }

    private CoverageBaselineOrderedGroup? EnableInstrumentationOwner(CoverageBaselineOrderedGroup? group)
    {
        var envelope = _expected.Supervisor.FrameworkEnvelope!;
        var request = ReadInstrumentationRequest();
        var product = _expected.Binaries.Single(item => item.Role == CoverageBinaryRole.Product);
        var launch = (request.ProfiledLaunch ?? request.OrderedProfiledLaunch!.Collection) with
        {
            InstrumentationPolicyVersion = CoverageInstrumentationPolicy.CurrentVersion,
            ProductAssembly = Path.GetFullPath(Path.Combine(_expected.BuildDirectory, product.DllRelativePath)),
            ProductAssemblySha256 = product.ExpectedIdentity.DllSha256,
        };
        request = request.ProfiledLaunch is not null ? request with { ProfiledLaunch = launch } :
            request with { OrderedProfiledLaunch = request.OrderedProfiledLaunch! with { Collection = launch } };
        WriteFrameworkJson(Path.Combine(_input.ArtifactDirectory, envelope.ExecutionRequest.RelativePath), request, _started.AddMilliseconds(500));
        var inner = JsonNode.Parse(File.ReadAllText(FrameworkArtifactPath(CoverageArtifactKind.CoverInvocation)))!.AsObject();
        inner["arguments"] = JsonSerializer.SerializeToNode(FrameworkExecutionDriver.TargetArguments(request), FrameworkFixtureJson);
        SetFrameworkArtifact(CoverageArtifactKind.CoverInvocation, Artifact(CoverageArtifactKind.CoverInvocation).RelativePath,
            inner.ToJsonString(FrameworkFixtureJson));
        var supervisorPath = Path.Combine(_input.ArtifactDirectory, envelope.Supervisor.RelativePath);
        var supervisor = JsonNode.Parse(File.ReadAllText(supervisorPath))!.AsObject();
        supervisor["launch"]!["instrumentationPolicyVersion"] = CoverageInstrumentationPolicy.CurrentVersion;
        WriteFrameworkText(supervisorPath, supervisor.ToJsonString(FrameworkFixtureJson), _started.AddMilliseconds(9900));
        _expected = _expected with { InstrumentationPolicyVersion = CoverageInstrumentationPolicy.CurrentVersion };
        if (group is not null) group = group with { Expectation = group.Expectation with { InstrumentationPolicyVersion = CoverageInstrumentationPolicy.CurrentVersion } };
        return SealInstrumentationOwner(group);
    }

    private FrameworkRunRequest ReadInstrumentationRequest() => JsonSerializer.Deserialize<FrameworkRunRequest>(
        File.ReadAllText(Path.Combine(_input.ArtifactDirectory, _expected.Supervisor.FrameworkEnvelope!.ExecutionRequest.RelativePath)), FrameworkFixtureJson)!;

    private CoverageBaselineOrderedGroup? SealInstrumentationOwner(CoverageBaselineOrderedGroup? group)
    {
        var envelope = _expected.Supervisor.FrameworkEnvelope!;
        var supervisorPath = Path.Combine(_input.ArtifactDirectory, envelope.Supervisor.RelativePath);
        var supervisor = JsonNode.Parse(File.ReadAllText(supervisorPath))!.AsObject();
        if (supervisor.ContainsKey("requestSha256")) supervisor["requestSha256"] = Hash(Path.Combine(_input.ArtifactDirectory, envelope.ExecutionRequest.RelativePath));
        WriteFrameworkText(supervisorPath, supervisor.ToJsonString(FrameworkFixtureJson), _started.AddMilliseconds(9900));
        RefreshBaselineSeals(this);
        if (group is null) return null;
        var updated = _expected.Supervisor.FrameworkEnvelope!;
        return group with { Input = group.Input with { Artifacts = _input.Artifacts }, Expectation = group.Expectation with
        {
            Supervisor = group.Expectation.Supervisor with { Artifacts = _input.Artifacts, Diagnostics = _expected.Supervisor.Diagnostics,
                FrameworkEnvelope = group.Expectation.Supervisor.FrameworkEnvelope with
                { ExecutionRequest = updated.ExecutionRequest, Supervisor = updated.Supervisor } },
        } };
    }

    private void AssertInstrumentationOwner(CoverageBaselineOrderedGroup? group)
    {
        if (group is null) Assert.That(Read().InstrumentationPolicyVersion, Is.EqualTo(CoverageInstrumentationPolicy.CurrentVersion));
        else
        {
            var admitted = OrderedProfileCoverageSession.ReadValidated(group.Input, group.Expectation);
            Assert.That(admitted.InstrumentationPolicyVersion, Is.EqualTo(CoverageInstrumentationPolicy.CurrentVersion));
            Assert.That(admitted.Members, Has.Count.EqualTo(2));
            Assert.That(admitted.IndividualCaseCoverage || admitted.CompleteBaseline || admitted.DependencyClosureProven, Is.False);
        }
    }

    private static void PreflightInstrumentation(FrameworkRunRequest request)
    {
        var guards = new List<FileStream>();
        var succeeded = false;
        try
        {
            FrameworkExecutionDriver.ValidateInstrumentationPolicy(request, request.ProfiledLaunch ?? request.OrderedProfiledLaunch!.Collection, guards);
            succeeded = true;
        }
        finally
        {
            var releaseFailed = false;
            foreach (var guard in guards) { try { guard.Dispose(); } catch { releaseFailed = true; } }
            if (releaseFailed && succeeded) throw new InvalidOperationException("instrumentation-fixture-input-release");
        }
    }
}
