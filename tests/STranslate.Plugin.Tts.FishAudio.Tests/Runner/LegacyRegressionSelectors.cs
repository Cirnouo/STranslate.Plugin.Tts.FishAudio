internal static class LegacyRegressionSelectors
{
    private static readonly IReadOnlyDictionary<string, bool> Selectors = new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        ["--execution-guard-tests"] = false,
        ["--native-key-state-tests"] = false,
        ["--favorite-order-tests"] = false,
        ["--favorite-compatibility-tests"] = false,
        ["--popup-direction-tests"] = true,
        ["--popup-heading-tests"] = false,
        ["--input-clear-compatibility-tests"] = false,
        ["--input-clear-control-tests"] = false,
        ["--input-clear-request-tests"] = false,
        ["--control-cursor-late-combo-tests"] = false,
        ["--release-documentation-tests"] = false,
        ["--control-cursor-lifecycle-tests"] = false,
        ["--control-cursor-tests"] = false,
        ["--voice-description-tests"] = true,
        ["--popup-proof-tests"] = true,
        ["--popup-lifetime-tests"] = false,
        ["--search-preview-retention-tests"] = false,
        ["--accepted-preview-continuity-tests"] = true,
        ["--preview-ownership-compatibility-tests"] = false,
        ["--numeric-bottom-tests"] = true,
        ["--numeric-bottom-lifecycle-tests"] = false,
        ["--numeric-typography-tests"] = true,
        ["--numeric-glyph-observation-tests"] = false,
        ["--search-adjustment-diagnostic-tests"] = false,
        ["--streaming-cancellation-probe-tests"] = false,
        ["--avatar-preview-tests"] = true,
        ["--voice-like-tests"] = false,
        ["--voice-card-layout-tests"] = true,
        ["--voice-creation-date-tests"] = false,
        ["--search-adjustment-control-tests"] = true,
        ["--search-adjustment-request-tests"] = false,
        ["--by-id-error-tests"] = false,
        ["--page-entry-tests"] = true,
        ["--voice-language-tags-tests"] = true,
        ["--voice-language-tests"] = false,
        ["--ui-locale-contract-tests"] = false,
        ["--package-contract-tests"] = false,
        ["--activity-dispatcher-tests"] = false,
        ["--repository-link-tests"] = false,
        ["--credential-display-tests"] = false,
        ["--streaming-output-tests"] = false,
        ["--voice-auth-tests"] = false,
        ["--voice-card-ink-tests"] = true,
        ["--settings-layout-tests"] = false,
        ["--settings-input-tests"] = true,
        ["--numeric-tooltip-diagnostics-tests"] = false,
        ["--settings-control-tests"] = false,
        ["--inline-numeric-tests"] = false,
        ["--runner-contract-tests"] = false,
        ["--build-selection-tests"] = false,
    };

    private static readonly IReadOnlyDictionary<string, string[]> Filters = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["--accepted-preview-continuity-tests"] = ["selection", "unlike", "browsing", "handoff", "credentials", "lifecycle", "lease"],
        ["--page-entry-tests"] = ["fields", "search", "favorites"],
        ["--search-adjustment-control-tests"] = ["geometry", "intent", "drafts", "proof", "lifetime", "material", "radio", "edges", "titles"],
        ["--settings-input-tests"] = ["tabs", "baseline", "wheel", "tooltip", "precision", "favorites", "copy", "fold-focus", "zero", "hover", "point"],
    };

    internal static bool ContainsSelector(string[] args) => args.Any(Selectors.ContainsKey);

    internal static bool AreArgumentsValid(string[] args)
    {
        if (args.Length == 0 || !Selectors.TryGetValue(args[0], out var allowsValue)
            || args.Length > (allowsValue ? 2 : 1)
            || (args.Length == 2 && (string.IsNullOrWhiteSpace(args[1]) || args[1].StartsWith("--", StringComparison.Ordinal)))
            || (args[0] == "--popup-proof-tests" && args.Length != 2))
            return false;
        return args.Length != 2 || !Filters.TryGetValue(args[0], out var filters)
            || filters.Contains(args[1], StringComparer.Ordinal);
    }

    // Null means that the new metadata selector owns the command line.
    internal static async Task<int?> TryRunAsync(string[] args)
    {
        if (!args.Any(Selectors.ContainsKey)) return null;
        if (!AreArgumentsValid(args))
        {
            Console.Error.WriteLine("Invalid legacy regression selector arguments.");
            return 2;
        }
        var selector = args[0];

        if (selector == "--execution-guard-tests")
        {
            await RegressionExecutionSuite.RunAsync();
            Console.WriteLine("Regression execution guard contracts passed.");
            return 0;
        }

        if (selector == "--native-key-state-tests")
        {
            await NativeKeyboardStateSuite.RunAsync();
            Console.WriteLine("Native keyboard state fixture regressions passed.");
            return 0;
        }

        if (selector == "--favorite-order-tests")
        {
            FavoriteVoicesSuite.NewLikesAndRelikesDisplayFirstWithoutRewritingDurableOrder();
            FavoriteVoicesSuite.LegacyFavoritesReverseGloballyAndAddingKeepsTheCurrentPage();
            Console.WriteLine("Newest-liked-first facade regressions passed.");
            return 0;
        }

        if (selector == "--favorite-compatibility-tests")
        {
            FavoriteSettingsIntegrationSuite.IdFileTransactionsLeaveStreamingSettingsAndSnapshotsUntouched();
            FavoriteSettingsIntegrationSuite.LegacySettingsAndFavoriteIdsRestartIndependently();
            FavoriteVoicesSuite.FavoriteVoiceIdsRejectWhitespaceAndRetainCanonicalDeduplication();
            await FavoriteVoicesSuite.ByIdLookupRejectsWhitespaceWithoutRequestOrSelectionChangeAsync();
            await FavoriteVoicesSuite.ByIdLookupKeepsSelectedVoiceUntilApplyAsync();
            FavoriteVoicesSuite.FavoriteMetadataMigratesBoundsAndRoundTrips();
            FavoriteVoicesSuite.FavoritesSyncAcrossCardsPersistAndClampPages();
            FavoriteVoicesSuite.FavoriteSavesRollbackAndRespectReadOnlyAndRetiredLeases();
            await FavoriteVoicesSuite.ByIdReplacementAndInputEditsRejectLateResultsAsync();
            await FavoriteVoicesSuite.FavoriteAndByIdPreviewRefreshesMetadataWithoutApplyingAsync();
            await FavoriteVoicesSuite.UnfavoriteCancelsOriginatingPreviewAndRejectsLateWorkAsync();
            await FavoriteVoicesSuite.FavoriteRequestsUseCurrentCredentialAsync();
            await FavoriteStorageCacheSuite.SamePathWritersKeepIdsAndCorruptFilesRemainUntouchedAsync();
            await FavoriteStorageCacheSuite.LeaseRetirementLinearizesIdWritesWithoutBlockingSnapshotsAsync();
            FavoriteStorageCacheSuite.MetadataCacheIsBoundedDetachedAndUsesAnIndependentClientTtl();
            FavoriteStorageCacheSuite.BookmarkMutationsCannotRefreshOrReplaceRemoteMetadata();
            await FavoriteStorageCacheSuite.FavoriteMissesLoadOnlyVisiblePageAndRejectRemovedCardResultsAsync();
            await FavoriteStorageCacheSuite.PluginCacheIsSharedAcrossSearchByIdAndReinitializationAsync();
            await FavoriteStorageCacheSuite.SearchAndFavoritesReuseExistingCoverCacheAndClearKeepsIdsAsync();
            Console.WriteLine("Favorite storage, cache, ownership and credential compatibility passed.");
            return 0;
        }

        if (selector == "--popup-direction-tests")
        {
            await PopupDirectionSuite.RunAsync(args.ElementAtOrDefault(Array.IndexOf(args, "--popup-direction-tests") + 1));
            return 0;
        }

        if (selector == "--popup-heading-tests")
        {
            await PopupHeadingSuite.RunAsync();
            Console.WriteLine("Popup native-heading regressions passed.");
            return 0;
        }

        if (selector == "--input-clear-compatibility-tests")
        {
            await ApiKeySettingsCardSuite.RunAllAsync();
            await StaTestHost.RunOnStaThreadAsync(SettingsLayoutSuite.PasteButtonsReserveInputSpace);
            Console.WriteLine("Input clear native peer compatibility passed.");
            return 0;
        }

        if (selector == "--input-clear-control-tests")
        {
            await InputClearControlSuite.RunAsync();
            Console.WriteLine("Input clear compiled-control regressions passed.");
            return 0;
        }

        if (selector == "--input-clear-request-tests")
        {
            await InputClearSuite.RunRequestsAsync();
            Console.WriteLine("Input clear request regressions passed.");
            return 0;
        }

        if (selector == "--control-cursor-late-combo-tests")
        {
            await ControlCursorSuite.RunLateComboAsync();
            Console.WriteLine("Late-realized native Bitrate combo cursor tests passed.");
            return 0;
        }

        if (selector == "--release-documentation-tests")
        {
            RepositoryContractSuite.Release112MetadataIsComplete();
            RepositoryContractSuite.Release111MetadataIsComplete();
            RepositoryContractSuite.Release110MetadataIsComplete();
            Console.WriteLine("Published release documentation contracts passed.");
            return 0;
        }

        if (selector == "--control-cursor-lifecycle-tests")
        {
            await ControlCursorSuite.RunLifecycleAsync();
            Console.WriteLine("Control cursor local-reload and foreign-removal tests passed.");
            return 0;
        }

        if (selector == "--control-cursor-tests")
        {
            await ControlCursorSuite.RunAsync();
            Console.WriteLine("Control cursor tests passed.");
            return 0;
        }

        if (selector == "--voice-description-tests")
        {
            await VoiceDescriptionSuite.RunAsync(args.ElementAtOrDefault(Array.IndexOf(args, "--voice-description-tests") + 1));
            Console.WriteLine("Voice description tests passed.");
            return 0;
        }

        if (selector == "--popup-proof-tests")
        {
            var directory = args.ElementAtOrDefault(Array.IndexOf(args, "--popup-proof-tests") + 1)
                ?? throw new ArgumentException("A private proof directory is required");
            await SearchAdjustmentControlSuite.RunAllAsync("proof", directory);
            await VoiceLanguageTagsSuite.RunProofsAsync(directory);
            Console.WriteLine("Representative popup proofs passed.");
            return 0;
        }

        if (selector == "--popup-lifetime-tests")
        {
            await SearchAdjustmentControlSuite.RunAllAsync("lifetime");
            await VoiceLanguageTagsSuite.RunLifetimeAsync();
            Console.WriteLine("Popup lifetime regressions passed.");
            return 0;
        }

        if (selector == "--search-preview-retention-tests")
        {
            await SearchPreviewRetentionSuite.RunAllAsync();
            Console.WriteLine("Search preview retention regressions passed.");
            return 0;
        }

        if (selector == "--accepted-preview-continuity-tests")
        {
            await AcceptedPreviewContinuitySuite.RunAllAsync(args.ElementAtOrDefault(Array.IndexOf(args, "--accepted-preview-continuity-tests") + 1));
            Console.WriteLine("Accepted preview continuity regressions passed.");
            return 0;
        }

        if (selector == "--preview-ownership-compatibility-tests")
        {
            await PreviewSuite.PreviewPublishesPreparationBeforeFirstAwaitAsync();
            await PreviewSuite.PreviewPreparationTransitionsToRealPlaybackAsync();
            await PreviewSuite.PreviewSameVoiceAcrossEntriesCancelsPreparationAsync();
            await PreviewSuite.PreviewOldCompletionCannotClearNewPreparationAsync();
            await PreviewSuite.PreviewPreparationFailureAndResetClearStateAsync();
            await PreviewSuite.PreviewQueuedPublicationsReadCurrentPreparationOwnerAsync();
            await PreviewSuite.RunBoundedPreviewRegressionTestsAsync();
            await PreviewSuite.DisplayPreviewValidUrlSkipsDetailRefreshAsync();
            await PreviewSuite.DisplayPreviewRefreshesCachedVoiceAndPlaysLatestUrlAsync();
            await PreviewSuite.DisplayPreviewOfflinePreflightStopsBeforeRefreshAsync();
            await PreviewSuite.DisplayPreviewRechecksNetworkBeforePlaybackAsync();
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
            await VoiceDiscoverySuite.SearchPaginationUpdatesVisiblePageAfterSuccessOnlyAsync();
            await VoiceDiscoverySuite.SearchCommittedQuerySurvivesEditsAndFailureAsync();
            await VoiceDiscoverySuite.SearchReplacementRejectsLatePaginationAsync();
            await VoiceDiscoverySuite.SearchPublishesPaginationBeforeResolvingCoversAsync();
            await VoiceDiscoverySuite.VoiceLookupRequestsCancelPreviousAndDisposeWorkAsync();
            await VoiceDiscoverySuite.VoiceLookupCompletionsAfterDisposeDoNotMutateStateAsync();
            await FavoriteVoicesSuite.FavoriteAndByIdPreviewRefreshesMetadataWithoutApplyingAsync();
            await FavoriteVoicesSuite.UnfavoriteCancelsOriginatingPreviewAndRejectsLateWorkAsync();
            await SearchAdjustmentSuite.RunRequestsAsync();
            Console.WriteLine("Preview ownership compatibility regressions passed.");
            return 0;
        }

        if (selector == "--numeric-bottom-tests")
        {
            await NumericBottomSuite.RunAsync(args.ElementAtOrDefault(Array.IndexOf(args, "--numeric-bottom-tests") + 1));
            Console.WriteLine("Numeric visible-bottom regressions passed.");
            return 0;
        }

        if (selector == "--numeric-bottom-lifecycle-tests")
        {
            await NumericAlignmentLifecycleSuite.RunAsync();
            return 0;
        }

        if (selector == "--numeric-typography-tests")
        {
            await NumericGlyphObservationSuite.EquivalentNativeRunGroupingAsync();
            await NumericTypographySuite.RunAsync(args.ElementAtOrDefault(Array.IndexOf(args, "--numeric-typography-tests") + 1));
            Console.WriteLine("Numeric typography regressions passed.");
            return 0;
        }

        if (selector == "--numeric-glyph-observation-tests")
        {
            await NumericGlyphObservationSuite.EquivalentNativeRunGroupingAsync();
            Console.WriteLine("Numeric glyph observation regressions passed.");
            return 0;
        }

        if (selector == "--search-adjustment-diagnostic-tests")
        {
            await SearchAdjustmentControlSuite.MatrixDiagnosticsPreserveFailuresWithoutRetryAsync();
            await SearchAdjustmentControlSuite.ViewportDiagnosticsPreserveAsyncCompletionAndFailuresAsync();
            Console.WriteLine("Search adjustment deadline diagnostic regressions passed.");
            return 0;
        }

        if (selector == "--streaming-cancellation-probe-tests")
        {
            await StreamingEntrySuite.CancellationProbeObservesSynchronousRetirementAsync();
            await StreamingEntrySuite.CancellationProbeRetainsOnlyActiveObservationAsync();
            await StreamingEntrySuite.CallerAndDisposalCancelOwnedSessionsAsync();
            Console.WriteLine("Streaming cancellation probe regressions passed.");
            return 0;
        }

        if (selector == "--avatar-preview-tests")
        {
            await AvatarPreviewSuite.RunAllAsync(args.ElementAtOrDefault(Array.IndexOf(args, "--avatar-preview-tests") + 1));
            return 0;
        }

        if (selector == "--voice-like-tests")
        {
            await VoiceLikeSuite.RunAllAsync();
            Console.WriteLine("Voice Like presentation tests passed.");
            return 0;
        }

        if (selector == "--voice-card-layout-tests")
        {
            await VoiceCardLayoutSuite.RunAllAsync(args.ElementAtOrDefault(Array.IndexOf(args, "--voice-card-layout-tests") + 1));
            Console.WriteLine("Voice card layout tests passed.");
            return 0;
        }

        if (selector == "--voice-creation-date-tests")
        {
            await VoiceCreationDateSuite.RunAllAsync();
            Console.WriteLine("Voice creation date tests passed.");
            return 0;
        }

        if (selector == "--search-adjustment-control-tests")
        {
            await SearchAdjustmentControlSuite.MatrixDiagnosticsPreserveFailuresWithoutRetryAsync();
            await SearchAdjustmentControlSuite.RunAllAsync(args.ElementAtOrDefault(Array.IndexOf(args, "--search-adjustment-control-tests") + 1));
            Console.WriteLine("Search adjustment control tests passed.");
            return 0;
        }

        if (selector == "--search-adjustment-request-tests")
        {
            await SearchAdjustmentSuite.RunRequestsAsync();
            Console.WriteLine("Search adjustment request tests passed.");
            return 0;
        }

        if (selector == "--by-id-error-tests")
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
            return 0;
        }

        if (selector == "--page-entry-tests")
        {
            await PageEntrySuite.RunAllAsync(args.ElementAtOrDefault(Array.IndexOf(args, "--page-entry-tests") + 1));
            Console.WriteLine("Page entry tests passed.");
            return 0;
        }

        if (selector == "--voice-language-tags-tests")
        {
            BuildScriptSuite.PackageMissingFlagIconsNoticeFails();
            await VoiceLanguageTagsSuite.RunAllAsync(args.ElementAtOrDefault(Array.IndexOf(args, "--voice-language-tags-tests") + 1));
            Console.WriteLine("Voice language tag tests passed.");
            return 0;
        }

        if (selector == "--voice-language-tests")
        {
            await VoiceLanguageSuite.RunAllAsync();
            Console.WriteLine("Voice language metadata tests passed.");
            return 0;
        }

        if (selector == "--ui-locale-contract-tests")
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
            return 0;
        }

        if (selector == "--package-contract-tests")
        {
            BuildScriptSuite.PackageMissingRussianXamlFails();
            BuildScriptSuite.PackageMissingTurkishMetadataFails();
            BuildScriptSuite.PackageMissingAudioRuntimeDependencyFails();
            BuildScriptSuite.PackageMissingStreamingRuntimeDependencyFails();
            BuildScriptSuite.PackageMissingOcticonsNoticeFails();
            BuildScriptSuite.ReleaseSwitchSelectsReleaseWithoutDebugPackaging();
            Console.WriteLine("Package contract tests passed.");
            return 0;
        }

        if (selector == "--activity-dispatcher-tests")
        {
            await OperationActivityDispatcherSuite.DeadlineDiagnosticsPreserveOriginalFailureWithoutRetryAsync();
            await OperationActivityDispatcherSuite.PlaybackFailureAndCancellationRetireBoundCommandsOnUiThreadAsync();
            await OperationActivityDispatcherSuite.QueuedRetirementCannotUnlockNewUiOperationAsync();
            Console.WriteLine("Activity dispatcher regression tests passed.");
            return 0;
        }

        if (selector == "--repository-link-tests")
        {
            await ProjectRepositoryLinkSuite.RunAllAsync();
            Console.WriteLine("Project repository link tests passed.");
            return 0;
        }

        if (selector == "--credential-display-tests")
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
            return 0;
        }

        if (selector == "--streaming-output-tests")
        {
            await StreamingPlaybackSuite.RunAllAsync();
            Console.WriteLine("Streaming output regression tests passed.");
            return 0;
        }

        if (selector == "--voice-auth-tests")
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
            return 0;
        }

        if (selector == "--voice-card-ink-tests")
        {
            await VoiceCardInkSuite.RunAsync(args.ElementAtOrDefault(Array.IndexOf(args, "--voice-card-ink-tests") + 1));
            return 0;
        }

        if (selector == "--settings-layout-tests")
        {
            await ApiKeySettingsCardSuite.RunAllAsync();
            await SettingsLayoutSuite.CompiledViewAudioNetworkAndFoldInteractionsAsync();
            Console.WriteLine("Compiled settings layout tests passed.");
            return 0;
        }

        if (selector == "--settings-input-tests")
        {
            await SettingsInputInteractionSuite.RunAllAsync(args.ElementAtOrDefault(Array.IndexOf(args, "--settings-input-tests") + 1));
            Console.WriteLine("Settings input interaction tests passed.");
            return 0;
        }

        if (selector == "--numeric-tooltip-diagnostics-tests")
        {
            await SettingsInputInteractionSuite.NativeToolTipDiagnosticsPreserveFailuresWithoutRetryAsync();
            Console.WriteLine("Native numeric tooltip diagnostic contracts passed.");
            return 0;
        }

        if (selector == "--settings-control-tests")
        {
            await SettingsControlAlignmentSuite.RunAllAsync();
            await FavoriteVoicesSuite.ByIdLookupRejectsWhitespaceWithoutRequestOrSelectionChangeAsync();
            Console.WriteLine("Settings control alignment tests passed.");
            return 0;
        }

        if (selector == "--inline-numeric-tests")
        {
            await SettingsLayoutSuite.CompactNumericGeometryAsync();
            await SettingsLayoutSuite.InlineNumericInteractionsAsync();
            Console.WriteLine("Inline numeric regression tests passed.");
            return 0;
        }

        if (selector == "--runner-contract-tests")
        {
            await RunnerContractSuite.RunAsync();
            await RegressionCatalogSuite.RunAsync();
            Console.WriteLine("Regression runner contracts passed.");
            return 0;
        }
        if (selector == "--build-selection-tests")
        {
            BuildScriptSuite.TestSelectionArgumentsReachRunner();
            BuildScriptSuite.TestSelectionWithoutTestIsRejected();
            BuildScriptSuite.FailedSelectedTestsKeepExitCode();
            Console.WriteLine("Build regression selection contracts passed.");
            return 0;
        }
        throw new InvalidOperationException("Registered legacy regression selector has no dispatch.");
    }
}
