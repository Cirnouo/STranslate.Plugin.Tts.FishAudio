using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

public sealed partial class FrameworkCiEvidenceTests
{
    // Called only after the existing actual-catalog case authenticated its outer
    // source/build/PDB/raw-discovery request. Expected correspondence is neither
    // registration nor a replacement for fresh discovery or actual execution.
    private static void AssertCurrentNativePopupOwnership(MigrationReference reference,
        MigrationLegacyCase[] legacy, List<string> paths, MigrationRecord[] migrations,
        MigrationConservationEvidence current, HybridExecutionSlotPlanResult slots)
    {
        Assert.That(legacy, Is.Empty);
        Assert.That(current.CheckedOwners.Select(item => item.OriginalId),
            Is.EqualTo(reference.Cases.Select(item => item.Id)));
        Assert.That(current.CheckedOwners.All(item => item.Owner == MigrationOwnerKind.NUnit), Is.True,
            "An empty current legacy catalog requires explicit ownership of every immutable original.");
        var originals = reference.Cases.Where(item => item.Partition == "native-popups").ToArray();
        Assert.That(originals, Has.Length.EqualTo(147));
        Assert.That(originals.Take(84).All(item => item.Id.StartsWith("popup.language-tags/", StringComparison.Ordinal)), Is.True);
        Assert.That(originals.Skip(84).Take(60).All(item => item.Id.StartsWith("popup.search-adjustment/native/", StringComparison.Ordinal)), Is.True);
        Assert.That(originals.Skip(144).Select(item => item.Id), Is.EqualTo(new[]
        {
            "popup.keyboard-state/run", "popup.heading/run", "popup.direction/run",
        }));
        var splitSuccessors = new[]
        {
            "popup.keyboard-state/transactions-preserve-state-and-failures",
            "popup.keyboard-state/failure-context-is-bounded-and-private",
            "popup.keyboard-state/post-action-checks-only-owned-bits-and-restores-everything",
            "popup.keyboard-state/cleanup-metadata-cannot-replace-primary-failure",
            "popup.keyboard-state/native-button-and-toggle-peer-transactions",
            "popup.heading/lease-ownership-and-failures",
            "popup.heading/compiled-search-locales-and-reopen",
            "popup.heading/compiled-language-locales-and-reopen",
            "popup.heading/optional-failure/read",
            "popup.heading/optional-failure/write",
            "popup.heading/optional-failure/update",
            "popup.heading/source-replacement-and-destroyed-retirement",
            "popup.direction/search/natural-sides",
            "popup.direction/search/geometry-lifetime",
            "popup.direction/language/natural-sides",
            "popup.direction/language/geometry-lifetime",
        };
        var expected = originals.Take(144).Select(item => item.Id).Concat(splitSuccessors).ToArray();
        Assert.That(expected, Has.Length.EqualTo(160));
        Assert.That(expected, Is.Unique);
        foreach (var original in originals.Take(144))
            Assert.That(migrations.Single(item => item.OriginalId == original.Id).SuccessorIds,
                Is.EqualTo(new[] { original.Id }), "Each named Language/Search responsibility retains its own exact leaf.");
        foreach (var split in new[]
        {
            (Id: "popup.keyboard-state/run", First: 0, Count: 5),
            (Id: "popup.heading/run", First: 5, Count: 7),
            (Id: "popup.direction/run", First: 12, Count: 4),
        })
            Assert.That(migrations.Single(item => item.OriginalId == split.Id).SuccessorIds,
                Is.EqualTo(splitSuccessors.Skip(split.First).Take(split.Count)),
                "Each aggregate must retain its own complete ordered split, without borrowing another original's leaves.");
        var checkedPopups = current.CheckedOwners.Where(item => item.Partition == "native-popups").ToArray();
        Assert.That(checkedPopups.Select(item => item.OriginalId), Is.EqualTo(originals.Select(item => item.Id)));
        Assert.That(checkedPopups.SelectMany(item => item.SuccessorIds), Is.EqualTo(expected));

        var metadata = paths.Select(path => (Path: path, Members: NUnitExecutionMetadata.ReadValidated(path))).ToArray();
        var popups = metadata.SelectMany(owner => owner.Members.Where(item => item.Profile == "native-popups")
            .Select(item => (owner.Path, Policy: item))).OrderBy(item => item.Policy.ProfileOrdinal).ToArray();
        Assert.That(popups, Has.Length.EqualTo(160));
        Assert.That(popups.All(item => item.Path == paths[0]), Is.True,
            "The complete Popup profile belongs to the authenticated plugin raw discovery.");
        Assert.That(popups.Select(item => item.Policy.Case.CanonicalId), Is.EqualTo(expected));
        Assert.That(popups.Select(item => item.Policy.ProfileOrdinal),
            Is.EqualTo(Enumerable.Range(0, 160).Select(value => (int?)value)));
        Assert.That(popups.All(item => item.Policy.FreshHost &&
            item.Policy.ExclusiveResources.SequenceEqual(new[] { "desktop" }, StringComparer.Ordinal)), Is.True);
        var groups = new (int First, int Last, string Fixture, string Method)[]
        {
            (0, 0, "LanguageCompiledCardCases", "CompiledSelectedCardRetainsReusableLanguages"),
            (1, 1, "LanguageCultureCases", "LabelsUseExplicitCultureAndTrustedFlags"),
            (2, 2, "LanguagePartitionCases", "PartitionAndDictionaryReplacement"),
            (3, 3, "LanguageOverflowRowCases", "OverflowDoesNotRaiseLanguageRow"),
            (4, 4, "LanguageResponsivePrefixCases", "ResponsivePrefixFitsOneRowWithoutACap"),
            (5, 5, "LanguageFitThresholdCases", "FitThresholdsAndLongLabels"),
            (6, 6, "LanguageFontMetricsCases", "InheritedFontMetricsRefreshTheSameInstance"),
            (7, 9, "LanguageInitializationFailureCases", "InitializationFailureRetiresItsOriginalOwnership"),
            (10, 10, "LanguagePopupInputCases", "PopupOwnsItsCompleteInputAndLifetimeChain"),
            (11, 11, "LanguageWindowOwnerCases", "WindowOwnerRetiresAndRepositionsTheSamePopup"),
            (12, 83, "LanguageRenderedCardCases", "SelectedAndActiveCardsRetainTheirCompleteRenderedOwner"),
            (84, 84, "SearchCaptionsKeyboardCases", "CaptionsAndKeyboardApplyRetainTheirOriginalChain"),
            (85, 86, "SearchRadioPeerCases", "RadioAppearanceMatchesTheIndependentNativePeer"),
            (87, 87, "SearchDraftInputCases", "DraftsApplyAndTileInputAsync"),
            (88, 88, "SearchHeldIntentCases", "HeldApplyReopensAppliedIntentAsync"),
            (89, 89, "SearchOverlayOwnershipCases", "OverlayOwnershipAndLifetime"),
            (90, 90, "SearchWindowScrollCases", "WindowAndScrollLifetime"),
            (91, 91, "SearchShortViewportCases", "ShortViewportKeepsBothTriggerEdgesInsideItsOwner"),
            (92, 92, "SearchViewportLifecycleCases", "CompleteSameSourceViewportChainRetainsItsAsyncObserver"),
            (93, 93, "SearchMaterialSourceCases", "MaterialOwnsOnlyItsExactPopupSource"),
            (94, 101, "SearchMaterialFallbackCases", "FallbackRetainsTheCompleteFirstSourceThemeAndReopenChain"),
            (102, 143, "SearchGeometryFlagCases", "GeometryAndFlagsRetainBothWidthsAndEveryLiveLocale"),
            (144, 144, "KeyboardTransactionCases", "TransactionsPreserveStateAndFailures"),
            (145, 145, "KeyboardPrivateFailureContextCases", "FailureContextIsBoundedAndPrivate"),
            (146, 146, "KeyboardOwnedDownBitCases", "PostActionChecksOnlyOwnedBitsAndRestoresEverything"),
            (147, 147, "KeyboardPrimaryFailureCases", "CleanupMetadataCannotReplacePrimaryFailure"),
            (148, 148, "KeyboardNativePeerCases", "NativeButtonAndToggleKeepTheOriginalTransactionChain"),
            (149, 149, "HeadingLeaseCases", "HeadingLeaseRetainsEveryOwnershipAndFailureBoundary"),
            (150, 150, "HeadingCompiledSearchCases", "CompiledSearchRetainsAllLocalesAndItsFreshReopen"),
            (151, 151, "HeadingCompiledLanguageCases", "CompiledLanguageRetainsAllLocalesAndItsFreshReopen"),
            (152, 154, "HeadingOptionalFailureCases", "OptionalHeadingFailureRetainsMaterialAndInput"),
            (155, 155, "HeadingSourceReplacementCases", "SourceReplacementRetainsReadBoundsStylesAndDestroyedRetirement"),
            (156, 156, "DirectionSearchNaturalSideCases", "NaturalSidesKeepEverySyntheticAndNativePolicyOnTheSameOwner"),
            (157, 157, "DirectionSearchLifetimeCases", "GeometryLifetimeKeepsReopenReversalRefusalAndRestorationContinuous"),
            (158, 158, "DirectionLanguageNaturalSideCases", "NaturalSidesKeepEverySyntheticAndNativePolicyOnTheSameOwner"),
            (159, 159, "DirectionLanguageLifetimeCases", "GeometryLifetimeKeepsReopenReversalRefusalAndRestorationContinuous"),
        };
        Assert.That(groups.SelectMany(group => Enumerable.Range(group.First, group.Last - group.First + 1)),
            Is.EqualTo(Enumerable.Range(0, 160)), "Typed boundaries have no omitted or multiply owned ordinal.");
        for (var ordinal = 0; ordinal < popups.Length; ordinal++)
        {
            var group = groups.Single(item => ordinal >= item.First && ordinal <= item.Last);
            var fixture = "NUnitCases.NativePopups." + group.Fixture;
            var policy = popups[ordinal].Policy;
            Assert.That(policy.Case.ClassName, Is.EqualTo(fixture));
            Assert.That(policy.FixtureFullName, Is.EqualTo(fixture));
            Assert.That(policy.Case.MethodName, Is.EqualTo(group.Method));
            Assert.That(policy.Case.FullName, Is.EqualTo(fixture + ".native-popups/" +
                ordinal.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + "-" + expected[ordinal].Replace('/', '-')),
                "Each original responsibility retains its named typed leaf, not only the total profile count.");
        }
        var plan = NUnitExecutionPlan.ReadValidated(paths[0], [expected[^1]], maximumConcurrency: 1);
        Assert.That(plan.ExpandedIds, Is.EquivalentTo(expected));
        Assert.That(plan.Phases, Has.Count.EqualTo(1));
        Assert.That(plan.Phases[0].Kind, Is.EqualTo("ordered-profile"));
        Assert.That(plan.Phases[0].Profile, Is.EqualTo("native-popups"));
        Assert.That(plan.Phases[0].RequireOrder, Is.True);
        Assert.That(plan.Phases[0].MaximumConcurrency, Is.EqualTo(1));
        Assert.That(plan.Phases[0].Cases.Select(item => item.CanonicalId), Is.EqualTo(expected));
        Assert.That(slots.Slots.Take(5).Select(item => item.Partition),
            Is.EqualTo(new[] { "core", "native-settings", "native-input", "native-popups", "native-cards" }));
        Assert.That(slots.Slots.Take(5).All(item => item.Owner == "framework" && item.SelectedIds.Count != 0), Is.True);
        Assert.That(slots.Slots[3].SelectedIds, Is.EqualTo(expected),
            "All 160 Popup leaves occupy the original fourth logical slot in their original native order.");
        Assert.That(slots.Slots.Skip(5).SelectMany(item => item.SelectedIds).Intersect(expected, StringComparer.Ordinal),
            Is.Empty, "Popup members cannot run again in the ordinary framework tail.");
        var complete = slots.Slots.SelectMany(item => item.SelectedIds).ToArray();
        Assert.That(complete, Is.Unique);
        Assert.That(complete, Is.EquivalentTo(metadata.SelectMany(item => item.Members).Select(item => item.Case.CanonicalId)),
            "Both complete current catalogs include ordinary new cases without a manually fixed global count.");
        Assert.That(current.AssertionConservationProven || current.LifecycleConservationProven ||
            current.SourceCorrespondenceProven || current.CompleteBaseline, Is.False);
    }
}
