using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using static AsyncTestWait;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static StaTestHost;
using static TestAssertions;

internal static class ArchitectureContractSuite
{
    internal static void MainDelegatesInitializationToLifecycleCoordinator()
    {
        const string coordinatorTypeName = "STranslate.Plugin.Tts.FishAudio.Lifecycle.PluginInitializationCoordinator";
        var pluginAssembly = typeof(Main).Assembly;
        var coordinatorType = pluginAssembly.GetType(coordinatorTypeName);
        AssertNotNull(coordinatorType, "Initialization should be owned by the lifecycle coordinator boundary");

        var mainFields = typeof(Main).GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        AssertEqual(
            1,
            mainFields.Count(field => field.FieldType.FullName == coordinatorTypeName),
            "Main should own exactly one lifecycle coordinator");
        AssertEqual(
            false,
            mainFields.Any(field => field.Name is "_persistenceGate" or "_stateGate"),
            "Main should not retain initialization gates after extraction");

        var coordinatorFields = coordinatorType!.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        AssertEqual(
            true,
            coordinatorFields.Any(field => field.Name == "_persistenceGate"),
            "The lifecycle coordinator should own the persistence gate");
        AssertEqual(
            true,
            coordinatorFields.Any(field => field.Name == "_stateGate"),
            "The lifecycle coordinator should own the state gate");
        AssertNotNull(
            coordinatorType.GetMethod("CaptureOperationSnapshot", BindingFlags.NonPublic | BindingFlags.Instance),
            "The lifecycle coordinator should atomically capture TTS dependencies");
    }

    internal static void ApiKeyValidationStateWasRemoved()
    {
        AssertEqual(
            false,
            File.Exists(FindRepoPath(Path.Combine("STranslate.Plugin.Tts.FishAudio", "ApiKeyValidationState.cs"))),
            "Runtime API Key validation state should be removed");
    }

    internal static void SettingsViewModelSplitsPreviewAndCoverCacheResponsibilities()
    {
        var constructors = typeof(SettingsViewModel).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        AssertEqual(
            true,
            constructors.Any(c =>
            {
                var parameters = c.GetParameters();
                return parameters.Length is 2 or 3
                    && parameters[0].ParameterType == typeof(IPluginContext)
                    && parameters[1].ParameterType == typeof(Settings)
                    && (parameters.Length == 2 || parameters[2].ParameterType == typeof(DateTimeOffset?));
            }),
            "SettingsViewModel should expose a public constructor without the obsolete pending credit task parameter");
        AssertEqual(
            false,
            constructors.SelectMany(c => c.GetParameters()).Any(p =>
                p.Name == "pendingCreditTask" || p.ParameterType == typeof(Task<(WalletCreditResponse?, long)>)),
            "SettingsViewModel public constructors should not expose the unused pendingCreditTask parameter");

        var viewModelAssembly = typeof(SettingsViewModel).Assembly;
        AssertNotNull(
            viewModelAssembly.GetType("STranslate.Plugin.Tts.FishAudio.Presentation.PreviewPlaybackController"),
            "Preview playback should be split into an internal controller boundary");
        AssertNotNull(
            viewModelAssembly.GetType("STranslate.Plugin.Tts.FishAudio.Presentation.CoverImageCacheDisplayManager"),
            "Cover image cache display and cleanup should be split into an internal manager boundary");
    }

    internal static void SettingsViewModelDelegatesCreditRefreshWorkflow()
    {
        const string coordinatorTypeName = "STranslate.Plugin.Tts.FishAudio.Presentation.CreditRefreshCoordinator";
        var pluginAssembly = typeof(SettingsViewModel).Assembly;
        var coordinatorType = pluginAssembly.GetType(coordinatorTypeName);
        AssertNotNull(coordinatorType, "Credit refresh should be owned by an internal coordinator boundary");

        var viewModelFields = typeof(SettingsViewModel).GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        AssertEqual(
            1,
            viewModelFields.Count(field => field.FieldType.FullName == coordinatorTypeName),
            "SettingsViewModel should own exactly one credit refresh coordinator");
        AssertEqual(
            false,
            viewModelFields.Any(field => field.Name is
                "_operationStateGate"
                or "_apiKeyOperationState"
                or "_creditOperationState"
                or "_startupCreditRefreshCycle"
                or "_latencyHideTimer"),
            "SettingsViewModel should not retain credit workflow state after extraction");

        var coordinatorFields = coordinatorType!.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        AssertEqual(
            true,
            coordinatorFields.Any(field => field.Name == "_operationStateGate"),
            "The credit coordinator should preserve the shared operation activity gate");
        AssertEqual(
            2,
            coordinatorFields.Count(field =>
                field.FieldType.FullName == "STranslate.Plugin.Tts.FishAudio.Presentation.OperationActivityCounter"),
            "The credit coordinator should preserve both activity counters under the shared gate");

        AssertNotNull(
            typeof(SettingsViewModel).GetMethod("BeginApiKeyOperation", BindingFlags.NonPublic | BindingFlags.Instance),
            "SettingsViewModel should preserve the internal API Key operation entry point");
        AssertNotNull(
            typeof(SettingsViewModel).GetMethod("EndApiKeyOperation", BindingFlags.NonPublic | BindingFlags.Instance),
            "SettingsViewModel should preserve the internal API Key operation completion entry point");
    }

    internal static void SettingsViewModelDelegatesVoiceDiscoveryWorkflow()
    {
        const string coordinatorTypeName = "STranslate.Plugin.Tts.FishAudio.Presentation.VoiceDiscoveryCoordinator";
        var pluginAssembly = typeof(SettingsViewModel).Assembly;
        var coordinatorType = pluginAssembly.GetType(coordinatorTypeName);
        AssertNotNull(coordinatorType, "Voice search and by-ID lookup should be owned by an internal coordinator boundary");
        AssertEqual(false, coordinatorType!.IsPublic, "The voice discovery coordinator should remain internal");

        var viewModelFields = typeof(SettingsViewModel).GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        AssertEqual(
            1,
            viewModelFields.Count(field => field.FieldType.FullName == coordinatorTypeName),
            "SettingsViewModel should own exactly one voice discovery coordinator");
        AssertEqual(
            false,
            viewModelFields.Any(field => field.Name is
                "_searchOperationId"
                or "_voiceIdOperationId"
                or "_searchCancellationTokenSource"
                or "_voiceIdCancellationTokenSource"),
            "SettingsViewModel should not retain voice discovery operation state after extraction");

        var coordinatorFields = coordinatorType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        AssertEqual(
            true,
            coordinatorFields.Any(field => field.Name == "_searchOperationId"),
            "The voice discovery coordinator should own search result versioning");
        AssertEqual(
            true,
            coordinatorFields.Any(field => field.Name == "_voiceIdOperationId"),
            "The voice discovery coordinator should own by-ID result versioning");
        AssertEqual(
            2,
            coordinatorFields.Count(field => field.FieldType == typeof(CancellationTokenSource)),
            "The voice discovery coordinator should own both cancellation sources");
    }

    internal static void SettingsViewModelDelegatesPreviewRefreshWorkflow()
    {
        const string coordinatorTypeName = "STranslate.Plugin.Tts.FishAudio.Presentation.PreviewRefreshCoordinator";
        const string policyTypeName = "STranslate.Plugin.Tts.FishAudio.Presentation.PreviewAudioUrlPolicy";
        const string validatorTypeName =
            "STranslate.Plugin.Tts.FishAudio.Presentation." + "PreviewAudioUrl" + "Validator";
        var pluginAssembly = typeof(SettingsViewModel).Assembly;
        var coordinatorType = pluginAssembly.GetType(coordinatorTypeName);
        AssertNotNull(coordinatorType, "Signed preview URL refresh should be owned by an internal coordinator boundary");
        AssertEqual(false, coordinatorType!.IsPublic, "The preview refresh coordinator should remain internal");
        AssertNotNull(pluginAssembly.GetType(policyTypeName), "Preview URL decisions should be owned by PreviewAudioUrlPolicy");
        AssertEqual(null, pluginAssembly.GetType(validatorTypeName), "The obsolete preview URL validator type should be removed");

        var viewModelFields = typeof(SettingsViewModel).GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        AssertEqual(
            1,
            viewModelFields.Count(field => field.FieldType.FullName == coordinatorTypeName),
            "SettingsViewModel should own exactly one preview refresh coordinator");
        AssertEqual(
            false,
            viewModelFields.Any(field => field.Name is
                "_displayPreviewOperationGate"
                or "_displayPreviewOperationId"
                or "_displayPreviewCancellationTokenSource"
                or "_displayPreviewOperationTarget"),
            "SettingsViewModel should not retain preview refresh operation state after extraction");
        AssertEqual(
            1,
            viewModelFields.Count(field =>
                field.FieldType.FullName == "STranslate.Plugin.Tts.FishAudio.Presentation.PreviewPlaybackController"),
            "SettingsViewModel should retain ownership of the preview playback controller");

        var coordinatorFields = coordinatorType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        AssertEqual(
            true,
            coordinatorFields.Any(field => field.Name == "_displayPreviewOperationGate"),
            "The preview refresh coordinator should own the target-operation gate");
        AssertEqual(
            true,
            coordinatorFields.Any(field => field.Name == "_displayPreviewOperationId"),
            "The preview refresh coordinator should own target versioning");
        AssertEqual(
            true,
            coordinatorFields.Any(field => field.FieldType == typeof(CancellationTokenSource)),
            "The preview refresh coordinator should own refresh cancellation");

        AssertEqual(
            "STranslate.Plugin.Tts.FishAudio.ViewModel",
            typeof(VoiceSearchItem).Namespace,
            "VoiceSearchItem should preserve its public namespace");
        AssertEqual(
            true,
            File.Exists(FindRepoPath(Path.Combine(
                "STranslate.Plugin.Tts.FishAudio",
                "Presentation",
                "VoiceSearchItem.cs"))),
            "VoiceSearchItem should live in its own presentation source file");
        var settingsViewModelSource = File.ReadAllText(FindRepoPath(Path.Combine(
            "STranslate.Plugin.Tts.FishAudio",
            "Presentation",
            "SettingsViewModel.cs")));
        AssertEqual(
            false,
            settingsViewModelSource.Contains("public partial class VoiceSearchItem", StringComparison.Ordinal),
            "SettingsViewModel should no longer contain the VoiceSearchItem declaration");
    }

    internal static void ApplyAvailableModelsUsesUiThreadInvoker()
    {
        var invoked = false;
        var previous = SettingsViewModel.UiThreadInvokerOverride;
        SettingsViewModel.UiThreadInvokerOverride = action =>
        {
            invoked = true;
            action();
        };

        try
        {
            var settings = new Settings { SelectedModel = FishAudioModelPolicy.S21ProFreeModel };
            var context = CreateContext(settings: settings);
            var proxy = (ContextProxy)(object)context;
            var viewModel = new SettingsViewModel(context, settings, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));

            viewModel.ApplyAvailableModels(FishAudioModelPolicy.FreeModelCutoffUtc);

            AssertEqual(true, invoked, "ApplyAvailableModels should marshal binding updates through the UI invoker");
            AssertEqual(FishAudioModelPolicy.S21ProModel, viewModel.SelectedModel, "ApplyAvailableModels should still normalize unavailable selected models");
            AssertEqual(0, proxy.SaveCount, "ApplyAvailableModels should not trigger an extra settings save while syncing startup state");
        }
        finally
        {
            SettingsViewModel.UiThreadInvokerOverride = previous;
        }
    }
}
