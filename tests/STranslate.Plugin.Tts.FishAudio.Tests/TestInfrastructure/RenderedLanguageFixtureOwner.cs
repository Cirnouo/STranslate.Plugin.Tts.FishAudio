using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

internal sealed class RenderedLanguageFixtureHooks
{
    internal Action<HwndSource, SettingsViewModel>? SourceAcquired;
    internal Action<RenderedLanguagePopupProbe>? PopupOpened;
    internal Action<RenderedLanguageFixtureOwner>? SourceRetiring;
    internal Action<HwndSource>? SourceDisposed;
    internal Action<SettingsViewModel>? ViewModelDisposed;
}

internal sealed record RenderedLanguagePopupProbe(HwndSource HostSource, SettingsViewModel ViewModel,
    VoiceLanguageTags Tags, NativePopupLifetimeOwner Lifetime, int OpenOrdinal);

// The shared rendered callback has zero or exactly two observed Popups. Fixed
// independent phases preserve its original start-both-before-join ordering and
// retain each cleanup type on the original body exception.
internal sealed class RenderedLanguageFixtureOwner(RenderedLanguageFixtureHooks? hooks = null)
{
    private sealed class Entry(VoiceLanguageTags tags)
    {
        internal readonly VoiceLanguageTags Tags = tags;
        internal readonly NativePopupLifetimeOwner Lifetime = new((Popup)tags.FindName("LanguagePopup"));
        internal Task? Closing;
    }

    internal SettingsViewModel? ViewModel;
    internal HwndSource? Source;
    private readonly List<Entry> _entries = [];
    internal IReadOnlyList<NativePopupLifetimeOwner> Lifetimes => _entries.Select(entry => entry.Lifetime).ToArray();

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(body,
        () => StartCloseAsync(0), () => StartCloseAsync(1),
        () => JoinCloseAsync(0), () => JoinCloseAsync(1),
        () => DetachAsync(0), () => DetachAsync(1),
        ObserveSourceRetiringAsync, DisposeSourceAsync, DisposeViewModelAsync);

    internal HwndSource AcquireSource(Func<HwndSource> acquire)
    {
        if (Source is not null) throw new InvalidOperationException("rendered-language-host-already-owned");
        Source = acquire();
        hooks?.SourceAcquired?.Invoke(Source, ViewModel!);
        return Source;
    }

    internal void Observe(VoiceLanguageTags tags)
    {
        if (_entries.Count == 2) throw new InvalidOperationException("rendered-language-more-than-two-popups");
        _entries.Add(new Entry(tags));
    }

    internal void Open(VoiceLanguageTags tags, Action originalOpen)
    {
        var entry = _entries.Single(value => ReferenceEquals(value.Tags, tags));
        try
        {
            originalOpen();
            entry.Lifetime.RequireOpenedSource();
            hooks?.PopupOpened?.Invoke(new RenderedLanguagePopupProbe(Source!, ViewModel!, tags,
                entry.Lifetime, _entries.IndexOf(entry) + 1));
        }
        finally { entry.Lifetime.CaptureSource(); }
    }

    private Task StartCloseAsync(int index)
    {
        if (_entries.Count <= index) return Task.CompletedTask;
        var entry = _entries[index];
        entry.Closing = entry.Lifetime.CloseAndJoinAsync(() =>
        {
            if (entry.Tags.IsPopupOpen) entry.Tags.ClosePopup();
        });
        return Task.CompletedTask;
    }

    private Task JoinCloseAsync(int index) =>
        _entries.Count <= index ? Task.CompletedTask : _entries[index].Closing ?? Task.CompletedTask;

    private Task DetachAsync(int index)
    {
        if (_entries.Count > index) _entries[index].Lifetime.Detach();
        return Task.CompletedTask;
    }

    private Task ObserveSourceRetiringAsync()
    {
        if (Source is not null) hooks?.SourceRetiring?.Invoke(this);
        return Task.CompletedTask;
    }

    private Task DisposeSourceAsync()
    {
        if (Source is not null)
        {
            Source.Dispose();
            hooks?.SourceDisposed?.Invoke(Source);
        }
        return Task.CompletedTask;
    }

    private Task DisposeViewModelAsync()
    {
        if (ViewModel is not null)
        {
            ViewModel.Dispose();
            hooks?.ViewModelDisposed?.Invoke(ViewModel);
        }
        return Task.CompletedTask;
    }
}
