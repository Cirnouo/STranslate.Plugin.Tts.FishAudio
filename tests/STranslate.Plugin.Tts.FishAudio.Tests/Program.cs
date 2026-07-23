using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.Service;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

const string AppliedKey = "0123456789abcdef0123456789abcdef";
const string DraftKey = "abcdef0123456789abcdef0123456789";

PackageReferenceUsesSdk1012();
NewSettingsSerializeCurrentSchemaFirst();
Version100SettingsMigrate();
Version101SettingsMigrate();
Version102UnversionedSettingsMigrate();
CurrentVoiceFieldsWinRegardlessOfPropertyOrder();
InvalidCurrentVoiceFieldsFallBackToLegacy();
WrongJsonTypeDefaultsOnlyThatField();
WrongSchemaTypeDefaultsOnlySchemaField();
UnknownAndDeletedPropertiesAreCleanedUp();
NumericSettingsNormalizeRangesAndGranularity();
OrdinaryDuplicateDefaultsOnlyThatField();
DuplicateAndFutureSchemasAreReadOnly();
CurrentSettingsRoundTrip();
ProductionUsesSettingsStoreForContextStorage();
ApiKeyValidationStateWasRemoved();
SettingsViewModelSplitsPreviewAndCoverCacheResponsibilities();
OperationActivityCounterLinearizesStateTransitions();
OperationActivityCounterPublishesOutsideGateAcrossThreads();
OperationActivityCounterRecoveryFailureLeavesPublisherReusable();
OperationActivityCounterPublishesPendingStateBeforeRethrowingPublicationFailure();
await OverlappingStartupManualAndTtsOperationsUnlockAfterFinalCompletionAsync();
ApiKeyOperationCounterHandlesPropertyChangingReentrancy();
await ApiKeyOperationCounterHandlesPropertyChangedReentrancyAsync();
ApiKeyEditingPersistsImmediately();
await StartupCreditRefreshPreflightSkipsRequestAsync();
await StartupCreditRefreshRequestsOncePerInitializationAsync();
await CompletedStartupCreditRefreshAppliesWhenSettingsViewModelIsCreatedAsync();
await PendingStartupCreditRefreshLocksSettingsUntilCompletionAsync();
await FailedStartupCreditRefreshKeepsPlaceholderWithoutSnackbarAsync();
await NullStartupCreditKeepsNotLoadedStateAsync();
await StartupCreditRefreshIgnoresResultAfterApiKeyChangesAsync();
await ReinitializedStartupCreditRefreshIgnoresOldCycleAsync();
await ReinitializedStartupCreditRefreshRemainsActiveDuringReloadAsync();
await ReinitializedSettingsViewUsesNewContextAndSettingsAsync();
await ReinitializationRetiresOldViewModelWithoutSavingSharedBackingStoreAsync();
await StartupSettingsSaveWinsBeforeFailedReplacementTransitionAsync();
FailedSameInstanceNormalizationRestoresOldStateAndBackingStore();
await SettingsViewModelCreationWaitsForReplacementCommitAsync();
SameSettingsInstanceSeparatesOldAndNewViewModelWriteLeases();
await FailedReinitializationRestoresOldViewModelWriteLeaseAsync();
DisposeRetiresSettingsWriteLease();
await OvertakenInitializationSkipsSettingsLoadAsync();
await ReinitializationWaitsForInFlightViewModelSaveAsync();
await DisposedSettingsViewModelIgnoresStartupCreditResultAsync();
ModelPolicyUsesCutoffDefaultsAndNormalizeLoudnessSupport();
FreeModelDeadlineDocumentationIsConsistent();
MainInitNormalizesStructuredSettingsWithoutClearingKeys();
await StartupRefreshSelectedVoiceMetadataAsync();
await StartupVoiceUiApplyRechecksExpectedVoiceIdAsync();
await DelayedStartupVoiceRefreshDoesNotRestoreClearedVoiceAsync();
await DelayedStartupVoiceRefreshDoesNotOverwriteNewSelectionAsync();
await ConcurrentPaidModelSelectionStillPublishesPostCutoffPolicyAsync();
await StartupModelSaveFailureRollsBackRuntimeAndStorageAsync();
await StartupVoiceSaveFailureRollsBackRuntimeAndStorageAsync();
await PublishedReplacementInvalidatesOldStartupCreditImmediatelyAsync();
await StartupModelNormalizationInvalidatesSpeculativeSettingsViewModelAsync();
await CommittedStartupSettingsInvalidatesPostReservationViewModelCandidateAsync();
await StartupVoiceRefreshInvalidatesSpeculativeSettingsViewModelAsync();
await StartupSettingsSaveSkipsAfterNewInitializationRequestAsync();
await InitializationLoadWaitsForAuthorizedStartupSettingsSaveAsync();
await FailedReinitializationRestoresStartupSettingsSaveAuthorizationAsync();
await StartupRefreshDisposeCancelsPendingWorkWithoutLoggingAsync();
await StartupRefreshCancellationAfterResponseSkipsSideEffectsAsync();
await ReinitializedStartupRefreshCannotMutateNewSettingsAsync();
ApplyAvailableModelsUsesUiThreadInvoker();
await PlayAudioUsesOldInitializationWhileReinitLoadIsBlockedAsync();
await PlayAudioUsesInitializationSnapshotAcrossReinitAsync();
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
await VoiceLookupCompletionsAfterDisposeDoNotMutateStateAsync();
await SearchPaginationUpdatesVisiblePageAfterSuccessOnlyAsync();
await PostTtsRequestHonorsModelSpecificProsodyAndTimeoutAsync();
PreviewAudioUrlValidationAllowsOnlyFishAudioStorageHosts();
PreviewAudioRejectsInvalidSearchUrlWithoutStartingPlayback();
await DisplayPreviewRefreshesCachedVoiceAndPlaysLatestUrlAsync();
await DisplayPreviewOfflinePreflightStopsBeforeRefreshAsync();
await DisplayPreviewRechecksNetworkBeforePlaybackAsync();
SearchPreviewOfflinePreflightStopsBeforePlayback();
SearchPreviewUsesListUrlWithoutDetailRefresh();
SearchPreviewSecondClickStopsWithoutNetwork();
await DisplayPreviewRefreshFailureSilentlyFallsBackAsync();
await DisplayPreviewNotFoundSilentlyFallsBackAsync();
await DisplayPreviewLatestVoiceWithoutSampleShowsUnavailableAsync();
await DisplayPreviewSecondClickStopsWithoutNetworkAsync();
await DisplayPreviewSecondClickCancelsPendingRefreshWithoutRestartAsync();
PreviewPlaybackFailureUsesNetworkAwareMessage();
await SearchPreviewInvalidatesPendingDisplayRefreshAsync();
await DisplayPreviewVoiceSwitchIgnoresLateRefreshAsync();
await DisplayPreviewDisposeCancelsAndIgnoresLateRefreshAsync();
PromoStatePersistsDismissalAndUse();
SettingsViewRemovesApiKeyValidationUiAndUsesLatencyBars();
SettingsViewShowsPersistentCreditPlaceholder();
SettingsViewIncludesS21PromoAndDynamicModelDescriptions();
LanguageResourcesMatchApiKeyRollback();
PreviewFailureLanguageResourcesAreComplete();
SettingsViewSliderTooltipsMatchDisplayedPrecision();
LanguageResourcesDescribeContextConditioningConsistency();
TranslatedReadmesMatchCurrentSourceAndControlNames();
CoverImageCacheUsesExistingFile();
await CoverImageCacheCreatesMissedFileAsync();
await CoverImageCacheRejectsInvalidDownloadsAsync();
await CoverImageCacheCancelsSlowDownloadAfterTimeoutAsync();
await CoverImageCacheDownloadsThroughBoundedStreamAsync();
CoverImageCacheClearsOnlyCoverImagesAndFormatsSize();
CoverImageCacheSizeScansCoverImagesDirectory();
await ClearCoverImageCacheCommandTracksBusyStateAsync();
await ClearCoverImageCacheCommandTimesOutAndRestoresButtonAsync();
await LateClearCoverImageCacheTaskDoesNotUnlockNewOperationAsync();
ClearCacheButtonUsesLocalizedTextAndBusySpinner();

Console.WriteLine("Fish Audio plugin regression tests passed.");

static void NewSettingsSerializeCurrentSchemaFirst()
{
    var settings = new Settings();
    var json = JsonSerializer.Serialize(settings);

    AssertEqual(SettingsSchema.Current, settings.SchemaVersion, "New settings should use the current schema");
    AssertEqual(
        true,
        json.StartsWith($"{{\"SchemaVersion\":{SettingsSchema.Current},", StringComparison.Ordinal),
        "SchemaVersion should be the first serialized settings property");
}

static void Version100SettingsMigrate()
{
    var settings = DeserializeSettings(
        """
        {
          "ApiKey": "v100-key",
          "ReferenceId": "00112233445566778899aabbccddeeff",
          "SelectedModel": "s2-pro",
          "Speed": 1.2,
          "Volume": -1.5,
          "NormalizeLoudness": false,
          "Temperature": 0.4,
          "TopP": 0.8,
          "Latency": "low",
          "Normalize": true,
          "CachedModel": {
            "Id": "deleted-id",
            "Title": "Version 1.0.0 Voice",
            "CoverImage": "cover/v100",
            "AuthorName": "Author 100",
            "AuthorAvatar": "deleted-avatar",
            "SampleAudioUrl": "https://platform.r2.fish.audio/v100.mp3",
            "SampleText": "deleted sample text"
          }
        }
        """);

    AssertEqual(SettingsSchema.Current, settings.SchemaVersion, "v1.0.0 settings should migrate to the current schema");
    AssertEqual("00112233445566778899aabbccddeeff", settings.VoiceId, "v1.0.0 ReferenceId should migrate to VoiceId");
    AssertEqual("Version 1.0.0 Voice", settings.CachedVoice?.Title, "v1.0.0 CachedModel should migrate to CachedVoice");
    AssertEqual("", settings.CachedVoice?.Description, "Missing v1.0.0 cached voice description should use its default");
    AssertEqual(0, settings.CachedVoice?.TaskCount, "Missing v1.0.0 task count should use its default");
    AssertEqual(Settings.DefaultMp3Bitrate, settings.Mp3Bitrate, "Missing v1.0.0 bitrate should use its default");
    AssertEqual(Settings.DefaultConditionOnPreviousChunks, settings.ConditionOnPreviousChunks, "Missing v1.0.0 context setting should use its default");
}

static void Version101SettingsMigrate()
{
    var settings = DeserializeSettings(
        """
        {
          "ApiKey": "v101-key",
          "ReferenceId": "11223344556677889900aabbccddeeff",
          "SelectedModel": "s2-pro",
          "Speed": 0.85,
          "Volume": 2.1,
          "NormalizeLoudness": true,
          "Temperature": 0.65,
          "TopP": 0.55,
          "Latency": "balanced",
          "Normalize": false,
          "CachedModel": {
            "Title": "Version 1.0.1 Voice",
            "CoverImage": "cover/v101",
            "AuthorName": "Author 101",
            "TaskCount": 31,
            "SampleAudioUrl": "https://platform.r2.fish.audio/v101.mp3"
          }
        }
        """);

    AssertEqual("11223344556677889900aabbccddeeff", settings.VoiceId, "v1.0.1 ReferenceId should migrate to VoiceId");
    AssertEqual("Version 1.0.1 Voice", settings.CachedVoice?.Title, "v1.0.1 CachedModel should migrate to CachedVoice");
    AssertEqual(31, settings.CachedVoice?.TaskCount, "v1.0.1 cached task count should be preserved");
    AssertEqual(Settings.DefaultMp3Bitrate, settings.Mp3Bitrate, "Missing v1.0.1 bitrate should use its default");
}

static void Version102UnversionedSettingsMigrate()
{
    var settings = DeserializeSettings(
        """
        {
          "ApiKey": "v102-key",
          "VoiceId": "22334455667788990011aabbccddeeff",
          "SelectedModel": "s2-pro",
          "Speed": 1.35,
          "Volume": -0.7,
          "NormalizeLoudness": false,
          "Temperature": 0.5,
          "TopP": 0.9,
          "Latency": "normal",
          "Normalize": true,
          "Mp3Bitrate": 128,
          "ConditionOnPreviousChunks": false,
          "CachedVoice": {
            "Title": "Version 1.0.2 Voice",
            "Description": "Current voice metadata",
            "CoverImage": "cover/v102",
            "AuthorName": "Author 102",
            "TaskCount": 52,
            "SampleAudioUrl": "https://platform.r2.fish.audio/v102.mp3"
          }
        }
        """);

    AssertEqual(SettingsSchema.Current, settings.SchemaVersion, "Unversioned v1.0.2 settings should migrate to the current schema");
    AssertEqual("22334455667788990011aabbccddeeff", settings.VoiceId, "Unversioned current VoiceId should be preserved");
    AssertEqual("Current voice metadata", settings.CachedVoice?.Description, "Unversioned current CachedVoice should be preserved");
    AssertEqual(128, settings.Mp3Bitrate, "v1.0.2 bitrate should be preserved");
    AssertEqual(false, settings.ConditionOnPreviousChunks, "v1.0.2 context setting should be preserved");
}

static void CurrentVoiceFieldsWinRegardlessOfPropertyOrder()
{
    AssertCurrentWins(
        """
        {
          "ReferenceId": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "VoiceId": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
          "CachedModel": { "Title": "Legacy" },
          "CachedVoice": { "Title": "Current" }
        }
        """);
    AssertCurrentWins(
        """
        {
          "CachedVoice": { "Title": "Current" },
          "CachedModel": { "Title": "Legacy" },
          "VoiceId": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
          "ReferenceId": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        }
        """);

    static void AssertCurrentWins(string json)
    {
        var settings = DeserializeSettings(json);
        AssertEqual("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", settings.VoiceId, "Valid current VoiceId should win over ReferenceId regardless of order");
        AssertEqual("Current", settings.CachedVoice?.Title, "Valid current CachedVoice should win over CachedModel regardless of order");
    }
}

static void InvalidCurrentVoiceFieldsFallBackToLegacy()
{
    var settings = DeserializeSettings(
        """
        {
          "VoiceId": 123,
          "ReferenceId": "cccccccccccccccccccccccccccccccc",
          "CachedVoice": ["wrong"],
          "CachedModel": { "Title": "Legacy Fallback", "TaskCount": 8 }
        }
        """);

    AssertEqual("cccccccccccccccccccccccccccccccc", settings.VoiceId, "Wrong-type VoiceId should fall back to valid ReferenceId");
    AssertEqual("Legacy Fallback", settings.CachedVoice?.Title, "Wrong-type CachedVoice should fall back to valid CachedModel");
    AssertEqual(8, settings.CachedVoice?.TaskCount, "Fallback cached voice metadata should be preserved");
}

static void WrongJsonTypeDefaultsOnlyThatField()
{
    var settings = DeserializeSettings(
        """
        {
          "SchemaVersion": 1,
          "ApiKey": { "wrong": true },
          "VoiceId": "malformed but intentionally preserved",
          "SelectedModel": "s1",
          "Speed": 1.25,
          "Volume": -2.0,
          "NormalizeLoudness": false
        }
        """);

    AssertEqual("", settings.ApiKey, "Wrong-type API Key should use only the API Key default");
    AssertEqual("malformed but intentionally preserved", settings.VoiceId, "Malformed string VoiceId should remain unchanged");
    AssertEqual("s1", settings.SelectedModel, "A neighboring valid model should remain unchanged");
    AssertEqual(1.25, settings.Speed, "A neighboring valid speed should remain unchanged");
    AssertEqual(-2.0, settings.Volume, "A neighboring valid volume should remain unchanged");
    AssertEqual(false, settings.NormalizeLoudness, "A neighboring valid boolean should remain unchanged");
}

static void WrongSchemaTypeDefaultsOnlySchemaField()
{
    var settings = DeserializeSettings(
        """
        {
          "SchemaVersion": "wrong",
          "ApiKey": "preserved-key-text",
          "VoiceId": "preserved-voice-text",
          "SelectedModel": "s2-pro"
        }
        """);
    var context = CreateContext(settings: settings);
    var proxy = (ContextProxy)(object)context;

    SettingsStore.Load(context, FishAudioRuntime.FreeModelCutoffUtc.AddTicks(-1));

    AssertEqual(SettingsSchema.Current, settings.SchemaVersion, "Wrong-type SchemaVersion should default through legacy migration to current");
    AssertEqual(false, settings.IsReadOnly, "A single wrong-type SchemaVersion should not make settings read-only");
    AssertEqual("preserved-key-text", settings.ApiKey, "Wrong-type SchemaVersion should not reset API Key");
    AssertEqual("preserved-voice-text", settings.VoiceId, "Wrong-type SchemaVersion should not reset VoiceId");
    AssertEqual("s2-pro", settings.SelectedModel, "Wrong-type SchemaVersion should not reset model");
    AssertEqual(1, proxy.SaveCount, "Wrong-type SchemaVersion should trigger canonical save");
}

static void UnknownAndDeletedPropertiesAreCleanedUp()
{
    var settings = DeserializeSettings(
        """
        {
          "ReferenceId": "dddddddddddddddddddddddddddddddd",
          "DeletedTopLevel": "remove me",
          "CachedModel": {
            "Id": "remove me",
            "Title": "Migrated",
            "AuthorAvatar": "remove me",
            "SampleText": "remove me"
          }
        }
        """);
    var context = CreateContext(settings: settings);
    var proxy = (ContextProxy)(object)context;

    var loaded = SettingsStore.Load(context, FishAudioRuntime.FreeModelCutoffUtc.AddTicks(-1));
    var canonicalJson = JsonSerializer.Serialize(loaded);

    AssertEqual(1, proxy.SaveCount, "Supported settings with legacy or unknown properties should be canonically saved once");
    AssertEqual(false, canonicalJson.Contains("ReferenceId", StringComparison.Ordinal), "Canonical settings should drop ReferenceId");
    AssertEqual(false, canonicalJson.Contains("CachedModel", StringComparison.Ordinal), "Canonical settings should drop CachedModel");
    AssertEqual(false, canonicalJson.Contains("DeletedTopLevel", StringComparison.Ordinal), "Canonical settings should drop unknown top-level properties");
    AssertEqual(false, canonicalJson.Contains("AuthorAvatar", StringComparison.Ordinal), "Canonical settings should drop deleted cached voice properties");
}

static void NumericSettingsNormalizeRangesAndGranularity()
{
    var settings = new Settings
    {
        SelectedModel = "removed-model",
        Latency = "turbo",
        Mp3Bitrate = 320,
        Speed = 1.075,
        Volume = -0.05,
        Temperature = 0.724,
        TopP = 0.726,
    };

    AssertEqual(true, SettingsNormalizer.Normalize(settings, FishAudioRuntime.FreeModelCutoffUtc), "Invalid or off-step structured settings should normalize");
    AssertEqual(FishAudioRuntime.S21ProModel, settings.SelectedModel, "Unavailable model should normalize through runtime policy");
    AssertEqual(Settings.DefaultLatency, settings.Latency, "Invalid latency should normalize to default");
    AssertEqual(Settings.DefaultMp3Bitrate, settings.Mp3Bitrate, "Invalid bitrate should normalize to default");
    AssertEqual(1.1, settings.Speed, "Speed midpoint should snap to 0.05 away from zero");
    AssertEqual(-0.1, settings.Volume, "Negative volume midpoint should snap to 0.1 away from zero");
    AssertEqual(0.7, settings.Temperature, "Temperature should snap to the nearest 0.05");
    AssertEqual(0.75, settings.TopP, "TopP should snap to the nearest 0.05");

    settings.Speed = double.NaN;
    settings.Volume = double.PositiveInfinity;
    settings.Temperature = -0.01;
    settings.TopP = 1.01;
    SettingsNormalizer.Normalize(settings, FishAudioRuntime.FreeModelCutoffUtc);

    AssertEqual(Settings.DefaultSpeed, settings.Speed, "Non-finite speed should normalize to default");
    AssertEqual(Settings.DefaultVolume, settings.Volume, "Non-finite volume should normalize to default");
    AssertEqual(Settings.DefaultTemperature, settings.Temperature, "Out-of-range temperature should normalize to default");
    AssertEqual(Settings.DefaultTopP, settings.TopP, "Out-of-range TopP should normalize to default");
}

static void OrdinaryDuplicateDefaultsOnlyThatField()
{
    var settings = DeserializeSettings(
        """
        {
          "SchemaVersion": 1,
          "ApiKey": "first",
          "VoiceId": "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
          "ApiKey": "second",
          "SelectedModel": "s2-pro"
        }
        """);
    var context = CreateContext(settings: settings);
    var proxy = (ContextProxy)(object)context;

    SettingsStore.Load(context, FishAudioRuntime.FreeModelCutoffUtc.AddTicks(-1));

    AssertEqual("", settings.ApiKey, "A duplicated ordinary field should use only that field's default");
    AssertEqual("eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee", settings.VoiceId, "A neighboring non-duplicate field should remain unchanged");
    AssertEqual("s2-pro", settings.SelectedModel, "A neighboring model should remain unchanged");
    AssertEqual(1, proxy.SaveCount, "An ordinary duplicate should trigger one canonical save");
}

static void DuplicateAndFutureSchemasAreReadOnly()
{
    var duplicate = DeserializeSettings(
        """
        {
          "SchemaVersion": 1,
          "ApiKey": "duplicate-schema-key",
          "SchemaVersion": 1,
          "VoiceId": "ffffffffffffffffffffffffffffffff",
          "SelectedModel": "s2-pro"
        }
        """);
    var duplicateLogger = new TestLogger();
    var duplicateContext = CreateContext(settings: duplicate, logger: duplicateLogger);
    var duplicateProxy = (ContextProxy)(object)duplicateContext;

    var loadedDuplicate = SettingsStore.Load(duplicateContext, FishAudioRuntime.FreeModelCutoffUtc.AddTicks(-1));
    SettingsStore.Save(duplicateContext, loadedDuplicate);

    AssertEqual(true, loadedDuplicate.IsReadOnly, "Duplicate SchemaVersion should make settings read-only");
    AssertEqual("duplicate-schema-key", loadedDuplicate.ApiKey, "Duplicate SchemaVersion should still parse compatible current fields");
    AssertEqual(0, duplicateProxy.SaveCount, "Duplicate SchemaVersion should block all host saves");
    AssertEqual(true, duplicateLogger.Contains("read-only"), "Blocked duplicate-schema saves should be logged");

    var future = DeserializeSettings(
        """
        {
          "SchemaVersion": 99,
          "ApiKey": "future-key",
          "VoiceId": "0123456789abcdef0123456789abcdef",
          "SelectedModel": "s1"
        }
        """);
    var futureLogger = new TestLogger();
    var futureContext = CreateContext(settings: future, logger: futureLogger);
    var futureProxy = (ContextProxy)(object)futureContext;

    var loadedFuture = SettingsStore.Load(futureContext, FishAudioRuntime.FreeModelCutoffUtc.AddTicks(-1));
    SettingsStore.Save(futureContext, loadedFuture);

    AssertEqual(99, loadedFuture.SchemaVersion, "Future schema number should be retained in memory");
    AssertEqual(true, loadedFuture.IsReadOnly, "Future schemas should be read-only");
    AssertEqual("future-key", loadedFuture.ApiKey, "Future schemas should still parse compatible current fields");
    AssertEqual("s1", loadedFuture.SelectedModel, "Future schemas should preserve compatible current model fields");
    AssertEqual(0, futureProxy.SaveCount, "Future schemas should block all host saves");
    AssertEqual(true, futureLogger.Contains("read-only"), "Blocked future-schema saves should be logged");
}

static void CurrentSettingsRoundTrip()
{
    var original = new Settings
    {
        ApiKey = "malformed key remains text",
        VoiceId = "malformed voice remains text",
        SelectedModel = "s2-pro",
        Speed = 1.35,
        Volume = -2.4,
        NormalizeLoudness = false,
        Temperature = 0.55,
        TopP = 0.85,
        Latency = "balanced",
        Normalize = true,
        Mp3Bitrate = 128,
        ConditionOnPreviousChunks = false,
        CachedVoice = new CachedVoiceInfo
        {
            Title = "Round Trip",
            Description = "Description",
            CoverImage = "cover/round-trip",
            AuthorName = "Author",
            TaskCount = 77,
            SampleAudioUrl = "https://platform.r2.fish.audio/round-trip.mp3",
        },
        IsS21ProFreePromoDismissed = true,
    };

    var roundTripped = DeserializeSettings(JsonSerializer.Serialize(original));

    AssertEqual(SettingsSchema.Current, roundTripped.SchemaVersion, "Current round trip should retain schema version");
    AssertEqual(original.ApiKey, roundTripped.ApiKey, "Current round trip should preserve API Key text");
    AssertEqual(original.VoiceId, roundTripped.VoiceId, "Current round trip should preserve VoiceId text");
    AssertEqual(original.SelectedModel, roundTripped.SelectedModel, "Current round trip should preserve model");
    AssertEqual(original.Speed, roundTripped.Speed, "Current round trip should preserve speed");
    AssertEqual(original.Volume, roundTripped.Volume, "Current round trip should preserve volume");
    AssertEqual(original.NormalizeLoudness, roundTripped.NormalizeLoudness, "Current round trip should preserve loudness normalization");
    AssertEqual(original.Temperature, roundTripped.Temperature, "Current round trip should preserve temperature");
    AssertEqual(original.TopP, roundTripped.TopP, "Current round trip should preserve TopP");
    AssertEqual(original.Latency, roundTripped.Latency, "Current round trip should preserve latency");
    AssertEqual(original.Normalize, roundTripped.Normalize, "Current round trip should preserve text normalization");
    AssertEqual(original.Mp3Bitrate, roundTripped.Mp3Bitrate, "Current round trip should preserve bitrate");
    AssertEqual(original.ConditionOnPreviousChunks, roundTripped.ConditionOnPreviousChunks, "Current round trip should preserve context setting");
    AssertEqual(original.CachedVoice?.Title, roundTripped.CachedVoice?.Title, "Current round trip should preserve cached voice metadata");
    AssertEqual(original.IsS21ProFreePromoDismissed, roundTripped.IsS21ProFreePromoDismissed, "Current round trip should preserve promo dismissal");
}

static void ProductionUsesSettingsStoreForContextStorage()
{
    var productionRoot = FindRepoPath("STranslate.Plugin.Tts.FishAudio");
    var offenders = Directory
        .GetFiles(productionRoot, "*.cs", SearchOption.AllDirectories)
        .Where(path => !string.Equals(Path.GetFileName(path), "SettingsStore.cs", StringComparison.Ordinal))
        .Where(path => Regex.IsMatch(
            File.ReadAllText(path),
            @"\.\s*(?:Load|Save)SettingStorage\s*<\s*Settings\s*>",
            RegexOptions.CultureInvariant))
        .Select(path => Path.GetRelativePath(productionRoot, path))
        .ToArray();

    AssertEqual(0, offenders.Length, $"Only SettingsStore may call context settings storage; found: {string.Join(", ", offenders)}");
    AssertEqual(
        false,
        File.Exists(Path.Combine(productionRoot, "Settings.cs")),
        "Settings should live in the focused Configuration directory");
}

static Settings DeserializeSettings(string json) =>
    JsonSerializer.Deserialize<Settings>(json) ?? throw new InvalidOperationException("Settings JSON returned null");

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

static void SettingsViewModelSplitsPreviewAndCoverCacheResponsibilities()
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
        viewModelAssembly.GetType("STranslate.Plugin.Tts.FishAudio.ViewModel.PreviewPlaybackController"),
        "Preview playback should be split into an internal controller boundary");
    AssertNotNull(
        viewModelAssembly.GetType("STranslate.Plugin.Tts.FishAudio.ViewModel.CoverImageCacheDisplayManager"),
        "Cover image cache display and cleanup should be split into an internal manager boundary");
}

static void ApiKeyEditingPersistsImmediately()
{
    var settings = new Settings { ApiKey = AppliedKey };
    var context = CreateContext(settings: settings);
    var proxy = (ContextProxy)(object)context;
    var viewModel = new SettingsViewModel(context, settings);

    viewModel.ApiKey = DraftKey;

    AssertEqual(DraftKey, settings.ApiKey, "Editing API Key should immediately update settings");
    AssertEqual(1, proxy.SaveCount, "Editing API Key should immediately save settings");
}

static async Task StartupCreditRefreshPreflightSkipsRequestAsync()
{
    using var network = OverrideNetworkAvailability(false);
    foreach (var apiKey in new[] { AppliedKey, "", "ABC", AppliedKey.ToUpperInvariant() })
    {
        network.Set(apiKey != AppliedKey);
        var settings = new Settings { ApiKey = apiKey };
        var (httpService, http) = TestHttpServiceProxy.Create();
        var logger = new TestLogger();
        var plugin = new Main();

        plugin.Init(CreateContext(settings: settings, httpService: httpService, logger: logger));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(
            0,
            http.GetUrls.Count(url => url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)),
            $"Startup credit refresh should skip the endpoint for API Key '{apiKey}'");
        AssertEqual(
            true,
            logger.Contains(LogLevel.Warning, "Startup credit refresh"),
            "Skipped startup credit refresh should be logged safely");
        if (!string.IsNullOrEmpty(apiKey))
            AssertEqual(false, logger.Contains(apiKey), "Startup credit refresh logs should not contain the API Key");
    }
}

static void OperationActivityCounterLinearizesStateTransitions()
{
    var sharedGate = new object();
    var counter = new OperationActivityCounter(sharedGate);

    AssertEqual(false, counter.IsActive, "A new operation counter should be inactive");
    AssertEqual(true, counter.Begin(), "The first Begin should report an inactive-to-active transition");
    AssertEqual(false, counter.Begin(), "Overlapping Begin should not report another active transition");
    AssertEqual(false, counter.End(), "A non-final End should not report an active-to-inactive transition");
    AssertEqual(true, counter.IsActive, "The counter should stay active until the final operation ends");
    AssertEqual(true, counter.End(), "The final End should report an active-to-inactive transition");
    AssertEqual(false, counter.IsActive, "The counter should become inactive after the final End");
    AssertEqual(false, counter.End(), "Extra End calls should be ignored without making the count negative");

    counter.Begin();
    var delayedEndTransition = counter.End();
    var newerBeginTransition = counter.Begin();

    AssertEqual(true, delayedEndTransition, "The old End should observe its own transition to inactive");
    AssertEqual(true, newerBeginTransition, "A newer Begin should transition the counter back to active");
    AssertEqual(true, counter.IsActive, "A delayed old notification must observe the newer active state");
    counter.End();
}

static void OperationActivityCounterPublishesOutsideGateAcrossThreads()
{
    var sharedGate = new object();
    var observableState = false;
    OperationActivityCounter? counter = null;
    Thread[]? workers = null;
    using var workersReady = new CountdownEvent(2);
    using var workersCompleted = new CountdownEvent(2);
    var workersStartedInsideCallback = false;
    var workersCompletedInsideCallback = false;
    counter = new OperationActivityCounter(sharedGate, value =>
    {
        observableState = value;
        if (value || workers is not null)
            return;

        workers = Enumerable.Range(0, 2)
            .Select(_ => new Thread(() =>
            {
                workersReady.Signal();
                try
                {
                    counter!.Begin();
                }
                finally
                {
                    workersCompleted.Signal();
                }
            })
            {
                IsBackground = true,
            })
            .ToArray();

        foreach (var worker in workers)
            worker.Start();

        workersStartedInsideCallback = workersReady.Wait(TimeSpan.FromSeconds(2));
        workersCompletedInsideCallback = workersStartedInsideCallback
            && workersCompleted.Wait(TimeSpan.FromSeconds(1));
    });

    counter.Begin();
    counter.End();

    AssertEqual(true, workersCompleted.Wait(TimeSpan.FromSeconds(2)), "Cross-thread operations should finish after the setter callback returns");
    foreach (var worker in workers!)
        worker.Join();

    AssertEqual(true, workersStartedInsideCallback, "Both worker operations should start while the setter callback is active");
    AssertEqual(true, workersCompletedInsideCallback, "The setter callback should not hold the operation gate while waiting for worker operations");
    AssertEqual(true, counter.IsActive, "Concurrent Begin operations should leave the counter active");
    AssertEqual(true, observableState, "Published state should converge to the active count after cross-thread reentrancy");

    counter.End();
    AssertEqual(true, counter.IsActive, "The first worker End should leave the other operation active");
    AssertEqual(true, observableState, "Observable state should remain active until the final worker End");
    counter.End();
    AssertEqual(false, counter.IsActive, "The final worker End should make the counter inactive");
    AssertEqual(false, observableState, "Observable state should converge to inactive after the final worker End");
    AssertEqual(false, counter.End(), "An extra End should remain safe after cross-thread publication");
}

static void OperationActivityCounterPublishesPendingStateBeforeRethrowingPublicationFailure()
{
    var sharedGate = new object();
    var observableState = false;
    var activePublicationCount = 0;
    var expectedException = new InvalidOperationException("Expected one-shot observable publication failure.");
    var throwOnInactivePublication = true;
    var reentrantWorkerCompletedInsideCallback = false;
    Thread? reentrantWorker = null;
    using var reentrantWorkerCompleted = new ManualResetEventSlim();
    OperationActivityCounter? counter = null;
    counter = new OperationActivityCounter(sharedGate, value =>
    {
        observableState = value;
        if (value)
        {
            activePublicationCount++;
            return;
        }

        if (!throwOnInactivePublication)
            return;

        throwOnInactivePublication = false;
        reentrantWorker = new Thread(() =>
        {
            try
            {
                counter!.Begin();
            }
            finally
            {
                reentrantWorkerCompleted.Set();
            }
        })
        {
            IsBackground = true,
        };
        reentrantWorker.Start();
        reentrantWorkerCompletedInsideCallback = reentrantWorkerCompleted.Wait(TimeSpan.FromSeconds(2));
        throw expectedException;
    });

    counter.Begin();
    Exception? caughtException = null;
    try
    {
        counter.End();
    }
    catch (Exception ex)
    {
        caughtException = ex;
    }

    reentrantWorker?.Join();
    AssertEqual(true, reentrantWorkerCompletedInsideCallback, "The reentrant Begin should complete before the failing setter callback returns");
    AssertEqual(expectedException, caughtException, "End should propagate the original observable publication exception");
    AssertEqual(2, activePublicationCount, "The pending active state should be published before the original exception is rethrown");
    AssertEqual(true, counter.IsActive, "The reentrant Begin should leave the counter active after exception recovery");
    AssertEqual(true, observableState, "Observable state should converge to the pending active count before End rethrows");

    AssertEqual(false, counter.Begin(), "A later overlapping Begin should remain usable after exception recovery");
    AssertEqual(false, counter.End(), "The overlapping operation should end without clearing the remaining active operation");
    AssertEqual(true, counter.End(), "The remaining operation should end normally after exception recovery");
    AssertEqual(false, observableState, "A later final End should publish inactive normally after exception recovery");
    AssertEqual(true, counter.Begin(), "A new operation should still publish active after exception recovery");
    AssertEqual(true, observableState, "A new operation should update observable state after exception recovery");
    AssertEqual(true, counter.End(), "The new operation should end normally after exception recovery");
    AssertEqual(false, observableState, "Observable state should finish inactive after the follow-up operation");
}

static void OperationActivityCounterRecoveryFailureLeavesPublisherReusable()
{
    var sharedGate = new object();
    var observableState = false;
    var activePublicationCount = 0;
    var initialException = new InvalidOperationException("Expected initial observable publication failure.");
    var recoveryException = new InvalidOperationException("Expected recovery observable publication failure.");
    var throwOnInactivePublication = true;
    var throwOnRecoveryPublication = false;
    Thread? reentrantWorker = null;
    using var reentrantWorkerCompleted = new ManualResetEventSlim();
    OperationActivityCounter? counter = null;
    counter = new OperationActivityCounter(sharedGate, value =>
    {
        observableState = value;
        if (value)
        {
            activePublicationCount++;
            if (throwOnRecoveryPublication)
            {
                throwOnRecoveryPublication = false;
                throw recoveryException;
            }

            return;
        }

        if (!throwOnInactivePublication)
            return;

        throwOnInactivePublication = false;
        reentrantWorker = new Thread(() =>
        {
            try
            {
                counter!.Begin();
            }
            finally
            {
                reentrantWorkerCompleted.Set();
            }
        })
        {
            IsBackground = true,
        };
        reentrantWorker.Start();
        if (!reentrantWorkerCompleted.Wait(TimeSpan.FromSeconds(2)))
            throw new TimeoutException("The reentrant Begin did not complete inside the setter callback.");

        throwOnRecoveryPublication = true;
        throw initialException;
    });

    counter.Begin();
    Exception? caughtException = null;
    try
    {
        counter.End();
    }
    catch (Exception ex)
    {
        caughtException = ex;
    }

    reentrantWorker?.Join();
    AssertEqual(typeof(AggregateException), caughtException?.GetType(), "A failed recovery publication should report both publication exceptions");
    var aggregateException = (AggregateException)caughtException!;
    AssertEqual(2, aggregateException.InnerExceptions.Count, "Recovery failure should retain both observable publication exceptions");
    AssertEqual(initialException, aggregateException.InnerExceptions[0], "Recovery failure should retain the original publication exception first");
    AssertEqual(recoveryException, aggregateException.InnerExceptions[1], "Recovery failure should retain the recovery publication exception second");
    AssertEqual(true, counter.IsActive, "The reentrant Begin should remain counted after both publication attempts fail");
    AssertEqual(true, observableState, "The failed recovery setter should still expose the state it assigned before throwing");

    AssertEqual(false, counter.Begin(), "A later overlapping Begin should reclaim publisher ownership after recovery failure");
    AssertEqual(3, activePublicationCount, "The next operation should republish the still-pending active version exactly once");
    AssertEqual(false, counter.End(), "The recovery Begin should end without clearing the remaining active operation");
    AssertEqual(true, counter.End(), "The original reentrant operation should end normally after publisher recovery");
    AssertEqual(false, observableState, "Observable state should converge to inactive after publisher recovery");
}

static void ApiKeyOperationCounterHandlesPropertyChangingReentrancy()
{
    var viewModel = new SettingsViewModel(CreateContext(), new Settings());
    viewModel.BeginApiKeyOperation();
    var injectedNewOperation = false;
    viewModel.PropertyChanging += (_, args) =>
    {
        if (!injectedNewOperation && args.PropertyName == nameof(SettingsViewModel.IsApiKeyInputLocked))
        {
            injectedNewOperation = true;
            viewModel.BeginApiKeyOperation();
        }
    };

    viewModel.EndApiKeyOperation();

    AssertEqual(true, injectedNewOperation, "The test should inject Begin while the old End is changing the observable lock state");
    AssertEqual(true, viewModel.IsApiKeyInputLocked, "A reentrant newer Begin should not be overwritten by the old End state assignment");

    viewModel.EndApiKeyOperation();
    AssertEqual(false, viewModel.IsApiKeyInputLocked, "The reentrant operation should unlock when its own End completes");
}

static async Task ApiKeyOperationCounterHandlesPropertyChangedReentrancyAsync()
{
    var viewModel = new SettingsViewModel(CreateContext(), new Settings());
    viewModel.BeginApiKeyOperation();
    var injectedNewOperation = false;
    viewModel.PropertyChanged += (_, args) =>
    {
        if (!injectedNewOperation && args.PropertyName == nameof(SettingsViewModel.IsApiKeyInputLocked))
        {
            injectedNewOperation = true;
            viewModel.BeginApiKeyOperation();
        }
    };

    await Task.Run(viewModel.EndApiKeyOperation).WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(true, injectedNewOperation, "The test should inject Begin after the old End changes the observable lock state");
    AssertEqual(true, viewModel.IsApiKeyInputLocked, "PropertyChanged reentrancy should restore the active API Key lock without deadlocking");

    viewModel.EndApiKeyOperation();
    AssertEqual(false, viewModel.IsApiKeyInputLocked, "The PropertyChanged reentrant operation should unlock after its own End completes");
}

static async Task StartupCreditRefreshRequestsOncePerInitializationAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings { ApiKey = AppliedKey };
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (url, _, _) => Task.FromResult(
        url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
            ? "{\"credit\":\"12.34\"}"
            : "{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    var context = CreateContext(settings: settings, httpService: httpService);
    var plugin = new Main();

    plugin.Init(context, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(
        1,
        http.GetUrls.Count(url => url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)),
        "A valid API Key should request credit once during initialization");
    AssertEqual(TimeSpan.FromSeconds(15), http.GetOptionsByUrl.Single(pair => pair.Url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)).Options?.Timeout, "Startup credit refresh should reuse the 15 second credit timeout");
    var headers = AssertHeaders(
        http.GetOptionsByUrl.Single(pair => pair.Url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)).Options,
        "Startup credit refresh should send Authorization headers");
    AssertEqual($"Bearer {AppliedKey}", headers["Authorization"], "Startup credit refresh should use the API Key snapshot");

    plugin.Init(context, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(
        2,
        http.GetUrls.Count(url => url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)),
        "Each initialization cycle should request credit exactly once");
}

static async Task CompletedStartupCreditRefreshAppliesWhenSettingsViewModelIsCreatedAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings { ApiKey = AppliedKey };
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (url, _, _) => Task.FromResult(
        url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
            ? "{\"credit\":\"23.45\"}"
            : "{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    var plugin = new Main();

    plugin.Init(CreateContext(settings: settings, httpService: httpService), FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    var viewModel = plugin.GetOrCreateSettingsViewModel();

    AssertEqual("23.45", viewModel.UserCredit, "A completed startup credit result should be visible when settings first opens");
    AssertEqual("", viewModel.LatencyText, "Startup credit refresh should not display latency");
    AssertEqual(false, viewModel.IsLoadingCredit, "A completed startup credit result should not leave the refresh state busy");
}

static async Task PendingStartupCreditRefreshLocksSettingsUntilCompletionAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings { ApiKey = AppliedKey };
    var creditStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseCredit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (url, _, _) =>
    {
        if (url.Contains("/wallet/self/api-credit", StringComparison.Ordinal))
        {
            creditStarted.TrySetResult();
            return releaseCredit.Task;
        }

        return Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    };
    var plugin = new Main();

    plugin.Init(CreateContext(settings: settings, httpService: httpService), FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    await creditStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    AssertEqual(false, plugin.PendingStartupTask.IsCompleted, "Main.Init should return without waiting for startup credit refresh");

    var viewModel = plugin.GetOrCreateSettingsViewModel();
    AssertEqual(true, viewModel.IsLoadingCredit, "Pending startup credit refresh should mark credit loading");
    AssertEqual(true, viewModel.IsApiKeyInputLocked, "Pending startup credit refresh should lock API Key input");
    AssertEqual(false, viewModel.IsApiKeyInputEnabled, "Pending startup credit refresh should disable API Key input");
    AssertEqual(false, viewModel.PasteApiKeyCommand.CanExecute(null), "Pending startup credit refresh should disable API Key paste");
    AssertEqual(false, viewModel.RefreshCreditCommand.CanExecute(null), "Pending startup credit refresh should disable manual credit refresh");

    releaseCredit.SetResult("{\"credit\":\"34.56\"}");
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    await WaitUntilAsync(() => !viewModel.IsLoadingCredit, "startup credit attachment to complete");

    AssertEqual("34.56", viewModel.UserCredit, "Pending startup credit success should update the balance");
    AssertEqual(false, viewModel.IsApiKeyInputLocked, "Startup credit completion should release the API Key lock");
    AssertEqual(true, viewModel.RefreshCreditCommand.CanExecute(null), "Startup credit completion should re-enable manual refresh");
    AssertEqual("", viewModel.LatencyText, "Startup credit completion should not display latency");
}

static async Task OverlappingStartupManualAndTtsOperationsUnlockAfterFinalCompletionAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings { ApiKey = AppliedKey };
    var startupCreditStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseStartupCredit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var manualCreditStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseManualCredit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var creditRequestCount = 0;
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (url, _, _) =>
    {
        if (!url.Contains("/wallet/self/api-credit", StringComparison.Ordinal))
            return Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");

        return Interlocked.Increment(ref creditRequestCount) switch
        {
            1 => StartRequest(startupCreditStarted, releaseStartupCredit),
            2 => StartRequest(manualCreditStarted, releaseManualCredit),
            _ => Task.FromException<string>(new InvalidOperationException("Unexpected credit request.")),
        };
    };
    var plugin = new Main();

    plugin.Init(CreateContext(settings: settings, httpService: httpService), FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    await startupCreditStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    var viewModel = plugin.GetOrCreateSettingsViewModel();
    viewModel.BeginApiKeyOperation();

    releaseStartupCredit.SetResult("{\"credit\":\"45.67\"}");
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    await WaitUntilAsync(() => !viewModel.IsLoadingCredit, "startup credit overlap to complete");

    AssertEqual(true, viewModel.IsApiKeyInputLocked, "Startup completion should not unlock the overlapping TTS operation");

    var manualRefreshTask = viewModel.RefreshCreditCommand.ExecuteAsync(null);
    await manualCreditStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    AssertEqual(true, viewModel.IsLoadingCredit, "Manual refresh should become the active credit operation");
    AssertEqual(true, viewModel.IsApiKeyInputLocked, "Manual refresh should keep the API Key locked while TTS is active");

    viewModel.EndApiKeyOperation();
    AssertEqual(true, viewModel.IsApiKeyInputLocked, "TTS completion should not unlock the overlapping manual refresh");

    var reentrantTtsStarted = false;
    viewModel.PropertyChanging += (_, args) =>
    {
        if (!reentrantTtsStarted && args.PropertyName == nameof(SettingsViewModel.IsApiKeyInputLocked))
        {
            reentrantTtsStarted = true;
            viewModel.BeginApiKeyOperation();
        }
    };

    releaseManualCredit.SetResult("{\"credit\":\"56.78\"}");
    await manualRefreshTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(true, reentrantTtsStarted, "Manual completion should deterministically start the final reentrant TTS operation");
    AssertEqual(false, viewModel.IsLoadingCredit, "Manual completion should clear credit loading after startup has completed");
    AssertEqual(true, viewModel.IsApiKeyInputLocked, "Manual completion should not overwrite the final TTS lock");

    viewModel.EndApiKeyOperation();
    AssertEqual(false, viewModel.IsApiKeyInputLocked, "The API Key should unlock only after the final TTS operation completes");

    static Task<string> StartRequest(TaskCompletionSource started, TaskCompletionSource<string> release)
    {
        started.TrySetResult();
        return release.Task;
    }
}

static async Task FailedStartupCreditRefreshKeepsPlaceholderWithoutSnackbarAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings { ApiKey = AppliedKey };
    var snackbar = new TestSnackbar();
    var logger = new TestLogger();
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (url, _, _) =>
        url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
            ? Task.FromException<string>(new TimeoutException($"request failed for {AppliedKey}"))
            : Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    var plugin = new Main();

    plugin.Init(
        CreateContext(snackbar, settings, httpService, logger: logger),
        FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    var viewModel = plugin.GetOrCreateSettingsViewModel();

    AssertEqual("", viewModel.UserCredit, "Failed startup credit refresh should keep the not-loaded placeholder state");
    AssertEqual("", viewModel.LatencyText, "Failed startup credit refresh should not display latency");
    AssertEqual(null, snackbar.LastError, "Failed startup credit refresh should not show a snackbar");
    AssertEqual(true, logger.Contains(LogLevel.Error, "Startup credit refresh failed"), "Failed startup credit refresh should be logged");
    AssertEqual(false, logger.Contains(AppliedKey), "Startup credit failure logs should not contain the API Key");
}

static async Task NullStartupCreditKeepsNotLoadedStateAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings { ApiKey = AppliedKey };
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (url, _, _) => Task.FromResult(
        url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
            ? "{\"credit\":null}"
            : "{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    var plugin = new Main();

    plugin.Init(CreateContext(settings: settings, httpService: httpService), FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    var viewModel = plugin.GetOrCreateSettingsViewModel();

    AssertEqual("", viewModel.UserCredit, "A null startup credit value should keep the not-loaded placeholder state");
}

static async Task StartupCreditRefreshIgnoresResultAfterApiKeyChangesAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings { ApiKey = AppliedKey };
    var releaseCredit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (url, _, _) =>
        url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
            ? releaseCredit.Task
            : Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    var plugin = new Main();
    plugin.Init(CreateContext(settings: settings, httpService: httpService), FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var viewModel = plugin.GetOrCreateSettingsViewModel();

    viewModel.ApiKey = DraftKey;
    releaseCredit.SetResult("{\"credit\":\"45.67\"}");
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    await WaitUntilAsync(() => !viewModel.IsLoadingCredit, "changed-key startup credit attachment to complete");

    AssertEqual("", viewModel.UserCredit, "Startup credit result should be ignored after the API Key changes");
    AssertEqual(false, viewModel.IsApiKeyInputLocked, "Ignored startup credit result should still release the API Key lock");
}

static async Task ReinitializedStartupCreditRefreshIgnoresOldCycleAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings { ApiKey = AppliedKey };
    var releaseOldCredit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var (oldHttpService, oldHttp) = TestHttpServiceProxy.Create();
    oldHttp.GetAsyncHandler = (url, _, _) =>
        url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
            ? releaseOldCredit.Task
            : Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    var plugin = new Main();
    plugin.Init(CreateContext(settings: settings, httpService: oldHttpService), FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var oldStartupTask = plugin.PendingStartupTask;
    var oldViewModel = plugin.GetOrCreateSettingsViewModel();

    var (newHttpService, newHttp) = TestHttpServiceProxy.Create();
    newHttp.GetAsyncHandler = (url, _, _) => Task.FromResult(
        url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
            ? "{\"credit\":\"56.78\"}"
            : "{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    plugin.Init(CreateContext(settings: settings, httpService: newHttpService), FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    var newViewModel = plugin.GetOrCreateSettingsViewModel();
    AssertEqual(false, ReferenceEquals(oldViewModel, newViewModel), "Reinitialization should replace the old settings ViewModel");
    AssertEqual("56.78", newViewModel.UserCredit, "The current initialization cycle should apply its credit result");

    releaseOldCredit.SetResult("{\"credit\":\"99.99\"}");
    await oldStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    await WaitUntilAsync(() => !oldViewModel.IsLoadingCredit, "old startup credit attachment to release its lock");

    AssertEqual("56.78", newViewModel.UserCredit, "An old initialization cycle should not overwrite current credit");
    AssertEqual(false, oldViewModel.IsApiKeyInputLocked, "The old initialization cycle should release its counted lock");
    AssertEqual(false, newViewModel.IsApiKeyInputLocked, "The current initialization cycle should release its counted lock");
}

static async Task PublishedReplacementInvalidatesOldStartupCreditImmediatelyAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var releaseOldCredit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var replacementPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releasePublicationWindow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var (oldHttpService, oldHttp) = TestHttpServiceProxy.Create();
    oldHttp.GetAsyncHandler = (url, _, _) =>
        url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
            ? releaseOldCredit.Task
            : Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    var plugin = new Main(
        (context, settings, settingsWriteLease, startupCreditRefreshCycle) =>
            new SettingsViewModel(
                context,
                settings,
                clearCoverImageCacheAsync: null,
                clearCoverImageCacheTimeout: null,
                startupCreditRefreshCycle: startupCreditRefreshCycle,
                settingsWriteLease: settingsWriteLease),
        initializationPublished: generation =>
        {
            if (generation != 2)
                return;

            replacementPublished.TrySetResult();
            releasePublicationWindow.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        });
    plugin.Init(
        CreateContext(settings: new Settings { ApiKey = AppliedKey }, httpService: oldHttpService),
        FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var oldStartupTask = plugin.PendingStartupTask;
    var oldViewModel = plugin.GetOrCreateSettingsViewModel();

    var releaseNewCredit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var (newHttpService, newHttp) = TestHttpServiceProxy.Create();
    newHttp.GetAsyncHandler = (url, _, _) =>
        url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
            ? releaseNewCredit.Task
            : Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    var reinitializeTask = Task.Run(() =>
        plugin.Init(
            CreateContext(settings: new Settings { ApiKey = DraftKey }, httpService: newHttpService),
            FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1)));
    await replacementPublished.Task.WaitAsync(TimeSpan.FromSeconds(2));

    releaseOldCredit.SetResult("{\"credit\":\"99.99\"}");
    await oldStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    await WaitUntilAsync(() => !oldViewModel.IsLoadingCredit, "old startup credit observer to finish inside the publication window");
    var oldCreditDuringPublicationWindow = oldViewModel.UserCredit;

    releasePublicationWindow.SetResult();
    await reinitializeTask.WaitAsync(TimeSpan.FromSeconds(2));
    var replacementInitCompletedWithCreditPending = !plugin.PendingStartupTask.IsCompleted;
    var newViewModel = plugin.GetOrCreateSettingsViewModel();
    releaseNewCredit.SetResult("{\"credit\":\"12.34\"}");
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    await WaitUntilAsync(() => !newViewModel.IsLoadingCredit, "replacement startup credit observer to finish");

    AssertEqual("", oldCreditDuringPublicationWindow, "Publishing a replacement state should immediately invalidate the old startup credit result");
    AssertEqual(true, replacementInitCompletedWithCreditPending, "Replacement initialization should not wait for its startup credit request");
    AssertEqual("12.34", newViewModel.UserCredit, "The replacement startup credit result should remain active");
}

static async Task ReinitializedStartupCreditRefreshRemainsActiveDuringReloadAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings { ApiKey = AppliedKey };
    var releaseOldCredit = new TaskCompletionSource<string>();
    var (oldHttpService, oldHttp) = TestHttpServiceProxy.Create();
    oldHttp.GetAsyncHandler = (url, _, _) =>
        url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
            ? releaseOldCredit.Task
            : Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    var plugin = new Main();
    plugin.Init(CreateContext(settings: settings, httpService: oldHttpService), FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var oldStartupTask = plugin.PendingStartupTask;
    var oldViewModel = plugin.GetOrCreateSettingsViewModel();

    var (newHttpService, newHttp) = TestHttpServiceProxy.Create();
    newHttp.GetAsyncHandler = (url, _, _) =>
        url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
            ? Task.FromException<string>(new TimeoutException("new startup credit failed"))
            : Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    var newContext = CreateContext(settings: settings, httpService: newHttpService);
    ((ContextProxy)(object)newContext).OnLoad = () => releaseOldCredit.SetResult("{\"credit\":\"99.99\"}");

    plugin.Init(newContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    await oldStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    var newViewModel = plugin.GetOrCreateSettingsViewModel();
    await WaitUntilAsync(() => !oldViewModel.IsLoadingCredit, "old startup credit lock to release during reload");

    AssertEqual("99.99", oldViewModel.UserCredit, "The old initialization should remain active until the replacement settings finish loading");
    AssertEqual("", newViewModel.UserCredit, "The old credit result should not flow into the replacement initialization");
}

static Task ReinitializedSettingsViewUsesNewContextAndSettingsAsync() =>
    RunOnStaThreadAsync(() =>
{
    using var network = OverrideNetworkAvailability(true);
    var oldSettings = new Settings { ApiKey = AppliedKey };
    var releaseOldCredit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var (oldHttpService, oldHttp) = TestHttpServiceProxy.Create();
    oldHttp.GetAsyncHandler = (url, _, _) =>
        url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
            ? releaseOldCredit.Task
            : Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    var oldContext = CreateContext(settings: oldSettings, httpService: oldHttpService);
    var plugin = new Main();

    plugin.Init(oldContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var oldStartupTask = plugin.PendingStartupTask;
    var settingsView = plugin.GetSettingUI();
    var oldViewModel = (SettingsViewModel)settingsView.DataContext;

    var newSettings = new Settings { ApiKey = DraftKey };
    var (newHttpService, newHttp) = TestHttpServiceProxy.Create();
    newHttp.GetAsyncHandler = (url, _, _) => Task.FromResult(
        url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
            ? "{\"credit\":\"78.90\"}"
            : "{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    var newContext = CreateContext(settings: newSettings, httpService: newHttpService);
    var newContextProxy = (ContextProxy)(object)newContext;

    plugin.Init(newContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
    var newViewModel = plugin.GetOrCreateSettingsViewModel();
    var reusedSettingsView = plugin.GetSettingUI();

    AssertEqual(true, ReferenceEquals(settingsView, reusedSettingsView), "Reinitialization should reuse the existing SettingsView control");
    AssertEqual(false, ReferenceEquals(oldViewModel, newViewModel), "Reinitialization should replace the ViewModel that owns readonly context and settings dependencies");
    AssertEqual(true, ReferenceEquals(newViewModel, reusedSettingsView.DataContext), "The cached SettingsView should bind the replacement ViewModel");
    AssertEqual(DraftKey, newViewModel.ApiKey, "The replacement ViewModel should load the second Settings object");
    AssertEqual("78.90", newViewModel.UserCredit, "The replacement ViewModel should display the second initialization balance");

    releaseOldCredit.SetResult("{\"credit\":\"99.99\"}");
    oldStartupTask.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
    AssertEqual("78.90", newViewModel.UserCredit, "The first initialization result should not overwrite the replacement ViewModel");

    const string editedNewKey = "ffffffffffffffffffffffffffffffff";
    newViewModel.ApiKey = editedNewKey;
    AssertEqual(editedNewKey, newSettings.ApiKey, "Replacement ViewModel edits should update the second Settings object");
    AssertEqual(AppliedKey, oldSettings.ApiKey, "Replacement ViewModel edits should not mutate the first Settings object");
    AssertEqual(1, newContextProxy.SaveCount, "Replacement ViewModel edits should save through the second Context");
});

static async Task ReinitializationRetiresOldViewModelWithoutSavingSharedBackingStoreAsync()
{
    using var network = OverrideNetworkAvailability(false);
    var backingStore = new SharedSettingsBackingStore(new Settings
    {
        ApiKey = AppliedKey,
        SelectedModel = FishAudioRuntime.S2ProModel,
    });
    var oldContext = CreateContext();
    var oldContextProxy = (ContextProxy)(object)oldContext;
    oldContextProxy.LoadSettings = backingStore.Load;
    oldContextProxy.SaveSettings = backingStore.Save;
    var plugin = new Main();

    plugin.Init(oldContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    _ = plugin.GetOrCreateSettingsViewModel();

    backingStore.Replace(new Settings
    {
        ApiKey = DraftKey,
        SelectedModel = FishAudioRuntime.S1Model,
    });
    var newContext = CreateContext();
    var newContextProxy = (ContextProxy)(object)newContext;
    newContextProxy.LoadSettings = backingStore.Load;
    newContextProxy.SaveSettings = backingStore.Save;

    plugin.Init(newContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    var newViewModel = plugin.GetOrCreateSettingsViewModel();
    var finalSnapshot = backingStore.Load();

    AssertEqual(DraftKey, newViewModel.ApiKey, "The replacement ViewModel should use the latest shared backing-store settings");
    AssertEqual(DraftKey, finalSnapshot.ApiKey, "Retiring the old ViewModel should not overwrite the replacement backing-store snapshot");
    AssertEqual(0, oldContextProxy.SaveCount, "Retiring an already auto-saved ViewModel should not issue a redundant host save");
}

static void SameSettingsInstanceSeparatesOldAndNewViewModelWriteLeases()
{
    var settings = new Settings { ApiKey = AppliedKey };
    var context = CreateContext(settings: settings);
    var contextProxy = (ContextProxy)(object)context;
    var firstSettings = SettingsStore.Load(
        context,
        FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1),
        out var firstWriteLease);
    var oldViewModel = new SettingsViewModel(
        context,
        firstSettings,
        clearCoverImageCacheAsync: null,
        clearCoverImageCacheTimeout: null,
        settingsWriteLease: firstWriteLease);

    SettingsStore.Retire(firstSettings, firstWriteLease);
    var secondSettings = SettingsStore.Load(
        context,
        FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1),
        out var secondWriteLease);
    var newViewModel = new SettingsViewModel(
        context,
        secondSettings,
        clearCoverImageCacheAsync: null,
        clearCoverImageCacheTimeout: null,
        settingsWriteLease: secondWriteLease);

    oldViewModel.ApiKey = "ffffffffffffffffffffffffffffffff";
    AssertEqual(0, contextProxy.SaveCount, "An old ViewModel lease should not save after the same Settings instance receives a new lease");
    AssertEqual(AppliedKey, settings.ApiKey, "An old ViewModel lease should not mutate a Settings instance now owned by the replacement lease");

    newViewModel.ApiKey = DraftKey;
    AssertEqual(1, contextProxy.SaveCount, "The replacement ViewModel lease should save the reused Settings instance");
    AssertEqual(DraftKey, settings.ApiKey, "The current ViewModel should retain write ownership of a reused Settings instance");
}

static void FailedSameInstanceNormalizationRestoresOldStateAndBackingStore()
{
    using var network = OverrideNetworkAvailability(false);
    var settings = new Settings { SelectedModel = FishAudioRuntime.S21ProFreeModel };
    var backingStore = new SharedSettingsBackingStore(settings);
    var context = CreateContext(settings: settings);
    var contextProxy = (ContextProxy)(object)context;
    contextProxy.SaveSettings = backingStore.Save;
    var factoryCallCount = 0;
    var plugin = new Main((viewModelContext, viewModelSettings, settingsWriteLease, startupCreditRefreshCycle) =>
    {
        factoryCallCount++;
        if (factoryCallCount == 2)
            throw new InvalidOperationException("failed normalized replacement factory");

        return new SettingsViewModel(
            viewModelContext,
            viewModelSettings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            startupCreditRefreshCycle: startupCreditRefreshCycle,
            settingsWriteLease: settingsWriteLease);
    });

    plugin.Init(context, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var oldViewModel = plugin.GetOrCreateSettingsViewModel();
    var factoryFailed = false;
    try
    {
        plugin.Init(context, FishAudioRuntime.FreeModelCutoffUtc);
    }
    catch (InvalidOperationException ex) when (ex.Message == "failed normalized replacement factory")
    {
        factoryFailed = true;
    }

    AssertEqual(true, factoryFailed, "The normalized replacement factory failure should remain observable");
    AssertEqual(FishAudioRuntime.S21ProFreeModel, settings.SelectedModel, "Failed replacement normalization should not mutate the host-owned old Settings instance");
    AssertEqual(FishAudioRuntime.S21ProFreeModel, oldViewModel.SelectedModel, "Failed replacement normalization should preserve the old ViewModel model");
    AssertEqual(FishAudioRuntime.S21ProFreeModel, backingStore.Load().SelectedModel, "Failed replacement normalization should not canonical-save over the backing store");
    AssertEqual(0, contextProxy.SaveCount, "Failed replacement normalization should not reach the host save boundary");
}

static async Task SettingsViewModelCreationWaitsForReplacementCommitAsync()
{
    using var network = OverrideNetworkAvailability(false);
    var plugin = new Main();
    plugin.Init(
        CreateContext(settings: new Settings { SelectedModel = FishAudioRuntime.S21ProFreeModel }),
        FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));

    var replacementCommitStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseReplacementCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var replacementContext = CreateContext(settings: new Settings
    {
        SelectedModel = FishAudioRuntime.S21ProFreeModel,
    });
    var replacementContextProxy = (ContextProxy)(object)replacementContext;
    replacementContextProxy.OnSave = () =>
    {
        replacementCommitStarted.TrySetResult();
        releaseReplacementCommit.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
    };

    var reinitializeTask = Task.Run(() =>
        plugin.Init(replacementContext, FishAudioRuntime.FreeModelCutoffUtc));
    await replacementCommitStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    var viewModelTask = Task.Run(plugin.GetOrCreateSettingsViewModel);
    var viewModelPublishedBeforeCommit = ReferenceEquals(
        await Task.WhenAny(viewModelTask, Task.Delay(TimeSpan.FromMilliseconds(500))),
        viewModelTask);

    releaseReplacementCommit.SetResult();
    await reinitializeTask.WaitAsync(TimeSpan.FromSeconds(2));
    var viewModel = await viewModelTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(false, viewModelPublishedBeforeCommit, "Settings ViewModel publication should wait for an in-flight replacement commit");
    AssertEqual(FishAudioRuntime.S21ProModel, viewModel.SelectedModel, "The waiting settings ViewModel request should bind the committed replacement state");
    AssertEqual(1, replacementContextProxy.SaveCount, "Replacement normalization should canonical-save exactly once");
}

static async Task FailedReinitializationRestoresOldViewModelWriteLeaseAsync()
{
    using var network = OverrideNetworkAvailability(false);
    var backingStore = new SharedSettingsBackingStore(new Settings { ApiKey = AppliedKey });
    var oldContext = CreateContext();
    var oldContextProxy = (ContextProxy)(object)oldContext;
    oldContextProxy.LoadSettings = backingStore.Load;
    oldContextProxy.SaveSettings = backingStore.Save;
    var plugin = new Main();

    plugin.Init(oldContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    var oldViewModel = plugin.GetOrCreateSettingsViewModel();
    var failedLoadContext = CreateContext();
    ((ContextProxy)(object)failedLoadContext).LoadSettings = () =>
        throw new InvalidOperationException("blocked replacement load");

    var loadFailed = false;
    try
    {
        plugin.Init(failedLoadContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    }
    catch (InvalidOperationException ex) when (ex.Message == "blocked replacement load")
    {
        loadFailed = true;
    }

    AssertEqual(true, loadFailed, "The replacement load failure should remain observable");
    oldViewModel.ApiKey = DraftKey;
    AssertEqual(1, oldContextProxy.SaveCount, "A failed replacement Load should restore the old ViewModel write lease");
    AssertEqual(DraftKey, backingStore.Load().ApiKey, "The restored old lease should persist edits after a failed replacement Load");

    var factoryCallCount = 0;
    var factoryContext = CreateContext(settings: new Settings { ApiKey = AppliedKey });
    var factoryContextProxy = (ContextProxy)(object)factoryContext;
    var factoryPlugin = new Main((viewModelContext, viewModelSettings, settingsWriteLease, startupCreditRefreshCycle) =>
    {
        factoryCallCount++;
        if (factoryCallCount == 2)
            throw new InvalidOperationException("blocked replacement factory");

        return new SettingsViewModel(
            viewModelContext,
            viewModelSettings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            startupCreditRefreshCycle: startupCreditRefreshCycle,
            settingsWriteLease: settingsWriteLease);
    });
    factoryPlugin.Init(factoryContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var factoryOldViewModel = factoryPlugin.GetOrCreateSettingsViewModel();

    var factoryFailed = false;
    try
    {
        factoryPlugin.Init(CreateContext(settings: new Settings { ApiKey = DraftKey }), FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    }
    catch (InvalidOperationException ex) when (ex.Message == "blocked replacement factory")
    {
        factoryFailed = true;
    }

    AssertEqual(true, factoryFailed, "The replacement ViewModel factory failure should remain observable");
    factoryOldViewModel.ApiKey = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
    AssertEqual(1, factoryContextProxy.SaveCount, "A failed replacement ViewModel construction should restore the old write lease");
}

static void DisposeRetiresSettingsWriteLease()
{
    using var network = OverrideNetworkAvailability(false);
    Settings? runtimeSettings = null;
    var plugin = new Main((context, settings, settingsWriteLease, startupCreditRefreshCycle) =>
    {
        runtimeSettings = settings;
        return new SettingsViewModel(
            context,
            settings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            startupCreditRefreshCycle: startupCreditRefreshCycle,
            settingsWriteLease: settingsWriteLease);
    });
    plugin.Init(
        CreateContext(settings: new Settings { ApiKey = AppliedKey }),
        FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    _ = plugin.GetOrCreateSettingsViewModel();

    AssertEqual(true, runtimeSettings?.ActiveWriteLease != 0, "Initialization should grant the detached runtime Settings a write lease");
    plugin.Dispose();
    AssertEqual(0L, runtimeSettings?.ActiveWriteLease, "Dispose should retire the detached runtime Settings write lease");
}

static async Task ReinitializationWaitsForInFlightViewModelSaveAsync()
{
    using var network = OverrideNetworkAvailability(false);
    var backingStore = new SharedSettingsBackingStore(new Settings { ApiKey = AppliedKey });
    var oldSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseOldSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var oldContext = CreateContext();
    var oldContextProxy = (ContextProxy)(object)oldContext;
    oldContextProxy.LoadSettings = backingStore.Load;
    oldContextProxy.SaveSettings = settings =>
    {
        oldSaveStarted.TrySetResult();
        releaseOldSave.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        backingStore.Save(settings);
    };
    var plugin = new Main();

    plugin.Init(oldContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    var oldViewModel = plugin.GetOrCreateSettingsViewModel();
    var oldEditTask = Task.Run(() => oldViewModel.ApiKey = DraftKey);
    await oldSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    var replacementLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var newContext = CreateContext();
    var newContextProxy = (ContextProxy)(object)newContext;
    newContextProxy.LoadSettings = () =>
    {
        replacementLoadStarted.TrySetResult();
        return backingStore.Load();
    };
    newContextProxy.SaveSettings = backingStore.Save;
    var reinitializeTask = Task.Run(() =>
        plugin.Init(newContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1)));
    var loadStartedBeforeOldSaveCompleted = ReferenceEquals(
        await Task.WhenAny(replacementLoadStarted.Task, Task.Delay(TimeSpan.FromMilliseconds(500))),
        replacementLoadStarted.Task);

    releaseOldSave.SetResult();
    await oldEditTask.WaitAsync(TimeSpan.FromSeconds(2));
    await reinitializeTask.WaitAsync(TimeSpan.FromSeconds(2));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    var newViewModel = plugin.GetOrCreateSettingsViewModel();

    AssertEqual(false, loadStartedBeforeOldSaveCompleted, "Replacement Load should wait for an in-flight old ViewModel save to finish");
    AssertEqual(DraftKey, newViewModel.ApiKey, "Replacement Load should observe the completed old ViewModel save");
    AssertEqual(DraftKey, backingStore.Load().ApiKey, "The serialized old save and replacement Load should leave backing storage consistent");
}

static async Task DisposedSettingsViewModelIgnoresStartupCreditResultAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings { ApiKey = AppliedKey };
    var releaseCredit = new TaskCompletionSource<string>();
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (url, _, cancellationToken) =>
    {
        if (!url.Contains("/wallet/self/api-credit", StringComparison.Ordinal))
            return Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");

        cancellationToken.Register(() => releaseCredit.SetResult("{\"credit\":\"67.89\"}"));
        return releaseCredit.Task;
    };
    var plugin = new Main();
    plugin.Init(CreateContext(settings: settings, httpService: httpService), FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var viewModel = plugin.GetOrCreateSettingsViewModel();

    plugin.Dispose();
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    await WaitUntilAsync(() => !viewModel.IsLoadingCredit, "disposed startup credit attachment to complete");

    AssertEqual("", viewModel.UserCredit, "Disposed settings ViewModel should ignore startup credit results");
    AssertEqual(false, viewModel.IsApiKeyInputLocked, "Disposed startup credit attachment should still release its counted lock");
}

static void ModelPolicyUsesCutoffDefaultsAndNormalizeLoudnessSupport()
{
    var expectedCutoff = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    var lastFreeInstant = expectedCutoff.AddTicks(-1);

    AssertEqual(expectedCutoff, FishAudioRuntime.FreeModelCutoffUtc, "Free model cutoff should be August 1, 2026 UTC");

    AssertEnumerableEqual(
        new[]
        {
            FishAudioRuntime.S21ProFreeModel,
            FishAudioRuntime.S21ProModel,
            FishAudioRuntime.S2ProModel,
            FishAudioRuntime.S1Model,
        },
        FishAudioRuntime.GetAvailableModels(lastFreeInstant),
        "Free model should remain available through the last tick of July 31 UTC");
    AssertEqual(FishAudioRuntime.S21ProFreeModel, FishAudioRuntime.GetDefaultModel(lastFreeInstant), "Free model should remain the default through the last tick of July 31 UTC");

    AssertEnumerableEqual(
        new[] { FishAudioRuntime.S21ProModel, FishAudioRuntime.S2ProModel, FishAudioRuntime.S1Model },
        FishAudioRuntime.GetAvailableModels(expectedCutoff),
        "Free model should be unavailable at August 1 UTC");
    AssertEqual(FishAudioRuntime.S21ProModel, FishAudioRuntime.GetDefaultModel(expectedCutoff), "s2.1-pro should be the default at August 1 UTC");

    AssertEqual(true, FishAudioRuntime.SupportsNormalizeLoudness(FishAudioRuntime.S21ProFreeModel), "s2.1-pro-free should support normalize_loudness");
    AssertEqual(true, FishAudioRuntime.SupportsNormalizeLoudness(FishAudioRuntime.S21ProModel), "s2.1-pro should support normalize_loudness");
    AssertEqual(true, FishAudioRuntime.SupportsNormalizeLoudness(FishAudioRuntime.S2ProModel), "s2-pro should support normalize_loudness");
    AssertEqual(false, FishAudioRuntime.SupportsNormalizeLoudness(FishAudioRuntime.S1Model), "s1 should not support normalize_loudness");

    using (OverrideLocalUtcNow(lastFreeInstant))
    {
        var settings = new Settings();
        new Main().Init(CreateContext(settings: settings), lastFreeInstant);
        AssertEqual(FishAudioRuntime.S21ProFreeModel, settings.SelectedModel, "New settings should use the free model default through the last tick of July 31 UTC");
    }

    using (OverrideLocalUtcNow(expectedCutoff))
    {
        var settings = new Settings();
        new Main().Init(CreateContext(settings: settings), expectedCutoff);
        AssertEqual(FishAudioRuntime.S21ProModel, settings.SelectedModel, "New settings should use s2.1-pro as the default at August 1 UTC");
    }
}

static void FreeModelDeadlineDocumentationIsConsistent()
{
    const string oldFreeDate = "2026-07-24";
    const string lastFreeDate = "2026-07-31";

    foreach (var relativePath in new[]
             {
                 "README.md",
                 Path.Combine("docs", "README_EN.md"),
                 Path.Combine("docs", "README_TW.md"),
                 Path.Combine("docs", "README_JA.md"),
                 Path.Combine("docs", "README_KO.md"),
             })
    {
        var readme = File.ReadAllText(FindRepoFile(relativePath));
        AssertEqual(true, readme.Contains(lastFreeDate, StringComparison.Ordinal), $"{relativePath} should use the July 31 free-model deadline");
        AssertEqual(false, readme.Contains(oldFreeDate, StringComparison.Ordinal), $"{relativePath} should not retain the old July 24 free-model deadline");
    }

    foreach (var locale in new[] { "zh-cn", "zh-tw", "en", "ja", "ko" })
    {
        var xaml = File.ReadAllText(FindRepoFile(Path.Combine(
            "STranslate.Plugin.Tts.FishAudio",
            "Languages",
            $"{locale}.xaml")));
        var freeDescriptionMatch = Regex.Match(
            xaml,
            "<sys:String x:Key=\"STranslate_Plugin_Tts_FishAudio_Engine_Description_Free\">(?<text>.*?)</sys:String>",
            RegexOptions.CultureInvariant);

        AssertEqual(true, freeDescriptionMatch.Success, $"{locale} should define the free-model description");
        var freeDescription = freeDescriptionMatch.Groups["text"].Value;
        AssertEqual(true, freeDescription.Contains(lastFreeDate, StringComparison.Ordinal), $"{locale} free-model description should use the July 31 deadline");
        AssertEqual(true, freeDescription.Contains("UTC", StringComparison.Ordinal), $"{locale} free-model description should identify the deadline as UTC");
        AssertEqual(false, freeDescription.Contains(oldFreeDate, StringComparison.Ordinal), $"{locale} free-model description should not retain the old July 24 deadline");
    }

    foreach (var relativePath in new[]
             {
                 Path.Combine("docs", "api-tts.md"),
                 Path.Combine("docs", "DESIGN_DECISIONS.md"),
             })
    {
        var documentation = File.ReadAllText(FindRepoFile(relativePath));
        AssertEqual(true, documentation.Contains(lastFreeDate, StringComparison.Ordinal), $"{relativePath} should use the July 31 free-model deadline");
        AssertEqual(true, documentation.Contains("UTC", StringComparison.Ordinal), $"{relativePath} should identify the free-model deadline as UTC");
    }

    var changelog = File.ReadAllText(FindRepoFile("CHANGELOG.md"));
    AssertEqual(
        true,
        changelog.Contains("从 2026-07-24 延长至 2026-07-31", StringComparison.Ordinal),
        "Unreleased changelog should explicitly record both the old and extended free-model deadlines");
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

static async Task StartupVoiceUiApplyRechecksExpectedVoiceIdAsync()
{
    using var network = OverrideNetworkAvailability(true);
    const string requestedVoiceId = "fedcba9876543210fedcba9876543210";
    const string replacementVoiceId = "0123456789abcdef0123456789abcdef";
    var backingStore = new SharedSettingsBackingStore(new Settings
    {
        VoiceId = requestedVoiceId,
        SelectedModel = FishAudioRuntime.S2ProModel,
        CachedVoice = new CachedVoiceInfo { Title = "Voice A (old)" },
    });
    var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var startupVoiceSaved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var queuedUiApply = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (url, _, _) =>
    {
        if (string.Equals(url, FishAudioRuntime.TimeApiUrl, StringComparison.Ordinal))
            return Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");

        requestStarted.TrySetResult();
        return releaseResponse.Task;
    };
    var context = CreateContext(httpService: httpService);
    var contextProxy = (ContextProxy)(object)context;
    contextProxy.LoadSettings = backingStore.Load;
    contextProxy.SaveSettings = backingStore.Save;
    contextProxy.OnSave = () => startupVoiceSaved.TrySetResult();
    Settings? runtimeSettings = null;
    var plugin = new Main((viewModelContext, viewModelSettings, settingsWriteLease, startupCreditRefreshCycle) =>
    {
        runtimeSettings = viewModelSettings;
        return new SettingsViewModel(
            viewModelContext,
            viewModelSettings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            startupCreditRefreshCycle: startupCreditRefreshCycle,
            settingsWriteLease: settingsWriteLease);
    });

    plugin.Init(context, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var viewModel = plugin.GetOrCreateSettingsViewModel();
    await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    SettingsViewModel.UiThreadInvokerOverride = action => queuedUiApply.TrySetResult(action);
    try
    {
        releaseResponse.SetResult($$"""
            {"_id":"{{requestedVoiceId}}","title":"Voice A (fresh)","description":"fresh response","cover_image":"","samples":[{"audio":"https://audio.example/voice-a.mp3"}],"task_count":1}
            """);
        await startupVoiceSaved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var applyStartupVoiceToUi = await queuedUiApply.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.SelectVoiceCommand.Execute(new VoiceSearchItem
        {
            Id = replacementVoiceId,
            Title = "Voice B",
            Description = "Replacement voice",
            SampleAudioUrl = "https://audio.example/voice-b.mp3",
        });
        applyStartupVoiceToUi();
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    }
    finally
    {
        SettingsViewModel.UiThreadInvokerOverride = null;
    }

    var persistedSettings = backingStore.Load();
    AssertEqual(2, contextProxy.SaveCount, "The final startup UI apply should not issue another save after the user selects another voice");
    AssertEqual(replacementVoiceId, runtimeSettings?.VoiceId, "The final startup UI apply should preserve the runtime replacement Voice ID");
    AssertEqual("Voice B", runtimeSettings?.CachedVoice?.Title, "The final startup UI apply should preserve runtime replacement metadata");
    AssertEqual(replacementVoiceId, persistedSettings.VoiceId, "The final startup UI apply should preserve the persisted replacement Voice ID");
    AssertEqual("Voice B", persistedSettings.CachedVoice?.Title, "The final startup UI apply should preserve persisted replacement metadata");
    AssertEqual(replacementVoiceId, viewModel.VoiceId, "The final startup UI apply should preserve the visible replacement Voice ID");
    AssertEqual("Voice B", viewModel.CachedVoiceTitle, "The final startup UI apply should preserve visible replacement metadata");
}

static async Task DelayedStartupVoiceRefreshDoesNotOverwriteNewSelectionAsync()
{
    using var network = OverrideNetworkAvailability(true);
    const string requestedVoiceId = "fedcba9876543210fedcba9876543210";
    const string replacementVoiceId = "0123456789abcdef0123456789abcdef";
    var backingStore = new SharedSettingsBackingStore(new Settings
    {
        VoiceId = requestedVoiceId,
        SelectedModel = FishAudioRuntime.S2ProModel,
        CachedVoice = new CachedVoiceInfo { Title = "Voice A (old)" },
    });
    var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (url, _, _) =>
    {
        if (string.Equals(url, FishAudioRuntime.TimeApiUrl, StringComparison.Ordinal))
            return Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");

        requestStarted.TrySetResult();
        return releaseResponse.Task;
    };
    var context = CreateContext(httpService: httpService);
    var contextProxy = (ContextProxy)(object)context;
    contextProxy.LoadSettings = backingStore.Load;
    contextProxy.SaveSettings = backingStore.Save;
    Settings? runtimeSettings = null;
    var plugin = new Main((viewModelContext, viewModelSettings, settingsWriteLease, startupCreditRefreshCycle) =>
    {
        runtimeSettings = viewModelSettings;
        return new SettingsViewModel(
            viewModelContext,
            viewModelSettings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            startupCreditRefreshCycle: startupCreditRefreshCycle,
            settingsWriteLease: settingsWriteLease);
    });

    plugin.Init(context, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var viewModel = plugin.GetOrCreateSettingsViewModel();
    await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    viewModel.SelectVoiceCommand.Execute(new VoiceSearchItem
    {
        Id = replacementVoiceId,
        Title = "Voice B",
        Description = "Replacement voice",
        SampleAudioUrl = "https://audio.example/voice-b.mp3",
    });

    releaseResponse.SetResult($$"""
        {"_id":"{{requestedVoiceId}}","title":"Voice A (fresh)","description":"stale response","cover_image":"","samples":[{"audio":"https://audio.example/voice-a.mp3"}],"task_count":1}
        """);
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    var persistedSettings = backingStore.Load();

    AssertEqual(1, contextProxy.SaveCount, "A delayed startup response should not save after the user selects another voice");
    AssertEqual(replacementVoiceId, runtimeSettings?.VoiceId, "A delayed startup response should preserve the runtime replacement Voice ID");
    AssertEqual("Voice B", runtimeSettings?.CachedVoice?.Title, "A delayed startup response should preserve runtime replacement metadata");
    AssertEqual(replacementVoiceId, contextProxy.Settings.VoiceId, "A delayed startup response should preserve the host-owned replacement Voice ID");
    AssertEqual("Voice B", contextProxy.Settings.CachedVoice?.Title, "A delayed startup response should preserve host-owned replacement metadata");
    AssertEqual(replacementVoiceId, persistedSettings.VoiceId, "A delayed startup response should preserve the persisted replacement Voice ID");
    AssertEqual("Voice B", persistedSettings.CachedVoice?.Title, "A delayed startup response should preserve persisted replacement metadata");
    AssertEqual(replacementVoiceId, viewModel.VoiceId, "A delayed startup response should preserve the visible replacement Voice ID");
    AssertEqual("Voice B", viewModel.CachedVoiceTitle, "A delayed startup response should preserve visible replacement metadata");
}

static async Task DelayedStartupVoiceRefreshDoesNotRestoreClearedVoiceAsync()
{
    using var network = OverrideNetworkAvailability(true);
    const string requestedVoiceId = "fedcba9876543210fedcba9876543210";
    var backingStore = new SharedSettingsBackingStore(new Settings
    {
        VoiceId = requestedVoiceId,
        SelectedModel = FishAudioRuntime.S2ProModel,
        CachedVoice = new CachedVoiceInfo { Title = "Voice A (old)" },
    });
    var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (url, _, _) =>
    {
        if (string.Equals(url, FishAudioRuntime.TimeApiUrl, StringComparison.Ordinal))
            return Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");

        requestStarted.TrySetResult();
        return releaseResponse.Task;
    };
    var context = CreateContext(httpService: httpService);
    var contextProxy = (ContextProxy)(object)context;
    contextProxy.LoadSettings = backingStore.Load;
    contextProxy.SaveSettings = backingStore.Save;
    Settings? runtimeSettings = null;
    var plugin = new Main((viewModelContext, viewModelSettings, settingsWriteLease, startupCreditRefreshCycle) =>
    {
        runtimeSettings = viewModelSettings;
        return new SettingsViewModel(
            viewModelContext,
            viewModelSettings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            startupCreditRefreshCycle: startupCreditRefreshCycle,
            settingsWriteLease: settingsWriteLease);
    });

    plugin.Init(context, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var viewModel = plugin.GetOrCreateSettingsViewModel();
    await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    viewModel.ClearVoiceCommand.Execute(null);

    releaseResponse.SetResult($$"""
        {"_id":"{{requestedVoiceId}}","title":"Voice A (fresh)","description":"stale response","cover_image":"","samples":[{"audio":"https://audio.example/voice-a.mp3"}],"task_count":1}
        """);
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    var persistedSettings = backingStore.Load();

    AssertEqual(1, contextProxy.SaveCount, "A delayed startup response should not save after the user clears the selected voice");
    AssertEqual("", runtimeSettings?.VoiceId, "A delayed startup response should preserve the cleared runtime Voice ID");
    AssertEqual(null, runtimeSettings?.CachedVoice, "A delayed startup response should preserve cleared runtime metadata");
    AssertEqual("", contextProxy.Settings.VoiceId, "A delayed startup response should preserve the cleared host-owned Voice ID");
    AssertEqual(null, contextProxy.Settings.CachedVoice, "A delayed startup response should preserve cleared host-owned metadata");
    AssertEqual("", persistedSettings.VoiceId, "A delayed startup response should preserve the cleared persisted Voice ID");
    AssertEqual(null, persistedSettings.CachedVoice, "A delayed startup response should preserve cleared persisted metadata");
    AssertEqual("", viewModel.VoiceId, "A delayed startup response should preserve the visible cleared Voice ID");
    AssertEqual(null, viewModel.CachedVoiceTitle, "A delayed startup response should preserve cleared visible metadata");
}

static async Task ConcurrentPaidModelSelectionStillPublishesPostCutoffPolicyAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var backingStore = new SharedSettingsBackingStore(new Settings
    {
        SelectedModel = FishAudioRuntime.S21ProFreeModel,
    });
    var releaseOnlineUtc = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var revisionPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseRevision = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (_, _, _) => releaseOnlineUtc.Task;
    var context = CreateContext(httpService: httpService);
    var contextProxy = (ContextProxy)(object)context;
    contextProxy.LoadSettings = backingStore.Load;
    contextProxy.SaveSettings = backingStore.Save;
    Settings? runtimeSettings = null;
    var plugin = new Main(
        (viewModelContext, viewModelSettings, settingsWriteLease, startupCreditRefreshCycle) =>
        {
            runtimeSettings = viewModelSettings;
            return new SettingsViewModel(
                viewModelContext,
                viewModelSettings,
                clearCoverImageCacheAsync: null,
                clearCoverImageCacheTimeout: null,
                startupCreditRefreshCycle: startupCreditRefreshCycle,
                settingsWriteLease: settingsWriteLease);
        },
        startupSettingsRevisionPublished: () =>
        {
            revisionPublished.TrySetResult();
            releaseRevision.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        });

    plugin.Init(context, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var viewModel = plugin.GetOrCreateSettingsViewModel();
    releaseOnlineUtc.SetResult("{\"dateTime\":\"2026-08-01T00:00:00Z\"}");
    await revisionPublished.Task.WaitAsync(TimeSpan.FromSeconds(2));

    viewModel.SelectedModel = FishAudioRuntime.S2ProModel;
    AssertEqual(1, contextProxy.SaveCount, "Selecting a paid model while startup is reserved should save the user choice once");
    releaseRevision.SetResult();
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(FishAudioRuntime.S2ProModel, runtimeSettings?.SelectedModel, "Post-cutoff policy publication should preserve the concurrent runtime paid-model choice");
    AssertEqual(FishAudioRuntime.S2ProModel, viewModel.SelectedModel, "Post-cutoff policy publication should preserve the visible paid-model choice");
    AssertEqual(FishAudioRuntime.S2ProModel, contextProxy.Settings.SelectedModel, "Post-cutoff policy publication should preserve the host-owned paid-model choice");
    AssertEqual(FishAudioRuntime.S2ProModel, backingStore.Load().SelectedModel, "Post-cutoff policy publication should preserve the persisted paid-model choice");
    AssertEqual(false, viewModel.Models.Contains(FishAudioRuntime.S21ProFreeModel, StringComparer.Ordinal), "Post-cutoff policy publication should remove the free model from the available list");
    AssertEqual(false, viewModel.IsS21ProFreeAvailable, "Post-cutoff policy publication should mark the free-model promo unavailable");
    AssertEqual(false, viewModel.ShowS21ProFreePromo, "Post-cutoff policy publication should hide the free-model promo");
    AssertEqual(2, contextProxy.SaveCount, "Post-cutoff policy publication should complete its accepted settings transaction after the user save");

    viewModel.UseS21ProFreePromoCommand.Execute(null);

    AssertEqual(FishAudioRuntime.S2ProModel, runtimeSettings?.SelectedModel, "An unavailable promo should not replace the runtime paid-model choice");
    AssertEqual(FishAudioRuntime.S2ProModel, viewModel.SelectedModel, "An unavailable promo should not replace the visible paid-model choice");
    AssertEqual(FishAudioRuntime.S2ProModel, backingStore.Load().SelectedModel, "An unavailable promo should not replace the persisted paid-model choice");
    AssertEqual(2, contextProxy.SaveCount, "An unavailable promo should not issue another settings save");
}

static async Task StartupModelSaveFailureRollsBackRuntimeAndStorageAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var backingStore = new SharedSettingsBackingStore(new Settings
    {
        SelectedModel = FishAudioRuntime.S21ProFreeModel,
    });
    var releaseOnlineUtc = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (_, _, _) => releaseOnlineUtc.Task;
    var context = CreateContext(httpService: httpService);
    var contextProxy = (ContextProxy)(object)context;
    contextProxy.LoadSettings = backingStore.Load;
    var failStartupSave = true;
    contextProxy.SaveSettings = settings =>
    {
        if (failStartupSave)
        {
            failStartupSave = false;
            throw new InvalidOperationException("expected startup model save failure");
        }

        backingStore.Save(settings);
    };
    Settings? runtimeSettings = null;
    var plugin = new Main((viewModelContext, viewModelSettings, settingsWriteLease, startupCreditRefreshCycle) =>
    {
        runtimeSettings = viewModelSettings;
        return new SettingsViewModel(
            viewModelContext,
            viewModelSettings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            startupCreditRefreshCycle: startupCreditRefreshCycle,
            settingsWriteLease: settingsWriteLease);
    });

    plugin.Init(context, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var viewModel = plugin.GetOrCreateSettingsViewModel();
    releaseOnlineUtc.SetResult("{\"dateTime\":\"2026-08-01T00:00:00Z\"}");
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(FishAudioRuntime.S21ProFreeModel, runtimeSettings?.SelectedModel, "A failed startup model save should roll back the runtime Settings model");
    AssertEqual(FishAudioRuntime.S21ProFreeModel, viewModel.SelectedModel, "A failed startup model save should leave the existing ViewModel model unchanged");
    AssertEqual(FishAudioRuntime.S21ProFreeModel, contextProxy.Settings.SelectedModel, "A failed startup model save should roll back the host-owned Settings object");
    AssertEqual(FishAudioRuntime.S21ProFreeModel, backingStore.Load().SelectedModel, "A failed startup model save should preserve backing storage");

    viewModel.ApiKey = DraftKey;
    var savedSnapshot = backingStore.Load();
    AssertEqual(2, contextProxy.SaveCount, "A later ViewModel edit should still save after startup model rollback");
    AssertEqual(DraftKey, savedSnapshot.ApiKey, "A later ViewModel edit should persist normally after startup model rollback");
    AssertEqual(FishAudioRuntime.S21ProFreeModel, savedSnapshot.SelectedModel, "A later save should not resurrect the rolled-back startup model");
}

static async Task StartupVoiceSaveFailureRollsBackRuntimeAndStorageAsync()
{
    using var network = OverrideNetworkAvailability(true);
    const string voiceId = "fedcba9876543210fedcba9876543210";
    var backingStore = new SharedSettingsBackingStore(new Settings
    {
        VoiceId = voiceId,
        SelectedModel = FishAudioRuntime.S2ProModel,
        CachedVoice = new CachedVoiceInfo { Title = "Old Voice" },
    });
    var releaseOnlineUtc = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (url, _, _) =>
        string.Equals(url, FishAudioRuntime.TimeApiUrl, StringComparison.Ordinal)
            ? releaseOnlineUtc.Task
            : Task.FromResult($$"""
                {"_id":"{{voiceId}}","title":"Fresh Voice","description":"","cover_image":"","samples":[],"task_count":0}
                """);
    var context = CreateContext(httpService: httpService);
    var contextProxy = (ContextProxy)(object)context;
    contextProxy.LoadSettings = backingStore.Load;
    var failStartupSave = true;
    contextProxy.SaveSettings = settings =>
    {
        if (failStartupSave)
        {
            failStartupSave = false;
            throw new InvalidOperationException("expected startup voice save failure");
        }

        backingStore.Save(settings);
    };
    Settings? runtimeSettings = null;
    var plugin = new Main((viewModelContext, viewModelSettings, settingsWriteLease, startupCreditRefreshCycle) =>
    {
        runtimeSettings = viewModelSettings;
        return new SettingsViewModel(
            viewModelContext,
            viewModelSettings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            startupCreditRefreshCycle: startupCreditRefreshCycle,
            settingsWriteLease: settingsWriteLease);
    });

    plugin.Init(context, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var viewModel = plugin.GetOrCreateSettingsViewModel();
    releaseOnlineUtc.SetResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual("Old Voice", runtimeSettings?.CachedVoice?.Title, "A failed startup voice save should roll back runtime cached metadata");
    AssertEqual("Old Voice", viewModel.CachedVoiceTitle, "A failed startup voice save should leave the existing ViewModel metadata unchanged");
    AssertEqual("Old Voice", contextProxy.Settings.CachedVoice?.Title, "A failed startup voice save should roll back the host-owned cached metadata");
    AssertEqual("Old Voice", backingStore.Load().CachedVoice?.Title, "A failed startup voice save should preserve cached metadata in backing storage");

    viewModel.ApiKey = DraftKey;
    var savedSnapshot = backingStore.Load();
    AssertEqual(2, contextProxy.SaveCount, "A later ViewModel edit should still save after startup voice rollback");
    AssertEqual(DraftKey, savedSnapshot.ApiKey, "A later ViewModel edit should persist after startup voice rollback");
    AssertEqual("Old Voice", savedSnapshot.CachedVoice?.Title, "A later save should not resurrect rolled-back startup voice metadata");
}

static async Task StartupModelNormalizationInvalidatesSpeculativeSettingsViewModelAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings
    {
        SelectedModel = FishAudioRuntime.S21ProFreeModel,
    };
    var releaseOnlineUtc = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var constructionSnapshotCaptured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseConstruction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var startupModelSaved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (_, _, _) => releaseOnlineUtc.Task;
    var context = CreateContext(settings: settings, httpService: httpService);
    var contextProxy = (ContextProxy)(object)context;
    contextProxy.OnSave = () => startupModelSaved.TrySetResult();
    var plugin = new Main((viewModelContext, viewModelSettings, settingsWriteLease, startupCreditRefreshCycle) =>
        new BlockingSettingsViewModel(
            viewModelContext,
            viewModelSettings,
            settingsWriteLease,
            startupCreditRefreshCycle,
            constructionSnapshotCaptured,
            releaseConstruction));

    plugin.Init(context, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var viewModelTask = Task.Run(plugin.GetOrCreateSettingsViewModel);
    await constructionSnapshotCaptured.Task.WaitAsync(TimeSpan.FromSeconds(2));

    releaseOnlineUtc.SetResult("{\"dateTime\":\"2026-08-01T00:00:00Z\"}");
    await startupModelSaved.Task.WaitAsync(TimeSpan.FromSeconds(2));
    releaseConstruction.SetResult();

    var viewModel = await viewModelTask.WaitAsync(TimeSpan.FromSeconds(2));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(FishAudioRuntime.S21ProModel, settings.SelectedModel, "Startup online time should normalize the current Settings model");
    AssertEqual(FishAudioRuntime.S21ProModel, viewModel.SelectedModel, "A ViewModel constructed across startup normalization should retry with the current model");
    AssertEqual(1, contextProxy.SaveCount, "Disposing a stale model-snapshot candidate should not repeat the startup settings save");
}

static async Task CommittedStartupSettingsInvalidatesPostReservationViewModelCandidateAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings
    {
        SelectedModel = FishAudioRuntime.S21ProFreeModel,
    };
    var releaseOnlineUtc = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var revisionPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseRevision = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var constructionSnapshotCaptured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseConstruction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var startupModelSaved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (_, _, _) => releaseOnlineUtc.Task;
    var context = CreateContext(settings: settings, httpService: httpService);
    var contextProxy = (ContextProxy)(object)context;
    contextProxy.OnSave = () => startupModelSaved.TrySetResult();
    var plugin = new Main(
        (viewModelContext, viewModelSettings, settingsWriteLease, startupCreditRefreshCycle) =>
            new BlockingSettingsViewModel(
                viewModelContext,
                viewModelSettings,
                settingsWriteLease,
                startupCreditRefreshCycle,
                constructionSnapshotCaptured,
                releaseConstruction),
        startupSettingsRevisionPublished: () =>
        {
            revisionPublished.TrySetResult();
            releaseRevision.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        });

    plugin.Init(context, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    releaseOnlineUtc.SetResult("{\"dateTime\":\"2026-08-01T00:00:00Z\"}");
    await revisionPublished.Task.WaitAsync(TimeSpan.FromSeconds(2));

    var viewModelTask = Task.Run(plugin.GetOrCreateSettingsViewModel);
    await constructionSnapshotCaptured.Task.WaitAsync(TimeSpan.FromSeconds(2));
    releaseRevision.SetResult();
    await startupModelSaved.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    releaseConstruction.SetResult();

    var viewModel = await viewModelTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(FishAudioRuntime.S21ProModel, settings.SelectedModel, "The committed startup transaction should update host settings");
    AssertEqual(FishAudioRuntime.S21ProModel, viewModel.SelectedModel, "A ViewModel candidate captured after revision reservation should retry after runtime commit");
    AssertEqual(1, contextProxy.SaveCount, "Invalidating the post-reservation candidate should not repeat the startup settings save");
}

static async Task StartupVoiceRefreshInvalidatesSpeculativeSettingsViewModelAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings
    {
        SelectedModel = FishAudioRuntime.S2ProModel,
        VoiceId = "fedcba9876543210fedcba9876543210",
        CachedVoice = new CachedVoiceInfo { Title = "Old Voice" },
    };
    var releaseOnlineUtc = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var constructionSnapshotCaptured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseConstruction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var startupVoiceSaved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (url, _, _) =>
        string.Equals(url, FishAudioRuntime.TimeApiUrl, StringComparison.Ordinal)
            ? releaseOnlineUtc.Task
            : Task.FromResult("{\"_id\":\"fedcba9876543210fedcba9876543210\",\"title\":\"Fresh Voice\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}");
    var context = CreateContext(settings: settings, httpService: httpService);
    var contextProxy = (ContextProxy)(object)context;
    contextProxy.OnSave = () => startupVoiceSaved.TrySetResult();
    var plugin = new Main((viewModelContext, viewModelSettings, settingsWriteLease, startupCreditRefreshCycle) =>
        new BlockingSettingsViewModel(
            viewModelContext,
            viewModelSettings,
            settingsWriteLease,
            startupCreditRefreshCycle,
            constructionSnapshotCaptured,
            releaseConstruction));

    plugin.Init(context, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var viewModelTask = Task.Run(plugin.GetOrCreateSettingsViewModel);
    await constructionSnapshotCaptured.Task.WaitAsync(TimeSpan.FromSeconds(2));

    releaseOnlineUtc.SetResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    await startupVoiceSaved.Task.WaitAsync(TimeSpan.FromSeconds(2));
    releaseConstruction.SetResult();

    var viewModel = await viewModelTask.WaitAsync(TimeSpan.FromSeconds(2));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual("Fresh Voice", settings.CachedVoice?.Title, "Startup voice refresh should update the current Settings cache");
    AssertEqual("Fresh Voice", viewModel.CachedVoiceTitle, "A ViewModel constructed across startup voice refresh should retry with current cached metadata");
    AssertEqual(1, contextProxy.SaveCount, "Disposing a stale cached-voice candidate should not repeat the startup settings save");
}

static async Task StartupSettingsSaveSkipsAfterNewInitializationRequestAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var backingStore = new SharedSettingsBackingStore(new Settings
    {
        ApiKey = AppliedKey,
        SelectedModel = FishAudioRuntime.S21ProFreeModel,
    });
    var releaseOldOnlineUtc = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var revisionPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseRevision = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var oldSaveCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var (oldHttpService, oldHttp) = TestHttpServiceProxy.Create();
    oldHttp.GetAsyncHandler = (_, _, _) => releaseOldOnlineUtc.Task;
    var oldContext = CreateContext(httpService: oldHttpService);
    var oldContextProxy = (ContextProxy)(object)oldContext;
    oldContextProxy.LoadSettings = backingStore.Load;
    oldContextProxy.SaveSettings = settings =>
    {
        backingStore.Save(settings);
        oldSaveCompleted.TrySetResult();
    };
    var plugin = new Main(
        (_, _, _, _) => throw new InvalidOperationException("This test should not construct a settings ViewModel."),
        startupSettingsRevisionPublished: () =>
        {
            revisionPublished.TrySetResult();
            releaseRevision.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        });

    plugin.Init(oldContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var oldStartupTask = plugin.PendingStartupTask;
    releaseOldOnlineUtc.SetResult("{\"dateTime\":\"2026-08-01T00:00:00Z\"}");
    await revisionPublished.Task.WaitAsync(TimeSpan.FromSeconds(2));

    backingStore.Replace(new Settings
    {
        ApiKey = DraftKey,
        SelectedModel = FishAudioRuntime.S1Model,
    });
    var newLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseNewLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var (newHttpService, newHttp) = TestHttpServiceProxy.Create();
    newHttp.GetAsyncHandler = (_, _, _) => Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    var newContext = CreateContext(httpService: newHttpService);
    var newContextProxy = (ContextProxy)(object)newContext;
    newContextProxy.LoadSettings = () =>
    {
        var snapshot = backingStore.Load();
        newLoadStarted.TrySetResult();
        releaseNewLoad.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        return snapshot;
    };
    newContextProxy.SaveSettings = backingStore.Save;

    var reinitializeTask = Task.Run(() =>
        plugin.Init(newContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1)));
    await newLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    releaseRevision.SetResult();
    var staleSaveCompletedBeforeLoad = ReferenceEquals(
        await Task.WhenAny(oldSaveCompleted.Task, Task.Delay(TimeSpan.FromMilliseconds(500))),
        oldSaveCompleted.Task);
    releaseNewLoad.SetResult();

    await reinitializeTask.WaitAsync(TimeSpan.FromSeconds(2));
    await oldStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    var finalSnapshot = backingStore.Load();

    AssertEqual(false, staleSaveCompletedBeforeLoad, "A startup settings save should wait while the replacement initialization is loading");
    AssertEqual(0, oldContextProxy.SaveCount, "A newer initialization request should invalidate the old startup settings save");
    AssertEqual(DraftKey, finalSnapshot.ApiKey, "An invalidated old startup save should not overwrite the replacement backing-store snapshot");
}

static async Task InitializationLoadWaitsForAuthorizedStartupSettingsSaveAsync()
{
    using var network = OverrideNetworkAvailability(true);
    const string voiceId = "fedcba9876543210fedcba9876543210";
    var backingStore = new SharedSettingsBackingStore(new Settings
    {
        VoiceId = voiceId,
        SelectedModel = FishAudioRuntime.S2ProModel,
        CachedVoice = new CachedVoiceInfo { Title = "Old Voice" },
    });
    var releaseOldOnlineUtc = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var oldSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseOldSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var (oldHttpService, oldHttp) = TestHttpServiceProxy.Create();
    oldHttp.GetAsyncHandler = (url, _, _) =>
        string.Equals(url, FishAudioRuntime.TimeApiUrl, StringComparison.Ordinal)
            ? releaseOldOnlineUtc.Task
            : Task.FromResult($$"""
                {"_id":"{{voiceId}}","title":"Fresh Voice","description":"","cover_image":"","samples":[],"task_count":0}
                """);
    var oldContext = CreateContext(httpService: oldHttpService);
    var oldContextProxy = (ContextProxy)(object)oldContext;
    oldContextProxy.LoadSettings = backingStore.Load;
    oldContextProxy.SaveSettings = settings =>
    {
        oldSaveStarted.TrySetResult();
        releaseOldSave.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        backingStore.Save(settings);
    };
    var plugin = new Main();

    plugin.Init(oldContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var oldStartupTask = plugin.PendingStartupTask;
    releaseOldOnlineUtc.SetResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    await oldSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    var newLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var (newHttpService, newHttp) = TestHttpServiceProxy.Create();
    newHttp.GetAsyncHandler = (url, _, _) =>
        string.Equals(url, FishAudioRuntime.TimeApiUrl, StringComparison.Ordinal)
            ? Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}")
            : Task.FromResult($$"""
                {"_id":"{{voiceId}}","title":"Fresh Voice","description":"","cover_image":"","samples":[],"task_count":0}
                """);
    var newContext = CreateContext(httpService: newHttpService);
    var newContextProxy = (ContextProxy)(object)newContext;
    newContextProxy.LoadSettings = () =>
    {
        var snapshot = backingStore.Load();
        newLoadStarted.TrySetResult();
        return snapshot;
    };
    newContextProxy.SaveSettings = backingStore.Save;

    var reinitializeTask = Task.Run(() =>
        plugin.Init(newContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1)));
    await WaitUntilAsync(() => plugin.InitializationGeneration >= 2, "replacement initialization request to publish its generation");
    var loadStartedBeforeSaveCompleted = ReferenceEquals(
        await Task.WhenAny(newLoadStarted.Task, Task.Delay(TimeSpan.FromMilliseconds(500))),
        newLoadStarted.Task);

    releaseOldSave.SetResult();
    await reinitializeTask.WaitAsync(TimeSpan.FromSeconds(2));
    await oldStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(false, loadStartedBeforeSaveCompleted, "A replacement load should wait for an already-authorized startup settings save");
    AssertEqual("Fresh Voice", newContextProxy.Settings.CachedVoice?.Title, "The replacement load should observe the completed startup settings save");
}

static async Task OvertakenInitializationSkipsSettingsLoadAsync()
{
    using var network = OverrideNetworkAvailability(false);
    var staleGenerationDeclared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseStaleGeneration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var plugin = new Main(
        (_, _, _, _) => throw new InvalidOperationException("This test should not construct a settings ViewModel."),
        initializationGenerationDeclared: generation =>
        {
            if (generation != 2)
                return;

            staleGenerationDeclared.TrySetResult();
            releaseStaleGeneration.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        });
    plugin.Init(CreateContext(settings: new Settings { ApiKey = AppliedKey }), FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));

    var staleContext = CreateContext(settings: new Settings
    {
        ApiKey = "ffffffffffffffffffffffffffffffff",
        SelectedModel = "obsolete-model",
    });
    var staleContextProxy = (ContextProxy)(object)staleContext;
    var staleInitializationTask = Task.Run(() =>
        plugin.Init(staleContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1)));
    await staleGenerationDeclared.Task.WaitAsync(TimeSpan.FromSeconds(2));

    var currentSettings = new Settings { ApiKey = DraftKey };
    var currentContext = CreateContext(settings: currentSettings);
    plugin.Init(currentContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    releaseStaleGeneration.SetResult();
    await staleInitializationTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(0, staleContextProxy.LoadCount, "An overtaken initialization should not call SettingsStore.Load");
    AssertEqual(0, staleContextProxy.SaveCount, "An overtaken initialization should not canonical-save stale settings");
    AssertEqual(DraftKey, ((ContextProxy)(object)currentContext).Settings.ApiKey, "The newest initialization should remain current");
}

static async Task FailedReinitializationRestoresStartupSettingsSaveAuthorizationAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings { SelectedModel = FishAudioRuntime.S21ProFreeModel };
    var releaseOldOnlineUtc = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var revisionPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseRevision = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var (oldHttpService, oldHttp) = TestHttpServiceProxy.Create();
    oldHttp.GetAsyncHandler = (_, _, _) => releaseOldOnlineUtc.Task;
    var oldContext = CreateContext(settings: settings, httpService: oldHttpService);
    var oldContextProxy = (ContextProxy)(object)oldContext;
    var plugin = new Main(
        (viewModelContext, viewModelSettings, settingsWriteLease, startupCreditRefreshCycle) =>
            new SettingsViewModel(
                viewModelContext,
                viewModelSettings,
                clearCoverImageCacheAsync: null,
                clearCoverImageCacheTimeout: null,
                startupCreditRefreshCycle: startupCreditRefreshCycle,
                settingsWriteLease: settingsWriteLease),
        startupSettingsRevisionPublished: () =>
        {
            revisionPublished.TrySetResult();
            releaseRevision.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        });

    plugin.Init(oldContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var oldStartupTask = plugin.PendingStartupTask;
    var oldViewModel = plugin.GetOrCreateSettingsViewModel();
    releaseOldOnlineUtc.SetResult("{\"dateTime\":\"2026-08-01T00:00:00Z\"}");
    await revisionPublished.Task.WaitAsync(TimeSpan.FromSeconds(2));

    var failedContext = CreateContext();
    ((ContextProxy)(object)failedContext).LoadSettings = () =>
        throw new InvalidOperationException("failed replacement load");
    var loadFailed = false;
    try
    {
        plugin.Init(failedContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    }
    catch (InvalidOperationException ex) when (ex.Message == "failed replacement load")
    {
        loadFailed = true;
    }

    releaseRevision.SetResult();
    await oldStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(true, loadFailed, "The replacement Load failure should remain observable");
    AssertEqual(1, oldContextProxy.SaveCount, "A failed replacement should restore an already-revised startup settings save");
    AssertEqual(FishAudioRuntime.S21ProModel, oldViewModel.SelectedModel, "A failed replacement should let the old ViewModel apply its authorized startup revision");
}

static async Task StartupSettingsSaveWinsBeforeFailedReplacementTransitionAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var settings = new Settings { SelectedModel = FishAudioRuntime.S21ProFreeModel };
    var releaseOldOnlineUtc = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var revisionPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseRevision = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var replacementGenerationDeclared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseReplacementGeneration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var (oldHttpService, oldHttp) = TestHttpServiceProxy.Create();
    oldHttp.GetAsyncHandler = (_, _, _) => releaseOldOnlineUtc.Task;
    var oldContext = CreateContext(settings: settings, httpService: oldHttpService);
    var oldContextProxy = (ContextProxy)(object)oldContext;
    var plugin = new Main(
        (viewModelContext, viewModelSettings, settingsWriteLease, startupCreditRefreshCycle) =>
            new SettingsViewModel(
                viewModelContext,
                viewModelSettings,
                clearCoverImageCacheAsync: null,
                clearCoverImageCacheTimeout: null,
                startupCreditRefreshCycle: startupCreditRefreshCycle,
                settingsWriteLease: settingsWriteLease),
        startupSettingsRevisionPublished: () =>
        {
            revisionPublished.TrySetResult();
            releaseRevision.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        },
        initializationGenerationDeclared: generation =>
        {
            if (generation != 2)
                return;

            replacementGenerationDeclared.TrySetResult();
            releaseReplacementGeneration.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        });

    plugin.Init(oldContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var oldStartupTask = plugin.PendingStartupTask;
    var oldViewModel = plugin.GetOrCreateSettingsViewModel();
    releaseOldOnlineUtc.SetResult("{\"dateTime\":\"2026-08-01T00:00:00Z\"}");
    await revisionPublished.Task.WaitAsync(TimeSpan.FromSeconds(2));

    var failedContext = CreateContext();
    ((ContextProxy)(object)failedContext).LoadSettings = () =>
        throw new InvalidOperationException("failed replacement transition");
    var replacementFailed = false;
    var replacementTask = Task.Run(() =>
    {
        try
        {
            plugin.Init(failedContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
        }
        catch (InvalidOperationException ex) when (ex.Message == "failed replacement transition")
        {
            replacementFailed = true;
        }
    });
    await replacementGenerationDeclared.Task.WaitAsync(TimeSpan.FromSeconds(2));

    releaseRevision.SetResult();
    await oldStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(1, oldContextProxy.SaveCount, "Startup settings Save should complete when it wins the transition gate before replacement Load");
    AssertEqual(FishAudioRuntime.S21ProModel, oldViewModel.SelectedModel, "The winning startup Save should apply its model revision to the old ViewModel");

    releaseReplacementGeneration.SetResult();
    await replacementTask.WaitAsync(TimeSpan.FromSeconds(2));
    AssertEqual(true, replacementFailed, "The later replacement Load failure should remain observable");
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
        var viewModel = new SettingsViewModel(context, settings, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));

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
        settings);

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
    var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings);

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
        settings);

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
        settings);

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
    var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings)
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
    var viewModel = new SettingsViewModel(context, settings)
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
    var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings);
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

static async Task VoiceLookupCompletionsAfterDisposeDoNotMutateStateAsync()
{
    var settings = new Settings();
    var (httpService, http) = TestHttpServiceProxy.Create();
    var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings)
    {
        SearchQuery = "late",
    };
    var searchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseSearch = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    http.GetAsyncHandler = (_, _, _) =>
    {
        searchStarted.SetResult();
        return releaseSearch.Task;
    };

    var searchTask = viewModel.SearchVoicesCommand.ExecuteAsync(null);
    await searchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    viewModel.Dispose();
    releaseSearch.SetResult("{\"total\":1,\"items\":[{\"_id\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"title\":\"Late Search\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":1}]}");
    await searchTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(false, viewModel.IsSearching, "Disposing during search should restore search busy state");
    AssertEqual(false, viewModel.HasSearched, "Search completion after dispose should not mark search as completed");
    AssertEqual(0, viewModel.SearchResults.Count, "Search completion after dispose should not apply stale results");

    const string originalVoiceId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    settings = new Settings
    {
        VoiceId = originalVoiceId,
        CachedVoice = new CachedVoiceInfo { Title = "Existing Voice" },
    };
    (httpService, http) = TestHttpServiceProxy.Create();
    var context = CreateContext(settings: settings, httpService: httpService);
    var proxy = (ContextProxy)(object)context;
    viewModel = new SettingsViewModel(context, settings)
    {
        VoiceIdInput = "cccccccccccccccccccccccccccccccc",
    };
    var byIdStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseById = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    http.GetAsyncHandler = (_, _, _) =>
    {
        byIdStarted.SetResult();
        return releaseById.Task;
    };

    var submitTask = viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
    await byIdStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    viewModel.Dispose();
    releaseById.SetResult("{\"_id\":\"cccccccccccccccccccccccccccccccc\",\"title\":\"Late Voice\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}");
    await submitTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(false, viewModel.IsSubmittingVoiceId, "Disposing during by-ID lookup should restore submit busy state");
    AssertEqual(originalVoiceId, settings.VoiceId, "By-ID completion after dispose should not update saved voice ID");
    AssertEqual("Existing Voice", settings.CachedVoice?.Title, "By-ID completion after dispose should not update cached voice");
    AssertEqual(0, proxy.SaveCount, "Dispose and late by-ID completion should not issue redundant or stale saves");
}

static async Task SearchPaginationUpdatesVisiblePageAfterSuccessOnlyAsync()
{
    var settings = new Settings();
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetResponseJson = "{\"total\":12,\"items\":[{\"_id\":\"11111111111111111111111111111111\",\"title\":\"Page 1\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":1}]}";
    var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings);

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

static void PreviewAudioUrlValidationAllowsOnlyFishAudioStorageHosts()
{
    AssertPreviewAudioUrlAllowed("https://platform.r2.fish.audio/audio/sample.mp3");
    AssertPreviewAudioUrlAllowed("https://bucket.r2.cloudflarestorage.com/audio/sample.mp3");
    AssertPreviewAudioUrlAllowed("https://nested.bucket.r2.cloudflarestorage.com/audio/sample.mp3");

    AssertPreviewAudioUrlRejected("file:///C:/Windows/win.ini");
    AssertPreviewAudioUrlRejected(@"\\localhost\share\sample.mp3");
    AssertPreviewAudioUrlRejected(@"C:\Windows\win.ini");
    AssertPreviewAudioUrlRejected("http://platform.r2.fish.audio/audio/sample.mp3");
    AssertPreviewAudioUrlRejected("https://localhost/audio/sample.mp3");
    AssertPreviewAudioUrlRejected("https://127.0.0.1/audio/sample.mp3");
    AssertPreviewAudioUrlRejected("https://[::1]/audio/sample.mp3");
    AssertPreviewAudioUrlRejected("https://public-platform.r2.fish.audio/audio/sample.mp3");
    AssertPreviewAudioUrlRejected("https://evil.example/audio/sample.mp3");
    AssertPreviewAudioUrlRejected("not a url");
}

static async Task PlayAudioUsesOldInitializationWhileReinitLoadIsBlockedAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var oldSettings = CreateTtsSettings(FishAudioRuntime.S2ProModel);
    var oldAudio = new TestAudioPlayer();
    var releaseTts = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
    var ttsRequestStarted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var (oldHttpService, oldHttp) = TestHttpServiceProxy.Create();
    oldHttp.GetAsyncHandler = (url, _, _) => Task.FromResult(
        url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
            ? "{\"credit\":\"1.00\"}"
            : "{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    oldHttp.PostAsBytesAsyncHandler = (_, _, _, _) =>
    {
        ttsRequestStarted.TrySetResult("old");
        return releaseTts.Task;
    };
    var oldContext = CreateContext(
        settings: oldSettings,
        httpService: oldHttpService,
        audioPlayer: oldAudio);
    var plugin = new Main();
    plugin.Init(oldContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    var oldViewModel = plugin.GetOrCreateSettingsViewModel();

    var newSettings = CreateTtsSettings(FishAudioRuntime.S1Model);
    newSettings.ApiKey = DraftKey;
    var newAudio = new TestAudioPlayer();
    var (newHttpService, newHttp) = TestHttpServiceProxy.Create();
    newHttp.GetAsyncHandler = (url, _, _) => Task.FromResult(
        url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
            ? "{\"credit\":\"2.00\"}"
            : "{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    newHttp.PostAsBytesAsyncHandler = (_, _, _, _) =>
    {
        ttsRequestStarted.TrySetResult("new");
        return releaseTts.Task;
    };
    var secondLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseSecondLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var newContext = CreateContext(
        settings: newSettings,
        httpService: newHttpService,
        audioPlayer: newAudio);
    ((ContextProxy)(object)newContext).OnLoad = () =>
    {
        secondLoadStarted.TrySetResult();
        releaseSecondLoad.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
    };

    var reinitializeTask = Task.Run(() =>
        plugin.Init(newContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1)));
    await secondLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    var ttsTask = plugin.PlayAudioAsync("load-window snapshot");
    var requestInitialization = await ttsRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    var oldViewModelLockedDuringTts = oldViewModel.IsApiKeyInputLocked;

    releaseSecondLoad.TrySetResult();
    await reinitializeTask.WaitAsync(TimeSpan.FromSeconds(2));
    var newStartupTask = plugin.PendingStartupTask;
    var newViewModel = plugin.GetOrCreateSettingsViewModel();
    releaseTts.TrySetResult(new byte[] { 7, 8, 9 });
    await ttsTask.WaitAsync(TimeSpan.FromSeconds(2));
    await newStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual("old", requestInitialization, "TTS started during settings load should use the complete old initialization");
    AssertEqual(1, oldHttp.PostAsBytesCallCount, "Load-window TTS should post through the old Context");
    AssertEqual(0, newHttp.PostAsBytesCallCount, "Load-window TTS should not combine the new Context with old Settings");
    var headers = AssertHeaders(oldHttp.LastPostOptions, "Load-window TTS should retain old request headers");
    AssertEqual($"Bearer {AppliedKey}", headers["Authorization"], "Load-window TTS should use the old API Key with the old Context");
    AssertEqual(FishAudioRuntime.S2ProModel, headers["model"], "Load-window TTS should use the old synthesis model with the old Context");
    AssertEqual(true, oldViewModelLockedDuringTts, "Load-window TTS should lock its old settings ViewModel");
    AssertEqual(false, oldViewModel.IsApiKeyInputLocked, "Load-window TTS completion should release its old ViewModel lock");
    AssertEqual(false, ReferenceEquals(oldViewModel, newViewModel), "The completed reinitialization should still replace the settings ViewModel");
    AssertEqual(1, oldAudio.PlayBytesCallCount, "Load-window TTS should play through the old AudioPlayer");
    AssertEqual(0, newAudio.PlayBytesCallCount, "Load-window TTS should not play through the new AudioPlayer");
}

static async Task PlayAudioUsesInitializationSnapshotAcrossReinitAsync()
{
    using var network = OverrideNetworkAvailability(true);
    var oldSettings = CreateTtsSettings("s2-pro");
    var oldLogger = new TestLogger();
    var oldAudio = new TestAudioPlayer();
    var oldTtsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseOldTts = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
    var (oldHttpService, oldHttp) = TestHttpServiceProxy.Create();
    oldHttp.GetAsyncHandler = (url, _, _) => Task.FromResult(
        url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)
            ? "{\"credit\":\"1.00\"}"
            : "{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
    oldHttp.PostAsBytesAsyncHandler = (_, _, _, _) =>
    {
        oldTtsStarted.TrySetResult();
        return releaseOldTts.Task;
    };
    var oldContext = CreateContext(
        settings: oldSettings,
        httpService: oldHttpService,
        audioPlayer: oldAudio,
        logger: oldLogger);
    var plugin = new Main();
    plugin.Init(oldContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    plugin.GetOrCreateSettingsViewModel();

    var oldTtsTask = plugin.PlayAudioAsync("snapshot test");
    await oldTtsStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    var newSettings = CreateTtsSettings("s2-pro");
    newSettings.ApiKey = DraftKey;
    var newLogger = new TestLogger();
    var newAudio = new TestAudioPlayer();
    var newCreditStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseNewStartupCredit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var newCreditRequestCount = 0;
    var (newHttpService, newHttp) = TestHttpServiceProxy.Create();
    newHttp.GetAsyncHandler = (url, _, _) =>
    {
        if (!url.Contains("/wallet/self/api-credit", StringComparison.Ordinal))
            return Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");

        newCreditRequestCount++;
        if (newCreditRequestCount == 1)
        {
            newCreditStarted.TrySetResult();
            return releaseNewStartupCredit.Task;
        }

        return Task.FromResult("{\"credit\":\"999.00\"}");
    };
    var newContext = CreateContext(
        settings: newSettings,
        httpService: newHttpService,
        audioPlayer: newAudio,
        logger: newLogger);

    plugin.Init(newContext, FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
    var newStartupTask = plugin.PendingStartupTask;
    var newViewModel = plugin.GetOrCreateSettingsViewModel();
    await newCreditStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    AssertEqual(true, newViewModel.IsApiKeyInputLocked, "The second initialization startup credit should lock its new ViewModel");

    releaseOldTts.SetResult(new byte[] { 4, 5, 6 });
    await oldTtsTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(1, oldAudio.PlayBytesCallCount, "An old TTS operation should keep using its original audio player after reinitialization");
    AssertEqual(0, newAudio.PlayBytesCallCount, "An old TTS operation should not play through the new Context");
    AssertEqual(true, newViewModel.IsApiKeyInputLocked, "Old TTS completion should not decrement the new ViewModel API Key lock");
    AssertEqual(1, newCreditRequestCount, "Old TTS completion should not start a silent credit refresh on the new initialization");
    AssertEqual(1, oldHttp.GetUrls.Count(url => url.Contains("/wallet/self/api-credit", StringComparison.Ordinal)), "A disposed old ViewModel should ignore post-TTS silent refresh");
    AssertEqual(false, oldLogger.Contains(AppliedKey), "Old TTS logs should not contain the old API Key");
    AssertEqual(false, newLogger.Contains(DraftKey), "New startup logs should not contain the new API Key");

    releaseNewStartupCredit.SetResult("{\"credit\":\"2.00\"}");
    await newStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
    await WaitUntilAsync(() => !newViewModel.IsApiKeyInputLocked, "new startup credit lock to release");

    AssertEqual(false, newViewModel.IsApiKeyInputLocked, "Only the new startup credit completion should unlock the new ViewModel");
}

static void PreviewAudioRejectsInvalidSearchUrlWithoutStartingPlayback()
{
    using var network = OverrideNetworkAvailability(true);
    var logger = new TestLogger();
    var player = new TestPreviewAudioPlayer();
    var viewModel = new SettingsViewModel(
        CreateContext(logger: logger),
        new Settings(),
        clearCoverImageCacheAsync: null,
        clearCoverImageCacheTimeout: null,
        previewAudioPlayerFactory: () => player);

    viewModel.ToggleSearchItemPreviewCommand.Execute(new VoiceSearchItem
    {
        Id = "0123456789abcdef0123456789abcdef",
        SampleAudioUrl = "file:///C:/Windows/win.ini",
    });

    AssertEqual(null, viewModel.PreviewingVoiceId, "Invalid preview URL should leave preview state stopped");
    AssertEqual(0, player.PlayCallCount, "Invalid preview URL should not start the player abstraction");
    AssertEqual(true, logger.Contains(LogLevel.Warning, "Rejected preview audio URL"), "Invalid preview URL should be logged as a recoverable warning");
}

static async Task DisplayPreviewRefreshesCachedVoiceAndPlaysLatestUrlAsync()
{
    using var network = OverrideNetworkAvailability(true);
    const string voiceId = "0123456789abcdef0123456789abcdef";
    const string oldUrl = "https://platform.r2.fish.audio/audio/old.mp3";
    const string freshUrl = "https://bucket.r2.cloudflarestorage.com/audio/fresh.mp3";
    var settings = new Settings
    {
        VoiceId = voiceId,
        CachedVoice = new CachedVoiceInfo
        {
            Title = "Old Voice",
            Description = "Old description",
            CoverImage = "old-cover",
            AuthorName = "Old Author",
            TaskCount = 1,
            SampleAudioUrl = oldUrl,
        },
    };
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetResponseJson = $$"""
        {"_id":"{{voiceId}}","title":"Fresh Voice","description":"Fresh description","cover_image":"fresh-cover","samples":[{"title":"Sample","text":"Hello","audio":"{{freshUrl}}"}],"task_count":42,"author":{"_id":"author-id","nickname":"Fresh Author","avatar":"avatar"},"tags":[],"languages":[],"like_count":7}
        """;
    var player = new TestPreviewAudioPlayer();
    var context = CreateContext(settings: settings, httpService: httpService);
    var contextProxy = (ContextProxy)(object)context;
    var viewModel = new SettingsViewModel(
        context,
        settings,
        clearCoverImageCacheAsync: null,
        clearCoverImageCacheTimeout: null,
        previewAudioPlayerFactory: () => player);

    await viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(1, http.GetCallCount, "Selected voice preview should refresh model details once");
    AssertEqual($"https://api.fish.audio/model/{voiceId}", http.LastGetUrl, "Selected voice preview should refresh the captured voice ID");
    AssertEqual("Bearer dummy", AssertHeaders(http.LastGetOptions, "Selected voice preview should send headers")["Authorization"], "Selected voice preview should use the dummy token");
    AssertEqual(TimeSpan.FromSeconds(15), http.LastGetOptions?.Timeout, "Selected voice preview refresh should use the model lookup timeout");
    AssertEqual("Fresh Voice", settings.CachedVoice?.Title, "Successful preview refresh should replace cached title");
    AssertEqual("Fresh description", settings.CachedVoice?.Description, "Successful preview refresh should replace cached description");
    AssertEqual("fresh-cover", settings.CachedVoice?.CoverImage, "Successful preview refresh should replace cached cover");
    AssertEqual("Fresh Author", settings.CachedVoice?.AuthorName, "Successful preview refresh should replace cached author");
    AssertEqual(42, settings.CachedVoice?.TaskCount, "Successful preview refresh should replace cached task count");
    AssertEqual(freshUrl, settings.CachedVoice?.SampleAudioUrl, "Successful preview refresh should replace cached sample URL");
    AssertEqual(1, contextProxy.SaveCount, "Successful preview refresh should save cached voice immediately");
    AssertEqual("Fresh Voice", viewModel.CachedVoiceTitle, "Successful preview refresh should update visible title");
    AssertEqual("Fresh description", viewModel.CachedVoiceDescription, "Successful preview refresh should update visible description");
    AssertEqual("Fresh Author", viewModel.CachedVoiceAuthor, "Successful preview refresh should update visible author");
    AssertEqual(42, viewModel.CachedVoiceTaskCount, "Successful preview refresh should update visible task count");
    AssertEqual(freshUrl, viewModel.CachedVoiceSampleUrl, "Successful preview refresh should update visible sample URL");
    AssertEqual(freshUrl, player.LastOpenedUri?.AbsoluteUri, "Successful preview refresh should play the latest sample URL");
    AssertEqual(1, player.PlayCallCount, "Successful preview refresh should start playback once");
    AssertEqual(voiceId, viewModel.PreviewingVoiceId, "Successful preview refresh should expose selected voice playback state");
}

static async Task DisplayPreviewOfflinePreflightStopsBeforeRefreshAsync()
{
    using var network = OverrideNetworkAvailability(false);
    const string voiceId = "0123456789abcdef0123456789abcdef";
    var settings = new Settings
    {
        VoiceId = voiceId,
        CachedVoice = new CachedVoiceInfo
        {
            Title = "Offline Voice",
            SampleAudioUrl = "https://platform.r2.fish.audio/audio/offline.mp3",
        },
    };
    var snackbar = new TestSnackbar();
    var (httpService, http) = TestHttpServiceProxy.Create();
    var player = new TestPreviewAudioPlayer();
    var viewModel = new SettingsViewModel(
        CreateContext(snackbar, settings, httpService),
        settings,
        clearCoverImageCacheAsync: null,
        clearCoverImageCacheTimeout: null,
        previewAudioPlayerFactory: () => player);

    await viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(FishAudioRuntime.NetworkUnavailableKey, snackbar.LastError, "Offline selected preview should show the existing network unavailable message");
    AssertEqual(0, http.GetCallCount, "Offline selected preview should not call model details");
    AssertEqual(0, player.PlayCallCount, "Offline selected preview should not start playback");
    AssertEqual(null, viewModel.PreviewingVoiceId, "Offline selected preview should remain stopped");
}

static async Task DisplayPreviewRechecksNetworkBeforePlaybackAsync()
{
    using var network = OverrideNetworkAvailability(true);
    const string voiceId = "0123456789abcdef0123456789abcdef";
    const string oldUrl = "https://platform.r2.fish.audio/audio/before-refresh.mp3";
    const string freshUrl = "https://platform.r2.fish.audio/audio/after-refresh.mp3";
    var settings = new Settings
    {
        VoiceId = voiceId,
        CachedVoice = new CachedVoiceInfo { Title = "Before Refresh", SampleAudioUrl = oldUrl },
    };
    var snackbar = new TestSnackbar();
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetResponseJson = $$"""
        {"_id":"{{voiceId}}","title":"After Refresh","description":"","cover_image":"","samples":[{"title":"","text":"","audio":"{{freshUrl}}"}],"task_count":0,"author":null,"tags":[],"languages":[],"like_count":0}
        """;
    var player = new TestPreviewAudioPlayer();
    var context = CreateContext(snackbar, settings, httpService);
    var contextProxy = (ContextProxy)(object)context;
    contextProxy.OnSave = () => network.Set(false);
    var viewModel = new SettingsViewModel(
        context,
        settings,
        clearCoverImageCacheAsync: null,
        clearCoverImageCacheTimeout: null,
        previewAudioPlayerFactory: () => player);

    await viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual("After Refresh", settings.CachedVoice?.Title, "Network loss before playback should not discard refreshed metadata");
    AssertEqual(freshUrl, viewModel.CachedVoiceSampleUrl, "Network loss before playback should still synchronize visible refreshed metadata");
    AssertEqual(1, contextProxy.SaveCount, "Network loss before playback should still save refreshed metadata");
    AssertEqual(0, player.PlayCallCount, "Network loss after refresh should stop before playback");
    AssertEqual(FishAudioRuntime.NetworkUnavailableKey, snackbar.LastError, "Network loss after refresh should show the existing network unavailable message");
}

static void SearchPreviewOfflinePreflightStopsBeforePlayback()
{
    using var network = OverrideNetworkAvailability(false);
    var snackbar = new TestSnackbar();
    var (httpService, http) = TestHttpServiceProxy.Create();
    var player = new TestPreviewAudioPlayer();
    var viewModel = new SettingsViewModel(
        CreateContext(snackbar, httpService: httpService),
        new Settings(),
        clearCoverImageCacheAsync: null,
        clearCoverImageCacheTimeout: null,
        previewAudioPlayerFactory: () => player);
    var item = new VoiceSearchItem
    {
        Id = "fedcba9876543210fedcba9876543210",
        SampleAudioUrl = "https://platform.r2.fish.audio/audio/search.mp3",
    };

    viewModel.ToggleSearchItemPreviewCommand.Execute(item);

    AssertEqual(FishAudioRuntime.NetworkUnavailableKey, snackbar.LastError, "Offline search preview should show the existing network unavailable message");
    AssertEqual(0, http.GetCallCount, "Search preview should never call model details");
    AssertEqual(0, player.PlayCallCount, "Offline search preview should not start playback");
    AssertEqual(null, viewModel.PreviewingVoiceId, "Offline search preview should remain stopped");
}

static void SearchPreviewUsesListUrlWithoutDetailRefresh()
{
    using var network = OverrideNetworkAvailability(true);
    const string listUrl = "https://platform.r2.fish.audio/audio/list-result.mp3";
    var (httpService, http) = TestHttpServiceProxy.Create();
    var player = new TestPreviewAudioPlayer();
    var viewModel = new SettingsViewModel(
        CreateContext(httpService: httpService),
        new Settings(),
        clearCoverImageCacheAsync: null,
        clearCoverImageCacheTimeout: null,
        previewAudioPlayerFactory: () => player);
    var item = new VoiceSearchItem
    {
        Id = "fedcba9876543210fedcba9876543210",
        SampleAudioUrl = listUrl,
    };

    viewModel.ToggleSearchItemPreviewCommand.Execute(item);

    AssertEqual(0, http.GetCallCount, "Search result preview should not call model details");
    AssertEqual(listUrl, player.LastOpenedUri?.AbsoluteUri, "Search result preview should play its list response URL directly");
    AssertEqual(1, player.PlayCallCount, "Online search result preview should start playback once");
}

static void SearchPreviewSecondClickStopsWithoutNetwork()
{
    using var network = OverrideNetworkAvailability(true);
    var snackbar = new TestSnackbar();
    var player = new TestPreviewAudioPlayer();
    var viewModel = new SettingsViewModel(
        CreateContext(snackbar),
        new Settings(),
        clearCoverImageCacheAsync: null,
        clearCoverImageCacheTimeout: null,
        previewAudioPlayerFactory: () => player);
    var item = new VoiceSearchItem
    {
        Id = "fedcba9876543210fedcba9876543210",
        SampleAudioUrl = "https://platform.r2.fish.audio/audio/search-stop.mp3",
    };

    viewModel.ToggleSearchItemPreviewCommand.Execute(item);
    network.Set(false);
    viewModel.ToggleSearchItemPreviewCommand.Execute(item);

    AssertEqual(1, player.StopCallCount, "Second search preview click should stop the active player immediately");
    AssertEqual(null, viewModel.PreviewingVoiceId, "Second search preview click should clear playback state");
    AssertEqual(null, snackbar.LastError, "Stopping search preview while offline should not show a network error");
}

static async Task DisplayPreviewRefreshFailureSilentlyFallsBackAsync()
{
    using var network = OverrideNetworkAvailability(true);
    const string voiceId = "0123456789abcdef0123456789abcdef";
    const string oldUrl = "https://platform.r2.fish.audio/audio/fallback.mp3";
    var originalCache = new CachedVoiceInfo
    {
        Title = "Keep Voice",
        Description = "Keep description",
        CoverImage = "keep-cover",
        AuthorName = "Keep Author",
        TaskCount = 9,
        SampleAudioUrl = oldUrl,
    };
    var settings = new Settings { VoiceId = voiceId, CachedVoice = originalCache };
    var snackbar = new TestSnackbar();
    var logger = new TestLogger();
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetException = new TimeoutException("model lookup timed out");
    var player = new TestPreviewAudioPlayer();
    var context = CreateContext(snackbar, settings, httpService, logger: logger);
    var contextProxy = (ContextProxy)(object)context;
    var viewModel = new SettingsViewModel(
        context,
        settings,
        clearCoverImageCacheAsync: null,
        clearCoverImageCacheTimeout: null,
        previewAudioPlayerFactory: () => player);

    await viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(null, snackbar.LastError, "Selected preview refresh failure should not show a refresh error");
    AssertEqual(true, ReferenceEquals(originalCache, settings.CachedVoice), "Selected preview refresh failure should preserve the old cache object");
    AssertEqual(0, contextProxy.SaveCount, "Selected preview refresh failure should not save settings");
    AssertEqual(oldUrl, player.LastOpenedUri?.AbsoluteUri, "Selected preview refresh failure should silently fall back to the captured old URL");
    AssertEqual(1, player.PlayCallCount, "Selected preview refresh failure should start fallback playback once");
    AssertEqual(true, logger.Contains(LogLevel.Warning, "Selected voice preview refresh failed"), "Selected preview refresh failure should be logged safely");
}

static async Task DisplayPreviewNotFoundSilentlyFallsBackAsync()
{
    using var network = OverrideNetworkAvailability(true);
    const string voiceId = "0123456789abcdef0123456789abcdef";
    const string oldUrl = "https://platform.r2.fish.audio/audio/not-found-fallback.mp3";
    var originalCache = new CachedVoiceInfo
    {
        Title = "Keep Missing Voice",
        SampleAudioUrl = oldUrl,
    };
    var settings = new Settings { VoiceId = voiceId, CachedVoice = originalCache };
    var snackbar = new TestSnackbar();
    var logger = new TestLogger();
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetException = new HttpRequestException(
        "Response status code does not indicate success: 404 (Not Found).",
        null,
        HttpStatusCode.NotFound);
    var player = new TestPreviewAudioPlayer();
    var context = CreateContext(snackbar, settings, httpService, logger: logger);
    var contextProxy = (ContextProxy)(object)context;
    var viewModel = new SettingsViewModel(
        context,
        settings,
        clearCoverImageCacheAsync: null,
        clearCoverImageCacheTimeout: null,
        previewAudioPlayerFactory: () => player);

    await viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(1, http.GetCallCount, "Selected preview should make one detail request before handling 404");
    AssertEqual(null, snackbar.LastError, "Selected preview 404 should not show a refresh error");
    AssertEqual(true, ReferenceEquals(originalCache, settings.CachedVoice), "Selected preview 404 should preserve the old cache object");
    AssertEqual(0, contextProxy.SaveCount, "Selected preview 404 should not save settings");
    AssertEqual(oldUrl, player.LastOpenedUri?.AbsoluteUri, "Selected preview 404 should silently fall back to the captured old URL");
    AssertEqual(1, player.PlayCallCount, "Selected preview 404 should start fallback playback once");
    AssertEqual(true, logger.Contains(LogLevel.Warning, "returned no voice"), "Selected preview 404 should be logged safely");
}

static async Task DisplayPreviewLatestVoiceWithoutSampleShowsUnavailableAsync()
{
    using var network = OverrideNetworkAvailability(true);
    const string voiceId = "0123456789abcdef0123456789abcdef";
    const string oldUrl = "https://platform.r2.fish.audio/audio/obsolete.mp3";
    var settings = new Settings
    {
        VoiceId = voiceId,
        CachedVoice = new CachedVoiceInfo { Title = "Old Voice", SampleAudioUrl = oldUrl },
    };
    var snackbar = new TestSnackbar();
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetResponseJson = $$"""
        {"_id":"{{voiceId}}","title":"Latest Voice","description":"Latest description","cover_image":"latest-cover","samples":[],"task_count":77,"author":{"_id":"author-id","nickname":"Latest Author","avatar":"avatar"},"tags":[],"languages":[],"like_count":3}
        """;
    var player = new TestPreviewAudioPlayer();
    var context = CreateContext(snackbar, settings, httpService);
    var contextProxy = (ContextProxy)(object)context;
    var viewModel = new SettingsViewModel(
        context,
        settings,
        clearCoverImageCacheAsync: null,
        clearCoverImageCacheTimeout: null,
        previewAudioPlayerFactory: () => player);

    await viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual("Latest Voice", settings.CachedVoice?.Title, "Latest details without a sample should still replace cached metadata");
    AssertEqual("Latest description", settings.CachedVoice?.Description, "Latest details without a sample should preserve the latest description");
    AssertEqual("latest-cover", settings.CachedVoice?.CoverImage, "Latest details without a sample should preserve the latest cover");
    AssertEqual("Latest Author", settings.CachedVoice?.AuthorName, "Latest details without a sample should preserve the latest author");
    AssertEqual(77, settings.CachedVoice?.TaskCount, "Latest details without a sample should preserve the latest task count");
    AssertEqual(null, settings.CachedVoice?.SampleAudioUrl, "Latest details without a sample should replace the obsolete sample URL");
    AssertEqual(1, contextProxy.SaveCount, "Latest details without a sample should be saved immediately");
    AssertEqual("Latest Voice", viewModel.CachedVoiceTitle, "Latest details without a sample should update visible metadata");
    AssertEqual(null, viewModel.CachedVoiceSampleUrl, "Latest details without a sample should remove the visible obsolete sample URL");
    AssertEqual(0, player.PlayCallCount, "Latest details without a sample should not fall back to obsolete playback");
    AssertEqual("STranslate_Plugin_Tts_FishAudio_Preview_Unavailable", snackbar.LastError, "Latest details without a sample should show the localized unavailable message");
}

static async Task DisplayPreviewSecondClickStopsWithoutNetworkAsync()
{
    using var network = OverrideNetworkAvailability(true);
    const string voiceId = "0123456789abcdef0123456789abcdef";
    const string freshUrl = "https://platform.r2.fish.audio/audio/repeat.mp3";
    var settings = new Settings
    {
        VoiceId = voiceId,
        CachedVoice = new CachedVoiceInfo { Title = "Repeat Voice", SampleAudioUrl = freshUrl },
    };
    var snackbar = new TestSnackbar();
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetResponseJson = $$"""
        {"_id":"{{voiceId}}","title":"Repeat Voice","description":"","cover_image":"","samples":[{"title":"","text":"","audio":"{{freshUrl}}"}],"task_count":0,"author":null,"tags":[],"languages":[],"like_count":0}
        """;
    var player = new TestPreviewAudioPlayer();
    var viewModel = new SettingsViewModel(
        CreateContext(snackbar, settings, httpService),
        settings,
        clearCoverImageCacheAsync: null,
        clearCoverImageCacheTimeout: null,
        previewAudioPlayerFactory: () => player);

    await viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));
    AssertEqual(voiceId, viewModel.PreviewingVoiceId, "First selected preview click should start playback");
    AssertEqual(1, http.GetCallCount, "First selected preview click should refresh details once");

    network.Set(false);
    await viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(1, http.GetCallCount, "Stopping selected preview should not make another detail request");
    AssertEqual(1, player.StopCallCount, "Second selected preview click should stop the active player immediately");
    AssertEqual(null, viewModel.PreviewingVoiceId, "Second selected preview click should clear playback state");
    AssertEqual(null, snackbar.LastError, "Stopping selected preview while offline should not show a network error");
}

static async Task DisplayPreviewSecondClickCancelsPendingRefreshWithoutRestartAsync()
{
    using var network = OverrideNetworkAvailability(true);
    const string voiceId = "0123456789abcdef0123456789abcdef";
    const string oldUrl = "https://platform.r2.fish.audio/audio/pending-old.mp3";
    const string lateUrl = "https://platform.r2.fish.audio/audio/pending-late.mp3";
    var originalCache = new CachedVoiceInfo { Title = "Pending Original", SampleAudioUrl = oldUrl };
    var settings = new Settings { VoiceId = voiceId, CachedVoice = originalCache };
    var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var requestCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (_, _, ct) =>
    {
        requestStarted.TrySetResult();
        ct.Register(() => requestCanceled.TrySetResult());
        return releaseResponse.Task;
    };
    var player = new TestPreviewAudioPlayer();
    var context = CreateContext(settings: settings, httpService: httpService);
    var contextProxy = (ContextProxy)(object)context;
    var viewModel = new SettingsViewModel(
        context,
        settings,
        clearCoverImageCacheAsync: null,
        clearCoverImageCacheTimeout: null,
        previewAudioPlayerFactory: () => player);

    viewModel.ToggleDisplayPreviewCommand.Execute(null);
    var firstPreviewTask = viewModel.ToggleDisplayPreviewCommand.ExecutionTask
        ?? throw new InvalidOperationException("First selected preview command should expose its execution task");
    await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    viewModel.ToggleDisplayPreviewCommand.Execute(null);
    var canceledBeforeRelease = requestCanceled.Task.IsCompleted;
    var getCallsBeforeRelease = http.GetCallCount;
    var savesBeforeRelease = contextProxy.SaveCount;
    var playsBeforeRelease = player.PlayCallCount;

    releaseResponse.SetResult($$"""
        {"_id":"{{voiceId}}","title":"Pending Late","description":"Late","cover_image":"late-cover","samples":[{"title":"","text":"","audio":"{{lateUrl}}"}],"task_count":99,"author":null,"tags":[],"languages":[],"like_count":0}
        """);
    await firstPreviewTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(true, canceledBeforeRelease, "Second selected preview click should cancel the pending request before its response is released");
    AssertEqual(1, getCallsBeforeRelease, "Second selected preview click should not start another model detail request");
    AssertEqual(0, savesBeforeRelease, "Second selected preview click should not save settings before the late response");
    AssertEqual(0, playsBeforeRelease, "Second selected preview click should not start playback before the late response");
    AssertEqual(true, ReferenceEquals(originalCache, settings.CachedVoice), "Late canceled response should not replace cached voice metadata");
    AssertEqual(0, contextProxy.SaveCount, "Late canceled response should not save settings");
    AssertEqual(0, player.PlayCallCount, "Late canceled response should not start playback");
    AssertEqual(null, viewModel.PreviewingVoiceId, "Canceling a pending selected preview should leave playback stopped");
}

static void PreviewPlaybackFailureUsesNetworkAwareMessage()
{
    using var network = OverrideNetworkAvailability(true);
    var snackbar = new TestSnackbar();
    var player = new TestPreviewAudioPlayer();
    var viewModel = new SettingsViewModel(
        CreateContext(snackbar),
        new Settings(),
        clearCoverImageCacheAsync: null,
        clearCoverImageCacheTimeout: null,
        previewAudioPlayerFactory: () => player);
    var item = new VoiceSearchItem
    {
        Id = "fedcba9876543210fedcba9876543210",
        SampleAudioUrl = "https://platform.r2.fish.audio/audio/failure.mp3",
    };

    viewModel.ToggleSearchItemPreviewCommand.Execute(item);
    player.RaiseFailed(new InvalidOperationException("media load failed"));

    AssertEqual("STranslate_Plugin_Tts_FishAudio_Preview_PlaybackFailed", snackbar.LastError, "Online media failure should show the localized playback failure message");
    AssertEqual(null, viewModel.PreviewingVoiceId, "Media failure should stop preview playback");

    snackbar.Clear();
    viewModel.ToggleSearchItemPreviewCommand.Execute(item);
    network.Set(false);
    player.RaiseFailed(new TimeoutException("media load timed out"));

    AssertEqual(FishAudioRuntime.NetworkUnavailableKey, snackbar.LastError, "Media failure after network loss should show the existing network unavailable message");
    AssertEqual(null, viewModel.PreviewingVoiceId, "Media failure after network loss should stop preview playback");
}

static void PreviewFailureLanguageResourcesAreComplete()
{
    var keys = new[]
    {
        "STranslate_Plugin_Tts_FishAudio_Preview_Unavailable",
        "STranslate_Plugin_Tts_FishAudio_Preview_PlaybackFailed",
    };

    foreach (var locale in new[] { "zh-cn", "zh-tw", "en", "ja", "ko" })
    {
        var xaml = File.ReadAllText(FindRepoFile(Path.Combine("STranslate.Plugin.Tts.FishAudio", "Languages", $"{locale}.xaml")));
        foreach (var key in keys)
        {
            var match = Regex.Match(
                xaml,
                $"<sys:String\\s+x:Key=\"{Regex.Escape(key)}\">(?<value>[^<]+)</sys:String>",
                RegexOptions.CultureInvariant);
            AssertEqual(true, match.Success, $"{locale}.xaml should define {key}");
            AssertEqual(false, string.IsNullOrWhiteSpace(match.Groups["value"].Value), $"{locale}.xaml should provide non-empty text for {key}");
        }
    }
}

static async Task SearchPreviewInvalidatesPendingDisplayRefreshAsync()
{
    using var network = OverrideNetworkAvailability(true);
    const string selectedVoiceId = "11111111111111111111111111111111";
    const string searchVoiceId = "22222222222222222222222222222222";
    const string selectedOldUrl = "https://platform.r2.fish.audio/audio/selected-old.mp3";
    const string selectedLateUrl = "https://platform.r2.fish.audio/audio/selected-late.mp3";
    const string searchUrl = "https://platform.r2.fish.audio/audio/search-current.mp3";
    var originalCache = new CachedVoiceInfo { Title = "Selected Original", SampleAudioUrl = selectedOldUrl };
    var settings = new Settings { VoiceId = selectedVoiceId, CachedVoice = originalCache };
    var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var requestCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (_, _, ct) =>
    {
        requestStarted.TrySetResult();
        ct.Register(() => requestCanceled.TrySetResult());
        return releaseResponse.Task;
    };
    var player = new TestPreviewAudioPlayer();
    var context = CreateContext(settings: settings, httpService: httpService);
    var contextProxy = (ContextProxy)(object)context;
    var viewModel = new SettingsViewModel(
        context,
        settings,
        clearCoverImageCacheAsync: null,
        clearCoverImageCacheTimeout: null,
        previewAudioPlayerFactory: () => player);

    var selectedPreviewTask = viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null);
    await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    viewModel.ToggleSearchItemPreviewCommand.Execute(new VoiceSearchItem
    {
        Id = searchVoiceId,
        SampleAudioUrl = searchUrl,
    });
    releaseResponse.SetResult($$"""
        {"_id":"{{selectedVoiceId}}","title":"Selected Late","description":"Late","cover_image":"late-cover","samples":[{"title":"","text":"","audio":"{{selectedLateUrl}}"}],"task_count":99,"author":null,"tags":[],"languages":[],"like_count":0}
        """);
    await selectedPreviewTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(searchVoiceId, viewModel.PreviewingVoiceId, "Search preview should remain active after the older selected refresh completes");
    AssertEqual(searchUrl, player.LastOpenedUri?.AbsoluteUri, "Late selected refresh should not replace the search preview URL");
    AssertEqual(1, player.PlayCallCount, "Late selected refresh should not start selected voice playback");
    AssertEqual(true, ReferenceEquals(originalCache, settings.CachedVoice), "Late selected refresh should not replace cached voice metadata after search preview starts");
    AssertEqual(0, contextProxy.SaveCount, "Late selected refresh should not save cached voice metadata after search preview starts");
    await requestCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
}

static async Task DisplayPreviewVoiceSwitchIgnoresLateRefreshAsync()
{
    using var network = OverrideNetworkAvailability(true);
    const string oldVoiceId = "11111111111111111111111111111111";
    const string newVoiceId = "22222222222222222222222222222222";
    const string oldUrl = "https://platform.r2.fish.audio/audio/old-selected.mp3";
    const string newUrl = "https://platform.r2.fish.audio/audio/new-selected.mp3";
    const string lateUrl = "https://platform.r2.fish.audio/audio/late-old.mp3";
    var settings = new Settings
    {
        VoiceId = oldVoiceId,
        CachedVoice = new CachedVoiceInfo { Title = "Old Voice", SampleAudioUrl = oldUrl },
    };
    var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var requestCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (_, _, ct) =>
    {
        requestStarted.TrySetResult();
        ct.Register(() => requestCanceled.TrySetResult());
        return releaseResponse.Task;
    };
    var player = new TestPreviewAudioPlayer();
    var context = CreateContext(settings: settings, httpService: httpService);
    var contextProxy = (ContextProxy)(object)context;
    var viewModel = new SettingsViewModel(
        context,
        settings,
        clearCoverImageCacheAsync: null,
        clearCoverImageCacheTimeout: null,
        previewAudioPlayerFactory: () => player);

    var previewTask = viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null);
    await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    viewModel.SelectVoiceCommand.Execute(new VoiceSearchItem
    {
        Id = newVoiceId,
        Title = "New Voice",
        Description = "New description",
        AuthorName = "New Author",
        TaskCount = 5,
        SampleAudioUrl = newUrl,
    });
    var saveCountAfterSwitch = contextProxy.SaveCount;

    releaseResponse.SetResult($$"""
        {"_id":"{{oldVoiceId}}","title":"Late Old Voice","description":"Late description","cover_image":"late-cover","samples":[{"title":"","text":"","audio":"{{lateUrl}}"}],"task_count":99,"author":{"_id":"","nickname":"Late Author","avatar":""},"tags":[],"languages":[],"like_count":0}
        """);
    await previewTask.WaitAsync(TimeSpan.FromSeconds(2));

    await requestCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    AssertEqual(newVoiceId, settings.VoiceId, "Voice switch should keep the new selected voice ID after a late preview refresh");
    AssertEqual("New Voice", settings.CachedVoice?.Title, "Voice switch should keep the new cached voice after a late preview refresh");
    AssertEqual(newUrl, settings.CachedVoice?.SampleAudioUrl, "Voice switch should keep the new sample URL after a late preview refresh");
    AssertEqual(saveCountAfterSwitch, contextProxy.SaveCount, "Late preview refresh should not save settings after the selected voice changes");
    AssertEqual(0, player.PlayCallCount, "Late preview refresh should not start playback after the selected voice changes");
}

static async Task DisplayPreviewDisposeCancelsAndIgnoresLateRefreshAsync()
{
    using var network = OverrideNetworkAvailability(true);
    const string voiceId = "11111111111111111111111111111111";
    const string oldUrl = "https://platform.r2.fish.audio/audio/dispose-old.mp3";
    const string lateUrl = "https://platform.r2.fish.audio/audio/dispose-late.mp3";
    var originalCache = new CachedVoiceInfo { Title = "Keep After Dispose", SampleAudioUrl = oldUrl };
    var settings = new Settings { VoiceId = voiceId, CachedVoice = originalCache };
    var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var requestCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var (httpService, http) = TestHttpServiceProxy.Create();
    http.GetAsyncHandler = (_, _, ct) =>
    {
        requestStarted.TrySetResult();
        ct.Register(() => requestCanceled.TrySetResult());
        return releaseResponse.Task;
    };
    var player = new TestPreviewAudioPlayer();
    var context = CreateContext(settings: settings, httpService: httpService);
    var contextProxy = (ContextProxy)(object)context;
    var viewModel = new SettingsViewModel(
        context,
        settings,
        clearCoverImageCacheAsync: null,
        clearCoverImageCacheTimeout: null,
        previewAudioPlayerFactory: () => player);

    var previewTask = viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null);
    await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    viewModel.Dispose();
    var saveCountAfterDispose = contextProxy.SaveCount;
    await requestCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));

    releaseResponse.SetResult($$"""
        {"_id":"{{voiceId}}","title":"Late After Dispose","description":"","cover_image":"","samples":[{"title":"","text":"","audio":"{{lateUrl}}"}],"task_count":0,"author":null,"tags":[],"languages":[],"like_count":0}
        """);
    await previewTask.WaitAsync(TimeSpan.FromSeconds(2));

    AssertEqual(true, ReferenceEquals(originalCache, settings.CachedVoice), "Disposed preview refresh should preserve the original cached voice");
    AssertEqual(saveCountAfterDispose, contextProxy.SaveCount, "Late preview refresh should not save settings after Dispose");
    AssertEqual(0, player.PlayCallCount, "Late preview refresh should not start playback after Dispose");
}

static void PromoStatePersistsDismissalAndUse()
{
    var settings = new Settings();
    var context = CreateContext(settings: settings);
    var proxy = (ContextProxy)(object)context;
    var viewModel = new SettingsViewModel(context, settings, nowUtc: FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));

    AssertEqual(true, viewModel.ShowS21ProFreePromo, "Promo should be visible before cutoff when not dismissed");

    viewModel.DismissS21ProFreePromoCommand.Execute(null);
    AssertEqual(true, settings.IsS21ProFreePromoDismissed, "Dismiss promo should persist dismissal");
    AssertEqual(false, viewModel.ShowS21ProFreePromo, "Dismiss promo should hide promo card");

    settings = new Settings { SelectedModel = FishAudioRuntime.S1Model };
    context = CreateContext(settings: settings);
    proxy = (ContextProxy)(object)context;
    viewModel = new SettingsViewModel(context, settings, nowUtc: FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));

    viewModel.UseS21ProFreePromoCommand.Execute(null);
    AssertEqual(FishAudioRuntime.S21ProFreeModel, settings.SelectedModel, "Using promo should select free model");
    AssertEqual(false, settings.IsS21ProFreePromoDismissed, "Using promo should not persist dismissal");
    AssertEqual(true, viewModel.ShowS21ProFreePromo, "Using promo should leave promo card visible");
    AssertEqual(1, proxy.SaveCount, "Using promo should save the selected model once");

    settings = new Settings();
    viewModel = new SettingsViewModel(CreateContext(settings: settings), settings, nowUtc: FishAudioRuntime.FreeModelCutoffUtc);
    AssertEqual(false, viewModel.ShowS21ProFreePromo, "Promo should be hidden at and after cutoff");

    settings = new Settings { SelectedModel = FishAudioRuntime.S1Model };
    viewModel = new SettingsViewModel(CreateContext(settings: settings), settings, nowUtc: FishAudioRuntime.FreeModelCutoffUtc.AddDays(-1));
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
            return Task.FromResult<Stream>(new MemoryStream(new byte[] { 9 }));
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
        var bytes = MinimalJpegBytes();
        var downloadCount = 0;
        string? callbackUrl = null;
        var cache = new CoverImageCacheService(root, (_, _) =>
        {
            downloadCount++;
            return Task.FromResult<Stream>(new MemoryStream(bytes));
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

async Task CoverImageCacheRejectsInvalidDownloadsAsync()
{
    await AssertRejectedCoverDownloadAsync(
        "55555555555555555555555555555555",
        (_, _) => Task.FromResult<Stream>(new MemoryStream(Array.Empty<byte>())),
        "Empty cover image response should not be cached");
    await AssertRejectedCoverDownloadAsync(
        "66666666666666666666666666666666",
        (_, _) => Task.FromResult<Stream>(new MemoryStream(NonImageBytes())),
        "Non-image cover response should not be cached");
    await AssertRejectedCoverDownloadAsync(
        "77777777777777777777777777777777",
        (_, _) => Task.FromResult<Stream>(new MemoryStream(OversizedImageBytes())),
        "Oversized cover image response should not be cached");
    await AssertRejectedCoverDownloadAsync(
        "88888888888888888888888888888888",
        (_, ct) => Task.FromCanceled<Stream>(ct.IsCancellationRequested ? ct : new CancellationToken(true)),
        "Canceled cover image download should not be cached");
}

async Task CoverImageCacheCancelsSlowDownloadAfterTimeoutAsync()
{
    var root = CreateTempDirectory();
    try
    {
        const string voiceId = "99999999999999999999999999999999";
        var downloadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloadCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? callbackUrl = null;
        var cache = new CoverImageCacheService(root, (_, ct) =>
        {
            downloadStarted.SetResult();
            ct.Register(() => downloadCanceled.TrySetResult());
            return Task.Delay(Timeout.InfiniteTimeSpan, ct).ContinueWith<Stream>(task =>
            {
                if (task.IsCanceled)
                    throw new OperationCanceledException(ct);

                return new MemoryStream(MinimalJpegBytes());
            }, CancellationToken.None);
        });

        var result = cache.ResolveCoverImageUrl(voiceId, "coverimage/slow", 128, url => callbackUrl = url);

        await downloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await downloadCanceled.Task.WaitAsync(TimeSpan.FromSeconds(12));
        await result.CacheTask!.WaitAsync(TimeSpan.FromSeconds(2));

        var cachedFile = Path.Combine(root, CoverImageCacheService.DirectoryName, $"{voiceId}.jpg");
        AssertEqual(false, File.Exists(cachedFile), "Timed-out cover image download should not write a cache file");
        AssertEqual(null, callbackUrl, "Timed-out cover image download should keep the UI on the remote URL fallback");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

async Task CoverImageCacheDownloadsThroughBoundedStreamAsync()
{
    var root = CreateTempDirectory();
    try
    {
        var settings = new Settings
        {
            VoiceId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            CachedVoice = new CachedVoiceInfo { Title = "Streamed", CoverImage = "coverimage/streamed" },
        };
        var oversizedStream = new LimitGuardStream((256 * 1024) + 32, CoverImageCacheService.MaxCachedImageBytes + 1);
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetStreamResponse = oversizedStream;
        var viewModel = new SettingsViewModel(
            CreateContext(settings: settings, httpService: httpService, pluginCacheDirectoryPath: root),
            settings);

        AssertEqual(
            "https://public-platform.r2.fish.audio/cdn-cgi/image/width=128,format=auto/coverimage/streamed",
            viewModel.CachedVoiceCoverUrl,
            "Missing selected voice cover cache should use remote fallback immediately");

        await http.GetStreamReturned.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await oversizedStream.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var cachedFile = Path.Combine(root, CoverImageCacheService.DirectoryName, $"{settings.VoiceId}.jpg");
        AssertEqual(1, http.GetAsStreamCallCount, "Cover image cache should download through GetAsStreamAsync");
        AssertEqual(0, http.GetAsBytesCallCount, "Cover image cache should not buffer downloads through GetAsBytesAsync");
        AssertEqual(TimeSpan.FromSeconds(10), http.LastGetStreamOptions?.Timeout, "Cover image stream request should set a 10 second timeout");
        AssertEqual(false, File.Exists(cachedFile), "Oversized streamed cover image should not write a cache file");
        AssertEqual(
            CoverImageCacheService.MaxCachedImageBytes + 1,
            oversizedStream.TotalBytesRead,
            "Oversized streamed cover image should stop reading after the cache limit plus one byte");
        AssertEqual(
            true,
            oversizedStream.ReadCallCount < 20,
            "Oversized streamed cover image should not read the full response");
        AssertEqual(
            "https://public-platform.r2.fish.audio/cdn-cgi/image/width=128,format=auto/coverimage/streamed",
            viewModel.CachedVoiceCoverUrl,
            "Rejected streamed cover image should keep remote fallback URL");
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

        var cache = new CoverImageCacheService(root, (_, _) => Task.FromResult<Stream>(new MemoryStream(new byte[] { 9 })));

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

async Task AssertRejectedCoverDownloadAsync(
    string voiceId,
    Func<string, CancellationToken, Task<Stream>> download,
    string message)
{
    var root = CreateTempDirectory();
    try
    {
        string? callbackUrl = null;
        var cache = new CoverImageCacheService(root, download);
        var result = cache.ResolveCoverImageUrl(voiceId, "coverimage/rejected", 128, url => callbackUrl = url);
        var expectedRemoteUrl = "https://public-platform.r2.fish.audio/cdn-cgi/image/width=128,format=auto/coverimage/rejected";

        AssertEqual(expectedRemoteUrl, result.DisplayUrl, $"{message}: missing cache should return the remote URL fallback immediately");
        AssertNotNull(result.CacheTask, $"{message}: missing cache should start a background download");

        await result.CacheTask!.WaitAsync(TimeSpan.FromSeconds(2));

        var cachedFile = Path.Combine(root, CoverImageCacheService.DirectoryName, $"{voiceId}.jpg");
        AssertEqual(false, File.Exists(cachedFile), $"{message}: invalid download should not write <id>.jpg");
        AssertEqual(null, callbackUrl, $"{message}: invalid download should not notify the UI with a local URL");
        AssertEqual("0 B", cache.GetFormattedCacheSize(), $"{message}: invalid download should not increase cache size");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static byte[] MinimalJpegBytes() =>
[
    0xFF, 0xD8, 0xFF, 0xE0,
    0x00, 0x10,
    0x4A, 0x46, 0x49, 0x46, 0x00,
    0x01, 0x01, 0x01,
    0x00, 0x60, 0x00, 0x60,
    0x00, 0x00,
    0xFF, 0xD9,
];

static byte[] NonImageBytes() => System.Text.Encoding.UTF8.GetBytes("not an image");

static byte[] OversizedImageBytes()
{
    var bytes = new byte[(256 * 1024) + 1];
    var jpeg = MinimalJpegBytes();
    Array.Copy(jpeg, bytes, jpeg.Length);
    return bytes;
}

void CoverImageCacheSizeScansCoverImagesDirectory()
{
    var root = CreateTempDirectory();
    try
    {
        const string voiceId = "44444444444444444444444444444444";
        var cacheDir = Path.Combine(root, CoverImageCacheService.DirectoryName);
        Directory.CreateDirectory(cacheDir);

        var cache = new CoverImageCacheService(root, (_, _) => Task.FromResult<Stream>(new MemoryStream(new byte[] { 9 })));
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
    ILogger? logger = null,
    string? pluginCacheDirectoryPath = null)
{
    var context = DispatchProxy.Create<IPluginContext, ContextProxy>();
    var proxy = (ContextProxy)(object)context;
    proxy.Snackbar = snackbar ?? new TestSnackbar();
    proxy.Settings = settings ?? new Settings();
    proxy.HttpService = httpService;
    proxy.AudioPlayer = audioPlayer;
    proxy.Logger = logger ?? new TestLogger();
    if (pluginCacheDirectoryPath is not null)
        proxy.MetaData = CreatePluginMetaData(pluginCacheDirectoryPath);
    return context;
}

static object CreatePluginMetaData(string pluginCacheDirectoryPath)
{
    var metadataType = typeof(IPluginContext).GetProperty("MetaData")!.PropertyType;
    var metadata = Activator.CreateInstance(metadataType)!;
    metadataType.GetProperty("PluginCacheDirectoryPath")!.SetValue(metadata, pluginCacheDirectoryPath);
    return metadata;
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

static void SettingsViewShowsPersistentCreditPlaceholder()
{
    var xaml = File.ReadAllText(FindRepoFile(Path.Combine("STranslate.Plugin.Tts.FishAudio", "View", "SettingsView.xaml")));
    const string placeholderKey = "STranslate_Plugin_Tts_FishAudio_Credit_NotLoaded";

    AssertEqual(
        true,
        xaml.Contains($"Text=\"{{DynamicResource {placeholderKey}}}\"", StringComparison.Ordinal),
        "The not-loaded credit state should use a persistent DynamicResource");
    AssertEqual(true, xaml.Contains("Binding=\"{Binding UserCredit}\" Value=\"\"", StringComparison.Ordinal), "The not-loaded placeholder should be driven by empty UserCredit state");
    AssertEqual(true, xaml.Contains("Text=\"{Binding UserCredit}\"", StringComparison.Ordinal), "Loaded credit should keep displaying the balance binding");
    AssertEqual(true, xaml.Contains("Text=\" $\"", StringComparison.Ordinal), "Loaded credit should keep displaying the dollar symbol");

    foreach (var locale in new[] { "zh-cn", "zh-tw", "en", "ja", "ko" })
    {
        var localeXaml = File.ReadAllText(FindRepoFile(Path.Combine(
            "STranslate.Plugin.Tts.FishAudio",
            "Languages",
            $"{locale}.xaml")));
        var match = Regex.Match(
            localeXaml,
            $"<sys:String\\s+x:Key=\"{placeholderKey}\">(?<value>[^<]+)</sys:String>",
            RegexOptions.CultureInvariant);
        AssertEqual(true, match.Success, $"{locale}.xaml should define {placeholderKey}");
        AssertEqual(false, string.IsNullOrWhiteSpace(match.Groups["value"].Value), $"{locale}.xaml should provide a complete credit placeholder");
    }
}

static async Task WaitUntilAsync(Func<bool> predicate, string description)
{
    var deadline = DateTime.UtcNow.AddSeconds(2);
    while (!predicate() && DateTime.UtcNow < deadline)
        await Task.Delay(10);

    if (!predicate())
        throw new InvalidOperationException($"Timed out waiting for {description}.");
}

static async Task RunOnStaThreadAsync(Action action)
{
    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var thread = new Thread(() =>
    {
        try
        {
            action();
            completion.SetResult();
        }
        catch (Exception ex)
        {
            completion.SetException(ex);
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
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

static void AssertPreviewAudioUrlAllowed(string url)
{
    AssertEqual(true, PreviewAudioUrlValidator.TryCreateAllowedUri(url, out var uri), $"{url} should be allowed as a preview audio URL");
    AssertEqual(url, uri?.AbsoluteUri, $"{url} should preserve the validated preview URI");
}

static void AssertPreviewAudioUrlRejected(string url)
{
    AssertEqual(false, PreviewAudioUrlValidator.TryCreateAllowedUri(url, out var uri), $"{url} should be rejected as a preview audio URL");
    AssertEqual(null, uri, $"{url} should not return a URI when rejected");
}

public class ContextProxy : DispatchProxy
{
    public object? MetaData { get; set; }
    public ISnackbar Snackbar { get; set; } = new TestSnackbar();
    public Settings Settings { get; set; } = new();
    public IHttpService? HttpService { get; set; }
    public IAudioPlayer? AudioPlayer { get; set; }
    public ILogger Logger { get; set; } = new TestLogger();
    public int SaveCount { get; private set; }
    public int LoadCount { get; private set; }
    public Action? OnSave { get; set; }
    public Action? OnLoad { get; set; }
    public Func<Settings>? LoadSettings { get; set; }
    public Action<Settings>? SaveSettings { get; set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            return null;

        if (targetMethod.Name == "get_Snackbar")
            return Snackbar;

        if (targetMethod.Name == "get_MetaData")
            return MetaData;

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
            LoadCount++;
            OnLoad?.Invoke();
            if (LoadSettings is not null)
                Settings = LoadSettings();
            return Settings;
        }

        if (targetMethod.Name == nameof(IPluginContext.SaveSettingStorage))
        {
            SaveCount++;
            OnSave?.Invoke();
            SaveSettings?.Invoke(Settings);
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

internal sealed class SharedSettingsBackingStore
{
    private readonly object _gate = new();
    private Settings _settings;

    public SharedSettingsBackingStore(Settings settings)
    {
        _settings = Clone(settings);
    }

    public Settings Load()
    {
        lock (_gate)
            return Clone(_settings);
    }

    public void Replace(Settings settings)
    {
        lock (_gate)
            _settings = Clone(settings);
    }

    public void Save(Settings settings)
    {
        lock (_gate)
            _settings = Clone(settings);
    }

    private static Settings Clone(Settings settings) => new()
    {
        ApiKey = settings.ApiKey,
        VoiceId = settings.VoiceId,
        SelectedModel = settings.SelectedModel,
        Speed = settings.Speed,
        Volume = settings.Volume,
        NormalizeLoudness = settings.NormalizeLoudness,
        Temperature = settings.Temperature,
        TopP = settings.TopP,
        Latency = settings.Latency,
        Normalize = settings.Normalize,
        Mp3Bitrate = settings.Mp3Bitrate,
        ConditionOnPreviousChunks = settings.ConditionOnPreviousChunks,
        CachedVoice = settings.CachedVoice is null
            ? null
            : new CachedVoiceInfo
            {
                Title = settings.CachedVoice.Title,
                Description = settings.CachedVoice.Description,
                CoverImage = settings.CachedVoice.CoverImage,
                AuthorName = settings.CachedVoice.AuthorName,
                TaskCount = settings.CachedVoice.TaskCount,
                SampleAudioUrl = settings.CachedVoice.SampleAudioUrl,
            },
        IsS21ProFreePromoDismissed = settings.IsS21ProFreePromoDismissed,
    };
}

public class TestHttpServiceProxy : DispatchProxy
{
    public int GetCallCount { get; private set; }
    public int GetAsBytesCallCount { get; private set; }
    public int GetAsStreamCallCount { get; private set; }
    public int PostAsBytesCallCount { get; private set; }
    public List<string> GetUrls { get; } = [];
    public List<(string Url, Options? Options)> GetOptionsByUrl { get; } = [];
    public string GetResponseJson { get; set; } = "{\"credit\":\"1.00\"}";
    public byte[] GetBytesResponse { get; set; } = new byte[] { 9 };
    public Stream GetStreamResponse { get; set; } = new MemoryStream(new byte[] { 9 });
    public byte[] PostBytes { get; set; } = new byte[] { 1 };
    public Exception? GetException { get; set; }
    public Exception? PostException { get; set; }
    public Func<string, Options?, CancellationToken, Task<string>>? GetAsyncHandler { get; set; }
    public Func<string, object?, Options?, CancellationToken, Task<byte[]>>? PostAsBytesAsyncHandler { get; set; }
    public TaskCompletionSource GetStreamReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string? LastGetUrl { get; private set; }
    public Options? LastGetOptions { get; private set; }
    public string? LastGetStreamUrl { get; private set; }
    public Options? LastGetStreamOptions { get; private set; }
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
            GetUrls.Add(LastGetUrl ?? "");
            LastGetOptions = GetOptionsArgument(args);
            GetOptionsByUrl.Add((LastGetUrl ?? "", LastGetOptions));
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

        if (targetMethod.Name == nameof(IHttpService.GetAsStreamAsync)
            && targetMethod.ReturnType == typeof(Task<Stream>))
        {
            GetAsStreamCallCount++;
            var hasClientName = args?.Length == 4;
            LastGetStreamUrl = GetStringArgument(args, hasClientName ? 1 : 0);
            LastGetStreamOptions = GetOptionsArgument(args);
            GetStreamReturned.SetResult();
            return Task.FromResult(GetStreamResponse);
        }

        if (targetMethod.Name == nameof(IHttpService.PostAsBytesAsync)
            && targetMethod.ReturnType == typeof(Task<byte[]>))
        {
            PostAsBytesCallCount++;
            var hasClientName = args?.Length == 5;
            LastPostUrl = GetStringArgument(args, hasClientName ? 1 : 0);
            LastPostBody = args?[hasClientName ? 2 : 1];
            LastPostOptions = args?[hasClientName ? 3 : 2] as Options;
            if (PostAsBytesAsyncHandler is not null)
            {
                return PostAsBytesAsyncHandler(
                    LastPostUrl ?? "",
                    LastPostBody,
                    LastPostOptions,
                    GetCancellationTokenArgument(args));
            }

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

public sealed class TestPreviewAudioPlayer : IPreviewAudioPlayer
{
    public event EventHandler? Opened;
    public event EventHandler? Ended;
    public event Action<Exception?>? Failed;

    public TimeSpan? Duration { get; set; }
    public TimeSpan Position { get; set; }
    public double Volume { get; set; }
    public Uri? LastOpenedUri { get; private set; }
    public int PlayCallCount { get; private set; }
    public int StopCallCount { get; private set; }
    public int DisposeCallCount { get; private set; }

    public void Open(Uri source) => LastOpenedUri = source;

    public void Play() => PlayCallCount++;

    public void Stop() => StopCallCount++;

    public void Dispose() => DisposeCallCount++;

    public void RaiseOpened() => Opened?.Invoke(this, EventArgs.Empty);

    public void RaiseEnded() => Ended?.Invoke(this, EventArgs.Empty);

    public void RaiseFailed(Exception? exception = null) => Failed?.Invoke(exception);
}

internal sealed class BlockingSettingsViewModel : SettingsViewModel
{
    public BlockingSettingsViewModel(
        IPluginContext context,
        Settings settings,
        SettingsWriteLease settingsWriteLease,
        StartupCreditRefreshCycle startupCreditRefreshCycle,
        TaskCompletionSource constructionSnapshotCaptured,
        TaskCompletionSource releaseConstruction)
        : base(
            context,
            settings,
            clearCoverImageCacheAsync: null,
            clearCoverImageCacheTimeout: null,
            previewAudioPlayerFactory: null,
            startupCreditRefreshCycle: startupCreditRefreshCycle,
            settingsWriteLease: settingsWriteLease)
    {
        constructionSnapshotCaptured.TrySetResult();
        releaseConstruction.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
    }
}

public sealed class LimitGuardStream : Stream
{
    private readonly long _length;
    private readonly long _maxAllowedReadBytes;
    private long _position;

    public LimitGuardStream(long length, long maxAllowedReadBytes)
    {
        _length = length;
        _maxAllowedReadBytes = maxAllowedReadBytes;
    }

    public long TotalBytesRead { get; private set; }
    public int ReadCallCount { get; private set; }
    public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_position >= _length)
            return 0;

        var remainingBeforeLimit = _maxAllowedReadBytes - TotalBytesRead;
        if (remainingBeforeLimit <= 0)
            throw new InvalidOperationException("Stream was read past the configured cache limit.");

        var count = (int)Math.Min(buffer.Length, Math.Min(_length - _position, remainingBeforeLimit));
        FillImageLikeBytes(buffer[..count], _position);
        _position += count;
        TotalBytesRead += count;
        ReadCallCount++;
        return count;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(buffer.Span));
    }

    protected override void Dispose(bool disposing)
    {
        Disposed.TrySetResult();
        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        Disposed.TrySetResult();
        return base.DisposeAsync();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private static void FillImageLikeBytes(Span<byte> buffer, long absoluteOffset)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var sourceIndex = absoluteOffset + i;
            buffer[i] = sourceIndex switch
            {
                0 => 0xFF,
                1 => 0xD8,
                2 => 0xFF,
                3 => 0xE0,
                _ => 0x20,
            };
        }
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
