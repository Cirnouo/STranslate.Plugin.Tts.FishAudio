using System.Text.Json;
using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

public sealed partial class IsolatedCoverageSessionTests
{
    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    [TestCase("stale-discovery", TestName = "coverage-full-compose/reject-current-discovery-drift")]
    [TestCase("missing-raw", TestName = "coverage-full-compose/reject-missing-raw-owner-file")]
    [TestCase("missing-owner", TestName = "coverage-full-compose/reject-missing-owner")]
    [TestCase("duplicate-owner", TestName = "coverage-full-compose/reject-duplicate-owner")]
    [TestCase("unknown-owner", TestName = "coverage-full-compose/reject-unknown-owner")]
    public void CompositionCommandReimportsActualRawOwnersBeforeWritingAUnion(string fault)
    {
        var scenario = PrepareBaselineScenario();
        var input = scenario.Input;
        var admission = scenario.Admission;
        switch (fault)
        {
            case "stale-discovery": File.AppendAllText(admission.Catalogs[0].DiscoveryPath, "<!-- stale -->"); break;
            case "missing-raw":
                var raw = input.IsolatedOwners[0].Input;
                File.Delete(Path.Combine(raw.ArtifactDirectory, raw.Artifacts.Single(item => item.Kind == CoverageArtifactKind.Snapshot).RelativePath));
                break;
            case "missing-owner": input = input with { IsolatedOwners = [input.IsolatedOwners[0]] }; break;
            case "duplicate-owner": input = input with { IsolatedOwners = [input.IsolatedOwners[0], input.IsolatedOwners[0]] }; break;
            case "unknown-owner": input = input with { IsolatedOwners = [input.IsolatedOwners[0] with { CatalogKey = "foreign-catalog" }, input.IsolatedOwners[1]] }; break;
        }
        var inputPath = Path.Combine(_root, "full-compose-input.json");
        var expectedPath = Path.Combine(_root, "full-compose-admission.json");
        var outputPath = Path.Combine(_root, "full-compose-output.json");
        var index = 0;
        FullCoverageCommands.OwnerFiles Files(CoverageBaselineIsolatedOwner owner)
        {
            var rawPath = Path.Combine(_root, "full-owner-" + index + "-input.json");
            var ownerExpectedPath = Path.Combine(_root, "full-owner-" + index++ + "-expectation.json");
            File.WriteAllBytes(rawPath, JsonSerializer.SerializeToUtf8Bytes(owner.Input, FullCoverageCommands.JsonOptions));
            File.WriteAllBytes(ownerExpectedPath, JsonSerializer.SerializeToUtf8Bytes(owner.Expectation, FullCoverageCommands.JsonOptions));
            return new(owner.CatalogKey, rawPath, ownerExpectedPath);
        }
        var files = new FullCoverageCommands.CompositionFiles(1, input.IsolatedOwners.Select(Files).ToArray(), []);
        File.WriteAllBytes(inputPath, JsonSerializer.SerializeToUtf8Bytes(files, FullCoverageCommands.JsonOptions));
        File.WriteAllBytes(expectedPath, JsonSerializer.SerializeToUtf8Bytes(admission, FullCoverageCommands.JsonOptions));
        var before = File.ReadAllBytes(inputPath);
        var previous = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            var exitCode = FullCoverageCommands.Run(["coverage-full-compose", "--input", inputPath,
                "--expectation", expectedPath, "--output", outputPath]);
            Assert.That(exitCode, Is.EqualTo(1), "Actual raw candidate admission must reject before output publication.");
        }
        finally { Console.SetError(previous); }
        Assert.That(File.Exists(outputPath), Is.False);
        Assert.That(File.ReadAllBytes(inputPath), Is.EqualTo(before));
        Assert.That(error.ToString(), Does.StartWith("coverage-full: coverage-baseline-candidate-"));
    }
}
