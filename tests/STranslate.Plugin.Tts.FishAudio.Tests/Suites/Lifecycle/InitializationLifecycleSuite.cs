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

[NUnit.Framework.TestFixture]
[NUnit.Framework.NonParallelizable]
internal static class InitializationLifecycleSuite
{
    internal static Task ReinitializedSettingsViewUsesNewContextAndSettingsAsync() =>
        RunOnStaThreadAsync(async () =>
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
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
        var newViewModel = plugin.GetOrCreateSettingsViewModel();
        var reusedSettingsView = plugin.GetSettingUI();

        AssertEqual(true, ReferenceEquals(settingsView, reusedSettingsView), "Reinitialization should reuse the existing SettingsView control");
        AssertEqual(false, ReferenceEquals(oldViewModel, newViewModel), "Reinitialization should replace the ViewModel that owns readonly context and settings dependencies");
        AssertEqual(true, ReferenceEquals(newViewModel, reusedSettingsView.DataContext), "The cached SettingsView should bind the replacement ViewModel");
        AssertEqual(DraftKey, newViewModel.ApiKey, "The replacement ViewModel should load the second Settings object");
        AssertEqual("78.90", newViewModel.UserCredit, "The replacement ViewModel should display the second initialization balance");

        releaseOldCredit.SetResult("{\"credit\":\"99.99\"}");
        await oldStartupTask.WaitAsync(RegressionExecution.Timeout);
        AssertEqual("78.90", newViewModel.UserCredit, "The first initialization result should not overwrite the replacement ViewModel");

        const string editedNewKey = "ffffffffffffffffffffffffffffffff";
        newViewModel.ApiKey = editedNewKey;
        AssertEqual(editedNewKey, newSettings.ApiKey, "Replacement ViewModel edits should update the second Settings object");
        AssertEqual(AppliedKey, oldSettings.ApiKey, "Replacement ViewModel edits should not mutate the first Settings object");
        AssertEqual(1, newContextProxy.SaveCount, "Replacement ViewModel edits should save through the second Context");
    });

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "lifecycle.initialization/reinitialization-retires-old-view-model-without-saving-shared-backing-store")]
    [NUnit.Framework.Property("OriginalOrdinal", 122)]
    [NUnit.Framework.Order(122)]
    public static Task ReinitializationRetiresOldViewModelWithoutSavingSharedBackingStoreAsync() =>
        RunReinitializationRetiresOldViewModelWithoutSavingSharedBackingStoreAsync();

    internal static async Task RunReinitializationRetiresOldViewModelWithoutSavingSharedBackingStoreAsync(
        Action<NUnitCompatibility.PublishedInitializationObservation>? onAcquired = null)
    {
        using var network = OverrideNetworkAvailability(false);
        var owner = new PublishedInitializationFixtureOwner();
        await owner.RunAsync(async () =>
        {
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
            owner.Plugin = plugin;

            plugin.Init(oldContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
            owner.CapturePublishedWork();
            await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
            _ = plugin.GetOrCreateSettingsViewModel();
            owner.CapturePublishedWork();

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
            owner.CapturePublishedWork();
            await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
            var newViewModel = plugin.GetOrCreateSettingsViewModel();
            owner.CapturePublishedWork();
            var finalSnapshot = backingStore.Load();
            onAcquired?.Invoke(owner.Observe());

            AssertEqual(DraftKey, newViewModel.ApiKey, "The replacement ViewModel should use the latest shared backing-store settings");
            AssertEqual(DraftKey, finalSnapshot.ApiKey, "Retiring the old ViewModel should not overwrite the replacement backing-store snapshot");
            AssertEqual(0, oldContextProxy.SaveCount, "Retiring an already auto-saved ViewModel should not issue a redundant host save");
        });
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
                releaseRevision.Task.WaitAsync(RegressionExecution.Timeout).GetAwaiter().GetResult();
            },
            initializationGenerationDeclared: generation =>
            {
                if (generation != 2)
                    return;

                replacementGenerationDeclared.TrySetResult();
                releaseReplacementGeneration.Task.WaitAsync(RegressionExecution.Timeout).GetAwaiter().GetResult();
            });

        plugin.Init(oldContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var oldStartupTask = plugin.PendingStartupTask;
        var oldViewModel = plugin.GetOrCreateSettingsViewModel();
        releaseOldOnlineUtc.SetResult("{\"dateTime\":\"2026-12-01T00:00:00Z\"}");
        await revisionPublished.Task.WaitAsync(RegressionExecution.Timeout);

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
        await replacementGenerationDeclared.Task.WaitAsync(RegressionExecution.Timeout);

        releaseRevision.SetResult();
        await oldStartupTask.WaitAsync(RegressionExecution.Timeout);

        AssertEqual(1, oldContextProxy.SaveCount, "Startup settings Save should complete when it wins the transition gate before replacement Load");
        AssertEqual(FishAudioModelPolicy.S21ProModel, oldViewModel.SelectedModel, "The winning startup Save should apply its model revision to the old ViewModel");

        releaseReplacementGeneration.SetResult();
        await replacementTask.WaitAsync(RegressionExecution.Timeout);
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
            releaseReplacementCommit.Task.WaitAsync(RegressionExecution.Timeout).GetAwaiter().GetResult();
        };

        var reinitializeTask = Task.Run(() =>
            plugin.Init(replacementContext, FishAudioModelPolicy.FreeModelCutoffUtc));
        await replacementCommitStarted.Task.WaitAsync(RegressionExecution.Timeout);

        var viewModelTask = Task.Run(plugin.GetOrCreateSettingsViewModel);
        var viewModelPublishedBeforeCommit = ReferenceEquals(
            await Task.WhenAny(viewModelTask, Task.Delay(TimeSpan.FromMilliseconds(500))),
            viewModelTask);

        releaseReplacementCommit.SetResult();
        await reinitializeTask.WaitAsync(RegressionExecution.Timeout);
        var viewModel = await viewModelTask.WaitAsync(RegressionExecution.Timeout);

        AssertEqual(false, viewModelPublishedBeforeCommit, "Settings ViewModel publication should wait for an in-flight replacement commit");
        AssertEqual(FishAudioModelPolicy.S21ProModel, viewModel.SelectedModel, "The waiting settings ViewModel request should bind the committed replacement state");
        AssertEqual(1, replacementContextProxy.SaveCount, "Replacement normalization should canonical-save exactly once");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "lifecycle.initialization/same-settings-instance-separates-old-and-new-view-model-write-leases")]
    [NUnit.Framework.Property("OriginalOrdinal", 126)]
    [NUnit.Framework.Order(126)]
    public static void SameSettingsInstanceSeparatesOldAndNewViewModelWriteLeases()
    {
        var settings = new Settings { ApiKey = AppliedKey };
        var context = CreateContext(settings: settings);
        var contextProxy = (ContextProxy)(object)context;
        Settings? firstSettings = null;
        Settings? secondSettings = null;
        SettingsWriteLease firstWriteLease = default;
        SettingsWriteLease secondWriteLease = default;
        SettingsViewModel? oldViewModel = null;
        SettingsViewModel? newViewModel = null;
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? primaryFailure = null;
        try
        {
            firstSettings = SettingsStore.Load(
                context,
                FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1),
                out firstWriteLease);
            oldViewModel = new SettingsViewModel(
                context,
                firstSettings,
                clearCoverImageCacheAsync: null,
                clearCoverImageCacheTimeout: null,
                settingsWriteLease: firstWriteLease);

            SettingsStore.Retire(firstSettings, firstWriteLease);
            secondSettings = SettingsStore.Load(
                context,
                FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1),
                out secondWriteLease);
            newViewModel = new SettingsViewModel(
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
        catch (Exception error) { primaryFailure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
        finally
        {
            FinishWriteLeaseOwners(firstSettings, firstWriteLease, oldViewModel,
                secondSettings, secondWriteLease, newViewModel, primaryFailure);
        }
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
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
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

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "lifecycle.initialization/dispose-retires-settings-write-lease")]
    [NUnit.Framework.Property("OriginalOrdinal", 128)]
    [NUnit.Framework.Order(128)]
    public static Task DisposeRetiresSettingsWriteLease() =>
        RunDisposeRetiresSettingsWriteLeaseAsync();

    internal static async Task RunDisposeRetiresSettingsWriteLeaseAsync(
        Action<NUnitCompatibility.PublishedInitializationObservation>? onAcquired = null)
    {
        using var network = OverrideNetworkAvailability(false);
        var owner = new PublishedInitializationFixtureOwner();
        await owner.RunAsync(() =>
        {
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
            owner.Plugin = plugin;
            plugin.Init(
                CreateContext(settings: new Settings { ApiKey = AppliedKey }),
                FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
            owner.CapturePublishedWork();
            _ = plugin.GetOrCreateSettingsViewModel();
            owner.CapturePublishedWork();

            AssertEqual(true, runtimeSettings?.ActiveWriteLease != 0, "Initialization should grant the detached runtime Settings a write lease");
            onAcquired?.Invoke(owner.Observe(runtimeSettings: runtimeSettings));
            plugin.Dispose();
            AssertEqual(0L, runtimeSettings?.ActiveWriteLease, "Dispose should retire the detached runtime Settings write lease");
            return Task.CompletedTask;
        });
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
                releaseStaleGeneration.Task.WaitAsync(RegressionExecution.Timeout).GetAwaiter().GetResult();
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
        await staleGenerationDeclared.Task.WaitAsync(RegressionExecution.Timeout);

        var currentSettings = new Settings { ApiKey = DraftKey };
        var currentContext = CreateContext(settings: currentSettings);
        plugin.Init(currentContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        releaseStaleGeneration.SetResult();
        await staleInitializationTask.WaitAsync(RegressionExecution.Timeout);

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
            releaseOldSave.Task.WaitAsync(RegressionExecution.Timeout).GetAwaiter().GetResult();
            backingStore.Save(settings);
        };
        var plugin = new Main();

        plugin.Init(oldContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
        var oldViewModel = plugin.GetOrCreateSettingsViewModel();
        var oldEditTask = Task.Run(() => oldViewModel.ApiKey = DraftKey);
        await oldSaveStarted.Task.WaitAsync(RegressionExecution.Timeout);

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
        await oldEditTask.WaitAsync(RegressionExecution.Timeout);
        await reinitializeTask.WaitAsync(RegressionExecution.Timeout);
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
        var newViewModel = plugin.GetOrCreateSettingsViewModel();

        AssertEqual(false, loadStartedBeforeOldSaveCompleted, "Replacement Load should wait for an in-flight old ViewModel save to finish");
        AssertEqual(DraftKey, newViewModel.ApiKey, "Replacement Load should observe the completed old ViewModel save");
        AssertEqual(DraftKey, backingStore.Load().ApiKey, "The serialized old save and replacement Load should leave backing storage consistent");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "lifecycle.initialization/model-policy-uses-cutoff-defaults-and-normalize-loudness-support")]
    [NUnit.Framework.Property("OriginalOrdinal", 132)]
    [NUnit.Framework.Order(132)]
    public static Task ModelPolicyUsesCutoffDefaultsAndNormalizeLoudnessSupport() =>
        RegressionFixtureOwner.RunAsync(async () =>
        {
            var expectedCutoff = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
            var lastFreeInstant = expectedCutoff.AddTicks(-1);

            AssertEqual(expectedCutoff, FishAudioModelPolicy.FreeModelCutoffUtc, "Free model cutoff should be December 1, 2026 UTC");

            AssertEnumerableEqual(
                new[]
                {
                    FishAudioModelPolicy.S21ProFreeModel,
                    FishAudioModelPolicy.Drama3Model,
                    FishAudioModelPolicy.S21ProModel,
                    FishAudioModelPolicy.S2ProModel,
                    FishAudioModelPolicy.S1Model,
                },
                FishAudioModelPolicy.GetAvailableModels(lastFreeInstant),
                "Free model should remain available through the last tick of November 30 UTC");
            AssertEqual(FishAudioModelPolicy.S21ProFreeModel, FishAudioModelPolicy.GetDefaultModel(lastFreeInstant), "Free model should remain the default through the last tick of November 30 UTC");

            AssertEnumerableEqual(
                new[] { FishAudioModelPolicy.Drama3Model, FishAudioModelPolicy.S21ProModel, FishAudioModelPolicy.S2ProModel, FishAudioModelPolicy.S1Model },
                FishAudioModelPolicy.GetAvailableModels(expectedCutoff),
                "Free model should be unavailable at December 1 UTC");
            AssertEqual(FishAudioModelPolicy.S21ProModel, FishAudioModelPolicy.GetDefaultModel(expectedCutoff), "s2.1-pro should be the default at December 1 UTC");

            AssertEqual(true, FishAudioModelPolicy.SupportsNormalizeLoudness(FishAudioModelPolicy.S21ProFreeModel), "s2.1-pro-free should support normalize_loudness");
            AssertEqual(true, FishAudioModelPolicy.SupportsNormalizeLoudness(FishAudioModelPolicy.S21ProModel), "s2.1-pro should support normalize_loudness");
            AssertEqual(true, FishAudioModelPolicy.SupportsNormalizeLoudness(FishAudioModelPolicy.S2ProModel), "s2-pro should support normalize_loudness");
            AssertEqual(false, FishAudioModelPolicy.SupportsNormalizeLoudness(FishAudioModelPolicy.S1Model), "s1 should not support normalize_loudness");

            using (OverrideLocalUtcNow(lastFreeInstant))
            {
                var owner = new PublishedInitializationFixtureOwner();
                await owner.RunAsync(() =>
                {
                    var settings = new Settings();
                    var plugin = new Main();
                    owner.Plugin = plugin;
                    plugin.Init(CreateContext(settings: settings), lastFreeInstant);
                    owner.CapturePublishedWork();
                    AssertEqual(FishAudioModelPolicy.S21ProFreeModel, settings.SelectedModel, "New settings should use the free model default through the last tick of November 30 UTC");
                    return Task.CompletedTask;
                });
            }

            using (OverrideLocalUtcNow(expectedCutoff))
            {
                var owner = new PublishedInitializationFixtureOwner();
                await owner.RunAsync(() =>
                {
                    var settings = new Settings();
                    var plugin = new Main();
                    owner.Plugin = plugin;
                    plugin.Init(CreateContext(settings: settings), expectedCutoff);
                    owner.CapturePublishedWork();
                    AssertEqual(FishAudioModelPolicy.S21ProModel, settings.SelectedModel, "New settings should use s2.1-pro as the default at December 1 UTC");
                    return Task.CompletedTask;
                });
            }
        });

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "lifecycle.initialization/main-init-normalizes-structured-settings-without-clearing-keys")]
    [NUnit.Framework.Property("OriginalOrdinal", 134)]
    [NUnit.Framework.Order(134)]
    public static Task MainInitNormalizesStructuredSettingsWithoutClearingKeys()
    {
        var owner = new PublishedInitializationFixtureOwner();
        return owner.RunAsync(() =>
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
            owner.Plugin = plugin;

            plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc);
            owner.CapturePublishedWork();

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
            return Task.CompletedTask;
        });
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "lifecycle.initialization/startup-refresh-selected-voice-metadata")]
    [NUnit.Framework.Property("OriginalOrdinal", 135)]
    [NUnit.Framework.Order(135)]
    public static Task StartupRefreshSelectedVoiceMetadataAsync() =>
        RegressionFixtureOwner.RunAsync(async () =>
        {
            foreach (var apiKey in new[] { "configured-startup-key", "", " \t" })
            {
                using var network = OverrideNetworkAvailability(true);
                var settings = new Settings
                {
                    ApiKey = apiKey,
                    VoiceId = "fedcba9876543210fedcba9876543210",
                    CachedVoice = new CachedVoiceInfo { Title = "Old", CoverImage = "old" },
                };
                var (httpService, http) = TestHttpServiceProxy.Create();
                http.GetResponseJson = "{\"_id\":\"fedcba9876543210fedcba9876543210\",\"title\":\"Fresh Voice\",\"description\":\"Fresh description\",\"cover_image\":\"fresh-cover\",\"samples\":[{\"audio\":\"https://audio.example/fresh.mp3\"}],\"task_count\":42,\"author\":{\"nickname\":\"Fresh Author\"}}";
                var context = CreateContext(settings: settings, httpService: httpService);
                var proxy = (ContextProxy)(object)context;
                var plugin = new Main();

                var owner1 = new PublishedInitializationFixtureOwner { Plugin = plugin };
                await owner1.RunAsync(async () =>
                {
                    plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
                    owner1.CapturePublishedWork();
                    await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);

                    AssertEqual(true, http.LastGetUrl?.Contains("/model/fedcba9876543210fedcba9876543210", StringComparison.Ordinal), "Startup refresh should call Get Model for the selected voice");
                    var headers = AssertHeaders(http.LastGetOptions, "Startup voice refresh should send Authorization headers");
                    AssertEqual($"Bearer {(string.IsNullOrWhiteSpace(apiKey) ? "dummy" : apiKey)}", headers["Authorization"], "Startup voice refresh should use the configured key without TTS validation");
                    AssertEqual("Fresh Voice", settings.CachedVoice?.Title, "Startup voice refresh should update cached voice title");
                    AssertEqual("Fresh description", settings.CachedVoice?.Description, "Startup voice refresh should update cached voice description");
                    AssertEqual("fresh-cover", settings.CachedVoice?.CoverImage, "Startup voice refresh should update cached voice image");
                    AssertEqual("Fresh Author", settings.CachedVoice?.AuthorName, "Startup voice refresh should update cached voice author");
                    AssertEqual(42, settings.CachedVoice?.TaskCount, "Startup voice refresh should update cached voice task count");
                    AssertEqual("https://audio.example/fresh.mp3", settings.CachedVoice?.SampleAudioUrl, "Startup voice refresh should update cached sample URL");
                    AssertEqual(1, proxy.SaveCount, "Successful startup voice refresh should save updated cached voice");
                });

                network.Set(false);
                settings = new Settings
                {
                    VoiceId = "0123456789abcdef0123456789abcdef",
                    CachedVoice = new CachedVoiceInfo { Title = "Keep" },
                };
                (httpService, http) = TestHttpServiceProxy.Create();
                plugin = new Main();
                var owner2 = new PublishedInitializationFixtureOwner { Plugin = plugin };
                await owner2.RunAsync(async () =>
                {
                    plugin.Init(CreateContext(settings: settings, httpService: httpService), FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
                    owner2.CapturePublishedWork();
                    await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);

                    AssertEqual(0, http.GetCallCount, "Startup voice refresh should skip server call when offline");
                    AssertEqual("Keep", settings.CachedVoice?.Title, "Skipped startup voice refresh should preserve cached voice");
                });
            }
        });

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "lifecycle.initialization/startup-metadata-rejects-changed-credentials")]
    [NUnit.Framework.Order(136)]
    public static async Task StartupMetadataRejectsChangedCredentialsAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        foreach (var returnToOriginal in new[] { false, true })
        {
            var settings = new Settings { ApiKey = "first-startup-account", VoiceId = "fedcba9876543210fedcba9876543210",
                CachedVoice = new() { Title = "Retained startup snapshot" } };
            var (service, http) = TestHttpServiceProxy.Create();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            http.GetAsyncHandler = (url, _, _) =>
            {
                if (url == FishAudioClock.TimeApiUrl) return Task.FromResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
                started.TrySetResult();
                return response.Task;
            };
            Settings? runtime = null;
            using var plugin = new Main((context, candidate, lease, cycle) =>
            {
                runtime = candidate;
                return new SettingsViewModel(context, candidate, null, null, startupCreditRefreshCycle: cycle, settingsWriteLease: lease);
            });
            var context = CreateContext(settings: settings, httpService: service);
            plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
            var view = plugin.GetOrCreateSettingsViewModel();
            await started.Task.WaitAsync(RegressionExecution.Timeout);
            view.ApiKey = "next-startup-account";
            if (returnToOriginal) view.ApiKey = "first-startup-account";
            var saves = ((ContextProxy)(object)context).SaveCount;
            response.SetResult("{\"_id\":\"fedcba9876543210fedcba9876543210\",\"title\":\"Late private voice\"}");
            await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
            AssertEqual("Retained startup snapshot", runtime!.CachedVoice?.Title, "Startup cannot overwrite retained metadata captured under a replaced credential identity, including A to B to A");
            AssertEqual("Retained startup snapshot", view.CachedVoiceTitle, "Late startup metadata cannot alter the preserved selected display");
            AssertEqual(saves, ((ContextProxy)(object)context).SaveCount, "Late startup metadata cannot write storage after any accepted credential change");
        }
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
        await requestStarted.Task.WaitAsync(RegressionExecution.Timeout);
        SettingsViewModel.UiThreadInvokerOverride = action => queuedUiApply.TrySetResult(action);
        try
        {
            releaseResponse.SetResult($$"""
                {"_id":"{{requestedVoiceId}}","title":"Voice A (fresh)","description":"fresh response","cover_image":"","samples":[{"audio":"https://audio.example/voice-a.mp3"}],"task_count":1}
                """);
            await startupVoiceSaved.Task.WaitAsync(RegressionExecution.Timeout);
            var applyStartupVoiceToUi = await queuedUiApply.Task.WaitAsync(RegressionExecution.Timeout);

            var replacementCard = new VoiceSearchItem
            {
                Id = replacementVoiceId,
                Title = "Voice B",
                Description = "Replacement voice",
                SampleAudioUrl = "https://audio.example/voice-b.mp3",
            };
            viewModel.SearchResults = [replacementCard];
            viewModel.SelectVoiceCommand.Execute(replacementCard);
            applyStartupVoiceToUi();
            await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
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
        await requestStarted.Task.WaitAsync(RegressionExecution.Timeout);
        viewModel.ClearVoiceCommand.Execute(null);

        releaseResponse.SetResult($$"""
            {"_id":"{{requestedVoiceId}}","title":"Voice A (fresh)","description":"stale response","cover_image":"","samples":[{"audio":"https://audio.example/voice-a.mp3"}],"task_count":1}
            """);
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
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
        await requestStarted.Task.WaitAsync(RegressionExecution.Timeout);
        var replacementCard = new VoiceSearchItem
        {
            Id = replacementVoiceId,
            Title = "Voice B",
            Description = "Replacement voice",
            SampleAudioUrl = "https://audio.example/voice-b.mp3",
        };
        viewModel.SearchResults = [replacementCard];
        viewModel.SelectVoiceCommand.Execute(replacementCard);

        releaseResponse.SetResult($$"""
            {"_id":"{{requestedVoiceId}}","title":"Voice A (fresh)","description":"stale response","cover_image":"","samples":[{"audio":"https://audio.example/voice-a.mp3"}],"task_count":1}
            """);
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
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
                releaseRevision.Task.WaitAsync(RegressionExecution.Timeout).GetAwaiter().GetResult();
            });

        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var viewModel = plugin.GetOrCreateSettingsViewModel();
        releaseOnlineUtc.SetResult("{\"dateTime\":\"2026-12-01T00:00:00Z\"}");
        await revisionPublished.Task.WaitAsync(RegressionExecution.Timeout);

        viewModel.SelectedModel = FishAudioModelPolicy.S2ProModel;
        AssertEqual(1, contextProxy.SaveCount, "Selecting a paid model while startup is reserved should save the user choice once");
        releaseRevision.SetResult();
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);

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
        releaseOnlineUtc.SetResult("{\"dateTime\":\"2026-12-01T00:00:00Z\"}");
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);

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
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);

        AssertEqual("Old Voice", runtimeSettings?.CachedVoice?.Title, "A failed startup voice save should roll back runtime cached metadata");
        AssertEqual("Old Voice", viewModel.CachedVoiceTitle, "A failed startup voice save should leave the existing ViewModel metadata unchanged");
        AssertEqual("Old Voice", contextProxy.Settings.CachedVoice?.Title, "A failed startup voice save should roll back the host-owned cached metadata");
        AssertEqual("Old Voice", backingStore.Load().CachedVoice?.Title, "A failed startup voice save should preserve cached metadata in backing storage");

        var acceptedApiKey = viewModel.ApiKey;
        viewModel.Speed = 1.2;
        var savedSnapshot = backingStore.Load();
        AssertEqual(2, contextProxy.SaveCount, "A later ViewModel edit should still save after startup voice rollback");
        AssertEqual(1.2, savedSnapshot.Speed, "A later independent ViewModel edit should persist after startup voice rollback");
        AssertEqual(acceptedApiKey, savedSnapshot.ApiKey, "An independent later save must preserve the accepted API Key");
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
        await constructionSnapshotCaptured.Task.WaitAsync(RegressionExecution.Timeout);

        releaseOnlineUtc.SetResult("{\"dateTime\":\"2026-12-01T00:00:00Z\"}");
        await startupModelSaved.Task.WaitAsync(RegressionExecution.Timeout);
        releaseConstruction.SetResult();

        var viewModel = await viewModelTask.WaitAsync(RegressionExecution.Timeout);
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);

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
                releaseRevision.Task.WaitAsync(RegressionExecution.Timeout).GetAwaiter().GetResult();
            });

        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        releaseOnlineUtc.SetResult("{\"dateTime\":\"2026-12-01T00:00:00Z\"}");
        await revisionPublished.Task.WaitAsync(RegressionExecution.Timeout);

        var viewModelTask = Task.Run(plugin.GetOrCreateSettingsViewModel);
        await constructionSnapshotCaptured.Task.WaitAsync(RegressionExecution.Timeout);
        releaseRevision.SetResult();
        await startupModelSaved.Task.WaitAsync(RegressionExecution.Timeout);
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
        releaseConstruction.SetResult();

        var viewModel = await viewModelTask.WaitAsync(RegressionExecution.Timeout);

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
        await constructionSnapshotCaptured.Task.WaitAsync(RegressionExecution.Timeout);

        releaseOnlineUtc.SetResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
        await startupVoiceSaved.Task.WaitAsync(RegressionExecution.Timeout);
        releaseConstruction.SetResult();

        var viewModel = await viewModelTask.WaitAsync(RegressionExecution.Timeout);
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);

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
                releaseRevision.Task.WaitAsync(RegressionExecution.Timeout).GetAwaiter().GetResult();
            });

        plugin.Init(oldContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var oldStartupTask = plugin.PendingStartupTask;
        releaseOldOnlineUtc.SetResult("{\"dateTime\":\"2026-12-01T00:00:00Z\"}");
        await revisionPublished.Task.WaitAsync(RegressionExecution.Timeout);

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
            releaseNewLoad.Task.WaitAsync(RegressionExecution.Timeout).GetAwaiter().GetResult();
            return snapshot;
        };
        newContextProxy.SaveSettings = backingStore.Save;

        var reinitializeTask = Task.Run(() =>
            plugin.Init(newContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1)));
        await newLoadStarted.Task.WaitAsync(RegressionExecution.Timeout);

        releaseRevision.SetResult();
        var staleSaveCompletedBeforeLoad = ReferenceEquals(
            await Task.WhenAny(oldSaveCompleted.Task, Task.Delay(TimeSpan.FromMilliseconds(500))),
            oldSaveCompleted.Task);
        releaseNewLoad.SetResult();

        await reinitializeTask.WaitAsync(RegressionExecution.Timeout);
        await oldStartupTask.WaitAsync(RegressionExecution.Timeout);
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);
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
            releaseOldSave.Task.WaitAsync(RegressionExecution.Timeout).GetAwaiter().GetResult();
            backingStore.Save(settings);
        };
        var plugin = new Main();

        plugin.Init(oldContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var oldStartupTask = plugin.PendingStartupTask;
        releaseOldOnlineUtc.SetResult("{\"dateTime\":\"2026-07-23T00:00:00Z\"}");
        await oldSaveStarted.Task.WaitAsync(RegressionExecution.Timeout);

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
        await reinitializeTask.WaitAsync(RegressionExecution.Timeout);
        await oldStartupTask.WaitAsync(RegressionExecution.Timeout);
        await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);

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
                releaseRevision.Task.WaitAsync(RegressionExecution.Timeout).GetAwaiter().GetResult();
            });

        plugin.Init(oldContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        var oldStartupTask = plugin.PendingStartupTask;
        var oldViewModel = plugin.GetOrCreateSettingsViewModel();
        releaseOldOnlineUtc.SetResult("{\"dateTime\":\"2026-12-01T00:00:00Z\"}");
        await revisionPublished.Task.WaitAsync(RegressionExecution.Timeout);

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
        await oldStartupTask.WaitAsync(RegressionExecution.Timeout);

        AssertEqual(true, loadFailed, "The replacement Load failure should remain observable");
        AssertEqual(1, oldContextProxy.SaveCount, "A failed replacement should restore an already-revised startup settings save");
        AssertEqual(FishAudioModelPolicy.S21ProModel, oldViewModel.SelectedModel, "A failed replacement should let the old ViewModel apply its authorized startup revision");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "lifecycle.initialization/startup-refresh-dispose-cancels-pending-work-without-logging")]
    [NUnit.Framework.Property("OriginalOrdinal", 150)]
    [NUnit.Framework.Order(150)]
    public static Task StartupRefreshDisposeCancelsPendingWorkWithoutLoggingAsync() =>
        RunStartupRefreshDisposeCancelsPendingWorkWithoutLoggingAsync();

    internal static async Task RunStartupRefreshDisposeCancelsPendingWorkWithoutLoggingAsync(
        Action<NUnitCompatibility.PublishedInitializationObservation>? onAcquired = null)
    {
        using var network = OverrideNetworkAvailability(true);
        var owner = new PublishedInitializationFixtureOwner();
        await owner.RunAsync(async () =>
        {
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
            owner.Plugin = plugin;

            plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
            owner.CapturePublishedWork();
            await requestStarted.Task.WaitAsync(RegressionExecution.Timeout);
            onAcquired?.Invoke(owner.Observe(requestStarted: requestStarted.Task, requestCanceled: requestCanceled.Task));

            var startupTask = plugin.PendingStartupTask;
            plugin.Dispose();

            await requestCanceled.Task.WaitAsync(RegressionExecution.Timeout);
            await startupTask.WaitAsync(RegressionExecution.Timeout);

            AssertEqual("Keep", settings.CachedVoice?.Title, "Canceled startup refresh should preserve cached voice");
            AssertEqual(0, proxy.SaveCount, "Canceled startup refresh should not save settings");
            AssertEqual(false, logger.Contains("startup refresh failed"), "Canceled startup refresh should not log a failure warning");
        });
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
        var startupTaskBeforeDispose = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
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

            startupTaskBeforeDispose.SetResult(plugin.PendingStartupTask);
            plugin.Dispose();
            return Task.FromResult("{\"_id\":\"fedcba9876543210fedcba9876543210\",\"title\":\"Should Not Apply\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}");
        };

        plugin.Init(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        await startupTaskBeforeDispose.Task.Unwrap().WaitAsync(RegressionExecution.Timeout);

        AssertEqual("Keep", settings.CachedVoice?.Title, "Startup cancellation after model response should skip cached voice side effects");
        AssertEqual(0, proxy.SaveCount, "Startup cancellation after model response should not save settings");
        AssertEqual(false, logger.Contains("startup refresh failed"), "Startup cancellation after model response should not log failure");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "lifecycle.initialization/reinitialized-startup-refresh-cannot-mutate-new-settings")]
    [NUnit.Framework.Property("OriginalOrdinal", 152)]
    [NUnit.Framework.Order(152)]
    public static Task ReinitializedStartupRefreshCannotMutateNewSettingsAsync() =>
        RunReinitializedStartupRefreshCannotMutateNewSettingsAsync();

    internal static async Task RunReinitializedStartupRefreshCannotMutateNewSettingsAsync(
        Action<NUnitCompatibility.PublishedInitializationObservation>? onAcquired = null)
    {
        using var network = OverrideNetworkAvailability(true);
        var owner = new PublishedInitializationFixtureOwner();
        await owner.RunAsync(async () =>
        {
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
            owner.ReleaseHeldResponse = () => oldRelease.TrySetResult("{\"_id\":\"11111111111111111111111111111111\",\"title\":\"Old Should Not Leak\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}");
            var (oldHttpService, oldHttp) = TestHttpServiceProxy.Create();
            oldHttp.GetAsyncHandler = (_, _, _) => oldRelease.Task;
            var oldContext = CreateContext(settings: oldSettings, httpService: oldHttpService);
            var oldProxy = (ContextProxy)(object)oldContext;

            var (newHttpService, newHttp) = TestHttpServiceProxy.Create();
            newHttp.GetResponseJson = "{\"dateTime\":\"2026-06-27T00:00:00Z\"}";
            var newContext = CreateContext(settings: newSettings, httpService: newHttpService);
            var newProxy = (ContextProxy)(object)newContext;
            var plugin = new Main();
            owner.Plugin = plugin;

            plugin.Init(oldContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
            var oldStartupTask = plugin.PendingStartupTask;
            owner.CapturePublishedWork();
            onAcquired?.Invoke(owner.Observe(response: oldRelease.Task));
            plugin.Init(newContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
            owner.CapturePublishedWork();
            onAcquired?.Invoke(owner.Observe(response: oldRelease.Task));
            oldRelease.SetResult("{\"_id\":\"11111111111111111111111111111111\",\"title\":\"Old Should Not Leak\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}");
            await oldStartupTask.WaitAsync(RegressionExecution.Timeout);
            await plugin.PendingStartupTask.WaitAsync(RegressionExecution.Timeout);

            AssertEqual("Old Keep", oldSettings.CachedVoice?.Title, "Canceled old startup refresh should not mutate old settings after reinitialization");
            AssertEqual("New Keep", newSettings.CachedVoice?.Title, "Canceled old startup refresh should not mutate new settings after reinitialization");
            AssertEqual(0, oldProxy.SaveCount, "Canceled old startup refresh should not save old context after reinitialization");
            AssertEqual(0, newProxy.SaveCount, "Canceled old startup refresh should not save new context after reinitialization");
        });
    }

    internal static void FinishWriteLeaseOwners(
        Settings? firstSettings, SettingsWriteLease firstWriteLease, SettingsViewModel? oldViewModel,
        Settings? secondSettings, SettingsWriteLease secondWriteLease, SettingsViewModel? newViewModel,
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? primaryFailure)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? cleanupFailure = null;
        try
        {
            if (secondSettings is not null && secondWriteLease.IsValid
                && secondSettings.ActiveWriteLease == secondWriteLease.Value)
                SettingsStore.Retire(secondSettings, secondWriteLease);
        }
        catch (Exception error) { cleanupFailure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
        try { newViewModel?.Dispose(); }
        catch (Exception error) { cleanupFailure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
        try
        {
            // The original scenario already retires the first lease before its
            // second Load. Only a failure before that boundary leaves it active.
            if (firstSettings is not null && firstWriteLease.IsValid
                && firstSettings.ActiveWriteLease == firstWriteLease.Value)
                SettingsStore.Retire(firstSettings, firstWriteLease);
        }
        catch (Exception error) { cleanupFailure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
        try { oldViewModel?.Dispose(); }
        catch (Exception error) { cleanupFailure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
        primaryFailure?.Throw();
        cleanupFailure?.Throw();
    }
}
