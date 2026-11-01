using System.Text.Json;
using System.Text.Json.Serialization;
using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

public sealed partial class IsolatedCoverageSessionTests
{
    // Inert raw protocol fixtures with actual copied DLL/PDB pairs. No process,
    // profiler, prepared build execution or current baseline is claimed here.
    private static readonly JsonSerializerOptions ShardFixtureJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    [TestCaseSource(nameof(ShardPreparedFaultCases))]
    public void PreparedAdmissionCannotBorrowAnotherSourceBuildPolicyOrCatalog(string fault)
    {
        var fixture = PrepareOperationalShardFixture();
        AssertPreparedShardFixture(fixture);
        var expected = fixture.Expected;
        var reason = fault switch
        {
            "identity" => "preparation-identity",
            "build" => "preparation-required-pin",
            "policy" => "preparation-identity",
            "catalog" => "file-pin-mismatch",
            "unlisted-source" => "unlisted-source-input",
            _ => throw new InvalidOperationException("unknown-prepared-fault"),
        };
        switch (fault)
        {
            case "identity": expected = expected with { SourceDirectory = fixture.Document.PreparationDirectory }; break;
            case "build": File.WriteAllText(Path.Combine(fixture.Document.BuildDirectories[0], "new-output.dll"), "unrecorded build output"); break;
            case "policy":
                var changed = fixture.Document with { Admission = fixture.Document.Admission with
                { BuildAuthority = fixture.Document.Admission.BuildAuthority with { InstrumentationPolicyVersion = null } } };
                ShardFixtureWrite(expected.PreparedPath, changed);
                expected = expected with { PreparedSha256 = Hash(expected.PreparedPath) };
                break;
            case "catalog": File.AppendAllText(fixture.Document.Admission.Catalogs[0].DiscoveryPath, "<!-- changed after prepare -->"); break;
            case "unlisted-source":
                var resourceDirectory = Path.Combine(fixture.Expected.SourceDirectory, "assets", "bin");
                Directory.CreateDirectory(resourceDirectory);
                File.WriteAllText(Path.Combine(resourceDirectory, "unlisted-resource.json"), "bin-named resource directory is not a project output");
                break;
        }
        var error = Assert.Throws<FullCoverageShardException>(() => WithShardFixtureGuards(guards => FullCoverageShards.ReadPrepared(expected, guards)));
        Assert.That(error!.Code, Is.EqualTo("full-coverage-shards-" + reason));
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-shards/partial-admission-never-becomes-full")]
    public void SuccessfulSelectedOwnersRemainPartialEvenWithCompleteDependencies()
    {
        var fixture = PrepareOperationalShardFixture();
        AssertPreparedShardFixture(fixture);
        var before = File.ReadAllBytes(fixture.Expected.PreparedPath);
        var result = WithShardFixtureGuards(guards => FullCoverageShards.ReadShard(fixture.Inputs[0], fixture.Expected, guards));
        Assert.That(result.SelectedCanonicalIds, Is.EqualTo(new[] { CanonicalId }));
        Assert.That(result.SelectedOwnerIds, Has.Count.EqualTo(1));
        Assert.That(result.UnselectedOwnerIds, Has.Count.EqualTo(1));
        Assert.That(result.SelectedOwnersAdmitted && result.DependencyRecordsComplete, Is.True);
        Assert.That(result.CoverageUnionComplete || result.CompleteBaseline || result.PublicationAuthorized || result.TiaSelectionAuthorized, Is.False);
        Assert.That(File.ReadAllBytes(fixture.Expected.PreparedPath), Is.EqualTo(before));
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-shards/deterministic-empty-high-index-has-plan-reason")]
    public void LargeValidShardCountCanSelectNoOwnersWithoutClaimingExecution()
    {
        var fixture = PrepareOperationalShardFixture();
        AssertPreparedShardFixture(fixture);
        var directory = Path.Combine(_root, "empty-shard");
        Directory.CreateDirectory(directory);
        var environment = Path.Combine(directory, "runtime-environment.json");
        File.Copy(fixture.Document.RuntimeEnvironmentPath, environment);
        var input = new FullCoverageShardInput(1, 99_999, 100_000, directory, environment, [], []);
        var result = WithShardFixtureGuards(guards => FullCoverageShards.ReadShard(input, fixture.Expected, guards));
        Assert.That(result.SelectedOwnerIds, Is.Empty);
        Assert.That(result.SelectedCanonicalIds, Is.Empty);
        Assert.That(result.UnselectedOwnerIds, Has.Count.EqualTo(2));
        Assert.That(result.EmptySelectionReason, Is.EqualTo("deterministic-current-owner-plan-empty"));
        Assert.That(result.CoverageUnionComplete || result.CompleteBaseline || result.PublicationAuthorized, Is.False);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-shards/compose-reimports-exact-disjoint-current-union")]
    public void AllShardsReimportRawOwnersWithoutTrustingReceiptSuccessFlags()
    {
        var fixture = PrepareOperationalShardFixture();
        AssertPreparedShardFixture(fixture);
        var result = WithShardFixtureGuards(guards => FullCoverageShards.Compose(fixture.Composition, fixture.Expected, guards));
        Assert.That(result.CoverageUnionComplete && result.DependencyRecordsComplete, Is.True);
        Assert.That(result.IsolatedOwners.Select(item => item.Coverage.Identity.CanonicalId), Is.EquivalentTo(new[] { CanonicalId, "baseline/tool-case" }));
        Assert.That(result.IsolatedOwners, Has.Count.EqualTo(2));
        Assert.That(result.OrderedGroups, Is.Empty);
        Assert.That(result.CompleteBaseline || result.PublicationAuthorized || result.MainProvenanceVerified || result.TiaSelectionAuthorized, Is.False);
        // Complete pending bytes from a failed publication are not a final
        // manifest, even when all their independently reimported owners pass.
        var pending = Path.Combine(fixture.Manifests[0].ShardDirectory, "shard.pending.json");
        File.Copy(fixture.Composition.Shards[0].Path, pending);
        var unpublished = fixture.Composition with
        { Shards = [new(pending, Hash(pending)), fixture.Composition.Shards[1]] };
        var rejected = Assert.Throws<FullCoverageShardException>(() =>
            WithShardFixtureGuards(guards => FullCoverageShards.Compose(unpublished, fixture.Expected, guards)));
        Assert.That(rejected!.Code, Is.EqualTo("full-coverage-shards-final-shard-manifest-required"));
    }

    [TestCaseSource(nameof(ShardCompositionFaultCases))]
    public void CompositionRejectsIncompleteDuplicateFailedMixedOrChangedRawEvidence(string fault)
    {
        var fixture = PrepareOperationalShardFixture();
        AssertPreparedShardFixture(fixture);
        Assert.That(WithShardFixtureGuards(guards => FullCoverageShards.Compose(fixture.Composition, fixture.Expected, guards)).CoverageUnionComplete, Is.True);
        var input = fixture.Composition;
        var reason = "";
        switch (fault)
        {
            case "missing": input = input with { Shards = [input.Shards[0]] }; reason = "complete-shard-set-required"; break;
            case "duplicate": input = input with { Shards = [input.Shards[0], input.Shards[0]] }; reason = "shard-identity-or-duplicate"; break;
            case "failed":
                ShardFixtureWrite(fixture.Manifests[0].ValidationProcess.Path, ShardFixtureProcess(exitCode: 1));
                var failed = fixture.Manifests[0] with { ValidationProcess = ShardFixturePin(fixture.Manifests[0].ValidationProcess.Path) };
                ShardFixtureWrite(input.Shards[0].Path, failed);
                input = input with { Shards = [new(input.Shards[0].Path, Hash(input.Shards[0].Path)), input.Shards[1]] };
                reason = "shard-validation-process";
                break;
            case "mixed":
                ShardFixtureWrite(input.Shards[0].Path, fixture.Manifests[0] with { PreparedSha256 = new string('f', 64) });
                input = input with { Shards = [new(input.Shards[0].Path, Hash(input.Shards[0].Path)), input.Shards[1]] };
                reason = "shard-identity-or-duplicate";
                break;
            case "raw-drift":
                var owner = fixture.Owners[0];
                File.AppendAllText(Path.Combine(owner.Input.ArtifactDirectory,
                    owner.Input.Artifacts.Single(item => item.Kind == CoverageArtifactKind.AggregateJson).RelativePath), " ");
                break;
            default: throw new InvalidOperationException("unknown-shard-fault");
        }
        if (fault == "raw-drift")
            Assert.That(Assert.Throws<CoverageBaselineCandidateException>(() =>
                WithShardFixtureGuards(guards => FullCoverageShards.Compose(input, fixture.Expected, guards)))!.Code,
                Is.EqualTo("coverage-baseline-candidate-isolated-owner-rejected"));
        else
            Assert.That(Assert.Throws<FullCoverageShardException>(() => WithShardFixtureGuards(guards => FullCoverageShards.Compose(input, fixture.Expected, guards)))!.Code,
                Is.EqualTo("full-coverage-shards-" + reason));
    }

    private sealed record OperationalShardFixture(FullCoveragePreparedDocument Document, FullCoveragePreparedExpectation Expected,
        IReadOnlyList<CoverageBaselineIsolatedOwner> Owners, IReadOnlyList<FullCoverageShardInput> Inputs,
        IReadOnlyList<FullCoverageShardManifest> Manifests, FullCoverageShardCompositionInput Composition);

    private static IEnumerable<TestCaseData> ShardPreparedFaultCases()
    {
        foreach (var row in new[]
        {
            (Fault: "identity", Id: "coverage-shards/reject-prepared-source-identity-drift"),
            (Fault: "build", Id: "coverage-shards/reject-prepared-build-inventory-drift"),
            (Fault: "policy", Id: "coverage-shards/reject-prepared-historical-policy"),
            (Fault: "catalog", Id: "coverage-shards/reject-prepared-current-catalog-drift"),
            (Fault: "unlisted-source", Id: "coverage-shards/reject-extra-unlisted-source-file"),
        }) yield return new TestCaseData(row.Fault).SetName(row.Id).SetProperty("FishAudioCaseId", row.Id);
    }

    private static IEnumerable<TestCaseData> ShardCompositionFaultCases()
    {
        foreach (var row in new[]
        {
            (Fault: "missing", Id: "coverage-shards/reject-missing-shard"),
            (Fault: "duplicate", Id: "coverage-shards/reject-duplicate-shard"),
            (Fault: "failed", Id: "coverage-shards/reject-failed-owned-validation"),
            (Fault: "mixed", Id: "coverage-shards/reject-mixed-preparation"),
            (Fault: "raw-drift", Id: "coverage-shards/reject-owner-raw-drift-after-shard"),
        }) yield return new TestCaseData(row.Fault).SetName(row.Id).SetProperty("FishAudioCaseId", row.Id);
    }

    private OperationalShardFixture PrepareOperationalShardFixture()
    {
        var preparation = Path.Combine(_root, "prepared");
        Directory.CreateDirectory(preparation);
        var first = CreateOperationalShardOwner(0, secondCatalog: false);
        var second = CreateOperationalShardOwner(1, secondCatalog: true);
        var sessions = new[] { first, second };
        var owners = new[] { WithDependencyObservation(first, "plugin"), WithDependencyObservation(second, "testing-tool") };
        var catalogs = sessions.Select((session, index) =>
        {
            var catalog = FreshBaselineCatalog(index == 0 ? "plugin" : "testing-tool", session, "current-" + index);
            var path = Path.Combine(preparation, "catalog-" + index + ".xml");
            File.Copy(catalog.DiscoveryPath, path);
            return catalog with { DiscoveryPath = path, DiscoverySha256 = Hash(path) };
        }).ToArray();
        var sdk = Path.Combine(_root, "host", "sdk", "10.0.403");
        Directory.CreateDirectory(sdk);
        var dotnet = Path.Combine(_root, "host", "dotnet.exe");
        var shell = Path.Combine(_root, "host", "pwsh.exe");
        var dotcover = Path.Combine(_root, "host", "dotCover.dll");
        var vstest = Path.Combine(sdk, "vstest.console.dll");
        foreach (var path in new[] { dotnet, shell, dotcover, vstest })
            File.Copy(Path.Combine(first._expected.BuildDirectory, "fixture-sdk", "dotnet.exe"), path);
        var context = Path.Combine(preparation, "snapshot-context.json");
        var source = Path.Combine(_root, "source");
        File.WriteAllText(Path.Combine(source, "fixture.csproj"), "<Project />");
        var tracked = new[] { "README.md", "fixture.csproj", "tools/Invoke-TestProcess.ps1" };
        ShardFixtureWrite(context, new { schemaVersion = 1, root = source, tree = first._expected.Identity.Source.Tree,
            sourceCommit = first._expected.Identity.Source.Commit,
            files = tracked.Select(path => new { path, sha256 = Hash(Path.Combine(source, path)) }).ToArray() });
        var environment = Path.Combine(preparation, "runtime-environment.json");
        ShardFixtureWrite(environment, new { schemaVersion = 1, kind = "coverage-full-environment", complete = true,
            value = new { isWindows = true, runtime = "inert-protocol-fixture" } });
        var template = Path.Combine(preparation, "runner-template.json");
        ShardFixtureWrite(template, new { kind = "inert-independent-template-fixture" });
        var authority = new CoverageBaselineBuildAuthority("fixture-independent-preparation", new string('e', 64), "10.0.403", "Release", "net10.0-windows",
            Hash(dotcover), Hash(vstest)) { InstrumentationPolicyVersion = CoverageInstrumentationPolicy.CurrentVersion };
        var admission = new CoverageBaselineAdmission(1, first._expected.Identity.Source, authority, catalogs);
        Assert.That(CoverageBaselineCandidate.ReadValidated(new(1, owners, []), admission).DependencyRecordsComplete, Is.True,
            "independently admitted complete raw fixture before prepared/shard packaging");
        var buildDirectories = sessions.Select(session => session._expected.BuildDirectory).ToArray();
        var tool = Path.Combine(first._expected.BuildDirectory, "driver", "FishAudio.Testing.dll");
        var pins = new[] { context, dotnet, shell, dotcover, vstest, environment, template, tool }
            .Concat(tracked.Select(path => Path.Combine(source, path)))
            .Concat(catalogs.Select(item => item.DiscoveryPath))
            .Concat(buildDirectories.SelectMany(Directory.EnumerateFiles).Where(path => Path.GetExtension(path) is ".dll" or ".pdb"))
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(ShardFixturePin).ToArray();
        var document = new FullCoveragePreparedDocument(1, first._expected.Identity.RunId, source, preparation, context,
            admission.Source, admission, tool, environment, template, buildDirectories, pins);
        var preparedPath = Path.Combine(preparation, "prepared.json");
        ShardFixtureWrite(preparedPath, document);
        var expected = new FullCoveragePreparedExpectation(1, preparedPath, Hash(preparedPath), source, context, tool, dotnet, shell, dotcover);
        var inputs = new List<FullCoverageShardInput>();
        var manifests = new List<FullCoverageShardManifest>();
        var manifestPins = new List<FullCoverageShardManifestPin>();
        for (var index = 0; index < 2; index++)
        {
            var directory = Path.GetDirectoryName(sessions[index]._root)!;
            var currentEnvironment = Path.Combine(directory, "runtime-environment.json");
            File.Copy(environment, currentEnvironment);
            var ownerInput = Path.Combine(directory, "coverage-input.json");
            var ownerExpected = Path.Combine(directory, "admission-expectation.json");
            ShardFixtureWrite(ownerInput, owners[index].Input);
            ShardFixtureWrite(ownerExpected, owners[index].Expectation);
            var input = new FullCoverageShardInput(1, index, 2, directory, currentEnvironment,
                [new(owners[index].CatalogKey, ownerInput, ownerExpected)], []);
            var inputPath = Path.Combine(directory, "shard-input.json");
            var expectedPath = Path.Combine(directory, "prepared-expectation.json");
            var receipt = Path.Combine(directory, "receipt.json");
            var argv = Path.Combine(directory, "argv.json");
            var process = Path.Combine(directory, "process.json");
            var coordinator = Path.Combine(directory, "coordinator.json");
            ShardFixtureWrite(inputPath, input);
            ShardFixtureWrite(expectedPath, expected);
            // Deliberately false: the reader must reimport raw evidence rather
            // than use this serialized receipt as its admission authority.
            ShardFixtureWrite(receipt, new { complete = false, coverageUnionComplete = false });
            ShardFixtureWrite(argv, new { executable = dotnet, workingDirectory = source,
                arguments = new[] { tool, "coverage-full-shard-check", "--input", inputPath, "--expectation", expectedPath, "--output", receipt } });
            ShardFixtureWrite(process, ShardFixtureProcess(0));
            ShardFixtureWrite(coordinator, new { operation = "Shard", operationComplete = true, failureType = (string?)null, reason = (string?)null });
            var manifest = new FullCoverageShardManifest(1, preparedPath, expected.PreparedSha256, index, 2, directory,
                ShardFixturePin(inputPath), ShardFixturePin(expectedPath), ShardFixturePin(receipt), ShardFixturePin(argv),
                ShardFixturePin(process), ShardFixturePin(coordinator));
            var manifestPath = Path.Combine(directory, "shard.json");
            ShardFixtureWrite(manifestPath, manifest);
            inputs.Add(input);
            manifests.Add(manifest);
            manifestPins.Add(new(manifestPath, Hash(manifestPath)));
        }
        return new(document, expected, owners, inputs, manifests, new(1, 2, manifestPins));
    }

    private IsolatedCoverageSessionTests CreateOperationalShardOwner(int index, bool secondCatalog)
    {
        var fixture = new IsolatedCoverageSessionTests();
        fixture.CreateOwnedSession();
        var directory = Path.Combine(_root, "shard-" + index);
        Directory.CreateDirectory(directory);
        var moved = Path.Combine(directory, Path.GetFileName(fixture._root));
        Directory.Move(fixture._root, moved);
        fixture._root = moved;
        fixture._parent = directory;
        // All nested session files belong to this test's original _root.
        // Registering a second teardown would depend on NUnit teardown order.
        fixture._input = fixture._input with { ArtifactDirectory = Path.Combine(moved, "raw") };
        var build = Path.Combine(_root, "source", "bin", "owner-" + index);
        Directory.CreateDirectory(Path.GetDirectoryName(build)!);
        Directory.Move(Path.Combine(moved, "build"), build);
        fixture._expected = fixture._expected with { SourceDirectory = Path.Combine(_root, "source"),
            BuildDirectory = build, ToolFile = Path.Combine(build, "pinned-tool.fixture") };
        if (secondCatalog)
        {
            foreach (var pair in new[] { (Name: "product", Assembly: typeof(IsolatedCoverageSessionTests).Assembly.Location),
                (Name: "tests", Assembly: typeof(BinaryIdentity).Assembly.Location) })
            {
                File.Copy(pair.Assembly, Path.Combine(fixture._expected.BuildDirectory, pair.Name + ".dll"), true);
                File.Copy(Path.ChangeExtension(pair.Assembly, ".pdb"), Path.Combine(fixture._expected.BuildDirectory, pair.Name + ".pdb"), true);
            }
            fixture._expected = fixture._expected with { Binaries = fixture._expected.Binaries.Select(binding => binding with
            { ExpectedIdentity = BinaryIdentity.ReadValidated(Path.Combine(fixture._expected.BuildDirectory, binding.DllRelativePath),
                Path.Combine(fixture._expected.BuildDirectory, binding.PdbRelativePath)) }).ToArray() };
        }
        fixture.PrepareFrameworkSession();
        if (secondCatalog) RetagBaselineSession(fixture, "baseline/tool-case", "33333333333333333333333333333333");
        ConfigureBaselineDiscovery(fixture, secondCatalog ? "baseline/tool-case" : CanonicalId, resourceOnly: false);
        if (secondCatalog) fixture.SetFrameworkArtifact(CoverageArtifactKind.AggregateJson, fixture.Artifact(CoverageArtifactKind.AggregateJson).RelativePath,
            Aggregate.Replace("\"FishAudio.Testing\"", "\"FishAudio.Testing.Tests\"", StringComparison.Ordinal));
        RefreshBaselineSeals(fixture);
        fixture.EnableInstrumentationOwner(group: null);
        fixture.AssertInstrumentationOwner(group: null);
        return fixture;
    }

    private static void AssertPreparedShardFixture(OperationalShardFixture fixture)
    {
        var admitted = WithShardFixtureGuards(guards => FullCoverageShards.ReadPrepared(fixture.Expected, guards));
        Assert.That(admitted.WholePlan.CatalogCanonicalIds, Is.EquivalentTo(new[] { CanonicalId, "baseline/tool-case" }));
        Assert.That(admitted.BuildAndCatalogsValidated, Is.True);
        Assert.That(admitted.CoverageUnionComplete || admitted.CompleteBaseline || admitted.PublicationAuthorized || admitted.PortableOrCrossRoot, Is.False);
        foreach (var input in fixture.Inputs)
            Assert.That(WithShardFixtureGuards(guards => FullCoverageShards.ReadShard(input, fixture.Expected, guards)).SelectedOwnersAdmitted, Is.True);
    }

    private static object ShardFixtureProcess(int exitCode) => new
    {
        schemaVersion = 1, kind = "owned-process", complete = true, exitCode, stdoutDrained = true, stderrDrained = true,
        outputDrained = true, allChildrenExited = true, cleanupFailed = false, ownership = "windows-job-before-target-go",
        rootProcessId = 1234, phaseTimeoutMilliseconds = 0,
    };

    private static FullCoverageFilePin ShardFixturePin(string path) => new(path, Hash(path), new FileInfo(path).Length);
    private static void ShardFixtureWrite(string path, object value) => File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(value, ShardFixtureJson));
    private static T WithShardFixtureGuards<T>(Func<List<FileStream>, T> read)
    {
        var guards = new List<FileStream>();
        var completed = false;
        try { var result = read(guards); completed = true; return result; }
        finally
        {
            var releaseFailed = false;
            foreach (var guard in guards) { try { guard.Dispose(); } catch { releaseFailed = true; } }
            if (completed && releaseFailed) throw new InvalidOperationException("shard-fixture-input-release");
        }
    }
}
