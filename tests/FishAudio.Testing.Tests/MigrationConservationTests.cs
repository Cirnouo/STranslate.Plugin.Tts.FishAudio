using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using FishAudio.Testing.Evidence;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class MigrationConservationTests
{
    // Synthetic identity/protocol fixtures, not execution evidence or approval
    // to split any real historical assertion or native lifecycle chain.
    private const string Accepts = "legacy.format/accepts";
    private const string Rejects = "legacy.format/rejects";
    private const string Group = "legacy.group/run-all";
    private const string NewAccepts = "format/accepts";
    private const string NewRejects = "format/rejects";
    private const string Responsibility = "The original format assertions remain in the declared successor owner.";
    private string _parent = "";
    private string _root = "";
    private bool _created;
    private MigrationReference _reference = null!;

    [SetUp]
    public void CreateOwnedEvidence()
    {
        var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
        if (!string.IsNullOrEmpty(configured) && !Path.IsPathFullyQualified(configured))
            throw new InvalidOperationException("migration-fixture-evidence-root-must-be-absolute");
        _parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            string.IsNullOrEmpty(configured) ? Path.GetTempPath() : configured));
        if (!Directory.Exists(_parent)) throw new InvalidOperationException("migration-fixture-parent-missing");
        RequireNoReparseAncestors(_parent);
        _root = Path.Combine(_parent, "fa-migration-" + Guid.NewGuid().ToString("N"));
        if (File.Exists(_root) || Directory.Exists(_root)) throw new InvalidOperationException("migration-fixture-not-fresh");
        Directory.CreateDirectory(_root);
        _created = true;
        MigrationLegacyCase[] cases = [new(Accepts, "core"), new(Rejects, "core"), new(Group, "core")];
        const string commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string tree = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, commit, tree, cases });
        WriteNew("original-legacy.json", bytes);
        _reference = new(1, new(commit, tree, Convert.ToHexStringLower(SHA256.HashData(bytes))), cases);
    }

    [TearDown]
    public void PreserveFailureOrDeleteOwnedEvidence()
    {
        if (!_created) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            var full = Path.GetFullPath(_root);
            var name = Path.GetFileName(full);
            if (!string.Equals(Path.GetDirectoryName(full), _parent, StringComparison.OrdinalIgnoreCase) ||
                !name.StartsWith("fa-migration-", StringComparison.Ordinal) ||
                !Guid.TryParseExact(name[13..], "N", out _))
                throw new InvalidOperationException("migration-fixture-owner-changed");
            RequireNoReparseAncestors(full);
            if (failed)
            {
                TestContext.Progress.WriteLine("migration-conservation-fixture: failed raw evidence retained as " + name + ".");
                return;
            }
            var entries = Directory.GetFileSystemEntries(full);
            foreach (var entry in entries)
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                    throw new InvalidOperationException("migration-fixture-unexpected-entry");
            }
            foreach (var entry in entries) File.Delete(entry);
            Directory.Delete(full, recursive: false);
        }
        catch (Exception error) when (failed)
        {
            TestContext.Progress.WriteLine("migration-conservation-fixture: original failure and raw evidence retained; cleanup type=" + error.GetType().Name + ".");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "migration/ordinary-attribute-case-needs-no-record")]
    public void OrdinaryAttributeCasesNeedNoMigrationRegistration()
    {
        var discovery = new[]
        {
            WriteDiscovery("plugin", [new(null, "OrdinaryAttributeCase")]),
            WriteDiscovery("tool", [new("new.tool/metadata", "ToolProbe")], "Example.ToolCases"),
        };
        var before = Bytes();

        var result = MigrationConservation.ReadValidated(_reference, _reference.Cases, discovery, []);

        Assert.That(result.SourceReference, Is.EqualTo(_reference.Source));
        Assert.That(result.CheckedOwners.Select(item => item.OriginalId), Is.EquivalentTo(_reference.Cases.Select(item => item.Id)));
        Assert.That(result.CheckedOwners.All(item => item.Owner == MigrationOwnerKind.Legacy &&
            item.SuccessorIds.Count == 0 && item.Responsibility is null), Is.True);
        Assert.That(result.OrdinaryNUnitIds, Is.EquivalentTo(new[] { "Example.MigrationCases.OrdinaryAttributeCase", "new.tool/metadata" }));
        AssertLimitedClaims(result);
        Unchanged(before);
    }

    [Test]
    [Property("FishAudioCaseId", "migration/partial-transfer-keeps-stable-legacy-id")]
    public void PartialTransferRetainsTheOldCanonicalIdAndEveryRemainingLegacyOwner()
    {
        var discovery = WriteDiscovery("partial", [new(Accepts, "Accepts"), new("new/ordinary", "Ordinary")]);
        var before = Bytes();

        var result = MigrationConservation.ReadValidated(_reference,
            _reference.Cases.Where(item => item.Id != Accepts).ToArray(), [discovery],
            [new(Accepts, [Accepts], Responsibility)]);

        Assert.That(result.CheckedOwners, Has.Count.EqualTo(3));
        var moved = result.CheckedOwners.Single(item => item.OriginalId == Accepts);
        Assert.That(moved.Partition, Is.EqualTo("core"));
        Assert.That(moved.Owner, Is.EqualTo(MigrationOwnerKind.NUnit));
        Assert.That(moved.SuccessorIds, Is.EqualTo(new[] { Accepts }));
        Assert.That(moved.Responsibility, Is.EqualTo(Responsibility));
        Assert.That(result.CheckedOwners.Where(item => item.OriginalId != Accepts).All(item => item.Owner == MigrationOwnerKind.Legacy), Is.True);
        Assert.That(result.OrdinaryNUnitIds, Is.EqualTo(new[] { "new/ordinary" }));
        AssertLimitedClaims(result);
        Unchanged(before);
    }

    [Test]
    [Property("FishAudioCaseId", "migration/explicit-historical-group-split")]
    public void AHistoricalGroupCanHaveSeveralExplicitDistinctSuccessors()
    {
        const string descriptor = "The declared first and second responsibilities belong to the two explicit successor cases; lifecycle review remains separate.";
        var discovery = WriteDiscovery("split", [new("group/first", "First"), new("group/second", "Second")]);
        var before = Bytes();

        var result = MigrationConservation.ReadValidated(_reference,
            _reference.Cases.Where(item => item.Id != Group).ToArray(), [discovery],
            [new(Group, ["group/first", "group/second"], descriptor)]);

        var split = result.CheckedOwners.Single(item => item.OriginalId == Group);
        Assert.That(split.Owner, Is.EqualTo(MigrationOwnerKind.NUnit));
        Assert.That(split.SuccessorIds, Is.EqualTo(new[] { "group/first", "group/second" }));
        Assert.That(split.Responsibility, Is.EqualTo(descriptor));
        Assert.That(result.CheckedOwners.Where(item => item.OriginalId != Group).All(item => item.Owner == MigrationOwnerKind.Legacy), Is.True);
        Assert.That(result.OrdinaryNUnitIds, Is.Empty);
        Assert.That(result.SourceReference, Is.EqualTo(_reference.Source));
        AssertLimitedClaims(result);
        Unchanged(before);
    }

    [TestCase("omission", "migration-original-unowned", TestName = "migration/reject-original-omission")]
    [TestCase("legacy-and-migrated", "migration-original-overlap", TestName = "migration/reject-legacy-and-migrated-overlap")]
    [TestCase("unrecorded-old-id", "migration-original-overlap", TestName = "migration/reject-unrecorded-old-id-overlap")]
    [TestCase("duplicate-legacy", "migration-duplicate-legacy-case", TestName = "migration/reject-duplicate-current-legacy")]
    [TestCase("duplicate-original", "migration-duplicate-original", TestName = "migration/reject-duplicate-original-reference")]
    [TestCase("duplicate-record", "migration-duplicate-record", TestName = "migration/reject-duplicate-migration-record")]
    [TestCase("missing-successor", "migration-successor-missing", TestName = "migration/reject-undiscovered-successor")]
    [TestCase("foreign-original", "migration-unknown-original", TestName = "migration/reject-foreign-original-record")]
    [TestCase("empty-responsibility", "migration-invalid-responsibility", TestName = "migration/reject-empty-responsibility")]
    [TestCase("empty-successors", "migration-invalid-successors", TestName = "migration/reject-empty-successor-set")]
    [TestCase("duplicate-successor", "migration-duplicate-successor", TestName = "migration/reject-duplicate-successor")]
    [TestCase("shared-successor", "migration-duplicate-successor", TestName = "migration/reject-ambiguous-shared-successor")]
    [TestCase("non-runnable", "migration-discovery-rejected", TestName = "migration/reject-non-runnable-successor")]
    [TestCase("combined-duplicate", "migration-duplicate-framework-case", TestName = "migration/reject-cross-assembly-canonical-duplicate")]
    [TestCase("partition-drift", "migration-legacy-partition-mismatch", TestName = "migration/reject-legacy-partition-drift")]
    [TestCase("foreign-legacy", "migration-unknown-legacy-case", TestName = "migration/reject-foreign-current-legacy")]
    public void EveryOriginalNeedsOneUnambiguousDeclaredOwner(string fault, string expectedCode)
    {
        var legacy = _reference.Cases.ToList();
        var migrations = new List<MigrationRecord>();
        var leaves = new List<DiscoveryLeaf> { new("new/ordinary", "Ordinary") };
        switch (fault)
        {
            case "omission":
                legacy.RemoveAt(0);
                break;
            case "legacy-and-migrated":
                migrations.Add(new(Accepts, [NewAccepts], Responsibility));
                leaves.Add(new(NewAccepts, "Accepts"));
                break;
            case "unrecorded-old-id":
                leaves.Add(new(Accepts, "Accepts"));
                break;
            case "duplicate-legacy":
                legacy.Add(legacy[0]);
                break;
            case "duplicate-original":
                _reference = _reference with { Cases = [.. _reference.Cases, _reference.Cases[0]] };
                break;
            case "duplicate-record":
                legacy.RemoveAt(0);
                migrations.Add(new(Accepts, [NewAccepts], Responsibility));
                migrations.Add(new(Accepts, [NewRejects], Responsibility));
                leaves.Add(new(NewAccepts, "Accepts"));
                leaves.Add(new(NewRejects, "Rejects"));
                break;
            case "missing-successor":
                legacy.RemoveAt(0);
                migrations.Add(new(Accepts, ["missing/successor"], Responsibility));
                break;
            case "foreign-original":
                migrations.Add(new("foreign/original", [NewAccepts], Responsibility));
                leaves.Add(new(NewAccepts, "Accepts"));
                break;
            case "empty-responsibility":
                legacy.RemoveAt(0);
                migrations.Add(new(Accepts, [NewAccepts], " "));
                leaves.Add(new(NewAccepts, "Accepts"));
                break;
            case "empty-successors":
                legacy.RemoveAt(0);
                migrations.Add(new(Accepts, [], Responsibility));
                break;
            case "duplicate-successor":
                legacy.RemoveAt(0);
                migrations.Add(new(Accepts, [NewAccepts, NewAccepts], Responsibility));
                leaves.Add(new(NewAccepts, "Accepts"));
                break;
            case "shared-successor":
                legacy.RemoveAll(item => item.Id is Accepts or Rejects);
                migrations.Add(new(Accepts, [NewAccepts], Responsibility));
                migrations.Add(new(Rejects, [NewAccepts], Responsibility));
                leaves.Add(new(NewAccepts, "Accepts"));
                break;
            case "non-runnable":
                legacy.RemoveAt(0);
                migrations.Add(new(Accepts, [NewAccepts], Responsibility));
                leaves.Add(new(NewAccepts, "Accepts", "NotRunnable"));
                break;
            case "combined-duplicate":
                break;
            case "partition-drift":
                legacy[0] = legacy[0] with { Partition = "native-input" };
                break;
            case "foreign-legacy":
                legacy.Add(new("foreign/current", "core"));
                break;
            default:
                throw new InvalidOperationException("migration-fixture-unknown-fault");
        }
        var discovery = new List<string> { WriteDiscovery("current", leaves) };
        if (fault == "combined-duplicate")
            discovery.Add(WriteDiscovery("other", [new("new/ordinary", "Another")], "Example.OtherCases"));
        var before = Bytes();

        var error = Assert.Throws<MigrationConservationException>(() =>
            MigrationConservation.ReadValidated(_reference, legacy, discovery, migrations));

        Assert.That(error!.Code, Is.EqualTo(expectedCode));
        Assert.That(error.Message, Is.EqualTo(expectedCode));
        Unchanged(before);
    }

    private static void AssertLimitedClaims(MigrationConservationEvidence result)
    {
        Assert.That(result.SchemaVersion, Is.EqualTo(1));
        Assert.That(result.AssertionConservationProven, Is.False);
        Assert.That(result.LifecycleConservationProven, Is.False);
        Assert.That(result.SourceCorrespondenceProven, Is.False);
        Assert.That(result.CompleteBaseline, Is.False);
    }

    private string WriteDiscovery(string name, IReadOnlyList<DiscoveryLeaf> leaves,
        string className = "Example.MigrationCases")
    {
        var cases = leaves.Select((leaf, index) => new XElement("test-case",
            new XAttribute("id", "0-" + (index + 1)), new XAttribute("name", leaf.Name),
            new XAttribute("fullname", className + "." + leaf.Name), new XAttribute("classname", className),
            new XAttribute("methodname", leaf.Name), new XAttribute("runstate", leaf.RunState),
            leaf.CanonicalId is null ? null : new XElement("properties", new XElement("property",
                new XAttribute("name", "FishAudioCaseId"), new XAttribute("value", leaf.CanonicalId)))));
        var document = new XDocument(new XElement("NUnitXml", new XElement("test-run",
            new XAttribute("id", "0"), new XAttribute("name", name + ".dll"),
            new XAttribute("runstate", "Runnable"), new XAttribute("testcasecount", leaves.Count),
            new XElement("test-suite", new XAttribute("type", "Assembly"), new XAttribute("id", "0-100"),
                new XAttribute("name", name + ".dll"), new XAttribute("runstate", "Runnable"),
                new XAttribute("testcasecount", leaves.Count), cases))));
        return WriteNew(name + ".dump", Encoding.UTF8.GetBytes(document.ToString()));
    }

    private string WriteNew(string name, byte[] bytes)
    {
        var path = Path.Combine(_root, name);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        output.Write(bytes);
        return path;
    }

    private Dictionary<string, byte[]> Bytes() => Directory.GetFiles(_root)
        .ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

    private void Unchanged(Dictionary<string, byte[]> before)
    {
        Assert.That(Directory.GetFileSystemEntries(_root), Is.EquivalentTo(before.Keys));
        Assert.That(before.All(item => File.ReadAllBytes(item.Key).SequenceEqual(item.Value)), Is.True,
            "Identity admission cannot rewrite the reference or raw discovery evidence.");
    }

    private static void RequireNoReparseAncestors(string path)
    {
        for (var item = new DirectoryInfo(path); item is not null; item = item.Parent)
            if (!item.Exists || (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("migration-fixture-reparse-or-missing-owner");
    }

    private sealed record DiscoveryLeaf(string? CanonicalId, string Name, string RunState = "Runnable");
}
