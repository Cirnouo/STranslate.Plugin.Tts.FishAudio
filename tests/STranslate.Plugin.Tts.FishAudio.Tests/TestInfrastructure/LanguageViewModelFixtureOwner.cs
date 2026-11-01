using STranslate.Plugin.Tts.FishAudio.ViewModel;

internal sealed class LanguageViewModelFixtureHooks
{
    internal Action<SettingsViewModel>? ViewModelAcquired;
    internal Action<SettingsViewModel>? ViewModelDisposed;
}

// Only the two non-native language callbacks use this owner. Each returned VM
// is retained before view construction; no native or async command is started.
internal sealed class LanguageViewModelFixtureOwner(LanguageViewModelFixtureHooks? hooks = null)
{
    private SettingsViewModel? _viewModel;

    internal Task RunAsync(Action<LanguageViewModelFixtureOwner> body) =>
        RegressionFixtureOwner.RunAsync(() =>
        {
            body(this);
            return Task.CompletedTask;
        }, DisposeViewModelAsync);

    internal SettingsViewModel Acquire(Func<SettingsViewModel> acquire)
    {
        if (_viewModel is not null) throw new InvalidOperationException("language-vm-already-owned");
        _viewModel = acquire();
        hooks?.ViewModelAcquired?.Invoke(_viewModel);
        return _viewModel;
    }

    private Task DisposeViewModelAsync()
    {
        if (_viewModel is not null)
        {
            _viewModel.Dispose();
            hooks?.ViewModelDisposed?.Invoke(_viewModel);
        }
        return Task.CompletedTask;
    }
}
