using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using FishAudio.Testing.Evidence;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class FullCoverageSmokePlanTests
{
    private const string FixturePrefix = "fa-full-coverage-smoke-plan-";
    private static readonly string[] SmokeIds = ["plugin/row-b", "tool/one"];
    private string _root = "";

    [SetUp]
    public void CreateOwnedInputs()
    {
        _root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), FixturePrefix + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void PreserveFailuresAndDeleteOnlyOwnedRegularFixture()
    {
        if (_root.Length == 0 || !Directory.Exists(_root)) return;
        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed) return;
        if (!Path.IsPathFullyQualified(_root)) throw new InvalidOperationException("smoke-plan-fixture-absolute-owner");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
        if (!string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())),
                StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(full).StartsWith(FixturePrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("smoke-plan-fixture-owner");
        var suffix = Path.GetFileName(full)[FixturePrefix.Length..];
        if (!Guid.TryParseExact(suffix, "N", out _)) throw new InvalidOperationException("smoke-plan-fixture-owner-id");
        for (DirectoryInfo? directory = new(full); directory is not null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidOperationException("smoke-plan-fixture-parent");
        foreach (var entry in Directory.EnumerateFileSystemEntries(full).Prepend(full))
            if ((File.GetAttributes(entry) & (FileAttributes.ReparsePoint | FileAttributes.Device | FileAttributes.Directory)) !=
                (entry == full ? FileAttributes.Directory : 0)) throw new InvalidOperationException("smoke-plan-fixture-entry");
        Directory.Delete(full, true);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-full-smoke/whole-plan-smoke-first-with-stable-owners-and-full-union")]
    public void WholePlanRunsSmokeOwnersFirstWithoutChangingOwnerIdentityOrCatalogUnion()
    {
        var input = Inputs();
        var plan = FullCoveragePlan.ReadValidated(input);
        Assert.That(plan.Owners.Take(SmokeIds.Length).SelectMany(owner => owner.Cases).Select(item => item.CanonicalId),
            Is.EqualTo(SmokeIds), "A whole-catalog plan must execute its metadata smoke owners before the remaining owners.");

        var unlabelled = FullCoveragePlan.ReadValidated(Inputs(smoke: false, suffix: "unlabelled"));
        var stableIds = unlabelled.Owners.SelectMany(owner => owner.Cases.Select(item => (item.CanonicalId, owner.Id)))
            .ToDictionary(item => item.CanonicalId, item => item.Id, StringComparer.Ordinal);
        foreach (var owner in plan.Owners)
            foreach (var item in owner.Cases)
                Assert.That(owner.Id, Is.EqualTo(stableIds[item.CanonicalId]));
        Assert.That(plan.Owners.Skip(SmokeIds.Length).Select(owner => owner.Id),
            Is.EqualTo(unlabelled.Owners.Where(owner => !owner.Cases.Any(item => SmokeIds.Contains(item.CanonicalId)))
                .Select(owner => owner.Id)));
        Assert.That(plan.SelectedOwnerIds, Is.EqualTo(plan.Owners.Select(owner => owner.Id)));
        Assert.That(plan.SelectedCanonicalIds, Is.EquivalentTo(plan.CatalogCanonicalIds));
        Assert.That(plan.SelectedCanonicalIds, Is.Unique);
        Assert.That(plan.IsWholeCatalogSelection, Is.True);
        Assert.That(plan.CoverageUnionComplete || plan.CompleteBaseline || plan.PublicationAuthorized, Is.False);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-full-smoke/reserved-shard-zero-and-rest-exact-union")]
    public void ShardZeroOwnsOnlySmokeWhileRemainingShardsKeepAnExactDisjointWholeOwnerUnion()
    {
        var input = Inputs();
        var full = FullCoveragePlan.ReadValidated(input);
        var smokeOwners = full.Owners.Where(owner => owner.Cases.Any(item => SmokeIds.Contains(item.CanonicalId))).ToArray();
        var remaining = full.Owners.Where(owner => !owner.Cases.Any(item => SmokeIds.Contains(item.CanonicalId))).ToArray();
        var profile = full.Owners.Single(owner => owner.Kind == "ordered-profile");
        Assert.That(smokeOwners.Length, Is.EqualTo(SmokeIds.Length));
        Assert.That(profile.Cases.Select(item => item.CanonicalId), Is.EqualTo(new[] { "plugin/first", "plugin/second" }));

        foreach (var count in new[] { 2, 3, 9 })
        {
            var shards = Enumerable.Range(0, count).Select(index => FullCoveragePlan.ReadValidated(input with
                { ShardIndex = index, ShardCount = count })).ToArray();
            Assert.That(shards[0].SelectedOwnerIds, Is.EqualTo(smokeOwners.Select(owner => owner.Id)));
            Assert.That(shards[0].SelectedCanonicalIds, Is.EqualTo(SmokeIds));
            for (var index = 1; index < shards.Length; index++)
                Assert.That(shards[index].SelectedOwnerIds,
                    Is.EqualTo(remaining.Where((_, ordinal) => ordinal % (count - 1) == index - 1).Select(owner => owner.Id)));
            var selectedOwners = shards.SelectMany(shard => shard.SelectedOwnerIds).ToArray();
            Assert.That(selectedOwners.Length, Is.EqualTo(full.Owners.Count));
            Assert.That(selectedOwners, Is.Unique);
            Assert.That(selectedOwners, Is.EquivalentTo(full.Owners.Select(owner => owner.Id)));
            var selectedCases = shards.SelectMany(shard => shard.SelectedCanonicalIds).ToArray();
            Assert.That(selectedCases, Is.Unique);
            Assert.That(selectedCases, Is.EquivalentTo(full.CatalogCanonicalIds));
            var profileShards = shards.Where(shard => shard.SelectedOwnerIds.Contains(profile.Id)).ToArray();
            Assert.That(profileShards.Length, Is.EqualTo(1));
            Assert.That(profileShards[0].SelectedCanonicalIds.Intersect(profile.Cases.Select(item => item.CanonicalId)),
                Is.EqualTo(new[] { "plugin/first", "plugin/second" }));
            foreach (var shard in shards)
            {
                var repeated = FullCoveragePlan.ReadValidated(input with { ShardIndex = shard.ShardIndex, ShardCount = count });
                Assert.That(repeated.SelectedOwnerIds, Is.EqualTo(shard.SelectedOwnerIds));
                Assert.That(shard.IsWholeCatalogSelection || shard.CoverageUnionComplete, Is.False);
            }
        }
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-full-smoke/labelled-parameter-row-does-not-promote-sibling")]
    public void SmokeMetadataSelectsOneNamedParameterRowWithoutSelectingItsSameMethodSibling()
    {
        var input = Inputs();
        var plan = FullCoveragePlan.ReadValidated(input with { ShardIndex = 0, ShardCount = 3 });
        var rows = plan.Owners.SelectMany(owner => owner.Cases)
            .Where(item => item.CanonicalId is "plugin/row-a" or "plugin/row-b").ToArray();
        Assert.That(rows.Length, Is.EqualTo(2));
        Assert.That(rows.Select(item => item.ClassName), Is.All.EqualTo("plugin.Rows"));
        Assert.That(rows.Select(item => item.MethodName), Is.All.EqualTo("RowProbe"));
        Assert.That(rows.Select(item => item.FullName), Is.Unique);
        Assert.That(plan.SelectedCanonicalIds, Does.Contain("plugin/row-b"));
        Assert.That(plan.SelectedCanonicalIds, Does.Not.Contain("plugin/row-a"));
        var siblingOwner = plan.Owners.Single(owner => owner.Cases.Any(item => item.CanonicalId == "plugin/row-a"));
        Assert.That(plan.UnselectedOwnerIds, Does.Contain(siblingOwner.Id));
    }

    [TestCase("false", TestName = "coverage-full-smoke/reject-false-property")]
    [TestCase("TRUE", TestName = "coverage-full-smoke/reject-noncanonical-uppercase-property")]
    [TestCase("duplicate", TestName = "coverage-full-smoke/reject-duplicate-scalar-property")]
    public void MalformedSmokeMetadataCannotSilentlyBecomeAnUnlabelledPlan(string fault)
    {
        var input = Inputs(malformedSmoke: fault);
        Assert.Throws<FullCoveragePlanException>(() => FullCoveragePlan.ReadValidated(input));
    }

    [TestCase("ordered-profile", TestName = "coverage-full-smoke/reject-ordered-profile-smoke-owner")]
    [TestCase("desktop-and-keyboard", TestName = "coverage-full-smoke/reject-desktop-or-keyboard-smoke-owner")]
    public void SmokeCannotMoveAnOrderedProfileOrDesktopResourceAheadOfItsRequiredFirstUse(string kind)
    {
        var boundaries = kind == "ordered-profile" ? new[] { "ordered-profile" } : new[] { "desktop", "keyboard" };
        foreach (var boundary in boundaries)
        {
            var input = Inputs(smoke: false, unsafeSmoke: boundary, suffix: boundary);
            Assert.Throws<FullCoveragePlanException>(() => FullCoveragePlan.ReadValidated(input), boundary);
        }
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-full-smoke/unlabelled-plan-retains-original-modulo")]
    public void UnlabelledDiscoveryKeepsTheExistingOwnerOrderAndModuloShardSelection()
    {
        var input = Inputs(smoke: false);
        var full = FullCoveragePlan.ReadValidated(input);
        Assert.That(full.Owners.SelectMany(owner => owner.Cases).Select(item => item.CanonicalId), Is.EqualTo(new[]
        {
            "plugin/before", "plugin/row-a", "plugin/row-b", "plugin/after", "plugin/first", "plugin/second",
            "plugin/fresh", "tool/one", "tool/two",
        }));
        foreach (var count in new[] { 1, 3, 9 })
            for (var index = 0; index < count; index++)
            {
                var shard = FullCoveragePlan.ReadValidated(input with { ShardIndex = index, ShardCount = count });
                Assert.That(shard.SelectedOwnerIds,
                    Is.EqualTo(full.Owners.Where((_, ordinal) => ordinal % count == index).Select(owner => owner.Id)));
            }
    }

    // These raw adapter-shaped inputs are inert metadata. Building the fixture
    // neither constructs a product fixture nor dispatches a test callback.
    private FullCoveragePlanInput Inputs(bool smoke = true, string? malformedSmoke = null,
        string? unsafeSmoke = null, string suffix = "smoke")
    {
        XElement[] rowSmoke = malformedSmoke switch
        {
            "false" => [Property("FishAudioSmoke", "false")],
            "TRUE" => [Property("FishAudioSmoke", "TRUE")],
            "duplicate" => [Property("FishAudioSmoke", "true"), Property("FishAudioSmoke", "true")],
            _ when smoke || unsafeSmoke is "desktop" or "keyboard" => [Property("FishAudioSmoke", "true")],
            _ => [],
        };
        var parameterRows = new XElement("test-suite", new XAttribute("type", "ParameterizedMethod"),
            new XAttribute("id", "parameters"), new XAttribute("name", "RowProbe"),
            new XAttribute("fullname", "plugin.Rows.RowProbe"), new XAttribute("runstate", "Runnable"),
            new XAttribute("testcasecount", 2));
        if (unsafeSmoke is "desktop" or "keyboard")
            parameterRows.Add(new XElement("properties", Property("FishAudioExclusiveResource", unsafeSmoke)));
        parameterRows.Add(Case("plugin", "plugin/row-a", "RowProbe"), Case("plugin", "plugin/row-b", "RowProbe", rowSmoke));
        var orderedSecond = Case("plugin", "plugin/second", "Second",
            Property("FishAudioExecutionProfile", "compat.ordered-profile"), Property("FishAudioProfileOrdinal", "1"));
        if (unsafeSmoke == "ordered-profile")
            orderedSecond.Element("properties")!.Add(Property("FishAudioSmoke", "true"));
        var plugin = Discovery("plugin", Case("plugin", "plugin/before", "Before"), parameterRows,
            orderedSecond, Case("plugin", "plugin/fresh", "Fresh", Property("FishAudioFreshHost", "true")),
            Case("plugin", "plugin/first", "First", Property("FishAudioExecutionProfile", "compat.ordered-profile"),
                Property("FishAudioProfileOrdinal", "0")), Case("plugin", "plugin/after", "After"));
        XElement[] toolSmoke = smoke ? [Property("FishAudioSmoke", "true")] : [];
        var tool = Discovery("tool", Case("tool", "tool/one", "One", toolSmoke), Case("tool", "tool/two", "Two"));
        var pluginPath = Path.Combine(_root, suffix + "-plugin.xml");
        var toolPath = Path.Combine(_root, suffix + "-tool.xml");
        File.WriteAllText(pluginPath, plugin, new UTF8Encoding(false));
        File.WriteAllText(toolPath, tool, new UTF8Encoding(false));
        string Digest(string file) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
        return new(1, [new("plugin", pluginPath, Digest(pluginPath)), new("testing-tool", toolPath, Digest(toolPath))]);
    }

    private static XElement Case(string assembly, string id, string method, params XElement[] extraProperties) =>
        new("test-case", new XAttribute("id", id), new XAttribute("name", id),
            new XAttribute("fullname", assembly + ".Rows." + method + "(\"" + id + "\")"),
            new XAttribute("classname", assembly + ".Rows"), new XAttribute("methodname", method),
            new XAttribute("runstate", "Runnable"), new XElement("properties", Property("FishAudioCaseId", id), extraProperties));

    private static string Discovery(string assembly, params XElement[] children)
    {
        var fixture = new XElement("test-suite", new XAttribute("type", "TestFixture"), new XAttribute("id", "fixture"),
            new XAttribute("name", "Rows"), new XAttribute("fullname", assembly + ".Rows"), new XAttribute("runstate", "Runnable"), children);
        var count = fixture.Descendants("test-case").Count();
        fixture.Add(new XAttribute("testcasecount", count));
        return new XDocument(new XElement("NUnitXml", new XElement("test-run", new XAttribute("id", "run"),
            new XAttribute("name", assembly + ".dll"), new XAttribute("runstate", "Runnable"), new XAttribute("testcasecount", count),
            new XElement("test-suite", new XAttribute("type", "Assembly"), new XAttribute("id", "assembly"),
                new XAttribute("name", assembly + ".dll"), new XAttribute("runstate", "Runnable"),
                new XAttribute("testcasecount", count), fixture)))).ToString(SaveOptions.DisableFormatting);
    }

    private static XElement Property(string name, string value) =>
        new("property", new XAttribute("name", name), new XAttribute("value", value));
}
