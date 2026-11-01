using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

public sealed partial class FrameworkCiEvidenceTests
{
    // Invoked by the existing actual-catalog case after RunnerMetadataInputs
    // authenticated the outer request, loaded binaries, PDB/source documents,
    // raw discovery and both current catalogs. No discovery is started here.
    private static void AssertCurrentNativeCardsOwnership(MigrationReference reference,
        MigrationLegacyCase[] legacy, List<string> paths, MigrationRecord[] migrations,
        MigrationConservationEvidence current, HybridExecutionSlotPlanResult slots)
    {
        var originals = reference.Cases.Where(item => item.Partition == "native-cards").ToArray();
        Assert.That(originals, Has.Length.EqualTo(855));
        Assert.That(legacy, Is.Empty, "Current identity metadata has no remaining legacy execution owner.");
        Assert.That(migrations, Has.Length.EqualTo(1580));
        Assert.That(migrations.Select(item => item.OriginalId),
            Is.EquivalentTo(reference.Cases.Select(item => item.Id)),
            "All immutable original responsibilities must be explicitly mapped, including the three split Popup aggregates.");
        Assert.That(migrations.Select(item => item.OriginalId), Is.Unique);
        Assert.That(migrations.SelectMany(item => item.SuccessorIds), Is.Unique);
        Assert.That(migrations.SelectMany(item => item.SuccessorIds).Count(), Is.EqualTo(1839));
        var checkedCards = current.CheckedOwners.Where(item => item.Partition == "native-cards").ToArray();
        Assert.That(checkedCards.Select(item => item.OriginalId), Is.EqualTo(originals.Select(item => item.Id)));
        Assert.That(checkedCards.All(item => item.Owner == MigrationOwnerKind.NUnit), Is.True);
        foreach (var original in originals)
        {
            var mapped = migrations.Single(item => item.OriginalId == original.Id);
            Assert.That(mapped.SuccessorIds, Is.EqualTo(new[] { original.Id }),
                "Each Cards original retains its own exact canonical successor, without borrowing another leaf.");
        }
        var expected = originals.Select(item => item.Id).ToArray();
        var metadata = paths.Select(path => (Path: path, Members: NUnitExecutionMetadata.ReadValidated(path))).ToArray();
        var cards = metadata.SelectMany(owner => owner.Members.Where(item => item.Profile == "native-cards")
            .Select(item => (owner.Path, Policy: item))).OrderBy(item => item.Policy.ProfileOrdinal).ToArray();
        Assert.That(cards, Has.Length.EqualTo(855));
        Assert.That(cards.All(item => item.Path == paths[0]), Is.True,
            "All transferred Cards owners must belong to the actual plugin raw discovery.");
        Assert.That(cards.Select(item => item.Policy.Case.CanonicalId), Is.EqualTo(expected));
        Assert.That(cards.Select(item => item.Policy.ProfileOrdinal),
            Is.EqualTo(Enumerable.Range(0, 855).Select(value => (int?)value)));
        Assert.That(cards.All(item => item.Policy.FreshHost &&
            item.Policy.ExclusiveResources.SequenceEqual(new[] { "desktop" }, StringComparer.Ordinal)), Is.True);

        // Typed source boundaries, independently compared against every raw row.
        // This is an expected correspondence oracle, not an execution registry.
        var groups = new (int First, int Last, string Fixture, string Method)[]
        {
            (0, 0, "CompiledTemplateAnchorCases", "SharedCardTemplateIsCompiled"),
            (1, 1, "VoiceCardLayoutCases", "ColdConstruction"),
            (2, 505, "VoiceCardLayoutCases", "StaticCards"),
            (506, 577, "VoiceCardLayoutCases", "Descriptions"),
            (578, 649, "VoiceCardLayoutCases", "Headers"),
            (650, 667, "VoiceCardLayoutCases", "Inheritance"),
            (668, 673, "VoiceCardLayoutCases", "Dates"),
            (674, 676, "VoiceCardLayoutCases", "Tooltips"),
            (677, 682, "VoiceCardLayoutCases", "Loading"),
            (683, 683, "VoiceDescriptionCases", "Construction"),
            (684, 707, "VoiceDescriptionCases", "RuntimeDescriptionSequence"),
            (708, 708, "VoiceLikeCases", "SharedCardTemplateIsCompiled"),
            (709, 709, "VoiceLikeCases", "TabPresentationAndLiveLocales"),
            (710, 710, "VoiceLikeCases", "SelectedHeartSharedState"),
            (711, 794, "VoiceLikeCases", "LocalizedLikeState"),
            (795, 830, "AvatarPreviewCases", "ContinuousLifecycle"),
            (831, 831, "SearchPreviewRetentionCases", "DiscoveryKeepsAcceptedAudio"),
            (832, 832, "SearchPreviewRetentionCases", "PendingOriginStillRetires"),
            (833, 833, "SearchPreviewRetentionCases", "DisplayedPendingOriginMayStillAccept"),
            (834, 834, "SearchPreviewRetentionCases", "AcceptanceReentrancyKeepsTheCorrectOwner"),
            (835, 835, "SearchPreviewRetentionCases", "AcceptedPublicationCannotRetireItsSuccessor"),
            (836, 836, "SearchPreviewRetentionCases", "ExplicitReplacementAndRetirement"),
            (837, 837, "SearchPreviewRetentionCases", "OtherOriginRemovalRetainsAcceptedAudio"),
            (838, 841, "AcceptedPreviewContinuityCases", "Selection"),
            (842, 843, "AcceptedPreviewContinuityCases", "Unlike"),
            (844, 844, "AcceptedPreviewContinuityCases", "FavoriteBrowsing"),
            (845, 845, "AcceptedPreviewContinuityCases", "ByIdBrowsing"),
            (846, 847, "AcceptedPreviewContinuityCases", "CollectionHandoff"),
            (848, 851, "AcceptedPreviewContinuityCases", "ManagementHandoff"),
            (852, 852, "AcceptedPreviewContinuityCases", "Credentials"),
            (853, 853, "AcceptedPreviewContinuityCases", "Lifecycle"),
            (854, 854, "AcceptedPreviewContinuityCases", "RejectedSaves"),
        };
        Assert.That(groups.SelectMany(group => Enumerable.Range(group.First, group.Last - group.First + 1)),
            Is.EqualTo(Enumerable.Range(0, 855)), "Typed boundaries must have no omitted or multiply owned ordinal.");
        for (var ordinal = 0; ordinal < cards.Length; ordinal++)
        {
            var group = groups.Single(item => ordinal >= item.First && ordinal <= item.Last);
            var owner = "NUnitCases.NativeCards." + group.Fixture;
            var policy = cards[ordinal].Policy;
            Assert.That(policy.Case.ClassName, Is.EqualTo(owner));
            Assert.That(policy.FixtureFullName, Is.EqualTo(owner));
            Assert.That(policy.Case.MethodName, Is.EqualTo(group.Method));
            Assert.That(policy.Case.FullName, Is.EqualTo(owner + ".native-cards/" +
                ordinal.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + "-" + expected[ordinal].Replace('/', '-')),
                "Every original ID must retain its actual named typed leaf, not merely the profile count.");
        }
        var plan = NUnitExecutionPlan.ReadValidated(paths[0], [expected[^1]], maximumConcurrency: 1);
        Assert.That(plan.ExpandedIds, Is.EquivalentTo(expected));
        Assert.That(plan.Phases, Has.Count.EqualTo(1));
        Assert.That(plan.Phases[0].Kind, Is.EqualTo("ordered-profile"));
        Assert.That(plan.Phases[0].Profile, Is.EqualTo("native-cards"));
        Assert.That(plan.Phases[0].MaximumConcurrency, Is.EqualTo(1));
        Assert.That(plan.Phases[0].RequireOrder, Is.True);
        Assert.That(plan.Phases[0].Cases.Select(item => item.CanonicalId), Is.EqualTo(expected));
        Assert.That(slots.Slots[4].Partition, Is.EqualTo("native-cards"));
        Assert.That(slots.Slots[4].Owner, Is.EqualTo("framework"));
        Assert.That(slots.Slots[4].SelectedIds, Is.EqualTo(expected),
            "Cards must occupy its original fifth logical slot with all original-order successors.");
        Assert.That(slots.Slots.Skip(5).SelectMany(item => item.SelectedIds).Intersect(expected, StringComparer.Ordinal),
            Is.Empty, "No profile member may be executed a second time in the ordinary framework tail.");
        var complete = slots.Slots.SelectMany(item => item.SelectedIds).ToArray();
        Assert.That(complete, Is.Unique);
        Assert.That(complete, Is.EquivalentTo(legacy.Select(item => item.Id)
            .Concat(metadata.SelectMany(item => item.Members).Select(item => item.Case.CanonicalId))));
        // The exact current-catalog union above includes ordinary new leaves
        // without a migration record or a manually maintained global count.
        Assert.That(current.AssertionConservationProven || current.LifecycleConservationProven ||
            current.SourceCorrespondenceProven || current.CompleteBaseline, Is.False);
    }
}
