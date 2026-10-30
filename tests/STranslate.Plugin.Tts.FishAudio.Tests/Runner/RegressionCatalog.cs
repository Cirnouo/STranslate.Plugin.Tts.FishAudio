internal static class RegressionCatalog
{
    // Registration is metadata-only: no fixtures, dispatchers, I/O or native warmup.
    // The sequence is the original Program dispatch order; filtering preserves it.
    internal static IReadOnlyList<RegressionCase> Create()
    {
        var cases = new List<RegressionCase>();
        Add("streaming.text/first-window-is-lazy-and-cuts-ordinary-long-sentences", "core", () => StreamingTextSuite.FirstWindowIsLazyAndCutsOrdinaryLongSentencesAsync());
        Add("cards.like/shared-card-template-is-compiled", "native-cards", () => VoiceLikeSuite.SharedCardTemplateIsCompiledAsync());
        AddCases(SettingsInputInteractionSuite.CreateCases(), "native-input");
        Add("repository.link/run-all", "native-input", () => ProjectRepositoryLinkSuite.RunAllAsync());
        AddCases(VoiceLanguageTagsSuite.CreateCases(), "native-popups");
        Add("input.page-entry/run-all", "native-input", () => PageEntrySuite.RunAllAsync());
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
