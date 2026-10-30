using NUnit.Framework;

namespace NUnitCases.Runner;

[TestFixture]
[NonParallelizable]
public sealed class RunnerSelectionCases
{
    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/snapshot-input-boundary")]
    public void Run() =>
        SnapshotContextSuite.Run();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/verifier-requires-complete-catalog")]
    public void VerifierRequiresCompleteCatalog() =>
        SnapshotContextSuite.VerifierRequiresCompleteCatalog();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/captures-the-owned-throw-and-caller-frames")]
    public void CapturesTheOwnedThrowAndCallerFrames() =>
        RegressionFailureEvidenceSuite.CapturesTheOwnedThrowAndCallerFrames();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/omits-framework-frames-and-bounds-owned-recursion")]
    public void OmitsFrameworkFramesAndBoundsOwnedRecursion() =>
        RegressionFailureEvidenceSuite.OmitsFrameworkFramesAndBoundsOwnedRecursion();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/serializes-only-approved-fields-without-private-exception-content")]
    public void SerializesOnlyApprovedFieldsWithoutPrivateExceptionContent() =>
        RegressionFailureEvidenceSuite.SerializesOnlyApprovedFieldsWithoutPrivateExceptionContent();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/retains-only-the-bounded-native-fixture-grammar")]
    public void RetainsOnlyTheBoundedNativeFixtureGrammar() =>
        RegressionFailureEvidenceSuite.RetainsOnlyTheBoundedNativeFixtureGrammar();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/handles-missing-physical-stack-without-using-remote-stack-text")]
    public void HandlesMissingPhysicalStackWithoutUsingRemoteStackText() =>
        RegressionFailureEvidenceSuite.HandlesMissingPhysicalStackWithoutUsingRemoteStackText();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/leaves-the-primary-exception-and-its-cleanup-evidence-unchanged")]
    public void LeavesThePrimaryExceptionAndItsCleanupEvidenceUnchanged() =>
        RegressionFailureEvidenceSuite.LeavesThePrimaryExceptionAndItsCleanupEvidenceUnchanged();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/sidecars-remain-secondary-to-the-original-outcome")]
    public Task SidecarsRemainSecondaryToTheOriginalOutcomeAsync() =>
        RegressionFailureEvidenceSuite.SidecarsRemainSecondaryToTheOriginalOutcomeAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/reader-drain-cannot-replace-the-writer-failure")]
    public Task ReaderDrainCannotReplaceTheWriterFailureAsync() =>
        RegressionReportSuite.ReaderDrainCannotReplaceTheWriterFailureAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/observed-reader-failure-cannot-authorize-writer")]
    public Task ObservedReaderFailureCannotAuthorizeWriterAsync() =>
        RegressionReportSuite.ObservedReaderFailureCannotAuthorizeWriterAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/terminal-readers-cannot-wait-for-unpublished-readiness")]
    public Task TerminalReadersCannotWaitForUnpublishedReadinessAsync() =>
        RegressionReportSuite.TerminalReadersCannotWaitForUnpublishedReadinessAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/reader-fixture-cleanup-preserves-primary-and-attempts-every-release")]
    public Task ReaderFixtureCleanupPreservesPrimaryAndAttemptsEveryReleaseAsync() =>
        RegressionReportSuite.ReaderFixtureCleanupPreservesPrimaryAndAttemptsEveryReleaseAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/reader-fixture-root-includes-setup-and-every-cleanup")]
    public Task ReaderFixtureRootIncludesSetupAndEveryCleanupAsync() =>
        RegressionReportSuite.ReaderFixtureRootIncludesSetupAndEveryCleanupAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/held-canonical-cannot-fail-durable-updates-or-repeat-actions")]
    public void HeldCanonicalCannotFailDurableUpdatesOrRepeatActions() =>
        RegressionReportSuite.HeldCanonicalCannotFailDurableUpdatesOrRepeatActions();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/initial-ownership-and-authoritative-failures-remain-strict")]
    public void InitialOwnershipAndAuthoritativeFailuresRemainStrict() =>
        RegressionReportSuite.InitialOwnershipAndAuthoritativeFailuresRemainStrict();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/concurrent-readers-cannot-block-immutable-updates")]
    public Task ConcurrentReadersCannotBlockImmutableUpdatesAsync() =>
        RegressionReportSuite.ConcurrentReadersCannotBlockImmutableUpdatesAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/stale-projection-replays-only-latest-incomplete-coverage")]
    public Task StaleProjectionReplaysOnlyLatestIncompleteCoverageAsync() =>
        RegressionReportSuite.StaleProjectionReplaysOnlyLatestIncompleteCoverageAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/canonical-and-latest-generation-tampering-cannot-authorize-reuse")]
    public void CanonicalAndLatestGenerationTamperingCannotAuthorizeReuse() =>
        RegressionReportSuite.CanonicalAndLatestGenerationTamperingCannotAuthorizeReuse();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/unreadable-or-missing-canonical-recovery-is-conservative")]
    public void UnreadableOrMissingCanonicalRecoveryIsConservative() =>
        RegressionReportSuite.UnreadableOrMissingCanonicalRecoveryIsConservative();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/bare-schema-one-reports-remain-compatible")]
    public void BareSchemaOneReportsRemainCompatible() =>
        RegressionReportSuite.BareSchemaOneReportsRemainCompatible();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/git-fixture-waits-for-the-same-process-beyond-five-seconds")]
    public Task GitFixtureWaitsForTheSameProcessBeyondFiveSecondsAsync() =>
        RunnerContractSuite.GitFixtureWaitsForTheSameProcessBeyondFiveSecondsAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/source-checkout-owner-preserves-failures-and-one-root")]
    public Task SourceCheckoutOwnerPreservesFailuresAndOneRootAsync() =>
        RunnerContractSuite.SourceCheckoutOwnerPreservesFailuresAndOneRootAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/native-owner-aliases-preserve-boundaries-and-recovery")]
    public Task NativeOwnerAliasesPreserveBoundariesAndRecoveryAsync() =>
        RunnerContractSuite.NativeOwnerAliasesPreserveBoundariesAndRecoveryAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/final-failure-summary-includes-every-failed-owner")]
    public Task FinalFailureSummaryIncludesEveryFailedOwnerAsync() =>
        RunnerContractSuite.FinalFailureSummaryIncludesEveryFailedOwnerAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/selection-unions-in-catalog-order-and-constrains-partitions")]
    public Task SelectionUnionsInCatalogOrderAndConstrainsPartitionsAsync() =>
        RunnerContractSuite.SelectionUnionsInCatalogOrderAndConstrainsPartitionsAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/invalid-and-empty-selections-cannot-execute")]
    public Task InvalidAndEmptySelectionsCannotExecuteAsync() =>
        RunnerContractSuite.InvalidAndEmptySelectionsCannotExecuteAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/listing-never-executes")]
    public Task ListingNeverExecutesAsync() =>
        RunnerContractSuite.ListingNeverExecutesAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/clean-source-identity-consistency-does-not-depend-on-caller-state")]
    public void CleanSourceIdentityConsistencyDoesNotDependOnCallerState() =>
        RunnerContractSuite.CleanSourceIdentityConsistencyDoesNotDependOnCallerState();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/failure-is-durable-and-replay-does-not-rewrite-it")]
    public Task FailureIsDurableAndReplayDoesNotRewriteItAsync() =>
        RunnerContractSuite.FailureIsDurableAndReplayDoesNotRewriteItAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/expired-fixture-entries-do-not-prevent-deletion")]
    public Task ExpiredFixtureEntriesDoNotPreventDeletionAsync() =>
        RunnerContractSuite.ExpiredFixtureEntriesDoNotPreventDeletionAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/fixture-cleanup-retains-its-primary-failure")]
    public Task FixtureCleanupRetainsItsPrimaryFailureAsync() =>
        RunnerContractSuite.FixtureCleanupRetainsItsPrimaryFailureAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/incremental-results-precede-delegates")]
    public Task IncrementalResultsPrecedeDelegatesAsync() =>
        RunnerContractSuite.IncrementalResultsPrecedeDelegatesAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/impact-selection-keeps-shared-and-unknown-dependencies-conservative")]
    public void ImpactSelectionKeepsSharedAndUnknownDependenciesConservative() =>
        RunnerContractSuite.ImpactSelectionKeepsSharedAndUnknownDependenciesConservative();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/leaf-impact-rules-avoid-unrelated-layout-and-explain-shared-expansion")]
    public void LeafImpactRulesAvoidUnrelatedLayoutAndExplainSharedExpansion() =>
        RunnerContractSuite.LeafImpactRulesAvoidUnrelatedLayoutAndExplainSharedExpansion();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/recovery-maps-clean-repairs-and-retains-reverted-dirty-scope")]
    public void RecoveryMapsCleanRepairsAndRetainsRevertedDirtyScope() =>
        RunnerContractSuite.RecoveryMapsCleanRepairsAndRetainsRevertedDirtyScope();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/provider-and-preview-leaf-rules-retain-direct-consumers")]
    public void ProviderAndPreviewLeafRulesRetainDirectConsumers() =>
        RunnerContractSuite.ProviderAndPreviewLeafRulesRetainDirectConsumers();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/selection/source-identity-and-affected-scope-ignore-foreign-working-directory")]
    public Task SourceIdentityAndAffectedScopeIgnoreForeignWorkingDirectoryAsync() =>
        RunnerContractSuite.SourceIdentityAndAffectedScopeIgnoreForeignWorkingDirectoryAsync();

}
