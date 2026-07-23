using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.Runtime;
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

internal static class InitializationLifecycleSuite
{
    internal static Task ReinitializedSettingsViewUsesNewContextAndSettingsAsync() =>
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

        plugin.Init(oldContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
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

        plugin.Init(newContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
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

    internal static async Task ReinitializationRetiresOldViewModelWithoutSavingSharedBackingStoreAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var backingStore = new SharedSettingsBackingStore(new Settings
        {
            ApiKey = AppliedKey,
            SelectedModel = FishAudioModelPolicy.S2ProModel,
        });
        var oldContext = CreateContext();
        var oldContextProxy = (ContextProxy)(object)oldContext;
        oldContextProxy.LoadSettings = backingStore.Load;
        oldContextProxy.SaveSettings = backingStore.Save;
        var plugin = new Main();

        plugin.Init(oldContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        _ = plugin.GetOrCreateSettingsViewModel();

        backingStore.Replace(new Settings
        {
            ApiKey = DraftKey,
            SelectedModel = FishAudioModelPolicy.S1Model,
        });
        var newContext = CreateContext();
        var newContextProxy = (ContextProxy)(object)newContext;
        newContextProxy.LoadSettings = backingStore.Load;
        newContextProxy.SaveSettings = backingStore.Save;

        plugin.Init(newContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        var newViewModel = plugin.GetOrCreateSettingsViewModel();
        var finalSnapshot = backingStore.Load();

        AssertEqual(DraftKey, newViewModel.ApiKey, "The replacement ViewModel should use the latest shared backing-store settings");
        AssertEqual(DraftKey, finalSnapshot.ApiKey, "Retiring the old ViewModel should not overwrite the replacement backing-store snapshot");
        AssertEqual(0, oldContextProxy.SaveCount, "Retiring an already auto-saved ViewModel should not issue a redundant host save");
    }

    internal static async Task StartupSettingsSaveWinsBeforeFailedReplacementTransitionAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings { SelectedModel = FishAudioModelPolicy.S21ProFreeModel };
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

        plugin.Init(oldContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var oldStartupTask = plugin.PendingStartupTask;
        var oldViewModel = plugin.GetOrCreateSettingsViewModel();
        releaseOldOnlineUtc.SetResult("{\"dateTime\":\"2026-09-01T00:00:00Z\"}");
        await revisionPublished.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var failedContext = CreateContext();
        ((ContextProxy)(object)failedContext).LoadSettings = () =>
            throw new InvalidOperationException("failed replacement transition");
        var replacementFailed = false;
        var replacementTask = Task.Run(() =>
        {
            try
            {
                plugin.Init(failedContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
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
        AssertEqual(FishAudioModelPolicy.S21ProModel, oldViewModel.SelectedModel, "The winning startup Save should apply its model revision to the old ViewModel");

        releaseReplacementGeneration.SetResult();
        await replacementTask.WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(true, replacementFailed, "The later replacement Load failure should remain observable");
    }

    internal static void FailedSameInstanceNormalizationRestoresOldStateAndBackingStore()
    {
        using var network = OverrideNetworkAvailability(false);
        var settings = new Settings { SelectedModel = FishAudioModelPolicy.S21ProFreeModel };
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

        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var oldViewModel = plugin.GetOrCreateSettingsViewModel();
        var factoryFailed = false;
        try
        {
            plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc);
        }
        catch (InvalidOperationException ex) when (ex.Message == "failed normalized replacement factory")
        {
            factoryFailed = true;
        }

        AssertEqual(true, factoryFailed, "The normalized replacement factory failure should remain observable");
        AssertEqual(FishAudioModelPolicy.S21ProFreeModel, settings.SelectedModel, "Failed replacement normalization should not mutate the host-owned old Settings instance");
        AssertEqual(FishAudioModelPolicy.S21ProFreeModel, oldViewModel.SelectedModel, "Failed replacement normalization should preserve the old ViewModel model");
        AssertEqual(FishAudioModelPolicy.S21ProFreeModel, backingStore.Load().SelectedModel, "Failed replacement normalization should not canonical-save over the backing store");
        AssertEqual(0, contextProxy.SaveCount, "Failed replacement normalization should not reach the host save boundary");
    }

    internal static async Task SettingsViewModelCreationWaitsForReplacementCommitAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var plugin = new Main();
        plugin.Init(
            CreateContext(settings: new Settings { SelectedModel = FishAudioModelPolicy.S21ProFreeModel }),
            FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));

        var replacementCommitStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReplacementCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementContext = CreateContext(settings: new Settings
        {
            SelectedModel = FishAudioModelPolicy.S21ProFreeModel,
        });
        var replacementContextProxy = (ContextProxy)(object)replacementContext;
        replacementContextProxy.OnSave = () =>
        {
            replacementCommitStarted.TrySetResult();
            releaseReplacementCommit.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        };

        var reinitializeTask = Task.Run(() =>
            plugin.Init(replacementContext, FishAudioModelPolicy.FreeModelCutoffUtc));
        await replacementCommitStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var viewModelTask = Task.Run(plugin.GetOrCreateSettingsViewModel);
        var viewModelPublishedBeforeCommit = ReferenceEquals(
            await Task.WhenAny(viewModelTask, Task.Delay(TimeSpan.FromMilliseconds(500))),
            viewModelTask);

        releaseReplacementCommit.SetResult();
        await reinitializeTask.WaitAsync(TimeSpan.FromSeconds(2));
        var viewModel = await viewModelTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(false, viewModelPublishedBeforeCommit, "Settings ViewModel publication should wait for an in-flight replacement commit");
        AssertEqual(FishAudioModelPolicy.S21ProModel, viewModel.SelectedModel, "The waiting settings ViewModel request should bind the committed replacement state");
        AssertEqual(1, replacementContextProxy.SaveCount, "Replacement normalization should canonical-save exactly once");
    }

    internal static void SameSettingsInstanceSeparatesOldAndNewViewModelWriteLeases()
    {
        var settings = new Settings { ApiKey = AppliedKey };
        var context = CreateContext(settings: settings);
        var contextProxy = (ContextProxy)(object)context;
        var firstSettings = SettingsStore.Load(
            context,
            FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1),
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
            FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1),
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

    internal static async Task FailedReinitializationRestoresOldViewModelWriteLeaseAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        var backingStore = new SharedSettingsBackingStore(new Settings { ApiKey = AppliedKey });
        var oldContext = CreateContext();
        var oldContextProxy = (ContextProxy)(object)oldContext;
        oldContextProxy.LoadSettings = backingStore.Load;
        oldContextProxy.SaveSettings = backingStore.Save;
        var plugin = new Main();

        plugin.Init(oldContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        var oldViewModel = plugin.GetOrCreateSettingsViewModel();
        var failedLoadContext = CreateContext();
        ((ContextProxy)(object)failedLoadContext).LoadSettings = () =>
            throw new InvalidOperationException("blocked replacement load");

        var loadFailed = false;
        try
        {
            plugin.Init(failedLoadContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
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
        factoryPlugin.Init(factoryContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var factoryOldViewModel = factoryPlugin.GetOrCreateSettingsViewModel();

        var factoryFailed = false;
        try
        {
            factoryPlugin.Init(CreateContext(settings: new Settings { ApiKey = DraftKey }), FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        }
        catch (InvalidOperationException ex) when (ex.Message == "blocked replacement factory")
        {
            factoryFailed = true;
        }

        AssertEqual(true, factoryFailed, "The replacement ViewModel factory failure should remain observable");
        factoryOldViewModel.ApiKey = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        AssertEqual(1, factoryContextProxy.SaveCount, "A failed replacement ViewModel construction should restore the old write lease");
    }

    internal static void DisposeRetiresSettingsWriteLease()
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
            FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        _ = plugin.GetOrCreateSettingsViewModel();

        AssertEqual(true, runtimeSettings?.ActiveWriteLease != 0, "Initialization should grant the detached runtime Settings a write lease");
        plugin.Dispose();
        AssertEqual(0L, runtimeSettings?.ActiveWriteLease, "Dispose should retire the detached runtime Settings write lease");
    }

    internal static async Task OvertakenInitializationSkipsSettingsLoadAsync()
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
        plugin.Init(CreateContext(settings: new Settings { ApiKey = AppliedKey }), FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));

        var staleContext = CreateContext(settings: new Settings
        {
            ApiKey = "ffffffffffffffffffffffffffffffff",
            SelectedModel = "obsolete-model",
        });
        var staleContextProxy = (ContextProxy)(object)staleContext;
        var staleInitializationTask = Task.Run(() =>
            plugin.Init(staleContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1)));
        await staleGenerationDeclared.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var currentSettings = new Settings { ApiKey = DraftKey };
        var currentContext = CreateContext(settings: currentSettings);
        plugin.Init(currentContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        releaseStaleGeneration.SetResult();
        await staleInitializationTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(0, staleContextProxy.LoadCount, "An overtaken initialization should not call SettingsStore.Load");
        AssertEqual(0, staleContextProxy.SaveCount, "An overtaken initialization should not canonical-save stale settings");
        AssertEqual(DraftKey, ((ContextProxy)(object)currentContext).Settings.ApiKey, "The newest initialization should remain current");
    }

    internal static async Task ReinitializationWaitsForInFlightViewModelSaveAsync()
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

        plugin.Init(oldContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
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
            plugin.Init(newContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1)));
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

    internal static void ModelPolicyUsesCutoffDefaultsAndNormalizeLoudnessSupport()
    {
        var expectedCutoff = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var lastFreeInstant = expectedCutoff.AddTicks(-1);

        AssertEqual(expectedCutoff, FishAudioModelPolicy.FreeModelCutoffUtc, "Free model cutoff should be September 1, 2026 UTC");

        AssertEnumerableEqual(
            new[]
            {
                FishAudioModelPolicy.S21ProFreeModel,
                FishAudioModelPolicy.S21ProModel,
                FishAudioModelPolicy.S2ProModel,
                FishAudioModelPolicy.S1Model,
            },
            FishAudioModelPolicy.GetAvailableModels(lastFreeInstant),
            "Free model should remain available through the last tick of August 31 UTC");
        AssertEqual(FishAudioModelPolicy.S21ProFreeModel, FishAudioModelPolicy.GetDefaultModel(lastFreeInstant), "Free model should remain the default through the last tick of August 31 UTC");

        AssertEnumerableEqual(
            new[] { FishAudioModelPolicy.S21ProModel, FishAudioModelPolicy.S2ProModel, FishAudioModelPolicy.S1Model },
            FishAudioModelPolicy.GetAvailableModels(expectedCutoff),
            "Free model should be unavailable at September 1 UTC");
        AssertEqual(FishAudioModelPolicy.S21ProModel, FishAudioModelPolicy.GetDefaultModel(expectedCutoff), "s2.1-pro should be the default at September 1 UTC");

        AssertEqual(true, FishAudioModelPolicy.SupportsNormalizeLoudness(FishAudioModelPolicy.S21ProFreeModel), "s2.1-pro-free should support normalize_loudness");
        AssertEqual(true, FishAudioModelPolicy.SupportsNormalizeLoudness(FishAudioModelPolicy.S21ProModel), "s2.1-pro should support normalize_loudness");
        AssertEqual(true, FishAudioModelPolicy.SupportsNormalizeLoudness(FishAudioModelPolicy.S2ProModel), "s2-pro should support normalize_loudness");
        AssertEqual(false, FishAudioModelPolicy.SupportsNormalizeLoudness(FishAudioModelPolicy.S1Model), "s1 should not support normalize_loudness");

        using (OverrideLocalUtcNow(lastFreeInstant))
        {
            var settings = new Settings();
            new Main().Init(CreateContext(settings: settings), lastFreeInstant);
            AssertEqual(FishAudioModelPolicy.S21ProFreeModel, settings.SelectedModel, "New settings should use the free model default through the last tick of August 31 UTC");
        }

        using (OverrideLocalUtcNow(expectedCutoff))
        {
            var settings = new Settings();
            new Main().Init(CreateContext(settings: settings), expectedCutoff);
            AssertEqual(FishAudioModelPolicy.S21ProModel, settings.SelectedModel, "New settings should use s2.1-pro as the default at September 1 UTC");
        }
    }

    internal static void MainInitNormalizesStructuredSettingsWithoutClearingKeys()
    {
        var settings = new Settings
        {
            ApiKey = "ABC",
            VoiceId = "not-a-voice-id",
            SelectedModel = FishAudioModelPolicy.S21ProFreeModel,
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

        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc);

        AssertEqual("ABC", settings.ApiKey, "Startup normalization should not clear malformed API Key text");
        AssertEqual("not-a-voice-id", settings.VoiceId, "Startup normalization should not clear malformed Voice ID text");
        AssertEqual(FishAudioModelPolicy.S21ProModel, settings.SelectedModel, "Expired persisted free model should normalize to the current default");
        AssertEqual("normal", settings.Latency, "Invalid latency should normalize to default");
        AssertEqual(192, settings.Mp3Bitrate, "Invalid MP3 bitrate should normalize to default");
        AssertEqual(1.0, settings.Speed, "Invalid speed should normalize to default");
        AssertEqual(0.0, settings.Volume, "Invalid volume should normalize to default");
        AssertEqual(0.7, settings.Temperature, "Invalid temperature should normalize to default");
        AssertEqual(0.7, settings.TopP, "Invalid top_p should normalize to default");
        AssertEqual(1, proxy.SaveCount, "Startup normalization should save corrected settings once");
    }

    internal static async Task StartupRefreshSelectedVoiceMetadataAsync()
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

        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
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
        plugin.Init(CreateContext(settings: settings, httpService: httpService), FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(0, http.GetCallCount, "Startup voice refresh should skip server call when offline");
        AssertEqual("Keep", settings.CachedVoice?.Title, "Skipped startup voice refresh should preserve cached voice");
    }

    internal static async Task StartupVoiceUiApplyRechecksExpectedVoiceIdAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string requestedVoiceId = "fedcba9876543210fedcba9876543210";
        const string replacementVoiceId = "0123456789abcdef0123456789abcdef";
        var backingStore = new SharedSettingsBackingStore(new Settings
        {
            VoiceId = requestedVoiceId,
            SelectedModel = FishAudioModelPolicy.S2ProModel,
            CachedVoice = new CachedVoiceInfo { Title = "Voice A (old)" },
        });
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startupVoiceSaved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queuedUiApply = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetAsyncHandler = (url, _, _) =>
        {
            if (string.Equals(url, FishAudioClock.TimeApiUrl, StringComparison.Ordinal))
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

        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
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

    internal static async Task DelayedStartupVoiceRefreshDoesNotRestoreClearedVoiceAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string requestedVoiceId = "fedcba9876543210fedcba9876543210";
        var backingStore = new SharedSettingsBackingStore(new Settings
        {
            VoiceId = requestedVoiceId,
            SelectedModel = FishAudioModelPolicy.S2ProModel,
            CachedVoice = new CachedVoiceInfo { Title = "Voice A (old)" },
        });
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetAsyncHandler = (url, _, _) =>
        {
            if (string.Equals(url, FishAudioClock.TimeApiUrl, StringComparison.Ordinal))
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

        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
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

    internal static async Task DelayedStartupVoiceRefreshDoesNotOverwriteNewSelectionAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string requestedVoiceId = "fedcba9876543210fedcba9876543210";
        const string replacementVoiceId = "0123456789abcdef0123456789abcdef";
        var backingStore = new SharedSettingsBackingStore(new Settings
        {
            VoiceId = requestedVoiceId,
            SelectedModel = FishAudioModelPolicy.S2ProModel,
            CachedVoice = new CachedVoiceInfo { Title = "Voice A (old)" },
        });
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetAsyncHandler = (url, _, _) =>
        {
            if (string.Equals(url, FishAudioClock.TimeApiUrl, StringComparison.Ordinal))
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

        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
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

    internal static async Task ConcurrentPaidModelSelectionStillPublishesPostCutoffPolicyAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var backingStore = new SharedSettingsBackingStore(new Settings
        {
            SelectedModel = FishAudioModelPolicy.S21ProFreeModel,
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

        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var viewModel = plugin.GetOrCreateSettingsViewModel();
        releaseOnlineUtc.SetResult("{\"dateTime\":\"2026-09-01T00:00:00Z\"}");
        await revisionPublished.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.SelectedModel = FishAudioModelPolicy.S2ProModel;
        AssertEqual(1, contextProxy.SaveCount, "Selecting a paid model while startup is reserved should save the user choice once");
        releaseRevision.SetResult();
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(FishAudioModelPolicy.S2ProModel, runtimeSettings?.SelectedModel, "Post-cutoff policy publication should preserve the concurrent runtime paid-model choice");
        AssertEqual(FishAudioModelPolicy.S2ProModel, viewModel.SelectedModel, "Post-cutoff policy publication should preserve the visible paid-model choice");
        AssertEqual(FishAudioModelPolicy.S2ProModel, contextProxy.Settings.SelectedModel, "Post-cutoff policy publication should preserve the host-owned paid-model choice");
        AssertEqual(FishAudioModelPolicy.S2ProModel, backingStore.Load().SelectedModel, "Post-cutoff policy publication should preserve the persisted paid-model choice");
        AssertEqual(false, viewModel.Models.Contains(FishAudioModelPolicy.S21ProFreeModel, StringComparer.Ordinal), "Post-cutoff policy publication should remove the free model from the available list");
        AssertEqual(false, viewModel.IsS21ProFreeAvailable, "Post-cutoff policy publication should mark the free-model promo unavailable");
        AssertEqual(false, viewModel.ShowS21ProFreePromo, "Post-cutoff policy publication should hide the free-model promo");
        AssertEqual(2, contextProxy.SaveCount, "Post-cutoff policy publication should complete its accepted settings transaction after the user save");

        viewModel.UseS21ProFreePromoCommand.Execute(null);

        AssertEqual(FishAudioModelPolicy.S2ProModel, runtimeSettings?.SelectedModel, "An unavailable promo should not replace the runtime paid-model choice");
        AssertEqual(FishAudioModelPolicy.S2ProModel, viewModel.SelectedModel, "An unavailable promo should not replace the visible paid-model choice");
        AssertEqual(FishAudioModelPolicy.S2ProModel, backingStore.Load().SelectedModel, "An unavailable promo should not replace the persisted paid-model choice");
        AssertEqual(2, contextProxy.SaveCount, "An unavailable promo should not issue another settings save");
    }

    internal static async Task StartupModelSaveFailureRollsBackRuntimeAndStorageAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var backingStore = new SharedSettingsBackingStore(new Settings
        {
            SelectedModel = FishAudioModelPolicy.S21ProFreeModel,
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

        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var viewModel = plugin.GetOrCreateSettingsViewModel();
        releaseOnlineUtc.SetResult("{\"dateTime\":\"2026-09-01T00:00:00Z\"}");
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(FishAudioModelPolicy.S21ProFreeModel, runtimeSettings?.SelectedModel, "A failed startup model save should roll back the runtime Settings model");
        AssertEqual(FishAudioModelPolicy.S21ProFreeModel, viewModel.SelectedModel, "A failed startup model save should leave the existing ViewModel model unchanged");
        AssertEqual(FishAudioModelPolicy.S21ProFreeModel, contextProxy.Settings.SelectedModel, "A failed startup model save should roll back the host-owned Settings object");
        AssertEqual(FishAudioModelPolicy.S21ProFreeModel, backingStore.Load().SelectedModel, "A failed startup model save should preserve backing storage");

        viewModel.ApiKey = DraftKey;
        var savedSnapshot = backingStore.Load();
        AssertEqual(2, contextProxy.SaveCount, "A later ViewModel edit should still save after startup model rollback");
        AssertEqual(DraftKey, savedSnapshot.ApiKey, "A later ViewModel edit should persist normally after startup model rollback");
        AssertEqual(FishAudioModelPolicy.S21ProFreeModel, savedSnapshot.SelectedModel, "A later save should not resurrect the rolled-back startup model");
    }

    internal static async Task StartupVoiceSaveFailureRollsBackRuntimeAndStorageAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string voiceId = "fedcba9876543210fedcba9876543210";
        var backingStore = new SharedSettingsBackingStore(new Settings
        {
            VoiceId = voiceId,
            SelectedModel = FishAudioModelPolicy.S2ProModel,
            CachedVoice = new CachedVoiceInfo { Title = "Old Voice" },
        });
        var releaseOnlineUtc = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetAsyncHandler = (url, _, _) =>
            string.Equals(url, FishAudioClock.TimeApiUrl, StringComparison.Ordinal)
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

        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
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

    internal static async Task StartupModelNormalizationInvalidatesSpeculativeSettingsViewModelAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings
        {
            SelectedModel = FishAudioModelPolicy.S21ProFreeModel,
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

        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var viewModelTask = Task.Run(plugin.GetOrCreateSettingsViewModel);
        await constructionSnapshotCaptured.Task.WaitAsync(TimeSpan.FromSeconds(2));

        releaseOnlineUtc.SetResult("{\"dateTime\":\"2026-09-01T00:00:00Z\"}");
        await startupModelSaved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        releaseConstruction.SetResult();

        var viewModel = await viewModelTask.WaitAsync(TimeSpan.FromSeconds(2));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(FishAudioModelPolicy.S21ProModel, settings.SelectedModel, "Startup online time should normalize the current Settings model");
        AssertEqual(FishAudioModelPolicy.S21ProModel, viewModel.SelectedModel, "A ViewModel constructed across startup normalization should retry with the current model");
        AssertEqual(1, contextProxy.SaveCount, "Disposing a stale model-snapshot candidate should not repeat the startup settings save");
    }

    internal static async Task CommittedStartupSettingsInvalidatesPostReservationViewModelCandidateAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings
        {
            SelectedModel = FishAudioModelPolicy.S21ProFreeModel,
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

        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        releaseOnlineUtc.SetResult("{\"dateTime\":\"2026-09-01T00:00:00Z\"}");
        await revisionPublished.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var viewModelTask = Task.Run(plugin.GetOrCreateSettingsViewModel);
        await constructionSnapshotCaptured.Task.WaitAsync(TimeSpan.FromSeconds(2));
        releaseRevision.SetResult();
        await startupModelSaved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        releaseConstruction.SetResult();

        var viewModel = await viewModelTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(FishAudioModelPolicy.S21ProModel, settings.SelectedModel, "The committed startup transaction should update host settings");
        AssertEqual(FishAudioModelPolicy.S21ProModel, viewModel.SelectedModel, "A ViewModel candidate captured after revision reservation should retry after runtime commit");
        AssertEqual(1, contextProxy.SaveCount, "Invalidating the post-reservation candidate should not repeat the startup settings save");
    }

    internal static async Task StartupVoiceRefreshInvalidatesSpeculativeSettingsViewModelAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings
        {
            SelectedModel = FishAudioModelPolicy.S2ProModel,
            VoiceId = "fedcba9876543210fedcba9876543210",
            CachedVoice = new CachedVoiceInfo { Title = "Old Voice" },
        };
        var releaseOnlineUtc = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var constructionSnapshotCaptured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConstruction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startupVoiceSaved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetAsyncHandler = (url, _, _) =>
            string.Equals(url, FishAudioClock.TimeApiUrl, StringComparison.Ordinal)
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

        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
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

    internal static async Task StartupSettingsSaveSkipsAfterNewInitializationRequestAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var backingStore = new SharedSettingsBackingStore(new Settings
        {
            ApiKey = AppliedKey,
            SelectedModel = FishAudioModelPolicy.S21ProFreeModel,
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

        plugin.Init(oldContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var oldStartupTask = plugin.PendingStartupTask;
        releaseOldOnlineUtc.SetResult("{\"dateTime\":\"2026-09-01T00:00:00Z\"}");
        await revisionPublished.Task.WaitAsync(TimeSpan.FromSeconds(2));

        backingStore.Replace(new Settings
        {
            ApiKey = DraftKey,
            SelectedModel = FishAudioModelPolicy.S1Model,
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
            plugin.Init(newContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1)));
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

    internal static async Task InitializationLoadWaitsForAuthorizedStartupSettingsSaveAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string voiceId = "fedcba9876543210fedcba9876543210";
        var backingStore = new SharedSettingsBackingStore(new Settings
        {
            VoiceId = voiceId,
            SelectedModel = FishAudioModelPolicy.S2ProModel,
            CachedVoice = new CachedVoiceInfo { Title = "Old Voice" },
        });
        var releaseOldOnlineUtc = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (oldHttpService, oldHttp) = TestHttpServiceProxy.Create();
        oldHttp.GetAsyncHandler = (url, _, _) =>
            string.Equals(url, FishAudioClock.TimeApiUrl, StringComparison.Ordinal)
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

        plugin.Init(oldContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var oldStartupTask = plugin.PendingStartupTask;
        releaseOldOnlineUtc.SetResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
        await oldSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var newLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (newHttpService, newHttp) = TestHttpServiceProxy.Create();
        newHttp.GetAsyncHandler = (url, _, _) =>
            string.Equals(url, FishAudioClock.TimeApiUrl, StringComparison.Ordinal)
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
            plugin.Init(newContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1)));
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

    internal static async Task FailedReinitializationRestoresStartupSettingsSaveAuthorizationAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings { SelectedModel = FishAudioModelPolicy.S21ProFreeModel };
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

        plugin.Init(oldContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var oldStartupTask = plugin.PendingStartupTask;
        var oldViewModel = plugin.GetOrCreateSettingsViewModel();
        releaseOldOnlineUtc.SetResult("{\"dateTime\":\"2026-09-01T00:00:00Z\"}");
        await revisionPublished.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var failedContext = CreateContext();
        ((ContextProxy)(object)failedContext).LoadSettings = () =>
            throw new InvalidOperationException("failed replacement load");
        var loadFailed = false;
        try
        {
            plugin.Init(failedContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        }
        catch (InvalidOperationException ex) when (ex.Message == "failed replacement load")
        {
            loadFailed = true;
        }

        releaseRevision.SetResult();
        await oldStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(true, loadFailed, "The replacement Load failure should remain observable");
        AssertEqual(1, oldContextProxy.SaveCount, "A failed replacement should restore an already-revised startup settings save");
        AssertEqual(FishAudioModelPolicy.S21ProModel, oldViewModel.SelectedModel, "A failed replacement should let the old ViewModel apply its authorized startup revision");
    }

    internal static async Task StartupRefreshDisposeCancelsPendingWorkWithoutLoggingAsync()
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

        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        plugin.Dispose();

        await requestCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual("Keep", settings.CachedVoice?.Title, "Canceled startup refresh should preserve cached voice");
        AssertEqual(0, proxy.SaveCount, "Canceled startup refresh should not save settings");
        AssertEqual(false, logger.Contains("startup refresh failed"), "Canceled startup refresh should not log a failure warning");
    }

    internal static async Task StartupRefreshCancellationAfterResponseSkipsSideEffectsAsync()
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

        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual("Keep", settings.CachedVoice?.Title, "Startup cancellation after model response should skip cached voice side effects");
        AssertEqual(0, proxy.SaveCount, "Startup cancellation after model response should not save settings");
        AssertEqual(false, logger.Contains("startup refresh failed"), "Startup cancellation after model response should not log failure");
    }

    internal static async Task ReinitializedStartupRefreshCannotMutateNewSettingsAsync()
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

        plugin.Init(oldContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var oldStartupTask = plugin.PendingStartupTask;
        plugin.Init(newContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        oldRelease.SetResult("{\"_id\":\"11111111111111111111111111111111\",\"title\":\"Old Should Not Leak\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}");
        await oldStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
        await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual("Old Keep", oldSettings.CachedVoice?.Title, "Canceled old startup refresh should not mutate old settings after reinitialization");
        AssertEqual("New Keep", newSettings.CachedVoice?.Title, "Canceled old startup refresh should not mutate new settings after reinitialization");
        AssertEqual(0, oldProxy.SaveCount, "Canceled old startup refresh should not save old context after reinitialization");
        AssertEqual(0, newProxy.SaveCount, "Canceled old startup refresh should not save new context after reinitialization");
    }
}
