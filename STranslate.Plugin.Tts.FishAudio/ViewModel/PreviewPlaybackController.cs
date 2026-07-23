using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace STranslate.Plugin.Tts.FishAudio.ViewModel;

internal interface IPreviewAudioPlayer : IDisposable
{
    event EventHandler? Opened;
    event EventHandler? Ended;
    event Action<Exception?>? Failed;

    TimeSpan? Duration { get; }
    TimeSpan Position { get; }
    double Volume { get; set; }

    void Open(Uri source);
    void Play();
    void Stop();
}

internal sealed class PreviewPlaybackController : IDisposable
{
    private readonly ILogger? _logger;
    private readonly Action<string?, double> _stateChanged;
    private readonly Action<Exception?> _playbackFailed;
    private readonly Func<IPreviewAudioPlayer> _playerFactory;

    private IPreviewAudioPlayer? _player;
    private DispatcherTimer? _timer;

    public PreviewPlaybackController(
        ILogger? logger,
        Action<string?, double> stateChanged,
        Action<Exception?> playbackFailed,
        Func<IPreviewAudioPlayer>? playerFactory = null)
    {
        _logger = logger;
        _stateChanged = stateChanged;
        _playbackFailed = playbackFailed;
        _playerFactory = playerFactory ?? (() => new MediaPlayerPreviewAudioPlayer());
    }

    public string? PreviewingVoiceId { get; private set; }
    public double PreviewProgress { get; private set; }

    public void Toggle(string voiceId, string audioUrl)
    {
        if (PreviewingVoiceId == voiceId)
            Stop();
        else
            Start(voiceId, audioUrl);
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;

        if (_player is not null)
        {
            _player.Opened -= OnMediaOpened;
            _player.Ended -= OnMediaEnded;
            _player.Failed -= OnMediaFailed;
            _player.Stop();
            _player.Dispose();
            _player = null;
        }

        SetState(null, 0);
    }

    public void Dispose() => Stop();

    private void Start(string voiceId, string audioUrl)
    {
        Stop();

        if (!PreviewAudioUrlValidator.TryCreateAllowedUri(audioUrl, out var previewUri))
        {
            _logger?.LogWarning("Rejected preview audio URL for voice {VoiceId}: host or scheme is not allowed", voiceId);
            return;
        }

        SetState(voiceId, 0);

        _player = _playerFactory();
        _player.Volume = 1.0;
        _player.Opened += OnMediaOpened;
        _player.Ended += OnMediaEnded;
        _player.Failed += OnMediaFailed;
        _player.Open(previewUri!);
        _player.Play();
    }

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnPreviewTick;
        _timer.Start();
    }

    private void OnMediaEnded(object? sender, EventArgs e) => Stop();

    private void OnMediaFailed(Exception? exception)
    {
        _logger?.LogWarning(exception, "Preview audio playback failed");
        Stop();
        _playbackFailed(exception);
    }

    private void OnPreviewTick(object? sender, EventArgs e)
    {
        if (_player?.Duration is not { } durationTime)
            return;

        var duration = durationTime.TotalMilliseconds;
        var position = _player.Position.TotalMilliseconds;
        SetState(PreviewingVoiceId, duration > 0 ? Math.Min(1.0, position / duration) : 0);
    }

    private void SetState(string? voiceId, double progress)
    {
        PreviewingVoiceId = voiceId;
        PreviewProgress = progress;
        _stateChanged(PreviewingVoiceId, PreviewProgress);
    }
}

internal sealed class MediaPlayerPreviewAudioPlayer : IPreviewAudioPlayer
{
    private readonly MediaPlayer _player = new();

    public MediaPlayerPreviewAudioPlayer()
    {
        _player.MediaOpened += OnMediaOpened;
        _player.MediaEnded += OnMediaEnded;
        _player.MediaFailed += OnMediaFailed;
    }

    public event EventHandler? Opened;
    public event EventHandler? Ended;
    public event Action<Exception?>? Failed;

    public TimeSpan? Duration => _player.NaturalDuration.HasTimeSpan ? _player.NaturalDuration.TimeSpan : null;
    public TimeSpan Position => _player.Position;
    public double Volume
    {
        get => _player.Volume;
        set => _player.Volume = value;
    }

    public void Open(Uri source) => _player.Open(source);
    public void Play() => _player.Play();
    public void Stop() => _player.Stop();

    public void Dispose()
    {
        _player.MediaOpened -= OnMediaOpened;
        _player.MediaEnded -= OnMediaEnded;
        _player.MediaFailed -= OnMediaFailed;
        _player.Close();
    }

    private void OnMediaOpened(object? sender, EventArgs e) => Opened?.Invoke(this, EventArgs.Empty);
    private void OnMediaEnded(object? sender, EventArgs e) => Ended?.Invoke(this, EventArgs.Empty);
    private void OnMediaFailed(object? sender, ExceptionEventArgs e) => Failed?.Invoke(e.ErrorException);
}
