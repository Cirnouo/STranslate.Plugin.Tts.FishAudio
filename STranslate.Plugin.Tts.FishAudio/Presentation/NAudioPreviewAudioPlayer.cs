using NAudio.Wave;
using System.IO;

namespace STranslate.Plugin.Tts.FishAudio.Presentation;

internal sealed class NAudioPreviewAudioPlayer : IPreviewAudioPlayer
{
    private WaveOutEvent? _output;
    private WaveStream? _reader;
    private double _volume = 1;

    public event EventHandler? Opened;
    public event EventHandler? Ended;
    public event Action<Exception?>? Failed;
    public TimeSpan? Duration => _reader?.TotalTime;
    public TimeSpan Position => _output is { OutputWaveFormat.AverageBytesPerSecond: > 0 } output
        ? TimeSpan.FromSeconds((double)output.GetPosition() / output.OutputWaveFormat.AverageBytesPerSecond)
        : TimeSpan.Zero;
    public double Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 1); if (_output is not null) _output.Volume = (float)_volume; }
    }

    public void Open(Stream source)
    {
        if (!source.CanRead || !source.CanSeek) throw new ArgumentException("Preview requires a readable seekable stream.");
        source.Position = 0;
        Span<byte> header = stackalloc byte[12];
        var length = source.Read(header);
        source.Position = 0;
        _reader = length == 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WAVE"u8)
            ? new WaveFileReader(source)
            : new Mp3FileReaderBase(source, format => new AcmMp3FrameDecompressor(format));
        _output = new WaveOutEvent();
        _output.PlaybackStopped += OnPlaybackStopped;
        _output.Init(_reader);
        _output.Volume = (float)_volume;
        Opened?.Invoke(this, EventArgs.Empty);
    }

    public void Play() => (_output ?? throw new InvalidOperationException("Preview is not initialized.")).Play();
    public void Stop() => _output?.Stop();

    private void OnPlaybackStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception is { } exception) Failed?.Invoke(exception);
        else Ended?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        var output = _output;
        _output = null;
        try
        {
            if (output is not null)
            {
                output.PlaybackStopped -= OnPlaybackStopped;
                output.Dispose();
            }
        }
        finally { _reader?.Dispose(); _reader = null; }
    }
}
