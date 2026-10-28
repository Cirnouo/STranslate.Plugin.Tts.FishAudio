internal static class RegressionCatalog
{
    // Registration is metadata-only: no fixtures, dispatchers, I/O or native warmup.
    // The sequence is the original Program dispatch order; filtering preserves it.
    internal static IReadOnlyList<RegressionCase> Create()
    {
        var cases = new List<RegressionCase>();
        Add("streaming.text/first-window-is-lazy-and-cuts-ordinary-long-sentences", "core", () => StreamingTextSuite.FirstWindowIsLazyAndCutsOrdinaryLongSentencesAsync());
        Add("lifecycle.activity/overlapping-startup-manual-and-tts-operations-unlock-after-final-completion", "core", () => OperationActivitySuite.OverlappingStartupManualAndTtsOperationsUnlockAfterFinalCompletionAsync());
        Add("runner.execution/run", "core", () => RegressionExecutionSuite.RunAsync());
        Add("lifecycle.activity-dispatcher/deadline-diagnostics-preserve-original-failure-without-retry", "core", () => OperationActivityDispatcherSuite.DeadlineDiagnosticsPreserveOriginalFailureWithoutRetryAsync());
        Add("lifecycle.activity-dispatcher/playback-failure-and-cancellation-retire-bound-commands-on-ui-thread", "native-settings", () => OperationActivityDispatcherSuite.PlaybackFailureAndCancellationRetireBoundCommandsOnUiThreadAsync());
        Add("lifecycle.activity-dispatcher/queued-retirement-cannot-unlock-new-ui-operation", "native-settings", () => OperationActivityDispatcherSuite.QueuedRetirementCannotUnlockNewUiOperationAsync());
        Add("settings.binding/api-key-password-input-persists-before-focus-leaves", "native-settings", () => SettingsInputBindingSuite.ApiKeyPasswordInputPersistsBeforeFocusLeavesAsync());
        Add("lifecycle.initialization/reinitialized-settings-view-uses-new-context-and-settings", "core", () => InitializationLifecycleSuite.ReinitializedSettingsViewUsesNewContextAndSettingsAsync());
        Add("lifecycle.initialization/reinitialization-retires-old-view-model-without-saving-shared-backing-store", "core", () => InitializationLifecycleSuite.ReinitializationRetiresOldViewModelWithoutSavingSharedBackingStoreAsync());
        Add("lifecycle.initialization/startup-settings-save-wins-before-failed-replacement-transition", "core", () => InitializationLifecycleSuite.StartupSettingsSaveWinsBeforeFailedReplacementTransitionAsync());
        AddSync("lifecycle.initialization/failed-same-instance-normalization-restores-old-state-and-backing-store", "core", () => InitializationLifecycleSuite.FailedSameInstanceNormalizationRestoresOldStateAndBackingStore());
        Add("lifecycle.initialization/settings-view-model-creation-waits-for-replacement-commit", "core", () => InitializationLifecycleSuite.SettingsViewModelCreationWaitsForReplacementCommitAsync());
        Add("lifecycle.initialization/failed-reinitialization-restores-old-view-model-write-lease", "core", () => InitializationLifecycleSuite.FailedReinitializationRestoresOldViewModelWriteLeaseAsync());
        AddSync("lifecycle.initialization/dispose-retires-settings-write-lease", "core", () => InitializationLifecycleSuite.DisposeRetiresSettingsWriteLease());
        Add("lifecycle.initialization/overtaken-initialization-skips-settings-load", "core", () => InitializationLifecycleSuite.OvertakenInitializationSkipsSettingsLoadAsync());
        Add("lifecycle.initialization/reinitialization-waits-for-in-flight-view-model-save", "core", () => InitializationLifecycleSuite.ReinitializationWaitsForInFlightViewModelSaveAsync());
        AddSync("lifecycle.initialization/model-policy-uses-cutoff-defaults-and-normalize-loudness-support", "core", () => InitializationLifecycleSuite.ModelPolicyUsesCutoffDefaultsAndNormalizeLoudnessSupport());
        AddSync("lifecycle.initialization/main-init-normalizes-structured-settings-without-clearing-keys", "core", () => InitializationLifecycleSuite.MainInitNormalizesStructuredSettingsWithoutClearingKeys());
        Add("lifecycle.initialization/startup-refresh-selected-voice-metadata", "core", () => InitializationLifecycleSuite.StartupRefreshSelectedVoiceMetadataAsync());
        Add("lifecycle.initialization/startup-voice-ui-apply-rechecks-expected-voice-id", "core", () => InitializationLifecycleSuite.StartupVoiceUiApplyRechecksExpectedVoiceIdAsync());
        Add("lifecycle.initialization/delayed-startup-voice-refresh-does-not-restore-cleared-voice", "core", () => InitializationLifecycleSuite.DelayedStartupVoiceRefreshDoesNotRestoreClearedVoiceAsync());
        Add("lifecycle.initialization/delayed-startup-voice-refresh-does-not-overwrite-new-selection", "core", () => InitializationLifecycleSuite.DelayedStartupVoiceRefreshDoesNotOverwriteNewSelectionAsync());
        Add("lifecycle.initialization/concurrent-paid-model-selection-still-publishes-post-cutoff-policy", "core", () => InitializationLifecycleSuite.ConcurrentPaidModelSelectionStillPublishesPostCutoffPolicyAsync());
        Add("lifecycle.initialization/startup-model-save-failure-rolls-back-runtime-and-storage", "core", () => InitializationLifecycleSuite.StartupModelSaveFailureRollsBackRuntimeAndStorageAsync());
        Add("lifecycle.initialization/startup-voice-save-failure-rolls-back-runtime-and-storage", "core", () => InitializationLifecycleSuite.StartupVoiceSaveFailureRollsBackRuntimeAndStorageAsync());
        Add("lifecycle.initialization/startup-model-normalization-invalidates-speculative-settings-view-model", "core", () => InitializationLifecycleSuite.StartupModelNormalizationInvalidatesSpeculativeSettingsViewModelAsync());
        Add("lifecycle.initialization/committed-startup-settings-invalidates-post-reservation-view-model-candidate", "core", () => InitializationLifecycleSuite.CommittedStartupSettingsInvalidatesPostReservationViewModelCandidateAsync());
        Add("lifecycle.initialization/startup-voice-refresh-invalidates-speculative-settings-view-model", "core", () => InitializationLifecycleSuite.StartupVoiceRefreshInvalidatesSpeculativeSettingsViewModelAsync());
        Add("lifecycle.initialization/startup-settings-save-skips-after-new-initialization-request", "core", () => InitializationLifecycleSuite.StartupSettingsSaveSkipsAfterNewInitializationRequestAsync());
        Add("lifecycle.initialization/initialization-load-waits-for-authorized-startup-settings-save", "core", () => InitializationLifecycleSuite.InitializationLoadWaitsForAuthorizedStartupSettingsSaveAsync());
        Add("lifecycle.initialization/failed-reinitialization-restores-startup-settings-save-authorization", "core", () => InitializationLifecycleSuite.FailedReinitializationRestoresStartupSettingsSaveAuthorizationAsync());
        Add("lifecycle.initialization/startup-refresh-dispose-cancels-pending-work-without-logging", "core", () => InitializationLifecycleSuite.StartupRefreshDisposeCancelsPendingWorkWithoutLoggingAsync());
        Add("lifecycle.initialization/startup-refresh-cancellation-after-response-skips-side-effects", "core", () => InitializationLifecycleSuite.StartupRefreshCancellationAfterResponseSkipsSideEffectsAsync());
        Add("lifecycle.initialization/reinitialized-startup-refresh-cannot-mutate-new-settings", "core", () => InitializationLifecycleSuite.ReinitializedStartupRefreshCannotMutateNewSettingsAsync());
        Add("streaming.tts/play-audio-uses-old-initialization-while-reinit-load-is-blocked", "core", () => TtsSuite.PlayAudioUsesOldInitializationWhileReinitLoadIsBlockedAsync());
        Add("streaming.tts/play-audio-uses-initialization-snapshot-across-reinit", "core", () => TtsSuite.PlayAudioUsesInitializationSnapshotAcrossReinitAsync());
        Add("cards.like/shared-card-template-is-compiled", "native-cards", () => VoiceLikeSuite.SharedCardTemplateIsCompiledAsync());
        Add("voice.favorite-cache/favorite-misses-load-only-visible-page-and-reject-removed-card-results", "core", () => FavoriteStorageCacheSuite.FavoriteMissesLoadOnlyVisiblePageAndRejectRemovedCardResultsAsync());
        Add("voice.discovery/voice-lookup-requests-cancel-previous-and-dispose-work", "core", () => VoiceDiscoverySuite.VoiceLookupRequestsCancelPreviousAndDisposeWorkAsync());
        Add("voice.discovery/voice-lookup-completions-after-dispose-do-not-mutate-state", "core", () => VoiceDiscoverySuite.VoiceLookupCompletionsAfterDisposeDoNotMutateStateAsync());
        Add("voice.discovery/dispose-restores-discovery-busy-state-before-cancel-callbacks", "core", () => VoiceDiscoverySuite.DisposeRestoresDiscoveryBusyStateBeforeCancelCallbacksAsync());
        Add("preview.ownership/preview-preparation-transitions-to-real-playback", "core", () => PreviewSuite.PreviewPreparationTransitionsToRealPlaybackAsync());
        Add("preview.ownership/preview-same-voice-across-entries-cancels-preparation", "core", () => PreviewSuite.PreviewSameVoiceAcrossEntriesCancelsPreparationAsync());
        Add("preview.ownership/preview-old-completion-cannot-clear-new-preparation", "core", () => PreviewSuite.PreviewOldCompletionCannotClearNewPreparationAsync());
        Add("preview.ownership/preview-preparation-failure-and-reset-clear-state", "core", () => PreviewSuite.PreviewPreparationFailureAndResetClearStateAsync());
        Add("preview.ownership/preview-queued-publications-read-current-preparation-owner", "core", () => PreviewSuite.PreviewQueuedPublicationsReadCurrentPreparationOwnerAsync());
        Add("preview.ownership/run-bounded-preview-regression-tests", "core", () => PreviewSuite.RunBoundedPreviewRegressionTestsAsync());
        Add("preview.ownership/search-preview-removed-result-ignores-late-refresh", "core", () => PreviewSuite.SearchPreviewRemovedResultIgnoresLateRefreshAsync());
        Add("preview.ownership/search-preview-second-click-cancels-pending-refresh", "core", () => PreviewSuite.SearchPreviewSecondClickCancelsPendingRefreshAsync());
        Add("preview.ownership/display-preview-second-click-cancels-pending-refresh-without-restart", "core", () => PreviewSuite.DisplayPreviewSecondClickCancelsPendingRefreshWithoutRestartAsync());
        Add("preview.ownership/search-preview-invalidates-pending-display-refresh", "core", () => PreviewSuite.SearchPreviewInvalidatesPendingDisplayRefreshAsync());
        Add("preview.ownership/display-preview-voice-switch-ignores-late-refresh", "core", () => PreviewSuite.DisplayPreviewVoiceSwitchIgnoresLateRefreshAsync());
        Add("preview.ownership/display-preview-dispose-cancels-and-ignores-late-refresh", "core", () => PreviewSuite.DisplayPreviewDisposeCancelsAndIgnoresLateRefreshAsync());
        Add("voice.cover-cache/cover-image-cache-creates-missed-file", "core", () => CoverImageCacheSuite.CoverImageCacheCreatesMissedFileAsync());
        Add("voice.cover-cache/cover-image-cache-rejects-invalid-downloads", "core", () => CoverImageCacheSuite.CoverImageCacheRejectsInvalidDownloadsAsync());
        Add("voice.cover-cache/cover-image-cache-cancels-slow-download-after-timeout", "core", () => CoverImageCacheSuite.CoverImageCacheCancelsSlowDownloadAfterTimeoutAsync());
        Add("voice.cover-cache/cover-image-cache-downloads-through-bounded-stream", "core", () => CoverImageCacheSuite.CoverImageCacheDownloadsThroughBoundedStreamAsync());
        Add("voice.cover-cache/clear-cover-image-cache-command-tracks-busy-state", "core", () => CoverImageCacheSuite.ClearCoverImageCacheCommandTracksBusyStateAsync());
        Add("voice.cover-cache/clear-cover-image-cache-command-times-out-and-restores-button", "core", () => CoverImageCacheSuite.ClearCoverImageCacheCommandTimesOutAndRestoresButtonAsync());
        Add("voice.cover-cache/late-clear-cover-image-cache-task-does-not-unlock-new-operation", "core", () => CoverImageCacheSuite.LateClearCoverImageCacheTaskDoesNotUnlockNewOperationAsync());
        Add("settings.api-key-card/run-all", "native-settings", () => ApiKeySettingsCardSuite.RunAllAsync());
        Add("settings.layout/compiled-view-audio-network-and-fold-interactions", "native-settings", () => SettingsLayoutSuite.CompiledViewAudioNetworkAndFoldInteractionsAsync());
        Add("settings.alignment/run-all", "native-settings", () => SettingsControlAlignmentSuite.RunAllAsync());
        AddCases(VoiceCardInkSuite.CreateCases(), "native-settings");
        Add("numeric.glyph/equivalent-native-run-grouping", "native-settings", () => NumericGlyphObservationSuite.EquivalentNativeRunGroupingAsync());
        AddCases(SettingsLayoutSuite.CreateCompactNumericCases(), "native-settings");
        AddCases(NumericBottomSuite.CreateCases(), "native-settings");
        Add("numeric.lifecycle/run", "native-settings", () => NumericAlignmentLifecycleSuite.RunAsync());
        Add("input.clear/run-requests", "core", () => InputClearSuite.RunRequestsAsync());
        Add("input.clear/run", "native-settings", () => InputClearControlSuite.RunAsync());
        Add("input.settings/native-tool-tip-diagnostics-preserve-failures-without-retry", "core", () => SettingsInputInteractionSuite.NativeToolTipDiagnosticsPreserveFailuresWithoutRetryAsync());
        AddCases(SettingsInputInteractionSuite.CreateCases(), "native-input");
        Add("repository.link/run-all", "native-input", () => ProjectRepositoryLinkSuite.RunAllAsync());
        AddCases(VoiceLanguageTagsSuite.CreateCases(), "native-popups");
        Add("input.page-entry/run-all", "native-input", () => PageEntrySuite.RunAllAsync());
        Add("voice.search-adjustment/run-requests", "core", () => SearchAdjustmentSuite.RunRequestsAsync());
        Add("popup.search-adjustment/matrix-diagnostics-preserve-failures-without-retry", "core", () => SearchAdjustmentControlSuite.MatrixDiagnosticsPreserveFailuresWithoutRetryAsync());
        Add("popup.search-adjustment/viewport-diagnostics-preserve-async-completion-and-failures", "core", () => SearchAdjustmentControlSuite.ViewportDiagnosticsPreserveAsyncCompletionAndFailuresAsync());
        AddCases(SearchAdjustmentControlSuite.CreateCases(), "native-popups");
        Add("popup.keyboard-state/run", "native-popups", () => NativeKeyboardStateSuite.RunAsync());
        Add("popup.heading/run", "native-popups", () => PopupHeadingSuite.RunAsync());
        Add("popup.direction/run", "native-popups", () => PopupDirectionSuite.RunAsync());
        foreach (var testCase in VoiceCardLayoutSuite.CreateCases())
            cases.Add(new RegressionCase(testCase.Id, "native-cards", testCase.Run));
        AddCases(VoiceDescriptionSuite.CreateCases(), "native-cards");
        AddCases(ControlCursorSuite.CreateCases(), "native-input");
        AddCases(VoiceLikeSuite.CreateCases(), "native-cards");
        AddCases(AvatarPreviewSuite.CreateCases(), "native-cards");
        AddCases(SearchPreviewRetentionSuite.CreateCases(), "native-cards");
        AddCases(AcceptedPreviewContinuitySuite.CreateCases(), "native-cards");
        Add("runner.contracts/selection", "core", RunnerContractSuite.RunAsync);
        Add("runner.contracts/catalog", "core", RegressionCatalogSuite.RunAsync);
        Add("runner.process/run-all", "core", RegressionProcessSuite.RunAsync);
        Add("runner.ci/verification", "core", RegressionCiSuite.RunAsync);
        Add("runner.ci/infrastructure", "core", RegressionCiInfrastructureSuite.RunAsync);
        return cases;

        void AddCases(IReadOnlyList<(string Id, Func<Task> Run)> descriptors, string partition)
        {
            foreach (var testCase in descriptors)
                Add(testCase.Id, partition, testCase.Run);
        }

        void Add(string id, string partition, Func<Task> run) => cases.Add(new RegressionCase(id, partition, run));

        void AddSync(string id, string partition, Action run) => Add(id, partition, () =>
        {
            run();
            return Task.CompletedTask;
        });
    }
}
