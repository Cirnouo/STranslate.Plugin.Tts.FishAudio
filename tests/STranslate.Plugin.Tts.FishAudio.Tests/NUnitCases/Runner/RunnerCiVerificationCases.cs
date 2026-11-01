using NUnit.Framework;

namespace NUnitCases.Runner;

[TestFixture]
[NonParallelizable]
public sealed class RunnerCiVerificationCases
{
    [Test]
    [Property("FishAudioCaseId", "infra.runner/ci-verification/evidence-snapshots-ignore-temporary-replacement-files")]
    public void EvidenceSnapshotsIgnoreTemporaryReplacementFiles() =>
        RegressionCiSuite.EvidenceSnapshotsIgnoreTemporaryReplacementFiles();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/ci-verification/cached-job-views-retain-their-actual-execution-owners")]
    public void CachedJobViewsRetainTheirActualExecutionOwners() =>
        RegressionCiSuite.CachedJobViewsRetainTheirActualExecutionOwners();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/ci-verification/unauthenticated-cached-views-cannot-borrow-old-success")]
    public void UnauthenticatedCachedViewsCannotBorrowOldSuccess() =>
        RegressionCiSuite.UnauthenticatedCachedViewsCannotBorrowOldSuccess();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/ci-verification/attempt-fences-must-describe-the-complete-history")]
    public void AttemptFencesMustDescribeTheCompleteHistory() =>
        RegressionCiSuite.AttemptFencesMustDescribeTheCompleteHistory();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/ci-verification/ambiguous-new-attempt-cannot-borrow-an-earlier-pass")]
    public void AmbiguousNewAttemptCannotBorrowAnEarlierPass() =>
        RegressionCiSuite.AmbiguousNewAttemptCannotBorrowAnEarlierPass();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/ci-verification/latest-missing-attempt-cannot-borrow-an-older-pass")]
    public void LatestMissingAttemptCannotBorrowAnOlderPass() =>
        RegressionCiSuite.LatestMissingAttemptCannotBorrowAnOlderPass();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/ci-verification/compact-reports-retain-exact-coverage-without-executing-delegates")]
    public void CompactReportsRetainExactCoverageWithoutExecutingDelegates() =>
        RegressionCiSuite.CompactReportsRetainExactCoverageWithoutExecutingDelegates();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/ci-verification/selective-recovery-uses-each-latest-attempt-and-preserves-old-evidence")]
    public void SelectiveRecoveryUsesEachLatestAttemptAndPreservesOldEvidence() =>
        RegressionCiSuite.SelectiveRecoveryUsesEachLatestAttemptAndPreservesOldEvidence();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/ci-verification/gate-only-rerun-retains-older-completed-siblings")]
    public void GateOnlyRerunRetainsOlderCompletedSiblings() =>
        RegressionCiSuite.GateOnlyRerunRetainsOlderCompletedSiblings();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/ci-verification/coverage-must-match-registry-rather-than-counts")]
    public void CoverageMustMatchRegistryRatherThanCounts() =>
        RegressionCiSuite.CoverageMustMatchRegistryRatherThanCounts();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/ci-verification/selected-reports-require-physical-identity-and-final-outcomes")]
    public void SelectedReportsRequirePhysicalIdentityAndFinalOutcomes() =>
        RegressionCiSuite.SelectedReportsRequirePhysicalIdentityAndFinalOutcomes();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/ci-verification/duplicate-identity-properties-cannot-use-last-value-wins")]
    public void DuplicateIdentityPropertiesCannotUseLastValueWins() =>
        RegressionCiSuite.DuplicateIdentityPropertiesCannotUseLastValueWins();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/ci-verification/parent-and-child-evidence-cannot-disagree")]
    public void ParentAndChildEvidenceCannotDisagree() =>
        RegressionCiSuite.ParentAndChildEvidenceCannotDisagree();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/ci-verification/job-and-artifact-owners-must-be-unambiguous")]
    public void JobAndArtifactOwnersMustBeUnambiguous() =>
        RegressionCiSuite.JobAndArtifactOwnersMustBeUnambiguous();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/ci-verification/cli-rejects-unknown-arguments-without-printing-private-values")]
    public Task CliRejectsUnknownArgumentsWithoutPrintingPrivateValuesAsync() =>
        RegressionCiSuite.CliRejectsUnknownArgumentsWithoutPrintingPrivateValuesAsync();

}
