using Microsoft.Extensions.Logging;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Presentation;
using STranslate.Plugin.Tts.FishAudio.Runtime;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using System.Windows.Controls;

namespace STranslate.Plugin.Tts.FishAudio.Lifecycle;

internal sealed class PluginInitializationCoordinator : IDisposable
{
    private readonly object _persistenceGate = new();
    private readonly object _stateGate = new();
    private readonly Func<IPluginContext, Settings, SettingsWriteLease, StartupCreditRefreshCycle, SettingsViewModel> _viewModelFactory;
    private readonly Action<long>? _initializationGenerationDeclared;
    private readonly Action? _startupSettingsRevisionPublished;
    private readonly Action<long>? _initializationPublished;
    private Control? _settingUi;
    private InitializationState? _initializationState;
    private long _initializationGeneration;

    internal PluginInitializationCoordinator(
        Func<IPluginContext, Settings, SettingsWriteLease, StartupCreditRefreshCycle, SettingsViewModel> viewModelFactory,
        Action? startupSettingsRevisionPublished = null,
        Action<long>? initializationGenerationDeclared = null,
        Action<long>? initializationPublished = null)
    {
        _viewModelFactory = viewModelFactory;
        _startupSettingsRevisionPublished = startupSettingsRevisionPublished;
        _initializationGenerationDeclared = initializationGenerationDeclared;
        _initializationPublished = initializationPublished;
    }

    internal long InitializationGeneration
    {
        get
        {
            lock (_stateGate)
                return _initializationGeneration;
        }
    }

    internal Task PendingStartupTask
    {
        get
        {
            lock (_stateGate)
                return _initializationState?.PendingStartupTask ?? Task.CompletedTask;
        }
    }

    internal Control GetSettingUI()
    {
        while (true)
        {
            _ = GetOrCreateSettingsViewModel();
            Control? settingUi;
            lock (_stateGate)
                settingUi = _settingUi;

            if (settingUi is null)
            {
                var candidate = new SettingsView();
                lock (_stateGate)
                {
                    _settingUi ??= candidate;
                    settingUi = _settingUi;
                }
            }

            SynchronizeSettingUiDataContext();
            return settingUi;
        }
    }

    internal SettingsViewModel GetOrCreateSettingsViewModel()
    {
        while (true)
        {
            InitializationState state;
            lock (_stateGate)
            {
                state = GetRequiredInitializationState();
                if (state.ViewModel is not null)
                    return state.ViewModel;
            }

            var viewModel = CreateSettingsViewModel(state);
            var published = false;
            lock (_persistenceGate)
            {
                lock (_stateGate)
                {
                    if (ReferenceEquals(_initializationState, state))
                    {
                        _initializationState = state with { ViewModel = viewModel };
                        published = true;
                    }
                }
            }

            if (!published)
            {
                viewModel.Dispose();
                continue;
            }

            SynchronizeSettingUiDataContext();
            lock (_stateGate)
            {
                if (ReferenceEquals(_initializationState?.ViewModel, viewModel))
                    return viewModel;
            }
        }
    }

    internal void Init(IPluginContext context)
    {
        Init(context, FishAudioClock.LocalUtcNow());
    }

    internal void Init(IPluginContext context, DateTimeOffset nowUtc)
    {
        long generation;
        lock (_stateGate)
            generation = ++_initializationGeneration;

        _initializationGenerationDeclared?.Invoke(generation);

        InitializationState? preparedState = null;
        InitializationState? previousState = null;
        TaskCompletionSource? publicationCompletion = null;
        var published = false;
        try
        {
            lock (_persistenceGate)
            {
                InitializationState? transitionState;
                bool shouldRecreateViewModel;
                lock (_stateGate)
                {
                    if (generation != _initializationGeneration)
                        return;

                    transitionState = _initializationState;
                    shouldRecreateViewModel = transitionState?.ViewModel is not null || _settingUi is not null;
                }

                if (transitionState is not null)
                    SettingsStore.Retire(transitionState.Settings, transitionState.SettingsWriteLease);

                Settings? replacementSettings = null;
                var replacementWriteLease = default(SettingsWriteLease);
                try
                {
                    replacementSettings = SettingsStore.Load(context, nowUtc, out replacementWriteLease);
                    var audioPlayer = context.AudioPlayer;
                    var cancellationTokenSource = new CancellationTokenSource();
                    var cancellationToken = cancellationTokenSource.Token;
                    var startupCreditRefreshCycle = StartupCreditRefreshCycle.Start(context, replacementSettings, cancellationToken);
                    var identity = new object();
                    publicationCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var metadataRefreshTask = RunStartupRefreshAsync(
                        publicationCompletion.Task,
                        identity,
                        context,
                        replacementSettings,
                        replacementWriteLease,
                        cancellationToken);
                    var pendingStartupTask = Task.WhenAll(metadataRefreshTask, startupCreditRefreshCycle.Completion);
                    preparedState = new InitializationState(
                        identity,
                        generation,
                        SettingsRevision: 0,
                        context,
                        replacementSettings,
                        replacementWriteLease,
                        audioPlayer,
                        ViewModel: null,
                        cancellationTokenSource,
                        startupCreditRefreshCycle,
                        pendingStartupTask);

                    if (shouldRecreateViewModel)
                        preparedState = preparedState with { ViewModel = CreateSettingsViewModel(preparedState) };

                    var settingsCommitted = false;
                    while (true)
                    {
                        var needsViewModel = false;
                        lock (_stateGate)
                        {
                            needsViewModel = preparedState.ViewModel is null
                                && (_initializationState?.ViewModel is not null || _settingUi is not null);
                        }

                        if (needsViewModel)
                        {
                            preparedState = preparedState with { ViewModel = CreateSettingsViewModel(preparedState) };
                            continue;
                        }

                        if (!settingsCommitted)
                        {
                            SettingsStore.Commit(context, replacementSettings, replacementWriteLease);
                            settingsCommitted = true;
                        }

                        lock (_stateGate)
                        {
                            needsViewModel = preparedState.ViewModel is null
                                && (_initializationState?.ViewModel is not null || _settingUi is not null);
                            if (!needsViewModel)
                            {
                                previousState = _initializationState;
                                previousState?.StartupCreditRefreshCycle.Invalidate();
                                _initializationState = preparedState;
                                published = true;
                                break;
                            }
                        }
                    }

                    if (!published)
                    {
                        SettingsStore.Restore(
                            transitionState?.Settings,
                            transitionState?.SettingsWriteLease ?? default,
                            replacementSettings,
                            replacementWriteLease);
                    }
                }
                catch
                {
                    SettingsStore.Restore(
                        transitionState?.Settings,
                        transitionState?.SettingsWriteLease ?? default,
                        replacementSettings,
                        replacementWriteLease);
                    throw;
                }
            }

            if (!published)
            {
                RetireInitializationState(preparedState);
                publicationCompletion?.TrySetCanceled();
                return;
            }

            _initializationPublished?.Invoke(preparedState!.Generation);
            publicationCompletion!.TrySetResult();
            try
            {
                RetireInitializationState(previousState);
            }
            finally
            {
                SynchronizeSettingUiDataContext();
            }
        }
        catch
        {
            if (!published)
            {
                RetireInitializationState(preparedState);
                publicationCompletion?.TrySetCanceled();
            }

            throw;
        }
    }

    internal OperationSnapshot CaptureOperationSnapshot()
    {
        lock (_stateGate)
        {
            var state = GetRequiredInitializationState();
            return new OperationSnapshot(
                state.Context,
                state.Settings,
                state.ViewModel,
                state.AudioPlayer);
        }
    }

    public void Dispose()
    {
        InitializationState? state;
        lock (_persistenceGate)
        {
            lock (_stateGate)
            {
                _initializationGeneration++;
                state = _initializationState;
                state?.StartupCreditRefreshCycle.Invalidate();
                _initializationState = null;
            }

            if (state is not null)
                SettingsStore.Retire(state.Settings, state.SettingsWriteLease);
        }

        try
        {
            RetireInitializationState(state);
        }
        finally
        {
            SynchronizeSettingUiDataContext();
        }
    }

    private SettingsViewModel CreateSettingsViewModel(InitializationState state) =>
        _viewModelFactory(
            state.Context,
            state.Settings,
            state.SettingsWriteLease,
            state.StartupCreditRefreshCycle);

    private async Task RunStartupRefreshAsync(
        Task publicationTask,
        object identity,
        IPluginContext context,
        Settings settings,
        SettingsWriteLease settingsWriteLease,
        CancellationToken cancellationToken)
    {
        try
        {
            var onlineUtc = await FishAudioClock.TryGetOnlineUtcNowAsync(
                context,
                FishAudioRequestPolicy.IsNetworkAvailable,
                cancellationToken);
            await publicationTask.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedModel = false;
            var settingsRevision = 0L;
            var currentOnlineUtc = onlineUtc.GetValueOrDefault();
            if (onlineUtc.HasValue
                && !TryReserveCurrentSettingsRevision(
                    identity,
                    settings,
                    settingsWriteLease,
                    () => SettingsNormalizer.NeedsSelectedModelNormalization(settings, currentOnlineUtc),
                    out normalizedModel,
                    out settingsRevision))
            {
                return;
            }

            if (normalizedModel)
            {
                _startupSettingsRevisionPublished?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
                if (!TrySaveStartupSettings(
                        identity,
                        settingsRevision,
                        context,
                        settings,
                        settingsWriteLease,
                        candidate =>
                        {
                            SettingsNormalizer.NormalizeSelectedModel(candidate, currentOnlineUtc);
                            return true;
                        }))
                {
                    return;
                }

                if (TryGetCurrentViewModel(identity, out var viewModel))
                    viewModel?.ApplyAvailableModels(currentOnlineUtc);
            }

            await RefreshSelectedVoiceMetadataOnStartupAsync(
                identity,
                context,
                settings,
                settingsWriteLease,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Logger?.LogWarning(ex, "Fish Audio startup refresh failed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshSelectedVoiceMetadataOnStartupAsync(
        object identity,
        IPluginContext context,
        Settings settings,
        SettingsWriteLease settingsWriteLease,
        CancellationToken cancellationToken)
    {
        var requestedVoiceId = settings.VoiceId;
        if (string.IsNullOrWhiteSpace(requestedVoiceId) || !SettingsValidation.IsValidVoiceIdFormat(requestedVoiceId))
            return;

        if (!FishAudioRequestPolicy.IsNetworkAvailable())
        {
            context.Logger?.LogWarning("Startup voice refresh skipped: Network unavailable");
            return;
        }

        try
        {
            var model = await FishAudioApi.GetModelAsync(context, "dummy", requestedVoiceId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (model is null)
                return;

            var cached = SettingsViewModel.CreateCachedVoiceInfo(model);
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryReserveCurrentSettingsRevision(
                    identity,
                    settings,
                    settingsWriteLease,
                    () => true,
                    out _,
                    out var settingsRevision))
            {
                return;
            }

            _startupSettingsRevisionPublished?.Invoke();
            if (!TrySaveStartupSettings(
                    identity,
                    settingsRevision,
                    context,
                    settings,
                    settingsWriteLease,
                    candidate =>
                    {
                        if (!string.Equals(candidate.VoiceId, requestedVoiceId, StringComparison.Ordinal))
                            return false;

                        candidate.CachedVoice = cached;
                        return true;
                    }))
            {
                return;
            }

            if (TryGetCurrentViewModel(identity, out var viewModel))
                viewModel?.ApplyRefreshedCachedVoice(requestedVoiceId, cached);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Logger?.LogWarning(ex, "Startup voice refresh failed");
        }
    }

    private InitializationState GetRequiredInitializationState() =>
        _initializationState
        ?? throw new InvalidOperationException("The Fish Audio plugin has not been initialized.");

    private bool TryGetCurrentViewModel(object identity, out SettingsViewModel? viewModel)
    {
        lock (_stateGate)
        {
            if (!ReferenceEquals(_initializationState?.Identity, identity))
            {
                viewModel = null;
                return false;
            }

            viewModel = _initializationState.ViewModel;
            return true;
        }
    }

    private bool TryReserveCurrentSettingsRevision(
        object identity,
        Settings settings,
        SettingsWriteLease settingsWriteLease,
        Func<bool> shouldRevise,
        out bool revised,
        out long settingsRevision)
    {
        var isCurrent = false;
        var localRevised = false;
        var localSettingsRevision = 0L;
        var leaseIsActive = SettingsStore.TryUpdate(
            settings,
            settingsWriteLease,
            () =>
            {
                lock (_stateGate)
                {
                    var state = _initializationState;
                    if (!ReferenceEquals(state?.Identity, identity)
                        || !ReferenceEquals(state.Settings, settings)
                        || state.SettingsWriteLease != settingsWriteLease)
                    {
                        return;
                    }

                    isCurrent = true;
                    localRevised = shouldRevise();
                    if (localRevised)
                    {
                        localSettingsRevision = state.SettingsRevision + 1;
                        _initializationState = state with { SettingsRevision = localSettingsRevision };
                    }
                    else
                    {
                        localSettingsRevision = state.SettingsRevision;
                    }
                }
            });

        revised = localRevised;
        settingsRevision = localSettingsRevision;
        return leaseIsActive && isCurrent;
    }

    private bool TrySaveStartupSettings(
        object identity,
        long settingsRevision,
        IPluginContext context,
        Settings settings,
        SettingsWriteLease settingsWriteLease,
        Func<Settings, bool> update)
    {
        lock (_persistenceGate)
        {
            lock (_stateGate)
            {
                var state = _initializationState;
                if (!ReferenceEquals(state?.Identity, identity)
                    || !ReferenceEquals(state.Settings, settings)
                    || state.SettingsWriteLease != settingsWriteLease
                    || state.SettingsRevision != settingsRevision)
                {
                    return false;
                }
            }

            try
            {
                var saved = SettingsStore.TryUpdateAndSave(
                    context,
                    settings,
                    settingsWriteLease,
                    update);
                if (saved)
                    RotateCurrentSettingsPublicationCredential(identity, settingsRevision, settings, settingsWriteLease);
                else
                    RollBackCurrentSettingsRevision(identity, settingsRevision, settings, settingsWriteLease);

                return saved;
            }
            catch
            {
                RollBackCurrentSettingsRevision(identity, settingsRevision, settings, settingsWriteLease);
                throw;
            }
        }
    }

    private void RotateCurrentSettingsPublicationCredential(
        object identity,
        long settingsRevision,
        Settings settings,
        SettingsWriteLease settingsWriteLease)
    {
        lock (_stateGate)
        {
            var state = _initializationState;
            if (ReferenceEquals(state?.Identity, identity)
                && ReferenceEquals(state.Settings, settings)
                && state.SettingsWriteLease == settingsWriteLease
                && state.SettingsRevision == settingsRevision)
            {
                _initializationState = state with { };
            }
        }
    }

    private void RollBackCurrentSettingsRevision(
        object identity,
        long settingsRevision,
        Settings settings,
        SettingsWriteLease settingsWriteLease)
    {
        lock (_stateGate)
        {
            var state = _initializationState;
            if (ReferenceEquals(state?.Identity, identity)
                && ReferenceEquals(state.Settings, settings)
                && state.SettingsWriteLease == settingsWriteLease
                && state.SettingsRevision == settingsRevision)
            {
                _initializationState = state with { SettingsRevision = settingsRevision - 1 };
            }
        }
    }

    private void SynchronizeSettingUiDataContext()
    {
        while (true)
        {
            Control? settingUi;
            object? identity;
            SettingsViewModel? viewModel;
            lock (_stateGate)
            {
                settingUi = _settingUi;
                if (settingUi is null)
                    return;

                identity = _initializationState?.Identity;
                viewModel = _initializationState?.ViewModel;
            }

            settingUi.DataContext = viewModel;

            lock (_stateGate)
            {
                if (ReferenceEquals(_settingUi, settingUi)
                    && ReferenceEquals(_initializationState?.Identity, identity)
                    && ReferenceEquals(_initializationState?.ViewModel, viewModel))
                {
                    return;
                }
            }
        }
    }

    private static void RetireInitializationState(InitializationState? state)
    {
        if (state is null)
            return;

        try
        {
            state.ViewModel?.Dispose();
        }
        finally
        {
            try
            {
                state.StartupRefreshCancellationTokenSource.Cancel();
            }
            finally
            {
                _ = state.PendingStartupTask.ContinueWith(
                    _ => state.StartupRefreshCancellationTokenSource.Dispose(),
                    TaskScheduler.Default);
            }
        }
    }

    internal sealed record OperationSnapshot(
        IPluginContext Context,
        Settings Settings,
        SettingsViewModel? ViewModel,
        IAudioPlayer AudioPlayer);

    private sealed record InitializationState(
        object Identity,
        long Generation,
        long SettingsRevision,
        IPluginContext Context,
        Settings Settings,
        SettingsWriteLease SettingsWriteLease,
        IAudioPlayer AudioPlayer,
        SettingsViewModel? ViewModel,
        CancellationTokenSource StartupRefreshCancellationTokenSource,
        StartupCreditRefreshCycle StartupCreditRefreshCycle,
        Task PendingStartupTask);
}
