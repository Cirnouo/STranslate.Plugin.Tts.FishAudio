using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using FishAudio.Testing.Evidence;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class FullCoveragePlanTests
{
    private string _root = "";

    [SetUp]
    public void CreateOwnedInputs()
    {
        _root = Path.Combine(Path.GetTempPath(), "fa-full-coverage-plan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void PreserveFailuresAndDeleteOnlyOwnedRegularFixture()
    {
        if (_root.Length == 0 || !Directory.Exists(_root)) return;
        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed) return;
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
        if (Path.GetDirectoryName(full) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) ||
            !Path.GetFileName(full).StartsWith("fa-full-coverage-plan-", StringComparison.Ordinal))
            throw new InvalidOperationException("full-coverage-fixture-owner");
        var suffix = Path.GetFileName(full)["fa-full-coverage-plan-".Length..];
        if (!Guid.TryParseExact(suffix, "N", out _)) throw new InvalidOperationException("full-coverage-fixture-owner-id");
        for (DirectoryInfo? directory = new(full); directory is not null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidOperationException("full-coverage-fixture-parent");
        foreach (var path in Directory.EnumerateFileSystemEntries(full).Prepend(full))
            if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Device | FileAttributes.Directory)) !=
                (path == full ? FileAttributes.Directory : 0)) throw new InvalidOperationException("full-coverage-fixture-entry");
        Directory.Delete(full, true);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-full-plan/metadata-parameters-and-whole-profile")]
    public void FullCatalogSplitsIndependentParametersAndKeepsOneOrderedGroup()
    {
        var request = Inputs();
        var plan = FullCoveragePlan.ReadValidated(request);
        Assert.That(plan.Owners, Has.Count.EqualTo(5));
        Assert.That(plan.Owners.Where(item => item.Kind == "isolated-case").SelectMany(item => item.Cases)
            .Select(item => item.CanonicalId), Is.EquivalentTo(new[] { "plugin/row-a", "plugin/row-b", "plugin/fresh", "tool/one" }));
        Assert.That(plan.Owners.Where(item => item.Kind == "isolated-case").All(item => item.Cases.Count == 1), Is.True);
        var group = plan.Owners.Single(item => item.Kind == "ordered-profile");
        Assert.That(group.Profile, Is.EqualTo("compat.ordered-profile"));
        Assert.That(group.Cases.Select(item => item.CanonicalId), Is.EqualTo(new[] { "plugin/first", "plugin/second" }));
        Assert.That(group.ExecutionDependencies, Is.EqualTo(new[] { "ordered-profile:compat.ordered-profile" }));
        Assert.That(plan.Owners.SelectMany(item => item.Cases).Select(item => item.CanonicalId), Is.Unique);
        Assert.That(plan.SelectedCanonicalIds, Is.EquivalentTo(plan.CatalogCanonicalIds));
        Assert.That(plan.IsWholeCatalogSelection, Is.True);
        Assert.That(plan.CoverageUnionComplete || plan.CompleteBaseline || plan.PublicationAuthorized, Is.False);
    }

    [TestCase(2, TestName = "coverage-full-plan/shards-two-exact-union")]
    [TestCase(3, TestName = "coverage-full-plan/shards-three-exact-union")]
    [TestCase(4, TestName = "coverage-full-plan/shards-four-exact-union")]
    [TestCase(9, TestName = "coverage-full-plan/shards-nine-with-explicit-empty-selections")]
    public void DeterministicShardsHaveNoCaseOverlapAndNeverSplitAnOrderedProfile(int count)
    {
        var input = Inputs();
        var full = FullCoveragePlan.ReadValidated(input);
        var shards = Enumerable.Range(0, count).Select(index => FullCoveragePlan.ReadValidated(input with
            { ShardIndex = index, ShardCount = count })).ToArray();
        var selectedOwners = shards.SelectMany(item => item.SelectedOwnerIds).ToArray();
        Assert.That(selectedOwners, Is.Unique);
        Assert.That(selectedOwners, Is.EquivalentTo(full.Owners.Select(item => item.Id)));
        var ids = shards.SelectMany(item => item.SelectedCanonicalIds).ToArray();
        Assert.That(ids, Is.Unique);
        Assert.That(ids, Is.EquivalentTo(full.CatalogCanonicalIds));
        foreach (var shard in shards)
        {
            Assert.That(shard.IsWholeCatalogSelection || shard.CoverageUnionComplete, Is.False);
            var repeat = FullCoveragePlan.ReadValidated(input with { ShardIndex = shard.ShardIndex, ShardCount = count });
            Assert.That(repeat.SelectedOwnerIds, Is.EqualTo(shard.SelectedOwnerIds));
        }
    }

    [TestCase("empty", TestName = "coverage-full-plan/reject-empty-catalog")]
    [TestCase("duplicate-id", TestName = "coverage-full-plan/reject-cross-catalog-duplicate-id")]
    [TestCase("duplicate-catalog", TestName = "coverage-full-plan/reject-duplicate-catalog")]
    [TestCase("missing-catalog", TestName = "coverage-full-plan/reject-missing-catalog")]
    [TestCase("stale-discovery", TestName = "coverage-full-plan/reject-stale-discovery")]
    [TestCase("missing-discovery", TestName = "coverage-full-plan/reject-missing-discovery")]
    [TestCase("shard", TestName = "coverage-full-plan/reject-invalid-shard")]
    public void InvalidCatalogOrPartitionCannotBecomeACompletePlan(string fault)
    {
        var input = Inputs();
        switch (fault)
        {
            case "empty": File.WriteAllText(input.Catalogs[1].DiscoveryPath, Discovery("tool", [])); break;
            case "duplicate-id": File.WriteAllText(input.Catalogs[1].DiscoveryPath, Discovery("tool", [("plugin/row-a", null, null, false)])); break;
            case "duplicate-catalog": input = input with { Catalogs = [input.Catalogs[0], input.Catalogs[0]] }; break;
            case "missing-catalog": input = input with { Catalogs = [input.Catalogs[0]] }; break;
            case "stale-discovery": File.AppendAllText(input.Catalogs[0].DiscoveryPath, "<!-- drift -->"); break;
            case "missing-discovery": File.Delete(input.Catalogs[0].DiscoveryPath); break;
            case "shard": input = input with { ShardIndex = 4, ShardCount = 4 }; break;
        }
        if (fault is "empty" or "duplicate-id")
            input = input with { Catalogs = input.Catalogs.Select(item => item.Key == "testing-tool" ? item with
                { DiscoverySha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(item.DiscoveryPath))) } : item).ToArray() };
        Assert.Throws<FullCoveragePlanException>(() => FullCoveragePlan.ReadValidated(input));
    }

    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    [TestCase("unknown-option", TestName = "coverage-full-cli/reject-unknown-option")]
    [TestCase("duplicate-option", TestName = "coverage-full-cli/reject-duplicate-option")]
    [TestCase("missing-value", TestName = "coverage-full-cli/reject-missing-value")]
    [TestCase("relative", TestName = "coverage-full-cli/reject-relative-path")]
    [TestCase("unknown-json", TestName = "coverage-full-cli/reject-unknown-json")]
    [TestCase("duplicate-json", TestName = "coverage-full-cli/reject-duplicate-json")]
    public void StrictCommandRejectsMalformedInvocationWithoutPublishing(string fault)
    {
        var request = Path.Combine(_root, "request.json");
        var output = Path.Combine(_root, "output.json");
        var bytes = JsonSerializer.Serialize(Inputs(), FullCoverageCommands.JsonOptions);
        if (fault == "unknown-json") bytes = "{\"unexpected\":true," + bytes[1..];
        if (fault == "duplicate-json") bytes = "{\"schemaVersion\":1," + bytes[1..];
        File.WriteAllText(request, bytes, new UTF8Encoding(false));
        var args = new[] { "coverage-full-plan", "--input", request, "--output", output };
        args = fault switch
        {
            "unknown-option" => [..args, "--other", request],
            "duplicate-option" => [..args, "--input", request],
            "missing-value" => [..args, "--other"],
            "relative" => ["coverage-full-plan", "--input", "relative.json", "--output", output],
            _ => args,
        };
        var previous = Console.Error;
        using var error = new StringWriter();
        try { Console.SetError(error); Assert.That(FullCoverageCommands.Run(args), Is.EqualTo(2)); }
        finally { Console.SetError(previous); }
        Assert.That(File.Exists(output), Is.False);
        Assert.That(error.ToString(), Is.EqualTo("coverage-full: invalid-input-or-output" + Environment.NewLine));
    }


    [Test]
    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    [Property("FishAudioCaseId", "coverage-full-cli/valid-plan-output-is-create-once")]
    public void ValidPlanUsesTheSharedWriterAndCannotReplaceExistingOutput()
    {
        var request = Path.Combine(_root, "valid-request.json");
        var output = Path.Combine(_root, "valid-output.json");
        File.WriteAllBytes(request, JsonSerializer.SerializeToUtf8Bytes(Inputs(), FullCoverageCommands.JsonOptions));
        var args = new[] { "coverage-full-plan", "--input", request, "--output", output };
        var previous = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            Assert.That(FullCoverageCommands.Run(args), Is.Zero);
            var original = File.ReadAllBytes(output);
            using var json = JsonDocument.Parse(original);
            Assert.That(json.RootElement.GetProperty("complete").GetBoolean(), Is.True);
            Assert.That(json.RootElement.GetProperty("baselineClaim").GetBoolean(), Is.False);
            Assert.That(json.RootElement.GetProperty("value").GetProperty("coverageUnionComplete").GetBoolean(), Is.False);
            Assert.That(FullCoverageCommands.Run(args), Is.EqualTo(2));
            Assert.That(File.ReadAllBytes(output), Is.EqualTo(original));
        }
        finally { Console.SetError(previous); }
    }

    private FullCoveragePlanInput Inputs()
    {
        var plugin = Path.Combine(_root, "plugin.xml");
        var tool = Path.Combine(_root, "tool.xml");
        File.WriteAllText(plugin, Discovery("plugin", [("plugin/row-a", null, null, false),
            ("plugin/second", "compat.ordered-profile", 1, false), ("plugin/fresh", null, null, true),
            ("plugin/row-b", null, null, false), ("plugin/first", "compat.ordered-profile", 0, false)]));
        File.WriteAllText(tool, Discovery("tool", [("tool/one", null, null, false)]));
        string Digest(string path) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        return new(1, [new("plugin", plugin, Digest(plugin)), new("testing-tool", tool, Digest(tool))]);
    }

    private static string Discovery(string assembly, (string Id, string? Profile, int? Ordinal, bool Fresh)[] rows)
    {
        var fixture = new XElement("test-suite", new XAttribute("type", "TestFixture"), new XAttribute("id", "fixture"),
            new XAttribute("name", "Rows"), new XAttribute("fullname", assembly + ".Rows"),
            new XAttribute("runstate", "Runnable"), new XAttribute("testcasecount", rows.Length));
        foreach (var row in rows)
        {
            var properties = new XElement("properties", Property("FishAudioCaseId", row.Id));
            if (row.Profile is not null) properties.Add(Property("FishAudioExecutionProfile", row.Profile));
            if (row.Ordinal is { } ordinal) properties.Add(Property("FishAudioProfileOrdinal", ordinal.ToString(CultureInfo.InvariantCulture)));
            if (row.Fresh) properties.Add(Property("FishAudioFreshHost", "true"));
            fixture.Add(new XElement("test-case", new XAttribute("id", row.Id), new XAttribute("name", row.Id),
                new XAttribute("fullname", assembly + ".Rows.Probe(\"" + row.Id + "\")"), new XAttribute("classname", assembly + ".Rows"),
                new XAttribute("methodname", "Probe"), new XAttribute("runstate", "Runnable"), properties));
        }
        return new XDocument(new XElement("NUnitXml", new XElement("test-run", new XAttribute("id", "run"),
            new XAttribute("name", assembly + ".dll"), new XAttribute("runstate", "Runnable"), new XAttribute("testcasecount", rows.Length),
            new XElement("test-suite", new XAttribute("type", "Assembly"), new XAttribute("id", "assembly"),
                new XAttribute("name", assembly + ".dll"), new XAttribute("runstate", "Runnable"),
                new XAttribute("testcasecount", rows.Length), fixture)))).ToString(SaveOptions.DisableFormatting);
    }
    private static XElement Property(string name, string value) => new("property", new XAttribute("name", name), new XAttribute("value", value));
}
