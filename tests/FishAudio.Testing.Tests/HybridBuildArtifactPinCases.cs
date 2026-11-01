using System.Text.Json.Nodes;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

public sealed partial class FrameworkCiEvidenceTests
{
    [Test]
    [Property("FishAudioCaseId", "infra.hybrid/pin-normal-build-artifact-names")]
    public async Task HybridBuildPinsUseNormalArtifactNames()
    {
        var expected = PrepareNormalBuildArtifacts();
        var result = await RunHybridBoundaryAsync("build-pins");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        AssertBuildArtifactPins(expected);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.hybrid/reject-changed-build-artifact")]
    public async Task HybridBuildPinsRejectChangedArtifact()
    {
        var expected = PrepareNormalBuildArtifacts();
        var result = await RunHybridBoundaryAsync("changed-build-pin");
        Assert.That(result.ExitCode, Is.Not.Zero, "Changed build bytes cannot retain the earlier pinned identity.");
        Assert.That(result.Error, Does.Contain("hybrid-build-changed"));
        AssertBuildArtifactPins(expected);
        Assert.That(Hash(Path.Combine(_root, "repository", expected[2].Path)),
            Is.Not.EqualTo(expected[2].Sha256), "The negative control must actually change a pinned artifact.");
    }

    private (string Path, string Sha256)[] PrepareNormalBuildArtifacts()
    {
        // Conventional compiler output names, created without the PowerShell
        // extension operation under test. These fixture bytes are only hashed.
        var artifacts = new List<(string Path, string Sha256)>();
        foreach (var assembly in new[]
        {
            "tests/STranslate.Plugin.Tts.FishAudio.Tests/bin/Release/net10.0-windows/STranslate.Plugin.Tts.FishAudio.Tests",
            "tests/FishAudio.Testing.Tests/bin/Release/net10.0-windows/FishAudio.Testing.Tests",
            "tools/FishAudio.Testing/bin/Release/net10.0-windows/FishAudio.Testing",
        })
        foreach (var suffix in new[] { ".dll", ".deps.json", ".runtimeconfig.json" })
        {
            var relative = assembly + suffix;
            var path = Path.Combine(_root, "repository", relative);
            Write(path, "owned-build-artifact:" + relative);
            artifacts.Add((relative, Hash(path)));
        }
        return artifacts.ToArray();
    }

    private void AssertBuildArtifactPins((string Path, string Sha256)[] expected)
    {
        var output = JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "hybrid-verification/build-pins.json")))!;
        var actual = output["pins"]!.AsArray();
        Assert.That(actual.Count, Is.EqualTo(9), "Both test hosts and the metadata tool retain all runtime artifacts.");
        Assert.That(actual.Select(pin => pin!["path"]!.GetValue<string>()),
            Is.EqualTo(expected.Select(pin => pin.Path)), "Artifact names and ownership order match actual output conventions.");
        Assert.That(actual.Select(pin => pin!["sha256"]!.GetValue<string>()),
            Is.EqualTo(expected.Select(pin => pin.Sha256)), "Each pinned identity comes from the artifact's real bytes.");
    }
}