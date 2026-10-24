using Microsoft.Extensions.Logging;
using System.IO;
using System.Windows.Threading;

namespace STranslate.Plugin.Tts.FishAudio.Presentation;

internal interface IPreviewAudioPlayer : IDisposable
{
    event EventHandler? Opened;
    event EventHandler? Ended;
    event Action<Exception?>? Failed;
    TimeSpan? Duration { get; }
    TimeSpan Position { get; }
    double Volume { get; set; }
    void Open(Stream source);
    void Play();
    void Stop();
}

internal sealed class PreviewPlaybackController : IDisposable
{
    private readonly ILogger? _logger;
    private readonly Action<string?, double> _stateChanged;
    private readonly Action<Exception?> _playbackFailed;
    private readonly Func<IPreviewAudioPlayer> _playerFactory;
    private readonly Action<Action> _runOnUiThread;
    private IPreviewAudioPlayer? _player;
    private MemoryStream? _audio;
    private DispatcherTimer? _timer;
    private EventHandler? _ended;
    private Action<Exception?>? _failed;
    private long _generation;
    private bool _disposed;

    public PreviewPlaybackController(ILogger? logger, Action<string?, double> stateChanged,
        Action<Exception?> playbackFailed, Func<IPreviewAudioPlayer>? playerFactory = null,
        Action<Action>? runOnUiThread = null)
    {
        _logger = logger;
        _stateChanged = stateChanged;
        _playbackFailed = playbackFailed;
        _playerFactory = playerFactory ?? (() => new NAudioPreviewAudioPlayer());
        _runOnUiThread = runOnUiThread ?? (action => action());
    }

    public string? PreviewingVoiceId { get; private set; }
    public double PreviewProgress { get; private set; }

    // Ownership of the seekable local buffer transfers on every call, including failure.
    public void Toggle(string voiceId, MemoryStream audio)
    {
        if (_disposed || PreviewingVoiceId == voiceId) { audio.Dispose(); Stop(); return; }
        Stop();
        var generation = _generation;
        _audio = audio;
        try
        {
            var player = _playerFactory();
            _player = player;
            _ended = (_, _) => _runOnUiThread(() =>
            {
                if (IsCurrent(player, generation)) Stop();
            });
            _failed = exception => _runOnUiThread(() =>
            {
                if (IsCurrent(player, generation)) Fail(exception);
            });
            player.Ended += _ended;
            player.Failed += _failed;
            player.Volume = 1;
            player.Open(audio);
            if (!IsCurrent(player, generation)) return;
            player.Play();
            if (!IsCurrent(player, generation)) return;
            SetState(voiceId, 0);
            if (!IsCurrent(player, generation)) return;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += OnPreviewTick;
            _timer.Start();
        }
        catch (Exception exception) { if (_generation == generation) Fail(exception); }
    }

    private bool IsCurrent(IPreviewAudioPlayer player, long generation) =>
        !_disposed && generation == _generation && ReferenceEquals(_player, player);

    public void Stop()
    {
        ++_generation;
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnPreviewTick;
            _timer = null;
        }
        var player = _player;
        _player = null;
        if (player is not null)
        {
            player.Ended -= _ended;
            player.Failed -= _failed;
            try { player.Stop(); }
            catch (Exception exception) { LogFailure("stop", exception); }
            try { player.Dispose(); }
            catch (Exception exception) { LogFailure("dispose", exception); }
        }
        _ended = null;
        _failed = null;
        _audio?.Dispose();
        _audio = null;
        SetState(null, 0);
    }

    private void Fail(Exception? exception)
    {
        LogFailure("playback", exception);
        Stop();
        if (!_disposed) _playbackFailed(exception);
    }

    private void LogFailure(string operation, Exception? exception) =>
        _logger?.LogWarning("Preview audio {Operation} failed ({ErrorType})", operation, exception?.GetType().Name ?? "Unknown");

    private void OnPreviewTick(object? sender, EventArgs args)
    {
        try
        {
            if (_player?.Duration is not { } duration) return;
            SetState(PreviewingVoiceId, duration.TotalMilliseconds > 0
                ? Math.Clamp(_player.Position.TotalMilliseconds / duration.TotalMilliseconds, 0, 1) : 0);
        }
        catch (Exception exception) { Fail(exception); }
    }

    private void SetState(string? voiceId, double progress)
    {
        PreviewingVoiceId = voiceId;
        PreviewProgress = progress;
        _stateChanged(voiceId, progress);
    }

    public void Dispose() { _disposed = true; Stop(); }
}
