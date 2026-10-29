internal static class RegressionCatalog
{
    // Registration is metadata-only: no fixtures, dispatchers, I/O or native warmup.
    // The sequence is the original Program dispatch order; filtering preserves it.
    internal static IReadOnlyList<RegressionCase> Create()
    {
        var cases = new List<RegressionCase>();
        Add("streaming.text/first-window-is-lazy-and-cuts-ordinary-long-sentences", "core", () => StreamingTextSuite.FirstWindowIsLazyAndCutsOrdinaryLongSentencesAsync());
        Add("lifecycle.activity/overlapping-startup-manual-and-tts-operations-unlock-after-final-completion", "core", () => OperationActivitySuite.OverlappingStartupManualAndTtsOperationsUnlockAfterFinalCompletionAsync());
        Add("lifecycle.activity-dispatcher/deadline-diagnostics-preserve-original-failure-without-retry", "core", () => OperationActivityDispatcherSuite.DeadlineDiagnosticsPreserveOriginalFailureWithoutRetryAsync());
        Add("cards.like/shared-card-template-is-compiled", "native-cards", () => VoiceLikeSuite.SharedCardTemplateIsCompiledAsync());
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
        Add("input.clear/run-requests", "core", () => InputClearSuite.RunRequestsAsync());
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
