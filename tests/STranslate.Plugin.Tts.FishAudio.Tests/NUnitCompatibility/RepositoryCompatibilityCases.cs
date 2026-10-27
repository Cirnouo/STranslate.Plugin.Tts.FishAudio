using System.Text.Json;
using NUnit.Framework;

namespace NUnitCompatibility;

[TestFixture]
public sealed class RepositoryCompatibilityCases
{
    [Test]
    [Property("FishAudioCaseId", "compat.repository/pinned-coverage-tool")]
    [Property("FishAudioEmptyProductCoverage", "RepositoryResourcesOnly")]
    [Property("FishAudioRepositoryDependency", ".config/dotnet-tools.json")]
    public void RepositoryPinsTheModernCoverageTool()
    {
        // FindRepoFile uses the explicit frozen SnapshotContext when present.
        // This contract deliberately executes no plugin product method.
        var path = TestAssertions.FindRepoFile(".config/dotnet-tools.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var tool = document.RootElement.GetProperty("tools")
            .GetProperty("jetbrains.dotcover.commandlinetools");
        Assert.That(tool.GetProperty("version").GetString(), Is.EqualTo("2026.2.3.1"));
        Assert.That(tool.GetProperty("commands").EnumerateArray().Select(item => item.GetString()),
            Is.EquivalentTo(new[] { "dotCover" }));
    }
}
