using System.Text.Json;
using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

public sealed partial class IsolatedCoverageSessionTests
{
    // These raw protocol fixtures use actual copied DLL/PDBs. They neither
    // execute a profiler nor establish current whole-catalog coverage.
    [Test]
    [Property("FishAudioCaseId", "coverage-shard-core/isolated-selection-is-never-full-union")]
    public void SelectedIsolatedOwnerKeepsBothCatalogsWithoutClaimingTheirCompleteUnion()
    {
        var scenario = PrepareBaselineScenario();
        Assert.That(CoverageBaselineCandidate.ReadValidated(scenario.Input, scenario.Admission).CoverageUnionComplete, Is.True);
        var before = CaptureBaselineBytes(scenario.Admission);
        var selected = scenario.Input with { IsolatedOwners = [scenario.Input.IsolatedOwners[0]] };
        var result = CoverageBaselineCandidate.ReadValidatedSelection(selected, scenario.Admission, [CanonicalId]);
        Assert.That(result.CoverageUnionComplete, Is.False);
        Assert.That(result.AllRequiredOwnersAdmitted, Is.True);
        Assert.That(result.Catalogs, Has.Count.EqualTo(2));
        Assert.That(result.Catalogs.SelectMany(item => item.Cases).ToArray(), Has.Length.EqualTo(2));
        Assert.That(result.IsolatedOwners.Select(item => item.Coverage.Identity.CanonicalId), Is.EqualTo(new[] { CanonicalId }));
        Assert.That(result.OrderedGroups, Is.Empty);
        var all = CoverageBaselineCandidate.ReadValidatedSelection(scenario.Input, scenario.Admission, [CanonicalId, "baseline/tool-case"]);
        Assert.That(all.CoverageUnionComplete, Is.False, "Selection admission never becomes complete composition, even when its set equals both catalogs.");
        Assert.That(result.CompleteBaseline || result.PublicationAuthorized || result.TiaSelectionAuthorized, Is.False);
        AssertBaselineBytes(before);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-shard-core/empty-selection-requires-empty-owner-lists")]
    public void EmptySelectionStillValidatesTheFullBuildAndCannotHideRawOwners()
    {
        var scenario = PrepareBaselineScenario();
        Assert.That(CoverageBaselineCandidate.ReadValidated(scenario.Input, scenario.Admission).CoverageUnionComplete, Is.True);
        var empty = scenario.Input with { IsolatedOwners = [], OrderedGroups = [] };
        var before = CaptureBaselineBytes(scenario.Admission);
        var result = CoverageBaselineCandidate.ReadValidatedSelection(empty, scenario.Admission, []);
        Assert.That(result.Catalogs, Has.Count.EqualTo(2));
        Assert.That(result.BinaryBuildMetadata, Has.Count.EqualTo(4));
        Assert.That(result.IsolatedOwners, Is.Empty);
        Assert.That(result.OrderedGroups, Is.Empty);
        Assert.That(result.CoverageUnionComplete || result.CompleteBaseline || result.PublicationAuthorized, Is.False);
        var error = Assert.Throws<CoverageBaselineCandidateException>(() =>
            CoverageBaselineCandidate.ReadValidatedSelection(scenario.Input, scenario.Admission, []));
        Assert.That(error!.Code, Is.EqualTo("coverage-baseline-candidate-owner-count"));
        AssertBaselineBytes(before);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-shard-core/reject-missing-selected-owner")]
    public void AValidPartialOwnerCannotSatisfyAnAdditionalRequiredCase()
    {
        var scenario = PrepareBaselineScenario();
        var selected = scenario.Input with { IsolatedOwners = [scenario.Input.IsolatedOwners[0]] };
        Assert.That(CoverageBaselineCandidate.ReadValidatedSelection(selected, scenario.Admission, [CanonicalId]).AllRequiredOwnersAdmitted, Is.True);
        var before = CaptureBaselineBytes(scenario.Admission);
        var error = Assert.Throws<CoverageBaselineCandidateException>(() => CoverageBaselineCandidate.ReadValidatedSelection(
            selected, scenario.Admission, [CanonicalId, "baseline/tool-case"]));
        Assert.That(error!.Code, Is.EqualTo("coverage-baseline-candidate-missing-owner"));
        Assert.That(error!.UnadmittedCanonicalIds, Is.EqualTo(new[] { "baseline/tool-case" }));
        AssertBaselineBytes(before);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-shard-core/reject-unselected-valid-owner")]
    public void AnotherIndependentlyValidRawOwnerCannotReplaceTheSelectedCase()
    {
        var scenario = PrepareBaselineScenario();
        Assert.That(CoverageBaselineCandidate.ReadValidated(scenario.Input, scenario.Admission).CoverageUnionComplete, Is.True);
        var selected = scenario.Input with { IsolatedOwners = [scenario.Input.IsolatedOwners[0]] };
        Assert.That(CoverageBaselineCandidate.ReadValidatedSelection(selected, scenario.Admission, [CanonicalId]).AllRequiredOwnersAdmitted, Is.True);
        var replacement = selected with { IsolatedOwners = [scenario.Input.IsolatedOwners[1]] };
        var before = CaptureBaselineBytes(scenario.Admission);
        var error = Assert.Throws<CoverageBaselineCandidateException>(() =>
            CoverageBaselineCandidate.ReadValidatedSelection(replacement, scenario.Admission, [CanonicalId]));
        Assert.That(error!.Code, Is.EqualTo("coverage-baseline-candidate-unselected-owner"));
        AssertBaselineBytes(before);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-shard-core/reject-split-current-ordered-profile")]
    public void CurrentOrderedProfileCannotBeReducedToOneSelectedMember()
    {
        var scenario = PrepareShardCoreGroup();
        Assert.That(CoverageBaselineCandidate.ReadValidated(scenario.Input, scenario.Admission).CoverageUnionComplete, Is.True);
        var selected = scenario.Input with { IsolatedOwners = [] };
        Assert.That(CoverageBaselineCandidate.ReadValidatedSelection(selected, scenario.Admission, [CanonicalId, "session/other"]).AllRequiredOwnersAdmitted, Is.True);
        var before = CaptureBaselineBytes(scenario.Admission);
        var error = Assert.Throws<CoverageBaselineCandidateException>(() =>
            CoverageBaselineCandidate.ReadValidatedSelection(selected, scenario.Admission, [CanonicalId]));
        Assert.That(error!.Code, Is.EqualTo("coverage-baseline-candidate-selection-profile-split"));
        AssertBaselineBytes(before);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-shard-core/whole-group-keeps-one-shared-aggregate")]
    public void SelectedWholeProfileRetainsItsOneRawGroupObservation()
    {
        var scenario = PrepareShardCoreGroup();
        Assert.That(CoverageBaselineCandidate.ReadValidated(scenario.Input, scenario.Admission).CoverageUnionComplete, Is.True);
        var selected = scenario.Input with { IsolatedOwners = [] };
        var before = CaptureBaselineBytes(scenario.Admission);
        var result = CoverageBaselineCandidate.ReadValidatedSelection(selected, scenario.Admission, [CanonicalId, "session/other"]);
        Assert.That(result.CoverageUnionComplete, Is.False);
        Assert.That(result.IsolatedOwners, Is.Empty);
        Assert.That(result.OrderedGroups, Has.Count.EqualTo(1));
        var group = result.OrderedGroups.Single().Coverage;
        Assert.That(group.Members.Select(item => item.Case.CanonicalId), Is.EqualTo(new[] { CanonicalId, "session/other" }));
        Assert.That(group.Aggregate.ReportSha256, Is.EqualTo(Artifact(CoverageArtifactKind.AggregateJson).Sha256));
        Assert.That(group.IndividualCaseCoverage || group.CompleteBaseline || group.DependencyClosureProven, Is.False);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(group));
        Assert.That(document.RootElement.GetProperty("Members").EnumerateArray().All(member => !member.TryGetProperty("Aggregate", out _)), Is.True);
        AssertBaselineBytes(before);
    }

    private (CoverageBaselineCandidateInput Input, CoverageBaselineAdmission Admission) PrepareShardCoreGroup()
    {
        var scenario = PrepareBaselineScenario();
        var group = PrepareBaselineGroup();
        var catalog = FreshBaselineCatalog("first", this, "fresh-1");
        return (scenario.Input with { IsolatedOwners = [scenario.Input.IsolatedOwners[1]], OrderedGroups = [group] },
            scenario.Admission with { Catalogs = [catalog, scenario.Admission.Catalogs[1]] });
    }
}
