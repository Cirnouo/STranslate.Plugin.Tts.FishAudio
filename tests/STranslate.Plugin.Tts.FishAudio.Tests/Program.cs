using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.Service;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;

const string AppliedKey = "0123456789abcdef0123456789abcdef";
const string DraftKey = "abcdef0123456789abcdef0123456789";

PackageReferenceUsesSdk1012();
ApiKeyValidationStateWasRemoved();
ApiKeyEditingPersistsImmediately();
MainInitDoesNotValidateCredit();
ModelPolicyUsesCutoffDefaultsAndNormalizeLoudnessSupport();
MainInitNormalizesStructuredSettingsWithoutClearingKeys();
await StartupRefreshSelectedVoiceMetadataAsync();
await StartupRefreshDisposeCancelsPendingWorkWithoutLoggingAsync();
await StartupRefreshCancellationAfterResponseSkipsSideEffectsAsync();
await ReinitializedStartupRefreshCannotMutateNewSettingsAsync();
ApplyAvailableModelsUsesUiThreadInvoker();
await PlayAudioPreflightStopsBeforeRequestForMissingNetworkAsync();
await PlayAudioPreflightStopsBeforeRequestForEmptyAndMalformedKeysAsync();
await PlayAudioWithFormattedKeyPostsAndPlaysWithoutRuntimeValidationAsync();
await PlayAudioTimeoutShowsLocalizedErrorAndLogsAsync();
await ManualCreditRefreshUsesPreflightAndLocksApiKeyInputAsync();
await ManualCreditRefreshSuccessShowsBalanceAndLatencyAsync();
await ManualCreditRefreshTimeoutShowsLocalizedErrorAndLogsAsync();
await SilentCreditRefreshPreflightAndTimeoutOnlyLogAsync();
await SearchAndByIdUseDummyTokenAsync();
await VoiceLookupRequestsUseTimeoutAndPreserveFailureSemanticsAsync();
await VoiceLookupRequestsCancelPreviousAndDisposeWorkAsync();
await SearchPaginationUpdatesVisiblePageAfterSuccessOnlyAsync();
await PostTtsRequestHonorsModelSpecificProsodyAndTimeoutAsync();
PromoStatePersistsDismissalAndUse();
SettingsViewRemovesApiKeyValidationUiAndUsesLatencyBars();
SettingsViewIncludesS21PromoAndDynamicModelDescriptions();
LanguageResourcesMatchApiKeyRollback();
SettingsViewSliderTooltipsMatchDisplayedPrecision();
LanguageResourcesDescribeContextConditioningConsistency();
TranslatedReadmesMatchCurrentSourceAndControlNames();
CoverImageCacheUsesExistingFile();
await CoverImageCacheCreatesMissedFileAsync();
CoverImageCacheClearsOnlyCoverImagesAndFormatsSize();
CoverImageCacheSizeScansCoverImagesDirectory();
await ClearCoverImageCacheCommandTracksBusyStateAsync();
await ClearCoverImageCacheCommandTimesOutAndRestoresButtonAsync();
await LateClearCoverImageCacheTaskDoesNotUnlockNewOperationAsync();
ClearCacheButtonUsesLocalizedTextAndBusySpinner();

Console.WriteLine("Fish Audio plugin regression tests passed.");

static void PackageReferenceUsesSdk1012()
{
    var project = File.ReadAllText(FindRepoFile(Path.Combine(
        "STranslate.Plugin.Tts.FishAudio",
        "STranslate.Plugin.Tts.FishAudio.csproj")));

    AssertEqual(
        true,
        project.Contains("<PackageReference Include=\"STranslate.Plugin\" Version=\"1.0.12\" />", StringComparison.Ordinal),
        "Plugin project should reference STranslate.Plugin SDK 1.0.12");
}

static void ApiKeyValidationStateWasRemoved()
{
    AssertEqual(
        false,
        File.Exists(FindRepoPath(Path.Combine("STranslate.Plugin.Tts.FishAudio", "ApiKeyValidationState.cs"))),
        "Runtime API Key validation state should be removed");
}

static void ApiKeyEditingPersistsImmediately()
{
    var settings = new Settings { ApiKey = AppliedKey };
    var context = CreateContext(settings: settings);
    var proxy = (ContextProxy)(object)context;
    var viewModel = new SettingsViewModel(context, settings, null);

    viewModel.ApiKey = DraftKey;

    AssertEqual(DraftKey, settings.ApiKey, "Editing API Key should immediately update settings");
    AssertEqual(1, proxy.SaveCount, "Editing API Key should immediately save settings");
}

static void MainInitDoesNotValidateCredit()
{
    using var network = OverrideNetworkAvailability(false);
    var settings = new Settings { ApiKey = AppliedKey };
    var (httpService, http) = TestHttpServiceProxy.Create();
    var plugin = new Main();

    plugin.Init(CreateContext(settings: settings, httpService: httpService));

    AssertEqual(0, http.GetCallCount, "Main.Init should not call the credit endpoint");
}

static void ModelPolicyUsesCutoffDefaultsAndNormalizeLoudnessSupport()
{
    var beforeCutoff = FishAudioRuntime.FreeModelCutoffUtc.AddTicks(-1);
    var onCutoff = FishAudioRuntime.FreeModelCutoffUtc;

    AssertEnumerableEqual(
        new[]
        {
            FishAudioRuntime.S21ProFreeModel,
            FishAudioRuntime.S21ProModel,
            FishAudioRuntime.S2ProModel,
            FishAudioRuntime.S1Model,
        },
        FishAudioRuntime.GetAvailableModels(beforeCutoff),
        "Free model should be available before the cutoff");
    AssertEqual(FishAudioRuntime.S21ProFreeModel, FishAudioRuntime.GetDefaultModel(beforeCutoff), "Free model should be the default before the cutoff");

    AssertEnumerableEqual(
        new[] { FishAudioRuntime.S21ProModel, FishAudioRuntime.S2ProModel, FishAudioRuntime.S1Model },
        FishAudioRuntime.GetAvailableModels(onCutoff),
        "Free model should be unavailable at and after the cutoff");
    AssertEqual(FishAudioRuntime.S21ProModel, FishAudioRuntime.GetDefaultModel(onCutoff), "s2.1-pro should be the default at and after the cutoff");

    AssertEqual(true, FishAudioRuntime.SupportsNormalizeLoudness(FishAudioRuntime.S21ProFreeModel), "s2.1-pro-free should support normalize_loudness");
    AssertEqual(true, FishAudioRuntime.SupportsNormalizeLoudness(FishAudioRuntime.S21ProModel), "s2.1-pro should support normalize_loudness");
    AssertEqual(true, FishAudioRuntime.SupportsNormalizeLoudness(FishAudioRuntime.S2ProModel), "s2-pro should support normalize_loudness");
    AssertEqual(false, FishAudioRuntime.SupportsNormalizeLoudness(FishAudioRuntime.S1Model), "s1 should not support normalize_loudness");

    using (OverrideLocalUtcNow(beforeCutoff))
    {
        var settings = new Settings();
        new Main().Init(CreateContext(settings: settings), beforeCutoff);
        AssertEqual(FishAudioRuntime.S21ProFreeModel, settings.SelectedModel, "New settings should use the free model default before the cutoff");
    }

    using (OverrideLocalUtcNow(onCutoff))
    {
        var settings = new Settings();
        new Main().Init(CreateContext(settings: settings), onCutoff);
        AssertEqual(FishAudioRuntime.S21ProModel, settings.SelectedModel, "New settings should use s2.1-pro as the default at and after the cutoff");
    }
}

static void MainInitNormalizesStructuredSettingsWithoutClearingKeys()
{
    var settings = new Settings
    {
        ApiKey = "ABC",
        VoiceId = "not-a-voice-id",
        SelectedModel = FishAudioRuntime.S21ProFreeModel,
        Latency = "turbo",
        Mp3Bitrate = 999,
        Speed = 10,
        Volume = -100,
        Temperature = 8,
        TopP = -2,
    };
    var context = CreateContext(settings: settings);
    var proxy = (ContextProxy)(object)context;
    var plugin = new Main();

    plugin.Init(context, FishAudioRuntime.FreeModelCutoffUtc);

    AssertEqual("ABC", settings.ApiKey, "Startup normalization should not clear malformed API Key text");
    AssertEqual("not-a-voice-id", settings.VoiceId, "Startup normalization should not clear malformed Voice ID text");
    AssertEqual(FishAudioRuntime.S21ProModel, settings.SelectedModel, "Expired persisted free model should normalize to the current default");
    AssertEqual("normal", settings.Latency, "Invalid latency should normalize to default");
    AssertEqual(192, settings.Mp3Bitrate, "Invalid MP3 bitrate should normalize to default");
    AssertEqual(1.0, settings.Speed, "Invalid speed should normalize to default");
    AssertEqual(0.0, settings.Volume, "Invalid volume should normalize to default");
    AssertEqual(0.7, settings.Temperature, "Invalid temperature should normalize to default");
    AssertEqual(0.7, settings.TopP, "Invalid top_p should normalize to default");
    AssertEqual(1, proxy.SaveCount, "Startup normalization should save corrected settings once");
}

static async Task StartupRefreshSelectedVoiceMetadataAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings
    {
        VoiceId = "fedcba9876543210fedcba9876543210",
        CachedVoice = new CachedVoiceInfo { Title = "Old", CoverImage = "old" },
    };
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetResponseJson = "{\"_id\":\"fedcba9876543210fedcba9876543210\",\"title\":\"Fresh Voice\",\"description\":\"Fresh description\",\"cover_image\":\"fresh-cover\",\"samples\":[{\"audio\":\"https://audio.example/fresh.mp3\"}],\"task_count\":42,\"author\":{\"nickname\":\"Fresh Author\"}}";
    var context = CreateContext(settings: settings, httpService: httpService);
    var proxy = (ContextProxy)(object)context;
    var plugin = new Main();

    plugin.Init(context, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(true, http.LastGetUrl?.Contains("/model/fedcba9876543210fedcba9876543210", StringComparison.Ordinal), "Startup refresh should call Get Model for the selected voice");
    var headers = AssertHeaders(http.LastGetOptions, "Startup voice refresh should send Authorization headers");
    AssertEqual("Bearer dummy", headers["Authorization"], "Startup voice refresh should use the dummy token");
    AssertEqual("Fresh Voice", settings.CachedVoice?.Title, "Startup voice refresh should update cached voice title");
    AssertEqual("Fresh description", settings.CachedVoice?.Description, "Startup voice refresh should update cached voice description");
    AssertEqual("fresh-cover", settings.CachedVoice?.CoverImage, "Startup voice refresh should update cached voice image");
    AssertEqual("Fresh Author", settings.CachedVoice?.AuthorName, "Startup voice refresh should update cached voice author");
    AssertEqual(42, settings.CachedVoice?.TaskCount, "Startup voice refresh should update cached voice task count");
    AssertEqual("https://audio.example/fresh.mp3", settings.CachedVoice?.SampleAudioUrl, "Startup voice refresh should update cached sample URL");
    AssertEqual(1, proxy.SaveCount, "Successful startup voice refresh should save updated cached voice");

    network.Set(false);
    settings = new Settings
    {
        VoiceId = "0123456789abcdef0123456789abcdef",
        CachedVoice = new CachedVoiceInfo { Title = "Keep" },
    };
    (httpService, http) = TestHttpServiceProxy.Create();
    plugin = new Main();
    plugin.Init(CreateContext(settings: settings, httpService: httpService), FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(0, http.GetCallCount, "Startup voice refresh should skip server call when offline");
    AssertEqual("Keep", settings.CachedVoice?.Title, "Skipped startup voice refresh should preserve cached voice");
}

static async Task StartupRefreshDisposeCancelsPendingWorkWithoutLoggingAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings
    {
        VoiceId = "fedcba9876543210fedcba9876543210",
        CachedVoice = new CachedVoiceInfo { Title = "Keep" },
    };
    var (httpService, http) = TestHttpServiceProxy.Create();
    var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var requestCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    http.GetAsyncHandler = (_, _, ct) =>
    {
        requestStarted.SetResult();
        ct.Register(() => requestCanceled.TrySetResult());
        return Task.Delay(TimeSpan.FromSeconds(30), ct).ContinueWith<string>(task =>
        {
            if (task.IsCanceled)
                throw new OperationCanceledException(ct);
            return "{\"dateTime\":\"2026-06-27T00:00:00Z\"}";
        }, CancellationToken.None);
    };
    var logger = new TestLogger();
    var context = CreateContext(settings: settings, httpService: httpService, logger: logger);
    var proxy = (ContextProxy)(object)context;
    var plugin = new Main();

    plugin.Init(context, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    plugin.Dispose();

    await requestCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual("Keep", settings.CachedVoice?.Title, "Canceled startup refresh should preserve cached voice");
    AssertEqual(0, proxy.SaveCount, "Canceled startup refresh should not save settings");
    AssertEqual(false, logger.Contains("startup refresh failed"), "Canceled startup refresh should not log a failure warning");
}

static async Task StartupRefreshCancellationAfterResponseSkipsSideEffectsAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings
    {
        VoiceId = "fedcba9876543210fedcba9876543210",
        CachedVoice = new CachedVoiceInfo { Title = "Keep" },
    };
    var (httpService, http) = TestHttpServiceProxy.Create();
    var plugin = new Main();
    var disposeBeforeModelResponse = true;
    var logger = new TestLogger();
    var context = CreateContext(settings: settings, httpService: httpService, logger: logger);
    var proxy = (ContextProxy)(object)context;

    http.GetAsyncHandler = (_, _, _) =>
    {
        if (disposeBeforeModelResponse)
        {
            disposeBeforeModelResponse = false;
            return Task.FromResult("{\"dateTime\":\"2026-06-27T00:00:00Z\"}");
        }

        plugin.Dispose();
        return Task.FromResult("{\"_id\":\"fedcba9876543210fedcba9876543210\",\"title\":\"Should Not Apply\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}");
    };

    plugin.Init(context, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual("Keep", settings.CachedVoice?.Title, "Startup cancellation after model response should skip cached voice side effects");
    AssertEqual(0, proxy.SaveCount, "Startup cancellation after model response should not save settings");
    AssertEqual(false, logger.Contains("startup refresh failed"), "Startup cancellation after model response should not log failure");
}

static async Task ReinitializedStartupRefreshCannotMutateNewSettingsAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var oldSettings = new Settings
    {
        VoiceId = "11111111111111111111111111111111",
        CachedVoice = new CachedVoiceInfo { Title = "Old Keep" },
    };
    var newSettings = new Settings
    {
        VoiceId = "",
        CachedVoice = new CachedVoiceInfo { Title = "New Keep" },
    };
    var oldRelease = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var (oldHttpService, oldHttp) = TestHttpServiceProxy.Create();
    oldHttp.GetAsyncHandler = (_, _, _) => oldRelease.Task;
    var oldContext = CreateContext(settings: oldSettings, httpService: oldHttpService);
    var oldProxy = (ContextProxy)(object)oldContext;

    var (newHttpService, newHttp) = TestHttpServiceProxy.Create();
    newHttp.GetResponseJson = "{\"dateTime\":\"2026-06-27T00:00:00Z\"}";
    var newContext = CreateContext(settings: newSettings, httpService: newHttpService);
    var newProxy = (ContextProxy)(object)newContext;
    var plugin = new Main();

    plugin.Init(oldContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var oldStartupTask = plugin.PendingStartupTask;
    plugin.Init(newContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    oldRelease.SetResult("{\"_id\":\"11111111111111111111111111111111\",\"title\":\"Old Should Not Leak\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}");
    await oldStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual("Old Keep", oldSettings.CachedVoice?.Title, "Canceled old startup refresh should not mutate old settings after reinitialization");
    AssertEqual("New Keep", newSettings.CachedVoice?.Title, "Canceled old startup refresh should not mutate new settings after reinitialization");
    AssertEqual(0, oldProxy.SaveCount, "Canceled old startup refresh should not save old context after reinitialization");
    AssertEqual(0, newProxy.SaveCount, "Canceled old startup refresh should not save new context after reinitialization");
}

static void ApplyAvailableModelsUsesUiThreadInvoker()
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
        var settings = new Settings { SelectedModel = FishAudioRuntime.S21ProFreeModel };
        var context = CreateContext(settings: settings);
        var proxy = (ContextProxy)(object)context;
        var viewModel = new SettingsViewModel(context, settings, null, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));

        viewModel.ApplyAvailableModels(FishAudioRuntime.FreeModelCutoffUtc);

        AssertEqual(true, invoked, "ApplyAvailableModels should marshal binding updates through the UI invoker");
        AssertEqual(FishAudioRuntime.S21ProModel, viewModel.SelectedModel, "ApplyAvailableModels should still normalize unavailable selected models");
        AssertEqual(0, proxy.SaveCount, "ApplyAvailableModels should not trigger an extra settings save while syncing startup state");
    }
    finally
    {
        SettingsViewModel.UiThreadInvokerOverride = previous;
    }
}

static async Task PlayAudioPreflightStopsBeforeRequestForMissingNetworkAsync()
{
    using var network = OverrideNetworkAvailability(false);
    var settings = CreateTtsSettings("s2-pro");
    var snackbar = new TestSnackbar();
    var logger = new TestLogger();
    var (httpService, http) = TestHttpServiceProxy.Create();
    var audio = new TestAudioPlayer();
    var plugin = new Main();

    plugin.Init(CreateContext(snackbar, settings, httpService, audio, logger));
    await plugin.PlayAudioAsync("hello");

    AssertEqual(
        "STranslate_Plugin_Tts_FishAudio_Network_Unavailable",
        snackbar.LastError,
        "TTS should show a localized network error before sending a request");
    AssertEqual(0, http.PostAsBytesCallCount, "TTS should not call Fish Audio when the machine is offline");
    AssertEqual(0, audio.PlayBytesCallCount, "TTS should not play audio when preflight fails");
    AssertEqual(true, logger.Contains(LogLevel.Warning, "Network unavailable"), "Offline preflight should be logged as warning");
    AssertEqual(false, logger.Contains(AppliedKey), "Logs should not contain the plaintext API Key");
}

static async Task PlayAudioPreflightStopsBeforeRequestForEmptyAndMalformedKeysAsync()
{
    using var network = OverrideNetworkAvailability(false);
    var snackbar = new TestSnackbar();
    var logger = new TestLogger();
    var (httpService, http) = TestHttpServiceProxy.Create();
    var audio = new TestAudioPlayer();
    var plugin = new Main();

    plugin.Init(CreateContext(snackbar, new Settings { ApiKey = "" }, httpService, audio, logger));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    network.Set(true);
    await plugin.PlayAudioAsync("hello");

    AssertEqual(
        "STranslate_Plugin_Tts_FishAudio_ApiKey_Empty",
        snackbar.LastError,
        "TTS should reject an empty API Key before sending a request");
    AssertEqual(0, http.PostAsBytesCallCount, "TTS should not call Fish Audio with an empty API Key");

    snackbar.Clear();
    network.Set(false);
    plugin = new Main();
    plugin.Init(CreateContext(snackbar, new Settings { ApiKey = "ABC" }, httpService, audio, logger));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    network.Set(true);
    await plugin.PlayAudioAsync("hello");

    AssertEqual(
        "STranslate_Plugin_Tts_FishAudio_ApiKey_InvalidFormat",
        snackbar.LastError,
        "TTS should reject a malformed API Key before sending a request");
    AssertEqual(0, http.PostAsBytesCallCount, "TTS should not call Fish Audio with a malformed API Key");
    AssertEqual(true, logger.Count(LogLevel.Warning) >= 2, "API Key preflight failures should be logged as warnings");
}

static async Task PlayAudioWithFormattedKeyPostsAndPlaysWithoutRuntimeValidationAsync()
{
    using var network = OverrideNetworkAvailability(false);
    var settings = CreateTtsSettings("s2-pro");
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.PostBytes = new byte[] { 1, 2, 3 };
    var audio = new TestAudioPlayer();
    var plugin = new Main();

    plugin.Init(CreateContext(settings: settings, httpService: httpService, audioPlayer: audio));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    network.Set(true);
    await plugin.PlayAudioAsync("hello");

    AssertEqual(0, http.GetCallCount, "TTS should not validate credit before playback");
    AssertEqual(1, http.PostAsBytesCallCount, "TTS should send a request when API Key format passes");
    AssertEqual(1, audio.PlayBytesCallCount, "TTS should play bytes returned by Fish Audio");
    AssertSequenceEqual(new byte[] { 1, 2, 3 }, audio.LastPlayedBytes, "TTS should play the returned audio bytes");
}

static async Task PlayAudioTimeoutShowsLocalizedErrorAndLogsAsync()
{
    using var network = OverrideNetworkAvailability(false);
    var settings = CreateTtsSettings("s2-pro");
    var snackbar = new TestSnackbar();
    var logger = new TestLogger();
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.PostException = new TimeoutException("request timed out");
    var audio = new TestAudioPlayer();
    var plugin = new Main();

    plugin.Init(CreateContext(snackbar, settings, httpService, audio, logger));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    network.Set(true);
    await plugin.PlayAudioAsync("hello");

    AssertEqual(
        "STranslate_Plugin_Tts_FishAudio_Request_Timeout",
        snackbar.LastError,
        "TTS timeout should show a localized timeout error");
    AssertEqual(0, audio.PlayBytesCallCount, "Timed out TTS should not play audio");
    AssertEqual(true, logger.Contains(LogLevel.Error, "TTS request failed"), "TTS timeout should be logged as error");
}

static async Task ManualCreditRefreshUsesPreflightAndLocksApiKeyInputAsync()
{
    using var network = OverrideNetworkAvailability(false);
    var snackbar = new TestSnackbar();
    var logger = new TestLogger();
    var (httpService, http) = TestHttpServiceProxy.Create();
    var settings = new Settings { ApiKey = AppliedKey };
    var viewModel = new SettingsViewModel(
        CreateContext(snackbar, settings, httpService, logger: logger),
        settings,
        null);

    await viewModel.RefreshCreditCommand.ExecuteAsync(null);
    AssertEqual(
        "STranslate_Plugin_Tts_FishAudio_Network_Unavailable",
        snackbar.LastError,
        "Manual credit refresh should use the shared offline preflight");
    AssertEqual(0, http.GetCallCount, "Manual credit refresh should not call the API when offline");
    AssertEqual(true, logger.Contains(LogLevel.Warning, "Network unavailable"), "Offline manual refresh should be logged");

    network.Set(true);
    snackbar.Clear();
    var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseRequest = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    http.GetAsyncHandler = (_, _, _) =>
    {
        requestStarted.SetResult();
        return releaseRequest.Task;
    };

    var commandTask = viewModel.RefreshCreditCommand.ExecuteAsync(null);
    await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(true, viewModel.IsApiKeyInputLocked, "Manual credit refresh should lock API Key input while the request is running");
    AssertEqual(false, viewModel.PasteApiKeyCommand.CanExecute(null), "Paste API Key should be disabled while API Key input is locked");

    releaseRequest.SetResult("{\"credit\":\"4.56\"}");
    await commandTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(false, viewModel.IsApiKeyInputLocked, "Manual credit refresh should unlock API Key input after the request completes");
    AssertEqual(true, viewModel.PasteApiKeyCommand.CanExecute(null), "Paste API Key should be enabled after API Key input unlocks");
}

static async Task ManualCreditRefreshSuccessShowsBalanceAndLatencyAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings { ApiKey = AppliedKey };
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetResponseJson = "{\"credit\":\"7.89\"}";
    var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings, null);

    await viewModel.RefreshCreditCommand.ExecuteAsync(null);

    AssertEqual("7.89", viewModel.UserCredit, "Manual credit refresh should update credit balance");
    AssertEqual(true, viewModel.LatencyText.EndsWith(" ms", StringComparison.Ordinal), "Manual credit refresh should show latency text");
    AssertNotNull(viewModel.LatencyBrush, "Manual credit refresh should color the latency text");
    AssertEqual(TimeSpan.FromSeconds(15), http.LastGetOptions?.Timeout, "Credit refresh should set an explicit request timeout");
}

static async Task ManualCreditRefreshTimeoutShowsLocalizedErrorAndLogsAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings { ApiKey = AppliedKey };
    var snackbar = new TestSnackbar();
    var logger = new TestLogger();
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetException = new TimeoutException("request timed out");
    var viewModel = new SettingsViewModel(
        CreateContext(snackbar, settings, httpService, logger: logger),
        settings,
        null);

    await viewModel.RefreshCreditCommand.ExecuteAsync(null);

    AssertEqual(
        "STranslate_Plugin_Tts_FishAudio_Request_Timeout",
        snackbar.LastError,
        "Manual credit refresh timeout should show a localized timeout error");
    AssertEqual("", viewModel.LatencyText, "Manual credit refresh timeout should clear latency text");
    AssertEqual(false, viewModel.IsApiKeyInputLocked, "Manual credit refresh timeout should unlock API Key input");
    AssertEqual(true, logger.Contains(LogLevel.Error, "Credit refresh failed"), "Manual credit refresh timeout should be logged");
}

static async Task SilentCreditRefreshPreflightAndTimeoutOnlyLogAsync()
{
    using var network = OverrideNetworkAvailability(false);
    var settings = new Settings { ApiKey = AppliedKey };
    var snackbar = new TestSnackbar();
    var logger = new TestLogger();
    var (httpService, http) = TestHttpServiceProxy.Create();
    var viewModel = new SettingsViewModel(
        CreateContext(snackbar, settings, httpService, logger: logger),
        settings,
        null);

    await viewModel.RefreshCreditSilentlyAsync();

    AssertEqual(null, snackbar.LastError, "Silent credit refresh preflight failure should not show a snackbar");
    AssertEqual(0, http.GetCallCount, "Silent credit refresh should not call the API when offline");
    AssertEqual(true, logger.Contains(LogLevel.Warning, "Network unavailable"), "Silent offline refresh should be logged");

    network.Set(true);
    http.GetException = new TimeoutException("request timed out");
    await viewModel.RefreshCreditSilentlyAsync();

    AssertEqual(null, snackbar.LastError, "Silent credit refresh timeout should not show a snackbar");
    AssertEqual(true, logger.Contains(LogLevel.Error, "Credit refresh failed"), "Silent credit refresh timeout should be logged");
}

static async Task SearchAndByIdUseDummyTokenAsync()
{
    var settings = new Settings { ApiKey = AppliedKey };
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetResponseJson = "{\"total\":0,\"items\":[]}";
    var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings, null)
    {
        SearchQuery = "voice",
    };

    await viewModel.SearchVoicesCommand.ExecuteAsync(null);

    var headers = AssertHeaders(http.LastGetOptions, "Voice search should send Authorization headers");
    AssertEqual("Bearer dummy", headers["Authorization"], "Voice search should use the dummy token by default");

    http.GetResponseJson = "{\"_id\":\"fedcba9876543210fedcba9876543210\",\"title\":\"Voice\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}";
    viewModel.VoiceIdInput = "fedcba9876543210fedcba9876543210";

    await viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);

    headers = AssertHeaders(http.LastGetOptions, "Voice by-ID lookup should send Authorization headers");
    AssertEqual("Bearer dummy", headers["Authorization"], "Voice by-ID lookup should use the dummy token by default");
}

static async Task VoiceLookupRequestsUseTimeoutAndPreserveFailureSemanticsAsync()
{
    var settings = new Settings();
    var (httpService, http) = TestHttpServiceProxy.Create();
    var snackbar = new TestSnackbar();
    var context = CreateContext(snackbar, settings, httpService);
    var viewModel = new SettingsViewModel(context, settings, null)
    {
        SearchQuery = "voice",
    };

    http.GetResponseJson = "{\"total\":0,\"items\":[]}";
    await viewModel.SearchVoicesCommand.ExecuteAsync(null);
    AssertEqual(TimeSpan.FromSeconds(15), http.LastGetOptions?.Timeout, "Voice search should set a 15 second timeout");

    http.GetResponseJson = "{\"_id\":\"fedcba9876543210fedcba9876543210\",\"title\":\"Voice\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}";
    viewModel.VoiceIdInput = "fedcba9876543210fedcba9876543210";
    await viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
    AssertEqual(TimeSpan.FromSeconds(15), http.LastGetOptions?.Timeout, "Voice by-ID lookup should set a 15 second timeout");

    http.GetException = new HttpRequestException("Response status code does not indicate success: 404 (Not Found).", null, HttpStatusCode.NotFound);
    viewModel.VoiceIdInput = "00000000000000000000000000000000";
    await viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
    AssertEqual(
        "STranslate_Plugin_Tts_FishAudio_Voice_NotFound",
        viewModel.VoiceIdError,
        "Only HTTP 404 should be shown as voice not found");

    http.GetException = new TimeoutException("lookup timed out");
    viewModel.VoiceIdInput = "11111111111111111111111111111111";
    await viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
    AssertEqual(
        "STranslate_Plugin_Tts_FishAudio_Request_Timeout",
        viewModel.VoiceIdError,
        "Voice by-ID timeout should show the localized timeout error instead of not found");

    http.GetException = new InvalidOperationException("server unavailable");
    viewModel.VoiceIdInput = "22222222222222222222222222222222";
    await viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
    AssertEqual("server unavailable", viewModel.VoiceIdError, "Non-404 lookup failures should preserve failure details");

    http.GetException = null;
    http.GetResponseJson = "null";
    viewModel.VoiceIdInput = "44444444444444444444444444444444";
    await viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
    AssertEqual(
        "Fish Audio model lookup returned an empty response.",
        viewModel.VoiceIdError,
        "Successful non-404 lookup responses that deserialize to null should be treated as response errors");
}

static async Task VoiceLookupRequestsCancelPreviousAndDisposeWorkAsync()
{
    var settings = new Settings();
    var (httpService, http) = TestHttpServiceProxy.Create();
    var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings, null);
    var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseSecond = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var calls = 0;

    http.GetAsyncHandler = (_, _, ct) =>
    {
        calls++;
        if (calls == 1)
        {
            firstStarted.SetResult();
            return Task.Delay(TimeSpan.FromSeconds(30), ct).ContinueWith<string>(task =>
            {
                if (task.IsCanceled)
                    throw new OperationCanceledException(ct);
                return "{\"total\":0,\"items\":[]}";
            }, CancellationToken.None);
        }

        return releaseSecond.Task;
    };

    var firstSearch = viewModel.SearchVoicesCommand.ExecuteAsync(null);
    await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    var secondSearch = viewModel.SearchVoicesCommand.ExecuteAsync(null);
    releaseSecond.SetResult("{\"total\":0,\"items\":[]}");

    await secondSearch.WaitAsync(TimeSpan.FromSeconds(2));
    await firstSearch.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(2, calls, "A new voice search should start even when a previous search is still pending");
    AssertEqual(false, viewModel.IsSearching, "Search busy state should recover after replacing a pending search");

    var byIdStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    http.GetAsyncHandler = (_, _, ct) =>
    {
        byIdStarted.SetResult();
        return Task.Delay(TimeSpan.FromSeconds(30), ct).ContinueWith<string>(task =>
        {
            if (task.IsCanceled)
                throw new OperationCanceledException(ct);
            return "{\"_id\":\"33333333333333333333333333333333\",\"title\":\"Voice\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}";
        }, CancellationToken.None);
    };

    viewModel.VoiceIdInput = "33333333333333333333333333333333";
    var submitTask = viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
    await byIdStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    viewModel.Dispose();
    await submitTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(false, viewModel.IsSubmittingVoiceId, "Disposing the view model should cancel by-ID lookup and restore busy state");
}

static async Task SearchPaginationUpdatesVisiblePageAfterSuccessOnlyAsync()
{
    var settings = new Settings();
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetResponseJson = "{\"total\":12,\"items\":[{\"_id\":\"11111111111111111111111111111111\",\"title\":\"Page 1\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":1}]}";
    var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings, null);

    await viewModel.SearchVoicesCommand.ExecuteAsync(null);

    AssertEqual(1, viewModel.SearchPage, "Initial search should load page 1");
    AssertEqual("1", viewModel.PageInput, "Initial search should show page input 1");
    AssertEqual("Page 1", viewModel.SearchResults[0].Title, "Initial search should display page 1 results");

    http.GetException = new TimeoutException("page 2 timed out");
    await viewModel.NextPageCommand.ExecuteAsync(null);

    AssertEqual(1, viewModel.SearchPage, "Failed next page load should keep previous visible page");
    AssertEqual("1", viewModel.PageInput, "Failed next page load should keep previous page input");
    AssertEqual(2, viewModel.SearchTotalPages, "Failed next page load should keep previous total pages");
    AssertEqual("Page 1", viewModel.SearchResults[0].Title, "Failed next page load should keep previous result cards");

    http.GetException = null;
    http.GetResponseJson = "{\"total\":12,\"items\":[{\"_id\":\"22222222222222222222222222222222\",\"title\":\"Page 2\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":2}]}";
    await viewModel.NextPageCommand.ExecuteAsync(null);

    AssertEqual(2, viewModel.SearchPage, "Successful next page load should update visible page after results arrive");
    AssertEqual("2", viewModel.PageInput, "Successful next page load should update page input after results arrive");
    AssertEqual("Page 2", viewModel.SearchResults[0].Title, "Successful next page load should display new result cards");
}

static async Task PostTtsRequestHonorsModelSpecificProsodyAndTimeoutAsync()
{
    foreach (var model in new[] { FishAudioRuntime.S21ProFreeModel, FishAudioRuntime.S21ProModel, FishAudioRuntime.S2ProModel })
    {
        var supportedSettings = CreateTtsSettings(model);
        var (supportedHttpService, supportedHttp) = TestHttpServiceProxy.Create();

        await FishAudioApi.PostTtsAsync(CreateContext(settings: supportedSettings, httpService: supportedHttpService), supportedSettings, "hello", CancellationToken.None);

        var supportedBody = AssertDictionary(supportedHttp.LastPostBody, $"{model} TTS request body should be a dictionary");
        var supportedProsody = AssertDictionary(supportedBody["prosody"], $"{model} TTS prosody should be a dictionary");
        var supportedHeaders = AssertHeaders(supportedHttp.LastPostOptions, $"{model} TTS request should include headers");

        AssertEqual(true, supportedProsody.ContainsKey("normalize_loudness"), $"{model} TTS request should include normalize_loudness");
        AssertEqual(false, supportedProsody["normalize_loudness"], $"{model} TTS request should preserve normalize_loudness setting");
        AssertEqual(model, supportedHeaders["model"], $"{model} TTS request should include selected model header");
    }

    var settings = CreateTtsSettings(FishAudioRuntime.S2ProModel);
    var (httpService, http) = TestHttpServiceProxy.Create();

    await FishAudioApi.PostTtsAsync(CreateContext(settings: settings, httpService: httpService), settings, "hello", CancellationToken.None);

    var body = AssertDictionary(http.LastPostBody, "TTS request body should be a dictionary");
    var prosody = AssertDictionary(body["prosody"], "TTS prosody should be a dictionary");

    AssertEqual("hello", body["text"], "TTS request should include text");
    AssertEqual("mp3", body["format"], "TTS request should request mp3 output");
    AssertEqual(128, body["mp3_bitrate"], "TTS request should preserve mp3 bitrate setting");
    AssertEqual(0.41, body["temperature"], "TTS request should preserve temperature setting");
    AssertEqual(0.82, body["top_p"], "TTS request should preserve top_p setting");
    AssertEqual(true, body["normalize"], "TTS request should preserve text normalization setting");
    AssertEqual("low", body["latency"], "TTS request should preserve latency setting");
    AssertEqual(false, body["condition_on_previous_chunks"], "TTS request should preserve context conditioning setting");
    AssertEqual("fedcba9876543210fedcba9876543210", body["reference_id"], "TTS request should include non-empty voice ID");
    AssertEqual(1.25, prosody["speed"], "TTS request should preserve speed setting");
    AssertEqual(-2.5, prosody["volume"], "TTS request should preserve volume setting");
    var headers = AssertHeaders(http.LastPostOptions, "TTS request should include headers");
    AssertEqual($"Bearer {AppliedKey}", headers["Authorization"], "TTS request should include API Key bearer token");
    AssertEqual(FishAudioRuntime.S2ProModel, headers["model"], "TTS request should include selected model header");
    AssertEqual(TimeSpan.FromSeconds(60), http.LastPostOptions?.Timeout, "TTS request should set an explicit timeout");

    settings = CreateTtsSettings(FishAudioRuntime.S1Model);
    settings.NormalizeLoudness = true;
    (httpService, http) = TestHttpServiceProxy.Create();

    await FishAudioApi.PostTtsAsync(CreateContext(settings: settings, httpService: httpService), settings, "hello", CancellationToken.None);

    prosody = AssertDictionary(
        AssertDictionary(http.LastPostBody, "s1 TTS request body should be a dictionary")["prosody"],
        "s1 TTS prosody should be a dictionary");

    headers = AssertHeaders(http.LastPostOptions, "s1 TTS request should include headers");
    AssertEqual(FishAudioRuntime.S1Model, headers["model"], "s1 TTS request should include selected model header");
    AssertEqual(false, prosody.ContainsKey("normalize_loudness"), "s1 TTS request should omit unsupported normalize_loudness");
    AssertEqual(1.25, prosody["speed"], "s1 TTS request should still include speed");
    AssertEqual(-2.5, prosody["volume"], "s1 TTS request should still include volume");
}

static void PromoStatePersistsDismissalAndUse()
{
    var settings = new Settings();
    var context = CreateContext(settings: settings);
    var proxy = (ContextProxy)(object)context;
    var viewModel = new SettingsViewModel(context, settings, null, nowUtc: FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));

    AssertEqual(true, viewModel.ShowS21ProFreePromo, "Promo should be visible before cutoff when not dismissed");

    viewModel.DismissS21ProFreePromoCommand.Execute(null);
    AssertEqual(true, settings.IsS21ProFreePromoDismissed, "Dismiss promo should persist dismissal");
    AssertEqual(false, viewModel.ShowS21ProFreePromo, "Dismiss promo should hide promo card");

    settings = new Settings { SelectedModel = FishAudioRuntime.S1Model };
    context = CreateContext(settings: settings);
    proxy = (ContextProxy)(object)context;
    viewModel = new SettingsViewModel(context, settings, null, nowUtc: FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));

    viewModel.UseS21ProFreePromoCommand.Execute(null);
    AssertEqual(FishAudioRuntime.S21ProFreeModel, settings.SelectedModel, "Using promo should select free model");
    AssertEqual(false, settings.IsS21ProFreePromoDismissed, "Using promo should not persist dismissal");
    AssertEqual(true, viewModel.ShowS21ProFreePromo, "Using promo should leave promo card visible");
    AssertEqual(1, proxy.SaveCount, "Using promo should save the selected model once");

    settings = new Settings();
    viewModel = new SettingsViewModel(CreateContext(settings: settings), settings, null, nowUtc: FishAudioRuntime.FreeModelCutoffUtc);
    AssertEqual(false, viewModel.ShowS21ProFreePromo, "Promo should be hidden at and after cutoff");

    settings = new Settings { SelectedModel = FishAudioRuntime.S1Model };
    viewModel = new SettingsViewModel(CreateContext(settings: settings), settings, null, nowUtc: FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    AssertEqual(false, viewModel.IsNormalizeLoudnessEnabled, "Normalize loudness should remain visible but disabled for s1");
    viewModel.SelectedModel = FishAudioRuntime.S2ProModel;
    AssertEqual(true, viewModel.IsNormalizeLoudnessEnabled, "Normalize loudness should be enabled for s2-pro and newer models");
}

static void SettingsViewRemovesApiKeyValidationUiAndUsesLatencyBars()
{
    var xaml = File.ReadAllText(FindRepoFile(Path.Combine("STranslate.Plugin.Tts.FishAudio", "View", "SettingsView.xaml")));

    AssertEqual(false, xaml.Contains("ConfirmApiKeyCommand", StringComparison.Ordinal), "Settings view should not contain API Key confirm command");
    AssertEqual(false, xaml.Contains("ApiKeyStatusKind", StringComparison.Ordinal), "Settings view should not contain API Key validation status UI");
    AssertEqual(false, xaml.Contains("ApiKeyStatusText", StringComparison.Ordinal), "Settings view should not bind API Key validation status text");
    AssertEqual(false, xaml.Contains("STranslate_Plugin_Tts_FishAudio_ApiKey_Waiting", StringComparison.Ordinal), "Settings view should not show API Key waiting text");
    AssertEqual(false, xaml.Contains("STranslate_Plugin_Tts_FishAudio_ApiKey_Applied", StringComparison.Ordinal), "Settings view should not show API Key applied text");
    AssertEqual(false, xaml.Contains("<PasswordBox.Style>", StringComparison.Ordinal), "API Key PasswordBox should not override the theme style");
    AssertEqual(true, xaml.Contains("IsEnabled=\"{Binding IsApiKeyInputEnabled}\"", StringComparison.Ordinal), "API Key PasswordBox should bind enabled state without replacing its style");

    var latencyBarsIndex = xaml.IndexOf("x:Name=\"LatencyBars\"", StringComparison.Ordinal);
    AssertEqual(true, latencyBarsIndex >= 0, "Latency display should use the named three-bar icon");
    var latencyTextIndex = xaml.IndexOf("Text=\"{Binding LatencyText}\"", latencyBarsIndex, StringComparison.Ordinal);
    AssertEqual(true, latencyTextIndex > latencyBarsIndex, "Latency bars should appear before latency text");
    var latencyBarsXaml = xaml.Substring(latencyBarsIndex, latencyTextIndex - latencyBarsIndex);
    AssertEqual(3, CountOccurrences(latencyBarsXaml, "<Rectangle"), "Latency icon should contain exactly three rectangles");
    AssertEqual(3, CountOccurrences(latencyBarsXaml, "VerticalAlignment=\"Bottom\""), "All latency rectangles should align to the same bottom baseline");
    AssertEqual(false, latencyBarsXaml.Contains("Segoe MDL2 Assets", StringComparison.Ordinal), "Latency icon should not use the old font glyph");
}

static void SettingsViewIncludesS21PromoAndDynamicModelDescriptions()
{
    var xaml = File.ReadAllText(FindRepoFile(Path.Combine("STranslate.Plugin.Tts.FishAudio", "View", "SettingsView.xaml")));

    AssertEqual(true, xaml.Contains("s2-pro-free-promo.webp", StringComparison.Ordinal), "Settings view should use the local Fish Audio s2.1 promo image");
    AssertEqual(false, xaml.Contains("media/blog-images/f614a042a9ac407890cad88d69abbf33", StringComparison.Ordinal), "Settings view should not load the promo image from the network");
    var promoImageIndex = xaml.IndexOf("s2-pro-free-promo.webp", StringComparison.Ordinal);
    var promoImageStart = xaml.LastIndexOf("<Image", promoImageIndex, StringComparison.Ordinal);
    var promoImageEnd = xaml.IndexOf("/>", promoImageIndex, StringComparison.Ordinal);
    AssertEqual(true, promoImageStart >= 0 && promoImageEnd > promoImageStart, "Promo image should be an Image element");
    var promoImageXaml = xaml.Substring(promoImageStart, promoImageEnd - promoImageStart);
    AssertEqual(true, promoImageXaml.Contains("Width=\"1024\"", StringComparison.Ordinal), "Promo placeholder should reserve the image width");
    AssertEqual(true, promoImageXaml.Contains("Height=\"540\"", StringComparison.Ordinal), "Promo placeholder should reserve the image height");
    AssertEqual(true, xaml.Contains("Stretch=\"Uniform\"", StringComparison.Ordinal), "Promo placeholder should preserve the image aspect ratio while resizing");
    AssertEqual(true, xaml.Contains("ShowS21ProFreePromo", StringComparison.Ordinal), "Promo visibility should be bound to ViewModel state");
    AssertEqual(true, xaml.Contains("S21ProFreePromoCard_MouseLeftButtonUp", StringComparison.Ordinal), "Promo card should route clicks to view-only code-behind behavior");
    AssertEqual(true, xaml.Contains("DismissS21ProFreePromoCommand", StringComparison.Ordinal), "Promo card should expose a dismissal command");
    AssertEqual(true, xaml.Contains("x:Name=\"SynthesisModelCard\"", StringComparison.Ordinal), "Synthesis model card should be named for scrolling/highlight behavior");
    AssertEqual(true, xaml.Contains("IsEnabled=\"{Binding IsNormalizeLoudnessEnabled}\"", StringComparison.Ordinal), "Normalize loudness should stay visible and become disabled for unsupported models");
    AssertEqual(false, xaml.Contains("Visibility=\"{Binding ShowNormalizeLoudness", StringComparison.Ordinal), "Normalize loudness card should not be hidden for s1");
    AssertEqual(true, xaml.Contains("STranslate_Plugin_Tts_FishAudio_Engine_Description_Free", StringComparison.Ordinal), "Model description should have a free-period DynamicResource");
    AssertEqual(true, xaml.Contains("STranslate_Plugin_Tts_FishAudio_Engine_Description_Paid", StringComparison.Ordinal), "Model description should have a post-cutoff DynamicResource");
    AssertEqual(false, xaml.Contains("免费限时", StringComparison.Ordinal), "Persistent promo/model text should not be hard-coded in XAML");

    var project = File.ReadAllText(FindRepoFile(Path.Combine("STranslate.Plugin.Tts.FishAudio", "STranslate.Plugin.Tts.FishAudio.csproj")));
    AssertEqual(true, project.Contains("<Resource Include=\"s2-pro-free-promo.webp\" />", StringComparison.Ordinal), "Project should embed the local promo image as a WPF resource");
}

static void LanguageResourcesMatchApiKeyRollback()
{
    foreach (var locale in new[] { "zh-cn", "zh-tw", "en", "ja", "ko" })
    {
        var xaml = File.ReadAllText(FindRepoFile(Path.Combine(
            "STranslate.Plugin.Tts.FishAudio",
            "Languages",
            $"{locale}.xaml")));

        AssertEqual(true, xaml.Contains("STranslate_Plugin_Tts_FishAudio_Network_Unavailable", StringComparison.Ordinal), $"{locale} should define network unavailable text");
        AssertEqual(true, xaml.Contains("STranslate_Plugin_Tts_FishAudio_Request_Timeout", StringComparison.Ordinal), $"{locale} should define timeout text");
        AssertEqual(true, xaml.Contains("STranslate_Plugin_Tts_FishAudio_Engine_Description_Free", StringComparison.Ordinal), $"{locale} should define free-period model description");
        AssertEqual(true, xaml.Contains("STranslate_Plugin_Tts_FishAudio_Engine_Description_Paid", StringComparison.Ordinal), $"{locale} should define paid-period model description");
        AssertEqual(true, xaml.Contains("STranslate_Plugin_Tts_FishAudio_Promo_Close", StringComparison.Ordinal), $"{locale} should define promo close tooltip");
        AssertEqual(false, xaml.Contains("STranslate_Plugin_Tts_FishAudio_ApiKey_NotVerified", StringComparison.Ordinal), $"{locale} should remove API Key not-verified text");
        AssertEqual(false, xaml.Contains("STranslate_Plugin_Tts_FishAudio_ApiKey_Waiting", StringComparison.Ordinal), $"{locale} should remove API Key waiting text");
        AssertEqual(false, xaml.Contains("STranslate_Plugin_Tts_FishAudio_ApiKey_Applied", StringComparison.Ordinal), $"{locale} should remove API Key applied text");
    }
}

static void SettingsViewSliderTooltipsMatchDisplayedPrecision()
{
    var xaml = File.ReadAllText(FindRepoFile(Path.Combine("STranslate.Plugin.Tts.FishAudio", "View", "SettingsView.xaml")));

    AssertSliderTooltipPrecision(xaml, "Speed", "2");
    AssertSliderTooltipPrecision(xaml, "Volume", "1");
    AssertSliderTooltipPrecision(xaml, "Temperature", "2");
    AssertSliderTooltipPrecision(xaml, "TopP", "2");
}

static void LanguageResourcesDescribeContextConditioningConsistency()
{
    var expectedDescriptions = new Dictionary<string, string>
    {
        ["zh-cn"] = "使用同一次合成音频的前序片段保持声音一致性，不会使用之前生成的其他音频",
        ["zh-tw"] = "使用同一次合成音訊的前序片段保持聲音一致性，不會使用先前產生的其他音訊",
        ["en"] = "Uses earlier chunks from the same synthesis to maintain voice consistency; it does not reference previously generated audio",
        ["ja"] = "同じ合成音声内の前のチャンクを使って声の一貫性を保ち、以前に生成した別の音声は参照しません",
        ["ko"] = "같은 합성 오디오 안의 앞선 조각을 사용해 보이스 일관성을 유지하며, 이전에 생성한 다른 오디오는 참조하지 않습니다",
    };

    foreach (var (locale, expectedDescription) in expectedDescriptions)
    {
        var xaml = File.ReadAllText(FindRepoFile(Path.Combine(
            "STranslate.Plugin.Tts.FishAudio",
            "Languages",
            $"{locale}.xaml")));

        AssertEqual(
            true,
            xaml.Contains(expectedDescription, StringComparison.Ordinal),
            $"{locale} should describe context conditioning as same-synthesis voice consistency");
    }
}

static void TranslatedReadmesMatchCurrentSourceAndControlNames()
{
    var expectedContextRows = new Dictionary<string, string>
    {
        ["README_EN.md"] = "| Context conditioning | On | Uses earlier chunks from the same synthesis to maintain voice consistency; it does not reference previously generated audio. |",
        ["README_TW.md"] = "| 上下文關聯 | 開啟 | 使用同一次合成音訊的前序片段保持聲音一致性，不會使用先前產生的其他音訊。 |",
        ["README_JA.md"] = "| コンテキスト連携 | オン | 同じ合成音声内の前のチャンクを使って声の一貫性を保ち、以前に生成した別の音声は参照しません。 |",
        ["README_KO.md"] = "| 컨텍스트 연동 | 켜짐 | 같은 합성 오디오 안의 앞선 조각을 사용해 보이스 일관성을 유지하며, 이전에 생성한 다른 오디오는 참조하지 않습니다. |",
    };

    foreach (var (fileName, expectedContextRow) in expectedContextRows)
    {
        var readme = File.ReadAllText(FindRepoFile(Path.Combine("docs", fileName)));

        AssertEqual(false, readme.Contains("s2-pro-free-promo.webp", StringComparison.Ordinal), $"{fileName} should not reference the removed README promo image");
        AssertEqual(true, readme.Contains("> [!TIP]", StringComparison.Ordinal), $"{fileName} should use the source README tip callout");
        AssertEqual(false, readme.Contains("[!NOTE]", StringComparison.Ordinal), $"{fileName} should remove the random voice note from section 2.5");
        AssertEqual(true, readme.Contains(expectedContextRow, StringComparison.Ordinal), $"{fileName} should match the updated context conditioning row");
    }

    var koreanReadme = File.ReadAllText(FindRepoFile(Path.Combine("docs", "README_KO.md")));
    AssertEqual(true, koreanReadme.Contains("컨텍스트 연동", StringComparison.Ordinal), "Korean README should use the localized control name for context conditioning");
    AssertEqual(false, koreanReadme.Contains("컨텍스트 연결", StringComparison.Ordinal), "Korean README should not use a different context conditioning label");
}

static Settings CreateTtsSettings(string selectedModel) => new()
{
    ApiKey = AppliedKey,
    VoiceId = "fedcba9876543210fedcba9876543210",
    SelectedModel = selectedModel,
    Speed = 1.25,
    Volume = -2.5,
    NormalizeLoudness = false,
    Mp3Bitrate = 128,
    Temperature = 0.41,
    TopP = 0.82,
    Latency = "low",
    Normalize = true,
    ConditionOnPreviousChunks = false,
};

void CoverImageCacheUsesExistingFile()
{
    var root = CreateTempDirectory();
    try
    {
        const string voiceId = "11111111111111111111111111111111";
        var cacheDir = Path.Combine(root, CoverImageCacheService.DirectoryName);
        Directory.CreateDirectory(cacheDir);
        var cachedFile = Path.Combine(cacheDir, $"{voiceId}.jpg");
        File.WriteAllBytes(cachedFile, new byte[] { 1, 2, 3 });

        var downloadCalled = false;
        var cache = new CoverImageCacheService(root, (_, _) =>
        {
            downloadCalled = true;
            return Task.FromResult(new byte[] { 9 });
        });

        var result = cache.ResolveCoverImageUrl(voiceId, "coverimage/existing", 128);

        AssertEqual(new Uri(cachedFile).AbsoluteUri, result.DisplayUrl, "Existing cover image cache should return local file URI");
        AssertEqual(null, result.CacheTask, "Existing cover image cache should not create a download task");
        AssertEqual(false, downloadCalled, "Existing cover image cache should not download again");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

async Task CoverImageCacheCreatesMissedFileAsync()
{
    var root = CreateTempDirectory();
    try
    {
        const string voiceId = "22222222222222222222222222222222";
        const string coverImage = "coverimage/missed";
        var bytes = new byte[] { 10, 20, 30, 40 };
        var downloadCount = 0;
        string? callbackUrl = null;
        var cache = new CoverImageCacheService(root, (_, _) =>
        {
            downloadCount++;
            return Task.FromResult(bytes);
        });

        var result = cache.ResolveCoverImageUrl(voiceId, coverImage, 128, url => callbackUrl = url);
        var expectedRemoteUrl = "https://public-platform.r2.fish.audio/cdn-cgi/image/width=128,format=auto/coverimage/missed";

        AssertEqual(expectedRemoteUrl, result.DisplayUrl, "Missing cover image cache should return remote URL immediately");
        AssertNotNull(result.CacheTask, "Missing cover image cache should create a download task");

        await result.CacheTask!;

        var cachedFile = Path.Combine(root, CoverImageCacheService.DirectoryName, $"{voiceId}.jpg");
        AssertEqual(true, File.Exists(cachedFile), "Missing cover image cache should create <id>.jpg");
        AssertEqual(bytes.Length, new FileInfo(cachedFile).Length, "Cached cover image file should contain downloaded bytes");
        AssertEqual(1, downloadCount, "Missing cover image cache should download once");
        AssertEqual(new Uri(cachedFile).AbsoluteUri, callbackUrl, "Missing cover image cache should notify with local file URI");
        AssertEqual($"{bytes.Length} B", cache.GetFormattedCacheSize(), "Cache size should include newly downloaded cover image");

        var secondResult = cache.ResolveCoverImageUrl(voiceId, coverImage, 128);
        AssertEqual(new Uri(cachedFile).AbsoluteUri, secondResult.DisplayUrl, "Created cover image cache should be reused");
        AssertEqual(null, secondResult.CacheTask, "Created cover image cache should not create another download task");
        AssertEqual(1, downloadCount, "Created cover image cache should not download again");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

void CoverImageCacheClearsOnlyCoverImagesAndFormatsSize()
{
    var root = CreateTempDirectory();
    try
    {
        const string voiceId = "33333333333333333333333333333333";
        var cacheDir = Path.Combine(root, CoverImageCacheService.DirectoryName);
        Directory.CreateDirectory(cacheDir);
        File.WriteAllBytes(Path.Combine(cacheDir, $"{voiceId}.jpg"), new byte[] { 1, 2, 3 });
        var unrelated = Path.Combine(root, "keep.txt");
        File.WriteAllText(unrelated, "keep");

        var cache = new CoverImageCacheService(root, (_, _) => Task.FromResult(new byte[] { 9 }));

        AssertEqual("3 B", cache.GetFormattedCacheSize(), "Cache size should count cover image files");
        cache.Clear();

        AssertEqual(false, File.Exists(Path.Combine(cacheDir, $"{voiceId}.jpg")), "Clear should remove cover image cache files");
        AssertEqual(true, File.Exists(unrelated), "Clear should not remove files outside cover_images");
        AssertEqual("0 B", cache.GetFormattedCacheSize(), "Cache size should refresh after clear");
        AssertEqual("1 KB", CoverImageCacheService.FormatBytes(1024), "Size formatter should use KB");
        AssertEqual("1.5 KB", CoverImageCacheService.FormatBytes(1536), "Size formatter should keep one decimal when needed");
        AssertEqual("1 MB", CoverImageCacheService.FormatBytes(1024 * 1024), "Size formatter should use MB");
        AssertEqual("1 TB", CoverImageCacheService.FormatBytes(1024L * 1024 * 1024 * 1024), "Size formatter should use TB");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

void CoverImageCacheSizeScansCoverImagesDirectory()
{
    var root = CreateTempDirectory();
    try
    {
        const string voiceId = "44444444444444444444444444444444";
        var cacheDir = Path.Combine(root, CoverImageCacheService.DirectoryName);
        Directory.CreateDirectory(cacheDir);

        var cache = new CoverImageCacheService(root, (_, _) => Task.FromResult(new byte[] { 9 }));
        AssertEqual("0 B", cache.GetFormattedCacheSize(), "Empty cover image cache should report zero bytes");

        var cachedFile = Path.Combine(cacheDir, $"{voiceId}.jpg");
        File.WriteAllBytes(cachedFile, new byte[] { 1, 2, 3, 4, 5 });

        AssertEqual("5 B", cache.GetFormattedCacheSize(), "Cache size should scan cover_images for files created outside the in-memory index");

        File.Delete(cachedFile);
        AssertEqual("0 B", cache.GetFormattedCacheSize(), "Cache size should scan cover_images after files are removed outside the in-memory index");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

async Task ClearCoverImageCacheCommandTracksBusyStateAsync()
{
    var clearStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseClear = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var viewModel = new SettingsViewModel(
        CreateContext(),
        new Settings(),
        null,
        () =>
        {
            clearStarted.SetResult();
            return releaseClear.Task;
        },
        TimeSpan.FromSeconds(5));

    var commandTask = viewModel.ClearCoverImageCacheCommand.ExecuteAsync(null);
    await clearStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(true, viewModel.IsClearingCoverImageCache, "Clear cache command should enter busy state while cleanup is running");
    AssertEqual(false, viewModel.ClearCoverImageCacheCommand.CanExecute(null), "Clear cache command should be disabled while cleanup is running");

    releaseClear.SetResult();
    await commandTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(false, viewModel.IsClearingCoverImageCache, "Clear cache command should leave busy state after cleanup completes");
    AssertEqual(true, viewModel.ClearCoverImageCacheCommand.CanExecute(null), "Clear cache command should be enabled after cleanup completes");
}

async Task ClearCoverImageCacheCommandTimesOutAndRestoresButtonAsync()
{
    var snackbar = new TestSnackbar();
    var blockedClear = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var viewModel = new SettingsViewModel(
        CreateContext(snackbar),
        new Settings(),
        null,
        () => blockedClear.Task,
        TimeSpan.FromMilliseconds(30));

    await viewModel.ClearCoverImageCacheCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(false, viewModel.IsClearingCoverImageCache, "Clear cache timeout should leave busy state");
    AssertEqual(true, viewModel.ClearCoverImageCacheCommand.CanExecute(null), "Clear cache timeout should re-enable the command");
    AssertEqual(
        "STranslate_Plugin_Tts_FishAudio_ClearCache_Timeout",
        snackbar.LastError,
        "Clear cache timeout should show a localized error message");

    blockedClear.SetResult();
    await Task.Delay(30);
}

async Task LateClearCoverImageCacheTaskDoesNotUnlockNewOperationAsync()
{
    var clearCall = 0;
    var firstClear = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var secondClear = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var viewModel = new SettingsViewModel(
        CreateContext(),
        new Settings(),
        null,
        () => ++clearCall == 1 ? firstClear.Task : secondClear.Task,
        TimeSpan.FromMilliseconds(150));

    await viewModel.ClearCoverImageCacheCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));
    AssertEqual(false, viewModel.IsClearingCoverImageCache, "First clear cache timeout should restore the button");

    var secondCommandTask = viewModel.ClearCoverImageCacheCommand.ExecuteAsync(null);
    AssertEqual(true, viewModel.IsClearingCoverImageCache, "Second clear cache operation should enter busy state");

    firstClear.SetResult();
    await Task.Delay(30);
    AssertEqual(true, viewModel.IsClearingCoverImageCache, "Late first clear task should not unlock the second operation");

    secondClear.SetResult();
    await secondCommandTask.WaitAsync(TimeSpan.FromSeconds(2));
    AssertEqual(false, viewModel.IsClearingCoverImageCache, "Second clear cache operation should leave busy state after completion");
}

static void ClearCacheButtonUsesLocalizedTextAndBusySpinner()
{
    var xaml = File.ReadAllText(FindRepoFile(Path.Combine("STranslate.Plugin.Tts.FishAudio", "View", "SettingsView.xaml")));
    const string commandBinding = "Command=\"{Binding ClearCoverImageCacheCommand}\"";
    var commandIndex = xaml.IndexOf(commandBinding, StringComparison.Ordinal);
    AssertEqual(true, commandIndex >= 0, "Settings view should bind a clear cover image cache button");

    var buttonStart = xaml.LastIndexOf("<Button", commandIndex, StringComparison.Ordinal);
    var buttonEnd = xaml.IndexOf("</Button>", commandIndex, StringComparison.Ordinal);
    AssertEqual(true, buttonStart >= 0 && buttonEnd > commandIndex, "Clear cover image cache command should belong to a button element");

    var buttonXaml = xaml.Substring(buttonStart, buttonEnd - buttonStart);
    AssertEqual(
        true,
        buttonXaml.Contains("Text=\"{DynamicResource STranslate_Plugin_Tts_FishAudio_ClearCache}\"", StringComparison.Ordinal),
        "Clear cache button should show explicit localized text");
    AssertEqual(
        true,
        buttonXaml.Contains("IsClearingCoverImageCache", StringComparison.Ordinal)
        && buttonXaml.Contains("<DoubleAnimation", StringComparison.Ordinal)
        && buttonXaml.Contains("RepeatBehavior=\"Forever\"", StringComparison.Ordinal),
        "Clear cache button should show a rotating busy indicator while clearing");
    AssertEqual(
        false,
        buttonXaml.Contains("Content=\"{DynamicResource STranslate_Plugin_Tts_FishAudio_ClearCache}\"", StringComparison.Ordinal),
        "Clear cache button content should be composed so text and busy indicator can be shown together");
    AssertEqual(
        false,
        buttonXaml.Contains("FontFamily=\"Segoe MDL2 Assets\"", StringComparison.Ordinal),
        "Clear cache button should not be icon-only");
}

static IPluginContext CreateContext(
    ISnackbar? snackbar = null,
    Settings? settings = null,
    IHttpService? httpService = null,
    IAudioPlayer? audioPlayer = null,
    ILogger? logger = null)
{
    var context = DispatchProxy.Create<IPluginContext, ContextProxy>();
    var proxy = (ContextProxy)(object)context;
    proxy.Snackbar = snackbar ?? new TestSnackbar();
    proxy.Settings = settings ?? new Settings();
    proxy.HttpService = httpService;
    proxy.AudioPlayer = audioPlayer;
    proxy.Logger = logger ?? new TestLogger();
    return context;
}

static string FindRepoFile(string relativePath)
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, relativePath);
        if (File.Exists(candidate))
            return candidate;

        directory = directory.Parent;
    }

    throw new FileNotFoundException($"Could not locate repository file: {relativePath}");
}

static string FindRepoPath(string relativePath)
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, relativePath);
        if (File.Exists(candidate) || Directory.Exists(candidate))
            return candidate;

        directory = directory.Parent;
    }

    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (root.Parent is not null)
        root = root.Parent;
    return Path.Combine(root.FullName, relativePath);
}

static string CreateTempDirectory()
{
    var path = Path.Combine(Path.GetTempPath(), "FishAudioTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static int CountOccurrences(string text, string value)
{
    var count = 0;
    var index = 0;
    while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
    {
        count++;
        index += value.Length;
    }

    return count;
}

static void AssertSliderTooltipPrecision(string xaml, string bindingName, string expectedPrecision)
{
    var bindingIndex = xaml.IndexOf($"Value=\"{{Binding {bindingName}}}\"", StringComparison.Ordinal);
    AssertEqual(true, bindingIndex >= 0, $"{bindingName} slider should bind to {bindingName}");

    var sliderStart = xaml.LastIndexOf("<Slider", bindingIndex, StringComparison.Ordinal);
    var sliderEnd = xaml.IndexOf("/>", bindingIndex, StringComparison.Ordinal);
    AssertEqual(true, sliderStart >= 0 && sliderEnd > bindingIndex, $"{bindingName} binding should be inside a Slider element");

    var sliderXaml = xaml.Substring(sliderStart, sliderEnd - sliderStart);
    AssertEqual(
        true,
        sliderXaml.Contains($"AutoToolTipPrecision=\"{expectedPrecision}\"", StringComparison.Ordinal),
        $"{bindingName} slider tooltip should show {expectedPrecision} decimal places");
}

static NetworkAvailabilityOverride OverrideNetworkAvailability(bool available)
{
    var current = FishAudioRuntime.NetworkAvailableOverride;
    var state = new NetworkAvailabilityOverride(current, available);
    FishAudioRuntime.NetworkAvailableOverride = () => state.IsAvailable;
    return state;
}

static LocalUtcNowOverride OverrideLocalUtcNow(DateTimeOffset nowUtc)
{
    var current = FishAudioRuntime.LocalUtcNowOverride;
    FishAudioRuntime.LocalUtcNowOverride = () => nowUtc;
    return new LocalUtcNowOverride(current);
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}. Expected: {expected}; Actual: {actual}");
}

static void AssertNotNull(object? value, string message)
{
    if (value is null)
        throw new InvalidOperationException(message);
}

static void AssertSequenceEqual(byte[] expected, byte[]? actual, string message)
{
    if (actual is null || !expected.SequenceEqual(actual))
        throw new InvalidOperationException($"{message}. Expected: [{string.Join(", ", expected)}]; Actual: [{(actual is null ? "null" : string.Join(", ", actual))}]");
}

static void AssertEnumerableEqual<T>(IEnumerable<T> expected, IEnumerable<T>? actual, string message)
{
    if (actual is null || !expected.SequenceEqual(actual))
        throw new InvalidOperationException($"{message}. Expected: [{string.Join(", ", expected)}]; Actual: [{(actual is null ? "null" : string.Join(", ", actual))}]");
}

static Dictionary<string, object> AssertDictionary(object? value, string message)
{
    if (value is Dictionary<string, object> dictionary)
        return dictionary;

    throw new InvalidOperationException(message);
}

static Dictionary<string, string> AssertHeaders(Options? options, string message)
{
    if (options?.Headers is { } headers)
        return headers;

    throw new InvalidOperationException(message);
}

public class ContextProxy : DispatchProxy
{
    public ISnackbar Snackbar { get; set; } = new TestSnackbar();
    public Settings Settings { get; set; } = new();
    public IHttpService? HttpService { get; set; }
    public IAudioPlayer? AudioPlayer { get; set; }
    public ILogger Logger { get; set; } = new TestLogger();
    public int SaveCount { get; private set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            return null;

        if (targetMethod.Name == "get_Snackbar")
            return Snackbar;

        if (targetMethod.Name == "get_HttpService")
            return HttpService;

        if (targetMethod.Name == "get_AudioPlayer")
            return AudioPlayer;

        if (targetMethod.Name == "get_Logger")
            return Logger;

        if (targetMethod.Name == nameof(IPluginContext.GetTranslation))
            return args?[0]?.ToString() ?? "";

        if (targetMethod.Name == nameof(IPluginContext.LoadSettingStorage)
            && targetMethod.IsGenericMethod
            && targetMethod.GetGenericArguments()[0] == typeof(Settings))
        {
            return Settings;
        }

        if (targetMethod.Name == nameof(IPluginContext.SaveSettingStorage))
        {
            SaveCount++;
            return null;
        }

        return GetDefault(targetMethod.ReturnType);
    }

    private static object? GetDefault(Type type)
    {
        if (type == typeof(void))
            return null;

        if (type == typeof(Task))
            return Task.CompletedTask;

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = type.GetGenericArguments()[0];
            var result = GetDefault(resultType);
            return typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, new[] { result });
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}

public class TestHttpServiceProxy : DispatchProxy
{
    public int GetCallCount { get; private set; }
    public int GetAsBytesCallCount { get; private set; }
    public int PostAsBytesCallCount { get; private set; }
    public string GetResponseJson { get; set; } = "{\"credit\":\"1.00\"}";
    public byte[] GetBytesResponse { get; set; } = new byte[] { 9 };
    public byte[] PostBytes { get; set; } = new byte[] { 1 };
    public Exception? GetException { get; set; }
    public Exception? PostException { get; set; }
    public Func<string, Options?, CancellationToken, Task<string>>? GetAsyncHandler { get; set; }
    public string? LastGetUrl { get; private set; }
    public Options? LastGetOptions { get; private set; }
    public string? LastPostUrl { get; private set; }
    public object? LastPostBody { get; private set; }
    public Options? LastPostOptions { get; private set; }

    public static (IHttpService Service, TestHttpServiceProxy Proxy) Create()
    {
        var service = DispatchProxy.Create<IHttpService, TestHttpServiceProxy>();
        return (service, (TestHttpServiceProxy)(object)service);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            return null;

        if (targetMethod.Name == nameof(IHttpService.GetAsync)
            && targetMethod.ReturnType == typeof(Task<string>))
        {
            GetCallCount++;
            LastGetUrl = GetStringArgument(args, args?.Length == 4 ? 1 : 0);
            LastGetOptions = GetOptionsArgument(args);
            var ct = GetCancellationTokenArgument(args);

            if (GetAsyncHandler is not null)
                return GetAsyncHandler(LastGetUrl ?? "", LastGetOptions, ct);

            return GetException is not null
                ? Task.FromException<string>(GetException)
                : Task.FromResult(GetResponseJson);
        }

        if (targetMethod.Name == nameof(IHttpService.GetAsBytesAsync)
            && targetMethod.ReturnType == typeof(Task<byte[]>))
        {
            GetAsBytesCallCount++;
            return Task.FromResult(GetBytesResponse);
        }

        if (targetMethod.Name == nameof(IHttpService.PostAsBytesAsync)
            && targetMethod.ReturnType == typeof(Task<byte[]>))
        {
            PostAsBytesCallCount++;
            var hasClientName = args?.Length == 5;
            LastPostUrl = GetStringArgument(args, hasClientName ? 1 : 0);
            LastPostBody = args?[hasClientName ? 2 : 1];
            LastPostOptions = args?[hasClientName ? 3 : 2] as Options;
            return PostException is not null
                ? Task.FromException<byte[]>(PostException)
                : Task.FromResult(PostBytes);
        }

        return GetDefault(targetMethod.ReturnType);
    }

    private static string? GetStringArgument(object?[]? args, int index) =>
        args is not null && args.Length > index ? args[index] as string : null;

    private static Options? GetOptionsArgument(object?[]? args) =>
        args?.OfType<Options>().FirstOrDefault();

    private static CancellationToken GetCancellationTokenArgument(object?[]? args) =>
        args?.OfType<CancellationToken>().FirstOrDefault() ?? CancellationToken.None;

    private static object? GetDefault(Type type)
    {
        if (type == typeof(void))
            return null;

        if (type == typeof(Task))
            return Task.CompletedTask;

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = type.GetGenericArguments()[0];
            var result = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
            return typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, new[] { result });
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}

public sealed class TestAudioPlayer : IAudioPlayer
{
    public int PlayBytesCallCount { get; private set; }
    public int PlayUrlCallCount { get; private set; }
    public byte[]? LastPlayedBytes { get; private set; }
    public string? LastPlayedUrl { get; private set; }

    public Task PlayAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        PlayBytesCallCount++;
        LastPlayedBytes = bytes;
        return Task.CompletedTask;
    }

    public Task PlayAsync(string url, CancellationToken cancellationToken)
    {
        PlayUrlCallCount++;
        LastPlayedUrl = url;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}

public sealed class TestSnackbar : ISnackbar
{
    public string? LastError { get; private set; }
    public string? LastWarning { get; private set; }

    public void Show(string message, Severity severity, int duration, string? actionLabel, Action? action)
    {
    }

    public void ShowSuccess(string message, int duration)
    {
    }

    public void ShowError(string message, int duration)
    {
        LastError = message;
    }

    public void ShowWarning(string message, int duration)
    {
        LastWarning = message;
    }

    public void ShowInfo(string message, int duration)
    {
    }

    public void Clear()
    {
        LastError = null;
        LastWarning = null;
    }
}

public sealed class TestLogger : ILogger
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _entries.Add((logLevel, formatter(state, exception)));
    }

    public bool Contains(LogLevel level, string messagePart) =>
        _entries.Any(e => e.Level == level && e.Message.Contains(messagePart, StringComparison.OrdinalIgnoreCase));

    public bool Contains(string messagePart) =>
        _entries.Any(e => e.Message.Contains(messagePart, StringComparison.OrdinalIgnoreCase));

    public int Count(LogLevel level) => _entries.Count(e => e.Level == level);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}

public sealed class NetworkAvailabilityOverride : IDisposable
{
    private readonly Func<bool>? _previous;
    private bool _isAvailable;

    public NetworkAvailabilityOverride(Func<bool>? previous, bool isAvailable)
    {
        _previous = previous;
        _isAvailable = isAvailable;
    }

    public bool IsAvailable => _isAvailable;

    public void Set(bool isAvailable) => _isAvailable = isAvailable;

    public void Dispose() => FishAudioRuntime.NetworkAvailableOverride = _previous;
}

public sealed class LocalUtcNowOverride : IDisposable
{
    private readonly Func<DateTimeOffset>? _previous;

    public LocalUtcNowOverride(Func<DateTimeOffset>? previous)
    {
        _previous = previous;
    }

    public void Dispose() => FishAudioRuntime.LocalUtcNowOverride = _previous;
}
