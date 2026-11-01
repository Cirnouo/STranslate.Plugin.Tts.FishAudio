using FishAudio.Testing.Execution;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

[TestFixture]
public sealed class RunnerMetadataCoverageLayoutTests
{
    // Typed layout fixtures only: no profiler, build or successful raw session
    // is fabricated. ReadActual still requires real PE/PDB/source/package and
    // authenticated request/control inputs after this structural boundary.
    [TestCase(false, TestName = "runner-metadata-coverage-layout/isolated-actual-profiled-shape")]
    [TestCase(true, TestName = "runner-metadata-coverage-layout/ordered-actual-profiled-shape")]
    public void ProfiledShapeUsesItsOwnSessionAndAnIndependentOtherCatalog(bool ordered)
    {
        var (context, request, path) = Inputs(ordered);
        var result = RunnerMetadataInputs.ReadLayout(context, request, path);
        Assert.That(result.Coverage, Is.True);
        Assert.That(result.SessionDirectory, Is.EqualTo(context.SessionDirectory));
        Assert.That(result.CatalogSessionDirectory, Is.EqualTo(context.CoverageCatalogSessionDirectory));
        Assert.That(result.SessionDirectory, Is.Not.EqualTo(result.CatalogSessionDirectory));
    }

    [Test]
    [Property("FishAudioCaseId", "runner-metadata-coverage-layout/normal-shape-retains-original-session")]
    public void NormalShapeRetainsItsExistingFourLevelSession()
    {
        var (context, request, _) = Inputs(false);
        context = context with { CoverageCatalogSessionDirectory = null };
        var output = Path.Combine(context.SessionDirectory, "testing-tool/execution/phase-0001/supervised");
        request = request with { OutputDirectory = output, ProfiledLaunch = null, RequireOrder = false };
        var result = RunnerMetadataInputs.ReadLayout(context, request,
            Path.Combine(Path.GetDirectoryName(output)!, "execution-request.json"));
        Assert.That(result.Coverage, Is.False);
        Assert.That(result.SessionDirectory, Is.EqualTo(context.SessionDirectory));
        Assert.That(result.CatalogSessionDirectory, Is.EqualTo(context.SessionDirectory));
    }

    [TestCase("both", TestName = "runner-metadata-coverage-layout/reject-ambiguous-launch")]
    [TestCase("no-template", TestName = "runner-metadata-coverage-layout/reject-missing-other-catalog-owner")]
    [TestCase("normal-impersonation", TestName = "runner-metadata-coverage-layout/reject-profiled-normal-layout")]
    [TestCase("request", TestName = "runner-metadata-coverage-layout/reject-wrong-request-file")]
    [TestCase("snapshot", TestName = "runner-metadata-coverage-layout/reject-foreign-snapshot")]
    [TestCase("concurrency", TestName = "runner-metadata-coverage-layout/reject-concurrent-profile")]
    [TestCase("normal-with-coverage-context", TestName = "runner-metadata-coverage-layout/reject-unprofiled-coverage-context")]
    public void InvalidShapeCannotSelectAWeakerLayout(string fault)
    {
        var (context, request, path) = Inputs(false);
        var launch = request.ProfiledLaunch!;
        switch (fault)
        {
            case "both": request = request with { OrderedProfiledLaunch = new("compat.profile", new string('a', 64), launch) }; break;
            case "no-template": context = context with { CoverageCatalogSessionDirectory = null }; break;
            case "normal-impersonation": request = request with { OutputDirectory = Path.Combine(context.SessionDirectory, "testing-tool/execution/phase/supervised") }; break;
            case "request": path = Path.Combine(context.SessionDirectory, "execution-request.json"); break;
            case "snapshot": request = request with { ProfiledLaunch = launch with { SnapshotPath = Path.Combine(context.SessionDirectory, "foreign.dcvr") } }; break;
            case "concurrency": request = request with { MaximumConcurrency = 2 }; break;
            case "normal-with-coverage-context": request = request with { ProfiledLaunch = null }; break;
        }
        var error = Assert.Throws<RunnerMetadataInputs.Rejected>(() => RunnerMetadataInputs.ReadLayout(context, request, path));
        Assert.That(error!.Message, Is.EqualTo(fault == "request" ? "runner-metadata-request-context-binding" : "runner-metadata-coverage-layout"));
    }

    private static (RunnerMetadataInputs.Context Context, FrameworkRunRequest Request, string Path) Inputs(bool ordered)
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fa-runner-coverage-layout");
        var session = System.IO.Path.Combine(root, "collection");
        var output = System.IO.Path.Combine(session, "framework-cover");
        var source = System.IO.Path.Combine(root, "source");
        var dll = System.IO.Path.Combine(source, "tests/FishAudio.Testing.Tests/bin/coverage/Release/net10.0-windows/FishAudio.Testing.Tests.dll");
        var tool = System.IO.Path.Combine(source, "tools/FishAudio.Testing/bin/coverage/Release/net10.0-windows/FishAudio.Testing.dll");
        var digest = new string('a', 64);
        var launch = new DotCoverSdkVstestLaunch(new string('1', 32), "dotCover.dll", digest, "vstest.console.dll", digest,
            System.IO.Path.Combine(output, "coverage.dcvr"), System.IO.Path.Combine(output, "cover.log"));
        var request = new FrameworkRunRequest(1, new string('2', 32), new string('b', 40), source,
            "pwsh.exe", "dotnet.exe", "Invoke-TestProcess.ps1", dll, digest, "settings", System.IO.Path.Combine(session, "discovery.dump"),
            "result.xml", "results.trx", output, [new("layout/one", "Layout.One")], 1, true,
            SourceFingerprint: digest, ProfiledLaunch: ordered ? null : launch,
            OrderedProfiledLaunch: ordered ? new("compat.profile", digest, launch) : null);
        var context = new RunnerMetadataInputs.Context(1, "runner-readonly-metadata", request.RunId, source,
            session, request.SourceTree, digest, System.IO.Path.Combine(session, "source.json"), digest,
            System.IO.Path.Combine(root, "snapshot-context.json"), digest, [], new(tool, digest, System.IO.Path.ChangeExtension(tool, ".pdb"), digest))
        { CoverageCatalogSessionDirectory = System.IO.Path.Combine(root, "discovery") };
        return (context, request, System.IO.Path.Combine(session, "framework-cover-request.json"));
    }
}
