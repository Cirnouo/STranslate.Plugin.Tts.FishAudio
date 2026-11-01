using System.Globalization;
using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

// Uses the existing owned raw XML fixture and real metadata/conservation readers.
// These synthetic identities do not claim product execution or a baseline.
public sealed partial class HybridExecutionSlotPlanTests
{
    private const string FirstCore = "legacy.core/first", SecondCore = "legacy.core/second";
    private const string UnmappedCoreTail = "slot.ordinary/unmapped";

    [Test]
    [Property("FishAudioCaseId", "infra.hybrid-slots/partial-core-retains-legacy-and-framework-tail")]
    public void PartialCoreMigrationRetainsItsNonemptyLegacySlotAndMappedFrameworkTail()
    {
        var reference = Reference();
        var paths = Discoveries();
        var legacy = Legacy(reference).Where(item => item.Id != FirstCore).ToArray();
        var records = Migrations().Append(new MigrationRecord(FirstCore, [Ordinary], "Synthetic first core owner.")).ToArray();
        PreflightOwnership(reference, legacy, paths, records);
        var before = Bytes();

        var plan = HybridExecutionSlotPlan.ReadValidated(reference, legacy, paths, records);

        Assert.That(plan.Slots[0].Owner, Is.EqualTo("legacy"));
        Assert.That(plan.Slots[0].Partition, Is.EqualTo("core"));
        Assert.That(plan.Slots[0].SelectedIds, Is.EqualTo(new[] { SecondCore }));
        Assert.That(plan.Slots[^1].Partition, Is.Null);
        Assert.That(plan.Slots[^1].SelectedIds, Is.EqualTo(new[] { Ordinary, Tool }),
            "Partial core successors still execute in the exact ordinary tail.");
        AssertFrameworkUnion(plan, paths);
        Unchanged(before);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.hybrid-slots/complete-core-uses-both-actual-catalogs-once")]
    public void CompleteCoreUsesMappedDiscoveryIdentitiesAcrossBothAssembliesAndExcludesThemFromTail()
    {
        var reference = Reference();
        var paths = CompleteCoreDiscoveries(includeTail: true);
        var legacy = Legacy(reference).Where(item => item.Partition != "core").ToArray();
        var records = CompleteCoreRecords();
        PreflightOwnership(reference, legacy, paths, records);
        var before = Bytes();

        var plan = HybridExecutionSlotPlan.ReadValidated(reference, legacy, paths, records);

        Assert.That(plan.Slots, Has.Count.EqualTo(6));
        Assert.That(plan.Slots.Take(5).Select(item => item.Partition),
            Is.EqualTo(new[] { "core", Profile, "native-input", "native-popups", "native-cards" }));
        Assert.That(plan.Slots.Select(item => item.Owner),
            Is.EqualTo(new[] { "framework", "framework", "legacy", "legacy", "legacy", "framework" }));
        Assert.That(plan.Slots[0].SelectedIds, Is.EqualTo(new[] { Ordinary, Tool }),
            "Core selection follows actual discovery order, even when migration-record order differs.");
        Assert.That(plan.Slots[1].SelectedIds, Is.EqualTo(NativeIds));
        Assert.That(plan.Slots[^1].SelectedIds, Is.EqualTo(new[] { UnmappedCoreTail }));
        Assert.That(plan.Discoveries.Select(item => item.Path), Is.EqualTo(paths));
        AssertFrameworkUnion(plan, paths);
        Unchanged(before);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.hybrid-slots/complete-core-never-creates-empty-passed-tail")]
    public void CompleteCoreWithNoUnmappedFrameworkWorkHasFiveNonemptySlotsAndNoEmptyTail()
    {
        var reference = Reference();
        var paths = CompleteCoreDiscoveries(includeTail: false);
        var legacy = Legacy(reference).Where(item => item.Partition != "core").ToArray();
        var records = CompleteCoreRecords();
        PreflightOwnership(reference, legacy, paths, records);
        var before = Bytes();

        var plan = HybridExecutionSlotPlan.ReadValidated(reference, legacy, paths, records);

        Assert.That(plan.Slots, Has.Count.EqualTo(5));
        Assert.That(plan.Slots.All(item => item.Partition is not null && item.SelectedIds.Count > 0), Is.True);
        Assert.That(plan.Slots[0].Owner, Is.EqualTo("framework"));
        Assert.That(plan.Slots[0].SelectedIds, Is.EqualTo(new[] { Ordinary, Tool }));
        AssertFrameworkUnion(plan, paths);
        Unchanged(before);
    }

    public static IEnumerable<TestCaseData> CoreOwnershipFaults()
    {
        foreach (var row in new[]
        {
            ("missing-owner", "migration-rejected"), ("missing-successor", "migration-rejected"),
            ("legacy-overlap", "migration-rejected"), ("empty-successors", "migration-rejected"),
            ("wrong-legacy-partition", "migration-rejected"),
            ("wrong-full-profile", "core-successor-profile"),
            ("wrong-partial-profile", "core-successor-profile"),
            ("duplicate-discovery", "duplicate-framework-case"),
        })
            yield return new TestCaseData(row.Item1, row.Item2).SetName("hybrid-slots/core-reject/" + row.Item1)
                .SetProperty("FishAudioCaseId", "infra.hybrid-slots/core-reject-" + row.Item1);
    }

    [TestCaseSource(nameof(CoreOwnershipFaults))]
    public void MissingOverlappingOrProfileBoundCoreCannotBecomeACompleteFrameworkOwner(string fault, string code)
    {
        var reference = Reference();
        var paths = CompleteCoreDiscoveries(includeTail: false);
        var legacy = Legacy(reference).Where(item => item.Partition != "core").ToArray();
        var records = CompleteCoreRecords();
        if (fault == "missing-owner") records = records.Where(item => item.OriginalId != SecondCore).ToArray();
        else if (fault == "missing-successor") records = records.Select(item => item.OriginalId == SecondCore
            ? item with { SuccessorIds = new[] { "slot.core/not-discovered" } } : item).ToArray();
        else if (fault == "legacy-overlap") legacy = Legacy(reference);
        else if (fault == "empty-successors") records = records.Select(item => item.OriginalId == FirstCore
            ? item with { SuccessorIds = Array.Empty<string>() } : item).ToArray();
        else if (fault == "wrong-legacy-partition") legacy = legacy.Append(new MigrationLegacyCase(FirstCore, "native-input")).ToArray();
        else if (fault is "wrong-full-profile" or "wrong-partial-profile")
        {
            records = records.Select(item => item.OriginalId == NativeOwner
                ? item with { SuccessorIds = NativeIds.Skip(1).ToArray() }
                : item.OriginalId == FirstCore ? item with { SuccessorIds = new[] { NativeIds[0] } } : item).ToArray();
            if (fault == "wrong-partial-profile")
            {
                records = records.Where(item => item.OriginalId != SecondCore).ToArray();
                legacy = legacy.Append(new MigrationLegacyCase(SecondCore, "core")).ToArray();
                // Reconstruct retained reference order before testing profile ownership.
                legacy = reference.Cases.Where(item => legacy.Any(retained => retained.Id == item.Id)).ToArray();
            }
            PreflightOwnership(reference, legacy, paths, records);
        }
        else if (fault == "duplicate-discovery")
            paths = [WriteDiscovery("core-duplicate-plugin", [new(Ordinary), .. NativeIds.Select((id, ordinal) => new Leaf(id, ordinal))]),
                WriteDiscovery("core-duplicate-tool", [new(Tool), new(Ordinary)])];
        else throw new InvalidOperationException("hybrid-core-control-invalid");
        var before = Bytes();

        var rejected = Assert.Throws<HybridExecutionSlotPlanException>(() =>
            HybridExecutionSlotPlan.ReadValidated(reference, legacy, paths, records))!;

        Assert.That(rejected.Code, Is.EqualTo("hybrid-slot-" + code));
        Assert.That(rejected.Message, Is.EqualTo(rejected.Code));
        Assert.That(rejected.InnerException, Is.Null);
        Unchanged(before);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.hybrid-slots/complete-core-binds-schema1-source-current-documents")]
    public void CompletePinnedCoreUsesExistingSchemaOneDocumentsAndExactInputDigests()
    {
        var existing = InputDocuments();
        var reference = PinnedLegacyReference.ReadValidated(existing.Reference);
        var originals = reference.Cases.Where(item => item.Partition == "core").ToArray();
        Assert.That(originals, Has.Length.EqualTo(309));
        var ids = originals.Select((_, index) => "slot.core/reference-" + index.ToString("D3", CultureInfo.InvariantCulture)).ToArray();
        var paths = new[]
        {
            WriteDiscovery("core-reference-plugin", ids.Where((_, index) => index % 2 == 0).Select(id => new Leaf(id)).ToArray()),
            WriteDiscovery("core-reference-tool", ids.Where((_, index) => index % 2 != 0).Select(id => new Leaf(id)).ToArray()),
        };
        var records = originals.Select((item, index) => new MigrationRecord(item.Id, new[] { ids[index] },
            "Synthetic complete original core identity transfer; no product execution claim.")).ToArray();
        var legacy = reference.Cases.Where(item => item.Partition != "core").ToArray();
        PreflightOwnership(reference, legacy, paths, records);
        var input = new InputFiles(existing.Reference,
            WriteJson("core-reference-legacy.json", new { schemaVersion = 1, cases = legacy }),
            WriteJson("core-reference-discoveries.json", new { schemaVersion = 1, paths }),
            WriteJson("core-reference-migrations.json", new { schemaVersion = 1, migrations = records }));
        var before = Bytes();

        var document = HybridExecutionSlotInput.ReadValidated(input.Reference, input.Legacy, input.Discoveries, input.Migrations);

        Assert.That(document.SchemaVersion, Is.EqualTo(1));
        Assert.That(document.Kind, Is.EqualTo("hybrid-execution-slot-plan"));
        Assert.That(document.Complete, Is.True);
        Assert.That(document.BaselineClaim, Is.False);
        Assert.That(document.Plan.Slots, Has.Count.EqualTo(5));
        Assert.That(document.Plan.Slots[0].Owner, Is.EqualTo("framework"));
        Assert.That(document.Plan.Slots[0].SelectedIds, Has.Count.EqualTo(309));
        Assert.That(document.Plan.Slots[0].SelectedIds,
            Is.EqualTo(paths.SelectMany(path => NUnitEvidence.ReadCatalog(path).Select(item => item.CanonicalId))));
        Assert.That(document.Inputs.ReferenceSha256, Is.EqualTo(Hash(File.ReadAllBytes(input.Reference))));
        Assert.That(document.Inputs.LegacyCatalogSha256, Is.EqualTo(Hash(File.ReadAllBytes(input.Legacy))));
        Assert.That(document.Inputs.DiscoveriesSha256, Is.EqualTo(Hash(File.ReadAllBytes(input.Discoveries))));
        Assert.That(document.Inputs.MigrationsSha256, Is.EqualTo(Hash(File.ReadAllBytes(input.Migrations))));
        AssertFrameworkUnion(document.Plan, paths);
        Unchanged(before);
    }

    private string[] CompleteCoreDiscoveries(bool includeTail) =>
        [WriteDiscovery("core-plugin", [new(Ordinary), .. NativeIds.Select((id, ordinal) => new Leaf(id, ordinal))]),
            WriteDiscovery("core-tool", includeTail ? new Leaf[] { new(Tool), new(UnmappedCoreTail) } : [new(Tool)])];

    // Opposite the actual catalog order, to ensure raw discovery owns selection identity/order.
    private static MigrationRecord[] CompleteCoreRecords() => [.. Migrations(),
        new(FirstCore, [Tool], "Synthetic first core owner in the tool assembly."),
        new(SecondCore, [Ordinary], "Synthetic second core owner in the plugin assembly.")];

    private static void AssertFrameworkUnion(HybridExecutionSlotPlanResult plan, string[] paths)
    {
        Assert.That(plan.Slots.All(item => item.SelectedIds.Count > 0), Is.True);
        var actual = plan.Slots.Where(item => item.Owner == "framework").SelectMany(item => item.SelectedIds).ToArray();
        Assert.That(actual, Is.Unique);
        Assert.That(actual, Is.EquivalentTo(paths.SelectMany(path => NUnitEvidence.ReadCatalog(path).Select(item => item.CanonicalId))),
            "The core/native/tail disjoint union must cover every actual raw discovery identity exactly once.");
    }
}
