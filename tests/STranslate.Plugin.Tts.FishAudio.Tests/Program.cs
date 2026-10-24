if (args.Contains("--numeric-typography-tests"))
{
    await NumericGlyphObservationSuite.EquivalentNativeRunGroupingAsync();
    await NumericTypographySuite.RunAsync(args.ElementAtOrDefault(Array.IndexOf(args, "--numeric-typography-tests") + 1));
    Console.WriteLine("Numeric typography regressions passed.");
    return;
}

if (args.Contains("--numeric-glyph-observation-tests"))
{
    await NumericGlyphObservationSuite.EquivalentNativeRunGroupingAsync();
    Console.WriteLine("Numeric glyph observation regressions passed.");
    return;
}

if (args.Contains("--search-adjustment-diagnostic-tests"))
{
    await SearchAdjustmentControlSuite.MatrixDiagnosticsPreserveFailuresWithoutRetryAsync();
    Console.WriteLine("Search adjustment deadline diagnostic regressions passed.");
    return;
}

if (args.Contains("--streaming-cancellation-probe-tests"))
{
    await StreamingEntrySuite.CancellationProbeObservesSynchronousRetirementAsync();
    await StreamingEntrySuite.CancellationProbeRetainsOnlyActiveObservationAsync();
    await StreamingEntrySuite.CallerAndDisposalCancelOwnedSessionsAsync();
    Console.WriteLine("Streaming cancellation probe regressions passed.");
    return;
}

if (args.Contains("--search-adjustment-control-tests"))
{
    await SearchAdjustmentControlSuite.MatrixDiagnosticsPreserveFailuresWithoutRetryAsync();
    await SearchAdjustmentControlSuite.RunAllAsync(args.ElementAtOrDefault(Array.IndexOf(args, "--search-adjustment-control-tests") + 1));
    Console.WriteLine("Search adjustment control tests passed.");
    return;
}

if (args.Contains("--search-adjustment-request-tests"))
{
    await SearchAdjustmentSuite.RunRequestsAsync();
    Console.WriteLine("Search adjustment request tests passed.");
    return;
}

if (args.Contains("--by-id-error-tests"))
{
    await VoiceDiscoverySuite.ByIdTimerCreationCannotOutliveConcurrentRetirementAsync();
    await VoiceDiscoverySuite.ReentrantByIdTimerCreationCannotReplaceItsSuccessorAsync();
    await VoiceDiscoverySuite.ByIdErrorsExpireAtFiveSecondsAsync();
    await VoiceDiscoverySuite.IdenticalByIdErrorsRestartTheirLifetimeAsync();
    await VoiceDiscoverySuite.EditsAndNewByIdLookupsClearErrorsImmediatelyAsync();
    await VoiceDiscoverySuite.DeferredByIdExpiryRechecksOccurrenceAndDisposalAsync();
    await VoiceDiscoverySuite.ByIdDisposalRetiresTimerAndLateResponsesAsync();
    await VoiceDiscoverySuite.VoiceLookupRequestsCancelPreviousAndDisposeWorkAsync();
    await VoiceDiscoverySuite.VoiceLookupCompletionsAfterDisposeDoNotMutateStateAsync();
    await VoiceDiscoverySuite.DisposeRestoresDiscoveryBusyStateBeforeCancelCallbacksAsync();
    Console.WriteLine("By-ID error lifecycle tests passed.");
    return;
}

if (args.Contains("--page-entry-tests"))
{
    await PageEntrySuite.RunAllAsync(args.ElementAtOrDefault(Array.IndexOf(args, "--page-entry-tests") + 1));
    Console.WriteLine("Page entry tests passed.");
    return;
}

if (args.Contains("--voice-language-tags-tests"))
{
    BuildScriptSuite.PackageMissingFlagIconsNoticeFails();
    await VoiceLanguageTagsSuite.RunAllAsync(args.ElementAtOrDefault(Array.IndexOf(args, "--voice-language-tags-tests") + 1));
    Console.WriteLine("Voice language tag tests passed.");
    return;
}

if (args.Contains("--voice-language-tests"))
{
    await VoiceLanguageSuite.RunAllAsync();
    Console.WriteLine("Voice language metadata tests passed.");
    return;
}

if (args.Contains("--ui-locale-contract-tests"))
{
    RepositoryContractSuite.UiLocalesHaveCompleteResourcesAndMetadata();
    RepositoryContractSuite.FreeModelDeadlineDocumentationIsConsistent();
    PresentationContractSuite.TranslatedReadmesMatchCurrentSourceAndControlNames();
    PresentationContractSuite.SettingsViewShowsPersistentCreditPlaceholder();
    PresentationContractSuite.LanguageResourcesMatchApiKeyRollback();
    PresentationContractSuite.PreviewFailureLanguageResourcesAreComplete();
    PresentationContractSuite.LanguageResourcesDescribeContextConditioningConsistency();
    StreamingSettingsSuite.StreamingSettingsUiResourcesAreComplete();
    Console.WriteLine("Seven UI locale contracts passed.");
    return;
}

if (args.Contains("--package-contract-tests"))
{
    BuildScriptSuite.PackageMissingRussianXamlFails();
    BuildScriptSuite.PackageMissingTurkishMetadataFails();
    BuildScriptSuite.PackageMissingAudioRuntimeDependencyFails();
    BuildScriptSuite.PackageMissingStreamingRuntimeDependencyFails();
    BuildScriptSuite.PackageMissingOcticonsNoticeFails();
    BuildScriptSuite.ReleaseSwitchSelectsReleaseWithoutDebugPackaging();
    Console.WriteLine("Package contract tests passed.");
    return;
}

if (args.Contains("--activity-dispatcher-tests"))
{
    await OperationActivityDispatcherSuite.DeadlineDiagnosticsPreserveOriginalFailureWithoutRetryAsync();
    await OperationActivityDispatcherSuite.PlaybackFailureAndCancellationRetireBoundCommandsOnUiThreadAsync();
    await OperationActivityDispatcherSuite.QueuedRetirementCannotUnlockNewUiOperationAsync();
    Console.WriteLine("Activity dispatcher regression tests passed.");
    return;
}

if (args.Contains("--repository-link-tests"))
{
    await ProjectRepositoryLinkSuite.RunAllAsync();
    Console.WriteLine("Project repository link tests passed.");
    return;
}

if (args.Contains("--credential-display-tests"))
{
    await CredentialDisplayPreservationSuite.AcceptedEditsPreserveCardsSnapshotsAndNavigationAsync();
    CredentialDisplayPreservationSuite.CredentialRoundTripRejectsQueuedPublications();
    await CredentialDisplayPreservationSuite.PendingMetadataCannotReplaceRetainedDisplayAsync();
    await CredentialDisplayPreservationSuite.ReinitializationDoesNotSeedNewCredentialCacheFromRetainedSnapshotAsync();
    await CredentialDisplayPreservationSuite.FailedSavesPreserveIdentitySubscriptionAndPendingWorkAsync();
    CredentialDisplayPreservationSuite.AcceptedPublicationOwnsIdentityAcrossCopiesAndReentrantSaves();
    CredentialDisplayPreservationSuite.SubscriptionRebindingAndDisposalKeepOnlyTheCurrentOwner();
    await CredentialDisplayPreservationSuite.RetainedPreviewEntryUsesResourceWithoutAuthAndRefreshesWithAcceptedKeyAsync();
    await CredentialDisplayPreservationSuite.StartupCredentialRoundTripAtSaveBoundaryCannotWriteAsync();
    Console.WriteLine("Credential display preservation tests passed.");
    return;
}

if (args.Contains("--streaming-output-tests"))
{
    await StreamingPlaybackSuite.RunAllAsync();
    Console.WriteLine("Streaming output regression tests passed.");
    return;
}

if (args.Contains("--voice-auth-tests"))
{
    VoiceCredentialScopeSuite.CacheWritesAndQueuedEventsKeepCapturedCredentialScope();
    await VoiceCredentialScopeSuite.RejectedCredentialSavesRetainAcceptedKeyAsync();
    VoiceCredentialScopeSuite.QueuedStartupApplyRechecksCredentialInsideDispatcher();
    await VoiceCredentialScopeSuite.FailedReplacementConstructionPreservesAcceptedCacheAsync();
    await VoiceCredentialScopeSuite.ReentrantPreviewMetadataEventCannotRestoreOldAccountAsync();
    await VoiceDiscoverySuite.SearchAndByIdUseConfiguredTokenOrDummyAsync();
    await VoiceDiscoverySuite.CredentialChangesPreserveDisplayAndInvalidatePendingSearchAsync();
    await FavoriteVoicesSuite.FavoriteRequestsUseCurrentCredentialAsync();
    await InitializationLifecycleSuite.StartupRefreshSelectedVoiceMetadataAsync();
    await InitializationLifecycleSuite.StartupMetadataRejectsChangedCredentialsAsync();
    await InitializationLifecycleSuite.StartupVoiceSaveFailureRollsBackRuntimeAndStorageAsync();
    await PreviewSuite.RunBoundedPreviewRegressionTestsAsync();
    await CoverImageCacheSuite.CoverImageCacheDownloadsThroughBoundedStreamAsync();
    Console.WriteLine("Voice authentication tests passed.");
    return;
}

if (args.Contains("--voice-card-ink-tests"))
{
    await VoiceCardInkSuite.RunAsync(args.ElementAtOrDefault(Array.IndexOf(args, "--voice-card-ink-tests") + 1));
    return;
}

if (args.Contains("--settings-layout-tests"))
{
    await ApiKeySettingsCardSuite.RunAllAsync();
    await SettingsLayoutSuite.CompiledViewAudioNetworkAndFoldInteractionsAsync();
    Console.WriteLine("Compiled settings layout tests passed.");
    return;
}

if (args.Contains("--settings-input-tests"))
{
    await SettingsInputInteractionSuite.RunAllAsync(args.ElementAtOrDefault(Array.IndexOf(args, "--settings-input-tests") + 1));
    Console.WriteLine("Settings input interaction tests passed.");
    return;
}

if (args.Contains("--settings-control-tests"))
{
    await SettingsControlAlignmentSuite.RunAllAsync();
    await FavoriteVoicesSuite.ByIdLookupRejectsWhitespaceWithoutRequestOrSelectionChangeAsync();
    Console.WriteLine("Settings control alignment tests passed.");
    return;
}

if (args.Contains("--inline-numeric-tests"))
{
    await SettingsLayoutSuite.CompactNumericGeometryAsync();
    await SettingsLayoutSuite.InlineNumericInteractionsAsync();
    Console.WriteLine("Inline numeric regression tests passed.");
    return;
}

await StreamingEntrySuite.FormalPlaybackUsesSessionWithoutWholeBufferHostAudioAsync();
await StreamingEntrySuite.SnapshotStaysImmutableWhileSettingsAreEditedAsync();
await StreamingEntrySuite.SupersessionWaitsForOutputCleanupAcrossThreeCallsAsync();
await StreamingEntrySuite.CallerAndDisposalCancelOwnedSessionsAsync();
await StreamingEntrySuite.CancellationProbeObservesSynchronousRetirementAsync();
await StreamingEntrySuite.CancellationProbeRetainsOnlyActiveObservationAsync();
await StreamingEntrySuite.TransportFailuresDoNotExposeCredentialsAsync();
StreamingSettingsSuite.MissingConfigurationLoadsPcm();
StreamingSettingsSuite.LegacyFormatsMigrateAndCurrentDefaultsUsePcm();
StreamingSettingsSuite.FutureAndDuplicateSchemasRemainReadOnly();
StreamingSettingsSuite.InvalidStreamingFieldTypesDefaultIndependently();
StreamingSettingsSuite.NewFieldsRoundTripAndSnapshotsPreserveAllSettings();
StreamingSettingsSuite.LeaseCommitAndRollbackPreserveStreamingSettings();
StreamingSettingsSuite.ModelOrderDefaultsAndSavedChoicesStayIndependent();
StreamingSettingsSuite.ViewModelFormatAndModelTogglesPreserveSettings();
StreamingSettingsSuite.StreamingSettingsUiResourcesAreComplete();
await StreamingPlaybackSuite.RunAllAsync();
await StreamingTextSuite.FirstWindowIsLazyAndCutsOrdinaryLongSentencesAsync();
await StreamingTextSuite.MarkupStaysOrderedAcrossWindowsWithoutDuplicateDirectionsAsync();
await StreamingTextSuite.ControlOnlyAndIncompleteInputNeverClaimsReadablePrefixAsync();
await StreamingTextSuite.BoundaryPriorityAndHugeTailDoNotChangeFirstPrefixAsync();
await StreamingSessionSuite.TailWaitsForSuccessfulPlaybackAndFinishWaitsForDrainAsync();
await StreamingSessionSuite.ShortInputStopsWithoutWaitingForPlaybackAsync();
await StreamingSessionSuite.ProtocolRejectsMalformedOversizeAndMissingFinishAsync();
await StreamingSessionSuite.FragmentedAudioUsesOwnedBoundedOrderedBlocksAsync();
await StreamingSessionSuite.ModelAndRequestSnapshotAreExactBeforeConnectAsync();
await StreamingSessionSuite.CancellationAndFaultsAbortEveryWorkerAsync();
await StreamingSessionSuite.ConnectFirstPcmAndIdleUseIndependentDeadlinesAsync();
await StreamingSessionSuite.IdleExcludesUnionOfActualQueueAndPcmPressureAsync();
await StreamingSessionSuite.FreeCutoffRejectsBeforeConnectAndAbortsLongSessionAsync();
await StreamingSessionSuite.ReceiverAndAudioFailuresKeepTheirCauseAndDisposeCancelsAsync();
await StreamingSessionSuite.CancellationCallbackCannotReplacePrimaryAudioFailureAsync();
ApiKeyCompatibilitySuite.ApiKeyFormatAcceptsLegacyAndPrefixedKeys();
ApiKeyCompatibilitySuite.ApiKeyFormatRejectsMalformedKeys();
ApiKeyCompatibilitySuite.VoiceIdFormatRemainsLegacyOnly();
await ApiKeyCompatibilitySuite.RequestPathsPreservePreflightAndBearerAsync();
RepositoryContractSuite.UiLocalesHaveCompleteResourcesAndMetadata();
RepositoryContractSuite.Release112MetadataIsComplete();
RepositoryContractSuite.Release111MetadataIsComplete();
RepositoryContractSuite.Release110MetadataIsComplete();
RepositoryContractSuite.PackageReferenceUsesSdk1015();
RepositoryContractSuite.ReleaseOutputIsIsolatedFromDebugArtifacts();
RepositoryContractSuite.ReleaseBuildUsesSingleReleaseSwitch();
BuildScriptSuite.PackageMissingRussianXamlFails();
BuildScriptSuite.PackageMissingTurkishMetadataFails();
BuildScriptSuite.ConfigurationParameterIsRejected();
BuildScriptSuite.ReleaseSwitchSelectsReleaseWithoutDebugPackaging();
BuildScriptSuite.PackageMissingAudioRuntimeDependencyFails();
BuildScriptSuite.PackageMissingOcticonsNoticeFails();
BuildScriptSuite.PackageMissingFlagIconsNoticeFails();
BuildScriptSuite.PackageMissingStreamingRuntimeDependencyFails();
BuildScriptSuite.OrdinaryBuildCleansEveryArtifactDirectoryBeforeBuilding();
BuildScriptSuite.CleanBuildCleansEveryArtifactDirectoryAfterBuilding();
BuildScriptSuite.CleanBuildCleansAfterFailedRegressionTests();
SettingsStorageSuite.NewSettingsSerializeCurrentSchemaFirst();
SettingsStorageSuite.Version100SettingsMigrate();
SettingsStorageSuite.Version101SettingsMigrate();
SettingsStorageSuite.Version102UnversionedSettingsMigrate();
SettingsStorageSuite.CurrentVoiceFieldsWinRegardlessOfPropertyOrder();
SettingsStorageSuite.InvalidCurrentVoiceFieldsFallBackToLegacy();
SettingsStorageSuite.WrongJsonTypeDefaultsOnlyThatField();
SettingsStorageSuite.WrongSchemaTypeDefaultsOnlySchemaField();
SettingsStorageSuite.UnknownAndDeletedPropertiesAreCleanedUp();
SettingsStorageSuite.NumericSettingsNormalizeRangesAndGranularity();
SettingsStorageSuite.OrdinaryDuplicateDefaultsOnlyThatField();
SettingsStorageSuite.DuplicateAndFutureSchemasAreReadOnly();
SettingsStorageSuite.CurrentSettingsRoundTrip();
SettingsStorageSuite.ProductionUsesSettingsStoreForContextStorage();
ArchitectureContractSuite.ApiKeyValidationStateWasRemoved();
ArchitectureContractSuite.MainDelegatesInitializationToLifecycleCoordinator();
ArchitectureContractSuite.SettingsViewModelSplitsPreviewAndCoverCacheResponsibilities();
ArchitectureContractSuite.SettingsViewModelDelegatesCreditRefreshWorkflow();
ArchitectureContractSuite.SettingsViewModelDelegatesVoiceDiscoveryWorkflow();
ArchitectureContractSuite.SettingsViewModelDelegatesPreviewRefreshWorkflow();
OperationActivitySuite.OperationActivityCounterLinearizesStateTransitions();
OperationActivitySuite.OperationActivityCounterPublishesOutsideGateAcrossThreads();
OperationActivitySuite.OperationActivityCounterRecoveryFailureLeavesPublisherReusable();
OperationActivitySuite.OperationActivityCounterPublishesPendingStateBeforeRethrowingPublicationFailure();
await OperationActivitySuite.OverlappingStartupManualAndTtsOperationsUnlockAfterFinalCompletionAsync();
OperationActivitySuite.ApiKeyOperationCounterHandlesPropertyChangingReentrancy();
await OperationActivitySuite.ApiKeyOperationCounterHandlesPropertyChangedReentrancyAsync();
await OperationActivityDispatcherSuite.DeadlineDiagnosticsPreserveOriginalFailureWithoutRetryAsync();
await OperationActivityDispatcherSuite.PlaybackFailureAndCancellationRetireBoundCommandsOnUiThreadAsync();
await OperationActivityDispatcherSuite.QueuedRetirementCannotUnlockNewUiOperationAsync();
SettingsStorageSuite.ApiKeyEditingPersistsImmediately();
await SettingsInputBindingSuite.ApiKeyPasswordInputPersistsBeforeFocusLeavesAsync();
await StartupCreditSuite.StartupCreditRefreshPreflightSkipsRequestAsync();
await StartupCreditSuite.StartupCreditRefreshRequestsOncePerInitializationAsync();
await StartupCreditSuite.CompletedStartupCreditRefreshAppliesWhenSettingsViewModelIsCreatedAsync();
await StartupCreditSuite.PendingStartupCreditRefreshLocksSettingsUntilCompletionAsync();
await StartupCreditSuite.FailedStartupCreditRefreshKeepsPlaceholderWithoutSnackbarAsync();
await StartupCreditSuite.NullStartupCreditKeepsNotLoadedStateAsync();
await StartupCreditSuite.StartupCreditRefreshIgnoresResultAfterApiKeyChangesAsync();
await StartupCreditSuite.ReinitializedStartupCreditRefreshIgnoresOldCycleAsync();
await StartupCreditSuite.ReinitializedStartupCreditRefreshRemainsActiveDuringReloadAsync();
await InitializationLifecycleSuite.ReinitializedSettingsViewUsesNewContextAndSettingsAsync();
await InitializationLifecycleSuite.ReinitializationRetiresOldViewModelWithoutSavingSharedBackingStoreAsync();
await InitializationLifecycleSuite.StartupSettingsSaveWinsBeforeFailedReplacementTransitionAsync();
InitializationLifecycleSuite.FailedSameInstanceNormalizationRestoresOldStateAndBackingStore();
await InitializationLifecycleSuite.SettingsViewModelCreationWaitsForReplacementCommitAsync();
InitializationLifecycleSuite.SameSettingsInstanceSeparatesOldAndNewViewModelWriteLeases();
await InitializationLifecycleSuite.FailedReinitializationRestoresOldViewModelWriteLeaseAsync();
InitializationLifecycleSuite.DisposeRetiresSettingsWriteLease();
await InitializationLifecycleSuite.OvertakenInitializationSkipsSettingsLoadAsync();
await InitializationLifecycleSuite.ReinitializationWaitsForInFlightViewModelSaveAsync();
await StartupCreditSuite.DisposedSettingsViewModelIgnoresStartupCreditResultAsync();
InitializationLifecycleSuite.ModelPolicyUsesCutoffDefaultsAndNormalizeLoudnessSupport();
RepositoryContractSuite.FreeModelDeadlineDocumentationIsConsistent();
InitializationLifecycleSuite.MainInitNormalizesStructuredSettingsWithoutClearingKeys();
await InitializationLifecycleSuite.StartupRefreshSelectedVoiceMetadataAsync();
await InitializationLifecycleSuite.StartupMetadataRejectsChangedCredentialsAsync();
await InitializationLifecycleSuite.StartupVoiceUiApplyRechecksExpectedVoiceIdAsync();
await InitializationLifecycleSuite.DelayedStartupVoiceRefreshDoesNotRestoreClearedVoiceAsync();
await InitializationLifecycleSuite.DelayedStartupVoiceRefreshDoesNotOverwriteNewSelectionAsync();
await InitializationLifecycleSuite.ConcurrentPaidModelSelectionStillPublishesPostCutoffPolicyAsync();
await InitializationLifecycleSuite.StartupModelSaveFailureRollsBackRuntimeAndStorageAsync();
await InitializationLifecycleSuite.StartupVoiceSaveFailureRollsBackRuntimeAndStorageAsync();
await StartupCreditSuite.PublishedReplacementInvalidatesOldStartupCreditImmediatelyAsync();
await InitializationLifecycleSuite.StartupModelNormalizationInvalidatesSpeculativeSettingsViewModelAsync();
await InitializationLifecycleSuite.CommittedStartupSettingsInvalidatesPostReservationViewModelCandidateAsync();
await InitializationLifecycleSuite.StartupVoiceRefreshInvalidatesSpeculativeSettingsViewModelAsync();
await InitializationLifecycleSuite.StartupSettingsSaveSkipsAfterNewInitializationRequestAsync();
await InitializationLifecycleSuite.InitializationLoadWaitsForAuthorizedStartupSettingsSaveAsync();
await InitializationLifecycleSuite.FailedReinitializationRestoresStartupSettingsSaveAuthorizationAsync();
await InitializationLifecycleSuite.StartupRefreshDisposeCancelsPendingWorkWithoutLoggingAsync();
await InitializationLifecycleSuite.StartupRefreshCancellationAfterResponseSkipsSideEffectsAsync();
await InitializationLifecycleSuite.ReinitializedStartupRefreshCannotMutateNewSettingsAsync();
ArchitectureContractSuite.ApplyAvailableModelsUsesUiThreadInvoker();
await TtsSuite.PlayAudioUsesOldInitializationWhileReinitLoadIsBlockedAsync();
await TtsSuite.PlayAudioUsesInitializationSnapshotAcrossReinitAsync();
await TtsSuite.PlayAudioPreflightStopsBeforeRequestForMissingNetworkAsync();
await TtsSuite.PlayAudioPreflightStopsBeforeRequestForEmptyAndMalformedKeysAsync();
await TtsSuite.PlayAudioWithFormattedKeyStreamsWithoutCreditValidationAsync();
await TtsSuite.PlayAudioTimeoutShowsLocalizedErrorAndLogsAsync();
await CreditRefreshSuite.ManualCreditRefreshUsesPreflightAndLocksApiKeyInputAsync();
await CreditRefreshSuite.ManualCreditRefreshSuccessShowsBalanceAndLatencyAsync();
await CreditRefreshSuite.ManualCreditRefreshTimeoutShowsLocalizedErrorAndLogsAsync();
await CreditRefreshSuite.SilentCreditRefreshPreflightAndTimeoutOnlyLogAsync();
await FavoriteVoicesSuite.ByIdLookupRejectsWhitespaceWithoutRequestOrSelectionChangeAsync();
FavoriteSettingsIntegrationSuite.IdFileTransactionsLeaveStreamingSettingsAndSnapshotsUntouched();
FavoriteSettingsIntegrationSuite.LegacySettingsAndFavoriteIdsRestartIndependently();
FavoriteVoicesSuite.FavoriteVoiceIdsRejectWhitespaceAndRetainCanonicalDeduplication();
await FavoriteVoicesSuite.ByIdLookupKeepsSelectedVoiceUntilApplyAsync();
FavoriteVoicesSuite.FavoriteMetadataMigratesBoundsAndRoundTrips();
FavoriteVoicesSuite.FavoritesSyncAcrossCardsPersistAndClampPages();
FavoriteVoicesSuite.FavoriteSavesRollbackAndRespectReadOnlyAndRetiredLeases();
await FavoriteVoicesSuite.ByIdReplacementAndInputEditsRejectLateResultsAsync();
await FavoriteVoicesSuite.FavoriteAndByIdPreviewRefreshesMetadataWithoutApplyingAsync();
await FavoriteVoicesSuite.UnfavoriteCancelsOriginatingPreviewAndRejectsLateWorkAsync();
FavoriteVoicesSuite.SharedCardsAndFavoriteResourcesArePackaged();
await FavoriteStorageCacheSuite.SamePathWritersKeepIdsAndCorruptFilesRemainUntouchedAsync();
await FavoriteStorageCacheSuite.LeaseRetirementLinearizesIdWritesWithoutBlockingSnapshotsAsync();
FavoriteStorageCacheSuite.MetadataCacheIsBoundedDetachedAndUsesAnIndependentClientTtl();
FavoriteStorageCacheSuite.BookmarkMutationsCannotRefreshOrReplaceRemoteMetadata();
await FavoriteStorageCacheSuite.FavoriteMissesLoadOnlyVisiblePageAndRejectRemovedCardResultsAsync();
await FavoriteStorageCacheSuite.PluginCacheIsSharedAcrossSearchByIdAndReinitializationAsync();
await FavoriteStorageCacheSuite.SearchAndFavoritesReuseExistingCoverCacheAndClearKeepsIdsAsync();
VoiceCredentialScopeSuite.CacheWritesAndQueuedEventsKeepCapturedCredentialScope();
await VoiceCredentialScopeSuite.RejectedCredentialSavesRetainAcceptedKeyAsync();
VoiceCredentialScopeSuite.QueuedStartupApplyRechecksCredentialInsideDispatcher();
await VoiceCredentialScopeSuite.FailedReplacementConstructionPreservesAcceptedCacheAsync();
await VoiceCredentialScopeSuite.ReentrantPreviewMetadataEventCannotRestoreOldAccountAsync();
await VoiceDiscoverySuite.SearchAndByIdUseConfiguredTokenOrDummyAsync();
await CredentialDisplayPreservationSuite.AcceptedEditsPreserveCardsSnapshotsAndNavigationAsync();
CredentialDisplayPreservationSuite.CredentialRoundTripRejectsQueuedPublications();
await CredentialDisplayPreservationSuite.PendingMetadataCannotReplaceRetainedDisplayAsync();
await CredentialDisplayPreservationSuite.ReinitializationDoesNotSeedNewCredentialCacheFromRetainedSnapshotAsync();
await CredentialDisplayPreservationSuite.FailedSavesPreserveIdentitySubscriptionAndPendingWorkAsync();
CredentialDisplayPreservationSuite.AcceptedPublicationOwnsIdentityAcrossCopiesAndReentrantSaves();
CredentialDisplayPreservationSuite.SubscriptionRebindingAndDisposalKeepOnlyTheCurrentOwner();
await CredentialDisplayPreservationSuite.RetainedPreviewEntryUsesResourceWithoutAuthAndRefreshesWithAcceptedKeyAsync();
await CredentialDisplayPreservationSuite.StartupCredentialRoundTripAtSaveBoundaryCannotWriteAsync();
await VoiceDiscoverySuite.CredentialChangesPreserveDisplayAndInvalidatePendingSearchAsync();
await FavoriteVoicesSuite.FavoriteRequestsUseCurrentCredentialAsync();
await VoiceDiscoverySuite.ByIdErrorsExpireAtFiveSecondsAsync();
await VoiceDiscoverySuite.ByIdTimerCreationCannotOutliveConcurrentRetirementAsync();
await VoiceDiscoverySuite.ReentrantByIdTimerCreationCannotReplaceItsSuccessorAsync();
await VoiceDiscoverySuite.IdenticalByIdErrorsRestartTheirLifetimeAsync();
await VoiceDiscoverySuite.EditsAndNewByIdLookupsClearErrorsImmediatelyAsync();
await VoiceDiscoverySuite.DeferredByIdExpiryRechecksOccurrenceAndDisposalAsync();
await VoiceDiscoverySuite.ByIdDisposalRetiresTimerAndLateResponsesAsync();
await VoiceDiscoverySuite.VoiceLookupRequestsUseTimeoutAndPreserveFailureSemanticsAsync();
await VoiceDiscoverySuite.VoiceLookupRequestsCancelPreviousAndDisposeWorkAsync();
await VoiceDiscoverySuite.VoiceLookupCompletionsAfterDisposeDoNotMutateStateAsync();
await VoiceDiscoverySuite.SearchPaginationUpdatesVisiblePageAfterSuccessOnlyAsync();
await VoiceDiscoverySuite.SearchPaginationHonorsServerWindowAsync();
VoiceDiscoverySuite.PaginationMetadataPreservesLegacyAndUnknownTotals();
await VoiceDiscoverySuite.SearchWindowTailAndExplicitHasMoreAsync();
await VoiceDiscoverySuite.SearchUnknownTotalsAndRawTypeFilteringAsync();
await VoiceDiscoverySuite.SearchCommittedQuerySurvivesEditsAndFailureAsync();
await VoiceDiscoverySuite.SearchEmptyPageStopsForwardNavigationAsync();
await VoiceDiscoverySuite.ByIdRejectsExplicitNonTtsModelsAsync();
await VoiceDiscoverySuite.SearchReplacementRejectsLatePaginationAsync();
await VoiceDiscoverySuite.SearchPublishesPaginationBeforeResolvingCoversAsync();
await VoiceDiscoverySuite.DisposeRestoresDiscoveryBusyStateBeforeCancelCallbacksAsync();
await FishAudioApiSuite.PostTtsRequestHonorsModelSpecificProsodyAndTimeoutAsync();
PreviewSuite.PreviewAudioUrlValidationAllowsOnlyFishAudioStorageHosts();
await PreviewSuite.PreviewDownloadUsesHostStreamWithoutCredentialsAsync();
await PreviewSuite.PreviewPublishesPreparationBeforeFirstAwaitAsync();
await PreviewSuite.PreviewPreparationTransitionsToRealPlaybackAsync();
await PreviewSuite.PreviewSameVoiceAcrossEntriesCancelsPreparationAsync();
await PreviewSuite.PreviewOldCompletionCannotClearNewPreparationAsync();
await PreviewSuite.PreviewPreparationFailureAndResetClearStateAsync();
await PreviewSuite.PreviewQueuedPublicationsReadCurrentPreparationOwnerAsync();
await PreviewSuite.RunBoundedPreviewRegressionTestsAsync();
PreviewSuite.PreviewAudioUrlExpirationRefreshesAtThirtySecondWindow();
PreviewSuite.PreviewAudioUrlExpirationTreatsMalformedSignaturesAsExpiring();
PreviewSuite.SelectedPreviewCacheUpdateRejectsChangedVoice();
PreviewSuite.PreviewAudioRejectsInvalidSearchUrlWithoutStartingPlayback();
await PreviewSuite.DisplayPreviewValidUrlSkipsDetailRefreshAsync();
await PreviewSuite.DisplayPreviewRefreshesCachedVoiceAndPlaysLatestUrlAsync();
await PreviewSuite.DisplayPreviewOfflinePreflightStopsBeforeRefreshAsync();
await PreviewSuite.DisplayPreviewRechecksNetworkBeforePlaybackAsync();
PreviewSuite.SearchPreviewOfflinePreflightStopsBeforePlayback();
await PreviewSuite.SearchPreviewUsesListUrlWithoutDetailRefresh();
await PreviewSuite.SearchPreviewExpiredUrlRefreshesVisibleMetadataAsync();
await PreviewSuite.SearchPreviewExpiredRefreshFailureDoesNotFallbackAsync();
await PreviewSuite.SearchPreviewRemovedResultIgnoresLateRefreshAsync();
await PreviewSuite.SearchPreviewSecondClickCancelsPendingRefreshAsync();
await PreviewSuite.SearchPreviewSecondClickStopsWithoutNetwork();
await PreviewSuite.DisplayPreviewExpiredRefreshFailureShowsErrorAsync();
await PreviewSuite.DisplayPreviewNotFoundShowsRefreshErrorAsync();
await PreviewSuite.DisplayPreviewLatestVoiceWithoutSampleShowsUnavailableAsync();
await PreviewSuite.DisplayPreviewRefreshReturningExpiredUrlDoesNotPlayAsync();
await PreviewSuite.DisplayPreviewSecondClickStopsWithoutNetworkAsync();
await PreviewSuite.DisplayPreviewSecondClickCancelsPendingRefreshWithoutRestartAsync();
await PreviewSuite.PreviewPlaybackFailureUsesNetworkAwareMessage();
await PreviewSuite.SearchPreviewInvalidatesPendingDisplayRefreshAsync();
await PreviewSuite.DisplayPreviewVoiceSwitchIgnoresLateRefreshAsync();
await PreviewSuite.DisplayPreviewDisposeCancelsAndIgnoresLateRefreshAsync();
PresentationContractSuite.PromoStatePersistsDismissalAndUse();
PresentationContractSuite.SettingsViewRemovesApiKeyValidationUiAndUsesLatencyBars();
PresentationContractSuite.SettingsViewShowsPersistentCreditPlaceholder();
PresentationContractSuite.SettingsViewIncludesS21PromoAndDynamicModelDescriptions();
PresentationContractSuite.LanguageResourcesMatchApiKeyRollback();
PresentationContractSuite.PreviewFailureLanguageResourcesAreComplete();
PresentationContractSuite.LanguageResourcesDescribeContextConditioningConsistency();
PresentationContractSuite.TranslatedReadmesMatchCurrentSourceAndControlNames();
CoverImageCacheSuite.CoverImageCacheUsesExistingFile();
await CoverImageCacheSuite.CoverImageCacheCreatesMissedFileAsync();
await CoverImageCacheSuite.CoverImageCacheRejectsInvalidDownloadsAsync();
await CoverImageCacheSuite.CoverImageCacheCancelsSlowDownloadAfterTimeoutAsync();
await CoverImageCacheSuite.CoverImageCacheDownloadsThroughBoundedStreamAsync();
CoverImageCacheSuite.CoverImageCacheClearsOnlyCoverImagesAndFormatsSize();
CoverImageCacheSuite.CoverImageCacheSizeScansCoverImagesDirectory();
await CoverImageCacheSuite.ClearCoverImageCacheCommandTracksBusyStateAsync();
await CoverImageCacheSuite.ClearCoverImageCacheCommandTimesOutAndRestoresButtonAsync();
await CoverImageCacheSuite.LateClearCoverImageCacheTaskDoesNotUnlockNewOperationAsync();
CoverImageCacheSuite.ClearCacheButtonUsesLocalizedTextAndBusySpinner();

SystemProxyAndSelectedFavoritesSuite.RetiredProxyInputsAreDiscardedAndSocketUsesSystemProxy();
await SystemProxyAndSelectedFavoritesSuite.RetiredProxyInputsCannotBlockFormalReadingAsync();
SystemProxyAndSelectedFavoritesSuite.SelectedFavoriteTracksApplyClearBrowseMutationsAndFailedWrites();
VoiceSelectionToggleSuite.MatchingCardsToggleSelectionWithoutChangingFavoriteIds();
VoiceSelectionToggleSuite.RemovedOriginsCannotApplyOrClearMatchingCurrentCards();
VoiceSelectionToggleSuite.FailedSelectionWritesKeepAppliedIdentityAndStars();
await VoiceSelectionToggleSuite.MatchingByIdQueriesInspectWithoutCancelingSelectionAsync();

TopPCompatibilitySuite.ClientFloorKeepsDisplayedPrecisionDefaultsAndTemperatureZero();
TopPCompatibilitySuite.OldZeroCanonicallyRewritesOnceAndSavedFloorRoundTrips();
TopPCompatibilitySuite.FutureAndDuplicateSchemasKeepTheirReadOnlyProtection();
await TopPCompatibilitySuite.RawSettingsCannotSendZeroThroughHttpOrNativePayloadsAsync();

await ApiKeySettingsCardSuite.RunAllAsync();
await SettingsLayoutSuite.CompiledViewAudioNetworkAndFoldInteractionsAsync();
await SettingsControlAlignmentSuite.RunAllAsync();
await VoiceCardInkSuite.RunAsync();
await NumericGlyphObservationSuite.EquivalentNativeRunGroupingAsync();
// Preserve the existing ink suite's first-use DPI characterization before
// the new numeric group explicitly warms actual DPI on compiled text views.
await SettingsLayoutSuite.CompactNumericGeometryAsync();

await SettingsInputInteractionSuite.RunAllAsync();
await ProjectRepositoryLinkSuite.RunAllAsync();
await VoiceLanguageSuite.RunAllAsync();
await VoiceLanguageTagsSuite.RunAllAsync();
await PageEntrySuite.RunAllAsync();
await SearchAdjustmentSuite.RunRequestsAsync();
await SearchAdjustmentControlSuite.MatrixDiagnosticsPreserveFailuresWithoutRetryAsync();
await SearchAdjustmentControlSuite.RunAllAsync();
Console.WriteLine("Fish Audio plugin regression tests passed.");
