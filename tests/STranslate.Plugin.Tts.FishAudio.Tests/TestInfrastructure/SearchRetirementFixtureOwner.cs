using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

internal sealed class SearchRetirementFixtureHooks
{
    internal Action<SearchRetirementPopupProbe>? Opened;
    internal Action<string, SearchRetirementPopupProbe>? Checkpoint;
    internal Action<SearchRetirementFixtureOwner>? HostRetiring;
    internal Action<SearchRetirementFixtureOwner>? HostRetired;
}

internal sealed record SearchRetirementPopupProbe(string Name, Popup Popup,
    FrameworkElement Control, NativePopupLifetimeOwner Lifetime, SearchRetirementFixtureOwner Owner);

// The overlay handoff can temporarily retain two different native Popup sources.
// Every open has its own existing lifetime observer; no scene is split or replayed.
internal sealed class SearchRetirementFixtureOwner(SearchRetirementFixtureHooks? hooks = null)
{
    private readonly List<SearchRetirementPopupProbe> _popups = [];
    internal IReadOnlyList<SearchRetirementPopupProbe> Popups => _popups;
    internal SettingsViewModel? ViewModel;
    internal HwndSource? Source;
    internal NativeWindowLifetimeOwner? WindowLifetime;
    internal string? ProofDirectory;
    internal HwndSource? HostSource => WindowLifetime?.Source ?? Source;

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(body,
        RequestAllPopupClosesAsync, JoinAllPopupsAsync, DetachAllPopupsAsync,
        ObserveHostRetiringAsync, RetireHostAsync, DetachHostAsync, ObserveHostRetiredAsync, DisposeViewModelAsync);

    internal SearchRetirementPopupProbe Open(string name, Popup popup, FrameworkElement control, Action originalOpen)
    {
        var lifetime = new NativePopupLifetimeOwner(popup);
        var probe = new SearchRetirementPopupProbe(name, popup, control, lifetime, this);
        _popups.Add(probe);
        try
        {
            originalOpen();
            lifetime.RequireOpenedSource();
            hooks?.Opened?.Invoke(probe);
            return probe;
        }
        finally { lifetime.CaptureSource(); }
    }

    internal void Checkpoint(string name, SearchRetirementPopupProbe probe) => hooks?.Checkpoint?.Invoke(name, probe);

    internal static async Task JoinAsync(SearchRetirementPopupProbe probe)
    {
        // An earlier lifetime may share the Popup object with a later source.
        // Never recapture that later source through an already verified owner.
        if (!probe.Lifetime.ClosureVerified) await probe.Lifetime.CloseAndJoinAsync();
        probe.Lifetime.Detach();
    }

    private Task RequestAllPopupClosesAsync() => RegressionFixtureOwner.RunAsync(() => Task.CompletedTask,
        _popups.Select<SearchRetirementPopupProbe, Func<Task>>(probe => () =>
        {
            if (probe.Lifetime.ClosureVerified) return Task.CompletedTask;
            try { if (probe.Popup.IsOpen) probe.Popup.IsOpen = false; }
            finally { probe.Lifetime.RecordCloseSetterReturn(); }
            return Task.CompletedTask;
        }).ToArray());

    private Task JoinAllPopupsAsync() => RegressionFixtureOwner.RunAsync(() => Task.CompletedTask,
        _popups.Select<SearchRetirementPopupProbe, Func<Task>>(probe => () => JoinAsync(probe)).ToArray());

    private Task DetachAllPopupsAsync() => RegressionFixtureOwner.RunAsync(() => Task.CompletedTask,
        _popups.Select<SearchRetirementPopupProbe, Func<Task>>(probe => () =>
        {
            probe.Lifetime.Detach();
            return Task.CompletedTask;
        }).ToArray());

    private Task ObserveHostRetiringAsync() { hooks?.HostRetiring?.Invoke(this); return Task.CompletedTask; }

    private async Task RetireHostAsync()
    {
        if (WindowLifetime is not null)
        {
            // The Window owns this source. Its original Close is the only normal
            // retirement request; no direct source Dispose substitutes for it.
            await WindowLifetime.CloseAndJoinAsync();
        }
        else Source?.Dispose();
    }

    private Task DetachHostAsync() { WindowLifetime?.Detach(); return Task.CompletedTask; }

    private Task ObserveHostRetiredAsync() { hooks?.HostRetired?.Invoke(this); return Task.CompletedTask; }
    private Task DisposeViewModelAsync() { ViewModel?.Dispose(); return Task.CompletedTask; }

    // Independent rescue belongs to controls after their original-work oracles.
    internal Task RescueAsync() => RegressionFixtureOwner.RunAsync(() => Task.CompletedTask,
        RequestAllPopupClosesAsync, JoinAllPopupsAsync, DetachAllPopupsAsync,
        RetireHostAsync, DetachHostAsync, DisposeViewModelAsync);
}
