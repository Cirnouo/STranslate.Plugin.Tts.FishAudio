using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using FishAudio.Testing.Evidence;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

// Synthetic identity/XML fixtures only. These are not native/product executions,
// authorization to split real scene chains, or source-authentication substitutes.
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed partial class HybridExecutionSlotPlanTests
{
    private const string Prefix = "fa-hybrid-slot-tests-";
    private const string Profile = "native-settings", Ordinary = "slot.ordinary/plugin", Tool = "slot.ordinary/tool";
    private const string NativeOwner = "legacy.settings/group";
    private static readonly string[] NativeIds = ["slot.native/first", "slot.native/second", "slot.native/third", "slot.native/fourth"];
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private string _parent = "", _root = "";

    [SetUp]
    public void CreateOwnedRawMetadata()
    {
        _parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        RequireDirectoryChain(_parent);
        _root = Path.Combine(_parent, Prefix + Guid.NewGuid().ToString("N"));
        if (File.Exists(_root) || Directory.Exists(_root)) throw new InvalidOperationException("hybrid-slot-owner-exists");
        Directory.CreateDirectory(_root);
        RequireOwnedRoot();
    }

    [TearDown]
    public void RetainFailureOrDeleteOnlyOwnedRawMetadata()
    {
        if (_root.Length == 0) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            RequireOwnedRoot();
            if (failed) { Note("hybrid-slot: failed owned raw inputs retained."); return; }
            foreach (var path in Directory.GetFileSystemEntries(_root))
                if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                    throw new InvalidOperationException("hybrid-slot-cleanup-entry-invalid");
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            Note("hybrid-slot: cleanup failed; owned inputs retained.");
            if (!failed) throw new InvalidOperationException("hybrid-slot-cleanup-failed");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "infra.hybrid-slots/native-profile-keeps-original-desktop-slot")]
    public void CompleteNativeSuccessorUnionOccupiesItsOriginalSlotAndLeavesAnExactGlobalTail()
    {
        var reference = Reference();
        var paths = Discoveries();
        var legacy = Legacy(reference);
        var migrations = Migrations();
        PreflightOwnership(reference, legacy, paths, migrations);
        var before = Bytes();

        var plan = HybridExecutionSlotPlan.ReadValidated(reference, legacy, paths, migrations);

        Assert.That(plan.Slots, Has.Count.EqualTo(6), "hybrid-slot-native-profile-must-have-original-desktop-slot");
        Assert.That(plan.Slots.Select(item => item.Partition),
            Is.EqualTo(new string?[] { "core", Profile, "native-input", "native-popups", "native-cards", null }));
        Assert.That(plan.Slots.Select(item => item.Owner),
            Is.EqualTo(new[] { "legacy", "framework", "legacy", "legacy", "legacy", "framework" }));
        Assert.That(plan.Slots.Select(item => item.Id),
            Is.EqualTo(Enumerable.Range(1, 6).Select(value => "slot-" + value.ToString("D4", CultureInfo.InvariantCulture))));
        Assert.That(plan.Slots[1].SelectedIds, Is.EqualTo(NativeIds),
            "hybrid-slot-native-order-must-use-profile-ordinal-not-discovery-order");
        Assert.That(plan.Slots[^1].SelectedIds, Is.EqualTo(new[] { Ordinary, Tool }));
        Assert.That(plan.Discoveries.Select(item => item.Path), Is.EqualTo(paths));
        for (var index = 0; index < paths.Length; index++)
        {
            Assert.That(plan.Discoveries[index].Sha256, Is.EqualTo(Hash(File.ReadAllBytes(paths[index]))));
            Assert.That(plan.Discoveries[index].CaseIds,
                Is.EqualTo(NUnitEvidence.ReadCatalog(paths[index]).Select(item => item.CanonicalId)));
        }
        var framework = plan.Slots.Where(item => item.Owner == "framework").SelectMany(item => item.SelectedIds).ToArray();
        Assert.That(framework, Is.Unique);
        Assert.That(framework, Is.EquivalentTo(paths.SelectMany(path => NUnitEvidence.ReadCatalog(path).Select(item => item.CanonicalId))));
        Unchanged(before);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.hybrid-slots/legacy-only-keeps-five-partitions")]
    public void NoNativeMigrationKeepsAllFiveSourceProtocolSlotsAndTheFrameworkTail()
    {
        var reference = Reference();
        var paths = new[] { WriteDiscovery("ordinary", [new(Ordinary)]) };
        PreflightOwnership(reference, reference.Cases, paths, []);
        var before = Bytes();

        var plan = HybridExecutionSlotPlan.ReadValidated(reference, reference.Cases, paths, []);

        Assert.That(plan.Slots, Has.Count.EqualTo(6), "hybrid-slot-legacy-owners-must-not-disappear");
        var partitions = new[] { "core", Profile, "native-input", "native-popups", "native-cards" };
        for (var index = 0; index < partitions.Length; index++)
        {
            Assert.That(plan.Slots[index].Owner, Is.EqualTo("legacy"));
            Assert.That(plan.Slots[index].Partition, Is.EqualTo(partitions[index]));
            Assert.That(plan.Slots[index].SelectedIds,
                Is.EqualTo(reference.Cases.Where(item => item.Partition == partitions[index]).Select(item => item.Id)));
        }
        Assert.That(plan.Slots[^1].SelectedIds, Is.EqualTo(new[] { Ordinary }));
        Unchanged(before);
    }

    public static IEnumerable<TestCaseData> OwnershipFaults()
    {
        yield return Fault("overlap", "owner-overlap");
        yield return Fault("partial-owner", "partial-native-ownership");
        yield return Fault("outside-profile", "native-successor-union");
        yield return Fault("resource", "native-resource");
        yield return Fault("cross-assembly", "profile-cross-assembly");
        yield return Fault("merged-assemblies", "discovery-assembly");
        yield return Fault("duplicate-framework", "duplicate-framework-case");
        yield return Fault("legacy-order", "legacy-order");
        yield return Fault("missing-profile", "owner-missing");
    }

    [TestCaseSource(nameof(OwnershipFaults))]
    public void InvalidOwnershipCannotBecomeACompleteSlotPlan(string fault, string code)
    {
        var reference = Reference(extraNativeOwner: fault == "partial-owner");
        var paths = Discoveries(fault);
        var legacy = fault == "overlap" ? reference.Cases.ToArray() : Legacy(reference);
        if (fault == "legacy-order") (legacy[0], legacy[1]) = (legacy[1], legacy[0]);
        var migrations = fault == "overlap" ? Array.Empty<MigrationRecord>() : Migrations();
        // Each raw dump has already passed the actual catalog, metadata and phase
        // readers. Global duplicated IDs are deliberately this admission's fault.
        if (fault != "duplicate-framework") PreflightOwnership(reference, legacy, paths, migrations);
        var before = Bytes();

        var error = Assert.Throws<HybridExecutionSlotPlanException>(() =>
            HybridExecutionSlotPlan.ReadValidated(reference, legacy, paths, migrations),
            "hybrid-slot-invalid-ownership-must-not-admit")!;

        Assert.That(error.Code, Is.EqualTo("hybrid-slot-" + code));
        Assert.That(error.Message, Is.EqualTo(error.Code));
        Assert.That(error.InnerException, Is.Null);
        Unchanged(before);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.hybrid-slots/source-current-input-documents")]
    public void TypedReaderAdmitsActualPinnedReferenceAndTheExistingSourceCurrentListShape()
    {
        var input = InputDocuments();
        var before = Bytes();

        var document = HybridExecutionSlotInput.ReadValidated(input.Reference, input.Legacy, input.Discoveries, input.Migrations);

        Assert.That(document.Plan.Slots, Has.Count.EqualTo(6), "hybrid-slot-source-current-input-must-produce-slots");
        Assert.That(document.SchemaVersion, Is.EqualTo(1));
        Assert.That(document.Kind, Is.EqualTo("hybrid-execution-slot-plan"));
        Assert.That(document.Complete, Is.True);
        Assert.That(document.BaselineClaim, Is.False);
        Assert.That(document.Inputs.ReferenceSha256, Is.EqualTo(Hash(File.ReadAllBytes(input.Reference))));
        Assert.That(document.Inputs.LegacyCatalogSha256, Is.EqualTo(Hash(File.ReadAllBytes(input.Legacy))));
        Assert.That(document.Inputs.DiscoveriesSha256, Is.EqualTo(Hash(File.ReadAllBytes(input.Discoveries))));
        Assert.That(document.Inputs.MigrationsSha256, Is.EqualTo(Hash(File.ReadAllBytes(input.Migrations))));
        Unchanged(before);
    }

    public static IEnumerable<TestCaseData> InputFaults()
    {
        foreach (var fault in new[] { "unknown-member", "duplicate-scalar", "unknown-schema" })
            yield return new TestCaseData(fault).SetName("hybrid-slots/input/" + fault)
                .SetProperty("FishAudioCaseId", "infra.hybrid-slots/input-" + fault);
    }

    [TestCaseSource(nameof(InputFaults))]
    public void ProducerPoliciesOrUnknownJsonFormatsAreRejected(string fault)
    {
        var input = InputDocuments();
        var valid = File.ReadAllText(input.Legacy, Encoding.UTF8);
        var invalid = fault switch
        {
            "unknown-member" => valid.Insert(1, "\"policies\":[],"),
            "duplicate-scalar" => valid.Insert(1, "\"schemaVersion\":1,"),
            _ => valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal),
        };
        Assert.That(invalid, Is.Not.EqualTo(valid), "hybrid-slot-input-fault-must-be-real");
        File.WriteAllText(input.Legacy, invalid, new UTF8Encoding(false));
        var before = Bytes();

        var error = Assert.Throws<HybridExecutionSlotPlanException>(() =>
            HybridExecutionSlotInput.ReadValidated(input.Reference, input.Legacy, input.Discoveries, input.Migrations),
            "hybrid-slot-invalid-source-document-must-not-admit")!;

        Assert.That(error.Code, Is.EqualTo("hybrid-slot-invalid-document"));
        Assert.That(error.Message, Is.EqualTo(error.Code));
        Assert.That(error.InnerException, Is.Null);
        Unchanged(before);
    }

    private static TestCaseData Fault(string fault, string code) =>
        new TestCaseData(fault, code).SetName("hybrid-slots/reject/" + fault)
            .SetProperty("FishAudioCaseId", "infra.hybrid-slots/reject-" + fault);

    private static MigrationReference Reference(bool extraNativeOwner = false)
    {
        var cases = new List<MigrationLegacyCase>
        {
            new("legacy.core/first", "core"), new(NativeOwner, Profile), new("legacy.core/second", "core"),
            new("legacy.input/one", "native-input"), new("legacy.popups/one", "native-popups"), new("legacy.cards/one", "native-cards"),
        };
        if (extraNativeOwner) cases.Insert(2, new("legacy.settings/second-owner", Profile));
        return new(1, new(new string('a', 40), new string('b', 40), new string('c', 64)), cases);
    }

    private static MigrationLegacyCase[] Legacy(MigrationReference reference) =>
        reference.Cases.Where(item => item.Id != NativeOwner).ToArray();

    private static MigrationRecord[] Migrations() =>
        [new(NativeOwner, NativeIds, "Synthetic complete group identity transfer; no product or execution claim.")];

    private string[] Discoveries(string? fault = null)
    {
        if (fault == "merged-assemblies")
        {
            var parts = Discoveries("cross-assembly");
            var first = XDocument.Load(parts[0]);
            var second = XDocument.Load(parts[1]).Descendants("test-suite")
                .Single(item => (string?)item.Attribute("type") == "Assembly");
            foreach (var property in second.Descendants("property")
                         .Where(item => (string?)item.Attribute("name") == "FishAudioProfileOrdinal"))
                property.SetAttributeValue("value", int.Parse((string)property.Attribute("value")!, CultureInfo.InvariantCulture) + 2);
            var run = first.Descendants("test-run").Single();
            run.SetAttributeValue("testcasecount", 6);
            run.Add(second);
            var merged = WriteNew("merged.xml", Encoding.UTF8.GetBytes(first.ToString(SaveOptions.DisableFormatting)));
            PreflightRaw(merged, 6);
            return [merged];
        }
        if (fault == "cross-assembly")
            return [WriteDiscovery("plugin", [new(Ordinary), new(NativeIds[0], 0), new(NativeIds[1], 1)]),
                WriteDiscovery("tool", [new(Tool), new(NativeIds[2], 0), new(NativeIds[3], 1)])];
        var leaves = new List<Leaf> { new(Ordinary) };
        // Reverse the two fixture groups: catalog order deliberately differs
        // from profile order while the complete raw profile remains 0..3.
        foreach (var index in new[] { 2, 3, 0, 1 })
            leaves.Add(new(NativeIds[index], fault == "missing-profile" || (fault == "outside-profile" && index == 3)
                ? null : index, index >= 2 ? "Earlier" : "Later", fault != "resource"));
        if (fault == "outside-profile" || fault == "missing-profile")
            leaves = leaves.Select(item => item.Ordinal is null ? item with { Fixture = "Ordinary" } : item).ToList();
        return [WriteDiscovery("plugin", leaves), WriteDiscovery("tool", [new(fault == "duplicate-framework" ? Ordinary : Tool)])];
    }

    private string WriteDiscovery(string label, IReadOnlyList<Leaf> leaves)
    {
        var assembly = new XElement("test-suite", new XAttribute("type", "Assembly"), new XAttribute("id", label),
            new XAttribute("name", label + ".dll"), new XAttribute("runstate", "Runnable"),
            new XAttribute("testcasecount", leaves.Count));
        foreach (var group in leaves.GroupBy(item => item.Fixture ?? (item.Ordinal is null ? "Ordinary" : "Native")))
        {
            var full = "HybridMetadata." + label + "." + group.Key;
            var fixture = new XElement("test-suite", new XAttribute("type", "TestFixture"),
                new XAttribute("id", label + "-" + group.Key), new XAttribute("name", group.Key),
                new XAttribute("fullname", full), new XAttribute("runstate", "Runnable"),
                new XAttribute("testcasecount", group.Count()));
            if (group.Any(item => item.Ordinal is not null))
            {
                Add(fixture, "FishAudioExecutionProfile", Profile);
                if (group.All(item => item.Desktop)) Add(fixture, "FishAudioExclusiveResource", "desktop");
            }
            var index = 0;
            foreach (var row in group)
            {
                var leaf = new XElement("test-case", new XAttribute("id", label + "-" + group.Key + "-" + index++),
                    new XAttribute("name", row.Id), new XAttribute("fullname", full + "." + row.Id),
                    new XAttribute("classname", full), new XAttribute("methodname", "Rows"), new XAttribute("runstate", "Runnable"));
                Add(leaf, "FishAudioCaseId", row.Id);
                if (row.Ordinal is { } ordinal) Add(leaf, "FishAudioProfileOrdinal", ordinal.ToString(CultureInfo.InvariantCulture));
                fixture.Add(leaf);
            }
            assembly.Add(fixture);
        }
        var xml = new XDocument(new XElement("NUnitXml", new XElement("test-run", new XAttribute("id", label + "-run"),
            new XAttribute("name", label + ".dll"), new XAttribute("runstate", "Runnable"),
            new XAttribute("testcasecount", leaves.Count), assembly)));
        var path = WriteNew(label + ".xml", Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting)));
        PreflightRaw(path, leaves.Count);
        return path;
    }

    private static void PreflightRaw(string path, int count)
    {
        var catalog = NUnitEvidence.ReadCatalog(path);
        Assert.That(catalog.Count, Is.EqualTo(count), "hybrid-slot-fixture-must-pass-current-catalog");
        Assert.That(NUnitExecutionMetadata.ReadValidated(path).Select(item => item.Case), Is.EqualTo(catalog),
            "hybrid-slot-fixture-must-pass-current-execution-metadata");
        Assert.That(NUnitExecutionPlan.ReadValidated(path, catalog.Select(item => item.CanonicalId).ToArray()).Phases,
            Is.Not.Empty, "hybrid-slot-fixture-must-pass-current-phase-planner");
    }

    private InputFiles InputDocuments()
    {
        var root = Environment.GetEnvironmentVariable("FISHAUDIO_TEST_REPOSITORY_ROOT");
        if (root is null || !Path.IsPathFullyQualified(root)) throw new InvalidOperationException("hybrid-slot-explicit-repository-required");
        RequireDirectoryChain(root);
        var golden = Path.Combine(root, "tests", "Migration", "legacy-catalog.json");
        if ((File.GetAttributes(golden) & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
            throw new InvalidOperationException("hybrid-slot-reference-file-invalid");
        var referencePath = WriteNew("reference.json", File.ReadAllBytes(golden));
        var reference = PinnedLegacyReference.ReadValidated(referencePath);
        var paths = new[] { WriteDiscovery("ordinary", [new(Ordinary)]) };
        PreflightOwnership(reference, reference.Cases, paths, []);
        return new(referencePath, WriteJson("legacy.json", new { schemaVersion = 1, cases = reference.Cases }),
            WriteJson("discoveries.json", new { schemaVersion = 1, paths }),
            WriteJson("migrations.json", new { schemaVersion = 1, migrations = Array.Empty<MigrationRecord>() }));
    }

    private static void PreflightOwnership(MigrationReference reference, IReadOnlyList<MigrationLegacyCase> legacy,
        string[] paths, IReadOnlyList<MigrationRecord> migrations) =>
        Assert.That(MigrationConservation.ReadValidated(reference, legacy, paths, migrations).CheckedOwners,
            Has.Count.EqualTo(reference.Cases.Count), "hybrid-slot-fixture-must-pass-independent-migration-admission");

    private string WriteJson(string name, object value) => WriteNew(name, JsonSerializer.SerializeToUtf8Bytes(value, Json));
    private string WriteNew(string name, byte[] bytes)
    {
        var path = Path.Combine(_root, name);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(bytes);
        return path;
    }

    private static void Add(XElement owner, string name, string value)
    {
        var properties = owner.Element("properties");
        if (properties is null) { properties = new XElement("properties"); owner.AddFirst(properties); }
        properties.Add(new XElement("property", new XAttribute("name", name), new XAttribute("value", value)));
    }

    private Dictionary<string, string> Bytes() => Directory.GetFiles(_root).ToDictionary(
        path => Path.GetFileName(path) ?? throw new InvalidOperationException("hybrid-slot-file-name-invalid"),
        path => Hash(File.ReadAllBytes(path)), StringComparer.Ordinal);

    private void Unchanged(Dictionary<string, string> before)
    {
        var after = Bytes();
        Assert.That(after.Keys, Is.EquivalentTo(before.Keys));
        foreach (var item in before) Assert.That(after[item.Key], Is.EqualTo(item.Value), "hybrid-slot-reader-must-preserve-input-bytes");
    }

    private void RequireOwnedRoot()
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
        var name = Path.GetFileName(full);
        if (!Path.IsPathFullyQualified(_root) || full != _root ||
            !string.Equals(Path.GetDirectoryName(full), _parent, StringComparison.OrdinalIgnoreCase) ||
            !name.StartsWith(Prefix, StringComparison.Ordinal) || name.Length != Prefix.Length + 32 ||
            !Guid.TryParseExact(name[Prefix.Length..], "N", out var id) || id == Guid.Empty)
            throw new InvalidOperationException("hybrid-slot-owner-invalid");
        RequireDirectoryChain(full);
    }

    private static void RequireDirectoryChain(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidOperationException("hybrid-slot-directory-invalid");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Note(string message)
    {
        try { TestContext.Progress.WriteLine(message); }
        catch { /* Preserve the primary assertion. */ }
    }

    private sealed record Leaf(string Id, int? Ordinal = null, string? Fixture = null, bool Desktop = true);
    private sealed record InputFiles(string Reference, string Legacy, string Discoveries, string Migrations);
}
