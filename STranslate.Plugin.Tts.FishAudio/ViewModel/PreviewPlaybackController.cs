using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace STranslate.Plugin.Tts.FishAudio.ViewModel;

internal sealed class PreviewPlaybackController : IDisposable
{
    private readonly ILogger? _logger;
    private readonly Action<string?, double> _stateChanged;

    private MediaPlayer? _player;
    private DispatcherTimer? _timer;

    public PreviewPlaybackController(ILogger? logger, Action<string?, double> stateChanged)
    {
        _logger = logger;
        _stateChanged = stateChanged;
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
            _player.MediaOpened -= OnMediaOpened;
            _player.MediaEnded -= OnMediaEnded;
            _player.MediaFailed -= OnMediaFailed;
            _player.Stop();
            _player.Close();
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

        _player = new MediaPlayer { Volume = 1.0 };
        _player.MediaOpened += OnMediaOpened;
        _player.MediaEnded += OnMediaEnded;
        _player.MediaFailed += OnMediaFailed;
        _player.Open(previewUri);
        _player.Play();
    }

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnPreviewTick;
        _timer.Start();
    }

    private void OnMediaEnded(object? sender, EventArgs e) => Stop();

    private void OnMediaFailed(object? sender, ExceptionEventArgs e) => Stop();

    private void OnPreviewTick(object? sender, EventArgs e)
    {
        if (_player?.NaturalDuration.HasTimeSpan != true)
            return;

        var duration = _player.NaturalDuration.TimeSpan.TotalMilliseconds;
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
