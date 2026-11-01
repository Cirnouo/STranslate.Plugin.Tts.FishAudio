using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

internal static partial class VoiceDescriptionSuite
{
    internal static Task ObserveConstructionAsync() => SearchAdjustmentControlSuite.ObserveMatrixActionAsync("construction", stage =>
            {
                stage("VM construction begin");
                var vm = new SettingsViewModel(ContextProxy.CreateContext(), new Settings());
                try
                {
                    stage("compiled view/native layout begin");
                    SettingsLayoutSuite.Layout(SettingsLayoutSuite.CreateThemedView(vm));
                    stage("compiled view/native layout completed");
                }
                finally
                {
                    stage("VM cleanup begin"); vm.Dispose(); stage("VM cleanup completed");
                }
            }, prefix: "description", details: "shared-guard construction action.");

    internal static Task ObserveContextAsync(double width, bool dark, VoiceBrowseMode mode, string? directory = null) =>
        SearchAdjustmentControlSuite.ObserveMatrixTaskActionAsync($"{mode}/{width}/{(dark ? "dark" : "light")}",
            stage => InspectAsync(width, dark, mode, directory, stage), prefix: "description",
            details: "selected/active-mode pair; full runtime sequence at actual 125% root DPI.");

    internal static Task ObserveTooltipOwnershipControlAsync(CardTooltipFixtureHooks hooks, RegressionDiagnostics.WorkCapture? diagnosticWork = null) =>
        SearchAdjustmentControlSuite.ObserveMatrixTaskActionAsync("Search/400/light",
            stage => InspectAsync(400, false, VoiceBrowseMode.Search, null, stage, hooks), prefix: "description",
            details: "selected/active-mode pair; full runtime sequence at actual 125% root DPI.", diagnosticWork: diagnosticWork);
}
